from pathlib import Path
import json,re
task=Path(__file__).resolve().parent;repo=task.parents[3];tag=task.name
audit=json.loads((task/'receipts/dll-audit.json').read_text(encoding='utf-8-sig'))
summary=('2026-09-17 现有所有世界长按连续购买开关加入投石车5币火药桶与希腊火塔2币弹药。'
 '只识别PayableWorkshopBarrel或同GO活动FireTower精确拥有的PayableComponent，火塔AI disabled不误排合法客机。'
 '弹药会话捕获owner并在加速/续买/等待回执/PerformPay/Tick复核，owner替换结束旧hold；复用原生扣款/容量/设施就绪/钱包/距离/暂停及PerformPay成功回执，客机本地Completed不视成功。'
 '默认off和首次正常/.6秒后加速节奏保持，不新增setting、hook、全场扫描或弹药库存写；只同步原面板/配置帮助文案。'
 f'候选{audit["candidateSha256"][:8].lower()}，build=9.0.0-hold-purchase-ammo-20260917。针对行为回归、actual2.4接口及完整构建、方法资源审计和独立复核通过；8PNG和侧枪架等其他玩法保持。')
receipt=task/'receipts/install.json'
if receipt.exists():
 install=json.loads(receipt.read_text(encoding='utf-8-sig'))
 summary+=f'已闭游戏备份安装既定E独立副本，{install["userDataFileCount"]}份原生档/附加档/配置hash保持；未启动游戏。'
else:summary+='候选已就绪，游戏运行时不得替换DLL。'
summary+='未commit/push/publish，公开9.0.0不变；真正连续购桶/满塔停止和主客机回执仍待实机，不把模拟测试当实测。'
block=f'<!-- {tag} -->\n{summary}\n<!-- {tag} -->\n'
for rel in ['AGENTS.md','docs/project-harness/progress.md','docs/project-harness/current/task_plan.md','docs/project-harness/current/review.md','docs/project-harness/current/closeout-packet.md','docs/project-harness/current/handoff-packet.md','docs/project-harness/domain-model.md','docs/project-harness/game-logic-map/patch-patterns.md']:
 p=repo/rel;s=p.read_text(encoding='utf-8-sig');pat=rf'<!-- {tag} -->.*?<!-- {tag} -->\n?'
 s=re.sub(pat,lambda _:block,s,count=1,flags=re.S) if re.search(pat,s,re.S) else block+'\n'+s
 p.write_text(s,encoding='utf-8')
p=repo/'docs/project-harness/harness-checklist.json';o=json.loads(p.read_text(encoding='utf-8-sig'))
item=next((i for i in o['items'] if i['id']==tag),None)
if item is None:
 item=dict(id=tag,title='长按购买增加桶与火塔弹药',status='doing',priority='p1',owner='codex',selected_in_session='codex-'+tag,updated_at='2026-09-17',dependencies=[],blocked_by=[],blocked_reason=None,acceptance='精确弹药白名单及owner有效性、原生付款成功链和off不干预、行为回归/actual2.4/独立复核与实机')
 o['items'].insert(0,item)
item['verification']=summary;item['handoff']='见result/review与receipts确认候选安装；实机检查5币桶/2币塔连购、满塔停止、松开/off和客户端延迟回包。'
p.write_text(json.dumps(o,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(task/'acceptance.md').write_text('# 弹药长按购买候选验收\n\n'+summary+'\n\n行为测试/2.4接口编译不是实机验证；详细证据见result.md、review.md与receipts。\n',encoding='utf-8')
print('Ammo hold-purchase status synchronized; in-game acceptance remains doing.')
