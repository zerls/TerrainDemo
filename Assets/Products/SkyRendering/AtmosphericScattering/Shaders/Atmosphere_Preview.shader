Shader "Zerls/Atmosphere/Atmosphere Preview (Space View)"
{
    Properties
    {
        [Header(Atmosphere Preview Settings)]
        _AtmosphereIntensity ("Atmosphere Intensity", Range(0, 5)) = 2.0
        _LimbGlowIntensity ("Limb Glow Intensity", Range(0, 10)) = 4.0
        _LimbGlowWidth ("Limb Glow Width", Range(0.1, 3)) = 1.5
        _LimbGlowColor ("Limb Glow Color", Color) = (0.4, 0.6, 1.0, 1)
        
        [Header(Scattering Colors)]
        _DaySideColor ("Day Side Color", Color) = (0.4, 0.6, 1.0, 1)
        _TerminatorColor ("Terminator Color", Color) = (1.0, 0.5, 0.3, 1)
        _NightSideColor ("Night Side Color", Color) = (0.05, 0.05, 0.1, 1)
        
        [Header(Earth Parameters)]
        _PlanetRadius ("Planet Radius (m)", Float) = 6371000
        _AtmosphereHeight ("Atmosphere Height (m)", Float) = 100000
        _ViewDistance ("View Distance (m)", Float) = 500000
        
        [Header(Scattering Coefficients)]
        _RayleighScatteringScale ("Rayleigh Scattering Scale", Range(0, 2)) = 1.0
        _MieScatteringScale ("Mie Scattering Scale", Range(0, 2)) = 1.0
        
        [Header(Advanced)]
        _Exposure ("Exposure", Float) = 1.5
    }
    
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" "RenderPipeline" = "UniversalPipeline" "PreviewType"="Skybox" }
        Cull Off ZWrite Off ZTest LEqual

        Pass
        {
            Name "Atmosphere Preview"
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 viewDir : TEXCOORD0;
            };

            // Properties
            float _AtmosphereIntensity;
            float _LimbGlowIntensity;
            float _LimbGlowWidth;
            float4 _LimbGlowColor;
            
            float4 _DaySideColor;
            float4 _TerminatorColor;
            float4 _NightSideColor;
            
            float _PlanetRadius;
            float _AtmosphereHeight;
            float _ViewDistance;
            
            float _RayleighScatteringScale;
            float _MieScatteringScale;
            
            float _Exposure;

            Varyings vert (Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                
                // 直接使用天空盒方向，不考虑相机位置
                output.viewDir = normalize(input.positionOS.xyz);
                
                return output;
            }

            // 射线与球体相交
            float RaySphereIntersect(float3 rayOrigin, float3 rayDir, float3 sphereCenter, float sphereRadius)
            {
                float3 oc = rayOrigin - sphereCenter;
                float b = dot(oc, rayDir);
                float c = dot(oc, oc) - sphereRadius * sphereRadius;
                float discriminant = b * b - c;
                
                if (discriminant < 0.0)
                    return -1.0;
                
                float t = -b - sqrt(discriminant);
                return t > 0.0 ? t : -b + sqrt(discriminant);
            }

            // 计算大气层散射（简化版）
            float3 CalculateAtmosphereScattering(float3 viewDir, float3 sunDir, float3 viewPos)
            {
                float3 planetCenter = float3(0, 0, 0);
                float atmosphereRadius = _PlanetRadius + _AtmosphereHeight;
                
                // 检查是否与大气层相交
                float distToAtmosphere = RaySphereIntersect(viewPos, viewDir, planetCenter, atmosphereRadius);
                if (distToAtmosphere < 0.0)
                    return float3(0, 0, 0);
                
                // 检查是否击中行星
                float distToPlanet = RaySphereIntersect(viewPos, viewDir, planetCenter, _PlanetRadius);
                
                // 如果击中行星，返回黑色
                if (distToPlanet > 0.0)
                    return float3(0, 0, 0);
                
                // 计算大气层入射点
                float3 atmospherePoint = viewPos + viewDir * distToAtmosphere;
                float3 atmosphereNormal = normalize(atmospherePoint - planetCenter);
                
                // 计算太阳照射角度
                float sunDot = dot(atmosphereNormal, sunDir);
                
                // 计算视角边缘因子（Rim lighting）
                float3 viewToAtmosphere = normalize(atmospherePoint - viewPos);
                float rimFactor = 1.0 - abs(dot(viewToAtmosphere, atmosphereNormal));
                rimFactor = pow(rimFactor, 1.0 / _LimbGlowWidth);
                
                // 根据太阳位置计算颜色
                float3 scatterColor;
                
                if (sunDot > 0.2) // 日照侧
                {
                    // 日照侧 - 蓝色散射
                    scatterColor = _DaySideColor.rgb;
                    scatterColor *= saturate(sunDot * 1.5);
                }
                else if (sunDot > -0.2) // 晨昏线（Terminator）
                {
                    // 晨昏线 - 橙红色
                    float t = (sunDot + 0.2) / 0.4; // 0到1
                    scatterColor = lerp(_TerminatorColor.rgb, _DaySideColor.rgb, t);
                    scatterColor *= 1.5; // 晨昏线更亮
                }
                else // 夜晚侧
                {
                    // 夜晚侧 - 深蓝/黑色
                    scatterColor = _NightSideColor.rgb;
                    scatterColor *= saturate(-sunDot * 0.5 + 0.3);
                }
                
                // 应用边缘发光
                float3 limbGlow = _LimbGlowColor.rgb * rimFactor * _LimbGlowIntensity;
                
                // 边缘发光在日照侧更强
                limbGlow *= saturate(sunDot * 0.5 + 0.7);
                
                // 组合散射和边缘发光
                float3 finalColor = scatterColor * _AtmosphereIntensity + limbGlow;
                
                // 距离衰减（可选，使远处更柔和）
                float distanceFade = saturate(1.0 - distToAtmosphere / (_ViewDistance * 2.0));
                finalColor *= distanceFade;
                
                // Rayleigh散射强度调制（基于视角）
                float rayleighPhase = 0.75 * (1.0 + dot(viewDir, sunDir) * dot(viewDir, sunDir));
                finalColor *= lerp(1.0, rayleighPhase, _RayleighScatteringScale * 0.3);
                
                return finalColor;
            }

            // 渲染太阳光盘
            float3 RenderSun(float3 viewDir, float3 sunDir)
            {
                float sunDot = dot(viewDir, sunDir);
                float sunAngle = acos(saturate(sunDot)) * (180.0 / 3.14159265);
                
                // 太阳角直径约0.5度
                if (sunAngle < 0.53)
                {
                    return float3(1, 1, 0.9) * 20.0;
                }
                
                return float3(0, 0, 0);
            }

            // 渲染背景星空（简单版）
            float3 RenderStars(float3 viewDir)
            {
                // 简单的程序化星空
                float3 p = viewDir * 100.0;
                float stars = frac(sin(dot(p, float3(12.9898, 78.233, 45.164))) * 43758.5453);
                stars = pow(stars, 50.0); // 稀疏的星星
                
                return float3(1, 1, 1) * stars * 0.5;
            }

            float4 frag (Varyings input) : SV_Target
            {
                float3 viewDir = normalize(input.viewDir);
                
                // 获取主光源方向（太阳）
                Light mainLight = GetMainLight();
                float3 sunDir = -mainLight.direction;
                
                // 固定的观察位置（从外太空看）
                float3 viewPos = float3(0, _ViewDistance, 0);
                
                // 初始化颜色
                float3 finalColor = float3(0, 0, 0);
                
                // 1. 渲染背景星空
                finalColor += RenderStars(viewDir);
                
                // 2. 渲染大气层
                float3 atmosphereColor = CalculateAtmosphereScattering(viewDir, sunDir, viewPos);
                finalColor += atmosphereColor;
                
                // 3. 渲染太阳
                finalColor += RenderSun(viewDir, sunDir);
                
                // 应用曝光
                finalColor *= _Exposure;
                
                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
    
    CustomEditor "AtmospherePreviewShaderGUI"
}
