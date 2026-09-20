# 人口穿地调查与有界诊断（2026-09-17）

结论：确认六名不同 Beggar 触发原生掉落保护，当前证据不足以确认其穿地原因。本轮没有修改位置、刚体、碰撞矩阵、出生数量或刷新间隔；不能宣称穿地已修好。只增加下次游玩可用的有界只读诊断。

## 已确认事实

冻结 `musketeer-save-check-20260917/Player.log` 行 1273/1291/1309/1327/1345/1363 分别为 Beggar P11 [555]、P12 [556]、P15 [5F3]、P9 [551]、P10 [554]、P13 [557]。六次均来自 `Game.TryShowMenu → World.CheckForFallThroughObjects → CheckForFallenThroughCondition → HandleFallenThroughObject`。这些名字/网络 ID 没有伴随出生或刚体快照，不能从日志推出所属 camp、出生坐标、速度、有效 collider 或物理设置。

只读 2.1 源码参考 `World.cs:848` 判断 world y < -100；保护方法把 x=0/y=1.875、速度和角速度清零。日志支持“引擎认为已经穿地并执行拉回”，旧参考阈值没有伪装成 2.4 已反汇编证明；按下暂停触发了检查，不表示暂停使他们穿地。英雄抖动与六人的穿地没有已证明的因果关系。

当前 `PopulationPerformanceCoordinator` 原逻辑只管理数量/归属/间隔及原生 SpawnBeggar 调用，没有写 Beggar transform 或 rigidbody。初始 ApplyToScene 只进入 Waiting，保存加载的超额成员不删除，不预补一大批人口。默认 cap=4/period=120，原生 fallback `spawnInterval=max(1,period-5)` 加原生五秒等待，全部保留。

2.1 参考 `BeggarCamp.SpawnBeggar` 使用 camp.position+(scale.x,.875,0)、camp.parent、原生 Beggar 池，然后设置 camp；`Kingdom.SpawnStartingBeggars` 使用 LevelConfig.startingBeggars 和 y=1。MOD 没有覆盖这两个位置参数。`Character.OnEnable` 原生恢复 isKinematic=false，但静态参考不能证明六名运行时 clone 的物理状态。Pool/FastSpawn 的 parent/local-position 与复用、collider 或速度残留仍需运行证据，不能据此直接重置所有人。

Ninja 的营地扩展只创建/归一化自有 HidingSpot 子锚点及登记，未修改 camp 根 collider 或刚体。钱袋偏移限定 CurrencyBag UI，币父级缩放保护针对货币，未见它们直接操作 Beggar 根 transform 的代码。现有友军互撞开关保持用户要求，不撤销。

## 实际 2.4 资源证据

来源为既定 E 盘独立测试副本中的 `KingdomTwoCrowns_Data`，仅只读。原始选定字段、文件 SHA256、pathID 见 `population-asset-evidence.json`；重现脚本在 `tests/population-grounding/read-assets.py`。

- globalgamemanagers：layer0=Default、10=Citizens、17=CollideWithGround、18=Beggars。Physics2D gravity=(0,-15)；序列化矩阵 layer17 rawMask=73，包含0；layer0也包含17。
- resources.assets：Beggar GameObject pathID19697 layer18，Rigidbody2D59544 为 Dynamic(0)、simulated=true、gravityScale1、constraints4、Discrete(0)；根 CapsuleCollider63017 enabled、trigger=true。
- 同一 Beggar 的子物体 Physical Collider pathID20090、Transform40631 为 layer17；CircleCollider61373 enabled、nontrigger，offset.y=.15625/radius=.15625。
- resources.assets Ground20759 为 layer0，BoxCollider62711 enabled、nontrigger。level2 的七个 Ground GameObject 也均为 layer0，具体组件与字段保存在证据 JSON。
- 冻结 LogOutput.log160 表明原友军禁碰撞实际处理的是10-10、10-17、17-10、17-17。它没有处理17-0；序列化矩阵的这些10/17内部对本来也关闭。因此现有资源/代码证据不支持“此操作关闭乞丐对地面碰撞”的判断。但没有六名故障时 runtime clone/矩阵快照，仍不排除当时其他运行状态。

## 本轮改动

新增 `il2cpp/PopulationGrounding.cs`，在现有 Coordinator 的 BeginScene、已有0.5秒 Reconcile 入册/检查、已确认 central SpawnBeggar delta=1、清场边界调用。没有增加 scene/resource 查询、额外人数遍历或 Harmony hook。

日志 `[PopulationGround]` 三类：first-observed（明确不声称出生）、central-spawn（现有调用刚确认产生一人）、below-layer（当前 world y < gameLayer.y-2 或非有限值）。每类每上下文最多12条，共36条，读取错误最多另1条；below-layer 同 pointer/instance/epoch 最多一次。身体/collider明细仅在消耗日志名额后读取，正常每次 reconcile 不做 GetComponents 快照。最多记录8个该演员子 collider，实际碰撞矩阵只查询已证明有关的 layer17↔ground0，绝不修改。

记录 name/instance/netID/epoch/time、world/local位置与scale/parent、原生camp和协调器推断ownerCamp（分开写）、body type/simulated/gravity/constraints/collision detection/velocity、ground及collider enabled/trigger/layer/bounds。日常生成、计数或重建不以诊断为条件，原生读取和logger异常均隔离。主开关关、非权威、暂停、非当前世界/layer、不活跃actor不读取诊断明细。暂停不会修正位置；原生保护完全保留。

## 验证与交付边界

- `dotnet run --project tests/population-grounding/Tests.csproj`：17 assertions，通过；涵盖真实生产诊断类的预算、同生命去重、epoch更换、正常千次 reconcile 不枚举collider、关闭/失权/暂停/外国world/layer、getter与logger异常，以及位置/速度保持。
- `dotnet build tests/population-grounding/actual-interop/Interop.csproj --nologo -v:q`：真实2.4 interop，0 warnings/0 errors。
- 完整主构建与其他并行slice集成由 root 负责。未启动游戏、未部署、未改用户档/附加档/配置、未提交发布。
- 后续读取诊断，将 central-spawn 或 first-observed 的 netID/epoch 与 below-layer 对上，才能区分“生成时已异常”和“后来物理状态变化”。当次历史六人无法补录这一证据。

生产冻结 SHA256：

- PatchPerformance_Population.cs：49E1A9B3919E11DEBD9EE6E0425BF5C06522368A0D079FF907DC020EBCC7A337
- PopulationGrounding.cs：91792750531A1B54AB1BC8BFC6D1358B7A7380640A719CABD4228DD0A178A2D7
