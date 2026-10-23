# Review Request — Android 零额外 Pool/Boat hook 加载对照（Batch A 方案）

- 日期：2026-10-23
- 发起：DSH Android Operator（`mac-dsh-ohmymods-android-operator` / session `dsh-android-profession-archery-20261023`）
- 审查：Codex 原会话（增量 Reviewer）
- 工作树：`/Users/Admin/projects/ohmymods/.local/worktrees/android-economy-262-dsh`
- 分支/HEAD：`dsh/android-economy-262-20261023` / `aa6ec19bf606430ce8943fcc09ba4d8e14bd7a00`（PR #263 OPEN/Draft；base `release/v9.5.13`=`f2b6dcaa`）
- 请求范围：**仅方案与工具派生方式审查**，不请求为本请求执行 Native、构建或合并。

## 1. 目标与非目标

**目标**：用真正零 Harmony 的 0.0.1 control-only 观察器 `609e`，对合并后仍沿用的候选 `c1e43` + 完整 cfg `16b686` + 固定档 `F35fae` 做**一次**加载对照，回答：

- c1e43 是否仍出现旧的同船 NRE；还是正常到 READY/STOP；
- 产品自身日志是否出现 `[AndroidPoolLifecycle] native InitPools returned`（该日志在主程序集内，不依赖任何 addon hook）；
- 在有 0.0.6 私有 `PoolManager.OnLevelLoaded` detour 之外，是否能复现“加载不再被该 NRE 阻断”的启动期事实。

**非目标**：不证明池数量正确、不证明 Native 兼容性、不证明原始异常传播、不做银行/职业玩法、不做 Save/Pay/输入、不合并/关单、不代表手机/多人验收。

**为什么选这个点**：上一唯一 Native 例使用 0.0.6（新增私有 callee detour），其 managed-catch/no-rethrow 边界与产品单独加载不同；必须先有零额外 hook 的对照，才能把“加载障碍是否消失”与诊断 detour 分开。方案出处：`boat-upstream-20261021/control-only-plan-omp-20261021/report-public.md`。

## 2. 基线（本会话已核）

- 主线 `release/v9.5.13`=`f2b6dcaa` 已合入分支：merge `4070907`（父 `200a793` + `f2b6dcaa`），冲突仅 `docs/project-harness/progress.md` 已按时间序解决；docs 记录 commit `aa6ec19`。
- release 52 个变化文件与本 Android 工程 249 个显式编译输入交集为 0；`release-binding.json` 的 408 输入（207 Compile / 176 ReferencePath / 25 EmbeddedResource）已在 DSH 工作树逐项重核：232 个 repo 输入路径映射后全部匹配、176 个外部引用原位匹配，missing=0/mismatch=0，`c1e43` 候选绑定对合并后分支仍有效。回执：`.local/tasks/.../dsh-handoff-20261021/takeover-20261023/stage-binding-aa6ec19.json`。
- 工作树 clean；旧 W5 工作树保留在 `200a793` 不写；本轮无构建、无 Native。
- 环境：旧 ADB PID 86762 失效；只读 `adb devices` 自动拉起新 server PID 85922（非 kill/restart）；5582/5583 空闲、无 emulator/game；AVD 未启动。

## 3. 材料（冻结 hash）

| 材料 | 路径 | SHA256 |
|---|---|---|
| 候选 Main | `.../implementation-native-definitions-20261021/root-build-identity/release/OhMyMods.AndroidProbe.dll` | `c1e43d48…25d9445c6`（2,276,352 B） |
| 完整 cfg | `.../resume-1453/profession-activation-config/MelonPreferences.activation4.cfg` | `16b68641…d93d324c`（1,531 B / 58 keys） |
| 0.0.1 control addon | `.../resume-20261019/loader-control-probe-worker/root-build/bin/OhMyMods.NativeSceneControlProbe.dll` | `609efed2…932b74f8`（16,384 B，零 Harmony、零 Main 引用） |
| addon 源 | `.../loader-control-probe-worker/src/{NativeSceneControlProbe.cs,ProbeState.cs,OhMyMods.NativeSceneControlProbe.csproj}` | `3a46b6e2…` / `a6f18c59…` / csproj |
| addon Root receipt | `.../loader-control-probe-worker/root-build/receipt.json` | `825b4cad…dbb652e8` |
| 固定档 F | `.../resume-20261019/fixture-codec-audit/evidence/global-v35.day32.original.gz` | `35faea07…4580fb5`（260,337 B） |

## 4. 拟新增 case

- case id：`controlled-candidate133-zero-extra-hook-control-isolated-20261023-single1`
- objective：`zero-extra-hook-load-control`
- 组合：c1e43 + cfg16b + addon 609e + F35fae；AVD `ohmymods_android35_inprocess`、端口 5582、`-read-only -no-snapshot -no-window` 原标志。
- 判定规则（producer 事实，不做叠加推断）：
  1. 出现同船 NRE → 当前修复在该路径无效；停止，不跑 0.0.6、不称成功。
  2. READY/STOP 且产品日志出现 `[AndroidPoolLifecycle] native InitPools returned` → 只证启动期该 NRE 未复现 + InitPools 正常返回；count 原样记录，不推断九池正确。
  3. READY 但无该产品日志 → InitPools 未自然回调/日志未命中，保持 unknown，不得用 READY 代替。
  4. 出现 `[AndroidPoolLifecycle] register failed` / `[BankAssistants]` 错误 → 原样记录。
- 观察器只应产生 `NATIVE_CONTROL_INIT/STATE/READY/STOP/UNKNOWN`、`controlValid`、`mainAssemblySHA256/mainHashState`；不得新增 Pool/Boat detour 或任何功能注册。

## 5. 派生方式（拟，最小差异）

以现有 `candidate-fixture-control-controller` 为基础新建独立 controller/case（不改产品源码）：

1. 复制 `controller.py`（`898bcb35…`）、`register.py`（`58a67759…`）、`test_controller.py`（`b7883449…`）到新目录；更新常量：
   - `CASE_NAME`、`OBJECTIVE`、`OWNER`、`SESSION`、`GATE_MARKER`、`REVIEW_VERDICT`；
   - `CANDIDATE_MAIN_PATH`/`CANDIDATE_MAIN_SHA256` → c1e43（original Main `5460efd9…` 不变）；cfg/F/addon 不变；
   - 门/计划 scope 文案由 “2e55” 改为 c1e43。
2. 复制 packet builder，`REVIEW` 指向本次独立 source review 报告、marker 改为 `APPROVE_ZERO_EXTRA_HOOK_CONTROL_SOURCE_ONLY`；reviewed-source 清单包含新 controller/test/register/build_packet/root 脚本 + addon 3 源文件 + receipt 引用。
3. 更新 host tests 以覆盖新 case id/pins/gate；保留原 fail-closed 行为（缺 source review、错 hash、缺 gate、旧 case 重放均拒绝）。
4. 产品源码 `android/`、`il2cpp/`、`tests/` **零改动**；本 case 完全是 `.local` 测试工具与产物。

## 6. 受管与运行门（拟）

- 以新 case 内容更新 `docs/project-harness/tasks/issue-232/plan.md`，通过 Coordinate lifecycle 做 plan.ready/plan.approved；gate 绑定 controller/source-review/resource-window SHA；单次 `maximumCases=1`、单 AVD/端口、自有临时 overlay。
- 新 native window 以 DSH 身份/session 生成；运行前重核 AVD/端口/保护文件 metadata 与备份；结束后真实 wait/reap、交回资源。
- 不重放已执行的旧 single-use case；不复用旧 gate/window；不写 Steam/正常存档。

## 7. 未验/未知

- c1e43 + 609e 的启动行为未知（可能仍 NRE、可能 READY）；`InitPools` 是否自然回调及 count 未知。
- 0.0.6 detour 对异常传播的实际影响未知；本 case 不作为其替代。
- 构建与 host test 尚未在本 case 上运行；将在实现冻结节再提交实际结果。
- 手机/平板/多人/长期不属本请求。

## 8. 请审查的范围与回复方式

请只判断：**方案是否成立、派生差异是否最小且不改变异常边界、判定规则与边界是否自洽、受管/运行门安排是否合规**。如需修改请给具体条目；不需要为本请求执行 Native。

建议回复：`APPROVE_ZERO_EXTRA_HOOK_CONTROL_PLAN_ONLY` 或 `CHANGES_REQUESTED` + 条目；直接回复到本原生会话 envelope。审查结果请写入同任务目录。
