using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

/// <summary>
/// 管理和预生成 Atmosphere LUT
/// 包含调试和可视化功能
/// </summary>
public class LutDebugView : MonoBehaviour
{
    #region Parameters

    public InputActionAsset actions;
    private InputAction regenerateAction;


    [FormerlySerializedAs("compute")]
    [FormerlySerializedAs("transmittanceLUTCompute")]
    [Header("Compute Shader & RT")]
    [SerializeField]
    public ComputeShader cs;

    // LUT 纹理
    [SerializeField] public RenderTexture transmittanceLUT;
    [SerializeField] public RenderTexture multiScatteringLUT;
    [SerializeField] public RenderTexture skyViewLUT;
    [SerializeField] public RenderTexture aerialPerspectiveLUT;


    [Header("Atmosphere Parameters")] public AtmosphereSettings m_atmosphereSettings;

    [Header("Debug Options")] [SerializeField]
    private string m_profilerTag = "LutDebugView";

    [Range(1.0f, 5.0f)] [SerializeField] private float DebugViewScale = 1.0f;

    [SerializeField] private bool isDebug = true;
    [SerializeField] private bool showUI = true;
    [SerializeField] private bool autoGenerate = true;
    [SerializeField] private bool isAutoRefresh = false;
    [SerializeField] private bool generateTransAndMultiScatterOnce = false;

    // Compute Shader Kernel
    private int _kernelTransmittanceLut;
    private int _kernelMultiScatteringLut;
    private int _kernelSkyViewLut;
    private int _kernelAerialPerspectiveLut;

    private Camera m_camera;
    private int lutWidth;
    private int lutHeight;

    #endregion


    #region Shader Property IDs

    private static readonly int TransmittanceLutGlobalID = Shader.PropertyToID("_transmittanceLut");
    private static readonly int MultiScatteringLutGlobalID = Shader.PropertyToID("_multiScatteringLut");
    private static readonly int SkyViewLutGlobalID = Shader.PropertyToID("_skyViewLut");
    private static readonly int AerialPerspectiveLutGlobalID = Shader.PropertyToID("_aerialPerspectiveLut");

    private static readonly int TransmittanceLUTID = Shader.PropertyToID("TransmittanceLUT");
    private static readonly int MultiScatteringLUTID = Shader.PropertyToID("MultiScatteringLUT");
    private static readonly int TransmittanceLUT_ReadOnlyID = Shader.PropertyToID("TransmittanceLUT_ReadOnly");
    private static readonly int MultiScatteringLUT_ReadOnlyID = Shader.PropertyToID("MultiScatteringLUT_ReadOnly");
    private static readonly int SkyViewLUTID = Shader.PropertyToID("SkyViewLUT");
    private static readonly int AerialPerspectiveLUTID = Shader.PropertyToID("AerialPerspectiveLUT");
    private static readonly int WidthID = Shader.PropertyToID("_Width");
    private static readonly int HeightID = Shader.PropertyToID("_Height");

    private static readonly int SeaLevelID = Shader.PropertyToID("_SeaLevel");
    private static readonly int PlanetRadiusID = Shader.PropertyToID("_PlanetRadius");
    private static readonly int AtmosphereHeightID = Shader.PropertyToID("_AtmosphereHeight");
    private static readonly int SunLightIntensityID = Shader.PropertyToID("_SunLightIntensity");
    private static readonly int SunLightColorID = Shader.PropertyToID("_SunLightColor");
    private static readonly int SunDiskAngleID = Shader.PropertyToID("_SunDiskAngle");
    private static readonly int RayleighScaleID = Shader.PropertyToID("_RayleighScatteringScale");
    private static readonly int RayleighScaleHeightID = Shader.PropertyToID("_RayleighScatteringScalarHeight");
    private static readonly int MieScaleID = Shader.PropertyToID("_MieScatteringScale");
    private static readonly int MieAnisotropyID = Shader.PropertyToID("_MieAnisotropy");

    private static readonly int MieScaleHeightID = Shader.PropertyToID("_MieScatteringScalarHeight");
    private static readonly int OzoneAbsorptionScaleID = Shader.PropertyToID("_OzoneAbsorptionScale");

    private static readonly int OzoneCenterHeightID = Shader.PropertyToID("_OzoneLevelCenterHeight");
    private static readonly int OzoneWidthID = Shader.PropertyToID("_OzoneLevelWidth");

    private static readonly int AerialPerspectiveDistanceID = Shader.PropertyToID("_AerialPerspectiveDistance");
    private static readonly int AerialPerspectiveVoxelSizeID = Shader.PropertyToID("_AerialPerspectiveVoxelSize");

    private static readonly int MiePhaseParamsID = Shader.PropertyToID("_MiePhaseParams");

    #endregion


    private void OnEnable()
    {
        regenerateAction = actions.FindActionMap("Debug").FindAction("Regenerate");
        regenerateAction.performed += ctx =>
        {
            Debug.Log("Regenerating Transmittance LUT...");
            GenerateLut();
        };
        regenerateAction.Enable();
    }

    private void OnDisable()
    {
        regenerateAction.Disable();
    }

    private void Start()
    {
        InitializeLUT();
        m_camera = Camera.current;
        if (autoGenerate)
        {
            GenerateLut();
        }

        if (generateTransAndMultiScatterOnce)
            Debug.Log("Generated Transmittance and Multi-Scattering LUT once.");
    }

    private void FixedUpdate()
    {
        m_camera = Camera.current;

        if (isDebug && isAutoRefresh)
        {
            // 间隔帧更新策略（同一帧只更新一个 LUT）：
            // - Transmittance + MultiScattering 每 16 帧更新一次
            // - AerialPerspective 每 6 帧更新一次
            // - SkyView 每 4 帧更新一次
            // 优先级按更新间隔从大到小，避免在同一帧触发多次更新
            int frame = Time.frameCount;

            if (frame % 120 == 0)
            {
                // 更新全局光照
                // SkyBox 依赖 Sky View LUT 进行渲染，因此需要在更新 Sky View LUT 后刷新 GI,更新 SH球谐
                DynamicGI.UpdateEnvironment();
                Debug.Log("Auto-refreshing Global Illumination...");
            }
            else if (frame % 16 == 0)
            {
                if (generateTransAndMultiScatterOnce) return; // 只生成一次则跳过后续更新 跳过之后FixedUpdate中的更新逻辑

                // 更新 Transmittance 和 Multi-Scattering LUT
                GenerateTransAndMultiScatterLut();
                // Debug.Log("Auto-refreshing Transmittance & Multi-Scattering LUT...");
            }
            else if (frame % 6 == 0)
            {
                // 更新 Aerial Perspective LUT
                GenerateAerialLut();
                // Debug.Log("Auto-refreshing Aerial Perspective LUT...");
            }
            else if (frame % 4 == 0)
            {
                // 更新 Sky View LUT
                GenerateSkylLut();
                // Debug.Log("Auto-refreshing Sky View LUT...");
            }

            CommandBuffer cmd = CommandBufferPool.Get(m_profilerTag);
            cmd.Clear();
            cmd.SetGlobalTexture(TransmittanceLutGlobalID, transmittanceLUT);
            cmd.SetGlobalTexture(MultiScatteringLutGlobalID, multiScatteringLUT);
            cmd.SetGlobalTexture(SkyViewLutGlobalID, skyViewLUT);
            cmd.SetGlobalTexture(AerialPerspectiveLutGlobalID, aerialPerspectiveLUT);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
    }

    private void Update()
    {
    }

    private void OnDestroy()
    {
        // ReleaseLUT();
    }

    /// <summary>
    /// 初始化 LUT 纹理
    /// </summary>
    private void InitializeLUT()
    {
        if (cs == null)
        {
            Debug.LogError("TransmittanceLUT Compute Shader is not assigned!");
            return;
        }

        // 获取 Kernel 索引
        _kernelTransmittanceLut = cs.FindKernel("CSTransmittanceLUT");
        _kernelMultiScatteringLut = cs.FindKernel("CSMultiScatteringLUT");
        _kernelSkyViewLut = cs.FindKernel("CSSkyViewLUT");
        _kernelAerialPerspectiveLut = cs.FindKernel("CSAerialPerspectiveLUT");

        //  RenderTexture
        if (transmittanceLUT == null || multiScatteringLUT == null || skyViewLUT == null ||
            aerialPerspectiveLUT == null)
        {
            Debug.Log($"TransmittanceLUT Need a new RenderTexture");
            return;
        }
    }


    #region Compute Shader

    private void SetComputeShaderParameters()
    {
        if (cs == null || m_atmosphereSettings == null)
        {
            Debug.LogError("Cannot set compute shader parameters: ComputeShader or AtmosphereSettings not assigned!");
            return;
        }

        cs.SetFloat(SeaLevelID, m_atmosphereSettings.SeaLevel);
        cs.SetFloat(PlanetRadiusID, m_atmosphereSettings.PlanetRadius);
        cs.SetFloat(AtmosphereHeightID, m_atmosphereSettings.AtmosphereHeight);
        cs.SetFloat(SunLightIntensityID, m_atmosphereSettings.SunLightIntensity);
        cs.SetVector(SunLightColorID, m_atmosphereSettings.SunLightColor);
        cs.SetFloat(SunDiskAngleID, m_atmosphereSettings.SunDiskAngle);
        cs.SetFloat(RayleighScaleID, m_atmosphereSettings.RayleighScatteringScale);
        cs.SetFloat(RayleighScaleHeightID, m_atmosphereSettings.RayleighScatteringScalarHeight);
        cs.SetFloat(MieScaleID, m_atmosphereSettings.MieScatteringScale);
        cs.SetFloat(MieAnisotropyID, m_atmosphereSettings.MieAnisotropy);
        cs.SetFloat(MieScaleHeightID, m_atmosphereSettings.MieScatteringScalarHeight);
        cs.SetFloat(OzoneAbsorptionScaleID, m_atmosphereSettings.OzoneAbsorptionScale);
        cs.SetFloat(OzoneCenterHeightID, m_atmosphereSettings.OzoneLevelCenterHeight);
        cs.SetFloat(OzoneWidthID, m_atmosphereSettings.OzoneLevelWidth);
        cs.SetFloat(AerialPerspectiveDistanceID, m_atmosphereSettings.AerialPerspectiveDistance);
        cs.SetVector(AerialPerspectiveVoxelSizeID, new Vector4(32, 32, 32, 0));

        cs.SetVector(MiePhaseParamsID, m_atmosphereSettings.MiePhaseParams);
    }

    #endregion

    #region Generate LUT Methods

    /// <summary>
    /// 生成 TransmittanceLUT
    /// </summary>
    [ContextMenu("Generate Atmosphere LUT")]
    public void GenerateLut()
    {
        if (cs == null || transmittanceLUT == null)
        {
            Debug.LogError("Cannot generate LUT: Compute Shader or RenderTexture not initialized!");
            return;
        }

        SetComputeShaderParameters();


        // Transmittance LUT
        cs.SetTexture(_kernelTransmittanceLut, TransmittanceLUTID, transmittanceLUT);
        DispatchFor(transmittanceLUT, _kernelTransmittanceLut, "Transmittance LUT");

        // Multi-Scattering LUT
        cs.SetTexture(_kernelMultiScatteringLut, TransmittanceLUT_ReadOnlyID, transmittanceLUT);
        cs.SetTexture(_kernelMultiScatteringLut, MultiScatteringLUTID, multiScatteringLUT);
        DispatchFor(multiScatteringLUT, _kernelMultiScatteringLut, "Multi-Scattering LUT");

        // Sky View LUT
        cs.SetTexture(_kernelSkyViewLut, TransmittanceLUT_ReadOnlyID, transmittanceLUT);
        cs.SetTexture(_kernelSkyViewLut, MultiScatteringLUT_ReadOnlyID, multiScatteringLUT);
        cs.SetTexture(_kernelSkyViewLut, SkyViewLUTID, skyViewLUT);
        DispatchFor(skyViewLUT, _kernelSkyViewLut, "Sky View LUT");

        // Aerial Perspective LUT
        cs.SetTexture(_kernelAerialPerspectiveLut, TransmittanceLUT_ReadOnlyID, transmittanceLUT);
        cs.SetTexture(_kernelAerialPerspectiveLut, MultiScatteringLUT_ReadOnlyID, multiScatteringLUT);
        cs.SetTexture(_kernelAerialPerspectiveLut, AerialPerspectiveLUTID, aerialPerspectiveLUT);
        DispatchFor(aerialPerspectiveLUT, _kernelAerialPerspectiveLut, "Aerial Perspective LUT");
    }

    public void GenerateTransAndMultiScatterLut()
    {
        if (cs == null || transmittanceLUT == null || multiScatteringLUT == null)
        {
            Debug.LogError("Cannot generate LUT: Compute Shader or RenderTexture not initialized!");
            return;
        }

        SetComputeShaderParameters();

        // Transmittance LUT
        cs.SetTexture(_kernelTransmittanceLut, TransmittanceLUTID, transmittanceLUT);
        DispatchFor(transmittanceLUT, _kernelTransmittanceLut, "Transmittance LUT");

        // Multi-Scattering LUT
        cs.SetTexture(_kernelMultiScatteringLut, TransmittanceLUT_ReadOnlyID, transmittanceLUT);
        cs.SetTexture(_kernelMultiScatteringLut, MultiScatteringLUTID, multiScatteringLUT);
        DispatchFor(multiScatteringLUT, _kernelMultiScatteringLut, "Multi-Scattering LUT");
    }


    public void GenerateSkylLut()
    {
        if (cs == null || transmittanceLUT == null || multiScatteringLUT == null || skyViewLUT == null)
        {
            Debug.LogError("Cannot generate LUT: Compute Shader or RenderTexture not initialized!");
            return;
        }

        SetComputeShaderParameters();

        // Sky View LUT
        cs.SetTexture(_kernelSkyViewLut, TransmittanceLUT_ReadOnlyID, transmittanceLUT);
        cs.SetTexture(_kernelSkyViewLut, MultiScatteringLUT_ReadOnlyID, multiScatteringLUT);
        cs.SetTexture(_kernelSkyViewLut, SkyViewLUTID, skyViewLUT);
        DispatchFor(skyViewLUT, _kernelSkyViewLut, "Sky View LUT");
    }

    public void GenerateAerialLut()
    {
        if (cs == null || transmittanceLUT == null || multiScatteringLUT == null || aerialPerspectiveLUT == null)
        {
            Debug.LogError("Cannot generate LUT: Compute Shader or RenderTexture not initialized!");
            return;
        }

        SetComputeShaderParameters();

        // Aerial Perspective LUT
        cs.SetTexture(_kernelAerialPerspectiveLut, TransmittanceLUT_ReadOnlyID, transmittanceLUT);
        cs.SetTexture(_kernelAerialPerspectiveLut, MultiScatteringLUT_ReadOnlyID, multiScatteringLUT);
        cs.SetTexture(_kernelAerialPerspectiveLut, AerialPerspectiveLUTID, aerialPerspectiveLUT);
        DispatchFor(aerialPerspectiveLUT, _kernelAerialPerspectiveLut, "Aerial Perspective LUT");
    }

    #endregion


    public static void DrawTextureWithMultiplier(Rect rect, Texture tex, float mul)
    {
        Color old = GUI.color;
        GUI.color = old * mul;

        GUI.DrawTexture(rect, tex);

        GUI.color = old;
    }

    public void EnableGUI()
    {
        showUI = !showUI;
    }

    public void SetGUIScale(float scale)
    {
        DebugViewScale = scale;
    }

    private void OnGUI()
    {
        if (!showUI || !isDebug || transmittanceLUT == null)
            return;

        float scale = Mathf.Max(1, DebugViewScale);
        int padding = 10;
        int labelHeight = Mathf.RoundToInt(30f * scale);

        var style = new GUIStyle(GUI.skin.label);
        style.fontSize = Mathf.RoundToInt(14f * scale);
        style.normal.textColor = Color.white;

        int previewY = padding;

        void DrawLut(string title, RenderTexture tex, float colorMul = 1.0f)
        {
            if (tex == null)
                return;

            // Label
            GUI.Label(new Rect(padding, previewY, 200 * scale, labelHeight), title, style);
            previewY += labelHeight;

            // Texture rect (clamp to screen width)
            float texW = tex.width * scale;
            float texH = tex.height * scale;
            float maxWidth = Screen.width - padding * 2;
            if (texW > maxWidth)
            {
                float ratio = maxWidth / texW;
                texW *= ratio;
                texH *= ratio;
            }

            Rect previewRect = new Rect(padding, previewY, texW, texH);
            GUI.DrawTexture(previewRect, tex);
            // DrawTextureWithMultiplier(previewRect, tex, colorMul);

            previewY += Mathf.CeilToInt(texH) + padding;
        }

        DrawLut("Transmittance LUT", transmittanceLUT);
        DrawLut("MultiScattering LUT", multiScatteringLUT, 50.0f);
        DrawLut("SkyView LUT", skyViewLUT);
        DrawLut("AerialPerspective LUT", aerialPerspectiveLUT);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // 确保尺寸是 8 的倍数（与线程组大小匹配）
        lutWidth = Mathf.Max(8, Mathf.CeilToInt(lutWidth / 8.0f) * 8);
        lutHeight = Mathf.Max(8, Mathf.CeilToInt(lutHeight / 8.0f) * 8);
    }
#endif


//====== 生成各类 LUT 的通用调度函数和调用 ======

// Helper: 根据目标 RenderTexture 设置尺寸、计算线程组并调度 ComputeShader
    void DispatchFor(RenderTexture target, int kernel, string name)
    {
        if (cs == null)
        {
            Debug.LogError("ComputeShader is null, cannot dispatch.");
            return;
        }

        if (target == null)
        {
            Debug.LogError($"{name} target RenderTexture is null!");
            return;
        }

        lutWidth = target.width;
        lutHeight = target.height;
        cs.SetInt(WidthID, lutWidth);
        cs.SetInt(HeightID, lutHeight);

        int threadGroupsX = Mathf.CeilToInt(lutWidth / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(lutHeight / 8.0f);

        cs.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);
        Debug.Log($"{name} generated: {threadGroupsX}x{threadGroupsY} thread groups");
    }
}