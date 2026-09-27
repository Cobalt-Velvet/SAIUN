// 구름을 절차적으로 그리는 UI 셰이더 (사양서 v1.1 10장, P2-05, 2026-09-27 웅대적운·채운 추가).
// 한 셰이더가 세 종류를 그린다. RawImage.uvRect의 x 정수부가 모양 시드, y 정수부가 종류다.
//   0 적운(뭉게구름): 봉우리 몇 개를 겹친 덩어리, 밑면이 평평하다. 사각형 2:1.
//   1 웅대적운(적운 congestus): 넓은 밑동 위로 층층이 쌓여 솟는 탑, 꼭대기는 콜리플라워처럼 부푼다. 사각형 1:1.3.
//     꼭짓점 색 R(자람)만큼 탑이 위로 자라고, 꼭대기일수록 난류가 잘고 또렷하다.
//   2 갓구름(pileus): 탑 꼭대기를 덮고 양옆으로 완만하게 흘러내리는 얇고 매끈한 너울. 사각형 2:1.
// 채운(구름 무지갯빛): 얇은 구름 가장자리에서 해 쪽을 향한 곳에 파스텔 무지개 띠가 두께의 등고선을 따라 흐른다.
//   꼭짓점 색 G가 세기다. 갓구름은 몸 전체가 얇아 가장 곱게 빛난다. 먹구름에서는 사라진다.
// 노이즈가 시간에 따라 흘러 모양이 천천히 피어오르고 무너진다.
// 해 쪽으로 몇 걸음 밀도를 쌓아 자기 그림자를 만들고, 빛 색·그늘 색으로 칠한다(광원에 따라 색이 바뀐다).
// 혹마다 볼록한 면(밀도 기울기)을 세워 해를 향한 혹은 밝고 등진 혹은 그늘져 뭉게뭉게한 결이 읽힌다.
// 시계 글자 바로 뒤(_ClearRect0·1, 화면 비율 좌표)에서는 글자가 드리운 그림자처럼 은은히 어두워지고 조금 옅어져
// 밝은 글자가 흰 구름에 묻히지 않는다. 투명하게만 하면 큰 탑에 구멍이 뚫린 듯 보여 어둡게 하는 쪽을 주로 쓴다.
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
        _Iridescence ("Iridescence", Range(0, 2)) = 1
        _IriBands ("Iridescence Bands", Float) = 2.5
        _IriSaturation ("Iridescence Saturation", Range(0, 1)) = 0.62
        _CapSaturation ("Pileus Iridescence Saturation", Range(0, 1)) = 0.72
        _ClearRect0 ("Keep Clear Rect 0 (screen xMin yMin xMax yMax)", Vector) = (-10, -10, -10, -10)
        _ClearRect1 ("Keep Clear Rect 1 (screen xMin yMin xMax yMax)", Vector) = (-10, -10, -10, -10)
        _ClearOpacity ("Opacity Behind Text", Range(0, 1)) = 0.85
        _ClearShade ("Brightness Behind Text", Range(0, 1)) = 0.62
        _ClearSoftness ("Keep Clear Softness (px)", Float) = 20
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
            #pragma target 3.5
            #include "UnityCG.cginc"

            #define KIND_CUMULUS 0
            #define KIND_CONGESTUS 1
            #define KIND_CAP 2

            fixed4 _SunColor;
            fixed4 _ShadowColor;
            fixed4 _StormColor;
            float4 _SunDir;
            float _Storm;
            float _Evolve;
            float _Softness;
            float _Absorption;
            float _Silver;
            float _Iridescence;
            float _IriBands;
            float _IriSaturation;
            float _CapSaturation;
            float4 _ClearRect0;
            float4 _ClearRect1;
            float _ClearOpacity;
            float _ClearShade;
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

            // 격자 꼭짓점의 값(0~1). 정수로 섞어 격자 번호가 커도 이웃 칸과 같은 꼭짓점은 늘 같은 값이다
            // (sin 해시는 번호가 크면 계산 순서에 따라 값이 어긋나 칸 경계에 가는 자국이 생긴다).
            float Lattice(int2 i)
            {
                uint h = (uint)i.x * 374761393u + (uint)i.y * 668265263u;
                h = (h ^ (h >> 13u)) * 1274126177u;
                h ^= h >> 16u;
                return (h & 0x00FFFFFFu) / 16777215.0;
            }

            // 모양 시드와 번호(salt)로 고르는 0~1 값. 정수만 쓰므로 CloudLayer.ShapeRandom이 똑같이 셈한다.
            float Rand(float seed, int salt)
            {
                return Lattice(int2((int)seed, salt));
            }

            float Noise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = p - cell;
                float2 u = f * f * (3.0 - 2.0 * f);
                int2 i = (int2)cell;
                float a = Lattice(i);
                float b = Lattice(i + int2(1, 0));
                float c = Lattice(i + int2(0, 1));
                float d = Lattice(i + int2(1, 1));
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

            float Puff(float2 p, float2 center, float radius)
            {
                return 1.0 - length(p - center) / radius;
            }

            // 두 덩어리를 매끈한 최댓값으로 합친다. 이음매가 각지지 않아 혹 사이 골이 부드럽게 그늘진다.
            float Merge(float a, float b)
            {
                const float blend = 0.12;
                float h = saturate(0.5 + 0.5 * (a - b) / blend);
                return lerp(b, a, h) + blend * h * (1.0 - h);
            }

            // 종류별 높이(p.y 최댓값). p.x는 −1~1이고 픽셀 척도가 x와 같다.
            float KindHeight(int kind)
            {
                return kind == KIND_CONGESTUS ? 2.6 : 1.0;
            }

            // ---- 적운 ----
            float Cumulus(float2 p, float seed, float time)
            {
                float body = -1.0;
                for (int i = 0; i < 5; i++)
                {
                    float fi = i;
                    float middle = 1.0 - abs(fi - 2.0) / 2.0;   // 가운데 봉우리가 가장 높고 크다
                    float2 center = float2(
                        lerp(-0.6, 0.6, fi / 4.0) + (Rand(seed, 10 + i) - 0.5) * 0.22,
                        0.2 + Rand(seed, 20 + i) * 0.18 + middle * 0.2);
                    float radius = 0.22 + Rand(seed, 30 + i) * 0.12 + middle * 0.12;
                    body = Merge(body, Puff(p, center, radius));
                }

                // 가장자리를 난류로 흔들고, 시간이 흐르면 천천히 피어오른다.
                float2 drift = float2(time, time * 0.4);
                float turbulence = Fbm(p * 2.6 + float2(seed * 7.3, seed * 3.1) + drift);
                float detail = Fbm(p * 6.0 - drift * 1.7 + seed);
                float density = body + (turbulence - 0.5) * 0.6 + (detail - 0.5) * 0.18;

                // 뭉게구름은 밑면이 평평하다.
                return density * smoothstep(0.0, 0.12, p.y);
            }

            // ---- 웅대적운 ----
            // 넓고 낮은 밑동 위로 콜리플라워 봉우리(열기둥) 셋이 나란히 솟는다. 하나가 가장 높이 오르고 옆 둘은 낮다.
            // 봉우리마다 층층이 부푼 덩어리가 굽이치며 오르고, 머리는 혹 셋이 모인 둥근 지붕이다.
            // 덩어리는 반지름보다 좁은 간격으로 쌓는다. 간격이 넓으면 허리가 잘록해져 눈사람처럼 보인다.
            // 봉우리는 오를수록 가운데로 조금 모이고 좁아져 탑 전체가 밑이 넓은 더미가 된다. 덩어리마다 좌우로
            // 굽이치고 크기가 달라 옆면이 벽처럼 곧지 않다.
            // 목이 잘록하거나 머리가 몸보다 넓으면 버섯구름으로 읽히므로, 머리는 바로 아래 덩어리보다 넓지 않게 하고
            // 밑동을 넓게, 봉우리 높이는 계단처럼 엇갈리게 둔다.
            // 덩어리는 틀 폭의 ±0.85 안에 두고, 틀 가장자리 가까이에서는 지워 잘린 선이 보이지 않게 한다.
            // growth(0~1)만큼 가장 높은 봉우리의 머리가 위로 오르고, 옆 봉우리도 비율대로 따라 오른다.
            float Congestus(float2 p, float seed, float time, float growth, bool fine)
            {
                float top = lerp(0.95, 2.45, growth);
                float lean = (Rand(seed, 2) - 0.5) * 0.2;
                float body = -1.0;

                // 밑동: 넓고 낮게 깔린 봉우리 여섯
                for (int i = 0; i < 6; i++)
                {
                    float fi = i;
                    float2 center = float2(lerp(-0.56, 0.56, fi / 5.0) + (Rand(seed, 40 + i) - 0.5) * 0.08,
                        0.26 + Rand(seed, 50 + i) * 0.1);
                    body = Merge(body, Puff(p, center, 0.23 + Rand(seed, 60 + i) * 0.07));
                }

                // 봉우리 셋: 하나(무작위)가 가장 높다. 가장 높은 봉우리의 가로 자리는 CloudLayer.TallestTurret이 같은 식으로 셈한다.
                int tallest = (int)floor(Rand(seed, 1) * 2.999);
                for (int s = 0; s < 3; s++)
                {
                    float h = Rand(seed, 70 + s);
                    bool main = s == tallest;
                    float x0 = (s - 1.0) * 0.3 + (h - 0.5) * 0.08;
                    float peak = main ? top : lerp(0.78, 0.92, h) * top + 0.06;
                    float width = main ? 0.4 : 0.3;
                    float tilt = lean * (peak / top);
                    for (int j = 0; j < 7; j++)
                    {
                        float t = j / 6.0;
                        float hj = Rand(seed, 80 + s * 10 + j);
                        float2 center = float2(x0 * lerp(1.0, 0.75, t) + tilt * t + sin(j * 2.3 + seed + s) * 0.07, lerp(0.45, peak - 0.24, t));
                        body = Merge(body, Puff(p, center, width * lerp(1.08, 0.85, t) * (0.85 + hj * 0.3)));
                    }
                    // 머리: 혹 셋이 모인 둥근 지붕. 양옆 혹이 몸 폭 안에 들어 머리가 몸보다 넓어지지 않는다.
                    for (int m = 0; m < 3; m++)
                    {
                        float angle = (m - 1.0) * 0.9 + (Rand(seed, 120 + s * 10 + m) - 0.5) * 0.3;
                        float2 center = float2(x0 * 0.75 + tilt + sin(angle) * width * 0.4, peak - 0.2 + cos(angle) * 0.06);
                        body = Merge(body, Puff(p, center, width * (0.52 + Rand(seed, 160 + s * 10 + m) * 0.1)));
                    }
                }

                // 끓어오르는 난류: 위로 흐르고, 꼭대기일수록 잘고 또렷하다(콜리플라워 결).
                float2 drift = float2(time * 0.5, -time * 1.2);
                float crisp = smoothstep(0.4, top, p.y);
                float density = body + (Fbm(p * 1.8 + float2(seed * 3.7, seed * 1.3) + drift) - 0.5) * 0.3;
                if (fine)
                {
                    density += (Fbm(p * 4.6 - drift * 1.6 + seed) - 0.5) * lerp(0.16, 0.28, crisp);
                    density += (Fbm(p * 10.0 + drift * 2.2 + seed * 3.0) - 0.5) * 0.07 * crisp;
                }

                density *= smoothstep(0.0, 0.1, p.y);                      // 평평한 밑면
                density -= smoothstep(top - 0.02, top + 0.3, p.y) * 2.0;   // 가장 높은 머리 위로는 없다
                density -= smoothstep(0.82, 0.98, abs(p.x)) * 2.0;         // 틀 옆 가장자리는 비운다
                return density;
            }

            // ---- 갓구름 ----
            // 탑 꼭대기를 덮는 얇은 너울: 가장 높은 봉우리 위에서 둥글게 솟고 양옆 봉우리 쪽으로 완만하게 흘러내린다.
            // 납작한 원반이면 버섯구름으로 읽히므로 봉우리 머리를 따라 휘게 한다. 끝으로 갈수록 가늘어져 실처럼 흩어진다.
            float CapCenter(float x, float seed, float time)
            {
                float arch = saturate(1.0 - x * x);
                return 0.26 + 0.3 * arch + sin(x * 3.1 + seed * 5.0 + time * 0.8) * 0.012;
            }

            float CapThickness(float x)
            {
                return 0.21 * pow(saturate(1.0 - x * x), 0.6) + 0.03;
            }

            // 너울 가운데 선에서 잰 거리(두께 절반 단위, 위가 +). 무지개 띠가 이 등고선을 따라 층층이 진다.
            float CapAcross(float2 p, float seed, float time)
            {
                return (p.y - CapCenter(p.x, seed, time)) / CapThickness(p.x);
            }

            float Cap(float2 p, float seed, float time)
            {
                float across = CapAcross(p, seed, time);
                float density = 1.0 - abs(across);
                // 비단 결: 활을 따라 길게 늘어난 난류라 결이 너울과 나란히 흐른다.
                density += (Fbm(float2(p.x * 2.4 + time * 0.3 + seed, across * 1.4)) - 0.5) * 0.5;
                // 두께가 군데군데 달라 너울 윤곽이 무지개 띠처럼 반듯하지 않다.
                density += (Fbm(float2(p.x * 1.3 - time * 0.2, seed * 0.7)) - 0.5) * 0.7;
                // 양 끝은 뾰족하게 맺히지 않고 옅어지며 흩어진다.
                density -= smoothstep(0.6, 1.0, abs(p.x)) * 1.2;
                density += (Fbm(p * 3.4 + float2(seed, time * 0.6)) - 0.5) * 0.25;
                return density;
            }

            float Density(int kind, float2 p, float seed, float time, float growth, bool fine)
            {
                if (kind == KIND_CONGESTUS) return Congestus(p, seed, time, growth, fine);
                if (kind == KIND_CAP) return Cap(p, seed, time);
                return Cumulus(p, seed, time);
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
            // 영역에서 잰 거리라 모서리가 둥글게 번진다(큰 탑 위에서 네모난 창처럼 보이지 않는다).
            float ClearMask(float2 screen, float4 rect)
            {
                float2 outside = max(max(rect.xy - screen, screen - rect.zw), 0.0) * _ScreenParams.xy;
                return 1.0 - smoothstep(0.0, _ClearSoftness * 2.0, length(outside));
            }

            // 파스텔 무지개(0~1 주기). 채도만큼만 흰색에서 벗어난다.
            float3 Pastel(float phase, float saturation)
            {
                float3 spectrum = 0.5 + 0.5 * cos(6.2831853 * (phase + float3(0.0, 0.33, 0.67)));
                return lerp(float3(1.0, 1.0, 1.0), spectrum, saturation);
            }

            fixed4 Fragment(Varyings input) : SV_Target
            {
                float seed = floor(input.uv.x + 0.0001);
                int kind = (int)floor(input.uv.y + 0.0001);
                float2 local = float2(input.uv.x - seed, input.uv.y - kind);
                float height = KindHeight(kind);
                float2 p = float2(local.x * 2.0 - 1.0, local.y * height);
                // 선형 색 공간에서는 캔버스가 꼭짓점 색을 선형으로 바꿔 넘긴다(0.86 → 0.71).
                // 자람·채운 세기는 색이 아니라 매개변수이므로 C#에서 넣은 값으로 되돌린다.
                float growth = input.color.r;
                float iridescence = input.color.g;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                growth = LinearToGammaSpaceExact(growth);
                iridescence = LinearToGammaSpaceExact(iridescence);
                #endif
                float time = _Time.y * _Evolve * (0.7 + Rand(seed, 0) * 0.6);

                float density = Density(kind, p, seed, time, growth, true);
                // 갓구름은 두께 절반 전체에 걸쳐 흐려져 가운데만 짙은 비단 너울이 된다(윤곽선이 서지 않는다).
                float softness = kind == KIND_CAP ? _Softness * 4.5 : _Softness;
                // 탑은 꼭대기로 갈수록 콜리플라워처럼 윤곽이 또렷하다.
                if (kind == KIND_CONGESTUS) softness *= lerp(1.0, 0.75, smoothstep(0.4, lerp(0.95, 2.45, growth), p.y));
                float alpha = smoothstep(0.0, softness, density);
                if (alpha <= 0.001) discard;

                // 해 쪽으로 걸어가며 쌓인 밀도만큼 빛이 줄어든다. 탑은 크므로 보폭을 넓힌다.
                float2 toSun = normalize(_SunDir.xy + float2(0.0001, 0.0));
                float stride = kind == KIND_CONGESTUS ? 0.09 : (kind == KIND_CAP ? 0.04 : 0.075);
                float optical = 0.0;
                for (int k = 1; k <= 4; k++)
                {
                    optical += saturate(Density(kind, p + toSun * (stride * k), seed, time, growth, false));
                }
                // 갓구름은 얇아 빛이 거의 그대로 지난다.
                float absorption = kind == KIND_CAP ? _Absorption * 0.35 : _Absorption;
                float lit = exp(-optical * absorption);

                // 혹마다 볼록한 면: 굵은 밀도의 기울기로 겉면 방향(법선)을 세운다. 덩어리는 원뿔꼴이라 기울기를 바로 쓰면
                // 혹 가운데가 뾰족하게 드러나므로(조약돌처럼), 넓은 간격의 중앙 차분으로 가운데를 뭉개 공처럼 둥근 음영을 만든다.
                // 간격이 좁으면 잔 난류의 기울기까지 음영이 져 스펀지처럼 얼룩진다.
                // 해를 향한 혹은 밝고 등진 혹은 그늘져 뭉게뭉게한 결이 산다. 얇은 갓구름은 매끈하므로 뺀다.
                if (kind != KIND_CAP)
                {
                    const float reach = 0.12;
                    float2 dx = float2(reach, 0.0);
                    float2 dy = float2(0.0, reach);
                    float2 slope = float2(
                        Density(kind, p + dx, seed, time, growth, false) - Density(kind, p - dx, seed, time, growth, false),
                        Density(kind, p + dy, seed, time, growth, false) - Density(kind, p - dy, seed, time, growth, false)) / (2.0 * reach);
                    float3 normal = normalize(float3(-slope * 0.25, 1.0));
                    // 해는 보는 사람 쪽에서도 조금 비춘다(z). 해가 낮아 옆에서 들어도 혹의 앞면이 어둡게 가라앉지 않는다.
                    float lobe = saturate(dot(normal, normalize(float3(toSun, 0.9))));
                    if (kind == KIND_CONGESTUS)
                    {
                        // 큰 탑은 두꺼워 해 쪽 가장자리까지만 빛이 스민다. 혹의 겉면에서 흩어진 빛을 더해
                        // 해가 낮아 옆에서 들어도 한 덩어리 회색이 되지 않고 덩어리 결이 읽히게 한다.
                        lit = lerp(0.22, 1.0, saturate(lit * 0.65 + lobe * 0.5));
                    }
                    else
                    {
                        lit = saturate(lit * lerp(0.7, 1.15, lobe));
                    }
                }

                // 밑면 쪽은 빛이 덜 든다. 탑은 밑동이 더 어둡고 꼭대기가 환하다.
                float occlusion = kind == KIND_CONGESTUS
                    ? lerp(0.6, 1.0, saturate(p.y / 1.3))
                    : lerp(0.72, 1.0, saturate(p.y / height * 1.8));
                // 햇빛 색은 밝은 곳에만 돌고 중간 톤은 맑은 회색에 머문다. 따뜻한 햇빛과 푸른 그늘을 곧게 섞으면
                // 중간이 갈색·카키로 떠서 구름이 흙덩이처럼 보인다.
                float warm = lit * lit;
                float3 color = (_ShadowColor.rgb * (1.0 - warm) + _SunColor.rgb * warm + (lit - warm) * 0.35) * occlusion;

                // 해를 향한 얇은 가장자리가 밝게 빛난다(은빛 테).
                float edge = 1.0 - smoothstep(0.0, softness * 1.6, density);
                color += _SunColor.rgb * edge * lit * _Silver;

                // 채운: 얇은 곳(가장자리, 갓구름은 몸 전체)에서 해 쪽일수록 무지개 띠가 두께의 등고선을 따라 흐른다.
                // 무지갯빛이 도는 얇은 곳: 갓구름은 몸 전체, 다른 구름은 가장자리 띠(은빛 테보다 넓게).
                float fringe = 1.0 - smoothstep(0.0, softness * 3.0, density);
                float thin = kind == KIND_CAP ? lerp(0.6, 1.0, 1.0 - saturate(density * 1.2)) : fringe;
                // 가장자리에서는 두께 등고선을 따라 띠가 지고, 빛깔은 물방울 크기처럼 조각마다 달라 얼룩이 번진다.
                // (몇 픽셀 안에서 여러 번 돌면 흰색으로 뭉개지므로 띠 수는 적게 둔다.)
                float along = density * _IriBands;
                float wander = 1.5;
                float saturation = _IriSaturation;
                float glow = 0.07;
                if (kind == KIND_CAP)
                {
                    // 갓구름: 띠가 너울과 나란히 층층이 흐르고, 양 끝으로 갈수록 다음 빛깔로 넘어간다. 가장 곱고 진하다.
                    along = CapAcross(p, seed, time) * 0.45 + abs(p.x) * 0.7;
                    wander = 1.0;
                    saturation = _CapSaturation;
                    glow = 0.14;
                }
                float phase = along + Fbm(p * 2.4 + float2(seed * 2.3, time * 0.4)) * wander + time * 0.08;
                float iri = iridescence * _Iridescence * thin * lerp(0.3, 1.0, lit) * (1.0 - _Storm);
                float3 pastel = Pastel(phase, saturation);
                // 무지갯빛은 구름 빛에 곱해 진주처럼 물들이고, 조금 더해 스스로 빛나는 듯하게 한다.
                color = lerp(color, color * pastel * 1.15 + pastel * glow, saturate(iri * 0.85));

                // 먹구름: 어둡고 무겁게. 명암과 가장자리 빛은 조금 남겨 덩어리가 읽히게 한다.
                float3 storm = _StormColor.rgb * lerp(0.55, 1.35, lit) * occlusion + _SunColor.rgb * edge * lit * _Silver * 0.3;
                color = lerp(color, storm, _Storm);

                float2 screen = input.screen.xy / input.screen.w;
                float behindText = max(ClearMask(screen, _ClearRect0), ClearMask(screen, _ClearRect1));
                // 글자 그림자는 푸른 기가 도는 그늘이라 따뜻한 구름이 갈색으로 탁해지지 않는다.
                color *= lerp(float3(1.0, 1.0, 1.0), _ClearShade * float3(0.92, 0.97, 1.08), behindText);
                alpha *= lerp(1.0, _ClearOpacity, behindText);

                return fixed4(color, alpha * input.color.a);
            }
            ENDCG
        }
    }

    Fallback Off
}
