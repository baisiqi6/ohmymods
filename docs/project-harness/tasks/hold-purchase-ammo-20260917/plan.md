# 快速购买覆盖火药桶与火塔弹药

用户请求：现有快速/长按连续购买开关同时覆盖投石车火药桶（原价5）与希腊火塔弹药（原价2）。沿既有所有世界便捷开关，不新增独立设置，不绑定希腊银行/自动补货状态。默认关闭保持原版；开启仍首次正常、连续按住.6秒后金币间隔加速与成功后续买，原生库存/设施就绪/钱包/距离/暂停/客机回执门保持。

最小生产范围PatchPlayer_HoldPurchase.cs白名单识别：精确PayableWorkshopBarrel；同GO活动FireTower且PayableComponent._owner.Pointer精确相符。不要借用SiegeAmmoCounts有缓存context/恢复副作用的Classify或扩大所有PayableComponent/建造/升级/祭坛。火塔owner变化/禁用/池生命周期不得沿用过期goods资格。

不直接扣钱包、生成弹药、写库存、改原价或容量，不新增Harmony钩子或全场扫描。原PerformPay成功回执+原UpdatePayState续按链复用，不能将本地Completed当客机出货成功。必要测试覆盖5/2币连续购买、首次不加速、上限/设施拒绝/钱不足/松开/开关off/owner不匹配/暂停/客机等待拒绝，不把模拟当实机。

当前本机候选6e2d89f1侧枪架必须保持，公开9.0.0不改。worker按17:52北京时间工作日14–18使用内置subagent；reviewer独立。root集成build标记、实际2.4构建、DLL方法/8资源审计、独立复核后在游戏已关闭时同步本机候选；不启动游戏、不覆盖存档/配置，不commit/push/publish。游戏运行时先等正常保存退出。
