# 英雄驿站暂停闪烁修复验收

2026-09-15 英雄驿站暂停闪烁修复：cfe8e50a / build=8.0.0-hero-shop-stability-20260915。用户确认主要开关暂停菜单整座消失；旧TryContext只接受Playing导致Menu清理重建。现仅精确Playing/Menu、同kingdom/layer/当前Postbox/header且对象有效时保留；Menu阻止付款，首次暂停有待付币时复用原生取消路径，成功后标记，恢复复用原对象；其他状态和未知context立即清理并记录原因。Core54、Invoker7、实际2.4 interop与完整构建通过（后两者0警告0错误），生产接线审计和独立审核通过。对d005审计2861方法保持、8改变、8新增、3移除（含Clear签名与闭包编号调整），4PNG保持，地面基准与owner ABI保留。已在游戏关闭后备份安装正确E盘，存档和配置hash保持；未启动游戏/提交/发布。暂停保留、半途投币暂停、恢复付款与实际高度仍待实机验收，跨岛身份运输仍未完成。

OMP 18.1.19 / deepseek/deepseek-v4-flash / max worker完成有界实现，operator将保留策略收紧为精确Menu并删除2秒未知上下文宽限。独立reviewer已复核最终代码，无阻断。源码策略测试和Cecil接线检查不是Unity实例实测。Invoker隔离测试带未使用Probe字段警告，不影响7项指针/JIT检查。

日志基线 receipts/stability/before.log 显示同址反复ready；用户确认暂停相关。2.4原生Playing/Menu调查见stability-plan.md。此候选沿用原有付款结算/原生取消实现、沉地修复和IntPtr owner ABI，不改变资产、存档或战斗行为。

实际验收：连续开关暂停、靠近商店半途投币暂停、返回后正常购买；对照pause/resume/clear原因日志和视觉。关闭功能/离岛须正常清理。
