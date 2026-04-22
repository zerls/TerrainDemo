Shader "Zerl/Terrain/terrainBasic"
{
    Properties
    {
        _Color ("Main Color", Color) = (.25, .8, .4, 1)
        _MainTex ("Texture", 2D) = "white" {}
        _HeightMap ("Texture", 2D) = "black" {}
        _NormalMap ("Texture", 2D) = "normal" {}
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Terrain Patch"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5 // 必须开启 4.5 以支持 StructuredBuffer


            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature _MIP_DEBUG
            #pragma shader_feature _PATCH_DEBUG
            #pragma shader_feature _LOD_SEAMLESS
            #pragma shader_feature _NODE_DEBUG

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "terrain_common.hlsl"


            StructuredBuffer<PatchDescriptor> _VisiblePatchList;

            float4 _Color;
            TEXTURE2D(_MainTex);
            float4 _MainTex_ST;
            TEXTURE2D(_HeightMap);
            TEXTURE2D(_NormalMap);
            uniform float3 _TerrainSize;
            float4x4 _WorldToNormalMapMatrix;
            float _HeightOffset;

            struct Attributes
            {
                float4 vertex :POSITION;
                float2 uv : TEXCOORD0;
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID; // 自动获取当前是第几个 Instance
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 color: TEXCOORD1;
                uint lod : TEXCOORD2;
            };

            float3 TransformNormalToWorldSpace(float3 normal)
            {
                return SafeNormalize(mul(normal, (float3x3)_WorldToNormalMapMatrix));
            }


            float3 SampleNormal(float2 uv)
            {
                float3 normal;
                normal.xz = SAMPLE_TEXTURE2D_LOD(_NormalMap, sampler_LinearClamp, uv, 0).xy * 2 - 1;
                normal.y = sqrt(max(0, 1 - dot(normal.xz, normal.xz)));
                normal = TransformNormalToWorldSpace(normal);
                return normal;
            }

            void FixLODConnectSeam(inout float4 vertex, inout float2 uv, PatchDescriptor patch)
            {
                uint4 lodTrans = UnpackLodTrans(patch.lodTransPacked);
                
                if (all(lodTrans == 0))     return;
                

                uint2 vertexIndex = (uint2)floor((vertex.xz + PATCH_MESH_SIZE * 0.5 + 0.01) / PATCH_MESH_GRID_SIZE);
                float uvGridStrip = 1.0 / PATCH_MESH_GRID_COUNT;

                // 1. 批量算出 4 个方向的取模掩码 (Mask)
                //标量按向量位移，相当于同时算了四条边的 (2^n - 1)
                uint4 mask = (uint4(1u, 1u, 1u, 1u) << lodTrans) - 1u;

                // 2. 批量计算 4 个方向的取模结果 (modIndex)
                // 根据原始逻辑：左(x)依赖y, 下(y)依赖x, 右(z)依赖y, 上(w)依赖x
                uint4 modIndex = uint4(vertexIndex.y, vertexIndex.x, vertexIndex.y, vertexIndex.x) & mask;

                // 3. 构造边缘判断遮罩 (Edge Mask)：在边缘则为 1.0，不在边缘则为 0.0
                float4 onEdge = float4(
                    vertexIndex.x == 0 ? 1.0 : 0.0,
                    vertexIndex.y == 0 ? 1.0 : 0.0,
                    vertexIndex.x == PATCH_MESH_GRID_COUNT ? 1.0 : 0.0,
                    vertexIndex.y == PATCH_MESH_GRID_COUNT ? 1.0 : 0.0
                );

                // 4. 计算右边缘(z)和上边缘(w)特有的反向偏移量
                uint offsetZ = ((1u << lodTrans.z) - modIndex.z) * (modIndex.z > 0 ? 1u : 0u);
                uint offsetW = ((1u << lodTrans.w) - modIndex.w) * (modIndex.w > 0 ? 1u : 0u);

                // 5. 合并最终的位移系数 (只有处在对应边缘上的顶点，位移才不为 0)
                // X轴位移：受上边缘(w)正向影响，受下边缘(y)反向影响
                float finalOffsetX = (float)offsetW * onEdge.w - (float)modIndex.y * onEdge.y;
                // Z轴位移：受右边缘(z)正向影响，受左边缘(x)反向影响
                float finalOffsetZ = (float)offsetZ * onEdge.z - (float)modIndex.x * onEdge.x;

                // 6. 统一执行位移
                vertex.x += finalOffsetX * PATCH_MESH_GRID_SIZE;
                vertex.z += finalOffsetZ * PATCH_MESH_GRID_SIZE;

                uv.x += finalOffsetX * uvGridStrip;
                uv.y += finalOffsetZ * uvGridStrip;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float4 inVertex = IN.vertex;
                float2 uv = IN.uv;

                PatchDescriptor patch = _VisiblePatchList[IN.instanceID];
                #if _LOD_SEAMLESS
                FixLODConnectSeam(inVertex,uv,patch);
                #endif
                uint lod = patch.lod;
                float scale = (float)(1u << lod);
               
                uint packedTrans = patch.lodTransPacked;
                uint4 lodTrans = uint4(
                    packedTrans & 0xFF,
                    (packedTrans >> 8) & 0xFF,
                    (packedTrans >> 16) & 0xFF,
                    (packedTrans >> 24) & 0xFF
                );

                inVertex.xz *= scale;
                inVertex.xz += patch.position;
                OUT.lod =patch.lod;

                float2 heightUV = (inVertex.xz + (_TerrainSize.xz * 0.5) + 0.5) / (_TerrainSize.xz + 1);
                float height = SAMPLE_TEXTURE2D_LOD(_HeightMap, sampler_LinearClamp, heightUV, 0).r;
                inVertex.y = height * _TerrainSize.y;
                inVertex.y += _HeightOffset * _TerrainSize.y;

                float3 normal = SampleNormal(heightUV);
                Light light = GetMainLight();
                OUT.color = max(0.05, dot(light.direction, normal));
                OUT.color *= light.color;

                float4 vertex = TransformObjectToHClip(inVertex.xyz);
                OUT.positionCS = vertex;
                OUT.uv =TRANSFORM_TEX(IN.uv,_MainTex);

                #if _MIP_DEBUG
                static half3 debugColorForMip[6] = {
                half3(0, 1, 0),
                half3(0, 0, 1),
                half3(1, 0, 0),
                half3(1, 1, 0),
                half3(0, 1, 1),
                half3(1, 0, 1),
            };
                
                uint4 lodColorIndex = lod + lodTrans;
                OUT.color *= (debugColorForMip[lodColorIndex.x] + 
                debugColorForMip[lodColorIndex.y] +
                debugColorForMip[lodColorIndex.z] +
                debugColorForMip[lodColorIndex.w]) * 0.25;
                #endif

                return OUT;
            }

            half4 frag(Varyings IN, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                half4 col = half4(1.0, 1.0, 1.0, 1.0);
                // col = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_LinearRepeat, IN.uv, IN.lod) * _Color;
                col = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearRepeat, IN.uv) *_Color;

                col.rgb *= IN.color;
                return col;
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

            ZWrite On
            ColorMask 0 // 只需要深度，不需要输出颜色，节省带宽
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5

            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "terrain_common.hlsl"

            StructuredBuffer<PatchDescriptor> _VisiblePatchList;
            TEXTURE2D(_HeightMap);
            SAMPLER(sampler_LinearClamp);
            uniform float3 _TerrainSize;
            float _HeightOffset;

            struct Attributes
            {
                float4 vertex : POSITION;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthOnlyVertex(Attributes IN)
            {
                Varyings OUT;
                float4 inVertex = IN.vertex;
                PatchDescriptor patch = _VisiblePatchList[IN.instanceID];
                uint lod = patch.lod;
                float scale = (float)(1u << lod);

                inVertex.xz *= scale;
                inVertex.xz += patch.position;
                
                float2 heightUV = (inVertex.xz + (_TerrainSize.xz * 0.5) + 0.5) / (_TerrainSize.xz + 1);
                float height = SAMPLE_TEXTURE2D_LOD(_HeightMap, sampler_LinearClamp, heightUV, 0).r;

                inVertex.y = height * _TerrainSize.y;
                inVertex.y += _HeightOffset * _TerrainSize.y;
                
                OUT.positionCS = TransformObjectToHClip(inVertex.xyz);
                return OUT;
            }

            half4 DepthOnlyFragment(Varyings IN) : SV_TARGET
            {
                // 仅写入深度，直接返回 0 即可
                return 0;
            }
            ENDHLSL
        }
    }
}