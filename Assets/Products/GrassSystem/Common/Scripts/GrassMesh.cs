using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Zerls.GrassSystem
{
    public class GrassMesh
    {
        public static Mesh CreateHighLODMesh()
        {
            Mesh mesh = new Mesh();
            mesh.name = "GrassMesh_HighLOD";
            mesh.vertices = new Vector3[]
            {
                new Vector3(0.000000f, 0.15599f, 0.03445f),
                new Vector3(0.000000f, 0.00000f, -0.03444f),
                new Vector3(0.000000f, 0.00000f, 0.03444f),
                new Vector3(0.000000f, 0.15599f, -0.03445f),
                new Vector3(0.000000f, 0.27249f, -0.03193f),
                new Vector3(0.000000f, 0.27249f, 0.03193f),
                new Vector3(0.000000f, 0.38111f, -0.02942f),
                new Vector3(0.000000f, 0.38111f, 0.02942f),
                new Vector3(0.000000f, 0.47325f, -0.02620f),
                new Vector3(0.000000f, 0.47325f, 0.02620f),
                new Vector3(0.000000f, 0.55531f, -0.02338f),
                new Vector3(0.000000f, 0.55531f, 0.02338f),
                new Vector3(0.000000f, 0.63064f, -0.01728f),
                new Vector3(0.000000f, 0.63064f, 0.01728f),
                new Vector3(0.000000f, 0.70819f, 0.00000f)
            };
            mesh.triangles = new int[]
            {
                0, 1, 2,
                0, 3, 1,
                0, 4, 3,
                0, 5, 4,
                5, 6, 4,
                5, 7, 6,
                7, 8, 6,
                7, 9, 8,
                9, 10, 8,
                9, 11, 10,
                12, 10, 11,
                11, 13, 12,
                13, 14, 12
            };
            mesh.colors = new Color[]
            {
                new Color(0.141177f, 0.000000f, 0.000000f, 1.000000f),
                new Color(0.000000f, 1.000000f, 0.000000f, 1.000000f),
                new Color(0.000000f, 0.000000f, 0.000000f, 1.000000f),
                new Color(0.141177f, 1.000000f, 0.000000f, 1.000000f),
                new Color(0.286275f, 1.000000f, 0.000000f, 1.000000f),
                new Color(0.286275f, 0.000000f, 0.000000f, 1.000000f),
                new Color(0.427451f, 1.000000f, 0.000000f, 1.000000f),
                new Color(0.427451f, 0.000000f, 0.000000f, 1.000000f),
                new Color(0.572549f, 1.000000f, 0.000000f, 1.000000f),
                new Color(0.572549f, 0.000000f, 0.000000f, 1.000000f),
                new Color(0.713726f, 1.000000f, 0.000000f, 1.000000f),
                new Color(0.713726f, 0.000000f, 0.000000f, 1.000000f),
                new Color(0.858824f, 1.000000f, 0.000000f, 1.000000f),
                new Color(0.858824f, 0.000000f, 0.000000f, 1.000000f),
                new Color(1.000000f, 0.498039f, 0.000000f, 1.000000f)
            };

            mesh.uv = new Vector2[]
            {
                new Vector2(0.000000f, 0.220262f),
                new Vector2(1.000000f, 0.000000f),
                new Vector2(0.000000f, 0.000000f),
                new Vector2(1.000000f, 0.220262f),
                new Vector2(0.963353f, 0.384773f), 
                new Vector2(0.036655f, 0.384773f), 
                new Vector2(0.926978f, 0.538140f), 
                new Vector2(0.073021f, 0.538140f), 
                new Vector2(0.880165f, 0.668258f), 
                new Vector2(0.119834f, 0.668258f), 
                new Vector2(0.839251f, 0.784132f), 
                new Vector2(0.160747f, 0.784132f), 
                new Vector2(0.750838f, 0.890497f), 
                new Vector2(0.249161f, 0.890497f), 
                new Vector2(0.500000f, 1.000000f)  // 尖端正好落在 0.5 的中心
            };

            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();

            return mesh;
        }

        public static Mesh CreateLowLODMesh()
        {
            Mesh mesh = new Mesh();
            mesh.name = "GrassMesh_LowLOD";

            // 7 个顶点 (3层高度 * 左右2个 + 1个尖端)
            mesh.vertices = new Vector3[]
            {
                new Vector3(0f, 0.00f, -0.034f), // 0: 底部_左 (Base Left)
                new Vector3(0f, 0.00f, 0.034f), // 1: 底部_右 (Base Right)
                new Vector3(0f, 0.27f, -0.032f), // 2: 中部_左 (Mid Left)
                new Vector3(0f, 0.27f, 0.032f), // 3: 中部_右 (Mid Right)
                new Vector3(0f, 0.55f, -0.023f), // 4: 顶部_左 (Top Left)
                new Vector3(0f, 0.55f, 0.023f), // 5: 顶部_右 (Top Right)
                new Vector3(0f, 0.71f, 0.000f) // 6: 尖端 (Tip Center)
            };

            // 【核心修复】：严格保证 g=0 对应左侧，g=1 对应右侧，绝不交叉！
            mesh.colors = new Color[]
            {
                new Color(0.00f, 0f, 0f, 1f), // 0: 高度t=0,    方向=左(g=0)
                new Color(0.00f, 1f, 0f, 1f), // 1: 高度t=0,    方向=右(g=1)
                new Color(0.28f, 0f, 0f, 1f), // 2: 高度t=0.28, 方向=左(g=0)
                new Color(0.28f, 1f, 0f, 1f), // 3: 高度t=0.28, 方向=右(g=1)
                new Color(0.71f, 0f, 0f, 1f), // 4: 高度t=0.71, 方向=左(g=0)
                new Color(0.71f, 1f, 0f, 1f), // 5: 高度t=0.71, 方向=右(g=1)
                new Color(1.00f, 0.5f, 0f, 1f) // 6: 高度t=1.0,  方向=中(g=0.5)
            };

            // UV 匹配顶点位置
            mesh.uv = new Vector2[]
            {
                new Vector2(0.0f, 0.00f), // 左底
                new Vector2(1.0f, 0.00f), // 右底
                new Vector2(0.0f, 0.38f), // 左侧第二段
                new Vector2(0.9f, 0.38f), // 右侧第二段 (按比例收缩)
                new Vector2(0.1f, 0.78f), // 左侧第三段 (按比例收缩)
                new Vector2(0.8f, 0.78f), // 右侧第三段
                new Vector2(0.5f, 1.00f)  // 尖端居中
            };

            // 标准顺时针 (Clockwise) 环绕，保证法线向外
            mesh.triangles = new int[]
            {
                0, 1, 2, // 下层 1
                2, 1, 3, // 下层 2
                2, 3, 4, // 中层 1
                4, 3, 5, // 中层 2
                4, 5, 6 // 尖端闭合
            };

            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();

            return mesh;
        }

        public static Mesh CreateCrossQuadLODMesh()
        {
            Mesh mesh = new Mesh();
            mesh.name = "GrassMesh_CrossQuadLOD_UltraWide";

            // ==========================================================
            // 参数调整：更宽 (0.3) 更矮 (0.15)
            // ==========================================================
            float w = 0.3f; // 视觉总宽度将达到 0.6 米
            float h = 0.15f; // 视觉高度仅 0.15 米

            // 顶点布局：通过对齐 X/Z 坐标确保它是标准的矩形，而非三角形
            mesh.vertices = new Vector3[]
            {
                // Quad 1 (沿 Z 轴分布)
                new Vector3(0, 0, -w), // 0: 底左
                new Vector3(0, 0, w), // 1: 底右
                new Vector3(0, h, -w), // 2: 顶左
                new Vector3(0, h, w), // 3: 顶右

                // Quad 2 (沿 X 轴分布)
                new Vector3(-w, 0, 0), // 4: 底左
                new Vector3(w, 0, 0), // 5: 底右
                new Vector3(-w, h, 0), // 6: 顶左
                new Vector3(w, h, 0) // 7: 顶右
            };

            // 数据编码：严格对齐 Shader 重建逻辑
            // r = t (0是根, 1是顶), g = side (0是左, 1是右), b = 垂直旋转标记
            mesh.colors = new Color[]
            {
                // Quad 1 (b = 0)
                new Color(0f, 0f, 0f, 1f), // 0: t=0, g=0
                new Color(0f, 1f, 0f, 1f), // 1: t=0, g=1
                new Color(1f, 0f, 0f, 1f), // 2: t=1, g=0
                new Color(1f, 1f, 0f, 1f), // 3: t=1, g=1

                // Quad 2 (b = 1)
                new Color(0f, 0f, 1f, 1f), // 4: t=0, g=0
                new Color(0f, 1f, 1f, 1f), // 5: t=0, g=1
                new Color(1f, 0f, 1f, 1f), // 6: t=1, g=0
                new Color(1f, 1f, 1f, 1f) // 7: t=1, g=1
            };

            // UV 映射
            mesh.uv = new Vector2[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1)
            };

            // 三角形拓扑
            mesh.triangles = new int[]
            {
                0, 2, 1, 1, 2, 3, // Quad 1
                4, 6, 5, 5, 6, 7 // Quad 2
            };

            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();

            return mesh;
        }
        public static Mesh CreateBillboardQuadMesh()
        {
            Mesh mesh = new Mesh();
            mesh.name = "GrassMesh_BillboardQuad";
            
            float w = 0.3f;   
            float h = 0.35f;  

            mesh.vertices = new Vector3[]
            {
                new Vector3(-w, 0, 0), // 0: 底左
                new Vector3( w, 0, 0), // 1: 底右
                new Vector3(-w, h, 0), // 2: 顶左
                new Vector3( w, h, 0)  // 3: 顶右
            };

            // t 和 side 完美映射
            mesh.colors = new Color[]
            {
                new Color(0f, 0f, 1f, 1f), 
                new Color(0f, 1f, 1f, 1f), 
                new Color(1f, 0f, 1f, 1f), 
                new Color(1f, 1f, 1f, 1f)  
            };

            mesh.uv = new Vector2[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1)
            };

            mesh.triangles = new int[] { 0, 2, 1, 1, 2, 3 };

            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            
            return mesh;
        }
    }
    
}