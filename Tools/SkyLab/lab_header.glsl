#version 300 es
precision highp float;
precision highp int;
precision highp sampler3D;
uniform vec2 uRes;
uniform float _Progress, _Growth, _Storm, _Seed, _Cap, _SkyTime, _Exposure;
uniform vec4 _View, _High, _Mid, _Low, _Special, _Extra, _VeilPlace, _SunDir, _Moon, _Drift, _DriftHigh, _AltHigh, _AltLow;
uniform float _VeilForm;
uniform sampler3D _Noise;
out vec4 fragColor;
#define STATIC
vec4 N(vec3 p) { return texture(_Noise, p); }
vec3 v3(float x) { return vec3(x); }
