Shader "VoxGeoFol/Vegetation/TrunkLit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.2
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "VegetationIndirectCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _BaseMap_ST;
            float _Smoothness;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float fogCoord : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                VegetationInstanceData instanceData = LoadVegetationInstance(input.instanceID);

                Varyings output;
                float3 positionWS = TransformVegetationPosition(input.positionOS, instanceData);
                output.normalWS = TransformVegetationNormal(input.normalOS, instanceData);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                float3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                float direct = saturate(dot(normalWS, mainLight.direction));
                float3 ambient = SampleSH(normalWS);
                float3 color = albedo * (ambient + (mainLight.color * direct));
                color = MixFog(color, input.fogCoord);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
