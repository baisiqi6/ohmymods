# #300 快速购买扩展：源码/主机交付，未安装（2026-10-27）

基线 `release/v9.5.13@4d3931dbcaa28aaa326992221c77f39337604ff8`，分支 `codex/quick-purchase-20261027`。本任务独立于 #286 正常档盾卫登记、#298 宝石奖励；不更改钱包/价格/出货/存档协议。新增兼容功能按未来整批 MINOR 计账，本 PR 不单独递增发行版本。

## 行为与修复点

- 火枪店是 4 Coins 的自定义 PayableComponent，旧分类仅认可 FireTower 的同类付款点，故漏接。新小 partial 只核当前 `_payable/_owner/_object/_layer`、同 GO 实例与活动身份。它不借用排斥手动操作的 AutoRestock 资格，也不以购买库存条件判断身份。火枪走既有非 Baker 工具店加速/续买，原生 CanPay/CanSelect/forceBlock、库存与存档身份保护保持。
- 原来不支持的目标，当前整价 `Price>=7 && Currency==Coins` 时仅加速当前一笔。沿用按住 0.6 秒后的 1/4 投币间隔，最低 0.03 秒且绝不放慢；保留第一枚/原生 Holding/飞币时序。每次重验对象、实例、场景、价格和币种；不用“钱包仍足够整单”阻断已投部分金币的正常交易。交易完成/取消/回 None 即结束该 hold，不合成下一笔，不跨同对象升级阶段。
- `ShopIsGoods` 仍独占续买、收据与补货等待。普通高价 PayableComponent 不再误入 goods-only owner 检查；原白名单的低价续买、货满、客户端确认、范围、暂停、失焦和字段归还机制保持。
- 独审发现真实火枪 `OnPay` 出货失败会退币后正常返回，不能仅凭 PerformPay 推断成功。原反例一次长按产生 10 次失败订单。修正位于真实两处拒绝责任边界：不完整支付的原生 Cancel 前、`!success` 的退款前，通知并终止精确玩家/付款点的既有 hold。通知不抛出、不改退款、不新增结果账本；AlreadySettled 重复回调不通知。未 Bind 的早帧只凭精确 completing payable/当前火枪身份保守终止；后续真实松开重按仍可再试。

## 验证与独立审查

| 项目 | 实际结果 |
|---|---|
| Root 变更前 source-linked suite | 103/103 |
| Root 最终 source-linked suite | **133/133**，exit0，输入 pins 再读一致 |
| 独立 Reviewer 最终原 suite / 扩展反例 | **133/133 /144/144**，exit0，输入 pins 未变 |
| Root Mac2.4.0 完整源码 NoDeploy | **0W0E**，346 Compile /275 resources |
| Root Windows2.4.3 实际 `.281.3` 冻结源最小增量 NoDeploy | **0W0E**，284 Compile，原38资源保持 |
| Root Android probe | **1 warning /0 errors**；未改文件 HeavyShieldBanner 的 CS0169 |
| Root Android AdapterTests | **1503 passed /1 failed**，exit1；既有 WallEngineerRuntime frozen-pin 失败，不能报全绿 |
| 三端最终产物真实 IL | OnPay 各恰2次 Notify，均先于取消/退款；AlreadySettled 真分支 ret |

第一轮新功能红例为 16 failed/110 passed；退款修订的 no-op hook 红例为 5 failed/128 passed，包含期望1次、实际10次的失败订单。红例、一次早帧测试夹具失败、之后绿例和首次 Android1502/2 都保留，不无痕改写为成功。最终133相对原103增加30个场景；不复用作者中间126/127的叙述作最终计数。旧断言未弱化。NativeSim 是原有模拟器，并非游戏实机；新小 helper 与通知桥链接真实生产源码，真实大 OnPay 接线另由三端产物 IL 核验。

Android唯一失败源在本任务和基线字节一致：`WallEngineerRuntime.cs` 当前/基线 SHA `17cc9e83525f6e9aa92c8e521b8d14eca7495c498b3af8bc90991b97b96f87ee`，既有测试 pin 期望 `6a5c196a539f1987b22388f7660fce0d20105a32a275fbde865fc7d06f997ce9`。本任务不越界重钉。Root 一处非必要 Android 菜单提示已撤回，避免为文案扩大 frozen pin 范围；保留现有功能开关，仅更新桌面说明。Android显式消费者只补新 partial Compile 项，无 pin/Main/保存变动。

Windows私有构建先核已装冻结源与 release 的 HoldPurchase/MusketeerShop 原件逐字相同，再只叠加此次源和新 partial；未夹带新 Palace 线。它不是完整 public-release Win243 全树兼容验收。构建沿用真实项目 net6.0 target，host tests 为 net8.0，SDK10；不是框架升级。

## 模型与交付判断

Root 按北京时间22点协议派发 OMP DeepSeek Flash；公开事件实际 provider/model 为 `deepseek/deepseek-flash`。同阶段 GLM 决策 r1 修正后通过、r2 approved、退款 r3 approved、最终代码交付 r4 approved；实际事件为 `zhipu-coding-plan/glm-5.3`。均请求 thinking=max；公开响应未独立标示 provider 内部 effort 值，不冒充服务端强度证明。独立内置 Reviewer 首轮 REQUEST CHANGES、修复后 **APPROVED（source/host）**。同阶段有效证据复用，读取、截图、已定命令不新增审批。

Ready PR 只表示本次代码交付可供合并审查；没有合并、正常安装、游戏/AVD运行、SSH、配置或正常存档写入。Issue 在 PR 未合并时保持开放。Native 的手动出货/续买、7币实际项目、取消/售罄、联机与长期观感仍待对应平台游戏验证，不用主机结果预付。

## 精确产物及证据

- Mac DLL `bdb6903e86cf8fedc59ce863dcf3773bd9168c7ae2e3070928e1d7302593a63b`
- Windows最小增量DLL `33f257a4ece1cadf2254327d57bb452ddc2cb43378fdea6a10c78e472489e057`
- Android probe `a469023cbc166b8ff6c44a863b5b35cbabaf5c1d85906346766c46817bc419e3`

这些是私有验证输出，不是正常装版 hashes。本机 `quick-purchase-20261027/` 保留 `root-verify/final/`、`implementation/`、`independent-review/r2/review.md` 和 `actual-artifact-IL.json`、各决策公开模型事件/结论与源码 pins；原始游戏数据/二进制和私人机器信息未提交。

Provenance: Mac Max / Codex · Maintainer Operator · acting_for=ohmymods Mac Operator.


## 后续实际合并安装

用户授权后的合并SHA、双端正常包新DLL、完整备份/独立读回及Native边界见[联合安装回执](../issue-298/receipts/merged-player-install-20261027.md)。本文前述“未安装”保留为原源码阶段历史，不作为当前状态。
