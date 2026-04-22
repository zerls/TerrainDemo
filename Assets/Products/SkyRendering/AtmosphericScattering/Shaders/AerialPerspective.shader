Shader "Zerls/Atmosphere/AerialPerspective"
{
    Properties
    {
        _MainTex ("_MainTex", 2D) = "white" {}
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"
        }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "AerialPerspective"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #pragma multi_compile_local _ _USE_FAST_SRGB_LINEAR_CONVERSION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Helper.hlsl"
            #include "Scattering.hlsl"
            #include "Atmosphere.hlsl"


            TEXTURE2D(_aerialPerspectiveLut);       SAMPLER(sampler_aerialPerspectiveLut);
            
            float _AerialPerspectiveDistance;
            float4 _AerialPerspectiveVoxelSize;

            float4 GetFragmentWorldPos(float2 screenPos)
            {
                float sceneRawDepth = SampleSceneDepth(screenPos);
                float4 ndc = float4(screenPos.x * 2 - 1, screenPos.y * 2 - 1, sceneRawDepth, 1);
                #if UNITY_UV_STARTS_AT_TOP
                ndc.y *= -1;
                #endif
                float4 worldPos = mul(UNITY_MATRIX_I_VP, ndc);
                worldPos /= worldPos.w;

                return worldPos;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float3 sceneColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).rgb;

                // 天空 mask
                float sceneRawDepth = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                if (sceneRawDepth == 0.0f) return float4(sceneColor, 1.0);
                #else
                if(sceneRawDepth == 1.0f) return float4(sceneColor, 1.0);
                #endif

                // 世界坐标计算
                float3 worldPos = GetFragmentWorldPos(input.texcoord).xyz;
                float3 eyePos = _WorldSpaceCameraPos.xyz;
                float dis = length(worldPos - eyePos);
                float3 viewDir = normalize(worldPos - eyePos);

                // 体素 slice 计算
                float dis01 = saturate(dis / _AerialPerspectiveDistance);
                float dis0Z = dis01 * (_AerialPerspectiveVoxelSize.z - 1); // [0 ~ SizeZ-1]
                float slice = floor(dis0Z);
                float nextSlice = min(slice + 1, _AerialPerspectiveVoxelSize.z - 1);
                float lerpFactor = dis0Z - floor(dis0Z);

                uv.x /= _AerialPerspectiveVoxelSize.x;

                // 采样 AerialPerspectiveVoxel
                float2 uv1 = float2(uv.x + slice / _AerialPerspectiveVoxelSize.z, uv.y);
                float2 uv2 = float2(uv.x + nextSlice / _AerialPerspectiveVoxelSize.z, uv.y);

                float4 data1 = SAMPLE_TEXTURE2D(_aerialPerspectiveLut, sampler_LinearClamp, uv1);
                float4 data2 = SAMPLE_TEXTURE2D(_aerialPerspectiveLut, sampler_LinearClamp, uv2);
                float4 data = lerp(data1, data2, lerpFactor);

                float3 inScattering = data.xyz;
                float transmittance = data.w;

                return float4(sceneColor * transmittance + inScattering, 1.0);
            }
            ENDHLSL
        }
    }
}