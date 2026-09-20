from pathlib import Path
import json,hashlib,shutil,re
task=Path(__file__).resolve().parent;repo=task.parents[3]
operator=Path('C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2')
out=operator/task.name;candidate=out/'candidate';candidate.mkdir(parents=True,exist_ok=True)
audit=json.loads((task/'receipts/dll-audit.json').read_text(encoding='utf-8-sig'))
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
base=json.loads((operator/'release-900-20260917/release-commit.json').read_text(encoding='utf-8'))
hashes={f['path']:f['sha256'] for f in base['files'] if f['path'].startswith('il2cpp/')}
side=json.loads((task.parent/'musketeer-side-rack-20260917/receipts/source-delta.json').read_text())
hashes.update(side['sourceSha256'])
changed=[n for n,h in hashes.items() if sha(repo/n)!=h]
allowed={'il2cpp/PatchPlayer_HoldPurchase.cs','il2cpp/KingdomEnhancedPlugin.cs','il2cpp/ModConfig.cs','il2cpp/ModPanel.cs'}
assert set(changed)==allowed,changed
production={p.relative_to(repo).as_posix() for p in (repo/'il2cpp').iterdir() if p.suffix in {'.cs','.csproj','.props'}}
production|={p.relative_to(repo).as_posix() for p in (repo/'il2cpp/Assets').glob('*.png')}
assert production==set(hashes)
dll=repo/'il2cpp/bin/Debug/KingdomEnhancedMod.dll';assert sha(dll)==audit['candidateSha256'].lower()
shutil.copy2(dll,candidate/dll.name)
(task/'receipts/source-delta.json').write_text(json.dumps({'baseline':'6e2d side-rack candidate','changed':changed,'newProductionFiles':[], 'sourceSha256':{n:sha(repo/n) for n in changed}},indent=2))
(candidate/'更新说明.txt').write_text('9.0.0 快速购买弹药本机候选\n\n现有F5便捷页“长按连续购买”加入投石车火药桶与希腊火焰塔弹药。首次投币仍按原版，连续按住后加速并续买，松开即停；原价5/2金币、设施/库存/钱包限制仍由原生判断。开关关闭保留原版。\n\nbuild=9.0.0-hold-purchase-ammo-20260917\nDLL SHA256='+sha(dll)+'\n\n已做代码回归和2.4接口构建，实际单机及主客机购买仍待实测。保留上一候选独立火枪侧架。此候选未公开发布。\n',encoding='utf-8')
s=(task.parent/'musketeer-side-rack-20260917/install-candidate.ps1').read_text(encoding='utf-8-sig')
s=s.replace('musketeer-side-rack-20260917',task.name)
s=re.sub(r"\$expected = '[0-9A-F]+'","$expected = '"+audit['candidateSha256']+"'",s)
s=re.sub(r"\$previous = '[0-9A-F]+'","$previous = '"+audit['baselineSha256']+"'",s)
s=s.replace('before-side-rack-','before-hold-ammo-')
s=s.replace('Side-rack layout/artwork and proven unclaimed rack-gun placement only; no native save/schema/config changes.','Hold purchase adds native barrel and FireTower ammo targets; no price/capacity/stock/save edits.')
(task/'install-candidate.ps1').write_text(s,encoding='utf-8-sig')
print('Candidate prepared:',str(candidate/dll.name),sha(dll))
