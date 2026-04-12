#ifndef VOXGEOFOL_VEGETATION_INDIRECT_COMMON_INCLUDED
#define VOXGEOFOL_VEGETATION_INDIRECT_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

struct VegetationInstanceData
{
    float4x4 objectToWorld;
    float4x4 worldToObject;
    uint packedLeafTint;
    uint padding0;
    uint padding1;
    uint padding2;
};

StructuredBuffer<VegetationInstanceData> _VegetationInstanceData;
StructuredBuffer<uint> _VegetationSlotPackedStarts;
uint _VegetationSlotIndex;
float3 _LightDirection;
float3 _LightPosition;

VegetationInstanceData LoadVegetationInstance(uint svInstanceId)
{
    return _VegetationInstanceData[_VegetationSlotPackedStarts[_VegetationSlotIndex] + svInstanceId];
}

float3 TransformVegetationPosition(float3 positionOS, VegetationInstanceData instanceData)
{
    return mul(instanceData.objectToWorld, float4(positionOS, 1.0f)).xyz;
}

float3 TransformVegetationNormal(float3 normalOS, VegetationInstanceData instanceData)
{
    return normalize(mul(transpose((float3x3)instanceData.worldToObject), normalOS));
}

float3 DecodePackedLeafTint(uint packedLeafTint)
{
    return float3(
        (packedLeafTint & 255u) / 255.0f,
        ((packedLeafTint >> 8u) & 255u) / 255.0f,
        ((packedLeafTint >> 16u) & 255u) / 255.0f);
}

float4 GetVegetationShadowPositionHClip(float3 positionOS, float3 normalOS, VegetationInstanceData instanceData)
{
    float3 positionWS = TransformVegetationPosition(positionOS, instanceData);
    float3 normalWS = TransformVegetationNormal(normalOS, instanceData);

#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif

    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
    return ApplyShadowClamping(positionCS);
}

#endif
