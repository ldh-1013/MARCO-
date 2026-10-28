Shader "PoolV12/BlockoutSurface"
{
    Properties { _BaseColor("Color", Color)=(.8,.8,.8,1) _UseCut("Cuttable wall",Float)=0 _MarcoClickerTerrain("Clicker terrain",Float)=0 }
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
            float _MarcoMinusEnabled, _MarcoLightRadius, _MarcoLightIntensity, _MarcoFalloffPower;
            float4 _MarcoPlayerPosition, _MarcoLightColor;
            float _MarcoClickerTerrain, _MarcoClickerEnabled, _MarcoClickerRadius, _MarcoClickerIntensity, _MarcoClickerFalloffPower;
            float4 _MarcoClickerPosition, _MarcoClickerColor;
            Output vert(Input v) { Output o; o.vertex=UnityObjectToClipPos(v.vertex); o.world=mul(unity_ObjectToWorld,v.vertex).xyz; o.normal=UnityObjectToWorldNormal(v.normal); return o; }
            float4 frag(Output i):SV_Target
            {
                if (_UseCut > .5 && _PoolV12CutHeight > .01) clip(_WallBase + _PoolV12CutHeight - i.world.y);
                float shade=.64+.36*saturate(dot(normalize(i.normal),normalize(float3(-.4,.8,-.3))));
                float3 baseColor = _BaseColor.rgb * shade;
                float3 litColor = baseColor;
                if (_MarcoMinusEnabled > .5)
                {
                    float distanceToPlayer = distance(i.world, _MarcoPlayerPosition.xyz);
                    float atten = _MarcoLightRadius > .001
                        ? pow(saturate(1.0 - distanceToPlayer / _MarcoLightRadius), max(.01, _MarcoFalloffPower))
                        : 0.0;
                    litColor *= _MarcoLightColor.rgb * _MarcoLightIntensity * atten;
                }
                if (_MarcoClickerEnabled > .5 && _MarcoClickerTerrain > .5)
                {
                    float distanceToFlash = distance(i.world, _MarcoClickerPosition.xyz);
                    float flashAtten = _MarcoClickerRadius > .001
                        ? pow(saturate(1.0 - distanceToFlash / _MarcoClickerRadius), max(.01, _MarcoClickerFalloffPower))
                        : 0.0;
                    float3 flashColor = baseColor * _MarcoClickerColor.rgb * _MarcoClickerIntensity * flashAtten;
                    litColor = max(litColor, flashColor);
                }
                return float4(litColor, 1);
            }
            ENDCG
        }
    }
}
