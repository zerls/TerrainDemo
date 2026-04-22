using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

namespace Zerls.GrassSystem
{
    public class SeamlessNoiseGenerator : MonoBehaviour
    {
        [Range(1, 50)] public float noiseScale = 10f;

        public int seed = 42;

        public Vector3Int channelOffsets = new Vector3Int(0, 100, 200);

        public Vector3 channelScales = new Vector3(1.0f, 1.2f, 0.8f);

        [Range(32, 1024)] public int textureSize = 512;

        public Texture2D generatedTexture;

        void OnEnable()
        {
            if (generatedTexture == null)
                GenerateNoiseTexture();
        }

        public void GenerateNoiseTexture()
        {
            textureSize = Mathf.ClosestPowerOfTwo(textureSize);

            if (generatedTexture == null || generatedTexture.width != textureSize)
            {
                generatedTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGB24, false);
            }

            Random.InitState(seed);

            Vector2[] channelOffsetVectors = new Vector2[3];
            for (int channel = 0; channel < 3; channel++)
            {
                int channelSeed = seed;

                switch (channel)
                {
                    case 0:
                        channelSeed += channelOffsets.x;
                        break;
                    case 1:
                        channelSeed += channelOffsets.y;
                        break;
                    case 2:
                        channelSeed += channelOffsets.z;
                        break;
                }

                Random.InitState(channelSeed);

                channelOffsetVectors[channel] = new Vector2(
                    Random.Range(-10000f, 10000f),
                    Random.Range(-10000f, 10000f)
                );
            }

            Color[] colorMap = new Color[textureSize * textureSize];

            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    Vector3 channelNoise = Vector3.zero;

                    for (int channel = 0; channel < 3; channel++)
                    {
                        float scale = noiseScale;
                        switch (channel)
                        {
                            case 0:
                                scale *= channelScales.x;
                                break;
                            case 1:
                                scale *= channelScales.y;
                                break;
                            case 2:
                                scale *= channelScales.z;
                                break;
                        }

                        float sampleX = x / (float)textureSize * scale;
                        float sampleY = y / (float)textureSize * scale;

                        float noiseValue = Mathf.PerlinNoise(channelOffsetVectors[channel].x + sampleX,
                            channelOffsetVectors[channel].y + sampleY);


                        switch (channel)
                        {
                            case 0:
                                channelNoise.x = noiseValue;
                                break;
                            case 1:
                                channelNoise.y = noiseValue;
                                break;
                            case 2:
                                channelNoise.z = noiseValue;
                                break;
                        }
                    }

                    colorMap[y * textureSize + x] = new Color(
                        channelNoise.x,
                        channelNoise.y,
                        channelNoise.z
                    );
                }
            }

            generatedTexture.SetPixels(colorMap);
            generatedTexture.Apply();
        }

        public void RandomizeSeed()
        {
            seed = Random.Range(0, 10000);
            GenerateNoiseTexture();
        }

        public void ExportTextureToPNG(string path)
        {
            if (generatedTexture == null)
            {
                Debug.LogError("没有纹理可以导出!");
                return;
            }

            byte[] bytes = generatedTexture.EncodeToPNG();
            File.WriteAllBytes(path, bytes);

        #if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
        #endif
        }
    }
}