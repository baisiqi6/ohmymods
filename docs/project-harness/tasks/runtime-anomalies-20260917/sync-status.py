from pathlib import Path
import json,re
task=Path(__file__).resolve().parent;root=task.parents[3];tag=task.name
summary='2026-09-17 按用户要求定位英雄走跑抖动、乞丐穿地和弩手漂移。实际2.4动画阈值Speed1且hero walk .975/run2.4，但没有切态速度现场，根因未确认；弩手单次巡检不能排除原生Mover重写y1的暂态，诊断无Greek门有误报风险，尚未修。英雄/弩手分支被自动安全审查拒绝Potentially unintended activity，未重试/转派；英雄未完成新增诊断已归档撤回，弩手未改。人口真实资产17↔0碰撞有效、友军ignore10/17不含地面0，未确认穿地源头；仅新增现0.5s维护内事件只读日志，三类各12条/上下文，无物理/位置/人数改动。'
p=task/'receipts/install.json'
if p.exists():
 r=json.loads(p.read_text(encoding='utf-8-sig'))
 summary+=f"已闭游戏备份安装人口诊断候选{r['candidateSha256'][:8].lower()}（8.0.0-population-ground-diagnostics-20260917），{r['userDataFileCount']}份存档/附加档/配置hash保持。17诊断/103英雄回退/37骑士与Dropinterop回归、actual2.4全构建与DLL审计通过；8PNG、全部Hero/Crossbow方法保持，无新Harmony hook。未启动/提交/发布。三项异常均未宣称修复；下一轮日志用于穿地定位，其他两项修改受自动审查阻断。"
else:summary+='人口候选待构建核对，当前069527b6保持；不把诊断当修复。'
block=f'<!-- {tag} -->\n{summary}\n<!-- {tag} -->\n'
for rel in ['AGENTS.md','docs/project-harness/progress.md','docs/project-harness/current/task_plan.md','docs/project-harness/current/review.md','docs/project-harness/current/closeout-packet.md','docs/project-harness/current/handoff-packet.md','docs/project-harness/domain-model.md','docs/project-harness/game-logic-map/patch-patterns.md']:
 p=root/rel;s=p.read_text(encoding='utf-8-sig');pat=rf'<!-- {tag} -->.*?<!-- {tag} -->\n?'
 s=re.sub(pat,lambda _:block,s,count=1,flags=re.S) if re.search(pat,s,re.S) else block+'\n'+s
 p.write_text(s,encoding='utf-8')
p=root/'docs/project-harness/harness-checklist.json';o=json.loads(p.read_text(encoding='utf-8-sig'))
item=next((i for i in o['items'] if i['id']==tag),None)
if item is None:
 item=dict(id=tag,title='英雄切态、人口穿地与弩手缩放定位',status='doing',priority='p1',owner='codex',selected_in_session='codex-'+tag,updated_at='2026-09-17',dependencies=[],blocked_by=[],blocked_reason=None,acceptance='确定来源后最小修订；未知项有界证据，不全局改物理/掩盖帧；回归与实机')
 o['items'].insert(0,item)
item['verification']=summary;item['handoff']='人口取证待下一轮用户正常游玩日志；Hero/Crossbow自动审查阻断记录见blocked-branches.md，不重试绕过。不得称三问题已修。'
p.write_text(json.dumps(o,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Runtime anomaly status synchronized; unresolved issues remain doing.')
