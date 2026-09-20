"""Knight-style empty hooks, occupied banners and torn banners. No text glyphs."""
from pathlib import Path
from PIL import Image, ImageDraw
import shutil, json
task=Path(__file__).resolve().parent
root=task.parents[3]
assets=root/'il2cpp/Assets'
source=task/'hero-shop-decorative-flags-before-status.png'
if not source.exists(): shutil.copy2(assets/'HeroShop.png',source)
original=Image.open(source).convert('RGBA').crop((0,0,128,80))
bare=original.copy()
# Move the two former decorative pennants into the actual occupancy renderers.
for x0,x1,sample in ((21,34,35),(88,102,87)):
    for y in range(33,69):
        for x in range(x0,x1): bare.putpixel((x,y),original.getpixel((sample,y)))
base=Image.new('RGBA',(512,80))
for i in range(4): base.alpha_composite(bare,(i*128,0))
base.save(assets/'HeroShop.png')
cloth=Image.new('RGBA',(16,40))
for y in range(32,69):
    for x in range(22,34):
        r,g,b,a=original.getpixel((x,y))
        if a and r>g*1.55 and r>b*1.3 and r>60:
            cloth.putpixel((x-20,y-32),(r,g,b,255))
d=ImageDraw.Draw(cloth); gold=(220,176,82,255)
d.line([(6,10),(8,11),(9,13),(10,15),(10,17),(9,19),(8,21),(6,22)],fill=gold,width=1)
d.line((6,10,6,22),fill=(179,149,91,255),width=1)
d.line((4,16,11,16),fill=gold,width=1)
broken=cloth.copy()
for y in range(40):
    for x in range(16):
        r,g,b,a=broken.getpixel((x,y))
        if y>26+(x%4) or (6<=x<=8 and y>=22): broken.putpixel((x,y),(0,0,0,0))
        elif a: broken.putpixel((x,y),(int(r*.8),int(g*.8),int(b*.8),a))
# Unavailable and available both show the empty hook; native coin slots express CanPay.
sheet=Image.new('RGBA',(80,160))
for frame,direction in enumerate((0,1,0,-1)):
    for state in (2,3,4):
        flag=broken if state==4 else cloth
        current=Image.new('RGBA',(16,40))
        for y in range(40):
            shift=direction if state==2 and y>=10 else 0
            current.alpha_composite(flag.crop((0,y,16,y+1)),(shift,y))
        sheet.alpha_composite(current,(state*16,frame*40))
sheet.save(assets/'HeroShopSeats.png')
preview=bare.copy()
preview.alpha_composite(sheet.crop((32,0,48,40)),(20,32))
preview.alpha_composite(sheet.crop((64,0,80,40)),(86,32))
preview.resize((768,480),Image.Resampling.NEAREST).save(task/'hero-shop-flags-preview.png')
bare.resize((768,480),Image.Resampling.NEAREST).save(task/'hero-shop-empty-preview.png')
receipt={'sheet':[80,160],'cell':[16,40],'columns':5,'rows':4,'ppu':32,
         'states':['empty-disabled','empty-payable','occupied-banner','reserved-banner','torn-banner'],
         'labels':False,'runtimeFonts':False,'alpha':sorted(set(sheet.getchannel('A').tobytes())),
         'nativeReference':'PayableShield Empty -> Taken -> Broken; direct training has no pending shield pickup state',
         'decorationMovedToOccupancy':True}
(task/'receipts/seat-art.json').write_text(json.dumps(receipt,indent=2),encoding='utf8')
assert receipt['alpha']==[0,255]
print(json.dumps(receipt))
