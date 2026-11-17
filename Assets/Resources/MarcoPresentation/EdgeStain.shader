// V28 A-0 체력 가장자리 표시 (연출.md 16 · 15.3): an organic stain that gathers in the corners, thins along the edges,
// has a noise-broken boundary and fades inward. Alpha blended (never additive). The shape mirrors
// InjuryEdgeRules.Mask (the CPU reference used by the tests); the centre 70 % is never drawn.
// Loaded through Resources so no prefab or GraphicsSettings entry is needed.
Shader "Marco/EdgeStain"
{
    Properties
    {
        _Color("Stain colour", Color) = (0.5, 0.1, 0.12, 1)
        _Alpha("Opacity at the very edge", Float) = 0.4
        _Seed("Pattern seed", Float) = 0
        _Drift("Pattern drift (UV)", Float) = 0
        _Aspect("Width / height", Float) = 1.7778
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

            fixed4 _Color;
            float _Alpha, _Seed, _Drift, _Aspect;

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
                // centre 70 % keep-out (InjuryEdgeRules.CentreKeepOut)
                if (edge.x > 0.15 && edge.y > 0.15) return fixed4(0, 0, 0, 0);
                float shortSide = min(_Aspect, 1.0);
                float dx = edge.x * _Aspect / shortSide, dy = edge.y / shortSide;
                float corner = smoothstep(0.0, 1.0, saturate(1.0 - edge.x * 2.0) * saturate(1.0 - edge.y * 2.0));
                float n = 0.65 * noise(float2(_Seed + uv.x * 3.1 + _Drift, _Seed * 0.37 + uv.y * 3.1))
                        + 0.35 * noise(float2(_Seed * 1.7 + uv.x * 7.3, _Seed + uv.y * 7.3 - _Drift));
                float depth = lerp(0.06, 0.15, corner) * lerp(0.55, 1.0, n);
                float m = max(1.0 - smoothstep(0.0, 1.0, dx / depth), 1.0 - smoothstep(0.0, 1.0, dy / depth));
                return fixed4(_Color.rgb, m * m * _Alpha);
            }
            ENDCG
        }
    }
}
