"""User-approved local pixel editing: neck wrap joining the two dynamic scarf tails.
Only the neck/shoulder patch changes. Existing poses, feet, hood, bow and timing remain.
"""
from pathlib import Path
from PIL import Image, ImageDraw
import json, hashlib

root=Path(__file__).resolve().parent
atlas=Image.open(root/'source-atlas.png').convert('RGBA')
meta=json.loads((root/'source-atlas.json').read_text(encoding='utf-8-sig'))
original=atlas.copy()
# Explicit pixel folds: no blur, no subpixel gradient, no change to face or bow.
colors={'d':(100,18,29,255),'r':(190,28,36,255),'s':(137,18,29,255),'l':(228,57,35,255)}
patch={
 15:(28,'ddddddd'),
 16:(26,'srrllllrs'),
 17:(27,'rssssr'),
 18:(27,'dsr'),
}
changes=[]
for row in meta['frames']:
 i=row['index']; ox=i%8*48;oy=i//8*32
 frame=original.crop((ox,oy,ox+48,oy+32))
 before=frame.copy()
 lift=row['torso_lift_pixels']
 allowed=set()
 for y,(x,pattern) in patch.items():
  for j,ch in enumerate(pattern):
   point=(x+j,y-lift)
   allowed.add(point)
   frame.putpixel(point,colors[ch])
 changed=[(x,y) for y in range(32) for x in range(48) if frame.getpixel((x,y))!=before.getpixel((x,y))]
 assert changed and set(changed)<=allowed
 assert frame.getbbox()==before.getbbox()
 assert set(frame.getchannel('A').tobytes())<={0,255}
 atlas.paste(frame,(ox,oy))
 frame.save(root/f'frame-{i:02}.png')
 row['sha256']=hashlib.sha256(frame.tobytes()).hexdigest()
 changes.append(dict(index=i,changed_pixels=len(changed),allowed_neck_patch_only=True,pivot_and_bbox_unchanged=True))
atlas.save(root/'HeroArcherAtlas.png')
meta['neck_wrap']='pixel shoulder connection, 4 flat red tones; dynamic tails remain separate geometry'
(root/'atlas.json').write_text(json.dumps(meta,indent=2),encoding='utf-8')
(root/'pixel-validation.json').write_text(json.dumps(dict(frames=31,changes=changes,
 binary_alpha=True,unchanged_outside_neck=True,atlas_sha256=hashlib.sha256((root/'HeroArcherAtlas.png').read_bytes()).hexdigest()),indent=2),encoding='utf-8')
board=Image.new('RGB',(1000,330),(39,44,49));d=ImageDraw.Draw(board)
for n,i in enumerate((0,17,24)):
 for rowno,(label,src) in enumerate((('Before',original),('Scarf neck wrap',atlas))):
  im=src.crop((i%8*48,i//8*32,i%8*48+48,i//8*32+32)).resize((240,160),Image.Resampling.NEAREST)
  x=n*330;y=rowno*165
  d.text((x+3,y+3),f'{label}: frame {i}',fill='white')
  board.paste(im,(x+55,y+3),im)
board.save(root/'neck-comparison.png')
print('31 frames updated only in the lifted neck/shoulder patch; pixel bounds and all other pixels preserved.')
