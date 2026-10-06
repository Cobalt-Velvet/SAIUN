# 4×2 칸 가운데 일부만 다른 그림(지난 장이 남은 칸)이 있는지: 48×48 조각마다 칸별 고역 평균의 최대/최소 비
import numpy as np, os, sys
from PIL import Image
def stat(path, skip_top=0):
    a=np.asarray(Image.open(path).convert('L')).astype(float)
    best=(0,None)
    H,W=a.shape
    for y in range(skip_top,H-48,24):
        for x in range(0,W-48,24):
            b=a[y:y+48,x:x+48]
            hp=b-0.5*(np.roll(b,1,axis=1)+np.roll(b,-1,axis=1)); hp=hp[:,1:-1]
            cells=[]
            for yy in range(2):
                for j in range(4):
                    cells.append(np.abs(hp[((yy-y)%2)::2, ((j-(x+1))%4)::4]).mean())
            cells=np.array(cells)
            r=cells.max()/max(cells.min(),0.05)
            if cells.mean()>0.3 and r>best[0]: best=(round(float(r),2),(x,y))
    return best
for d in sys.argv[1:]:
    for f in sorted(os.listdir(d)):
        if f.endswith('.png'): print(d, f, stat(os.path.join(d,f), 0 if '_' in f[4:] else 150))
