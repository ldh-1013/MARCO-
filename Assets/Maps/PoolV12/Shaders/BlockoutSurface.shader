Shader "PoolV12/BlockoutSurface"
{
    Properties { _BaseColor("Color", Color)=(.8,.8,.8,1) _UseCut("Cuttable wall",Float)=0 }
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
            struct Input { float4 vertex:POSITION; float3 normal:NORMAL; };
            struct Output { float4 vertex:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; };
            float4 _BaseColor; float _UseCut, _PoolV12CutHeight, _WallBase;
            Output vert(Input v) { Output o; o.vertex=UnityObjectToClipPos(v.vertex); o.world=mul(unity_ObjectToWorld,v.vertex).xyz; o.normal=UnityObjectToWorldNormal(v.normal); return o; }
            fixed4 frag(Output i):SV_Target
            {
                if (_UseCut > .5 && _PoolV12CutHeight > .01) clip(_WallBase + _PoolV12CutHeight - i.world.y);
                float shade=.64+.36*saturate(dot(normalize(i.normal),normalize(float3(-.4,.8,-.3))));
                return float4(_BaseColor.rgb*shade,1);
            }
            ENDCG
        }
    }
}
