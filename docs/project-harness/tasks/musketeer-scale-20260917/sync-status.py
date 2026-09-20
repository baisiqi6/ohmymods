from pathlib import Path
import json, re

task=Path(__file__).resolve().parent; repo=task.parents[3]; tag=task.name
audit=json.loads((task/'receipts/dll-audit.json').read_text(encoding='utf-8-sig'))
summary=('2026-09-17 用户要求火枪手当前外观缩为0.9：共用AppearanceScale，仅自有sprite child绝对(.9,.9,1)、枪口localXY同系数，脚点/朝向保持；'
         '不改actor/物理/速度/动画时钟/伤害/射程/节奏/全局缩放/商店工具弹丸大小/持久化，8PNG与上一combat-reliability保持。'
         f'候选{audit["candidateSha256"][:8].lower()} / build=9.0.0-musketeer-scale-20260917；79runtime回归、actual2.4接口/完整build0W0E、范围审计和独立复核通过。')
p=task/'receipts/install.json'
if p.exists():
    install=json.loads(p.read_text(encoding='utf-8-sig'))
    summary+=f'已闭游戏备份安装既定E独立副本，{install["userDataFileCount"]}份原生档/附加档/配置hash保持，未启动游戏。'
else: summary+='候选已就绪但游戏仍运行，尚未安装；等待正常保存退出，已装仍284b78a0。'
summary+=('本次19:34启动日志23名火枪loaded exact/bound23且saved23，marker注册正常、未见伤害故障日志，但不能当逐弹/高密度/帧耗时验收。'
          'Player另有城堡盾牌店InvalidNetID11次、2名Archer穿地被引擎搬回、英雄快速Stand/Walk，根因与具体职业未确认；仅记录，不把0.9当修复。'
          '未commit/push/publish，公开9.0.0不变，0.9观感/枪口实机待验收，保持doing。')
block=f'<!-- {tag} -->\n{summary}\n<!-- {tag} -->\n'
for rel in ['AGENTS.md','docs/project-harness/progress.md','docs/project-harness/current/task_plan.md','docs/project-harness/current/review.md',
            'docs/project-harness/current/closeout-packet.md','docs/project-harness/current/handoff-packet.md','docs/project-harness/domain-model.md',
            'docs/project-harness/game-logic-map/patch-patterns.md']:
    p=repo/rel;s=p.read_text(encoding='utf-8-sig');pat=rf'<!-- {tag} -->.*?<!-- {tag} -->\n?'
    p.write_text(re.sub(pat,lambda _:block,s,count=1,flags=re.S) if re.search(pat,s,re.S) else block+'\n'+s,encoding='utf-8')
p=repo/'docs/project-harness/harness-checklist.json';data=json.loads(p.read_text(encoding='utf-8-sig'))
item=next((i for i in data['items'] if i['id']==tag),None)
if item is None:
    item=dict(id=tag,title='火枪手外观0.9倍与本次日志核对',status='doing',priority='p2',owner='codex',selected_in_session='codex-'+tag,
              updated_at='2026-09-17',dependencies=[],blocked_by=[],blocked_reason=None,
              acceptance='自有外观XY0.9/脚点及枪口一致、其余玩法保持、回归/实际2.4构建/独立复核与实机')
    data['items'].insert(0,item)
item['verification']=summary
item['handoff']='见tasks/musketeer-scale-20260917/result.md、log-findings.md、review.md和receipts；安装看install.json，无回执则必须先确认退出再安装。不能宣称日志位置/NetID异常已修。'
p.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(task/'acceptance.md').write_text('# 火枪手0.9候选验收\n\n'+summary+'\n',encoding='utf-8')
print('Musketeer scale status synchronized; actual visual acceptance remains doing.')
