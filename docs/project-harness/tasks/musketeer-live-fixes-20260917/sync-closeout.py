from pathlib import Path
import json,re
task=Path(__file__).resolve().parent
root=task.parents[3]
receipt=json.loads((task/'receipts/install.json').read_text(encoding='utf-8-sig'))
sha=receipt['candidateSha256'][:8].lower()
summary=(f'2026-09-17 双商店/火铳实机反馈候选已闭游戏备份安装正确E盘：{sha} / build=8.0.0-musketeer-live-fixes-20260917。'
 '购买移除逐次LoadAll，付费前校验本世界原生Bow池缓存并增加有界分段耗时；333ms卡顿贡献仍待实测。'
 '撤Character.DropItem Nullable桥接，复用Droppable.Drop显式source。火铳职业子集均衡，转职/读档完成后合并一次分配，原生fresh列表避普通单位depth，支持无墙回退，延期事件绑定world。'
 '骑士身份改稳定context/epoch与窄时钟归一化，精确legacy匹配保留GUID/类型；失配保历史不重种，失败load不提交binding，真实新生成另建epoch，升级输出先回读校验。不能宣称恢复无法匹配的旧类型。'
 '双店为奇幻弓匠/西式枪匠及局部待机帧，地面与暂停保留；只有双店PNG更换。'
 '代码回归、实际2.4构建、授权DLL/资源审计和新增切片独立复核通过；安装前后存档/附加档/配置hash保持。'
 '未启动游戏/提交/发布，公开8.0.0不变。真实购买卡顿、2/2守位、掉枪、店主观感和骑士重进仍待验收；旧火铳完整身份终审及跨岛/联机缺口保持。')
tag='musketeer-live-fixes-20260917'
block=f'<!-- {tag} -->\n{summary}\n<!-- {tag} -->\n'
paths=['AGENTS.md','docs/project-harness/progress.md','docs/project-harness/current/task_plan.md',
 'docs/project-harness/current/review.md','docs/project-harness/current/closeout-packet.md','docs/project-harness/current/handoff-packet.md',
 'docs/project-harness/domain-model.md','docs/project-harness/game-logic-map/patch-patterns.md']
for relative in paths:
    p=root/relative;s=p.read_text(encoding='utf-8-sig')
    pattern=rf'<!-- {tag} -->.*?<!-- {tag} -->\n?'
    s=re.sub(pattern,lambda _:block,s,count=1,flags=re.S) if re.search(pattern,s,re.S) else block+'\n'+s
    p.write_text(s,encoding='utf-8')
p=root/'docs/project-harness/harness-checklist.json';obj=json.loads(p.read_text(encoding='utf-8-sig'))
for item in obj['items']:
    if item['id']==tag:
        item['verification']=summary
        item['handoff']='已安装候选，下一步用户实测与日志；不自动启动，不改用户档，不发布。状态doing。'
p.write_text(json.dumps(obj,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(task/'installed-summary.txt').write_text(summary+'\n',encoding='utf-8')
print('Synchronized task evidence; live acceptance remains doing.')
