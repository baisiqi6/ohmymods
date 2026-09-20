# 火枪手自动补货 worker 结果（2026-09-17，生产已冻结待独立终审）

实现 role 8，原 0..7 顺序不变。补货服务使用独立火枪计数族，覆盖量为活火枪手、当前世界已购买且未拾取/未被敌人认领的枪具、队列预留订单。普通弓箭手和弓具缓存排除已绑定火枪职业/枪具；同一个 Character 后绑定或解除职业通过精确事件重新分类，不重扫角色名册。

枪铺新增专用自动入口，位于 `il2cpp/MusketeerRestock.cs`（MusketeerShop partial）：`TryGetAutoRestockTarget`、`CanAutoRestock`、`PurchaseForAutoRestock`。自动入口需要希腊、单机权威、真实当前店铺、原生 Bow 缓存可用、枪架未满、非保存/暂停、无玩家付款、身份计数可确认。完整预检后同步调用中央 `TrySpendForAutoRestock(8)` 一次，立即将订单转 Departing，复用现有 `TryCreateGun` 原生池出货和 paid identity 注册。没有向手动付款传空玩家，没有伪造玩家支付状态，没有从玩家钱包扣款。手动 4 币凭据与退款路径保持。

自动入口结果明确分为 Rejected、Purchased、PaidUncertain。出货失败或扣款后回调异常都保留已付订单阶段并将目标标为本世界故障，不盲重试或发免费枪；这是既有补货 uncertain fault 策略，不提供自动退款。成功出货按原生 PerformPay 口径记录 CoinsSpent 原价 4 一次，统计异常独立记录，不重试购买。

Identity 的新读取口 `TryGetRestockCounts` 只遍历有界自有 career 注册表，不新增 scene/resource 周期扫描。Ready、writable、resolved、baseline、context/world 精确且无 pending stock restore 才公布计数，读取异常不当作零。地面掉枪继续计库存，已拾取/敌方认领/foreign/失效不计。

独立复核后的边界修正：读取活跃/场景/layer 失败直接让整个计数不可用，不经吞异常的显示查询 helper；Despawn getter 异常不会成为终止证据。零人数的 readiness 翻转也唤醒已停下的规划器。手动真实 receipt Arm 后，native 取消但未退浮币时仍阻断；native 返币和取消选中完成后自动恢复，残留 Arm 本身不会永久锁住自动补货，也不修改该 receipt。联合测试直接链接生产 receipt 核心覆盖此流程。

只在已有立即 Despawn 边界新增本会话计数证据：prefix 在 Playing、非 saving、当前世界且确切绑定时捕获 career/life/world；postfix 真实 inactive 且身份仍一致才允许该 unbound career 作为已确认损失计零。Bind 清除对应证明，状态/世界变化清空；普通 OnDisable、延迟/拒绝 despawn、life/world 变化、未知历史都不构成证据。没有修改 archive、Persistence 或任何存档模型。

## 已执行验证

全部命令使用 `C:/Users/ADMIN/dotnet8/dotnet.exe`；工作目录 `C:/Users/ADMIN/projects/ohmymods`。

| 工程 | 结果 | 说明 |
|---|---:|---|
| tests/auto-restock/AutoRestockTests.csproj | 147 passed / 0 failed | 原 120 + 新 27；生产服务与生产 MusketeerRestock adapter 联合编译运行，替身仅游戏/计数/native shipment 边界。覆盖 preflight 零扣、单次 8+1、无 TransactionComplete、满架/Bow/未知/存档/支付/暂停/联机/世界、中途关闭/满足目标、失败故障不重试、统计故障、其他职业不受阻 |
| tests/auto-restock-counts/CountTests.csproj | 86 passed / 0 failed | 原 84 + 同 root 分类变更与 paid gun 剥离 2 项，无额外名册枚举 |
| tests/musketeer-restock/Tests.csproj | 72 assertions | 生产 Identity 实际读取口及现有 drop/despawn 生命周期；无 archive 写入 |
| tests/musketeer-identity/Tests.csproj | 273 assertions | 既有生产 archive/identity/persistence 回归 |
| tests/musketeer-shop/Tests.csproj | 18 assertions | 手动 payment/rack/retention 保持 |
| tests/musketeer-shop-perf/PerfTests.csproj | 550 assertions | Bow 缓存与购买性能策略保持 |
| tests/musketeer-shop-perf/actual-interop/MusketeerShopInteropCheck.csproj | build 0 warnings / 0 errors | 真实 2.4 interop，包含生产自动 adapter 和 Stats API，不运行游戏 |
| tests/greek-bank-scope/GreekBankScopeRegression.csproj | 32 passed / 0 failed | 必要 disabled firegun stub 兼容，原业务断言不改 |
| tests/greek-bank-assistants-scope/Regression.csproj | 24 passed / 0 failed | 同上，税收助手回归 |
| tests/musketeer-defense/Integration.csproj | 118 assertions | Character 健康字段 stub 兼容，原业务断言不改 |

## 限制与后续

终止证据仅属本会话、不持久化。保存读档后若旧历史 career 没有可确认绑定，自动补货会停止，不能把这些未知记录当作死亡。旧火枪完整 archive 身份终审、跨岛和联机缺口保留；此任务不重写其模型。联机火枪补货明确禁用。真实游戏采购/掉枪/重载仍待实机；没有启动游戏、部署、修改用户配置/存档、提交或发布。全量实际 2.4 构建和 DLL 审计由 root 完成。

生产冻结 SHA256：

| 文件 | SHA256 |
|---|---|
| PatchEconomy_AutoRestock.cs | 5D4180B6025D8CF55A406593F78481B91426DB1C3133051495909F3DA823C8D2 |
| AutoRestockCounts.cs | 8D8DB993A9D0A962BD9E9717ABA8302B9D73A72C786CD723BA4A2CCBC2BFEFC5 |
| MusketeerIdentity.cs | 83A87E8AC523FCB614F15FDAF3548B878E6C20FE6FFDAC1632E8825DFE2C2B88 |
| MusketeerShop.cs | F14C39BBDA3C0621A946D3ACE0A2877823B2EEC0CBA16C195152E6A97C6063B8 |
| MusketeerRestock.cs | 2DCD38762962BDC6C3D854BFE54051FF509816E778FE08F08900D1CEBCE0E949 |
