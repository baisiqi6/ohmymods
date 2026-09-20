"""SUPERSEDED DRAFT: user rejected written signs; use prepare-shop-flags.py.
Rasterized once during asset preparation; game never generates text textures.
"""
raise SystemExit('Written signs were rejected; use prepare-shop-flags.py instead.')
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import json

task=Path(__file__).resolve().parent
root=task.parents[3]
width,height=32,18
font=ImageFont.truetype('C:/Windows/Fonts/simsun.ttc',12)
states=[('暂无',(135,135,122)),('有货',(183,206,119)),('占位',(224,170,111)),('待认',(229,194,95))]
sheet=Image.new('RGBA',(width*8,height))
for side in range(2):
    for state,(label,colour) in enumerate(states):
        tile=Image.new('RGBA',(width,height))
        draw=ImageDraw.Draw(tile)
        draw.rectangle((0,1,31,17),fill=(47,32,26,255))
        draw.rectangle((0,1,31,16),outline=(113,77,49,255))
        draw.line((1,2,30,2),fill=(142,98,57,255))
        # Direction identifies the seat independently of colour.
        draw.line((2,9,5,9),fill=colour+(255,))
        if side==0:
            draw.point((3,8),fill=colour+(255,)); draw.point((3,10),fill=colour+(255,))
            draw.point((4,7),fill=colour+(255,)); draw.point((4,11),fill=colour+(255,))
        else:
            draw.point((4,8),fill=colour+(255,)); draw.point((4,10),fill=colour+(255,))
            draw.point((3,7),fill=colour+(255,)); draw.point((3,11),fill=colour+(255,))
        mask=Image.new('L',(24,14)); md=ImageDraw.Draw(mask)
        md.text((0,0),label,font=font,fill=255,anchor='lt',stroke_width=0)
        mask=mask.point(lambda v:255 if v>=96 else 0)
        ink=Image.new('RGBA',mask.size,colour+(255,)); ink.putalpha(mask)
        tile.alpha_composite(ink,(7,3))
        sheet.alpha_composite(tile,((side*4+state)*width,0))
path=root/'il2cpp/Assets/HeroShopSeats.png';sheet.save(path)
sheet.resize((width*8*4,height*4),Image.Resampling.NEAREST).save(task/'hero-seat-signs-preview.png')
body=Image.open(root/'il2cpp/Assets/HeroShop.png').crop((0,0,128,80))
# Same positions used by the renderer: +/-17.5 native pixels, local y0.8125.
body.alpha_composite(sheet.crop((32,0,64,18)),(30,43))
body.alpha_composite(sheet.crop((192,0,224,18)),(65,43))
body.resize((768,480),Image.Resampling.NEAREST).save(task/'hero-shop-stock-preview.png')
receipt={'sheet':[256,18],'cell':[32,18],'ppu':32,'sides':['left','right'],
         'states':[s[0] for s in states],'alpha':sorted(set(sheet.getchannel('A').tobytes())),
         'allFramesDistinct':len({sheet.crop((i*32,0,(i+1)*32,18)).tobytes() for i in range(8)})==8,
         'rendering':'baked bitmap glyphs; no runtime font loading; Point filter'}
(task/'receipts/seat-art.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2),encoding='utf8')
assert receipt['allFramesDistinct'] and receipt['alpha']==[0,255]
print(json.dumps(receipt,ensure_ascii=False))
