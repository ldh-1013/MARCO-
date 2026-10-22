// Flat-color placeholder for the network test objects (player capsule, valves, exits, drain hatches).
// The project's URP asset renders cameras with the 2D Renderer by default, which only draws
// "SRPDefaultUnlit" / "Universal2D" passes, so built-in Standard materials stay invisible.
// This pass is drawn by the 2D and the 3D (Forward) renderer alike and needs no scene lights;
// a fixed fake light keeps the shapes readable, like the map's BlockoutSurface.
Shader "Marco/Network/Placeholder"
{
    Properties { [MainColor] _BaseColor("Color", Color) = (0.8, 0.8, 0.8, 1) }
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
            struct Input { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct Output { float4 vertex : SV_POSITION; float3 normal : TEXCOORD0; float3 worldPosition : TEXCOORD1; };
            float4 _BaseColor;
            // MARCO- microphone light, set globally by MicrophoneVisionController.
            float _MarcoMinusEnabled;
            float _MarcoLightRadius;
            float _MarcoLightIntensity;
            float _MarcoFalloffPower;
            float4 _MarcoPlayerPosition;
            float4 _MarcoLightColor;
            Output vert(Input v)
            {
                Output o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.worldPosition = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            fixed4 frag(Output i) : SV_Target
            {
                float shade = 0.55 + 0.45 * saturate(dot(normalize(i.normal), normalize(float3(-0.4, 0.8, -0.3))));
                float3 color = _BaseColor.rgb * shade;
                if (_MarcoMinusEnabled > 0.5)
                {
                    float distanceToPlayer = distance(i.worldPosition, _MarcoPlayerPosition.xyz);
                    float attenuation = _MarcoLightRadius > 0.001
                        ? pow(saturate(1.0 - distanceToPlayer / _MarcoLightRadius), max(0.01, _MarcoFalloffPower))
                        : 0.0;
                    color *= _MarcoLightColor.rgb * _MarcoLightIntensity * attenuation;
                }
                return float4(color, 1);
            }
            ENDCG
        }
    }
}
