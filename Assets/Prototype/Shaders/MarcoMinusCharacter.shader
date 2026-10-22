Shader "MARCO/WorldSpaceCharacter"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Surface ("Surface Type", Float) = 0
        _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.25
        _Cull ("Cull", Float) = 2
        _SrcBlend ("Source Blend", Float) = 1
        _DstBlend ("Destination Blend", Float) = 0
        _ZWrite ("Z Write", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "WorldSpaceCharacter"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend]
            Cull [_Cull]
            ZWrite [_ZWrite]

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float _AlphaClip;
            float _Cutoff;
            float _MarcoMinusEnabled;
            float _MarcoLightRadius;
            float _MarcoLightIntensity;
            float _MarcoFalloffPower;
            float4 _MarcoPlayerPosition;
            float4 _MarcoLightColor;

            struct Attributes
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPosition : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float4 surface = tex2D(_BaseMap, input.uv) * _BaseColor;
                if (_AlphaClip > 0.5) clip(surface.a - _Cutoff);

                float3 normal = normalize(input.worldNormal);
                float shade = 0.72 + 0.28 * saturate(dot(normal, normalize(float3(-0.4, 0.8, -0.3))));
                if (_MarcoMinusEnabled > 0.5)
                {
                    float distanceToPlayer = distance(input.worldPosition, _MarcoPlayerPosition.xyz);
                    float attenuation = _MarcoLightRadius > 0.001
                        ? pow(saturate(1.0 - distanceToPlayer / _MarcoLightRadius), max(0.01, _MarcoFalloffPower))
                        : 0.0;
                    surface.rgb *= shade * _MarcoLightColor.rgb * _MarcoLightIntensity * attenuation;
                }
                else
                {
                    surface.rgb *= shade;
                }
                return surface;
            }
            ENDCG
        }
    }
    Fallback Off
}
