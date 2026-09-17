// 그림자만 그리는 바닥 (사양서 v1.1 2-3 "바닥 지형 - 그림자 수신 전용").
// 창이 유리라 바닥 자체는 보이면 안 되고, 주 광원이 가린 곳만 어둡게 얹는다.
// 가장자리는 월드 길이 기준으로 흐려서 바닥의 경계가 드러나지 않게 한다.
Shader "SAIUN/ShadowCatcher"
{
    Properties
    {
        _ShadowColor ("Shadow Color", Color) = (0.141, 0.22, 0.173, 1)
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.55
        _FadeWidth ("Edge Fade Width (world)", Float) = 0.8
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // 알파 채널도 쌓아야 투명 창에서 그림자가 사라지지 않는다.
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vertex
            #pragma fragment Fragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                half _ShadowStrength;
                float _FadeWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float2 size : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;

                // 배율을 월드 길이로 넘겨서, 바닥 크기가 달라도 흐려지는 폭이 같게 한다.
                // 가장자리 거리는 abs가 들어가 선형이 아니므로 정점이 아니라 픽셀에서 계산한다.
                output.uv = input.uv;
                output.size = float2(
                    length(GetObjectToWorldMatrix()._m00_m10_m20),
                    length(GetObjectToWorldMatrix()._m01_m11_m21));
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord, input.positionWS, half4(1, 1, 1, 1));

                float2 edgeDistance = (0.5 - abs(input.uv - 0.5)) * input.size;
                float edge = min(edgeDistance.x, edgeDistance.y);
                half fade = saturate(edge / max(_FadeWidth, 1e-4));

                half alpha = (1.0 - mainLight.shadowAttenuation) * _ShadowStrength * fade;
                return half4(_ShadowColor.rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
