# #290 HeroShop 选址严格相切回归

基线 `release/v9.5.13@c1e9e5c`；分支 `codex/shop-placement-boundary-20261025`。仅源码与 host 验证，未启动游戏、未安装、未跑 Native 验收。

## 修复

`il2cpp/HeroShop.cs::HeroShopPlacement.Find` 原先把候选限制为领地外端加每个分割 cell 的 3 个内部采样，漏掉 cell 边界本身。现对已有 `thresholds`（含 `low/high` 与 interval 端点）的每个 float 调用原 `Add`，继续由 `Inside`、`HashSet` 去重、`value != center` 和原 native `fits` 裁决。Native adapter、排除半径、排序、付款与身份逻辑未动。按新增候选将查询上界从 `6n+6` 更新为 `8n+6`，仍随边界线性有界；排序仍为 O(n log n)。

## 证据

| 检查 | 结果 |
|---|---|
| 修复前源码 SHA-256 | `57954a58e3891c6c22fee643e210cfd0d0a3f134df65c5f02de44be5984dc42c` |
| RED（旧源码 + 新断言） | exit 134；`exact strict Payable contact at the only legal centre is found`，行为断言失败，非编译错误 |
| Core（修复后） | exit 0；`HeroShop core: 71 assertions passed.`（原 67 + 4） |
| heavy-shield-shop-runtime | exit 0；`PASS 372 production payment/native adapter assertions`（基线 372） |
| musketeer-shop consumer | exit 0；`PASS 45 shop payment/rack/retention assertions` |
| 修复后源码 SHA-256 | `754659fde8fd10c4cde52b624c9141c2d560388f81fcae38ebfdd0b50c481051` |

新增回归使用独立数学 oracle：eligible Payable 为 `abs(p-center) < distance+half`（严格，来自已核 native Retrieve），blocker 保持闭区间。两 Payable center 0/8、distance 2、shop half 2、`left=-1/right=8` 时唯一合法 center 为字面量 `4`，旧真实 `Find` 返回 false，新实现返回 `x==4f`。同点用闭区间 blocker oracle 必须拒绝；另有全拒绝、重复 boundary/重复搜索确定性。既有 narrow gap、ignored superset、1500 interval、非有限/反向区间、Inside 与双店避让回归保留。

## Interop 说明

`dotnet build tests/hero-shop/Interop.csproj`（README 命令）在本机 exit 1：项目硬编码 Windows `E:` 引用（58 errors）。注入本机 frozen 2.4 refs 后仍有 2 个既有缺项错误：`ShopV2Visuals`/`CustomShopLighting` 未被 `InteropStubs.cs` 提供；`c1e9e5c` 的 `HeroShop.cs` 同样引用它们，且 `tests/hero-shop/Interop.csproj` 自 v9.0.0 未改，属既有基线，未顺修。作为补充，同一 `HeroShop.cs` + `HeroShopBannerVisuals.cs` 对 frozen Mac 2.4 interop 的私有编译（仅补两个无关 MOD 侧类型占位）为 0 warnings / 0 errors；不据此声称 Native 验收。`tests/heavy-shield-shop-shell/test_contract.py` 因缺少未跟踪的 `artifacts/heavy-shield-approved/atlas-manifest.json` 无法运行，未作为本轮判据。

## 边界

本轮只证明源码与 host 纯函数行为。不声称已解决该岛 `NoSpace`（原 504 + extra 166 次候选仍全 false），不预付游戏内、安装、Native、Android 或真机验收。未改 Android pin、`Main.cs`、构建入口或正常存档。

## Operator 完整构建与独立复核

- Mac240、Win240、Win243三套全源私有NoDeploy编译均0 warnings/0 errors，275项资源均从实际PE读回逐项等于生产输入；Win243仅私有叠加PR #250 typed aliases，不声称已原生适配验收。Source仍为本页754659fd…，无占位MOD类型参与这三套完整产品构建。
- Android实际net6 probe编译0 errors/1条既有CS0169 warning；真实artifact SHA `6dcee5d4d317b795e7afea3abdb8f36e3d7dba8a76b1ddfce068ec6baaa39abb`。AdapterTests带该artifact为1503 passed/1 failed，唯一失败是基线`WallEngineerRuntime.cs` frozen pin：c1e9e5c及当前均17cc9e83…，测试期望6a5c196a…；本PR未改该源/pin。Android专项已独立确认属先前P4修复漏同步，交其收尾；本PR不改hash跳检，不称全绿或设备通过。
- 曾有两次Operator命令不完整：net10 override恢复目标不符、Adapter未传artifact参数，均原件保留；最终以上真实net6及带artifact结果替代，不把工具失败归罪产品。
- 独立实现审查APPROVED。额外核对公开工具编辑顺序，RED与最终Program同为3e717d8a…；早期`red-source-sha.txt`含的是加测前HEAD基线，不是红测Program版本。红断言与旧生产SHA相符。
- 本阶段GLM5.3/max方案审和源码独立复核、Root测试/构建各留本机证据。未变计划/输入复用，不为文案再跑产品。所有原始日志、模型流与精确清单留`.local/tasks/shop-placement-boundary-20261025/`，无玩家存档/原始日志入库；正常18文件及原仓库未提交文件均保持。
