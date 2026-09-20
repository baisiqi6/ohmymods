from pathlib import Path
from datetime import datetime
import json,hashlib
task=Path(__file__).resolve().parent;root=task.parents[3];h=root/'docs/project-harness'
operator=Path('C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/hero-save-restore-20260915')
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
receipt=read(task/'receipts/install.json');audit=read(task/'receipts/dll-audit.json');repair=read(task/'receipts/recovery-staged.json')
sha=hashlib.sha256((operator/'candidate/KingdomEnhancedMod.dll').read_bytes()).hexdigest()
assert sha.upper()==receipt['dllSha256']==audit['candidateSha256']
assert receipt['nativeAndOtherDataUnchanged'] and audit['harmonyPatchTypesUnchanged']
stamp='8.0.0-hero-save-restore-20260915'
summary=(f'2026-09-15 英雄购买读档恢复候选已闭游戏备份安装正确E盘：{sha[:8]} / build={stamp}。'
 '根因确认：旧scope使用IslandSaveData.realStartDateTime.Ticks，但该字段未进入原生存档，每次读档重建，已付记录存在却被新scope漏取。'
 '英雄附加档升schema2：稳定文件/战役/挑战/land上下文与opaque epoch，legacy来源逐快照保留，精确回退搜索未归属旧scope，v2已确认空记录优先于legacy付费，冲突/身份不明锁槽不收费。'
 '新指纹仅排除三个顶层游玩计时字段，完整人物/钱包/建筑和其他字段仍参与；原生异步落盘的时钟漂移是旧最新快照不匹配的候选原因，未称唯一确因。'
 '当前旧v1无精确匹配，因此另用冻结原生SHA/精确岛JSON、实际购买与保存日志、唯一NPC记录作一次本机MOD侧修复：保留93c12776与9265e66e两笔原购买及全部旧快照，未改原生进度/生命值/金币。'
 '243购买/回退/日期变化/真实fixture绑定回归、9真实岛指纹检查、完整实际2.4构建0警告0错误，独立复核通过。'
 'DLL审计2904旧方法保持、27改变、98新增、42签名或闭包替换移除，修改限英雄持久化与build文字，Harmony类型和4PNG保持。'
 '备份与安装摘要核对完成，原生存档及其他配置保持；未启动游戏/提交/发布，公开8.0.0不变。实机下次读档找回两英雄仍待验证，跨岛运输仍待，旧透明遮挡/邻居跳动不因此宣称修好。')
limits=('原版重存造成完整人物数据变化且无精确匹配时保留名额并暂停收费，不按临时ID猜身份；最多64上下文、8epoch/上下文，满额保留历史并拒绝扩张。'
 '同指纹不同付费数据拒绝覆盖并保留当前运行身份，进入只读；真实购买会改变原生Wallet，当前未观察该冲突。'
 '回归中的角色使用可控stub，实际2.4 DLL编译与真实保存fixture不能代替游戏实测。骑士也使用旧运行期scope helper，此次未扩改骑士身份，留后续专门调查。')
review=('独立reviewer hero_store_flags_review最终通过：迁移回退、权威空记录、来源不洗白、单context归属、原子提交与容量、窄指纹、一次性恢复和DLL审计。'
 'Operator修正四处测试夹具/期望错误；新增真实当前恢复绑定/日期与时钟变化、同指纹冲突拒绝写入回归。'
 '恢复stager补最后saved日志前缀与来源快照一致、空alias baseline一致断言；安装前后重新解压原生JSON并核对原文和SHA。')
tests={'recruitmentAssertions':243,'realFingerprintAssertions':9,'fullBuildWarnings':0,'fullBuildErrors':0}
(task/'receipts/verification.json').write_text(json.dumps(dict(tests=tests,review=review,installed=True,actualGameReloadVerified=False),ensure_ascii=False,indent=2),encoding='utf8')
sources={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in [root/'il2cpp'/n for n in ['HeroRecruitment.cs','HeroRecruitmentArchive.cs','HeroRecruitmentContext.cs','HeroRecruitmentFingerprint.cs','KingdomEnhancedPlugin.cs']]}
(task/'receipts/source-hashes.json').write_text(json.dumps(sources,indent=2),encoding='utf8')
metadata=[]
for p in (operator/'sessions').glob('*.jsonl'):
    for line in p.read_text(encoding='utf-8-sig').splitlines():
        try:e=json.loads(line)
        except ValueError:continue
        if e.get('type') in ('session','model_change','thinking_level_change'):metadata.append(e)
(task/'receipts/worker-metadata.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2),encoding='utf8')
(task/'plan.md').write_text('# 英雄购买保存修复\n\n'+summary+'\n\n待实机：正常加载，两侧已购英雄/占位旗，正常付费死亡补位，再保存退出重进；旧存档回退及换岛验证。\n',encoding='utf8')
(task/'review.md').write_text('# 独立复核\n\n'+review+'\n\n'+limits+'\n',encoding='utf8')
(task/'acceptance.md').write_text('# 本机候选交付\n\n'+summary+'\n\n'+limits+'\n\n'+review+'\n\nDLL备份：'+receipt['backup']+'\n\n原附加档备份：'+receipt['sidecarBackup']+'\n\n安装过程：首次PowerShell空backup参数被拒，未修改sidecar；显式唯一备份路径续接，写前后证据全核通过。\n',encoding='utf8')
manifest=dict(build=stamp,dllSha256=sha,installed=True,published=False,gameStarted=False,nativeAndOtherDataUnchanged=True,
 tests=tests,sidecarSha256=repair['recoveredSidecarSha256'],pending=['actual game reload','cross-island transport','earlier occlusion and neighbor jumping acceptance'],createdAt=datetime.now().astimezone().isoformat())
(operator/'candidate/candidate.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
(operator/'candidate/KingdomEnhancedMod.dll.sha256').write_text(sha+'  KingdomEnhancedMod.dll\n',encoding='utf8')
(operator/'candidate/本机候选更新说明.txt').write_text('8.0.0 本机英雄购买保存修复候选\n\n购买记录跨读档保持稳定；恢复本机当前两笔已付英雄购买，旧记录保留。\n原生存档与金币不变。未公开发布。\n实际重新进入游戏仍待验证。\n',encoding='utf8')
marker='<!-- hero-save-restore-20260915 -->'
for path,first in [(root/'AGENTS.md',True),(h/'progress.md',True),(h/'domain-model.md',False),(h/'game-logic-map/patch-patterns.md',False),
                   (h/'current/task_plan.md',True),(h/'current/review.md',True),(h/'current/closeout-packet.md',True)]:
    old=path.read_text(encoding='utf-8-sig');assert marker not in old
    block=marker+'\n'+summary+'\n'+marker+'\n\n'
    path.write_text(block+old if first else old+'\n\n'+block,encoding='utf8')
p=h/'harness-checklist.json';data=read(p)
assert not any(i['id']=='hero-save-restore-20260915' for i in data['items'])
data['items'].append(dict(id='hero-save-restore-20260915',title='英雄购买附加存档跨读档恢复',status='doing',priority='p1',owner='codex',
 selected_in_session='codex-hero-save-restore-20260915',updated_at='2026-09-15',dependencies=[],blocked_by=[],blocked_reason=None,
 acceptance='重复读档与保存保持购买；旧记录精确恢复；不影响原生数据；实机确认英雄及占位',verification=summary,
 handoff='本机候选与两笔购买关联恢复已安装，下一次真实读档验收待做；不擅自启动或发布。',
 artifacts={k:f'docs/project-harness/tasks/hero-save-restore-20260915/{v}' for k,v in {'plan':'plan.md','acceptance':'acceptance.md','review':'review.md','install':'receipts/install.json'}.items()}))
shop=next(i for i in data['items'] if i['id']=='hero-shop-20260915')
shop['handoff']='购买持久化根因与当前本机恢复见hero-save-restore-20260915，已安装待实际读档。Esc确认修好；跨岛运输和其他实机项保留。'
p.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
print(json.dumps({'installed':True,'sha':sha,'recoveredExistingPurchases':2,'nativeDataUnchanged':True,'gameStarted':False}))
