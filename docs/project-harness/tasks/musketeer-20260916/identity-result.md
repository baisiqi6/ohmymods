# 火铳手身份/附加档 worker 结果（musketeer-20260916 identity，review 修复轮）

状态：**代码与测试完成（本 worker 只有 read/edit/write：未构建、未运行测试、未安装、未启动游戏、未提交）**。
Operator 已对**上一修订**验证：测试 PASS 234、完整实际 2.4 构建 0W0E（并修正了两个测试夹具，本文件与代码均按原样保留）。本轮的货架生命周期改动需要 Operator 再跑一次同一套测试与构建；实机（游戏内）验证仍未做、本文件不声称通过。

## 0. 本轮修复（对应 Operator 构建错误与独立 reviewer 阻断项）

| # | 问题 | 修复 |
|---|---|---|
| B1 | `tests/musketeer-identity/Tests.csproj` 第 7 行 XML 注释含 `--`（`run --project`）导致工程不可加载 | 注释改为「进入本目录执行 `dotnet.exe run`」，不再出现连续双横线 |
| B2 | `MusketeerPersistence.cs` 缺 `using System.Linq;`（`Select`/`All`/`OrderBy`） | 已补 |
| 1 | 转移不更新 `Career.Kind`（枪→单位后仍是 Gun，`IsUnit` 永远 false） | `Bind(...)` 统一派生 Kind（有 Character=Unit，否则 Gun），并强制 Unit 的 StockSlot=-1；查询/落档都以绑定为准 |
| 2 | 转职用单一全局捕获，嵌套转职互相覆盖；`GunPromotionInProgress` 生命周期不严谨 | 改为每调用 `PromotionState`（Harmony `__state`，prefix/snapshot→postfix→finalizer），全局只保留深度计数：postfix 不关闭 scope，只有对应 finalizer 递减；未注册工具不进入 scope。prefix 在原生调用前快照 career GUID + 枪指针 + life；被确认 despawn 消费的枪仍可转给返回的 Archer；根被复用时不夺回、不误标；原生失败不伪造 owner，paid career 保持 reservation |
| 3 | 冲突/未见证行/kind 不符/重复行只在内存解绑，仍继承 baseline | `LoadCapture.End`：`Conflict`/witness-miss(`pending`) → `Unresolved=true`、`HasBaseline=false`、不提交 baseline、`CanPurchase=false`；普通无关行忽略，但被记录的行必须精确 frozen id + 正确 kind；失败读档恢复旧状态与旧绑定且不写盘 |
| 4 | 查询只看 `activeInHierarchy` | `IsMarked/IsUnit/IsGun/CopyUnits/CopyGuns` 增加 `TrackAllowed` + 当前世界/layer（`MusketeerAccess.InWorld`）；失去权威时 `Tick` 不改 `_current`，查询不暴露其他世界绑定；元数据保留、无 RPC、无跨世界对象写入 |
| 5 | 缺少付费货架元数据 | 新增 `StockSlot`（-1 = 普通掉落枪/单位，0..2 = 付费货架），全链路 Copy/Same/Encode/Decode/ValidRecords 携带；新增 `TryRegisterPaidGun(tool, stockSlot=-1)`、`StockSlot(tool)`、`ReleaseStock(tool)` 与 `Droppable.Drop` 清旗 hook；加载成功且完全解析时按精确 stock gun 还原 `Rigidbody2D.isKinematic=true` + 速度 0（不写位置、不猜对象、失败/部分/联机不写） |

## 0b. 本轮修复（付费货架生命周期收口）

| 问题 | 修复 |
|---|---|
| `pickedUp` / `enemyClaimer` 的枪仍可能保留 StockSlot（此前只在转职/Drop 清） | 新增统一“货架证明”`StockClaimProven`（活绑定 + 当前世界 + 未收集 + 无敌人认领）。三处强制撤销：**有界巡检**（既有 2s Sweep，只遍历自有注册表，无全场扫描）、**权威存档捕获**（写 StockSlot 之前先证明，否则撤销后再写 -1）、**读档物理还原前**（未证明则撤销且不冻结）。只清元数据，不碰物品/不撤销原生拾取/物理；联机与异层一律不动。 |
| 转职捕获可能被 `pickedUp=false` 前置条件破坏 | 身份捕获（`TryGetBound`/promote/drop）从不检查 `pickedUp`：原生在 `Character.Promote` 之前就把 `tool.pickedUp=true`，回归用例覆盖“pickedUp 的货架枪仍按精确 career 转职”。 |
| `RestoreStockPhysics` 吞掉错误/缺 body、无重试、仍可继续购买 | 失败不再是静默成功：`TryRestoreStockPhysics` 返回 false，`LoadCapture.End` 把该 career 记为 **pending**（`IslandState.StockRestores`，上限 3）；pending 期间 `CanPurchase=false`、`HasUnresolved=true`、`StatusText` 说明；每次 Tick 的既有有界巡检重试同一把已证明的当前世界货架枪，成功即清除（解锁），若 career 已释放/被证明不再成立则清除条目（不做永久锁）。只写 `isKinematic`/速度，绝不写位置，也不检查其它对象。 |
| Operator 修正的两个测试夹具 | 保留：stub 中 `MusketeerAccess.Playing` 依赖 `TrackAllowed`（与生产一致）；hidden 用例的原生 JSON 省略该行，使后续可见存档的 hash 不同。 |

## 1. 交付文件

| 文件 | 职责 |
|---|---|
| `il2cpp/MusketeerArchive.cs` | 附加档纯逻辑：career 记录（含 StockSlot）、快照、context/epoch、严格 schema v2、CAS+备份存储、岛 JSON 指纹 |
| `il2cpp/MusketeerIdentity.cs` | 内存身份模型（career ↔ root slot，life/GoId/world 守卫）、每调用转职作用域、货架元数据、查询、Tick/巡检、状态文本、运行时原生桥（Promote/DropItem/Droppable.Drop/Pool 的 Harmony patch） |
| `il2cpp/MusketeerPersistence.cs` | 存档/读档/新岛生成桥、context 解析、CAS Commit、货架物理还原 |
| `tests/musketeer-identity/{Tests.csproj,Stubs.cs,Program.cs}` | 离线回归（链接三份生产原件 + 替身）。运行：进入该目录执行 `C:/Users/ADMIN/dotnet8/dotnet.exe run` |
| 本文件 | 契约、hook 矩阵、不变量、限制 |

## 2. 导出契约（供 Shop/Panel/Runtime/Operator 接线）

`internal static class MusketeerIdentity`：

- `void Tick()`：每面板 Update 在 Runtime/Shop 之前调用；解析 context、选中状态、每 2 秒有界巡检（解绑失效引用、撤销已被收集/被敌人认领的货架声明、重试失败的货架物理还原）；失权时不动 `_current`。
- `bool CanPurchase`：`MusketeerAccess.Playing` + Ready/非只读/非 unresolved/有 baseline + 本帧 context + 未达容量 + **无待恢复的货架物理**。
- `bool HasUnresolved` / `string StatusText`：包含 pending 货架恢复的说明。
- `bool IsMarked(GameObject)` / `bool IsUnit(Archer)` / `bool IsGun(DroppableTool)`：注册表查询，要求当前世界/layer + 权威；不受功能开关影响。
- `bool TryRegisterPaidGun(DroppableTool tool, int stockSlot = -1)`：商店生成原生 ToolBow 后、交给玩家前调用。`stockSlot` 传货架位（0..2）或 -1；同一货架位不能有两个活枪；带槽注册要求该枪未收集且无敌人认领（`pickedUp`/`enemyClaimer`）。返回 false = 商店 despawn + 原生退款。
- `int StockSlot(DroppableTool tool)`：该枪当前货架位（-1 = 无/单位/未跟踪）。
- `void ReleaseStock(DroppableTool tool)`：撤销货架声明（被拾取/被偷/被敌人认领/非创建期掉落时由 Operator 调用）；身份 GUID 不变。
- `void ForgetUnpaidGun(DroppableTool tool)`：仅创建失败清理。
- `void CopyUnits(List<Archer>)` / `void CopyGuns(List<DroppableTool>)`：清空后加入当前世界且权威的活绑定。
- `bool GunPromotionInProgress`：从付费枪 `Character.Promote` 的 prefix 起，到**同一次调用**的 finalizer 结束为止为 true；嵌套转职保持 true 直到最外层结束。与其他 mod 的 prefix/postfix 顺序无关。

## 3. 原生 hook 矩阵

| 目标 | 类型 | 作用 |
|---|---|---|
| `Character.Promote(DroppableTool,IUnitController)` | prefix `Priority.First` + postfix `Priority.First` + finalizer（均带 `PromotionState __state`） | prefix 在嵌套 despawn 前快照 career/枪指针/life；postfix 精确转给返回 Archer；finalizer 只关闭本次 scope |
| `Character.DropItem` | postfix | `__result` 恰为原生 ToolBow 时 career 转给掉落枪（普通枪，无货架声明）；旧 unit 身份结束，原 Demote 继续 |
| `Droppable.Drop(GameObject,Vector2,Vector2,PickUpPolicy,bool,bool,bool)` | postfix（2.4 interop 精确签名） | 任何真实掉落都撤销该跟踪枪的货架声明，避免把被拿走/掉落的枪当付费货架还原 |
| `Pool.FastSpawn(Vector3,Quaternion,Transform,short,bool)` | postfix | 已确认 despawn 的 root 开新 life |
| `Pool.FastDespawn(GameObject,float,bool)` | prefix(delay<=0) + postfix | 仅「立即 + 已销毁/已 inactive」确认为回收完成并释放活引用 |
| `IslandSaveData.Save(int,int,int)` | prefix + postfix `Priority.Last` + finalizer | 存档捕获窗口；只有原生 Save 正常返回后才提交 |
| `IslandSaveData.GetID(Persistent)` | postfix | 逐持久对象捕获「career ↔ 原生行 ID」 |
| `IslandSaveData.TryPopObjectsToScene()` | prefix `Priority.First` + finalizer | 读档窗口：解析、冻结行、绑定、分类提交/回滚、货架物理还原 |
| `IslandSaveData.TryCreateOrFind(ObjectData)` | postfix | 按被见证 NativeId 精确绑定；无关行忽略；kind 不符 = 冲突 |
| `CampaignSaveData.ApplyToScene` | prefix + postfix | 新岛生成：新 opaque epoch + 空 baseline 原子提交（once-guard） |

无新增原生 hook 类型、无自定义 Persistent 组件、无 `Resources` 路径写入。

## 4. 核心不变量

1. **一 career 一 owner**：同一时刻只绑定一个 root；同一 root 只承载一个 career。
2. **Kind 由绑定派生**：枪→单位→掉落枪后，落档/查询始终反映当前载体类型；单位永不携带货架位。
3. **只写被见证的 NativeId**：只有同一次原生 Save 中 `GetID` 对绑定根的捕获才落档；reservation 一律空 id（池复用回收 `名字-InstanceID`，旧 id 顺延会误绑）。
4. **释放只在被证明的结束时**：确认的立即 `FastDespawn`、root 销毁/实例号变化、显式转移；原生 OnDisable 不是删除。
5. **life/GoId/world 守卫**：root slot 记录 `GoId + Life + World(gameLayer 指针)`；池复用必须经 `FastSpawn` 开新 life；跨 runtime world 的绑定永不成立。
6. **查询要世界与权威**：失去权威或对象不在当前 layer 时，查询/拷贝一律 false/空；`Tick` 不动 `_current`。
7. **fail-closed**：未知快照有付费记录 → 保留 + 禁购；读档冲突/未见证行/kind 不符/重复行 → `Unresolved` + 无 baseline + 禁购；读档失败回滚旧状态且不写盘；写盘失败 → `ReadOnly`。
8. **容量不驱逐付费引用**：每岛 4096 条，满了拒绝新注册（商店退款）。
9. **不做 first-N 分配**：只按被见证 NativeId 精确恢复。
10. **货架位是元数据不是身份**：0..2 仅表示「仍在付费货架」，转移/掉落/撤销即清除；同一快照内货架位唯一。

## 5. 附加档与流程

- 路径：`BepInEx ConfigPath/KingdomEnhancedMod/ModSave/musketeer-identities.v2.json`（+`.bak`）。
- schema v2（本轮扩展，尚未部署过旧文件）：`records:[{id,kind,nativeId,stockSlot}]`；严格字段校验；单快照 ≤4096 记录；scope ≤128、快照 ≤8、context ≤64、epoch ≤8。
- context key = 稳定 `file+campaign+challenge+land`；epoch = 随机 opaque；指纹域 `musketeer-island-objects-v2`（排除三个顶层计时字段）。
- Save：收集 GetID 捕获 → 校验（活动绑定 career 全部被捕获、无重复 id、行在 objects 中恰好一条、货架位来自权威绑定）→ 写快照（baseline 留给读档确认）；失败不写盘。
- Load：精确匹配 → 带 NativeId/StockSlot 进入状态 → `TryCreateOrFind` 精确绑定 → `End` 分类：
  - 全部见证行绑定成功且无冲突 → 确认 baseline；完全解析时对**精确 stock gun** 还原 `isKinematic=true`、速度 0；
  - 冲突/未见证/kind 不符/重复 → `Unresolved=true`、`HasBaseline=false`、不写 baseline、禁购（记录全部保留）；
  - 读档失败 → 旧状态与旧绑定原样。
- 日志：`loaded:kind=...:records=...:bound=...:unbound=...:pending=...:stock=...:conflict=...`、`saved:records=...:bound=...`，以及各拒绝键（`save-missed-row` / `save-duplicate-id` / `save-row-missing` / `save-invalid-rows` / `promote-*` / `stock-released` / `load-bind-first`）。

## 6. Operator 需要接线的地方

- `ModPanel` 每帧顺序：`MusketeerIdentity.Tick()` 先于 Shop/Runtime 读取。
- 其他 mod 排除项：优先 `GunPromotionInProgress`（顺序无关），其次 `IsUnit(__result)`。
- 商店：付款成功 → 生成原生 ToolBow → `TryRegisterPaidGun(tool, slot)`；false → despawn + 原生退款。货架容量 3（`MusketeerIdentity.StockSlots`），占用位可用 `StockSlot(tool)` 统计。
- 货架枪必须走原生持久行（`persistObject` 开、存档时 active）；被拾取/被偷/被敌人认领/非创建期掉落时调用 `ReleaseStock(tool)`（`Droppable.Drop` hook 已自动覆盖掉落路径）。

## 7. 限制与风险（未实证项）

- **构建/测试状态**：Operator 已对上一修订跑通测试（PASS 234）与完整实际 2.4 构建（0W0E）；本轮改动（货架声明证明 + pending 重试）需再跑一次同一套命令。`Droppable.Drop` 的 7 参数签名取自 `actual-interop.txt`（2.4 实测 metadata），上一轮编译已通过该 patch。
- **`TryCreateOrFind` 是 private static 原生方法**（坑 17 同族）：若被 native caller 绕过，读档绑定不命中。可诊断信号：`records>0` 而 `bound=0/pending=N` + 一次性 `[Musketeer] load-bind-first` canary；此时 fail-closed（不误绑、禁购），由 Operator 决定换边界或加日志门禁。
- **购买耐久语义与英雄一致**：`TryRegisterPaidGun` 只建内存 career，下一次原生 Save 才落档；未保存退出与英雄购买一样随原生存档回退。
- **读档 anomaly 会禁购直到下一次干净读档**：冲突/未见证行的状态保持 `Unresolved`（本条会话禁购，但允许写“reservation 化”的快照自愈）；不会误绑、不会丢记录。
- **待续期/未持久对象**：绑定但存档时 inactive 的对象按 reservation 落档；若对象在存档期间仍 active 却未被 GetID 捕获，则拒绝整次附加档写入（保留旧 baseline，日志给键）。
- **reservation 不自动消化**：死亡/丢失后的记录保留（无 NativeId、无货架位），仅在 4096 容量处拒绝新购买。
- **在线/非权威 fail-closed**：`TrackAllowed=false` 时注册/购买/读写档/查询全部不动作，不发 RPC；离线第一版。
- **货架物理还原仅覆盖精确 stock gun**：不移动位置、不推断最近弓、不解冻未知对象；失败/冲突/部分读档、联机、异层一律不写。
- **货架声明按“证明”撤销**：收集（`pickedUp`）或敌人认领（`enemyClaimer`）后，声明会在下一次有界巡检（≤2s）、下一次权威存档、或下一次读档还原前被清除；联机/异层期间不触碰任何元数据。只清元数据，绝不移动物品或撤销原生拾取/物理。
- **失败的货架物理还原保持责任**：该 gun 记为 pending → 禁购并在 `StatusText` 显示；每次 Tick 重试**同一把已证明的当前世界货架枪**，成功即解锁。若该 gun 被消耗/释放或证明不再成立，条目随之清除（不永久锁）。缺 body 属异常（原生 Droppable 必有 Rigidbody2D），只记录日志并持续 fail-closed。
- **未验证的实机项**：真实 2.4 的 Promote/DropItem/Drop/FastSpawn/FastDespawn 命中与顺序、`GetID`/`TryCreateOrFind` 真实触发、真实货架枪的持久化与物理还原、`pickedUp`/`enemyClaimer` 在真实拾取/被偷流程中的取值时机、跨岛与暂停语义。全部需要 Operator 的构建 + 实机/日志证据。
