from pathlib import Path
import json
task=Path(__file__).resolve().parent
root=task.parents[3]
harness=root/'docs/project-harness'
receipt=json.loads((task/'receipts/local-install.json').read_text(encoding='utf-8-sig'))
assert receipt['userDataUnchanged'] and receipt['dllSha256'].lower().startswith('a6b5d90b')
summary=('2026-09-15 用户确认接入后，已在游戏关闭时备份并安装a6b5d90b英雄驿站候选到正确E盘独立副本，'
         'build=8.0.0-hero-shop-flags-ground-20260915。备份原正式80522bf1并核对，22份原生存档/配置/附加档hash保持，未启动游戏。'
         '可测试当前岛8币购买、无文字占位旗/死亡破旗、英雄不上箭塔；109购买/31塔/81效果/31商店及构建/独立审核证据沿用精确候选。'
         '这是本机候选安装，不是再次发布；公开8.0.0不变。跨岛名额范围/身份运输仍未完成，实际投币、显示、死亡与撤塔仍待实机。')
report='# 英雄驿站本机接入\n\n'+summary+'\n\n'
report+='安装目标：`'+receipt['installed']+'`\n\n备份：`'+receipt['backup']+'`\n\n'
report+='验证：精确候选/原DLL/备份/安装后SHA256及22份用户文件hash。没有重建或更改已审候选，也没有改配置启用值。\n\n'
report+='测试入口：原E盘KingdomTwoCrowns.exe，进入游戏后F5→弓箭→英雄驿站。当前阶段验证当前岛流程，不能将跨岛身份接续视为已完成。\n'
(task/'local-install.md').write_text(report,encoding='utf8')
for name in ('progress.md','domain-model.md','game-logic-map/patch-patterns.md'):
    path=harness/name
    path.write_text(path.read_text(encoding='utf-8-sig')+'\n\n### 2026-09-15 英雄驿站本机接入\n\n'+summary+'\n',encoding='utf8')
for name in ('current/task_plan.md','current/review.md','current/closeout-packet.md'):
    path=harness/name
    path.write_text(summary+'\n\n详见tasks/hero-shop-20260915/local-install.md。\n\n'+path.read_text(encoding='utf-8-sig'),encoding='utf8')
path=harness/'harness-checklist.json';data=json.loads(path.read_text(encoding='utf-8-sig'))
item=next(x for x in data['items'] if x['id']=='hero-shop-20260915')
item.update(status='doing',verification=summary,handoff='候选已安装，等待当前岛实机反馈；跨岛名额范围与身份运输仍待完成，公开版不变。')
item['artifacts']['installation']='docs/project-harness/tasks/hero-shop-20260915/local-install.md'
path.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
path=root/'AGENTS.md';text=path.read_text(encoding='utf-8-sig');marker='### 2026-09-06 启动事故临时门禁\n'
assert marker in text
path.write_text(text.replace(marker,marker+'- '+summary+'\n',1),encoding='utf8')
path=task/'plan.md';path.write_text('# 最新安装状态\n\n'+summary+'\n\n'+path.read_text(encoding='utf-8-sig'),encoding='utf8')
print('Installation receipt recorded; gameplay and save data not modified.')
