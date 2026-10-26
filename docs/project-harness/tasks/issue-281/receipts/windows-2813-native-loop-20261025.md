# Windows .281.3 首座坐骑岛出航与冷重进实测

用户在正常Windows2.4.3手动测试，Agent只读两份日志并存证，没有输入、结束游戏、替换DLL或修改正常存档。产品版本 `.281.3` / DLL `63d43ff1…0f32ae`；代码commit `d3da7c9`，安装依据前次回执。

## 本轮通过范围

- 第一实例 PID169940，birth `2026-10-26T06:09:49.7418219Z`：来源岛8的火枪手禁船资格命中；保存捕获15/15，`applied=1 conflict=0 missing=0`，无读错／截断，并出现native pair verified。
- 地图结果提交到物理岛11；新生成8种坐骑设施、8个放置验证marker完成，进入Playing。用户确认坐骑表现正常。
- 首实例的有效读后身份样本记录 `Menu:OnButtonSaveAndQuit`；实例随后结束。第二实例 PID163088，birth `2026-10-26T06:23:16.4307565Z`，以同 `.281.3` 启动并加载已有岛，进入Playing，没有重新grant+inject。
- 用户明确确认新进程仍在坐骑岛、坐骑及地图剪影正常。第二实例有效样本没有 `_ShowSaveFailurePrompt`、`CaptureFault`、`save-missed-row`、`sync-reject`，另一次UI保存及pair verified也已记录。
- 读者在两次实例结束后正常exit0，无GameKilled；final-drain缺读后身份复核，未作验收主证。主证仅使用绑定PID/birth/path且ReadIdentityAfter=true的样本和用户操作结果；不只凭mtime判session。

据此，本轮“来源8出航→第一坐骑岛11生成→保存退出→新进程冷读并继续游玩”的单机存读档回路通过，不代表所有玩法或所有岛均通过。

## 仍有问题

第一实例有2次 `HashSet Enumerator / Knight.<EmbarkArchers>` 集合修改异常（在源保存前），上轮.281.2相同出航阶段原始日志未见；属于本候选新增观测，需要查实际状态写入者，尚未定根因。首次登岛后还有50条 `Array Out of Bounds: Increase Size (Forest...)`。第二实例均未重现，但没有再次出航，不能据此判为已修。

冷读时 `MountIslandFresh NoEnvelope/Unknown` 诊断及加载早期skill pool暂未就绪日志仍保留；它们未造成这次实际冷读拒绝，不能用Error标签直接当作存档失败，也不能称整轮零错误。

第二岛、返源核对15名火枪手、异常已登船降级、各坐骑技能／战斗、联机及第二次退出后的第三进程尚未验证。PR #282仍Draft、Issue #281开放；本记录不合并、不发版，不将额外错误无痕改写为通过。

## 可复核证据

私有case：`transport-contract/manual-2813-20261026T055641Z/`（主任务boat-scope-next下）；`events.jsonl`、`reader-exit.json`、`interpretation-rules.json`、`round-acceptance.json`，独立Astra `first-voyage-log-audit.md` / `cold-reload-audit.md`。原始日志与玩家身份不上传。

| 有效样本 | SHA256 |
|---|---|
| `00052-pid169940-bepinex.log` | `39bae40821d5d86fe259b743216ff9d196ee0914cfa5a5ea4812f4c786a3ed2f` |
| `00052-pid169940-player.log` | `018314e23d1c27679799c061d249a6377484988c8652fbc6ac92f745d4b22ba4` |
| `00083-pid163088-bepinex.log` | `70b45db2aa22a3c1e39ffc3a55d71cd4266e018b0a79cabf8e8d1f674aa3a8d6` |
| `00083-pid163088-player.log` | `f2f7690f0165f33520f20877af2ee346f45c0e26428530ed89dc9e54aa934f60` |
