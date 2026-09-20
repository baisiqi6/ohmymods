from pathlib import Path
import json,UnityPy
task=Path(__file__).parent
env=UnityPy.load('E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/KingdomTwoCrowns_Data/resources.assets')
def inspect(go,depth=0):
 d=go.read_typetree();row=dict(id=go.path_id,name=d['m_Name'],layer=d['m_Layer'],components=[])
 for ref in d['m_Component']:
  ref=ref['component']
  if ref['m_FileID']:continue
  obj=go.assets_file.objects[ref['m_PathID']]
  if obj.type.name not in ('Transform','BoxCollider2D','CircleCollider2D','CapsuleCollider2D','PolygonCollider2D'):continue
  fields=obj.read_typetree();keep={k:v for k,v in fields.items() if k in ('m_LocalPosition','m_LocalScale','m_IsTrigger','m_Enabled','m_Size','m_Radius','m_Offset','m_Points')}
  row['components'].append(dict(type=obj.type.name,fields=keep))
  if obj.type.name=='Transform' and depth<2:
   row['children']=[]
   for r in fields['m_Children']:
    if r['m_FileID']:continue
    tr=go.assets_file.objects[r['m_PathID']].read_typetree()
    row['children'].append(inspect(go.assets_file.objects[tr['m_GameObject']['m_PathID']],depth+1))
 return row
rows=[]
for obj in env.objects:
 if obj.type.name!='GameObject':continue
 d=obj.read_typetree();name=d.get('m_Name','')
 if name in ('Deer','Archer','Hind','Critter') or name.lower().startswith('deer_'):rows.append(inspect(obj))
(task/'asset-geometry.json').write_text(json.dumps(rows,indent=2),encoding='utf-8')
print(json.dumps(rows))
