using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using WindSystem;

public class WindSimulationFeature : ScriptableRendererFeature
{
    class CustomRenderPass : ScriptableRenderPass
    {
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) { }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (WindSystem.WindManager.Instance != null && WindSystem.WindManager.Instance.gameObject.activeSelf)
            {
                CommandBuffer cmd = CommandBufferPool.Get();
                
                WindSystem.WindManager.Instance.DoRenderWindVolume(cmd);
                
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
        
        public override void OnCameraCleanup(CommandBuffer cmd) { }
    }

    CustomRenderPass m_ScriptablePass;

    public override void Create()
    {
        m_ScriptablePass = new CustomRenderPass();
        // 设置在 URP 渲染不透明物体之前执行
        m_ScriptablePass.renderPassEvent = RenderPassEvent.BeforeRendering;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var cameraData = renderingData.cameraData;
        if ((cameraData.camera.cameraType == CameraType.Game || cameraData.camera.cameraType == CameraType.SceneView) && 
            cameraData.renderType == CameraRenderType.Base)
        {
            renderer.EnqueuePass(m_ScriptablePass);
        }
    }
}