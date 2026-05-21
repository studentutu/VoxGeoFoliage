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
    float4 wind;
};

#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && defined(_VOXGEOFOL_INDIRECT_RENDERING)
#define VOXGEOFOL_USE_INDIRECT_INSTANCE_BUFFER 1
#endif

#if defined(VOXGEOFOL_USE_INDIRECT_INSTANCE_BUFFER)
StructuredBuffer<VegetationInstanceData> _VegetationInstanceData;
#endif

float3 _LightDirection;
float3 _LightPosition;
int _VegetationInstanceDataBaseOffset;
float _VegetationWindStrength;
float _VegetationWindFrequency;
float4 _VegetationWindDirection;
float4 _VegetationLeafFlutterSettings;

void SetupVegetation()
{
}

VegetationInstanceData LoadVegetationInstance()
{
#if defined(VOXGEOFOL_USE_INDIRECT_INSTANCE_BUFFER)
    return _VegetationInstanceData[(uint)_VegetationInstanceDataBaseOffset + unity_InstanceID];
#else
    VegetationInstanceData instanceData;
    instanceData.objectToWorld = GetObjectToWorldMatrix();
    instanceData.worldToObject = GetWorldToObjectMatrix();
    instanceData.packedLeafTint = 16777215u;
    instanceData.padding0 = 0u;
    instanceData.padding1 = 0u;
    instanceData.padding2 = 0u;
    instanceData.wind = float4(0.0f, 0.0f, 0.0f, 0.0f);
    return instanceData;
#endif
}

float3 TransformVegetationPosition(float3 positionOS, VegetationInstanceData instanceData)
{
    float3 positionWS = mul(instanceData.objectToWorld, float4(positionOS, 1.0f)).xyz;
    float height01 = saturate((positionWS.y - instanceData.wind.w) * 0.08f);
    float2 windXZ = _VegetationWindDirection.xz;
    float windLength = max(length(windXZ), 0.0001f);
    float2 windDirection = windXZ / windLength;
    float trunkPhase = instanceData.wind.x * 6.28318530718f +
                       _TimeParameters.x * _VegetationWindFrequency;
    float trunkBend = sin(trunkPhase) * instanceData.wind.y * height01 * height01;
    float offset = _VegetationWindStrength * trunkBend;
    positionWS.xz += windDirection * offset;
    return positionWS;
}

float3 ApplyVegetationLeafFlutter(float3 positionWS, float3 normalWS, float2 uv, VegetationInstanceData instanceData)
{
    float leafWeight = saturate(instanceData.wind.z);
    float height01 = saturate((positionWS.y - instanceData.wind.w) * 0.08f);
    float2 windXZ = _VegetationWindDirection.xz;
    float windLength = max(length(windXZ), 0.0001f);
    float2 windDirection = windXZ / windLength;
    float trunkPhase = instanceData.wind.x * 6.28318530718f +
                       _TimeParameters.x * _VegetationWindFrequency;
    float spatialPhase = dot(positionWS.xz, float2(windDirection.y, -windDirection.x)) *
                         max(_VegetationLeafFlutterSettings.z, 0.0f);
    float uvPhase = dot(uv, float2(17.17f, 31.31f));
    float leafPhase = trunkPhase * max(_VegetationLeafFlutterSettings.y, 0.0f) + spatialPhase + uvPhase;
    float flutter = sin(leafPhase) +
                    sin(leafPhase * 1.37f + 1.9f) * max(_VegetationLeafFlutterSettings.w, 0.0f);
    float3 crossWindWS = float3(-windDirection.y, 0.0f, windDirection.x);
    float3 flutterDirectionWS = normalize(crossWindWS * 0.75f + normalWS * 0.25f);
    float amplitude = _VegetationWindStrength * leafWeight * height01 * height01 *
                      max(_VegetationLeafFlutterSettings.x, 0.0f);
    return positionWS + flutterDirectionWS * flutter * amplitude;
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

float4 GetVegetationShadowPositionHClipFromWorld(float3 positionWS, float3 normalWS)
{
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif

    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
    return ApplyShadowClamping(positionCS);
}

float4 GetVegetationShadowPositionHClip(float3 positionOS, float3 normalOS, VegetationInstanceData instanceData)
{
    float3 positionWS = TransformVegetationPosition(positionOS, instanceData);
    float3 normalWS = TransformVegetationNormal(normalOS, instanceData);
    return GetVegetationShadowPositionHClipFromWorld(positionWS, normalWS);
}

float4 GetVegetationCanopyShadowPositionHClip(float3 positionOS, float3 normalOS, float2 uv, VegetationInstanceData instanceData)
{
    float3 positionWS = TransformVegetationPosition(positionOS, instanceData);
    float3 normalWS = TransformVegetationNormal(normalOS, instanceData);
    positionWS = ApplyVegetationLeafFlutter(positionWS, normalWS, uv, instanceData);
    return GetVegetationShadowPositionHClipFromWorld(positionWS, normalWS);
}

#endif
