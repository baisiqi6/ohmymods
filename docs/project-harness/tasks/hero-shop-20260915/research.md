# 英雄商店接入调查（尚未实现）

本文保留初始只读调查结论。后续当前岛实现已完成代码回归与候选构建，最新状态见同目录acceptance.md；跨岛运输与实机验证仍未完成。

## 原生投币

原版 ShopPlanner 使用固定 ShopType 槽位，不为原创商店虚构枚举或挤占已有商店。候选方案是自有 GameObject + 原生 PayableComponent + 自有 owner 接口，使用原生投币/撤销界面。

2.4 证据由只读 worker 基于本机 interop 与原生反汇编核验：
- PayableComponent.Pay RVA 0x664e30（224 字节）要求非空 owner；null 到 0x664efb 异常路径。不能只挂 PerformPay postfix 而留空 owner。
- PayableComponent.CanPay RVA 0x664860（288 字节）依次检查 base、owner.CanPay、IsLocked。
- Payable.SendTransactionStarted RVA 0x669120（320 字节）没有离线早退，要求 parentHeaderRef；不能因为首版离线就省略 CRPCHeader 的正常注册生命周期。
- 本机 Il2CppInterop.Runtime 提供 RegisterTypeOptions.Interfaces / InterfacesResolver 及带 options 的 RegisterTypeInIl2Cpp；注入实现 IPayableComponentOwner 的组件是候选，接口虚调用尚未实机验证。
- 现有 Kingdom.HasBorderLoaded、campfirePosition、GetBorderSide / GetBorderSideIntact、OnBordersChanged 可作为选址边界输入。实际摆放算法还未实现。

旧 game-source/Assembly-CSharp-2.1.0/PayableComponent.cs 和 IPayableComponentOwner.cs 只作为流程说明，不能替代 2.4 核验。

## 付费身份

现有 HeroArcherRuntime 是自动选举，运行期 pointer/GOID/life 不是持久身份。必须拆分 PurchasedIdentity 与 ActiveHero：关闭功能或临时上塔/登船先撤效果，不能删除付费身份或自动免费补人。死亡、丢弓、名额归属及关闭后重开规则需要在实施契约中明确。

建议独立 hero-identities.v1.json，复用 KnightIdentity 的完整岛精确快照/原生 Persistent ID 映射机制，但不往骑士 style 收据文件混写英雄数据。Hermes 个体记录含原生数据扩展，不是本功能要求的纯 sidecar 可直接照搬方案。

新购买只进入运行期账本，随下次正常原生保存对应的新快照写入 sidecar。不能照抄 KnightIdentityLoadSeed 把新付费结果写回加载时旧快照：这样会发生金币回滚而免费保留英雄。未保存退出时购买与金币一起按原版回退，不代替玩家强制保存。关闭开关后保存仍应携带已购买收据。

用原版继续游玩并重新保存可能导致精确快照失配；不得猜测位置或 NetID 恢复。跨岛匿名运输的逐人身份恢复也不是现有骑士附加档已解决的能力，首版不能未经实现就承诺。

## 本轮验证界限

只读代码/原生接口调查和首张美术草图完成，任务文档 validator 通过。没有新增玩法实现，没有构建/安装游戏 DLL，没有操作用户存档或配置，没有进行游戏内商店、支付、注入接口、读档或联机验证。
