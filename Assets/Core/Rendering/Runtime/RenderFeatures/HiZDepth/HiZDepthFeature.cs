using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering; // 引入现代 GraphicsFormat

public class HiZDepthFeature : ScriptableRendererFeature
{
    public enum DepthQuality { High = 0, Middle = 1, Low = 2 }
    public enum CopyMode { Fragment, Compute }

    [System.Serializable]
    public class Settings
    {
        public DepthQuality quality = DepthQuality.High;
        public CopyMode depthCopyMode = CopyMode.Fragment;
        
        [Tooltip("控制 Hi-Z 生成的时机")]
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingOpaques; 
        
        [Space(10)]
        public ComputeShader hiZDepthCompute;
        public Shader copyDepthShader;
    }

    public Settings settings = new Settings();
    private HiZDepthPass m_HiZPass;

#if UNITY_EDITOR
    private void Reset()
    {
        string basePath = "Assets/Core/Rendering/Runtime/RenderFeatures/HiZDepth/";
        settings.hiZDepthCompute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(basePath + "HiZDepth.compute");
        settings.copyDepthShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(basePath + "CopyDepth.shader");
    }
#endif

    public override void Create()
    {
        if (settings.hiZDepthCompute == null) return;
        if (settings.depthCopyMode == CopyMode.Fragment && settings.copyDepthShader == null) return;

        m_HiZPass = new HiZDepthPass(settings)
        {
            renderPassEvent = settings.passEvent
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_HiZPass != null && renderingData.cameraData.cameraType == CameraType.Game)
        {
            renderer.EnqueuePass(m_HiZPass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        m_HiZPass?.Dispose();
    }

    // ---------------------------------------------------------
    // Render Pass
    // ---------------------------------------------------------
    class HiZDepthPass : ScriptableRenderPass
    {
        private Settings m_Settings;
        private Material m_CopyDepthMaterial;
        
        private RTHandle m_DepthHandle; 
        private int m_DepthTextureSize;
        private int m_DepthTextureMipLevel;

        private int m_GenMipmapKernel;
        private int m_CopyDepthKernel;
        
        private static readonly ProfilingSampler s_CopyDepthSampler = new ProfilingSampler("HiZ: Copy Depth (Mip 0)");
        private static readonly ProfilingSampler s_GenMipmapSampler = new ProfilingSampler("HiZ: Generate Mipmap");
        
        private static readonly int s_PrevTextureID = Shader.PropertyToID("_PrevTexture");
        private static readonly int s_TargetTextureID = Shader.PropertyToID("_TargetTexture");
        private static readonly int s_TargetSizeID = Shader.PropertyToID("_TargetSize");
        private static readonly int s_TextureSizeID = Shader.PropertyToID("_TextureSize");
        
        private static readonly int s_HiZVaildID = Shader.PropertyToID("_HizVaild");
        private static readonly int s_HizMapID = Shader.PropertyToID("_HizMap");
        private static readonly int s_HizCameraMatrixVPID = Shader.PropertyToID("_HizCameraMatrixVP");
        private static readonly int s_HizMapSizeID = Shader.PropertyToID("_HizMapSize");
        private static readonly int s_HizCameraPositionWSID = Shader.PropertyToID("_HizCameraPositionWS");

        public HiZDepthPass(Settings settings)
        {
            m_Settings = settings;
            m_GenMipmapKernel = settings.hiZDepthCompute.FindKernel("CSGenMipmap");

            if (settings.depthCopyMode == CopyMode.Fragment)
                m_CopyDepthMaterial = CoreUtils.CreateEngineMaterial(settings.copyDepthShader);
            else
                m_CopyDepthKernel = settings.hiZDepthCompute.FindKernel("CSCopyDepth");
        }

        private void SetupRTHandle()
        {
            int targetWidth = 1024 >> (int)m_Settings.quality;
            if (m_DepthHandle != null && m_DepthTextureSize == targetWidth) return;

            // 释放旧的 RTHandle
            m_DepthHandle?.Release();

            m_DepthTextureSize = targetWidth;
            m_DepthTextureMipLevel = 10 - (int)m_Settings.quality;
            
            GraphicsFormat format = SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, FormatUsage.Render) 
                ? GraphicsFormat.R16_SFloat 
                : GraphicsFormat.R32_SFloat;

            RenderTextureDescriptor desc = new RenderTextureDescriptor(m_DepthTextureSize, m_DepthTextureSize >> 1, format, 0, m_DepthTextureMipLevel)
            {
                useMipMap = true,
                autoGenerateMips = false,
                enableRandomWrite = true,
                dimension = TextureDimension.Tex2D
            };

            m_DepthHandle = RTHandles.Alloc(desc, filterMode: FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: "HiZDepthRT");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            SetupRTHandle();
            CommandBuffer cmd = CommandBufferPool.Get("Generate Hi-Z Depth");

            // 1. Copy Depth
            using (new ProfilingScope(cmd, s_CopyDepthSampler))
            {
                if (m_Settings.depthCopyMode == CopyMode.Fragment)
                {
                    cmd.Blit(null, m_DepthHandle, m_CopyDepthMaterial);
                }
                else 
                {
                    cmd.SetComputeTextureParam(m_Settings.hiZDepthCompute, m_CopyDepthKernel, s_TargetTextureID, m_DepthHandle.nameID, 0);
                    cmd.SetComputeVectorParam(m_Settings.hiZDepthCompute, s_TargetSizeID, 
                        new Vector4(m_DepthTextureSize, m_DepthTextureSize >> 1, 1f / m_DepthTextureSize, 1f / (m_DepthTextureSize >> 1)));
                    
                    int groupX = Mathf.CeilToInt(m_DepthTextureSize / 8f);
                    int groupY = Mathf.CeilToInt((m_DepthTextureSize >> 1) / 8f);
                    cmd.DispatchCompute(m_Settings.hiZDepthCompute, m_CopyDepthKernel, groupX, groupY, 1);
                }
            }

            // 2. Generate Mipmap
            using (new ProfilingScope(cmd, s_GenMipmapSampler))
            {
                int width = m_DepthTextureSize;
                int height = m_DepthTextureSize >> 1;

                for (int i = 1; i < m_DepthTextureMipLevel; ++i)
                {
                    width = Mathf.Max(1, width >> 1);
                    height = Mathf.Max(1, height >> 1);

                    cmd.SetComputeTextureParam(m_Settings.hiZDepthCompute, m_GenMipmapKernel, s_PrevTextureID, m_DepthHandle.nameID, i - 1);
                    cmd.SetComputeTextureParam(m_Settings.hiZDepthCompute, m_GenMipmapKernel, s_TargetTextureID, m_DepthHandle.nameID, i);
                    cmd.SetComputeVectorParam(m_Settings.hiZDepthCompute, s_TextureSizeID, new Vector4(width, height, 0, 0));

                    cmd.DispatchCompute(m_Settings.hiZDepthCompute, m_GenMipmapKernel, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);
                }
            }

            // 3. Set Globals
            var camera = renderingData.cameraData.camera;
            var proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            Matrix4x4 viewProj = proj * camera.worldToCameraMatrix;

            // 贴图尺寸参数: x = 宽度, y = 高度, z = Mipmap总层级, w = 占位
            Vector4 hizMapSize = new Vector4(m_DepthTextureSize, m_DepthTextureSize >> 1, m_DepthTextureMipLevel, 0);
            // 相机世界坐标: 必须传 Vector4，w 分量通常为 1（表示点）
            Vector4 cameraPosWS = new Vector4(camera.transform.position.x, camera.transform.position.y, camera.transform.position.z, 1.0f);

            cmd.SetGlobalTexture(s_HizMapID, m_DepthHandle.nameID);
            cmd.SetGlobalMatrix(s_HizCameraMatrixVPID, viewProj);
            cmd.SetGlobalVector(s_HizMapSizeID, hizMapSize);
            cmd.SetGlobalVector(s_HizCameraPositionWSID, cameraPosWS);
            cmd.SetGlobalInt(s_HiZVaildID, 1);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            Shader.SetGlobalInt(s_HiZVaildID, 0);
            m_DepthHandle?.Release(); 
            if (m_CopyDepthMaterial != null) CoreUtils.Destroy(m_CopyDepthMaterial);
        }
    }
}