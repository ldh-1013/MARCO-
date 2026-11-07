Shader "PoolV12/WaterSpray"
{
    SubShader
    {
        Tags { "Queue"="Transparent+1" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float _MarcoMinusEnabled, _MarcoLightRadius, _MarcoLightIntensity, _MarcoFalloffPower;
            float4 _MarcoPlayerPosition, _MarcoLightColor;
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float2 p = (input.uv - .5) * 2;
                float alpha = 1.0 - smoothstep(.25, 1, dot(p, p));
                float3 color = input.color.rgb;
                // Spray follows the same restricted-vision lighting as the pool surface.
                if (_MarcoMinusEnabled > .5)
                {
                    float attenuation = pow(saturate(1.0 - distance(input.positionWS, _MarcoPlayerPosition.xyz)
                        / max(.001, _MarcoLightRadius)), max(.01, _MarcoFalloffPower));
                    color *= _MarcoLightColor.rgb * _MarcoLightIntensity * attenuation;
                }
                return half4(color, input.color.a * alpha);
            }
            ENDHLSL
        }
    }
}
