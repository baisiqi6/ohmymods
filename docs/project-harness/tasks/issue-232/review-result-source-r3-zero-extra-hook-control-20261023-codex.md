# CHANGES_REQUESTED

- case: `controlled-candidate133-zero-extra-hook-control-isolated-20261023-single1`
- objective: `zero-extra-hook-load-control`
- verdict: `changes_requested`
- sourceSHA256: `8495518a1ab14c86b133ec3cfea5576361c3c746f98e72e704d71ea7b0abce81` (new controller)
- mustFix: `[R3: inherited RESOURCE_BINDING is stale and has no effective refresh/override]`
- unverified: packet/source-review/gate/window/registration, runtime preflight execution, candidate+609e Native load, product InitPools return, gameplay/save/payment/phone/multiplayer/long-running behavior.

审查对象：DSH 工作树 HEAD `7ee48fd06ecaa76336b4a86ee116455efbaf368b`；请求 SHA256 `44b6d252887e7d52cab4311128f85c60dcc15fbc23528f6ba7b8b3366a86d2f1`；冻结 diff SHA256 `e1ae2460a528bbe9ffacd9460a3b9a9b5d68eeb97f356ac553b37047a8676e25`。本结论只覆盖这次 source/host 审查；没有 SOURCE_ONLY 批准，不授予构建、登记、gate、Native 或合并许可。

## R3 / P2：本 case 仍继承失效的资源身份，当前字节会在 VM 启动前拒绝

产生处是历史基础 controller 的 `RESOURCE_BINDING`，并非现有 preflight 守卫有误：

- `.../g19-s1-runtime-preparation-20261018/profession-fixture-load-controller/controller.py:107` 固定 AVD 目录、ini/config、6 个 image 的 `device=16777231`，以及 ADB PID `88434` / `Wed Oct 14 08:01:26 2026` 身份。
- 本次 `zero-extra-hook-control-controller/controller.py:1008–1017` 把未覆盖的基础常量导出，再回绑到 `_L` / `_F`。七个派生文件均没有本 case 的新 `RESOURCE_BINDING` 或已有有效注入入口；当前 packet builder 的 plan/window、root-launch 参数也不提供此绑定。
- 新 controller 第 705 行（及 745 行）调用 `_F.check_preflight`；基础 `_check_own_resources` 第 1217–1220 行先 `_read_resource_binding`，第 946–949 行核对 device/inode，随后 `_check_adb:1080–1086` 精确核对 listener PID/uid/start/args。root-postflight:41 同样使用此入口。

本轮仅用 `stat` / `lsof` / `ps` 只读核对现场：2026-10-23T17:17:56Z 附近，AVD 目录实际 `st_dev=16777234`（inode `57994989` 与旧记录一致）；目录、ini/config 及 6 images 的 device 均不匹配旧值。5037 实际唯一监听为 ADB PID `85922`、UID `502`、`Fri Oct 23 09:12:11 2026`，旧 PID `88434` 已不存在。故即使其余 packet 正确，当前源码也会在 preflight 的资源身份检查拒绝，无法完成本 case。此结论来自实际绑定/调用链与现场差异；没有运行 controller 或声称捕获过 Native 失败。

修正建议：复用现有资源身份核验机制，在本次派生入口冻结这次 case 的真实资源绑定，使它通过已有常量回绑到实际调用方；核对 realpath/stat/config 与现有 5037 进程身份，并将改变的字节纳入本批冻结/hash。保留历史 base/helper 原文件及 hash，保持现有精确核验和漂移即拒绝语义；不要放宽守卫、引入自动重绑/扫描/重试、重启 ADB 去迎合旧记录或复制一套 helper。用 host 范围确认实际 `_F` 生效的是本 case 绑定，漂移仍会拒绝即可。之后只增量复审该绑定及受影响 hash，无需重开已接受方案或重跑未变化的产品构建。

## 已接受内容及本轮验证

R1 的身份/case/报告两组绑定、保留 task operation、实际 reviewer、删除旧工作树 `mirror.write_bytes` 均已落实。root-postflight 现在用同 run 的 2 protection + 6 disks；其 fresh schema 与实际 `host_file_snapshot` 一致，能比较 before/after/fresh，未使用旧窗口保护基线。当前主工作区 checklist hash 仍匹配该只读脚本断言。

R2 的采集语义保持：controller 差异未改变 Pool/Boat 异常边界、未新增 hook/parser/addon/结果账本，`evidenceReportable`/exit0 仍只表明采集完备。限定正结论仍须同一可信 fresh segment 的正确候选归属 + 609e INIT/controlValid + READY 后正常 STOP + 无同船 NRE，并联合产品 InitPools returned 日志；UNKNOWN/缺失/截断/归属不明不得补绿。

独立核对请求、diff、文档所列 30 个 hash 条目（含重复）均匹配；另核对 c1e43 与 609e DLL、probe receipt 的 5 个引用 hash 均匹配。7 个派生 Python 文件 AST 语法检查通过。本轮实际执行 `python3 -B .../zero-extra-hook-control-controller/test_controller.py -v`：10/10，通过，exit0；只用 fake/temp host 数据。未执行 builder/register/root-launch/root-postflight，没有构建、SSH mutation、ADB 命令、模拟器或游戏操作。

10 项测试不覆盖本次真实资源身份、root-postflight 执行或联合产品 InitPools 正判；不能以这些测试或采集 exit0 声称启动成功。builder 无 source review 的 fail-closed 本轮仅从源码确认，未重跑生成器。受管当前 phase/lease、实际窗口与单次 gate 在后续已有阶段核验，本轮未刷新这些运行许可。

Reviewer：`mac-ohmymods-codex-desktop`，用户指定的原 Codex 增量 Reviewer；由根 Reviewer 核对当前源码与现场，并使用一个只读内置 subagent 交叉核对派生差异及 R3 调用链。未将历史本人参与实现的基础工具重新标为全新独立实现证据。
