from pathlib import Path
from datetime import datetime
import hashlib
import json
import shutil

task = Path(__file__).resolve().parent
root = task.parents[3]
harness = root / 'docs/project-harness'
out = Path('C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/hero-shop-20260915/candidate')
out.mkdir(parents=True, exist_ok=True)
binary = root / 'il2cpp/bin/Debug/KingdomEnhancedMod.dll'
digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
audit = json.loads((task/'receipts/dll-audit.json').read_text(encoding='utf-8-sig'))
assert digest(binary).lower() == audit['candidate'].lower()
assert audit['unchangedMethods'] == 2665 and len(audit['changedMethods']) == 10
installed = Path('E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll')
assert digest(installed) == '80522bf18952fe609c4f6f91fd6b52c26ab79cbf87ee918159770306953348f4'
for source in (binary, task/'候选说明.txt'):
    shutil.copy2(source, out/source.name)
manifest = {'status':'local-development-candidate-not-installed-not-published',
            'build':'8.0.0-hero-shop-20260915', 'dllSha256':digest(binary),
            'installedDllSha256':digest(installed), 'assemblyVersion':'8.0.0.0',
            'sourceState':'uncommitted-canonical-workspace',
            'verified':['IL2CPP build 0 warnings 0 errors','recruitment runtime/archive 87 assertions',
                        'independent review 93 assertions (87 plus 6)','hero effects 81 assertions',
                        'shop core 31 assertions','shop actual 2.4 interop compile',
                        'combat xUnit 146','Greek impact xUnit 85',
                        '2665 old methods unchanged; 10 reviewed integration changes; two old PNG resources unchanged'],
            'pending':['campaign-vs-island seat scope selection','cross-island transport identity bridge',
                       'actual game injection, payment/refund, positioning, visual and save/load validation'],
            'userDataModified':False,'gameStarted':False,'createdAt':datetime.now().astimezone().isoformat()}
(out/'candidate.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(out/'KingdomEnhancedMod.dll.sha256').write_text(digest(binary)+'  KingdomEnhancedMod.dll\n',encoding='utf-8')
shutil.copy2(out/'candidate.json',task/'receipts/candidate.json')
summary = ('2026-09-15 英雄驿站当前岛候选完成，未安装/发布：8币训练现有弓手、每侧固定1席直到确认死亡；'
           '关闭/塔/丢弓转职/临时停用保留购买，真实回池不串人。独立sidecar精确快照+pin基线，修原生落盘失败回退锁槽、'
           '原版重存未知身份静默丢失、新岛初始化及OnEnable误解绑；未知身份保留名额并暂停收费。'
           '原创512x80四帧商店贴图，原英雄2PNG/围巾/金箭保留。build0W0E、87购买/81效果/31商店/146+85xUnit、'
           'actualinterop及独立93回归通过；DLL审计2665旧方法不变/10集成修改，候选SHA '+digest(binary)[:8]+'. '
           '跨岛名额范围待用户选择，运输身份桥和实机验收未完成；E盘仍80522bf1正式8.0.0，未操作游戏或用户数据。')
acceptance = '# 英雄驿站当前岛候选验收\n\n'+summary+'\n\n'
acceptance += '候选目录：`'+str(out)+'`。候选说明及SHA256与DLL一同保存。\n\n'
acceptance += '## 已验证\n\n- '+ '\n- '.join(manifest['verified'])+'\n\n'
acceptance += ('## 独立审查\n\n'
    'reviewer两条隔离失败复现已修复：IslandSave完成/Global磁盘未提交后读旧零席快照；已购正常保存但未再次MOD读档就被原版重存。'
    '另核对新岛零基线、真实FastDespawn后解绑和非回池OnEnable身份保留。最终93断言通过。'
    '审查冻结HeroRecruitment SHA256 73DB1341EE69C2C48D78B3F353C16C2B5F698E94F3B4C9500A5740E67477E33F。\n\n'
    '## 未完成\n\n'
    '用户已收到名额范围选项：整个战役共2个，或每岛各2个并处理目标岛容量。尚未回复，不自行迁移匿名身份。'
    '原版CarryForward仅有人数与工具标志；跨岛运输身份桥尚未实现，不能说英雄随船跨岛已经可用。'
    '真实接口注入、投币/退款、选址/比例、生成岛、死亡和保存读档尚未实机验证。'
    '因此本任务doing，当前候选不安装、不发布。\n')
(task/'acceptance.md').write_text(acceptance,encoding='utf-8')
review = '# 英雄驿站独立审查\n\n当前岛候选代码审查通过。\n\n'+acceptance.split('## 独立审查\n\n',1)[1]
(task/'review.md').write_text(review,encoding='utf-8')
data = json.loads((harness/'harness-checklist.json').read_text(encoding='utf-8-sig'))
item = next(x for x in data['items'] if x['id']=='hero-shop-20260915')
item.update(status='doing',verification=summary,
            handoff='等待用户确定跨岛名额范围，继续运输接续后再做实际游戏验证/安装；当前正式8.0不动。')
item['artifacts'].update(acceptance='docs/project-harness/tasks/hero-shop-20260915/acceptance.md',
                         review='docs/project-harness/tasks/hero-shop-20260915/review.md')
(harness/'harness-checklist.json').write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
for relative in ('progress.md','domain-model.md','game-logic-map/patch-patterns.md'):
    path=harness/relative
    path.write_text(path.read_text(encoding='utf-8-sig')+'\n\n### 2026-09-15 英雄驿站候选与后续边界\n\n'+summary+'\n',encoding='utf-8')
for relative in ('current/task_plan.md','current/review.md','current/closeout-packet.md'):
    path=harness/relative
    path.write_text(summary+'\n\n后续：先接收跨岛名额选择，再完成运输身份桥；不得把当前岛验收推广为跨岛或实机已完成。\n\n'+path.read_text(encoding='utf-8-sig'),encoding='utf-8')
agents=root/'AGENTS.md'
text=agents.read_text(encoding='utf-8-sig')
marker='### 2026-09-06 启动事故临时门禁\n'
assert marker in text
agents.write_text(text.replace(marker,marker+'- '+summary+'\n',1),encoding='utf-8')
print(json.dumps({'candidate':str(out),'sha256':digest(binary),'installed':'unchanged official 8.0.0'},ensure_ascii=False))
