using UnityEngine;
using UnityEngine.Rendering;

namespace Zerls.GrassSystem
{
    public class MeshGrassBaker : MonoBehaviour
    {
        [Header("Target Environment")]
        public MeshRenderer targetMeshRenderer;
        
        [Header("Bake Settings")]
        public int resolution = 2048; 
        public Shader bakeShader;

        [Header("Bake Area Override")]
        [Tooltip("勾选后，可以手动指定烘焙的包围盒大小和中心点，不再强制使用整个 Mesh 的边界")]
        public bool useCustomBounds = false;
        public Vector3 customCenter = Vector3.zero;
        public Vector3 customSize = new Vector3(100, 50, 100);

        public RenderTexture meshDataRT { get; private set; }
        public Bounds meshBounds { get; private set; }

        public void BakeMeshGrassData()
        {
            if (targetMeshRenderer == null || bakeShader == null) return;
            
            if (useCustomBounds)
            {
                meshBounds = new Bounds(customCenter, customSize);
            }
            else
            {
                meshBounds = targetMeshRenderer.bounds;
            }

            // 保持 ARGBFloat 保证高度计算的精度
            meshDataRT = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGBFloat);
            meshDataRT.filterMode = FilterMode.Bilinear;
            meshDataRT.wrapMode = TextureWrapMode.Clamp;
            meshDataRT.Create();

            Vector3 center = meshBounds.center;
            Vector3 size = meshBounds.size;
            
            // 相机位置
            Vector3 cameraPos = center + Vector3.up * (size.y * 0.5f + 10f);
            
            Matrix4x4 trs = Matrix4x4.TRS(cameraPos, Quaternion.Euler(90f, 0, 0), new Vector3(1, 1, -1));
            Matrix4x4 viewMatrix = trs.inverse;

            Matrix4x4 projMatrix = Matrix4x4.Ortho(-size.x * 0.5f, size.x * 0.5f, -size.z * 0.5f, size.z * 0.5f, 0.1f, size.y + 20f);
            projMatrix = GL.GetGPUProjectionMatrix(projMatrix, false);

            CommandBuffer cmd = new CommandBuffer();
            cmd.name = "Bake Grass Mesh Data";
            
            cmd.SetRenderTarget(meshDataRT);
            cmd.ClearRenderTarget(true, true, Color.clear);
            cmd.SetViewProjectionMatrices(viewMatrix, projMatrix);
            
            Material bakeMat = new Material(bakeShader);
            // 传递包围盒信息给 Shader
            bakeMat.SetVector("_MeshBoundsMin", meshBounds.min);
            bakeMat.SetVector("_MeshBoundsSize", meshBounds.size);

            cmd.DrawRenderer(targetMeshRenderer, bakeMat);

            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
            DestroyImmediate(bakeMat);

            // 将数据同步给全局变量
            Shader.SetGlobalTexture("_MeshGrassDataRT", meshDataRT);
            Shader.SetGlobalVector("_MeshBoundsMin", meshBounds.min);
            Shader.SetGlobalVector("_MeshBoundsSize", meshBounds.size);
            
            Debug.Log("Mesh 草地数据烘焙成功！");
        }
        
        void OnDrawGizmosSelected()
        {
            Bounds b;
            if (useCustomBounds)
            {
                b = new Bounds(customCenter, customSize);
            }
            else
            {
                if (targetMeshRenderer == null) return;
                b = targetMeshRenderer.bounds;
            }

            // 绘制绿色框体
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(b.center, b.size);

            // 绘制正交相机预览
            Vector3 cameraPos = b.center + Vector3.up * (b.size.y * 0.5f + 10f);
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(cameraPos, 0.5f);
            Gizmos.DrawLine(cameraPos, cameraPos + Vector3.down * (b.size.y + 20f));
            Gizmos.DrawWireCube(cameraPos, new Vector3(b.size.x, 0, b.size.z));
        }
    }
}