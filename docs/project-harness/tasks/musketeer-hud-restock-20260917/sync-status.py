from pathlib import Path
import json,re
task=Path(__file__).resolve().parent;root=task.parents[3];tag=task.name
summary='2026-09-17 火枪手加入职业HUD与阈值补货：HUD单独计已确认火枪手并从普通弓手剥离，补货role8保留旧索引，独立默认off/目标15/范围1–200；沿现希腊单机金库与助手队列，手动4/自动8，活体+可用已买枪+在途订单覆盖目标。自定义铺独立自动入口，保留手动玩家凭据与库存/世界/暂停门；未知身份不作0消费，确定回池损失仅会话证据，旧unbound跨读档仍可能阻断。用户追加英雄日常持弓再改为贴身近竖拿，保留缩头与22..30战斗/中位。'
p=task/'receipts/install.json'
if p.exists():
    r=json.loads(p.read_text(encoding='utf-8-sig'))
    summary+=f"已闭游戏备份安装{r['candidateSha256'][:8].lower()}到既定E盘，{r['userDataFileCount']}份存档/附加档/配置hash保持。针对回归、actual2.4构建、方法/资源审计和本轮独立复核通过；仅英雄持弓图集授权改动，其余七PNG与既有其他功能保持。未启动/提交/发布，公开8.0.0不变。实机HUD/补货/余额/拾枪/竖拿观感仍待验，旧火枪fullarchive终审缺口保持。"
else:summary+='当前实现与独立复核进行中，未安装，保留2c7fe54f英雄放松携弓候选。'
block=f'<!-- {tag} -->\n{summary}\n<!-- {tag} -->\n'
for rel in ['AGENTS.md','docs/project-harness/progress.md','docs/project-harness/current/task_plan.md','docs/project-harness/current/review.md','docs/project-harness/current/closeout-packet.md','docs/project-harness/current/handoff-packet.md','docs/project-harness/domain-model.md','docs/project-harness/game-logic-map/patch-patterns.md']:
    path=root/rel;s=path.read_text(encoding='utf-8-sig');pat=rf'<!-- {tag} -->.*?<!-- {tag} -->\n?'
    s=re.sub(pat,lambda _:block,s,count=1,flags=re.S) if re.search(pat,s,re.S) else block+'\n'+s
    path.write_text(s,encoding='utf-8')
p=root/'docs/project-harness/harness-checklist.json';o=json.loads(p.read_text(encoding='utf-8-sig'))
item=next((i for i in o['items'] if i['id']==tag),None)
if item is None:
    item=dict(id=tag,title='火枪手职业HUD与阈值补货',status='doing',priority='p1',owner='codex',selected_in_session='codex-'+tag,updated_at='2026-09-17',dependencies=[],blocked_by=[],blocked_reason=None,acceptance='火枪职业不重计、可用枪与订单防超买、自动8币单次交易、范围与失败门、回归复核及实机')
    o['items'].insert(0,item)
item['verification']=summary;item['handoff']='保持doing待实机；不自动启动，不改用户档/配置，不提交发布。'
p.write_text(json.dumps(o,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Task state synchronized.')
