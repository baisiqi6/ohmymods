from pathlib import Path
import json,re
task=Path(__file__).resolve().parent;repo=task.parents[3];tag=task.name
audit=json.loads((task/'receipts/dll-audit.json').read_text(encoding='utf-8-sig'))
summary=('2026-09-17 火铳铺独立侧架候选：保留西式店与枪匠四帧原像素，右侧空木板由imagegen生成并按已授权本地裁切近邻缩放拼接；176x80四帧、pivot64,2、PPU32，板36x32，真枪x2.8125/y.25/.5/.75。'
 '实际2.4居民/工具碰撞资产发现旧预览最高枪不可达，已压低，未改碰撞体。占地center+.75/half2.75，付款仍店中央；整数stockSlot分配，无效重复拒绝收费；仅同世界完整身份就绪、playing、非保存且未领取/未友敌认领的确证架枪一次重锚，掉枪-1不搬，重摆维护复用既有名册0.5s；日常购买检查最多3架枪且不读取全体Unit原生状态，不新增全场扫描或因无关历史Unit未知锁手动商店。'
 f'候选{audit["candidateSha256"][:8].lower()}，build=9.0.0-musketeer-side-rack-20260917。代码/针对回归、actual2.4构建、资源/方法审计和独立复核通过。仅侧架3生产文件与build标记/商店PNG变更，其余7PNG及其他玩法保持。')
receipt=task/'receipts/install.json'
if receipt.exists():
 install=json.loads(receipt.read_text(encoding='utf-8-sig'))
 summary+=f'已闭游戏备份同步既定E独立副本，{install["userDataFileCount"]}份原生档/附加档/配置hash保持；未启动游戏。'
else:summary+='游戏运行中未替换DLL，候选待退出后安装。'
summary+='未commit/push/publish，公开9.0.0不变；实际最高层领取、多枪同时接触、旧档重锚/美术观感待实机，保持doing。不涉及此前blockedHero/Crossbow诊断。'
block=f'<!-- {tag} -->\n{summary}\n<!-- {tag} -->\n'
for rel in ['AGENTS.md','docs/project-harness/progress.md','docs/project-harness/current/task_plan.md','docs/project-harness/current/review.md','docs/project-harness/current/closeout-packet.md','docs/project-harness/current/handoff-packet.md','docs/project-harness/domain-model.md','docs/project-harness/game-logic-map/patch-patterns.md']:
 p=repo/rel;s=p.read_text(encoding='utf-8-sig');pat=rf'<!-- {tag} -->.*?<!-- {tag} -->\n?'
 s=re.sub(pat,lambda _:block,s,count=1,flags=re.S) if re.search(pat,s,re.S) else block+'\n'+s
 p.write_text(s,encoding='utf-8')
p=repo/'docs/project-harness/harness-checklist.json';o=json.loads(p.read_text(encoding='utf-8-sig'))
item=next((i for i in o['items'] if i['id']==tag),None)
if item is None:
 item=dict(id=tag,title='火铳铺独立侧枪架',status='doing',priority='p1',owner='codex',selected_in_session='codex-'+tag,updated_at='2026-09-17',dependencies=[],blocked_by=[],blocked_reason=None,acceptance='原店像素保持，侧板真库存与拾取/占地对应，旧枪有证重锚，回归构建审核及实机')
 o['items'].insert(0,item)
item['verification']=summary;item['handoff']='按receipts确认是否安装；游戏运行时不替换。最高层和多枪领取、暂停读档/旧库存及观感待正常实机。'
p.write_text(json.dumps(o,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(task/'acceptance.md').write_text('# 侧枪架候选验收\n\n'+summary+'\n\n详见result.md、review.md、receipts与artifacts/shops/20260917-side-rack/layout.json。\n',encoding='utf-8')
print('Side rack status synchronized; runtime acceptance remains doing.')
