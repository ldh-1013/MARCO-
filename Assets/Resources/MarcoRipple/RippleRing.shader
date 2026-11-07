// Ring pulse (기획서 §5.5-1 · §5.5-3 · §16.4). One static strip per ring; the wavefront radius is a
// material property, so expanding a ring never rewrites its mesh. Each vertex carries its direction,
// the distance at which a wall or ledge stopped that direction, and the floor-height profile along it.
// The marching that produced those values is the CPU side (RippleReach); this shader only draws.
// Loaded through Resources so no prefab or GraphicsSettings entry is needed.
Shader "Marco/RippleRing"
{
    Properties
    {
        _BaseColor("Color", Color) = (0.91, 0.91, 0.91, 0.85)
        _Radius("Wavefront radius", Float) = 1
        _Step("Profile sample spacing", Float) = 1
        _Core("Core width", Float) = 0.05
        _Trail("Trail width", Float) = 0.35
        _Halo("Front halo width", Float) = 0.06
        _Glow("Glow strength", Float) = 0.45
        _Double("Double edge", Float) = 0
        _Gap("Double edge gap", Float) = 0.14
        _InnerBrightness("Inner edge brightness", Float) = 0.6
        _StopFade("Stopped fade distance", Float) = 1
        _StopDim("Stopped brightness", Float) = 0.55
        _MinPixels("Minimum width in pixels", Float) = 1.5
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "RippleRing"
            // Emitted light on a black world: additive, no depth write, depth-tested so walls hide it.
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Radius, _Step, _Core, _Trail, _Halo, _Glow, _Double, _Gap, _InnerBrightness;
                float _StopFade, _StopDim, _MinPixels;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 ray : TEXCOORD0;      // xy = direction (xz plane), z = reach, w = 0 inner / 1 outer
                float4 h0 : TEXCOORD1;       // floor height profile, 16 samples spaced _Step apart
                float4 h1 : TEXCOORD2;
                float4 h2 : TEXCOORD3;
                float4 h3 : TEXCOORD4;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float radial : TEXCOORD0;    // distance from the source along this direction
                float front : TEXCOORD1;     // wavefront distance for this direction (stops at reach)
                float reach : TEXCOORD2;
            };

            // Linear interpolation of the 16-sample profile written as a tent filter (no dynamic indexing).
            float Height(Attributes v, float d)
            {
                float f = clamp(d / max(_Step, 1e-4), 0.0, 15.0);
                float4 k0 = float4(0, 1, 2, 3);
                float4 w0 = saturate(1.0 - abs(f - k0));
                float4 w1 = saturate(1.0 - abs(f - (k0 + 4)));
                float4 w2 = saturate(1.0 - abs(f - (k0 + 8)));
                float4 w3 = saturate(1.0 - abs(f - (k0 + 12)));
                return dot(w0, v.h0) + dot(w1, v.h1) + dot(w2, v.h2) + dot(w3, v.h3);
            }

            Varyings vert(Attributes v)
            {
                float2 dir = v.ray.xy;
                float reach = v.ray.z;
                float front = min(_Radius, reach);

                // Keep the line at least _MinPixels wide on screen: pad the strip by the world size
                // of a pixel at this depth (the fragment stage does the exact coverage).
                float3 frontWS = TransformObjectToWorld(float3(dir.x * front, Height(v, front), dir.y * front));
                float depth = max(0.01, -TransformWorldToView(frontWS).z);
                float projection = max(abs(GetViewToHClipMatrix()[1][1]), 1e-3);
                float worldPerPixel = depth * 2.0 / (projection * max(_ScreenParams.y, 1.0));
                float pad = _MinPixels * worldPerPixel * 2.0;

                float inner = _Trail + _Core + _Double * _Gap + pad;
                float outer = _Halo + pad;
                float d = v.ray.w > 0.5 ? front + outer : max(0.0, front - inner);

                float3 positionOS = float3(dir.x * d, Height(v, d), dir.y * d);
                Varyings o;
                o.positionHCS = TransformObjectToHClip(positionOS);
                o.radial = d;
                o.front = front;
                o.reach = reach;
                return o;
            }

            // Anti-aliased coverage of [-width, 0] relative to the wavefront.
            float Band(float x, float width, float px)
            {
                return saturate((x + width) / px + 0.5) * saturate(-x / px + 0.5);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float px = max(fwidth(i.radial), 1e-4);
                float x = i.radial - i.front;                 // > 0 outside the wavefront
                float core = max(_Core, _MinPixels * px);

                // Sharp bright front, soft trail behind it, a thin halo ahead of it.
                float behind = saturate(-x / max(_Trail, 1e-3));
                float trail = x < 0 ? _Glow * (1.0 - behind) * (1.0 - behind) : 0;
                float ahead = saturate(1.0 - x / max(_Halo + px, 1e-3));
                float halo = x > 0 ? _Glow * 0.6 * ahead * ahead : 0;
                float ring = max(Band(x, core, px), trail + halo);

                // Seeker rings: a second, dimmer edge just inside the first (§16.4 형태적 구분).
                float second = Band(x + _Gap, core, px) * _InnerBrightness;
                ring = max(ring, second * _Double);

                // A direction stopped by a wall stays at the wall and dims there instead of jumping.
                float stopped = saturate((_Radius - i.reach) / max(_StopFade, 1e-3));
                ring *= lerp(1.0, _StopDim, stopped);

                return half4(_BaseColor.rgb, saturate(_BaseColor.a * ring));
            }
            ENDHLSL
        }
    }
}
