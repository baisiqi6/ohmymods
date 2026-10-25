# #286 购买绑定候选验证回执

基线 `release/v9.5.13@98259aff338bb9e5ee1b666e4f5c67da24eed6fe`；本轮只交付首次生成身份修复候选，Refs #252 / #286。未合并、未部署或启动游戏；未修改正常存档/配置、Steam、Mono、Android pin 或构建入口。

## Operator 独立复跑

| 主机套件 | 结果 |
| --- | --- |
| --generation-slot | GENERATION-SLOT regressions: 21 checks, 0 failures |
| --generation-delta | GENERATION-DELTA regressions: 41 checks, 0 failures |
| --enrollment | ENROLLMENT regressions: 105 checks, 0 failures |
| --r2 | R2 production regressions: 38 checks, 0 failures; R3 prepared-campaign and typed-activation production regressions: 27 checks, 0 failures |
| --snapshot-compat | PASS paid legacy source→production restore→Stage/Prepare→cold load: 30 checks (transport/native objects are controlled test seams) |
| default | PASS heavy-shield production native-boundary synthetic: 136 checks; PASS heavy-shield real identity/persistence durability compatibility: 228 checks, 0 failures |

上述命令实际执行为 `dotnet build tests/heavy-shield-identity/HeavyShieldIdentityTests.csproj`，随后运行同一产物的上述 modes；默认套件同时执行 durability，合计626个检查通过。它们链接实际产品 Persistence/Identity/Enrollment，Native 对象/传输使用受控桩，不等于游戏验收。

## NoDeploy 构建与产物

Mac240、Win240、Win243 三个独立 net6 wrapper 均 0 warnings / 0 errors；冻结343个生产 C#、5个 props、275个资源，实际 PE 资源回读全部哈希相同，构建后确认源码/资源未漂移。`NoDeploy=true`、`BepInExPluginsPath` 为空；private QA version 为 `10.9.39-qa.286`，不是正式发布或已安装版本。Win243 仅叠加 PR #250 的五文件 typed-save aliases 后验证编译，未混入本 PR。

| profile | DLL SHA256 |
| --- | --- |
| mac240 | `dc5a6c509be17bd5a54683e96f6f3a875ed3fe67768d394d6c5e73449dc5894a` |
| win240 | `fe0884a8db1323c32ac545243e77d38f586a9656ccd2e48a8ca35b5a0a7058da` |
| win243 | `113cea122f482971805ddc0b640da8fd1dd2c1749acc22fe2736926107fcbb27` |

Android probe 重建：0 errors、1条既有 `HeavyShieldBanner._nextActivation` CS0169 warning，SHA256 `9720cc2e4c496ecd721d18b1e0c82174a2434b24f68dd6f710ae06c1106cf6e7`；AdapterTests **1265 passed / 0 failed**。本次两个共享源不在 `android/tests/Program.cs` 的 FrozenSources pin 列表，无重钉；未做 APK/AVD/设备玩法或联机验收。

## 根因及定向反例

基线真实 source-linked raw-slot suite：21 checks / 8 failures → 候选21/0。新增审查反例分批复跑：30/14 → 30/0、39/5 → 39/0、armed lease39/1 → 39/0、最终两项顺序/anchor41/2 → 41/0；保留原始日志，不把编译失败或测试桩失效当产品红。

生成边界以 exact 原生 island pointer 的唯一 campaign slot 识别当前新岛，允许 prefix raw0 在 End 仍为0或规范到本slot；普通 Load/Save 和非当前 visited 行仍严格。前置纯读全 scope 门，detached session 及 origin anchor 避免失败前重置旧 Quota，paid/unpaid 使用同一身份门。已有 R2 付费工具及士兵 A→新B→A 回归通过，旧 paid 行/耐久保留；已有 target 行及无 target 行两种 armed lease 均保留。首次保存/Prepare/一次冷 Load 的字段规范化由桩明确模拟，raw0-still 以及捕获 marker 后字段漂移均拒绝。

## 审查与边界

方案由本机 OMP `zhipu-coding-plan/glm-5.3` / max 集中审查；Worker 为 OMP `deepseek/deepseek-flash` / max，模型均以公开原生 model event 与末次 assistant metadata 核验，无 fallback。独立内置 Reviewer 对增量修正验收通过，Root 实际复跑上述构建/主机检查；精确 provider transcripts/model evidence 留本机任务证据，不将私人思考、原始玩家存档或身份收据提交仓库。

Native raw-land→slot 的实际 writer/时序仍未知；本候选不写原生 land、不放宽普通 Save/Load、不自动清 `_economyFault`、不绕 Prepared、不补未知 paid claim。当前 Mac 正常档只读核对为 campaign0 / land11，盾卫登记仅0/4/8：这个已访问岛的缺行不由本候选自动回填，须另以隔离原始副本查证，不能称现存旧档已修。Windows 当前保存的已有付费声明，其 NativeId 在同份已保存岛对象 uniqueID 中均未找到；现行恢复没有旧ID重映射，不能把 PaidTool 声明当作实际货架仍有工具或擅自补发。这是另一旧账恢复边界，尚未证实现场先命中哪个门。槽位具体 renderer 和本次故障平台尚未确认，渲染尚未改；不能从 order+1 推导一定前景。

完整本机证据位于 `.local/tasks/heavy-shield-shop-20261024/`：`root-identity-tests.json`、`build-final/manifest.json`、`resource-readback.json`、`android-probe-build.log`、`android-adaptertests.log`、`model-evidence.json`、各原始 red/green 与审查。
