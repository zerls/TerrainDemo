using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Pool;

namespace WindSystem
{
    [ExecuteInEditMode]
    public class WindManager : MonoBehaviour
    {
        #region Settings
        public enum WindAdvection
        {
            Scatter,
            MacCormack,
        }

        [Header("Basic Setting")] 
        public Transform TargetTransform;
        public Vector3 CameraCenterOffset;
        [Tooltip("风场尺寸")]
        public Vector3Int m_WindBrand = new Vector3Int(32, 16, 32);
        public ComputeShader WindCS;
        public WindMotor WindMotorPrefab;
        public bool CPUWindUseGlobalWind = true;
        
        [Header("Wind Fluid Parameters")]
        public WindAdvection WindAdvectionMethod = WindAdvection.Scatter;
        [Range(0, 0.16f)] public float DiffusionForce = 0.5f;
        [Range(0, 0.6f)] public float AdvectionForce = 0.5f;
        public bool UseVorticity = true;
        [Range(0, 0.4f)] public float VorticityScale = 0.03f;
        public float MaxWindSpeed = 14f;
        public float OverallPower = 0.1f;
        
        [Header("Debug View")]
        public bool isDebug = true;
        public bool showUI = true;
        [Range(1.0f, 10.0f)] public float DebugViewScale = 4.0f; // 默认放大4倍方便观察

        private RenderTexture m_WindDebugTexture2D;
        private int K_DebugFlatten;
        private int m_DebugCols, m_DebugRows;
        private readonly int s_WindDebugTexture2DID = Shader.PropertyToID("WindDebugTexture2D");

        #endregion
        //==============================================================

        #region Property
        
        private static WindManager m_Instance;
        public static WindManager Instance => m_Instance;
        
        // --- 核心流体双缓冲 ---
        private RenderTexture[] m_WindVelocityBuffers = new RenderTexture[2];
        private RenderTexture m_IntermediateBuffer;
        private int m_CurrentIndex = 0;

        private RenderTexture CurrentInput => m_WindVelocityBuffers[m_CurrentIndex];
        private RenderTexture CurrentOutput => m_WindVelocityBuffers[1 - m_CurrentIndex];

        // --- 风源数据池 ---
        private UnityEngine.Pool.ObjectPool<WindMotor> m_WindPool;
        private const int MAXMOTOR = 32;
        private MotorDirectional[] m_DirMotorArray = new MotorDirectional[MAXMOTOR];
        private MotorOmni[] m_OmniMotorArray = new MotorOmni[MAXMOTOR];
        private MotorVortex[] m_VortexMotorArray = new MotorVortex[MAXMOTOR];
        private MotorMoving[] m_MovingMotorArray = new MotorMoving[MAXMOTOR];
        public List<WindMotor> m_MotorList = new List<WindMotor>();

        private ComputeBuffer m_DirMotorBuffer, m_OmniMotorBuffer, m_VortexMotorBuffer, m_MovingMotorBuffer, m_WindAtomicBuffer;
        private ComputeBuffer m_WindDataForCPUBuffer;
        private Vector3[] m_WindDataForCPU;
        private bool m_IsReadbackPending = false;

        // --- 内核索引与缓存参数 ---
        private int K_Shift, K_Motor, K_Diffusion, K_AdvectionFwd, K_AdvectionCorr, K_AdvectionRev, K_Merge, K_MergeAndClear, K_AdvectionFwd_MacCormack, K_CalcVorticity, K_ApplyVorticity;
        private Vector3 m_VolumeSize, m_VolumeSizeMinusOne, m_OffsetPos, m_LastOffsetPosInt;
        
        
        
        
        #endregion

        private void Update()
        {
            // 如果在非 URP 流程中（如 Edit 模式预览），则自动生成临时 CommandBuffer 运行
            if (Application.isPlaying) return; 
            DoRenderWindVolume(); 
        }
        
        private void InitSystem()
        {
            ReleaseResources();
            Shader.EnableKeyword("_WIND_SIMULATION");
            
            m_VolumeSize = new Vector3(m_WindBrand.x, m_WindBrand.y, m_WindBrand.z);
            m_VolumeSizeMinusOne = new Vector3(m_WindBrand.x - 1, m_WindBrand.y - 1, m_WindBrand.z - 1);

            RenderTextureDescriptor desc = new RenderTextureDescriptor(m_WindBrand.x, m_WindBrand.y, RenderTextureFormat.ARGBHalf, 0)
            {
                dimension = TextureDimension.Tex3D, volumeDepth = m_WindBrand.z, enableRandomWrite = true, sRGB = false
            };

            for (int i = 0; i < 2; i++)
            {
                m_WindVelocityBuffers[i] = new RenderTexture(desc) { name = $"WindVelocity_RT_{i}" };
                m_WindVelocityBuffers[i].Create();
            }

            m_IntermediateBuffer = new RenderTexture(desc) { name = "WindIntermediate_RT" };
            m_IntermediateBuffer.Create();

            int total = m_WindBrand.x * m_WindBrand.y * m_WindBrand.z;
            m_WindDataForCPU = new Vector3[total];
            m_WindDataForCPUBuffer = new ComputeBuffer(total, sizeof(float) * 3);
            m_WindAtomicBuffer = new ComputeBuffer(total * 3, sizeof(int));

            m_DirMotorBuffer = new ComputeBuffer(MAXMOTOR, 28);
            m_OmniMotorBuffer = new ComputeBuffer(MAXMOTOR, 20);
            m_VortexMotorBuffer = new ComputeBuffer(MAXMOTOR, 32);
            m_MovingMotorBuffer = new ComputeBuffer(MAXMOTOR, 36);

            K_Shift = WindCS.FindKernel("CSWindShiftPosition");
            K_Motor = WindCS.FindKernel("CSWindMotorVelocity");
            K_Diffusion = WindCS.FindKernel("CSWindDiffusion");
            K_AdvectionFwd = WindCS.FindKernel("CSWindAdvectionForward");
            K_AdvectionFwd_MacCormack = WindCS.FindKernel("CSWindAdvectionForward_MacCormack");
            K_AdvectionCorr = WindCS.FindKernel("CSWindAdvectionCorrection");
            K_AdvectionRev = WindCS.FindKernel("CSWindAdvectionReverse");
            K_Merge = WindCS.FindKernel("CSWindMerge");
            K_MergeAndClear = WindCS.FindKernel("CSWindMergeAndClear");
            K_CalcVorticity = WindCS.FindKernel("CSWindCalculateVorticity");
            K_ApplyVorticity = WindCS.FindKernel("CSWindApplyVorticity");

            if (TargetTransform != null)
            {
                UpdateTargetPosition();
                m_LastOffsetPosInt = GetTargetPosInt();
            }

            InitWindPool();
            BindStaticProperties();
            
            K_DebugFlatten = WindCS.FindKernel("CSWindDebugFlatten");

            // 强制平铺为 1 到 2 行。如果切片数 <= 8 就是 1 行，> 8 就是 2 行
            m_DebugRows = m_WindBrand.y / 8 ;
            m_DebugCols = Mathf.CeilToInt((float)m_WindBrand.y / m_DebugRows);

            int debugTexWidth = m_WindBrand.x * m_DebugCols;
            int debugTexHeight = m_WindBrand.z * m_DebugRows;

            if (m_WindDebugTexture2D != null) m_WindDebugTexture2D.Release();
            m_WindDebugTexture2D = new RenderTexture(debugTexWidth, debugTexHeight, 0, RenderTextureFormat.ARGB32)
            {
                name = "WindDebug2D_RT",
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            m_WindDebugTexture2D.Create();
            
        }

        private void BindStaticProperties()
        {
            WindCS.SetVector(s_VolumeSizeID, m_VolumeSize);
            WindCS.SetVector(s_VolumeSizeMinusOneID, m_VolumeSizeMinusOne);

            WindCS.SetBuffer(K_AdvectionFwd, s_WindAtomicBufferID, m_WindAtomicBuffer);
            WindCS.SetBuffer(K_Merge, s_WindAtomicBufferID, m_WindAtomicBuffer);
            WindCS.SetTexture(K_Merge, s_WindBufferOutputID, m_IntermediateBuffer);
            WindCS.SetTexture(K_AdvectionRev, s_WindBufferIntermediateID, m_IntermediateBuffer);
            WindCS.SetBuffer(K_AdvectionRev, s_WindAtomicBufferID, m_WindAtomicBuffer);
            WindCS.SetBuffer(K_MergeAndClear, s_WindAtomicBufferID, m_WindAtomicBuffer);
            WindCS.SetBuffer(K_MergeAndClear, s_WindDataForCPUBufferID, m_WindDataForCPUBuffer);
        }

        private void InitWindPool()
        {
            m_WindPool = new UnityEngine.Pool.ObjectPool<WindMotor>(
                createFunc: () => Instantiate(WindMotorPrefab, transform).gameObject.GetComponent<WindMotor>(),
                actionOnGet: motor => motor.gameObject.SetActive(true),
                actionOnRelease: motor => motor.gameObject.SetActive(false),
                actionOnDestroy: motor => { if (motor != null) DestroyImmediate(motor.gameObject); },
                collectionCheck: false, defaultCapacity: 10, maxSize: MAXMOTOR
            );
        }

        public WindMotor SpawnWindMotor(Vector3 position, MotorType type)
        {
            if (WindMotorPrefab == null) return null;
            WindMotor motor = m_WindPool.Get();
            motor.transform.position = position;
            motor.MotorType = type;
            return motor;
        }

        // =================渲染函数==============================
        public void DoRenderWindVolume(CommandBuffer externalCmd = null)
        {
            if (TargetTransform == null || WindCS == null) return;
            
            bool isExternalCmd = externalCmd != null;
            CommandBuffer cmd = isExternalCmd ? externalCmd : CommandBufferPool.Get();
            
            using (new ProfilingScope(cmd, new ProfilingSampler("Wind Simulation System")))
            {
                UpdateGPUParameter(cmd);

                if (DoShift(cmd, CurrentInput, CurrentOutput)) SwapBuffers();
                
                DoRenderWindVelocity(cmd, CurrentInput); 
                
                DoDiffusion(cmd, CurrentInput, CurrentOutput); 
                SwapBuffers(); 
                
                if (VorticityScale > 0.001f && UseVorticity)
                {
                    DoVorticity(cmd, CurrentInput, CurrentOutput);
                    SwapBuffers();
                }
                
                if (WindAdvectionMethod == WindAdvection.Scatter)
                    DoAdvection(cmd, CurrentInput, CurrentOutput);
                else
                    DoAdvection_MacCormack(cmd, CurrentInput, CurrentOutput);
                SwapBuffers(); 

                // 全局变量绑定写入 Cmd
                cmd.SetGlobalTexture(s_GlobalWindVelocityMapID, CurrentInput);
                cmd.SetGlobalVector(s_GlobalWindVolumeOffsetID, m_OffsetPos);
            }

            // 如果没有外部传入 cmd（如在 Edit 模式下），则手动执行
            if (!isExternalCmd)
            {
                Graphics.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
            
            
            //------------------Debug----------------
            if (isDebug && showUI && m_WindDebugTexture2D != null)
            {
                cmd.SetComputeIntParam(WindCS, "DebugGridCols", m_DebugCols);
                cmd.SetComputeTextureParam(WindCS, K_DebugFlatten, s_WindBufferInputID, CurrentInput);
                cmd.SetComputeTextureParam(WindCS, K_DebugFlatten, s_WindDebugTexture2DID, m_WindDebugTexture2D);
    
                int threadX = Mathf.CeilToInt(m_WindDebugTexture2D.width / 8f);
                int threadY = Mathf.CeilToInt(m_WindDebugTexture2D.height / 8f);
                cmd.DispatchCompute(WindCS, K_DebugFlatten, threadX, threadY, 1);
            }
        }

        private void UpdateGPUParameter(CommandBuffer cmd)
        {
            UpdateTargetPosition();
            //Compute Shader
            cmd.SetComputeFloatParam(WindCS, s_DiffusionForceID, DiffusionForce);
            cmd.SetComputeFloatParam(WindCS, s_AdvectionForceID, AdvectionForce);
            cmd.SetComputeVectorParam(WindCS, s_VolumeSizeMinusOneID, m_VolumeSizeMinusOne);
            cmd.SetComputeVectorParam(WindCS, s_VolumeSizeID, m_VolumeSize);
            cmd.SetComputeFloatParam(WindCS, s_MaxWindSpeedID, MaxWindSpeed);
            cmd.SetComputeFloatParam(WindCS, s_VorticityScaleID, VorticityScale);
            
            //Global Shader
            cmd.SetGlobalFloat(s_OverallPowerId, OverallPower);
            cmd.SetGlobalVector(s_VolumeSizeID, m_VolumeSize);
            cmd.SetGlobalVector(s_VolumeSizeMinusOneID, m_VolumeSizeMinusOne);
            cmd.SetGlobalVector( s_VolumePosOffsetID, m_OffsetPos);
            
        }

        private bool DoShift(CommandBuffer cmd, RenderTexture src, RenderTexture dst)
        {
            Vector3 currentPosInt = GetTargetPosInt();
            Vector3 shift = currentPosInt - m_LastOffsetPosInt;

            if (shift != Vector3.zero)
            {
                cmd.SetComputeVectorParam(WindCS, s_ShiftOffsetID, shift);
                cmd.SetComputeTextureParam(WindCS, K_Shift, s_WindBufferInputID, src);
                cmd.SetComputeTextureParam(WindCS, K_Shift, s_WindBufferOutputID, dst);
                Dispatch(cmd, K_Shift);
                m_LastOffsetPosInt = currentPosInt;
                return true;
            }
            return false;
        }

        private void DoRenderWindVelocity(CommandBuffer cmd, RenderTexture rt)
        {
            int d = 0, o = 0, v = 0, m = 0;
            float currentTime = Time.time;

            for (int i = m_MotorList.Count - 1; i >= 0; i--)
            {
                var motor = m_MotorList[i];
                if (!motor.enabled) continue;
                if (!motor.Tick(currentTime)) { m_WindPool.Release(motor); continue; }

                switch (motor.MotorType)
                {
                    case MotorType.Directional: if (d < MAXMOTOR) m_DirMotorArray[d++] = motor.motorDirectional; break;
                    case MotorType.Omni: if (o < MAXMOTOR) m_OmniMotorArray[o++] = motor.motorOmni; break;
                    case MotorType.Vortex: if (v < MAXMOTOR) m_VortexMotorArray[v++] = motor.motorVortex; break;
                    case MotorType.Moving: if (m < MAXMOTOR) m_MovingMotorArray[m++] = motor.motorMoving; break;
                }
            }

            for (int i = d; i < MAXMOTOR; i++) m_DirMotorArray[i] = WindMotor.GetEmptyMotorDirectional();
            for (int i = o; i < MAXMOTOR; i++) m_OmniMotorArray[i] = WindMotor.GetEmptyMotorOmni();
            for (int i = v; i < MAXMOTOR; i++) m_VortexMotorArray[i] = WindMotor.GetEmptyMotorVortex();
            for (int i = m; i < MAXMOTOR; i++) m_MovingMotorArray[i] = WindMotor.GetEmptyMotorMoving();

            m_DirMotorBuffer.SetData(m_DirMotorArray);
            m_OmniMotorBuffer.SetData(m_OmniMotorArray);
            m_VortexMotorBuffer.SetData(m_VortexMotorArray);
            m_MovingMotorBuffer.SetData(m_MovingMotorArray);

            cmd.SetComputeIntParam(WindCS, s_DirMotorCountID, d);
            cmd.SetComputeIntParam(WindCS, s_OmniMotorCountID, o);
            cmd.SetComputeIntParam(WindCS, s_VortexMotorCountID, v);
            cmd.SetComputeIntParam(WindCS, s_MovingMotorCountID, m);
            cmd.SetComputeVectorParam(WindCS, s_VolumePosOffsetID, m_OffsetPos);
            
            cmd.SetComputeBufferParam(WindCS, K_Motor, s_DirMotorBufferID, m_DirMotorBuffer);
            cmd.SetComputeBufferParam(WindCS, K_Motor, s_OmniMotorBufferID, m_OmniMotorBuffer);
            cmd.SetComputeBufferParam(WindCS, K_Motor, s_VortexMotorBufferID, m_VortexMotorBuffer);
            cmd.SetComputeBufferParam(WindCS, K_Motor, s_MovingMotorBufferID, m_MovingMotorBuffer);
            cmd.SetComputeTextureParam(WindCS, K_Motor, s_WindVelocityBufferID, rt);

            cmd.SetComputeVectorParam(WindCS, s_VolumeSizeID, m_VolumeSize);
            Dispatch(cmd, K_Motor);
        }

        private void DoDiffusion(CommandBuffer cmd, RenderTexture src, RenderTexture dst)
        {
            cmd.SetComputeTextureParam(WindCS, K_Diffusion, s_WindBufferInputID, src);
            cmd.SetComputeTextureParam(WindCS, K_Diffusion, s_WindBufferOutputID, dst);
            Dispatch(cmd, K_Diffusion);
        }

        private void DoAdvection_MacCormack(CommandBuffer cmd, RenderTexture src, RenderTexture dst)
        {
            cmd.SetComputeTextureParam(WindCS, K_AdvectionFwd_MacCormack, s_WindBufferInputID, src);
            cmd.SetComputeTextureParam(WindCS, K_AdvectionFwd_MacCormack, s_WindBufferIntermediateID, m_IntermediateBuffer);
            Dispatch(cmd, K_AdvectionFwd_MacCormack);

            cmd.SetComputeTextureParam(WindCS, K_AdvectionCorr, s_WindBufferInputID, src);
            cmd.SetComputeTextureParam(WindCS, K_AdvectionCorr, s_WindBufferIntermediateID, m_IntermediateBuffer);
            cmd.SetComputeTextureParam(WindCS, K_AdvectionCorr, s_WindBufferOutputID, dst);
            cmd.SetComputeBufferParam(WindCS, K_AdvectionCorr, s_WindDataForCPUBufferID, m_WindDataForCPUBuffer);
            Dispatch(cmd, K_AdvectionCorr);
        }

        private void DoAdvection(CommandBuffer cmd, RenderTexture src, RenderTexture dst)
        {
            cmd.SetComputeTextureParam(WindCS, K_AdvectionFwd, s_WindBufferInputID, src);
            Dispatch(cmd, K_AdvectionFwd);
            Dispatch(cmd, K_Merge);

            cmd.SetComputeTextureParam(WindCS, K_AdvectionRev, s_WindBufferInputID, src);
            Dispatch(cmd, K_AdvectionRev);
            cmd.SetComputeTextureParam(WindCS, K_MergeAndClear, s_WindBufferOutputID, dst);
            Dispatch(cmd, K_MergeAndClear);
        }

        private void DoVorticity(CommandBuffer cmd, RenderTexture src, RenderTexture dst)
        {
            cmd.SetComputeTextureParam(WindCS, K_CalcVorticity, s_WindBufferInputID, src);
            cmd.SetComputeTextureParam(WindCS, K_CalcVorticity, s_WindBufferIntermediateID, m_IntermediateBuffer);
            Dispatch(cmd, K_CalcVorticity);

            cmd.SetComputeTextureParam(WindCS, K_ApplyVorticity, s_WindBufferInputID, src);
            cmd.SetComputeTextureParam(WindCS, K_ApplyVorticity, s_WindBufferIntermediateID, m_IntermediateBuffer);
            cmd.SetComputeTextureParam(WindCS, K_ApplyVorticity, s_WindBufferOutputID, dst);
            Dispatch(cmd, K_ApplyVorticity);
        }

        private void UpdateTargetPosition()
        {
            m_OffsetPos = TargetTransform.position + TargetTransform.rotation * CameraCenterOffset - m_VolumeSize * 0.5f;
            Shader.SetGlobalVector(s_GlobalWindVolumeOffsetID, m_OffsetPos); 
        }

        private Vector3 GetTargetPosInt() => new Vector3(Mathf.Floor(m_OffsetPos.x), Mathf.Floor(m_OffsetPos.y), Mathf.Floor(m_OffsetPos.z));
        
        private void Dispatch(CommandBuffer cmd, int kernel)
        {
            cmd.DispatchCompute(WindCS, kernel, m_WindBrand.x / 8, m_WindBrand.y / 8, m_WindBrand.z / 8);
        }

        private void SwapBuffers() => m_CurrentIndex = 1 - m_CurrentIndex;

        public void AddWindMotor(WindMotor m) => m_MotorList.Add(m);
        public void RemoveWindMotor(WindMotor m) => m_MotorList.Remove(m);
        
        private void ReleaseResources()
        {
            foreach (var rt in m_WindVelocityBuffers) if (rt != null) rt.Release();
            m_IntermediateBuffer?.Release();
            m_WindDataForCPUBuffer?.Release();
            m_DirMotorBuffer?.Release();
            m_OmniMotorBuffer?.Release();
            m_VortexMotorBuffer?.Release();
            m_MovingMotorBuffer?.Release();
            m_WindAtomicBuffer?.Release();
            m_WindDebugTexture2D?.Release();
        }

        private void Awake() { m_Instance = this; }
        private void OnEnable() { m_Instance = this; InitSystem(); }
        private void OnDisable() { ReleaseResources(); Shader.DisableKeyword("_WIND_SIMULATION"); }
        private void OnDestroy() { ReleaseResources(); Shader.DisableKeyword("_WIND_SIMULATION"); }

        private void OnGUI()
        {
            if (!showUI || !isDebug || m_WindDebugTexture2D == null) return;

            float scale = Mathf.Max(1, DebugViewScale);
            int padding = 8;
            int labelHeight = Mathf.RoundToInt(10f * scale);

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(8f * scale),
            };
            style.fontStyle = FontStyle.Bold;
            
            int previewY = padding;
            
            // 绘制底色阴影 (黑色偏移)，防止在亮色天空盒下看不清字体
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(padding +1, previewY +1, 600 * scale, labelHeight), 
                $"Wind Velocity 3D LUT (Y-Slices: {m_WindBrand.y} | Grid: {m_DebugCols}x{m_DebugRows})", style);
            
            // 绘制标题
            style.normal.textColor = Color.yellow;
            GUI.Label(new Rect(padding, previewY, 600 * scale, labelHeight), 
                $"Wind Velocity 3D LUT (Y-Slices: {m_WindBrand.y} | Grid: {m_DebugCols}x{m_DebugRows})", style);
            previewY += labelHeight;

            // 绘制压平后的 2D 纹理
            float texW = m_WindDebugTexture2D.width * scale;
            float texH = m_WindDebugTexture2D.height * scale;

            Rect previewRect = new Rect(padding, previewY, texW, texH);
            GUI.DrawTexture(previewRect, m_WindDebugTexture2D, ScaleMode.StretchToFill, false);
            
        }
        
        
        #region Shader Property IDs

        private static readonly int s_ShiftOffsetID = Shader.PropertyToID("ShiftOffset"),
            s_VolumeSizeID = Shader.PropertyToID("VolumeSize"),
            s_VolumeSizeMinusOneID = Shader.PropertyToID("VolumeSizeMinusOne"),
            s_VolumePosOffsetID = Shader.PropertyToID("VolumePosOffset"),
            s_WindBufferInputID = Shader.PropertyToID("WindBufferInput"),
            s_WindBufferOutputID = Shader.PropertyToID("WindBufferOutput"),
            s_WindVelocityBufferID = Shader.PropertyToID("WindVelocityBuffer"),
            s_WindBufferIntermediateID = Shader.PropertyToID("WindBufferIntermediate"),
            s_WindDataForCPUBufferID = Shader.PropertyToID("WindDataForCPUBuffer"),
            s_WindAtomicBufferID = Shader.PropertyToID("WindAtomicBuffer"),
            s_DirMotorCountID = Shader.PropertyToID("DirectionalMotorBufferCount"),
            s_OmniMotorCountID = Shader.PropertyToID("OmniMotorBufferCount"),
            s_VortexMotorCountID = Shader.PropertyToID("VortexMotorBufferCount"),
            s_MovingMotorCountID = Shader.PropertyToID("MovingMotorBufferCount"),
            s_DirMotorBufferID = Shader.PropertyToID("DirectionalMotorBuffer"),
            s_OmniMotorBufferID = Shader.PropertyToID("OmniMotorBuffer"),
            s_VortexMotorBufferID = Shader.PropertyToID("VortexMotorBuffer"),
            s_MovingMotorBufferID = Shader.PropertyToID("MovingMotorBuffer"),
            s_DiffusionForceID = Shader.PropertyToID("DiffusionForce"),
            s_AdvectionForceID = Shader.PropertyToID("AdvectionForce"),
            s_VorticityScaleID = Shader.PropertyToID("VorticityScale"),
            s_MaxWindSpeedID = Shader.PropertyToID("MaxWindSpeed"),
            s_GlobalWindVelocityMapID = Shader.PropertyToID("WindVelocityData"),
            s_GlobalWindVolumeOffsetID = Shader.PropertyToID("_WindVolumeOffset"),
            s_OverallPowerId = Shader.PropertyToID("OverallPower");

        #endregion
    }
}