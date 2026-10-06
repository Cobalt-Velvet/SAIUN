// ==== 해와 달 (하늘 패스와 바다 패스가 같이 쓴다) ====
// _SunDir은 하늘 좌표의 해 방향, _Moon.xyz는 달 방향이고 _Moon.w는 달이 찬 정도(0 그믐 ~ 1 보름)다.
// _Extra.w는 밤(0~1): 박명이 끝난 뒤(해가 지평선 7.5° 아래) 해가 18° 아래까지 더 내려가며 하늘빛이 사그라진다.
// 밤이 절반을 넘으면 하늘·구름·바다를 비추는 빛이 해에서 달로 넘어간다. 그때는 해가 12° 아래라 해의 빛이 거의 없고
// 달빛도 막 오르기 시작하므로 이음매가 보이지 않는다. 달이 없거나 지평선 아래면 별과 밤하늘 빛만 남는다.

// 보름달이 비추는 세기(햇빛 1 기준). 실제(약 40만분의 1)보다 훨씬 밝게 잡았다: 밤이면 노출을 올려
// 눈이 어둠에 익은 것처럼 보이므로, 달빛 아래 하늘이 짙은 파랑으로, 구름이 은빛 가장자리로 읽히는 값이다.
STATIC const float MOON_LIGHT = 0.012;
// 달빛의 빛깔: 어둠에 익은 눈은 푸른빛에 예민해(푸르킨예 현상) 달빛 풍경이 푸르고 옅게 보인다.
STATIC const vec3 MOON_TINT = vec3(0.72, 0.84, 1.0);

vec3 SunDir() { return normalize(_SunDir.xyz + vec3(0.0, 1e-5, 0.0)); }

vec3 MoonDir() { return normalize(_Moon.xyz + vec3(0.0, 1e-5, 0.0)); }

float Night() { return _Extra.w; }

// 달이 하늘을 비추는 정도(0~1): 밤이 깊고, 달이 떠 있고, 차 있을수록 밝다.
float MoonShine() {
  return smoothstep(0.5, 1.0, Night()) * smoothstep(-0.01, 0.08, MoonDir().y) * pow(clamp(_Moon.w, 0.0, 1.0), 1.5);
}

// 하늘·구름·바다를 비추는 빛의 방향과 세기·빛깔(햇빛 1 기준)
bool MoonLights() { return Night() > 0.5; }
vec3 LightDir() { return MoonLights() ? MoonDir() : SunDir(); }
vec3 LightScale() { return MoonLights() ? MOON_TINT * (MOON_LIGHT * MoonShine()) : vec3(1.0, 1.0, 1.0); }
