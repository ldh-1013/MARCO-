// V31 연출.md 2.2 벽 통과(메아리) — 0.1 s white-noise overlay at the screen edges. The edge mask is the A-0 shape
// (EdgeStain: gathers in the corners, thinner along the sides, noise-broken boundary); the centre 70 % (each axis) is
// never drawn. The noise is a low-frequency value noise times a per-frame high-frequency grain (re-seeded every frame
// by _Frame — no repeating sine). White, alpha blended. Loaded through Resources.
Shader "Marco/WallPassNoise"
{
    Properties
    {
        _Alpha("Opacity", Float) = 0.25
        _Frame("Frame seed", Float) = 0
        _Aspect("Width / height", Float) = 1.7778
        _CornerDepth("Corner depth (short side)", Float) = 0.15
        _EdgeDepth("Edge-middle depth (short side)", Float) = 0.06
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" }
        ZTest Always ZWrite Off Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _Alpha, _Frame, _Aspect, _CornerDepth, _EdgeDepth;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
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
                float2 uv = i.uv;
                float2 edge = min(uv, 1.0 - uv);
                if (edge.x > 0.15 && edge.y > 0.15) return fixed4(1, 1, 1, 0);   // centre 70 %
                float shortSide = min(_Aspect, 1.0);
                float dx = edge.x * _Aspect / shortSide, dy = edge.y / shortSide;
                float corner = smoothstep(0.0, 1.0, saturate(1.0 - edge.x * 2.0) * saturate(1.0 - edge.y * 2.0));
                float n = 0.65 * noise(float2(uv.x * 3.1, uv.y * 3.1)) + 0.35 * noise(float2(uv.x * 7.3, uv.y * 7.3));
                float depth = lerp(min(_EdgeDepth, 0.06), min(_CornerDepth, 0.15), corner) * lerp(0.55, 1.0, saturate(n));
                float m = max(1.0 - smoothstep(0.0, 1.0, dx / depth), 1.0 - smoothstep(0.0, 1.0, dy / depth));
                // static: low-frequency clouds × fine grain, both re-seeded every frame
                float low = noise(uv * float2(_Aspect, 1.0) * 9.0 + _Frame * 1.37);
                float grain = hash(floor(uv * float2(_Aspect, 1.0) * 270.0) + _Frame * 0.173);
                float staticNoise = saturate(0.35 + 0.4 * low + 0.45 * grain);
                return fixed4(1, 1, 1, m * m * staticNoise * _Alpha);
            }
            ENDCG
        }
    }
}
