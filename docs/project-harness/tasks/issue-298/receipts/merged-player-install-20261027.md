# #298/#300 合并与双端正常包安装回执（2026-10-27）

用户明确授权“两个合并安装，后续有问题再修复”。此授权覆盖此前仅交付Draft/Ready的边界；不代表Native已验。本次仅两项功能，未处理盾卫旧档或其它未合并任务，未公开发版/tag/递增正式版本。

## 合并与代码状态

1. PR #299，head `3048942a2aaaf2f7579c461d8a5d6d272da4fba9`，merge **`e46fefa9ddd5f3413237edf93b61aff4a9cb4efd`**。
2. PR #301，原head `0c9142f`，并入新base后head `72b7c056f9e53f2cfe35103a2b90a6a26af0ef87`，merge **`fdfe9c4ad4341135719c528c3b818c15f061eb86`**。

两个merge均使用expected head SHA。#301仅解决progress顶部文档冲突、完整保留双方记录；最终整棵Git tree `e5128232aaa49616e2f3946210a18d207b449d45` 与已审、已验private union精确相同。独立source审查确认33文件为两PR并集，ModPanel仅合入预期Tick/Clear/提示串，无第三任务。

代码Issue #298/#300在两PR全部合并后实际关闭并读回。仓库checklist无对应条目，本机未发现harnessctl入口，未裸改JSON/DB、未虚构受管done。安装与Native状态独立于代码关单。

## 联合验证

- 独立合并树：Gem **78/78**、Hold **133/133**，pins未漂；此前独立反例144/144有效复用。
- Root实际Gem→实际Msk物理prefix组合 **28/28**；pipeline **233 assertions**；legacy **18 cases /405 assertions**，均通过。私有writer程序集命名冲突、pipeline漏CanonicalHashBuffer链接的初次夹具失败均保留；只修私有执行夹具，未改产品断言。
- 最终两正常包候选均 **0W0E**。Android合并树probe **1既有warning/0 errors**；Adapter **1503/1**旧WallEngineerRuntime pin失败（该文件与baseline同hash），不报全绿或设备通过。
- GLM5.3（请求max、实际公开model事件核实）对合并/安装阶段及Mac脚本修订进行审查，均按具体候选/脚本SHA留证；源/产物独审与实际Apply分开记录。

## 已安装的精确候选

| 平台 | 原DLL | 实装新DLL SHA256 | 变更 |
|---|---|---|---|
| Mac ARM64 2.4.0 | `055368a3964d307b9a3155160d6a2036cd554f8765c7ea79bdac198b6e59fcaf` | **`c0d7aa08f7a73a9865ca38a51478b66b685936341bbad0c310e0c73826b41b25`** | 9,140,224 bytes；冻结r4基底+9源overlay |
| Windows 2.4.3 | `63d43ff17ff601d1c0f95f26beeafdde21ddffae4e73419d640119c8060f32ae` | **`9ed9309c58a8934e5ed2336d9fbd627b2aba9e8d5a1a47ff9bcc5143283ca1ae`** | 4,594,176 bytes；原 `.281.3` 冻结基底最小overlay |

两者是**已实装冻结基线加两个PR的增量**，不是“整个最新主线/正式11包”；原版本/现有功能保持，不夹带其它未合并候选。Mac依据实际0553的623文件闭包、161refs、275资源，用原SDK8.0.425 Release及原MSBuild evaluated344项Compile顺序，原位替五源再追加四源。严格独审证明旧TypeDef序保持、275资源/35程序集引用及范围外10371方法记录保持；原Debug或错误类型顺序候选拒绝且未安装。Win恢复原SDK8以避免新编译器改写旧Dispose等方法，独审确认38资源/33refs及范围外IL保持。完整raw差异和旧阻断证据未覆盖改写。

## 备份、实际写入与独立读回

- Mac正常包 `/Applications/ohmymods/arm64/`；备份 `/Applications/ohmymods/backups/gems-quick-20261027T151519Z`，覆盖原native Release、config/ModSave、旧DLL/metadata，另保原README/launcher，共22保护文件。r3实际 **APPLIED 2026-10-27T15:51:00Z**。只替DLL、package-manifest、SHA256SUMS；后两者准确登记overlay，原README/launcher字节保持，仅纠正其既存manifest登记漂移（checksum原本已是当前值）。启动器仅做bash语法检查，未执行。
- Windows原独立E盘2.4.3包；备份 `E:\ohmymods-backups\gems-quick-20261027T151519Z-140367fe`，26文件/17,096,027 bytes。实际 **APPLIED 2026-10-27T15:42:29Z**。只替一个DLL，原存档/配置/ModSave保持，未改系统账户、ACL或桌面。
- 两端独立读回均确认备份匹配、只有预期文件改变；Mac全部包payload现与manifest一致，22保护文件中仅上述3目标改变；Win26文件中仅DLL改变。Apply前后核进程与实际游戏指纹，无并行占用；不自动恢复旧存档覆盖玩家后续进度。
- Mac第一次Apply在checksum构造断言处停止、22正式文件全未变；旧journal `replaced=[]`原样保留，修正为两条精确旧/现checksum双态及CheckOnly预演后，另开r3一次性journal并复审成功。最终脚本SHA `b719bcc8ff323dd35810f6dea708e2c5fb4938ceb53dc2a0b65f4f6655389a01`；Win脚本SHA `537e99dba21ce90a43794ee48522d21db65cd7ffa7587b910165b0248e089ab5`。不将机械失败冒称首轮成功。

## 玩家入口及尚未验证

Mac仍用 `/Applications/ohmymods/arm64/launcher.command`；Windows仍用原 `ohmymods-win243-preview.cmd`。沿用现有功能开关，未强改配置。本次没有启动游戏、没有Native/联机/Android设备验收；宝箱实际一次性投放与保存冷读、火枪出货/续买、7+金币项目手感由后续游玩继续验证，发现新问题另开Issue/PR。

原始证据仅本机 `gems-quick-install-20261027/`，含联合/产物独审、全部失败与最终构建、模型事件/脚本pins、双端备份、Apply及独立postflight。玩家存档、游戏二进制、私人机器信息未上传。

Provenance: Mac Max / Codex · Maintainer Operator · acting_for=ohmymods Mac Operator.
