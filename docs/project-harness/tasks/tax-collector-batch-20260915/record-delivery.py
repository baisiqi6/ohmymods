from pathlib import Path
from datetime import datetime
import json,hashlib
task=Path(__file__).resolve().parent;root=task.parents[3];h=root/'docs/project-harness'
operator=Path('C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/tax-collector-batch-20260915')
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
receipt=read(task/'receipts/install.json');audit=read(task/'receipts/dll-audit.json');verification=read(task/'receipts/verification.json')
sha=hashlib.sha256((operator/'candidate/KingdomEnhancedMod.dll').read_bytes()).hexdigest()
assert sha.upper()==receipt['dllSha256']==audit['candidateSha256']
assert receipt['userDataUnchanged'] and audit['harmonyPatchTypesUnchanged'] and verification['passed']
summary=(f'2026-09-15 税收助手连续收币候选已闭游戏备份安装正确E盘：{sha[:8]} / build=8.0.0-tax-collector-batch-20260915。'
 '用户希望每趟约20枚。旧实际容量至少100，少量回家来自成熟快照暂时耗尽就立即收工。现每趟20枚，已有收获且未满时断流原地等4.2秒（3秒成熟+2次0.6秒扫描），deadline不被空扫描不断延长，续收后重置；空手无目标立即退出。'
 '场上零币CleanupNoCandidates与接链统一策略，保留认领清理门；第20枚回家后立即终止本帧扫币，不因携带计数归零再吃第21枚。'
 '只在希腊authority生效，补货租用/归还、演员替换、回池/失权/离场清等待；原入账事务不改，回家不重复入账。'
 '当前英雄保存恢复、动作、商店及4PNG保持。针对回归、实际2.4完整构建与独立复核通过，详情见本任务receipts；DLL方法审计限制银行助手与build标记，无新增Hook类型。'
 '全部原生存档/附加档/配置hash保持，未启动游戏/提交/发布。实际连续扔20枚、停扔等待回家、正常补货与暂停仍待游戏验证，公开8.0.0不变。')
review=('author_impact_reviewer独立审查计划与最终实现，重点核验零币清理入口、20th归零后的终止、等待生命周期和补货租用。'
 '第一次OMP worker越界尝试配置工具权限，已停止并精确撤回创建的项目config/probe及global插入块；scope-incident.json记录。'
 '重新派发限制read/edit/write、禁扩展/技能的OMP deepseek-v4-flash max，实际native模型事件核验，无fallback。')
(task/'plan.md').write_text('# 税收助手连续收币\n\n'+summary+'\n\n用户实际观感验收仍待，不将离线回归称实机通过。\n',encoding='utf8')
(task/'review.md').write_text('# 复核与边界\n\n'+review+'\n\n结果见receipts/verification.json与dll-audit.json。\n',encoding='utf8')
(task/'acceptance.md').write_text('# 本机候选交付\n\n'+summary+'\n\n'+review+'\n\n备份：'+receipt['backup']+'\n',encoding='utf8')
manifest=dict(build=receipt['build'],dllSha256=sha,installed=True,published=False,gameStarted=False,userDataUnchanged=True,
 tests=verification,pending=['actual continuous dropping / idle return / restock'],createdAt=datetime.now().astimezone().isoformat())
(operator/'candidate/candidate.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
(operator/'candidate/KingdomEnhancedMod.dll.sha256').write_text(sha+'  KingdomEnhancedMod.dll\n',encoding='utf8')
(operator/'candidate/本机候选更新说明.txt').write_text('8.0.0 本机税收助手收币候选\n\n每趟收集20枚；连续扔币中断时最多等约4秒，停止扔币后正常回家。只影响希腊世界税收助手，金币仍拾取时入账，不重复记账。保留英雄保存恢复及现有修复。实际游戏体验待验，未公开发布。\n',encoding='utf8')
meta=[]
for p in (operator/'retry-sessions').glob('*.jsonl'):
    for line in p.read_text(encoding='utf-8-sig').splitlines():
        try:e=json.loads(line)
        except ValueError:continue
        if e.get('type') in ('session','model_change','thinking_level_change'):meta.append(e)
(task/'receipts/worker-metadata.json').write_text(json.dumps(meta,ensure_ascii=False,indent=2),encoding='utf8')
marker='<!-- tax-collector-batch-20260915 -->'
for path,first in [(root/'AGENTS.md',True),(h/'progress.md',True),(h/'domain-model.md',False),(h/'game-logic-map/patch-patterns.md',False),
                   (h/'current/task_plan.md',True),(h/'current/review.md',True),(h/'current/closeout-packet.md',True)]:
    old=path.read_text(encoding='utf-8-sig');assert marker not in old
    block=marker+'\n'+summary+'\n'+marker+'\n\n';path.write_text(block+old if first else old+'\n\n'+block,encoding='utf8')
p=h/'harness-checklist.json';data=read(p)
assert not any(i['id']=='tax-collector-batch-20260915' for i in data['items'])
data['items'].append(dict(id='tax-collector-batch-20260915',title='税收助手每趟20枚及断流等待',status='doing',priority='p1',owner='codex',
 selected_in_session='codex-tax-collector-batch-20260915',updated_at='2026-09-15',dependencies=[],blocked_by=[],blocked_reason=None,
 acceptance='连续扔币每趟20枚、短间隔不回家、停扔有限等待后收工；经济和希腊范围保持；实机验证',verification=summary,
 handoff='本机候选已安装，连续投币、停投回家、补货和暂停待实机；不擅自启动或发布。',
 artifacts={k:f'docs/project-harness/tasks/tax-collector-batch-20260915/{v}' for k,v in {'plan':'plan.md','acceptance':'acceptance.md','review':'review.md','install':'receipts/install.json'}.items()}))
p.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
print(json.dumps({'installed':True,'sha':sha,'userDataUnchanged':True,'gameStarted':False}))
