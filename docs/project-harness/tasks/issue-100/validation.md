# Issue 100 验证记录

## 变更面

- 生产：`il2cpp/MainBankerFixedDomain.cs`（新）；`il2cpp/PatchEconomy_Banker.cs`；
  `il2cpp/PatchEconomy_BankAssistants.cs`。
- 测试：`tests/banker-fixed-foundations`（新）；`tests/greek-bank-scope` 与
  `tests/greek-bank-assistants-scope` 契约翻转适配；`tests/coin-courier-economy` 仅 stub 编译面适配。

## 托管回归（本机 .NET 8，`dotnet run -c Release`）

| 套件 | 结果 |
|---|---|
| `tests/banker-fixed-foundations/Regression.csproj`（新） | 30 passed, 0 failed |
| `tests/greek-bank-scope/GreekBankScopeRegression.csproj` | 35 passed, 0 failed |
| `tests/greek-bank-assistants-scope/Regression.csproj` | 73 passed, 0 failed |
| `tests/coin-courier-economy/CoinCourierEconomyTests.csproj` | 37 passed, 0 failed |
| `tests/auto-restock/AutoRestockTests.csproj`（相关回归） | 159 passed, 0 failed |
| `tests/auto-restock-counts/CountTests.csproj`（相关回归） | 86 passed, 0 failed |

## 实际 2.4 interop 整包编译

以精确 2.4 BepInEx/Il2CppInterop 引用（161 个 reference）编译全部 `il2cpp/*.cs`
（186 既有 + 1 新增）：**0 warning / 0 error**。编译输出仅留在本地证据目录，不部署、不写游戏目录。

## 本批覆盖的契约

- 选择器：未建 ±9（`PayableUpgrade.nextPrefab`→`Wall`）、不对称、Overgrown ±8.75 + 外层 ±17
  取最近；升级同坐标归并；不按 name/orderedWalls/border/硬编码。
- 加载门：未通知不发布；`fromSave=false`（新岛）与 `true` 均为成功；通知即绑定 context key，
  key 读不到不武装；旧通知不得授权新 context；**capture 入库时对“入场通知”取 local 快照**，
  扫描期间同 context 重入成功通知会作废本次快照并保留新 pending；**可读 context mismatch
  永久作废该通知 proof/pending**，回到旧 wrapper 无 fresh 通知不复活；重复通知撤销后重发一致。
- 快照完整性：缺单侧/NaN 候选/NaN campfire/遍历异常均不发布且不部分发布；失败后低频重试成功。
- 缓存冻结：外墙扩建、同坐标升级新根、墙体变化都不重选。
- 边界：严格 `left < x < right`，等于边界归助手；NaN/Infinity 全链拒绝（claim/pickup/助手）。
- 活动：wander 收进已证域（`min(8.75, 最小距离−0.25)`）；域未知归还原生值；
  **部分归还（setter/身份故障）后同代重新开启，首个前缀即时重新应用**。
- movement：Idle/GrabCoin 域外位置 goal 取消一次并排队 Idle；goal 向内（含 Actor 域外）不动；
  任何已排队切换、DropOff(4)/Payout(5)、Object goal、非有限 goal 一律不覆盖。
- 认领回收：多币快照只清本人（exact `friendlyClaimer==banker.gameObject`）当前层的域外
  Player/Coins；他人/农田/异层/域未知不动；**target 清理两条路径都带 current-layer 门**；
  明确证据才清 `_targetCoin`。
- 钱包硬门：exact 当前 authority 本体钱包 + 同层 Player/Coins；域未知/非有限/域外拒绝；
  **资格成立后位置读取故障一并 fail-closed**；其他钱包、非玩家/非金币、异层、客机（无 world
  auth）、异世界全部保持原生。
- 助手：固定域同源 scan+commit；非有限币拒绝；边界币归助手。

## 未验证（实机，待合并后安装）

读档/换岛时首次捕获的真实事件顺序与观感、连续升级/摧毁期间的银行家观感、密集投币下认领
回收频率、主客机联机表现。本批未启动游戏、未触碰玩家存档、未提交/安装。
