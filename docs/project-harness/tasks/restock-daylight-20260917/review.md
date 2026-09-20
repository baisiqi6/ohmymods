# 自动补货白天门：独立复核

**最终结论：本轮有界 PASS，无剩余阻断项。** 对应精确候选 `96566DEF78BC6F990234BA1FEAB2CA45273B539118FA8B86FFF4EEE9B23DA202`，基线 `63EA50ADDE922CC1600EF11B8D2C4CFE10FBB12C555D83914CED55DA7DBD354A`。下面初轮问题记录为历史，均已按最终源码复核关闭。

## 最终修订与证据

- 白天事实使用原生 Kingdom.isDaytime；对象缺失/读取异常拒绝自动补货。Tick 在订单推进前挡夜间，并在 StepOrders 后再次复核，日出通过原 cadence 重新规划，没有新增逐帧全场扫描。
- ScanAndAssign 借人前复核；TryCreateOrder 在 reserve 返回后、Place 前后复核，翻夜归还刚借的助手；失败返回后 Scan 再判断夜间并撤回其余未付订单。EmitCoin 入口、prefab 准备后、Spawn 后都有门；Spawn 内翻夜时已生成的表现币走 fake/despawn，不继续 MoveTo，不作为退款。
- TrySpendForAutoRestock 入口和 TryPrimeSharedLedger 后、首次扣款写前都复核白天，覆盖火枪直接自动入口。该门仅位于自动补货专用扣款函数，没有改普通银行/收税/手动购买入口。
- Reset(true) 的原始收尾语义保持：未付订单撤回，已付只释放助手；没有退款或再购买路径，故障目标记录跨夜保持。GetSummary 读取当前日夜状态，保留 world/关闭优先；界面帮助文字明确“白天补货”。
- 读取并复核回归源码与 root 日志：**161 auto-restock + 35 bank + 24 real-assistants = 220 项通过**。覆盖全部九类夜间冷启动/日出恢复、未付各阶段、回调翻夜、直接火枪最终门、已付故障跨夜保留及普通银行仍工作。银行测试的 PlayerPrefs 读取回调可在 Prime 中翻夜；没有以只修改 service 的 stub 门替代该最终付款测试。root 修正冷启动资金与字段拼写属于夹具修正。reviewer 未重复运行套件。
- 实际 2.4 完整 build 日志为 **0 warnings / 0 errors**。
- 独立重算 before 与候选 DLL hash，并用 Mono.Cecil 比较方法指令、locals、InitLocals/MaxStackSize 和异常处理边界：**3644 方法保持、10 改变、8 新增、2 移除**，具体集合与最终 receipt 一致；移除的两项是 FinalizePurchase 闭包重编号。HarmonyPatch 类型不变，全部 **8 张嵌入 PNG 字节一致**。
- 对 source-before.json 独立核对，只有 AutoRestock、Banker、ModPanel 和 Plugin 四项变化；四项当前 hash 与 source-delta.json 全部一致。0.9 火枪外观、伤害可靠性和其他既有逻辑保持。

未运行游戏、修改生产/存档/配置或安装。本结论限代码、模型回归及编译/产物审计；真实昼夜边界、助手归还与 UI 表现尚未实机验证，不宣称已完成实战验收。

## 方案与初轮记录（已被上述最终结论更新）

既有 `Reset(true)` 可用于夜间撤单：服务只释放订单持有的助手并清空订单，设置重新规划；没有退款或再次购买调用。助手协调器先清 RestockReserved / Moving / WaitDeadline，再在当前世界仍有效时归还。借出前本来要求已携带/未入账金币清零。`_faultedTargets` 不在 Reset 中清除，只有 world 变化才清，故已付不确定回执可以跨夜保留，不会因日出重试故障交易。

最终实现需核以下窄边界：

- 原生 Kingdom.isDaytime 是唯一日夜口径；未知/读取异常保守停止。Tick 日夜门在计数刷新与订单推进前；夜间不新增规划、借人、动画币或扣款；日出仍按原 cadence 重新规划。
- 不能仅在 Tick 开头缓存一次 day。计数刷新、借人及 native 行为回调可能翻夜，须在后续创建订单/动画币前复核。TryReserveForRestock 可能先归还携带金币再 reserve；返回后若已翻夜须释放租约，不能继续 Place/建单。
- 最终 TrySpendForAutoRestock 在可能回调的 ledger 准备之后、首次 treasury 写入之前仍须有白天证据。它是火枪直接自动入口与普通服务订单共同的经济门；不要误改手动购买、普通银行或税收功能。
- 入夜对未付单撤回，已付单只完成收尾；不得退款、重扣或因已付款后的日夜变化把原生交易再执行。已有 `_faultedTargets` 跨夜保持。
- 九类角色统一覆盖；夜间状态文案不覆盖非希腊/关闭等更优先的说明。针对回归应验证各未付阶段、已付故障、日出恢复以及回调中日夜变化，不能只测静态 cold start。

本次预审只读源码与任务契约，未运行游戏/写用户档，未修改生产或测试。不扩大到其他功能或旧 blocked 诊断。

## 初轮实现窄预核

隔离工作副本快照：AutoRestock SHA256 `964D5745B4DCA94EB5BA2025813775DE6D759C2B5415D1318A2D25CB33D1C2CF`；Banker `942B3C4C4D7B4487D1C60EED24FE8A802BFD536BD16E27E397A315916747E044`。以下行号指该快照，未执行 worker 测试。

当前 Tick、OrderContextValid、FinalizePurchase 的基本夜间门已经加入，Reset(true) 保留原故障账本。但仍有三个回调边界缺口，需修后再审：

1. Banker.TrySpendForAutoRestock 只在 TryPrimeSharedLedger **之前**检查 day，准备完成后至首次 `_stashedCoins` 扣款之间没有最终日夜检查。应紧贴经济提交前再核。对应测试覆盖准备阶段翻夜，以及直接火枪自动入口拒绝夜间付款；普通银行路径保持。
2. ScanAndAssign:959–1038 / TryCreateOrder:1064–1109 没有 day 门。计数、ShopBuyable 或上一订单回调翻夜后，仍可借人、Place、创建新单。建议 Tick 刷新/StepOrders 后复核，TryCreateOrder 在 reserve 前及 reserve/Place 返回后复核；借出过程中翻夜必须释放租约，不继续下一角色规划。
3. EmitCoin:754–780 没有 day 终门。OrderContextValid 在 day 检查之后还会执行 ShopBuyableSnapshot:751，后者若翻夜，仍会生成动画币。需在 Emit 入口、准备完 prefab 到 Spawn 前、Spawn 后到 MoveTo 前复核。Spawn 内部回调翻夜时不可能回滚已经发生的 Spawn 调用，应沿既有 fake/despawn 路径立即清理，不继续动画或付款。

另 GetSummary:262–275 只返回上次 Tick 摘要，未直接查询当前夜间状态；暂停/首次查询时可能显示旧白天文案。建议保留 Greek/关闭优先后再直接显示夜间等待，并测“不跑 Tick 直接读摘要”。

现有九类冷启动、未付阶段与普通已付夜停测试有价值，但不能覆盖以上回调内转换。再补“付款成功后原生 callback 抛错 / gun PaidUncertain → 夜间 Reset → 日出”断言：故障目标仍拒绝重复、总扣款一次、不退款。测试针对边界即可，不需建立新大测试体系。
