from pathlib import Path
import json,re
task=Path(__file__).resolve().parent; root=task.parents[3]; tag=task.name
installed=task/'receipts/install.json'
summary='2026-09-17 英雄放松携弓/小幅缩头：基于当前58362d47最终31槽图集做局部像素编辑，站走跑改放低携弓，全动作头部同步稍收窄缩短，颈部围巾/脚点/腿步态/斜向上战斗弓/整体0.9保持。运行时原生动作采样无需改动；只允许HeroArcherAtlas.png与build文字改变。'
if installed.exists():
    receipt=json.loads(installed.read_text(encoding='utf-8-sig'))
    summary+=f"已闭游戏备份安装候选{receipt['candidateSha256'][:8].lower()}到既定E盘，{receipt['userDataFileCount']}份存档/附加档/配置hash保持。像素检查、针对回归、完整2.4构建、DLL/资源审计与独立复核通过；未启动游戏/提交/发布。观感和实机衔接仍待用户验收，任务doing。"
else: summary+='worker按北京时间14–18规则使用内置，仅生成候选素材；当前待预览、构建与复核，未替换DLL或改用户数据。'
block=f'<!-- {tag} -->\n{summary}\n<!-- {tag} -->\n'
paths=['AGENTS.md','docs/project-harness/progress.md','docs/project-harness/current/task_plan.md','docs/project-harness/current/review.md','docs/project-harness/current/closeout-packet.md','docs/project-harness/current/handoff-packet.md','docs/project-harness/domain-model.md','docs/project-harness/game-logic-map/patch-patterns.md']
for rel in paths:
    p=root/rel;s=p.read_text(encoding='utf-8-sig');pattern=rf'<!-- {tag} -->.*?<!-- {tag} -->\n?'
    s=re.sub(pattern,lambda _:block,s,count=1,flags=re.S) if re.search(pattern,s,re.S) else block+'\n'+s
    p.write_text(s,encoding='utf-8')
p=root/'docs/project-harness/harness-checklist.json';obj=json.loads(p.read_text(encoding='utf-8-sig'))
item=next((i for i in obj['items'] if i['id']==tag),None)
if item is None:
    item=dict(id=tag,title='英雄放松携弓与头部比例调整',status='doing',priority='p1',owner='codex',selected_in_session='codex-'+tag,updated_at='2026-09-17',dependencies=[],blocked_by=[],blocked_reason=None,acceptance='非战斗持弓放松、头部微缩、动作与脚点/双围巾保持，像素/构建/审核及实机验证')
    obj['items'].insert(0,item)
item['verification']=summary;item['handoff']='本机候选实机观感待验收；不自动启动，不操作存档/配置，不发布。'
p.write_text(json.dumps(obj,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(summary)
