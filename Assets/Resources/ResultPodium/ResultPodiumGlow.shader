Shader "MARCO/ResultPodiumGlow"
{
    // 표시 전용. Flat result podium decor on quads: _Shape 0 = radial spot, 1 = vertical backdrop
    // gradient, 2 = soft-edged ribbon, 3 = hollow frame (award slot), 4 = light ray (fades along V),
    // 5 = solid disc (award badge), 6 = rounded card filled with _Color and outlined with _Color2
    // (_Aspect = quad width / height keeps the corners round; _Shine = position of a glint sweeping the
    // outline, -1..1 across the card, off outside), 7 = _MainTex × _Color (code-drawn award icons),
    // 8 = soft glow around a card (_Aspect of the glow quad, _Inset = margin around the card in quad
    // heights), 9 = spotlight cone from above (narrow at the top, pooled at the feet).
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
        _Color2("Second Color", Color) = (0,0,0,1)
        _Shape("Shape", Float) = 0
        _Aspect("Aspect", Float) = 1
        _Shine("Shine", Float) = -10
        _Inset("Inset", Float) = .15
        _MainTex("Texture", 2D) = "white" {}
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
            float _Shape, _Aspect, _Shine, _Inset;
            sampler2D _MainTex;
            struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct Output { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; };
            Output vert(Input v)
            {
                Output o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Rounded box, p and half size in the same units, corner radius r.
            float RoundedBox(float2 p, float2 halfSize, float r)
            {
                return length(max(abs(p) - (halfSize - r), 0)) - r;
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
                else if (_Shape < 4.5)
                {
                    color.a *= (1 - i.uv.y) * smoothstep(.5, .1, abs(c.x));
                }
                else if (_Shape < 5.5)
                {
                    float r = length(c) * 2;
                    float aa = fwidth(r) * 1.2;
                    color.a *= 1 - smoothstep(1 - aa, 1, r);
                }
                else if (_Shape < 6.5)
                {
                    // Rounded box in units of the card height: half size (aspect/2, 1/2), corner radius .16.
                    float2 p = float2(c.x * _Aspect, c.y);
                    float d = RoundedBox(p, float2(_Aspect * .5, .5), .16);
                    float aa = fwidth(d) * 1.2;
                    float inside = 1 - smoothstep(-aa, 0, d);
                    float border = smoothstep(-.035 - aa, -.035, d);
                    color = lerp(_Color, _Color2, border);
                    // Glint: a slanted band of light running along the outline, a faint sheen inside.
                    float s = c.x * 2 + c.y * .7;
                    float band = exp(-pow((s - _Shine) / .11, 2));
                    color.rgb = lerp(color.rgb, float3(1, 1, 1), band * (border * .8 + (1 - border) * .06));
                    color.a = lerp(color.a, max(color.a, .9), band * border);
                    color.a *= inside;
                }
                else if (_Shape < 7.5)
                {
                    color = tex2D(_MainTex, i.uv) * _Color;
                }
                else if (_Shape < 8.5)
                {
                    // Glow quad = card + _Inset on every side (in quad heights); falls off outside the card.
                    float2 p = float2(c.x * _Aspect, c.y);
                    float k = 1 - 2 * _Inset;
                    float d = RoundedBox(p, float2(_Aspect * .5 - _Inset, .5 - _Inset), .16 * k);
                    float glow = exp(-max(d, 0) / (_Inset * .32));
                    color.a *= glow * smoothstep(-.03, .0, d) * (1 - smoothstep(_Inset * .75, _Inset, d));
                }
                else
                {
                    float width = lerp(1, .2, i.uv.y);
                    float x = abs(c.x) * 2 / width;
                    float body = smoothstep(1, .25, x);
                    color.a *= body * (.3 + .7 * pow(1 - i.uv.y, 1.4)) * smoothstep(1, .8, i.uv.y);
                }
                return color;
            }
            ENDCG
        }
    }
}
