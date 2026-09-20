from pathlib import Path
from datetime import datetime
import json,hashlib
task=Path(__file__).resolve().parent;root=task.parents[3];harness=root/'docs/project-harness'
receipt=json.loads((task/'receipts/grounding/install.json').read_text(encoding='utf-8-sig'))
audit=json.loads((task/'receipts/grounding/dll-audit.json').read_text(encoding='utf-8-sig'))
assert receipt['dllSha256']==audit['candidateSha256'] and receipt['userDataUnchanged']
out=Path('C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/hero-shop-20260915/candidate-grounding')
assert hashlib.sha256((out/'KingdomEnhancedMod.dll').read_bytes()).hexdigest()==receipt['dllSha256'].lower()
summary=('2026-09-15 英雄驿站沉地修复已闭游戏备份安装E盘：d005c4e1 / build=8.0.0-hero-shop-grounding-20260915。'
         '前版f8c25095已真实通过owner预检、ready与purchasecompleted，但用户截图下半部被地面挡。实际94商店资源rootY0.875/0.88、'
         'rootbody bottompivot0/PPU32，旧自有root错用GameLayer.y。现仅取当前world活动Bow/Hammer/Scythe、根body可用且pivot0的worldY，'
         '主体/旗/币槽一起抬升，缺参考延后；保留自有pivot2px及底边约1px草沿，不改PNG/横向选址/z/付款。'
         'Core39/Invoker7/actualinterop与完整build0W0E、独立资源/代码review通过；对f8审计2868旧方法全同，'
         '仅Create和buildstamp改变、2新增方法、4PNG保持。备份/安装hash与全部用户数据hash通过，未启动/提交/发布；新高度仍待用户截图实测，跨岛仍待。')
manifest={'build':'8.0.0-hero-shop-grounding-20260915','dllSha256':receipt['dllSha256'],
          'installed':True,'published':False,'gameStarted':False,'userDataUnchanged':True,
          'change':'sample current-world standard equipment-shop root world y instead of game-layer origin',
          'verified':{'core':39,'invoker':7,'fullBuild':'0W0E','actualInterop':'0W0E','oldMethodsUnchanged':2868,'oldMethodsChanged':2,'newMethods':2,'embeddedPngs':'all4 unchanged','review':'passed'},
          'previousLiveValidation':['f8c25095 owner preflight passed','f8c25095 shop ready','f8c25095 purchase completed'],
          'pending':['new ground height in actual game','cross-island identity transport'],'createdAt':datetime.now().astimezone().isoformat()}
(out/'candidate.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
(out/'KingdomEnhancedMod.dll.sha256').write_text(receipt['dllSha256'].lower()+'  KingdomEnhancedMod.dll\n',encoding='utf8')
(out/'修复说明.txt').write_text('英雄驿站高度修复\n\n'+summary+'\n\n下一次游戏应看到[HeroShop] ground source=native-root，rootY与finalY使用同一个已存在原生商店根基准。这里只修纵向位置，购买记录/存档/配置保留。\n',encoding='utf8')
report=('# 英雄驿站高度修复验收\n\n'+summary+'\n\n'
        '## 原因与证据\n\n用户截图见receipts/grounding/before.png，原版资源提取见resource-grounding.json。'
        '93对象Y=0.875、1对象Y≈0.88，94body root/pivot0/32PPU。自有alpha bbox(2,14,124,79)、pivot底2px不变；'
        '最低可见边在根下约1px，不把这个正常草沿余量与旧28px整体沉地混为一谈。\n\n'
        '## 实施与审查\n\nOMP18.1.19 DeepSeekV4Flash/max worker在授权HeroShop/tests范围实现，model_change非fallback。'
        'worker自身bash权限拒绝后由operator直接完成全部验证。Operator按review收窄为Bow/Hammer/Scythe及rootbody/pivot条件，'
        '独立reviewer重读冻结版本通过。外部worker已结束，无继续后台实施。\n\n'
        'Core39、Invoker7、actualinterop与完整构建通过；精确DLL方法对照见receipts/grounding/dll-audit.json。'
        '没有改变原生对象、购买身份/战斗、同root局部旗帜位置和币槽配置，没有额外hook。\n\n'
        '## 本机安装\n\n目标：`'+receipt['target']+'`\n\n备份：`'+receipt['backup']+'`\n\n'
        '安装时进程关闭，前后用户文件hash一致，没有操作游戏启动。该高度修正还需下一次真实截图验证；此前ready和purchase只证明owner修复及实际商店付款正例。\n')
(task/'grounding-acceptance.md').write_text(report,encoding='utf8')
for name in ('progress.md','domain-model.md','game-logic-map/patch-patterns.md'):
    p=harness/name;p.write_text(p.read_text(encoding='utf-8-sig')+'\n\n### 2026-09-15 英雄驿站地面基准\n\n'+summary+'\n',encoding='utf8')
for name in ('current/task_plan.md','current/review.md','current/closeout-packet.md'):
    p=harness/name;p.write_text(summary+'\n\n详见tasks/hero-shop-20260915/grounding-acceptance.md。\n\n'+p.read_text(encoding='utf-8-sig'),encoding='utf8')
p=harness/'harness-checklist.json';data=json.loads(p.read_text(encoding='utf-8-sig'));item=next(i for i in data['items'] if i['id']=='hero-shop-20260915')
item.update(status='doing',verification=summary,handoff='d005高度修复已安装，等用户实际截图；owner/ready/付款前版已获正例，跨岛运输仍未完成。')
item['artifacts']['grounding']='docs/project-harness/tasks/hero-shop-20260915/grounding-acceptance.md'
p.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
p=root/'AGENTS.md';text=p.read_text(encoding='utf-8-sig');marker='### 2026-09-06 启动事故临时门禁\n';assert marker in text
p.write_text(text.replace(marker,marker+'- '+summary+'\n',1),encoding='utf8')
p=task/'plan.md';p.write_text('最新：'+summary+'\n\n'+p.read_text(encoding='utf-8-sig'),encoding='utf8')
print(json.dumps({'installed':True,'sha':receipt['dllSha256'],'userFilesChecked':len(receipt['userDataHashes']),'gameStarted':False}))
