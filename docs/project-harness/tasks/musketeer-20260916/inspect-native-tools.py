from pathlib import Path
import UnityPy,json
root=Path('E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/KingdomTwoCrowns_Data')
env=UnityPy.load(str(root/'resources.assets'))
rows=[]
for obj in env.objects:
    if obj.type.name!='GameObject':continue
    go=obj.read()
    if not (go.m_Name.startswith('ToolBow') or go.m_Name.startswith('ShopBow')):continue
    row={'name':go.m_Name,'asset':obj.assets_file.name,'id':obj.path_id,'components':[]}
    for pair in go.m_Component:
        reader=pair.component.deref()
        try:tree=reader.read_typetree()
        except ValueError:
            row['components'].append({'type':reader.type.name,'unreadableCustomTypeTree':True});continue
        selected={k:v for k,v in tree.items() if k in ['m_LocalPosition','m_LocalScale','m_Offset','m_Size','m_Radius','m_IsTrigger','itemPlacement','itemSpacing','maxItems','m_Script','path','persistObject','m_BodyType','_nobodyCooldown','pickUpPolicy','dropOffset']}
        if selected:row['components'].append({'type':reader.type.name,'data':selected})
    rows.append(row)
out=Path(__file__).with_name('native-tool-shops.json');out.write_text(json.dumps(rows,indent=2),encoding='utf8')
print(json.dumps(rows))
