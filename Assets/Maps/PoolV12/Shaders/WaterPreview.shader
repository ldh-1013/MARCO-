Shader "PoolV12/WaterPreview"
{
    Properties
    {
        _BaseColor ("Pool Water Color", Color) = (0.08, 0.69, 0.86, 0.45)
        _NormalMap ("SUIMONO Calm Ripples", 2D) = "bump" {}
        _DetailNormalMap ("SUIMONO Turbulent Ripples", 2D) = "bump" {}
        _FoamMap ("SUIMONO Foam", 2D) = "white" {}
        _RippleScale ("Ripple Tiling Per Meter", Float) = 0.28
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.32
        _FlowSpeed ("Ripple Speed", Range(0, 1)) = 0.055
        _FoamStrength ("Foam Strength", Range(0, 1)) = 0.08
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.22
        _ReflectionCube ("SUIMONO Reflection", Cube) = "" {}
        _DeepColor ("Deep Water Tint", Color) = (0.025, 0.32, 0.43, 1)
        _WaveHeight ("Wave Height (meters)", Range(0, 0.05)) = 0.018
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            // The game uses Renderer2D. Its mesh pass accepts SRPDefaultUnlit,
            // while UniversalForward silently skips the entire water surface.
            // This pass is also supported by the project's 3D preview renderer.
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_DetailNormalMap); SAMPLER(sampler_DetailNormalMap);
            TEXTURE2D(_FoamMap); SAMPLER(sampler_FoamMap);
            TEXTURECUBE(_ReflectionCube); SAMPLER(sampler_ReflectionCube);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _RippleScale;
                float _RippleStrength;
                float _FlowSpeed;
                float _FoamStrength;
                float _ReflectionStrength;
                float4 _DeepColor;
                float _WaveHeight;
            CBUFFER_END

            float _MarcoMinusEnabled;
            float _MarcoLightRadius;
            float _MarcoLightIntensity;
            float _MarcoFalloffPower;
            float4 _MarcoPlayerPosition;
            float4 _MarcoLightColor;
            float4 _MarcoWaterRipples[16]; // XZ origin, birth time, strength.
            float4 _MarcoWaterRippleBounds[16];
            int _MarcoWaterRippleCount;
            float _MarcoWaterRippleClock;

            // Analytic ring normals give small wakes detail even on the coarse pool mesh.
            float3 Interaction(float2 position)
            {
                float3 result = 0;
                [loop] for (int i = 0; i < min(_MarcoWaterRippleCount, 16); i++)
                {
                    float4 ring = _MarcoWaterRipples[i];
                    float age = _MarcoWaterRippleClock - ring.z;
                    float4 limits = _MarcoWaterRippleBounds[i];
                    if (age < 0 || age > 2.5 || any(position < limits.xy) || any(position > limits.zw)) continue;
                    float2 delta = position - ring.xy;
                    float radius = length(delta);
                    float wave = radius - age * 1.65;
                    if (abs(wave) > 1.2) continue;
                    float envelope = exp(-wave * wave * 5.0) * exp(-age * 1.3) * ring.w;
                    float slope = cos(wave * 13.0) * envelope * .28;
                    result.xy += delta / max(radius, .05) * slope;
                    result.z += pow(saturate(cos(wave * 13.0)), 6.0) * envelope;
                }
                return result;
            }

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.uv = input.uv;
                float edge = saturate(min(min(input.uv.x, 1.0 - input.uv.x),
                    min(input.uv.y, 1.0 - input.uv.y)) * 18.0);
                // Pin the perimeter to the pool's gameplay waterline (Y=0).
                float2 p = output.positionWS.xz;
                output.positionWS.y += _WaveHeight * edge *
                    (sin(p.x * 1.3 + p.y * 0.8 + _Time.y * 1.1) * 0.6 +
                     sin(p.x * -0.7 + p.y * 1.5 + _Time.y * 1.6) * 0.4);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.positionWS.xz * _RippleScale;
                float2 uvA = uv + _Time.y * _FlowSpeed * float2(0.7, 0.35);
                float2 uvB = uv * 1.37 + _Time.y * _FlowSpeed * float2(-0.4, 0.65);
                float3 rippleA = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvA));
                float3 rippleB = UnpackNormal(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, uvB));
                float3 interaction = Interaction(input.positionWS.xz);
                float3 normalWS = normalize(float3((rippleA.x + rippleB.x) * _RippleStrength + interaction.x,
                    1.0, (rippleA.y + rippleB.y) * _RippleStrength + interaction.y));
                float3 viewDirection = normalize(GetCameraPositionWS() - input.positionWS);
                normalWS *= viewDirection.y < 0.0 ? -1.0 : 1.0;
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirection)), 4.0);

                Light mainLight = GetMainLight();
                float3 halfDirection = normalize(mainLight.direction + viewDirection);
                float glint = pow(saturate(dot(normalWS, halfDirection)), 80.0);
                float edgeDistance = min(min(input.uv.x, 1.0 - input.uv.x),
                    min(input.uv.y, 1.0 - input.uv.y));
                float foam = smoothstep(0.4, 0.85,
                    SAMPLE_TEXTURE2D(_FoamMap, sampler_FoamMap, uvA * 0.65).r);
                foam *= 1.0 - smoothstep(0.0, 0.025, edgeDistance);

                float3 reflection = SAMPLE_TEXTURECUBE(_ReflectionCube, sampler_ReflectionCube,
                    reflect(-viewDirection, normalWS)).rgb;
                float3 color = lerp(_BaseColor.rgb, _DeepColor.rgb, fresnel * 0.35);
                color = lerp(color, reflection, _ReflectionStrength * (0.2 + 0.8 * fresnel));
                // Fine highlights remain visible in Renderer2D, which has no 3D main light.
                float rippleLight = pow(saturate(dot(normalWS,
                    normalize(viewDirection + normalize(float3(0.4, 1.0, 0.3))))), 100.0);
                color += mainLight.color.rgb * glint * 0.3 + rippleLight * 0.13;
                color = lerp(color, float3(0.8, 0.95, 0.98), foam * _FoamStrength);
                color = lerp(color, float3(.65, .9, .94), saturate(interaction.z * .32));

                if (_MarcoMinusEnabled > 0.5)
                {
                    float distanceToPlayer = distance(input.positionWS, _MarcoPlayerPosition.xyz);
                    float attenuation = _MarcoLightRadius > 0.001
                        ? pow(saturate(1.0 - distanceToPlayer / _MarcoLightRadius), max(0.01, _MarcoFalloffPower))
                        : 0.0;
                    color *= _MarcoLightColor.rgb * _MarcoLightIntensity * attenuation;
                }

                return half4(color, saturate(_BaseColor.a + fresnel * 0.2 + foam * _FoamStrength * 0.1 + interaction.z * .12));
            }
            ENDHLSL
        }
    }
}
