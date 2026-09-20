from pathlib import Path
import json,hashlib,shutil,re
task=Path(__file__).resolve().parent;repo=task.parents[3]
operator=Path('C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2')
out=operator/task.name;candidate=out/'candidate';candidate.mkdir(parents=True,exist_ok=True)
audit=json.loads((task/'receipts/dll-audit.json').read_text(encoding='utf-8-sig'))
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
baseline=json.loads((operator/'release-900-20260917/release-commit.json').read_text(encoding='utf-8'))
changed=[f['path'] for f in baseline['files'] if f['path'].startswith('il2cpp/') and sha(repo/f['path'])!=f['sha256']]
allowed={'il2cpp/MusketeerShop.cs','il2cpp/MusketeerShopRules.cs','il2cpp/MusketeerGunVisuals.cs','il2cpp/KingdomEnhancedPlugin.cs','il2cpp/Assets/MusketeerShop.png'}
assert set(changed)==allowed,changed
production={p.relative_to(repo).as_posix() for p in (repo/'il2cpp').iterdir() if p.suffix in {'.cs','.csproj','.props'}}
production|={p.relative_to(repo).as_posix() for p in (repo/'il2cpp/Assets').glob('*.png')}
assert production=={f['path'] for f in baseline['files'] if f['path'].startswith('il2cpp/')}
dll=repo/'il2cpp/bin/Debug/KingdomEnhancedMod.dll';assert sha(dll)==audit['candidateSha256'].lower()
shutil.copy2(dll,candidate/dll.name)
(task/'receipts/source-delta.json').write_text(json.dumps({'baseline':'v9.0.0','changed':changed,'newProductionFiles':[], 'sourceSha256':{p:sha(repo/p) for p in changed}},indent=2))
(candidate/'更新说明.txt').write_text('9.0.0 火铳铺独立侧架本机候选\n\n保留西式店铺与枪匠，右侧独立三层陈列板显示实际已买枪具。同步扩大占地、按真实槽位放枪。旧存档仅移动身份确认且无人领取的架枪，掉落枪不拉回。\n\nbuild=9.0.0-musketeer-side-rack-20260917\nDLL SHA256='+sha(dll)+'\n\n已通过代码回归与2.4接口构建；最高层领取、多枪同时接触、旧档摆位及观感仍需实机确认。游戏运行时禁止替换DLL。此候选未公开发布。\n',encoding='utf-8')
s=(task.parent/'runtime-anomalies-20260917/install-candidate.ps1').read_text(encoding='utf-8-sig')
s=s.replace('runtime-anomalies-20260917',task.name)
s=re.sub(r"\$expected = '[0-9A-F]+'","$expected = '"+audit['candidateSha256']+"'",s)
s=re.sub(r"\$previous = '[0-9A-F]+'","$previous = '"+audit['baselineSha256']+"'",s)
s=s.replace('8.0.0-population-ground-diagnostics-20260917','9.0.0-musketeer-side-rack-20260917')
s=s.replace('Population read-only diagnostics only; no physics, position, population setting, Hero/Crossbow or save changes.','Side-rack layout/artwork and proven unclaimed rack-gun placement only; no native save/schema/config changes.')
s=s.replace('before-musketeer-','before-side-rack-')
(task/'install-candidate.ps1').write_text(s,encoding='utf-8-sig')
print('Candidate prepared:',str(candidate/dll.name),sha(dll))
