Shader "MARCO/ResultPodiumSurface"
{
    // 표시 전용. Result podium characters and props: self-lit toon shading so the stage never depends
    // on the map's lights or the MARCO- microphone light (Resources/CharacterAssets/MarcoMinusCharacter).
    Properties
    {
        _BaseMap("Texture", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0,1)) = .35
        _RimColor("Rim Color", Color) = (0,0,0,0)
        _Dim("Dim", Range(0,1)) = 0
        _Emission("Emission", Range(0,1)) = 0
        _Cull("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull [_Cull]
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _BaseMap;
            float4 _BaseMap_ST, _BaseColor, _RimColor;
            float _Cutoff, _Dim, _Emission;
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
            float4 frag(Output i, float facing:VFACE):SV_Target
            {
                float4 tex = tex2D(_BaseMap, i.uv) * _BaseColor;
                clip(tex.a - _Cutoff);
                float3 n = normalize(i.normal) * (facing > 0 ? 1 : -1);
                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                // Key light upper-front-left of the camera, soft two-tone step; a cool fill keeps backs readable.
                float key = smoothstep(-.05, .25, dot(n, normalize(float3(-.45, .75, -.5))));
                float3 lit = tex.rgb * lerp(float3(.42, .44, .55), float3(1.08, 1.04, 1.0), key);
                // Team-colored back rim separates the silhouette from the dark backdrop.
                float rim = pow(saturate(1 - dot(n, view)), 3) * saturate(dot(n, float3(0, .35, 1)) + .6);
                lit += _RimColor.rgb * rim * _RimColor.a * 1.6;
                lit = lerp(lit, tex.rgb, _Emission);
                // Losing side: desaturate and darken, still readable.
                float grey = dot(lit, float3(.299, .587, .114));
                lit = lerp(lit, grey * float3(.62, .66, .78), _Dim * .75);
                return float4(lit, 1);
            }
            ENDCG
        }
    }
}
