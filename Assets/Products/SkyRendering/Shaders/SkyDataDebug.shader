Shader "Hidden/SkyDataDebug"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        // 全屏特效标准设置：关闭深度写入和剔除，永远通过深度测试
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "SkyDataDebug"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "sky_shared_struct.hlsl"

            // ==========================================
            // 顶点与片元逻辑
            // ==========================================
            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            // 声明 URP 14 全屏 Pass 自动传入的屏幕纹理
            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;

                // 左上角 (x < 0.5, y > 0.5)：测试嵌套的 SkyInfo (天空面板颜色)
                if (uv.x < 0.5 && uv.y >= 0.5) 
                {
                    return half4(frameData.sky.color, 1.0);
                }
                
                // 右上角 (x > 0.5, y > 0.5)：测试相机世界坐标
                if (uv.x >= 0.5 && uv.y >= 0.5) 
                {
                    return half4(frac(frameData.camWorldPos.xyz / 10.0), 1.0);
                }

                // 左下角 (x < 0.5, y < 0.5)：测试基础时间浮点数 (正弦波红色呼吸)
                if (uv.x < 0.5 && uv.y < 0.5) 
                {
                    float sinTime = (frameData.appTime.y + 1.0) * 0.5; // 映射到 0~1
                    return half4(sinTime, 0.0, 0.0, 1.0);
                }

                // 右下角 (x > 0.5, y < 0.5)：显示真实的屏幕画面
                // 采样 _BlitTexture 即可获取原本该位置渲染的画面内容
                half4 screenColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv*2.0 - 1.0);
                return screenColor;
            }
            ENDHLSL
        }
    }
}