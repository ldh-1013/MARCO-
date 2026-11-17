// V30 연출.md 2.2 잠수 — 화면 가장자리 물 굴절. Samples the camera's opaque texture (enabled for the player camera only,
// only while diving — CameraFeelStack) and draws it back slightly displaced near the screen edges. Queue Transparent-1:
// after the opaque copy and before every transparent (all ≥ 3000 — ripple rings, afterglow, ghosts), so nothing drawn
// later is lost and nothing earlier but opaques is under it. The centre 70 % (each axis) is never touched (alpha 0).
// Displacement ≤ 0.4 % of the screen width, low-frequency value noise flowing slowly (no sine).
Shader "Marco/WaterEdgeRefraction"
{
    Properties
    {
        _Strength("Max displacement (fraction of the screen width)", Float) = 0.004
        _Aspect("Width / height", Float) = 1.7778
        _Flow("Noise flow (noise cells per second)", Float) = 0.15
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-1" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        ZTest Always ZWrite Off Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _CameraOpaqueTexture;
            float _Strength, _Aspect, _Flow;

            struct v2f { float4 pos : SV_POSITION; float4 screen : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x),
                            lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w;
                float2 edge = min(uv, 1.0 - uv);
                if (edge.x > 0.15 && edge.y > 0.15) return fixed4(0, 0, 0, 0);   // centre 70 %
                float m = max(1.0 - smoothstep(0.0, 0.15, edge.x), 1.0 - smoothstep(0.0, 0.15, edge.y));
                float t = _Time.y * _Flow;
                float2 d = float2(noise(uv * 3.0 + float2(t, 0.37 * t)), noise(uv * 3.0 + float2(5.2 - 0.61 * t, 1.7 + t))) * 2.0 - 1.0;
                d *= 0.70710678;                      // |d| ≤ 1
                float2 disp = d * _Strength * m;      // in screen-width units
                disp.y *= _Aspect;                    // same pixel length vertically
                fixed3 col = tex2D(_CameraOpaqueTexture, uv + disp).rgb;
                return fixed4(col, m);
            }
            ENDCG
        }
    }
}
