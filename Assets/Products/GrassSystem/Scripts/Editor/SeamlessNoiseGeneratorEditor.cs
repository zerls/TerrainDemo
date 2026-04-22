
using UnityEditor;
using UnityEngine;
using System.IO;

namespace Zerls.GrassSystem
{
    [CustomEditor(typeof(SeamlessNoiseGenerator))]
    public class SeamlessNoiseGeneratorEditor : Editor
    {
        // 序列化属性
        private SerializedProperty noiseScale;
        private SerializedProperty seed;
        private SerializedProperty channelOffsets;
        private SerializedProperty channelScales;
        private SerializedProperty textureSize;

        // 折叠选项
        private bool showNoiseSettings = true;
        private bool showChannelSettings = true;
        private bool showTextureSettings = true;
        private bool showExportSettings = true;


        // 导出选项

        private string exportFileName = "NoiseTexture";
        private string exportPath = "";

        private void OnEnable()
        {
            // 查找所有序列化属性
            noiseScale = serializedObject.FindProperty("noiseScale");
            seed = serializedObject.FindProperty("seed");
            channelOffsets = serializedObject.FindProperty("channelOffsets");
            channelScales = serializedObject.FindProperty("channelScales");

            textureSize = serializedObject.FindProperty("textureSize");

            // 设置导出路径
            exportPath = Application.dataPath;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SeamlessNoiseGenerator generator = (SeamlessNoiseGenerator)target;

            // 纹理预览
            if (generator.generatedTexture != null)
            {
                EditorGUILayout.Space(5);
                Rect previewRect = GUILayoutUtility.GetRect(256, 256);
                GUI.DrawTexture(previewRect, generator.generatedTexture, ScaleMode.ScaleToFit);
            }

            EditorGUILayout.Space(10);

            //基本设置
            showNoiseSettings = EditorGUILayout.Foldout(showNoiseSettings, "基本噪声设置",true, EditorStyles.foldoutHeader);
            if (showNoiseSettings)
            {
                EditorGUILayout.PropertyField(noiseScale,new GUIContent("噪声比例"));
                EditorGUILayout.PropertyField(seed,new GUIContent("随机种子"));
                
                EditorGUILayout.Space(5);
                if (GUILayout.Button("随机化种子", GUILayout.Height(25)))
                {
                    generator.RandomizeSeed();
                }
            }

            EditorGUILayout.Space(5);
            
            //RGB通道设置
            showChannelSettings = EditorGUILayout.Foldout(showChannelSettings, "RGB通道设置", true, EditorStyles.foldoutHeader);
            if (showChannelSettings)
            {
                EditorGUILayout.PropertyField(channelOffsets,new GUIContent("通道偏移 (R,G,B) "));

                if (GUILayout.Button("随机化通道偏移",GUILayout.Height(25)))
                {
                    channelOffsets.vector3IntValue = new Vector3Int(
                        Random.Range(0, 1000),
                        Random.Range(1000, 2000),
                        Random.Range(2000, 3000)
                    );
                }
                EditorGUILayout.PropertyField(channelScales,new GUIContent("通道偏移 (R,G,B) "));
            }
            
            EditorGUILayout.Space(5);
            
            //纹理设置
            showTextureSettings = EditorGUILayout.Foldout(showTextureSettings, "纹理设置", true, EditorStyles.foldoutHeader);
            if (showTextureSettings)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(textureSize,new GUIContent("纹理大小"));
                if (EditorGUI.EndChangeCheck())
                {
                    textureSize.intValue = Mathf.ClosestPowerOfTwo(textureSize.intValue);
                }
            }
            EditorGUILayout.Space(5);
            
            //导出设置
            showExportSettings = EditorGUILayout.Foldout(showExportSettings, "导出设置", true, EditorStyles.foldoutHeader);
            if (showExportSettings)
            {
                exportFileName = EditorGUILayout.TextField("文件名", exportFileName);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("导出路径", exportPath,EditorStyles.wordWrappedLabel);
                if (GUILayout.Button("选择", GUILayout.Width(60)))
                {
                    string path =EditorUtility.SaveFolderPanel("选择导出文件夹", exportPath, "");
                    if (!string.IsNullOrEmpty(path))
                    {
                        exportPath = path;
                    }
                }
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(5);
                if(GUILayout.Button("导出为PNG",GUILayout.Height(25)))
                {
                    if(string.IsNullOrEmpty(exportFileName))
                    {
                        EditorUtility.DisplayDialog("错误","请指定文件名!","确定");
                        return;
                    }
                    string fullPath =Path.Combine(exportPath,exportFileName+".png");
                    generator.ExportTextureToPNG(fullPath);
                }
            }


            //生成按钮
            if (GUILayout.Button("生成噪声纹理", GUILayout.Height(30)))
            {
                generator.GenerateNoiseTexture();
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}