# APPROVE_ZERO_EXTRA_HOOK_CONTROL_SOURCE_ONLY

- case: `controlled-candidate133-zero-extra-hook-control-isolated-20261023-single1`
- objective: `zero-extra-hook-load-control`
- verdict: `approved_exact_zero_extra_hook_control_source_only`
- sourceSHA256: `c21d740f21d92a7d96fe4cd359e30c2b289ca52fc9a76e987096fdf2467ee99c`
- mustFix: `[]`
- unverified: generated packet/reviewed-source/source-review.json, managed plan/phase/lease/registration, runtime gate/window, actual preflight/VM startup, c1e43+609e Native load, product InitPools return, gameplay/save/payment/phone/multiplayer/long-running behavior.

R4 增量复审：HEAD `2249dbfb2dfaa1af85364526687b2d7b313d0997`；请求 SHA256 `5bbe500ec2a53b993d555b6aabe2a5a48dad9245da28383bf1e4734242811c51`。controller/test/diff SHA256 分别为 `c21d740f21d92a7d96fe4cd359e30c2b289ca52fc9a76e987096fdf2467ee99c` / `b96f874c250db10d669944d61214f48e983ec1c6c9b25beabf2181f2f5f31b60` / `474931502d4647a3a1ae6539a79673a2fd689dd3b421b62f2385539f0ba08d4d`（24532B），均独立匹配。审前、审后 DSH worktree clean，HEAD 一致。

上一轮 R3 报告 SHA256 `a870f87709ccf663b7f00e2784a8efa4cb454b42755222c11c46c38c41743563` 已在本 HEAD 的 `docs/project-harness/tasks/issue-232/review-result-source-zero-extra-hook-control-20261023-codex.md` 原样保留并核对。本次仅更新现行 R4 结论；R1/R2/R3 与未变化的有效证据继续复用。

## R4 通过

问题来自派生入口继承的正常包旧 pin，而不是现有保护机制错误。builder 拒绝日志 SHA256 `4daebf0d0817c2754c6c876628dec1e17ae685324b39de1014fd1bff71c4152e` 与请求匹配，实际报 `host protection pin mismatch: normalDLL`。本次仅在派生入口冻结 `NORMAL_DLL_SHA256` 与完整 `HOST_PROTECTION` 集合，正常包路径不变、checklist pin 不变；uppercase 导出/回绑使 `_L/_F` 和实际 plan/验证调用方使用新值。历史 base/helper 字节不变，原 `verify_plan_files` 精确读 hash 和运行前后保护比较均保持，没有正常包写入、自动刷新、重试或放宽检查。

新 normalDLL pin `25d9ffef5ad20436bf49c8f7f7c7fdb395e16a86f2bae63900ee6df0a8f4c156` 有本 case 之前的完整证据：`boat-upstream-20261021/implementation-native-definitions-20261021/root-runtime-preflight/receipt.json`（SHA256 `e7506edf101308d5a2a5d10731781ba78c980d2606c7f8cc220ab63e28e63614`，UTC `2026-10-21T13:02:24.200503+00:00`）的 `freshProtected8` 记录同一路径、4280320B 与同 SHA。handoff snapshot 的 priorFullSHA 是对该完整基线的引用，不能把 snapshot 自身视为重新 hash。本轮根 Reviewer 另实际只读 hash 正常包与本机 checklist，分别与 `25d9...` / `efb52c89...` 精确匹配，确认当前保护的是已有正常包字节。

## 验证

把 controller 新 HOST_PROTECTION 块在内存中还原为上一轮继承语句，精确重建 R3 controller SHA `144006ab...e5ed7e5`；去掉测试新增的保护等值/文件 hash 断言，精确重建 R3 test SHA `0db1e4c...42479`。其余旧请求中排除 controller/test 后的 26 条 hash（含重复）均匹配，未重审未变化实现。

根 Reviewer 实际执行 host tests：11/11 通过、exit0。新增断言确认生效保护集合与两文件当前 hash；既有资源 device 漂移拒绝仍通过。另从已冻结 builder 的 AST 中仅提取 plan 字典表达式，在内存构造 plan，实际调用现有 `validate_plan_semantics` 和 `verify_plan_files` 均通过，覆盖全部候选/cfg/addon/fixture/runtimeRefs/hostProtection/hostDisks 的原有只读 pin 校验。只改变内存 plan 的 normalDLL 预期 SHA 为错误值，原 `verify_plan_files` 精确拒绝并报 `host protection pin mismatch: normalDLL`。未改变实际受保护文件，也未导入/执行 builder。

本轮没有产品构建、packet 生成、register/root-launch/root-postflight 执行、SSH mutation、ADB 命令、进程启动/重启、AVD/游戏/Native 操作。只读 plan 校验不是受管登记或运行许可。

## 批准边界

仅批准上述精确源码的 source/host 阶段，可按既定方案重新生成并冻结 packet/reviewed-source 草案，进入下一阶段集中审查。既有 R3 资源身份仍是本 case 冻结输入；未来漂移继续拒绝，不自动刷新。实际受管 plan/phase/lease/登记、有效资源窗口和单次 gate 仍按既定阶段落实。本报告不批准 Native、产品构建、合并/安装或玩法验收。

R2 结论边界保持：采集 exit0/evidenceReportable 可与完整失败并存；限定正结论须同一可信 fresh segment 的正确候选归属、609e INIT/controlValid、真实 READY 后正常 STOP、无同船 NRE，联合产品 InitPools returned 日志。UNKNOWN/缺失/截断/归属不明保持 unknown/invalid。

Reviewer：`mac-ohmymods-codex-desktop`，用户指定的原 Codex 增量 Reviewer；根 Reviewer 核对当前差异、哈希、实际 host tests、只读正负校验与历史完整来源，另有一个内置只读 subagent 交叉核对来源与生效调用链。历史本人参与实现过的工具不重新标为全新独立实现证据。
