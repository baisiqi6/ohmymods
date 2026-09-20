from pathlib import Path
import hashlib,json,re,shutil
task=Path(__file__).resolve().parent; repo=task.parents[3]
op=Path('C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2')/task.name
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
baseline=json.loads((task/'receipts/source-before.json').read_text(encoding='utf-8-sig'))
changed=[n for n,h in baseline.items() if sha(repo/n)!=h]
assert set(changed)=={'il2cpp/PatchEconomy_AutoRestock.cs','il2cpp/PatchEconomy_Banker.cs','il2cpp/ModPanel.cs','il2cpp/KingdomEnhancedPlugin.cs'},changed
audit=json.loads((task/'receipts/dll-audit.json').read_text(encoding='utf-8-sig'))
dll=repo/'il2cpp/bin/Debug/KingdomEnhancedMod.dll';assert sha(dll)==audit['candidateSha256'].lower()
assert len(audit['unchangedResources'])==8 and not audit['changedResources'] and not audit['newHarmonyPatchTypes']
candidate=op/'candidate';candidate.mkdir(parents=True,exist_ok=True);shutil.copy2(dll,candidate/dll.name)
(task/'receipts/source-delta.json').write_text(json.dumps({'changed':changed,'sourceSha256':{n:sha(repo/n) for n in changed}},indent=2),encoding='utf-8')
(candidate/'更新说明.txt').write_text('9.0.0 自动补货仅白天本机候选\n\n全部现有自动补货类别只在原版判定白天时采购。夜间等待天亮，尚未扣款的订单撤回并归还税收官；已成交只收尾，不重复购买或退款；天亮后按原阈值和调度节奏重新补足。\n最终自动扣款入口同样拒绝夜间交易，手动购买、普通存取款和收税功能保持。现有仅希腊世界范围、双倍金库费用及全部门槛保持。面板增加白天补货和等待天亮提示。\n保留火枪手0.9外观、伤害可靠性及所有贴图。实际跨昼夜游玩仍待验证。\nbuild=9.0.0-restock-daylight-20260917\nDLL SHA256='+sha(dll)+'\n本机候选，未公开发布。\n',encoding='utf-8')
s=(task.parent/'musketeer-scale-20260917/install-candidate.ps1').read_text(encoding='utf-8-sig').replace('musketeer-scale-20260917',task.name).replace('before-musketeer-scale-','before-restock-daylight-')
s=re.sub(r"\$expected = '[0-9A-F]+'","$expected = '"+audit['candidateSha256']+"'",s)
s=re.sub(r"\$previous = '[0-9A-F]+'","$previous = '"+audit['baselineSha256']+"'",s)
s=s.replace('User requested musketeer 0.9 visual scale;','User requested daytime-only automatic restocking;')
s=s.replace('Only musketeer owned visual XY scale and corresponding muzzle offset; native actor scale, physics, damage, PNGs and save/config unchanged.','Automatic restocking and its final debit require native daytime; manual purchase, normal banking/tax collection and save/config unchanged.')
(task/'install-candidate.ps1').write_text(s,encoding='utf-8-sig')
print('Prepared exact daylight-restock candidate',sha(dll))
