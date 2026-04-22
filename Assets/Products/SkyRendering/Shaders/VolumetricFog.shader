Shader "Zerl/PostProcessing/VolumetricFog"
{
    Properties
    {
        
        [Header(RayMarching)]
        [Space(10)]
        _MaxDistance("Max distance", float) = 100
        _StepSize("Step size", Range(0.1, 20)) = 1 //光线步进距离
        _NoiseOffset("Noise offset", float) = 0 //起始光线步进位置偏移
        
        [Header(Fog)]
        [Space(10)]
        _Color("Fog Color", Color) = (1, 1, 1, 1)
        [NoScaleOffset]_FogNoise("Fog noise", 3D) = "white" {}
        _NoiseTiling("Noise Tiling", float) = 1
        _DensityThreshold("Density threshold", Range(0, 1)) = 0.1
        _DensityMultiplier("Density multiplier", Range(0, 10)) = 1
        _DensityPower("Density power", Range(0.1, 5)) = 1
        
        [Header(Light)]
        [Space(10)]
        [HDR]_LightContribution("Light contribution", Color) = (1, 1, 1, 1)
        _LightScattering("Light scattering", Range(0, 1)) = 0.2
        
        [HideInInspector]_FogBoundsMin("",Vector) =(1,1,1)
        [HideInInspector]_FogBoundsMax("",Vector) =(1,1,1)
        
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_local __ AABBBOX_ON  //

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _Color;
            float _MaxDistance;
            float _DensityMultiplier;
            float _StepSize;
            float _NoiseOffset;
            TEXTURE3D(_FogNoise);
            SamplerState sampler_TrilinearRepeat;
            float _DensityThreshold;
            float _DensityPower;
            float _NoiseTiling;
            float4 _LightContribution;
            float _LightScattering;
            float4 _BlitTexture_TexelSize;
            float3 _FogBoundsMin = float3(-12.5,-10.5,-15.0);
            float3 _FogBoundsMax = float3(12.5,12,15.0);

            float henyey_greenstein(float angle, float scattering)
            {
                return (1.0 - angle * angle) / (4.0 * PI * pow(1.0 + scattering * scattering - (2.0 * scattering) * angle, 1.5f));
            }
            
            
            // #define Box_Min  float3(-12.5,-10.5,-15.0)
            // #define Box_Max  float3(12.5,12,15.0)
            //_FogBoundsMin
            //_FogBoundsMax
            
            // https://gist.github.com/DomNomNom/46bb1ce47f68d255fd5d
            // compute the near and far intersections of the cube (stored in the x and y components) using the slab method
            // no intersection means really tNear > tFar
            float2 intersectAABB(float3 rayOrigin, float3 rayDir, float3 boxMin, float3 boxMax)
            {
                float3 invDir = 1.0 / rayDir;
                float3 tMin = (boxMin - rayOrigin) * invDir;
                float3 tMax = (boxMax - rayOrigin) * invDir;
                float3 t1 = min(tMin, tMax);
                float3 t2 = max(tMin, tMax);
                float tNear = max(max(t1.x, t1.y), t1.z);
                float tFar = min(min(t2.x, t2.y), t2.z);
                return float2(tNear, tFar);
            }
            
            float SampleFogDensity(float3 worldPos)
            {
                float4 noise = _FogNoise.SampleLevel(sampler_TrilinearRepeat, worldPos * 0.01 * _NoiseTiling +float3(-_Time.y*0.02,0.0,sin(_Time.x)), 0);
                // float density = dot(noise, noise);
                float density = noise.g; //单通道噪声
                density =pow((saturate(density - _DensityThreshold)),_DensityPower)  * _DensityMultiplier;
                return density;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                
                // float4 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, IN.texcoord);
                float4 screenCol =_BlitTexture.Sample(sampler_LinearClamp,IN.texcoord);
                float depth = SampleSceneDepth(IN.texcoord);
                float3 worldPos = ComputeWorldSpacePosition(IN.texcoord, depth, UNITY_MATRIX_I_VP);

                float3 entryPoint = _WorldSpaceCameraPos;
                float3 viewDir = worldPos - _WorldSpaceCameraPos;
                float3 rayDir = normalize(viewDir);
                float rayLength = length(viewDir);
                
                //AABB BOX
                #ifdef AABBBOX_ON
                    float2 inter = intersectAABB(entryPoint, rayDir, _FogBoundsMin, _FogBoundsMax);
                    if(inter.x>inter.y) discard; // 未进入雾区
                #endif

                float2 pixelCoords = IN.texcoord * _BlitTexture_TexelSize.zw;
                float distLimit = min(rayLength, _MaxDistance);
                float distTravelled = InterleavedGradientNoise(pixelCoords, (int)(_Time.y / max(HALF_EPS, unity_DeltaTime.x))) * _NoiseOffset;
                float transmittance = 1;
                float4 fogCol = _Color;

                while(distTravelled < distLimit)
                {
                    float3 rayPos = entryPoint + rayDir * distTravelled;
                    float density = SampleFogDensity(rayPos); //采样密度贴图
                    if (density > 0)
                    {
                        Light mainLight = GetMainLight(TransformWorldToShadowCoord(rayPos));
                        fogCol.rgb += mainLight.color.rgb * _LightContribution.rgb * henyey_greenstein(dot(rayDir, mainLight.direction), _LightScattering) * density * mainLight.shadowAttenuation * _StepSize;
                        transmittance *= exp(-density * _StepSize); //transmittance 透光率 指数衰减
                    }
                    distTravelled += _StepSize;
                }
                
                return lerp(screenCol, fogCol, 1.0 - saturate(transmittance));
            }
            ENDHLSL
        }
    }
}