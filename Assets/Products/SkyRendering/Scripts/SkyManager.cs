using UnityEngine;
using UnityEngine.Serialization; 

[ExecuteAlways] 
public class SkyManager : MonoBehaviour
{
    public static SkyManager Instance { get; private set; }

    [Header("Global Lighting")] 
    public Light sunLight; 
    private Color skyBaseColor = new Color(0.5f, 0.7f, 1.0f);
    private float skyIntensity = 1.0f;
    public int shadowType = 1;
    public bool enableRayTracedShadows = false;

    [Header("Textures")] 
    public Texture3D cloudBasicNoise;
    public Texture3D cloudDetailNoise;
    public Texture2D weatherTexture;
    public Texture2D curlNoiseTexture;
    public Texture2D blueNoise;

    [Header("Sky Settings")] 
    public AtmosphereSettings atmosphereSettings;
    public CloudSettings cloudSettings;

    public Material CloudMaterial;
    public Shader CloudShader;
    
    [FormerlySerializedAs("DensityThreshold")]
    [Space(10)]
    [Tooltip("云层密度阈值")]
    [SerializeField] [Range(0.0f, 1.0f)] public float CloudDensityThreshold = 0.333f;
    
    [FormerlySerializedAs("DensityPower")]
    [Tooltip("云层密度乘方")]
    [SerializeField] [Range(0.1f, 5.0f)] public float CloudDensityPower = 2.0f;
    
    [Tooltip("云层贴图偏移")]
    [SerializeField] public Vector3 CloudOffset = new Vector3(0.0f, 0.0f, 0.0f);

    [Header("Debug Settings")]
    public bool showGUI = true;
    public Vector2 guiPosition = new Vector2(10, 10);
    public int fontSize = 16;
    public Color fontColor = Color.cyan;
    public bool useSphericalAltitude = false;

    private Camera targetCamera;
    
    // 优化: 缓存 GUIStyle 避免每帧 new
    private GUIStyle m_DebugGUIStyle;

    private void OnEnable()
    {
        Instance = this;
        targetCamera = GetComponent<Camera>();
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    private void OnDisable()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
    
    void Update()
    {
        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
            if (targetCamera == null) targetCamera = Camera.main;
        }

        if (sunLight != null)
        {
             skyBaseColor = sunLight.color;
             skyIntensity = sunLight.intensity; 
        }
    }

    void OnGUI()
    {
        if (!showGUI || targetCamera == null) return;

        if (m_DebugGUIStyle == null)
        {
            m_DebugGUIStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                normal = { textColor = fontColor }
            };
        }
        else
        {
            m_DebugGUIStyle.fontSize = fontSize;
            m_DebugGUIStyle.normal.textColor = fontColor;
        }

        Vector3 camPos = targetCamera.transform.position;

        float seaLevel = atmosphereSettings != null ? atmosphereSettings.SeaLevel : 0f;
        float planetRadius = atmosphereSettings != null ? atmosphereSettings.PlanetRadius : 0f;

        float altitudeFromSea = camPos.y - seaLevel;
        float altitudeFromPlanet = 0f;
        
        if (atmosphereSettings != null && planetRadius > 0f)
        {
            altitudeFromPlanet = camPos.magnitude - planetRadius;
        }

        float cloudStart = cloudSettings != null ? cloudSettings.CloudAreaStartHeight : 0f;
        float cloudThickness = cloudSettings != null ? cloudSettings.CloudAreaThickness : 0f;

        string altitudeText = useSphericalAltitude && atmosphereSettings != null && planetRadius > 0f
            ? $"相机高度（球面量）: {altitudeFromPlanet + planetRadius:0.00}m (PlanetRadius {planetRadius:0.0}m)"
            : $"相机海拔高度（相对于海平面）：{altitudeFromSea:0.00}m (SeaLevel {seaLevel:0.00}m)";

        string cloudText = $"云层起始高度：{cloudStart:0.00}m，厚度：{cloudThickness:0.00}m\n离云层起始高度：{camPos.y - cloudStart:0.00}m";

        float normalizedCloudHeight = (cloudSettings != null && cloudThickness > 0f) ? Mathf.Clamp01((camPos.y - cloudStart) / cloudThickness) : 0f;
        cloudText += $"\n云层高度归一化：{normalizedCloudHeight:0.00}";

        int lineCount = 4 + (cloudSettings != null ? 1 : 0) + 1;
        float textHeight = m_DebugGUIStyle.lineHeight + 4f;
        float areaHeight = textHeight * lineCount + 10f;

        GUILayout.BeginArea(new Rect(guiPosition.x, guiPosition.y, 500, areaHeight));
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("==== Camera Altitude Debug ====", m_DebugGUIStyle);
        GUILayout.Label(altitudeText, m_DebugGUIStyle);
        
        if (cloudSettings != null)
        {
            GUILayout.Label(cloudText, m_DebugGUIStyle);
        }
        
        GUILayout.Label($"Camera World Pos: {camPos.x:0.00}, {camPos.y:0.00}, {camPos.z:0.00}", m_DebugGUIStyle);
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
    
    public SkyInfo GetSkyInfo(Camera cam)
    {
        SkyInfo info = new SkyInfo
        {
            color = new Vector3(skyBaseColor.r, skyBaseColor.g, skyBaseColor.b),
            intensity = skyIntensity,
            shadowType = shadowType,
            rayTraceShadow = enableRayTracedShadows ? 1 : 0,
            direction = sunLight != null ? -sunLight.transform.forward : Vector3.up
        };

        if (atmosphereSettings != null)
        {
            info.atmosphereConfig = new AtmosphereParameters
            {
                SeaLevel = atmosphereSettings.SeaLevel,
                PlanetRadius = atmosphereSettings.PlanetRadius,
                AtmosphereHeight = atmosphereSettings.AtmosphereHeight,
                
                SunLightIntensity = sunLight != null ? sunLight.intensity : atmosphereSettings.SunLightIntensity,
                SunLightColor = sunLight != null 
                    ? new Vector3(sunLight.color.r, sunLight.color.g, sunLight.color.b) 
                    : new Vector3(atmosphereSettings.SunLightColor.r, atmosphereSettings.SunLightColor.g, atmosphereSettings.SunLightColor.b),
                
                SunDiskAngle = atmosphereSettings.SunDiskAngle,
                RayleighScatteringScale = atmosphereSettings.RayleighScatteringScale,
                RayleighScatteringScalarHeight = atmosphereSettings.RayleighScatteringScalarHeight,
                MieScatteringScale = atmosphereSettings.MieScatteringScale,
                MieAnisotropy = atmosphereSettings.MieAnisotropy,
                MieScatteringScalarHeight = atmosphereSettings.MieScatteringScalarHeight,
                OzoneAbsorptionScale = atmosphereSettings.OzoneAbsorptionScale,
                OzoneLevelCenterHeight = atmosphereSettings.OzoneLevelCenterHeight,
                OzoneLevelWidth = atmosphereSettings.OzoneLevelWidth,
                MiePhaseParams = atmosphereSettings.MiePhaseParams
            };
        }

        if (cloudSettings != null)
        {
            info.cloudConfig = new CloudParameters
            {
                CloudAreaStartHeight = cloudSettings.CloudAreaStartHeight,
                CloudAreaThickness = cloudSettings.CloudAreaThickness,
                CloudGodRayScale = cloudSettings.CloudGodRayScale,
                CloudShadowExtent = cloudSettings.CloudShadowExtent,
                CloudWeatherUVScale = cloudSettings.CloudWeatherUVScale,
                CloudCoverage = cloudSettings.CloudCoverage,
                CloudDensity = cloudSettings.CloudDensity,
                CloudShadingSunLightScale = cloudSettings.CloudShadingSunLightScale,
                CloudFogFade = cloudSettings.CloudFogFade,
                CloudMaxTraceingDistance = cloudSettings.CloudMaxTraceingDistance,
                CloudTracingStartMaxDistance = cloudSettings.CloudTracingStartMaxDistance,
                CloudDirection = cloudSettings.CloudDirection,
                CloudSpeed = cloudSettings.CloudSpeed,
                CloudMultiScatterExtinction = cloudSettings.CloudMultiScatterExtinction,
                CloudMultiScatterScatter = cloudSettings.CloudMultiScatterScatter,
                CloudBasicNoiseScale = cloudSettings.CloudBasicNoiseScale,
                CloudDetailNoiseScale = cloudSettings.CloudDetailNoiseScale,
                CloudAlbedo = new Vector3(cloudSettings.CloudAlbedo.r, cloudSettings.CloudAlbedo.g, cloudSettings.CloudAlbedo.b),
                CloudPhaseForward = cloudSettings.CloudPhaseForward,
                CloudPhaseBackward = cloudSettings.CloudPhaseBackward,
                CloudPhaseMixFactor = cloudSettings.CloudPhaseMixFactor,
                CloudPowderScale = cloudSettings.CloudPowderScale,
                CloudPowderPow = cloudSettings.CloudPowderPow,
                CloudLightStepMul = cloudSettings.CloudLightStepMul,
                CloudLightBasicStep = cloudSettings.CloudLightBasicStep,
                CloudLightStepNum = cloudSettings.CloudLightStepNum,
                CloudEnableGroundContribution = cloudSettings.CloudEnableGroundContribution,
                CloudMarchingStepNum = cloudSettings.CloudMarchingStepNum,
                CloudSunLitMapOctave = cloudSettings.CloudSunLitMapOctave,
                CloudNoiseScale = cloudSettings.CloudNoiseScale,
                CloudGodRay = cloudSettings.CloudGodRay
            };
        }

        if (cam != null)
        {
            info.cloudConfig.CamWorldPos = cam.transform.position;
        }

        return info;
    }
}

