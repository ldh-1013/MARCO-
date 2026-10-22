Shader "MARCO/WorldSpaceSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "CanUseSpriteAtlas"="True"
            "IgnoreProjector"="True"
        }

        Pass
        {
            Name "WorldSpaceSprite"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _MarcoMinusEnabled;
            float _MarcoLightRadius;
            float _MarcoLightIntensity;
            float _MarcoFalloffPower;
            float4 _MarcoPlayerPosition;
            float4 _MarcoLightColor;

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPosition : TEXCOORD1;
                fixed4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(Varyings input) : SV_Target
            {
                fixed4 surface = tex2D(_MainTex, input.uv) * input.color;
                if (_MarcoMinusEnabled > 0.5)
                {
                    float distanceToPlayer = distance(input.worldPosition, _MarcoPlayerPosition.xyz);
                    float attenuation = _MarcoLightRadius > 0.001
                        ? pow(saturate(1.0 - distanceToPlayer / _MarcoLightRadius), max(0.01, _MarcoFalloffPower))
                        : 0.0;
                    surface.rgb *= _MarcoLightColor.rgb * _MarcoLightIntensity * attenuation;
                }
                return surface;
            }
            ENDCG
        }
    }
}
