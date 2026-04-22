using UnityEngine;
using System.IO;

public class CloudNoiseGenerator : MonoBehaviour
{
    [Header("Compute Shader Settings")]
    public ComputeShader cloudNoiseShader;
    public bool generate3D = true;
    public bool generate2D = true;

    public int resolution = 256;
    public int resolutionZ = 128;
    public float basicFrequency = 4.0f;
    public float basicNoiseMixFactor = 0.5f;
    public float detailFrequency = 8.0f;
    // public int seed = 0; // For randomization

    [Header("Output Settings")]
    public string savePath = "Assets/GeneratedTextures/";
    public string basicNoiseFileName3D = "BasicNoise3D.asset";
    public string worleyNoiseFileName3D = "DetailNoise3D.asset";
    public string basicNoiseFileName2D = "BasicNoise2D.png";
    public string worleyNoiseFileName2D = "DetailNoise2D.png";

    private Texture3D basicNoiseTexture3D;
    private Texture3D worleyNoiseTexture3D;
    private Texture2D basicNoiseTexture2D;
    private Texture2D worleyNoiseTexture2D;
    
    protected ComputeBuffer tempComputeBuffer;

    void Start()
    {
        // GenerateTextures() 不再在Start中自动调用
        // 需要通过Editor或代码显式调用
    }

    public void GenerateTextures()
    {
        // 设置种子
        // Random.InitState(seed);

        if (generate3D)
        {
            // 生成3D基本噪声纹理
            basicNoiseTexture3D = GenerateBasicNoiseTexture3D();
            if (basicNoiseTexture3D != null)
                Debug.Log("Generated 3D Basic Noise Texture for preview");

            // 生成3D Worley噪声纹理
            worleyNoiseTexture3D = GenerateWorleyNoiseTexture3D();
            if (worleyNoiseTexture3D != null)
                Debug.Log("Generated 3D Worley Noise Texture for preview");
        }

        if (generate2D)
        {
            // 生成2D基本噪声纹理
            basicNoiseTexture2D = GenerateBasicNoiseTexture2D();
            if (basicNoiseTexture2D != null)
                Debug.Log("Generated 2D Basic Noise Texture for preview");

            // 生成2D Worley噪声纹理
            worleyNoiseTexture2D = GenerateWorleyNoiseTexture2D();
            if (worleyNoiseTexture2D != null)
                Debug.Log("Generated 2D Worley Noise Texture for preview");
        }

        Debug.Log("Cloud noise textures generated successfully!");
    }

    public void SaveToDisk()
    {
        // 确保保存路径存在
        if (!Directory.Exists(savePath))
        {
            Directory.CreateDirectory(savePath);
        }

        bool savedAny = false;

        if (generate3D)
        {
            if (basicNoiseTexture3D != null)
            {
                SaveTexture3D(basicNoiseTexture3D, savePath + basicNoiseFileName3D);
                Debug.Log($"Saved 3D Basic Noise to {savePath + basicNoiseFileName3D}");
                savedAny = true;
            }
            else
            {
                Debug.LogWarning("3D Basic Noise texture not generated. Please call GenerateTextures() first.");
            }

            if (worleyNoiseTexture3D != null)
            {
                SaveTexture3D(worleyNoiseTexture3D, savePath + worleyNoiseFileName3D);
                Debug.Log($"Saved 3D Worley Noise to {savePath + worleyNoiseFileName3D}");
                savedAny = true;
            }
            else
            {
                Debug.LogWarning("3D Worley Noise texture not generated. Please call GenerateTextures() first.");
            }
        }

        if (generate2D)
        {
            if (basicNoiseTexture2D != null)
            {
                SaveTexture2D(basicNoiseTexture2D, savePath + basicNoiseFileName2D);
                Debug.Log($"Saved 2D Basic Noise to {savePath + basicNoiseFileName2D}");
                savedAny = true;
            }
            else
            {
                Debug.LogWarning("2D Basic Noise texture not generated. Please call GenerateTextures() first.");
            }

            if (worleyNoiseTexture2D != null)
            {
                SaveTexture2D(worleyNoiseTexture2D, savePath + worleyNoiseFileName2D);
                Debug.Log($"Saved 2D Worley Noise to {savePath + worleyNoiseFileName2D}");
                savedAny = true;
            }
            else
            {
                Debug.LogWarning("2D Worley Noise texture not generated. Please call GenerateTextures() first.");
            }
        }

        if (savedAny)
        {
            Debug.Log("Cloud noise textures saved to disk successfully!");
        }
        else
        {
            Debug.LogWarning("No textures to save. Please generate textures first.");
        }
    }

    private Texture3D GenerateBasicNoiseTexture3D()
    {
        return GenerateNoiseTexture3D("CSMainBaiscNoise", basicFrequency, basicNoiseMixFactor, detailFrequency);
    }

    private Texture3D GenerateWorleyNoiseTexture3D()
    {
        return GenerateNoiseTexture3D("CSMainWorleyNoise", basicFrequency, basicNoiseMixFactor, detailFrequency);
    }

    private Texture2D GenerateBasicNoiseTexture2D()
    {
        return GenerateNoiseTexture2D("CSMainBasicNoise2D", basicFrequency, basicNoiseMixFactor, detailFrequency);
    }

    private Texture2D GenerateWorleyNoiseTexture2D()
    {
        return GenerateNoiseTexture2D("CSMainWorleyNoise2D", basicFrequency, basicNoiseMixFactor, detailFrequency);
    }

    private Texture3D GenerateNoiseTexture3D(string kernelName, float basicFreq, float mixFactor, float detailFreq)
    {
        // 查找kernel
        int kernelIndex = cloudNoiseShader.FindKernel(kernelName);
        if (kernelIndex < 0)
        {
            Debug.LogError($"Kernel '{kernelName}' not found in compute shader!");
            return null;
        }

        // 创建3D RenderTexture，必须在设置完所有属性后显式调用 Create()
        RenderTexture tempRT = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBFloat);
        tempRT.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        tempRT.volumeDepth = resolutionZ;
        tempRT.wrapMode = TextureWrapMode.Repeat;
        tempRT.filterMode = FilterMode.Bilinear;
        tempRT.enableRandomWrite = true;
        tempRT.Create();

        // 创建 ComputeBuffer：每个体素一个 float4（stride = 16 字节）
        int totalVoxels = resolution * resolution * resolutionZ;
        tempComputeBuffer = new ComputeBuffer(totalVoxels, 16);

        // 设置Compute Shader参数
        cloudNoiseShader.SetBuffer(kernelIndex, "_Colors", tempComputeBuffer);
        cloudNoiseShader.SetTexture(kernelIndex, "NoiseTex", tempRT);
        cloudNoiseShader.SetFloat("_BasicFrequency", basicFreq);
        cloudNoiseShader.SetFloat("_BasicNoiseMixFactor", mixFactor);
        cloudNoiseShader.SetFloat("_DetailFrequency", detailFreq);
        cloudNoiseShader.SetInts("_texSize", resolution, resolution, resolutionZ);

        // 计算dispatch大小（XY 每组8线程，Z轴每层1组）
        int threadGroupsX = Mathf.CeilToInt((float)resolution / 8);
        int threadGroupsY = Mathf.CeilToInt((float)resolution / 8);

        // 执行Compute Shader
        cloudNoiseShader.Dispatch(kernelIndex, threadGroupsX, threadGroupsY, resolutionZ);

        // 从 ComputeBuffer 读取数据并写入 Texture3D
        Texture3D resultTexture = new Texture3D(resolution, resolution, resolutionZ, TextureFormat.RGBAFloat, false);
        resultTexture.wrapMode = TextureWrapMode.Repeat;
        resultTexture.filterMode = FilterMode.Bilinear;

        Color[] colors = new Color[tempComputeBuffer.count];
        tempComputeBuffer.GetData(colors);
        resultTexture.SetPixels(colors);
        resultTexture.Apply();

        // 清理临时资源
        tempRT.Release();
        Destroy(tempRT);
        tempComputeBuffer.Release();
        tempComputeBuffer = null;

        Debug.Log($"Generated 3D noise texture: {kernelName} ({threadGroupsX}x{threadGroupsY}x{resolutionZ})");
        return resultTexture;
    }

    private Texture2D GenerateNoiseTexture2D(string kernelName, float basicFreq, float mixFactor, float detailFreq)
    {
        // 查找kernel
        int kernelIndex = cloudNoiseShader.FindKernel(kernelName);
        if (kernelIndex < 0)
        {
            Debug.LogError($"Kernel '{kernelName}' not found in compute shader!");
            return null;
        }

        // 创建临时RenderTexture以支持浮点数据
        RenderTexture tempRT = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBFloat);
        tempRT.wrapMode = TextureWrapMode.Repeat;
        tempRT.filterMode = FilterMode.Bilinear;
        tempRT.enableRandomWrite = true;
        tempRT.Create();

        // 设置Compute Shader参数
        cloudNoiseShader.SetTexture(kernelIndex, "NoiseTex2D", tempRT);
        cloudNoiseShader.SetFloat("_BasicFrequency", basicFreq);
        cloudNoiseShader.SetFloat("_BasicNoiseMixFactor", mixFactor);
        cloudNoiseShader.SetFloat("_DetailFrequency", detailFreq);
        cloudNoiseShader.SetInts("_texSize2D", resolution, resolution);

        // 计算dispatch大小
        int threadGroupsX = Mathf.CeilToInt((float)resolution / 8);
        int threadGroupsY = Mathf.CeilToInt((float)resolution / 8);

        // 执行Compute Shader
        cloudNoiseShader.Dispatch(kernelIndex, threadGroupsX, threadGroupsY, 1);

        // 读取RenderTexture到Texture2D
        Texture2D resultTexture = new Texture2D(resolution, resolution, TextureFormat.RGBAFloat, false);
        RenderTexture.active = tempRT;
        resultTexture.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
        resultTexture.Apply();
        RenderTexture.active = null;

        // 清理临时资源
        tempRT.Release();
        Destroy(tempRT);

        Debug.Log($"Generated 2D noise texture: {kernelName} ({resolution}x{resolution})");
        return resultTexture;
    }

    private void SaveTexture3D(Texture3D texture, string path)
    {
        // 将纹理保存为资产
        #if UNITY_EDITOR
        UnityEditor.AssetDatabase.CreateAsset(texture, path);
        UnityEditor.AssetDatabase.Refresh();
        #endif
    }

    private void SaveTexture2D(Texture2D texture, string path)
    {
        // 将纹理保存为PNG
        byte[] bytes = texture.EncodeToPNG();
        File.WriteAllBytes(path, bytes);
        #if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
        #endif
    }

    // 公共方法，用于在运行时获取生成的纹理
    public Texture3D GetBasicNoiseTexture3D()
    {
        return basicNoiseTexture3D;
    }

    public Texture3D GetWorleyNoiseTexture3D()
    {
        return worleyNoiseTexture3D;
    }

    public Texture2D GetBasicNoiseTexture2D()
    {
        return basicNoiseTexture2D;
    }

    public Texture2D GetWorleyNoiseTexture2D()
    {
        return worleyNoiseTexture2D;
    }
}