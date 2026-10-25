# Review Request — Source / tooling derivation for zero-extra-hook control case

- 日期：2026-10-23
- 发起：DSH Android Operator（`mac-dsh-ohmymods-android-operator` / session `dsh-android-profession-archery-20261023`）
- 审查：Codex 原会话（增量 Reviewer）
- 工作树：`/Users/Admin/projects/ohmymods/.local/worktrees/android-economy-262-dsh`；分支 `dsh/android-economy-262-20261023`；上一已审 docs HEAD `bfeead0`；PR #263 OPEN/Draft
- 方案依据：`review-result-zero-extra-hook-control-20261023-codex-r2.md`（SHA256 `b795c68e57405bc122476444da68027ad98a0a4d1c385ddaf30d0e139d592b0d`）批准 plan-only；本请求为**最小工具派生的 source/host 审查**，不请求 Native/构建/合并/gate。

## 1. 目标

冻结并审查新 case `controlled-candidate133-zero-extra-hook-control-isolated-20261023-single1` 的最小派生工具：0.0.1 零 Harmony 观察器 `609e` + 候选 `c1e43` + cfg `16b686` + F `35fae` 的一次加载对照；工具只采集事实，不新增 Pool/Boat detour、不新增 parser/addon/结果账本，不修改产品源码。

## 2. 派生文件（相对 G19 任务根，冻结 hash）

| 文件 | SHA256 | 相对模板改动行 |
|---|---|---|
| `zero-extra-hook-control-controller/controller.py` | `8495518a1ab14c86b133ec3cfea5576361c3c746f98e72e704d71ea7b0abce81` | 26 |
| `zero-extra-hook-control-controller/test_controller.py` | `c94ca6642f5c997ed66e7bebcc7fa34c21424343340f89abdda33048fefb284c` | 4 |
| `zero-extra-hook-control-controller/register.py` | `d405af3c3577d1c7c7d54c94e02e872a485bd99f1c402b9d87fbb4b19487dc63` | 28 |
| `next-zero-extra-hook-control-case-20261023/packet/build_packet.py` | `185fc84818c4eccfb088d7f4d621e2b6a1c39b5da406e0e20306382161078ae7` | 12 |
| `next-zero-extra-hook-control-case-20261023/packet/root-launch.py` | `e666aa4dcd6517823e1a6bb9ab0dab8ab8265608e9b5c62941d274fad4d8435f` | 8 |
| `next-zero-extra-hook-control-case-20261023/packet/root-postflight.py` | `fda868374128b37a7124eff0f632fc79bb29f52ea3f2658ccdd993377bb7c971` | 14 |
| `next-zero-extra-hook-control-case-20261023/packet/root-normal-readback.py` | `9e1612870f54561a24ac86934bdecbb1f1e1f89e4cb228c6927e96ead91ab2f6` | 12 |

完整冻结 diff：`/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/next-zero-extra-hook-control-case-20261023/source-diff.patch`，SHA256 `e1ae2460a528bbe9ffacd9460a3b9a9b5d68eeb97f356ac553b37047a8676e25`（19137 B）。模板对照仍在 `candidate-fixture-control-controller/` 与 `next-candidate-fixture-control-case/packet/`，未改动。

## 3. 依赖 hash 闭包（引用原文件，不复制新库）

| 源 | SHA256 |
|---|---|
| `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g1-native-memory-apkbudget/common.py` | `1d08c5b6ccf328321dc1eb5fb54c6d7de0664f3f6c2a40616c4f65dd0ef8899f` |
| `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g12-writer-durability-20261016/paired_main_cfg.py` | `905acc90ab33f640e6fe1ccc19d5cba5da8a9e562f930041970c7559dca0ff06` |
| `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g18-shared-save-sidecar-20261018/worker/root_read_sidecar_case.py` | `c148deaf1cb84482784fb11b35568db6d69265b691a005a7e517a86e6ae44ec7` |
| `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g17-apk-verification-scope-20261016/packet/root-execute-controlled-main85-case.py` | `340d027666a20ee7b98c99687b8a26d527728deeccd370f5b13aeb5a69bdc56e` |
| `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g17-apk-verification-scope-20261016/packet/prepare_startup_case.py` | `04c4f4d8eb7304605da5ccb69f74be5f681f16dc859b24dcc93bade1d8857893` |
| `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g17-apk-verification-scope-20261016/packet/startup_observe.py` | `1b00aae0eee9d8476d16278a45cbebcc878f503244d49f315e335739a871627e` |
| `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g17-apk-verification-scope-20261016/packet/case_spec.py` | `599db7183ff34821be8a8ff5824a6747c1f5b7fbafe599ecb61fe11cbee7393f` |
| `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/profession-fixture-load-controller/controller.py` | `49334a43b8990e96eae3e7278f1e582ecf863ae47100d83e1d117c292d9f51b8` |
| `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/loader-control-controller/controller.py` | `b38b6e812e8ad995f9f0198665786987252df66e95310e6fdc0341a91e1d41af` |
| `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/zero-extra-hook-control-controller/controller.py` | `8495518a1ab14c86b133ec3cfea5576361c3c746f98e72e704d71ea7b0abce81` |
| `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/zero-extra-hook-control-controller/test_controller.py` | `c94ca6642f5c997ed66e7bebcc7fa34c21424343340f89abdda33048fefb284c` |
| `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/zero-extra-hook-control-controller/register.py` | `d405af3c3577d1c7c7d54c94e02e872a485bd99f1c402b9d87fbb4b19487dc63` |
| `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/resume-20261019/loader-control-probe-worker/src/NativeSceneControlProbe.cs` | `3a46b6e2926eb51b9cacbee775afc206d2a71cfd0302a4e668327cda8852c410` |
| `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/resume-20261019/loader-control-probe-worker/src/ProbeState.cs` | `a6f18c59f72fd0b29d2524350be4c8902de6a66f8acf133a9efa1d930044a601` |
| `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/resume-20261019/loader-control-probe-worker/src/OhMyMods.NativeSceneControlProbe.csproj` | `fd11b90f48b6d459d9fcfde1ab25e8003565beffc91c78fbc65f8ae6db092007` |
| `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/resume-20261019/loader-control-probe-worker/root-build/receipt.json` | `825b4cadf835f9949ddff7ff184164bd54e212d39b2af9ae345b6029dbb652e8` |
| `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/next-zero-extra-hook-control-case-20261023/packet/build_packet.py` | `185fc84818c4eccfb088d7f4d621e2b6a1c39b5da406e0e20306382161078ae7` |
| `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/next-zero-extra-hook-control-case-20261023/packet/root-launch.py` | `e666aa4dcd6517823e1a6bb9ab0dab8ab8265608e9b5c62941d274fad4d8435f` |
| `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/next-zero-extra-hook-control-case-20261023/packet/root-postflight.py` | `fda868374128b37a7124eff0f632fc79bb29f52ea3f2658ccdd993377bb7c971` |
| `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/next-zero-extra-hook-control-case-20261023/packet/root-normal-readback.py` | `9e1612870f54561a24ac86934bdecbb1f1e1f89e4cb228c6927e96ead91ab2f6` |
| candidate Main c1e43 `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/boat-upstream-20261021/implementation-native-definitions-20261021/root-build-identity/release/OhMyMods.AndroidProbe.dll` | `c1e43d48960a99977c870f88f331e106ded3a8ea2201c4f81e9f01925d9445c6` |
| cfg 16b686 `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/resume-1453/profession-activation-config/MelonPreferences.activation4.cfg` | `16b68641cedac18f8bfeea9fc19948c11d6aaed16331b643a0623904d93d324c` |
| fixture F35fae `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/resume-20261019/fixture-codec-audit/evidence/global-v35.day32.original.gz` | `35faea071dd59332d61e80e9136ffa2c190f8bbb76f00e4e21cca169f4580fb5` |

## 4. R1 绑定处置（已落到派生差异）

- task operation `a7459a58-3446-4856-aaa3-5f02e1115de2` 保留，不新建 task/operation；owner/session=DSH（register.py 与 controller.py 常量）。
- register.py 同时重绑 `CASE`/`PACKET_CASES` 与 `SOURCE_REPORT_PATH`/`SOURCE_REPORT_MARKER` 两组；`--branch` 保持 coordinator 分配给 #232 的 `codex/android-profession-archery`；reviewer 记为本轮实际身份 `mac-ohmymods-codex-desktop`；`OLD`=当前 canonical plan SHA `e1040c5f…`，`BASE`=`bfeead0`（本批源代码基线；docs 后续 commit 不改本批工具字节）。
- `root-launch.py` 重绑新 controller/case/objective/review；`build_packet.py` 重绑 REVIEW/marker 与 gate 文案；`root-normal-readback.py` **删除**旧 `/Users/Admin/.codex/worktrees/android-economy-recovery/...` 的 `mirror.write_bytes`，只保留 canonical checklist/plan 只读回读与比对。
- `root-postflight.py` 的保护基线改为**同一次运行**的 `result.hostProtectionBefore + hostDisksBefore`（8 文件），不再复用旧 `resume-1453/root-resume-host-preflight.json`；fresh stat/hash 与 pre-run 快照一致才 fullOriginal；候选 hash 断言更新为 c1e43。
- 未变 base/helper（loader-control、profession-fixture-load、g1/g12/g17/g18）按上表原路径/hash 引用；reviewed-source 快照沿用既有 builder 机制，不新增库副本。

## 5. R2 判定分离（工具只记录事实）

- `evidenceReportable`/exit0 语义不变：只表示观察结束、postrun 读取、清理与保护可报告；完整采到同船 NRE 仍可为 true/0，不作为加载成功。
- 限定结论在结果判定阶段按同一可信 fresh segment 联合核对：候选 SHA 归属、609e INIT/controlValid、真实 READY 后正常 STOP、无同船 NRE，再与同段产品 `[AndroidPoolLifecycle] native InitPools returned`（`android/PatchAndroid_ProfessionPoolLifecycle.cs` 产品 postfix）核对；缺失/UNKNOWN/截断/归属不明保持 unknown/invalid。
- 不新增 parser/addon/扫描/结果账本；root postflight 只记录 `mainAssemblyPresent/candidateMainHashSeen/controlValid/ready/stop/unknown/sameBoatFailureObserved/evidenceReportable` 等既有 facts 供判定。

## 6. 已做验证

- `python3 -m py_compile`：7 个派生 .py 全部通过。
- 派生 `test_controller.py`：10/10 host tests 通过（含 fresh main hash 三态、READY/STOP 与同船 NRE 终端、pair ledger、device guard、postrun 失败标签）。
- `build_packet.py` 在无 source review 时按预期 fail-closed（exit 1，未生成任何 packet 产物）。
- **未做**：packet 构建、source-review.json、plan revise/approve、构建、ADB/AVD/Native、安装/合并。产品源码零改动；候选/cfg/F/addon 字节未变。

## 7. 请求审查范围与下一步

请审查：第 2 节 7 个派生文件与冻结 diff、第 3 节 hash 闭包、R1/R2 处置是否符合已批准方案；是否有必须修正项。
如批准，请把 source 报告写到 `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/resume-20261019/zero-extra-hook-control-review/report-public.md`，首行含 `APPROVE_ZERO_EXTRA_HOOK_CONTROL_SOURCE_ONLY`，并含 case/objective/verdict/sourceSHA256/mustFix/unverified 字段，供 builder 生成 source-review.json。
随后我运行 builder 生成 packet/reviewed-source/gate/window，冻结新 hash 后再提交 packet/source-review 的下一阶段审查；在受管 plan/gate 与单次窗口落实前不启动 Native。

## 8. 未验/未知

c1e43+609e 实际启动行为未知；InitPools 是否自然回调/count 未知；0.0.6 detour 影响未知；本 stage 无运行/构建证据；手机/多人/长期不在范围。
