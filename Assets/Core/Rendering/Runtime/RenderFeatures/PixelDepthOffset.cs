using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PixelDepthOffset : ScriptableRendererFeature
{
    public enum TexQuality
    {
        High = 0,
        Middle,
        Low,
    }

    private class CustomRenderPass : ScriptableRenderPass
    {
        private static readonly string s_ProfileTag = "PDO";
        private readonly ProfilingSampler m_ProfilingSampler = new ProfilingSampler(s_ProfileTag);

        
        private const string _PDOAlbedo = "_G_PDO_AlbedoTexture";
        private const string _PDONormal = "_G_PDO_NormalTexture";
        private const string _PDODepth = "_G_PDO_DepthTexture";
        

        private static readonly int s_PDOAlbedoPropID = Shader.PropertyToID(_PDOAlbedo);
        private static readonly int s_PDONormalPropID = Shader.PropertyToID(_PDONormal);
        private static readonly int s_PDODepthPropID = Shader.PropertyToID(_PDODepth);
        

        private readonly PixelDepthOffset m_Owner;

        private readonly List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>();

        private FilteringSettings m_FilteringSettings;
        
        private RTHandle m_PDOAlbedoHandle;
        private RTHandle m_PDONormalHandle;
        private RTHandle m_PDODepthHandle;
        private RTHandle[] m_PDOBuffers;

        public CustomRenderPass(PixelDepthOffset owner)
        {
            m_Owner = owner;

            renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
            m_ShaderTagIdList.Add(new ShaderTagId("PDO"));

            m_FilteringSettings = new FilteringSettings();
            m_FilteringSettings.layerMask = -1;
            m_FilteringSettings.renderingLayerMask = 0xffffffff;
            m_FilteringSettings.sortingLayerRange = SortingLayerRange.all;
        }

        public void Dispose()
        {
            m_PDOAlbedoHandle?.Release();
            m_PDONormalHandle?.Release();
            m_PDODepthHandle?.Release();
        }


        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var cameraTextureDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            int width = cameraTextureDescriptor.width >> (int)m_Owner.m_TexQuality;
            int height = cameraTextureDescriptor.height >> (int)m_Owner.m_TexQuality;

            // Albedo
            var albedoDesc = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGBHalf, 0)
            {
                msaaSamples = 1,
                bindMS = false
            };
            RenderingUtils.ReAllocateIfNeeded(ref m_PDOAlbedoHandle, albedoDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_G_PDO_AlbedoTex");

            // Normal
            var normalDesc = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0)
            {
                msaaSamples = 1,
                sRGB = false
            };
            RenderingUtils.ReAllocateIfNeeded(ref m_PDONormalHandle, normalDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_G_PDO_NormalTex");

            // Depth
            var depthDesc = new RenderTextureDescriptor(width, height, GraphicsFormat.None, GraphicsFormat.D32_SFloat)
            {
                msaaSamples = 1,
                bindMS = false
            };
            RenderingUtils.ReAllocateIfNeeded(ref m_PDODepthHandle, depthDesc, FilterMode.Point, TextureWrapMode.Clamp, name: _PDODepth);

            cmd.SetGlobalTexture(s_PDOAlbedoPropID, m_PDOAlbedoHandle);
            cmd.SetGlobalTexture(s_PDONormalPropID, m_PDONormalHandle);
            cmd.SetGlobalTexture(s_PDODepthPropID, m_PDODepthHandle);

            m_PDOBuffers = new[] { m_PDOAlbedoHandle, m_PDONormalHandle };

            ConfigureTarget(m_PDOBuffers, m_PDODepthHandle);
            ConfigureClear(ClearFlag.All, Color.black);
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            ref CameraData cameraData = ref renderingData.cameraData;
            ref ScriptableRenderer renderer = ref cameraData.renderer;
            Camera camera = cameraData.camera;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                m_FilteringSettings.layerMask = m_Owner.m_CullMask;  // 设置渲染Layer
                // 只渲染不透明物
                m_FilteringSettings.renderQueueRange = RenderQueueRange.opaque;
                DrawingSettings drawingOpaqueSettings = CreateDrawingSettings(m_ShaderTagIdList, ref renderingData, SortingCriteria.CommonOpaque);
                context.DrawRenderers(renderingData.cullResults, ref drawingOpaqueSettings, ref m_FilteringSettings);
                
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void FrameCleanup(CommandBuffer cmd)
        {
        }
    }

    [SerializeField] private TexQuality m_TexQuality = TexQuality.Low;

    [SerializeField] private LayerMask m_CullMask = -1;

    private CustomRenderPass m_ScriptablePass;

    public override void Create()
    {
        m_ScriptablePass = new CustomRenderPass(this);
    }

    protected override void Dispose(bool disposing)
    {
        m_ScriptablePass?.Dispose();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.camera.cameraType == CameraType.Game)
        {
            renderer.EnqueuePass(m_ScriptablePass);
        }
    }
}