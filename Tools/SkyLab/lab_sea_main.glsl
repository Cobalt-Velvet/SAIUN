void main() {
  vec2 tuv = gl_FragCoord.xy / uRes;
  vec4 c = Present(tuv, gl_FragCoord.xy);
  fragColor = vec4(pow(max(c.rgb, vec3(0.0)), vec3(1.0 / 2.2)), 1.0);
}
