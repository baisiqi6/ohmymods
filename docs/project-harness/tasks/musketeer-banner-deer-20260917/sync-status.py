from pathlib import Path
import json,re
task=Path(__file__).resolve().parent;repo=task.parents[3];tag=task.name
summary=(task/'status.txt').read_text(encoding='utf-8').strip()
block=f'<!-- {tag} -->\n{summary}\n<!-- {tag} -->\n'
for rel in ['AGENTS.md','docs/project-harness/progress.md','docs/project-harness/current/task_plan.md','docs/project-harness/current/review.md','docs/project-harness/current/closeout-packet.md','docs/project-harness/current/handoff-packet.md','docs/project-harness/domain-model.md','docs/project-harness/game-logic-map/patch-patterns.md']:
 p=repo/rel;s=p.read_text(encoding='utf-8-sig');pat=rf'<!-- {tag} -->.*?<!-- {tag} -->\n?'
 p.write_text(re.sub(pat,lambda _:block,s,count=1,flags=re.S) if re.search(pat,s,re.S) else block+'\n'+s,encoding='utf-8')
p=repo/'docs/project-harness/harness-checklist.json';d=json.loads(p.read_text(encoding='utf-8-sig'));item=next((i for i in d['items'] if i['id']==tag),None)
if item is None:
 item=dict(id=tag,title='举旗火枪后排与白天猎鹿',status='doing',priority='p2',owner='codex',selected_in_session='codex-'+tag,updated_at='2026-09-17',dependencies=[],blocked_by=[],blocked_reason=None,acceptance='原4+4及船队保持，4额外火枪后排/左右与生命周期，白天只Deer不误伤小动物，实际2.4接口/回归/review，实机命中与回防')
 d['items'].insert(0,item)
item['verification']=summary;item['handoff']='详见tasks/'+tag+'/plan.md、result.md及receipts；无实际游戏证据不置done。'
p.write_text(json.dumps(d,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Task status synchronized.')
