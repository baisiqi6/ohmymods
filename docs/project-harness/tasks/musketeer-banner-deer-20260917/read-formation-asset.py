from pathlib import Path
import UnityPy,struct,json
env=UnityPy.load('E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/KingdomTwoCrowns_Data/resources.assets')
rows=[]
for o in env.objects:
 if o.type.name!='GameObject':continue
 d=o.read_typetree()
 if d['m_Name']!='Player':continue
 for c in d['m_Component']:
  r=o.assets_file.objects[c['component']['m_PathID']]
  if r.type.name!='MonoBehaviour':continue
  obj=r.read(check_read=False)
  if obj.m_Script.deref().read().m_ClassName!='Formation':continue
  raw=r.get_raw_data();name_len=struct.unpack_from('<i',raw,28)[0];offset=32+((name_len+3)//4)*4
  kind,n=struct.unpack_from('<ii',raw,offset);assert 0<n<100;offset+=8
  spacing=struct.unpack_from('<'+'f'*n,raw,offset);offset+=n*4
  cnt=struct.unpack_from('<i',raw,offset)[0];offset+=4;assert 0<cnt<100
  types=struct.unpack_from('<'+'i'*cnt,raw,offset);offset+=cnt*4
  start,speed,cooldown=struct.unpack_from('<fff',raw,offset)
  rows.append(dict(go=o.path_id,component=r.path_id,formationType=kind,spacing=spacing,unitTypes=types,startOffset=start,overrideMoveSpeed=speed,overrideShootCooldown=cooldown,rawHex=raw.hex()))
Path(__file__).with_name('player-formation-asset.json').write_text(json.dumps(rows,indent=2),encoding='utf-8')
print(json.dumps([{k:v for k,v in r.items() if k!='rawHex'} for r in rows]))
