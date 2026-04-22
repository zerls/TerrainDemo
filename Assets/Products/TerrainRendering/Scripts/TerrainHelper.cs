using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainRendering
{
    public class TerrainHelper
    {
        /// <summary>
        /// 创建居中的地形网格 (Plane)
        /// </summary>
        /// <param name="size">单边网格分辨率</param>
        /// <returns>生成的 Mesh</returns>
        public static Mesh CreateTerrainPlaneMesh(int size)
        {
            var mesh = new Mesh {name ="TerrainPlane" };

            if (size <= 0) return mesh; // 拦截非法输入

            var sizePerGrid = 0.5f; //地形分辨率为 0.5m
            var totalSizeMeter = size * sizePerGrid;
            
            int vertexCountPerAxis = size + 1;
            int totalVertices = vertexCountPerAxis * vertexCountPerAxis;
            var gridCount = size * size;
            
            Vector3[] vertices = new Vector3[totalVertices];
            Vector2[] uvs = new Vector2[totalVertices];

            var vOffset = - totalSizeMeter * 0.5f;
            float uvStrip = 1.0f / size;
            
            int vIndex = 0;
            for (int z = 0; z <= size; z++)
            {
                for (int x = 0; x <= size; x++)
                {
                    vertices[vIndex] = new Vector3(vOffset + x * sizePerGrid, 0.0f, vOffset + z * sizePerGrid);
                    uvs[vIndex] = new Vector2(x * uvStrip, z * uvStrip);
                    vIndex++;
                }
            }

            int[] indices = new int[gridCount * 6]; // 每个网格2个三角形，共6个顶点索引
            int offset = 0;
            for (int gridIndex = 0; gridIndex < gridCount; gridIndex++)
            {
                // 计算当前格子所在的行和列
                int row = gridIndex / size;
                int col = gridIndex % size;

                // 计算该格子4个顶点在 vertices 数组中的索引
                int bottomLeft = row * vertexCountPerAxis + col;
                int bottomRight = bottomLeft + 1;
                int topLeft = bottomLeft + vertexCountPerAxis;
                int topRight = topLeft + 1;

                // 第一个三角形 (左下 -> 左上 -> 右下) - 顺时针
                indices[offset]     = bottomLeft;
                indices[offset + 1] = topLeft;
                indices[offset + 2] = bottomRight;

                // 第二个三角形 (右下 -> 左上 -> 右上) - 顺时针
                indices[offset + 3] = bottomRight;
                indices[offset + 4] = topLeft;
                indices[offset + 5] = topRight;
                
                offset += 6;
            }
            
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = indices;
            
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            mesh.UploadMeshData(false);
            return mesh;
        }
        
        
        public static RenderTexture CreateRenderTextureWithMipTextures(Texture2D[] mipmaps,RenderTextureFormat format){
            var mip0 = mipmaps[0];
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(mip0.width,mip0.height,format,0,mipmaps.Length);
            descriptor.autoGenerateMips = false;
            descriptor.useMipMap = true;
            RenderTexture rt = new RenderTexture(descriptor);
            rt.filterMode = mip0.filterMode;
            rt.Create();
            for(var i = 0; i < mipmaps.Length; i ++){
                Graphics.CopyTexture(mipmaps[i],0,0,rt,0,i);
            }
            return rt;
        }

        public static RenderTexture CreateLODMap(int size){
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(size,size,RenderTextureFormat.R8,0,1);
            descriptor.autoGenerateMips = false;
            descriptor.enableRandomWrite = true;
            RenderTexture rt = new RenderTexture(descriptor);
            rt.filterMode = FilterMode.Point;
            rt.Create();
            return rt;
        }
    }
}