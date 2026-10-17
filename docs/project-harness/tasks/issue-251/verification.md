# Issue #251：首次登坐骑岛保存失败候选

## 范围与已确认事实

2026-10-17 玩家确认第一次到目标岛。当前 2.4.0 Mac 日志中，岛 11 的新生成火枪 context 对应 land 0，而英雄和共享保存来源指向岛 11；物理 writer 至少一次因 `NoMatchingSnapshot` 拒绝。当前原生主档可解析，已保存岛表只有 9 槽、目标为 11，缺目标完整快照可能是正常航行检查点，不能据此判整档损坏。失败事件的全部原生调用分支尚未取得，身份错绑是已复现的机制，未证明是当前弹窗唯一原因。

代码基线 `release/v9.5.13@1f9c6753ef2a87ba994d38c96dc49b111927551c`。生产仅修改 `MusketeerIdentity.cs` 和 `MusketeerPersistence.cs`；正常私有累计版本的宫廷、盾卫、地图等输入保留，不用公共 DLL 覆盖。原生字段、存档格式、历史 epoch/GUID、补偿、physical writer、Heavy/Bank/Courier 产品与共享接线不改。盾卫同源风险另跟踪 #252。

## 修复行为

- exact owning campaign 表内唯一 island pointer → slot；零指针、重复、表外、非零 land 与 slot 分歧、owner 不符均拒绝。
- generation 实际 Commit/Stage/Install 成功后才建立单个会话 receipt，证明同 file/global/owner/island/world/slot 的默认 land 0；live 换岛/null、owner/world/file/slot 变化和 exact reload 撤销。原每帧缓存保留。
- 显式 `Save(Land >= 0)` 保留原旧岛来源。仅 `-1` 延迟解析：Save 入口冻结 file/global/event owner，真实 GetID 对象必须是该 owner 的唯一成员，首次绑定后每次有效 Capture 与 Stage 前 Apply 持续复证；一旦失配，恢复字段也不复活。
- 不加零 GetID 兜底。新出站 JSON 有有效变化却未被 Capture 时仍由真实 writer 拒绝，原 bytes/proof/epoch 保持。

## 实际验证

R3 冻结后 operator 独立重跑四组 source-linked managed 套件，全部退出 0：pipeline **86**、identity **508**、defense **127**、restock **72**。命令统一为：

```sh
DOTNET_ROLL_FORWARD=Major <dotnet10>/dotnet run --project <project> -c Release \
  -p:BepInExPluginsPath= -p:UseSharedCompilation=false --disable-build-servers
```

四项目分别为 `tests/musketeer-native-save-pipeline/MusketeerNativeSavePipelineTests.csproj`、`tests/musketeer-identity/Tests.csproj`、`tests/musketeer-defense/Integration.csproj`、`tests/musketeer-restock/Tests.csproj`。实际命令/日志/退出码绑定在本机任务证据 `implementation/final-candidate-build/operator-tests.json`；不把 doubles 当原生行为。

12 槽 fixture 独立建模 CurrentIsland、表成员、CurrentLand 和 getter/static owner，含原岛 0 与新岛 11 的付费身份、真实 Stage/Apply/Gate、建模冷载和返岛。receipt 失效、挑战 campaign=-1、非法负 land、冻结来源首绑定前后漂移均有反例。zero GetID 的空岛组先用真实 Capture/Stage/Accept 完成检查点；paid 组先证 Gate Pass，再改变 hash-significant native objects 得到独立 `NoMatchingSnapshot`，恢复 JSON 后再 Pass，不删除既有 proof/fault。共享重入/异常只做中性 disabled spy，不宣称盾卫/金库/信使算法实测。

最早红例按原基线产品重放：pipeline/identity 退出 134，恢复候选后绿；这是 baseline replay，非伪称完整 R3 新测试全部先红。完整历史日志保留。pipeline/identity 无警告；defense/restock 保留既有 CS0649。

完整私有累计输入为 **270 C# / 39 resources / 161 references**。先无修复重建 net6/net8 各 0W0E，net6 与正常安装版本逐资源、metadata、Cecil IL 和 raw method body 等价。再只应用两文件 diff，R3 net6/net8 各 **0W0E**，明确 `BepInExPluginsPath=`，没有部署副作用。

| 输入或产物 | SHA-256 |
|---|---|
| public MusketeerIdentity | `f43622524ae4ba1979ae5e46952b5f588d41ff15d4dfd5115b2b248553e0277e` |
| public MusketeerPersistence | `57bb27497c845007bf478e9de9d111bc842dfaace2a3ef1ee9e3cda6313b0bba` |
| private Persistence，保留既有 !ANDROID 包装 | `a78a8540c53b78c4a0cc5e87b2077b57624c3b0ee4a7b25acd4839aa78b104a5` |
| 本次 R3 net6 候选 DLL | `06435ddeb82ce3ce9e3120c98a87942898547eb03fc42fb9a16fbce87ed6bdda` |
| 本次 R3 net8 候选 DLL | `0c78e449ced890133f4c2611f7dde7484a9bd7617cd88b6e1c93ebf1d92de205` |
| 保持原样的正常 DLL | `5fb9c5f8afcdb2774e7f250dc9ae9bd3c24f9218f37e241c262b6acd4ee89352` |

## 审查、保全与未验边界

独立代码最终复审和第二独立 A/B 复审均批准候选/Draft 交付。最终 net6/net8 程序集独审通过：8 个实际修改方法、11 个新增 helper、单 witness 类型/slot 枚举与精确闭包换号均对应两授权源；39 资源 payload、其余类型/方法及共享 hooks 保持。完整输入闭包 471 项仅两源码变化，保全复核单独留证；GLM 5.3 max 决策审查与 DeepSeek Flash max worker 的实际 model/thinking 事件保留，不以请求参数代替实际模型证据。

当前正常游戏进程仍在运行，用户此前已被要求保留失败弹窗。12 份原生/ModSave/prefs/config 文件与首次只读快照逐字保持，正常 DLL 保持；备份不含进程内未保存航行/登岛状态。没有退出/操作游戏、启动新游戏、恢复存档或安装候选。既有 paired backup 的原岛 8 对照部分通过，但其他岛未证完整，不能把它称作完整可恢复存档，也不自动回滚。

**本阶段仅候选与 Draft PR，#251 继续开放。** 原生 Save/GetID 真实时序、原生 land 赋值、缺岛 11 完整快照的冷进入、真实付款/保存/退出/重进、Harmony/ABI 和当前失败会话恢复仍未验；Windows/Android/多人亦未验。未合并、未安装。后续须先协调当前正常窗口，说明可能丢失未保存航行进度，再用隔离环境验证，不能与正常版并行。

本机详细证据根：`.local/tasks/mount-island-save-failure-20261017/`。不上传私有累计源码或玩家存档。

## 2026-10-17 原生隔离 R1：generation 修正生效，完整保存仍失败

本节更新上文原生未验和正常游戏仍在运行的历史状态。用户明确允许结束正常游戏并继续隔离验证后，已保全原件并停止原窗口；原退出码未知，不作为保存通过。新单次 R1 使用上述 net6 `06435dde…`，精确隔离 launcher/game 83924/84171，原生 UI 观察到到岛场景；自动保存已有 CaptureFault，随后从暂停菜单选择“保存＋退出”再次出现原生 SaveFailurePrompt。未点击重试，经 CmdQ 正常结束 own launcher，rc0仅为退出成功。

实际日志先有 `virgin-epoch:277028ac:8f267d35:slot=11:dataLand=0:campaign=0:challenge=0`，后有 `capture-fault:277028ac`、`gate-fail:CaptureFault`、`sync-reject:CaptureFault`。因此新 generation 的 slot11 来源已获得原生正向证据，但受保护 capture 没有闭合；目前尚不能区分显式 land11/raw0 交叉检查、GetID/静态时序、Apply 早退或异常，不能猜测后加兜底。

独立 postimage 核验：native `global-v35` SHA `798bec70ccfdcb4d2e9beba533041e70f6a0632ecaf9998e315f68e988acd189` 仍逐字节等 seed，9槽且无index11；Msk sidecar及配对主/备档亦等seed，保留全部旧付费历史，但这不是新保存。隔离 Hero/Knight sidecar新增空scope/active变化，旧scope与prior epochs值保留，不能称全部数据未写。完整保存判定 **FAIL_NO_TARGET11_CHECKPOINT**。

controller完成 closed 32.057秒观察、五通道恢复（normal/game/save/prefs/logs），再 strict 31.492秒稳定观察；Operator另核最初12份原档/配置逐字恢复，正常DLL仍 `5fb9c5f8…`。R1已消费，不复用。一次菜单坐标操作曾误进Heraldry，立即退出且未选择修改，保存fault早于此操作；后续改逐键导航到已截图确认的SaveQuit。

本机 case：`/Applications/ohmymods/player-tests/issue251-native-save-20261017-r1/runs/manual-a50c7056e26640a39c75c97055a16504`。BepInEx日志 SHA `48eec00011a3d5da696136aea04f9f6ad1714f80e99bc5a10f212cd80fd20044`；独立核验 JSON SHA `ff2794ce5b4761776185218650e0e372e9641b9dde8bc1ef660a1b2f7540236b`，详见本机 `native-preflight/r1-postimage-review.md` 与 `capture-fault-diagnostic-plan.md`。不上传玩家文件。

**R1推翻了“该候选已足以消除此场景保存失败”的推断；不推翻slot11 generation已生效的证据。PR #253仍Draft、Issue #251开放，未合并安装。** 下一步只在新私有候选的既有hooks补有界分支诊断，明确首次退因后修根因，再重新做原生保存和冷读验证。
