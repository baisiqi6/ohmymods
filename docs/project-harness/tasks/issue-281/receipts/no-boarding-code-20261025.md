# Issue #281 — 火枪手留守与异常登船降级代码验收

本轮按用户明确选择替代完整职业运输方案：正常付费火枪手不上船、留守来源岛；异常已登船者在来源保存捕获前归还自有战斗／视觉包并结束职业，原生运输到岸为普通弓箭手。弩手仍沿用原生弓兵运输和原有比例机制。不新增运输账本、schema、退款、赠枪或逐帧扫描。

## 根因与边界

.281.2 实机已证首 missing unit 是当前主船严格子级、已登船，active但原生已注销持久化；carry-forward仅存单位／盾牌计数，不能携带付费GUID。新补丁在Archer资格getter、Embarkee目标与登船入口拒绝精确当前职业；旧异常状态于明确来源保存前处理，不在missing-row或writer拒绝分支绕过保护。

转换只接受同state/context/world/root/GoId/life/Archer/Embarkee/owner且仍已登船的见证；清包前后冻结并复核。读取未知、归还失败、换life、foreign或背景保存均不结束职业。战斗字段复用既有CAS，视觉需证明own隐藏、native恢复；严格待退回执在后续Sync仍保持严格结算，只有销毁延后。原有生命周期调用者不改语义。

旧补偿schema3另有已复现的兼容风险：职业退出后，快照轮转可淘汰grant最后真实凭据，第7/8次新hash导致编码拒绝。Record现保护baseline与Complete grant最后witness，保持8项上限；无可淘汰项无副作用拒绝，同hash冲突仍拒绝。没有把已退出职业放回当前快照。unknown-hash历史reservation语义未扩充。

## 独立验证

| 检查 | 结果 |
|---|---|
| 真实Identity/Archive/Persistence，default / diagnostic | 728 / 926断言通过，0W0E |
| 真实SaveCapture / NativeSave payload gate | 258断言通过，0W0E |
| Runtime / Visuals source-linked | 128用例通过，0W0E；Astra独立128/0 |
| Archive旧用例 / 补偿回归 | 21 / 507断言通过，0W0E；补偿独立507/0 |
| legacy native gate | 405断言、18case通过，0W0E |
| Windows 2.4.0 / 2.4.3，default / diagnostic | 四种私有API编译0W0E |
| Mac ARM64 2.4.0 私有包装器 | 最终完整源码编译0W0E；首轮验证工程漏宫廷子目录失败日志保留，未改产品修该错误 |
| Android host probe / AdapterTests | 0 errors；1条未变的HeavyShieldBanner CS0169警告；1265/0，非设备 |

A/B均由Astra先反证后复审通过；B早期118/0和122/0不足以验收，已由未知身份误清账、hidden native、legacy adoption等真实source-linked故障注入推翻并修正。OMP有界任务退出1时未被当作完成，最后机械修正及adoption补缺由Operator完成，再独立复核。C保留原HistoricalExclusion全部11项旧断言，并补Pending不产生保留义务的负控；先红后绿，未缩减旧覆盖。

Operator实现方案、两组增量与验收决策使用OMP `zhipu-coding-plan/glm-5.3`、请求max，并核验公开message_start实际provider/model；实现同模型上午路由（本机ZCode不可用）。私有逐轮日志、原始失败和模型事件保留，未上传玩家日志/身份。

## 冻结候选与复核线索

Windows候选基于实际已装.281.2冻结源码，只替换以下4源及版本信息；没有混入其它主线功能。候选`10.9.39-win243-diag.281.3` DLL SHA256 `63d43ff17ff601d1c0f95f26beeafdde21ddffae4e73419d640119c8060f32ae`；输入清单SHA `ac7b5fa78da078b7e7dcf0044527548ae2e6d668b079db12ee1031afbcd42726`；build log SHA `de275aa959425e0665b2a3992266d3a71379a4497d06794cab4af6e1f3028ff2`。

| 当前源码 | SHA256 |
|---|---|
| `Patch_MusketeerBoardingPolicy.cs` | `0ccd682fa6d29396f12c4b96c587fdb25bc6e32b254240828c110196da0819cd` |
| `MusketeerRuntime.cs` | `7242b69eb692e80c42dcfe62fd66fa4894b37f8349995a39befee8644ef8d490` |
| `MusketeerVisuals.cs` | `706ebe442eaaccc7a7acc0ca6a612b9b658c01d0a526f7e978ce6f1fcec56780` |
| `MusketeerArchive.cs` | `f68cfea3480ae1067072c763f5121c9656028a91a6713973d117302721adf5d1` |

候选Cecil比对8181→8204 methods，范围外方法未变、38项资源逐字节相同；Same/Encode/Decode的差异为新增局部函数导致的编译器匿名方法引用序号变化，其源方法体未改。默认PC产物不应包含诊断类型，Android不接入新Desktop policy。所有构建NoDeploy，未写Steam。

当前静态新hook证据来自Win2.4.3与MacARM64 2.4.0真实方法体和完整映射，非interop DLL方法实现。Win2.4.0本轮为API编译边界。Native保存写入、出航AI选择、登岛、保存退出与新进程重进尚待玩家验证；新增pipeline只证明payload gate和未调用writer时旧盘不动，不能称真实writer I/O失败已验。

私有证据根：`transport-contract/`（主任务`.local/tasks/windows243-mount-save-20261024/manual-20261025T161239Z-pid166112/boat-scope-next/`下）。主要索引：`operator-host/results.json`、`operator-runtime-strict-green/results.json`、`ledger-independent-review.md`、`runtime-astra-review/strict-final-REVIEW.md`、`policy-astra-review/delta-REVIEW.md`、`compile-check/no-boarding-r1/results.json`、`android-check/results.json`、`il-scope-check/verdict.json`。安装与实机另记；PR #282仍Draft，Issue #281开放。
