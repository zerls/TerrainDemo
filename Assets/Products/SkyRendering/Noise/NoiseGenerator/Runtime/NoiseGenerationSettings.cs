using UnityEngine;

namespace Zerlll.Plugins.NoiseGenerator
{
    [System.Serializable]
    public class NoiseGenerationSettings
    {
        public NoiseType type = NoiseType.Perlin;
        public int width = 512;
        public int height = 512;
        public int depth = 1;
        public float scale = 10f;
        public Vector3 offset = Vector3.zero;
        public int octaves = 4;
        public float persistence = 0.5f;
        public float lacunarity = 2f;
        public float blueNoiseRadius = 0.45f;
        public int blueNoiseSamples = 64;

        public bool Is3D => depth > 1;

        public void Validate()
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            depth = Mathf.Max(1, depth);
            scale = Mathf.Max(0.0001f, scale);
            octaves = Mathf.Clamp(octaves, 1, 12);
            persistence = Mathf.Clamp01(persistence);
            lacunarity = Mathf.Max(1f, lacunarity);
            blueNoiseRadius = Mathf.Max(0.0001f, blueNoiseRadius);
            blueNoiseSamples = Mathf.Clamp(blueNoiseSamples, 1, 1024);
        }

        public static NoiseGenerationSettings Create2D(int width, int height, NoiseType type)
        {
            return new NoiseGenerationSettings
            {
                width = width,
                height = height,
                depth = 1,
                type = type,
            };
        }

        public static NoiseGenerationSettings Create3D(int width, int height, int depth, NoiseType type)
        {
            return new NoiseGenerationSettings
            {
                width = width,
                height = height,
                depth = depth,
                type = type,
            };
        }
    }
}