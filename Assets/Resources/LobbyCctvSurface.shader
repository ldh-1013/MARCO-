Shader "MARCO/LobbyCctvSurface"
{
    Properties
    {
        _BaseMap("Texture", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)
        _Brightness("Brightness", Float) = .65
        _Subject("Subject", Float) = 0
        _Water("Water", Float) = 0
        _RevealOrigin("Voice origin", Vector) = (0,0,0,0)
        _RevealRadius("Voice radius", Float) = 0
        _RevealIntensity("Voice intensity", Float) = 0
        _RevealFalloff("Voice falloff", Float) = 1.7
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _BaseMap;
            float4 _BaseMap_ST, _BaseColor, _RevealOrigin;
            float _Brightness, _Subject, _Water;
            float _RevealRadius, _RevealIntensity, _RevealFalloff;
            struct Input { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; };
            struct Output { float4 vertex:SV_POSITION; float3 normal:TEXCOORD0; float2 uv:TEXCOORD1; float3 world:TEXCOORD2; };
            Output vert(Input v)
            {
                Output o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            float4 frag(Output i):SV_Target
            {
                float4 tex = tex2D(_BaseMap, i.uv);
                clip(tex.a - .15);
                float shade = .32 + .68 * saturate(dot(normalize(i.normal), normalize(float3(.4,.8,-.3))));
                float grey = dot(tex.rgb * _BaseColor.rgb, float3(.2126,.7152,.0722));
                if (_Water > .5)
                {
                    float waves = sin(i.world.x * 4 + _Time.y * .7) * sin(i.world.z * 6 - _Time.y * .4);
                    grey = .26 + .045 * waves;
                }
                // Same radial attenuation shape as the in-game microphone light:
                // silence is black; voice reveals the subject AND nearby surfaces.
                float distanceToVoice = distance(i.world, _RevealOrigin.xyz);
                float attenuation = _RevealRadius > .001
                    ? pow(saturate(1 - distanceToVoice / _RevealRadius), max(.1, _RevealFalloff)) : 0;
                float level = lerp(grey, max(grey,.42), _Subject) * shade * _Brightness * attenuation * _RevealIntensity;
                return float4(level * float3(.78,.97,.89), 1);
            }
            ENDCG
        }
    }
}
