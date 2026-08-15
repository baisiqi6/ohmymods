# save-repair-012 执行收据

- 执行时间：2026-08-15 23:51:18 +08:00
- 游戏进程：未运行
- 脚本：`scripts/repair-peasant-overflow.ps1`
- 脚本 SHA-256：`EFC14FBD5A9F513BF880057A8230CFBA50DA9AB02BA44C2AD652A0661E6336E7`
- worker/reviewer：真实dry-run、独立复算与逐行审查后`APPLY_APPROVED`

## 统计纠正

- 当前存档真正带`WorkerData`的工匠只有14名；先前约458名的结论误信了对象池残留`name`。
- 421个名称以Worker开头的对象实际按Peasant prefab保存和恢复；本次脚本完全不使用`name`判职业。
- 用户据此明确确认删除350名Peasant，而不删除真正Worker。

## 输入与备份

- 输入：`%USERPROFILE%/AppData/LocalLow/noio/KingdomTwoCrowns/Release/global-v35`
- 输入长度：748,730 bytes
- 输入 SHA-256：`2C681C5C2CA01E6BBCBB5F05BDEA32FC63A0D86EA563F68325D12C08D088F87A`
- 新备份：`%USERPROFILE%/AppData/LocalLow/noio/KingdomTwoCrowns/Release/KEM-backups/global-v35.before-direct-peasant-prune-20260815-235118`
- 备份长度/hash与输入完全一致。

## Dry-run 与 Apply

- dry-run：`Worker=14 Peasant=733->383 Greek=638->288 Norse=95 removed=350 Beggar=10 groups=5/5`
- dry-run candidate SHA-256：`63884D91421A7B74AD0049C8FB00BFD3E910857F05005490B2704E856FE93FED`
- Apply使用相同Input/Backup/ExpectedSHA/ExpectedLength并显式`-Apply`；输出hash与dry-run candidate精确一致。

## 写后独立复读

- gzip、严格UTF-8与JSON均可读；`serializedSaveDataVersion=16`、currentCampaign=1、currentLand=7。
- land7 objects：2046 → 1696；真Worker：14 → 14；Peasant：733 → 383；Greek：638 → 288；Norse：95 → 95。
- Beggar仍为10，按营地坐标-120/70分组仍为5/5。
- 最终文件：728,071 bytes；SHA-256=`63884D91421A7B74AD0049C8FB00BFD3E910857F05005490B2704E856FE93FED`。
- 临时/rollback文件：0。
- 存档不进入Git；仍待独立测试副本实际读档与性能体感验证。
