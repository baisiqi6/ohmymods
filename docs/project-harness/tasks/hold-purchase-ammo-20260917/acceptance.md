# 弹药长按购买候选验收

2026-09-17 现有所有世界长按连续购买开关加入投石车5币火药桶与希腊火塔2币弹药。只识别PayableWorkshopBarrel或同GO活动FireTower精确拥有的PayableComponent，火塔AI disabled不误排合法客机。弹药会话捕获owner并在加速/续买/等待回执/PerformPay/Tick复核，owner替换结束旧hold；复用原生扣款/容量/设施就绪/钱包/距离/暂停及PerformPay成功回执，客机本地Completed不视成功。默认off和首次正常/.6秒后加速节奏保持，不新增setting、hook、全场扫描或弹药库存写；只同步原面板/配置帮助文案。候选0b1ad4e1，build=9.0.0-hold-purchase-ammo-20260917。针对行为回归、actual2.4接口及完整构建、方法资源审计和独立复核通过；8PNG和侧枪架等其他玩法保持。已闭游戏备份安装既定E独立副本，28份原生档/附加档/配置hash保持；未启动游戏。未commit/push/publish，公开9.0.0不变；真正连续购桶/满塔停止和主客机回执仍待实机，不把模拟测试当实测。

行为测试/2.4接口编译不是实机验证；详细证据见result.md、review.md与receipts。
