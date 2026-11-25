// Ring pulse (기획서 §5.5-1 · §5.5-3 · §16.4). One static strip per ring; the wavefront radius is a
// material property, so expanding a ring never rewrites its mesh. Each vertex carries its direction,
// the distance at which a wall or ledge stopped that direction, the floor-height profile along it, and the
// stop distance of the other end of its quad. The marching that produced those values is the CPU side
// (RippleReach); this shader only draws. Loaded through Resources so no prefab or GraphicsSettings entry is needed.
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
        _JoinStart("Corner fade start", Float) = 0.3
        _JoinEnd("Corner fade end", Float) = 0.6
        _MaxPad("Maximum strip padding", Float) = 1.5
        _Mode("0 ring / 1 wall reveal / 2 afterglow", Float) = 0
        _WallFade("Wall reveal fade distance", Float) = 1.5
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
                float _StopFade, _StopDim, _MinPixels, _JoinStart, _JoinEnd, _MaxPad, _Mode, _WallFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 ray : TEXCOORD0;      // xy = direction (xz plane), z = reach, w = 0 inner / 1 outer
                float4 h0 : TEXCOORD1;       // floor height profile, 16 samples spaced _Step apart
                float4 h1 : TEXCOORD2;
                float4 h2 : TEXCOORD3;
                float4 h3 : TEXCOORD4;
                float4 other : TEXCOORD5;    // x = reach at the other end of this quad, y = 1 if this end stopped at a wall
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float radial : TEXCOORD0;    // distance from the source along this direction
                float front : TEXCOORD1;     // wavefront distance for this direction (stops at reach)
                float reach : TEXCOORD2;
                float join : TEXCOORD3;      // 0 where the quad would stretch radially across a corner
                float wall : TEXCOORD4;      // ring/afterglow: 1 at wall-stopped ends; wall reveal: height fraction
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

            // World metres covered by one screen pixel along the ring's radial direction at this point.
            // Seen at a grazing angle the radial direction is foreshortened, so this grows and the strip widens.
            float RadialMetresPerPixel(float3 positionWS, float3 radialWS)
            {
                const float probe = 0.05;
                float4 a = TransformWorldToHClip(positionWS);
                float4 b = TransformWorldToHClip(positionWS + radialWS * probe);
                if (a.w <= 1e-3 || b.w <= 1e-3) return _MaxPad;
                float2 halfScreen = _ScreenParams.xy * 0.5;
                float pixels = length(b.xy / b.w * halfScreen - a.xy / a.w * halfScreen) / probe;
                return 1.0 / max(pixels, 1e-4);
            }

            Varyings vert(Attributes v)
            {
                // Wall reveal: a static strip standing on the wall where this ring stops (§16.4 2차 연출).
                if (_Mode > 0.5 && _Mode < 1.5)
                {
                    Varyings w;
                    w.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                    w.radial = 0; w.front = 0; w.reach = v.ray.z; w.join = 1; w.wall = v.ray.w;
                    return w;
                }
                float2 dir = v.ray.xy;
                float reach = v.ray.z;
                float front = min(_Radius, reach);

                float3 frontWS = TransformObjectToWorld(float3(dir.x * front, Height(v, front), dir.y * front));
                float3 radialWS = normalize(TransformObjectToWorldDir(float3(dir.x, 0, dir.y)));
                // Room for at least _MinPixels across the line on screen; the fragment stage does exact coverage.
                float pad = min(_MinPixels * 2.0 * RadialMetresPerPixel(frontWS, radialWS), _MaxPad);

                float inner = _Trail + _Core + _Double * _Gap + pad;
                float outer = _Halo + pad;
                float d = v.ray.w > 0.5 ? front + outer : max(0.0, front - inner);

                float3 positionOS = float3(dir.x * d, Height(v, d), dir.y * d);
                Varyings o;
                o.positionHCS = TransformObjectToHClip(positionOS);
                o.radial = d;
                o.front = front;
                o.reach = reach;
                // Both ends of a quad compute the same difference, so the whole quad fades together.
                float otherFront = min(_Radius, v.other.x);
                o.join = 1.0 - smoothstep(_JoinStart, _JoinEnd, abs(front - otherFront));
                o.wall = v.other.y;
                return o;
            }

            // Anti-aliased coverage of [-width, 0] relative to the wavefront.
            float Band(float x, float width, float px)
            {
                return saturate((x + width) / px + 0.5) * saturate(-x / px + 0.5);
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_Mode > 0.5 && _Mode < 1.5)
                {
                    // Lights the moment the wavefront reaches this wall, then fades as the ring moves on;
                    // brightest at the floor, gone towards the top.
                    float reached = _Radius - i.reach;
                    float flash = smoothstep(0.0, 0.15, reached) * pow(1.0 - saturate(reached / max(_WallFade, 1e-3)), 2.0);
                    float up = 1.0 - saturate(i.wall);
                    return half4(_BaseColor.rgb, saturate(_BaseColor.a * flash * up * up));
                }
                float px = max(fwidth(i.radial), 1e-4);       // metres per pixel across the line, on screen
                float x = i.radial - i.front;                 // > 0 outside the wavefront
                float core = max(_Core, _MinPixels * px);

                // Sharp bright front, soft trail behind it, a thin halo ahead of it.
                float behind = saturate(-x / max(_Trail, 1e-3));
                float trail = x < 0 ? _Glow * (1.0 - behind) * (1.0 - behind) : 0;
                float ahead = saturate(1.0 - x / max(_Halo + px, 1e-3));
                float halo = x > 0 ? _Glow * 0.6 * ahead * ahead : 0;
                float ring = max(Band(x, core, px), trail + halo);

                // Seeker rings: a second, dimmer edge just inside the first (§16.4 형태적 구분).
                float second = Band(x + _Gap, max(_Core, _MinPixels * px), px) * _InnerBrightness;
                ring = max(ring, second * _Double);

                if (_Mode > 1.5)
                {
                    // Afterglow (§16.4): only the core line where this ring stopped at a wall, no glow.
                    float edge = Band(x, core, px) * saturate(i.wall) * i.join;
                    return half4(_BaseColor.rgb, saturate(_BaseColor.a * edge));
                }
                // A direction stopped by a wall stays at the wall and dims there instead of jumping.
                float stopped = saturate((_Radius - i.reach) / max(_StopFade, 1e-3));
                ring *= lerp(1.0, _StopDim, stopped) * i.join;

                return half4(_BaseColor.rgb, saturate(_BaseColor.a * ring));
            }
            ENDHLSL
        }
    }
}
