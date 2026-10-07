# 顶部 HUD 随身钻石（Issue #175）

在现有顶部日历条增加本机君主的随身钻石，与主城金库分列。基线 `19c55438af0be47eb271ba861f9342e381299323` 已含 #169/#170/#174；分支 `codex/hud-gems-20261006`。本条只负责代码交付；主会话负责累积构建及冷安装，不用公共源码验证 DLL 替换带宫廷入口的运行候选。

代码交付已完成：PR #178 固定 head `5223323b54588adb7e65d2a5b040e74214a4f6a8` 合并为 `03b7e584959d46c53ea8938dc822b60e3c8d618a`，Issue #175 已关闭。产品与测试9个输入逐项绑定公开验证摘要和独审，合并不表示已安装或玩法已验。

## 读取责任与接入位置

原顶栏没有钱包信息；新增读取接在 `CalendarHud.Tick` 既有半秒缓存，Draw 只用缓存文本，Clear 清空。每次从当前 `Kingdom.playerOne/playerTwo` 读取，不跨世界保留 Player/Wallet；仅接受活动、位于当前 world scene、`hasLocalAuthority` 的玩家。钱包 `Gems` getter 为唯一余额来源，不读金库、存档或世界总量，不写钱包、不发 RPC、不增加 Harmony target 或全场扫描。沿用原 ShowCalendarHud 开关。

当前 2.4 ARM 原生链证明此身份/归属约束：Player.Awake 在 `0x7d6498–0x7d649c` 取同 GameObject 的 Wallet 写入 player.wallet；Wallet.Awake 在 `0x956208–0x95620c` 取同 GameObject 的 Player 写入 wallet._playerRef。Wallet.Gems `0x95559c` 读取 currencyAmount 中 Gems 槽。Server_OnClientConnected `0x76b560` 与 UNetRouter.CatchupPlayers `0x783010` 清远端 P2 的 local authority；客户端 Kingdom.RecvSpawnP2 `0x6e22bc` 在 SendInputTo 后建立本机 authority。地址仅适用于本轮冻结 ARM 输入，非跨版本常量，不用于运行 patch。

单本机显示数量（0 显示 0），未知显示 `—`。双本机分别显示 `1P <值>` / `2P 钻石 <值>`；一人的钱包缺失、负值、归属不符或读取故障时，保留其槽位并只把该值标未知，避免将另一人的数字误认为自己的。相同 Player pointer 去重；不同玩家共享错误钱包引用不合并。受限 getter 故障最多记录一次 warning，不影响日历本体，不重做副作用。

## 审查修正与撤回

Worker 初稿把“玩家存在”与“余额可读”合在一起。独审反例证明双人中一人缺钱包/负值/读取故障会折成裸数字。Operator 改在确认身份后建立未知槽位，再读取余额，公共回归直接覆盖产生处。

审查中曾建议排除 TunnelInput；补查当前原生 ReceiveInput 后撤回：tunnel 是输入路由，允许本机君主控制另一 controllable，并不等于远端身份。最终未加入该排除，回归证明本机钱包仍显示；远端过滤由上述 local-authority 原生链承担。Owner 校验有实际 Awake 绑定证据，因此保留。

## 验证及边界

- 公共 helper 40/40、实际 CalendarHud 接线 46/46；原 CalendarReader 回归 95,702 项断言通过。
- 单人、双人、钱包更换/缺失、错误共享、负值/故障、换世界/菜单/开关、缓存不在 Draw 读值、四档屏幕列布局、GUI 状态归还与无钱包写入通过。渲染 doubles 不代表真实字体观感。
- 真实 2.4 ARM interop 全量构建 0 warning / 0 error；验证 DLL SHA256 `9cf278b4710cd3cd4834abbc481be7f1663200e2af105e81e4ddbd41c69cd4b7`。它不含宫廷 WIP，仅用于验证，未安装。
- 对 #174 公共验证 DLL `241d28a9…` 的 Cecil 审计通过：6825 旧方法保持，10 处变化/13 新增均在 CalendarHud（含 compiler-generated nested）、ModConfig 或新 helper；34 PNG、338 Harmony targets、其它类型/字段/方法元数据及资源集保持。
- EXharness validator 0 warning；本 Issue 没有独立受管 checklist 节点，不为小型显示功能新建重要节点，也未直接改 canonical JSON。

独审结论见 [review.md](review.md)。原生 disassembly、worker provider 会话、构建/测试/审计原件保留本机任务 `hud-gems-shop-art-20261006/hud/`，公开摘要见 [validation-summary.json](validation-summary.json)。未操作游戏，未写运行 DLL、配置或存档；真实拾取/支付、字体布局、联机运行、Windows/Android 和宫廷联动仍待相应平台及玩家验证。

版本账：最近正式 `10.8.35`；“顶栏随身钻石显示”为小型新功能暂记 minor +1，下次正式发布整批去重核算。本轮内部构建标记沿用，不创建 tag 或正式包。盾卫/英雄商店外观另属 #176/#177，不在本 PR 更换素材。
