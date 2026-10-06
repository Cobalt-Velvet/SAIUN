// ==== 보는 눈 (하늘 패스와 바다 패스가 같이 쓴다) ====
// 눈은 지평선에서 _View.x(도)만큼 올려다보고, 초점은 _View.y(화면 높이 1 기준), 오른쪽으로 _View.z(도)만큼 돌아 있다.
// 떠 있는 카드는 14°·0.95·0°(세로 화각 약 55°). 화면 세로 전체를 채우는 사이드바는 화소당 각도를 같게 두고 화각을 넓히며,
// 폭이 좁아진 만큼 조금 돌아 탑·윤슬이 창 오른쪽 가장자리에서 카드와 같은 거리에 선다(SkyView·ViewRig가 정한다).
// uv는 화면 가운데가 0이고 세로 -0.5~0.5, 가로는 화면 비율만큼이다.
vec3 CameraRay(vec2 uv) {
  float p = radians(_View.x);
  float y = radians(_View.z);
  vec3 rd = normalize(vec3(uv.x, uv.y, _View.y));
  rd = vec3(rd.x, rd.y * cos(p) + rd.z * sin(p), -rd.y * sin(p) + rd.z * cos(p));
  return vec3(rd.x * cos(y) + rd.z * sin(y), rd.y, -rd.x * sin(y) + rd.z * cos(y));
}

// 방향이 화면에 맺히는 uv(CameraRay를 거꾸로). 눈 뒤를 보는 방향은 아주 멀리 보낸다.
vec2 CameraUv(vec3 r) {
  float p = radians(_View.x);
  float y = radians(_View.z);
  r = vec3(r.x * cos(y) - r.z * sin(y), r.y, r.x * sin(y) + r.z * cos(y));
  vec3 c = vec3(r.x, r.y * cos(p) - r.z * sin(p), r.y * sin(p) + r.z * cos(p));
  return c.xy / max(c.z, 1e-3) * _View.y;
}
