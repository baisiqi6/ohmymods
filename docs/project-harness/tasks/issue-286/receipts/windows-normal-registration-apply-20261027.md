# Windows 正常档当前岛登记恢复：已应用，待玩家实测（2026-10-27）

用户明确授权完整备份后修复 Windows 正常档，再由用户进入商店实测。用户已正常保存退出；本轮重新取得当前匹配输入，不使用此前离线候选覆盖新进度。旧[副本回执](windows-empty-island-copy-20261027.md)保留为当时证据，本文记录后续实际写入。

## 备份与新鲜输入

- 正常存档目录 `C:\Users\ADMIN\AppData\LocalLow\noio\KingdomTwoCrowns\Release` 与独立游戏副本 `E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.v2.4.3-P2P\BepInEx\config` 合计 **25 文件、12,545,158 bytes**，完整备份至 `E:\ohmymods-backups\issue286\before-registration-20261027T132210Z-ceeaf356`；逐文件哈希匹配。
- 已核对当前用户 SID、同用户游戏进程为 0、实际插件 DLL 及两项 Native 二进制指纹；默认 CheckOnly 对完整备份、原数据与候选 pins 检查通过。
- 新鲜 global 为 `b4fec84df2ed59e992d6586638b052e816fe96cd80af0f8aaede4ef4ac6eee7f`，primary pair 为 `2b07465363ffc17480777f8b10f288797aa10c0e188d8f1d7a8265701dd19f96`。盾卫 key 仍为原 schema4、slot1/challenge0、land8 两责，当前 land11 缺行。

## 恢复内容与实际执行

恢复仅新增当前 land11 的 `hashKind=2 / claims=[]` canonical 登记行；付费盾模、旧 land8 两条责任及其 hashKind1 保持。global 解压内容仅替换 HeavyShield 的唯一 value token，前 19,334,740 bytes、后 655 bytes 与其余原生数据保持；生产 Save 从新 global 重建配对，新新恢复通过、两向新旧混配拒绝。新鲜 canonical 为 `728067b1853e7fc1f0c39bcce0efb885931b36e8b8564980df7c58e053c59d08`，仍属于离线计算，未冒充 Native 现场读数。

- r2 首次实际执行在第一个 `File.Replace` 处因 PowerShell 将第三参数 `$null` 绑定为空字符串而停止。独立读回确认两份正式目标仍为上述原哈希，`Replaced=[]`、游戏进程为 0；只留下两份已核验的候选临时文件。原停止 journal 保留，没有自动回滚或覆盖后续进度。
- r3 使用已在独立 dummy 文件验证成功的 C# literal-null `File.Replace(source,target,null)` shim，锁定旧停止 journal 哈希，仅复用那两份准确路径和哈希的临时文件；使用独立 r3 journal/receipt。每次替换前检查全部 25 文件，已完成目标采用新哈希，其余采用原哈希。真实 Windows Parser/CheckOnly 与独立增量审查通过。
- **2026-10-27T13:50:03.4166258Z 实际返回 `APPLIED`**，仅替换下表两份正常数据文件。两个目标各自为同卷替换，不声称跨文件原子事务。

| 正常数据文件 | 应用后 SHA256 |
|---|---|
| `Release/global-v35` | `b05cb2021f3a08d82826af18d1bca5336fc515bcbbb9e681f8d850d7c4b3dcd4` |
| `BepInEx/config/KingdomEnhancedMod/ModSave/musketeer-pair-global-v35.json` | `ecdded19b50f5f79dde616f25ad1dd0761f667a7fead94f169bac9198274a1b0` |

应用后的独立读回确认：25 个正式数据文件完全符合预期，仅以上两份改变；全部备份仍匹配原始数据，临时文件已无残留，游戏进程为 0。插件 DLL 保持 `63d43ff17ff601d1c0f95f26beeafdde21ddffae4e73419d640119c8060f32ae`，未部署新 DLL、未改其它配置/sidecar、未启动或结束游戏、未写 Steam 正式目录。

## 验收边界与证据

用户已被告知可以启动正常 Windows 游戏实测。当前只证明备份、精确两文件应用及独立读回完成；修复后的 F5 身份、Native canonical、实际付款/领取/转职、换岛及保存冷重进 **仍未验收**，不能记为购买已修。PR #287 的生成预防、Mac 私有副本结论与其它问题不由本次数据恢复改变；不更改 Issue/checklist 状态。

原始数据、脚本和日志仅保留本机任务目录 `shield-purchase-windows-20261027/normal-apply/`：`backup-summary.json`、`expected-inputs.json`、`fresh-recovery/`、`after-failed-replace.stdout.json`、`replace-helper-probe.stdout.json`、`check-r3.stdout.json`、`apply-r3.stdout.json`、`independent-readback-summary.json` 及 `fresh-review/r3-verdict.md`。批准并执行的 r3 脚本 SHA256 为 `6785bad8aaab8889b2386bbe74521ce560a381b071004a3e9eba72551cf8c4c4`；旧候选和失败回执均保留。
