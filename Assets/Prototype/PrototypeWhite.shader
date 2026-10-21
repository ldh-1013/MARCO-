Shader "Marco/PrototypeWhite"
{
    Properties { _Color ("Color", Color) = (0.9,0.9,0.9,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 vertex : SV_POSITION; float3 world : TEXCOORD0; };
            fixed4 _Color;
            float _MarcoMinusEnabled, _MarcoLightRadius, _MarcoLightIntensity, _MarcoFalloffPower;
            float4 _MarcoPlayerPosition, _MarcoLightColor;
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                if (_MarcoMinusEnabled <= .5) return _Color;
                float d = distance(i.world, _MarcoPlayerPosition.xyz);
                float atten = _MarcoLightRadius > .001
                    ? pow(saturate(1.0 - d / _MarcoLightRadius), max(.01, _MarcoFalloffPower))
                    : 0.0;
                return float4(_Color.rgb * _MarcoLightColor.rgb * _MarcoLightIntensity * atten, _Color.a);
            }
            ENDCG
        }
    }
}
