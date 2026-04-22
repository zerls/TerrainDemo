Shader "Zerl/PostProcessing/VolumetricCloud"
{
    Properties {}

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "CloudRaymarch"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_local __ AABBBOX_ON  //

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"


            #include "cloud_common.hlsl"
            #define BLUE_SIZE 512
            float3 _offset;

            struct FragmentOutput
            {
                half4 color : SV_Target0; // 对应 m_QuarterResCloudColor
                float depth : SV_Target1; // 对应 m_QuarterResCloudDepth
            };

            FragmentOutput frag(Varyings IN)
            {
                float2 uv = IN.texcoord;
                // float4 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, IN.texcoord);
                float4 screenColor = SAMPLE_TEXTURE2D_X(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv);

                // Offset retarget for new seeds each frame
                uint2 offset = uint2(float2(0.754877669, 0.569840296) * (frameData.frameIndex.x) * uint2(_ScreenParams.xy));
                uint2 pixelCoord = uint2(uv * _ScreenParams.xy);
                uint2 offsetId = pixelCoord + offset;
                offsetId.x = offsetId.x % BLUE_SIZE;
                offsetId.y = offsetId.y % BLUE_SIZE;
                float blueNoise = inBlueNoise.SampleLevel(linearRepeatSampler, offsetId / BLUE_SIZE, 0).r;

                // 从屏幕空间重建世界空间位置,未基于地球中心坐标系
                float depth = SampleSceneDepth(IN.texcoord);
                float3 worldPos = ComputeWorldSpacePosition(IN.texcoord, depth, UNITY_MATRIX_I_VP);

                // bool bEarlyOutCloud = false;
                float cloudZ;
                half4 CloudColor = cloudColorCompute(worldPos, _offset, uv, blueNoise, cloudZ);

                //=================Output=======================
                FragmentOutput output;
                output.color = CloudColor;
                output.depth = cloudZ;

                return output;
            }
            ENDHLSL
        }

        Pass
        {
            Name "CloudReconstruct"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment fragReconstruct

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            
            #include "sky_shared_struct.hlsl"
            #include "shared_functions.hlsl"

            // --- 纹理与采样器声明 ---
            // 1/4 分辨率输入 (来自 Pass 0)
            TEXTURE2D(inCloudRenderTexture);                            SAMPLER(sampler_inCloudRenderTexture);
            TEXTURE2D(inCloudDepthTexture);                             SAMPLER(sampler_inCloudDepthTexture);
            // 全分辨率历史帧输入 (来自上一帧的 Pass 1 结果)
            TEXTURE2D(inCloudReconstructionTextureHistory);             SAMPLER(sampler_inCloudReconstructionTextureHistory);
            TEXTURE2D(inCloudDepthReconstructionTextureHistory);        SAMPLER(sampler_inCloudDepthReconstructionTextureHistory);

            // MRT 输出结构
            struct FragmentOutput
            {
                half4 color : SV_Target0;
                float depth : SV_Target1;
            };

           FragmentOutput fragReconstruct(Varyings IN)
            {
                FragmentOutput output;
                float2 uv = IN.texcoord;

                int2 texSize = int2(_ScreenParams.xy);
                int2 workPos = int2(uv * texSize);
                
                // 手动计算 1/4 分辨率的 Texel Size，确保 3x3 采样准确
                float2 texelSizeQuarter = 4.0 / _ScreenParams.xy;
               
                float curDepthZ = SAMPLE_TEXTURE2D(inCloudDepthTexture, sampler_PointClamp, uv).r;
                half4 curColor = SAMPLE_TEXTURE2D(inCloudRenderTexture, sampler_LinearClamp, uv);

                // 3. 历史重投影
                float3 worldPosCur = ComputeWorldSpacePosition(uv, curDepthZ, UNITY_MATRIX_I_VP);
               
                float4 projPosPrev = mul(_PreviousCameraVP, float4(worldPosCur, 1.0));
                float2 uvPrev = (projPosPrev.xy / projPosPrev.w) * 0.5 + 0.5;

                bool bCameraCut = frameData.bCameraCut != 0;
                bool bPrevUvValid = all(uvPrev >= 0.0 && uvPrev <= 1.0) && !bCameraCut;

                half4 finalColor = half4(0, 0, 0, 0);
                float finalDepth = curDepthZ;

                if (bPrevUvValid)
                {
                    float preDepthZ = SAMPLE_TEXTURE2D(inCloudDepthReconstructionTextureHistory, sampler_PointClamp, uvPrev).r;
                    half4 preColor = SAMPLE_TEXTURE2D(inCloudReconstructionTextureHistory, sampler_LinearClamp, uvPrev);

                    uint bayerIndex = frameData.frameIndex.x % 16;
                    int2 bayerOffset = int2(kBayerMatrix16[bayerIndex] % 4, kBayerMatrix16[bayerIndex] / 4);
                    int2 workDeltaPos = workPos % 4;
                    bool bUpdateEvaluate = (workDeltaPos.x == bayerOffset.x) && (workDeltaPos.y == bayerOffset.y);

                    // 将深度判断提到外面。云边缘移动产生严重深度断层时，立刻丢弃历史，防止拖尾残影
                    if (abs(preDepthZ - curDepthZ) > 0.05)
                    {
                        finalColor = curColor;
                        finalDepth = curDepthZ;
                    }
                    else if (bUpdateEvaluate)
                    {
                        // 轮到当前像素更新，执行完整的 3x3 邻域方差裁剪
                        finalDepth = curDepthZ;

                        float wsum = 0.0;
                        float4 vsum = 0.0;
                        float4 vsum2 = 0.0;

                        for (int y = -1; y <= 1; ++y)
                        {
                            for (int x = -1; x <= 1; ++x)
                            {
                                float2 neighborUv = uv + texelSizeQuarter * float2(x, y);
                                float4 neigh = SAMPLE_TEXTURE2D(inCloudRenderTexture, sampler_PointClamp, neighborUv);
                                
                                float w = exp(-0.75 * (x * x + y * y));
                                vsum += neigh * w;
                                vsum2 += neigh * neigh * w;
                                wsum += w;
                            }
                        }

                        float4 ex = vsum / wsum;
                        float4 ex2 = vsum2 / wsum;
                        float4 dev = sqrt(max(ex2 - ex * ex, 0.0));
                        float boxSize = 2.5;

                        float4 nmin = ex - dev * boxSize;
                        float4 nmax = ex + dev * boxSize;

                        float4 clampColorHistory = clamp(preColor, nmin, nmax);
                        finalColor = lerp(clampColorHistory, curColor, 0.5);
                    }
                    else
                    {
                        // 消除点状纹的ticker。
                        // 即便没轮到更新，也微微拉取 3% 的当前帧颜色(EMA 滤波)。
                        // 这样在相机静止时，历史帧会被持续柔和地冲刷，防止像素卡在旧状态形成马赛克。
                        finalColor = lerp(preColor, curColor, 0.03); 
                        finalDepth = preDepthZ;
                    }
                }
                else
                {
                    // 历史失效（如屏幕边缘新出现的云）
                    finalColor = curColor;
                    finalDepth = curDepthZ;
                }
                
                output.color = finalColor;
                output.depth = finalDepth;
                return output;
            }
            ENDHLSL
        }

        Pass
        {
            Name "CloudComposite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment fragComposite

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            #include "sky_shared_struct.hlsl"
            #include "shared_functions.hlsl"

            // 接收来自 Pass 1 的全分辨率重建结果
            TEXTURE2D(inCloudReconstructionTexture);
            SAMPLER(sampler_inCloudReconstructionTexture);

            half4 fragComposite(Varyings IN) : SV_Target
            {
                float2 uv = IN.texcoord;

                // 1. 采样当前屏幕的 Opaque 场景颜色 (作为底图)
                half4 srcColor = SAMPLE_TEXTURE2D_X(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, UnityStereoTransformScreenSpaceTex(uv));

                // 2. 采样重建后的体积云颜色 (RGB: 散射光, A: 透射率)
                half4 cloudColor = SAMPLE_TEXTURE2D(inCloudReconstructionTexture, sampler_inCloudReconstructionTexture, uv);

                // 3. 采样相机深度
                #if UNITY_REVERSED_Z
                float sceneDepth = SampleSceneDepth(uv);
                #else
                    float sceneDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
                #endif

                half3 result = srcColor.rgb;

                // 4. 深度测试与合成
                bool isSky = sceneDepth <= 0.00001f;

                if (isSky)
                {
                    // 预乘 Alpha 混合: 背景颜色 * 透射率 + 云层散射光
                    result = srcColor.rgb * cloudColor.a + cloudColor.rgb;

                    // (可选)FogColor 的合并逻辑，可以在这里追加:
                    // result = result * fogColor.a + max(0.0, fogColor.rgb);
                }
                else
                {
                    // 云层渲染在高山等 Opaque 物体前方，
                    // 需要在这里引入 inCloudDepthReconstructionTextureHistory 与 sceneLinearDepth 进行比较，
                    // 并根据云的深度进行混合。大多数情况下云在背景，所以这里保持原样。
                }

                // ==========================================
                // 预留的 God Rays (体积光/丁达尔效应) 接口
                // ==========================================
                /*
                if (frameData.sky.cloudConfig.CloudGodRay != 0)
                {
                    // 获取 URP 主光源和阴影衰减
                    Light mainLight = GetMainLight();
                    
                    // 这里需要沿着视线向相机步进，计算云层投射到空气中的体积阴影
                    // URP 中需要使用 TransformWorldToShadowCoord() 和 MainLightRealtimeShadow()
                }
                */

                return half4(result, 1.0);
            }
            ENDHLSL
        }
    }
}