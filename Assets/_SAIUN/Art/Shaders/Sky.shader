// 카드의 하늘: 대기 산란으로 셈한 하늘빛 위에 웅대적운 탑, 채운, 그리고 나머지 구름 모두를 그린다
// (2026-09-28, 사용자 "구름이 이 프로그램의 절반", "하늘이 인공적이라 미려하게").
// 본문은 시안 실험실(WebGL)과 같은 소스에서 만든다. 보는 사람은 원점(km), +z가 앞, +x가 오른쪽, +y가 위.
//  - 하늘빛은 레일리·미 산란과 오존 흡수로 셈한다. 해 방향(_SunDir)은 정원 그림자를 만드는 해와 같은 방위라,
//    해가 움직이면 하늘빛(짙은 파랑, 금빛 지평선, 노을, 땅 그림자와 푸른 박명)이 따라 바뀐다.
//  - 웅대적운 탑은 부피로, 층을 이루는 구름(권운·권적운·권층운·고적운·고층운·층적운·층운·난층운)은 고도별 평면으로,
//    뭉게구름 떼는 낮은 층을 걸어서, 멀리 옆으로 누운 구름(렌즈구름·아치구름·물결구름·야광운)은 보는 방향으로 그린다.
//  - 만난 구름을 가까운 순서로 겹치고, 사이 공기가 빛을 더하고 덜어 멀수록 하늘에 잠긴다.
//    지평선 아래는 배경 유리를 켜면 투명해져 유리로 이어지고, 끄면(_Ground) 먼 들판을 그린다.
// 패스 0(하늘): 무거우므로 한 번에 화소의 1/8만 그린다(_Phase 0~7, 4×2 격자). _Phase가 음수면 전부 그린다.
//   출력은 톤매핑까지 마친 값이고, sRGB 렌더 텍스처에 쓰면 화면에서 그대로 보인다. 알파는 곧은 알파다.
// 패스 1(섞기): 다 그린 두 장(_PrevTex → _MainTex)을 _Blend만큼 섞는다. 반쯤 새로 그린 장을 보이면 빗살이 지므로
//   다 그린 장끼리만 천천히 넘겨 보인다.
Shader "Hidden/SAIUN/Sky"
{
    Properties
    {
        _Noise ("Noise (3D: R 값 노이즈, G·B·A 워리 4·8·16칸)", 3D) = "" {}
        _Progress ("Day Progress", Range(0, 1)) = 0.5
        _Growth ("Tower Growth", Range(0, 1)) = 1
        _Storm ("Storm", Range(0, 1)) = 0
        _Cap ("Pileus", Range(0, 1)) = 1
        _High ("Cirrus, Cirrocumulus, Cirrostratus, Noctilucent", Vector) = (0, 0, 0, 0)
        _Mid ("Altocumulus, Altostratus, Lenticular, Virga", Vector) = (0, 0, 0, 0)
        _Low ("Stratocumulus, Stratus, Cumulus, Nimbostratus", Vector) = (0, 0, 0, 0)
        _Special ("Anvil, Mammatus, Arcus, Fallstreak Hole", Vector) = (0, 0, 0, 0)
        _Extra ("Kelvin-Helmholtz, Twilight, Tower, Unused", Vector) = (0, 0, 1, 0)
        _SunDir ("Sun Direction (sky space)", Vector) = (0, 0.7, -0.7, 0)
        _Ground ("Draw Ground Below Horizon", Range(0, 1)) = 0
        _SkyTime ("Evolve Time", Float) = 0
        _Seed ("Shape Seed", Float) = 3
        _Exposure ("Exposure", Float) = 1
        _Phase ("Interleave Phase", Float) = -1
        _SkySize ("Target Size (px)", Vector) = (480, 680, 0, 0)
        _MainTex ("Current Sky", 2D) = "black" {}
        _PrevTex ("Previous Sky", 2D) = "black" {}
        _Blend ("Blend", Range(0, 1)) = 1
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Fragment
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler3D _Noise;
            float _Progress;
            float _Growth;
            float _Storm;
            float _Cap;
            float4 _High;
            float4 _Mid;
            float4 _Low;
            float4 _Special;
            float4 _Extra;
            float4 _SunDir;
            float _Ground;
            float _SkyTime;
            float _Seed;
            float _Exposure;
            float _Phase;
            float4 _SkySize;

            #define STATIC static

            float4 N(float3 p) { return tex3Dlod(_Noise, float4(p, 0.0)); }
            float3 v3(float x) { return float3(x, x, x); }

            STATIC const float PI = 3.14159265;
            // 탑 밑면 가운데(km): 오른쪽에서 솟아 화면 가장자리에서 잘린다(시계는 파란 하늘 위에 남는다).
            STATIC const float3 TOWER = float3(5.6, 0.3, 21.0);
            STATIC const float3 EYE = float3(0.0, 0.1, 0.0);
            STATIC const float EARTH_RADIUS = 6371.0;
            // 겉껍질이 반투명하게 비치도록 낮춘 소광 계수. 높으면 겉부터 꽉 막혀 석고 덩어리처럼 보인다.
            STATIC const float SIGMA = 9.0;
            STATIC const int MAX_LAYERS = 14;

            // 구름층 고도(km)
            STATIC const float CIRRUS_ALTITUDE = 9.0;
            STATIC const float CIRROCUMULUS_ALTITUDE = 7.6;
            STATIC const float CIRROSTRATUS_ALTITUDE = 8.4;
            STATIC const float ALTOCUMULUS_ALTITUDE = 4.2;
            STATIC const float ALTOSTRATUS_ALTITUDE = 4.8;
            STATIC const float STRATOCUMULUS_ALTITUDE = 1.7;
            STATIC const float STRATUS_ALTITUDE = 0.75;
            STATIC const float NIMBOSTRATUS_ALTITUDE = 2.4;
            STATIC const float CUMULUS_BASE = 1.3;
            STATIC const float CUMULUS_DEPTH = 2.4;

            // 겹쳐 그릴 구름들: 거리, 미리 곱한 빛, 비치는 정도
            STATIC float gDist[14];
            STATIC float3 gPre[14];
            STATIC float gTr[14];
            STATIC int gCount;

            float Remap(float v, float lo, float hi) { return clamp((v - lo) / max(hi - lo, 1e-4), 0.0, 1.0); }

            float SMin(float a, float b, float k) {
                float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
                return lerp(b, a, h) - k * h * (1.0 - h);
            }

            float H1(float n) { return frac(sin(n * 91.345 + 17.13) * 43758.5453); }
            float Hash2(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            // 둥근 혹: 뒤집은 워리(원뿔꼴)를 반구꼴로 바꾼다.
            float Dome(float w) { float d = 1.0 - w; return sqrt(max(1.0 - d * d, 0.0)); }

            float2 Rotate(float2 p, float a) { float c = cos(a); float s = sin(a); return float2(c * p.x - s * p.y, s * p.x + c * p.y); }

            // 바람에 흘러가는 거리(km). 높은 구름일수록 빠르다.
            float2 Drift(float speed) { return float2(_SkyTime, _SkyTime * 0.3) * 0.02 * speed; }

            float HG(float c, float g) { float g2 = g * g; return (1.0 - g2) / (4.0 * PI * pow(1.0 + g2 - 2.0 * g * c, 1.5)); }

            // ---- 대기 ----
            // 땅의 중심은 보는 사람 아래 반지름만큼. 높이는 km, 산란 계수는 1/km.
            STATIC const float PLANET_RADIUS = 6360.0;
            STATIC const float ATMOSPHERE_RADIUS = 6420.0;
            STATIC const float3 PLANET_CENTER = float3(0.0, -6360.0, 0.0);
            STATIC const float3 RAYLEIGH_SCATTER = float3(5.802e-3, 13.558e-3, 33.1e-3);
            STATIC const float RAYLEIGH_HEIGHT = 8.0;
            // 미 산란(먼지·물방울 안개): 조금 흐린 여름 하늘. 지평선이 뿌옇고 노을이 짙다.
            STATIC const float MIE_SCATTER = 5.0e-3;
            STATIC const float MIE_EXTINCTION = 5.6e-3;
            STATIC const float MIE_HEIGHT = 1.2;
            STATIC const float MIE_G = 0.8;
            STATIC const float3 OZONE_ABSORB = float3(0.65e-3, 1.881e-3, 0.085e-3);
            STATIC const float OZONE_CENTER = 25.0;
            STATIC const float OZONE_WIDTH = 15.0;
            // 하늘을 비추는 햇빛의 세기, 구름을 비추는 햇빛의 세기(톤매핑 전 값)
            STATIC const float SKY_SUN = 9.0;
            STATIC const float CLOUD_SUN = 1.7;
            // 여러 번 흩어진 빛: 한 번 흩어진 빛만 셈하면 해 진 뒤 하늘이 너무 빨리 까매진다.
            STATIC const float MULTI_SCATTER = 0.22;
            STATIC const int SKY_STEPS = 16;
            // 땅을 비추는 햇빛의 세기(배경 유리를 껐을 때)
            STATIC const float GROUND_SUN = 3.0;
            // 해 진 뒤 남는 푸른 빛(블루아워). 높은 하늘이 받은 빛이 여러 번 흩어져 한동안 하늘을 파랗게 채운다.
            STATIC const float3 BLUE_HOUR = float3(0.3, 0.5, 1.0) * 0.014;
            STATIC const int SUN_STEPS = 8;
            // 땅(배경 유리를 껐을 때): 먼 들판과 숲의 반사율
            STATIC const float3 GROUND_ALBEDO = float3(0.09, 0.105, 0.07);

            // 광선 위 거리마다 쌓인 공기 빛(보는 사람 쪽 감쇠를 거친 값)과 공기를 지나 남은 빛
            STATIC float gAirD[17];
            STATIC float3 gAirL[17];
            STATIC float3 gAirT[17];
            // 고도별 햇빛(대기를 지나 남은 비율): 0, 2, 5, 9, 14 km
            STATIC float3 gSunAt[5];
            STATIC float3 gMulti;

            float3 SunDir() { return normalize(_SunDir.xyz + float3(0.0, 1e-5, 0.0)); }

            float SunElevationDeg() { return degrees(asin(clamp(SunDir().y, -1.0, 1.0))); }

            // 해가 진 정도: 해가 지평선에 걸리면 0, 8° 아래로 내려가면 1.
            float Twilight() { return smoothstep(1.0, -8.0, SunElevationDeg()); }

            // 공 반지름 r과 만나는 두 거리. 만나지 않으면 (-1, -1).
            float2 RaySphere(float3 o, float3 d, float r) {
                float3 c = o - PLANET_CENTER;
                float b = dot(c, d);
                float disc = b * b - (dot(c, c) - r * r);
                if (disc < 0.0) return float2(-1.0, -1.0);
                float h = sqrt(disc);
                return float2(-b - h, -b + h);
            }

            float Altitude(float3 p) { return length(p - PLANET_CENTER) - PLANET_RADIUS; }

            // 고도 h에서 레일리·미·오존의 밀도
            float3 AirDensity(float h) {
                return float3(exp(-max(h, 0.0) / RAYLEIGH_HEIGHT), exp(-max(h, 0.0) / MIE_HEIGHT), max(0.0, 1.0 - abs(h - OZONE_CENTER) / OZONE_WIDTH));
            }

            float3 Extinction(float3 rho) { return RAYLEIGH_SCATTER * rho.x + v3(MIE_EXTINCTION * rho.y) + OZONE_ABSORB * rho.z; }

            // p에서 해까지 대기를 지나 남은 햇빛. 땅에 가리면 0(가장자리는 부드럽게: 굴절과 반그림자).
            float3 SunTransmittance(float3 p, float3 sd) {
                float3 c = p - PLANET_CENTER;
                float b = dot(c, sd);
                float closest = sqrt(max(dot(c, c) - b * b, 0.0)) - PLANET_RADIUS;
                float lit = b > 0.0 ? 1.0 : smoothstep(-0.6, 0.6, closest);
                if (lit <= 0.0) return v3(0.0);
                float tEnd = RaySphere(p, sd, ATMOSPHERE_RADIUS).y;
                float dt = tEnd / float(SUN_STEPS);
                float3 od = v3(0.0);
                [loop] for (int i = 0; i < SUN_STEPS; i++) {
                    float3 q = p + sd * (dt * (float(i) + 0.5));
                    od += Extinction(AirDensity(Altitude(q))) * dt;
                }
                return exp(-od) * lit;
            }

            float RayleighPhase(float mu) { return 3.0 / (16.0 * PI) * (1.0 + mu * mu); }

            float MiePhase(float mu) {
                float g2 = MIE_G * MIE_G;
                return 3.0 / (8.0 * PI) * ((1.0 - g2) * (1.0 + mu * mu)) / ((2.0 + g2) * pow(1.0 + g2 - 2.0 * MIE_G * mu, 1.5));
            }

            // 고도별 햇빛을 미리 셈해 둔다(구름은 이 값을 고도로 사이에 끼워 읽는다). 여러 번 흩어진 빛의 세기도 함께.
            void PrepareSun(float3 sd) {
                gSunAt[0] = SunTransmittance(EYE + float3(0.0, 0.0, 0.0), sd);
                gSunAt[1] = SunTransmittance(EYE + float3(0.0, 2.0, 0.0), sd);
                gSunAt[2] = SunTransmittance(EYE + float3(0.0, 5.0, 0.0), sd);
                gSunAt[3] = SunTransmittance(EYE + float3(0.0, 9.0, 0.0), sd);
                gSunAt[4] = SunTransmittance(EYE + float3(0.0, 14.0, 0.0), sd);
                // 여러 번 흩어진 빛은 하늘 전체가 받은 햇빛에 비례한다. 해가 져도 높은 하늘이 받은 빛이 한동안 남는다.
                float3 high = SunTransmittance(EYE + float3(0.0, 30.0, 0.0), sd);
                gMulti = (gSunAt[1] * 0.6 + gSunAt[3] * 0.25 + high * 0.15) * MULTI_SCATTER * (0.25 + 0.75 * smoothstep(-0.1, 0.3, sd.y));
                float el = degrees(asin(clamp(sd.y, -1.0, 1.0)));
                gMulti += BLUE_HOUR * smoothstep(-15.0, -1.0, el) * smoothstep(3.0, -1.0, el);
            }

            // 고도 h(km)의 햇빛(대기를 지나 남은 비율). 해가 지면 땅 그림자보다 낮은 곳은 0이다.
            float3 SunLit(float h) {
                if (h < 2.0) return lerp(gSunAt[0], gSunAt[1], h / 2.0);
                if (h < 5.0) return lerp(gSunAt[1], gSunAt[2], (h - 2.0) / 3.0);
                if (h < 9.0) return lerp(gSunAt[2], gSunAt[3], (h - 5.0) / 4.0);
                if (h < 14.0) return lerp(gSunAt[3], gSunAt[4], (h - 9.0) / 5.0);
                // 더 높은 곳(야광운)은 해를 거의 막힘없이 받는다. 땅 그림자만 따진다.
                float3 c = EYE + float3(0.0, h, 0.0) - PLANET_CENTER;
                float3 sd = SunDir();
                float b = dot(c, sd);
                float closest = sqrt(max(dot(c, c) - b * b, 0.0)) - PLANET_RADIUS;
                return v3(b > 0.0 ? 1.0 : smoothstep(-2.0, 2.0, closest - 12.0));
            }

            // 보는 방향으로 대기를 걸으며 공기 빛을 쌓는다. 가까운 곳을 촘촘히 걷도록 거리를 지수로 나눈다.
            // 반환: 끝(땅 또는 대기 밖)까지의 거리. 땅에 닿았으면 hitGround가 참이다.
            float IntegrateAir(float3 ro, float3 rd, float3 sd, out bool hitGround) {
                float tEnd = RaySphere(ro, rd, ATMOSPHERE_RADIUS).y;
                float2 g = RaySphere(ro, rd, PLANET_RADIUS);
                hitGround = g.x > 0.0;
                if (hitGround) tEnd = g.x;
                float mu = dot(rd, sd);
                float pr = RayleighPhase(mu);
                float pm = MiePhase(mu);
                const float spread = 3.5;
                float norm = 1.0 / (exp(spread) - 1.0);
                float3 T = v3(1.0);
                float3 L = v3(0.0);
                gAirD[0] = 0.0;
                gAirL[0] = L;
                gAirT[0] = T;
                float t0 = 0.0;
                [loop] for (int i = 0; i < SKY_STEPS; i++) {
                    float t1 = tEnd * (exp(spread * float(i + 1) / float(SKY_STEPS)) - 1.0) * norm;
                    float dt = t1 - t0;
                    float3 p = ro + rd * (0.5 * (t0 + t1));
                    float3 rho = AirDensity(Altitude(p));
                    float3 ext = Extinction(rho);
                    float3 scatR = RAYLEIGH_SCATTER * rho.x;
                    float scatM = MIE_SCATTER * rho.y;
                    float3 sunT = SunTransmittance(p, sd);
                    float3 source = (scatR * pr + v3(scatM * pm)) * sunT + (scatR + v3(scatM)) * gMulti;
                    float3 stepT = exp(-ext * dt);
                    // 한 걸음 안에서 감쇠를 고려해 쌓는다(걸음이 커도 에너지가 넘치지 않는다).
                    L += T * source * SKY_SUN * (v3(1.0) - stepT) / max(ext, v3(1e-6));
                    T *= stepT;
                    gAirD[i + 1] = t1;
                    gAirL[i + 1] = L;
                    gAirT[i + 1] = T;
                    t0 = t1;
                }
                return tEnd;
            }

            // 거리 d까지 쌓인 공기 빛과 남은 빛(사이를 곧게 끼워 읽는다).
            void AirAt(float d, out float3 light, out float3 transmit) {
                light = gAirL[SKY_STEPS];
                transmit = gAirT[SKY_STEPS];
                [loop] for (int i = 0; i < SKY_STEPS; i++) {
                    if (d <= gAirD[i + 1]) {
                        float k = clamp((d - gAirD[i]) / max(gAirD[i + 1] - gAirD[i], 1e-4), 0.0, 1.0);
                        light = lerp(gAirL[i], gAirL[i + 1], k);
                        transmit = lerp(gAirT[i], gAirT[i + 1], k);
                        return;
                    }
                }
            }

            // 흐린 정도: 넓게 덮는 층구름일수록 하늘을 잿빛으로 누른다.
            float Overcast() { return max(max(_Low.w, _Mid.y * 0.85), max(_Low.y * 0.7, _Low.x * 0.45)); }

            // 층구름을 지나 땅 가까이까지 오는 햇빛의 비율.
            float SunTransmit() {
                return (1.0 - 0.85 * _Low.w) * (1.0 - 0.7 * _Mid.y) * (1.0 - 0.45 * _Low.y) * (1.0 - 0.3 * _Low.x) * (1.0 - 0.15 * _High.z);
            }

            // 흐리면 하늘이 잿빛으로, 뇌우면 더 어둡게 가라앉는다. 밝기는 하늘 빛에서 가져온다.
            float3 Overcasted(float3 col) {
                float lum = dot(col, float3(0.3, 0.5, 0.2));
                float3 grey = v3(lum) * float3(0.95, 0.98, 1.04) * lerp(1.0, 0.45, _Storm);
                return lerp(col, grey, clamp(max(_Storm * 0.9, Overcast() * 0.75), 0.0, 1.0));
            }

            // ---- 땅(배경 유리를 껐을 때) ----
            // 먼 산 능선 두 겹과 그 앞의 들판. 멀수록 공기에 잠겨 푸르고 옅다. 능선이 지평선을 가려 하늘과 땅의 경계가 부드럽다.
            STATIC const float NEAR_RIDGE_DISTANCE = 11.0;
            STATIC const float FAR_RIDGE_DISTANCE = 30.0;
            STATIC const float3 FOREST_ALBEDO = float3(0.05, 0.075, 0.06);

            // 거리 d에 선 능선 꼭대기의 올려본각(라디안). 방위(라디안)에 따라 부드럽게 오르내린다. 땅이 둥글어 멀수록 내려앉는다.
            float RidgeAngle(float az, float d, float salt, float height) {
                float n = N(float3(az * 1.3, salt, 0.21)).r * 0.65 + N(float3(az * 4.1, salt, 0.43)).r * 0.35;
                float h = height * (0.35 + 0.9 * n);
                return (h - EYE.y - d * d / (2.0 * PLANET_RADIUS)) / d;
            }

            // 이 방향으로 땅이나 능선에 닿는 거리와 빛. 닿지 않으면 -1.
            float Terrain(float3 rd, float groundT, bool hitGround, float3 s, float3 ambTop, out float3 color) {
                color = v3(0.0);
                float az = atan2(rd.x, rd.z);
                float slope = rd.y / max(length(rd.xz), 1e-4);
                float d = -1.0;
                float3 albedo = FOREST_ALBEDO;
                if (slope < RidgeAngle(az, NEAR_RIDGE_DISTANCE, 0.3, 0.5)) {
                    d = NEAR_RIDGE_DISTANCE;
                } else if (slope < RidgeAngle(az, FAR_RIDGE_DISTANCE, 0.7, 1.5)) {
                    d = FAR_RIDGE_DISTANCE;
                } else if (hitGround) {
                    d = groundT;
                    float3 gp = EYE + rd * groundT;
                    float patchy = N(float3(gp.xz * 0.02, 0.37)).r * 0.6 + N(float3(gp.xz * 0.08, 0.53)).r * 0.4;
                    albedo = GROUND_ALBEDO * lerp(0.7, 1.35, patchy);
                }
                if (d < 0.0) return -1.0;
                // 능선도 들판도 해와 하늘빛을 받는다(능선은 옆에서 받아 조금 덜 밝다).
                color = albedo * (gSunAt[0] * max(s.y, 0.0) * GROUND_SUN * SunTransmit() + ambTop * 1.1);
                return d;
            }

            // 별: 박명이 깊어지면 하나둘 돋는다.
            float3 Stars(float3 rd) {
                float tw = Twilight();
                if (tw < 0.5 || rd.y < 0.02) return v3(0.0);
                float2 cell = floor(float2(atan2(rd.x, rd.z), asin(clamp(rd.y, -1.0, 1.0))) * 300.0);
                float star = smoothstep(0.9978, 1.0, Hash2(cell)) * smoothstep(0.5, 1.0, tw) * smoothstep(0.05, 0.35, rd.y);
                return star * float3(0.8, 0.88, 1.0) * 0.4 * (1.0 - Overcast());
            }

            // 채운의 빛깔: 분홍 → 박하 → 연보라 → 옅은 금빛. 실제 채운은 무지개 일곱 빛보다 분홍·초록이 주로 번진다.
            float3 Iridescence(float ph) {
                float t = frac(ph) * 4.0;
                float3 pink = float3(1.0, 0.5, 0.76);
                float3 mint = float3(0.45, 1.0, 0.72);
                float3 lilac = float3(0.66, 0.58, 1.0);
                float3 gold = float3(1.0, 0.86, 0.45);
                float3 a = t < 1.0 ? pink : (t < 2.0 ? mint : (t < 3.0 ? lilac : gold));
                float3 b = t < 1.0 ? mint : (t < 2.0 ? lilac : (t < 3.0 ? gold : pink));
                return lerp(a, b, smoothstep(0.0, 1.0, frac(t)));
            }

            // ---- 웅대적운 탑 · 적란운 모루 · 채운 갓구름 ----

            float TowerTop() { return lerp(4.5, 13.0, _Growth); }

            // 모루가 왼쪽(바람 부는 쪽)으로 뻗은 길이(km)
            float AnvilReach() { return lerp(2.0, 11.0, _Special.x); }

            // 봉우리 하나: 큰 구 송이를 쌓는다. 송이마다 옆으로 비껴 윤곽이 크게 불룩불룩하다.
            float Column(float3 q, float2 c, float h, float rb, float rt, float salt) {
                float d = 1e5;
                [unroll] for (int j = 0; j < 8; j++) {
                    float t = float(j) / 7.0;
                    float r = lerp(rb, rt, t) * (0.88 + 0.24 * H1(salt + float(j) * 3.1 + _Seed));
                    float y = lerp(rb * 0.55, h - rt * 0.85, t);
                    float2 off = (float2(H1(salt + float(j) * 5.7 + _Seed), H1(salt + float(j) * 7.3 + _Seed)) - 0.5) * float2(1.3, 0.9) * r * 0.55;
                    d = SMin(d, length(q - float3(c.x + off.x, y, c.y + off.y)) - r, 0.55);
                }
                return d;
            }

            // 몸통의 큰 틀: 가운데 가장 높은 봉우리와 곁 봉우리 셋이 밑동에서 한 덩어리로 이어진다.
            float Envelope(float3 q) {
                float h = TowerTop();
                float d = Column(q, float2(0.0, 0.0), h, 2.3, 1.9, 1.0);
                d = SMin(d, Column(q, float2(-2.2, 0.6), max(h * 0.55, 2.6), 2.0, 1.5, 11.0), 0.7);
                d = SMin(d, Column(q, float2(2.0, -0.4), max(h * 0.84, 2.8), 1.9, 1.5, 23.0), 0.8);
                d = SMin(d, Column(q, float2(-1.0, -0.9), max(h * 0.9, 2.8), 1.6, 1.3, 37.0), 0.8);
                return max(d, -q.y);
            }

            // 적란운의 모루: 탑 꼭대기가 대류권 끝에 막혀 옆으로 퍼진 평평한 구름. 바람 부는 쪽(왼쪽)으로 길게 뻗으며
            // 끝으로 갈수록 얇아져 실처럼 흩어진다. 밑면은 탑 쪽으로 처져 몸통과 이어지고, 윗면은 매끈하다.
            float AnvilDensity(float3 q, float3 drift) {
                float a = _Special.x;
                if (a <= 0.001) return 0.0;
                float h = TowerTop();
                float reach = AnvilReach();
                if (q.x > 3.0 || q.x < -reach - 2.0 || q.y < h - 3.5 || q.y > h + 1.6) return 0.0;
                float along = clamp(-q.x / reach, 0.0, 1.0);          // 0 탑 위 ~ 1 모루 끝
                float halfWidth = lerp(3.0, 6.0, along);
                float side = abs(q.z) / halfWidth;
                if (side > 1.3) return 0.0;
                float fib = N(float3(q.x * 0.05, q.y * 0.7, q.z * 0.15) + drift * 0.5).r;
                float fib2 = N(float3(q.x * 0.15, q.y * 1.5, q.z * 0.4) + drift).r;
                // 윗면은 가운데가 조금 솟고 끝으로 낮아진다. 밑면은 탑 가까이에서 몸통으로 처져 내려온다.
                float top = h + lerp(1.2, 0.4, along) - side * side * 0.3;
                float bottom = h - 0.1 + along * 0.2 - pow(1.0 - along, 3.0) * 2.8 + (fib - 0.5) * 0.5;
                float thick = max(top - bottom, 0.05);
                float inside = min(q.y - bottom, top - q.y) / thick * 2.0;
                float end = 1.0 - smoothstep(0.45, 1.05, along + (fib2 - 0.5) * 0.35);
                float edge = 1.0 - smoothstep(0.55, 1.15, side + (fib2 - 0.5) * 0.4);
                float root = smoothstep(3.0, 0.8, q.x);
                return Remap(inside + (fib2 - 0.5) * 0.5, 0.0, 0.5) * end * edge * root * a;
            }

            // 밀도 0~1. 큰 틀 + 큰·중간·잔 송이를 한 장(場)에 더하고 얇게 문턱을 넘겨 또렷한 겉면을 세운다.
            float TowerDensity(float3 p, bool detail) {
                float3 q = p - TOWER;
                float h = TowerTop();
                float3 drift = float3(_SkyTime * 0.004, -_SkyTime * 0.01, 0.0) + float3(_Seed * 1.37, 0.0, _Seed * 0.71);
                float anvil = AnvilDensity(q, drift);
                if (q.y < -0.2 || q.y > h + 1.8 || abs(q.x) > 6.6 || abs(q.z) > 5.6) return anvil;
                float warp = N(q * 0.045 + drift * 0.3).r - 0.5;
                float env = Envelope(q) + warp * 0.9;
                if (env > 2.0) return anvil;
                float f = -env * 0.6;
                // 송이의 층: 큰 송이 → 중간 송이 → 잔 송이. 워리를 반구꼴로 바꿔 겉으로 불룩하게 더한다.
                float upper = smoothstep(0.45, 0.95, q.y / h);
                f += (Dome(N(q * 0.09 + drift).g) - 0.6) * lerp(0.9, 1.15, upper);
                if (f < -0.7) return anvil;
                float crisp = smoothstep(0.25, 0.8, q.y / h);
                if (detail) {
                    f += (Dome(N(q * 0.09 + drift * 1.3).b) - 0.5) * lerp(0.4, 0.55, upper);
                    f += (Dome(N(q * 0.09 + drift * 1.8).a) - 0.6) * lerp(0.14, 0.24, crisp);
                    // 가장 잔 송이(약 0.35 km): 겉이 매끈한 돌처럼 보이지 않게 한다.
                    f += (Dome(N(q * 0.19 + drift * 2.2).a) - 0.6) * lerp(0.06, 0.12, crisp);
                    f += (N(q * 0.9 + drift * 2.4).r - 0.5) * 0.06;
                    // 겉 가장자리: 잔 무늬로 갉아 실오라기처럼 풀린다. 해 받는 봉우리 위쪽은 또렷하고 옆·밑은 흐릿하다.
                    float edge = 1.0 - smoothstep(0.0, 0.3, f);
                    float wisp = N(q * 0.6 + drift * 3.0).r;
                    f -= edge * (wisp - 0.3) * lerp(0.45, 0.14, crisp);
                } else {
                    f += 0.02;
                }
                // 밑면은 평평하고 조금 흐릿하다.
                f -= smoothstep(0.35, 0.0, q.y) * 0.4;
                // 겉에서 속으로 천천히 짙어진다. 겉껍질이 반투명해야 덩어리가 아니라 김으로 읽힌다.
                return max(Remap(f, 0.0, lerp(0.55, 0.11, crisp)) * _Extra.z, anvil);
            }

            // 채운 갓구름: 꼭대기를 두건처럼 덮는 얇고 매끈한 너울. 탑 꼭대기 송이가 아래에서 밀고 올라와 너울 가운데를 뚫기도 한다.
            // across는 너울 가운데 면에서 잰 거리(두께 절반 단위), thin은 가장자리로 갈수록 얇아지는 정도(0 가운데 ~ 1 끝)다.
            float CapDensity(float3 p, out float across, out float thin) {
                float3 q = p - TOWER;
                float h = TowerTop();
                across = 0.0;
                thin = 1.0;
                if (q.y < h - 2.4 || q.y > h + 2.0) return 0.0;
                float2 xz = q.xz * float2(1.0, 1.35);
                float r = length(xz);
                const float reach = 4.6;
                if (r > reach) return 0.0;
                float u = r / reach;
                thin = u;
                // 가운데는 봉우리 머리 바로 위, 가장자리로 갈수록 처진다. 결을 따라 살짝 물결친다.
                float wave = (N(float3(xz * 0.12, 0.2) + float3(_SkyTime * 0.002, 0.0, _Seed)).r - 0.5) * 0.5;
                float yc = h + 0.75 - r * r / (2.0 * 4.0) + wave;
                float th = 0.34 * (1.0 - u * u) + 0.04;
                across = (q.y - yc) / th;
                float shell = 1.0 - across * across;
                if (shell <= 0.0) return 0.0;
                // 비단 결: 너울을 따라 길게 늘어난 무늬로 군데군데 얇아진다.
                float silk = N(float3(xz.x * 0.08, xz.y * 0.35, q.y * 0.6) + float3(_SkyTime * 0.003, 0.0, _Seed)).r;
                float edge = smoothstep(1.0, 0.7, u);
                return shell * edge * lerp(0.45, 1.0, smoothstep(0.3, 0.7, silk)) * _Cap * (1.0 - _Storm) * _Extra.z;
            }

            // ---- 뭉게구름 떼(적운) ----
            // 맑은 날 낮게 흩어진 뭉게구름. 밑면이 평평하고 위가 둥글게 부푼다. 탑보다 가까워 크게 보이고, 오후일수록 자란다.

            // 송이 꼭대기의 높이(밑면에서 km). 0이면 구름이 없다. 송이는 서로 떨어져 있고 오후일수록 높이 자란다.
            float CumulusHeight(float2 xz) {
                float cov = _Low.z;
                float2 w = xz + Drift(1.0);
                float cell = Dome(N(float3(w * 0.1, 0.17)).g);
                float field = N(float3(w * 0.02, 0.29)).r;
                float grow = lerp(0.8, 1.5, smoothstep(0.15, 0.75, _Progress));
                float h = (cell - lerp(0.99, 0.8, cov)) * 7.0 + (field - 0.55) * 1.2 * cov;
                return max(h, 0.0) * grow;
            }

            float CumulusDensity(float3 p, bool detail) {
                float y = p.y - CUMULUS_BASE;
                if (y < -0.05 || y > CUMULUS_DEPTH) return 0.0;
                float h = CumulusHeight(p.xz);
                if (h <= 0.0) return 0.0;
                float f = (h - y) * 1.2;
                if (detail) {
                    // 겉에 잔 송이가 부풀어 콜리플라워 결이 난다.
                    float3 w = p + float3(Drift(1.0).x, 0.0, Drift(1.0).y);
                    f += (Dome(N(w * 0.25).b) - 0.55) * 0.6;
                    f += (Dome(N(w * 0.6).a) - 0.6) * 0.22;
                }
                // 밑면은 평평하다.
                f -= smoothstep(0.12, 0.0, y) * 0.5;
                // 멀리 지평선 쪽 송이는 공기에 잠겨 옅어진다.
                float distant = 1.0 - smoothstep(30.0, 55.0, length(p.xz));
                return Remap(f, 0.0, 0.6) * _Low.z * distant;
            }

            // ---- 평면 구름층 ----

            // 고도 h의 평면과 만나는 거리. 없으면 -1.
            float PlaneHit(float3 ro, float3 rd, float h) { return rd.y > 0.003 ? (h - ro.y) / rd.y : -1.0; }

            // 보는 방향(방위·고도, 도)을 고도 h 평면 위의 점으로 옮긴다. 방향으로 자리를 정하는 구름(구멍 등)에 쓴다.
            float2 DirectionOnPlane(float az, float el, float h) {
                float d = (h - EYE.y) / tan(radians(el));
                return float2(sin(radians(az)), cos(radians(az))) * d;
            }

            // 구멍구름: 과냉각 물방울 층 한가운데가 얼며 떨어져 동그랗게 뚫리고, 가운데로 얼음 꼬리가 늘어진다.
            // 시계 왼쪽 아래 하늘(방위 −9°, 고도 24°)에 뚫린다. 반환: 층을 남기는 비율, iceFall은 가운데 얼음 꼬리.
            float FallstreakHole(float2 xz, float altitude, float radius, out float iceFall) {
                iceFall = 0.0;
                float hole = _Special.w;
                if (hole <= 0.001) return 1.0;
                float2 c = DirectionOnPlane(-9.0, 24.0, altitude);
                float2 d = xz - c;
                float fray = N(float3(xz * 0.3, 0.93)).r;
                float r = length(d * float2(1.0, 1.25)) + (fray - 0.5) * radius * 0.35;
                float keep = smoothstep(radius * 0.8, radius * 1.15, r);
                float2 f = Rotate(d, 0.4);
                float fibers = N(float3(f.x * 0.4, f.y * 2.4, 0.37)).r;
                iceFall = hole * (1.0 - smoothstep(0.0, radius * 0.75, r)) * Remap(fibers, 0.35, 0.75);
                return lerp(1.0, keep, hole);
            }

            // 권운(새털구름): 높은 바람결을 따라 길게 늘어난 가는 비단실. 드문드문 무리를 짓고, 결이 크게 굽이친다.
            float CirrusDensity(float2 xz) {
                float cov = _High.x;
                if (cov <= 0.001) return 0.0;
                float2 w = Rotate(xz, 0.35) + Drift(3.0);
                // 큰 굽이: 실이 곧지 않고 물결처럼 휜다.
                float bend = N(float3(w * 0.008, 0.11)).r;
                w.y += (bend - 0.5) * 22.0;
                float s1 = N(float3(w.x * 0.004, w.y * 0.08, 0.23)).r;
                float s2 = N(float3(w.x * 0.011, w.y * 0.28, 0.37)).r;
                float s3 = N(float3(w.x * 0.03, w.y * 0.9, 0.51)).r;
                float fib = Remap(s1 * 0.5 + s2 * 0.33 + s3 * 0.17, 0.44, 0.68);
                float where = N(float3(xz * 0.006 + Drift(1.0) * 0.006, 0.61)).r;
                float mask = Remap(where, 0.7 - 0.45 * cov, 0.9 - 0.3 * cov);
                return pow(fib, 1.5) * mask;
            }

            // 권적운(조개구름): 아주 잘고 흰 알갱이가 조각조각 모인다. 알갱이가 옅은 물결 줄을 짓기도 한다.
            float CirrocumulusDensity(float2 xz, out float iceFall) {
                iceFall = 0.0;
                float cov = _High.y;
                if (cov <= 0.001) return 0.0;
                float2 w = Rotate(xz, -0.25) + Drift(2.5);
                float grain = Dome(N(float3(w * 0.32, 0.33)).a);
                float grain2 = Dome(N(float3(w * 0.7, 0.13)).a);
                float ripple = 0.5 + 0.5 * sin(w.x * 7.0 + N(float3(w * 0.05, 0.71)).r * 9.0);
                float where = N(float3(xz * 0.03 + Drift(1.0) * 0.01, 0.43)).r * 0.7 + N(float3(xz * 0.1, 0.53)).r * 0.3;
                float mask = Remap(where, 0.62 - 0.4 * cov, 0.82 - 0.3 * cov);
                float d = mask * Remap(grain * 0.7 + grain2 * 0.3 + (ripple - 0.5) * 0.12, 0.55, 0.9);
                return d * FallstreakHole(xz, CIRROCUMULUS_ALTITUDE, 3.4, iceFall);
            }

            // 권층운(햇무리구름): 하늘을 우윳빛으로 엷게 덮는 흰 너울. 결이 아주 옅게 비친다.
            float CirrostratusDensity(float2 xz) {
                float cov = _High.z;
                if (cov <= 0.001) return 0.0;
                float2 w = Rotate(xz, 0.2) + Drift(2.0);
                float fib = N(float3(w.x * 0.02, w.y * 0.1, 0.13)).r;
                float sheet = N(float3(xz * 0.01, 0.27)).r;
                return cov * (0.55 + 0.3 * fib + 0.3 * (sheet - 0.5));
            }

            // 중층·하층 구름은 탑이 선 오른쪽 하늘에서 성기다. 탑 둘레는 오르는 공기를 메우려 내려앉는 공기(하강 기류)로 맑고,
            // 그래야 이 앱의 주인공인 탑이 층구름에 다 가려지지 않는다. x는 층 위 자리의 가로(km).
            float TowerSideThin(float x) { return 1.0 - 0.55 * smoothstep(-2.0, 9.0, x); }

            // 고적운(양떼구름): 가운데가 잿빛이고 가장자리가 흰 송이가 줄지어 모인다.
            float AltocumulusDensity(float2 xz) {
                float cov = _Mid.x;
                if (cov <= 0.001) return 0.0;
                float2 w = Rotate(xz, 0.5) + Drift(1.6);
                float puff = Dome(N(float3(w * 0.15, 0.19)).b);
                float row = 0.5 + 0.5 * sin(w.y * 2.4 + N(float3(w * 0.04, 0.83)).r * 5.0);
                float where = N(float3(xz * 0.02 + Drift(1.0) * 0.01, 0.57)).r;
                float mask = Remap(where * TowerSideThin(xz.x), 0.66 - 0.45 * cov, 0.86 - 0.3 * cov);
                float fine = N(float3(w * 0.8, 0.07)).r;
                return mask * Remap(puff * lerp(0.6, 1.0, row) + (fine - 0.5) * 0.15, 0.38, 0.8);
            }

            // 고층운(차일구름): 잿빛으로 하늘을 넓게 덮는 두꺼운 너울. 해가 간유리 너머처럼 흐려진다.
            float AltostratusDensity(float2 xz) {
                float cov = _Mid.y;
                if (cov <= 0.001) return 0.0;
                float2 w = xz + Drift(1.2);
                float n = N(float3(w * 0.025, 0.47)).r * 0.7 + N(float3(w * 0.1, 0.59)).r * 0.3;
                return cov * Remap(n, 0.2 - 0.3 * cov, 0.55);
            }

            // 층적운(두루마리구름): 크고 낮은 잿빛 덩어리가 틈을 두고 이어진다. 덩어리마다 작은 혹이 부풀어 윤곽이 울퉁불퉁하다.
            float StratocumulusDensity(float2 xz) {
                float cov = _Low.x;
                if (cov <= 0.001) return 0.0;
                float2 w = Rotate(xz, -0.4) + Drift(1.0);
                float puff = Dome(N(float3(w * 0.1, 0.61)).g);
                float lump = Dome(N(float3(w * 0.22, 0.41)).b);
                float fine = Dome(N(float3(w * 0.5, 0.21)).a);
                float roll = 0.5 + 0.5 * sin(w.y * 0.9 + N(float3(w * 0.03, 0.39)).r * 4.0);
                float f = (puff * lerp(0.75, 1.0, roll) + (lump - 0.6) * 0.35 + (fine - 0.6) * 0.15) * lerp(0.8, 1.0, TowerSideThin(xz.x));
                return Remap(f, lerp(1.0, 0.5, cov), lerp(1.1, 0.85, cov));
            }

            // 층운(안개구름): 낮게 깔린 매끈한 잿빛 층. 아침 안개가 걷히듯 군데군데 해지고 끝이 찢어진다.
            float StratusDensity(float2 xz) {
                float cov = _Low.y;
                if (cov <= 0.001) return 0.0;
                float2 w = xz + Drift(0.8);
                float n = N(float3(w * 0.05, 0.73)).r * 0.65 + N(float3(w * 0.22, 0.87)).r * 0.35;
                return Remap(n, 0.62 - 0.5 * cov, 0.78 - 0.3 * cov);
            }

            // 유방운 송이 자리: 결을 휘어 워리 칸의 곧은 모서리(다각형)가 드러나지 않게 한다.
            float2 MammatusCoord(float2 xz) {
                float2 w = xz + float2(_SkyTime * 0.03, _SkyTime * 0.01);
                float2 warp = float2(N(float3(w * 0.05, 0.12)).r, N(float3(w * 0.05, 0.62)).r) - 0.5;
                return w * 0.1 + warp * 0.35;
            }

            // 난층운(비구름) 밑면의 두께와, 유방운 송이 높이.
            float NimbostratusDensity(float2 xz, out float thick, out float pouch) {
                float2 w = xz + float2(_SkyTime * 0.03, _SkyTime * 0.01);
                float n = N(float3(w * 0.07, 0.31)).r * 0.55 + Dome(N(float3(w * 0.16, 0.53)).g) * 0.3 + N(float3(w * 0.5, 0.77)).r * 0.15;
                float cov = _Low.w;
                thick = smoothstep(0.42, 0.82, n);
                // 유방운: 밑면에서 주머니처럼 둥글게 늘어진 송이. 폭풍이 지나간 뒤 낮은 해를 받아 도드라진다.
                pouch = Dome(N(float3(MammatusCoord(xz), 0.9)).b) * _Special.y;
                return max(smoothstep(0.56 - cov * 0.4, 0.72 - cov * 0.34, n), _Special.y * 0.95);
            }

            // ---- 방향으로 그리는 구름(멀리 옆으로 누운 것들) ----

            float2 Direction(float3 rd) { return float2(degrees(atan2(rd.x, rd.z)), degrees(asin(clamp(rd.y, -1.0, 1.0)))); }

            // 채운 렌즈구름: 산을 넘는 바람의 물결 꼭대기에 생겨 제자리에 머무는 매끈한 렌즈. 막 생긴 작고 고른 물방울이라 채운이 가장 곱다.
            // 렌즈마다 가운데(방위, 고도)와 반폭(가로, 두께). 탑과 시계를 피해 왼쪽 아래 빈 하늘에 둔다.
            STATIC const float4 LENS0 = float4(-9.5, 15.0, 10.5, 1.7);
            STATIC const float4 LENS1 = float4(-12.5, 9.8, 8.0, 1.15);
            STATIC const float4 LENS2 = float4(-3.5, 20.0, 4.5, 0.7);

            float LensShape(float2 dir, float4 lens, float salt) {
                float2 q = (dir - lens.xy) / lens.zw;
                // 결을 따라 윤곽이 조금 일렁인다.
                float wob = N(float3(dir * float2(0.05, 0.16), salt) + float3(_SkyTime * 0.0012, 0.0, 0.0)).r - 0.5;
                q.y += wob * 0.9;
                q.x += wob * 0.25;
                float halfThickness = pow(max(1.0 - q.x * q.x, 0.0), 0.85);
                // 윗면은 볼록하고 밑면은 조금 평평하다.
                float y = q.y > 0.0 ? q.y : q.y * 1.3;
                float t = 1.0 - (y * y) / max(halfThickness * halfThickness, 1e-4);
                return clamp(t, 0.0, 1.0) * smoothstep(0.0, 0.25, halfThickness);
            }

            float LensField(float2 dir) {
                if (_Mid.z <= 0.001 || dir.x > 2.0 || dir.y < 3.0 || dir.y > 26.0) return 0.0;
                float d = max(LensShape(dir, LENS0, 0.1), max(LensShape(dir, LENS1, 0.4) * 0.9, LensShape(dir, LENS2, 0.7) * 0.8));
                if (d <= 0.0) return 0.0;
                // 비단 결: 긴 축을 따라 늘어난 옅은 무늬로 군데군데 얇아진다.
                float silk = N(float3(dir * float2(0.08, 0.9), 0.83) + float3(_SkyTime * 0.002, 0.0, 0.0)).r;
                d *= lerp(0.55, 1.0, silk);
                // 가장자리는 잔 무늬로 갉혀 실처럼 풀린다.
                float fray = N(float3(dir * float2(0.35, 1.2), 0.47) + float3(_SkyTime * 0.003, 0.0, 0.0)).r;
                d -= (1.0 - smoothstep(0.0, 0.35, d)) * (fray - 0.35) * 0.35;
                float grow = _Mid.z * (1.0 - _Storm);
                return clamp(d - (1.0 - grow), 0.0, 1.0);
            }

            // 아치구름(선반구름): 뇌우 앞에서 차가운 돌풍이 따뜻한 공기를 밀어 올려 생기는, 지평선을 따라 길게 누운 쐐기.
            // 윗면은 층층이 매끈한 띠가 지고, 앞으로 튀어나온 밑면은 어둡다. 그 밑은 조금 밝은 틈, 더 밑은 비 커튼이다.
            // face는 쐐기 안의 자리(0 윗면 ~ 1 밑면), under는 쐐기 밑 비 커튼의 짙기.
            float ArcusDensity(float2 dir, out float face, out float under) {
                face = 0.0;
                under = 0.0;
                float a = _Special.z;
                if (a <= 0.001 || dir.y > 14.0) return 0.0;
                float n = N(float3(dir.x * 0.015, 0.15, 0.61) + float3(_SkyTime * 0.001, 0.0, 0.0)).r;
                float n2 = N(float3(dir.x * 0.06, dir.y * 0.4, 0.31)).r;
                float bottom = lerp(1.2, 3.6, a) + (n - 0.5) * 0.8;
                float top = bottom + lerp(1.2, 6.0, a) * (0.8 + 0.4 * n) + (n2 - 0.5) * 1.2;
                float y = (dir.y - bottom) / max(top - bottom, 0.1);
                // 쐐기 밑: 밝은 틈을 지나 지평선까지 비 커튼이 내린다.
                under = (1.0 - smoothstep(-0.15, 0.0, y)) * smoothstep(-1.2, -0.3, y) * a;
                if (y < -0.05 || y > 1.15) return 0.0;
                face = clamp(y, 0.0, 1.0);
                // 윗면은 부풀어 울퉁불퉁하고, 밑선은 칼로 자른 듯 곧다.
                float d = (1.0 - smoothstep(0.85, 1.1, y + (n2 - 0.5) * 0.25)) * smoothstep(-0.05, 0.03, y);
                return d * a;
            }

            // 켈빈-헬름홀츠 물결구름: 위아래 바람이 어긋나는 얇은 층의 꼭대기가 파도처럼 말려 부서진다. 몇 분이면 사라진다.
            // 시계 바로 아래 왼쪽 하늘(방위 −24~1°, 고도 18~27°)에 한 줄로 선다. 물결마다 얇은 너울이 비탈을 타고 올라
            // 머리에서 오른쪽으로 말려 들어간다(나선). curl은 말려 들어간 안쪽(그늘이 지는 곳)이다.
            float KelvinHelmholtzDensity(float2 dir, out float curl) {
                curl = 0.0;
                float k = _Extra.x;
                if (k <= 0.001 || dir.x > 2.0 || dir.x < -25.0 || dir.y < 17.0 || dir.y > 28.0) return 0.0;
                const float period = 6.5;
                float along = dir.x + 25.0 - _SkyTime * 0.02 + (N(float3(dir.x * 0.03, 0.2, 0.44)).r - 0.5) * 2.0;
                float cellIndex = floor(along / period);
                float amp = lerp(0.65, 1.1, H1(cellIndex * 7.1)) * lerp(0.6, 1.0, k);
                // 층이 길게 너울거려 바닥 띠가 곧은 줄이 되지 않는다.
                float swell = (N(float3(dir.x * 0.04, 0.3, 0.2)).r - 0.5) * 1.4;
                float2 p = float2(frac(along / period) * period, dir.y - 18.8 - swell);
                // 말린 머리: 눈(가운데)을 도는 나선. 왼쪽 위에서 시작해 위 → 오른쪽 → 아래로 감기며 좁아진다.
                float2 c = float2(period * 0.6, 0.6 + 2.0 * amp);
                float2 d = p - c;
                float r = length(d);
                float ang = atan2(d.y, d.x);
                float u = frac((2.618 - ang) / 6.2832) * 6.2832;
                float spiralR = 1.5 * amp * exp(-0.16 * u);
                float stroke = (1.0 - smoothstep(0.15 * amp, 0.85 * amp, abs(r - spiralR))) * (1.0 - smoothstep(3.0, 4.8, u));
                // 머리 속은 말려 든 구름으로 반쯤 차 있다(눈만 조금 비어 있다).
                float fill = (1.0 - smoothstep(0.35, 1.1, r / (1.5 * amp))) * smoothstep(0.1, 0.45, r / (1.5 * amp)) * 0.55;
                // 비탈: 바닥 너울에서 머리 왼쪽 위까지 비스듬히 오른다.
                float2 a0 = float2(period * 0.05, 0.45);
                float2 a1 = c + 1.5 * amp * float2(-0.866, 0.5);
                float2 ab = a1 - a0;
                float h = clamp(dot(p - a0, ab) / dot(ab, ab), 0.0, 1.0);
                float slope = 1.0 - smoothstep(0.2, 0.8, length(p - a0 - ab * h));
                // 바닥에 깔린 얇은 너울
                float band = 1.0 - smoothstep(0.2, 0.85, abs(p.y - 0.45));
                curl = smoothstep(2.4, 3.6, u) * stroke;
                float fib = N(float3(dir * float2(0.3, 0.8), 0.57)).r;
                float fib2 = N(float3(dir * float2(1.1, 2.4), 0.77)).r;
                float edge = smoothstep(0.0, 4.0, dir.x + 25.0) * smoothstep(0.0, 4.0, 2.0 - dir.x);
                float shape = max(max(band * 0.65, slope * 0.85), max(stroke, fill));
                return clamp(Remap(shape * lerp(0.45, 1.0, fib) + (fib2 - 0.5) * 0.35, 0.12, 0.9), 0.0, 1.0) * edge * k;
            }

            // 야광운: 해가 진 뒤 80 km 높이의 얼음 구름이 아직 햇빛을 받아 푸른 은빛으로 빛난다. 물결 무늬가 잘게 진다.
            float NoctilucentDensity(float2 dir) {
                float k = _High.w * smoothstep(0.35, 0.8, Twilight());
                if (k <= 0.001 || dir.y < 1.0 || dir.y > 16.0) return 0.0;
                float2 w = dir + float2(_SkyTime * 0.004, 0.0);
                float warp = N(float3(w * float2(0.04, 0.12), 0.19)).r;
                float warp2 = N(float3(w * float2(0.1, 0.3), 0.39)).r;
                // 긴 물결(띠)과 잔물결이 비스듬히 엇갈린다. 결이 크게 휘어 반듯한 빗금이 되지 않는다.
                float bands = 0.5 + 0.5 * sin(w.x * 0.9 + w.y * 1.7 + warp * 10.0);
                float ripples = 0.5 + 0.5 * sin(w.x * 3.4 - w.y * 2.1 + warp2 * 12.0);
                float fib = N(float3(w * float2(0.25, 0.9), 0.61)).r;
                float where = N(float3(w * float2(0.02, 0.07), 0.67)).r;
                float height = smoothstep(1.0, 3.0, dir.y) * (1.0 - smoothstep(6.0, 15.0, dir.y));
                float d = Remap(where, 0.35, 0.65) * lerp(0.3, 1.0, bands) * lerp(0.6, 1.0, ripples) * lerp(0.5, 1.0, fib);
                return d * height * k;
            }

            // ---- 겹쳐 그리기 ----

            void AddLayer(float dist, float3 premult, float transmit) {
                if (gCount >= MAX_LAYERS || transmit > 0.999) return;
                gDist[gCount] = dist;
                gPre[gCount] = premult;
                gTr[gCount] = transmit;
                gCount++;
            }

            // 빛깔 col, 불투명도 a인 구름을 거리 dist에 둔다. 공기 원근(멀수록 하늘에 잠김)은 겹칠 때 셈한다.
            void AddCloud(float dist, float3 col, float a) {
                if (a <= 0.001) return;
                AddLayer(dist, col * a, 1.0 - a);
            }

            // 평면 구름층의 빛: 얇은 곳은 해를 받아 밝고, 두꺼운 가운데는 잿빛. toward는 해 쪽으로 조금 옮긴 곳의 두께(그늘).
            float3 LayerLight(float thick, float toward, float forward, float3 sc, float3 amb, float bright, float grey) {
                float3 lit = sc * (0.85 + 0.3 * forward) + amb * 1.1;
                float3 dim = amb * 1.0 + sc * 0.3;
                float3 col = lerp(lit, dim, smoothstep(0.15, 0.95, thick) * grey);
                return col * lerp(1.0, 0.72, clamp(toward, 0.0, 1.0)) * bright;
            }

            // uv: 화면 가운데가 0, 세로 한 칸이 1. pixel: 화소 번호(걸음 흔들기용). 반환: 톤매핑한 빛(선형)과 불투명도.
            float4 Shade(float2 uv, float2 pixel) {
                gCount = 0;
                float pitch = radians(14.0);
                float3 rd = normalize(float3(uv.x, uv.y, 0.95));
                rd = float3(rd.x, rd.y * cos(pitch) + rd.z * sin(pitch), -rd.y * sin(pitch) + rd.z * cos(pitch));
                float3 ro = EYE;
                float3 s = SunDir();
                float2 dir = Direction(rd);
                float tw = Twilight();

                // 대기: 고도별 햇빛을 셈하고, 이 방향으로 쌓이는 공기 빛을 걷는다.
                PrepareSun(s);

                // 구름을 비추는 햇빛(색은 고도별로 SunLit이 곱한다). 층구름이 덮거나 뇌우면 줄어든다.
                float3 sc = v3(CLOUD_SUN * (1.0 - 0.8 * _Storm) * SunTransmit());
                float ovc = Overcast();
                // 그늘을 채우는 하늘빛: 머리 위와 지평선 하늘의 빛에서 가져온다. 밑은 땅에서 튄 빛이다.
                float3 zenithSky = v3(0.0);
                float3 horizonSky = v3(0.0);
                bool hitGround;
                IntegrateAir(ro, float3(0.0, 1.0, 0.0), s, hitGround);
                zenithSky = gAirL[SKY_STEPS];
                IntegrateAir(ro, normalize(float3(rd.x, 0.12, rd.z)), s, hitGround);
                horizonSky = gAirL[SKY_STEPS];
                // 이 방향의 공기 빛은 마지막에 걷는다(겹칠 때 이 값을 쓴다).
                float airEnd = IntegrateAir(ro, rd, s, hitGround);
                float3 ambTop = (zenithSky * 0.55 + horizonSky * 0.45) * 0.55;
                float3 ambBottom = GROUND_ALBEDO * (gSunAt[0] * max(s.y, 0.0) * GROUND_SUN + ambTop * 1.2) + ambTop * 0.25;
                // 해가 지면 구름 밑은 하늘보다 어둡다(위에서 오는 하늘빛만 받는다).
                ambTop *= lerp(1.0, 0.45, tw);
                ambTop = lerp(ambTop, v3(dot(ambTop, float3(0.3, 0.5, 0.2))) * 1.1, ovc * 0.7) * lerp(1.0, 0.45, _Storm);
                ambBottom = lerp(ambBottom, v3(dot(ambBottom, float3(0.3, 0.5, 0.2))), ovc * 0.7) * lerp(1.0, 0.45, _Storm);
                float3 ambMid = lerp(ambBottom, ambTop, 0.8);
                float cosT = dot(rd, s);
                float forward = min(HG(cosT, 0.6) * 4.0 * PI, 6.0);
                float jitter = frac(sin(dot(pixel, float2(12.9898, 78.233))) * 43758.5453);
                float2 sunXZ = normalize(s.xz + float2(1e-4, 0.0));

                // ---- 웅대적운 탑(모루·갓구름 포함) ----
                if (_Extra.z > 0.01 || _Special.x > 0.01) {
                    float h = TowerTop();
                    float anvilLeft = _Special.x > 0.001 ? AnvilReach() + 1.5 : 0.0;
                    float3 bmin = TOWER + float3(-6.6 - anvilLeft, -0.2, -5.6);
                    float3 bmax = TOWER + float3(6.6, h + 3.4, 5.6);
                    float3 inv = 1.0 / rd;
                    float3 ta = (bmin - ro) * inv;
                    float3 tb = (bmax - ro) * inv;
                    float3 tmin = min(ta, tb);
                    float3 tmax = max(ta, tb);
                    float t0 = max(max(tmin.x, tmin.y), tmin.z);
                    float t1 = min(min(tmax.x, tmax.y), tmax.z);
                    if (t1 > max(t0, 0.0)) {
                        t0 = max(t0, 0.0);
                        const int STEPS = 170;
                        float dt = (t1 - t0) / float(STEPS);
                        float t = t0 + dt * jitter;
                        float3 col = v3(0.0);
                        float T = 1.0;
                        float firstHit = -1.0;
                        [loop] for (int i = 0; i < STEPS; i++) {
                            float3 p = ro + rd * t;
                            float den = TowerDensity(p, true);
                            float across;
                            float thin;
                            float cap = CapDensity(p, across, thin) * 0.7;
                            float total = den + cap;
                            if (total > 0.003) {
                                if (firstHit < 0.0) firstHit = t;
                                // 해 쪽으로 걸어 쌓인 밀도. 가까운 두 걸음은 잔 송이까지 넣어 송이마다 그늘이 진다.
                                float od = 0.0;
                                float ls = 0.08;
                                float3 lp = p;
                                [loop] for (int k = 0; k < 7; k++) {
                                    lp += s * ls;
                                    od += TowerDensity(lp, k < 2) * ls;
                                    ls *= 1.6;
                                }
                                // 직접 산란: 해를 등지고 볼수록 앞쪽으로 쏠린 빛이 가장자리를 은빛으로 태운다.
                                float single = exp(-od * SIGMA);
                                // 여러 번 흩어진 빛: 덜 감쇠해 두꺼운 곳도 은은하다.
                                float multi = exp(-od * SIGMA * 0.18);
                                float ms = single * (0.75 + 0.25 * forward);
                                float powder = 1.0 - exp(-total * SIGMA * 0.35);
                                // 위와 보는 쪽이 막힌 골은 하늘빛을 덜 받는다.
                                float occ = TowerDensity(p + float3(0.0, 0.3, 0.0), false) + TowerDensity(p + float3(0.0, 0.8, 0.0), false) * 0.8
                                                    + TowerDensity(p - rd * 0.4, false) * 0.6;
                                float ao = exp(-occ * 1.7);
                                float hgt = clamp((p.y - TOWER.y) / h, 0.0, 1.0);
                                float3 amb = lerp(ambBottom, ambTop, smoothstep(0.0, 0.8, hgt));
                                // 박명: 땅 그림자보다 높은 곳만 붉은 햇빛을 받는다.
                                float3 scHere = sc * SunLit(p.y);
                                // 여러 번 흩어진 빛은 하늘빛과 섞여 해 색보다 희고 푸르다(따뜻한 색이 그늘에 들면 흙빛이 된다).
                                float3 scMulti = lerp(scHere, v3(dot(scHere, float3(0.3, 0.5, 0.2))) * float3(0.92, 0.97, 1.06), 0.55);
                                float3 light = (scHere * ms * lerp(0.6, 1.0, powder) * lerp(0.75, 1.0, ao) + scMulti * multi * 0.32 * lerp(0.5, 1.0, ao)
                                                        + amb * lerp(0.35, 1.0, ao)) * lerp(1.0, 0.45, _Storm);
                                if (cap > 0.003) {
                                    // 채운: 너울은 하얗고 매끈하게 빛나고, 얇은 곳일수록 파스텔 빛깔(분홍·초록·보라)이 번진다.
                                    // 빛깔은 물방울 크기에 따라 조각조각 달라 무지개처럼 반듯한 띠가 되지 않는다.
                                    float ang = degrees(acos(clamp(cosT, -1.0, 1.0)));
                                    float speck = N(p * 0.11 + float3(0.0, 0.0, _Seed)).r;
                                    float ph = speck * 2.2 + thin * 0.7 + across * 0.08 + ang / 60.0 + _SkyTime * 0.002;
                                    float sat = lerp(0.45, 1.0, smoothstep(0.15, 0.85, thin)) * smoothstep(0.15, 0.5, speck + thin * 0.35);
                                    float3 iri = lerp(v3(1.0), Iridescence(ph), sat);
                                    float3 veil = (scHere * 0.8 + amb * 1.2) * iri * 1.2;
                                    light = lerp(light, veil, cap / total);
                                }
                                float ts = exp(-total * SIGMA * dt);
                                col += T * light * (1.0 - ts);
                                T *= ts;
                                if (T < 0.01) break;
                            }
                            t += dt;
                        }
                        if (firstHit > 0.0) AddLayer(firstHit, col, T);
                    }
                }

                // ---- 뭉게구름 떼 ----
                if (_Low.z > 0.01 && rd.y > 0.012) {
                    // 머리 위(시계 자리)는 비우고, 5 km 밖부터 지평선 쪽으로 멀어지며 늘어선다.
                    float ta = max((CUMULUS_BASE - ro.y) / rd.y, 5.0);
                    float tb = min((CUMULUS_BASE + CUMULUS_DEPTH - ro.y) / rd.y, 60.0);
                    if (tb > ta) {
                        // 가까운 송이는 촘촘히, 먼 송이는 성기게 걷는다(먼 것은 화면에서 작다).
                        const int CSTEPS = 110;
                        float t = ta + max(0.06, ta * 0.018) * jitter;
                        float3 col = v3(0.0);
                        float T = 1.0;
                        float firstHit = -1.0;
                        [loop] for (int i = 0; i < CSTEPS; i++) {
                            if (t > tb) break;
                            float dt = max(0.06, t * 0.018);
                            float3 p = ro + rd * t;
                            float den = CumulusDensity(p, true);
                            if (den > 0.003) {
                                if (firstHit < 0.0) firstHit = t;
                                float od = CumulusDensity(p + s * 0.12, true) * 0.12 + CumulusDensity(p + s * 0.4, false) * 0.3
                                                + CumulusDensity(p + s * 0.9, false) * 0.5;
                                float single = exp(-od * SIGMA);
                                float multi = exp(-od * SIGMA * 0.18);
                                float ao = exp(-(CumulusDensity(p + float3(0.0, 0.25, 0.0), false) + CumulusDensity(p + float3(0.0, 0.6, 0.0), false)) * 1.2);
                                float3 amb = lerp(ambBottom, ambTop, smoothstep(0.0, 1.2, p.y - CUMULUS_BASE));
                                float3 scHere = sc * SunLit(p.y);
                                float3 scMulti = lerp(scHere, v3(dot(scHere, float3(0.3, 0.5, 0.2))) * float3(0.92, 0.97, 1.06), 0.55);
                                float3 light = scHere * single * (0.75 + 0.25 * forward) * lerp(0.75, 1.0, ao) + scMulti * multi * 0.36 + amb * lerp(0.5, 1.0, ao);
                                float ts = exp(-den * SIGMA * dt);
                                col += T * light * (1.0 - ts);
                                T *= ts;
                                if (T < 0.02) break;
                            }
                            t += dt;
                        }
                        if (firstHit > 0.0) AddLayer(firstHit, col, T);
                    }
                }

                // ---- 평면 구름층 ----
                if (rd.y > 0.003) {
                    // 권운
                    float t = PlaneHit(ro, rd, CIRRUS_ALTITUDE);
                    float d = CirrusDensity((ro + rd * t).xz);
                    if (d > 0.001) {
                        float3 c = sc * SunLit(CIRRUS_ALTITUDE) * (0.95 + 0.4 * forward) + ambMid * 1.15;
                        AddCloud(t, c, d * 0.62 * smoothstep(0.003, 0.05, rd.y));
                    }
                    // 권층운
                    t = PlaneHit(ro, rd, CIRROSTRATUS_ALTITUDE);
                    d = CirrostratusDensity((ro + rd * t).xz);
                    if (d > 0.001) {
                        float3 c = sc * SunLit(CIRROSTRATUS_ALTITUDE) * (0.9 + 0.5 * forward) + ambMid * 1.2;
                        AddCloud(t, c, d * 0.42);
                    }
                    // 권적운(구멍구름의 얼음 꼬리 포함)
                    t = PlaneHit(ro, rd, CIRROCUMULUS_ALTITUDE);
                    float2 xz = (ro + rd * t).xz;
                    float ice;
                    d = CirrocumulusDensity(xz, ice);
                    if (d > 0.001 || ice > 0.001) {
                        float iceNear;
                        float toward = CirrocumulusDensity(xz + sunXZ * 0.12, iceNear) * 0.5;
                        float3 c = LayerLight(d, toward, forward, sc * SunLit(CIRROCUMULUS_ALTITUDE), ambMid, 1.05, 0.35);
                        float3 iceCol = sc * SunLit(CIRROCUMULUS_ALTITUDE) * (0.95 + 0.4 * forward) + ambMid * 1.15;
                        float a = max(d * 0.75, ice * 0.55);
                        AddCloud(t, lerp(c, iceCol, ice / max(a, 1e-3) * 0.55), a * smoothstep(0.003, 0.05, rd.y));
                    }
                    // 고층운
                    t = PlaneHit(ro, rd, ALTOSTRATUS_ALTITUDE);
                    d = AltostratusDensity((ro + rd * t).xz);
                    if (d > 0.001) {
                        float3 c = lerp(sc * SunLit(ALTOSTRATUS_ALTITUDE) * 0.45 + ambMid * 1.5, ambMid * 1.25 + sc * 0.15, smoothstep(0.3, 1.0, d));
                        AddCloud(t, c, clamp(d, 0.0, 0.97));
                    }
                    // 고적운(구멍구름·꼬리구름 포함)
                    t = PlaneHit(ro, rd, ALTOCUMULUS_ALTITUDE);
                    xz = (ro + rd * t).xz;
                    d = AltocumulusDensity(xz);
                    ice = 0.0;
                    float keep = FallstreakHole(xz, ALTOCUMULUS_ALTITUDE, 2.0, ice);
                    d *= keep;
                    if (d > 0.001 || ice > 0.001) {
                        float toward = AltocumulusDensity(xz + sunXZ * 0.3);
                        float3 c = LayerLight(d, toward, forward, sc * SunLit(ALTOCUMULUS_ALTITUDE), ambMid, 1.0, 0.8);
                        float3 iceCol = sc * SunLit(ALTOCUMULUS_ALTITUDE) * (0.9 + 0.4 * forward) + ambMid * 1.15;
                        float a = max(smoothstep(0.0, 0.35, d) * 0.92, ice * 0.5);
                        AddCloud(t, lerp(c, iceCol, clamp(ice * 1.5, 0.0, 1.0) * (1.0 - smoothstep(0.0, 0.3, d))), a);
                    }
                    // 꼬리구름: 고적운 송이 밑에서 비가 떨어지다 마르며 늘어진 흰 꼬리. 바람에 비스듬히 휜다.
                    if (_Mid.w > 0.001 && _Mid.x > 0.001) {
                        float tTop = PlaneHit(ro, rd, ALTOCUMULUS_ALTITUDE);
                        float tBottom = PlaneHit(ro, rd, ALTOCUMULUS_ALTITUDE - 2.4);
                        if (tBottom > 0.0 && tTop < 90.0) {
                            const int VSTEPS = 10;
                            float vdt = (tTop - tBottom) / float(VSTEPS);
                            float vt = tBottom + vdt * jitter;
                            float va = 0.0;
                            [loop] for (int i = 0; i < VSTEPS; i++) {
                                float3 p = ro + rd * vt;
                                float fall = (ALTOCUMULUS_ALTITUDE - p.y) / 2.4;
                                float src = AltocumulusDensity(p.xz + float2(1.2, 0.3) * fall * fall * 2.4);
                                float streak = Remap(N(float3(p.x * 1.1, 0.13, p.z * 1.1)).r, 0.3, 0.7);
                                float dv = src * pow(max(1.0 - fall, 0.0), 0.8) * streak * _Mid.w * 1.6;
                                va += dv * vdt * 0.9 * (1.0 - va);
                                vt += vdt;
                            }
                            // 빗줄기라 송이보다 잿빛이다.
                            float3 c = sc * SunLit(ALTOCUMULUS_ALTITUDE - 1.2) * (0.25 + 0.3 * forward) + ambMid * 1.25;
                            AddCloud(0.5 * (tTop + tBottom) - 0.01, c, clamp(va, 0.0, 0.7));
                        }
                    }
                    // 난층운·먹구름(유방운 포함)
                    if (_Low.w > 0.01 || _Special.y > 0.01) {
                        t = PlaneHit(ro, rd, NIMBOSTRATUS_ALTITUDE);
                        if (t < 80.0) {
                            xz = (ro + rd * t).xz;
                            float thick;
                            float pouch;
                            float cover = NimbostratusDensity(xz, thick, pouch);
                            // 두꺼운 밑면은 짙고, 얇은 곳은 위의 빛이 비쳐 밝다.
                            float3 deck = lerp(ambTop * 1.6 + sc * SunLit(NIMBOSTRATUS_ALTITUDE) * 0.1, ambBottom * 0.5, thick);
                            deck = lerp(deck * 1.25, deck, cover);
                            if (pouch > 0.001) {
                                // 유방운: 주머니 송이의 해 쪽 볼은 낮은 해를 받아 금빛으로 밝고, 반대쪽과 사이의 골은 어둡다.
                                float e = 0.2;
                                float px = Dome(N(float3(MammatusCoord(xz + float2(e, 0.0)), 0.9)).b);
                                float pz = Dome(N(float3(MammatusCoord(xz + float2(0.0, e)), 0.9)).b);
                                float p0 = Dome(N(float3(MammatusCoord(xz), 0.9)).b);
                                float3 nrm = normalize(float3(-(px - p0) / (e * 0.1), 5.0, -(pz - p0) / (e * 0.1)));
                                float lit = 0.5 + 0.5 * dot(nrm, normalize(float3(sunXZ.x, 0.25, sunXZ.y)));
                                lit = smoothstep(0.35, 0.95, lit);
                                float crease = smoothstep(0.05, 0.55, p0);
                                float3 pouchCol = lerp(ambBottom * 1.1, sc * 0.9 + ambBottom * 1.3, lit) * lerp(0.45, 1.0, crease);
                                deck = lerp(deck, pouchCol, _Special.y);
                            }
                            AddCloud(t, deck, cover * max(_Low.w, _Special.y) * smoothstep(0.003, 0.03, rd.y));
                        }
                    }
                    // 층적운
                    t = PlaneHit(ro, rd, STRATOCUMULUS_ALTITUDE);
                    if (t < 70.0) {
                        xz = (ro + rd * t).xz;
                        d = StratocumulusDensity(xz);
                        if (d > 0.001) {
                            float toward = StratocumulusDensity(xz + sunXZ * 0.6);
                            float3 c = LayerLight(d, toward, forward, sc * SunLit(STRATOCUMULUS_ALTITUDE), ambMid, 0.95, 1.0);
                            AddCloud(t, c, smoothstep(0.0, 0.4, d) * 0.96);
                        }
                    }
                    // 층운
                    t = PlaneHit(ro, rd, STRATUS_ALTITUDE);
                    if (t < 60.0) {
                        d = StratusDensity((ro + rd * t).xz);
                        if (d > 0.001) {
                            float3 c = ambMid * 1.55 + sc * SunLit(STRATUS_ALTITUDE) * 0.4 * (0.8 + 0.4 * forward);
                            AddCloud(t, c, d * 0.93);
                        }
                    }
                }

                // ---- 방향으로 그리는 구름 ----
                // 채운 렌즈구름: 탑보다 가까워 앞에 온다.
                float lens = LensField(dir);
                if (lens > 0.001) {
                    float ang = degrees(acos(clamp(cosT, -1.0, 1.0)));
                    // 채운은 해에 가까울수록 진하다(아침 해가 왼쪽에 있다). 해가 멀어도 옅게는 남긴다.
                    float nearSun = lerp(0.45, 1.0, smoothstep(90.0, 40.0, ang));
                    // 얇아 빛이 거의 그대로 지난다. 두꺼운 가운데는 조금 그늘지고, 결을 따라 밝고 어두운 줄이 옅게 진다.
                    float streak = N(float3(dir * float2(0.12, 1.4), 0.57)).r;
                    float3 light = (sc * SunLit(5.0) * (1.0 + 0.3 * forward) + ambMid * 1.1) * lerp(1.04, 0.86, lens) * lerp(0.9, 1.06, streak);
                    float wob = N(float3(dir * float2(0.06, 0.25), 0.29) + float3(_SkyTime * 0.002, 0.0, 0.0)).r;
                    float ph = lens * 1.8 + wob * 1.0 + ang / 90.0;
                    // 빛깔은 몸이 보이기 시작하는 바깥쪽 절반에 번진다(끝은 너무 옅어 안 보이고, 가운데는 두꺼워 하얗다).
                    // 해가 지평선에 걸리면 빛이 붉고 약해 채운이 서지 않는다.
                    float sat = smoothstep(0.02, 0.25, lens) * (1.0 - smoothstep(0.55, 0.95, lens)) * nearSun * smoothstep(0.5, 5.0, SunElevationDeg());
                    // 채운은 휘어 나온 빛이라 색을 곱하기만 하면 밝은 구름에서 톤매핑에 눌려 사라진다. 빛깔을 조금 더한다.
                    float3 iri = Iridescence(ph);
                    light = light * lerp(v3(1.0), iri, sat) + (iri - v3(0.78)) * sat * 0.9;
                    // 가장자리가 넓게 비치도록 두께만큼 천천히 짙어진다.
                    float a = pow(smoothstep(0.0, 0.85, lens), 0.8) * 0.82;
                    AddLayer(15.0, light * a, 1.0 - a);
                }
                // 아치구름
                float face;
                float under;
                float arcus = ArcusDensity(dir, face, under);
                if (arcus > 0.001 || under > 0.001) {
                    // 몸통은 검푸른 잿빛이고, 윗면 띠만 희끄무레하게 층층이 줄진다. 쐐기 밑은 지평선 빛이 새어 들어 밝다.
                    float tiers = 0.5 + 0.5 * sin(face * 24.0 + dir.x * 0.04 + N(float3(dir.x * 0.05, 0.4, 0.1)).r * 5.0);
                    float3 body = ambBottom * 0.7 + sc * SunLit(1.0) * 0.05;
                    float3 band = ambTop * 1.5 + sc * SunLit(1.5) * 0.25;
                    float3 c = lerp(body, band, smoothstep(0.35, 0.85, face) * tiers);
                    c = lerp(c * 0.7, c, smoothstep(0.0, 0.25, face));
                    float3 gap = ambTop * 2.0 + sc * SunLit(0.5) * 0.15;
                    float a = max(clamp(arcus, 0.0, 0.97), under * 0.6);
                    AddCloud(9.0, lerp(gap, c, arcus / max(a, 1e-3)), a);
                }
                // 켈빈-헬름홀츠 물결구름
                float curl;
                float kh = KelvinHelmholtzDensity(dir, curl);
                if (kh > 0.001) {
                    float3 c = (sc * SunLit(3.0) * (0.95 + 0.35 * forward) + ambMid * 1.15) * lerp(1.0, 0.72, curl);
                    AddCloud(24.0, c, clamp(kh * 0.75, 0.0, 0.75));
                }
                // 야광운: 모든 구름 너머 아주 멀리. 땅 그림자 위 80 km에서 햇빛을 받는다.
                float nlc = NoctilucentDensity(dir);
                if (nlc > 0.001) {
                    float3 c = float3(0.45, 0.75, 1.2) * 0.8 * SunLit(82.0);
                    AddLayer(400.0, c * nlc * 0.9, 1.0 - nlc * 0.9);
                }

                // ---- 가까운 것부터 겹친다 ----
                [loop] for (int i = 1; i < MAX_LAYERS; i++) {
                    if (i >= gCount) break;
                    float dd = gDist[i];
                    float3 pp = gPre[i];
                    float tt = gTr[i];
                    int j = i - 1;
                    [loop] for (int k = 0; k < MAX_LAYERS; k++) {
                        if (j < 0) break;
                        if (gDist[j] <= dd) break;
                        gDist[j + 1] = gDist[j];
                        gPre[j + 1] = gPre[j];
                        gTr[j + 1] = gTr[j];
                        j--;
                    }
                    gDist[j + 1] = dd;
                    gPre[j + 1] = pp;
                    gTr[j + 1] = tt;
                }
                // 배경 유리를 껐으면 땅과 먼 산 능선이 구름과 하늘을 가린다.
                float3 terrainColor = v3(0.0);
                float terrainT = _Ground > 0.5 ? Terrain(rd, airEnd, hitGround, s, ambTop, terrainColor) : -1.0;

                // 가까운 구름부터: 구름 사이 공기가 더하는 빛(앞 구름에 가린 만큼), 공기를 지나 남은 구름 빛을 차례로 쌓는다.
                float3 c = v3(0.0);
                float T = 1.0;
                float3 prevL = v3(0.0);
                [loop] for (int i = 0; i < MAX_LAYERS; i++) {
                    if (i >= gCount) break;
                    if (terrainT > 0.0 && gDist[i] > terrainT) break;
                    float3 airL;
                    float3 airT;
                    AirAt(gDist[i], airL, airT);
                    c += T * (airL - prevL);
                    c += T * airT * gPre[i];
                    T *= gTr[i];
                    prevL = airL;
                }
                if (terrainT > 0.0) {
                    // 땅·능선까지의 공기 빛과, 공기를 지나 남은 땅 빛
                    float3 airL;
                    float3 airT;
                    AirAt(terrainT, airL, airT);
                    c += T * Overcasted(airL - prevL);
                    c += T * airT * terrainColor;
                } else {
                    // 마지막 구름 너머의 하늘. 흐리면 잿빛이다.
                    c += T * Overcasted(gAirL[SKY_STEPS] - prevL);
                    if (!hitGround) {
                        // 해: 화면 밖에 있을 때가 많지만 들어오면 눈부신 원반이다.
                        float disk = smoothstep(0.99996, 0.99999, dot(rd, s));
                        c += T * gAirT[SKY_STEPS] * disk * 40.0 * (1.0 - Overcast());
                        c += T * Stars(rd);
                    }
                }

                // 톤매핑(지수). 해가 낮거나 지면 눈이 어둠에 익듯 노출을 올린다.
                float el = SunElevationDeg();
                float exposure = _Exposure * lerp(1.0, 1.7, smoothstep(25.0, 0.0, el)) * lerp(1.0, 3.2, smoothstep(0.0, -9.0, el));
                c = 1.0 - exp(-c * exposure);
                // 지평선 아래: 배경 유리를 켰으면 하늘이 안개처럼 풀려 유리로 이어지고, 껐으면 땅까지 그린다.
                float skyAmount = _Ground > 0.5 ? 1.0 : smoothstep(-0.09, 0.01, rd.y);
                float alpha = max(skyAmount, (1.0 - T) * smoothstep(-0.06, 0.0, rd.y));
                return float4(c, alpha);
            }

            float4 Fragment(v2f_img input) : SV_Target
            {
                // 한 번에 4×2 격자의 한 칸만 그린다. 나머지 화소는 지난 그림을 그대로 둔다.
                float2 pixel = floor(input.uv * _SkySize.xy);
                if (_Phase >= 0.0)
                {
                    float cell = fmod(pixel.x, 4.0) + fmod(pixel.y, 2.0) * 4.0;
                    if (abs(cell - _Phase) > 0.5) discard;
                }
                float2 uv = (input.uv - 0.5) * float2(_SkySize.x / _SkySize.y, 1.0);
                return Shade(uv, pixel);
            }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Mix
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _PrevTex;
            float _Blend;

            float4 Mix(v2f_img input) : SV_Target
            {
                return lerp(tex2D(_PrevTex, input.uv), tex2D(_MainTex, input.uv), _Blend);
            }
            ENDCG
        }
    }

    Fallback Off
}
