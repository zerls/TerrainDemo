using UnityEngine;
using System.Collections;

namespace Zerlll.Plugins.NoiseGenerator
{
    public enum NoiseType
    {
        Perlin,
        Worley,
        Voronoi,
        Simplex,
        Fractal,
        BlueNoise // 新增蓝噪声类型
    }

    // [CreateAssetMenu(menuName = "Noise/Generator")] // 可选：添加创建菜单
    public class NoiseGenerator : ScriptableObject
    {
        private const int ThreadGroupSize = 8;

        public ComputeShader noiseComputeShader;

        public Texture2D GenerateNoiseTexture(int width, int height, NoiseType type,
            float scale = 10f, float offsetX = 0, float offsetY = 0,
            int octaves = 4, float persistence = 0.5f,
            float lacunarity = 2f,
            float blueNoiseRadius = 0.45f, int blueNoiseSamples = 64)
        {
            NoiseGenerationSettings settings = NoiseGenerationSettings.Create2D(width, height, type);
            settings.scale = scale;
            settings.offset = new Vector3(offsetX, offsetY, 0f);
            settings.octaves = octaves;
            settings.persistence = persistence;
            settings.lacunarity = lacunarity;
            settings.blueNoiseRadius = blueNoiseRadius;
            settings.blueNoiseSamples = blueNoiseSamples;

            return (Texture2D)GenerateNoiseTexture(settings);
        }

        public Texture3D GenerateNoiseTexture3D(int width, int height, int depth, NoiseType type,
            float scale = 10f, float offsetX = 0, float offsetY = 0, float offsetZ = 0,
            int octaves = 4, float persistence = 0.5f,
            float lacunarity = 2f,
            float blueNoiseRadius = 0.45f, int blueNoiseSamples = 64)
        {
            NoiseGenerationSettings settings = NoiseGenerationSettings.Create3D(width, height, depth, type);
            settings.scale = scale;
            settings.offset = new Vector3(offsetX, offsetY, offsetZ);
            settings.octaves = octaves;
            settings.persistence = persistence;
            settings.lacunarity = lacunarity;
            settings.blueNoiseRadius = blueNoiseRadius;
            settings.blueNoiseSamples = blueNoiseSamples;

            return (Texture3D)GenerateNoiseTexture(settings);
        }

        public Texture GenerateNoiseTexture(NoiseGenerationSettings settings)
        {
            if (noiseComputeShader == null)
            {
                throw new System.InvalidOperationException("Noise compute shader is not assigned.");
            }

            settings.Validate();

            int voxelCount = settings.width * settings.height * settings.depth;
            int kernel = noiseComputeShader.FindKernel(settings.type.ToString());
            ComputeBuffer colorBuffer = new ComputeBuffer(voxelCount, sizeof(float) * 4);

            try
            {
                noiseComputeShader.SetBuffer(kernel, "Colors", colorBuffer);
                noiseComputeShader.SetInt("width", settings.width);
                noiseComputeShader.SetInt("height", settings.height);
                noiseComputeShader.SetInt("depth", settings.depth);
                noiseComputeShader.SetInt("generate3D", settings.Is3D ? 1 : 0);
                noiseComputeShader.SetFloat("scale", settings.scale);
                noiseComputeShader.SetVector("offset", settings.offset);
                noiseComputeShader.SetInt("octaves", settings.octaves);
                noiseComputeShader.SetFloat("persistence", settings.persistence);
                noiseComputeShader.SetFloat("lacunarity", settings.lacunarity);
                noiseComputeShader.SetFloat("blueNoiseRadius", settings.blueNoiseRadius);
                noiseComputeShader.SetInt("blueNoiseSamples", settings.blueNoiseSamples);

                int dispatchX = Mathf.CeilToInt(settings.width / (float)ThreadGroupSize);
                int dispatchY = Mathf.CeilToInt(settings.height / (float)ThreadGroupSize);
                int dispatchZ = settings.Is3D ? settings.depth : 1;

                noiseComputeShader.Dispatch(kernel, dispatchX, dispatchY, dispatchZ);

                Color[] colors = new Color[voxelCount];
                colorBuffer.GetData(colors);

                if (settings.Is3D)
                {
                    return CreateTexture3D(settings.width, settings.height, settings.depth, colors);
                }

                return CreateTexture2D(settings.width, settings.height, colors);
            }
            finally
            {
                colorBuffer.Release();
            }
        }

        private Texture2D CreateTexture2D(int width, int height, Color[] colors)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels(colors);
            texture.Apply();
            return texture;
        }

        private Texture3D CreateTexture3D(int width, int height, int depth, Color[] colors)
        {
            Texture3D texture = new Texture3D(width, height, depth, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels(colors);
            texture.Apply();
            return texture;
        }
    }
}