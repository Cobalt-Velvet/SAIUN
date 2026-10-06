# out/의 PNG를 variants.txt 순서대로 한 장에 모은다.
import sys, os
from PIL import Image, ImageDraw
d = os.path.dirname(os.path.abspath(__file__))
names = [l.split()[0] for l in open(os.path.join(d, 'variants.txt'), encoding='utf-8') if l.strip() and not l.startswith('#')]
cols = int(sys.argv[2]) if len(sys.argv) > 2 else 3
scale = float(sys.argv[3]) if len(sys.argv) > 3 else 0.6
w, h = int(480 * scale), int(680 * scale)
rows = (len(names) + cols - 1) // cols
sheet = Image.new('RGB', (cols * (w + 4), rows * (h + 18)), (0, 0, 0))
dr = ImageDraw.Draw(sheet)
for i, n in enumerate(names):
    im = Image.open(os.path.join(d, 'out', n + '.png')).convert('RGB').resize((w, h), Image.LANCZOS)
    x, y = (i % cols) * (w + 4), (i // cols) * (h + 18)
    sheet.paste(im, (x, y + 18))
    dr.text((x + 3, y + 3), n, fill=(255, 255, 255))
sheet.save(os.path.join(d, 'out', sys.argv[1]))
print('sheet', sys.argv[1])
