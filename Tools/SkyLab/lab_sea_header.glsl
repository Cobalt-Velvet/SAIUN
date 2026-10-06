#version 300 es
precision highp float;
precision highp int;
precision highp sampler3D;
uniform vec2 uRes;
uniform sampler2D uSky;
uniform sampler3D _Noise;
uniform vec4 _View, _SunDir, _Moon, _Extra, _Low, _SkySize, _SeaWind, _SunColor;
uniform float _Storm, _Exposure, _SeaTime;
out vec4 fragColor;
#define STATIC
vec4 N(vec3 p) { return texture(_Noise, p); }
vec3 v3(float x) { return vec3(x); }
vec4 SkySample(vec2 t) { vec4 c = texture(uSky, t); return vec4(pow(c.rgb, vec3(2.2)), c.a); }
vec4 SkyQuick(vec2 t) { vec4 c = texture(uSky, t); return vec4(pow(c.rgb, vec3(2.2)), c.a); }

