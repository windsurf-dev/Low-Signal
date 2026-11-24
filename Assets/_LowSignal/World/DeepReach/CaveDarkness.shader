Shader "Low Signal/Deep Reach/Cave Darkness"
{
    Properties
    {
        _BaseColor("Rock colour", Color) = (.15,.2,.2,1)
        _BaseMap("Base map", 2D) = "white" {}
        _InteriorAmbient("Interior ambient light", Range(0,1)) = .06
        _InteriorFill("Faint cave visibility", Range(0,.3)) = .10
        _ExitZ("Exit world Z", Float) = 155
        _TransitionLength("Exit light transition metres", Float) = 18
        _CaveFogColor("Interior fog colour", Color) = (.001,.002,.003,1)
        _Cull("Culling", Float) = 0
        _Cutoff("Alpha cutoff", Range(0,1)) = .5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "CaveForward"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                half _InteriorAmbient;
                half _InteriorFill;
                float _ExitZ;
                float _TransitionLength;
                half4 _CaveFogColor;
                half _Cutoff;
            CBUFFER_END
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;};
            struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;float2 uv:TEXCOORD2;half fog:TEXCOORD3;};
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions=GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS=positions.positionCS;output.positionWS=positions.positionWS;
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);
                output.uv=TRANSFORM_TEX(input.uv,_BaseMap);output.fog=ComputeFogFactor(positions.positionCS.z);
                return output;
            }
            half3 Contribution(Light light,half3 normal)
            {
                return light.color*(saturate(dot(normal,light.direction))*light.distanceAttenuation*light.shadowAttenuation);
            }
            half4 Frag(Varyings input):SV_Target
            {
                half3 normal=normalize(input.normalWS);
                half outside=smoothstep(_ExitZ-max(1,_TransitionLength),_ExitZ,input.positionWS.z);
                half3 lighting=SampleSH(normal)*lerp(_InteriorAmbient,1,outside);
                // A faint cool hemispheric fill keeps the floor and rock silhouettes readable.
                half hemisphere=lerp(.6h,1.0h,saturate(normal.y*.5h+.5h));
                lighting+=half3(.62h,.80h,.92h)*(_InteriorFill*hemisphere*(1-outside));
                Light sun=GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                lighting+=Contribution(sun,normal)*outside;
                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                    InputData inputData=(InputData)0;
                    inputData.positionWS=input.positionWS;
                    inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(input.positionCS);
                    uint count=GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(count)
                        Light local=GetAdditionalLight(lightIndex,input.positionWS,half4(1,1,1,1));
                        lighting+=Contribution(local,normal);
                    LIGHT_LOOP_END
                #endif
                half3 albedo=_BaseColor.rgb*SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb;
                half3 fog=lerp(_CaveFogColor.rgb,unity_FogColor.rgb,outside);
                return half4(MixFogColor(albedo*lighting,fog,input.fog),_BaseColor.a);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
