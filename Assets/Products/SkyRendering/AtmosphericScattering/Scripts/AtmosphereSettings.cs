using System;
using UnityEngine;

[Serializable]
[CreateAssetMenu(fileName = "Atmosphere", menuName = "Rendering/AtmosphereSettings")]
public class AtmosphereSettings : ScriptableObject
{
    [Header("星球设置 Planet Settings")]
    [Tooltip("海平面高度，默认设置为0，设置为负值可模拟低于海平面的环境，获得低海拔下更好的大气效果")]
    [SerializeField] public float SeaLevel = 0.0f;

    [Tooltip("星球半径，地球约为6360000m")]
    [SerializeField] public float PlanetRadius = 6360000.0f;

    [Tooltip("大气层高度，地球约为60000m")]
    [SerializeField] public float AtmosphereHeight = 60000.0f;

    [Header("太阳设置 Sun Settings")]
    [Tooltip("太阳光强度")]
    [SerializeField] public float SunLightIntensity = 31.4f;

    [Tooltip("太阳光颜色")]
    [SerializeField] public Color SunLightColor = Color.white;

    [Tooltip("太阳圆盘角度，单位为度")]
    [SerializeField] public float SunDiskAngle = 9.0f;

    [Header("瑞利散射 Rayleigh Scattering")]
    
    [Tooltip("瑞利散射颜色")]
    [SerializeField] public Color RayleighScatteringColor = new Color(0.5f, 0.7f, 1.0f);
    
    [Tooltip("瑞利散射强度缩放")]
    [SerializeField] [Range(0.1f, 10.0f)] public float RayleighScatteringScale = 1.0f;

    [Tooltip("瑞利散射高度参数")]
    [SerializeField] public float RayleighScatteringScalarHeight = 8000.0f;

    [Header("米氏散射 Mie Scattering")]
    
    [Tooltip("米氏散射颜色")]
    [SerializeField] public Color MieScatteringColor = new Color(1.0f, 0.95f, 0.9f);
    
    [Tooltip("米氏散射强度缩放")]
    [SerializeField] [Range(0.1f, 10.0f)] public float MieScatteringScale = 1.0f;

    [Tooltip("米氏散射各向异性系数")]
    [HideInInspector][SerializeField] [Range(0.0f, 0.999f)] public float MieAnisotropy = 0.8f;
    
    [Tooltip("米氏散射相函数参数")]
    [SerializeField] public Vector4 MiePhaseParams = new Vector4(0.8f, -0.3f, 0.9f,1.0f);

    [Tooltip("米氏散射高度参数")]
    [SerializeField] public float MieScatteringScalarHeight = 1200.0f;

    [Header("臭氧吸收 Ozone Absorption")]
    [Tooltip("臭氧吸收强度缩放")]
    [SerializeField] [Range(0.1f, 10.0f)] public float OzoneAbsorptionScale = 1.0f;

    [Tooltip("臭氧层中心高度")]
    [SerializeField] public float OzoneLevelCenterHeight = 25000.0f;

    [Tooltip("臭氧层宽度")]
    [SerializeField] public float OzoneLevelWidth = 15000.0f;

    [Header("空气透视 Aerial Perspective")]
    [Tooltip("空气透视最大距离")]
    [SerializeField] public float AerialPerspectiveDistance = 32000.0f;
    
    [Tooltip("空气透视体素尺寸")]
    [SerializeField] public Vector4 AerialPerspectiveVoxelSize = new Vector4(32, 32, 32, 0);
}