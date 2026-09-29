# Sarasa Gothic K에서 SAIUN이 쓰는 글자만 남긴 글꼴을 만든다 (2026-09-29).
#
# 원본은 굵기 하나가 약 24 MB(한중일 한자 전부)라, 앱이 보여 주는 고정 문구의 글자와
# 사용자가 입력하는 글(할 일, 방해 앱 이름)에 쓰일 한글 완성형·가나·영문·기호만 남긴다.
# 입력한 한자처럼 여기 없는 글자는 Unity 쪽 대체 글꼴(Windows의 맑은 고딕·Yu Gothic)이 그린다.
#
# 원본: https://github.com/be5invis/Sarasa-Gothic 릴리스의 SarasaGothicK-TTF-<버전>.7z
# 라이선스: SIL Open Font License 1.1. 글자를 추리는 것은 수정에 해당하므로 결과물도 OFL이며,
#           예약 이름 "Source"는 쓰지 않는다(Sarasa 이름은 예약되지 않았다). 라이선스 전문을 함께 둔다.
#
# 쓰는 법(fonttools 필요: python -m pip install --user fonttools):
#   python Tools/Fonts/subset_sarasa.py <원본 TTF 폴더> <LICENSE 파일>
# 고정 문구가 바뀌면 다시 돌리고, Unity에서 SAIUN/Setup Main Scene으로 글꼴 에셋을 새로 만든다.

import os
import re
import sys

from fontTools import subset

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SOURCE_DIRS = [os.path.join(ROOT, 'Assets', '_SAIUN', 'Scripts'), os.path.join(ROOT, 'Assets', '_SAIUN', 'Editor')]
ASSET_DIRS = [os.path.join(ROOT, 'Assets', '_SAIUN')]
OUT_DIR = os.path.join(ROOT, 'Assets', '_SAIUN', 'Art', 'Fonts', 'Sarasa')
WEIGHTS = ['Regular', 'Light']

# 입력용으로 통째로 남기는 범위
RANGES = [
    (0x0020, 0x007E),   # 기본 라틴(영문·숫자·기호)
    (0x00A0, 0x00FF),   # 라틴-1 보충(·, °, × 등)
    (0x2010, 0x2027),   # 대시·따옴표·줄임표
    (0x2030, 0x205E),   # 퍼밀·프라임 등
    (0x2190, 0x2199),   # 화살표
    (0x3000, 0x303F),   # 한중일 기호·구두점
    (0x3040, 0x309F),   # 히라가나
    (0x30A0, 0x30FF),   # 가타카나
    (0x3131, 0x318E),   # 한글 호환 자모(입력 중 조합 글자)
    (0xAC00, 0xD7A3),   # 한글 완성형 11,172자
    (0xFF01, 0xFF60),   # 전각 영숫자·기호
]

STRING = re.compile(r'@?\$?"((?:[^"\\\n]|\\.)*)"')
ESCAPE = re.compile(r'\\u([0-9a-fA-F]{4})')


def collect():
    chars = set()
    # C# 문자열 상수(주석은 빼고 따옴표 안만)
    for base in SOURCE_DIRS:
        for folder, _, files in os.walk(base):
            for name in files:
                if not name.endswith('.cs') or name.endswith('Test.cs'):
                    continue
                text = open(os.path.join(folder, name), encoding='utf-8').read()
                for match in STRING.finditer(text):
                    chars.update(match.group(1))
    # 씬·프리팹·에셋에 저장된 글(TMP 문구, 작물 이름 등). YAML은 \uXXXX로도 적는다.
    for base in ASSET_DIRS:
        for folder, _, files in os.walk(base):
            for name in files:
                if not name.endswith(('.unity', '.prefab', '.asset')):
                    continue
                path = os.path.join(folder, name)
                if os.path.getsize(path) > 20 * 1024 * 1024:
                    continue
                text = open(path, encoding='utf-8', errors='ignore').read()
                chars.update(c for c in text if ord(c) > 0x7E)
                chars.update(chr(int(h, 16)) for h in ESCAPE.findall(text))
    return {c for c in chars if c.isprintable() and not c.isspace() or c == ' '}


def main():
    if len(sys.argv) < 3:
        print(__doc__ or 'usage: subset_sarasa.py <source ttf dir> <license file>')
        sys.exit(1)
    source_dir, license_file = sys.argv[1], sys.argv[2]
    os.makedirs(OUT_DIR, exist_ok=True)

    fixed = collect()
    codes = set(ord(c) for c in fixed)
    for lo, hi in RANGES:
        codes.update(range(lo, hi + 1))
    extra = sorted(c for c in fixed if not any(lo <= ord(c) <= hi for lo, hi in RANGES))
    with open(os.path.join(OUT_DIR, 'charset-extra.txt'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('# 입력용 범위 밖에서 고정 문구가 쓰는 글자(subset_sarasa.py가 모은다)\n')
        f.write(''.join(extra) + '\n')

    for weight in WEIGHTS:
        options = subset.Options()
        options.layout_features = ['*']
        options.name_IDs = ['*']
        options.name_languages = ['*']
        options.notdef_outline = True
        options.hinting = False
        font = subset.load_font(os.path.join(source_dir, f'SarasaGothicK-{weight}.ttf'), options)
        subsetter = subset.Subsetter(options)
        subsetter.populate(unicodes=codes)
        subsetter.subset(font)
        out = os.path.join(OUT_DIR, f'SarasaGothicK-{weight}.ttf')
        subset.save_font(font, out, options)
        print(weight, os.path.getsize(out) // 1024, 'KB')

    license_text = open(license_file, encoding='utf-8').read()
    with open(os.path.join(OUT_DIR, 'OFL.txt'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(license_text)
    print('extra glyphs from fixed text:', len(extra))


if __name__ == '__main__':
    main()
