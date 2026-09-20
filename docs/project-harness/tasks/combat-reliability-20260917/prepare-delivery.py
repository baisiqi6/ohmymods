from pathlib import Path
import hashlib, json, re, shutil

task = Path(__file__).resolve().parent
repo = task.parents[3]
operator = Path('C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2') / task.name
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
baseline = json.loads((task / 'receipts/source-before.json').read_text(encoding='utf-8-sig'))
audit = json.loads((task / 'receipts/dll-audit.json').read_text(encoding='utf-8-sig'))
production = {p.relative_to(repo).as_posix(): sha(p) for p in (repo / 'il2cpp').iterdir()
              if p.suffix in ('.cs', '.props', '.csproj')}
production.update({p.relative_to(repo).as_posix(): sha(p) for p in (repo / 'il2cpp/Assets').glob('*.png')})
allowed = {'il2cpp/KingdomEnhancedPlugin.cs', 'il2cpp/MusketeerCombat.cs',
           'il2cpp/PatchArcher_Impact.cs', 'il2cpp/PatchArcher_GreekImpact.cs',
           'il2cpp/CombatDamage.cs', 'il2cpp/CombatTargetLife.cs'}
changed = [n for n, h in production.items() if baseline.get(n) != h]
assert set(changed) == allowed
assert not (set(baseline) - set(production))
assert len(audit['unchangedResources']) == 8 and not audit['changedResources']
assert not audit['newHarmonyPatchTypes'] and not audit['removedHarmonyPatchTypes']
assert audit['version'] == '9.0.0.0'
dll = repo / 'il2cpp/bin/Debug/KingdomEnhancedMod.dll'
assert sha(dll) == audit['candidateSha256'].lower()
candidate = operator / 'candidate'
candidate.mkdir(parents=True, exist_ok=True)
shutil.copy2(dll, candidate / dll.name)
(task / 'receipts/source-delta.json').write_text(json.dumps({
    'changed': changed, 'newProductionFiles': [n for n in production if n not in baseline],
    'sourceSha256': {n: production[n] for n in changed}, 'allProductionSha256': production
}, indent=2), encoding='utf-8')

notes = '''9.0.0 伤害可靠性与计算优化 — 本机候选

火枪弹丸与希腊/英雄火焰箭的额外范围伤害共用同步提交模块，最终调用原版 ReceiveDamage，保留护盾、无敌和受伤事件；普通箭原生直伤流程保持。
火枪保持基础伤害2、原射程与装填节奏；密集碰撞超过原256结果数组时改用完整列表选择最近有效目标；弹丸记录池化、完整帧时间扫掠和回调重入防护。最近免疫前排仍能挡弹，不变成穿透。
范围伤害保持半径0.25、额外1点、直接命中者不重复受伤、同一生命同一轮齐射一次。普通贪婪小怪此前核对的受击范围宽度约0.375，供尺寸比较；碰撞体边缘进入圆内也可命中，0.25不是角色中心间距限制。新增对象生命标记避免回池后沿用旧去重记录；单批快照防止回调中新生对象被旧碰撞引用误打。
作者火焰公式、三层颜色/尺寸/寿命和所有贴图保持。同一计算时刻重复Perlin计算共享，满16个效果测试由3456次降至72次；量化顶点未变时跳过上传。这是调用量测试，不是实测帧率。

没有新增伤害延迟队列、逐帧全场扫描、原生getter/生命周期钩子或存档字段。
代码回归、真实2.4接口/完整构建与独立复核见项目记录；真实怪群密度、运行时注入与List调用、血量/护盾表现及帧耗时仍待游戏验证。不能承诺零开销或绝无漏伤。既有32个AoE目标/64碰撞缓冲、4096同时弹丸等保护上限仍保留；原生接口或生命证据不可用时保守放弃该次额外伤害，不伪造命中或重试部分生效回调。
此前英雄步态/人物位置缩放等未定因问题不属于本次修复。

build=9.0.0-combat-reliability-20260917
公开版本仍9.0.0，本候选未发布。
DLL SHA256=''' + sha(dll) + '\n'
(candidate / '更新说明.txt').write_text(notes, encoding='utf-8')
install = (task.parent / 'hold-purchase-ammo-20260917/install-candidate.ps1').read_text(encoding='utf-8-sig')
install = install.replace('hold-purchase-ammo-20260917', task.name)
install = re.sub(r"\$expected = '[0-9A-F]+'", "$expected = '" + audit['candidateSha256'] + "'", install)
install = re.sub(r"\$previous = '[0-9A-F]+'", "$previous = '" + audit['baselineSha256'] + "'", install)
install = install.replace('before-hold-ammo-', 'before-combat-reliability-')
install = install.replace('User requested extending hold purchase;', 'User requested damage reliability fixes;')
install = install.replace('Hold purchase adds native barrel and FireTower ammo targets; no price/capacity/stock/save edits.',
                          'Shared native damage submission, pooled bullets, target-life AoE dedupe and equivalent FX computation; no save/config edits.')
(task / 'install-candidate.ps1').write_text(install, encoding='utf-8-sig')
print('Prepared local candidate:', sha(dll))
