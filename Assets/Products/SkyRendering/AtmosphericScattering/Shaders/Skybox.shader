Shader "Zerls/Atmosphere/Skybox"
{
    Properties
    {
        _SourceHdrTexture ("Source HDR Texture", 2D) = "white" {}

        [Header(Star Field)]
        [Toggle(STARFIELD_ON)] _EnableStarfield ("Enable Starfield", Float) = 1
        _StarfieldTex ("Starfield Cubemap", Cube) = "black" {}
        _StarfieldIntensity ("Starfield Intensity", Float) = 1.0
        _StarfieldRotationY ("Starfield Rotation Y", Range(0, 360)) = 0
        _StarfieldRotationZ ("Starfield Rotation Z", Range(0, 360)) = 0
        _StarTwinkleSpeed ("Star Twinkle Speed", Float) = 0.5
        _StarTwinkleAmount ("Star Twinkle Amount", Range(0, 1)) = 0.1

        [Header(Moon)]
        _MoonTex ("Moon Texture", 2D) = "black" {}
        _MoonSize ("Moon Size", Range(0.5, 100)) = 2.0
        _MoonIntensity ("Moon Intensity", Float) = 1.0
        [HDR]_MoonColor ("Moon Color", Color) = (1, 1, 1, 1)
        //        [Toggle] _UseMoonDirection ("Use Moon Light Direction", Float) = 0
        _MoonDirectionOffset ("Manual Moon Direction Offset", Vector) = (0.0, 0.0, 0.0, 0)
        
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Background" "Queue"="Background" "RenderPipeline" = "UniversalPipeline" "PreviewType"="Skybox"
        }
        Cull Off ZWrite Off ZTest LEqual

        Pass
        {
            Name "Skybox"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #pragma  shader_feature_local_fragment   _ STARFIELD_ON
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Helper.hlsl"
            #include "Scattering.hlsl"
            #include "Atmosphere.hlsl"

            CBUFFER_START(PerMaterial)
                float _StarfieldIntensity;
                float _StarfieldRotationY;
                float _StarfieldRotationZ;
                float _StarTwinkleSpeed;
                float _StarTwinkleAmount;


                float _MoonSize;
                float _MoonIntensity;
                float4 _MoonColor;
                //                float _UseMoonDirection;
                float3 _MoonDirectionOffset;
            CBUFFER_END

            TEXTURECUBE(_StarfieldTex);
            SAMPLER(sampler_StarfieldTex);
            TEXTURE2D(_MoonTex);
            SAMPLER(sampler_MoonTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                return output;
            }

            TEXTURE2D(_skyViewLut);
            // SAMPLER(sampler_LinearClamp);
            TEXTURE2D(_transmittanceLut);
            TEXTURE2D(_SourceHdrTexture);

            float3 GetSunDisk(in AtmosphereParameter param, float3 eyePos, float3 viewDir, float3 lightDir, float transmittance)
            {
                // 计算入射光照
                float cosine_theta = dot(viewDir, -lightDir);
                float theta = acos(cosine_theta) * (180.0 / PI);
                float3 sunLuminance = param.SunLightColor * param.SunLightIntensity;

                // 计算衰减
                sunLuminance *= transmittance;

                if (theta < param.SunDiskAngle) return sunLuminance;
                return float3(0, 0, 0);
            }


            // 渲染月亮
            float3 GetMoonDisk
            ( AtmosphereParameter param,
            float3 eyePos,
            float3 viewDir,
            float3 moonDir,
            float transmittance)
            {
                 moonDir = normalize(moonDir + _MoonDirectionOffset);
                // 计算视线与月亮方向的夹角
                float cosAngle = dot(viewDir, moonDir);
                float angle = acos(saturate(cosAngle));
                
                // 月亮角度大小（弧度）
                float moonAngularSize = (_MoonSize * 0.5) * (PI / 180.0);
                
                if (angle > moonAngularSize)
                    return float3(0, 0, 0);
                
                // 计算UV坐标
                float2 moonUV = float2(0.5, 0.5);
                if (angle < moonAngularSize)
                {
                    // 创建月亮的局部坐标系
                    float3 right = normalize(cross(moonDir, float3(0, 1, 0)));
                    if (length(right) < 0.001)
                        right = normalize(cross(moonDir, float3(0, 0, 1)));
                    float3 up = normalize(cross(right, moonDir));
                    
                    // 投影到月亮平面
                    float3 localDir = viewDir - moonDir * cosAngle;
                    float x = - dot(localDir, right);
                    float y =  dot(localDir, up);
                    
                    // 转换为UV
                    moonUV = float2(x, y) *rcp (moonAngularSize * 2.0) + 0.5;
                }
                
                // 采样月亮纹理
                float4 moonSample = SAMPLE_TEXTURE2D(_MoonTex, sampler_MoonTex, moonUV);
                
                
                // 月亮颜色
                float3 moonColor = moonSample.rgb * _MoonColor.rgb * _MoonIntensity * transmittance;
                
                // 使用alpha进行边缘混合
                float edgeFade = 1.0 - smoothstep(moonAngularSize * 0.9, moonAngularSize, angle);
                moonColor *= moonSample.a * edgeFade;
                
                return moonColor;
            }
            

            // 旋转向量（用于星空旋转）
            float3 RotateAroundY(float3 vec, float angle)
            {
                float rad = radians(angle);
                float cosA = cos(rad);
                float sinA = sin(rad);
                
                float3x3 rotMatrix = float3x3(
                    cosA, 0, sinA,
                    0, 1, 0,
                    -sinA, 0, cosA
                );
                
                return mul(rotMatrix, vec);
            }
            float3 RotateAroundX(float3 vec, float angle)
            {
               float rad = radians(angle);
                float cosA = cos(rad);
                float sinA = sin(rad);
                
                float3x3 rotMatrix = float3x3(
                    1, 0, 0,
                    0, cosA, -sinA,
                    0, sinA, cosA
                );
                
                return mul(rotMatrix, vec);
            }
            float3 RotateAroundZ(float3 vec, float angle)
            {
                float rad = radians(angle);
                float cosA = cos(rad);
                float sinA = sin(rad);
                
                float3x3 rotMatrix = float3x3(
                    cosA, -sinA, 0,
                    sinA, cosA, 0,
                    0, 0, 1
                );
                
                return mul(rotMatrix, vec);
            }

            //  sunDir.y = 1 → 太阳在头顶（正午）
            //  sunDir.y = 0 → 地平线（晨昏）、
            //  sunDir.y < 0 → 太阳在地下（夜晚）
            
            // 昼夜：太阳在地下最强，头顶最弱
            float NightFactor(float3 sunDir)
            {
                // sunDir.y: [-1, 1]
                // 映射成：正午0 → 晨昏0.5 → 午夜1
                float h = saturate(-sunDir.y);
                // 加个 2 次方让夜晚过渡更柔
                return h * h;
            }

            float SunDirToDayTime(float3 sunDir)
            {
                float sunY = sunDir.y;
                // sunY ∈ [-1,1]
                // angle: 0（正午） → π（午夜）
                float angle = acos(saturate(sunY));

                // 映射到 [0,1]
                float t = angle / PI; // 0=正午, 1=午夜
                

                t =frac(t);
                
                return t;
            }

            // 根据昼夜时间获取星空强度
            //TODO: 与当前摄像机高度也有关系，需要处理
            float GetStarIntensity(float dayTime )
            {

                // ±6 小时窗口
               const  float halfWindow = 6.0 / 12.0;

                float d = abs(dayTime );        // 正午 = 0.0
                float noonFade = saturate(d / halfWindow);

                return  noonFade;
            }


            
            
            float3 GetStarfieldColor(in AtmosphereParameter param, float3 viewDir)
            {
                float3  starColor =float3(0.0,0.0,0.0);
                // 旋转星空
                float3 rotatedViewDir  =RotateAroundY(viewDir, _StarfieldRotationY);

                 rotatedViewDir  =RotateAroundZ(rotatedViewDir, _StarfieldRotationZ);
                // 采样星空贴图
                starColor = SAMPLE_TEXTURECUBE(_StarfieldTex, sampler_LinearClamp, rotatedViewDir).rgb;

                // 添加闪烁效果
                float twinkle = (sin(_Time.y * _StarTwinkleSpeed + dot(rotatedViewDir, float3(12.9898, 78.233, 45.164)) * 43758.5453) + 1.0) * 0.5;
                twinkle = lerp(1.0 - _StarTwinkleAmount, 1.0 + _StarTwinkleAmount, twinkle);
                starColor *= twinkle;

                return starColor * _StarfieldIntensity;
            }

            float4 frag(Varyings input) : SV_Target
            {
                AtmosphereParameter param = GetAtmosphereParameter();

                float4 color = float4(0, 0, 0, 1);
                float3 viewDir = normalize(input.positionWS);

                Light mainLight = GetMainLight();
                float3 lightDir = mainLight.direction;

                
                float h = max(1.0, _WorldSpaceCameraPos.y - param.SeaLevel) + param.PlanetRadius;
                float3 eyePos = float3(0, h, 0);

                float dayTime = SunDirToDayTime(lightDir);

                // 判断光线是否被星球阻挡  disToPlanet >= 0 表示被星球阻挡
                float disToPlanet = RayIntersectSphere(float3(0, 0, 0), param.PlanetRadius, eyePos, viewDir);
                if (disToPlanet < 0)
                {
                    // 计算大气透射率（如果在大气内）
                    float3 transmittance = float3(1, 1, 1);
                    float distToAtmosphere = RayIntersectSphere(float3(0, 0, 0), param.PlanetRadius + param.AtmosphereHeight, eyePos, viewDir);
                    if (distToAtmosphere >= 0)
                    {
                        transmittance = TransmittanceToTopOfAtmosphere(param, eyePos, viewDir, _transmittanceLut, sampler_LinearClamp);
                    }

                    color.rgb += GetMoonDisk(param, eyePos, viewDir, -lightDir, transmittance);
                    color.rgb += GetSunDisk(param, eyePos, viewDir, -lightDir, transmittance);
                }

                #if STARFIELD_ON
                    color.rgb += GetStarfieldColor(param, viewDir) *2.0 * GetStarIntensity(dayTime);
                #endif
                color.rgb += SAMPLE_TEXTURE2D(_skyViewLut, sampler_LinearClamp, ViewDirToUV(viewDir)).rgb;

                return color;
            }
            ENDHLSL
        }
    }
    CustomEditor "Zerls.BasicShaderGUI"
}