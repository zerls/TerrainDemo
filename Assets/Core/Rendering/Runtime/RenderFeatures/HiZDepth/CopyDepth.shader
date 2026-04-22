Shader "Hidden/HiZ/CopyDepth"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "CopyDepth"
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5 // [优化] 根据平台能力，如果支持 Gather 则使用更高的 Shader Model

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_FLOAT(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);
            float4 _CameraDepthTexture_TexelSize;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.texcoord = input.texcoord;
                return output;
            }

            float frag(Varyings input) : SV_Target
            {
                // 确保采样点在 2x2 像素交界处
                float2 uv = input.texcoord + _CameraDepthTexture_TexelSize.xy * 0.5;

                // [优化] 硬件级 Gather 聚合采样
                #if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
                    float4 depths = _CameraDepthTexture.GatherRed(sampler_CameraDepthTexture, uv);
                #else
                    float2 offset = _CameraDepthTexture_TexelSize.xy * 0.5;
                    float4 depths = float4(
                        SAMPLE_TEXTURE2D_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, input.texcoord + offset, 0).r,
                        SAMPLE_TEXTURE2D_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, input.texcoord - offset, 0).r,
                        SAMPLE_TEXTURE2D_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, input.texcoord + float2(offset.x, -offset.y), 0).r,
                        SAMPLE_TEXTURE2D_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, input.texcoord + float2(-offset.x, offset.y), 0).r
                    );
                #endif

                #if UNITY_REVERSED_Z
                return min(min(depths.x, depths.y), min(depths.z, depths.w));
                #else
                return max(max(depths.x, depths.y), max(depths.z, depths.w));
                #endif
            }
            ENDHLSL
        }
    }
}