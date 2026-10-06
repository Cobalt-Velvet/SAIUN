# 공통 본문(sky_common.glsl)을 HLSL로 옮겨 Unity 셰이더(Sky.shader)를 만든다.
import os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
# 저장소 안의 Unity 셰이더 자리(이 폴더 기준 ../../Assets/...)
OUT = os.path.normpath(os.path.join(HERE, '..', '..', 'Assets', '_SAIUN', 'Art', 'Shaders', 'Sky.shader'))


def split_args(s, start):
    """s[start]는 '('. 짝이 맞는 ')'까지의 인자 목록과 끝 위치를 돌려준다."""
    depth = 0
    args = []
    cur = ''
    i = start
    while i < len(s):
        ch = s[i]
        if ch == '(':
            depth += 1
            if depth > 1:
                cur += ch
        elif ch == ')':
            depth -= 1
            if depth == 0:
                args.append(cur)
                return args, i
            cur += ch
        elif ch == ',' and depth == 1:
            args.append(cur)
            cur = ''
        else:
            cur += ch
        i += 1
    raise ValueError('unbalanced parentheses')


def convert(src):
    out = src
    # 두 인자 atan → atan2
    res = ''
    i = 0
    for m in re.finditer(r'\batan\(', out):
        pass
    while True:
        m = re.search(r'\batan\(', out[i:])
        if not m:
            res += out[i:]
            break
        a = i + m.start()
        paren = a + len('atan')
        args, end = split_args(out, paren)
        res += out[i:a]
        res += ('atan2(' if len(args) == 2 else 'atan(') + ','.join(args) + ')'
        i = end + 1
    out = res
    # 한 인자 벡터 생성자는 HLSL에 없다. 공통 본문에서는 v3()를 써야 한다.
    for m in re.finditer(r'\bvec([234])\(', out):
        args, _ = split_args(out, m.end() - 1)
        if len(args) == 1:
            line = out.count('\n', 0, m.start()) + 1
            raise ValueError(f'single-argument vec{m.group(1)}() at line {line}')
    out = re.sub(r'\bvec([234])\b', r'float\1', out)
    out = re.sub(r'\bmix\(', 'lerp(', out)
    out = re.sub(r'\bfract\(', 'frac(', out)
    # GLSL mod()는 HLSL에 없고, fmod()는 음수에서 값이 달라 그대로 옮길 수 없다. fract()로 셈하게 한다.
    if re.search(r'\bmod\(', out):
        raise ValueError('mod() has no HLSL equivalent with the same sign rules; use fract()')
    # 반복문: 작은 고정 횟수는 풀고 나머지는 돌린다.
    def loop_attr(m):
        rest = out[m.end():]
        bound = re.match(r'\s*int\s+\w+\s*=\s*\d+\s*;\s*\w+\s*<\s*(\d+)', rest)
        # 반복 몸통(첫 중괄호 짝)에 무거운 밀도 함수가 있으면 풀지 않는다(셰이더가 지나치게 커진다).
        brace = rest.index('{')
        depth = 0
        for k in range(brace, len(rest)):
            if rest[k] == '{':
                depth += 1
            elif rest[k] == '}':
                depth -= 1
                if depth == 0:
                    break
        body = rest[brace:k]
        heavy = 'Density(' in body
        attr = '[unroll] ' if bound and int(bound.group(1)) <= 8 and not heavy else '[loop] '
        return m.group(1) + attr + 'for ('
    out = re.sub(r'(^[ \t]*)for \(', loop_attr, out, flags=re.M)
    # 들여쓰기: 두 칸 → 셰이더 안 네 칸 단위, 앞에 12칸
    lines = []
    for line in out.split('\n'):
        stripped = line.lstrip(' ')
        depth = (len(line) - len(stripped)) // 2
        lines.append(('            ' + '    ' * depth + stripped) if stripped else '')
    return '\n'.join(lines)


HEADER = r'''// 카드의 하늘: 대기 산란으로 셈한 하늘빛 위에 웅대적운 탑, 채운, 그리고 나머지 구름 모두를 그린다.
// 본문은 시안 실험실(WebGL)과 같은 소스에서 만든다. 보는 사람은 원점(km), +z가 앞, +x가 오른쪽, +y가 위.
//  - 하늘빛은 레일리·미 산란과 오존 흡수로 셈한다. 해 방향(_SunDir)은 정원 그림자를 만드는 해와 같은 방위라,
//    해가 움직이면 하늘빛(짙은 파랑, 금빛 지평선, 노을, 땅 그림자와 푸른 박명)이 따라 바뀐다.
//  - 웅대적운 탑은 부피로, 층을 이루는 구름(권운·권적운·권층운·고적운·고층운·층적운·층운·난층운)은 고도별 평면으로,
//    뭉게구름 떼는 낮은 층을 걸어서, 멀리 옆으로 누운 구름(렌즈구름·아치구름·물결구름·야광운)은 보는 방향으로 그린다.
//  - 만난 구름을 가까운 순서로 겹치고, 사이 공기가 빛을 더하고 덜어 멀수록 하늘에 잠긴다.
//  - 구름은 층마다 다른 바람에 흘러간다(_Drift 낮은 층·중층, _DriftHigh 높은 층): 높을수록 빠르고 방향이 조금씩 비껴 돈다.
//  - 위는 하늘, 지평선 아래는 바다와 모래밭이다. 바다는 보이는 장을 만드는 패스(패스 2)가 매 프레임 그린다.
// 패스 0(하늘): 무거우므로 한 번에 화소의 1/8만 그린다. 4×2 격자의 한 칸(_Phase 0~7)을 그 칸만 모은 작은 그림
//   (가로 1/4·세로 1/2, _TileSize)에 통째로 그린다.
//   출력은 톤매핑까지 마친 값이고, sRGB 렌더 텍스처에 쓰면 화면에서 그대로 보인다. 알파는 곧은 알파다.
// 패스 1(짜 맞추기): 여덟 칸의 작은 그림(_Tile0~7)을 큰 장의 화소 자리로 짜 맞춘다.
//   칸 그림은 그 칸 화소만 셈하고, 모든 그리기가 대상을 통째로 덮어써 지난 내용에 기대지 않는다.
// 패스 2(보이기): 다 그린 두 장(_PrevTex → _MainTex)을 _Blend만큼 섞고 잔 얼룩을 살짝 거른 뒤, 지평선 아래를 바다로 채운다.
//   바다는 섞은 하늘을 거울 방향으로 읽어 비추고, 윤슬·파도는 매 프레임(_SeaTime) 움직인다.
//   반쯤 새로 그린 장을 보이면 빗살이 지므로 다 그린 장끼리만 천천히 넘겨 보인다.
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
        _Extra ("Kelvin-Helmholtz, Twilight, Tower, Night", Vector) = (0, 0, 1, 0)
        _VeilPlace ("Iridescent Veil Place (azimuth, elevation, scale, pattern)", Vector) = (0, 0, 1, 0)
        _VeilForm ("Iridescent Veil Form (0 band, 1 wisp, 2 sun patch)", Float) = 0
        _SunDir ("Sun Direction (sky space)", Vector) = (0, 0.7, -0.7, 0)
        _Moon ("Moon Direction (sky space) and lit fraction", Vector) = (0, -1, 0, 0)
        _AltHigh ("Altitude km: Cirrus, Cirrocumulus, Cirrostratus", Vector) = (9, 7.6, 8.4, 0)
        _AltLow ("Altitude km: Altocumulus, Altostratus, Stratocumulus, Cumulus Base", Vector) = (4.2, 4.8, 1.7, 1.3)
        _Drift ("Wind Drift km (low xy, mid zw)", Vector) = (0, 0, 0, 0)
        _DriftHigh ("Wind Drift km (high xy, hole zw)", Vector) = (0, 0, 0, 0)
        _SeaTime ("Sea Time (s)", Float) = 0
        _SeaWind ("Sea Wind (direction xy, amount z)", Vector) = (0.7, 0.7, 0.5, 0)
        _SunColor ("Sunlight At Sea Level", Vector) = (1, 1, 1, 0)
        _View ("View (tilt up degrees, focal, yaw degrees)", Vector) = (14, 0.95, 0, 0)
        _SkyTime ("Evolve Time", Float) = 0
        _Seed ("Shape Seed", Float) = 3
        _Exposure ("Exposure", Float) = 1
        _Phase ("Interleave Phase", Float) = 0
        _SkySize ("Target Size (px)", Vector) = (480, 680, 0, 0)
        _TileSize ("Interleave Tile Size (px)", Vector) = (120, 340, 0, 0)
        _Tile0 ("Tile 0", 2D) = "black" {}
        _Tile1 ("Tile 1", 2D) = "black" {}
        _Tile2 ("Tile 2", 2D) = "black" {}
        _Tile3 ("Tile 3", 2D) = "black" {}
        _Tile4 ("Tile 4", 2D) = "black" {}
        _Tile5 ("Tile 5", 2D) = "black" {}
        _Tile6 ("Tile 6", 2D) = "black" {}
        _Tile7 ("Tile 7", 2D) = "black" {}
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
            float4 _VeilPlace;
            float _VeilForm;
            float4 _SunDir;
            float4 _Moon;
            float4 _AltHigh;
            float4 _AltLow;
            float4 _View;
            float4 _Drift;
            float4 _DriftHigh;
            float _SkyTime;
            float _Seed;
            float _Exposure;
            float _Phase;
            float4 _SkySize;
            float4 _TileSize;

            #define STATIC static

            float4 N(float3 p) { return tex3Dlod(_Noise, float4(p, 0.0)); }
            float3 v3(float x) { return float3(x, x, x); }

'''

FOOTER = r'''

            float4 Fragment(v2f_img input) : SV_Target
            {
                // 칸 그림의 화소 하나가 큰 장의 화소 (4i + 칸 가로, 2j + 칸 세로)다.
                float2 cell = float2(fmod(_Phase, 4.0), floor(_Phase / 4.0));
                float2 pixel = floor(input.uv * _TileSize.xy) * float2(4.0, 2.0) + cell;
                float2 uv = ((pixel + 0.5) / _SkySize.xy - 0.5) * float2(_SkySize.x / _SkySize.y, 1.0);
                return Shade(uv, pixel);
            }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Compose
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _Tile0;
            sampler2D _Tile1;
            sampler2D _Tile2;
            sampler2D _Tile3;
            sampler2D _Tile4;
            sampler2D _Tile5;
            sampler2D _Tile6;
            sampler2D _Tile7;
            float4 _SkySize;
            float4 _TileSize;

            // 큰 장의 화소마다 그 칸의 작은 그림에서 제 화소를 꺼낸다(작은 그림은 점 거르기라 섞이지 않는다).
            float4 Compose(v2f_img input) : SV_Target
            {
                float2 pixel = floor(input.uv * _SkySize.xy);
                float k = fmod(pixel.x, 4.0) + fmod(pixel.y, 2.0) * 4.0;
                float4 at = float4((floor(pixel / float2(4.0, 2.0)) + 0.5) / _TileSize.xy, 0.0, 0.0);
                if (k < 0.5) return tex2Dlod(_Tile0, at);
                if (k < 1.5) return tex2Dlod(_Tile1, at);
                if (k < 2.5) return tex2Dlod(_Tile2, at);
                if (k < 3.5) return tex2Dlod(_Tile3, at);
                if (k < 4.5) return tex2Dlod(_Tile4, at);
                if (k < 5.5) return tex2Dlod(_Tile5, at);
                if (k < 6.5) return tex2Dlod(_Tile6, at);
                return tex2Dlod(_Tile7, at);
            }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Mix
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _PrevTex;
            float _Blend;
            sampler3D _Noise;
            float4 _SunDir;
            float4 _Moon;
            float4 _Extra;
            float4 _View;
            float4 _SkySize;
            float4 _SeaWind;
            float4 _SunColor;
            float4 _Low;
            float _Storm;
            float _Exposure;
            float _SeaTime;

            #define STATIC static

            float4 N(float3 p) { return tex3Dlod(_Noise, float4(p, 0.0)); }
            float3 v3(float x) { return float3(x, x, x); }

            // 반 화소씩 비낀 네 점을 모은다(겹선형 거르기와 합쳐 3×3 천막 거르기). 화소마다 흩뜨린 광선 시작점이 구름
            // 가장자리에 남기는 잔 얼룩을 누그러뜨린다. 알파를 곱해 모아야 구름 가장자리가 검게 번지지 않는다.
            float4 Gather(sampler2D sky, float2 uv)
            {
                float2 o = _MainTex_TexelSize.xy * 0.5;
                float4 a = tex2Dlod(sky, float4(uv + float2(-o.x, -o.y), 0.0, 0.0));
                float4 b = tex2Dlod(sky, float4(uv + float2(o.x, -o.y), 0.0, 0.0));
                float4 c = tex2Dlod(sky, float4(uv + float2(-o.x, o.y), 0.0, 0.0));
                float4 d = tex2Dlod(sky, float4(uv + float2(o.x, o.y), 0.0, 0.0));
                return (float4(a.rgb * a.a, a.a) + float4(b.rgb * b.a, b.a) + float4(c.rgb * c.a, c.a) + float4(d.rgb * d.a, d.a)) * 0.25;
            }

            // 빛살용 빠른 표본: 거르지 않고 한 점씩 읽는다(알파는 구름 너머로 트인 정도).
            float4 SkyQuick(float2 uv)
            {
                return lerp(tex2Dlod(_PrevTex, float4(uv, 0.0, 0.0)), tex2Dlod(_MainTex, float4(uv, 0.0, 0.0)), _Blend);
            }

            // 지난 장과 지금 장을 섞은 하늘(곧은 색·알파)
            float4 SkySample(float2 uv)
            {
                float4 p = lerp(Gather(_PrevTex, uv), Gather(_MainTex, uv), _Blend);
                return float4(p.rgb / max(p.a, 1e-4), p.a);
            }

@@SEA@@

            float4 Mix(v2f_img input) : SV_Target
            {
                return Present(input.uv, floor(input.uv * _SkySize.xy));
            }
            ENDCG
        }
    }

    Fallback Off
}
'''

if __name__ == '__main__':
    common = open(os.path.join(HERE, 'sky_common.glsl'), encoding='utf-8').read()
    view = open(os.path.join(HERE, 'view_common.glsl'), encoding='utf-8').read()
    view += chr(10) + open(os.path.join(HERE, 'light_common.glsl'), encoding='utf-8').read()
    sea = open(os.path.join(HERE, 'sea_common.glsl'), encoding='utf-8').read()
    # 시안 전용 머리글(파일 첫 설명)은 셰이더 머리글이 대신한다.
    body = common[common.index('STATIC const float PI'):]
    text = HEADER + convert(view + '\n' + body).rstrip() + FOOTER.replace('@@SEA@@', convert(view + '\n' + sea).rstrip())
    text = text.replace('\r\n', '\n').replace('\n', '\r\n')
    open(OUT, 'w', encoding='utf-8', newline='').write(text)
    print('wrote', OUT, len(text))
