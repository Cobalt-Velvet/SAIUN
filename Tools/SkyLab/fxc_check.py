# Sky.shader 패스 0을 D3DCompile로 직접 컴파일해 경고(초기화되지 않은 변수 등)를 본다.
import ctypes, os, re, sys
from ctypes import wintypes

SHADER = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Assets', '_SAIUN', 'Art', 'Shaders', 'Sky.shader'))
DLL = r'C:/Program Files/Unity/Hub/Editor/6000.3.11f1/Editor/Data/Tools/D3DCompiler_47.dll'

src = open(SHADER, encoding='utf-8').read()
blocks = re.findall(r'CGPROGRAM(.*?)ENDCG', src, re.S)
which = int(sys.argv[1]) if len(sys.argv) > 1 else 0
entry = {0: 'Fragment', 1: 'Compose', 2: 'Mix'}[which]
body = blocks[which]
body = re.sub(r'#pragma[^\n]*', '', body)
stub = '''
struct appdata_img { float4 vertex : POSITION; float2 texcoord : TEXCOORD0; };
struct v2f_img { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
'''
body = body.replace('#include "UnityCG.cginc"', stub)
code = body.encode('utf-8')

d3d = ctypes.WinDLL(DLL)
D3DCompile = d3d.D3DCompile
D3DCompile.restype = ctypes.c_long


class ID3DBlob(ctypes.Structure):
    pass


def blob_text(pblob):
    if not pblob:
        return ''
    vtbl = ctypes.cast(pblob, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p))).contents
    GetBufferPointer = ctypes.WINFUNCTYPE(ctypes.c_void_p, ctypes.c_void_p)(vtbl[3])
    GetBufferSize = ctypes.WINFUNCTYPE(ctypes.c_size_t, ctypes.c_void_p)(vtbl[4])
    ptr = GetBufferPointer(pblob)
    size = GetBufferSize(pblob)
    return ctypes.string_at(ptr, size).decode('utf-8', 'replace')


code_blob = ctypes.c_void_p()
err_blob = ctypes.c_void_p()
D3DCOMPILE_ENABLE_BACKWARDS_COMPATIBILITY = 1 << 12
D3DCOMPILE_OPTIMIZATION_LEVEL3 = 1 << 15
hr = D3DCompile(code, len(code), b'sky_pass', None, None, entry.encode(), b'ps_5_0',
                D3DCOMPILE_ENABLE_BACKWARDS_COMPATIBILITY | D3DCOMPILE_OPTIMIZATION_LEVEL3, 0,
                ctypes.byref(code_blob), ctypes.byref(err_blob))
print('hr', hex(hr & 0xffffffff))
text = blob_text(err_blob)
lines = text.splitlines()
print(len(lines), 'messages')
seen = set()
for l in lines:
    m = re.search(r'\((\d+),\d+(?:-\d+)?\): (warning|error) (X\d+): (.*)', l)
    if not m:
        print(l[:200]); continue
    key = (m.group(3), m.group(4))
    if key in seen: continue
    seen.add(key)
    ln = int(m.group(1))
    print(m.group(2), m.group(3), 'line', ln, m.group(4)[:150])
    src_lines = body.splitlines()
    if 0 < ln <= len(src_lines):
        print('    >', src_lines[ln - 1].strip()[:160])
