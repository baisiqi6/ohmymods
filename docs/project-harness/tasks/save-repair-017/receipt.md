# save-repair-017 执行收据

- 执行时间：2026-08-16 14:41:47 +08:00
- 游戏进程：执行前、替换紧邻前及执行后均为0
- 脚本：`scripts/repair-fire-hermit-passenger.ps1`
- 脚本 SHA-256：`86F58C237C4D77833565E525B9A84909FE97F5C698B1863E62C225E24DE26C0B`
- worker/reviewer：plan approved、script static approved、真实 dry-run 后 `APPLY_APPROVED`，
  Apply 后独立复读并获 `EXECUTION_APPROVED`

## 输入与备份

- 输入：`%USERPROFILE%/AppData/LocalLow/noio/KingdomTwoCrowns/Release/global-v35`
- 输入长度：817,111 bytes
- 输入 SHA-256：`C3A8CEF5B3B59B0C4A763235B138381ED6327ABAAA2311F95530624AC17E55E8`
- 新备份：`%USERPROFILE%/AppData/LocalLow/noio/KingdomTwoCrowns/Release/KEM-backups/`
  `global-v35.before-fire-hermit-passenger-20260816-143300`
- 备份长度/hash与输入完全一致；没有覆盖历史备份。

## Dry-run 与 Apply

- 即时身份：schema16、campaigns=2、active campaign=1、Call of Olympus biome=5、challenge=0、
  reign=1、currentLand=6、currentReign.isCurrent=true。
- 全目标campaign对象=11,665；Dynamic netID980=0、non-Dynamic netID980=0；
  `HermitFire` / `Hermit Fire` / `Fire Hermit` 标记对象=0；原有Passenger=0。
- Fire修改前两份状态均为 `position/player/land=0/0/0`。
- 唯一修改路径：
  - `/campaigns/1/hermitStatuses/6/position`
  - `/campaigns/1/currentReign/hermitStatuses/6/position`
- 两处均仅 `position:0→5`；`player=0、land=0`保持不变。
- dry-run candidate SHA-256：`5C43780197C30F2B2F843D7139A5281A76CD836C9295F9307310F9A24FEE0DFE`，
  818,055 bytes；dry-run后源文件不变且没有创建备份。
- Apply使用完全相同的脚本、Input/ExpectedSHA/ExpectedLength与新BackupPath，并显式`-Apply`；
  输出hash与dry-run candidate精确一致。

## 写后独立复读

- 最终源：818,055 bytes，SHA-256=
  `5C43780197C30F2B2F843D7139A5281A76CD836C9295F9307310F9A24FEE0DFE`。
- Fire两份状态均为`5/0/0`，两份7项hermitStatuses数组一致，Passenger恰1。
- 把上述两处position在before/after root中归一为0后，整棵`JsonNode.DeepEquals=True`；
  证明其余campaign、岛屿对象、五位隐士、小屋、任务、经济、地图与未知字段语义零变化。
- 对象数及所有Fire对象门禁保持不变；candidate/rollback/system-temp残留=0。
- 仍待实机：首次读档P1只携带1位Fire隐士、放下变Roaming、火焰塔升级、重复读档/登船/
  换岛/死亡换君主不重复不丢失，以及无Pool/NetID/RPC异常。

