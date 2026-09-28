// 카드 위쪽의 하늘: 웅대적운 탑과 채운 갓구름을 부피로 그린다 (2026-09-28, 사용자 "구름이 이 프로그램의 절반").
// 보는 사람은 원점(km), +z가 앞, +x가 오른쪽, +y가 위. 하늘을 향해 광선을 쏘아 구름 속을 걸으며 빛을 쌓는다.
//  - 세션 진행률이 하루다: 아침 금빛 → 한낮 짙은 파랑 → 늦은 오후 금빛 → 해 질 녘 주황·장밋빛.
//    해도 같은 궤도로 돈다(아침엔 왼쪽 옆, 한낮엔 등 뒤 높이, 해 질 녘엔 오른쪽 옆).
//  - 탑: 넓은 밑동에서 봉우리 넷이 한 덩어리로 솟고(가장 높은 봉우리가 가운데), 큰·중간·잔 송이(뒤집은 워리 노이즈)를
//    겉으로 불룩하게 더해 콜리플라워 결을 만든다. _Growth만큼 꼭대기가 오른다.
//  - 채운 갓구름: 탑이 다 자라면 꼭대기를 두건처럼 덮는 매끈한 너울. 얇은 가장자리일수록 분홍·박하·연보라 빛깔이 조각조각 번진다.
//  - 채운 렌즈구름: 탑과 따로, 왼쪽 빈 하늘에 떠 있는 렌즈구름(고적운) 무리. 가장자리를 따라 빛깔 띠가 층층이 둘러지고,
//    해가 가까운 아침에 가장 곱다. 몇 분마다 피었다 사라진다(_Lens).
//  - 먹구름: 하늘이 잿빛으로 가라앉고, 낮은 구름층이 하늘을 덮어 탑을 가린다.
//  - 지평선 아래로는 하늘이 안개처럼 투명해져 아래의 유리(바탕화면)로 이어진다.
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
        _Lens ("Iridescent Lenticulars", Range(0, 1)) = 1
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

            #define PI 3.14159265
            // 탑 밑면 가운데(km): 오른쪽에서 솟아 화면 가장자리에서 잘린다(시계는 파란 하늘 위에 남는다).
            #define TOWER float3(5.6, 0.3, 21.0)
            #define EYE float3(0.0, 0.1, 0.0)
            #define PITCH_DEG 14.0
            #define FOCAL 0.95
            #define SIGMA 14.0
            #define STEPS 170
            #define STORM_DECK_ALTITUDE 2.4

            sampler3D _Noise;
            float _Progress;
            float _Growth;
            float _Storm;
            float _Cap;
            float _Lens;
            float _SkyTime;
            float _Seed;
            float _Exposure;
            float _Phase;
            float4 _SkySize;

            float4 Noise(float3 p) { return tex3Dlod(_Noise, float4(p, 0.0)); }

            float Remap(float v, float lo, float hi) { return saturate((v - lo) / max(hi - lo, 1e-4)); }

            float SMin(float a, float b, float k)
            {
                float h = saturate(0.5 + 0.5 * (b - a) / k);
                return lerp(b, a, h) - k * h * (1.0 - h);
            }

            float H1(float n) { return frac(sin(n * 91.345 + 17.13) * 43758.5453); }

            // 해: 아침엔 왼쪽 옆 낮게, 한낮엔 등 뒤 높이, 해 질 녘엔 오른쪽 옆 낮게. 방위는 보는 방향에서 시계 방향.
            float3 SunDir()
            {
                float az = radians(lerp(300.0, 85.0, _Progress));
                float el = radians(lerp(12.0, 62.0, sin(PI * _Progress)));
                return normalize(float3(sin(az) * cos(el), sin(el), cos(az) * cos(el)));
            }

            float TowerTop() { return lerp(4.5, 13.0, _Growth); }

            // 봉우리 하나: 큰 구 송이를 쌓는다. 송이마다 옆으로 비껴 윤곽이 크게 불룩불룩하다.
            float Column(float3 q, float2 c, float h, float rb, float rt, float salt)
            {
                float d = 1e5;
                [unroll]
                for (int j = 0; j < 8; j++)
                {
                    float t = j / 7.0;
                    float r = lerp(rb, rt, t) * (0.88 + 0.24 * H1(salt + j * 3.1 + _Seed));
                    float y = lerp(rb * 0.55, h - rt * 0.85, t);
                    float2 off = (float2(H1(salt + j * 5.7 + _Seed), H1(salt + j * 7.3 + _Seed)) - 0.5) * float2(1.3, 0.9) * r * 0.55;
                    d = SMin(d, length(q - float3(c.x + off.x, y, c.y + off.y)) - r, 0.55);
                }
                return d;
            }

            // 몸통의 큰 틀: 가운데 가장 높은 봉우리와 곁 봉우리 셋이 밑동에서 한 덩어리로 이어진다.
            float Envelope(float3 q)
            {
                float h = TowerTop();
                float d = Column(q, float2(0.0, 0.0), h, 2.3, 1.9, 1.0);
                d = SMin(d, Column(q, float2(-2.2, 0.6), max(h * 0.55, 2.6), 2.0, 1.5, 11.0), 0.7);
                d = SMin(d, Column(q, float2(2.0, -0.4), max(h * 0.84, 2.8), 1.9, 1.5, 23.0), 0.8);
                d = SMin(d, Column(q, float2(-1.0, -0.9), max(h * 0.9, 2.8), 1.6, 1.3, 37.0), 0.8);
                return max(d, -q.y);
            }

            // 둥근 혹: 뒤집은 워리(원뿔꼴)를 반구꼴로 바꾼다.
            float Dome(float w) { float d = 1.0 - w; return sqrt(max(1.0 - d * d, 0.0)); }

            // 밀도 0~1. 큰 틀 + 큰·중간·잔 송이를 한 장(場)에 더하고 얇게 문턱을 넘겨 또렷한 겉면을 세운다.
            float TowerDensity(float3 p, bool detail)
            {
                float3 q = p - TOWER;
                float h = TowerTop();
                if (q.y < -0.2 || q.y > h + 1.8 || abs(q.x) > 6.6 || abs(q.z) > 5.6) return 0.0;
                float3 drift = float3(_SkyTime * 0.004, -_SkyTime * 0.01, 0.0) + float3(_Seed * 1.37, 0.0, _Seed * 0.71);
                float warp = Noise(q * 0.045 + drift * 0.3).r - 0.5;
                float env = Envelope(q) + warp * 0.9;
                if (env > 2.0) return 0.0;
                float f = -env * 0.6;
                float upper = smoothstep(0.45, 0.95, q.y / h);
                f += (Dome(Noise(q * 0.09 + drift).g) - 0.6) * lerp(0.9, 1.15, upper);
                if (f < -0.7) return 0.0;
                float crisp = smoothstep(0.25, 0.8, q.y / h);
                if (detail)
                {
                    f += (Dome(Noise(q * 0.09 + drift * 1.3).b) - 0.5) * lerp(0.4, 0.55, upper);
                    f += (Dome(Noise(q * 0.09 + drift * 1.8).a) - 0.6) * lerp(0.14, 0.24, crisp);
                    // 가장 잔 송이(약 0.35 km): 겉이 매끈한 돌처럼 보이지 않게 한다.
                    f += (Dome(Noise(q * 0.19 + drift * 2.2).a) - 0.6) * lerp(0.06, 0.12, crisp);
                    f += (Noise(q * 0.9 + drift * 2.4).r - 0.5) * 0.06;
                }
                else
                {
                    f += 0.02;
                }
                // 밑면은 평평하고 조금 흐릿하다.
                f -= smoothstep(0.35, 0.0, q.y) * 0.4;
                return Remap(f, 0.0, lerp(0.26, 0.06, crisp));
            }

            // 채운 갓구름: 꼭대기를 두건처럼 덮는 얇고 매끈한 너울. 울퉁불퉁한 탑과 달리 비단처럼 매끈하고 양옆으로 길게 흘러내린다.
            // 탑 꼭대기 송이가 아래에서 밀고 올라와 너울 가운데를 뚫기도 한다. across는 너울 가운데 면에서 잰 거리(두께 절반 단위),
            // thin은 가장자리로 갈수록 얇아지는 정도(0 가운데 ~ 1 끝)다.
            float CapDensity(float3 p, out float across, out float thin)
            {
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
                float wave = (Noise(float3(xz * 0.12, 0.2) + float3(_SkyTime * 0.002, 0.0, _Seed)).r - 0.5) * 0.5;
                float yc = h + 0.75 - r * r / (2.0 * 4.0) + wave;
                float th = 0.34 * (1.0 - u * u) + 0.04;
                across = (q.y - yc) / th;
                float shell = 1.0 - across * across;
                if (shell <= 0.0) return 0.0;
                // 비단 결: 너울을 따라 길게 늘어난 무늬로 군데군데 얇아진다.
                float silk = Noise(float3(xz.x * 0.08, xz.y * 0.35, q.y * 0.6) + float3(_SkyTime * 0.003, 0.0, _Seed)).r;
                float edge = smoothstep(1.0, 0.7, u);
                return shell * edge * lerp(0.45, 1.0, smoothstep(0.3, 0.7, silk)) * _Cap * (1.0 - _Storm);
            }

            float HG(float c, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / (4.0 * PI * pow(1.0 + g2 - 2.0 * g * c, 1.5));
            }

            float3 SunColor()
            {
                float3 col = float3(1.0, 0.96, 0.9);
                col = lerp(col, float3(1.0, 0.78, 0.55), smoothstep(0.3, 0.0, _Progress));
                col = lerp(col, float3(1.0, 0.8, 0.5), smoothstep(0.6, 0.88, _Progress));
                col = lerp(col, float3(1.0, 0.5, 0.22), smoothstep(0.86, 1.0, _Progress));
                return col;
            }

            // 하늘 세 층(지평선·중간·천정). 주황과 파랑을 곧게 섞으면 보라가 되므로 가운데에 맑은 층을 둔다.
            void SkyPalette(out float3 hor, out float3 mid, out float3 zen)
            {
                float morning = smoothstep(0.3, 0.0, _Progress);
                float golden = smoothstep(0.6, 0.88, _Progress);
                float dusk = smoothstep(0.86, 1.0, _Progress);
                hor = float3(0.78, 1.08, 1.5);
                mid = float3(0.2, 0.52, 1.25);
                zen = float3(0.02, 0.13, 0.78);
                hor = lerp(hor, float3(1.5, 1.12, 0.78), morning);
                mid = lerp(mid, float3(0.55, 0.82, 1.25), morning);
                zen = lerp(zen, float3(0.05, 0.2, 0.72), morning);
                hor = lerp(hor, float3(1.7, 1.1, 0.6), golden);
                mid = lerp(mid, float3(0.7, 0.8, 1.05), golden);
                zen = lerp(zen, float3(0.04, 0.14, 0.62), golden);
                hor = lerp(hor, float3(2.1, 0.72, 0.28), dusk);
                mid = lerp(mid, float3(1.35, 0.72, 0.6), dusk);
                zen = lerp(zen, float3(0.05, 0.08, 0.4), dusk);
            }

            float3 SkyColor(float3 rd, float3 s, float3 hor, float3 mid, float3 zen)
            {
                float y = max(rd.y, 0.0);
                float3 col = lerp(hor, mid, smoothstep(0.0, 0.22, y));
                col = lerp(col, zen, smoothstep(0.12, 0.85, y));
                float c = max(dot(rd, s), 0.0);
                float3 sc = SunColor();
                col += sc * (0.25 * pow(c, 4.0) + 0.5 * pow(c, 40.0) + 1.5 * pow(c, 500.0));
                col += sc * 8.0 * smoothstep(0.99975, 0.9999, c);
                // 먹구름이 오면 하늘이 잿빛으로 가라앉는다.
                float3 grey = lerp(float3(0.42, 0.46, 0.5), float3(0.2, 0.23, 0.27), smoothstep(0.0, 0.6, y));
                return lerp(col, grey, _Storm * 0.9);
            }

            // 채운의 빛깔: 분홍 → 박하 → 연보라 → 옅은 금빛. 실제 채운은 무지개 일곱 빛보다 분홍·초록이 주로 번진다.
            float3 Iridescence(float ph)
            {
                float t = frac(ph) * 4.0;
                const float3 pink = float3(1.0, 0.5, 0.76);
                const float3 mint = float3(0.45, 1.0, 0.72);
                const float3 lilac = float3(0.66, 0.58, 1.0);
                const float3 gold = float3(1.0, 0.86, 0.45);
                float3 a = t < 1.0 ? pink : (t < 2.0 ? mint : (t < 3.0 ? lilac : gold));
                float3 b = t < 1.0 ? mint : (t < 2.0 ? lilac : (t < 3.0 ? gold : pink));
                return lerp(a, b, smoothstep(0.0, 1.0, frac(t)));
            }

            // ---- 채운 렌즈구름 ----
            // 산을 넘는 바람의 물결 꼭대기에 생겨 제자리에 머무는 매끈한 렌즈. 막 생긴 작고 고른 물방울이라 채운이 가장 곱게 선다.
            // 물방울은 가장자리에서 생겨 안쪽으로 갈수록 자라므로, 빛깔 띠가 가장자리를 따라 층층이 둘러진다.
            // 멀리 얇게 떠 있어 보는 방향의 각(방위·고도, 도)으로 모양을 잡는다. 탑과 시계를 피해 왼쪽 아래 빈 하늘에 둔다.
            // 렌즈마다 가운데(방위, 고도)와 반폭(가로, 두께).
            #define LENS0 float4(-9.5, 15.0, 10.5, 1.7)
            #define LENS1 float4(-12.5, 9.8, 8.0, 1.15)
            #define LENS2 float4(-3.5, 20.0, 4.5, 0.7)

            // 렌즈 하나의 두께(0~1). 아몬드꼴: 가운데가 두껍고 양 끝으로 갈수록 가늘어 실처럼 흩어진다.
            float LensShape(float2 dir, float4 lens, float salt)
            {
                float2 q = (dir - lens.xy) / lens.zw;
                // 결을 따라 윤곽이 조금 일렁인다.
                float wob = Noise(float3(dir * float2(0.05, 0.16), salt) + float3(_SkyTime * 0.0012, 0.0, 0.0)).r - 0.5;
                q.y += wob * 0.9;
                q.x += wob * 0.25;
                float halfThickness = pow(max(1.0 - q.x * q.x, 0.0), 0.85);
                // 윗면은 볼록하고 밑면은 조금 평평하다.
                float y = q.y > 0.0 ? q.y : q.y * 1.3;
                float t = 1.0 - (y * y) / max(halfThickness * halfThickness, 1e-4);
                return saturate(t) * smoothstep(0.0, 0.25, halfThickness);
            }

            // 렌즈구름 무리의 두께(0~1). dir은 보는 방향의 방위·고도(도).
            float LensField(float3 rd, out float2 dir)
            {
                dir = float2(degrees(atan2(rd.x, rd.z)), degrees(asin(clamp(rd.y, -1.0, 1.0))));
                if (dir.x > 2.0 || dir.y < 3.0 || dir.y > 26.0) return 0.0;
                float d = max(LensShape(dir, LENS0, 0.1), max(LensShape(dir, LENS1, 0.4) * 0.9, LensShape(dir, LENS2, 0.7) * 0.8));
                if (d <= 0.0) return 0.0;
                // 비단 결: 긴 축을 따라 늘어난 옅은 무늬로 군데군데 얇아진다.
                float silk = Noise(float3(dir * float2(0.08, 0.9), 0.83) + float3(_SkyTime * 0.002, 0.0, 0.0)).r;
                d *= lerp(0.65, 1.0, silk);
                float grow = _Lens * (1.0 - _Storm);
                return saturate(d - (1.0 - grow));
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
                float pitch = radians(PITCH_DEG);
                float3 rd = normalize(float3(uv.x, uv.y, FOCAL));
                rd = float3(rd.x, rd.y * cos(pitch) + rd.z * sin(pitch), -rd.y * sin(pitch) + rd.z * cos(pitch));
                float3 ro = EYE;
                float3 s = SunDir();

                // 빛 세기: 한낮은 앞에서 받아 하얗게 날리기 쉬워 낮추고, 노을은 색이 살도록 더 낮춘다.
                float strength = lerp(1.8, 1.35, smoothstep(0.0, 0.4, _Progress));
                strength = lerp(strength, 1.6, smoothstep(0.55, 0.8, _Progress));
                strength = lerp(strength, 1.05, smoothstep(0.88, 1.0, _Progress));
                float3 sc = SunColor() * strength * (1.0 - 0.8 * _Storm);

                float3 hor, mid, zen;
                SkyPalette(hor, mid, zen);
                float3 sky = SkyColor(rd, s, hor, mid, zen);
                float day = smoothstep(0.1, 0.7, s.y);
                // 그늘을 채우는 하늘빛(위는 푸르고 밑은 땅에서 튄 따뜻한 빛)
                float3 ambTop = lerp(lerp(float3(0.20, 0.22, 0.45), float3(0.14, 0.24, 0.52), day), float3(0.16, 0.18, 0.2), _Storm);
                float3 ambBottom = lerp(lerp(float3(0.12, 0.08, 0.10), float3(0.07, 0.09, 0.13), day), float3(0.06, 0.07, 0.08), _Storm);

                // 탑 둘레 상자와 만나는 구간만 걷는다.
                float h = TowerTop();
                float3 bmin = TOWER + float3(-6.6, -0.2, -5.6);
                float3 bmax = TOWER + float3(6.6, h + 3.4, 5.6);
                float3 inv = 1.0 / rd;
                float3 ta = (bmin - ro) * inv;
                float3 tb = (bmax - ro) * inv;
                float3 tmin = min(ta, tb);
                float3 tmax = max(ta, tb);
                float t0 = max(max(tmin.x, tmin.y), tmin.z);
                float t1 = min(min(tmax.x, tmax.y), tmax.z);

                float3 col = 0.0;
                float T = 1.0;
                float cosT = dot(rd, s);
                float firstHit = -1.0;
                if (t1 > max(t0, 0.0))
                {
                    t0 = max(t0, 0.0);
                    float dt = (t1 - t0) / STEPS;
                    // 걸음 시작을 화소마다 무작위로 비껴 층 무늬를 없앤다(규칙적인 무늬는 1/8 격자와 겹쳐 빗금이 진다).
                    float t = t0 + dt * frac(sin(dot(pixel, float2(12.9898, 78.233))) * 43758.5453);
                    [loop]
                    for (int i = 0; i < STEPS; i++)
                    {
                        float3 p = ro + rd * t;
                        float den = TowerDensity(p, true);
                        float across;
                        float thin;
                        float cap = CapDensity(p, across, thin) * 0.7;
                        float total = den + cap;
                        if (total > 0.003)
                        {
                            if (firstHit < 0.0) firstHit = t;
                            // 해 쪽으로 걸어 쌓인 밀도. 가까운 두 걸음은 잔 송이까지 넣어 송이마다 그늘이 진다.
                            float od = 0.0;
                            float ls = 0.08;
                            float3 lp = p;
                            [loop]
                            for (int k = 0; k < 7; k++)
                            {
                                lp += s * ls;
                                od += TowerDensity(lp, k < 2) * ls;
                                ls *= 1.6;
                            }
                            // 직접 산란: 해를 등지고 볼수록 앞쪽으로 쏠린 빛이 가장자리를 은빛으로 태운다.
                            float forward = min(HG(cosT, 0.6) * 4.0 * PI, 6.0);
                            float single = exp(-od * SIGMA);
                            // 여러 번 흩어진 빛: 덜 감쇠해 두꺼운 곳도 은은하다.
                            float multi = exp(-od * SIGMA * 0.18);
                            float ms = single * (0.75 + 0.25 * forward);
                            float powder = 1.0 - exp(-total * SIGMA * 0.35);
                            // 위와 보는 쪽이 막힌 골은 하늘빛을 덜 받는다.
                            float occ = TowerDensity(p + float3(0.0, 0.3, 0.0), false) + TowerDensity(p + float3(0.0, 0.8, 0.0), false) * 0.8
                                      + TowerDensity(p - rd * 0.4, false) * 0.6;
                            float ao = exp(-occ * 1.7);
                            float hgt = saturate((p.y - TOWER.y) / h);
                            float3 amb = lerp(ambBottom, ambTop, smoothstep(0.0, 0.8, hgt));
                            // 여러 번 흩어진 빛은 하늘빛과 섞여 해 색보다 희고 푸르다(따뜻한 색이 그늘에 들면 흙빛이 된다).
                            float3 scMulti = lerp(sc, dot(sc, float3(0.3, 0.5, 0.2)) * float3(0.92, 0.97, 1.06), 0.55);
                            float3 light = (sc * ms * lerp(0.6, 1.0, powder) * lerp(0.75, 1.0, ao) + scMulti * multi * 0.32 * lerp(0.5, 1.0, ao)
                                          + amb * lerp(0.35, 1.0, ao)) * lerp(1.0, 0.45, _Storm);
                            if (cap > 0.003)
                            {
                                // 채운: 너울은 하얗고 매끈하게 빛나고, 얇은 곳일수록 파스텔 빛깔(분홍·초록·보라)이 번진다.
                                // 빛깔은 물방울 크기에 따라 조각조각 달라 무지개처럼 반듯한 띠가 되지 않는다.
                                float ang = degrees(acos(clamp(cosT, -1.0, 1.0)));
                                float speck = Noise(p * 0.11 + float3(0.0, 0.0, _Seed)).r;
                                float ph = speck * 2.2 + thin * 0.7 + across * 0.08 + ang / 60.0 + _SkyTime * 0.002;
                                float sat = lerp(0.45, 1.0, smoothstep(0.15, 0.85, thin)) * smoothstep(0.15, 0.5, speck + thin * 0.35);
                                float3 iri = lerp(1.0, Iridescence(ph), sat);
                                float3 veil = (sc * 0.8 + amb * 1.2) * iri * 1.2;
                                light = lerp(light, veil, cap / total);
                            }
                            float ts = exp(-total * SIGMA * dt);
                            col += T * light * (1.0 - ts);
                            T *= ts;
                            if (T < 0.01) break;
                        }
                        t += dt;
                    }
                    // 먼 거리의 대기 원근: 밑동일수록 지평선 안개에 잠긴다.
                    if (firstHit > 0.0)
                    {
                        float3 hit = ro + rd * firstHit;
                        float haze = 1.0 - exp(-firstHit * 0.006);
                        haze += 0.35 * exp(-max(hit.y - 0.2, 0.0) / 0.7);
                        col = lerp(col, hor * 0.8 * (1.0 - T), saturate(min(haze, 0.6)));
                    }
                }

                float3 c = col + T * sky;

                // 채운 렌즈구름: 탑보다 가까워 앞에 온다.
                float2 ldir;
                float lens = LensField(rd, ldir);
                if (lens > 0.001)
                {
                    float ang = degrees(acos(clamp(cosT, -1.0, 1.0)));
                    // 채운은 해에 가까울수록 진하다(아침 해가 왼쪽에 있다). 해가 멀어도 옅게는 남긴다.
                    float nearSun = lerp(0.45, 1.0, smoothstep(90.0, 40.0, ang));
                    float forward = min(HG(cosT, 0.6) * 4.0 * PI, 6.0);
                    // 얇아 빛이 거의 그대로 지난다. 두꺼운 가운데는 조금 그늘지고, 결을 따라 밝고 어두운 줄이 옅게 진다.
                    float streak = Noise(float3(ldir * float2(0.12, 1.4), 0.57)).r;
                    // 높이 떠 있어 해 질 녘에도 햇빛을 곧게 받아 하늘보다 밝게 물든다.
                    float dusk = smoothstep(0.86, 1.0, _Progress);
                    float3 light = (sc * (1.0 + 0.3 * forward) * lerp(1.0, 1.8, dusk) + lerp(ambBottom, ambTop, 0.8) * 1.1)
                                 * lerp(1.04, 0.86, lens) * lerp(0.9, 1.06, streak);
                    float wob = Noise(float3(ldir * float2(0.06, 0.25), 0.29) + float3(_SkyTime * 0.002, 0.0, 0.0)).r;
                    float ph = lens * 1.8 + wob * 1.0 + ang / 90.0;
                    // 빛깔은 몸이 보이기 시작하는 바깥쪽 절반에 번진다(끝은 너무 옅어 안 보이고, 가운데는 두꺼워 하얗다).
                    float sat = smoothstep(0.02, 0.25, lens) * (1.0 - smoothstep(0.55, 0.95, lens)) * nearSun;
                    // 채운은 휘어 나온 빛이라 색을 곱하기만 하면 밝은 구름에서 톤매핑에 눌려 사라진다. 빛깔을 조금 더한다.
                    float3 iri = Iridescence(ph);
                    light = light * lerp(1.0, iri, sat) + (iri - 0.78) * sat * 0.9;
                    // 가장자리가 넓게 비치도록 두께만큼 천천히 짙어진다.
                    float a = pow(smoothstep(0.0, 0.75, lens), 0.7) * 0.92;
                    c = lerp(c, light, a);
                    T *= 1.0 - a;
                }
                // 먹구름 층: 낮게 깔려 하늘을 덮고, 그보다 높은 탑을 가린다. 반쯤 흐리면 조각나 하늘이 보인다.
                if (_Storm > 0.01 && rd.y > 0.0)
                {
                    float td = (STORM_DECK_ALTITUDE - ro.y) / rd.y;
                    if (td < 80.0 && (firstHit < 0.0 || td < firstHit))
                    {
                        float3 pd = ro + rd * td;
                        float2 w = pd.xz + float2(_SkyTime * 0.03, _SkyTime * 0.01);
                        float n = Noise(float3(w * 0.07, 0.31)).r * 0.55 + Dome(Noise(float3(w * 0.16, 0.53)).g) * 0.3
                                + Noise(float3(w * 0.5, 0.77)).r * 0.15;
                        float cover = smoothstep(0.56 - _Storm * 0.4, 0.72 - _Storm * 0.34, n);
                        float thick = smoothstep(0.42, 0.82, n);
                        // 두꺼운 밑면은 짙고, 얇은 곳은 위의 빛이 비쳐 밝다.
                        float3 deck = lerp(float3(0.46, 0.49, 0.53), float3(0.07, 0.08, 0.1), thick);
                        deck = lerp(deck * 1.25, deck, cover);
                        deck = lerp(deck, sky, saturate(min(1.0 - exp(-td * 0.02), 0.6)));
                        float a = cover * _Storm * smoothstep(0.0, 0.03, rd.y);
                        c = lerp(c, deck, a);
                        T = lerp(T, 0.0, a);
                    }
                }

                // 톤매핑(지수). sRGB 렌더 텍스처가 화면용으로 바꿔 쓴다.
                c = 1.0 - exp(-c * 0.95 * _Exposure);
                // 지평선 아래로 하늘이 안개처럼 풀려 아래의 유리로 이어진다. 구름 밑동은 조금 더 남긴다.
                float skyAmount = smoothstep(-0.09, 0.01, rd.y);
                float alpha = max(skyAmount, (1.0 - T) * smoothstep(-0.06, 0.0, rd.y));
                return float4(c, alpha);
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
