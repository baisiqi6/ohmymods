# APPROVE_ZERO_EXTRA_HOOK_CONTROL_SOURCE_ONLY

- case: `controlled-candidate133-zero-extra-hook-control-isolated-20261023-single1`
- objective: `zero-extra-hook-load-control`
- verdict: `approved_exact_zero_extra_hook_control_source_only`
- sourceSHA256: `144006ab030540a04db990c859097381fe42004db7cee0b1488f6d941e5ed7e5`
- mustFix: `[]`
- unverified: packet/reviewed-source/source-review.json, managed plan/lease/phase/registration, runtime gate/window, actual preflight/VM startup, c1e43+609e Native load, product InitPools return, gameplay/save/payment/phone/multiplayer/long-running behavior.

本次为 R3 的增量复审。HEAD `717716d28a262c3244802389c367b49060ee802b`；请求 SHA256 `09cb7063931c80d6e08a9f7cf556b6c6bb8eac547e736b1d625ffb23f17f2bc2`；controller/test/diff SHA256 分别为 `144006ab030540a04db990c859097381fe42004db7cee0b1488f6d941e5ed7e5` / `0db1e4c526b14087f41b326296a9d00624156a26cb98aec0473e59da84742479` / `5ae81e6ff0089d8a5ef3c36aac24826a815cf96d0a14f7f32420e37caea06d56`（diff 23520B），均独立核对匹配。审前、审后 DSH worktree clean、HEAD 一致。

上一轮 HEAD7ee48fd 的 CHANGES_REQUESTED 报告 SHA256 `1ac21757d0811b8eefa29f6de9b4523c9c06bd39b0deb3e478105b94899389ba` 已原样保存在本 HEAD 的 `docs/project-harness/tasks/issue-232/review-result-source-r3-zero-extra-hook-control-20261023-codex.md`，本轮核验其字节未变。该历史结论不删除；本次因 R3 已修正更新现行报告。R1/R2 与其余已接受内容、未变化的有效证据继续复用。

## R3 已解决

派生 controller:93–106 新增本 case 的冻结 `RESOURCE_BINDING`，在继承导出前定义；1022–1031 保留现有 uppercase 回绑机制，明确将本 case 值设置到 `_L` / `_F`。实际 `_F._read_resource_binding`、`_check_own_resources`、`_check_adb` 及 root-postflight 的导入路径均使用该绑定。未修改基础 helper 的原文件/hash，未复制库，未改变 Pool/Boat hook 或异常边界，未新增自动重绑/扫描/重试，未放宽原 exact-identity / drift-refusal 检查。

新绑定 9 路径 `device=16777234`、各 inode/config SHA 和 ADB85922/UID502/启动时间/args 与本轮只读现场相符。新增 host test 调用真实 `_read_resource_binding()` 验证文件身份，再仅改变绑定 device 证明拒绝，finally 恢复原对象。测试只解析 ADB 描述，未将其误写成实时 listener 验证；根 Reviewer 另外只读 `lsof -nP -iTCP:5037 -sTCP:LISTEN` 与 `ps -p 85922 -o pid=,uid=,lstart=,args=` 确认唯一监听及 UID502 / Fri Oct23 09:12:11 2026 / 精确 adb args。未运行 ADB 命令或启动/重启任何进程。

这是本 case 当前身份的绑定修复，不延长进程身份有效性；后续资源/ADB 身份漂移仍按现有机制拒绝。不得用本报告授权自动刷新、放宽检查或复用失效运行窗口。

## 本轮验证

将新增 RESOURCE_BINDING 块从当前 controller 在内存中剔除后，重建字节 SHA 精确回到上一轮 `8495518a...b0abce81`；同样去掉 test 的新增 import/method，精确回到 `c94ca664...efb284c`，证明无其它实现差异。旧请求中排除变更 controller/test 后的 26 个 hash 条目（含重复）全部匹配，覆盖其余5派生文件及原有基线/helpers/候选/cfg/F/probe来源。复用上一轮对 DLL/receipt5引用与冻结依赖的有效核对。

根 Reviewer 实际执行 `python3 -B .../zero-extra-hook-control-controller/test_controller.py -v`：11/11 通过、exit0，含本轮新增测试。未重新执行 builder、register、root-launch 或 root-postflight，没有构建、SSH mutation、登记、gate 消费、AVD/游戏/Native 操作。原 builder 的 source report fail-closed 条件未改，本轮不以生成器执行作为证据。

采集完成与加载成功继续分离：`evidenceReportable`/exit0 可以与完整采到的同船 NRE 并存。限定正结论仍需同一可信 fresh segment 中的候选归属 + 609e INIT/controlValid + 真实 READY 后正常 STOP + 无同船 NRE，再联合产品 InitPools returned 日志；缺失/UNKNOWN/截断/归属不明保持 unknown/invalid。host tests 不能代替这些运行证据。

## 批准边界

仅批准上述精确源码的 source/host 阶段，可按既定方案生成并冻结 packet/reviewed-source 草案，提交下一阶段集中审查。本报告不是实际运行许可，不替代受管 plan/有效 phase与lease/登记回读、资源窗口或单次 gate，也不批准 Native、产品构建、合并/安装或游戏验收。后续审查复用未变化证据，只核对真实增量及有效运行边界。

Reviewer：`mac-ohmymods-codex-desktop`，用户指定的原 Codex 增量 Reviewer；根 Reviewer 核对当前差异、哈希、实际 host tests 与只读现场，另有一个内置只读 subagent 交叉核对 R3 生效路径和测试。未将历史本人参与实现过的工具重新标为全新独立实现证据。
