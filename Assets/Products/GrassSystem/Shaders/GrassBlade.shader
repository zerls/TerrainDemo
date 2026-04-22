Shader "Zerls/GrassSystem/GrassBlade"
{
    // =============================================================================================
    // GrassBlade.shader (实例草片渲染)
    // 渲染输入: Compute 侧生成的 _GrassBlades StructuredBuffer (每实例 14 float 属性)
    // 网格拓扑: 使用一个“参考草片”Mesh (通过索引 Triangles / 顶点 Colors / Uvs) 在 GPU 端复用
    // 形状生成: 顶点阶段将参考草片沿着一条三次贝塞尔曲线 (p0->p3) 弯曲, 控制点 p1 / p2 受 bend + 风影响偏移
    // 动画来源:
    //   1) Compute 赋予的 bend / rotAngle / sideBend / windForce 静态或半静态参数
    //   2) 这里再基于时间 _Time 与 hash 做正弦风偏移 (p2 / p3 控制点) + 顶端前倾 (PushTipForward)
    // 法线处理:
    //   curvedNorm: 侧向添加弯曲量使光照更圆润
    //   originalNorm: 原始法线保留, 用于反面翻转处理减少光照反转伪影
    // 着色:
    //   通过 _TopColor / _BottomColor 与 纹理 _GrassAlbedo / _GrassGloss 叠加
    //   使用 URP Main Light + 环境 SH (SampleSH)
    // 可扩展点:
    //   - 添加多光源、光照探针或自定义 Subsurface
    //   - 在顶点阶段输出顶端权重用于自发光或花籽等特效
    // =============================================================================================
    Properties
    {
        [Header(Shape)]
        _TaperAmount ("Taper Amount", Float) = 0
        _CurvedNormalAmount("Curved Normal Amount", Range(0, 5)) = 1
        _p1Offset ("p1Offset", Float) = 1
        _p2Offset ("p2Offset", Float) = 1

        [Header(Shading)]
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        _TopColor ("Top Color", Color) = (.25, .5, .5, 1)
        _BottomColor ("Bottom Color", Color) = (.25, .5, .5, 1)
        _GrassAlbedo("Grass albedo", 2D) = "white" {}
        _GrassGloss("Grass gloss", 2D) = "white" {}
        _Gloss("Grass gloss",Range(0, 1)) = 1.0

        [Header(Wind Animation)]
        _WaveAmplitude("Wave Amplitude", Float) = 1
        _WaveSpeed("Wave Speed", Float) = 1
        _SinOffsetRange("Phase Variation", Range(0, 10)) = 0.3
        _PushTipForward("Push Tip Forward", Range(0, 2)) = 0
        
        
        _ShadowIntensity("ShadowIntensity", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "GrassBlade.hlsl"
        ENDHLSL

        Pass
        {
            Name "Simple Grass Blade"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Cull Off

            HLSLPROGRAM
            // Required to compile gles3.0 on some platforms
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 2.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _WIND_SIMULATION
            
            #pragma vertex vert
            #pragma fragment frag


            struct Varyings
            {
                float4 positionCS : SV_POSITION; 
                float3 positionWS : TEXCOORD0; 
                float3 curvedNorm : TEXCOORD1; // 修改后的法线 (添加侧向弯曲)
                float3 originalNorm : TEXCOORD2; // 原始法线 (用于反面处理)
                float2 uv : TEXCOORD3; // 贴图 UV
                float t : TEXCOORD4; // 沿贝塞尔曲线的 0..1 位置 (驱动顶/底渐变)
            };


            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                CalculateGrassVertexData(IN, OUT.positionWS, OUT.originalNorm, OUT.curvedNorm, OUT.uv, OUT.t);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            half4 frag(Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                // 3. 纹理采样 + 顶底渐变
                float4 grassAlbedo = saturate(_GrassAlbedo.Sample(sampler_GrassAlbedo, i.uv));
                clip(grassAlbedo.a - _Cutoff);
                float4 grassCol = lerp(_BottomColor, _TopColor, i.t); // 基础渐变: 根 -> 顶
                float3 albedo = grassCol.rgb * grassAlbedo.rgb;
                
                
                // 1. 法线: 反面使用反射纠正法线方向，减少背面光照突变
                float3 n = isFrontFace
                               ? normalize(i.curvedNorm)
                               : -reflect(-normalize(i.curvedNorm), normalize(i.originalNorm));

                // 2. 主光 + 相机向量
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 v = normalize(GetCameraPositionWS() - i.positionWS);

                // 4. Gloss/粗糙度: 简单反转控制 (可扩展为金属度等)
                float gloss = ( _GrassGloss.Sample(sampler_GrassGloss, i.uv).r);
                gloss =lerp(gloss,0.96,_Gloss);

                // 5. 环境光 (球谐) + 主光 BRDF
                half3 GI = SampleSH(n);
                BRDFData brdfData;
                half alpha = 1;
                InitializeBRDFData(albedo, 0, half3(1, 1, 1), gloss, alpha, brdfData);
                float3 directBRDF = DirectBRDF(brdfData, n, mainLight.direction, v) * mainLight.color;

                // 6. 合成最终颜色 (可扩展: 侧光色调、次表面、风高光闪烁等)
                float3 finalColor = GI * albedo + directBRDF * (mainLight.shadowAttenuation * mainLight.
                    distanceAttenuation);
                return half4(finalColor, grassCol.a); // 保持 alpha 以便未来做透明/半裁剪
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags
            {
                "LightMode" = "ShadowCaster"
            }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 2.0

            // 引入 URP 阴影核心库，获取 GetShadowPositionHClip 函数
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

           struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD1;
                float t : TEXCOORD0; 
            };

            Varyings ShadowPassVertex(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS, originalNorm, curvedNorm;
                float2 uv;
                float t;

                CalculateGrassVertexData(IN, positionWS, originalNorm, curvedNorm, uv, t);
                
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.t = t; // 把高度权重传下去
                OUT.uv = uv;

                return OUT;
            }
            

            half4 ShadowPassFragment(Varyings input) : SV_TARGET
            {
                float alpha = _GrassAlbedo.Sample(sampler_GrassAlbedo, input.uv).a;
                clip(alpha - _Cutoff);
                
                // 1. 获取屏幕空间的像素坐标
                float2 screenPos = input.positionCS.xy;

                //使用 Bayer Matrix (有序抖动矩阵)
                // float dither = GetBayer4x4(screenPos);
                // 使用 IGN
                float dither = InterleavedGradientNoise(screenPos);
                float threshold = _ShadowIntensity * (1.0 - input.t * 0.3); 
                clip(threshold - dither); // 如果生成的噪点值大于阈值，则不写入阴影深度
                

                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }

            // DepthOnly 的标准配置: 开启深度写入，关闭颜色输出
            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 2.0

            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD1;
            };

            DepthVaryings DepthOnlyVertex(Attributes IN)
            {
                DepthVaryings OUT;
                float3 positionWS, originalNorm, curvedNorm;
                float2 uv;
                float t;
                
                CalculateGrassVertexData(IN, positionWS, originalNorm, curvedNorm, uv, t);

                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.uv =uv;
                return OUT;
            }

            half4 DepthOnlyFragment(DepthVaryings input) : SV_TARGET
            {
                float alpha = _GrassAlbedo.Sample(sampler_GrassAlbedo, input.uv).a;
                clip(alpha - _Cutoff);
                return 0; // 只写入深度，颜色输出 0
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/InternalErrorShader"
}