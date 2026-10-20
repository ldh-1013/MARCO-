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
            struct Output { float4 vertex : SV_POSITION; float3 normal : TEXCOORD0; };
            float4 _BaseColor;
            Output vert(Input v)
            {
                Output o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            fixed4 frag(Output i) : SV_Target
            {
                float shade = 0.55 + 0.45 * saturate(dot(normalize(i.normal), normalize(float3(-0.4, 0.8, -0.3))));
                return float4(_BaseColor.rgb * shade, 1);
            }
            ENDCG
        }
    }
}
