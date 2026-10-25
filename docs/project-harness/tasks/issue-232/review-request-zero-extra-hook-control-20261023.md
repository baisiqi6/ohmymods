# Review Request — Android 零额外 Pool/Boat hook 加载对照（Batch A 方案，R1/R2 修订）

- 日期：2026-10-23（修订）
- 发起：DSH Android Operator（`mac-dsh-ohmymods-android-operator` / session `dsh/android-economy-262-20261023`）
- 审查：Codex 原会话（增量 Reviewer）
- 工作树：`/Users/Admin/projects/ohmymods/.local/worktrees/android-economy-262-dsh`
- 上一轮实际审查 HEAD：`8d49205da9bcfa653d254dfa75287daa07eb2791`；本修订提交后的新 HEAD/请求 SHA 见回复消息
- 分支：`dsh/android-economy-262-20261023`；PR #263 OPEN/Draft；base `release/v9.5.13`=`f2b6dcaa`
- 请求范围：**仅 R1/R2 方案说明增量复审**；不请求实现、构建、Native、SSH mutation 或合并。

## 0. 上一轮结论与保留范围

Codex 审查结果存于 `docs/project-harness/tasks/issue-232/review-result-zero-extra-hook-control-20261023-codex.md`（SHA256 `6a2b9dadeb585ca80ecc7775f746312bc90d645c3df3cd531343ecf9c6868b03`）：609e 冻结 addon 复用、零额外 Pool/Boat detour 对照方向、候选/cfg/F、单次 case、自有 AVD/临时 overlay 等方向接受；仅 R1/R2 两项须补齐。本修订只补这两项；已接受内容、上一轮请求中未变化的材料/hash 与冻结构建绑定直接复用，不重做整个 controller。

## 1. 目标与非目标（不变）

**目标**：用 0.0.1 zero-Harmony 观察器 `609e`，对 `c1e43` + cfg `16b686` + F `35fae` 做一次加载对照，分别回答：是否仍同船 NRE；是否到 READY/STOP；产品自身 `[AndroidPoolLifecycle] native InitPools returned` 是否出现；为 0.0.6 私有 detour 之外提供零额外 hook 的启动期事实。

**非目标**：不证池数量/注册正确、不证 Native 兼容/原始异常传播、不做银行/职业玩法、不 Save/Pay/输入、不合并/关单、不代表手机/多人验收。

## 2. 基线（不变，已核）

- 主线 `f2b6dcaa` 已合入：merge `4070907`（父 `200a793` + `f2b6dcaa`），冲突仅 `progress.md` 已按时间序解决；docs commit `aa6ec19`。
- release 52 个变化文件与 Android 249 个显式编译输入交集为 0；`release-binding.json` 408 输入（207 Compile / 176 ReferencePath / 25 EmbeddedResource）在 DSH 工作树逐项重核 missing=0/mismatch=0，`c1e43` 对合并后分支仍有效。回执：`.../takeover-20261023/stage-binding-aa6ec19.json`。
- 环境：旧 ADB 86762 失效；只读检查自动拉起新 server PID 85922（非 kill/restart）；5582/5583 空闲、无 emulator/game；AVD 未启动；本轮无构建/Native。

## 3. 材料（hash 不变）

| 材料 | 路径 | SHA256 |
|---|---|---|
| 候选 Main | `.../root-build-identity/release/OhMyMods.AndroidProbe.dll` | `c1e43d48…25d9445c6`（2,276,352 B） |
| 完整 cfg | `.../resume-1453/profession-activation-config/MelonPreferences.activation4.cfg` | `16b68641…d93d324c`（1,531 B / 58 keys） |
| 0.0.1 addon | `.../resume-20261019/loader-control-probe-worker/root-build/bin/OhMyMods.NativeSceneControlProbe.dll` | `609efed2…932b74f8`（16,384 B） |
| addon 源 3 文件 + receipt | `.../loader-control-probe-worker/src/{NativeSceneControlProbe.cs,ProbeState.cs,OhMyMods.NativeSceneControlProbe.csproj}`、`.../root-build/receipt.json` | `3a46b6e2…` / `a6f18c59…` / csproj / `825b4cad…dbb652e8` |
| 固定档 F | `.../fixture-codec-audit/evidence/global-v35.day32.original.gz` | `35faea07…4580fb5`（260,337 B） |

## 4. 拟新增 case（不变）

- case id `controlled-candidate133-zero-extra-hook-control-isolated-20261023-single1`；objective `zero-extra-hook-load-control`；组合 c1e43 + cfg16b + 609e + F35fae；AVD/端口/launchFlags 与原 case 相同。
- 观察器只产生 `NATIVE_CONTROL_INIT/STATE/READY/STOP/UNKNOWN`、`controlValid`、`mainAssemblySHA256/mainHashState`；不得新增 Pool/Boat detour 或功能注册。

## 5. 派生合同（R1）

### 5.1 实际生效绑定及处理

| 绑定位置 | 现有值/行为 | 本 case 处理 |
|---|---|---|
| task identity（canonical） | issue-232；split_operation `a7459a58-3446-4856-aaa3-5f02e1115de2`；owner/session/status/phase 以执行时 canonical 回读为准 | **保留现有合法 task operation identity**，不新建 task/operation；owner=DSH、session=DSH、status=doing、phase 由 lifecycle 现状回读；旧 owner/session 仅历史 |
| `register.py:19–25` | ROOT=`/var/lib/coordinate/workspaces/ohmymods`；PP=issue-232 plan 路径；OWNER/SESSION/OP/OLD/BASE 旧值 | 重新绑定：ROOT 不变；PP 不变；OWNER/SESSION=DSH；OP 保留上述 operation；OLD=当前 canonical plan SHA（执行时回读）；BASE=本修订新 HEAD |
| `register.py:31–32` | CASE 旧 case；`PACKET_CASES` 旧 packet 路径 | 指向新 case 与新 packet 路径；旧 case 不在允许集合 |
| `register.py:139–144` | 前置断言旧 owner/session/operation/status/phase/old plan SHA | 前置改为执行时 canonical 回读：DSH owner/session、operation 保持不变、`status=doing`、phase/lease 与计划状态一致、当前 plan SHA=OLD；不通过改 JSON/放宽 assert 绕过 |
| `register.py:155–177` | plan revise/approve：branch `codex/android-profession-archery`、phase ready、旧 reviewer 名称、旧 idempotency key | branch 保持 coordinator 分配给 issue-232 的 `codex/android-profession-archery`（不迁移到 economy 分支）；reviewer 使用本轮实际 Reviewer 身份 `mac-ohmymods-codex-desktop`（不冒用历史 reviewer/model 名）；idempotency key 以新 plan SHA 派生；payload base_commit=新 HEAD、implementation_plan_sha256=新 plan |
| `root-launch.py:12–18` | 旧 controller 路径、旧 case、objective、旧 review 路径 | 重绑新 controller/case/objective/review 路径；启动命令/参数结构不变 |
| 新 controller 常量 | — | CASE_NAME、OBJECTIVE、OWNER=DSH、SESSION=DSH、GATE_MARKER、REVIEW_VERDICT、`CANDIDATE_MAIN_PATH/SHA256`（c1e43）、gate 文案候选名；addon/cfg/F 常量不变 |
| packet builder | REVIEW 指向旧 source 报告、marker 旧 | 指向本 case 新独立 source review、marker `APPROVE_ZERO_EXTRA_HOOK_CONTROL_SOURCE_ONLY`；reviewed-source 清单覆盖新 controller/test/register/build_packet/root 脚本 + 引用的既有 base/helper + addon 3 源文件与 receipt 引用 |
| **`root-normal-readback.py:42–45`** | 对旧 `/Users/Admin/.codex/worktrees/android-economy-recovery/ohmymods/.../issue-232/plan.md` 执行 `mirror.write_bytes(p)`，不是纯读回 | **删除该旧工作树写入**；只保留 canonical checklist/plan 的只读回读与比对。如后续确需同步，只允许写 DSH 自有目标并如实标为写入、保留受管边界；不得借旁路写入满足登记 |
| 未变 base/helper | `loader-control-controller/controller.py` 等既有基础/helpers | 继续按原绝对路径引用并纳入 hash 闭包/manifest，**不复制一套新库**；传递来源在 reviewed-source 中逐项列出 |

### 5.2 受管登记方式

新 case 的 `issue-232/plan.md` 通过现有 Coordinate lifecycle `plan revise/approve` 更新（server-side CLI/SSH 既有入口），保留 task operation、owner/session/lease 与单次 gate；登记后回读 canonical plan 与 checklist，确认只有 issue-232 计划变化、其它行/计划不变、validator 通过。不裸改 JSON/DB，不用旧批准/旧 reviewer 名绕过。

## 6. 受管与运行门（更新）

- 新 plan 绑定 controller SHA、source-review SHA、resource-window SHA；`maximumCases=1`、单 AVD/端口、自有临时 overlay、真实 wait/reap。
- 新 native window 以 DSH 身份/session 生成；运行前重核 AVD/端口/保护文件 metadata 与备份；结束交回资源。
- 不重放旧 single-use case、不复用旧 gate/window、不写 Steam/正常存档。
- 上述只为方案说明；本轮不生成 gate、不运行、不建立实现。

## 7. 证据判定（R2）

### 7.1 采集完备与加载结论分离

- `evidenceReportable`（复用 `loader-control-controller/controller.py:993–1004`）与进程 exit 0 只说明观察结束、postrun 读取、清理和保护状态可报告；**不要求 READY、不排除同船 NRE、不解析产品 InitPools 日志**。因此“完整采到同船 NRE”仍可为 true/0，不能当作加载成功。
- 保留原始字段与退出码语义；失败事实完整也保留为失败，不为“绿”改变语义。

### 7.2 限定正结论条件（同一可信 fresh observation segment）

必须同一段满足下列全部：
1. 实际候选归属：Melon 加载 `OhMyMods.AndroidProbe.dll` 且 SHA=`c1e43…`；609e `NATIVE_CONTROL_INIT` 合同成立（`mainAssemblyPresent=true`、loader=melon）且 `controlValid=true`；
2. 真实到达 READY，随后按原观察契约正常 STOP；
3. 同段**无同船 NRE**；
4. 同段产品日志出现 `[AndroidPoolLifecycle] native InitPools returned pm=… count=… active=…`（来源 `android/PatchAndroid_ProfessionPoolLifecycle.cs:22–42` 的产品 postfix）。

满足 1–4 才可形成限定结论：“本候选在本场景启动期未复现该 NRE 且 InitPools 正常返回”；count 原样记录，不推断九池/注册/完整兼容/保存/玩法通过。

### 7.3 不可补绿情况

- 同船 NRE（无论采集是否完整）→ 不作加载成功结论；停止，不跑 0.0.6、不称成功。
- READY 但缺产品 return 日志 → unknown，不得用 READY 或旧日志替代。
- 候选归属错误、probe UNKNOWN、提前结束、日志缺失/截断/归属不明 → unknown/invalid，不补绿。
- 复用已有原始日志与 facts 直接复核；不新增 parser/addon/扫描/结果账本。

### 7.4 后续最小 host/结果核对（本轮不运行）

至少覆盖：完整采到 NRE 仍非成功；READY 缺产品 return 为 unknown；错 Main 归属不能通过；只有正常配对证据给限定启动期结论。

## 8. 未验/未知（不变）

c1e43+609e 实际启动行为未知；InitPools 是否自然回调及 count 未知；0.0.6 detour 的异常传播影响未知；本 case 实现/构建/host tests 尚未运行；手机/平板/多人/长期不在范围。

## 9. 请复审的范围与回复

请只复审 **R1 派生合同/写入边界** 与 **R2 采集-结论分离及同段条件** 两项增量；其余已接受内容与冻结证据直接复用，不要求重写 controller 或重跑旧审查。

建议回复：`APPROVE_ZERO_EXTRA_HOOK_CONTROL_PLAN_ONLY`（认可 R1/R2 方案）或 `CHANGES_REQUESTED` + 具体条目；回复请带本修订 commit HEAD 与请求文件 SHA256。审查结果写入同任务目录。
