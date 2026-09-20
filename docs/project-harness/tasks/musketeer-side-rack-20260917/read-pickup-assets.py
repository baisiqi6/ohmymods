from pathlib import Path
import json,UnityPy
p=Path('E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/KingdomTwoCrowns_Data/resources.assets')
env=UnityPy.load(str(p));result=[]
def inspect(go):
 data=go.read_typetree(); row={'id':go.path_id,'name':data['m_Name'],'layer':data['m_Layer'],'components':[]}
 for c in data['m_Component']:
  ref=c['component']
  if ref['m_FileID']:continue
  r=go.assets_file.objects[ref['m_PathID']]
  if r.type.name not in ('Transform','Rigidbody2D','BoxCollider2D','CapsuleCollider2D','CircleCollider2D'):continue
  fields=r.read_typetree()
  keep={k:v for k,v in fields.items() if k in ('m_LocalPosition','m_LocalScale','m_IsTrigger','m_Enabled','m_Size','m_Radius','m_Offset','m_BodyType')}
  row['components'].append(dict(type=r.type.name,fields=keep))
 return row
for obj in env.objects:
 if obj.type.name!='GameObject':continue
 d=obj.read_typetree()
 if d.get('m_Name') not in ('Peasant','ToolBow'):continue
 result.append(inspect(obj))
 for ref in d['m_Component']:
  r=obj.assets_file.objects.get(ref['component']['m_PathID'])
  if r and r.type.name=='Transform':
   for child in r.read_typetree()['m_Children']:
    tr=obj.assets_file.objects.get(child['m_PathID'])
    if tr:
     go=tr.read_typetree()['m_GameObject']
     result.append(inspect(obj.assets_file.objects[go['m_PathID']]))
Path(__file__).with_name('pickup-asset-evidence.json').write_text(json.dumps(result,indent=2))
print(json.dumps(result))
