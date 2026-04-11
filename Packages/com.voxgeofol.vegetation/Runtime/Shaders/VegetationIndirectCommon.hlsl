#ifndef VOXGEOFOL_VEGETATION_INDIRECT_COMMON_INCLUDED
#define VOXGEOFOL_VEGETATION_INDIRECT_COMMON_INCLUDED

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
uint _VegetationInstanceStart;

VegetationInstanceData LoadVegetationInstance(uint svInstanceId)
{
    return _VegetationInstanceData[_VegetationInstanceStart + svInstanceId];
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

#endif
