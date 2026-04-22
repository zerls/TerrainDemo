#ifndef WIND_SYSTEM_INCLUDED
#define WIND_SYSTEM_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Computer Shader
TEXTURE3D(WindVelocityData);
SAMPLER(sampler_WindVelocityData);

TEXTURE3D(WindNoise);
SAMPLER(sampler_WindNoise);

uniform float OverallPower;
uniform float4 VolumeSize;
uniform float4 VolumePosOffset;
uniform float3 GlobalAmbientWind;
uniform float3 WindNoiseRcpTexSize;
uniform float3 WindNoiseOffset;
uniform float3 WindNoiseUVScale;
uniform float3 WindNoiseScale;

#define WindPIMax 2.513274122871834590768 // 0.8 * PI

void EvaluateWindBending(float heightMask, float3 normalWS, inout float3 positionWS, out float3 windData)
{
    
        float3 uvw = (positionWS - VolumePosOffset.xyz) / VolumeSize.xyz;
        windData = SAMPLE_TEXTURE3D_LOD(WindVelocityData, sampler_WindVelocityData, uvw, 0);
        

        uvw = max(0, abs(uvw - 0.5) - 0.5);
        half uvwLen = length(uvw) * 10.0;
        half fadeDis = saturate(1 - uvwLen);
    
        float3 windFinal = windData * fadeDis ;
        windFinal *= OverallPower;
        float windStrength = length(windFinal);
    

        float rad = clamp(windStrength * PI * 0.9, -WindPIMax, WindPIMax) / 2.0;
        float x, y; //弯曲后,x为单位球在wind方向计量，y为grassUp方向计量
        sincos(rad, x, y);
    
        float3 windDir = SafeNormalize(windFinal - dot(windFinal, normalWS) * normalWS) ;
        float3 windedPos = x * windDir + y * normalWS;
        float3 posOffset = (windedPos-normalWS) * heightMask ;

        positionWS += posOffset;

    } 


#endif