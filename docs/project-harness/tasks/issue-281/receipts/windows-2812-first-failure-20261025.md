# 2026-10-25 — diag.281.2 首错：已证登船火枪手的源岛行要求冲突

用户在下排左岛确认后再次停于四选一，未点任何选项。当前实际Windows 2.4.3进程出生时间及路径已再次核验，插件加载版本 `.281.2`，实际DLL SHA256 `a3d49aabd6652db244be21be301c522871c3a39dfce537d3fc34d6606f14d83c`。完整日志、用户截图、25份save/config副本与逐hash收据只留本机；不上传玩家GUID、原生指针或数据。采集器exit0、gameKilled=false；本轮没有输入、安装、源码修改、正常存档写入或恢复。

## 本次定案与边界

UI→原生交接 source8 / physical11 成功。首个missing unit在begin/apply两个快照均同career/同根、`persistRootMatch=1, reg=0, pinstance=0, active=1, should=0`；**同一当前主船、严格boatChild=1、boatSelf=0、同根Archer.embarked=1、penabled=1**，各state均ok。GetID未观察到；整体2115有效ID调用、11映射，readFault/incomplete/truncated全0，missing=1仅布尔首见，不能据careers15−ids11推断4个运输单位。

后续真实链仍为 `save-missed-row → CaptureFault → sync-reject → _ShowSaveFailurePrompt`。当前实际`SaveCapture.Apply`仅凭ValidBinding+active便要求本轮源岛对象行，与已登船、原生持久资格已在Save开始前关闭的事实冲突。此前“该root是否在船上未知”在本轮两采样点已闭合，历史诊断原件保留。已核243原生Game.Run对主船递归DontPersistInstance可产生这一组合，但未hook调用栈，不能声称已直接观测该writer命中，也不倒推出更早不存在disable历史。

这定位了Mod的源岛保存契约错误，**没有证明目的岛身份承接、更没有形成可安全放行的修复**。不新增注册/强开Persistent，不直接转Reserved或跳过CaptureFault，不按序号/位置/颜色分配付费GUID。无需用户继续重复未改候选的同一资格诊断。

## 磁盘独立核对

- 25项同集合，读取期间源字节稳定，全部副本SHA/size正确；20项逐字同安装后前像，5项变化。
- 12份save组全同；原生global-v35 **998959 bytes**、SHA `dc1b4872dfb9a22a9e7add066ed1997009648e23d196b94f62be46c9848e8bfe` 不变。Musketeer archive/pair及两bak四件逐字不变。
- Hero/Knight主文件与bak为快照轮转，新bak均精确等于旧main。Hero各context/baseline/席位不变；Knight最新15项逐条与旧latest相同，旧0..6移到新1..7，淘汰的22条最旧快照造成总条数少7，不等于当前权益减少。未据ID并集或快照保留声称冷读通过。
- `KingdomEnhancedMod.cfg` **57项键值零差异**，仅首行版本注释 `.281.1 → .281.2`；不是本次保存拒绝的配置变化。

日志全捕获：BepInEx `2ca1535ebc958a0ac87570df68cccd1601b676d1387735a12b11e89225934bf9`（61984 bytes）；Player `9ad899bdddbc1b1a42600ebe362a261c6321127c98f76d505e290720f144be95`（112238 bytes）。原始私有证据与公开脱敏摘要分别保留。

## 接下来的修复范围

先完成有限静态链：PopulateCarryForward对实际actor/工具列表的选择，SpawnUnitsWithTools→SpawnNearP1返回实例，Game.Run在保存失败、取消、到岸以及“已重生尚未rebind”之间的责任顺序。原生工具类型List<int>可表达分类/数量，未见逐ModGUID载体，不能拿它猜映射。源岛行缺失被接受前须有可随native global提交/回读的唯一运输责任，目的岛必须精确承接，失败/取消/重试不能丢失或重复权益。契约确定后再写补丁与验证，不提前承诺实现方案。

Astra直接独立复核真实日志与243静态证据通过；OMP实际 `zhipu-coding-plan/glm-5.3`（请求max，公开模型事件核验）通过本阶段诊断定案/后续边界选择；不预批未知代码、合并或安装。PR #282保持Draft、Issue #281开放。本轮未生成新DLL、未恢复正常数据，真实完整出航/保存退出/冷重进仍未通过。
