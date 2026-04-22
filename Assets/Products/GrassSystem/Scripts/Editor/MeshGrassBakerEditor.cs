using UnityEngine;
using UnityEditor;
using System.IO;
using Zerls.GrassSystem;

[CustomEditor(typeof(MeshGrassBaker))]
public class MeshGrassBakerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        MeshGrassBaker baker = (MeshGrassBaker)target;

        //当开启自定义边界时，显示一个便捷按钮，快速拉取 Mesh 的原始大小
        if (baker.useCustomBounds && baker.targetMeshRenderer != null)
        {
            GUILayout.Space(5);
            if (GUILayout.Button("🔄 从 Target Mesh 读取初始边界大小", GUILayout.Height(25)))
            {
                // 记录 Undo 操作，方便撤销
                Undo.RecordObject(baker, "Fetch Bounds from Mesh");
                baker.customCenter = baker.targetMeshRenderer.bounds.center;
                baker.customSize = baker.targetMeshRenderer.bounds.size;
                EditorUtility.SetDirty(baker);
            }
            EditorGUILayout.HelpBox("修改 Custom Center 和 Size 来缩小或移动绿色的烘焙区域了。", MessageType.Info);
        }

        GUILayout.Space(10);
        if (GUILayout.Button("一键烘焙并预览", GUILayout.Height(30)))
        {
            baker.BakeMeshGrassData();
            EditorUtility.SetDirty(baker);
        }

        if (baker.meshDataRT != null)
        {
            GUILayout.Space(10);
            Rect rect = GUILayoutUtility.GetAspectRect(1.0f);
            EditorGUI.DrawPreviewTexture(rect, baker.meshDataRT);

            if (GUILayout.Button("💾 保存为 PNG 贴图"))
            {
                SaveRTToPNG(baker.meshDataRT);
            }
        }
    }

    private void SaveRTToPNG(RenderTexture rt)
    {
        string path = EditorUtility.SaveFilePanelInProject("保存 PNG", "GrassData", "png", "选择保存位置");
        if (string.IsNullOrEmpty(path)) return;

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        
        RenderTexture.active = prev;

        byte[] bytes = tex.EncodeToPNG();
        File.WriteAllBytes(path, bytes);
        AssetDatabase.Refresh();
        
        Debug.Log("PNG 导出成功！");
    }
}