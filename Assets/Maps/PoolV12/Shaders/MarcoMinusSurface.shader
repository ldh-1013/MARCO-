Shader "MARCO/WorldSpaceTexturedSurface"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _MarcoClickerTerrain ("Clicker terrain", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float _MarcoMinusEnabled;
            float _MarcoLightRadius;
            float _MarcoLightIntensity;
            float _MarcoFalloffPower;
            float4 _MarcoPlayerPosition;
            float4 _MarcoLightColor;
            float _MarcoClickerTerrain;
            float _MarcoClickerEnabled;
            float _MarcoClickerRadius;
            float _MarcoClickerIntensity;
            float _MarcoClickerFalloffPower;
            float4 _MarcoClickerPosition;
            float4 _MarcoClickerColor;

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPosition : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float4 surface = tex2D(_BaseMap, input.uv) * _BaseColor;
                float3 litColor = surface.rgb;
                if (_MarcoMinusEnabled > 0.5)
                {
                    float distanceToPlayer = distance(input.worldPosition, _MarcoPlayerPosition.xyz);
                    float attenuation = _MarcoLightRadius > 0.001
                        ? pow(saturate(1.0 - distanceToPlayer / _MarcoLightRadius), max(0.01, _MarcoFalloffPower))
                        : 0.0;
                    litColor *= _MarcoLightColor.rgb * _MarcoLightIntensity * attenuation;
                }
                if (_MarcoClickerEnabled > 0.5 && _MarcoClickerTerrain > 0.5)
                {
                    float distanceToFlash = distance(input.worldPosition, _MarcoClickerPosition.xyz);
                    float flashAttenuation = _MarcoClickerRadius > 0.001
                        ? pow(saturate(1.0 - distanceToFlash / _MarcoClickerRadius), max(0.01, _MarcoClickerFalloffPower))
                        : 0.0;
                    float3 flashColor = surface.rgb * _MarcoClickerColor.rgb * _MarcoClickerIntensity * flashAttenuation;
                    litColor = max(litColor, flashColor);
                }
                surface.rgb = litColor;
                return surface;
            }
            ENDCG
        }
    }
}
