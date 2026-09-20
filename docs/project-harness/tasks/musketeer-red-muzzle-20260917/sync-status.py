from pathlib import Path
import json,re
task=Path(__file__).resolve().parent;repo=task.parents[3];tag=task.name
a=json.loads((task/'receipts/dll-audit.json').read_text(encoding='utf-8-sig'))
summary=('2026-09-17 侧边转交红色枪口喷焰，用户继而要求四五帧预览。最终5帧各50ms，Fire总.25秒保持，射速/装填/弹丸/伤害不改。'
         '现有可编辑像素source派生：root draw_preview.py只替换Fire口部红焰，worker负责67帧表/anchor/既有测试；备用worker生成器未使用。'
         'atlas尺寸672x192/56x32/PPU32/pivot31,2/.9保持，Fire36..40，Reload41/Lower53/Retreat59；new0..35及new41..66映射old40..65逐像素保持，身体枪手脚及x55空边保护，旧十字移除无叠层。'
         f'预览候选{a["candidateSha256"][:8].lower()} / build=9.0.0-musketeer-red-muzzle-20260917；79runtime、actual2.4接口/完整build0W0E、67anchor/9clip与像素保护及独立review通过；仅Atlas资源变化，其他7PNG和daylight/伤害功能保持。'
         '用户要先看动图，本轮尚未安装；既定E路径仍96566def白天补货版。未启动游戏、改存档配置、commit/push/publish，公开9.0.0保持。'
         '五帧红焰.gif为素材预览非游戏录像，实机观感/低帧率采样待验，任务doing。')
receipt=task/'receipts/install.json'
if receipt.exists():
    install=json.loads(receipt.read_text(encoding='utf-8-sig'))
    summary=summary.replace('用户要先看动图，本轮尚未安装；既定E路径仍96566def白天补货版。',f'用户后续采用预览后，已闭游戏备份安装，{install["userDataFileCount"]}份原生档/附加档/配置hash保持。')
block=f'<!-- {tag} -->\n{summary}\n<!-- {tag} -->\n'
for rel in ['AGENTS.md','docs/project-harness/progress.md','docs/project-harness/current/task_plan.md','docs/project-harness/current/review.md',
            'docs/project-harness/current/closeout-packet.md','docs/project-harness/current/handoff-packet.md','docs/project-harness/domain-model.md',
            'docs/project-harness/game-logic-map/patch-patterns.md']:
    p=repo/rel;s=p.read_text(encoding='utf-8-sig');pat=rf'<!-- {tag} -->.*?<!-- {tag} -->\n?'
    p.write_text(re.sub(pat,lambda _:block,s,count=1,flags=re.S) if re.search(pat,s,re.S) else block+'\n'+s,encoding='utf-8')
p=repo/'docs/project-harness/harness-checklist.json';data=json.loads(p.read_text(encoding='utf-8-sig'));item=next((i for i in data['items'] if i['id']==tag),None)
if item is None:
    item=dict(id=tag,title='火枪五帧红色喷焰预览',status='doing',priority='p2',owner='codex',selected_in_session='codex-'+tag,updated_at='2026-09-17',
              dependencies=[],blocked_by=[],blocked_reason=None,acceptance='5帧红焰/表与anchor映射/其他身体动作像素保持、原战斗时序保持，预览与实际2.4构建review及实机')
    data['items'].insert(0,item)
item['verification']=summary;item['handoff']='候选BCA6AAEB未安装，用户先看artifacts/musketeer/20260917-red-muzzle/五帧红焰.gif；采用后检查gameclosed再跑task/install-candidate.ps1。源码已接入候选但E仍96566DEF。'
p.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(task/'acceptance.md').write_text('# 五帧红焰预览候选\n\n'+summary+'\n',encoding='utf-8')
if receipt.exists():
    item['handoff']='BCA6AAEB已按用户确认闭游戏备份安装；28份存档/附加档/配置hash保持。实机五帧观感待验，公开版本不变。'
    p.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Five-frame red muzzle status synchronized; installed.' if receipt.exists() else 'Five-frame red muzzle preview status synchronized; candidate not installed.')
