Shader "PoolV12/WaterPreview"
{
    Properties { _BaseColor("Color",Color)=(.12,.72,.82,.45) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _BaseColor;
            float _MarcoMinusEnabled, _MarcoLightRadius, _MarcoLightIntensity, _MarcoFalloffPower;
            float4 _MarcoPlayerPosition, _MarcoLightColor;
            struct Attributes { float4 vertex:POSITION; };
            struct Varyings { float4 position:SV_POSITION; float3 world:TEXCOORD0; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.world = mul(unity_ObjectToWorld, input.vertex).xyz;
                return output;
            }
            float4 frag(Varyings input):SV_Target
            {
                if (_MarcoMinusEnabled <= .5) return _BaseColor;
                float d = distance(input.world, _MarcoPlayerPosition.xyz);
                float atten = _MarcoLightRadius > .001
                    ? pow(saturate(1.0 - d / _MarcoLightRadius), max(.01, _MarcoFalloffPower))
                    : 0.0;
                return float4(_BaseColor.rgb * _MarcoLightColor.rgb * _MarcoLightIntensity * atten, _BaseColor.a);
            }
            ENDCG
        }
    }
}
