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

## 2026-10-17 原生隔离 R2：锁定首次拒绝，仍未修复

单次私有诊断候选 `55e0f49339d5c6c0c6544068d493a91e5b78e7373535184424fae4e77d0cc63d` 只在既有 hooks 增加有界计数，flag-off 与 R3 逐方法 IL 等价；没有改变状态、返回、原生字段或 fault。独立源码透明性及 binary scope 审查通过后，经 GLM 5.3 max 决策关口启动，未安装正常版。

稳定日志 SHA `89999973be0d4269ec5bc89a713e454d1593078d2c037d2a17f99fe8d299dd46` 的首个 Save 是显式参数 campaign0/land11/challenge0。4114 次有效对象 capture 均通过原生 static/isSaving 检查，却均在 `raw land != 11` 比较处早退；只单独记录了**首次 raw=0**，不推断后续各次原值。未绑定 Island，无 conflict/exception；Apply 进入后返回 `no-captured-island`，随后 CaptureFault 和 writer gate 拒绝。独立审查确认稳定快照与最终日志前缀相同。

这推翻了 R3 显式保存支路的 raw/index 等同前提；没有证明 raw0 是扩展岛 producer 缺陷或完整 Save 后仍为0。扩展桥只负责容量、LevelConfig、landData 和导航目标；没有证据表明它拥有 raw `IslandSaveData.land` 初始化责任。继续核验当前原生字段写入/加载语义，不能为通过 guard 而写 raw11、改 hash、清 fault 或新增补偿。

native postimage 仍为旧 seed `798bec70…`，没有目标11检查点。仅等待首次 autosave取证，未手动保存、未移动或改设置，CmdQ 结束 own launcher rc0。controller closed32.615秒、五通道恢复、strict31.529秒；Operator随后重新计算当前完整五通道 manifest，全部等 prepared 原件。正常 DLL 仍 `5fb9c5f8…`、无游戏进程，R2已消费。**保存业务仍 FAIL，PR253保持Draft，未合并安装。**

本机证据：`native-preflight/r2-diagnosis-review.{md,json}`、`r2-operator-result.json`、`capture-diagnostics/producer-responsibility-readonly.md`。不上传玩家文件或私有诊断源码。


## 2026-10-17 R4：源码及离线候选已通过，原生首存/冷读待验

本节替代上文R3的生产范围与“显式保存保留旧raw比较”边界，历史证据保留。当前修复在R3之上修改Persistence、Archive和NativeSave；R3 Identity/witness源码保持。当前ARM64静态证据明确 `IslandSaveData.land` 为 +0x48，并由原生加载协程按请求岛号写入；这解释了保存JSON raw0与冷载raw11可能产生不同指纹，不能因此写原生字段或放宽任意hash。

显式与-1保存统一按入口冻结global/file/event owner与唯一表成员slot证明来源。首次及后续Capture、Apply持续校验，失配sticky；若任何capture观察到非主岛raw0，generation witness义务及首次SourceWorld一直保持，不允许字段随后恢复掩盖。新generation仍exact-only；只有通过来源证明的Load启用只读兼容指纹：仅把唯一顶层无符号整数land token从可信slot投影为0，其余UTF8字节必须原样，原hash算法/domain/schema/hashKind不变，不产生投影JSON或新的历史snapshot。

Load完整收集eligible exact/preimage历史，再比较全records；empty/paid或任何Id/Kind/NativeId/StockSlot冲突都quarantine，不择优。physical writer按proof.Land真实数组索引验完整原始hash与rows，同JSON放错槽仍拒；只有确实使用preimage的分支才扫描同context最多8个epoch的所有匹配记录，纯持久化HistoricalOnly排除保持，其他epoch不能代替本proof。旧exact写侧与保守Load冲突行为不对称明确保留；旧raw==slot adoption/收费生命周期另跟踪#256，不称本轮已修。

真实业务红例覆盖explicit raw0、wrong-slot同hash、accepted gzip重建fresh DTO后的cold0→slot、SourceWorld漂移、VirginCapture误开第三入口，以及另一epoch冲突。最终源码独立复审通过。Operator冻结后独立重跑pipeline **233**、identity **652**、真实defense Integration **127**、restock **72**，全部exit0；worker另跑legacy-native-gate **18 cases/405**、legacy-compensation **202/202**，只作为worker回执，未冒充operator重跑。真实Materialized compensation完整生命周期、native ABI及原生冷载仍未验，pipeline禁用边界不作替代。

完整私有累计闭包仍270 C#/39资源/161引用/1Import，与正常来源相比仅四源有差异：Identity、Persistence、Archive、NativeSave。public R4业务prefix与原private PC hook尾部作精确拼合，既有!ANDROID包装保留，R2诊断已移除。net6/net8均0W0E、禁用部署目标，候选SHA分别为 `69ce8eb7f2055c26e571fa312017cf75993d05f01fb56fb1fbe957c8d7ae5257`、`76b0303ba3faf4f3dda027e2140e9345d3c078f45caa0e9f6e5aeb4062f5d80b`。二进制范围独审与隔离controller关口分别留证。

当前无游戏运行，正常DLL仍5fb9c5f8；R1/R2五通道已完整恢复。新C3准备首次保存，C4须继承**真正C3验收postimage native及对应完整ModSave**再冷读重存，不能拿旧pair或退出码当通过。模板初审发现schemaVersion误读、最终stat取早、父路径接线与来源校验缺口，已要求修正；尚未prepare新运行包。PR253继续Draft、Issue251开放，未合并安装。详细本机证据在 `implementation-r4/` 与 `capture-diagnostics/`；不上传私有累计源码或玩家存档。
