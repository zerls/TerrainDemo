using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;
// using Zerls.GrassSystem;


namespace TerrainRendering
{
    [ExecuteInEditMode]
    public class TerrainManager : MonoBehaviour, System.IDisposable
    {
        [Header("Assets & References")] public TerrainAsset terrainAsset;
        public Material terrainMaterial;

        [Header("Terrain Settings")] [Range(-10.0f, 10.0f)]
        public float HeightOffset;

        public bool seamLess = true;

        [Header("Culling Settings")] public bool isFrustumCullEnabled = true;
        public bool isHizOcclusionCullingEnabled = true;
        [Range(0.01f, 1000f)] public float hizDepthBias = 1f;
        [Range(0, 100)] public int boundsHeightRedundance = 5;

        [Header("LOD Settings")] [Range(0.1f, 1.9f)]
        public float distanceEvaluation = 1.2f;

        [Header("Debug View")] 
        public bool mipDebug = false;

        // ====== 内部变量 ======
        private Camera _camera;
        private CommandBuffer _cmd;
        private ComputeShader _computeShader;
        private RenderTexture _lodMap;

        // ====== Compute Buffers ======
        private ComputeBuffer _patchIndirectArgsBuffer;
        private ComputeBuffer _indirectArgsBuffer;
        private ComputeBuffer _topLevelNodeBuffer;
        private ComputeBuffer _tempANodeListBuffer;
        private ComputeBuffer _tempBNodeListBuffer;
        private ComputeBuffer _finalNodeListBuffer;
        private ComputeBuffer _visiblePatchBuffer;
        private ComputeBuffer _nodeDescriptors;

        // ====== Kernel IDs ======
        private int _traverseQuadTreeKernel;
        private int _buildLodMapKernel;
        private int _cullPatchesKernel;

        // ====== 视锥剔除缓存 ======
        private Plane[] _cameraFrustumPlanes = new Plane[6];
        private Vector4[] _cameraFrustumPlanesV4 = new Vector4[6];

        // ====== 常量与魔法数字提取 ======
        private const int MAX_NODE_BUFFER_SIZE = 200;
        private const int TEMP_NODE_BUFFER_SIZE = 50;
        private const int PATCH_STRIP_SIZE = 6 * 4; // PatchDescriptor 结构体字节大小
        private const int PATCH_PER_NODE = 64; // 8x8 = 64 patches per node
        
        private static readonly ProfilingSampler s_TraverseQuadTreeSampler = new ProfilingSampler("Terrain: TraverseQuadTree");
        private static readonly ProfilingSampler s_GenerateLodMapSampler = new ProfilingSampler("Terrain: Generate LodMap");
        private static readonly ProfilingSampler s_GeneratePatchesSampler = new ProfilingSampler("Terrain: Generate Patches");


        private void Start()
        {
            if (!ValidateResources()) return;

            _computeShader = terrainAsset.computeShader;
            _cmd = new CommandBuffer { name = "GPUDrivenTerrainDatas" };
            _camera = Camera.main;

            InitKernels();
            InitBuffers();
            InitWorldParams();
            
        }

        private bool ValidateResources()
        {
            if (terrainAsset == null || terrainAsset.computeShader == null || terrainMaterial == null)
            {
                Debug.LogError("[TerrainManager] 缺少必要的 TerrainAsset, ComputeShader 或 Material，初始化失败！");
                return false;
            }

            return true;
        }

        private void InitKernels()
        {
            _traverseQuadTreeKernel = _computeShader.FindKernel("TraverseQuadTree");
            _buildLodMapKernel = _computeShader.FindKernel("BuildLodMap");
            _cullPatchesKernel = _computeShader.FindKernel("CullPatches");
        }

        private void InitBuffers()
        {
            // 1. 初始化可见 Patch Buffer (Append Buffer)
            _visiblePatchBuffer = new ComputeBuffer(MAX_NODE_BUFFER_SIZE * PATCH_PER_NODE, PATCH_STRIP_SIZE, ComputeBufferType.Append);

            // 2. 初始化绘制参数 Buffer [IndexCount, InstanceCount, StartIndex, BaseVertex, StartInstance]
            _patchIndirectArgsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
            _patchIndirectArgsBuffer.SetData(new uint[] { TerrainAsset.patchMesh.GetIndexCount(0), 0, 0, 0, 0 });

            // 3. Compute Shader 的间接调度参数 Buffer [ThreadGroupX, Y, Z]
            _indirectArgsBuffer = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments);
            _indirectArgsBuffer.SetData(new uint[] { 1, 1, 1 });

            // 4. 四叉树节点相关的 Buffer
            _topLevelNodeBuffer = new ComputeBuffer(TerrainAsset.MAX_LOD_NODE_COUNT * TerrainAsset.MAX_LOD_NODE_COUNT, 8, ComputeBufferType.Append);
            InitTopLevelNodeListDatas();

            _tempANodeListBuffer = new ComputeBuffer(TEMP_NODE_BUFFER_SIZE, 8, ComputeBufferType.Append);
            _tempBNodeListBuffer = new ComputeBuffer(TEMP_NODE_BUFFER_SIZE, 8, ComputeBufferType.Append);
            _finalNodeListBuffer = new ComputeBuffer(MAX_NODE_BUFFER_SIZE, 12, ComputeBufferType.Append);
            _nodeDescriptors = new ComputeBuffer((int)(TerrainAsset.MAX_NODE_ID + 1), 4);

            _lodMap = TerrainHelper.CreateLODMap(160);

            // 5. 绑定静态资源到对应的 Kernel
            BindComputeShaderResources();
        }

        private void BindComputeShaderResources()
        {
            // Traverse Quad Tree
            _computeShader.SetBuffer(_traverseQuadTreeKernel, ShaderIDs.AppendFinalNodeList, _finalNodeListBuffer);
            _computeShader.SetBuffer(_traverseQuadTreeKernel, ShaderIDs.NodeDescriptors, _nodeDescriptors);
            _computeShader.SetTexture(_traverseQuadTreeKernel, ShaderIDs.MinMaxHeightTexture, terrainAsset.minMaxHeightMap);

            // Build LOD Map
            _computeShader.SetBuffer(_buildLodMapKernel, ShaderIDs.NodeDescriptors, _nodeDescriptors);
            _computeShader.SetTexture(_buildLodMapKernel, ShaderIDs.LodMap, _lodMap);

            // Cull Patches
            _computeShader.SetBuffer(_cullPatchesKernel, ShaderIDs.FinalNodeList, _finalNodeListBuffer);
            _computeShader.SetBuffer(_cullPatchesKernel, ShaderIDs.VisiblePatches, _visiblePatchBuffer);
            _computeShader.SetTexture(_cullPatchesKernel, ShaderIDs.LodMap, _lodMap);
            _computeShader.SetTexture(_cullPatchesKernel, ShaderIDs.MinMaxHeightTexture, terrainAsset.minMaxHeightMap);

            // 绑定给材质供渲染读取
            terrainMaterial.SetBuffer(ShaderIDs.VisiblePatchList, _visiblePatchBuffer);
        }

        private void InitTopLevelNodeListDatas()
        {
            var maxLODNodeCount = TerrainAsset.MAX_LOD_NODE_COUNT;
            uint2[] datas = new uint2[maxLODNodeCount * maxLODNodeCount];
            var index = 0;
            for (uint i = 0; i < maxLODNodeCount; i++)
            {
                for (uint j = 0; j < maxLODNodeCount; j++)
                {
                    datas[index++] = new uint2(i, j);
                }
            }

            _topLevelNodeBuffer.SetData(datas);
        }

        private void InitWorldParams()
        {
            float wSize = terrainAsset.worldSize.x;
            int nodeCount = TerrainAsset.MAX_LOD_NODE_COUNT;
            Vector4[] worldLODParams = new Vector4[TerrainAsset.MAX_LOD + 1];

            for (var lod = TerrainAsset.MAX_LOD; lod >= 0; lod--)
            {
                var nodeSize = wSize / nodeCount;
                var patchExtent = nodeSize / 16;
                var sectorCountPerNode = (int)Mathf.Pow(2, lod);
                worldLODParams[lod] = new Vector4(nodeSize, patchExtent, nodeCount, sectorCountPerNode);
                nodeCount *= 2;
            }

            _computeShader.SetVectorArray(ShaderIDs.WorldLodParams, worldLODParams);

            int[] nodeIDOffsetLOD = new int[(TerrainAsset.MAX_LOD + 1) * 4];
            int nodeIdOffset = 0;
            for (int lod = TerrainAsset.MAX_LOD; lod >= 0; lod--)
            {
                nodeIDOffsetLOD[lod * 4] = nodeIdOffset;
                nodeIdOffset += (int)(worldLODParams[lod].z * worldLODParams[lod].z);
            }

            _computeShader.SetInts(ShaderIDs.NodeIDOffsetOfLOD, nodeIDOffsetLOD);
        }

  

        private void Update()
        {
            UpdateRenderParameter(_camera);
            UpdateCameraFrustumPlanes(_camera);

            _cmd.Clear();
            ClearBufferCounter();

            // ==========================================
            // Pass 1: Traverse Quad Tree (Ping-Pong 划分节点)
            // ==========================================
            using (new ProfilingScope(_cmd, s_TraverseQuadTreeSampler))
            {
                _cmd.CopyCounterValue(_topLevelNodeBuffer, _indirectArgsBuffer, 0);

                var consumeNodeList = _tempANodeListBuffer;
                var produceNodeList = _tempBNodeListBuffer;

                for (var lod = TerrainAsset.MAX_LOD; lod >= 0; lod--)
                {
                    _cmd.SetComputeIntParam(_computeShader, ShaderIDs.PassLOD, lod);

                    ComputeBuffer sourceBuffer = (lod == TerrainAsset.MAX_LOD) ? _topLevelNodeBuffer : consumeNodeList;
                    _cmd.SetComputeBufferParam(_computeShader, _traverseQuadTreeKernel, ShaderIDs.ConsumeNodeList, sourceBuffer);
                    _cmd.SetComputeBufferParam(_computeShader, _traverseQuadTreeKernel, ShaderIDs.ProduceNodeList, produceNodeList);

                    _cmd.DispatchCompute(_computeShader, _traverseQuadTreeKernel, _indirectArgsBuffer, 0);
                    _cmd.CopyCounterValue(produceNodeList, _indirectArgsBuffer, 0);

                    // Ping-Pong 交换 Buffer
                    (consumeNodeList, produceNodeList) = (produceNodeList, consumeNodeList);
                }
            }

            // ==========================================
            // Pass 2: Build LOD Map (处理接缝)
            // ==========================================
            using (new ProfilingScope(_cmd, s_GenerateLodMapSampler))
            {
                _cmd.DispatchCompute(_computeShader, _buildLodMapKernel, 20, 20, 1);
            }

            // ==========================================
            // Pass 3: Cull And Generator Patches (剔除与生成绘制数据)
            // ==========================================
            using (new ProfilingScope(_cmd, s_GeneratePatchesSampler))
            {
                _cmd.CopyCounterValue(_finalNodeListBuffer, _indirectArgsBuffer, 0);
                _cmd.DispatchCompute(_computeShader, _cullPatchesKernel, _indirectArgsBuffer, 0);

                // 将存活下来的 Patch 数量拷贝到 DrawInstancedIndirect 的 InstanceCount 位置
                _cmd.CopyCounterValue(_visiblePatchBuffer, _patchIndirectArgsBuffer, 4);
            }

            Graphics.ExecuteCommandBuffer(_cmd);

            // ==========================================
            // Pass 4: 间接绘制 (GPU Drivern 核心)
            // ==========================================
            Bounds hugeBounds = new Bounds(Vector3.zero, Vector3.one * 10240);
            Graphics.DrawMeshInstancedIndirect(TerrainAsset.patchMesh, 0, terrainMaterial, hugeBounds, _patchIndirectArgsBuffer);

        }

        private void UpdateCameraFrustumPlanes(Camera camera)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, _cameraFrustumPlanes);
            for (var i = 0; i < _cameraFrustumPlanes.Length; i++)
            {
                Vector4 v4 = (Vector4)_cameraFrustumPlanes[i].normal;
                v4.w = _cameraFrustumPlanes[i].distance;
                _cameraFrustumPlanesV4[i] = v4;
            }

            _computeShader.SetVectorArray(ShaderIDs.CameraFrustumPlanes, _cameraFrustumPlanesV4);
        }

        private void ClearBufferCounter()
        {
            _cmd.SetBufferCounterValue(_topLevelNodeBuffer, (uint)_topLevelNodeBuffer.count);
            _cmd.SetBufferCounterValue(_visiblePatchBuffer, 0);
            _cmd.SetBufferCounterValue(_tempANodeListBuffer, 0);
            _cmd.SetBufferCounterValue(_tempBNodeListBuffer, 0);
            _cmd.SetBufferCounterValue(_finalNodeListBuffer, 0);
        }

        private void UpdateRenderParameter(Camera camera)
        {
            // 材质参数同步
            terrainMaterial.SetVector(ShaderIDs.TerrainSize, terrainAsset.worldSize);
            terrainMaterial.SetMatrix(ShaderIDs.WorldToNormalMapMatrix, Matrix4x4.Scale(terrainAsset.worldSize).inverse);
            terrainMaterial.SetFloat(ShaderIDs.TerrainHeightOffset, HeightOffset);
            CoreUtils.SetKeyword(terrainMaterial, "_LOD_SEAMLESS", seamLess);
            CoreUtils.SetKeyword(terrainMaterial, "_MIP_DEBUG", mipDebug);
            // CoreUtils.SetKeyword(terrainMaterial, "_PATCH_DEBUG", patchDebug);
            // CoreUtils.SetKeyword(terrainMaterial, "_NODE_DEBUG", nodeDebug);

            // CS参数同步
            CoreUtils.SetKeyword(_computeShader, "_FRUSTUM_CULL", isFrustumCullEnabled);
            CoreUtils.SetKeyword(_computeShader, "_HIZ_CULL", isHizOcclusionCullingEnabled);
            CoreUtils.SetKeyword(_computeShader, "_ENABLE_SEAM", seamLess);
            CoreUtils.SetKeyword(_computeShader, "_REVERSE_Z", SystemInfo.usesReversedZBuffer);

            _computeShader.SetVector(ShaderIDs.NodeEvaluationC, new Vector4(distanceEvaluation, 0, 0, 0));
            _computeShader.SetFloat(ShaderIDs.HizDepthBias, Mathf.Clamp(hizDepthBias, 0.01f, 1000f));
            _computeShader.SetInt(ShaderIDs.BoundsHeightRedundance, boundsHeightRedundance);
            _computeShader.SetVector(ShaderIDs.CameraPositionWS, camera.transform.position);
            _computeShader.SetVector(ShaderIDs.TerrainSize, terrainAsset.worldSize);
            _computeShader.SetFloats(ShaderIDs.TerrainHeightOffset, HeightOffset);
        }


        public void Dispose()
        {
            _topLevelNodeBuffer?.Dispose();
            _tempANodeListBuffer?.Dispose();
            _tempBNodeListBuffer?.Dispose();
            _finalNodeListBuffer?.Dispose();
            _visiblePatchBuffer?.Dispose();
            _patchIndirectArgsBuffer?.Dispose();
            _indirectArgsBuffer?.Dispose();
            _nodeDescriptors?.Dispose();

            if (_lodMap != null)
            {
                _lodMap?.Release();
                #if  UNITY_EDITOR
                DestroyImmediate(_lodMap);
                #else
                Destroy(_lodMap);
                #endif
                
            }
        }

        private void OnDestroy()
        {
            Dispose();
        }

        /// <summary>
        /// 缓存所有的 Shader 属性 ID，消除 Update 中的字符串 Hash 开销
        /// </summary>
        private static class ShaderIDs
        {
            public static readonly int AppendFinalNodeList = Shader.PropertyToID("AppendFinalNodeList");
            public static readonly int NodeDescriptors = Shader.PropertyToID("NodeDescriptors");
            public static readonly int MinMaxHeightTexture = Shader.PropertyToID("MinMaxHeightTexture");
            // public static readonly int QuadTreeTexture = Shader.PropertyToID("QuadTreeTexture");
            public static readonly int LodMap = Shader.PropertyToID("_LodMap");
            public static readonly int FinalNodeList = Shader.PropertyToID("FinalNodeList");
            public static readonly int VisiblePatches = Shader.PropertyToID("VisiblePatches");
            public static readonly int VisiblePatchList = Shader.PropertyToID("_VisiblePatchList");
            public static readonly int WorldLodParams = Shader.PropertyToID("WorldLodParams");
            public static readonly int NodeIDOffsetOfLOD = Shader.PropertyToID("NodeIDOffsetOfLOD");
            public static readonly int CameraFrustumPlanes = Shader.PropertyToID("_CameraFrustumPlanes");
            public static readonly int NodeEvaluationC = Shader.PropertyToID("_NodeEvaluationC");
            public static readonly int HizDepthBias = Shader.PropertyToID("_HizDepthBias");
            public static readonly int BoundsHeightRedundance = Shader.PropertyToID("_BoundsHeightRedundance");
            public static readonly int CameraPositionWS = Shader.PropertyToID("_CameraPosWS");
            public static readonly int TerrainSize = Shader.PropertyToID("_TerrainSize");
            public static readonly int WorldToNormalMapMatrix = Shader.PropertyToID("_WorldToNormalMapMatrix");
            public static readonly int PassLOD = Shader.PropertyToID("_PassLOD");
            public static readonly int ConsumeNodeList = Shader.PropertyToID("ConsumeNodeList");
            public static readonly int ProduceNodeList = Shader.PropertyToID("ProduceNodeList");

            public static readonly int TerrainHeightOffset = Shader.PropertyToID("_HeightOffset");
            public static readonly int s_HizMapID = Shader.PropertyToID("_HizMap");
        }
    }
}