// 그림자만 그리는 바닥 (사양서 v1.1 2-3 "바닥 지형 - 그림자 수신 전용").
// 창이 유리라 바닥 자체는 보이면 안 되고, 주 광원이 가린 곳만 어둡게 얹는다.
// 가장자리는 월드 길이 기준으로 흐려서 바닥의 경계가 드러나지 않게 한다.
// 뒤에 그려진 하늘·바다(Sky.shader)와 맞춘다: 지평선 위(하늘)에는 그림자를 얹지 않고, 바다 위는 옅게, 모래밭은 그대로 얹는다.
Shader "SAIUN/ShadowCatcher"
{
    Properties
    {
        _ShadowColor ("Shadow Color", Color) = (0.141, 0.22, 0.173, 1)
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.55
        _FadeWidth ("Edge Fade Width (world)", Float) = 0.8
        _SeaShadow ("Shadow On Sea (ratio)", Range(0, 1)) = 0.3
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
                half _SeaShadow;
            CBUFFER_END

            // Sky.shader의 눈·바다와 같은 값이다(view_common·sea_common). 그쪽을 바꾸면 같이 바꾼다.
            static const float ViewPitch = 14.0;
            static const float ViewFocal = 0.95;
            static const float SeaEye = 0.012;
            static const float Shore = 0.118;
            static const float ShoreBend = 0.016;
            static const float ShoreSoftness = 0.004;
            static const float HorizonSoftness = 0.01;

            // 화면 자리(0~1, 아래가 0)의 바닥에 그림자를 얼마나 얹을지: 하늘 0, 바다 _SeaShadow, 모래밭 1.
            half GroundMask(float2 screenUv)
            {
                float2 uv = (screenUv - 0.5) * float2(_ScreenParams.x / _ScreenParams.y, 1.0);
                float pitch = radians(ViewPitch);
                float3 rd = normalize(float3(uv.x, uv.y, ViewFocal));
                rd = float3(rd.x, rd.y * cos(pitch) + rd.z * sin(pitch), -rd.y * sin(pitch) + rd.z * cos(pitch));
                if (rd.y >= 0.0) return 0.0;
                float t = SeaEye / -rd.y;
                float dist = t * length(rd.xz);
                float shore = Shore + ShoreBend * sin(rd.x * t * 6.0 + 1.3);
                half sand = 1.0 - smoothstep(shore - ShoreSoftness, shore + ShoreSoftness, dist);
                return smoothstep(0.0, -HorizonSoftness, rd.y) * lerp(_SeaShadow, 1.0, sand);
            }

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

                // 화면 자리: 클립 좌표를 아래가 0인 화면 좌표로(렌더 텍스처에 뒤집어 그릴 때도 같게).
                float4 clip = TransformWorldToHClip(input.positionWS);
                float2 screenUv = float2(clip.x, clip.y * _ProjectionParams.x) / clip.w * 0.5 + 0.5;

                half alpha = (1.0 - mainLight.shadowAttenuation) * _ShadowStrength * fade * GroundMask(screenUv);
                return half4(_ShadowColor.rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
