// ==== 하늘 본문 (브라우저 시안과 Unity 셰이더가 같은 소스를 쓴다) ====
// 보는 사람은 원점(km), +z가 앞, +x가 오른쪽, +y가 위. 하늘을 향해 광선을 쏘아 구름을 만나는 대로 빛을 쌓는다.
// 구름마다 제 자리(거리)를 가지므로, 만난 구름을 가까운 순서로 겹쳐 그린다.
//
// 매개변수(0~1, 구름 양):
//   _High    권운(새털구름), 권적운(조개구름), 권층운(햇무리구름), 야광운
//   _Mid     고적운(양떼구름), 고층운(차일구름), 채운 너울, 꼬리구름(고적운에서 늘어진 비)
//   _Low     층적운(두루마리구름), 층운(안개구름), 적운(뭉게구름 떼), 난층운(비구름)
//   _Special 적란운 모루, 유방운, 아치구름(선반구름), 구멍구름(폴스트리크 홀)
//   _Extra   켈빈-헬름홀츠 물결구름, 박명(해가 진 정도), 웅대적운 탑이 보이는 정도, 밤
//   _VeilPlace 채운 너울의 자리: 방위 이동(°), 고도 이동(°), 크기 배율, 무늬(0~1). 사라져 있을 때만 바뀐다.
//   _VeilForm 채운 너울의 모양: 0 띠(불무지개), 1 실(무지개 실구름 한 가닥), 2 해 둘레 조각(해를 둘러싼 고리 빛깔)
//   _Growth 탑이 자란 정도, _Cap 채운 갓구름, _Storm 뇌우의 어둠, _Progress 하루(세션 진행률)
//   _SunDir 해 방향(하늘 좌표, 정원 그림자를 만드는 해와 같은 방위)
//   위는 하늘이고, 지평선 아래는 보이는 장을 만드는 패스가 바다로 채운다(여기서는 그리지 않는다).
//   _Drift 층마다 바람에 흘러간 거리(km): xy 낮은 층(땅 바람), zw 중층. _DriftHigh xy 높은 층(제트기류), zw 구멍구름이 뚫린 뒤 흐른 거리
//
// 하늘빛은 손으로 칠하지 않고 대기를 지나는 햇빛을 셈한다(레일리·미 산란, 오존 흡수, 여러 번 흩어진 빛의 근사).
// 해가 높으면 짙은 파랑, 낮으면 지평선이 금빛·주황으로 물들고, 해가 지면 땅 그림자가 올라오며 푸른 박명이 된다.
// 구름은 같은 대기를 지나온 햇빛과 하늘빛을 받고, 보는 사람과의 사이 공기가 빛을 더하고 덜어 멀수록 하늘에 잠긴다.

STATIC const float PI = 3.14159265;
// 탑 밑면 가운데(km): 오른쪽에서 솟아 화면 가장자리에서 잘린다(시계는 파란 하늘 위에 남는다).
STATIC const vec3 TOWER = vec3(6.8, 0.3, 21.0);
// 탑(곁 덩어리 포함)이 밑동 가운데에서 옆(x)·앞뒤(z)로 뻗는 거리(km)
STATIC const vec2 TOWER_REACH = vec2(7.6, 6.2);
// 모루가 앞뒤로 퍼지는 거리(km): 모루 끝 반폭 6 km × 가장자리 1.3배 + 여유
STATIC const float ANVIL_SIDE_REACH = 8.5;
// 모루 셈을 끊는 자리(모루 길이의 배수). 실처럼 풀리는 끝이 다 사라진 뒤다.
STATIC const float ANVIL_TAIL = 1.4;
// 너울 선반이 없을 확률: 첫 겹, 둘째 겹
STATIC const float VELUM_ABSENT = 0.35;
STATIC const float VELUM_SECOND_ABSENT = 0.75;
STATIC const vec3 EYE = vec3(0.0, 0.1, 0.0);
STATIC const float EARTH_RADIUS = 6371.0;
// 겉껍질이 반투명하게 비치도록 낮춘 소광 계수. 높으면 겉부터 꽉 막혀 석고 덩어리처럼 보인다.
STATIC const float SIGMA = 9.0;
// 웅대적운 탑(사진 속 웅대적운처럼): 송이가 빽빽해 겉이 또렷하고 송이 사이 골에 그늘이 깊다.
// 해 받는 면은 하얗게 빛나고 그늘은 하늘빛을 받아 푸르다. 그래서 탑만 더 짙고, 해를 더 받고, 흩어진 빛과 하늘빛은 덜 받는다.
STATIC const float TOWER_SIGMA = 2.5;
STATIC const float TOWER_SUN = 1.8;
STATIC const float TOWER_MULTI = 0.16;
STATIC const float TOWER_AMBIENT = 0.6;
// 뭉게구름 떼도 탑처럼 명암이 강하다: 해 받는 윗면은 밝고 밑면·그늘은 깊다.
STATIC const float CUMULUS_SIGMA = 2.0;
STATIC const float CUMULUS_SUN = 1.4;
STATIC const float CUMULUS_MULTI = 0.24;
STATIC const float CUMULUS_AMBIENT = 0.75;
STATIC const int MAX_LAYERS = 14;

// 구름층 고도(km). 같은 종류라도 늘 같은 높이에 뜨지 않는다: SkyView가 층이 보이지 않는
// 사이에 범위 안에서 새 높이를 골라 _AltHigh(권운·권적운·권층운)·_AltLow(고적운·고층운·층적운·적운 밑면)로 넘긴다.
// 적운 밑면은 하루 동안 올라간다(땅이 데워지며 응결 높이가 오른다).
#define CIRRUS_ALTITUDE (_AltHigh.x)
#define CIRROCUMULUS_ALTITUDE (_AltHigh.y)
// 권적운 잔 혹(뒤집은 워리)의 평균. 혹이 화소보다 잘면 이 값으로 둔다.
STATIC const float CIRROCUMULUS_LUMP_MEAN = 0.5;
#define CIRROSTRATUS_ALTITUDE (_AltHigh.z)
#define ALTOCUMULUS_ALTITUDE (_AltLow.x)
#define ALTOSTRATUS_ALTITUDE (_AltLow.y)
#define STRATOCUMULUS_ALTITUDE (_AltLow.z)
STATIC const float STRATUS_ALTITUDE = 0.75;
STATIC const float NIMBOSTRATUS_ALTITUDE = 2.4;
// 유방운 주머니가 밑면에서 늘어지는 깊이(km)
STATIC const float MAMMATUS_DEPTH = 0.5;
// 주머니 반지름(셀 간격 대비, 뒤집은 워리 거리 단위)
STATIC const float MAMMATUS_RADIUS = 0.6;
#define CUMULUS_BASE (_AltLow.w)
STATIC const float CUMULUS_DEPTH = 2.4;

// 겹쳐 그릴 구름들: 거리, 미리 곱한 빛, 비치는 정도, 공기 원근을 셈할 때 거리에 곱할 값(1이면 실제 거리)
STATIC float gDist[14];
STATIC vec3 gPre[14];
STATIC float gTr[14];
STATIC float gHaze[14];
STATIC int gCount;

float Remap(float v, float lo, float hi) { return clamp((v - lo) / max(hi - lo, 1e-4), 0.0, 1.0); }

float SMin(float a, float b, float k) {
  float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
  return mix(b, a, h) - k * h * (1.0 - h);
}

// 해시는 sin 없이 곱셈·fract로만 섞는다. fract(sin(x) * 43758) 꼴은 큰 인자에서 sin 오차를 수만 배로 키워,
// 같은 값으로 그려도 GPU가 셈하는 길에 따라 결과가 달라진다(탑 송이·새털 자리가 칸마다 바뀌어 빗살이 서고,
// 화소 좌표에서는 규칙적인 무늬가 된다).
float H1(float n) {
  n = fract(n * 0.1031);
  n *= n + 33.33;
  n *= n + n;
  return fract(n);
}

float Hash2(vec2 p) {
  vec3 p3 = fract(vec3(p.x, p.y, p.x) * 0.1031);
  p3 += dot(p3, p3.yzx + 33.33);
  return fract((p3.x + p3.y) * p3.z);
}

// 화소마다 고른 흩뜨림 값(0~1)
float PixelHash(vec2 p) { return Hash2(p); }

// 둥근 혹: 뒤집은 워리(원뿔꼴)를 반구꼴로 바꾼다.
float Dome(float w) { float d = 1.0 - w; return sqrt(max(1.0 - d * d, 0.0)); }

// 두 방위(도)의 차를 −180~180°로 접는다(GLSL mod와 HLSL fmod가 음수에서 달라 fract로 셈한다).
float AngleDelta(float a, float b) { return (fract((a - b) / 360.0 + 0.5) - 0.5) * 360.0; }
vec2 Rotate(vec2 p, float a) { float c = cos(a); float s = sin(a); return vec2(c * p.x - s * p.y, s * p.x + c * p.y); }
// 보는 방향의 방위·고도(도). 방위는 +z(앞)에서 +x(오른쪽)로 잰다.
vec2 Direction(vec3 rd) { return vec2(degrees(atan(rd.x, rd.z)), degrees(asin(clamp(rd.y, -1.0, 1.0)))); }

// 층마다 바람에 흘러간 거리(km). 낮은 층은 땅 바람을, 중층은 조금 비껴 더 빠른 바람을, 높은 층은 가장 빠른 제트기류를 따른다.
// SkyView가 바람을 시간에 따라 쌓아 넘긴다(바람이 바뀌어도 구름이 튀지 않는다).
vec2 LowDrift() { return _Drift.xy; }
vec2 MidDrift() { return _Drift.zw; }
vec2 HighDrift() { return _DriftHigh.xy; }

float HG(float c, float g) { float g2 = g * g; return (1.0 - g2) / (4.0 * PI * pow(1.0 + g2 - 2.0 * g * c, 1.5)); }

// ---- 대기 ----
// 땅의 중심은 보는 사람 아래 반지름만큼. 높이는 km, 산란 계수는 1/km.
STATIC const float PLANET_RADIUS = 6360.0;
STATIC const float ATMOSPHERE_RADIUS = 6420.0;
STATIC const vec3 PLANET_CENTER = vec3(0.0, -6360.0, 0.0);
STATIC const vec3 RAYLEIGH_SCATTER = vec3(5.802e-3, 13.558e-3, 33.1e-3);
STATIC const float RAYLEIGH_HEIGHT = 8.0;
// 미 산란(먼지·물방울 안개): 조금 흐린 여름 하늘. 지평선이 뿌옇고 노을이 짙다.
STATIC const float MIE_SCATTER = 5.0e-3;
STATIC const float MIE_EXTINCTION = 5.6e-3;
STATIC const float MIE_HEIGHT = 1.2;
STATIC const float MIE_G = 0.8;
STATIC const vec3 OZONE_ABSORB = vec3(0.65e-3, 1.881e-3, 0.085e-3);
STATIC const float OZONE_CENTER = 25.0;
STATIC const float OZONE_WIDTH = 15.0;
// 하늘을 비추는 햇빛의 세기, 구름을 비추는 햇빛의 세기(톤매핑 전 값)
STATIC const float SKY_SUN = 9.0;
STATIC const float CLOUD_SUN = 1.7;
// 여러 번 흩어진 빛: 한 번 흩어진 빛만 셈하면 해 진 뒤 하늘이 너무 빨리 까매진다.
STATIC const float MULTI_SCATTER = 0.22;
STATIC const int SKY_STEPS = 16;
// 땅을 비추는 햇빛의 세기
STATIC const float GROUND_SUN = 3.0;
// 해 진 뒤 남는 푸른 빛(블루아워). 높은 하늘이 받은 빛이 여러 번 흩어져 한동안 하늘을 파랗게 채운다.
STATIC const vec3 BLUE_HOUR = vec3(0.3, 0.5, 1.0) * 0.014;
// 깊은 밤에도 남는 하늘빛(대기광과 별빛). 달이 없어도 하늘이 먹빛이 아니라 아주 짙은 남색으로 남는다.
STATIC const vec3 NIGHT_GLOW = vec3(0.16, 0.24, 0.55) * 0.0034;
// 달: 실제(지름 0.5°)보다 크게 그린다(지름 약 1°). 달 원반의 밝기(톤매핑 전)와 어두운 쪽을 비추는 지구빛.
STATIC const float MOON_RADIUS = 0.0085;
STATIC const float MOON_BRIGHT = 1.1;
STATIC const float EARTHSHINE = 0.015;
STATIC const int SUN_STEPS = 8;
// 땅: 먼 들판과 숲의 반사율
STATIC const vec3 GROUND_ALBEDO = vec3(0.09, 0.105, 0.07);

// 광선 위 거리마다 쌓인 공기 빛(보는 사람 쪽 감쇠를 거친 값)과 공기를 지나 남은 빛
STATIC float gAirD[17];
STATIC vec3 gAirL[17];
STATIC vec3 gAirT[17];
// 고도별 햇빛(대기를 지나 남은 비율): 0, 2, 5, 9, 14 km
STATIC vec3 gSunAt[5];
STATIC vec3 gMulti;

float SunElevationDeg() { return degrees(asin(clamp(SunDir().y, -1.0, 1.0))); }

// 해가 진 정도: 해가 지평선에 걸리면 0, 8° 아래로 내려가면 1.
float Twilight() { return smoothstep(1.0, -8.0, SunElevationDeg()); }

// 공 반지름 r과 만나는 두 거리. 만나지 않으면 (-1, -1).
vec2 RaySphere(vec3 o, vec3 d, float r) {
  vec3 c = o - PLANET_CENTER;
  float b = dot(c, d);
  float disc = b * b - (dot(c, c) - r * r);
  if (disc < 0.0) return vec2(-1.0, -1.0);
  float h = sqrt(disc);
  return vec2(-b - h, -b + h);
}

float Altitude(vec3 p) { return length(p - PLANET_CENTER) - PLANET_RADIUS; }

// 고도 h에서 레일리·미·오존의 밀도
vec3 AirDensity(float h) {
  return vec3(exp(-max(h, 0.0) / RAYLEIGH_HEIGHT), exp(-max(h, 0.0) / MIE_HEIGHT), max(0.0, 1.0 - abs(h - OZONE_CENTER) / OZONE_WIDTH));
}


vec3 Extinction(vec3 rho) { return RAYLEIGH_SCATTER * rho.x + v3(MIE_EXTINCTION * rho.y) + OZONE_ABSORB * rho.z; }

// p에서 해까지 대기를 지나 남은 햇빛. 땅에 가리면 0(가장자리는 부드럽게: 굴절과 반그림자).
vec3 SunTransmittance(vec3 p, vec3 sd) {
  vec3 c = p - PLANET_CENTER;
  float b = dot(c, sd);
  float closest = sqrt(max(dot(c, c) - b * b, 0.0)) - PLANET_RADIUS;
  float lit = b > 0.0 ? 1.0 : smoothstep(-0.6, 0.6, closest);
  if (lit <= 0.0) return v3(0.0);
  float tEnd = RaySphere(p, sd, ATMOSPHERE_RADIUS).y;
  float dt = tEnd / float(SUN_STEPS);
  vec3 od = v3(0.0);
  for (int i = 0; i < SUN_STEPS; i++) {
    vec3 q = p + sd * (dt * (float(i) + 0.5));
    od += Extinction(AirDensity(Altitude(q))) * dt;
  }
  return exp(-od) * lit;
}

float RayleighPhase(float mu) { return 3.0 / (16.0 * PI) * (1.0 + mu * mu); }

// 편광 하늘(짙은 하늘로 극적인 명암을 만든다): 해에서 90° 떨어진 하늘의 한 번 흩어진
// 빛은 거의 다 편광돼 있다(레일리 편광도 sin²θ/(1+cos²θ)). 사진가가 편광 필터로 그 빛을 걸러 하늘을 짙게 하듯
// POLARIZER만큼 걸러 해 반대편·옆 하늘이 짙은 파랑이 된다. 구름과 여러 번 흩어진 빛은 편광되지 않아 그대로다.
STATIC const float POLARIZER = 0.6;
float Polarizer(float mu) { return 1.0 - POLARIZER * (1.0 - mu * mu) / (1.0 + mu * mu); }

float MiePhase(float mu) {
  float g2 = MIE_G * MIE_G;
  return 3.0 / (8.0 * PI) * ((1.0 - g2) * (1.0 + mu * mu)) / ((2.0 + g2) * pow(1.0 + g2 - 2.0 * MIE_G * mu, 1.5));
}

// 고도별 햇빛을 미리 셈해 둔다(구름은 이 값을 고도로 사이에 끼워 읽는다). 여러 번 흩어진 빛의 세기도 함께.
void PrepareSun(vec3 sd) {
  gSunAt[0] = SunTransmittance(EYE + vec3(0.0, 0.0, 0.0), sd);
  gSunAt[1] = SunTransmittance(EYE + vec3(0.0, 2.0, 0.0), sd);
  gSunAt[2] = SunTransmittance(EYE + vec3(0.0, 5.0, 0.0), sd);
  gSunAt[3] = SunTransmittance(EYE + vec3(0.0, 9.0, 0.0), sd);
  gSunAt[4] = SunTransmittance(EYE + vec3(0.0, 14.0, 0.0), sd);
  // 여러 번 흩어진 빛은 하늘 전체가 받은 햇빛에 비례한다. 해가 져도 높은 하늘이 받은 빛이 한동안 남는다.
  vec3 high = SunTransmittance(EYE + vec3(0.0, 30.0, 0.0), sd);
  gMulti = (gSunAt[1] * 0.6 + gSunAt[3] * 0.25 + high * 0.15) * MULTI_SCATTER * (0.25 + 0.75 * smoothstep(-0.1, 0.3, sd.y))
           * LightScale();
  // 블루아워와 밤하늘 빛은 해가 진 정도를 따른다(달빛과 따로).
  float el = SunElevationDeg();
  gMulti += BLUE_HOUR * smoothstep(-15.0, -1.0, el) * smoothstep(3.0, -1.0, el);
  gMulti += NIGHT_GLOW * smoothstep(0.2, 0.8, Night());
}

// 불타는 노을: 해가 지평선 가까이(위 10°~아래 4°) 있으면 구름이 받는 햇빛이
// 더 세고 더 붉다. 실제로도 낮은 해는 긴 공기를 지나 붉어지지만, 노을 사진처럼 구름 밑이 타오르게 빛깔을 한 번 더 짙게 한다.
float SunsetFire() {
  float el = SunElevationDeg();
  return smoothstep(10.0, 1.0, el) * smoothstep(-4.5, -0.5, el);
}

vec3 Fire(vec3 c) {
  float k = SunsetFire();
  if (k <= 0.0) return c;
  float m = max(max(c.r, c.g), max(c.b, 1e-4));
  vec3 n = pow(c / m, v3(1.0 + 0.9 * k));
  return n * m * (1.0 + 0.7 * k);
}

// 고도 h(km)의 햇빛(대기를 지나 남은 비율). 해가 지면 땅 그림자보다 낮은 곳은 0이다.
vec3 SunLitRaw(float h) {
  if (h < 2.0) return mix(gSunAt[0], gSunAt[1], h / 2.0);
  if (h < 5.0) return mix(gSunAt[1], gSunAt[2], (h - 2.0) / 3.0);
  if (h < 9.0) return mix(gSunAt[2], gSunAt[3], (h - 5.0) / 4.0);
  if (h < 14.0) return mix(gSunAt[3], gSunAt[4], (h - 9.0) / 5.0);
  // 더 높은 곳(야광운)은 해를 거의 막힘없이 받는다. 땅 그림자만 따진다.
  vec3 c = EYE + vec3(0.0, h, 0.0) - PLANET_CENTER;
  vec3 sd = SunDir();
  float b = dot(c, sd);
  float closest = sqrt(max(dot(c, c) - b * b, 0.0)) - PLANET_RADIUS;
  return v3(b > 0.0 ? 1.0 : smoothstep(-2.0, 2.0, closest - 12.0));
}

vec3 SunLit(float h) { return Fire(SunLitRaw(h)); }

// 보는 방향으로 대기를 걸으며 공기 빛을 쌓는다. 가까운 곳을 촘촘히 걷도록 거리를 지수로 나눈다.
// 반환: 끝(땅 또는 대기 밖)까지의 거리. 땅에 닿았으면 hitGround가 참이다.
float IntegrateAir(vec3 ro, vec3 rd, vec3 sd, out bool hitGround) {
  float tEnd = RaySphere(ro, rd, ATMOSPHERE_RADIUS).y;
  vec2 g = RaySphere(ro, rd, PLANET_RADIUS);
  hitGround = g.x > 0.0;
  if (hitGround) tEnd = g.x;
  float mu = dot(rd, sd);
  float pr = RayleighPhase(mu);
  float pm = MiePhase(mu);
  const float spread = 3.5;
  float norm = 1.0 / (exp(spread) - 1.0);
  vec3 T = v3(1.0);
  vec3 L = v3(0.0);
  gAirD[0] = 0.0;
  gAirL[0] = L;
  gAirT[0] = T;
  float t0 = 0.0;
  for (int i = 0; i < SKY_STEPS; i++) {
    float t1 = tEnd * (exp(spread * float(i + 1) / float(SKY_STEPS)) - 1.0) * norm;
    float dt = t1 - t0;
    vec3 p = ro + rd * (0.5 * (t0 + t1));
    vec3 rho = AirDensity(Altitude(p));
    vec3 ext = Extinction(rho);
    vec3 scatR = RAYLEIGH_SCATTER * rho.x;
    float scatM = MIE_SCATTER * rho.y;
    vec3 sunT = SunTransmittance(p, sd);
    vec3 source = (scatR * pr * Polarizer(mu) + v3(scatM * pm)) * sunT * LightScale() + (scatR + v3(scatM)) * gMulti;
    vec3 stepT = exp(-ext * dt);
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
void AirAt(float d, out vec3 light, out vec3 transmit) {
  light = gAirL[SKY_STEPS];
  transmit = gAirT[SKY_STEPS];
  for (int i = 0; i < SKY_STEPS; i++) {
    if (d <= gAirD[i + 1]) {
      float k = clamp((d - gAirD[i]) / max(gAirD[i + 1] - gAirD[i], 1e-4), 0.0, 1.0);
      light = mix(gAirL[i], gAirL[i + 1], k);
      transmit = mix(gAirT[i], gAirT[i + 1], k);
      return;
    }
  }
}

// 흐린 정도: 넓게 덮는 층구름일수록 하늘을 잿빛으로 누른다.
float Overcast() { return max(max(_Low.w, _Mid.y * 0.85), max(_Low.y * 0.35, _Low.x * 0.45)); }

// 층구름을 지나 땅 가까이까지 오는 햇빛의 비율.
float SunTransmit() {
  return (1.0 - 0.85 * _Low.w) * (1.0 - 0.7 * _Mid.y) * (1.0 - 0.45 * _Low.y) * (1.0 - 0.3 * _Low.x) * (1.0 - 0.15 * _High.z);
}

// 흐리면 하늘이 잿빛으로, 뇌우면 더 어둡게 가라앉는다. 밝기는 하늘 빛에서 가져온다.
vec3 Overcasted(vec3 col) {
  float lum = dot(col, vec3(0.3, 0.5, 0.2));
  vec3 grey = v3(lum) * vec3(0.95, 0.98, 1.04) * mix(1.0, 0.45, _Storm);
  return mix(col, grey, clamp(max(_Storm * 0.9, Overcast() * 0.75), 0.0, 1.0));
}

// ---- 땅 ----
// 박명광(퍼플 라이트): 해가 지평선 아래 2~6°로 내려가면, 높은 하늘의 먼지가 붉어진 햇빛을 받아
// 해 진 쪽 하늘이 분홍·보랏빛으로 한 번 더 달아오른다. 노을이 끝났다 싶을 때 다시 번지는 빛이다.
vec3 PurpleLight(vec3 rd, vec3 sd) {
  float el = SunElevationDeg();
  float k = smoothstep(-1.0, -3.0, el) * smoothstep(-7.0, -4.0, el);
  if (k <= 0.0 || rd.y < 0.0) return v3(0.0);
  vec2 a = normalize(rd.xz + vec2(1e-4, 0.0));
  vec2 b = normalize(sd.xz + vec2(1e-4, 0.0));
  float toward = pow(max(dot(a, b), 0.0), 1.5);
  float band = smoothstep(0.02, 0.12, rd.y) * (1.0 - smoothstep(0.25, 0.6, rd.y));
  return vec3(0.95, 0.38, 0.62) * 0.06 * k * toward * band;
}

// 별: 박명이 깊어지면 하나둘 돋는다.
vec3 Stars(vec3 rd) {
  float tw = Twilight();
  if (tw < 0.5 || rd.y < 0.02) return v3(0.0);
  vec2 cell = floor(vec2(atan(rd.x, rd.z), asin(clamp(rd.y, -1.0, 1.0))) * 300.0);
  float star = smoothstep(0.9978, 1.0, Hash2(cell)) * smoothstep(0.5, 1.0, tw) * smoothstep(0.05, 0.35, rd.y);
  // 보이는 장을 만들며 살짝 거르므로(섞기 패스) 그만큼 밝게 찍는다.
  return star * vec3(0.8, 0.88, 1.0) * 0.6 * mix(1.0, 1.5, Night()) * (1.0 - 0.6 * MoonShine()) * (1.0 - Overcast());
}

// 달 원반: 해 쪽이 밝고, 찬 정도만큼 둥글게 찬다. 어두운 쪽은 지구빛으로 아주 옅게 비치고, 바다(어두운 무늬)가 얼룩진다.
// 낮에도 떠 있으면 하늘빛에 묻혀 옅게 보인다.
vec3 MoonDisc(vec3 rd) {
  vec3 m = MoonDir();
  if (m.y < -0.02 || dot(rd, m) < cos(MOON_RADIUS * 1.2)) return v3(0.0);
  vec3 side = abs(m.y) > 0.99 ? vec3(1.0, 0.0, 0.0) : normalize(cross(vec3(0.0, 1.0, 0.0), m));
  vec3 up = cross(m, side);
  vec2 q = vec2(dot(rd, side), dot(rd, up)) / MOON_RADIUS;
  float r = length(q);
  if (r >= 1.0) return v3(0.0);
  // 보는 쪽 반구의 겉면 방향
  vec3 n = side * q.x + up * q.y - m * sqrt(max(1.0 - r * r, 0.0));
  // 비추는 해: 원반 위에서 해가 있는 쪽으로, 찬 정도(위상)만큼 달 뒤로 돌아간다.
  vec3 s = SunDir();
  vec3 toward = s - m * dot(s, m);
  vec3 u = length(toward) > 1e-4 ? normalize(toward) : up;
  float cosPhase = 2.0 * clamp(_Moon.w, 0.0, 1.0) - 1.0;
  vec3 light = -m * cosPhase + u * sqrt(max(1.0 - cosPhase * cosPhase, 0.0));
  float lit = smoothstep(-0.04, 0.12, dot(n, light));
  float maria = mix(0.72, 1.0, smoothstep(0.38, 0.62, N(vec3(q * 0.23 + 0.5, 0.61)).r));
  float edge = 1.0 - smoothstep(0.8, 1.0, r);
  return vec3(1.0, 0.97, 0.9) * (lit * maria + EARTHSHINE) * MOON_BRIGHT * edge;
}

// 채운의 빛깔(실제보다 아름다움을 앞세운다): 사진 속 채운처럼 무지개 차례로 맑고 짙게 번진다.
// x 0 빨강 → 주황 → 노랑 → 초록 → 청록 → 파랑 → 1 보라. 화면에서 본 빛깔(sRGB)로 고르고 선형으로 옮겼다.
vec3 Spectrum(float x) {
  float k = clamp(x, 0.0, 1.0) * 6.0;
  vec3 red = vec3(1.0, 0.06, 0.08);       // sRGB (1.0, 0.27, 0.32)
  vec3 orange = vec3(1.0, 0.32, 0.03);    // sRGB (1.0, 0.6, 0.2)
  vec3 yellow = vec3(1.0, 0.83, 0.1);     // sRGB (1.0, 0.92, 0.35)
  vec3 green = vec3(0.13, 1.0, 0.17);     // sRGB (0.4, 1.0, 0.45)
  vec3 cyan = vec3(0.05, 0.69, 1.0);      // sRGB (0.25, 0.85, 1.0)
  vec3 blue = vec3(0.1, 0.22, 1.0);       // sRGB (0.35, 0.5, 1.0)
  vec3 violet = vec3(0.45, 0.13, 1.0);    // sRGB (0.7, 0.4, 1.0)
  vec3 a = k < 1.0 ? red : (k < 2.0 ? orange : (k < 3.0 ? yellow : (k < 4.0 ? green : (k < 5.0 ? cyan : blue))));
  vec3 b = k < 1.0 ? orange : (k < 2.0 ? yellow : (k < 3.0 ? green : (k < 4.0 ? cyan : (k < 5.0 ? blue : violet))));
  return mix(a, b, smoothstep(0.0, 1.0, fract(min(k, 5.999))));
}

// 밝은 구름 빛 c를 무지개 빛깔 x로 물들인다(sat 0 그대로 ~ 1 가장 짙게). 밝은 빛에 빛깔을 곱하기만 하면 톤매핑에서
// 채널이 1에 걸려 하얗게 바래므로, 구름 밝기를 IRI_LUMINANCE 아래로 누른 뒤 IRI_BRIGHT배 한 빛깔로 바꾼다.
STATIC const float IRI_LUMINANCE = 1.0;
STATIC const float IRI_BRIGHT = 1.25;
vec3 Iridesce(vec3 c, float x, float sat) {
  float l = dot(c, vec3(0.2126, 0.7152, 0.0722));
  return mix(c, Spectrum(x) * min(l, IRI_LUMINANCE) * IRI_BRIGHT, clamp(sat, 0.0, 1.0));
}

// 빛깔 차례가 돌아가는 곳(구름 조각마다 다른 빛깔)에서는 빨강→보라→빨강으로 되짚어 끊김 없이 잇는다.
float SpectrumCycle(float ph) { return 1.0 - abs(fract(ph) * 2.0 - 1.0); }

// ---- 웅대적운 탑 · 적란운 모루 · 채운 갓구름 ----

float TowerTop() { return mix(4.5, 13.0, _Growth); }

// 모루가 왼쪽(바람 부는 쪽)으로 뻗은 길이(km)
float AnvilReach() { return mix(2.0, 11.0, _Special.x); }

// 봉우리 하나: 큰 구 송이를 쌓는다. 송이마다 옆으로 비껴 윤곽이 크게 불룩불룩하다.
float Column(vec3 q, vec2 c, float h, float rb, float rt, float salt) {
  float d = 1e5;
  for (int j = 0; j < 8; j++) {
    float t = float(j) / 7.0;
    float r = mix(rb, rt, t) * (0.88 + 0.24 * H1(salt + float(j) * 3.1 + _Seed));
    float y = mix(rb * 0.55, h - rt * 0.85, t);
    vec2 off = (vec2(H1(salt + float(j) * 5.7 + _Seed), H1(salt + float(j) * 7.3 + _Seed)) - 0.5) * vec2(1.3, 0.9) * r * 0.55;
    d = SMin(d, length(q - vec3(c.x + off.x, y, c.y + off.y)) - r, 0.55);
  }
  return d;
}

// 몸통의 큰 틀: 가운데 가장 높은 봉우리 둘레로 높이가 다른 봉우리들이 엉겨 넓은 산을 이룬다.
// 위로 갈수록 송이가 굵어져 꼭대기가 부푼다(적란운으로 자라기 직전의 웅대적운). 왼쪽 아래와 오른쪽 아래에는 낮은 곁 덩어리가 붙는다.
float Envelope(vec3 q) {
  float h = TowerTop();
  float d = Column(q, vec2(0.0, 0.0), h, 2.4, 2.5, 1.0);
  d = SMin(d, Column(q, vec2(-2.3, 0.7), max(h * 0.62, 2.6), 2.1, 1.9, 11.0), 0.7);
  d = SMin(d, Column(q, vec2(2.4, -0.5), max(h * 0.82, 2.8), 2.1, 2.0, 23.0), 0.8);
  d = SMin(d, Column(q, vec2(-1.1, -1.3), max(h * 0.9, 2.8), 1.8, 1.8, 37.0), 0.8);
  d = SMin(d, Column(q, vec2(0.9, 1.9), max(h * 0.72, 2.6), 1.9, 1.7, 41.0), 0.8);
  d = SMin(d, Column(q, vec2(-3.9, -0.2), max(h * 0.36, 2.2), 1.9, 1.5, 53.0), 0.7);
  d = SMin(d, Column(q, vec2(4.3, 0.6), max(h * 0.46, 2.4), 1.8, 1.5, 67.0), 0.7);
  return max(d, -q.y);
}

// 적란운의 모루: 탑 꼭대기가 대류권 끝에 막혀 옆으로 퍼진 평평한 구름. 바람 부는 쪽(왼쪽)으로 길게 뻗으며
// 끝으로 갈수록 얇아져 실처럼 흩어진다. 밑면은 탑 쪽으로 처져 몸통과 이어지고, 윗면은 매끈하다.
float AnvilDensity(vec3 q, vec3 drift) {
  float a = _Special.x;
  if (a <= 0.001) return 0.0;
  float h = TowerTop();
  float reach = AnvilReach();
  if (q.x > 3.0 || q.x < -reach * ANVIL_TAIL - 2.0 || q.y < h - 3.5 || q.y > h + 1.6) return 0.0;
  float alongRaw = -q.x / reach;
  float along = clamp(alongRaw, 0.0, 1.0);          // 0 탑 위 ~ 1 모루 끝
  float halfWidth = mix(3.0, 6.0, along);
  float side = abs(q.z) / halfWidth;
  if (side > 1.3) return 0.0;
  float fib = N(vec3(q.x * 0.05, q.y * 0.7, q.z * 0.15) + drift * 0.5).r;
  float fib2 = N(vec3(q.x * 0.15, q.y * 1.5, q.z * 0.4) + drift).r;
  // 윗면은 가운데가 조금 솟고 끝으로 낮아진다. 밑면은 탑 가까이에서 몸통으로 처져 내려온다.
  float top = h + mix(1.2, 0.4, along) - side * side * 0.3;
  float bottom = h - 0.1 + along * 0.2 - pow(1.0 - along, 3.0) * 2.8 + (fib - 0.5) * 0.5;
  float thick = max(top - bottom, 0.05);
  float inside = min(q.y - bottom, top - q.y) / thick * 2.0;
  // 끝은 잘리지 않고 실처럼 풀려 사라진다(셈을 끊는 자리보다 한참 앞에서 0이 된다).
  float end = 1.0 - smoothstep(0.4, 1.15, alongRaw + (fib2 - 0.5) * 0.45);
  float edge = 1.0 - smoothstep(0.45, 1.2, side + (fib2 - 0.5) * 0.5);
  float root = smoothstep(3.0, 0.8, q.x);
  return Remap(inside + (fib2 - 0.5) * 0.5, 0.0, 0.5) * end * edge * root * a;
}

// 너울 선반(벨룸): 탑이 습한 층을 뚫고 솟으면 그 층이 탑 허리에 얇고 매끈한 선반처럼 둘린다(사진의 탑 양옆 날개).
// 높이가 다른 선반이 한두 겹 탑에서 바깥으로 넓게 뻗고, 끝으로 갈수록 얇아지며 조금 처진다. 탑이 새로 솟을 때마다(_Seed)
// 선반이 있기도 없기도 하고 높이·너비가 다르다. 같은 층에서 생긴 너울이라 결이 옆으로 길게 늘어난다.
float VelumDensity(vec3 q, float h) {
  float v = 0.0;
  for (int k = 0; k < 2; k++) {
    float salt = _Seed * 1.7 + float(k) * 5.3;
    // 첫 겹은 흔하고 둘째 겹은 드물다. 둘째 겹은 첫 겹보다 탑 높이의 1/4쯤 위에 둘린다.
    if (H1(salt) < (k == 0 ? VELUM_ABSENT : VELUM_SECOND_ABSENT)) continue;
    float yc = h * (mix(0.36, 0.56, H1(_Seed * 1.7 + 1.1)) + float(k) * 0.24);
    if (abs(q.y - yc) > 1.4) continue;
    // 옆(바람 방향)으로 길고 앞뒤로 얇다. 바람 부는 쪽(왼쪽)으로 더 길게 뻗는다.
    vec2 xz = q.xz * vec2(q.x < 0.0 ? 0.9 : 1.0, 2.4);
    float r = length(xz);
    float reach = mix(4.4, 6.4, H1(salt + 2.3));
    if (r > reach) continue;
    float u = r / reach;
    float wave = (N(vec3(xz * 0.1, fract(salt))).r - 0.5) * 0.6;
    float across = (q.y - (yc - u * u * 0.7 + wave)) / mix(0.3, 0.07, u);
    float shell = 1.0 - across * across;
    if (shell <= 0.0) continue;
    float silk = N(vec3(xz.x * 0.06, xz.y * 0.3, q.y * 0.5) + vec3(_SkyTime * 0.002, 0.0, fract(salt * 0.37))).r;
    float edge = 1.0 - smoothstep(0.55, 1.0, u + (silk - 0.5) * 0.35);
    // 탑 몸통 둘레에서는 송이에 묻힌다(앞면을 가로지르는 띠가 되지 않게). 몸통 밖으로 나온 날개만 남는다.
    float outside = smoothstep(0.3, 0.55, u);
    v = max(v, shell * edge * outside * mix(0.45, 1.0, smoothstep(0.3, 0.7, silk)));
  }
  return v * 0.7 * _Extra.z * (1.0 - _Storm);
}

// 밀도 0~1. 큰 틀 + 큰·중간·잔 송이를 한 장(場)에 더하고 얇게 문턱을 넘겨 또렷한 겉면을 세운다.
float TowerDensity(vec3 p, bool detail) {
  vec3 q = p - TOWER;
  float h = TowerTop();
  vec3 drift = vec3(_SkyTime * 0.004, -_SkyTime * 0.01, 0.0) + vec3(_Seed * 1.37, 0.0, _Seed * 0.71);
  float anvil = max(AnvilDensity(q, drift), VelumDensity(q, h));
  if (q.y < -0.2 || q.y > h + 2.2 || abs(q.x) > TOWER_REACH.x || abs(q.z) > TOWER_REACH.y) return anvil;
  float warp = N(q * 0.045 + drift * 0.3).r - 0.5;
  float env = Envelope(q) + warp * 0.9;
  if (env > 2.0) return anvil;
  float f = -env * 0.6;
  // 송이의 층: 큰 송이 → 중간 송이 → 잔 송이. 워리를 반구꼴로 바꿔 겉으로 불룩하게 더한다.
  float upper = smoothstep(0.45, 0.95, q.y / h);
  f += (Dome(N(q * 0.09 + drift).g) - 0.6) * mix(1.1, 1.45, upper);
  if (f < -0.7) return anvil;
  float crisp = smoothstep(0.25, 0.8, q.y / h);
  if (detail) {
    f += (Dome(N(q * 0.09 + drift * 1.3).b) - 0.5) * mix(0.55, 0.75, upper);
    f += (Dome(N(q * 0.09 + drift * 1.8).a) - 0.6) * mix(0.2, 0.34, crisp);
    // 가장 잔 송이(약 0.35 km): 겉이 매끈한 돌처럼 보이지 않게 한다.
    f += (Dome(N(q * 0.19 + drift * 2.2).a) - 0.6) * mix(0.06, 0.12, crisp);
    f += (N(q * 0.9 + drift * 2.4).r - 0.5) * 0.035;
    // 겉 가장자리: 잔 무늬로 갉아 실오라기처럼 풀린다. 해 받는 봉우리 위쪽은 또렷하고 옆·밑은 흐릿하다.
    float edge = 1.0 - smoothstep(0.0, 0.3, f);
    float wisp = N(q * 0.6 + drift * 3.0).r;
    f -= edge * (wisp - 0.3) * mix(0.4, 0.08, crisp);
  } else {
    f += 0.02;
  }
  // 밑면은 평평하고 조금 흐릿하다.
  f -= smoothstep(0.35, 0.0, q.y) * 0.4;
  // 겉에서 속으로 천천히 짙어진다. 겉껍질이 반투명해야 덩어리가 아니라 김으로 읽힌다.
  return max(Remap(f, 0.0, mix(0.4, 0.07, crisp)) * _Extra.z, anvil);
}

// 채운 갓구름: 꼭대기를 두건처럼 덮는 얇고 매끈한 너울. 탑 꼭대기 송이가 아래에서 밀고 올라와 너울 가운데를 뚫기도 한다.
// 꼭대기 송이들이 h 가까이까지 부풀므로 너울은 CAP_LIFT만큼 띄워야 송이에 묻히지 않고 갓처럼 얹혀 보인다.
STATIC const float CAP_LIFT = 1.6;
// across는 너울 가운데 면에서 잰 거리(두께 절반 단위), thin은 가장자리로 갈수록 얇아지는 정도(0 가운데 ~ 1 끝)다.
float CapDensity(vec3 p, out float across, out float thin) {
  vec3 q = p - TOWER;
  float h = TowerTop();
  across = 0.0;
  thin = 1.0;
  if (q.y < h - 2.4 || q.y > h + 2.6) return 0.0;
  vec2 xz = q.xz * vec2(1.0, 1.35);
  float r = length(xz);
  const float reach = 5.6;
  if (r > reach) return 0.0;
  float u = r / reach;
  thin = u;
  // 가운데는 봉우리 머리 바로 위, 가장자리로 갈수록 처진다. 결을 따라 살짝 물결친다.
  float wave = (N(vec3(xz * 0.12, 0.2) + vec3(_SkyTime * 0.002, 0.0, _Seed)).r - 0.5) * 0.5;
  float yc = h + CAP_LIFT - r * r / (2.0 * 5.5) + wave;
  float th = 0.34 * (1.0 - u * u) + 0.04;
  across = (q.y - yc) / th;
  float shell = 1.0 - across * across;
  if (shell <= 0.0) return 0.0;
  // 비단 결: 너울을 따라 길게 늘어난 무늬로 군데군데 얇아진다.
  float silk = N(vec3(xz.x * 0.08, xz.y * 0.35, q.y * 0.6) + vec3(_SkyTime * 0.003, 0.0, _Seed)).r;
  // 가장자리는 얇게 풀려 고리 실루엣이 서지 않는다. 탑이 적란운이 되어 모루가 서면 갓구름은 모루에 흡수되어 걷힌다.
  float edge = smoothstep(1.0, 0.55, u) * mix(0.6, 1.0, smoothstep(0.85, 0.5, u + (silk - 0.5) * 0.3));
  float calm = (1.0 - _Storm) * (1.0 - smoothstep(0.02, 0.25, _Special.x));
  return shell * edge * mix(0.45, 1.0, smoothstep(0.3, 0.7, silk)) * _Cap * calm * _Extra.z;
}

// ---- 뭉게구름 떼(적운) ----
// 맑은 날 낮게 흩어진 뭉게구름. 밑면이 평평하고 위가 둥글게 부푼다. 탑보다 가까워 크게 보이고, 오후일수록 자란다.

// 송이 꼭대기의 높이(밑면에서 km). 0이면 구름이 없다. 송이는 서로 떨어져 있고 오후일수록 높이 자란다.
// 송이 크기는 고르지 않다: 실제 뭉게구름 밭은
// 작은 송이가 아주 많고 큰 송이는 드물며, 작은 것은 납작하고(편평운) 큰 것일수록 높이 솟는다(중간운).
// 크기가 다른 세 겹의 송이 자리(간격 약 1.2·2.5·5.5 km)를 겹치고, 클수록 드물게(문턱을 높게) 하고 높이 솟게 한다.
float CumulusHeight(vec2 xz) {
  float cov = _Low.z;
  vec2 w = xz - LowDrift();
  float field = N(vec3(w * 0.02, 0.29)).r;
  float region = (field - 0.55) * 1.2 * cov;
  float small = (Dome(N(vec3(w * 0.1, 0.61)).b) - mix(0.985, 0.85, cov)) * 5.0;
  float medium = (Dome(N(vec3(w * 0.1, 0.17)).g) - mix(1.0, 0.87, cov)) * 7.0;
  // 큰 송이는 드물게, 낮은 주파수 자리(field)가 높은 곳에만 선다.
  float large = (Dome(N(vec3(w * 0.045, 0.83)).g) - mix(1.03, 0.92, cov) - (0.55 - field) * 0.5) * 22.0;
  float grow = mix(0.8, 1.5, smoothstep(0.15, 0.75, _Progress));
  float h = max(max(small, medium), large) + region;
  return max(h, 0.0) * grow;
}

// 송이 하나는 봉우리 두세 개가 솟은 덩어리다: 몸통(CumulusHeight)의
// 지붕을 봉우리 칸(약 0.8 km)이 군데군데 밀어 올리고, 겉에는 콜리플라워 송이(약 0.3 km)와 잔 혹(약 0.12 km)이 부푼다.
// 위쪽일수록 송이가 불룩하고 겉면이 또렷하며(해 받는 윗면), 밑면은 평평하고 조금 흐릿하다.
// 바람에 흘러가며 송이가 천천히 끓어오른다.
float CumulusDensity(vec3 p, bool detail) {
  float y = p.y - CUMULUS_BASE;
  if (y < -0.05 || y > CUMULUS_DEPTH) return 0.0;
  float h = CumulusHeight(p.xz);
  if (h <= 0.0) return 0.0;
  vec3 w = p - vec3(LowDrift().x, _SkyTime * 0.006, LowDrift().y);
  float turret = Dome(N(vec3(w.xz * 0.16, 0.41)).b);
  float top = h * (0.55 + 0.75 * turret);
  float f = (top - y) * 1.3;
  if (f < -0.9) return 0.0;
  float upper = clamp(y / max(top, 0.1), 0.0, 1.0);
  f += (Dome(N(w * 0.4).b) - 0.55) * mix(0.35, 0.7, upper);
  if (detail) {
    // 잔 혹은 멀어질수록 한 화소보다 잘아져 반짝이는 잔점이 되므로 거리에 따라 줄인다.
    float far = length(p.xz);
    f += (Dome(N(w * 0.9).a) - 0.6) * mix(0.12, 0.26, upper) * (1.0 - smoothstep(10.0, 28.0, far));
    f += (N(w * 1.3).r - 0.5) * 0.05 * (1.0 - smoothstep(5.0, 14.0, far));
  }
  f -= smoothstep(0.12, 0.0, y) * 0.5;
  // 멀리 지평선 쪽 송이는 공기에 잠겨 옅어진다.
  float distant = 1.0 - smoothstep(30.0, 55.0, length(p.xz));
  return Remap(f, 0.0, mix(0.4, 0.12, upper)) * _Low.z * distant;
}

// ---- 평면 구름층 ----

// 고도 h의 평면과 만나는 거리. 없으면 -1.
float PlaneHit(vec3 ro, vec3 rd, float h) { return rd.y > 0.003 ? (h - ro.y) / rd.y : -1.0; }

// 층의 높이 굴곡(km, 평균 0): 수십 km에 걸친 긴 굽이와 완만한 기울기. 구름층은 반듯한 판이 아니라
// 이쪽이 조금 높고 저쪽이 낮게 기울고 너울처럼 굽이친다. salt가 층마다(층 높이가 새로 정해질 때마다) 모양을 바꾼다.
float LayerLift(vec2 xz, float salt) {
  float wave = N(vec3(xz * 0.011, fract(salt))).r - 0.5;
  vec2 tilt = vec2(sin(salt * 12.9898), cos(salt * 78.233));
  return wave * 1.4 + dot(xz, tilt) * 0.006;
}

// 높이 h(평균)인 굽이친 층과 만나는 거리. 평균 높이에서 만난 자리의 굴곡만큼 한 번 고쳐 잡는다. amp는 굴곡 배율.
float LayerHit(vec3 ro, vec3 rd, float h, float amp) {
  float t = PlaneHit(ro, rd, h);
  if (t < 0.0) return t;
  return PlaneHit(ro, rd, h + LayerLift((ro + rd * t).xz, h * 7.31) * amp);
}

// 보는 방향(방위·고도, 도)을 고도 h 평면 위의 점으로 옮긴다. 방향으로 자리를 정하는 구름(구멍 등)에 쓴다.
vec2 DirectionOnPlane(float az, float el, float h) {
  float d = (h - EYE.y) / tan(radians(el));
  return vec2(sin(radians(az)), cos(radians(az))) * d;
}

// 구멍구름: 과냉각 물방울 층 한가운데가 얼며 떨어져 동그랗게 뚫리고, 가운데로 얼음 꼬리가 늘어진다.
// 시계 왼쪽 아래 하늘(방위 −9°, 고도 24°)에 뚫린다. 반환: 층을 남기는 비율, iceFall은 가운데 얼음 꼬리.
float FallstreakHole(vec2 xz, float altitude, float radius, out float iceFall) {
  iceFall = 0.0;
  float hole = _Special.w;
  if (hole <= 0.001) return 1.0;
  vec2 c = DirectionOnPlane(-9.0, 24.0, altitude) + _DriftHigh.zw;
  vec2 d = xz - c;
  // 가장자리는 매끈한 타원이 아니라 해져 들쭉날쭉하다(큰 굽이와 잔 해짐 두 겹).
  float fray = N(vec3(xz * 0.18, 0.93)).r * 0.65 + N(vec3(xz * 0.6, 0.41)).r * 0.35;
  float r = length(d * vec2(1.0, 1.25)) + (fray - 0.5) * radius * 0.6;
  float keep = smoothstep(radius * 0.7, radius * 1.25, r);
  // 얼음 꼬리: 가운데보다 조금 바람 아래에 작고 부드러운 비단 다발로 늘어진다. 결은 성기고 군데군데 끊긴다.
  vec2 f = Rotate(d - vec2(radius * 0.15, 0.0), 0.4);
  float fibers = N(vec3(f.x * 0.25, f.y * 1.1, 0.37)).r * 0.7 + N(vec3(f.x * 0.6, f.y * 2.0, 0.19)).r * 0.3;
  float tuft = 1.0 - smoothstep(0.0, radius * 0.6, length(f * vec2(0.8, 1.3)) + (fray - 0.5) * radius * 0.4);
  iceFall = hole * tuft * Remap(fibers, 0.4, 0.8) * 0.8;
  return mix(1.0, keep, hole);
}

// 권운(새털구름): 말꼬리구름. 엉긴 머리 술에서 얼음 꼬리가 바람 아래로 갈고리처럼 휘며 가늘어지는 비단실 가닥이
// 드문드문 흩어진다(하늘 전체를 고르게 덮는 평행한 결은 나뭇결처럼 인공적으로 보인다). 가닥마다 길이·휨·굵기가 다르고,
// 가닥 속은 결을 따라 가는 실이 갈라진다. 사이사이 아주 옅은 실구름이 깔린다.
// 가닥 한 칸의 크기(km), 칸에 가닥이 설 확률(구름 양 1일 때), 가닥 길이 범위(km)
STATIC const float CIRRUS_CELL = 10.0;
STATIC const float CIRRUS_FILL = 1.0;
STATIC const vec2 CIRRUS_LENGTH = vec2(7.0, 16.0);
// blur는 이 자리 한 화소가 덮는 길이(km). 가닥이 그보다 가늘면 굵히고 옅게 해 깜빡이지 않게 한다.
float CirrusDensity(vec2 xz, float blur) {
  float cov = _High.x;
  if (cov <= 0.001) return 0.0;
  vec2 w = Rotate(xz - HighDrift(), 0.35);
  vec2 base = floor(w / CIRRUS_CELL);
  float d = 0.0;
  // 가닥은 머리에서 +x(바람 아래)로 한 칸 넘게 뻗고 머리 술은 조금 앞으로도 번지므로 이웃 칸을 모두 본다.
  for (int j = -1; j <= 1; j++) {
    for (int i = -2; i <= 1; i++) {
      vec2 cell = base + vec2(float(i), float(j));
      float n = cell.x * 127.1 + cell.y * 311.7;
      if (H1(n) > CIRRUS_FILL * sqrt(cov)) continue;
      vec2 head = (cell + vec2(0.15, 0.2) + vec2(0.5, 0.6) * vec2(H1(n + 1.3), H1(n + 2.7))) * CIRRUS_CELL;
      float len = mix(CIRRUS_LENGTH.x, CIRRUS_LENGTH.y, H1(n + 4.1));
      vec2 q = w - head;
      float u = q.x;
      if (u < -3.0 || u > len + 1.0) continue;
      float s = clamp(u / len, 0.0, 1.0);
      // 꼬리는 바람 아래로 갈수록 한쪽으로 휜다(갈고리). 휘는 쪽과 정도가 가닥마다 다르다.
      float hook = (H1(n + 5.9) < 0.5 ? -1.0 : 1.0) * mix(0.015, 0.05, H1(n + 7.3));
      float wave = (N(vec3(u * 0.05, n * 0.013, 0.21)).r - 0.5) * 1.2;
      float dv = q.y - hook * max(u, 0.0) * max(u, 0.0) * 0.5 - wave * s;
      // 머리에서 꼬리 끝으로 가늘어진다. 화소보다 가늘면 굵히고 그만큼 옅게 한다.
      float width = mix(1.1, 0.12, pow(s, 0.6)) * mix(0.7, 1.3, H1(n + 8.8));
      float shown = max(width, blur * 1.2);
      // 가닥 속 결: 꼬리를 따라 길게 이어지며 갈라지는 가는 실 두 겹. 꼬리 쪽으로 갈수록 실이 벌어진다.
      float across = dv / max(shown, 0.05) / mix(1.0, 1.6, s);
      // 가닥을 가로지르는 결은 몇 가닥만 둔다(가닥 폭이 몇 화소뿐이라 더 촘촘하면 화소와 어긋나 점선이 된다).
      float threads = N(vec3(u * 0.012, across * 0.35 + n * 0.37, 0.43)).r * 0.6 + N(vec3(u * 0.02, across * 0.7 + n * 0.19, 0.71)).r * 0.4;
      // 가장자리는 결을 따라 들쭉날쭉 풀린다.
      float edge = abs(dv) + (threads - 0.5) * shown * 0.7;
      float body = (1.0 - smoothstep(shown * 0.2, shown, edge)) * (width / shown);
      body *= mix(0.3, 1.0, smoothstep(0.35, 0.72, threads));
      body *= smoothstep(-1.5, 0.3, u) * (1.0 - smoothstep(len * 0.7, len + 1.0, u));
      // 머리: 꼬리 시작에 엉긴 술(조금 더 짙고 둥글다)
      vec2 hq = q * vec2(0.55, 1.0);
      float tuft = exp(-dot(hq, hq) * 2.6) * mix(0.3, 1.0, smoothstep(0.3, 0.7, threads)) * 0.8;
      d = max(d, max(body * mix(0.95, 0.55, s), tuft));
    }
  }
  // 사이사이 아주 옅은 실구름
  vec2 fw = w + vec2(0.0, (N(vec3(w * 0.01, 0.11)).r - 0.5) * 18.0);
  float fib = Remap(N(vec3(fw.x * 0.006, fw.y * 0.12, 0.23)).r * 0.6 + N(vec3(fw.x * 0.02, fw.y * 0.4, 0.37)).r * 0.4, 0.5, 0.75);
  float where = Remap(N(vec3((xz - HighDrift()) * 0.006, 0.61)).r, 0.75 - 0.3 * cov, 0.95);
  d = max(d, fib * where * 0.5 * smoothstep(0.6, 0.1, blur));
  return d * smoothstep(0.0, 0.3, cov);
}

// 하늘 장 한 화소가 차지하는 각(라디안). ViewRig가 어느 창에서나 화소당 각을 카드와 같게 둔다: 1 / (0.95 × 680).
STATIC const float SKY_PIXEL_ANGLE = 0.00155;

// 높이 h 평면에서 이 방향 한 화소가 덮는 길이(km, 지평선 쪽으로 늘어나는 세로 방향).
// 평면 위 무늬가 이보다 잘면 화소마다 제멋대로 집혀 지평선으로 모이는 빗살이 되므로, 그만큼 무늬를 뭉갠다.
float PlaneFootprint(float h, float rdy) { return (h - EYE.y) * SKY_PIXEL_ANGLE / max(rdy * rdy, 1e-4); }

// 권적운(조개구름): 잘고 흰 구름 알갱이가 파란 틈을 두고 조각조각 모인다(알갱이 하나가 하늘에서 1° 남짓).
// 알갱이는 바람을 가로지르는 물결 줄을 따라 늘어서지만 고르지 않다. 결이 곳곳에서 휘고 벌어지며,
// 알갱이 크기도 자리마다 다르고(잘게 흩뿌린 곳, 굵게 엉긴 곳), 군데군데 알갱이가 빠져 틈이 제멋대로 트인다.
// blur는 한 화소가 덮는 길이(km). 알갱이가 화소보다 잘아지는 지평선 쪽에서는 알갱이 대신 평균 덮임으로 옅은 너울이 된다.
float CirrocumulusDensity(vec2 xz, float blur, out float iceFall) {
  iceFall = 0.0;
  float cov = _High.y;
  if (cov <= 0.001) return 0.0;
  vec2 w = Rotate(xz - HighDrift() * 0.9, -0.25);
  float fine = smoothstep(0.015, 0.05, blur);
  float coarse = smoothstep(0.04, 0.12, blur);
  // 결 비틀기: 알갱이 자리를 낮은 주파수로 밀어 줄과 간격이 곳곳에서 휘고 벌어진다.
  vec2 warp = vec2(N(vec3(w * 0.05, 0.21)).r, N(vec3(w * 0.05, 0.47)).r) - 0.5;
  vec2 v = w + warp * 1.7;
  // 알갱이: 잔 송이(0.2×0.3 km)와 굵은 송이(0.3×0.4 km)를 자리마다 섞는다. 잔 혹이 가장자리를 깎는다.
  float small = N(vec3(v.x * 0.3, v.y * 0.2, 0.33)).a;
  float large = N(vec3(v.x * 0.42, v.y * 0.3, 0.77)).b;
  float size = N(vec3(w * 0.07, 0.83)).r;
  float cell = mix(small, large, smoothstep(0.42, 0.62, size));
  float lump = N(vec3(v * 0.75, 0.13)).a;
  float grain = cell - (1.0 - mix(lump, CIRROCUMULUS_LUMP_MEAN, fine)) * 0.3;
  // 물결 줄(0.8 km 안팎): 마루에는 알갱이가 굵고 골에는 성기다. 줄은 크게 굽이치고 곳에 따라 흐려진다.
  float bend = N(vec3(w * 0.03, 0.71)).r;
  float rows = 0.5 + 0.5 * sin(v.x * 8.0 + bend * 16.0);
  float rowsHere = smoothstep(0.45, 0.65, N(vec3(w * 0.04, 0.37)).r);
  // 줄은 평면 위 곧은 결이라 멀리서는 지평선으로 모이는 빗살이 된다. 알갱이와 함께 뭉갠다.
  rows = mix(0.5, rows, rowsHere * (1.0 - coarse));
  // 무리: 알갱이가 엉겨 비늘 무더기를 짓고 무더기 사이로 하늘이 트인다. 잔 구멍이 알갱이를 군데군데 뺀다.
  float clump = N(vec3(w * 0.1, 0.59)).r;
  float gaps = mix(N(vec3(v * 0.22, 0.91)).r, 0.5, fine);
  float lo = mix(0.58, 0.36, cov) - rows * 0.12 - (clump - 0.5) * 0.6 + (gaps - 0.5) * 0.45;
  // 가장자리 무름: 어떤 무리는 또렷하고 어떤 무리는 번진다.
  float soft = mix(0.16, 0.4, N(vec3(w * 0.09, 0.15)).r);
  float scales = Remap(grain, lo, lo + soft);
  float sheet = mix(0.25, 0.55, cov) * mix(0.6, 1.4, clump);
  float d = mix(scales, sheet, coarse);
  // 조각: 하늘에 드문드문 무리를 짓고, 덮임이 클수록 넓게 퍼진다.
  vec2 m = xz - HighDrift() * 0.9;
  float where = N(vec3(m * 0.03, 0.43)).r * 0.7 + N(vec3(m * 0.1, 0.53)).r * 0.3;
  float mask = Remap(where, 0.62 - 0.4 * cov, 0.82 - 0.3 * cov);
  return d * mask * FallstreakHole(xz, CIRROCUMULUS_ALTITUDE, 3.4, iceFall);
}

// 권층운(햇무리구름): 하늘을 우윳빛으로 엷게 덮는 흰 너울. 두께가 크게 일렁이고, 바람결을 따라 가는 결이 비친다.
// 해가 비치면 해 둘레 22°에 햇무리가 서고(CirrostratusHalo), 해 높이 양옆에 무리해가 뜬다.
float CirrostratusDensity(vec2 xz) {
  float cov = _High.z;
  if (cov <= 0.001) return 0.0;
  vec2 w = Rotate(xz - HighDrift() * 0.8, 0.2);
  w.y += (N(vec3(w * 0.006, 0.71)).r - 0.5) * 20.0;
  float fib = N(vec3(w.x * 0.012, w.y * 0.09, 0.13)).r * 0.6 + N(vec3(w.x * 0.03, w.y * 0.3, 0.51)).r * 0.4;
  float sheet = N(vec3(xz * 0.008, 0.27)).r;
  return cov * clamp(0.45 + 0.5 * (fib - 0.5) * 1.6 + 0.45 * (sheet - 0.5), 0.1, 1.0);
}

// 햇무리와 무리해: 권층운의 얼음 육각기둥이 햇빛을 22° 꺾어 해 둘레에 둥근 무리가 선다. 안쪽 가장자리가 붉고 바깥으로
// 희어지며, 안쪽은 조금 어둡다. 해가 낮으면 해 높이 양옆 22° 남짓에 무지개 빛 밝은 점(무리해)이 뜬다.
// rd는 보는 방향, s는 해 방향이다. 반환은 해빛에 곱할 빛(빛깔 포함).
STATIC const float HALO_RADIUS = 22.0;
vec3 CirrostratusHalo(vec3 rd, vec3 s) {
  float ang = degrees(acos(clamp(dot(rd, s), -1.0, 1.0)));
  float ring = exp(-pow((ang - HALO_RADIUS - 0.6) / 1.1, 2.0));
  // 안쪽 가장자리 붉게 → 바깥 노랑·흰빛
  vec3 tint = mix(vec3(1.0, 0.45, 0.25), vec3(1.0, 0.95, 0.85), smoothstep(HALO_RADIUS - 0.3, HALO_RADIUS + 1.8, ang));
  vec3 halo = tint * ring * 1.8 - v3(0.25) * (1.0 - smoothstep(HALO_RADIUS - 6.0, HALO_RADIUS, ang)) * smoothstep(3.0, 8.0, ang);
  // 무리해: 해가 낮을수록 22°보다 조금 바깥, 해 높이에 선다.
  vec2 sun = Direction(s);
  vec2 dir = Direction(rd);
  float sunEl = sun.y;
  float dogAz = HALO_RADIUS / max(cos(radians(sunEl)), 0.3);
  float dAz = abs(AngleDelta(dir.x, sun.x)) - dogAz;
  float dog = exp(-pow(dAz / 1.0, 2.0) - pow((dir.y - sunEl) / 1.3, 2.0)) * (1.0 - smoothstep(25.0, 50.0, sunEl));
  // 무리해 꼬리: 해에서 먼 쪽으로 희게 늘어진다.
  float tail = exp(-pow((dir.y - sunEl) / 0.8, 2.0)) * exp(-max(dAz, 0.0) / 3.0) * step(0.0, dAz) * 0.35;
  vec3 dogTint = Spectrum(clamp(0.05 + (dAz + 1.2) * 0.2, 0.0, 1.0));
  halo += (dogTint * dog * 1.6 + v3(tail)) * (1.0 - smoothstep(25.0, 50.0, sunEl));
  return halo;
}

// 중층·하층 구름은 탑이 선 오른쪽 하늘에서 성기다. 탑 둘레는 오르는 공기를 메우려 내려앉는 공기(하강 기류)로 맑고,
// 그래야 이 앱의 주인공인 탑이 층구름에 다 가려지지 않는다. x는 층 위 자리의 가로(km).
float TowerSideThin(float x) { return 1.0 - 0.55 * smoothstep(-2.0, 9.0, x); }

// 고적운(양떼구름): 둥근 구름 덩이(약 0.4~0.8 km)가 파란 틈을
// 두고 물결 줄로 늘어선다. 덩이 크기는 자리마다 다르고 결이 곳곳에서 휜다. 덩이 가운데가 두꺼워 둥근 덩이로 읽히고,
// 해 쪽 가장자리는 밝고 반대쪽은 잿빛이다(그늘은 겹치는 쪽이 해 쪽 가까운 자리의 두께로 셈한다).
// blur는 한 화소가 덮는 길이(km): 덩이가 화소보다 잘아지는 지평선 쪽은 평균 덮임으로 옅은 너울이 된다.
// 둥근 송이 결(양떼구름과 해 둘레 채운 조각이 함께 쓴다). cov는 송이가 덮는 정도다.
float Cloudlets(vec2 xz, float blur, float cov) {
  vec2 w = Rotate(xz - MidDrift(), 0.5);
  vec2 warp = vec2(N(vec3(w * 0.04, 0.29)).r, N(vec3(w * 0.04, 0.63)).r) - 0.5;
  vec2 v = w + warp * 1.4;
  float fine = smoothstep(0.03, 0.09, blur);
  float coarse = smoothstep(0.08, 0.2, blur);
  // 덩이: 크고 작은 둥근 덩이가 섞이고, 잔 혹이 가장자리를 깎아 송이 결이 난다.
  float small = N(vec3(v.x * 0.16, v.y * 0.12, 0.19)).a;
  float large = N(vec3(v.x * 0.24, v.y * 0.18, 0.53)).b;
  float cell = mix(small, large, smoothstep(0.42, 0.62, N(vec3(w * 0.05, 0.71)).r));
  float lump = mix(N(vec3(v * 0.55, 0.13)).a, 0.5, fine);
  float grain = cell - (1.0 - lump) * 0.3;
  float row = 0.5 + 0.5 * sin(v.y * 3.0 + N(vec3(w * 0.03, 0.83)).r * 6.0);
  row = mix(row, 0.5, coarse);
  // 무리: 덩이가 엉겨 큰 무더기를 짓는 곳과 성긴 곳이 있다.
  float clump = N(vec3(w * 0.06, 0.37)).r;
  float lo = mix(0.44, 0.24, cov) - row * 0.1 - (clump - 0.5) * 0.45;
  float puffs = Remap(grain, lo, lo + 0.36);
  float sheet = mix(0.3, 0.6, cov) * mix(0.6, 1.4, clump);
  return mix(puffs, sheet, coarse);
}

float AltocumulusDensity(vec2 xz, float blur) {
  float cov = _Mid.x;
  if (cov <= 0.001) return 0.0;
  float where = N(vec3((xz - MidDrift()) * 0.02, 0.57)).r;
  float mask = Remap(where * TowerSideThin(xz.x), 0.58 - 0.45 * cov, 0.8 - 0.3 * cov);
  return mask * Cloudlets(xz, blur, cov);
}

// 고층운(차일구름): 잿빛으로 하늘을 넓게 덮는 두꺼운 너울. 해가 간유리 너머처럼 흐려진다.
// 밑면이 크게 일렁여 짙고 옅은 너울이 번갈고, 바람을 가로지르는 넓은 물결이 은은히 비친다.
float AltostratusDensity(vec2 xz) {
  float cov = _Mid.y;
  if (cov <= 0.001) return 0.0;
  vec2 w = xz - MidDrift() * 0.9;
  float n = N(vec3(w * 0.02, 0.47)).r * 0.6 + N(vec3(w * 0.07, 0.59)).r * 0.25 + N(vec3(w * 0.2, 0.31)).r * 0.15;
  vec2 r = Rotate(w, 0.5);
  float undul = 0.5 + 0.5 * sin(r.y * 0.35 + N(vec3(r * 0.02, 0.83)).r * 7.0);
  return cov * Remap(n + (undul - 0.5) * 0.12, 0.15 - 0.3 * cov, 0.62);
}

// 층적운(두루마리구름): 크고 낮은 잿빛 덩어리가 틈을 두고 이어진다. 덩어리마다 작은 혹이 부풀어 윤곽이 울퉁불퉁하다.
float StratocumulusDensity(vec2 xz) {
  float cov = _Low.x;
  if (cov <= 0.001) return 0.0;
  vec2 w = Rotate(xz - LowDrift() * 0.85, -0.4);
  float puff = Dome(N(vec3(w * 0.1, 0.61)).g);
  float lump = Dome(N(vec3(w * 0.22, 0.41)).b);
  float fine = Dome(N(vec3(w * 0.5, 0.21)).a);
  float roll = 0.5 + 0.5 * sin(w.y * 0.9 + N(vec3(w * 0.03, 0.39)).r * 4.0);
  float f = (puff * mix(0.75, 1.0, roll) + (lump - 0.6) * 0.35 + (fine - 0.6) * 0.15) * mix(0.8, 1.0, TowerSideThin(xz.x));
  return Remap(f, mix(1.0, 0.5, cov), mix(1.1, 0.85, cov));
}

// 층운(안개구름): 아침 바다 안개가 들려 올라간 낮은 층. 바람을 가로지르는 잔물결(파상)이 지고, 군데군데 크게 해져
// 파란 하늘이 비치며, 해진 가장자리는 바람결로 늘어난 실처럼 풀린다. 수평선에는 바다 안개 둑(FogBank)이 깔린다.
// blur는 이 자리 한 화소가 덮는 길이(km). 잔물결·실이 그보다 잘면 뭉개 지평선 쪽 빗살을 막는다.
float StratusDensity(vec2 xz, float blur) {
  float cov = _Low.y;
  if (cov <= 0.001) return 0.0;
  // 층이 낮아(0.75 km) 머리 위 몇 km만 보인다. 무늬는 그 크기로 짠다: 틈 3~6 km, 덩이 약 1 km, 잔 결 수백 m.
  vec2 w = xz - LowDrift() * 0.6;
  vec2 r = Rotate(w, 0.3);
  float fineFade = 1.0 - smoothstep(0.04, 0.2, blur);
  float midFade = 1.0 - smoothstep(0.15, 0.6, blur);
  // 크게 해진 자리
  float big = N(vec3(w * 0.22, 0.73)).r * 0.7 + N(vec3(w * 0.5, 0.41)).r * 0.3;
  // 덩이: 바람결로 조금 늘어난 둥근 덩이가 파상처럼 줄을 짓는다(줄이 고르지 않게 결을 휜다).
  vec2 bent = r + vec2(0.0, (N(vec3(r * 0.15, 0.19)).r - 0.5) * 1.5);
  float lumps = mix(0.5, N(vec3(bent.x * 0.6, bent.y * 1.3, 0.29)).r, midFade);
  // 잔 결: 해진 가장자리를 실처럼 풀어 준다.
  float fib = mix(0.5, N(vec3(r.x * 1.2, r.y * 3.0, 0.87)).r, fineFade);
  float lo = 0.62 - 0.36 * cov;
  float d = Remap(big + (lumps - 0.5) * 0.25 + (fib - 0.5) * 0.12, lo, lo + 0.18);
  // 덩이 사이 골은 얇아 밝다.
  return d * mix(0.7, 1.0, smoothstep(0.3, 0.7, lumps));
}

// 바다 안개 둑: 수평선 위에 낮게 깔린 짙은 안개. 윗자락이 천천히 굽이치고 실처럼 풀린 자락이 위로 번진다.
// 반환은 짙기, top은 윗자락에 가까운 정도(0 아래 ~ 1 윗자락, 해를 받아 밝다).
float FogBank(vec2 dir, out float top) {
  top = 0.0;
  float k = _Low.y;
  if (k <= 0.001 || dir.y > 6.0) return 0.0;
  float x = dir.x - degrees(LowDrift().x / 30.0);
  float swell = N(vec3(x * 0.02, 0.3, 0.57)).r;
  // 윗자락은 둥근 덩이가 줄지어 부푼다(큰 덩이와 잔 덩이 두 겹).
  float roll = N(vec3(x * 0.22, 0.6, 0.21)).r * 0.65 + N(vec3(x * 0.7, 0.2, 0.47)).r * 0.35;
  float height = mix(0.8, 3.5, k) * mix(0.6, 1.3, swell) + (roll - 0.5) * 1.6;
  float y = dir.y / max(height, 0.1);
  top = smoothstep(0.4, 1.0, y);
  float body = 1.0 - smoothstep(0.55, 1.05, y);
  // 윗자락 위로 풀린 실
  float wisps = N(vec3(x * 0.05, dir.y * 0.9, 0.33)).r;
  float above = smoothstep(1.6, 0.9, y) * smoothstep(0.5, 0.8, wisps) * 0.45;
  return clamp(max(body, above), 0.0, 1.0) * smoothstep(0.0, 0.25, k);
}

// 유방운 송이 자리: 결을 휘어 워리 칸의 곧은 모서리(다각형)가 드러나지 않게 한다.
vec2 MammatusCoord(vec2 xz) {
  vec2 w = xz - LowDrift() * 1.2;
  vec2 warp = vec2(N(vec3(w * 0.05, 0.12)).r, N(vec3(w * 0.05, 0.62)).r) - 0.5;
  return w * 0.1 + warp * 0.35;
}

// 유방운 주머니(0 주머니 사이 골 ~ 1 주머니 밑자락). 크고 작은 주머니가 섞여 늘어진다.
// 유방운이 매달린 자리(0~1): 적란운 모루 밑에 넓은 조각으로 매달리고, 지평선 쪽으로는 엷어진다(하늘 전체를 덮지 않는다).
float MammatusPatch(vec2 xz) {
  vec2 w = xz - LowDrift() * 1.2;
  float patch0 = smoothstep(0.38, 0.62, N(vec3(w * 0.012, 0.52)).r * 0.75 + N(vec3(w * 0.04, 0.18)).r * 0.25);
  return patch0 * (1.0 - smoothstep(18.0, 45.0, length(xz)));
}

float MammatusPouch(vec2 xz) {
  float area = MammatusPatch(xz);
  if (area <= 0.0) return 0.0;
  vec2 c = MammatusCoord(xz);
  // 셀 가운데에서 잰 거리(셀 단위)로 반구를 짓는다. 반지름이 셀 간격의 절반쯤이라 주머니 사이에 깊은 골이 진다.
  float bigD = (1.0 - N(vec3(c, 0.9)).b) / MAMMATUS_RADIUS;
  float smallD = (1.0 - N(vec3(c * 1.7 + 0.37, 0.27)).b) / MAMMATUS_RADIUS;
  float big = sqrt(max(1.0 - bigD * bigD, 0.0));
  float small = sqrt(max(1.0 - smallD * smallD, 0.0)) * 0.7;
  // 겉은 매끈한 풍선이 아니라 김이 엉긴 면이라 잔 혹이 조금 진다.
  float lumps = N(vec3(xz * 0.8 - LowDrift() * 0.96, 0.43)).r - 0.5;
  return clamp(max(big, small) + lumps * 0.12, 0.0, 1.0) * area;
}

// 광선이 밑면에서 늘어진 유방운 주머니 면과 처음 만나는 거리:
// 평평한 판에 음영만 그리면 비늘처럼 보이므로, 주머니가 실제로 MAMMATUS_DEPTH만큼 늘어진 높이 면을 걸어서 찾는다.
// 그래서 지평선 쪽에서는 주머니 밑자락이 겹치며 윤곽을 짓는다. 못 만나면 밑면(층)까지. pouchAt은 만난 자리의 주머니 값이다.
float MammatusHit(vec3 ro, vec3 rd, out float pouchAt) {
  float tTop = PlaneHit(ro, rd, NIMBOSTRATUS_ALTITUDE);
  float tLow = PlaneHit(ro, rd, NIMBOSTRATUS_ALTITUDE - MAMMATUS_DEPTH);
  pouchAt = 0.0;
  if (tTop < 0.0 || tLow < 0.0) return tTop;
  const int STEPS = 24;
  float prevT = tLow;
  for (int i = 0; i <= STEPS; i++) {
    float t = mix(tLow, tTop, float(i) / float(STEPS));
    vec3 p = ro + rd * t;
    float gap = p.y - (NIMBOSTRATUS_ALTITUDE - MAMMATUS_DEPTH * MammatusPouch(p.xz));
    if (gap >= 0.0) {
      // 앞 걸음과 이 걸음 사이를 반씩 나눠 면을 정확히 찾는다(걸음 간격이 골에 계단 줄무늬로 남지 않게).
      float a = prevT;
      float b = t;
      for (int k = 0; k < 5; k++) {
        float m = 0.5 * (a + b);
        vec3 q = ro + rd * m;
        if (q.y - (NIMBOSTRATUS_ALTITUDE - MAMMATUS_DEPTH * MammatusPouch(q.xz)) >= 0.0) b = m; else a = m;
      }
      pouchAt = MammatusPouch((ro + rd * b).xz);
      return b;
    }
    prevT = t;
  }
  return tTop;
}

// 난층운(비구름) 밑면의 두께와, 유방운 송이 높이.
float NimbostratusDensity(vec2 xz, out float thick, out float pouch) {
  vec2 w = xz - LowDrift() * 1.2;
  float n = N(vec3(w * 0.07, 0.31)).r * 0.55 + Dome(N(vec3(w * 0.16, 0.53)).g) * 0.3 + N(vec3(w * 0.5, 0.77)).r * 0.15;
  float cov = _Low.w;
  thick = smoothstep(0.42, 0.82, n);
  // 유방운: 밑면에서 주머니처럼 둥글게 늘어진 송이. 폭풍이 지나간 뒤 낮은 해를 받아 도드라진다.
  pouch = Dome(N(vec3(MammatusCoord(xz), 0.9)).b) * _Special.y;
  // 유방운이 매달린 조각은 그리는 쪽(MammatusPatch)에서 덮는다.
  return smoothstep(0.56 - cov * 0.4, 0.72 - cov * 0.34, n);
}

// ---- 방향으로 그리는 구름(멀리 옆으로 누운 것들) ----


// ---- 채운 너울(실제보다 아름다움을 앞세운다) ----
// 해 가까운 하늘에 뜬 얇은 비단 구름 조각에 무지개 빛깔이 번진다(환수평호·불무지개처럼). 사진 속 채운처럼 빛깔 장은
// 넓고 매끈하고, 구름의 실 같은 결과 해진 틈이 그 빛깔을 드러내거나 가린다.
// 너울은 탑과 시계를 피해 왼쪽 빈 하늘(VEIL_HOME)에 서고, 나타날 때마다 _VeilPlace만큼 옮겨 자리·크기·무늬가 달라진다.
// 가장 높이 올라도 시계 밑 지금 시각 글자(고도 약 22.5°)에 짙게 걸치지 않는다.
STATIC const vec2 VEIL_HOME = vec2(-9.0, 15.5);
// 너울 조각의 반폭(도, 배율 1)
STATIC const vec2 VEIL_SIZE = vec2(24.0, 8.0);
// 윤곽이 옅어지기 시작하는 자리(반폭 대비), 굵은 결·틈이 서는 문턱, 결이 큰 물결을 따라 휘는 정도
STATIC const float VEIL_CORE = 0.12;
STATIC const float VEIL_FIBER = 0.27;
STATIC const float VEIL_HOLES = 0.25;
STATIC const float VEIL_BEND = 0.2;
// 결의 불투명도 배율, 결 없이 옅게 깔린 너울의 불투명도
STATIC const float VEIL_OPACITY = 2.2;
STATIC const float VEIL_HAZE = 0.2;
// 너울이 떠 있는 높이(km). 뭉게구름보다 높아 가까운 뭉게구름이 앞을 가린다.
STATIC const float VEIL_ALTITUDE = 5.2;
// 공기 원근을 셈할 때 채운(너울·갓구름)까지 거리에 곱할 값. 실제 거리(약 20 km)면 공기가 빛깔을 하늘색으로 씻어 낸다.
STATIC const float IRI_AIR = 0.25;
// 하늘 시간 1마다 빛깔 차례가 흐르는 바퀴(갓구름·구름 가장자리)
STATIC const float IRI_FLOW = 0.03;
// 갓구름 가운데에 남는 빛깔
STATIC const float IRI_CAP_CENTER = 0.3;
// 채운 빛깔이 해에서 1°마다 바뀌는 바퀴, 해에서 멀 때 남는 세기
STATIC const float IRI_OPD_PER_DEGREE = 0.006;
STATIC const float IRI_FAR_SUN = 0.65;
// 해에서 이 각(도) 안의 구름은 얇은 가장자리가 채운으로 물든다.
STATIC const float IRI_SUN_REACH = 40.0;
// 구름 종류별 채운 세기: 두꺼운 뭉게구름은 은은하게, 얇은 양떼구름은 짙게
STATIC const float IRI_CUMULUS = 0.45;
STATIC const float IRI_ALTOCUMULUS = 0.7;

// 구름의 채운: 해에서 IRI_SUN_REACH° 안에 든 얇은 구름이 은은한 무지개 빛깔로 물든다. 해에 가까울수록 진하다.
// 빛깔은 해까지의 각과 큰 무늬를 따라 여러 송이에 걸친 조각으로 번진다(송이마다 가장자리에 테를 두르면
// 형광 윤곽선·색수차처럼 보인다). 얇은 송이는 통째로, 두꺼운 송이는 바깥만 물든다.
// resolve는 구름 결이 화면에서 또렷한 정도(1 가까운 송이 ~ 0 지평선 쪽 너울), strength는 구름 종류별 세기다.
vec3 EdgeIridesce(vec3 col, float cover, float cosT, vec3 rd, float resolve, float strength) {
  float ang = degrees(acos(clamp(cosT, -1.0, 1.0)));
  float near = 1.0 - smoothstep(IRI_SUN_REACH * 0.4, IRI_SUN_REACH, ang);
  if (near <= 0.0) return col;
  float thin = smoothstep(0.0, 0.12, cover) * (1.0 - smoothstep(0.45, 0.95, cover));
  // 빛깔이 피는 자리와 덜 피는 자리
  float bloom = smoothstep(0.3, 0.7, N(vec3(rd.xz * 1.2, rd.y * 2.0) + vec3(_SkyTime * 0.002, 0.0, 0.37)).r);
  float sat = thin * near * resolve * smoothstep(-1.0, 4.0, SunElevationDeg()) * (1.0 - _Storm) * strength * mix(0.4, 1.0, bloom);
  float drift = N(vec3(rd.xz * 1.5, rd.y * 2.5) + vec3(0.0, 0.0, 0.17)).r - 0.5;
  return Iridesce(col, SpectrumCycle(ang * 0.03 + drift * 0.8 + _SkyTime * IRI_FLOW), sat);
}

// across는 빛깔 차례를 정하는 띠 좌표(−1 아래 ~ 1 위)이고, haze는 결 없이 옅게 번진 너울의 세기다.
float VeilDensity(vec2 dir, out float across, out float haze) {
  across = 0.0;
  haze = 0.0;
  if (_Mid.z <= 0.001) return 0.0;
  float scale = max(_VeilPlace.z, 0.1);
  vec2 rel = (dir - VEIL_HOME - _VeilPlace.xy) / scale;
  if (abs(rel.x) > VEIL_SIZE.x * 1.5 || abs(rel.y) > VEIL_SIZE.y * 2.4) return 0.0;
  float salt = _VeilPlace.w;
  float t = _SkyTime;
  // 윤곽: 길쭉한 조각, 가장자리는 해진 비단처럼 들쭉날쭉하다.
  float rim = N(vec3(rel * vec2(0.06, 0.16), salt) + vec3(t * 0.002, 0.0, 0.0)).r - 0.5;
  float r = length(rel / VEIL_SIZE);
  float env = 1.0 - smoothstep(VEIL_CORE, 1.0, r + rim * 0.8);
  // 빛깔 차례를 정하는 띠 좌표. 띠가 조각을 따라 크게 굽이친다.
  across = rel.y / VEIL_SIZE.y + (N(vec3(rel.x * 0.025, 0.4, salt + 0.1)).r - 0.5) * 0.9;
  if (env <= 0.0) return 0.0;
  // 결: 가로로 늘어난 실 같은 줄 세 겹(굵은 결, 가는 결, 머리카락 결). 조금 비스듬하고, 큰 물결을 따라 휘고 갈라진다.
  vec2 bend = vec2(N(vec3(rel * vec2(0.035, 0.09), salt + 0.15)).r, N(vec3(rel * vec2(0.035, 0.09), salt + 0.45)).r) - 0.5;
  vec2 f = vec2(rel.x + rel.y * 1.2 + bend.x * 6.0, rel.y + bend.y * VEIL_BEND * 10.0);
  float coarse = N(vec3(f * vec2(0.025, 0.5), salt + 0.3) + vec3(t * 0.003, 0.0, 0.0)).r;
  float fine = N(vec3(f * vec2(0.07, 2.2), salt + 0.6) + vec3(t * 0.004, 0.0, 0.0)).r;
  float hair = N(vec3(f * vec2(0.12, 4.5), salt + 0.9) + vec3(t * 0.005, 0.0, 0.0)).r;
  float fiber = smoothstep(VEIL_FIBER, VEIL_FIBER + 0.4, coarse) * mix(0.3, 1.0, smoothstep(0.25, 0.75, fine)) * mix(0.7, 1.0, hair);
  // 틈: 군데군데 해져 파란 하늘이 비친다.
  float holes = smoothstep(VEIL_HOLES, VEIL_HOLES + 0.32, N(vec3(rel * vec2(0.09, 0.25), salt + 0.8)).r);
  float grow = _Mid.z * (1.0 - _Storm);
  haze = env * grow;
  return env * mix(0.15, 1.0, fiber) * mix(0.3, 1.0, holes) * grow;
}

// 채운 실: 바람에 날리는 비단 띠처럼 S자로 휘어 흐르는 가는 실구름 한 다발. 여러 가닥이 나란히 흐르다 모이고
// 흩어지며, 띠가 비틀리는 곳은 좁아진다. 두꺼운 가운데는 희게 빛나고 얇은 가장자리일수록 빛깔이 짙으며,
// 길이를 따라 무지개 차례가 몇 번 되풀이된다. 둘레에 같은 빛이 은은히 번진다.
STATIC const vec2 WISP_HOME = vec2(-9.0, 12.5);
// 띠의 반길이, 가장 넓은 곳의 반폭(도, 배율 1), 짙기(얇게 비치도록 결의 불투명도를 낮춘다)
STATIC const float WISP_LENGTH = 13.0;
STATIC const float WISP_WIDTH = 2.3;
STATIC const float WISP_DENSITY = 0.5;
float WispDensity(vec2 dir, out float x, out float haze) {
  x = 0.5;
  haze = 0.0;
  float scale = max(_VeilPlace.z, 0.1);
  float salt = _VeilPlace.w;
  vec2 rel = (dir - WISP_HOME - _VeilPlace.xy) / scale;
  // 비스듬히(25~50°) 흐른다. q.x는 띠를 따라, q.y는 가로질러 잰다.
  float side = H1(salt * 13.1) < 0.5 ? -1.0 : 1.0;
  vec2 q = Rotate(rel, -side * radians(mix(25.0, 50.0, H1(salt * 31.7))));
  float s = q.x / WISP_LENGTH;
  if (abs(s) > 1.3 || abs(q.y) > 9.0) return 0.0;
  // 등뼈: 한 번 반쯤 S자로 휘고 큰 무늬를 따라 조금 일렁인다.
  float phase = H1(salt * 7.7) * 6.2832;
  float spine = sin(s * 2.6 + phase) * mix(1.4, 2.6, H1(salt * 3.3)) + (N(vec3(q.x * 0.05, salt, 0.27)).r - 0.5) * 1.0;
  float dv = q.y - spine;
  // 폭: 가운데가 넓고 두 끝으로 가늘어진다. 띠가 비틀리는 곳은 좁아진다.
  float u = clamp(s * 0.5 + 0.5, 0.0, 1.0);
  float twist = abs(cos(s * 3.4 + phase * 0.7));
  float width = WISP_WIDTH * sin(3.1416 * u) * mix(0.35, 1.0, twist) + 0.15;
  float v = dv / width;
  // 실: 띠를 따라 길게 이어지는 가는 결(가로지른 자리 v로 재어 띠가 좁아지면 결도 함께 모인다).
  float f1 = N(vec3(q.x * 0.035, v * 1.1 + salt * 3.0, 0.53)).r;
  float f2 = N(vec3(q.x * 0.08, v * 2.3 + salt * 5.0, 0.77)).r;
  float fibers = f1 * 0.6 + f2 * 0.4;
  float strand = smoothstep(0.42, 0.68, fibers);
  // 몸은 가운데가 짙고 가장자리로 풀리며, 가장자리 밖으로도 몇 가닥이 삐져나온다.
  float edge = abs(v);
  float body = (1.0 - smoothstep(0.55, 1.05, edge)) * mix(0.35, 1.0, strand);
  float stray = (1.0 - smoothstep(1.0, 2.2, edge)) * 0.5 * smoothstep(0.62, 0.8, fibers);
  float ends = smoothstep(1.1, 0.7, abs(s));
  float dens = max(body, stray) * ends;
  // 빛깔: 가장자리 쪽으로, 또 길이를 따라 무지개 차례가 되풀이된다.
  x = SpectrumCycle(edge * 0.3 + s * 0.7 + (fibers - 0.5) * 0.2 + salt);
  float grow = _Mid.z * (1.0 - _Storm);
  haze = exp(-edge * edge * 0.35) * smoothstep(9.0, 6.0, abs(dv)) * ends * 0.8 * grow;
  return dens * WISP_DENSITY * grow;
}

// 해 둘레 채운 조각: 해 쪽 하늘에 양떼구름 송이가 한 무리 피고, 송이들이 해를 둘러싼 고리 차례로 물든다
// (해 가까운 채운 사진처럼). 송이는 양떼구름 층에 함께 그려 같은 빛·그늘을 받는다. 해가 화면 밖이면 해가 있는 쪽
// 가장자리에 서서, 화면 밖 해를 둘러싼 고리의 한 자락이 보인다.
// 무리의 반폭(도, 배율 1), 고리 빛깔이 한 바퀴 도는 해까지의 각(도), 송이를 양떼구름보다 굵게 할 배율
STATIC const vec2 PATCH_SIZE = vec2(17.0, 8.0);
STATIC const float PATCH_RING = 20.0;
STATIC const float PATCH_GRAIN = 0.35;
// 해 둘레 조각이 이 방향에 피는 정도(0~1). hue는 그 자리의 무지개 빛깔 자리다.
float SunPatch(vec2 dir, vec2 sun, out float hue) {
  hue = 0.5;
  if (_VeilForm < 1.5 || _Mid.z <= 0.001) return 0.0;
  float scale = max(_VeilPlace.z, 0.1);
  float salt = _VeilPlace.w;
  vec2 center = vec2(clamp(sun.x, -13.0, 13.0), clamp(sun.y + 9.0, 11.0, 16.0)) + _VeilPlace.xy * vec2(0.5, 0.4);
  vec2 rel = (dir - center) / scale;
  if (abs(rel.x) > PATCH_SIZE.x * 1.6 || abs(rel.y) > PATCH_SIZE.y * 2.2) return 0.0;
  float rim = N(vec3(rel * vec2(0.07, 0.12), salt + 0.2)).r - 0.5;
  float env = 1.0 - smoothstep(0.25, 1.0, length(rel / PATCH_SIZE) + rim * 0.8);
  // 빛깔: 해에서 잰 각을 따라 넓은 고리처럼 차례가 돈다. 큰 무늬만 고리를 흔들어 빛깔 조각이 넓고 매끈하다.
  vec2 off = vec2(AngleDelta(dir.x, sun.x), dir.y - sun.y);
  hue = SpectrumCycle(length(off) / PATCH_RING + (N(vec3(rel * 0.05, salt + 0.9)).r - 0.5) * 0.3 + salt);
  return env * _Mid.z * (1.0 - _Storm);
}

// 양떼구름 층의 짙기: 장면의 양떼구름에 해 둘레 조각(sunPatch)의 송이를 더한다.
float AltocumulusLayer(vec2 xz, float blur, float sunPatch) {
  float d = AltocumulusDensity(xz, blur);
  if (sunPatch <= 0.001) return d;
  float salt = _VeilPlace.w;
  return max(d, sunPatch * Cloudlets((xz + vec2(37.0, 53.0) + salt * 40.0) * PATCH_GRAIN, blur * PATCH_GRAIN, 0.95));
}

// 채운 너울의 짙기(모양은 _VeilForm). x는 무지개 빛깔 자리(0 빨강 ~ 1 보라), tint는 빛깔이 서는 정도,
// haze는 결 없이 옅게 깔린 너울이다. sun은 해의 방향(방위·고도, 도).
// 해 둘레 조각은 양떼구름 층에서 그리므로 여기서는 0이다.
float IridescentCloud(vec2 dir, out float x, out float tint, out float haze) {
  x = 0.5;
  tint = 1.0;
  haze = 0.0;
  if (_Mid.z <= 0.001) return 0.0;
  if (_VeilForm < 0.5) {
    float across;
    float d = VeilDensity(dir, across, haze);
    x = 0.5 - across * 0.5;
    tint = 1.0 - smoothstep(0.85, 1.4, abs(across));
    return d;
  }
  if (_VeilForm < 1.5) return WispDensity(dir, x, haze);
  return 0.0;
}

// 아치구름(선반구름): 뇌우 앞에서 차가운 돌풍이 따뜻한 공기를 밀어 올려 생기는, 하늘을 가로지르는 거대한 활.
// 가운데가 다가와 높고 양 끝은 지평선으로 내려간다. 윗면은 층층이 띠가 지고, 앞으로 튀어나온 밑면은 짙게 말려 매달린다.
// 그 밑은 조금 밝은 틈, 더 밑은 군데군데 기둥진 비 커튼이다.
// face는 쐐기 안의 자리(0 밑면 ~ 1 윗면), under는 쐐기 밑 비 커튼의 짙기.
float ArcusDensity(vec2 dir, out float face, out float under) {
  face = 0.0;
  under = 0.0;
  float a = _Special.z;
  if (a <= 0.001 || dir.y > 16.0) return 0.0;
  // 쐐기는 화면을 가로질러 크게 휘어진 활(아치)이다. 가운데가 다가와 높고, 양 끝은 멀어져 지평선으로 낮아진다.
  float x = dir.x / 24.0;
  float arch = max(1.0 - x * x, 0.0);
  float n = N(vec3(dir.x * 0.015, 0.15, 0.61) + vec3(_SkyTime * 0.001, 0.0, 0.0)).r;
  float n2 = N(vec3(dir.x * 0.06, dir.y * 0.4, 0.31)).r;
  float bottom = mix(0.5, mix(2.0, 5.0, a), arch) + (n - 0.5) * 0.8;
  float top = bottom + mix(1.5, 7.5, a) * mix(0.35, 1.0, arch) * (0.8 + 0.4 * n) + (n2 - 0.5) * 1.2;
  float y = (dir.y - bottom) / max(top - bottom, 0.1);
  // 쐐기 밑: 밝은 틈을 지나 지평선까지 비 커튼이 내린다. 커튼은 군데군데 짙게 기둥져 내린다.
  float shafts = N(vec3(dir.x * 0.12, 0.3, 0.77)).r;
  under = (1.0 - smoothstep(-0.15, 0.0, y)) * smoothstep(-1.2, -0.3, y) * a * mix(0.5, 1.2, shafts);
  if (y < -0.25 || y > 1.3) return 0.0;
  face = clamp(y, 0.0, 1.0);
  // 윗면: 층층이 포갠 선반 끝이 물결처럼 비죽비죽 나오고, 위로는 부푼 덩이가 얹혀 울퉁불퉁하다.
  float lumps = N(vec3(dir.x * 0.18, dir.y * 0.35, 0.47)).r;
  // 밑면: 앞으로 튀어나온 선반 밑이 둥글게 말려 매달리고(물결진 밑선), 밑으로 풀린 자락이 비 커튼으로 이어진다.
  float roll = (N(vec3(dir.x * 0.09, 0.6, 0.83)).r - 0.5) * 0.28;
  float d = (1.0 - smoothstep(0.75, 1.15, y + (n2 - 0.5) * 0.25 - (lumps - 0.5) * 0.35))
            * smoothstep(-0.2, 0.06, y - roll);
  return d * a;
}

// 켈빈-헬름홀츠 물결구름: 위아래 바람이 어긋나는 얇은 층의 윗면이 바다의 부서지는 파도처럼 줄지어 솟아
// 앞(오른쪽, 위 바람이 부는 쪽)으로 말려 넘어간다. 몇 분이면 사라진다. 시계 아래 왼쪽 하늘(방위 −29~3°, 고도 15~29°).
// 가는 나선은 손글씨·아이콘처럼 보이므로 부서지는 파도의 모양으로 빚는다.
//  - 등: 층에서 완만하게 솟아 마루로 오르는 꽉 찬 덩어리
//  - 입술: 마루에서 앞으로 던져져 아래로 말려 내려오는 두툼한 관(끝으로 갈수록 가늘고 찢긴다)
//  - 통: 입술 밑의 빈 속. 하늘이 비친다. 그 안쪽 벽(curl)은 해를 등져 그늘진다.
// 물결마다 자란 정도가 달라(막 솟는 것, 다 말린 것) 도장처럼 되풀이되지 않는다. 물결은 넓게 깔린 구름 둑에서 솟는다
// (물결만 하늘에 떠 있으면 스티커처럼 보인다). 둑은 물결 줄보다 양옆으로 길게 뻗어 결을 지으며 옅어진다.
float KelvinHelmholtzDensity(vec2 dir, out float curl) {
  curl = 0.0;
  float k = _Extra.x;
  if (k <= 0.001 || dir.x > 14.0 || dir.x < -40.0 || dir.y < 13.0 || dir.y > 28.0) return 0.0;
  const float period = 7.0;
  float along = dir.x + 29.0 - degrees(MidDrift().x / 24.0) + (N(vec3(dir.x * 0.03, 0.2, 0.44)).r - 0.5) * 2.0;
  float cellIndex = floor(along / period);
  float grown = mix(0.55, 1.2, H1(cellIndex * 7.1)) * mix(0.6, 1.0, k);
  float swell = (N(vec3(dir.x * 0.04, 0.3, 0.2)).r - 0.5) * 1.2;
  vec2 p = vec2(fract(along / period) * period, dir.y - 18.2 - swell);
  float ragged = N(vec3(dir * vec2(0.9, 1.3), 0.91)).r - 0.5;
  float fine = N(vec3(dir * vec2(2.4, 3.0), 0.37)).r - 0.5;

  float h = 3.6 * grown;
  float R = 0.5 * h;
  float Rin = 0.3 * R;
  float xc = period * 0.62 + (H1(cellIndex * 3.7 + 1.3) - 0.5) * 0.8;
  vec2 C = vec2(xc, h - R);
  vec2 d = p - C;
  float r = length(d);
  float ang = degrees(atan(d.y, d.x));
  // 입술: 마루 뒤(180°)에서 위(90°)를 지나 앞으로 넘어가 아래(끝 각)까지. 끝으로 갈수록 바깥 반지름이 줄어 가늘다.
  float endAng = mix(-20.0, -70.0, smoothstep(0.6, 1.1, grown));
  float sPath = (180.0 - ang) / (180.0 - endAng);
  float outer = mix(R, Rin + 0.3 * R, smoothstep(0.55, 1.0, sPath)) * (1.0 + ragged * 0.22);
  float lip = 0.0;
  if (ang >= endAng - 15.0) {
    lip = smoothstep(outer, outer - 0.25 * R, r) * smoothstep(Rin - 0.1 * R, Rin + 0.12 * R, r + fine * 0.2 * R);
    lip *= 1.0 - smoothstep(0.85, 1.05, sPath + ragged * 0.2);
  }
  // 등: 마루 뒤로 층까지 완만하게 내려가는 꽉 찬 덩어리. 앞면은 통의 왼쪽 벽을 따라 둥글게 파인다.
  // 등은 칸 안에서 시작한다(칸 경계를 넘으면 잘려 세로 벽이 선다).
  float x0 = max(xc - mix(3.4, 4.2, H1(cellIndex * 1.9)), 0.3);
  float u = clamp((p.x - x0) / max(xc - 0.3 * R - x0, 0.1), 0.0, 1.0);
  // 등은 완만하게 솟아 마루로 이어진다(오목하게 솟으면 마루 직전이 수직 벽이 되어 기둥처럼 보인다).
  float backTop = (C.y + 0.6 * R) * smoothstep(0.0, 1.0, u);
  float face = p.y > C.y - Rin ? C.x - sqrt(max(Rin * Rin - (p.y - C.y) * (p.y - C.y), 0.0)) : C.x - Rin * 0.3;
  float back = smoothstep(0.25, -0.2, p.y - backTop + ragged * 0.5) * smoothstep(0.15, -0.15, p.x - face + ragged * 0.3)
               * smoothstep(-0.9, -0.2, p.y + ragged * 0.4);
  // 통 안쪽 벽과 입술 밑면은 그늘
  curl = max(lip * smoothstep(Rin + 0.45 * R, Rin, r) * step(ang, 90.0), back * smoothstep(face - 0.8, face, p.x) * smoothstep(C.y + 0.2 * R, C.y - Rin, p.y) * 0.7);
  // 물결은 줄의 가운데(방위 −29~3°)에서만 솟고 양 끝으로 갈수록 낮아진다.
  float waves = smoothstep(0.0, 5.0, dir.x + 29.0) * smoothstep(0.0, 5.0, 3.0 - dir.x);
  lip *= waves;
  back *= waves;
  curl *= waves;
  // 물결을 낳은 둑: 두툼하고 울퉁불퉁하게 깔리고 밑면은 찢겨 풀린다. 양옆으로 길게 뻗으며 가늘어진다.
  // 노이즈 격자가 한 줄로 드러나 밑면에 같은 간격의 방울이 매달리지 않게 좌표를 비스듬히 비튼다.
  vec2 skew = vec2(dir.x + dir.y * 0.6, dir.y - dir.x * 0.15);
  float lumpy = N(vec3(skew * vec2(0.37, 0.8), 0.23)).r - 0.5;
  float streaks = N(vec3(skew.x * 0.08, skew.y * 1.1, 0.67)).r;
  float reach = smoothstep(0.0, 9.0, dir.x + 40.0) * smoothstep(0.0, 9.0, 14.0 - dir.x);
  float thick = mix(0.35, 1.0, waves) * reach;
  float band = smoothstep(-1.8 * thick, -0.6 * thick, p.y + lumpy * 1.2) * (1.0 - smoothstep(0.4 * thick, 1.0 * thick, p.y + lumpy * 0.5));
  band *= mix(0.55, 1.0, smoothstep(0.3, 0.7, streaks)) * reach;
  float shape = max(max(lip, back), band * 0.85);
  float fib = N(vec3(dir * vec2(0.3, 0.8), 0.57)).r;
  return clamp(Remap(shape * mix(0.7, 1.0, fib) + fine * 0.18, 0.15, 0.7), 0.0, 1.0) * k;
}

// 야광운: 해가 진 뒤 80 km 높이의 얼음 구름이 아직 햇빛을 받아 푸른 은빛으로 빛난다. 물결 무늬가 잘게 진다.
float NoctilucentDensity(vec2 dir) {
  float k = _High.w * smoothstep(0.35, 0.8, Twilight());
  if (k <= 0.001 || dir.y < 1.0 || dir.y > 16.0) return 0.0;
  vec2 w = dir - vec2(degrees(HighDrift().x / 300.0), 0.0);
  float warp = N(vec3(w * vec2(0.04, 0.12), 0.19)).r;
  float warp2 = N(vec3(w * vec2(0.1, 0.3), 0.39)).r;
  // 긴 물결(띠)과 잔물결이 비스듬히 엇갈린다. 결이 크게 휘어 반듯한 빗금이 되지 않는다.
  float bands = 0.5 + 0.5 * sin(w.x * 0.9 + w.y * 1.7 + warp * 10.0);
  float ripples = 0.5 + 0.5 * sin(w.x * 3.4 - w.y * 2.1 + warp2 * 12.0);
  float fib = N(vec3(w * vec2(0.25, 0.9), 0.61)).r;
  float where = N(vec3(w * vec2(0.02, 0.07), 0.67)).r;
  float height = smoothstep(1.0, 3.0, dir.y) * (1.0 - smoothstep(6.0, 15.0, dir.y));
  float d = Remap(where, 0.35, 0.65) * mix(0.3, 1.0, bands) * mix(0.6, 1.0, ripples) * mix(0.5, 1.0, fib);
  return d * height * k;
}

// ---- 겹쳐 그리기 ----

// haze는 공기 원근을 셈할 때 거리에 곱할 값이다. 1보다 작으면 실제보다 가까이 있는 것처럼 또렷하다(채운).
void AddLayer(float dist, vec3 premult, float transmit, float haze) {
  if (gCount >= MAX_LAYERS || transmit > 0.999) return;
  gDist[gCount] = dist;
  gPre[gCount] = premult;
  gTr[gCount] = transmit;
  gHaze[gCount] = haze;
  gCount++;
}

void AddLayer(float dist, vec3 premult, float transmit) {
  AddLayer(dist, premult, transmit, 1.0);
}

// 빛깔 col, 불투명도 a인 구름을 거리 dist에 둔다. 공기 원근(멀수록 하늘에 잠김)은 겹칠 때 셈한다.
void AddCloud(float dist, vec3 col, float a) {
  if (a <= 0.001) return;
  AddLayer(dist, col * a, 1.0 - a);
}

// 평면 구름층의 빛: 얇은 곳은 해를 받아 밝고, 두꺼운 가운데는 잿빛. toward는 해 쪽으로 조금 옮긴 곳의 두께(그늘).
vec3 LayerLight(float thick, float toward, float forward, vec3 sc, vec3 amb, float bright, float grey) {
  vec3 lit = sc * (0.85 + 0.3 * forward) + amb * 1.1;
  vec3 dim = amb * 1.0 + sc * 0.3;
  vec3 col = mix(lit, dim, smoothstep(0.15, 0.95, thick) * grey);
  return col * mix(1.0, 0.72, clamp(toward, 0.0, 1.0)) * bright;
}

// 톤매핑: 빛깔마다 따로 누르면(1 − e^−x) 가장 밝은 파랑이 가장 많이 눌려 맑은 하늘이 옅게 바랜다.
// 밝기로 누르고 빛깔 비율을 지킨 값을 TONE_HUE만큼 섞어 한낮 하늘이 짙푸르게 남는다(구름의 흰빛은 그대로다).
// 그 위에 S자 곡선을 TONE_CONTRAST만큼 입혀 어두운 쪽은 더 어둡고 밝은 쪽은 더 밝게 명암 폭을 넓힌다.
// 바다 패스는 하늘 장을 빛깔마다 따로 되돌렸다가 다시 누르므로, 비친 하늘은 이 색 그대로다.
STATIC const float TONE_HUE = 0.85;
STATIC const float TONE_CONTRAST = 0.35;
// 이 높이(광선의 y)부터 하늘 장의 알파가 구름 너머로 트인 정도다(그 아래 지평선 띠는 하늘과 바다를 잇는 알파).
STATIC const float SKY_OPEN_FROM = 0.012;
vec3 ToneMap(vec3 x) {
  // 빛은 음수가 될 수 없다(셈의 작은 어긋남이 음수가 되면 빛깔 비율·S자 곡선이 엉뚱한 색을 낸다).
  x = max(x, v3(0.0));
  vec3 perChannel = v3(1.0) - exp(-x);
  float l = dot(x, vec3(0.2126, 0.7152, 0.0722));
  vec3 hue = min(x * ((1.0 - exp(-l)) / max(l, 1e-4)), v3(1.0));
  vec3 c = mix(perChannel, hue, TONE_HUE);
  return mix(c, c * c * (3.0 - 2.0 * c), TONE_CONTRAST);
}

// uv: 화면 가운데가 0, 세로 한 칸이 1. pixel: 화소 번호(걸음 흔들기용). 반환: 톤매핑한 빛(선형)과 불투명도.
vec4 Shade(vec2 uv, vec2 pixel) {
  gCount = 0;
  vec3 rd = CameraRay(uv);
  // 지평선 아래는 바다가 덮으므로 무거운 셈을 건너뛴다.
  // 지평선 바로 아래 몇 줄은 남긴다: 보이는 장을 거를 때 지평선 줄이 이웃을 읽는다.
  if (rd.y < -0.02) return vec4(0.0, 0.0, 0.0, 0.0);
  vec3 ro = EYE;
  // 하늘·구름을 비추는 빛: 낮과 박명에는 해, 깊은 밤에는 달.
  vec3 s = LightDir();
  vec2 dir = Direction(rd);
  float tw = Twilight();

  // 대기: 고도별 햇빛을 셈하고, 이 방향으로 쌓이는 공기 빛을 걷는다.
  PrepareSun(s);

  // 구름을 비추는 햇빛(색은 고도별로 SunLit이 곱한다). 층구름이 덮거나 뇌우면 줄어든다.
  vec3 sc = CLOUD_SUN * (1.0 - 0.8 * _Storm) * SunTransmit() * LightScale();
  float ovc = Overcast();
  // 그늘을 채우는 하늘빛: 머리 위와 지평선 하늘의 빛에서 가져온다. 밑은 땅에서 튄 빛이다.
  vec3 zenithSky = v3(0.0);
  vec3 horizonSky = v3(0.0);
  bool hitGround;
  IntegrateAir(ro, vec3(0.0, 1.0, 0.0), s, hitGround);
  zenithSky = gAirL[SKY_STEPS];
  IntegrateAir(ro, normalize(vec3(rd.x, 0.12, rd.z)), s, hitGround);
  horizonSky = gAirL[SKY_STEPS];
  // 이 방향의 공기 빛은 마지막에 걷는다(겹칠 때 이 값을 쓴다).
  float airEnd = IntegrateAir(ro, rd, s, hitGround);
  vec3 ambTop = (zenithSky * 0.55 + horizonSky * 0.45) * 0.55;
  vec3 ambBottom = GROUND_ALBEDO * (gSunAt[0] * max(s.y, 0.0) * GROUND_SUN * LightScale() + ambTop * 1.2) + ambTop * 0.25;
  // 해가 지면 구름 밑은 하늘보다 어둡다(위에서 오는 하늘빛만 받는다).
  ambTop *= mix(1.0, 0.45, tw);
  ambTop = mix(ambTop, v3(dot(ambTop, vec3(0.3, 0.5, 0.2))) * 1.1, ovc * 0.7) * mix(1.0, 0.45, _Storm);
  ambBottom = mix(ambBottom, v3(dot(ambBottom, vec3(0.3, 0.5, 0.2))), ovc * 0.7) * mix(1.0, 0.45, _Storm);
  vec3 ambMid = mix(ambBottom, ambTop, 0.8);
  float cosT = dot(rd, s);
  float forward = min(HG(cosT, 0.6) * 4.0 * PI, 6.0);
  float jitter = PixelHash(pixel);
  vec2 sunXZ = normalize(s.xz + vec2(1e-4, 0.0));

  // ---- 웅대적운 탑(모루·갓구름 포함) ----
  if (_Extra.z > 0.01 || _Special.x > 0.01) {
    float h = TowerTop();
    float anvilLeft = _Special.x > 0.001 ? AnvilReach() * ANVIL_TAIL + 2.5 : 0.0;
    // 모루는 끝으로 갈수록 앞뒤로도 넓게 퍼진다(반폭 최대 약 8 km). 걷는 상자가 모루를 자르지 않게 넓힌다.
    float anvilSide = _Special.x > 0.001 ? ANVIL_SIDE_REACH : 0.0;
    vec3 bmin = TOWER + vec3(-TOWER_REACH.x - anvilLeft, -0.2, -max(TOWER_REACH.y, anvilSide));
    vec3 bmax = TOWER + vec3(TOWER_REACH.x, h + 3.4, max(TOWER_REACH.y, anvilSide));
    vec3 inv = 1.0 / rd;
    vec3 ta = (bmin - ro) * inv;
    vec3 tb = (bmax - ro) * inv;
    vec3 tmin = min(ta, tb);
    vec3 tmax = max(ta, tb);
    float t0 = max(max(tmin.x, tmin.y), tmin.z);
    float t1 = min(min(tmax.x, tmax.y), tmax.z);
    if (t1 > max(t0, 0.0)) {
      t0 = max(t0, 0.0);
      const int STEPS = 170;
      float dt = (t1 - t0) / float(STEPS);
      float t = t0 + dt * jitter;
      vec3 col = v3(0.0);
      float T = 1.0;
      float firstHit = -1.0;
      // 광선이 처음 만난 것이 갓구름인 정도. 갓구름은 렌즈구름처럼 공기 원근을 덜 받아 빛깔이 하늘색에 씻기지 않는다.
      float capFirst = 0.0;
      for (int i = 0; i < STEPS; i++) {
        vec3 p = ro + rd * t;
        float den = TowerDensity(p, true);
        float across;
        float thin;
        float cap = CapDensity(p, across, thin) * 0.85;
        float total = den + cap;
        if (total > 0.003) {
          if (firstHit < 0.0) {
            firstHit = t;
            capFirst = cap / total;
          }
          // 해 쪽으로 걸어 쌓인 밀도. 가까운 두 걸음은 잔 송이까지 넣어 송이마다 그늘이 진다.
          float od = 0.0;
          float ls = 0.08;
          vec3 lp = p;
          for (int k = 0; k < 7; k++) {
            lp += s * ls;
            od += TowerDensity(lp, k < 2) * ls;
            ls *= 1.6;
          }
          // 직접 산란: 해를 등지고 볼수록 앞쪽으로 쏠린 빛이 가장자리를 은빛으로 태운다.
          float single = exp(-od * SIGMA * TOWER_SIGMA);
          // 여러 번 흩어진 빛: 덜 감쇠해 두꺼운 곳도 은은하다.
          float multi = exp(-od * SIGMA * TOWER_SIGMA * 0.18);
          float ms = single * (0.75 + 0.25 * forward);
          float powder = 1.0 - exp(-total * SIGMA * TOWER_SIGMA * 0.35);
          // 위와 보는 쪽이 막힌 골은 하늘빛을 덜 받는다.
          float occ = TowerDensity(p + vec3(0.0, 0.3, 0.0), false) + TowerDensity(p + vec3(0.0, 0.8, 0.0), false) * 0.8
                    + TowerDensity(p - rd * 0.4, false) * 0.6;
          float ao = exp(-occ * 1.7);
          float hgt = clamp((p.y - TOWER.y) / h, 0.0, 1.0);
          vec3 amb = mix(ambBottom, ambTop, smoothstep(0.0, 0.8, hgt));
          // 박명: 땅 그림자보다 높은 곳만 붉은 햇빛을 받는다.
          vec3 scHere = sc * SunLit(p.y) * TOWER_SUN;
          // 여러 번 흩어진 빛은 하늘빛과 섞여 해 색보다 희고 푸르다(따뜻한 색이 그늘에 들면 흙빛이 된다).
          vec3 scMulti = mix(scHere, v3(dot(scHere, vec3(0.3, 0.5, 0.2))) * vec3(0.92, 0.97, 1.06), 0.55);
          vec3 light = (scHere * ms * mix(0.6, 1.0, powder) * mix(0.75, 1.0, ao) + scMulti * multi * TOWER_MULTI * mix(0.5, 1.0, ao)
                      + amb * TOWER_AMBIENT * mix(0.35, 1.0, ao)) * mix(1.0, 0.45, _Storm);
          if (cap > 0.003) {
            // 채운: 너울은 희게 빛나고, 얇은 바깥 가장자리를 따라 무지개 띠가 둘린다(바깥이 빨강, 안쪽으로 보라).
            // 띠는 물방울 크기에 따라 조금씩 일렁이며 천천히 흐른다.
            float ang = degrees(acos(clamp(cosT, -1.0, 1.0)));
            float drift = N(p * 0.05 + vec3(0.0, 0.0, _Seed)).r - 0.5;
            float x = clamp(1.15 - thin * 1.2 + drift * 0.35 + sin(_SkyTime * IRI_FLOW * 6.2832) * 0.08, 0.0, 1.0);
            float sat = mix(IRI_CAP_CENTER, 1.0, smoothstep(0.2, 0.75, thin)) * mix(IRI_FAR_SUN, 1.0, smoothstep(80.0, 25.0, ang))
                        * smoothstep(-1.0, 4.0, SunElevationDeg());
            vec3 veil = Iridesce((scHere * 0.8 + amb * 1.2) * 1.2, x, sat);
            light = mix(light, veil, cap / total);
          }
          float ts = exp(-total * SIGMA * TOWER_SIGMA * dt);
          col += T * light * (1.0 - ts);
          T *= ts;
          if (T < 0.01) break;
        }
        t += dt;
      }
      if (firstHit > 0.0) AddLayer(firstHit, col, T, mix(1.0, IRI_AIR, capFirst));
    }
  }

  // ---- 뭉게구름 떼 ----
  if (_Low.z > 0.01 && rd.y > 0.012) {
    // 머리 위(시계 자리)는 비우고, 5 km 밖부터 지평선 쪽으로 멀어지며 늘어선다.
    float ta = max((CUMULUS_BASE - ro.y) / rd.y, 5.0);
    float tb = min((CUMULUS_BASE + CUMULUS_DEPTH - ro.y) / rd.y, 60.0);
    if (tb > ta) {
      // 가까운 송이는 촘촘히, 먼 송이는 성기게 걷는다(먼 것은 화면에서 작다).
      const int CSTEPS = 200;
      float t = ta + max(0.05, ta * 0.012) * jitter;
      vec3 col = v3(0.0);
      float T = 1.0;
      float firstHit = -1.0;
      for (int i = 0; i < CSTEPS; i++) {
        if (t > tb) break;
        float dt = max(0.05, t * 0.012);
        vec3 p = ro + rd * t;
        float den = CumulusDensity(p, true);
        if (den > 0.003) {
          if (firstHit < 0.0) firstHit = t;
          float od = CumulusDensity(p + s * 0.12, true) * 0.12 + CumulusDensity(p + s * 0.4, false) * 0.3
                   + CumulusDensity(p + s * 0.9, false) * 0.5;
          float single = exp(-od * SIGMA * CUMULUS_SIGMA);
          float multi = exp(-od * SIGMA * CUMULUS_SIGMA * 0.18);
          float ao = exp(-(CumulusDensity(p + vec3(0.0, 0.25, 0.0), false) + CumulusDensity(p + vec3(0.0, 0.6, 0.0), false)) * 1.2);
          vec3 amb = mix(ambBottom, ambTop, smoothstep(0.0, 1.2, p.y - CUMULUS_BASE));
          vec3 scHere = sc * SunLit(p.y);
          vec3 scMulti = mix(scHere, v3(dot(scHere, vec3(0.3, 0.5, 0.2))) * vec3(0.92, 0.97, 1.06), 0.55);
          vec3 light = scHere * single * (0.75 + 0.25 * forward) * mix(0.75, 1.0, ao) * CUMULUS_SUN + scMulti * multi * CUMULUS_MULTI
                     + amb * mix(0.5, 1.0, ao) * CUMULUS_AMBIENT;
          dt *= 0.5;
          float ts = exp(-den * SIGMA * dt);
          col += T * light * (1.0 - ts);
          T *= ts;
          if (T < 0.02) break;
        }
        t += dt;
      }
      // 해 가까이 있는 송이의 얇은 가장자리는 채운으로 물든다(가장자리에서 안쪽으로 띠가 바뀐다).
      if (firstHit > 0.0) col = EdgeIridesce(col, 1.0 - T, cosT, rd, 1.0 - smoothstep(15.0, 35.0, firstHit), IRI_CUMULUS);
      if (firstHit > 0.0) AddLayer(firstHit, col, T);
    }
  }

  // ---- 평면 구름층 ----
  if (rd.y > 0.003) {
    // 권운
    float t = LayerHit(ro, rd, CIRRUS_ALTITUDE, 1.0);
    float d = CirrusDensity((ro + rd * t).xz, PlaneFootprint(CIRRUS_ALTITUDE, rd.y));
    if (d > 0.001) {
      vec3 c = sc * SunLit(CIRRUS_ALTITUDE) * (0.95 + 0.4 * forward) + ambMid * 1.15;
      AddCloud(t, c, d * 0.62 * smoothstep(0.003, 0.05, rd.y));
    }
    // 권층운
    t = LayerHit(ro, rd, CIRROSTRATUS_ALTITUDE, 0.8);
    d = CirrostratusDensity((ro + rd * t).xz);
    if (d > 0.001) {
      // 앞으로 흩어진 빛은 조금만 받는다(해 둘레가 하얗게 타면 햇무리가 묻힌다. 실제로도 무리 안쪽은 조금 어둡다).
      vec3 c = sc * SunLit(CIRROSTRATUS_ALTITUDE) * (0.9 + 0.25 * min(forward, 2.5)) + ambMid * 1.2;
      // 햇무리·무리해는 너울이 짙을수록 또렷하다. 해가 지면 사라진다.
      c += sc * SunLit(CIRROSTRATUS_ALTITUDE) * CirrostratusHalo(rd, s) * smoothstep(0.2, 0.7, d) * smoothstep(-1.0, 3.0, SunElevationDeg());
      AddCloud(t, c, d * 0.42);
    }
    // 권적운(구멍구름의 얼음 꼬리 포함)
    t = LayerHit(ro, rd, CIRROCUMULUS_ALTITUDE, 0.8);
    vec2 xz = (ro + rd * t).xz;
    float ice;
    float ccBlur = PlaneFootprint(CIRROCUMULUS_ALTITUDE, rd.y);
    d = CirrocumulusDensity(xz, ccBlur, ice);
    if (d > 0.001 || ice > 0.001) {
      float iceNear;
      float toward = CirrocumulusDensity(xz + sunXZ * 0.12, ccBlur, iceNear) * 0.5;
      vec3 c = LayerLight(d, toward, forward, sc * SunLit(CIRROCUMULUS_ALTITUDE), ambMid, 1.05, 0.35);
      vec3 iceCol = sc * SunLit(CIRROCUMULUS_ALTITUDE) * (0.95 + 0.4 * forward) + ambMid * 1.15;
      float a = max(d * 0.75, ice * 0.55);
      AddCloud(t, mix(c, iceCol, ice / max(a, 1e-3) * 0.55), a * smoothstep(0.003, 0.05, rd.y));
    }
    // 고층운
    t = LayerHit(ro, rd, ALTOSTRATUS_ALTITUDE, 0.6);
    d = AltostratusDensity((ro + rd * t).xz);
    if (d > 0.001) {
      vec3 c = mix(sc * SunLit(ALTOSTRATUS_ALTITUDE) * 0.45 + ambMid * 1.5, ambMid * 1.25 + sc * 0.15, smoothstep(0.3, 1.0, d));
      // 간유리 너머의 해: 너울이 얇은 곳에서 해가 흐릿한 원반으로 비친다.
      float through = 1.0 - smoothstep(0.55, 1.0, d);
      c += sc * SunLit(ALTOSTRATUS_ALTITUDE) * (exp(-pow(degrees(acos(clamp(cosT, -1.0, 1.0))) / 4.0, 2.0)) * 1.5
                                               + exp(-degrees(acos(clamp(cosT, -1.0, 1.0))) / 12.0) * 0.35) * through;
      AddCloud(t, c, clamp(d, 0.0, 0.97));
    }
    // 고적운(구멍구름·꼬리구름 포함)
    t = LayerHit(ro, rd, ALTOCUMULUS_ALTITUDE, 0.6);
    xz = (ro + rd * t).xz;
    float acBlur = PlaneFootprint(ALTOCUMULUS_ALTITUDE, rd.y);
    float patchHue;
    float sunPatch = SunPatch(dir, Direction(s), patchHue);
    d = AltocumulusLayer(xz, acBlur, sunPatch);
    ice = 0.0;
    float keep = FallstreakHole(xz, ALTOCUMULUS_ALTITUDE, 2.0, ice);
    d *= keep;
    if (d > 0.001 || ice > 0.001) {
      // 덩이 반지름의 절반쯤 해 쪽 자리가 두꺼우면 해를 등진 쪽이다.
      float toward = AltocumulusLayer(xz + sunXZ * 0.12, acBlur, sunPatch);
      // 해 둘레 조각의 송이는 얇아 속까지 빛이 지나 잿빛 가운데가 없다(잿빛에 빛깔을 입히면 탁한 보라가 된다).
      vec3 c = LayerLight(d, toward, forward, sc * SunLit(ALTOCUMULUS_ALTITUDE), ambMid, 1.0, mix(0.8, 0.1, sunPatch))
               * mix(1.0, mix(0.78, 0.95, sunPatch), clamp(toward, 0.0, 1.0));
      vec3 iceCol = sc * SunLit(ALTOCUMULUS_ALTITUDE) * (0.9 + 0.4 * forward) + ambMid * 1.15;
      // 덩이 가장자리는 얇아 하늘이 비친다(속은 짙고 가장자리는 부드럽게 풀린다).
      float a = max(smoothstep(0.0, 0.6, d) * 0.92, ice * 0.5);
      c = EdgeIridesce(c, a, cosT, rd, 1.0 - smoothstep(0.03, 0.08, acBlur), IRI_ALTOCUMULUS);
      // 해 둘레 조각: 송이 전체가 고리 빛깔로 물들고, 얇은 가장자리일수록 짙다. 공기 원근도 채운처럼 덜 받는다.
      float patchSat = sunPatch * mix(0.65, 1.0, 1.0 - smoothstep(0.3, 0.9, a)) * smoothstep(-1.0, 4.0, SunElevationDeg());
      c = Iridesce(c, patchHue, patchSat);
      vec3 col = mix(c, iceCol, clamp(ice * 1.5, 0.0, 1.0) * (1.0 - smoothstep(0.0, 0.3, d)));
      AddLayer(t, col * a, 1.0 - a, mix(1.0, IRI_AIR, smoothstep(0.0, 0.3, sunPatch)));
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
        for (int i = 0; i < VSTEPS; i++) {
          vec3 p = ro + rd * vt;
          float fall = (ALTOCUMULUS_ALTITUDE - p.y) / 2.4;
          float src = AltocumulusDensity(p.xz + vec2(1.2, 0.3) * fall * fall * 2.4, 0.03);
          float streak = Remap(N(vec3(p.x * 1.1, 0.13, p.z * 1.1)).r, 0.3, 0.7);
          float dv = src * pow(max(1.0 - fall, 0.0), 0.8) * streak * _Mid.w * 1.6;
          va += dv * vdt * 0.9 * (1.0 - va);
          vt += vdt;
        }
        // 빗줄기라 송이보다 잿빛이다.
        vec3 c = sc * SunLit(ALTOCUMULUS_ALTITUDE - 1.2) * (0.25 + 0.3 * forward) + ambMid * 1.25;
        AddCloud(0.5 * (tTop + tBottom) - 0.01, c, clamp(va, 0.0, 0.7));
      }
    }
    // 난층운·먹구름(유방운 포함)
    if (_Low.w > 0.01 || _Special.y > 0.01) {
      float pouchAt = 0.0;
      t = _Special.y > 0.01 ? MammatusHit(ro, rd, pouchAt) : PlaneHit(ro, rd, NIMBOSTRATUS_ALTITUDE);
      if (t > 0.0 && t < 80.0) {
        xz = (ro + rd * t).xz;
        float thick;
        float pouch;
        float cover = NimbostratusDensity(xz, thick, pouch);
        // 두꺼운 밑면은 짙고, 얇은 곳은 위의 빛이 비쳐 밝다.
        vec3 deck = mix(ambTop * 1.6 + sc * SunLit(NIMBOSTRATUS_ALTITUDE) * 0.1, ambBottom * 0.5, thick);
        deck = mix(deck * 1.25, deck, cover);
        if (_Special.y > 0.01) {
          // 유방운: 늘어진 주머니 면의 기울기로 겉 방향을 셈한다(밑을 향한다). 낮은 해를 받는 볼은 금빛으로 밝고,
          // 해를 등진 쪽은 아래 바다·지평선에서 튄 빛만 받아 어둡다. 주머니 사이 골은 깊게 그늘진다.
          float e = 0.12;
          float fx = (MammatusPouch(xz + vec2(e, 0.0)) - pouchAt) / e;
          float fz = (MammatusPouch(xz + vec2(0.0, e)) - pouchAt) / e;
          vec3 nrm = normalize(vec3(-MAMMATUS_DEPTH * fx, -1.0, -MAMMATUS_DEPTH * fz));
          float sunSide = max(dot(nrm, s) + 0.25, 0.0) / 1.25;
          float crease = smoothstep(0.0, 0.45, pouchAt);
          // 아래에서 오는 빛: 겉이 아래를 볼수록 바다·지평선 빛을 받는다.
          float below = 0.6 + 0.4 * max(-nrm.y, 0.0);
          vec3 pouchCol = (ambBottom * 1.15 * below + ambTop * 0.18) * mix(0.25, 1.0, crease)
                          + sc * SunLit(NIMBOSTRATUS_ALTITUDE - MAMMATUS_DEPTH * 0.5) * sunSide * mix(0.4, 1.0, crease) * 1.6;
          // 폭풍 속에서는 모루 밑이 어둡다.
          pouchCol *= mix(1.0, 0.55, _Storm);
          float mam = _Special.y * smoothstep(0.0, 0.35, MammatusPatch(xz));
          deck = mix(deck, pouchCol, mam);
          cover = max(cover, mam);
        }
        AddCloud(t, deck, cover * max(_Low.w, _Special.y) * smoothstep(0.003, 0.03, rd.y));
      }
    }
    // 층적운
    t = LayerHit(ro, rd, STRATOCUMULUS_ALTITUDE, 0.35);
    if (t < 70.0) {
      xz = (ro + rd * t).xz;
      d = StratocumulusDensity(xz);
      if (d > 0.001) {
        float toward = StratocumulusDensity(xz + sunXZ * 0.6);
        vec3 c = LayerLight(d, toward, forward, sc * SunLit(STRATOCUMULUS_ALTITUDE), ambMid, 0.95, 1.0);
        AddCloud(t, c, smoothstep(0.0, 0.4, d) * 0.96);
      }
    }
    // 층운
    t = PlaneHit(ro, rd, STRATUS_ALTITUDE);
    if (t < 60.0) {
      d = StratusDensity((ro + rd * t).xz, PlaneFootprint(STRATUS_ALTITUDE, rd.y));
      if (d > 0.001) {
        // 얇은 곳은 해를 받아 희게 빛나고 두꺼운 곳은 푸른 잿빛이다. 해 쪽 얇은 안개는 빛이 비쳐 은빛으로 환하고,
        // 해는 흐릿한 은빛 원반으로 비친다.
        float thin = 1.0 - smoothstep(0.25, 0.95, d);
        float ang = degrees(acos(clamp(cosT, -1.0, 1.0)));
        vec3 sunHere = sc * SunLit(STRATUS_ALTITUDE);
        vec3 c = ambMid * mix(1.25, 1.6, thin) + sunHere * (0.25 + 0.75 * thin) * (0.7 + 0.5 * min(forward, 3.0));
        c += sunHere * (exp(-pow(ang / 2.2, 2.0)) * 3.0 + exp(-ang / 9.0) * 0.9) * mix(0.35, 1.0, thin);
        AddCloud(t, c, d * 0.93);
      }
    }
  }

  // ---- 방향으로 그리는 구름 ----
  // 채운 너울: 고도 VEIL_ALTITUDE에 떠 있어, 그보다 가까운 뭉게구름이 앞을 가린다.
  float hue;
  float tint;
  float veilHaze;
  float veil = IridescentCloud(dir, hue, tint, veilHaze);
  if (veilHaze > 0.002 && rd.y > 0.01) {
    float veilDist = (VEIL_ALTITUDE - ro.y) / rd.y;
    float ang = degrees(acos(clamp(cosT, -1.0, 1.0)));
    float day = smoothstep(-1.0, 4.0, SunElevationDeg()) * (1.0 - _Storm);
    float nearSun = mix(IRI_FAR_SUN, 1.0, smoothstep(80.0, 25.0, ang));
    // 몸빛: 해를 받아 밝게 빛나는 얇은 구름
    vec3 light = sc * SunLit(VEIL_ALTITUDE) * (0.95 + 0.35 * forward) + ambMid * 1.15;
    // 빛깔: 모양마다 정한 무지개 자리. 두꺼운 결은 희게 빛난다.
    float sat = tint * day * mix(0.75, 1.0, nearSun) * (1.0 - 0.35 * smoothstep(0.6, 1.6, veil * VEIL_OPACITY));
    vec3 col = Iridesce(light, hue, sat);
    // 결 없는 옅은 너울이 빛깔을 은은히 깔고, 그 위에 결이 또렷이 선다.
    float a = clamp(veil * VEIL_OPACITY + veilHaze * VEIL_HAZE, 0.0, 0.95);
    AddLayer(veilDist, col * a, 1.0 - a, IRI_AIR);
  }
  // 바다 안개 둑: 수평선 위 먼 바다(약 20 km)에 깔려 가까운 구름 뒤에 선다.
  float fogTop;
  float fog = FogBank(dir, fogTop);
  if (fog > 0.001) {
    // 아래는 바다 빛을 받은 잿빛, 윗자락은 해를 받아 희고 따뜻하게 빛난다.
    vec3 c = mix(ambBottom * 1.5, ambTop * 2.0, fogTop) + sc * SunLit(0.3) * mix(0.3, 1.2, fogTop) * (0.8 + 0.5 * min(forward, 3.0));
    AddCloud(20.0, c, fog * 0.95);
  }
  // 아치구름
  float face;
  float under;
  float arcus = ArcusDensity(dir, face, under);
  if (arcus > 0.001 || under > 0.001) {
    // 몸통은 검푸른 잿빛이고, 윗면 띠만 희끄무레하게 층층이 줄진다. 띠는 활을 따라 휘며 몇 겹으로 포개진다.
    // 쐐기 밑은 지평선 빛이 새어 들어 밝다.
    float tiers = 0.5 + 0.5 * sin(face * 15.0 + dir.x * 0.04 + N(vec3(dir.x * 0.05, 0.4, 0.1)).r * 5.0);
    vec3 body = ambBottom * 0.45 + sc * SunLit(1.0) * 0.03;
    vec3 band = ambTop * 1.7 + sc * SunLit(1.5) * 0.35;
    vec3 c = mix(body, band, smoothstep(0.3, 0.9, face) * mix(0.55, 1.0, tiers));
    c = mix(c * 0.7, c, smoothstep(0.0, 0.25, face));
    vec3 gap = ambTop * 2.0 + sc * SunLit(0.5) * 0.15;
    float a = max(clamp(arcus, 0.0, 0.97), under * 0.6);
    AddCloud(9.0, mix(gap, c, arcus / max(a, 1e-3)), a);
  }
  // 켈빈-헬름홀츠 물결구름
  float curl;
  float kh = KelvinHelmholtzDensity(dir, curl);
  if (kh > 0.001) {
    // 부피 음영: 위가 비고 아래가 찬 곳(물결 윗면·말린 머리 꼭대기)은 해를 받아 밝고, 아래가 빈 곳(밑면·말린 안쪽)은 어둡다.
    // 해 쪽 옆면도 조금 밝다. 송이 결을 따라 밝기가 일렁인다.
    float tmp;
    float up = KelvinHelmholtzDensity(dir + vec2(0.0, 0.35), tmp);
    float down = KelvinHelmholtzDensity(dir - vec2(0.0, 0.35), tmp);
    float side = sign(Direction(s).x - dir.x);
    float toward = KelvinHelmholtzDensity(dir + vec2(0.35 * side, 0.0), tmp);
    float lit = clamp(0.45 + (down - up) * 1.3 + (kh - toward) * 0.6, 0.0, 1.0);
    float puffs = N(vec3(dir * 0.8, 0.53)).r;
    vec3 c = sc * SunLit(3.0) * (0.9 + 0.35 * forward) * mix(0.3, 1.25, lit) * mix(1.0, 0.45, curl) * mix(0.85, 1.1, puffs)
             + ambMid * mix(0.75, 1.1, lit) * mix(1.0, 0.8, curl);
    AddCloud(24.0, c, clamp(kh * 0.9, 0.0, 0.9));
  }
  // 야광운: 모든 구름 너머 아주 멀리. 땅 그림자 위 80 km에서 햇빛을 받는다.
  float nlc = NoctilucentDensity(dir);
  if (nlc > 0.001) {
    vec3 c = vec3(0.45, 0.75, 1.2) * 0.8 * SunLit(82.0);
    AddLayer(400.0, c * nlc * 0.9, 1.0 - nlc * 0.9);
  }

  // ---- 가까운 것부터 겹친다 ----
  for (int i = 1; i < MAX_LAYERS; i++) {
    if (i >= gCount) break;
    float dd = gDist[i];
    vec3 pp = gPre[i];
    float tt = gTr[i];
    float hh = gHaze[i];
    int j = i - 1;
    for (int k = 0; k < MAX_LAYERS; k++) {
      if (j < 0) break;
      if (gDist[j] <= dd) break;
      gDist[j + 1] = gDist[j];
      gPre[j + 1] = gPre[j];
      gTr[j + 1] = gTr[j];
      gHaze[j + 1] = gHaze[j];
      j--;
    }
    gDist[j + 1] = dd;
    gPre[j + 1] = pp;
    gTr[j + 1] = tt;
    gHaze[j + 1] = hh;
  }
  // 톤매핑(지수). 해가 낮거나 지면 눈이 어둠에 익듯 노출을 올린다.
  float el = SunElevationDeg();
  float exposure = _Exposure * mix(1.0, 1.7, smoothstep(25.0, 0.0, el)) * mix(1.0, 3.2, smoothstep(0.0, -9.0, el));

  // 가까운 구름부터: 구름 사이 공기가 더하는 빛(앞 구름에 가린 만큼), 공기를 지나 남은 구름 빛을 차례로 쌓는다.
  vec3 c = v3(0.0);
  float T = 1.0;
  vec3 prevL = v3(0.0);
  for (int i = 0; i < MAX_LAYERS; i++) {
    if (i >= gCount) break;
    vec3 airL;
    vec3 airT;
    AirAt(gDist[i] * gHaze[i], airL, airT);
    // 가까이 당긴 층(gHaze < 1)이 앞 구름보다 공기를 덜 셈할 수는 없다: 공기 빛은 앞으로 갈수록 줄지 않는다.
    airL = max(airL, prevL);
    c += T * (airL - prevL);
    c += T * airT * gPre[i];
    T *= gTr[i];
    prevL = airL;
  }
  // 마지막 구름 너머의 하늘. 흐리면 잿빛이다.
  c += T * Overcasted(gAirL[SKY_STEPS] - prevL + PurpleLight(rd, SunDir()));
  if (!hitGround) {
    // 해: 화면 밖에 있을 때가 많지만 들어오면 눈부신 원반이다.
    float disk = smoothstep(0.99996, 0.99999, dot(rd, SunDir()));
    c += T * gAirT[SKY_STEPS] * disk * 40.0 * (1.0 - Overcast());
    c += T * gAirT[SKY_STEPS] * MoonDisc(rd) * (1.0 - Overcast());
    c += T * Stars(rd);
  }

  // 지평선 아래: 하늘이 안개처럼 풀려 바다로 이어진다. 지평선에 걸친 구름은 조금 더 남는다.
  float alpha = max(smoothstep(-0.09, 0.01, rd.y), (1.0 - T) * smoothstep(-0.06, 0.0, rd.y));
  // 지평선 위에서는 알파에 구름 너머로 트인 정도(0.5 구름에 막힘 ~ 1 트인 하늘)를 담는다. 알파는 덮는 데 쓰지 않으므로,
  // 보이기 패스가 빛살을 셈할 때 구름이 빛을 막는 자리로 읽는다(0이 되면 거르기가 깨지므로 0.5부터).
  if (rd.y > SKY_OPEN_FROM) alpha = 0.5 + 0.5 * T;
  return vec4(ToneMap(c * exposure), alpha);
}
