using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

[CustomEditor(typeof(CloudNoiseGenerator), true)]
public class CloudNoiseGeneratorEditor : Editor
{
    private class SlicePreviewCache
    {
        public int width;
        public int height;
        public int depth;
        public Color[] voxels;
        public Color[] slicePixels;
        public Texture2D previewTexture;
        public int lastSlice = -1;
    }

    private CloudNoiseGenerator instance;
    private bool show3DPreview = false;
    private bool show2DPreview = false;
    private const int PREVIEW_SIZE = 256;
    
    private int basicNoise3DSlice = 0;
    private int worleyNoise3DSlice = 0;
    private readonly Dictionary<int, SlicePreviewCache> slicePreviewCache = new Dictionary<int, SlicePreviewCache>();
    
    private void OnEnable() {
        instance = target as CloudNoiseGenerator;
    }

    private void OnDisable()
    {
        foreach (var kv in slicePreviewCache)
        {
            if (kv.Value != null && kv.Value.previewTexture != null)
            {
                DestroyImmediate(kv.Value.previewTexture);
            }
        }

        slicePreviewCache.Clear();
    }

    public override void OnInspectorGUI() {
        serializedObject.Update();

        EditorGUI.BeginChangeCheck();
        base.OnInspectorGUI();
        bool settingsChanged = EditorGUI.EndChangeCheck();
        serializedObject.ApplyModifiedProperties();

        GUILayout.Space(10);
        if (GUILayout.Button("Generate", GUILayout.Height(30))) {
            instance.GenerateTextures();
        }

        GUILayout.Space(5);
        if (GUILayout.Button("SaveToDisk", GUILayout.Height(30))) {
            instance.SaveToDisk();
        }

        // 预览部分
        GUILayout.Space(30);
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
        
        // 3D纹理预览
        show3DPreview = EditorGUILayout.Foldout(show3DPreview, "3D Textures Preview");
        if (show3DPreview)
        {
            DrawTexture3DPreview("Basic Noise 3D", instance.GetBasicNoiseTexture3D(), ref basicNoise3DSlice);
            GUILayout.Space(15);
            DrawTexture3DPreview("Worley Noise 3D", instance.GetWorleyNoiseTexture3D(), ref worleyNoise3DSlice);
        }

        GUILayout.Space(10);

        // 2D纹理预览
        show2DPreview = EditorGUILayout.Foldout(show2DPreview, "2D Textures Preview");
        if (show2DPreview)
        {
            DrawTexture2DPreview("Basic Noise 2D", instance.GetBasicNoiseTexture2D());
            GUILayout.Space(15);
            DrawTexture2DPreview("Worley Noise 2D", instance.GetWorleyNoiseTexture2D());
        }
    }

    private void DrawTexture3DPreview(string label, Texture3D texture, ref int sliceIndex)
    {
        if (texture == null)
        {
            EditorGUILayout.HelpBox($"{label}: Not generated yet", MessageType.Info);
            return;
        }
        
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

        // 层切片控制
        EditorGUILayout.LabelField("Slice Viewer", EditorStyles.miniLabel);
        sliceIndex = EditorGUILayout.IntSlider("Depth Slice", sliceIndex, 0, texture.depth - 1);
        
        // 提取并显示切片
        Texture2D sliceTexture = ExtractSliceFrom3D(texture, sliceIndex);
        if (sliceTexture != null)
        {
            Rect previewRect = EditorGUILayout.GetControlRect(GUILayout.Height(PREVIEW_SIZE));
            EditorGUI.DrawPreviewTexture(previewRect, sliceTexture, null, ScaleMode.ScaleToFit);
        }

        EditorGUILayout.LabelField($"Format: {texture.format}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Size: {texture.width}x{texture.height}x{texture.depth}", EditorStyles.miniLabel);
        
        
        // 显示纹理资源
        // EditorGUILayout.ObjectField("Texture", texture, typeof(Texture3D), false);
        // GUILayout.Space(10);
    }

    private void DrawTexture2DPreview(string label, Texture2D texture)
    {
        if (texture == null)
        {
            EditorGUILayout.HelpBox($"{label}: Not generated yet", MessageType.Info);
            return;
        }
        
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

        // 显示纹理预览缩略图
        Rect previewRect = EditorGUILayout.GetControlRect(GUILayout.Height(PREVIEW_SIZE));
        EditorGUI.DrawPreviewTexture(previewRect, texture, null, ScaleMode.ScaleToFit);
        
        EditorGUILayout.LabelField($"Format: {texture.format}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Size: {texture.width}x{texture.height}", EditorStyles.miniLabel);
        
        // 显示纹理资源
        // EditorGUILayout.ObjectField("Texture", texture, typeof(Texture2D), false);
        // GUILayout.Space(10);
    }

    private Texture2D ExtractSliceFrom3D(Texture3D texture3D, int sliceIndex)
    {
        if (texture3D == null || sliceIndex < 0 || sliceIndex >= texture3D.depth)
            return null;

        try
        {
            int width = texture3D.width;
            int height = texture3D.height;
            int depth = texture3D.depth;
            int slicePixelCount = width * height;
            int expectedLength = slicePixelCount * depth;

            int key = texture3D.GetInstanceID();
            if (!slicePreviewCache.TryGetValue(key, out SlicePreviewCache cache) || cache == null)
            {
                cache = new SlicePreviewCache();
                slicePreviewCache[key] = cache;
            }

            bool dimensionChanged = cache.width != width || cache.height != height || cache.depth != depth;
            if (dimensionChanged || cache.voxels == null || cache.voxels.Length < expectedLength)
            {
                // 读取一次完整体素数据，后续切片切换只做内存拷贝。
                cache.voxels = texture3D.GetPixels(0);
                cache.width = width;
                cache.height = height;
                cache.depth = depth;
                cache.lastSlice = -1;

                if (cache.voxels == null || cache.voxels.Length < expectedLength)
                {
                    return null;
                }
            }

            if (cache.slicePixels == null || cache.slicePixels.Length != slicePixelCount)
            {
                cache.slicePixels = new Color[slicePixelCount];
                cache.lastSlice = -1;
            }

            if (cache.previewTexture == null || cache.previewTexture.width != width || cache.previewTexture.height != height)
            {
                if (cache.previewTexture != null)
                {
                    DestroyImmediate(cache.previewTexture);
                }

                cache.previewTexture = new Texture2D(width, height, TextureFormat.RGBAFloat, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                cache.lastSlice = -1;
            }

            if (cache.lastSlice != sliceIndex)
            {
                int sliceOffset = sliceIndex * slicePixelCount;
                System.Array.Copy(cache.voxels, sliceOffset, cache.slicePixels, 0, slicePixelCount);
                cache.previewTexture.SetPixels(cache.slicePixels);
                cache.previewTexture.Apply(false, false);
                cache.lastSlice = sliceIndex;
            }

            return cache.previewTexture;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to extract slice from 3D texture: {e.Message}");
            return null;
        }
    }
}