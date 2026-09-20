from pathlib import Path
import json,re
task=Path(__file__).resolve().parent;root=task.parents[3];tag=task.name
summary='2026-09-17 英雄持弓纠正：下垂手臂握中央弓把、腿侧横持；用户继而明确弓弦应在上，故整体翻转为弦上弧下且握点不移。0..21改手/弓，原人体不重画但允许武器前景遮挡腰/大腿上缘；头/步态/双围巾保持，23..29战斗保持，22/30中位按连续性核验。'
p=task/'receipts/install.json'
if p.exists():
    r=json.loads(p.read_text(encoding='utf-8-sig'))
    summary+=f"已闭游戏备份安装{r['candidateSha256'][:8].lower()}到既定E盘，{r['userDataFileCount']}份存档/附加档/配置hash保持。素材几何/区域保护核验、2.4构建和DLL资源审计通过，仅HeroArcherAtlas和build文字变化。未启动/提交/发布，实机观感待验。既有火枪HUD/补货保持；原生Walk/Run频繁归零仍待定位，未因换图宣称解决。"
else:summary+='当前worker仅生成候选，未安装；保留EC80全部功能和用户数据。'
block=f'<!-- {tag} -->\n{summary}\n<!-- {tag} -->\n'
for rel in ['AGENTS.md','docs/project-harness/progress.md','docs/project-harness/current/task_plan.md','docs/project-harness/current/review.md','docs/project-harness/current/closeout-packet.md','docs/project-harness/current/handoff-packet.md','docs/project-harness/domain-model.md','docs/project-harness/game-logic-map/patch-patterns.md']:
    p=root/rel;s=p.read_text(encoding='utf-8-sig');pat=rf'<!-- {tag} -->.*?<!-- {tag} -->\n?'
    s=re.sub(pat,lambda _:block,s,count=1,flags=re.S) if re.search(pat,s,re.S) else block+'\n'+s
    p.write_text(s,encoding='utf-8')
p=root/'docs/project-harness/harness-checklist.json';o=json.loads(p.read_text(encoding='utf-8-sig'))
item=next((i for i in o['items'] if i['id']==tag),None)
if item is None:
    item=dict(id=tag,title='英雄中央弓把与腿侧横持',status='doing',priority='p1',owner='codex',selected_in_session='codex-'+tag,updated_at='2026-09-17',dependencies=[],blocked_by=[],blocked_reason=None,acceptance='自然垂臂、中央握点匹配、横弓完整、动作衔接/其他像素保持及实机观感')
    o['items'].insert(0,item)
item['verification']=summary;item['handoff']='按用户最新弦上/中央握把横持方向；实机观感待验，不自动启动，不改用户档/配置。'
p.write_text(json.dumps(o,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Synchronized hero grip task.')
