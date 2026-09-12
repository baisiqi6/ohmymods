# save-repair-011 执行收据

- 执行时间：2026-08-15 23:24:03 +08:00
- 游戏进程：未运行
- 脚本：`scripts/repair-beggar-overflow.ps1`
- 脚本 SHA-256：`FE4469AB1E4D53B6A7C46568B3391ADF76CC4EBBB918F0502CB38D0FEB07261D`
- worker/reviewer：静态审查与真实dry-run后 `APPLY_APPROVED`

## 输入与备份

- 输入：`%USERPROFILE%/AppData/LocalLow/noio/KingdomTwoCrowns/Release/global-v35`
- 输入长度：751,068 bytes
- 输入 SHA-256：`68D4F779DA3CFA45A659D2082B2B15F135777699EC4A309F1F6AEAE14C724B16`
- 新备份：`%USERPROFILE%/AppData/LocalLow/noio/KingdomTwoCrowns/Release/KEM-backups/global-v35.before-direct-beggar-prune-20260815-232403`
- 备份长度/hash与输入完全一致。

## Dry-run 与 Apply

- 独立dry-run：`before=158 removed=148 after=10 groups=5/5`
- dry-run candidate SHA-256：`2C681C5C2CA01E6BBCBB5F05BDEA32FC63A0D86EA563F68325D12C08D088F87A`
- Apply使用完全相同Input/Backup/ExpectedSHA/ExpectedLength并显式`-Apply`；输出hash与dry-run candidate精确一致。

## 写后独立复读

- gzip、严格UTF-8与JSON均可读；`serializedSaveDataVersion=16`、currentCampaign=1、currentLand=7。
- land7 objects：2194 → 2046；Beggar：158 → 10；按营地坐标-120/70分组为5/5。
- 最终文件：748,730 bytes；SHA-256=`2C681C5C2CA01E6BBCBB5F05BDEA32FC63A0D86EA563F68325D12C08D088F87A`。
- 临时/rollback文件：0。
- 存档不进入Git；仍待独立测试副本实际读档与补员硬上限验证。
