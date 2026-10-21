Shader "PoolV12/WorldLabel"
{
    Properties
    {
        _MainTex ("Font Texture", 2D) = "white" {}
        _Color ("Text Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "WorldLabel"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float _MarcoMinusEnabled, _MarcoLightRadius, _MarcoLightIntensity, _MarcoFalloffPower;
            float4 _MarcoPlayerPosition, _MarcoLightColor;
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.color = input.color * _Color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                half3 color = input.color.rgb;
                if (_MarcoMinusEnabled > .5)
                {
                    float d = distance(input.positionWS, _MarcoPlayerPosition.xyz);
                    float atten = _MarcoLightRadius > .001
                        ? pow(saturate(1.0 - d / _MarcoLightRadius), max(.01, _MarcoFalloffPower))
                        : 0.0;
                    color *= _MarcoLightColor.rgb * _MarcoLightIntensity * atten;
                }
                return half4(color, input.color.a * alpha);
            }
            ENDHLSL
        }
    }
}
