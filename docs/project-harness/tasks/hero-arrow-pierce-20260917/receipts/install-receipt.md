# 安装回执：hero-arrow-pierce 候选 → E 盘独立副本

时间：2026-09-18 约 00:55 北京时间（游戏关闭状态安装）
方式：`receipts/install-verify.ps1`（同目录脚本，含完整校验逻辑；输出值誊写如下）

| 项 | 值 |
|---|---|
| 游戏进程 | 未运行（脚本门禁通过后才执行） |
| 用户数据基线（LocalLow/noio/KingdomTwoCrowns 全树 SHA256 汇总） | `D2FCA830`（17 文件） |
| 安装前 E 盘 DLL（正式 9.4.5） | `0F8C1FC8` |
| 备份（→ receipts/backup-formal-945-KingdomEnhancedMod.dll） | `0F8C1FC8`（与安装前一致，门禁通过） |
| 安装后 E 盘 DLL（候选） | `2482F0D3` = il2cpp/bin/Debug 最终构建（build=9.4.5-hero-arrow-pierce-20260918） |
| 用户数据复检 | `D2FCA830`（与基线一致，未被触碰） |

未启动游戏、未 commit/push/publish；公开 9.4.5 不变。
实机待验：穿墙效果、0.65 观感、trail 粗细、密集齐射帧耗时、联机。

（注：本回执为事后誊写——安装当时脚本输出在 operator 会话控制台，本文件按已核实的输出值补档，用于审计留痕。）
