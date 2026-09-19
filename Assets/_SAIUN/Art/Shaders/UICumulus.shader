// 뭉게구름(적운)을 절차적으로 그리는 UI 셰이더 (사양서 v1.1 10장, P2-05).
// 봉우리 몇 개를 겹친 덩어리에 난류 노이즈로 가장자리를 흔들고, 밑면은 평평하게 자른다.
// 노이즈가 시간에 따라 흘러 모양이 천천히 피어오르고 무너진다.
// 해 쪽으로 몇 걸음 밀도를 쌓아 자기 그림자를 만들고, 빛 색·그늘 색으로 칠한다(광원에 따라 색이 바뀐다).
// 구름마다 모양이 다르도록 RawImage.uvRect의 x 정수부를 시드로 쓴다.
// 시계 글자 바로 뒤(_ClearRect0·1, 화면 비율 좌표)에서는 옅어져 밝은 글자가 흰 구름에 묻히지 않는다.
Shader "SAIUN/UI/Cumulus"
{
    Properties
    {
        [PerRendererData] _MainTex ("Unused", 2D) = "white" {}
        _SunColor ("Sun Color", Color) = (1, 0.96, 0.9, 1)
        _ShadowColor ("Shadow Color", Color) = (0.55, 0.6, 0.66, 1)
        _StormColor ("Storm Color", Color) = (0.2, 0.24, 0.23, 1)
        _SunDir ("Screen Direction To Sun (xy)", Vector) = (0.4, 0.9, 0, 0)
        _Storm ("Storm", Range(0, 1)) = 0
        _Evolve ("Evolve Speed", Float) = 0.035
        _Softness ("Edge Softness", Range(0.02, 0.6)) = 0.22
        _Absorption ("Absorption", Range(0, 4)) = 1.1
        _Silver ("Silver Lining", Range(0, 2)) = 0.6
        _ClearRect0 ("Keep Clear Rect 0 (screen xMin yMin xMax yMax)", Vector) = (-10, -10, -10, -10)
        _ClearRect1 ("Keep Clear Rect 1 (screen xMin yMin xMax yMax)", Vector) = (-10, -10, -10, -10)
        _ClearOpacity ("Opacity Behind Text", Range(0, 1)) = 0.35
        _ClearSoftness ("Keep Clear Softness (px)", Float) = 14
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }

        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "UnityCG.cginc"

            fixed4 _SunColor;
            fixed4 _ShadowColor;
            fixed4 _StormColor;
            float4 _SunDir;
            float _Storm;
            float _Evolve;
            float _Softness;
            float _Absorption;
            float _Silver;
            float4 _ClearRect0;
            float4 _ClearRect1;
            float _ClearOpacity;
            float _ClearSoftness;

            struct Attributes
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 screen : TEXCOORD1;
            };

            float Hash(float n)
            {
                return frac(sin(n * 12.9898 + 4.1414) * 43758.5453);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float n = i.x + i.y * 57.0;
                float a = Hash(n);
                float b = Hash(n + 1.0);
                float c = Hash(n + 57.0);
                float d = Hash(n + 58.0);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 4옥타브 난류. 옥타브마다 조금 돌려 격자 무늬가 드러나지 않게 한다.
            float Fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                const float2x2 turn = float2x2(0.8, -0.6, 0.6, 0.8);
                for (int octave = 0; octave < 4; octave++)
                {
                    value += amplitude * Noise(p);
                    p = mul(turn, p) * 2.03;
                    amplitude *= 0.5;
                }
                return value;
            }

            // 구름 안쪽이 양수인 밀도. p.x는 −1~1, p.y는 0(밑면)~1.
            float Density(float2 p, float seed, float time)
            {
                float body = -1.0;
                for (int i = 0; i < 5; i++)
                {
                    float fi = i;
                    float middle = 1.0 - abs(fi - 2.0) / 2.0;   // 가운데 봉우리가 가장 높고 크다
                    float2 center = float2(
                        lerp(-0.6, 0.6, fi / 4.0) + (Hash(seed * 7.1 + fi * 1.7) - 0.5) * 0.22,
                        0.2 + Hash(seed * 3.3 + fi * 3.1) * 0.18 + middle * 0.2);
                    float radius = 0.22 + Hash(seed * 5.7 + fi * 5.3) * 0.12 + middle * 0.12;
                    body = max(body, 1.0 - length(p - center) / radius);
                }

                // 가장자리를 난류로 흔들고, 시간이 흐르면 천천히 피어오른다.
                float2 drift = float2(time, time * 0.4);
                float turbulence = Fbm(p * 2.6 + float2(seed * 7.3, seed * 3.1) + drift);
                float detail = Fbm(p * 6.0 - drift * 1.7 + seed);
                float density = body + (turbulence - 0.5) * 0.6 + (detail - 0.5) * 0.18;

                // 뭉게구름은 밑면이 평평하다.
                density *= smoothstep(0.0, 0.12, p.y);
                return density;
            }

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.color = input.color;
                output.uv = input.uv;
                output.screen = ComputeScreenPos(output.position);
                return output;
            }

            // 글자 영역 안이면 1, 영역 밖으로 _ClearSoftness의 두 배만큼 가면 0.
            float ClearMask(float2 screen, float4 rect)
            {
                float2 outside = max(rect.xy - screen, screen - rect.zw) * _ScreenParams.xy;
                return 1.0 - smoothstep(0.0, _ClearSoftness * 2.0, max(outside.x, outside.y));
            }

            fixed4 Fragment(Varyings input) : SV_Target
            {
                float seed = floor(input.uv.x + 0.0001);
                float2 local = float2(input.uv.x - seed, input.uv.y);
                float2 p = float2(local.x * 2.0 - 1.0, local.y);   // 사각형이 2:1이라 x와 y의 픽셀 척도가 같다
                float time = _Time.y * _Evolve * (0.7 + Hash(seed) * 0.6);

                float density = Density(p, seed, time);
                float alpha = smoothstep(0.0, _Softness, density);
                if (alpha <= 0.001) discard;

                // 해 쪽으로 걸어가며 쌓인 밀도만큼 빛이 줄어든다.
                float2 toSun = normalize(_SunDir.xy + float2(0.0001, 0.0));
                float optical = 0.0;
                for (int k = 1; k <= 4; k++)
                {
                    optical += saturate(Density(p + toSun * (0.075 * k), seed, time));
                }
                float lit = exp(-optical * _Absorption);

                // 밑면 쪽은 빛이 덜 든다.
                float occlusion = lerp(0.72, 1.0, saturate(p.y * 1.8));
                float3 color = lerp(_ShadowColor.rgb, _SunColor.rgb, lit) * occlusion;

                // 해를 향한 얇은 가장자리가 밝게 빛난다(은빛 테).
                float rim = (1.0 - smoothstep(0.0, _Softness * 1.6, density)) * lit;
                color += _SunColor.rgb * rim * _Silver;

                // 먹구름: 어둡고 무겁게. 명암과 가장자리 빛은 조금 남겨 덩어리가 읽히게 한다.
                float3 storm = _StormColor.rgb * lerp(0.55, 1.35, lit) * occlusion + _SunColor.rgb * rim * _Silver * 0.3;
                color = lerp(color, storm, _Storm);

                float2 screen = input.screen.xy / input.screen.w;
                float behindText = max(ClearMask(screen, _ClearRect0), ClearMask(screen, _ClearRect1));
                alpha *= lerp(1.0, _ClearOpacity, behindText);

                return fixed4(color * input.color.rgb, alpha * input.color.a);
            }
            ENDCG
        }
    }

    Fallback Off
}
