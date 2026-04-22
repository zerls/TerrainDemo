using System;
using UnityEngine;

[Serializable]
[CreateAssetMenu(fileName = "CloudSettings", menuName = "Rendering/CloudSettings")]
public class CloudSettings : ScriptableObject
{
    [Header("云层基础尺寸 Cloud Dimensions")]
    [Tooltip("云层起始高度，单位：米(m)")]
    [SerializeField] public float CloudAreaStartHeight = 1500.0f;

    [Tooltip("云层厚度，单位：米(m)")]
    [SerializeField] public float CloudAreaThickness = 4000.0f;

    [Tooltip("体积阴影/云影的覆盖范围 (x4)")]
    [SerializeField] public float CloudShadowExtent = 10.0f;

    [Header("云层形态与覆盖 Cloud Coverage & Density")]
    [Tooltip("天气图(Weather Map)的 UV 缩放系数")]
    [SerializeField] public Vector2 CloudWeatherUVScale = new Vector2(0.01f, 0.01f);

    [Tooltip("云层覆盖率控制全局多云/晴朗")]
    [SerializeField] [Range(0.0f, 1.0f)] public float CloudCoverage = 0.5f;

    [Tooltip("云层密度乘数")]
    [SerializeField] [Range(0.1f, 10.0f)] public float CloudDensity = 1.0f;

    [Tooltip("云层整体噪声的缩放比")]
    [SerializeField] public float CloudNoiseScale = 1.0f;

    [Header("动态与风速 Animation & Wind")]
    [Tooltip("云层飘动的方向")]
    [SerializeField] public Vector3 CloudDirection = new Vector3(1.0f, 0.0f, 0.0f);

    [Tooltip("云层飘动的速度")]
    [SerializeField] public float CloudSpeed = 1.0f;

    [Header("光照与色彩 Lighting & Color")]
    [Tooltip("云层的固有色 (反照率)")]
    [SerializeField] public Color CloudAlbedo = Color.white;

    [Tooltip("太阳光对云层的遮蔽/亮度影响缩放")]
    [SerializeField] public float CloudShadingSunLightScale = 1.0f;

    [Tooltip("云层边缘与大气雾的混合消隐系数")]
    [SerializeField] public float CloudFogFade = 1.0f;

    [Tooltip("是否开启地面反弹的环境光贡献 (1为开启，0为关闭)")]
    [SerializeField] [Range(0, 1)] public int CloudEnableGroundContribution = 1;

    [Header("相函数与多重散射 Phase & Multiple Scattering")]
    [Tooltip("前向散射系数 (向着太阳看时的银边高亮)")]
    [SerializeField] [Range(0.0f, 0.999f)] public float CloudPhaseForward = 0.8f;

    [Tooltip("后向散射系数 (背对太阳看时的漫反射)")]
    [SerializeField] [Range(-0.999f, 0.0f)] public float CloudPhaseBackward = -0.2f;

    [Tooltip("前后向散射的混合权重")]
    [SerializeField] [Range(0.0f, 1.0f)] public float CloudPhaseMixFactor = 0.5f;

    [Tooltip("多重散射时的能量衰减因子")]
    [SerializeField] [Range(0.0f, 1.0f)] public float CloudMultiScatterExtinction = 0.5f;

    [Tooltip("多重散射时的散射发散因子")]
    [SerializeField] [Range(0.0f, 1.0f)] public float CloudMultiScatterScatter = 0.5f;

    [Tooltip("粉末效应(Powder Effect)的强度范围")]
    [SerializeField] public float CloudPowderScale = 1.0f;

    [Tooltip("粉末效应的指数")]
    [SerializeField] public float CloudPowderPow = 1.0f;

    [Header("体积光/体积雾 God Ray")]
    [Tooltip("是否开启云下体积光体积雾 (1为开启，0为关闭)")]
    [SerializeField] [Range(0, 1)] public int CloudGodRay = 1;

    [Tooltip("神明光强度缩放")]
    [SerializeField] public float CloudGodRayScale = 1.0f;

    [Header("步进与性能 Ray Marching & Performance")]
    [Tooltip("主视线在云层中的最大步进次数")]
    [SerializeField] public int CloudMarchingStepNum = 64;

    [Tooltip("向太阳步进计算光照的次数")]
    [SerializeField] public int CloudLightStepNum = 6;

    [Tooltip("向太阳步进的基础步长 (m)")]
    [SerializeField] public float CloudLightBasicStep = 100.0f;

    [Tooltip("向太阳步进时，每次步长放大的倍数 (加速跳出云层)")]
    [SerializeField] public float CloudLightStepMul = 2.0f;

    [Tooltip("最大追踪距离 (超过此距离不渲染云)，单位：km")]
    [SerializeField] public float CloudMaxTraceingDistance = 3000.0f;

    [Tooltip("追踪起点的最大限制距离，单位：km")]
    [SerializeField] public float CloudTracingStartMaxDistance = 9.0f;

    [Tooltip("基础形状噪声 3D Texture 的缩放频率")]
    [SerializeField] public float CloudBasicNoiseScale = 1.0f;

    [Tooltip("细节侵蚀噪声 3D Texture 的缩放频率")]
    [SerializeField] public float CloudDetailNoiseScale = 2.0f;

    [Tooltip("光照缓存 (SunLit Map) 的细节层级")]
    [SerializeField] public int CloudSunLitMapOctave = 3;
    
}