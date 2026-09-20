from pathlib import Path
from datetime import datetime
import hashlib,json,shutil
task=Path(__file__).resolve().parent;root=task.parents[3];harness=root/'docs/project-harness'
out=Path('C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/hero-shop-20260915/candidate-owner-fix')
sha=lambda path:hashlib.sha256(path.read_bytes()).hexdigest()
audit=json.loads((task/'receipts/owner-fix-dll-audit.json').read_text(encoding='utf-8-sig'))
assert sha(out/'KingdomEnhancedMod.dll')==audit['candidateSha256'].lower()
install=task/'receipts/owner-fix-install.json'
installed=install.exists() and json.loads(install.read_text(encoding='utf-8-sig'))['dllSha256']==audit['candidateSha256']
state='已闭游戏备份安装正确E盘，用户存档/配置hash保持' if installed else '修复候选已就绪；用户游戏运行中，尚未替换DLL'
summary=('2026-09-15 英雄驿站实际未出现已定位并修候选f8c25095 / build=8.0.0-hero-shop-owner-interop-20260915。'
         'a6b5实际日志类型注册后InvalidProgramException；根因为本机ClassInjector对out enum生成ldobj LockReason&非法Invoker，'
         '注册/Marshal成功直到首次JIT才失败。只改自有IsLocked为ABI等价IntPtr输出桥，精确WriteInt32(NotLocked21)，'
         '保留原interface预检并核写回，增加首异常阶段/完整栈。7真实production指针/JIT/guard断言、31商店、'
         '完整和actualinterop0W0E、独立复现与review通过；2862方法不变/4方法改动，旧IsLocked换签名+编译器闭包编号变化，'
         '全部4PNG保持。'+state+'；未启动游戏/公开发布，preflight passed+ready及实际投币待验证。跨岛仍未完成。')
manifest={'build':'8.0.0-hero-shop-owner-interop-20260915','sha256':sha(out/'KingdomEnhancedMod.dll'),
          'installed':installed,'published':False,'rootCause':'ClassInjector lazy JIT invalid ldobj byref enum invoker',
          'change':'local injected owner IntPtr ABI adapter; no framework patches',
          'verified':['7 invoker and actual pointer adapter checks','31 shop checks','full IL2CPP build 0W0E',
                      'actual interop compile 0W0E','2862 old method bodies identical','four embedded PNG identical',
                      'independent ABI / JIT review passed'],
          'pending':['game interface preflight and shop ready log','actual payment, flag and tower behavior','cross-island identity transport'],
          'gameStarted':False,'createdAt':datetime.now().astimezone().isoformat()}
(out/'candidate.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
(out/'KingdomEnhancedMod.dll.sha256').write_text(manifest['sha256']+'  KingdomEnhancedMod.dll\n',encoding='utf8')
note=('英雄驿站创建失败修复候选\n\n'+summary+'\n\n'
      '这次不是重新发布版本。当前原生界面、购买/保存/英雄战斗/不上塔/旗帜逻辑保持原样。'
      '下一次启动需同时看到[HeroShop] owner preflight passed及[HeroShop] ready。'
      '只有构建/JIT对照完成，尚不能宣称真实游戏商店已出现。\n')
(out/'修复说明.txt').write_text(note,encoding='utf8')
report=('# 英雄驿站InvalidProgram修复验收\n\n'+summary+'\n\n'
        '## 证据\n\n'
        '- receipts/first-live-failure/LogOutput.log：用户真实启动错误与候选build，配置为开启。\n'
        '- receipts/owner-interop/invoker-repro.cs及txt：独立原故障复现；静态wrapper JIT对照全部通过。\n'
        '- tests/hero-shop/Invoker.csproj：链接实际WriteUnlocked源码，旧ref enum注册后JIT失败、新IntPtr两路径和4字节/布尔返回哨兵通过。\n'
        '- receipts/owner-interop/build.txt、interop-build.txt、shop-tests.txt、invoker-tests.txt。\n'
        '- receipts/owner-fix-dll-audit.json：精确DLL对a6b5逐方法及资源对照。\n\n'
        '## 工作范围\n\n'
        'OMP18.1.19 DeepSeekV4Flash/max进行了初始有界依赖调查，同session恢复接收独立复现。'
        '调查后停止外部进程，未留下后台worker；其无生产修改。Operator采纳最小参数桥实现并集成，独立reviewer只读审核通过。'
        '没有全局替换/patch Il2CppInterop，不改原生compressed save，不启动关闭用户游戏。\n\n'
        '## 后续\n\n'+state+'。实际接口调用、商店出现/投币还须游戏验证，任务仍doing。\n')
(task/'owner-fix-acceptance.md').write_text(report,encoding='utf8')
for name in ('progress.md','domain-model.md','game-logic-map/patch-patterns.md'):
    path=harness/name;path.write_text(path.read_text(encoding='utf-8-sig')+'\n\n### 2026-09-15 英雄驿站运行接口修复\n\n'+summary+'\n',encoding='utf8')
for name in ('current/task_plan.md','current/review.md','current/closeout-packet.md'):
    path=harness/name;path.write_text(summary+'\n\n详见tasks/hero-shop-20260915/owner-fix-acceptance.md。\n\n'+path.read_text(encoding='utf-8-sig'),encoding='utf8')
path=harness/'harness-checklist.json';data=json.loads(path.read_text(encoding='utf-8-sig'));item=next(i for i in data['items'] if i['id']=='hero-shop-20260915')
item.update(status='doing',verification=summary,handoff='真实接口创建失败修复候选已审核；确认游戏退出后安装，再看preflight/ready日志，不许宣称仅编译就实机通过。跨岛仍待。')
item['artifacts']['owner_fix']='docs/project-harness/tasks/hero-shop-20260915/owner-fix-acceptance.md'
path.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
path=root/'AGENTS.md';text=path.read_text(encoding='utf-8-sig');marker='### 2026-09-06 启动事故临时门禁\n';assert marker in text
path.write_text(text.replace(marker,marker+'- '+summary+'\n',1),encoding='utf8')
print(json.dumps({'candidate':manifest['sha256'],'installed':installed,'gameStarted':False}))
