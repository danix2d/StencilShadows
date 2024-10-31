Shader "SharpShadow/VisualizeShadows"
{
    Properties
    {
        _ShadowIntensity ("Shadow Intensity", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Tags { "LightMode"="LightweightForward" }

            Stencil
            {
                Ref 0
                Comp NotEqual
                Pass Keep
            }

            Cull Off
            ZWrite Off
            Blend OneMinusSrcAlpha OneMinusSrcAlpha
          //  Blend One Zero

            HLSLPROGRAM
            #pragma target 2.0
            
            #pragma multi_compile_instancing
            #pragma vertex VisualizeShadowVertex
            #pragma fragment VisualizeShadowFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            half _ShadowIntensity;

            struct Attributes
            {
                float4 position : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 worldNormal : TEXCOORD0;  // Use TEXCOORD0 for world normal
            };

            Varyings VisualizeShadowVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.position = TransformObjectToHClip(input.position.xyz);
                
                
                // Calculate world normal
                float3 worldNormal = normalize(mul((float3x3)unity_ObjectToWorld, input.normal));
                output.worldNormal = worldNormal;

                return output;
            }

            half4 VisualizeShadowFragment(Varyings input) : SV_Target
            {
                    return half4(0.0, 0.0, 0.0, _ShadowIntensity);
            }
            ENDHLSL
        }
    }
}
