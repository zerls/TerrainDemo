Shader "Zerls/GrassSystem/BezierBlade"
{
    Properties
    {
     [Header(Shape)]
        _Height("Height", Float) = 1.0
        _Tilt("Tilt", Float) = 0.9
        _BladeWidth("Blade Width", Float) = 0.1
        _TaperAmount("Taper Amount", Float) = 0
        _p1Offset("P1 Offset", Float) = 1
        _p2Offset("P2 Offset", Float) = 1
        _CurvedNormalAmount("Curved Normal Amount", Range(0,5)) = 1
        
        [Header(Shading) ]
        _TopColor("Top Color", Color) = (0.25,.5,0.5,1)
        _BottomColor("Bottom Color", Color) = (0.25,0.5,0.5,1)
        _GrassAlbedo("Grass Albedo", 2D) = "white" {}
        _GrassGloss("Grass Gloss",2D) = "white" {}
    }
    SubShader
    {
        Tags {"RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline"}

        Pass
        {
            Name "Simple Grass Blade"
//            Tags {"LightMode" ="UniversalForward"}
        
            Cull Off
            
            HLSLPROGRAM
            //Required to compile  GLES3.0 on some platforms (e.g. Android)
            // #pragma  prefer_hlslcc gles
            // #pragma exclude_renderers d3d11_9x
            // #pragma  target 2.0
            
            #pragma  vertex vert
            #pragma  fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CubicBezier.hlsl"

            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            CBUFFER_START(UnityPerMaterial)
                float _Height;
                float _Tilt;
                float _BladeWidth;
                float _TaperAmount;
                float _p1Offset;
                float _p2Offset;
                float _CurvedNormalAmount;
            float4 _TopColor;
            float4 _BottomColor;

            CBUFFER_END
                TEXTURE2D(_GrassAlbedo);
                SAMPLER(sampler_GrassAlbedo);
                TEXTURE2D(_GrassGloss);
                SAMPLER(sampler_GrassGloss);
            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR; 
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
           
                float3 positionWS : TEXCOORD0;
                float3 curvedNormal : TEXCOORD1;
                float3 originNormal : TEXCOORD2;
                float2 uv:TEXCOORD3;
                float t:TEXCOORD4;
                // float2 uv : TEXCOORD0;
                // float4 color : COLOR;
            };

            float3 GetP0()
            {
                return float3(0,0,0);
            }

            void GetP1P2(float3 p0,float3 p3,out float3 p1 ,out float3 p2)
            {
                p1 =lerp(p0,p3,0.33);
                p2=lerp(p0,p3,0.66);

                float3 bladeDir = normalize(p3 - p0);
                float3 bezCtrlOffsetDir =normalize(cross(bladeDir,float3(0,0,1)));

                p1 += bezCtrlOffsetDir * _p1Offset;
                p2 += bezCtrlOffsetDir * _p2Offset;
            }


            float3 GetP3(float height ,float tilt)
            {
                float p3y = saturate(tilt) * height;
                float p3x = sqrt(height *height -p3y*p3y);
                
                return float3(-p3x,p3y,0);
            }
            
            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 p0 =GetP0();
                float3 p3 =GetP3(_Height,_Tilt);

                float3 p1=float3(0,0,0);
                float3 p2=float3(0,0,0);
                GetP1P2(p0,p3,p1,p2);

                float t=IN.color.r;
                float3 centerPos =CubicBezier(p0,p1,p2,p3,t);

                float width =_BladeWidth * (1 - t * _TaperAmount);
                float side =IN.color.g * 2 - 1; // -1 or 1
                float3 vertexPos = centerPos + float3(0,0,width * side);

                OUT.positionCS = TransformObjectToHClip(vertexPos);
                float3 tangnet = CubicBezierTangent(p0,p1,p2,p3,t);
                float3 bitangent = float3(0,0,1);
                float3 normal = normalize(cross(tangnet,bitangent));

                float3 curvedNormal =normal;
                curvedNormal.z +=side *_CurvedNormalAmount;
                curvedNormal =normalize(curvedNormal);
                
                OUT.originNormal = TransformObjectToWorldNormal(curvedNormal);
                OUT.positionWS = TransformObjectToWorld(vertexPos);
                OUT.curvedNormal=TransformObjectToWorldNormal(normal);
                OUT.uv =IN.uv;
                // OUT.color =IN.color;
                OUT.t =t;

                // VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
				 // OUT.positionCS = positionInputs.positionCS;
                // OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                // OUT.positionCS = TransformObjectToHClip(IN.vertex);
                return OUT;
            }
            half4 frag(Varyings IN,bool isFrontFace :SV_IsFrontFace ) : SV_Target
            {
                 // return half4(abs(IN.positionCS.xyz), 1); // 用位置当颜色

                //Calculate normal
                float3 normalWS =isFrontFace ? normalize(IN.curvedNormal) : -reflect(-normalize(IN.curvedNormal),normalize(IN.originNormal));
                Light mainLight =GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                // float shadow=mainLight.shadowAttenuation;
                // return half4(shadow,shadow,shadow,1);
                float3 lightDir = normalize(mainLight.direction);
                float3 viewDir =normalize(GetCameraPositionWS() - IN.positionWS);
                float halfDir =normalize(lightDir + viewDir);

                float3 grassAlbedo=saturate(_GrassAlbedo.Sample(sampler_GrassAlbedo,IN.uv));

                float4 grassCol =lerp(_BottomColor,_TopColor,IN.t);

                float3 albedo =grassCol.rgb *grassAlbedo;
                // return half4(albedo,1);
                // return 
                float gloss =(1-_GrassGloss.Sample(sampler_GrassGloss,IN.uv).r) *0.2;

                half3 GI =SampleSH(normalWS);

                BRDFData brdfData;
                half alpha =1;

                InitializeBRDFData(albedo,0,half3(1,1,1),gloss,alpha,brdfData);
                float3 directBRDF =DirectBRDF(brdfData,normalWS,mainLight.direction,viewDir)*mainLight.color;

                //Final color calculation
                float3 finalColor =GI *albedo + directBRDF * (mainLight.shadowAttenuation*mainLight.distanceAttenuation);

                float4 col;
                col =float4(finalColor,grassCol.a); //Alpha from grassCol
                
                return half4(col);
            }
            ENDHLSL
        }
    }
    Fallback "Hidden/InternalErrorShader"
}