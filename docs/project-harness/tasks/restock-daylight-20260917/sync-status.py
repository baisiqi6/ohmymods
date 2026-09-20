from pathlib import Path
import json,re
task=Path(__file__).resolve().parent;repo=task.parents[3];tag=task.name
audit=json.loads((task/'receipts/dll-audit.json').read_text(encoding='utf-8-sig'))
tests=json.loads((task/'receipts/verification-summary.json').read_text(encoding='utf-8-sig'))
summary=('2026-09-17 自动补货统一仅原生Kingdom.isDaytime白天生效，覆盖全部9类；夜间停止规划/借人/动画币/自动扣款，未付订单撤回释放，已付只收尾不退款/二次；'
         '保留已扣款不确定目标故障回执，天亮在原cadence重算缺口。调度/订单/native回调边界和TrySpendForAutoRestock最终commit前复核day，手动购买/普通银行/收税保持。'
         '面板显示白天补货及等待天亮，原Greek-only/host/双倍价/阈值库存门保持，无新增hook/扫描/配置项。'
         f'候选{audit["candidateSha256"][:8].lower()} / build=9.0.0-restock-daylight-20260917；{tests["passed"]}针对回归、actual2.4完整build0W0E、方法/8PNG审计和独立复核通过。')
receipt=task/'receipts/install.json'
if receipt.exists():
    install=json.loads(receipt.read_text(encoding='utf-8-sig'))
    summary+=f'已闭游戏备份安装既定E独立副本，{install["userDataFileCount"]}份原生档/附加档/配置hash保持，未启动游戏。'
else:summary+='候选就绪但尚未安装，游戏运行时不得替换DLL。'
summary+='保留火枪0.9、伤害可靠性及其余功能；未commit/push/publish，公开9.0.0不变。真实跨昼夜撤单/日出补货/余额与相关主客机仍待实机，doing不等于玩法验收完成。'
block=f'<!-- {tag} -->\n{summary}\n<!-- {tag} -->\n'
for rel in ['AGENTS.md','docs/project-harness/progress.md','docs/project-harness/current/task_plan.md','docs/project-harness/current/review.md',
            'docs/project-harness/current/closeout-packet.md','docs/project-harness/current/handoff-packet.md','docs/project-harness/domain-model.md',
            'docs/project-harness/game-logic-map/patch-patterns.md']:
    p=repo/rel;s=p.read_text(encoding='utf-8-sig');pat=rf'<!-- {tag} -->.*?<!-- {tag} -->\n?'
    p.write_text(re.sub(pat,lambda _:block,s,count=1,flags=re.S) if re.search(pat,s,re.S) else block+'\n'+s,encoding='utf-8')
p=repo/'docs/project-harness/harness-checklist.json';data=json.loads(p.read_text(encoding='utf-8-sig'));item=next((i for i in data['items'] if i['id']==tag),None)
if item is None:
    item=dict(id=tag,title='自动补货仅白天',status='doing',priority='p1',owner='codex',selected_in_session='codex-'+tag,updated_at='2026-09-17',
              dependencies=[],blocked_by=[],blocked_reason=None,acceptance='全部9类白天门/夜撤单不退款二次/日出恢复/最终扣款门/原玩法保持、针对回归与实际2.4构建review和实机')
    data['items'].insert(0,item)
item['verification']=summary;item['handoff']='见tasks/restock-daylight-20260917/result.md、review.md与receipts；实际昼夜边界和余额实机仍待验收，夜间保留普通收税与玩家手动购买。'
p.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(task/'acceptance.md').write_text('# 白天补货候选验收\n\n'+summary+'\n',encoding='utf-8')
print('Daylight restock status synchronized; in-game acceptance remains doing.')
