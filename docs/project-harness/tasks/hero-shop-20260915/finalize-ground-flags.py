from pathlib import Path
from datetime import datetime
import json, hashlib, shutil
task=Path(__file__).resolve().parent
root=task.parents[3]
harness=root/'docs/project-harness'
out=Path('C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/hero-shop-20260915/candidate-ground-flags')
out.mkdir(parents=True,exist_ok=True)
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
binary=root/'il2cpp/bin/Debug/KingdomEnhancedMod.dll'
audit=json.loads((task/'receipts/dll-audit.json').read_text(encoding='utf-8-sig'))
assert sha(binary)==audit['candidate'].lower()
assert audit['unchangedMethods']==2665 and len(audit['changedMethods'])==10
installed=Path('E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll')
assert sha(installed)=='80522bf18952fe609c4f6f91fd6b52c26ab79cbf87ee918159770306953348f4'
for source in (binary,task/'候选说明.txt'):
    shutil.copy2(source,out/source.name)
manifest={'build':'8.0.0-hero-shop-flags-ground-20260915','dllSha256':sha(binary),
          'status':'local-candidate-not-installed-not-published',
          'verified':{'IL2CPP':'0 warnings 0 errors','recruitmentAssertions':109,'towerAssertions':31,
                      'heroEffectsAssertions':81,'shopCoreAssertions':31,'shopBannerInterop':'0 warnings 0 errors',
                      'oldMethodsUnchanged':2665,'oldIntegrationMethodsChanged':10,'oldHeroPngs':'unchanged',
                      'independentReview':'passed: native knight state mapping, tower policy and no-text banners'},
          'pending':['campaign-vs-island slot rule','cross-island identity transport','actual game test'],
          'tornFlagPersistence':'session-only feedback, never purchase authority',
          'inWorldTextLabels':False,'userDataModified':False,'gameStarted':False,
          'sourceState':'uncommitted workspace','createdAt':datetime.now().astimezone().isoformat()}
(out/'candidate.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
(out/'KingdomEnhancedMod.dll.sha256').write_text(sha(binary)+'  KingdomEnhancedMod.dll\n',encoding='utf8')
shutil.copy2(out/'candidate.json',task/'receipts/candidate-ground-flags.json')
summary=('2026-09-15 英雄驿站地面/旗帜候选 '+sha(binary)[:8]+' / build=8.0.0-hero-shop-flags-ground-20260915：'
         '已购启用英雄不接箭塔岗位，已在塔位通过原生Exit退出；普通补位、骑士任务和关闭恢复原生保留。'
         '用户拒绝文字牌，现改空挂点/原生投币→完整金弓红旗占位→确认死亡破旗，Reserved保留完整旗。'
         '旧装饰旗转为真实占位旗，无文字/运行字体；破旗仅会话反馈不影响持久购买。'
         '109购买状态/31塔/81效果/31商店回归、完整和实际interop构建0W0E、独立2.4原生/像素/代码review通过；'
         '2665旧方法保持/10集成修改、原英雄2PNG保持。未安装/启动/发布，E仍80522bf1正式8.0。'
         '跨岛名额范围未答、运输桥和实机未完成；最新候选在operator hero-shop-20260915/candidate-ground-flags。')
report=('# 英雄地面岗位与骑士式商店旗帜\n\n'+summary+'\n\n'
        '原生2.4 PayableShield：Empty可付/无旗；Available是付款后待领取盾牌；Taken完整旗展开；Broken破旗可付。'
        'set_status RVA0x678190，Pay0x6776E0，MakeAvailable0x676EC0，OnShieldTaken0x6776D0，OnKnightDestroyed0x677320，CanPay0x676B20。'
        '当前立即训练不会模拟一个不存在的待领取阶段。\n\n'
        '新增塔门的native证据见tests/hero-tower-policy/native-audit.json：IsAvailableForJob/AssignJob/SetGuardSlot/EnterGuardSlot均独址长方法。'
        'AssignJob含内联SetGuardSlot写入，不能只钩单个setter。同步ExitGuardSlot补员时仍拒英雄，普通补员保留。\n\n'
        '旗帜每0.25秒读一次只读状态，2个renderer、一张80x160图集和20缓存Sprite；0.6秒展开，完整旗轻摆，Reserved静态完整旗。'
        '旧文字脚本已禁用，文字草稿仅历史素材，不属于候选资源。\n\n'
        '已独立复核旗片顺序、透明度0/255、静态状态、资源清理和状态权威。未实机验证购买、死亡、撤塔或屏幕观感。\n\n'
        '候选目录：`'+str(out)+'`。旧70d53395候选保留为历史。任务仍doing；下一步接收跨岛范围选择并完成运输接续，再安装测试。\n')
(task/'ground-flags-acceptance.md').write_text(report,encoding='utf8')
for relative in ('progress.md','domain-model.md','game-logic-map/patch-patterns.md'):
    path=harness/relative
    path.write_text(path.read_text(encoding='utf-8-sig')+'\n\n### 2026-09-15 英雄地面岗位与旗帜状态\n\n'+summary+'\n',encoding='utf8')
for relative in ('current/task_plan.md','current/review.md','current/closeout-packet.md'):
    path=harness/relative
    path.write_text(summary+'\n\n详见tasks/hero-shop-20260915/ground-flags-acceptance.md。\n\n'+path.read_text(encoding='utf-8-sig'),encoding='utf8')
data=json.loads((harness/'harness-checklist.json').read_text(encoding='utf-8-sig'))
item=next(x for x in data['items'] if x['id']=='hero-shop-20260915')
item.update(status='doing',verification=summary,handoff='商店无文字占位旗和英雄不上塔已做本机候选；跨岛规则/运输/实机继续待办，未安装。')
item['artifacts']['ground_flags']='docs/project-harness/tasks/hero-shop-20260915/ground-flags-acceptance.md'
(harness/'harness-checklist.json').write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
agents=root/'AGENTS.md'; text=agents.read_text(encoding='utf-8-sig'); marker='### 2026-09-06 启动事故临时门禁\n'
assert marker in text
agents.write_text(text.replace(marker,marker+'- '+summary+'\n',1),encoding='utf8')
print(json.dumps({'path':str(out),'sha256':sha(binary),'installed':'unchanged official 8.0.0'}))
