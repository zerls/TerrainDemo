using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Core.Rendering.RenderFeature
{
    public class Atmosphere : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Header("General Settings")] public string m_profilerTag = "Atmosphere";
            public RenderPassEvent lutGenerationEvent = RenderPassEvent.BeforeRenderingShadows;

            public ComputeShader computeShader;
            public AtmosphereSettings atmosphereSettings;

            [Space(10)] public bool generateTransAndMultiScatterOnce = false;

            [Header("Aerial Perspective Effect Settings")] [Tooltip("启用Aerial Perspective后处理效果")]
            public bool enableAerialPerspectiveEffect = true;

            public Material aerialPerspectiveMaterial;

            [Tooltip("Aerial Perspective效果在渲染管线中的位置")]
            public RenderPassEvent aerialPerspectiveEvent = RenderPassEvent.BeforeRenderingPostProcessing;

            [Header("LUT Dimensions")] [HideInInspector]
            public Vector2Int transmittanceLUTSize = new Vector2Int(256, 64);

            [HideInInspector] public Vector2Int multiScatteringLUTSize = new Vector2Int(32, 32);
            [HideInInspector] public Vector2Int skyViewLUTSize = new Vector2Int(256, 128);
            [HideInInspector] public Vector2Int aerialPerspectiveLUTSize = new Vector2Int(1024, 32);
            
        }

        // LUT生成Pass
        class AtmosphereLUTGenerationPass : ScriptableRenderPass
        {
            private Settings m_Settings;

            // RTHandles for LUTs
            private RTHandle m_TransmittanceLUT;
            private RTHandle m_MultiScatteringLUT;
            private RTHandle m_SkyViewLUT;
            private RTHandle m_AerialPerspectiveLUT;

            // Compute Shader Kernels
            private int m_KernelTransmittance;
            private int m_KernelMultiScattering;
            private int m_KernelSkyView;
            private int m_KernelAerialPerspective;

            // Shader Property IDs
            private static readonly int TransmittanceLUTID = Shader.PropertyToID("TransmittanceLUT");
            private static readonly int MultiScatteringLUTID = Shader.PropertyToID("MultiScatteringLUT");
            private static readonly int TransmittanceLUT_ReadOnlyID = Shader.PropertyToID("TransmittanceLUT_ReadOnly");
            private static readonly int MultiScatteringLUT_ReadOnlyID = Shader.PropertyToID("MultiScatteringLUT_ReadOnly");
            private static readonly int SkyViewLUTID = Shader.PropertyToID("SkyViewLUT");
            private static readonly int AerialPerspectiveLUTID = Shader.PropertyToID("AerialPerspectiveLUT");

            private static readonly int TransmittanceLutGlobalID = Shader.PropertyToID("_transmittanceLut");
            private static readonly int MultiScatteringLutGlobalID = Shader.PropertyToID("_multiScatteringLut");
            private static readonly int SkyViewLutGlobalID = Shader.PropertyToID("_skyViewLut");
            private static readonly int AerialPerspectiveLutGlobalID = Shader.PropertyToID("_aerialPerspectiveLut");
            
            private static readonly int inSkyViewLutID =Shader.PropertyToID("inSkyViewLut");
            private static readonly int inSkyIrradianceID =Shader.PropertyToID("inSkyIrradiance");
            private static readonly int inTransmittanceLutID =Shader.PropertyToID("inTransmittanceLut");
            private static readonly int inFroxelScatterID =Shader.PropertyToID("inFroxelScatter");
            
            private static readonly int WidthID = Shader.PropertyToID("_Width");
            private static readonly int HeightID = Shader.PropertyToID("_Height");

            // Atmosphere Parameters
            private static readonly int SeaLevelID = Shader.PropertyToID("_SeaLevel");
            private static readonly int PlanetRadiusID = Shader.PropertyToID("_PlanetRadius");
            private static readonly int AtmosphereHeightID = Shader.PropertyToID("_AtmosphereHeight");
            private static readonly int SunLightIntensityID = Shader.PropertyToID("_SunLightIntensity");
            private static readonly int SunLightColorID = Shader.PropertyToID("_SunLightColor");
            private static readonly int SunDiskAngleID = Shader.PropertyToID("_SunDiskAngle");
            private static readonly int RayleighScaleID = Shader.PropertyToID("_RayleighScatteringScale");
            private static readonly int RayleighScaleHeightID = Shader.PropertyToID("_RayleighScatteringScalarHeight");
            private static readonly int MieScaleID = Shader.PropertyToID("_MieScatteringScale");
            private static readonly int MieAnisotropyID = Shader.PropertyToID("_MieAnisotropy");
            private static readonly int MieScaleHeightID = Shader.PropertyToID("_MieScatteringScalarHeight");
            private static readonly int OzoneAbsorptionScaleID = Shader.PropertyToID("_OzoneAbsorptionScale");
            private static readonly int OzoneCenterHeightID = Shader.PropertyToID("_OzoneLevelCenterHeight");
            private static readonly int OzoneWidthID = Shader.PropertyToID("_OzoneLevelWidth");
            private static readonly int AerialPerspectiveDistanceID = Shader.PropertyToID("_AerialPerspectiveDistance");
            private static readonly int AerialPerspectiveVoxelSizeID = Shader.PropertyToID("_AerialPerspectiveVoxelSize");
            private static readonly int MiePhaseParamsID = Shader.PropertyToID("_MiePhaseParams");
            
            private static readonly int WorldSpaceCameraPosID = Shader.PropertyToID("_WorldSpaceCameraPos");
            private static readonly int ScreenParamsID = Shader.PropertyToID("_ScreenParams");
            private static readonly int CameraToWorldID = Shader.PropertyToID("unity_CameraToWorld");
            private static readonly int MainLightDirectionID = Shader.PropertyToID("_MainLightDirection");

            private bool m_IsInitialized = false;
            private int m_FrameCount = 0;

            public AtmosphereLUTGenerationPass(Settings settings)
            {
                m_Settings = settings;
            }

            private void InitializeLUTs()
            {
                if (m_Settings.computeShader == null)
                {
                    Debug.LogError("AtmosphereRender: Compute Shader is not assigned!");
                    return;
                }

                // 检查RTHandles是否需要重新分配（可能在视图切换时失效）
                bool needsReinitialization = !m_IsInitialized;

                if (m_IsInitialized)
                {
                    // 验证现有RTHandles是否仍然有效
                    if (m_TransmittanceLUT == null || m_MultiScatteringLUT == null ||
                        m_SkyViewLUT == null || m_AerialPerspectiveLUT == null)
                    {
                        needsReinitialization = true;
                        m_IsInitialized = false;
                    }
                }

                if (!needsReinitialization)
                    return;

                // Find kernels
                m_KernelTransmittance = m_Settings.computeShader.FindKernel("CSTransmittanceLUT");
                m_KernelMultiScattering = m_Settings.computeShader.FindKernel("CSMultiScatteringLUT");
                m_KernelSkyView = m_Settings.computeShader.FindKernel("CSSkyViewLUT");
                m_KernelAerialPerspective = m_Settings.computeShader.FindKernel("CSAerialPerspectiveLUT");

                // 创建/分配 LUT 渲染目标，使用 Settings 中的尺寸
                var skySize = m_Settings.skyViewLUTSize;
                RenderTextureDescriptor skyViewDesc = new RenderTextureDescriptor(skySize.x, skySize.y, RenderTextureFormat.ARGBFloat, 0)
                {
                    useMipMap = false,
                    autoGenerateMips = false,
                    depthBufferBits = 0,
                    enableRandomWrite = true
                };

                var transSize = m_Settings.transmittanceLUTSize;
                RenderTextureDescriptor transmittanceDesc = new RenderTextureDescriptor(transSize.x, transSize.y, RenderTextureFormat.ARGBFloat, 0)
                {
                    useMipMap = false,
                    autoGenerateMips = false,
                    depthBufferBits = 0,
                    enableRandomWrite = true
                };

                var multiSize = m_Settings.multiScatteringLUTSize;
                RenderTextureDescriptor multiScatteringDesc = new RenderTextureDescriptor(multiSize.x, multiSize.y, RenderTextureFormat.ARGBFloat, 0)
                {
                    useMipMap = false,
                    autoGenerateMips = false,
                    depthBufferBits = 0,
                    enableRandomWrite = true
                };

                var aerialSize = m_Settings.aerialPerspectiveLUTSize;
                RenderTextureDescriptor aerialPerspectiveDesc = new RenderTextureDescriptor(aerialSize.x, aerialSize.y, RenderTextureFormat.ARGBFloat, 0)
                {
                    useMipMap = false,
                    autoGenerateMips = false,
                    depthBufferBits = 0,
                    enableRandomWrite = true
                };

                RenderingUtils.ReAllocateIfNeeded(ref m_SkyViewLUT, skyViewDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_SkyViewLut");
                RenderingUtils.ReAllocateIfNeeded(ref m_TransmittanceLUT, transmittanceDesc, FilterMode.Bilinear, TextureWrapMode.Clamp,
                    name: "_TransmittanceLut");
                RenderingUtils.ReAllocateIfNeeded(ref m_MultiScatteringLUT, multiScatteringDesc, FilterMode.Bilinear, TextureWrapMode.Clamp,
                    name: "_MultiScatteringLut");
                RenderingUtils.ReAllocateIfNeeded(ref m_AerialPerspectiveLUT, aerialPerspectiveDesc, FilterMode.Bilinear, TextureWrapMode.Clamp,
                    name: "_AerialPerspectiveLut");

                m_IsInitialized = true;
                m_FrameCount = 0; // 重置帧计数，强制重新生成所有LUTs
            }

            private void SetComputeShaderParameters(CommandBuffer cmd, ref RenderingData renderingData)
            {
                var cs = m_Settings.computeShader;
                var param = m_Settings.atmosphereSettings;

                if (cs == null || param == null)
                    return;

                cmd.SetComputeFloatParam(cs, SeaLevelID, param.SeaLevel);
                cmd.SetComputeFloatParam(cs, PlanetRadiusID, param.PlanetRadius);
                cmd.SetComputeFloatParam(cs, AtmosphereHeightID, param.AtmosphereHeight);
                cmd.SetComputeFloatParam(cs, SunLightIntensityID, param.SunLightIntensity);
                cmd.SetComputeVectorParam(cs, SunLightColorID, param.SunLightColor);
                cmd.SetComputeFloatParam(cs, SunDiskAngleID, param.SunDiskAngle);
                cmd.SetComputeFloatParam(cs, RayleighScaleID, param.RayleighScatteringScale);
                cmd.SetComputeFloatParam(cs, RayleighScaleHeightID, param.RayleighScatteringScalarHeight);
                cmd.SetComputeFloatParam(cs, MieScaleID, param.MieScatteringScale);
                cmd.SetComputeFloatParam(cs, MieAnisotropyID, param.MieAnisotropy);
                cmd.SetComputeFloatParam(cs, MieScaleHeightID, param.MieScatteringScalarHeight);
                cmd.SetComputeFloatParam(cs, OzoneAbsorptionScaleID, param.OzoneAbsorptionScale);
                cmd.SetComputeFloatParam(cs, OzoneCenterHeightID, param.OzoneLevelCenterHeight);
                cmd.SetComputeFloatParam(cs, OzoneWidthID, param.OzoneLevelWidth);
                cmd.SetComputeFloatParam(cs, AerialPerspectiveDistanceID, param.AerialPerspectiveDistance);
                cmd.SetComputeVectorParam(cs, AerialPerspectiveVoxelSizeID, param.AerialPerspectiveVoxelSize);
                cmd.SetComputeVectorParam(cs, MiePhaseParamsID, param.MiePhaseParams);
                
                // --- 新增：传递相机和光照数据 ---
                Camera camera = renderingData.cameraData.camera;
                
                // 传递相机位置
                cmd.SetComputeVectorParam(cs, WorldSpaceCameraPosID, camera.transform.position);
                
                // 传递屏幕参数: x = width, y = height, z = 1 + 1/width, w = 1 + 1/height
                cmd.SetComputeVectorParam(cs, ScreenParamsID, new Vector4(camera.pixelWidth, camera.pixelHeight, 1.0f + 1.0f / camera.pixelWidth, 1.0f + 1.0f / camera.pixelHeight));
                
                // 传递相机矩阵
                cmd.SetComputeMatrixParam(cs, CameraToWorldID, camera.cameraToWorldMatrix);

                // 传递主光源方向 (URP)
                Vector3 mainLightDir = Vector3.down; // 默认值
                if (renderingData.lightData.mainLightIndex != -1)
                {
                    VisibleLight mainLight = renderingData.lightData.visibleLights[renderingData.lightData.mainLightIndex];
                    // URP 光照方向是从物体指向光源，通过矩阵的第2列取反获取前向向量
                    mainLightDir = -mainLight.localToWorldMatrix.GetColumn(2); 
                }
                cmd.SetComputeVectorParam(cs, MainLightDirectionID, mainLightDir);
                // --------------------------------
                
                Shader.SetGlobalFloat(AerialPerspectiveDistanceID,param.AerialPerspectiveDistance);
                Shader.SetGlobalVector(AerialPerspectiveVoxelSizeID,param.AerialPerspectiveVoxelSize);
            }

            private void DispatchComputeShader(CommandBuffer cmd, int kernel, RTHandle target)
            {
                var cs = m_Settings.computeShader;

                int width = target.rt.width;
                int height = target.rt.height;

                cmd.SetComputeIntParam(cs, WidthID, width);
                cmd.SetComputeIntParam(cs, HeightID, height);

                int threadGroupsX = Mathf.CeilToInt(width / 8.0f);
                int threadGroupsY = Mathf.CeilToInt(height / 8.0f);

                cmd.DispatchCompute(cs, kernel, threadGroupsX, threadGroupsY, 1);
            }

            private void GenerateTransmittanceAndMultiScatteringLUT(CommandBuffer cmd, ref RenderingData renderingData)
            {
                var cs = m_Settings.computeShader;

                SetComputeShaderParameters(cmd,ref renderingData);

                // Generate Transmittance LUT
                cmd.SetComputeTextureParam(cs, m_KernelTransmittance, TransmittanceLUTID, m_TransmittanceLUT);
                DispatchComputeShader(cmd, m_KernelTransmittance, m_TransmittanceLUT);

                // Generate MultiScattering LUT
                cmd.SetComputeTextureParam(cs, m_KernelMultiScattering, TransmittanceLUT_ReadOnlyID, m_TransmittanceLUT);
                cmd.SetComputeTextureParam(cs, m_KernelMultiScattering, MultiScatteringLUTID, m_MultiScatteringLUT);
                DispatchComputeShader(cmd, m_KernelMultiScattering, m_MultiScatteringLUT);
            }

            private void GenerateSkyViewLUT(CommandBuffer cmd, ref RenderingData renderingData)
            {
                var cs = m_Settings.computeShader;

                SetComputeShaderParameters(cmd,ref renderingData);

                cmd.SetComputeTextureParam(cs, m_KernelSkyView, TransmittanceLUT_ReadOnlyID, m_TransmittanceLUT);
                cmd.SetComputeTextureParam(cs, m_KernelSkyView, MultiScatteringLUT_ReadOnlyID, m_MultiScatteringLUT);
                cmd.SetComputeTextureParam(cs, m_KernelSkyView, SkyViewLUTID, m_SkyViewLUT);
                DispatchComputeShader(cmd, m_KernelSkyView, m_SkyViewLUT);
            }

            private void GenerateAerialPerspectiveLUT(CommandBuffer cmd,ref RenderingData renderingData)
            {
                var cs = m_Settings.computeShader;

                SetComputeShaderParameters(cmd,ref renderingData);

                cmd.SetComputeTextureParam(cs, m_KernelAerialPerspective, TransmittanceLUT_ReadOnlyID, m_TransmittanceLUT);
                cmd.SetComputeTextureParam(cs, m_KernelAerialPerspective, MultiScatteringLUT_ReadOnlyID, m_MultiScatteringLUT);
                cmd.SetComputeTextureParam(cs, m_KernelAerialPerspective, AerialPerspectiveLUTID, m_AerialPerspectiveLUT);
                DispatchComputeShader(cmd, m_KernelAerialPerspective, m_AerialPerspectiveLUT);
            }

            private void GenerateAllLUTs(CommandBuffer cmd, ref RenderingData renderingData)
            {
                GenerateTransmittanceAndMultiScatteringLUT(cmd,ref renderingData);
                GenerateSkyViewLUT(cmd,ref renderingData);
                GenerateAerialPerspectiveLUT(cmd,ref renderingData);
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                InitializeLUTs();

                // 如果是Game视图相机，且之前的帧是Scene视图，重置帧计数以强制重新生成LUTs
                if (!renderingData.cameraData.isSceneViewCamera)
                {
                    // 检查RTHandle是否仍然有效，如果无效则重置
                    if (m_IsInitialized && (m_AerialPerspectiveLUT == null || !m_AerialPerspectiveLUT.rt.IsCreated()))
                    {
                        m_FrameCount = 0;
                    }
                }
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                // 跳过Scene视图的渲染
                if (renderingData.cameraData.isSceneViewCamera)
                    return;

                if (!m_IsInitialized)
                    return;

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, new ProfilingSampler(m_Settings.m_profilerTag)))
                {
                    cmd.BeginSample(m_Settings.m_profilerTag);
                    // Initial generation or full refresh
                    // 当m_FrameCount为0时，说明是第一次渲染或刚从Scene视图切换回来
                    if (m_FrameCount == 0)
                    {
                        GenerateAllLUTs(cmd,ref renderingData);
                    }

                    // Auto-refresh logic based on frame count
                    int frame = Time.frameCount;

                    if (frame % 120 == 0)
                    {
                        DynamicGI.UpdateEnvironment();
                        m_FrameCount = 1; // Reset frame count after GI update;
                        Debug.Log("Global Illumination Updated");
                    }
                    else if (frame % 16 == 0 && !m_Settings.generateTransAndMultiScatterOnce)
                    {
                        GenerateTransmittanceAndMultiScatteringLUT(cmd,ref renderingData);
                        Debug.Log("Transmittance & MultiScattering LUTs Updated");
                    }
                    else if (frame % 6 == 0)
                    {
                        GenerateAerialPerspectiveLUT(cmd,ref renderingData);
                        Debug.Log("Aerial Perspective LUT Updated");
                    }
                    else if (frame % 4 == 0)
                    {
                        GenerateSkyViewLUT(cmd,ref renderingData);
                        Debug.Log("Sky View LUT Updated ");
                    }

                    // Set global textures
                    cmd.SetGlobalTexture(TransmittanceLutGlobalID, m_TransmittanceLUT);
                    cmd.SetGlobalTexture(MultiScatteringLutGlobalID, m_MultiScatteringLUT);
                    cmd.SetGlobalTexture(SkyViewLutGlobalID, m_SkyViewLUT);
                    cmd.SetGlobalTexture(AerialPerspectiveLutGlobalID, m_AerialPerspectiveLUT);

                    cmd.SetGlobalTexture(inSkyViewLutID,m_SkyViewLUT);
                    cmd.SetGlobalTexture(inSkyIrradianceID,m_MultiScatteringLUT);
                    cmd.SetGlobalTexture(inTransmittanceLutID,m_TransmittanceLUT);
                    cmd.SetGlobalTexture(inFroxelScatterID,m_AerialPerspectiveLUT);
                    
                    cmd.SetGlobalFloat(SunDiskAngleID, m_Settings.atmosphereSettings.SunDiskAngle);
                    cmd.SetGlobalFloat(SunLightIntensityID, m_Settings.atmosphereSettings.SunLightIntensity);
                    cmd.SetGlobalColor(SunLightColorID, m_Settings.atmosphereSettings.SunLightColor);
                    cmd.EndSample(m_Settings.m_profilerTag);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);

                m_FrameCount++;
            }

            public override void OnCameraCleanup(CommandBuffer cmd)
            {
            }

            public void Dispose()
            {
                m_TransmittanceLUT?.Release();
                m_MultiScatteringLUT?.Release();
                m_SkyViewLUT?.Release();
                m_AerialPerspectiveLUT?.Release();

                m_IsInitialized = false;
            }
        }

        // Aerial Perspective应用Pass
        class AerialPerspectivePass : ScriptableRenderPass
        {
            private Material m_AerialPerspectiveMaterial;
            private RTHandle m_TempColorTarget;
            private RTHandle m_CameraColorTarget;
            private string m_ProfilerTag;

            public AerialPerspectivePass(Material material, string profilerTag)
            {
                m_AerialPerspectiveMaterial = material;
                m_ProfilerTag = profilerTag;
            }

            public void Setup(RTHandle cameraColorTarget)
            {
                m_CameraColorTarget = cameraColorTarget;
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;

                RenderingUtils.ReAllocateIfNeeded(ref m_TempColorTarget, descriptor, FilterMode.Bilinear,
                    TextureWrapMode.Clamp, name: "_TempAerialPerspective");
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (m_AerialPerspectiveMaterial == null)
                {
                    Debug.LogError("Aerial Perspective Material is null");
                    return;
                }

                
                if (m_CameraColorTarget == null || m_TempColorTarget == null)
                {
                    return;
                }

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, new ProfilingSampler(m_ProfilerTag)))
                {
                   
                    Blitter.BlitCameraTexture(cmd, m_CameraColorTarget, m_TempColorTarget, m_AerialPerspectiveMaterial, 0);
                    Blitter.BlitCameraTexture(cmd, m_TempColorTarget, m_CameraColorTarget);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public override void OnCameraCleanup(CommandBuffer cmd)
            {
                // 重置相机颜色目标引用，避免在视图切换时保留过期引用
                m_CameraColorTarget = null;
            }

            public void Dispose()
            {
                m_TempColorTarget?.Release();
            }
        }

        public Settings settings = new Settings();
        private AtmosphereLUTGenerationPass m_LUTGenerationPass;
        private AerialPerspectivePass m_AerialPerspectivePass;

        public override void Create()
        {
            // 创建LUT生成Pass
            m_LUTGenerationPass = new AtmosphereLUTGenerationPass(settings);
            m_LUTGenerationPass.renderPassEvent = settings.lutGenerationEvent;

            // 创建Aerial Perspective应用Pass（如果启用）
            if (settings.enableAerialPerspectiveEffect && settings.aerialPerspectiveMaterial != null)
            {
                m_AerialPerspectivePass = new AerialPerspectivePass(
                    settings.aerialPerspectiveMaterial,
                    "Aerial Perspective Effect"
                );
                m_AerialPerspectivePass.renderPassEvent = settings.aerialPerspectiveEvent;
            }
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            // 设置Aerial Perspective应用Pass的渲染目标
            if (settings.enableAerialPerspectiveEffect && m_AerialPerspectivePass != null)
            {
                if (renderer != null && renderer.cameraColorTargetHandle != null)
                {
                    m_AerialPerspectivePass.Setup(renderer.cameraColorTargetHandle);
                }
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // 添加LUT生成Pass
            if (settings.computeShader != null && settings.atmosphereSettings != null)
            {
                if (!renderingData.cameraData.isSceneViewCamera)
                {
                    renderer.EnqueuePass(m_LUTGenerationPass);
                }
            }
            else
            {
                Debug.LogWarning("AtmosphereRender: Missing compute shader or atmosphere settings!");
            }

            // 添加Aerial Perspective应用Pass（如果启用）
            if (settings.enableAerialPerspectiveEffect)
            {
                if (settings.aerialPerspectiveMaterial == null)
                {
                    Debug.LogWarning("AtmosphereRender: Aerial Perspective is enabled but material is missing!");
                }
                else if (m_AerialPerspectivePass != null)
                {
                    renderer.EnqueuePass(m_AerialPerspectivePass);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            m_LUTGenerationPass?.Dispose();
            m_AerialPerspectivePass?.Dispose();
        }
    }
}