Shader "MARCO/ResultPodiumGlow"
{
    // 표시 전용. Flat result podium decor on quads: _Shape 0 = radial spot, 1 = vertical backdrop
    // gradient, 2 = soft-edged ribbon, 3 = hollow frame (award slot), 4 = light ray (fades along V).
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
        _Color2("Second Color", Color) = (0,0,0,1)
        _Shape("Shape", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color, _Color2;
            float _Shape;
            struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct Output { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; };
            Output vert(Input v)
            {
                Output o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            float4 frag(Output i):SV_Target
            {
                float2 c = i.uv - .5;
                float4 color = _Color;
                if (_Shape < .5)
                {
                    float r = length(c) * 2;
                    color.a *= pow(saturate(1 - r), 1.6);
                }
                else if (_Shape < 1.5)
                {
                    color = lerp(_Color2, _Color, smoothstep(0, 1, i.uv.y));
                    color.a = 1;
                }
                else if (_Shape < 2.5)
                {
                    float edge = smoothstep(.5, .44, abs(c.x)) * smoothstep(.5, .3, abs(c.y));
                    color.rgb *= lerp(.75, 1.15, i.uv.y);
                    color.a *= edge;
                }
                else if (_Shape < 3.5)
                {
                    float d = max(abs(c.x), abs(c.y));
                    color.a *= smoothstep(.5, .47, d) * smoothstep(.40, .43, d) + .18 * smoothstep(.5, .47, d);
                }
                else
                {
                    color.a *= (1 - i.uv.y) * smoothstep(.5, .1, abs(c.x));
                }
                return color;
            }
            ENDCG
        }
    }
}
