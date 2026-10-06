# 공통 본문에 시안용 머리·꼬리를 붙여 sky.html을 만든다(하늘 패스 + 바다 패스).
import os
d=os.path.dirname(os.path.abspath(__file__))
r=lambda n: open(os.path.join(d,n),encoding='utf-8').read()
fs=r('lab_header.glsl')+'\n'+r('view_common.glsl')+'\n'+r('light_common.glsl')+'\n'+r('sky_common.glsl')+'\n'+r('lab_main.glsl')
sea=r('lab_sea_header.glsl')+'\n'+r('view_common.glsl')+'\n'+r('light_common.glsl')+'\n'+r('sea_common.glsl')+'\n'+r('lab_sea_main.glsl')
open(os.path.join(d,'fs.glsl'),'w',encoding='utf-8').write(fs)
open(os.path.join(d,'sea_fs.glsl'),'w',encoding='utf-8').write(sea)
t=r('sky_template.html')
open(os.path.join(d,'sky.html'),'w',encoding='utf-8').write(t.replace('@@FS@@',fs).replace('@@SEA@@',sea))
print('built')
