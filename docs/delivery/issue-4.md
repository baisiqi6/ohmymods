> **Agent provenance:** `Mac Max / Codex` · role=`Maintainer Operator` · acting_for=`ohmymods Mac Operator`

# v9.5.12 correctness 候选验收记录

关联 [Issue #4](https://github.com/baisiqi6/ohmymods/issues/4)。本次将此前本地完成的15个源码/测试文件原样提交；基线为v9.5.12 / 3b9653156c384375380906569bc3877db8a1a3c2，文件hash见 [source manifest](issue-4-source.json)。这是事后补登记和Draft审查，不是游戏验收或发布。

## 修复

- 再基线后重绑持有足够owner/life证据的旧骑士；新旧混合保存/重载不丢GUID。所有失证、重复owner/life和容量缺口在可能写盘前拒绝。
- IO重试不因main缺失而跳过backup保护；来源备份非Valid或字节变化则拒绝恢复。未引入新schema或跨进程CAS。
- 每箭账本持有实际接管的碰撞对，外观撤回与碰撞归还共同收尾；TTL、异常或复用不丢责任。失败时保留待归还责任，不让新renderer/collider绕过旧责任。
- 恢复商店identityMs赋值；购买/枪架调度保持。

## 2026-09-21 独立PR工作树复跑（Mac）

| 检查 | 结果 |
| --- | --- |
| stable / load-seed / integration / network | 16 / 34 / 9 / 48 通过 |
| runtime | 57通过、1失败 |
| archive | 45通过、2失败 |
| arrow gold / missing | 177 / 13 通过 |
| appearance gold / missing / invalid | 164 / 19 / 19 通过 |

三个身份失败与前序候选完全一致：`a_failed_write_retry_is_logged_once_and_keeps_the_previous_file`、`io.ioFailureKeepsOldFileAndLeavesNoTemp`、`io.transientIoFailureHealsWithinOneRetry`。现有FileShare.None夹具在Mac没有制造预期Windows文件锁；断言保留，不称Mac全套绿色。

可复跑命令（SDK8）：各 `tests/knight-*` 目录使用 `dotnet run --project <csproj> -c Release`。箭矢 `tests/hero-arrow-pierce/Tests.csproj`、`tests/hero-artemis-arrow/Tests.csproj` 先 `dotnet build -t:Rebuild -c Release`，再 `dotnet run --no-build -c Release -- --mode=gold`；缺资源构建传 `-p:EmbedArtemis=false` 并运行 `--mode=missing`；外观坏资源构建传 `-p:ArtemisPng=<absolute>/tests/hero-artemis-arrow/invalid-resource.bin` 并运行 `--mode=invalid`。每次模式变更都重建，不能复用上一资源模式产物。

## 2026-09-20 历史精确候选证据

- 同一correctness源码在Windows独立临时快照中 runtime58、archive47、musketeer303全部通过；没有改Windows维护者工程或存档。
- 历史Windows快照digest：`f5e29ee0a7d09cef1b6441b7761167c242eed48deff602ed213df97fc1b2b329`。
- 两份独立静态审查最终APPROVE，旧身份安全反例2项、箭矢原始反例4项通过。Windows维护者另在Issue3报告APPROVE WITH NOTES，其具体意见待关联，不能替代上述来源说明。
- 历史完整Mod编译是“v9.5.12 + correctness + 钱袋补丁”的组合，Mac/Windows各自实际interop均0W0E；**不是此独立PR无钱袋改动时的新完整编译**。
- 历史组合MacDLL `5af0e99d0176bc88ac2e3a9c1e36828b72435350076564decf1fa605b7bc400c`；WindowsDLL `909b58e709f4717ed9ace3fa481887036d4abc7d5285e042a59590337d89ea1e`。不随PR分发二进制。

## 未完成

Mac ARM64、Mac x64/Rosetta、Windows分别验证真实旧档自愈→招募→保存/重载、Unity碰撞撤销/复用、密集齐射、切岛和适用联机；未验收项不置done。未安装/发布，不改变版本tag。最终整合头另需完整构建及平台验收。身份重试保护不是跨进程原子写；未知对象保留有界归还责任可能让新效果降级，此限制保留。
