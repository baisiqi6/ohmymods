# Issue #255 — 忍者/狂战士商店跨世界扩展（PC 2.4.0）

## 目标
复用现有跨世界商店机制，把忍者、狂战士商店扩展到 Europe（中世纪）、Dead Lands、Norse Lands、
Shogun（幕府），保留希腊既有适配、各世界原生商店与科技进度：
- Europe(0)：Ninja + Berserker
- Dead Lands(2)：Ninja + Berserker
- Norse(3)：Ninja（原生 Berserker / ShieldShop 保持）
- Shogun(1)：Berserker（原生 Ninja 保持）
- Greece(5)：既有行为不变

## 基线（实际游玩版核对，非默认主目录）
- Mac 游玩 DLL SHA256 `5fb9c5f8afcdb2774e7f250dc9ae9bd3c24f9218f37e241c262b6acd4ee89352`（v10.9.38），
  安装回执记录 release `ee153aca3f2b70aee5f73de46af4362b08e332b8`；Mac 游戏 2.4.0 / Unity 6000.0.61f1 /
  BepInEx 6.0.0-be.788 / .NET6。
- 主线 `release/v9.5.13@1f9c675`；`git diff ee153ac..1f9c675 -- il2cpp` 为空（仅一个测试文件与
  Android 文档不同，见 Issue #254 基线记录）。
- Windows 2.4.3 独立宿主（BuildInfo 2.4.3 rev23537 hash36d5d6e9a5，Unity 6000.0.66f2）；本 Mac
  会话未在其上构建/实机，证据另记。

## 槽位审计（静态）
依据 `game-logic-map/castle-upgrade.md` 的原生入队白名单（Mono 2.1.0 已验证；2.4.0 仅 ShopType
枚举值重排，全部按名引用）：

| 原生商店 | 白名单 biome | 结论 |
|---|---|---|
| NinjaLeft/Right (7/8) | 仅 1 | 0/2/3/5 空闲（5 已被既有 mod 使用） |
| ShieldShopLeft/Right (12/13) | 仅 3 | 0/1/2/5 空闲；希腊方案已实机验证 |
| PikeLeft/Right (9/10) | 0/2/3/5 | 不占用；北欧即原生狂战士商店 |
| ChangeRuler (11) | 2 | 不占用 |
| ChangeItem (14) | CatchupToLevel 3/5；ReQueueAllBuildings 仅 3（两条路径不同） | 不占用 |
| Workshop (3/6) | 0/1/2/5 | 不占用 |

因此：忍者用 7/8；狂战士用 12/13（其 prefab shopType=Pike，必须在 Awake 临时改写为 ShieldShop
才会自注册到 12/13，然后恢复 Pike）。Norse 不覆盖 12/13 与 9/10，保留原生 ShieldShop 和狂战士店；
Shogun 不写 7/8，保留原生 Ninja。

## 实现
- 新增 `il2cpp/WorldShopPlan.cs`：纯静态世界→计划（Ninja/Berserker/Greece 标志），无 Unity/interop 依赖；
  Android 编译时只保留 Greek 计划，其它 biome 返回 none。
- `PatchEconomy_Shops`：按计划把 `ShopBerserker_norselands` 注册到 12/13（0/1/2/5）；
  SidedShop Awake 前缀/后缀的身份改写从 Greece 判定改为计划判定（Norse 不在计划内），并新增
  `HarmonyFinalizer` 异常兜底（原生 Awake 抛异常时 Postfix 不执行，仍恢复 Pike）。
- `PatchRoles_Castle`：CatchupToLevel / ReQueueAllBuildings postfix 改走 `EnsureWorldExtraShops`；
  Ninja 入队泛化为计划判定（Castle5）；非希腊计划世界新增 `EnsureBerserkerShop`（Castle4，
  host authority，幂等，先校验 12/13 prefab 映射存在，槽位异常 fail-closed）；
  ShopPlanner.Start 后重试同样泛化；`ReRegisterModPools` 按计划恢复 Ninja/Berserker 工具、角色与
  忍者 FX 池（希腊顺序保持不变；非希腊分支缺某个 tag 时只跳过该项，不提前 return）。
- `PatchRoles_Ninja`：运行池与 thicket/tree/beggar-camp 隐藏点门控由 Greece 改为计划 Ninja 世界；
  幕府原生竹子隐藏点不受影响。
- Android probe 项目显式加入 `WorldShopPlan.cs`（Castle 被 Android 共享编译）；Android 行为由
  plan 的 ANDROID 分支保持 Greek-only。

## 依赖审计
- Holder：既有跨 biome 注册已把各世界 `uniqueCharacters` 加入 `tagCharacterPairs`；新增世界可直接
  `GetCharacterByTag("Ninja"/"Berserker"/"BerserkerLeader")`。
- 同步池：ThrowingStar 固定 syncID=41（与原生 bamboo 一致，冲突即拒绝）；Smokebomb local；
  ToolNinja / ToolBerserker / 角色池走既有 `RegisterSyncedPool`。每次 `PoolManager.InitPools` 后由
  `ReRegisterModPools` 重注册，host/client 均执行。
- 拾取/转职：Katana→Ninja 由 Peasant 白名单；BerserkerTool→Berserker 由 Worker 白名单，均原生路径，
  既有 `PatchRoles_Berserker` 六次招募序列在所有 biome 生效。
- 照明：新店使用原生商店 prefab 与其原生灯具/光照组件；`CustomShopLighting` 仅在 Hero/Musketeer/
  HeavyShield/CoinCourier 自建店 Shell 内 Attach（实现文件 grep 证据），不对原生店附加第二套灯。
  跨 biome 的原生灯具观感仍需实机确认。
- 迁址库存：本次不克隆商店、不新增库存所有者；购买物由原生 CreateItem 生成独立 Droppable。自建店
  的 `CustomShopStocks/StockTransit`（Issue #225）不覆盖原生 Ninja/狂战士店。迁址后库存行为由原生
  逻辑负责，需实机验证。
- 残余：`ToolBerserkerLeader` 池全库未注册；本 mod 第 6 次招募直接换 BerserkerLeader prefab，不需要
  该工具池，但若扩展世界存在原生 LeaderTool 来源会缺池，列为实机检查项。

## 验证
- PC 纯编译（Mac wrapper 模板、源 glob 指向本 worktree、无 Compile Remove；镜像模板的 canonical EmbeddedAssets.props，嵌入 PNG 资源；不复制游玩版）：
  0 warning / 0 error，DLL SHA256 `2e2356cf004ad0f72faa41ace704f1f22fc3e5642708a86ec8971aa5bd38d5cc`；
  metadata 含 WorldShopPlan/EnsureWorldExtraShops/EnsureBerserkerShop/SidedShop_Awake_Finalizer 与对照
  PatchRoles_Castle（见 receipts/pc-build.log、pc-metadata.txt）。
- 计划矩阵测试：PC 13/13、ANDROID 模式 10/10（`tests/world-shop-plan`，无 Unity 依赖）。
- 既有回归：shop-cleanup 49/49；custom-shop-lighting 201 checks/0 failures；custom-shop-stock-transit
  213 checks/28 tests（后两套源未改，仅作无回归旁证）。
- Android：用本机既有 interop/loader 目录实际构建 `OhMyMods.AndroidProbe.csproj`：0 errors（1 个既有
  CS0169 warning，来自 Patch_HeavyShieldBanner.cs，与本改动无关），probe SHA256
  `b41ccfeb1861c9bbcc0aace5e98029c1ebdaaeaecaccd3a359d9c517c8261b9d`；`AdapterTests` 1265 checks /
  0 failed（含 frozen-source 校验）。本任务改动的共享源不在 Android 冻结 pin 列表内
  （receipts/android-frozen-pins.txt），Android 行为由 plan ANDROID 分支保持 Greek-only；probe 仅本地
  构建，未安装/未部署。见 receipts/android-probe-build.log、android-adaptertests.log。
- 未做：实机购买/领取/转职/战斗/迁址/读档/换岛/照明/联机（主会话排期）；Android 设备/模拟器玩法未验。

## 复审修正（approved 后落地的非阻断项）
- 身份改写增加 `HarmonyFinalizer` 异常兜底。
- `EnsureBerserkerShop` 入队前校验 12/13 prefab 映射存在（资源缺失时 fail-closed）。
- 非希腊 `ReRegisterModPools` 不再因缺 Ninja tag 提前 return，Berserker 池仍可恢复。
- 删除生产未使用的 `NinjaMinCastleLevel/BerserkerMinCastleLevel`，测试不再误称 native 门槛。
- plan 表标注 ChangeItem 在 Catchup/ReQueue 两条路径的白名单差异。
- 已知测试缺口：非希腊 dispatch 尚无自动化用例（shop-cleanup doubles 只覆盖希腊清理语义），
  由主会话实机步骤覆盖；如需可在后续任务补 doubles。

## 生效与边界
- 仅在 host world authority 入队；单机通过不等于联机通过。跨世界联机未验证。
- 槽位分配依赖原生白名单（2.1.0 文档 + 2.4.0 既有希腊实机证据）；四个新增世界的 2.4.0 原生
  入队白名单没有直接证据，需实机复核（日志会记录 queued/skip 与错误）。
- 读档/换岛：Castle 两条入队路径 + Start 重试 + PoolManager 重建后重注册；商店实例身份由 Awake 改写
  每次重建/读档重新计算。
- Mac 当前停在保存失败弹窗：本任务未操作该进程、未替换正在使用的 DLL、未覆盖/恢复共享存档。

## 供主会话的实机步骤（简短）
1. 完整退出游戏并处理保存失败弹窗；备份共享存档。
2. 由主会话安装候选到独立 ARM64 运行包（不要装 Steam 正式目录），记录 DLL SHA256。
3. 分别新建/进入 Europe、Dead Lands、Norse、Shogun：按上表确认商店出现；购买→平民/工匠拾取→
   转职→战斗；扩墙/科技变化后确认迁址与库存；保存→退出→重进；换岛后返回确认槽位。
4. 夜间/暗处确认照明无冲突；观察 Pool not found/重复 syncID/NRE；联机另行开任务验证。
