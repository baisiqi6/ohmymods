"""Turn the approved generated concept into bounded game pixel assets.
User authorized local scripts for cleaning backgrounds and preparing game assets.
Building silhouette is explicitly masked; no inferred chroma-key of dark wood.
"""
from pathlib import Path
from PIL import Image, ImageDraw
import json

task = Path(__file__).resolve().parent
root = task.parents[3]
source = Image.open(task / 'hero-shop-concept-01.png').convert('RGBA')
mask = Image.new('L', source.size)
outline = [(358,244),(388,244),(422,278),(638,278),(638,260),(894,260),
 (894,278),(1110,278),(1146,244),(1176,244),(1176,276),(1162,308),
 (1186,348),(1210,377),(1234,399),(1268,412),(1268,443),(1234,445),
 (1234,479),(1250,469),(1282,463),(1294,474),(1294,493),(1271,493),
 (1267,522),(1305,535),(1305,582),(1263,582),(1263,543),(1257,523),
 (1245,508),(1234,516),(1234,681),(1247,681),(1247,724),(1263,724),
 (1263,787),(313,787),(313,761),(331,761),(331,729),(350,729),
 (350,692),(357,692),(357,486),(332,464),(320,442),(269,442),
 (269,413),(298,413),(321,390),(340,365),(358,331),(371,310),
 (358,277)]
ImageDraw.Draw(mask).polygon(outline, fill=255)
source.putalpha(mask)
crop = source.crop((256,224,1344,800)).resize((128,68), Image.Resampling.NEAREST)
# Flat palette and binary alpha, point sampled at runtime. Dithering would shimmer.
alpha = crop.getchannel('A').point(lambda v: 255 if v >= 128 else 0)
crop = crop.convert('RGB').quantize(colors=24, method=Image.Quantize.MEDIANCUT,
                                  dither=Image.Dither.NONE).convert('RGBA')
crop.putalpha(alpha)
body = Image.new('RGBA',(128,80)); body.alpha_composite(crop,(0,12))
# Keep the gold emblem legible after palette reduction, without bloom.
for y in range(19,42):
    for x in range(48,75):
        r,g,b,a = body.getpixel((x,y))
        if a and r > 90 and r > g*1.08 and g > b*1.12:
            body.putpixel((x,y),(204,157,76,255))

# Move only the red cloth pixels by one native pixel below their fixed root.
# Four discrete frames, no geometry/whole-building motion or per-frame textures.
flags = Image.new('RGBA',body.size)
background = body.copy()
for x0,y0,x1,y1 in ((22,32,32,68),(89,32,99,68)):
    for y in range(y0,y1):
        for x in range(x0,x1):
            r,g,b,a = body.getpixel((x,y))
            if a and r > g*1.55 and r > b*1.3 and r>60:
                flags.putpixel((x,y),(r,g,b,a))
                background.putpixel((x,y),(55,36,26,255))
sheet = Image.new('RGBA',(512,80))
for frame, direction in enumerate((0,1,0,-1)):
    current = background.copy()
    for y in range(80):
        shift = direction if y >= 48 else 0
        strip = flags.crop((0,y,128,y+1))
        current.alpha_composite(strip,(shift,y))
    sheet.alpha_composite(current,(frame*128,0))
asset = root/'il2cpp/Assets/HeroShop.png'
sheet.save(asset)
body.resize((768,480),Image.Resampling.NEAREST).save(task/'hero-shop-pixel-preview.png')
frames = [sheet.crop((x*128,0,(x+1)*128,80)).resize((512,320),Image.Resampling.NEAREST)
          for x in range(4)]
frames[0].save(task/'hero-shop-flags-preview.gif',save_all=True,append_images=frames[1:],
               duration=220,loop=0,disposal=2)
stats = {'source': 'hero-shop-concept-01.png', 'asset':str(asset),
         'sheet':[512,80], 'frame':[128,80], 'frames':4, 'ppu':32,
         'pivot':[0.5,0.025], 'alpha':sorted(set(sheet.getchannel('A').tobytes())),
         'rgba_colors':len(sheet.getcolors(100000)),
         'note':'Concept cleaned and pixel-quantized; native game scale and appearance not yet tested.'}
(task/'art-receipt.json').write_text(json.dumps(stats,indent=2),encoding='utf8')
print(json.dumps(stats))
