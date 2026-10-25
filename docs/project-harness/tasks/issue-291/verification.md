# Issue #291 离线坐骑映射→技能池顺序（源码/host）

基线 `c1e9e5c`，分支 `codex/mount-pool-order-20261025`。生产改动仅
`il2cpp/CrossWorldMountDependencies.cs`；测试 `adapter Program/Stubs`；本页。未改 Harmony/NoEnvelope/
Android/构建入口/Main/存档，不启动游戏、不部署、不合并。

## 根因与修复

原生 `PoolManager.InitPools` 先清池再重建，本功能钩子是 postfix。旧 `OnPoolsRebuilt` 只 Invalidate 后
`EnsurePools(true)`；此时映射可能未登记，`assets.GetSteedByType` 对缺键回退 Horse Regular（非 null），
旧代码把回退当能力来源，四个有池定义（Sleipnir/Kelpie/Golem/Beetle）得到错误能力集，五池未正确建立。

1. `OnPoolsRebuilt`：gate 后先 `EnsureRegistered(true)` 再 `EnsurePools(true)`。
2. `EnsurePools`：每个 definition 调用 getter 前要求自身 primary mapping `Ready`；缺前置保持 pending、
   不设冷却、不读 Horse、不阻塞其他 definition。仍保留真实失败，未把 error 改 warning。
3. `StillResolved`：原 try 内先精确键 + 非 Unity-null 预检，缺键早退；**不按 raw 指针提前拒绝**，
   实例一致性只由保留的真实 getter（含 `GetPrefabSwap`）回读判定。

重入门 `_inDependencyScope` 以 try/finally 归还；OFF/foreign/challenge/context gate、冲突不覆盖、
每代清池后重注册、变体覆盖不写共享表均保持。

## 验证（host，非 native）

C10 覆盖：不 `EnsureAll` 预热直接调用实际 `PatchRide_CrossWorldMount_PoolInit.Postfix`；连续三代清池
累计 0 fallback；同 assets 清映射重建；映射容器/单 primary 资源暂缺及恢复；P2 别名暂缺时 primary 池
独立且 `IsDefinitionReady` 仍拒绝；Unity-null 与整键缺失早退；单 type 冲突不覆盖（复用 C2）；正确类型
真无能力保留 `no pressed-ability` 错误；OFF 重建再 ON；重入旗标归还；五池 syncID/static 池表/名称缓存。

Swap 增量（本轮）：raw 精确键 A→B，`BiomeSpecificAssets.PrefabSwap(B)=A`（A 为 Ready 旧实例），经实际
post hook 断言 `Ready` 保持、raw B 未被覆写、`PrefabSwap` 确实以 B 被调用。第一版候选（含 raw 指针
提前拒绝）在 `swap-red.log` 单条 RED；删除该提前检查后 `swap-green.log` **33/33 全绿**，既有 C10 全绿。

- `green-deps-v2.log`：77 PASS / 2 FAIL / 79 checks；C10 33/33。2 条失败为同一 issue-200 编译粘连下
  重跑的 C3 授权链对照，非本改动引入。
- `green-full-v2.log`：164 PASS / 37 FAIL / 201 checks；37 条失败集合与 v1 全量对照逐条相同。
- 旧 raw 日志与 E 的 `archive-v1/result-v1.*` 归档保留，未改写。

## 证据措辞边界

- 37 条失败是**同一 issue-200 编译 glue 下对 HEAD 重跑的可运行对照**；原 4 源适配器在 `c1e9e5c`
  本来不能编译（`LevelConfig`、`MountIslandGrantAuthority` 等未链接），不能称“原项目可运行基线”。
- stub 的 `mapped == null` 是 CLR-null，不模拟 Unity fake-null；stub 直接调用 `GetSteedByType`，
  不自动经过 Harmony prefix。所谓“缺键 getter 0 次”只指该 stub 直调未发生，不能称为全局 getter 0 次。
- 旧 C1/C4/C5/C6/C8 授权/生成断言因 issue-200 land11/13 语义漂移仍失败，未修、未冒充通过。
- `tests/cross-world-mounts` 与 `cross-world-mounts-repro` 在基线与改动后均因同一批未链接类型编译失败。

## 未验边界

仅 source/host。未验 Native 技能、死亡/撤退、池复用、换岛、联机、Windows/Mac 实机；全源 NoDeploy
由 Root 重跑；不影响 #286/#290。

## Root 全源 NoDeploy 与独立复审

- 最终v2已删除不必要的raw pointer早退，精确键/非null预检后保留原GetPrefabSwap回读。新增真实A/B→Swap回归在第一版候选RED（32/1/33），最终独立执行33/0/33通过；raw B未被改写、Ready保持且Swap确实看到B。新增Reset清PrefabSwap，避免案例间污染。
- Mac240、Win240、Win243完整私有产品构建均0 warnings/0 errors，275项资源全部从真实PE读回等于生产输入；Win243仅私有叠加PR #250 typed aliases，不冒充实际兼容或技能验收。三构建源/资源、私有输出与模型日志均留本机任务目录。
- 最终源码与source-linked host独立增量复审APPROVED。37条对照失败及2条deps失败仍如实保留，不称全套绿；CLR-null不模拟Unity fake-null，stub不自动执行真实Harmony Prefix，未据此扩称Native重入验证。
- Android当前显式Compile不含CrossWorldMount*，不改其pin/入口、不为本项重复设备或host验证。#290遇到的旧WallEngineerRuntime frozen pin与本项无关，由Android专项收尾。
- 正常18份运行/数据文件与原仓库未提交改动保持，不合并/安装；此页的host/源码通过不表示五轮旧日志已在新Native中复验消失。
