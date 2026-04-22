#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Zerls.GrassSystem;

namespace Zerls.EditorTools
{
    public class GrassMeshGeneratorEditor
    {
        [MenuItem("Tools/Terrain/Generate Grass Mesh (High LOD - 15 Verts)")]
        public static void GenerateAndSaveHighMesh() => SaveMeshAsset(GrassMesh.CreateHighLODMesh(), "GrassMesh_HighLOD");

        [MenuItem("Tools/Terrain/Generate Grass Mesh (Low LOD - 7 Verts)")]
        public static void GenerateAndSaveLowMesh() => SaveMeshAsset(GrassMesh.CreateLowLODMesh(), "GrassMesh_LowLOD");


        [MenuItem("Tools/Terrain/Generate Grass Mesh (Cross Quad - 8 Verts)")]
        public static void GenerateAndSaveCrossMesh() => SaveMeshAsset(GrassMesh.CreateCrossQuadLODMesh(), "GrassMesh_CrossQuad");

        [MenuItem("Tools/Terrain/Generate Grass Mesh (Billboard Quad - 4 Verts)")]
        public static void GenerateAndSaveBillboardMesh() => SaveMeshAsset(GrassMesh.CreateBillboardQuadMesh(), "GrassMesh_Billboard");

        private static void SaveMeshAsset(Mesh mesh, string defaultName)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                $"保存草地网格 ({defaultName})",
                defaultName,
                "asset",
                "选择保存网格资源的位置"
            );

            if (string.IsNullOrEmpty(path)) return;

            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            Debug.Log($"<color=green>草地网格已成功生成并保存至: {path}</color>");
            Selection.activeObject = mesh;
        }
    }
}
#endif