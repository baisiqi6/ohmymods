from pathlib import Path
import json
task=Path(__file__).resolve().parent;root=task.parents[3]
note='2026-09-17 15:08后只读核对13:44结束的旧musketeer-20260916会话：最终18条火枪手/18绑定，生产指纹匹配当前原生岛，18GUID/nativeId唯一且原生各一次，保存已确认，下一次实际加载未验。当前15:06安装EC80保持，日志不能当新版实测。新增关注英雄8.556秒102次Walk/Run且相位归零，另6名Beggar穿地被引擎拉回、1次弩手缩放漂移；DropItem/骑士旧问题已有后续代码修订但新版仍待测。只读无游戏/用户档写入，见tasks/musketeer-save-check-20260917/findings.md。'
for rel in ['AGENTS.md','docs/project-harness/progress.md','docs/project-harness/current/handoff-packet.md']:
    p=root/rel;s=p.read_text(encoding='utf-8-sig');p.write_text('<!-- musketeer-save-check-20260917 -->\n'+note+'\n<!-- musketeer-save-check-20260917 -->\n\n'+s,encoding='utf-8')
p=root/'docs/project-harness/harness-checklist.json';o=json.loads(p.read_text(encoding='utf-8-sig'))
for item in o['items']:
    if item['id']=='musketeer-20260916':
        item['verification']+=' '+note
        item['handoff']='当前原生与附加档18单位精确匹配，重进实际恢复待验；完整身份审查/跨岛/联机缺口保持，EC80新候选尚无运行日志。'
p.write_text(json.dumps(o,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
