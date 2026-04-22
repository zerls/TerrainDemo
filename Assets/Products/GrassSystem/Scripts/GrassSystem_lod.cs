using System;
using System.Collections.Generic;
using UnityEngine;
using TerrainRendering;
using UnityEngine.Rendering;

namespace Zerls.GrassSystem
{
    [ExecuteInEditMode]
    public  class GrassSystem_lod : MonoBehaviour
    {
        [Serializable]
        public struct GrassLODSetting
        {
            public string name;
            public Mesh mesh;
            public Material material;
            [Range(64, 1024)] public int resolution;
            [Range(0.001f, 1f)] public float densityMultiplier;

            [Range(1f, 10f)] public float scaleMultiplier;
            public bool castShadows;
            // public bool isBillboard;
        }

        #region SerializeField & Settings

        [SerializeField] private ComputeShader computeShader;
        public Camera cam;
        [Header("GPU Driven Terrain Data")] public TerrainAsset terrainAsset;
        public Texture2D controlMap;
        public float heightOffset;
        public ShadowCastingMode shadowCastingMode=ShadowCastingMode.Off;

        [Header("Debug")] public bool visualizeTiles = true;

        [Header("Grid Setup (Clipmap Topology)")]
        public float baseTileSize = 10f;
        // public int baseTileResolution = 32; 

        [Header("Global Density & Culling")] [Range(0.0f, 1.0f)]
        public float globalDensity = 1.0f;

        [Range(0.0f, 2.0f)] public float jitterStrength = 0.8f;
        public float maxRenderDistance = 150f;
        public float distanceFadeLength = 20f;
        public float frustumCullNearOffset = 2f;
        public float frustumCullEdgeOffset = 2f;

        [Header("LOD Pipeline Setup")] public List<GrassLODSetting> lodSettings = new List<GrassLODSetting>();

        [Header("Clumping")] public int clumpTextureHeight = 256;
        public int clumpTextureWidth = 256;
        public Material clumpingVoronoiMaterial;
        public float clumpScale = 0.01f;
        public List<ClumpParameters> clumpParameters = new List<ClumpParameters>();

        [Header("Wind")] [SerializeField] private Texture2D localWindTex;
        [Range(0.0f, 1.0f)] [SerializeField] private float localWindStrength = 0.5f;
        [SerializeField] private float localWindScale = 0.01f;
        [SerializeField] private float localWindSpeed = 0.1f;
        [Range(0.0f, 1.0f)] [SerializeField] private float localWindRotateAmount = 0.3f;

        #endregion

        private Vector3 TerrainMinPosition =>
            terrainAsset != null ? new Vector3(-terrainAsset.worldSize.x * 0.5f, 0, -terrainAsset.worldSize.z * 0.5f) : Vector3.zero;

        #region Buffers

        private int LodCount => lodSettings.Count;

        private ComputeBuffer[] grassBladesBuffers;
        private ComputeBuffer[] argsBuffers;
        private ComputeBuffer[] meshTrianglesBuffers;
        private ComputeBuffer[] meshColorsBuffers;
        private ComputeBuffer[] meshUvsBuffers;
        private ComputeBuffer clumpParametersBuffer;
        private const int ARGS_STRIDE = sizeof(int) * 5;

        private ClumpParameters[] clumpParametersArray;
        private Texture2D clumpTexture;

        private List<Tile>[] tilesPerLOD;
#if UNITY_EDITOR
        private List<Tile> debugVisibleTiles = new List<Tile>();
#endif

        #endregion

        private struct Tile
        {
            public Bounds bounds;
            public int resolution;
            public float currentSize;
            public int lodLevel;
            public Vector2Int localId; // 在 4x4 矩阵中的局部拓扑坐标 (范围 -2 ~ 1)
            public Vector2Int globalId; // 映射到 LOD0 底层大网格上的绝对世界坐标

            public Tile(Bounds b, int res, float size, int level, Vector2Int loc, Vector2Int glob)
            {
                bounds = b;
                resolution = res;
                currentSize = size;
                lodLevel = level;
                localId = loc;
                globalId = glob;
            }
        }

        void Awake() => Initialize();

        void Update()
        {
            UpdateGrassTiles();
            UpdateGpuParameters();
        }

        void LateUpdate() => RenderGrass();

        void OnDestroy()
        {
            DisposeBuffers();
            DestroyClumpTexture();
        }

        private void Initialize()
        {
            if (terrainAsset == null || LodCount == 0) return;

            materialPropertyBlock = new MaterialPropertyBlock();
            CreateClumpTexture();
            InitializeComputeBuffers();
            SetupMeshBuffers();
        }

        private void InitializeComputeBuffers()
        {
            if (clumpParameters.Count == 0)
                clumpParameters.Add(new ClumpParameters { baseHeight = 1.0f, baseWidth = 0.2f });

            grassBladesBuffers = new ComputeBuffer[LodCount];
            argsBuffers = new ComputeBuffer[LodCount];
            meshTrianglesBuffers = new ComputeBuffer[LodCount];
            meshColorsBuffers = new ComputeBuffer[LodCount];
            meshUvsBuffers = new ComputeBuffer[LodCount];
            tilesPerLOD = new List<Tile>[LodCount];

            for (int i = 0; i < LodCount; i++)
            {
                // int currentResolution = Mathf.RoundToInt(baseTileResolution * Mathf.Pow(2, i));
                // 每一层级最多 4x4 = 16 个网格
                // int maxInstancesPerLOD = currentResolution * currentResolution * 16; 
                int res = lodSettings[i].resolution;
                int maxInstancesPerLOD = res * res * 16;

                grassBladesBuffers[i] = new ComputeBuffer(maxInstancesPerLOD, sizeof(float) * 14, ComputeBufferType.Append);
                argsBuffers[i] = new ComputeBuffer(1, ARGS_STRIDE, ComputeBufferType.IndirectArguments);
                tilesPerLOD[i] = new List<Tile>();
            }

            clumpParametersArray = new ClumpParameters[Mathf.Max(1, clumpParameters.Count)];
            clumpParametersBuffer = new ComputeBuffer(clumpParametersArray.Length, sizeof(float) * 10);
        }

        private void SetupMeshBuffers()
        {
            for (int i = 0; i < LodCount; i++)
            {
                Mesh m = lodSettings[i].mesh;
                if (m == null) continue;

                meshTrianglesBuffers[i] = new ComputeBuffer(m.triangles.Length, sizeof(int));
                meshTrianglesBuffers[i].SetData(m.triangles);

                meshColorsBuffers[i] = new ComputeBuffer(m.colors.Length, sizeof(float) * 4);
                meshColorsBuffers[i].SetData(m.colors);

                meshUvsBuffers[i] = new ComputeBuffer(m.uv.Length, sizeof(float) * 2);
                meshUvsBuffers[i].SetData(m.uv);

                argsBuffers[i].SetData(new int[] { meshTrianglesBuffers[i].count, 0, 0, 0, 0 });
            }
        }

        private void UpdateClumpParametersBuffer()
        {
            if (clumpParameters.Count == 0) return;

            if (clumpParametersArray.Length != clumpParameters.Count)
            {
                clumpParametersArray = new ClumpParameters[clumpParameters.Count];
                clumpParametersBuffer.Dispose();
                clumpParametersBuffer = new ComputeBuffer(clumpParameters.Count, sizeof(float) * 10);
            }

            clumpParameters.CopyTo(clumpParametersArray);
            clumpParametersBuffer.SetData(clumpParametersArray);
        }

        private void CreateClumpTexture()
        {
            clumpingVoronoiMaterial.SetFloat("_NumClumpTypes", clumpParameters.Count);
            RenderTexture rt =
                RenderTexture.GetTemporary(clumpTextureWidth, clumpTextureHeight, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            Graphics.Blit(null, rt, clumpingVoronoiMaterial, 0);

            RenderTexture.active = rt;
            clumpTexture = new Texture2D(clumpTextureWidth, clumpTextureHeight, TextureFormat.RGBAHalf, false, true);
            clumpTexture.filterMode = FilterMode.Point;
            clumpTexture.ReadPixels(new Rect(0, 0, clumpTextureWidth, clumpTextureHeight), 0, 0, true);
            clumpTexture.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
        }

        private void UpdateGrassTiles()
        {
#if UNITY_EDITOR
            debugVisibleTiles.Clear();
#endif
            if (terrainAsset == null || LodCount == 0) return;

            Vector3 camPosInTerrain = cam.transform.position - TerrainMinPosition;
            Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(cam);

            // 提前算出 LOD0 级别的绝对网格中心索引
            int centerGridX = Mathf.FloorToInt(camPosInTerrain.x / baseTileSize);
            int centerGridZ = Mathf.FloorToInt(camPosInTerrain.z / baseTileSize);

            Vector3 snappedCenter = new Vector3(
                centerGridX * baseTileSize,
                0,
                centerGridZ * baseTileSize
            );

            for (int level = 0; level < LodCount; level++)
            {
                tilesPerLOD[level].Clear();

                float currentTileSize = baseTileSize * Mathf.Pow(2, level);
                int currentResolution = lodSettings[level].resolution;

                // 计算当前 LOD 的尺寸倍数 (LOD0=1, LOD1=2, LOD2=4)
                int sizeMultiplier = (int)Mathf.Pow(2, level);

                for (int x = -2; x <= 1; x++)
                {
                    for (int z = -2; z <= 1; z++)
                    {
                        if (level > 0 && x >= -1 && x <= 0 && z >= -1 && z <= 0) continue;

                        // 【核心】：正向记录局部和全局坐标
                        Vector2Int localId = new Vector2Int(x, z);
                        Vector2Int globalId = new Vector2Int(
                            centerGridX + x * sizeMultiplier,
                            centerGridZ + z * sizeMultiplier
                        );

                        Vector3 min = TerrainMinPosition + snappedCenter + new Vector3(x * currentTileSize, -10f, z * currentTileSize);
                        Vector3 max = min + new Vector3(currentTileSize, terrainAsset.worldSize.y + 20f, currentTileSize);
                        Bounds tileBounds = new Bounds();
                        tileBounds.SetMinMax(min, max);

                        float distToTile = Mathf.Max(0, tileBounds.SqrDistance(cam.transform.position));
                        if (distToTile > maxRenderDistance * maxRenderDistance) continue;

                        if (GeometryUtility.TestPlanesAABB(frustumPlanes, tileBounds))
                        {
                            Tile t = new Tile(tileBounds, currentResolution, currentTileSize, level, localId, globalId);
                            tilesPerLOD[level].Add(t);
#if UNITY_EDITOR
                            debugVisibleTiles.Add(t);
#endif
                        }
                    }
                }
            }
        }

        private void UpdateGpuParameters()
        {
            if (terrainAsset == null || LodCount == 0) return;

            UpdateClumpParametersBuffer();

            computeShader.SetVector(worldSpaceCameraPositionID, cam.transform.position);
            Matrix4x4 vpMatrix = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false) * cam.worldToCameraMatrix;
            computeShader.SetMatrix(vpMatrixID, vpMatrix);
            computeShader.SetFloat(TimeID, Time.time);

            computeShader.SetTexture(0, heightMapID, terrainAsset.heightMap);
            if (controlMap != null) computeShader.SetTexture(0, detailMapID, controlMap);
            computeShader.SetVector(terrainSizeID, terrainAsset.worldSize);
            computeShader.SetFloat(heightOffsetID, heightOffset);
            computeShader.SetVector(terrainPositionID, TerrainMinPosition);

            computeShader.SetFloat(globalDensityID, globalDensity);
            computeShader.SetFloat(maxRenderDistanceID, maxRenderDistance);
            computeShader.SetFloat(distanceFadeLengthID, distanceFadeLength);

            computeShader.SetBuffer(0, clumpParametersID, clumpParametersBuffer);
            computeShader.SetTexture(0, clumpTexID, clumpTexture);
            computeShader.SetFloat(clumpScaleID, clumpScale);
            computeShader.SetInt(numClumpParametersID, clumpParameters.Count);

            computeShader.SetTexture(0, LocalWindTexID, localWindTex);
            computeShader.SetFloat(LocalWindScaleID, localWindScale);
            computeShader.SetFloat(LocalWindSpeedID, localWindSpeed);
            computeShader.SetFloat(LocalWindStrengthID, localWindStrength);
            computeShader.SetFloat(LocalWindRotateAmountID, localWindRotateAmount);

            for (int level = 0; level < LodCount; level++)
            {
                if (tilesPerLOD[level].Count == 0) continue;
                if (lodSettings[level].densityMultiplier <= 0.002f) continue; // 调度拦截

                grassBladesBuffers[level].SetCounterValue(0);
                computeShader.SetBuffer(0, grassBladesBufferID, grassBladesBuffers[level]);
                computeShader.SetFloat(currentLODDensityID, lodSettings[level].densityMultiplier);
                computeShader.SetFloat(currentLODScaleID, lodSettings[level].scaleMultiplier);

                // computeShader.SetFloat(isBillboardID, lodSettings[level].isBillboard ? 1.0f : 0.0f);

                foreach (Tile tile in tilesPerLOD[level])
                {
                    computeShader.SetFloat(tileSizeID, tile.currentSize);
                    computeShader.SetInt(resolutionXID, tile.resolution);
                    computeShader.SetInt(resolutionYID, tile.resolution);
                    computeShader.SetFloat(jitterStrengthID, jitterStrength);
                    computeShader.SetVector(tilePositionID, tile.bounds.min);
                    computeShader.SetFloat(frustumCullNearOffsetID, frustumCullNearOffset);
                    computeShader.SetFloat(frustumCullEdgeOffsetID, frustumCullEdgeOffset);

                    // 现在的调度数完全取决于美术设定的 Resolution，外圈 Dispatch 暴降！
                    int threadGroupsXY = Mathf.CeilToInt(tile.resolution / 8f);
                    computeShader.Dispatch(0, threadGroupsXY, threadGroupsXY, 1);
                    // _cmd.DispatchCompute(computeShader, 0, threadGroupsXY, threadGroupsXY, 1);
                }
            }
        }

        private void RenderGrass()
        {
            if (terrainAsset == null || LodCount == 0) return;
            Bounds bigBounds = new Bounds(TerrainMinPosition + terrainAsset.worldSize * 0.5f, terrainAsset.worldSize);

            for (int i = 0; i < LodCount; i++)
            {
                ShadowCastingMode lodShadowMode = (lodSettings[i].castShadows && shadowCastingMode == ShadowCastingMode.On) ? ShadowCastingMode.On : ShadowCastingMode.Off;
                
                if (meshTrianglesBuffers[i] == null || lodSettings[i].material == null) continue;
                if (lodSettings[i].densityMultiplier <= 0.001f) continue; // 绘制拦截

                ComputeBuffer.CopyCount(grassBladesBuffers[i], argsBuffers[i], sizeof(int));

                materialPropertyBlock.SetBuffer(grassBladesBufferID, grassBladesBuffers[i]);
                materialPropertyBlock.SetBuffer(materialTrianglesBufferID, meshTrianglesBuffers[i]);
                materialPropertyBlock.SetBuffer(materialColorsBufferID, meshColorsBuffers[i]);
                materialPropertyBlock.SetBuffer(materialUvsBufferID, meshUvsBuffers[i]);

                Graphics.DrawProceduralIndirect(lodSettings[i].material, bigBounds, MeshTopology.Triangles, argsBuffers[i],
                    0, null, materialPropertyBlock, lodShadowMode, true, gameObject.layer);
            }
        }

        private void DisposeBuffers()
        {
            if (grassBladesBuffers == null) return;
            for (int i = 0; i < grassBladesBuffers.Length; i++)
            {
                DisposeBuffer(grassBladesBuffers[i]);
                DisposeBuffer(argsBuffers[i]);
                DisposeBuffer(meshTrianglesBuffers[i]);
                DisposeBuffer(meshColorsBuffers[i]);
                DisposeBuffer(meshUvsBuffers[i]);
            }

            DisposeBuffer(clumpParametersBuffer);
        }

        private void DestroyClumpTexture()
        {
            if (clumpTexture != null)
            {
                #if UNITY_EDITOR
                DestroyImmediate(clumpTexture);
                #else
                Destroy(clumpTexture);
                #endif
                
                clumpTexture = null;
            }
        }

        private void DisposeBuffer(ComputeBuffer buffer)
        {
            if (buffer != null)
            {
                buffer.Dispose();
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!visualizeTiles || debugVisibleTiles == null || debugVisibleTiles.Count == 0) return;

            // 初始化文字样式
            GUIStyle labelStyle = new GUIStyle();
            labelStyle.normal.textColor = Color.white;
            labelStyle.fontSize = 10;
            labelStyle.fontStyle = FontStyle.Bold; // 加粗让文字在草地中更明显
            labelStyle.alignment = TextAnchor.MiddleCenter;

            foreach (Tile tile in debugVisibleTiles)
            {
                // 1. 画框
                float colorLerp = (float)tile.lodLevel / Mathf.Max(1, LodCount - 1);
                Gizmos.color = Color.Lerp(new Color(0, 0.8f, 0, 0.5f), new Color(0, 0, 0.8f, 0.5f), colorLerp);
                var center = tile.bounds.center;
                center.y = terrainAsset.worldSize.y * 0.5f;
                var size = tile.bounds.size;
                size.y = terrainAsset.worldSize.y * 0.8f;
                Gizmos.DrawWireCube(center, size);

                // 2. 渲染坐标文本
                // Loc: 验证 4x4 局部拓扑是否正确挖空
                // Glb: 验证世界绝对物理坐标是否跳变
                string labelText = $"LOD {tile.lodLevel}\nLoc:({tile.localId.x}, {tile.localId.y})\nGlb:({tile.globalId.x}, {tile.globalId.y})";

                Vector3 labelPos = center;
                labelPos.y += size.y * 0.5f + 1f; // 悬浮在方块顶部

                UnityEditor.Handles.Label(labelPos, labelText, labelStyle);
            }
        }
#endif

        #region IDs

        private static readonly int
            resolutionXID = Shader.PropertyToID("_ResolutionX"),
            resolutionYID = Shader.PropertyToID("_ResolutionY"),
            tileSizeID = Shader.PropertyToID("_TileSize"),
            globalDensityID = Shader.PropertyToID("_GlobalDensity"),
            currentLODDensityID = Shader.PropertyToID("_CurrentLODDensity"),
            currentLODScaleID = Shader.PropertyToID("_CurrentLODScale"),
            // isBillboardID = Shader.PropertyToID("_IsBillboard"),
            maxRenderDistanceID = Shader.PropertyToID("_MaxRenderDistance"),
            distanceFadeLengthID = Shader.PropertyToID("_DistanceFadeLength"),
            jitterStrengthID = Shader.PropertyToID("_JitterStrength"),
            heightMapID = Shader.PropertyToID("_HeightMap"),
            detailMapID = Shader.PropertyToID("_DetailMap"),
            terrainPositionID = Shader.PropertyToID("_TerrainPosition"),
            tilePositionID = Shader.PropertyToID("_TilePosition"),
            terrainSizeID = Shader.PropertyToID("_TerrainSize"),
            heightOffsetID = Shader.PropertyToID("_HeightOffset"),
            worldSpaceCameraPositionID = Shader.PropertyToID("_CameraPosWS"),
            vpMatrixID = Shader.PropertyToID("_VP_MATRIX"),
            frustumCullNearOffsetID = Shader.PropertyToID("_FrustumCullNearOffset"),
            frustumCullEdgeOffsetID = Shader.PropertyToID("_FrustumCullEdgeOffset"),
            clumpParametersID = Shader.PropertyToID("_ClumpParameters"),
            numClumpParametersID = Shader.PropertyToID("_NumClumpParameters"),
            clumpTexID = Shader.PropertyToID("ClumpTex"),
            clumpScaleID = Shader.PropertyToID("_ClumpScale"),
            LocalWindTexID = Shader.PropertyToID("_LocalWindTex"),
            LocalWindScaleID = Shader.PropertyToID("_LocalWindScale"),
            LocalWindSpeedID = Shader.PropertyToID("_LocalWindSpeed"),
            LocalWindStrengthID = Shader.PropertyToID("_LocalWindStrength"),
            LocalWindRotateAmountID = Shader.PropertyToID("_LocalWindRotateAmount"),
            TimeID = Shader.PropertyToID("_Time"),
            grassBladesBufferID = Shader.PropertyToID("_GrassBlades"),
            materialTrianglesBufferID = Shader.PropertyToID("Triangles"),
            materialColorsBufferID = Shader.PropertyToID("Colors"),
            materialUvsBufferID = Shader.PropertyToID("Uvs");

        private MaterialPropertyBlock materialPropertyBlock;

        #endregion
    }
}