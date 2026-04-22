using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Mathematics;
using UnityEngine.Serialization;

public class SkyRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Tooltip("决定数据何时被推送到 GPU，建议在不透明物体渲染前")]
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingOpaques;
        public RenderPassEvent cloudPassEvent = RenderPassEvent.AfterRenderingSkybox;
        
        [Tooltip("是否在 Scene 窗口也应用此天空数据")]
        public bool applyInSceneView = true;
        
    }

    public Settings settings = new Settings();
    private SkyFrameDataPass m_SkyFrameDataPass;
    private VolumetricCloudPass m_CloudRenderPass = null;
    private Material m_Material;
    
    
    public override void Create()
    {
        if(SkyManager.Instance ==null) return;
        // 实例化 Pass
        if (m_SkyFrameDataPass == null)
        {
            m_SkyFrameDataPass = new SkyFrameDataPass(settings.passEvent, settings.applyInSceneView);
        }

        if (SkyManager.Instance.CloudShader != null)
        {
            m_Material = CoreUtils.CreateEngineMaterial(SkyManager.Instance.CloudShader);
            
        }else if ( SkyManager.Instance.CloudMaterial != null)
        {
            m_Material = SkyManager.Instance.CloudMaterial;
        }else
        {
            Debug.LogWarning("SkyRendererFeature: No cloud shader or material assigned. Cloud rendering will be disabled.");
        }

        
        m_CloudRenderPass = new VolumetricCloudPass(m_Material,settings.cloudPassEvent);

    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        CameraType camType = renderingData.cameraData.cameraType;
        if (camType == CameraType.Preview || camType == CameraType.Reflection) return;
        if (!settings.applyInSceneView && camType == CameraType.SceneView) return;

        renderer.EnqueuePass(m_SkyFrameDataPass);
        
        if (m_CloudRenderPass != null)
        {
            renderer.EnqueuePass(m_CloudRenderPass);
        }
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        CameraType camType = renderingData.cameraData.cameraType;
        bool isTargetCamera = camType == CameraType.Game || (settings.applyInSceneView && camType == CameraType.SceneView);

        if (m_CloudRenderPass != null && isTargetCamera)
        {
            m_CloudRenderPass.ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);
            m_CloudRenderPass.SetTarget(renderer.cameraColorTargetHandle);
        }
    }

    protected override void Dispose(bool disposing)
    {
        // 必须在 Feature 销毁时清理 GPU 内存，防止内存泄漏
        if (m_SkyFrameDataPass != null)
        {
            m_SkyFrameDataPass.Cleanup();
            m_SkyFrameDataPass = null;
        }
        
        CoreUtils.Destroy(m_Material);
    }

    // =====================================================================
    // Render Pass 内部类
    // =====================================================================
    class SkyFrameDataPass : ScriptableRenderPass
    {
        private GraphicsBuffer m_StructuredBuffer;
        private PerFrameData[] m_FrameDataArray;
        private Plane[] m_FrustumPlanes = new Plane[6];
        
        private Material m_RayMarchingMaterial; // 可选：如果需要在 Pass 中使用特定材质，可以在这里指定

        private bool m_ApplyInSceneView;
        
        private static readonly int s_SkyFrameDataID = Shader.PropertyToID("SkyFrameData");
        private static readonly int s_BasicNoiseID = Shader.PropertyToID("inBasicNoise");
        private static readonly int s_DetailNoiseID = Shader.PropertyToID("inDetailNoise");
        private static readonly int s_WeatherTexID = Shader.PropertyToID("inWeatherTexture");
        private static readonly int s_CloudCurlNoiseID = Shader.PropertyToID("inCloudCurlNoise");
        private static readonly int s_BlueNoiseID = Shader.PropertyToID("inBlueNoise");

        // 缓存上一帧的相机信息用于 Temporal 效果 (如 TAA, 云层时域混合)
        private Vector4 m_PrevCamInfo;
        private Matrix4x4 m_PrevViewProj;
        private Matrix4x4 m_PrevViewProjNoJitter;
        private Vector4 m_PrevJitterData;
        
        public SkyFrameDataPass(RenderPassEvent passEvent, bool applyInScene)
        {
            this.renderPassEvent = passEvent;
            m_ApplyInSceneView = applyInScene;

            int stride = Marshal.SizeOf<PerFrameData>();
            m_StructuredBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, stride);
            m_FrameDataArray = new PerFrameData[1];
        }
    
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var cameraTextureDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            
            
            // ConfigureTarget(m_PDOBuffers, m_PDODepthHandle);
            // ConfigureClear(ClearFlag.All, Color.black);
            // ConfigureInput(ScriptableRenderPassInput.Depth);
        }         
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get("Update Sky Frame Data StrcutedBuffer");
            // CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, new ProfilingSampler("Update Sky Frame Data StrcutedBuffer")))
            {
                // 1. 调用独立函数组装数据
                PerFrameData data = SetupPerFrameData(ref renderingData);

                // 2. 提交数据到 GPU
                m_FrameDataArray[0] = data;
                m_StructuredBuffer.SetData(m_FrameDataArray);
                // cmd.SetGlobalConstantBuffer(m_StructuredBuffer, m_SkyFrameDataPropertyID, 0, m_StructuredBuffer.stride);
                cmd.SetGlobalBuffer(s_SkyFrameDataID, m_StructuredBuffer);
                
                // 3. 缓存当前帧数据，供下一帧作为历史数据使用
                m_PrevCamInfo = data.camInfo;
                m_PrevViewProj = data.camViewProj;
                m_PrevViewProjNoJitter = data.camViewProjNoJitter;
                m_PrevJitterData = new Vector4(data.jitterData.x, data.jitterData.y, 0, 0);
                
                SetSkyTextures();
                
                Debug.Log("Update Sky Frame Data CBuffer");
                // cmd.Blit();
            }
            
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            CommandBufferPool.Release(cmd);
        }

        private void SetSkyTextures()
        {
            if (SkyManager.Instance != null)
            {
                Shader.SetGlobalTexture(s_BasicNoiseID,SkyManager.Instance.cloudBasicNoise);
                Shader.SetGlobalTexture(s_DetailNoiseID,SkyManager.Instance.cloudDetailNoise);
                Shader.SetGlobalTexture(s_BlueNoiseID,SkyManager.Instance.blueNoise);
                Shader.SetGlobalTexture(s_WeatherTexID,SkyManager.Instance.weatherTexture);
                Shader.SetGlobalTexture(s_CloudCurlNoiseID,SkyManager.Instance.curlNoiseTexture);
            }
        }

        /// <summary>
        /// 独立的数据组装函数：负责提取所有渲染管线状态并填充 PerFrameData 结构体
        /// </summary>
        private PerFrameData SetupPerFrameData(ref RenderingData renderingData)
        {
            CameraData cameraData = renderingData.cameraData;
            Camera cam = cameraData.camera;
            PerFrameData data = new PerFrameData();

            // --- 1. 全局时间与帧计数 ---
            float time = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
            data.appTime = new Vector4(time, Mathf.Sin(time), Mathf.Cos(time), 0);
            
            uint fCount = (uint)Time.frameCount;
            data.frameIndex = new uint4(fCount, fCount % 8, fCount % 16, fCount % 32);

            // --- 2. 相机基础信息 ---
            data.camWorldPos = cam.transform.position;
            data.camForward = cam.transform.forward;
            data.camInfo = new Vector4(cam.fieldOfView, cam.aspect, cam.nearClipPlane, cam.farClipPlane);
            data.camInfoPrev = m_PrevCamInfo;

            // --- 3. 屏幕与渲染分辨率 ---
            data.renderWidth = cameraData.cameraTargetDescriptor.width;
            data.renderHeight = cameraData.cameraTargetDescriptor.height;
            data.displayWidth = Screen.width;
            data.displayHeight = Screen.height;

            // --- 4. 获取矩阵 (包含 TAA 抖动) ---
            Matrix4x4 viewMat = cameraData.GetViewMatrix();
            Matrix4x4 projMat = cameraData.GetGPUProjectionMatrix(); 
            Matrix4x4 viewProjMat = projMat * viewMat;

            data.camView = viewMat;
            data.camProj = projMat;
            data.camViewProj = viewProjMat;
            data.camInvertView = viewMat.inverse;
            data.camInvertProj = projMat.inverse;
            data.camInvertViewProj = viewProjMat.inverse;

            // --- 5. 无抖动矩阵 (NoJitter Matrices) ---
            bool isJittered = cameraData.antialiasing == AntialiasingMode.TemporalAntiAliasing;
            Matrix4x4 projNoJitter = isJittered 
                ? GL.GetGPUProjectionMatrix(cam.nonJitteredProjectionMatrix, true) 
                : projMat;
            Matrix4x4 viewProjNoJitter = projNoJitter * viewMat;

            data.camProjNoJitter = projNoJitter;
            data.camViewProjNoJitter = viewProjNoJitter;
            data.camInvertProjNoJitter = projNoJitter.inverse;
            data.camInvertViewProjNoJitter = viewProjNoJitter.inverse;

            // --- 6. 历史帧矩阵 ---
            data.camViewProjPrev = m_PrevViewProj;
            data.camViewProjPrevNoJitter = m_PrevViewProjNoJitter;

            // --- 7. Jitter 数据提取 ---
            if (isJittered)
            {
                // 获取当前帧的投影偏移量作为 jitter 值
                float jitterX = projMat.m02; 
                float jitterY = projMat.m12;
                data.jitterData = new Vector4(jitterX, jitterY, m_PrevJitterData.x, m_PrevJitterData.y);
                data.bEnableJitter = 1;
            }
            else
            {
                // 即使未开启抖动，也要把上一帧的数据传进去以防着色器出错
                data.jitterData = new Vector4(0, 0, m_PrevJitterData.x, m_PrevJitterData.y);
                data.bEnableJitter = 0;
            }

            // --- 8. 视锥体裁剪平面 ---
            GeometryUtility.CalculateFrustumPlanes(viewProjMat, m_FrustumPlanes);
            data.frustumPlane0 = new Vector4(m_FrustumPlanes[0].normal.x, m_FrustumPlanes[0].normal.y, m_FrustumPlanes[0].normal.z, m_FrustumPlanes[0].distance);
            data.frustumPlane1 = new Vector4(m_FrustumPlanes[1].normal.x, m_FrustumPlanes[1].normal.y, m_FrustumPlanes[1].normal.z, m_FrustumPlanes[1].distance);
            data.frustumPlane2 = new Vector4(m_FrustumPlanes[2].normal.x, m_FrustumPlanes[2].normal.y, m_FrustumPlanes[2].normal.z, m_FrustumPlanes[2].distance);
            data.frustumPlane3 = new Vector4(m_FrustumPlanes[3].normal.x, m_FrustumPlanes[3].normal.y, m_FrustumPlanes[3].normal.z, m_FrustumPlanes[3].distance);
            data.frustumPlane4 = new Vector4(m_FrustumPlanes[4].normal.x, m_FrustumPlanes[4].normal.y, m_FrustumPlanes[4].normal.z, m_FrustumPlanes[4].distance);
            data.frustumPlane5 = new Vector4(m_FrustumPlanes[5].normal.x, m_FrustumPlanes[5].normal.y, m_FrustumPlanes[5].normal.z, m_FrustumPlanes[5].distance);
            // --- 9. 接入 SkyManager 数据 ---
            if (SkyManager.Instance != null)
            {
                data.skyValid = 1;
                data.sky = SkyManager.Instance.GetSkyInfo(cam);
            }
            else
            {
                data.skyValid = 0;
                data.sky = default;
            }

            return data;
        }

        public override void FrameCleanup(CommandBuffer cmd)
        {
        }
        
        public void Cleanup()
        {
            if (m_StructuredBuffer != null)
            {
                m_StructuredBuffer.Release();
                m_StructuredBuffer = null;
            }
        }
    }
    
    internal class VolumetricCloudPass : ScriptableRenderPass
    {
        private ProfilingSampler m_ProfilingSampler = new ProfilingSampler("Volumetric Clouds");
        private Material m_Material;
        private RTHandle m_CameraColorTarget;

        // --- RTHandles ---
        // 1/4 分辨率缓冲 (Raymarching 结果)
        private RTHandle m_QuarterResCloudColor;
        private RTHandle m_QuarterResCloudDepth;
        
        // 全分辨率缓冲 (重建结果)
        private RTHandle m_ReconstructCloudColor;
        private RTHandle m_ReconstructCloudDepth;
        
        // 历史帧缓冲 (Ping-Pong 双缓冲)
        private RTHandle[] m_HistoryCloudColor = new RTHandle[2];
        private RTHandle[] m_HistoryCloudDepth = new RTHandle[2];
        private int m_HistoryIndex = 0; // 控制当前帧使用哪一个历史缓冲

        private bool m_IsFirstFrame = true;
        private Matrix4x4 m_PreviousCameraVP;

        // Shader Property IDs (对应 GLSL 中的变量名)
        private static readonly int s_InCloudRenderTexture = Shader.PropertyToID("inCloudRenderTexture");
        private static readonly int s_InCloudDepthTexture = Shader.PropertyToID("inCloudDepthTexture");
        private static readonly int s_InCloudReconstructionTextureHistory = Shader.PropertyToID("inCloudReconstructionTextureHistory");
        private static readonly int s_InCloudDepthReconstructionTextureHistory = Shader.PropertyToID("inCloudDepthReconstructionTextureHistory");
        private static readonly int s_InCloudReconstructionTexture = Shader.PropertyToID("inCloudReconstructionTexture");

        public VolumetricCloudPass(Material material, RenderPassEvent passEvent)
        {
            m_Material = material;
            this.renderPassEvent = passEvent;
        }

        public void SetTarget(RTHandle colorHandle)
        {
            m_CameraColorTarget = colorHandle;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor cameraDesc = renderingData.cameraData.cameraTargetDescriptor;
            cameraDesc.depthBufferBits = 0; 

            // 1. 分配 1/4 分辨率 RT
            RenderTextureDescriptor quarterDesc = cameraDesc;
            quarterDesc.width = Mathf.Max(1, quarterDesc.width / 4);
            quarterDesc.height = Mathf.Max(1, quarterDesc.height / 4);
            quarterDesc.colorFormat = RenderTextureFormat.ARGBHalf; 
            
            RenderingUtils.ReAllocateIfNeeded(ref m_QuarterResCloudColor, quarterDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_QuarterResCloudColor");
            
            quarterDesc.colorFormat = RenderTextureFormat.RFloat; 
            RenderingUtils.ReAllocateIfNeeded(ref m_QuarterResCloudDepth, quarterDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_QuarterResCloudDepth");

            // 2. 分配全分辨率重建 RT
            RenderTextureDescriptor fullDesc = cameraDesc;
            fullDesc.colorFormat = RenderTextureFormat.ARGBHalf;
            RenderingUtils.ReAllocateIfNeeded(ref m_ReconstructCloudColor, fullDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_ReconstructCloudColor");
            
            fullDesc.colorFormat = RenderTextureFormat.RFloat;
            RenderingUtils.ReAllocateIfNeeded(ref m_ReconstructCloudDepth, fullDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_ReconstructCloudDepth");

            // 3. 分配历史帧缓冲 (Frame N 和 Frame N-1)
            for (int i = 0; i < 2; i++)
            {
                fullDesc.colorFormat = RenderTextureFormat.ARGBHalf;
                RenderingUtils.ReAllocateIfNeeded(ref m_HistoryCloudColor[i], fullDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: $"_HistoryCloudColor_{i}");
                
                fullDesc.colorFormat = RenderTextureFormat.RFloat;
                RenderingUtils.ReAllocateIfNeeded(ref m_HistoryCloudDepth[i], fullDesc, FilterMode.Point, TextureWrapMode.Clamp, name: $"_HistoryCloudDepth_{i}");
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_Material == null || renderingData.cameraData.cameraType != CameraType.Game)
                return;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                // 确定 Ping-Pong 索引：curr 写入，prev 读取
                int currHistory = m_HistoryIndex;
                int prevHistory = (m_HistoryIndex + 1) % 2;

                if (m_IsFirstFrame)
                {
                    m_PreviousCameraVP = GetViewProjectionMatrix(renderingData.cameraData.camera);
                    // 第一帧清除历史缓冲，防止读取到脏数据 (对应 C++ vkCmdClearColorImage)
                    cmd.SetRenderTarget(m_HistoryCloudColor[prevHistory]);
                    cmd.ClearRenderTarget(false, true, Color.clear);
                    cmd.SetRenderTarget(m_HistoryCloudDepth[prevHistory]);
                    cmd.ClearRenderTarget(false, true, Color.clear);
                    m_IsFirstFrame = false;
                }

                // 传递 SkyManager 的配置
                m_Material.SetFloat("_DensityThreshold", SkyManager.Instance.CloudDensityThreshold);
                m_Material.SetFloat("_DensityPower", SkyManager.Instance.CloudDensityPower);
                m_Material.SetVector("_offset", SkyManager.Instance.CloudOffset);

                // ==============================================================================
                // Phase 1: Quarter-Res Raymarching (Pass 0)
                // ==============================================================================
                cmd.BeginSample("CloudRaymarching");
                // MRT (Multiple Render Targets) 输出颜色和深度。
                CoreUtils.SetRenderTarget(cmd, new RenderTargetIdentifier[] { m_QuarterResCloudColor.nameID, m_QuarterResCloudDepth.nameID }, m_QuarterResCloudDepth.nameID);
                Blitter.BlitTexture(cmd, m_QuarterResCloudColor, new Vector4(1, 1, 0, 0), m_Material, 0);
                cmd.EndSample("CloudRaymarching");

                // ==============================================================================
                // Phase 2: Temporal Reconstruction (Pass 1)
                // ==============================================================================
                cmd.BeginSample("CloudReconstruction");
                // 绑定 1/4 结果作为当前帧输入
                cmd.SetGlobalTexture(s_InCloudRenderTexture, m_QuarterResCloudColor);
                cmd.SetGlobalTexture(s_InCloudDepthTexture, m_QuarterResCloudDepth);
                
                // 绑定上一帧作为历史输入
                cmd.SetGlobalTexture(s_InCloudReconstructionTextureHistory, m_HistoryCloudColor[prevHistory]);
                cmd.SetGlobalTexture(s_InCloudDepthReconstructionTextureHistory, m_HistoryCloudDepth[prevHistory]);
                m_Material.SetMatrix("_PreviousCameraVP", m_PreviousCameraVP);

                // 渲染重建结果到当前帧的 History Buffer
                CoreUtils.SetRenderTarget(cmd, new RenderTargetIdentifier[] { m_HistoryCloudColor[currHistory].nameID, m_HistoryCloudDepth[currHistory].nameID }, m_HistoryCloudDepth[currHistory].nameID);
                Blitter.BlitTexture(cmd, m_HistoryCloudColor[currHistory], new Vector4(1, 1, 0, 0), m_Material, 1);
                cmd.EndSample("CloudReconstruction");

                // ==============================================================================
                // Phase 3: Composite to Scene (Pass 2)
                // ==============================================================================
                // 将刚算好的 History Buffer 拿出来准备合成
                cmd.BeginSample("CloudComposite");
                cmd.SetGlobalTexture(s_InCloudReconstructionTexture, m_HistoryCloudColor[currHistory]);
                // 叠加回主相机的颜色目标
                CoreUtils.SetRenderTarget(cmd, m_CameraColorTarget);
                Blitter.BlitTexture(cmd, m_CameraColorTarget, new Vector4(1, 1, 0, 0), m_Material, 2);
                cmd.EndSample("CloudComposite");

                // 切换 Ping-Pong 索引，为下一帧做准备
                m_HistoryIndex = prevHistory;
                m_PreviousCameraVP = GetViewProjectionMatrix(renderingData.cameraData.camera);
            }
            
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            // 释放所有 RTHandles 以防内存泄漏
            m_QuarterResCloudColor?.Release();
            m_QuarterResCloudDepth?.Release();
            m_ReconstructCloudColor?.Release();
            m_ReconstructCloudDepth?.Release();
            
            for (int i = 0; i < 2; i++)
            {
                m_HistoryCloudColor[i]?.Release();
                m_HistoryCloudDepth[i]?.Release();
            }
        }
        
        public static Matrix4x4 GetViewProjectionMatrix(Camera cam) {
            return GL.GetGPUProjectionMatrix(cam.projectionMatrix, false) * cam.worldToCameraMatrix;
        }
    }
}