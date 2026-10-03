# Issue 112 验证记录：Mac ARM64 启动器终端精简输出（Round3 最终候选）

分支 `codex/terminal-console-20261003`，基线 `14e6f64`（release/v9.5.13）。Worker 交付
验证；**未启动游戏**、未安装、未 commit/push/发布。本文件只记录合成夹具、最小 FIFO
复现与聚合统计，不含任何用户日志文本/地址/ID。

## 变更面（最终）

- `compat/macos-arm64/package/launcher.command`（SHA256 `9d2e6d8c…`，0755）——
  精简终端、`--verbose`、包内原始日志管线（PID 自持与有界收尾，含 tee 独立限时）。
- `compat/macos-arm64/package/tools/console-filter.awk`（`8296fbba…`，0644）——
  逐行确证白名单过滤器（launcher 与测试共用，input-lock 固化）。
- `compat/macos-arm64/package/README.md`（`d2e1026d…`）——与实际已证实范围同步。
- `compat/macos-arm64/package/input-lock.json`（`b4a06fb6…`）——上列文件哈希/大小更新。
- `compat/macos-arm64/package/tests/{run_tests.sh,make_fixture.py}`——A/B/C/D 全部新负例
  与回归（见下）。

未改：cfg 行为、DLL/游戏文件/原生库/存档；无新增运行时依赖或新 helper。

## 最终设计与审查项落实（Round2 P2 修正）

- **A：tee 有界回收**。过滤端的 DONE 标记只代表过滤端结束，不代表 `tee` 已 EOF；
  `tee` 在所有分支都先接受独立 tick 检查（`LOG_TEE_TICKS`），超限即明确告警并
  `TERM→有界等待→KILL→wait`，任何情况下都不无期限 `wait` 存活的 `tee`。真实游戏
  进程（`CHILD=$!`）与退出码语义保持不变。
- **B：MEMORY MAP 区间行全字段校验**。要求完整 9 字段且逐字段合法：地址对、
  两个 `perm(hex)`、`copy|share`、`N`、`N|Y`、hex 偏移、`default`、整数；
  字段不足、额外尾部、未知值一律保留（实测真实样本 68 行全部 9 字段）。
- **C：FarmCat 完整数值负载**。仅 `native activity state unproven cat=<int> pc=<int>
  x=<dec> bodyVx=<dec>$` 被收起；未知事件名/未知负载保留（真实样本另有 3 种事件名
  继续可见）。
- **D：英雄帧仅完整正常语法**。`[HeroArcherNative]`/`[HeroArcherPoseVisit]` 需
  键序完整、数值有限、`native=1`/`hero=1`/`reason=None`、动作配对为实测组合
  （Prepare→Shoot / Prepare→Stand / Shoot→Stand）；`t=unexpected`、`reason` 非
  None、额外尾部字段、未确证配对等异常帧全部保留（可能是真实视觉故障，不当作噪声；
  未以任何方式改游戏日志级别）。
- （Round1/2 已冻结）FIFO 双进程管线 + `<&3` 显式 stdin、启动器收尾按记录 PID 逐一
  有界 kill/wait、logger trap 先收其 awk/fallback cat、fallback cat 实时透传并由
  启动器在结束后从完整 raw 回放补预读缺口、信号清理同样报告并回放。

## 验证（本机 `/bin/bash` 3.2.57 + 系统 awk）

| 命令/场景 | 结果 |
|---|---|
| `bash tests/run_tests.sh`（完整包套件，合成夹具） | **PASS=503 FAIL=0**（exit 0；日志见私有 `worker-evidence/full-suite-final3.log`） |
| 最小 FIFO repro（Bash 3.2） | `awk <&3`：过滤成功 rc=0；`cat <&3`：读到流；无显式 stdin 反例 seen=0 |
| 真实样本只读复算（脱敏统计） | 9278 行 → 保留 418；W/E/F 9/9；ClockDiag 13/13；HeroNative/PoseVisit 正常帧 120/60 全收起；FarmCat 45 条数值负载收起、其余 3 条事件保留；区间行 68/68 收起 |

新增/加强回归：

- **A**：DONE+LOGGER 0（过滤端读完首行即提前退出）+上游孤儿写端 → `tee` 有界告警、
  `TERM/KILL/wait`、返回有界、raw 无后写；DONE+LOGGER 143（过滤端被强杀）+上游孤儿
  写端 → 同样的 tee 收尾 + 记录 PID（tee/filter/cat）全部回收。
- **B/C/D 负例**：8 字段/带额外尾部的区间行；未知 LC；未闭合/闭括号未知
  `MEMORY MAP(...)`；`cat=unexpected runtime mismatch`/`bodyVx=unexpected`；
  `t=unexpected`、`reason=Other`、合法前缀+额外尾部、`action=Unknown`、未确证配对
  ——全部断言保留；对应正常帧断言收起（计数断言避免子串互相包含）。
- 既有（Round1/2）TERM/HUP/超时/tee 写盘失败/过滤器故障实时透传/stderr 合并/
  ≥200KB 大输出/退出码保留/check-only 零写入/四类日志目标拒绝/helper fail-closed/
  ZIP 哈希与权限继续全绿。

## 剩余边界（如实保留）

- **未实机**：未启动游戏；真实终端观感、真实日志并存留待用户下一次手动启动验收。
- `tee` 自身写盘失败（磁盘满等）时 raw 可能不完整，但会显式告警并以独立非零结束；
  不宣称对极端灾难路径提供 SIGPIPE 绝对免疫。
- 排空有界（正常约 10s、信号清理约 3s、tee 独立约 3s），窗口内尾部输出可能缺失并
  告警；故障回放允许重复行。
- 外部信号直接 SIGKILL 日志进程属不可捕获场景（兜底仅覆盖可确证属于本管线的
  awk/孤儿 cat，避免误杀无关进程）。
- 启动器层修复，无 DLL/玩法/版本变更；安装/发布/PR 归 Operator。
