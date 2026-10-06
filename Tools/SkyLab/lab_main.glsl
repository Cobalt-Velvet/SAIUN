void main() {
  vec2 uv = (gl_FragCoord.xy - 0.5 * uRes) / uRes.y;
  vec4 c = Shade(uv, gl_FragCoord.xy);
  fragColor = vec4(pow(max(c.rgb, vec3(0.0)), vec3(1.0 / 2.2)), c.a);
}
