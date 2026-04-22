using UnityEngine;
using UnityEditor;
using System.IO;

namespace Zerlll.Plugins.NoiseGenerator
{

public class NoiseGeneratorEditor : EditorWindow {
    private static readonly int[] TEXTURE_SIZES = { 64, 128, 256, 512, 1024, 2048, 4096 };
    private static readonly int[] TEXTURE_3D_SIZES = { 16, 32, 64, 128, 256 };
    private static readonly GUIContent[] SIZE_LABELS = {
        new GUIContent("64x64"),
        new GUIContent("128x128"),
        new GUIContent("256x256"),
        new GUIContent("512x512"),
        new GUIContent("1024x1024"),
        new GUIContent("2048x2048"),
        new GUIContent("4096x4096")
    };
    private static readonly GUIContent[] SIZE_3D_LABELS = {
        new GUIContent("16x16x16"),
        new GUIContent("32x32x32"),
        new GUIContent("64x64x64"),
        new GUIContent("128x128x128"),
        new GUIContent("256x256x256")
    };
    private static readonly NoiseType[] NOISE_TYPE_OPTIONS = {
        NoiseType.Perlin,
        NoiseType.Worley,
        NoiseType.Voronoi,
        NoiseType.Simplex,
        NoiseType.Fractal,
        NoiseType.BlueNoise,
    };
    private static readonly GUIContent[] NOISE_TYPE_LABELS = {
        new GUIContent("Perlin"),
        new GUIContent("Worley"),
        new GUIContent("Voronoi"),
        new GUIContent("Simplex"),
        new GUIContent("Fractal"),
        new GUIContent("Blue Noise"),
    };

    private int _selectedSizeIndex = 3; // 默认选择512x512
    private int _selectedVolumeSizeIndex = 2;
    private int textureSize = 512;
    private int textureDepth = 64;
    private bool generate3D = false;
    private NoiseType noiseType = NoiseType.Perlin;
    private float scale = 10f;
    private Vector3 offset = Vector3.zero;
    private int octaves = 4;
    private float persistence = 0.5f;
    private float lacunarity = 2f;
    // 新增蓝噪声专用参数
    private float blueNoiseRadius = 0.45f;
    private int blueNoiseSamples = 64;
    private Texture2D previewTexture;
    private Texture3D previewTexture3D;
    private int previewSlice = 0;

    private static ComputeShader computeShader;
    private const string SHADER_PATH = 
        "Assets/Products/SkyRendering/Noise/Shaders/NoiseCompute.compute";
        // "Assets/Plugins/NoiseGenerator/Shaders/NoiseCompute.compute";
    
    [MenuItem("Tools/Noise Generator")]
    public static void ShowWindow() {
        var window = GetWindow<NoiseGeneratorEditor>();
        window.titleContent = new GUIContent("Noise Generator");
        window.minSize = new Vector2(300, 420);
        // window.LoadPreferences();
        window.Show();
        // 提前加载并验证Compute Shader
        computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(SHADER_PATH);
        if (computeShader == null) {
            Debug.LogError("🔴 Compute Shader加载失败！确认以下内容：\n" +
                           "1. 文件路径：Assets/Products/SkyRendering/Noise/Shaders/NoiseCompute.compute\n" +
                           "2. 扩展名必须为.compute\n" +
                           "3. 文件已通过Unity导入");
            return;
        }
    }

    void OnGUI() {
        GUILayout.Label("Noise Settings", EditorStyles.boldLabel);
        generate3D = EditorGUILayout.Toggle("Generate 3D", generate3D);
        DrawSizeSelector();
        DrawNoiseTypeSelector();
        scale = EditorGUILayout.Slider("Frequency", scale, 1f, 100f);
        if (generate3D) {
            offset = EditorGUILayout.Vector3Field("Offset", offset);
        }
        else {
            Vector2 offset2D = EditorGUILayout.Vector2Field("Offset", new Vector2(offset.x, offset.y));
            offset = new Vector3(offset2D.x, offset2D.y, 0f);
        }
        
        if(noiseType == NoiseType.Fractal) {
            octaves = EditorGUILayout.IntSlider("Octaves", octaves, 1, 8);
            persistence = EditorGUILayout.Slider("Persistence", persistence, 0.1f, 1f);
            lacunarity = EditorGUILayout.Slider("Lacunarity", lacunarity, 1f, 4f);
        }
        if(noiseType == NoiseType.BlueNoise) {
            blueNoiseRadius = EditorGUILayout.Slider("Point Radius", blueNoiseRadius, 0.1f, 1.0f);
            blueNoiseSamples = EditorGUILayout.IntSlider("Sample Points", blueNoiseSamples, 16, 256);
        }

        if (GUILayout.Button("Generate Preview")) {
            GeneratePreview();
        }

        if (previewTexture != null) {
            GUILayout.Label("Preview:");
            if (previewTexture3D != null) {
                int newSlice = EditorGUILayout.IntSlider("Preview Slice", previewSlice, 0, previewTexture3D.depth - 1);
                if (newSlice != previewSlice) {
                    previewSlice = newSlice;
                    Refresh3DPreviewSlice();
                }
            }
            EditorGUI.DrawPreviewTexture(
                GUILayoutUtility.GetAspectRect(1), 
                previewTexture,
                null,
                ScaleMode.ScaleToFit
            );
        }

        if (GUILayout.Button("Save to Assets")) {
            SaveTexture();
        }
    }
    void DrawSizeSelector()
    {
        EditorGUILayout.LabelField("Texture Size", EditorStyles.boldLabel);

        if (generate3D)
        {
            int newIndex = EditorGUILayout.Popup(
                new GUIContent("Select Volume Size"),
                _selectedVolumeSizeIndex,
                SIZE_3D_LABELS
            );

            if (newIndex != _selectedVolumeSizeIndex)
            {
                _selectedVolumeSizeIndex = newIndex;
                textureSize = TEXTURE_3D_SIZES[_selectedVolumeSizeIndex];
                textureDepth = TEXTURE_3D_SIZES[_selectedVolumeSizeIndex];
                previewSlice = Mathf.Clamp(previewSlice, 0, textureDepth - 1);
            }
        }
        else
        {
            int newIndex = EditorGUILayout.Popup(
                new GUIContent("Select Size"),
                _selectedSizeIndex,
                SIZE_LABELS
            );

            if (newIndex != _selectedSizeIndex)
            {
                _selectedSizeIndex = newIndex;
                textureSize = TEXTURE_SIZES[_selectedSizeIndex];
            }
            textureDepth = 1;
        }
    }

    void DrawNoiseTypeSelector()
    {
        int currentIndex = System.Array.IndexOf(NOISE_TYPE_OPTIONS, noiseType);
        if (currentIndex < 0) {
            currentIndex = 0;
        }

        int newIndex = EditorGUILayout.Popup(new GUIContent("Noise Type"), currentIndex, NOISE_TYPE_LABELS);
        noiseType = NOISE_TYPE_OPTIONS[newIndex];
    }

    private void GeneratePreview() {

        // 创建实例并注入依赖
        NoiseGenerator generator = ScriptableObject.CreateInstance<NoiseGenerator>();
        generator.noiseComputeShader = computeShader;

        // 执行生成前进行空值检查
        if (generator.noiseComputeShader == null) {
            Debug.LogError("⚠️ Compute Shader未成功注入生成器");
            DestroyImmediate(generator);
            return;
        }

        NoiseGenerationSettings settings = generate3D
            ? NoiseGenerationSettings.Create3D(textureSize, textureSize, textureDepth, noiseType)
            : NoiseGenerationSettings.Create2D(textureSize, textureSize, noiseType);

        settings.scale = scale;
        settings.offset = offset;
        settings.octaves = octaves;
        settings.persistence = persistence;
        settings.lacunarity = lacunarity;
        settings.blueNoiseRadius = blueNoiseRadius;
        settings.blueNoiseSamples = blueNoiseSamples;

        Texture texture = generator.GenerateNoiseTexture(settings);
        previewTexture3D = texture as Texture3D;
        previewTexture = texture as Texture2D;

        if (previewTexture3D != null) {
            previewSlice = Mathf.Clamp(previewSlice, 0, textureDepth - 1);
            Refresh3DPreviewSlice();
        }

        DestroyImmediate(generator);
    }

    private void Refresh3DPreviewSlice()
    {
        if (previewTexture3D == null) {
            return;
        }

        int volumeWidth = previewTexture3D.width;
        int volumeHeight = previewTexture3D.height;
        int volumeDepth = previewTexture3D.depth;

        Color[] volumeColors = previewTexture3D.GetPixels();
        Color[] sliceColors = new Color[volumeWidth * volumeHeight];
        int clampedSlice = Mathf.Clamp(previewSlice, 0, volumeDepth - 1);
        int sliceOffset = clampedSlice * volumeWidth * volumeHeight;

        for (int i = 0; i < sliceColors.Length; i++) {
            sliceColors[i] = volumeColors[sliceOffset + i];
        }

        if (previewTexture == null || previewTexture.width != volumeWidth || previewTexture.height != volumeHeight) {
            previewTexture = new Texture2D(volumeWidth, volumeHeight, TextureFormat.RGBA32, false);
            previewTexture.wrapMode = TextureWrapMode.Repeat;
            previewTexture.filterMode = FilterMode.Bilinear;
        }

        previewTexture.SetPixels(sliceColors);
        previewTexture.Apply();
    }

    private void SaveTexture() {
        if (previewTexture == null && previewTexture3D == null) return;

        if (previewTexture3D != null) {
            string assetPath = EditorUtility.SaveFilePanelInProject(
                "Save 3D Noise Texture",
                "noise_volume",
                "asset",
                "Select save location");

            if (!string.IsNullOrEmpty(assetPath)) {
                Texture3D assetTexture = new Texture3D(
                    previewTexture3D.width,
                    previewTexture3D.height,
                    previewTexture3D.depth,
                    previewTexture3D.format,
                    false
                );
                assetTexture.name = Path.GetFileNameWithoutExtension(assetPath);
                assetTexture.wrapMode = previewTexture3D.wrapMode;
                assetTexture.filterMode = previewTexture3D.filterMode;
                assetTexture.SetPixels(previewTexture3D.GetPixels());
                assetTexture.Apply(false, false);

                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.CreateAsset(assetTexture, assetPath);
                EditorUtility.SetDirty(assetTexture);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return;
        }

        string path = EditorUtility.SaveFilePanelInProject(
            "Save Noise Texture",
            "noise_texture",
            "png",
            "Select save location");

        if (!string.IsNullOrEmpty(path)) {
            byte[] bytes = previewTexture.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
            AssetDatabase.Refresh();

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer != null) {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }
    }
}
}