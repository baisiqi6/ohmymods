# #300 快速购买扩展实施计划

**Goal:** 精确纳入4金币火枪店；对其余单次整价≥7金币的付款只加速当笔。
**Architecture:** 保持现有默认关闭开关、0.6秒后1/4间隔和0.03秒下限；从既有续买资格分出只加速判断。付款、CanPay、钱包、收据及RPC仍由原生和原商店负责。
**Tech Stack:** IL2CPP 2.4.0 / Windows2.4.3 preview，BepInEx6、Il2CppInterop、HarmonyX、C#，source-linked .NET tests。

基线 `release/v9.5.13@4d3931dbcaa28aaa326992221c77f39337604ff8`，独立分支 `codex/quick-purchase-20261027`。GitHub #300 已登记并读回；开放任务按快速购买/HoldPurchase/火枪关键词查重，无同范围在途实现。#158/#168/#173 为既有行为依据，不重做其修复；#281/#286 保存/身份不在本范围。正常 DLL/配置/存档、Steam/Mono、Main/build/版本号不改；只提交本任务拥有的文件。

## 单阶段实现

Files: `il2cpp/PatchPlayer_HoldPurchase.cs`，`il2cpp/MusketeerShop.cs` 的小只读身份接口，`tests/hold-purchase/` 必要源码链接测试/替身；现有开关说明如需更新仅改对应字符串。无新框架、新配置或扫描。

接口：`MusketeerShop.IsCurrentManualPayable(Payable payable)` 仅核当前静态对象/owner与传入payment的精确关系和当前有效层，不调用 `CanAutoRestock` 或 `CanPurchase`。HoldPurchase 火枪分支仍需按捕获owner精确重验，不缓存跨池复用资格。

- [x] 先在原有103场景上补红例：精确火枪4币、错误/更换owner、普通PayableComponent高价；7币加速/6币不加速，8币及非Coins负例；恰好整单钱包/部分已投；持续长按不合成第二单；动态币种/价格与失效、取消和归还保护。
- [x] 最小接入：火枪纳入既有非Baker库存店语义；`RecheckAmmoGoods`只给真实白名单，普通高价组件不误走goods-only重验。Bind失败不可保留新资格。
- [x] 单次加速每帧只读当前 `Price>=7 && Currency==Coins`、对象/实例/场景，异常不加速；不调用续买的整单钱包门（已投币时钱包可不足整价）。原白名单保留续买；其余只改变加速门，不arm、不合成、不跨同对象下一升级阶段。
- [x] 跑新旧source-linked测试，不改旧断言；归还/异常/停顿/菜单/失焦/换场景门继续有效。
- [x] Operator独立读diff与测试，独立Reviewer复查；GLM阶段增量复审必要修改，保留公开model事件。北京时间22点worker按协议使用OMP DeepSeek Flash/max。
- [x] Mac完整源码、Windows实际C最小增量NoDeploy验证；Android共享源probe与hostAdapter验证（分别标注）。不把host当native/device验收，不顺手修既有全量Win243编译缺口。
交付步骤：写receipt/progress后仅提交 owned diff，push并创建关联PR/附加；未合并不关Issue，安装/实机另留证。实际交付状态以GitHub为准。新增兼容功能按未来整批MINOR计账，本任务不独立重编号。

## 审查与证据

Root独立基线：当前 source-linked hold suite **103/103**，private output under `quick-purchase-20261027/baseline/`。决策r1实际provider `zhipu-coding-plan` / `glm-5.3`、请求max，verdict修正后通过；条件为资格分离、普通高价component排除goods重验、Bind异常复位、火枪停购退款不重发。r2对这四点增量复审，不重复读日志/截图审批。原始公开模型事件、结论及源码pins只留本机task目录，不提交私人游戏数据。

## 独审增量（r3）

真实 `MusketeerShop.OnPay` 出货失败会退款但正常返回，PerformPay不能独自证明此自定义店履约成功。独立合同反例一次长按产生10次拒绝；因此新增范围为该源两处实际拒绝分支，在退款/Cancel前通知精确玩家/付款点的hold终止，保持原退款和后续真实松按重试。GLM5.3请求max的r3已approved（公开model事件核实），要求通知不抛出、不通知AlreadySettled重复回调、覆盖Bind前的早付款且无新账本。已有126绿不足以替代此修复的红绿与复审。

Root中间NoDeploy：Mac/Win0W0E，Android1既有unused字段warning/0E。Android宿主1503/1，失败为未变化的WallEngineerRuntime pin：当前/基线均17cc9e83…，断言期望6a5c196a…；不在本任务改写。仅撤回Root一处非必要Android菜单提示改动，避免为文案扩展其pin范围。最终结果以增量后的receipt为准。

## 最终源码阶段

退款边界修订和独立复审已完成，Root133/133、独立144/144，GLM r4批准代码Ready PR交付。精确构建与未验边界见[回执](receipt.md)。不因此合并安装或关闭未合并Issue。
