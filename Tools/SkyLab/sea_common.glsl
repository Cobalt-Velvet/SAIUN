// ==== 바다와 해변 (보이는 장을 만드는 패스, 매 프레임) ====
// 지평선 아래는 바다다. 빛이 물결에 흩어져 반짝인다.
// 물은 하늘을 비추므로 하늘의 빛깔·구름·노을이 그대로 아래로 이어진다.
//  - 물결 면이 비추는 하늘은 이미 그린 하늘 장에서 거울 방향으로 읽는다. 웅대적운과 구름도 물에 비친다.
//  - 해 쪽으로는 물결 면마다 햇빛을 튕겨 윤슬 길이 선다(콕스-멍크 물결 기울기 분포). 반짝임은 매 프레임 새로 인다.
//  - 가까운 물결은 결이 보이고, 멀어질수록 한 화소에 물결이 여럿 들어가 매끈한 거울에 거친 반사로 바뀐다.
//  - 앞쪽은 모래밭이다. 물가에서 파도가 밀려왔다 빠지며 젖은 모래가 하늘을 비춘다.
// 하늘 장은 톤매핑을 마친 값이라 빛을 셈하기 전에 되돌리고(HDR), 셈한 뒤 다시 톤매핑한다.
// 바깥에서 줄 것: SkySample(텍스처 uv) = 하늘 장의 곧은 색(선형)·알파, N(p) = 3D 노이즈.

STATIC const float SEA_PI = 3.14159265;
// 모래 언덕 위 눈높이(km)
STATIC const float SEA_EYE = 0.012;
// 물가까지의 거리(km)와 물가가 휘는 정도
STATIC const float SHORE = 0.118;
STATIC const float SHORE_BEND = 0.016;
// 파도가 모래밭으로 올라오는 거리(km)와 한 번 밀려왔다 빠지는 시간(초)
STATIC const float RUNUP = 0.02;
STATIC const float SWASH_SECONDS = 9.0;
STATIC const vec3 SAND_ALBEDO = vec3(0.50, 0.43, 0.33);
// 물속에서 올라오는 빛의 빛깔(맑은 바다의 짙은 청록)
STATIC const vec3 DEEP_WATER = vec3(0.01, 0.055, 0.08);
// 해가 땅과 물을 비추는 세기(하늘 셰이더의 GROUND_SUN과 같은 단위)
STATIC const float SEA_SUN = 3.0;
// 먼 바다가 대기에 잠기는 거리(km)
STATIC const float SEA_HAZE = 16.0;

// sin 없이 곱셈·fract로만 섞는 해시(0~1). sin 해시는 큰 인자에서 GPU마다 값이 흔들리고 화소 좌표에서 무늬가 된다.
float SeaHash(vec2 p) {
  vec3 p3 = fract(vec3(p.x, p.y, p.x) * 0.1031);
  p3 += dot(p3, p3.yzx + 33.33);
  return fract((p3.x + p3.y) * p3.z);
}

// 화소마다 고른 흩뜨림 값(0~1)
float SeaPixelHash(vec2 p) { return SeaHash(p); }

float SeaSunElevation() { return degrees(asin(clamp(_SunDir.y, -1.0, 1.0))); }

// 하늘 셰이더와 같은 노출(해가 낮거나 지면 올린다).
float SeaExposure() {
  float el = SeaSunElevation();
  return _Exposure * mix(1.0, 1.7, smoothstep(25.0, 0.0, el)) * mix(1.0, 3.2, smoothstep(0.0, -9.0, el));
}

vec3 ToHdr(vec3 c) { return -log(max(vec3(1.0, 1.0, 1.0) - c, vec3(1e-3, 1e-3, 1e-3))) / SeaExposure(); }
vec3 ToDisplay(vec3 x) { return vec3(1.0, 1.0, 1.0) - exp(-x * SeaExposure()); }

vec2 TextureUv(vec2 uv) { return uv / vec2(_SkySize.x / _SkySize.y, 1.0) + 0.5; }

// 방향 r의 하늘빛(HDR). 지평선 아래를 가리키면 지평선 바로 위로, 화면 밖은 가장자리로 대신한다.
vec3 SkyHdr(vec3 r) {
  vec2 t = TextureUv(CameraUv(vec3(r.x, max(r.y, 0.006), r.z)));
  t = clamp(t, vec2(0.003, 0.003), vec2(0.997, 0.997));
  return ToHdr(SkySample(t).rgb);
}

float Schlick(float cosT) {
  float k = 1.0 - clamp(cosT, 0.0, 1.0);
  return 0.02 + 0.98 * k * k * k * k * k;
}

// 물결 높이(0~1).
//  - 너울: 먼바다에서 밀려오는 긴 결. 물가에 가까워지며 해안과 나란히 누워 늘 물가 쪽(앞)으로 온다. 바람과 상관없다.
//  - 잔물결: 바람이 이는 잔 결 두 겹. _SeaWind.xy(바람을 따라 흘러간 거리, km)만큼 밀려 흐른다.
// 결을 바람 방향으로 돌리면 바람이 조금만 바뀌어도 먼 물결이 눈을 축으로 휩쓸리듯 돌아
// 수면이 너무 빨리 움직여 보인다. 그래서 무늬는 돌리지 않고 흘러간 거리로만 민다. 바람이 바뀌면 흐르는 방향이 서서히 바뀔 뿐 무늬는 튀지 않는다.
// 잔물결 주파수는 SEA_DRIFT_PERIOD(km)에서 정수 번 돌게 골라, 흘러간 거리를 그 주기로 되감아도 무늬가 이어진다.
float WaveHeight(vec2 p) {
  float t = _SeaTime;
  float a = N(vec3(p.y * 26.0 + t * 0.045, p.x * 11.0, 0.13 + t * 0.004)).r;
  vec2 d = p - _SeaWind.xy;
  float b = N(vec3(d.x * 64.0, d.y * 64.0 + 0.6, 0.57 + t * 0.011)).r;
  vec2 k = vec2(d.x * 0.8 + d.y * 0.6, d.y * 0.8 - d.x * 0.6);
  float c = N(vec3(k.x * 40.0, k.y * 28.0 + 0.2, 0.31 + t * 0.006)).r;
  return a * 0.5 + b * 0.25 + c * 0.25;
}

// 해가 바다 수면에 닿는 빛(바깥에서 대기를 지난 해 빛깔을 준다). 해가 지면 사라진다.
// 물을 비추는 빛(해 또는 달)의 빛깔과 세기. _SunColor는 그 빛이 대기를 지나 남은 빛깔이다.
vec3 SunAtSea() { return _SunColor.rgb * LightScale() * smoothstep(-0.6, 0.4, degrees(asin(clamp(LightDir().y, -1.0, 1.0)))); }

// 하늘에서 고르게 내려오는 빛(HDR): 머리 위와 지평선 하늘을 섞는다.
vec3 SkyAmbient() {
  vec3 up = SkyHdr(normalize(vec3(0.0, 0.75, 0.66)));
  vec3 side = SkyHdr(normalize(vec3(0.0, 0.12, 1.0)));
  return up * 0.6 + side * 0.4;
}

// 물결 기울기 분포(콕스-멍크): 바람이 셀수록 넓다. 해가 튕겨 오는 면의 비율이 윤슬 세기다.
float SlopeVariance() {
  float windMs = mix(2.0, 10.0, _SeaWind.z) + _Storm * 6.0;
  return 0.003 + 0.00512 * windMs;
}

float GlintPdf(vec3 hN, vec3 n, float variance) {
  float c = max(dot(hN, n), 1e-3);
  float c2 = c * c;
  float tan2 = (1.0 - c2) / c2;
  return exp(-tan2 / (2.0 * variance)) / (2.0 * SEA_PI * variance * c2 * c2);
}

// 한 화소의 반짝임(0 또는 번쩍): 물결 면 하나가 해를 튕기는 순간. 해가 튕겨 올 면이 많을수록 자주 반짝인다.
float Sparkle(vec2 pixel, float chance) {
  vec2 cell = floor(pixel / 1.5);
  float h = SeaHash(cell);
  float rate = 2.2 + 2.5 * SeaHash(cell + 7.1);
  float phase = fract(_SeaTime * rate + h * 13.7);
  float which = floor(_SeaTime * rate + h * 13.7);
  float on = step(1.0 - chance, SeaHash(cell + which * 0.123 + 3.3));
  float s = sin(phase * SEA_PI);
  return on * s * s * s * s;
}

// 지평선 아래 한 화소의 빛(HDR). rd는 보는 방향(rd.y < 0), pixel은 화면 화소.
vec3 SeaColor(vec3 rd, vec2 pixel) {
  vec3 s = LightDir();
  vec3 sun = SunAtSea();
  vec3 amb = SkyAmbient();
  float t = SEA_EYE / max(-rd.y, 1e-4);
  vec3 p = rd * t;
  float dist = t * length(rd.xz);
  // 한 화소가 덮는 물 위 길이(km): 멀수록, 지평선에 가까울수록 커진다.
  float footprint = dist * dist * 0.0015 / SEA_EYE + dist * 0.0015;
  float detail = exp(-footprint / 0.012);

  // 물가: 앞(가까운 쪽)이 모래밭, 너머가 바다. 물가는 부드럽게 휘고, 파도가 밀려왔다 빠진다.
  float shore = SHORE + SHORE_BEND * sin(p.x * 6.0 + 1.3) + 0.01 * (N(vec3(p.x * 2.5, 0.31, 0.71)).r - 0.5);
  float cycle = _SeaTime / SWASH_SECONDS + N(vec3(p.x * 1.7, 0.12, 0.23)).r * 0.8;
  float wash = 0.5 + 0.5 * sin(cycle * 2.0 * SEA_PI);
  float front = shore - RUNUP * wash * wash;
  float water = smoothstep(front - 0.0012, front + 0.0012, dist);

  // ---- 바다 ----
  float e = 0.0015 + footprint * 0.5;
  float h0 = WaveHeight(p.xz);
  vec2 g = vec2(WaveHeight(p.xz + vec2(e, 0.0)) - h0, WaveHeight(p.xz + vec2(0.0, e)) - h0) / e;
  float slope = mix(0.0022, 0.0045, _SeaWind.z) * (1.0 + _Storm);
  // 물가 가까이는 물이 얕아 결이 잔잔하다.
  float calm = smoothstep(shore, shore + 0.03, dist);
  vec3 n = normalize(vec3(-g.x * slope * detail * calm, 1.0, -g.y * slope * detail * calm));
  vec3 v = -rd;
  vec3 r = reflect(rd, n);
  float F = Schlick(dot(v, n));
  // 먼 물은 한 화소에 물결이 여럿이라 거울상이 위아래로 번진다.
  float smear = sqrt(SlopeVariance()) * (1.0 - detail) * 0.9;
  vec3 mirror = (SkyHdr(r) * 2.0 + SkyHdr(r + vec3(0.0, smear, 0.0)) + SkyHdr(r + vec3(0.0, smear * 2.2, 0.0))
                 + SkyHdr(r - vec3(0.0, smear * 0.5, 0.0))) / 5.0;
  vec3 body = DEEP_WATER * (amb * 1.2 + sun * max(s.y, 0.0) * SEA_SUN * 0.5);
  vec3 sea = mix(body, mirror, F);

  // 윤슬: 결이 보이는 가까운 물은 좁은 반사, 먼 물은 넓게 번진 반사 길. 그 안에서 물결 면이 번쩍인다.
  float variance = SlopeVariance() * mix(0.35, 1.0, 1.0 - detail);
  vec3 hN = normalize(v + s);
  float pdf = GlintPdf(hN, n, variance);
  float fs = Schlick(dot(v, hN));
  float cosV = max(dot(v, n), 0.06);
  vec3 glint = sun * SEA_SUN * fs * pdf / (4.0 * cosV);
  float chance = clamp(pdf * 0.02, 0.0, 0.55) * step(0.0, s.y + 0.01);
  sea += glint * 0.55 + sun * SEA_SUN * 6.0 * fs * Sparkle(pixel, chance) / cosV;

  // 물가에 부서지는 파도 거품과, 밀려온 물 끝의 거품 띠
  float breakLine = exp(-abs(dist - shore - 0.006) / 0.005) * smoothstep(0.3, 0.7, N(vec3(p.x * 30.0, _SeaTime * 0.03, 0.4)).r);
  float lip = exp(-max(dist - front, 0.0) / 0.004) * step(front - 0.0015, dist) * (0.4 + 0.6 * wash);
  float foam = clamp(max(breakLine * 0.8, lip), 0.0, 1.0) * (0.6 + 0.4 * N(vec3(p.x * 90.0, dist * 60.0, 0.83)).r);
  vec3 foamLight = sun * max(s.y, 0.0) * SEA_SUN * 0.8 + amb * 1.1;
  sea = mix(sea, foamLight, foam * 0.9);

  // ---- 모래밭 ----
  float grain = N(vec3(p.x * 420.0, dist * 900.0, 0.21)).r;
  float ripples = N(vec3(p.x * 60.0, dist * 150.0, 0.66)).r;
  float patches = N(vec3(p.x * 9.0, dist * 25.0, 0.48)).r;
  float damp = smoothstep(shore - RUNUP - 0.03, shore - RUNUP, dist) * 0.25;
  vec3 albedo = SAND_ALBEDO * (0.8 + 0.14 * grain + 0.1 * ripples + 0.18 * patches) * (1.0 - damp);
  vec3 sandLight = sun * max(s.y, 0.0) * SEA_SUN + amb * 0.9;
  vec3 sand = albedo * sandLight;
  // 물이 막 빠진 모래는 젖어 짙고, 매끈해 하늘을 비춘다.
  float wet = smoothstep(shore - RUNUP - 0.004, shore - RUNUP * 0.3, dist) * (1.0 - water);
  float wetF = Schlick(v.y) * 0.9;
  vec3 wetSand = albedo * 0.55 * sandLight;
  wetSand = mix(wetSand, SkyHdr(vec3(rd.x, -rd.y, rd.z)), wetF);
  sand = mix(sand, wetSand, wet);

  vec3 c = mix(sand, sea, water);
  // 먼 바다는 대기에 잠겨 지평선 하늘로 이어진다.
  vec3 haze = SkyHdr(vec3(rd.x, 0.0, rd.z));
  // 바다 안개(층운)가 끼면 먼 바다가 금세 안개 빛으로 잠긴다.
  return mix(c, haze, 1.0 - exp(-dist / (SEA_HAZE * mix(1.0, 0.12, smoothstep(0.0, 0.8, _Low.y)))));
}

// 보이는 장의 한 화소: 지평선 위는 하늘 장, 아래는 바다로 채운다.
// ---- 빛내림(화면에서) ----
// 해의 화면 자리를 향해 걸으며 해 둘레의 밝은 하늘(구름 틈으로 트인 하늘, 해를 받은 구름 가장자리)을 모은다.
// 구름 틈으로 새는 빛이 해에서 부채꼴로 뻗는 빛살이 되고, 구름에 가린 쪽은 빛을 모으지 못해 그림자 줄기가 된다.
// 공기 속 빛이라 바다 위로도 내린다. 해가 화면에서 멀거나 지평선 아래로 깊이 가면 사라진다.
STATIC const int RAY_TAPS = 28;
// 걸음마다 남는 빛(멀리서 온 빛일수록 옅다)
STATIC const float RAY_DECAY = 0.94;
// 해 둘레 빛무리를 재는 거리(화면 높이 1 기준)
STATIC const float RAY_HALO = 0.05;
// 빛살이 해 쪽으로 뻗는 길이(해까지의 거리 대비), 빛무리 평균 밝기 대비 빛살이 되는 문턱, 세기
STATIC const float RAY_LENGTH = 0.9;
STATIC const float RAY_THRESHOLD = 0.8;
STATIC const float RAY_STRENGTH = 2.5;
vec3 SunRays(vec2 tuv, vec3 rd) {
  vec3 s = SunDir();
  float facing = dot(rd, s);
  if (s.y < -0.06 || facing < 0.3 || _Storm > 0.95) return v3(0.0);
  vec2 sunUv = TextureUv(CameraUv(s));
  // 문턱은 해 자리 밝기에 비례한다: 한낮의 흰 해 둘레도, 노을의 주황 해 둘레도 그 둘레에서 가장 밝은 곳만 빛살이 된다.
  // 해 원반 한 점은 너무 밝으므로 해 둘레 빛무리의 평균 밝기를 기준으로 삼는다.
  float lSun = 0.0;
  for (int k = 0; k < 6; k++) {
    float ang = float(k) * 1.0472;
    vec2 at = clamp(sunUv + vec2(cos(ang), sin(ang)) * vec2(RAY_HALO * _SkySize.y / _SkySize.x, RAY_HALO), vec2(0.002, 0.002), vec2(0.998, 0.998));
    lSun += dot(SkyQuick(at).rgb, vec3(0.3, 0.59, 0.11)) / 6.0;
  }
  lSun = max(lSun, 0.05);
  float lo = lSun * RAY_THRESHOLD;
  vec2 delta = (sunUv - tuv) * (RAY_LENGTH / float(RAY_TAPS));
  // 걸음 시작을 화소마다 흩뜨려 걸음 간격의 동심원 줄무늬를 없앤다(남는 잔 얼룩은 빛살이 흐려 보이지 않는다).
  vec2 uv = tuv + delta * SeaPixelHash(floor(tuv * _SkySize.xy) + fract(_SeaTime) * 37.0);
  float decay = 1.0;
  vec3 sum = v3(0.0);
  for (int i = 0; i < RAY_TAPS; i++) {
    uv += delta;
    vec2 at = clamp(uv, vec2(0.002, 0.002), vec2(0.998, 0.998));
    vec4 c = SkyQuick(at);
    // 구름이 막은 자리는 빛을 보내지 못한다(하늘 장 알파: 0.5 막힘 ~ 1 트임, 지평선 띠는 트인 것으로 본다).
    float open = CameraRay((at - 0.5) * vec2(_SkySize.x / _SkySize.y, 1.0)).y > 0.012 ? clamp(c.a * 2.0 - 1.0, 0.0, 1.0) : 1.0;
    float l = dot(c.rgb, vec3(0.3, 0.59, 0.11));
    sum += c.rgb * open * smoothstep(lo, lSun, l) * decay;
    decay *= RAY_DECAY;
  }
  // 해가 화면 밖으로 멀어지면 옅어진다.
  vec2 off = max(abs(sunUv - 0.5) - 0.5, vec2(0.0, 0.0));
  float onScreen = 1.0 - smoothstep(0.0, 0.35, max(off.x, off.y));
  float sunUp = smoothstep(-0.06, 0.02, s.y);
  return sum / float(RAY_TAPS) * smoothstep(0.3, 0.95, facing) * onScreen * sunUp * (1.0 - _Storm) * RAY_STRENGTH;
}

vec4 Present(vec2 tuv, vec2 pixel) {
  vec4 sky = SkySample(tuv);
  vec3 rd = CameraRay((tuv - 0.5) * vec2(_SkySize.x / _SkySize.y, 1.0));
  // 지평선 한 화소 폭으로 하늘과 바다를 잇는다.
  float pixelAngle = 0.97 / _SkySize.y;
  float below = smoothstep(0.0, -pixelAngle, rd.y);
  vec3 rays = SunRays(tuv, rd);
  if (below <= 0.0) return vec4(sky.rgb + rays * (v3(1.0) - sky.rgb), 1.0);
  vec3 sea = ToDisplay(SeaColor(vec3(rd.x, min(rd.y, -pixelAngle * 0.5), rd.z), pixel));
  vec3 c = mix(sky.rgb, sea, below);
  return vec4(c + rays * (v3(1.0) - c), 1.0);
}
