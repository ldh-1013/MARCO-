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
            float4 vert(float4 vertex:POSITION):SV_POSITION { return UnityObjectToClipPos(vertex); }
            fixed4 frag():SV_Target { return _BaseColor; }
            ENDCG
        }
    }
}
