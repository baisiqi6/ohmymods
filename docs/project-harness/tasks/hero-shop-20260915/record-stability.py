from pathlib import Path
import hashlib, json

task = Path(__file__).resolve().parent
root = task.parents[3]
harness = root / 'docs/project-harness'
audit = json.loads((task / 'receipts/stability/dll-audit.json').read_text(encoding='utf-8-sig'))
candidate = Path('C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/hero-shop-20260915/candidate-stability')
sha = hashlib.sha256((candidate / 'KingdomEnhancedMod.dll').read_bytes()).hexdigest()
assert sha.upper() == audit['candidateSha256']
install_path = task / 'receipts/stability/install.json'
installed = install_path.exists()
if installed:
    receipt = json.loads(install_path.read_text(encoding='utf-8-sig'))
    assert receipt['dllSha256'].lower() == sha and receipt['userDataUnchanged']
state = '已在游戏关闭后备份安装正确E盘，存档和配置hash保持' if installed else '候选就绪，游戏运行中尚未安装，等待用户保存退出'
summary = ('2026-09-15 英雄驿站暂停闪烁修复：'+sha[:8]+' / build=8.0.0-hero-shop-stability-20260915。'
    '用户确认主要开关暂停菜单整座消失；旧TryContext只接受Playing导致Menu清理重建。现仅精确Playing/Menu、同kingdom/layer/当前Postbox/header且对象有效时保留；'
    'Menu阻止付款，首次暂停有待付币时复用原生取消路径，成功后标记，恢复复用原对象；其他状态和未知context立即清理并记录原因。'
    'Core54、Invoker7、实际2.4 interop与完整构建通过（后两者0警告0错误），生产接线审计和独立审核通过。'
    '对d005审计2861方法保持、8改变、8新增、3移除（含Clear签名与闭包编号调整），4PNG保持，地面基准与owner ABI保留。'
    +state+'；未启动游戏/提交/发布。暂停保留、半途投币暂停、恢复付款与实际高度仍待实机验收，跨岛身份运输仍未完成。')
manifest = dict(build='8.0.0-hero-shop-stability-20260915', dllSha256=sha, installed=installed,
    published=False, gameStarted=False, verified=dict(core=54, invoker=7, actualInterop='0W0E', fullBuild='0W0E', wiring='pass', independentReview='pass'),
    pending=['live pause/resume and pending-payment cancellation', 'visual ground-height acceptance', 'cross-island transport'])
(candidate/'candidate.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
(candidate/'KingdomEnhancedMod.dll.sha256').write_text(sha+'  KingdomEnhancedMod.dll\n',encoding='utf8')
(candidate/'修复说明.txt').write_text('英雄驿站暂停闪烁修复\n\n'+summary+'\n\n验证提示：开关暂停应依次出现 pause: retain shop 和 resume: retained same shop，不能伴随新的 ready 或 clear；半途投币暂停须实测退币且不扣重。\n',encoding='utf8')
report = '# 英雄驿站暂停闪烁修复验收\n\n'+summary+'\n\n'
report += ('OMP 18.1.19 / deepseek/deepseek-v4-flash / max worker完成有界实现，operator将保留策略收紧为精确Menu并删除2秒未知上下文宽限。'
    '独立reviewer已复核最终代码，无阻断。源码策略测试和Cecil接线检查不是Unity实例实测。Invoker隔离测试带未使用Probe字段警告，不影响7项指针/JIT检查。\n\n'
    '日志基线 receipts/stability/before.log 显示同址反复ready；用户确认暂停相关。2.4原生Playing/Menu调查见stability-plan.md。'
    '此候选沿用原有付款结算/原生取消实现、沉地修复和IntPtr owner ABI，不改变资产、存档或战斗行为。\n\n'
    '实际验收：连续开关暂停、靠近商店半途投币暂停、返回后正常购买；对照pause/resume/clear原因日志和视觉。关闭功能/离岛须正常清理。\n')
(task/'stability-acceptance.md').write_text(report,encoding='utf8')
marker='<!-- hero-shop-stability-20260915 -->'
def record(path, prepend=False):
    old=path.read_text(encoding='utf-8-sig')
    if marker in old:
        before,tail=old.split(marker,1)
        _,after=tail.split(marker,1)
        old=before+after
    block=marker+'\n'+summary+'\n'+marker+'\n'
    path.write_text(block+'\n'+old if prepend else old+'\n'+block,encoding='utf8')
for name in ('progress.md','domain-model.md','game-logic-map/patch-patterns.md'):
    record(harness/name)
for name in ('current/task_plan.md','current/review.md','current/closeout-packet.md'):
    record(harness/name,True)
record(task/'plan.md',True)
record(root/'AGENTS.md',True)
p=harness/'harness-checklist.json'
data=json.loads(p.read_text(encoding='utf-8-sig'))
item=next(i for i in data['items'] if i['id']=='hero-shop-20260915')
item.update(status='doing',verification=summary,handoff=state+'；暂停/付款/高度待实测，跨岛仍未完成。')
item['artifacts']['stability']='docs/project-harness/tasks/hero-shop-20260915/stability-acceptance.md'
p.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
p=root/'tests/hero-shop/README.md'
text=p.read_text(encoding='utf-8-sig').replace('; a live shop whose manager context is briefly unreadable is retained for at most `ContextHoldSeconds` (2 s) before it is cleared as `context-lost`', '; unreadable context or any game state other than exact Playing/Menu clears immediately').replace('`Clear()` at shutdown','`Clear(reason)` at shutdown')
p.write_text(text,encoding='utf8')
p=task/'stability-worker-result.md'
note='Operator final adjustment: only exact Playing/Menu is retained; unreadable context clears immediately. The proposed 2-second grace and broad non-playing retention below are superseded. See stability-acceptance.md.\n\n'
old=p.read_text(encoding='utf-8-sig')
if not old.startswith('Operator final adjustment:'): p.write_text(note+old,encoding='utf8')
print(json.dumps({'installed':installed,'sha256':sha}))
