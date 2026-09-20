# 时间/金库像素风本机验收

已集成 canonical IL2CPP 源码并部署 E 独立副本。DLL `2BD2055C1554DE7D0C5BA0910E974C209C2FEA418CE69FDD5EC3CD5C8B3EFB8B`，build `5.0.0-hud-tower-refill-20260912`。同包保留本次塔位重新分配修复。公开5.0 release/tag/ZIP不变，未commit/push，Steam和Mono未动。

仅常驻时间/金库样式：900×86缩为552×54紧凑顶部中央条，暖黑底、暗铜直边、暖白文字、16×16 point像素小图标；一次有界查找已加载Zpix字体，失败回退GUI字体，不写共享font/material/texture。日期、时间、季节/下一季日期、金库值及半秒缓存来源保持不变；左上角留空，尚未实现人口统计或占位UI。银行整数去币后缀，硬币图标和主城金库标签保留。

验证：完整构建0警告0错误；HUD与塔helper共144条可达Unity调用无unstrip失败桩；HUD独立review通过无阻塞发现；说明用HTML布局脚本语法及4个交互状态通过。受控启动 `2026-09-12T13:52:55.5749645+08:00` 至 `2026-09-12T13:53:43.7723097+08:00` PID 36700，无新增BepInEx Error/Fatal。save前后 `52DEF381398BE2AF8C235B9538B41DF081730E139EF857F52A14CEE7174867DD` 一致。

仍待游戏内目视验收：隐藏启动没有捕获HUD重绘/字体ready日志，不能声称已经看到Zpix或最终字号。HTML是示意数据与近似布局，不是游戏截图；本次最终回复嵌入 `C:/Users/ADMIN/.codex/visualizations/2026/09/05/01a06fd5-70a6-7150-bc85-16ea0c87ffb5/kingdom-hud-layout.html`。原自动补货四开关和15/15/15/10目标未改；本次未解决/证实真实采购。
补充日志核验：最终Player.log与上一508CC2启动均2条LogError签名，新增0，见player-log-comparison.json。
