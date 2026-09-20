from pathlib import Path
import json, re

task = Path(__file__).resolve().parent
repo = task.parents[3]
tag = task.name
audit = json.loads((task/'receipts/dll-audit.json').read_text(encoding='utf-8-sig'))
summary = ('2026-09-17 伤害可靠性候选：火枪弹丸与Greek/英雄额外AoE共用同步CombatDamage→原生ReceiveDamage，保留原生护盾/无敌/事件，异常不重试且隔离后续目标。'
           '弹丸32→256数组饱和改完整List最近有效命中，记录池化/long租约/重入门/回调world门/完整dt射程寿命扫掠；保持伤害2与原射速射程。'
           'AoE无Update自有marker真实GO生命周期区分新life，缺证据不猜；64有界快照提交前完整复核，提交32上限与.25半径/额外1/直击排除保持。'
           'marker不写hideFlags、不实现保存接口，非Unity助手HideFromIl2Cpp，注册仅一次避免故障热循环。'
           '作者FX同time共享72次noise（满16效果原3456），ties-even量化不变、未变顶点不上传、固定bounds；8PNG保持。'
           f'候选{audit["candidateSha256"][:8].lower()} / build=9.0.0-combat-reliability-20260917；392针对检查、actual2.4接口/完整构建0W0E、方法资源审计与独立复核通过。')
receipt = task/'receipts/install.json'
if receipt.exists():
    install = json.loads(receipt.read_text(encoding='utf-8-sig'))
    summary += f'已闭游戏备份安装既定E独立副本，{install["userDataFileCount"]}份原生档/附加档/配置hash保持，未启动游戏。'
else:
    summary += '候选已就绪，尚未安装；游戏运行时禁止替换DLL。'
summary += ('未commit/push/publish，公开9.0.0不变。真实marker消息/List AOT/血量护盾/密集战斗帧耗时与相关联机仍待实机，不能声称零开销/零漏伤。'
            '原生箭直伤、商店/长按弹药购买等保持；不涉及此前blocked英雄/弩手诊断，旧动作/位置异常未称修复。')
block = f'<!-- {tag} -->\n{summary}\n<!-- {tag} -->\n'
for rel in ['AGENTS.md','docs/project-harness/progress.md','docs/project-harness/current/task_plan.md',
            'docs/project-harness/current/review.md','docs/project-harness/current/closeout-packet.md',
            'docs/project-harness/current/handoff-packet.md','docs/project-harness/domain-model.md',
            'docs/project-harness/game-logic-map/patch-patterns.md']:
    p = repo/rel
    s = p.read_text(encoding='utf-8-sig')
    pat = rf'<!-- {tag} -->.*?<!-- {tag} -->\n?'
    s = re.sub(pat, lambda _: block, s, count=1, flags=re.S) if re.search(pat,s,re.S) else block+'\n'+s
    p.write_text(s, encoding='utf-8')
p = repo/'docs/project-harness/harness-checklist.json'
data = json.loads(p.read_text(encoding='utf-8-sig'))
item = next((i for i in data['items'] if i['id']==tag), None)
if item is None:
    item = dict(id=tag,title='火枪与额外AoE伤害可靠性和等价特效计算',status='doing',priority='p1',owner='codex',
                selected_in_session='codex-'+tag,updated_at='2026-09-17',dependencies=[],blocked_by=[],blocked_reason=None,
                acceptance='原生共同伤害入口、生命/回调/密集碰撞/完整dt回归，FX等价与调用量，实际2.4构建、独立复核与实机')
    data['items'].insert(0,item)
item['verification'] = summary
item['handoff'] = '见tasks/combat-reliability-20260917/result.md、review.md与receipts；候选不等于实机验收。检查实际marker首次命中/回池、List调用及密集战斗护盾与HP、帧耗时，火枪现有单机范围保持。'
p.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(task/'acceptance.md').write_text('# 伤害可靠性候选验收\n\n'+summary+'\n\n代码与实际游戏接口构建不能替代真实游戏战斗验证。\n',encoding='utf-8')
print('Combat reliability status synchronized; gameplay acceptance remains doing.')
