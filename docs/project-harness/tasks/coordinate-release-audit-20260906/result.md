# v4.5.0 发行材料静态一致性审计结果

Task ID：`coordinate-release-audit-20260906`（win-omp 受管 worker，静态审计）
审计日期：2026-09-07（Windows 本机 UTC+8；材料中 `+08:00` 时间戳均指该本机本地时间，§3.8 统一换算为 UTC/Z 呈现）。
版本：R2 修订（响应 review-r1.md 的 CHANGES_REQUESTED 与 Operator 同范围精度要求）。
本文件是唯一产出：R1 与 R2 均只写本文件；worker 未修改其它文件、未运行 harness/Coordinate mutation、未运行脚本/构建/游戏/打包/部署、未访问网络/远端/Steam/测试游戏目录/日志/配置/存档/凭据、未发送消息、未 commit。本报告不改变任何 checklist 游戏任务状态，本身不构成任务验收；本修订不自我标记 approved/done，等待原独立 reviewer 复核。

R2 修订范围说明（其余章节内容不变）：§1 工作区表述、§3.1 字段定位（P2-5）、§3.8 时间线时区表述（P1）、§3.9 文档覆盖描述（P2-1/2/3）、§4 脚本边界表述（P2-4 + Operator 精度要求）、§5 O-1 相应同步。输入范围、冻结基线、§2 的 27 份哈希/字节表与第 6/7 节结论分类不变：材料 26 份按 `2437c6d` 读取、审计 plan 按 `847badc` 读取，表值沿用 R1（Operator 已全量独立复算匹配）。本修订重读核对的对象：tag 对象、publication.md、package-receipt.json、github-release.json、startup-deployment.json、scripts/package_release.py、csproj、release/ 四份当前文档与 MOD_V4.0.0 说明、两份 plan.md 的相关行。

## 1. 基线定义与范围合规

- 审计材料基线 commit：`2437c6d28de71849168884cc8e4ff9fb73fe8579`（提交信息 "docs: record v4.5.0 publication and verified deployment"；commit 时间 2026-09-06 15:59:43 +08:00 = 07:59:43Z）。
- 当前 HEAD：`847badc6e1ade36ee25b2ecd17c863a18773770e`（commit 时间 2026-09-07 01:02:39 +08:00）。基线 → HEAD 之间仅两个 Coordinate 接入 commit（`3a62143` "Add scoped Coordinate onboarding and release audit task"、`847badc` "Preserve legacy Windows plan locators in Coordinate readback"）。`git diff-tree 2437c6d..HEAD` 在材料范围内（脚本/csproj/release-450 任务目录/release/）为空 → 全部材料文件在两个基线下字节相同（R2 重查仍为空）。
- 「工作区 clean」是首轮输入读取前核对的事实（R1 记录当时 `git status --porcelain` 为空）。R2 读取时点起，当前工作区已含 Operator 侧 review/packet 元数据，不能把整个当前工作区描述为 clean：porcelain 非空项为未跟踪的 `result.md`、`review-r1.md`（本任务目录）、`docs/project-harness/current/review.md`、`docs/project-harness/current/closeout-packet.md`，以及已修改的 `events.jsonl`、`harness-checklist.json`、`progress.md`（后三者为 harness/Coordinate 状态文件，由 Operator 维护）。该变化不影响本审计：porcelain 未列出任何材料路径，材料文件内容与冻结基线一致（见上 diff-tree）。
- 基线内 tracked 的 ZIP/DLL 数量为 0（R2 重查全树 `ls-tree | grep -Eic '\.(zip|dll)$'` = 0）→ 无产物实物，本轮不可能重算 ZIP/DLL 哈希、CRC 或实际 IL。
- 输入范围内文件全部先经 `git ls-tree` 确认为基线 tracked 后使用；未使用任何工作区未跟踪文件作为输入（review-r1.md 仅作修订依据，不在输入表内）。
- 哈希方法：`git cat-file blob <基线blob> | sha256sum`（git 对象原始字节，LF 规范；复现接口见 §8）。27 份表值沿用 R1 冻结表（R1 曾抽查复算一致；Operator 已全量独立复算匹配，R2 任务书确认）。

## 2. 输入清单（27 份，逐份 SHA-256 + 字节数 + 基线 + 读取深度）

材料基线 `2437c6d`（26 份）；审计 plan 不在材料基线中，读取基线为 HEAD `847badc`（1 份，表末单独列出）。
读取深度标注：`全文`= 全部行经 read 读取；`分区+扫描`= 关键区/首尾读取 + 对全文件作版本模式正则扫描（`4\.5\.0|V4\.5|4\.0\.0|v4\.5`，grep 覆盖每一行）；`头部+扫描`= 仅头部数行 + 全文件版本模式扫描。哈希一律为整文件内容哈希。

### docs/project-harness/tasks/release-450-20260906/（14 份，均全文）

| 相对路径 | 字节 | SHA-256 |
|---|---|---|
| plan.md | 1469 | a4fd20c4563335f19f4c77d0d3ccde1aa0fd7521880c8aaf5661d6e9e31cfb49 |
| publication.md | 2541 | 54839d843b623661df79dba804d9257a03e2c9e90d6f4a8e1340f85c56cf9088 |
| package-receipt.json | 852 | 571ac89a21529ee84d6774ff0c77f2e7d5c2e54a24a03a8eabc4b24af3414848 |
| github-release.json | 747 | e5476a079194a77a888b6ddfd18648a97c32f99f7c89f23faaf0885af4a58410 |
| dll-metadata.json | 158 | ccde6404bfbb70f3e86343544ba1549abcde70e6e8174e31c03ea7c1b73d243c |
| startup-deployment.json | 3410 | f4f4d073e661d11917cff86bc83cc17b2466c6398e99df91cf35432dbf2b5066 |
| release-body.md | 3432 | f9e559d71f149b18ba65be23c11ad913b853b97b028eb395afb876455e927e12 |
| review.md | 3634 | 552e24242f0b7d054aeaf2da9b2563a780b97fc0c8bc67186e5987b6e753bd0f |
| user-acceptance.md | 812 | 3ab73b3c0662e9de356e50f3448941d618f390de94393d413d2b60dc9b8a38f1 |
| clean-build.txt | 403 | 585ba66f4cc43c32a3b7edfe008a4c532c85dfe2c1d92935f4150078a5914dec |
| build.txt | 175 | 21ab6b13e3793c9ff108d1fbc096a9b914a979cd2327033e68b7c3273e4de8b6 |
| calendar-tests.txt | 41 | dc7aba8f05ee7dc0c2acb3ab1cc95cf0bd3a64658a8d26708c07298f41b6311a |
| crossbow-defense-tests.txt | 587 | 8ec562f4cc108c561d067d51a82de7a3bea51bb6b20006bdf1181a1825d94455 |
| knight-powers-tests.txt | 1945 | c50f8ad449e4367194f643b0f3e05a6ac9abee646646995a50f21c4593334604 |

### 工程与脚本（2 份，均全文）

| 相对路径 | 字节 | SHA-256 |
|---|---|---|
| il2cpp/KingdomEnhancedMod.csproj | 7399 | 8b223bb57743810b5b331c3adc000a453b650636ca43e73b320970c68c3ffc3a |
| scripts/package_release.py | 5105 | 6a2f419b33a93c31cb2e82bebf703a97fe21f071cb60ad1dbdcd68fd6d6cdd6b |

### release/ 玩家说明（10 份）

| 相对路径 | 字节 | SHA-256 | 读取深度 |
|---|---|---|---|
| MOD_V4.5.0版本更新说明.txt | 3598 | 3b8a408eea942e4ab6315594caf54b06847fd898b28cdb3f63235b9b12804fa1 | 全文 |
| MOD_USER_GUIDE_ZH.txt | 25311 | a5e092af65e167ffe64bb96405385fc7e215b7d11e40957ab9eea295abcaa58b | 分区+扫描（1–300/478 行全文读取，其余行版本模式扫描） |
| MOD_UPDATE_AND_FIX_LOG_ZH.txt | 28825 | 6ef0966c7573ffae46848ec80bf7a7c2214054b6656ccc4f9d5764111c28419a | 分区+扫描（1–103/522 与 495–522 行读取，其余行扫描） |
| MOD_CAPABILITIES_AND_ROADMAP_ZH.txt | 16887 | fcf6b3758a191787731aa3193e8795312ac6cd48e3e38a222c0902d364aa9ef7 | 分区+扫描（1–73/274 与 240–274 行读取，其余行扫描） |
| MOD_V4.0.0版本更新说明.txt | 5659 | 9c52efd4a368c94b4854e2ff1442223f202fd6fbe6bb77820aca5579b92ac99f | 头部+扫描 |
| MOD_V3.5版本更新说明.txt | 6098 | abdb8d3ca14113ebce307749743def05012f702de943569e28b60061832a2319 | 头部+扫描 |
| MOD_V3.1版本更新说明.txt | 4969 | 1324d9e4ad696ae67b4140869b5f64e466ec9f741be38cd8be4ca93ba5a8aa41 | 头部+扫描 |
| MOD_V3版本更新说明.txt | 8183 | 3dc140e04b21ff5bf702165da68175736baa23c9150cac1aa8c47d27da183f2c | 头部+扫描 |
| MOD_V2.1版本更新说明.txt | 2568 | 64fcdf65ce9cc4652e4e0364c92b4008c0e3542dd808a8e45753315117e16411 | 头部+扫描 |
| MOD_V2版本更新说明.txt | 8419 | f9b0dd3432e731a828d282399df1ef197af6e88626264eae63a00ce13401c96a | 头部+扫描 |

历史版本说明（V2–V4.0.0）只承载各自版本的历史声明，版本模式整树扫描确认其不含 4.5.0 引用；审计的版本一致性结论不受其正文影响。

### 审计任务 plan（1 份；材料基线中不存在，读取基线为 HEAD `847badc`）

| 相对路径 | 字节 | SHA-256 |
|---|---|---|
| docs/project-harness/tasks/coordinate-release-audit-20260906/plan.md | 3498 | eefc016e9f5dcd96a48c704eee7e5e18e866d98c31478b72eada05d31a654b56 |

## 3. 逐项核对结果

状态标记：`[已核验]`= 本轮可独立复算/复读确认；`[记载未复验]`= 材料互相支撑但需实物/运行才能重验；`[观察]`= 非阻断性说明。

### 3.1 tag → source commit `[已核验]`

- 本仓库 `git rev-parse v4.5.0^{commit}` = `c2032185a5bc213f085a5831abedbbaacfab8b36`；v4.5.0 为 annotated tag（对象 `6c436cec883c…`，tagger 时间 2026-09-06T15:57:15+08:00 = 07:57:15Z（epoch 1788681435），消息 "Kingdom Enhanced Mod v4.5.0"）。
- 材料字段定位：publication.md 第 4 行（"Release tag v4.5.0 peels to source commit c203218…"）；github-release.json 的 `tagName: "v4.5.0"`、`targetCommitish: "c203218…"`；package-receipt.json `commit` 字段；审计任务 plan（本任务书 `coordinate-release-audit-20260906/plan.md`）"问题与目标"段第 9 行（"…该提交记录的 release tag 指向 `c203218…`…"）。更正（P2-5）：release-450 的 plan.md 第 3 行仅含授权语 "…a new v4.5.0 tag…"，无 commit 值，不充当 tag→commit 的定位依据。
- `c203218` 是审计基线的祖先（`merge-base --is-ancestor` 通过）。基线相对发布 commit 只新增 6 份记录文件（clean-build.txt、dll-metadata.json、github-release.json、package-receipt.json、publication.md、startup-deployment.json，`git diff-tree --name-status` 确认），未触碰任何源码/脚本/说明 → "receipt 后续提交推进但 tag 不指错"成立，tag 剥离目标与材料完全一致。
- 远端 GitHub tag/Release 状态（"published, stable, Latest"）仅由 github-release.json API 快照与 publication.md 记载：本轮无网络访问，远端侧 `[记载未复验]`。

### 3.2 plugin/assembly 版本链 `[已核验（声明层）/ 部分记载]`

- csproj 第 12 行 `<Version>4.5.0</Version>`；该值在 c203218 与基线相同（`git show c203218:…csproj` + 空 diff 确认），且是发布时刻的真实版本声明。AssemblyVersion 未显式声明，SDK 默认由 Version 派生（4.5.0 → 4.5.0.0）；`BepInEx.PluginInfoProps 2.*`（csproj PackageReference，第 24 行）构建期生成插件版本 4.5.0。
- 材料字段定位：dll-metadata.json `version: "4.5.0"`、`assemblyVersion: "4.5.0.0"`；package-receipt.json `version`、manifest `ModVersion`；publication.md 第 7 行 "assembly 4.5.0.0 / plugin 4.5.0"；release-body.md 首行、MOD_V4.5.0版本更新说明.txt 首行标题、MOD_USER_GUIDE_ZH.txt 第 5 行（"Mod 版本：V4.5.0（上一正式版 V4.0.0）"）、MOD_UPDATE_AND_FIX_LOG_ZH.txt 第 4 行（"V4.5.0（2026-09-06）"）、MOD_CAPABILITIES_AND_ROADMAP_ZH.txt 第 3 行（"V4.5.0 版，更新日期 2026-09-06"）→ 声明层全部一致。
- publication.md 第 7 行 "Actual embedded IL confirms no shared Dispose patch and Samurai disable cleanup is not gated by Enabled"、review.md Verdict PASS 条件、dll-metadata.json 布尔项（`samuraiDisableConfigGated: false`、`samuraiCleanupCalled: true`、`unsafeDisposePatch: false`）与 plan.md 第 7 行清理处方（只移除 Samurai OnDisable postfix 的 Enabled 门）互相支撑 → 这些 IL 级结论本身 `[记载未复验]`（无 DLL 实物、未运行反编译/静态 IL 检查；本次输入范围也不含 il2cpp 源码）。

### 3.3 ZIP size/hash/entries `[字段交叉一致；实物未重算]`

| 字段 | package-receipt.json | publication.md 第 6 行 | github-release.json asset |
|---|---|---|---|
| sha256 | `zipSha256: 658c68074e6a618e0eb78b4a8b6a7618b77bbddf71260dd90e25774f43539e06` | 同值 | `digest: "sha256:658c6807…"` |
| bytes | `zipBytes: 39440865` | 39440865 | `size: 39440865` |
| entries | `entries: 313` | 313 | — |
| 名称 | — | KingdomEnhancedMod_v4.5.0_IL2CPP.zip（第 5 行本地路径字段的文件名） | `name: KingdomEnhancedMod_v4.5.0_IL2CPP.zip` |

三份材料同值、无内部矛盾。仓库基线无任何 tracked ZIP → 哈希/CRC/条目数/压缩内容 `[记载未复验]`，本轮未声称重验。

### 3.4 DLL size/hash `[字段交叉一致；实物未重算]`

- package-receipt.json `dllSha256: aa4b1959c16e1dbbdcc5546437f559725e029ba4386b456a55ad6c5b09e3bd8a`、`dllBytes: 374272`；manifest `DllSHA256`/`DllSize: "374272"` 同值。
- publication.md 第 7 行同值同 size。
- startup-deployment.json `dll: "AA4B1959C16E1DBBDCC5546437F559725E029BA4386B456A55AD6C5B09E3BD8A"`（大写形式，与 receipt 小写哈希逐字节同一值，十六进制大小写等价）。
- 发布 commit 链：receipt `commit` = manifest `GitCommit` = publication 第 4 行 = 实测 tag 目标 `c203218` → 打包/测试/发布使用同一 source commit，字段一致。
- DLL 产物未入库 → 字节数/哈希/实际 IL `[记载未复验]`。

### 3.5 构建记录 `[记录互证；未重跑]`

- build.txt 与 clean-build.txt 均记 "0 个警告 / 0 个错误"，与 publication.md 第 8 行 "0 warnings/errors" 及 plan.md 门禁一致。clean-build.txt 的还原耗时/输出路径指向 clean 检出工作树（相对 release commit 的 detached worktree），与 receipt `commit: c203218`、脚本的 git-clean + `git rev-parse HEAD` 写 manifest 逻辑一致（打包工作树 HEAD 即 c203218 时 manifest GitCommit 才等于该值）。
- csproj `CopyOutput` target 带 `'$(BepInExPluginsPath)' != ''` 条件（第 127 行起，Debug 配置下）→ 与 "deployment disabled 构建" 的机制描述一致（不发 DLL 到插件目录）。
- 本轮未运行任何构建 `[记载未复验]`（输入约束禁止）。

### 3.6 回归测试计数 `[记录互证；未重跑]`

- publication.md 第 10 行：calendar 95702 assertions、knight 27 scenarios、crossbow 9 scenarios/1462 assertions。
- 对应记录文件：calendar-tests.txt（"Calendar tests passed: 95702 assertions."）、knight-powers-tests.txt（"RESULT: 27 passed, 0 failed"）、crossbow-defense-tests.txt（"RESULT: 9 passed, 0 failed; 1462 assertions"）→ 全部吻合。
- 本轮未运行测试；这些记录为原测试执行结果，按任务约束不得当作本轮新测试 `[记载未复验]`。

### 3.7 startup/deployment 观测记录 `[记录互证；未复验运行]`

- startup-deployment.json：start 2026-09-06T15:55:51.5100453+08:00（= 07:55:51.510Z），end 15:56:39.8617666+08:00（= 07:56:39.862Z）→ 时长 48.351721 s（本轮独立计算），与 publication.md 第 9 行 "observed for 48.4s" 一致（四舍五入）；20 个采样点（2.3 s → 48.1 s）全部 `responding: true`；`reason` 与 publication "until scene/ClockDiag; stop" 描述一致。
- `saveBefore` == `saveAfter` == `95172D158C29DCE1B42790BAF20FD9B416B5D22C66F0A8446BE5AADD73DE00B1`，与 publication.md 第 9 行同值。
- `previousDll: 8390AF…` 与 review.md 所述 "已实测的 8390AF DLL" 互证；最终部署的是 AA4B（= receipt aa4b…），与 review.md 的发布门禁执行结果一致（未复用 8390AF 产物打包）。
- 观测本身（游戏进程行为、存档字节）未在本轮复现 `[记载未复验]`。

### 3.8 时间线顺序 `[已核验，内部一致]`（全序列统一 UTC/Z）

时间戳换算依据：材料 `+08:00` 字段为打包机（Windows 本机 UTC+8）本地时间，与 Z 差 8 小时；tag 对象 tagger 时间 15:57:15+08:00 = 07:57:15Z。

- receipt manifest `GeneratedUtc` 2026-09-06T07:55:01.904Z（= 15:55:01.904+08:00）——manifest 生成时刻：脚本构造 `BUILD-MANIFEST.txt` 文本时调用 `datetime.now(timezone.utc)`，先于 `with ZipFile` 组装 ZIP 与 `writestr` 写入，不是 ZIP 构建完成/落盘时刻；
- startup-deployment.json start 07:55:51.510Z、end 07:56:39.862Z（= 15:55:51.5100453+08:00 / 15:56:39.8617666+08:00）；
- tag 对象 tagger 时间 07:57:15Z（= 15:57:15+08:00，epoch 1788681435；与 §3.1 同刻）；
- GitHub asset createdAt 07:57:23Z → publishedAt/updatedAt 07:57:30Z（github-release.json 快照字段）；
- receipt commit 2437c6d 07:59:43Z（= 15:59:43+08:00）。

序列严格单调：07:55:01.904Z < 07:55:51.510Z < 07:56:39.862Z < 07:57:15Z < 07:57:23Z < 07:57:30Z < 07:59:43Z。可核对的是各记录字段的时区换算与先后：GeneratedUtc（manifest 生成时刻，见上）早于 startup 记录开始（07:55:51.510Z），发布动作（tag 07:57:15Z、asset createdAt 07:57:23Z）晚于测试结束记录（07:56:39.862Z）。"完整 ZIP 已在受控启动测试前准备/验证"的时序结论来自 publication.md 末段第 13 行（连同第 8 行 release commit 的 clean detached worktree 构建、0 warnings/errors）的记载，不能由该时间戳独立推出；本轮未运行打包/启动测试，该叙事未独立复验。

### 3.9 玩家文档同步 `[已核验]`

- release-body.md 与 MOD_V4.5.0版本更新说明.txt 逐行 diff 仅 8 行差异：正文（一～五节 + 安装升级 + 验证/边界声明）逐字相同；差异仅为首部 4 行头注格式与末行发布页链接（release-body 用 markdown 加粗单行头，说明文件用"上一正式版/适配游戏/不适用 Mono"明细头并附发布页 URL）。
- V4.5.0 版本标识：4/4 份顶部带——V4.5.0 说明第 1 行标题、USER_GUIDE 第 5 行（"Mod 版本：V4.5.0（上一正式版 V4.0.0）"）、UPDATE_LOG 第 4 行（"V4.5.0（2026-09-06）"）、CAPABILITIES 第 3 行（"（V4.5.0 版，更新日期 2026-09-06，上一正式版 V4.0.0）"）。
- 2026-09-06 日期：仅 2/4 份带——UPDATE_LOG 第 4 行与 CAPABILITIES 第 3 行（见上）；MOD_V4.5.0版本更新说明.txt 与 USER_GUIDE 全文无日期行（各自以"上一正式版 V4.0.0"关联发布时点）。
- "新说明优先于后文历史数值"层级约定按实际承载方式分述：USER_GUIDE 第 7 行与 CAPABILITIES 第 5 行以明示文字（"V4.5.0使用补充/新增能力（以下新说明优先于后文历史数值）："）声明 4.5.0 区块优先；UPDATE_LOG 无此明示句，以第 52 行"以下为历史版本记录："分隔 V4.5.0 记录与历史记录，实现同义分层（最新条目在顶、历史条目向下逐版排列）；V4.5.0 说明为单版发布说明、无历史区段，该约定不适用。
- 安装指引（同文"安装与升级"区块）实见于 3 份：V4.5.0 说明第 41–48 行、USER_GUIDE 第 43–50 行、UPDATE_LOG 第 40–47 行。内容为五步：解压到包含 KingdomTwoCrowns.exe 的文件夹（不套版本层）、首启生成缓存、日志中应出现 "Loading [KingdomEnhancedMod 4.5.0]" 及 "build=4.5.0"、旧版升级可覆盖解压且包内不携带个人 KingdomEnhancedMod.cfg、联机双方使用相同游戏版本和 v4.5.0 Mod；"验证情况：已通过构建、回归检查、启动及场景恢复测试…"声明段同文见于三份（第 50–51 / 52–53 / 49–50 行）。CAPABILITIES 无安装指引章节；仅在其"三、仍在持续观察的内容"一节第 206 行含"（联机双方必须安装完全相同版本）"表述——观察语境，与安装指引不同类。
- "上一正式版 V4.0.0" 与 release/MOD_V4.0.0版本更新说明.txt（标题"王国增强 Mod V4.0.0 正式大版本更新公告"，其正文含"安装包不包含个人 Mod 配置、存档、游戏本体或反编译资料"等）存在性一致；V4.0.0 说明文件自身不载日期。
- "日志出现 Loading…"与 csproj Version 4.5.0 → 插件版本 4.5.0 的声明链一致（加载日志格式为 BepInEx 对插件名+版本号的输出，声明层一致；插件名元数据在 il2cpp 源码内，本审计输入范围不含，不延伸断言）。

## 4. 打包脚本静态边界审查（scripts/package_release.py，未执行）

脚本实现的条件与排除（基线 blob 全文审读，R2 重读核对）：
- 版本一致性门：`argv <ModVersion>` 必须等于 csproj `.//Version`（ET 解析）否则退出 → 与 receipt `version 4.5.0`/csproj `<Version>4.5.0</Version>` 静态吻合。
- 打包前 git-clean 门：`git status --porcelain` 的 stdout 非空即退出（脚本自身要求打包工作区 clean）；源码未检查 `git status` 与随后 `git rev-parse HEAD` 的返回码，manifest `GitCommit` 直接取 rev-parse 的 stdout。可静态确认的只有"porcelain 输出非空则不打包"；"GitCommit 必来自 clean HEAD"不是代码保证（历史 clean 构建的说法按 §3.5 材料记载分级，本轮未运行）。
- 输入边界（读取面按脚本源码枚举，全部为具名文件或具名目录）：
  - DLL 固定取 `il2cpp/bin/Debug/KingdomEnhancedMod.dll`（不存在即退出）；
  - ZIP 内容仅可能来自：测试副本游戏目录根级三个具名 doorstop 文件（`.doorstop_version`、`doorstop_config.ini`、`winhttp.dll`，存在才写）；`dotnet/`、`BepInEx/core`、`BepInEx/unity-libs` 全树递归（经 leak_check 黑名单过滤）；`BepInEx/config` 目录 `*.cfg` glob 中名字恰为 `BepInEx.cfg` 的文件（其余 `continue` 跳过，注释 "Never distribute the tester's personal gameplay settings."）；`BepInEx/plugins/KingdomEnhancedMod/` 下仅本 DLL；仓库 `release/` 下四份具名说明文档（存在才写）；仓库根 `release-notes-il2cpp.md`（存在则写入 `INSTALL.md`）；生成的 `BUILD-MANIFEST.txt`（ModVersion/GameCompatibility/GitCommit/DllSHA256/DllSize/GeneratedUtc）。
- 排除词（`leak_check`，小写化后匹配）：`.bak`、`logoutput`、`/cache/`、`/interop/`、`skidrow`、`_data/`、`kingdomenhancedmod.pdb`；根级三个 doorstop 名豁免；注释说明不泛匹配 `.pdb`（BepInEx/core 自带 Mono.Cecil.Pdb.dll 属合法核心程序集）。
- 静态可证明的产物约束（限定表述，P2-4 + Operator 精度要求）：
  - "个人玩法设置（如 KingdomEnhancedMod.cfg）及其它 `.cfg` 不入包"仅对 `BepInEx/config` 目录静态成立（该分支按文件名只放行 `BepInEx.cfg`）；`leak_check` 词表不含 `.cfg`，对 `dotnet/`、`BepInEx/core`、`BepInEx/unity-libs` 内不命中任何排除词的杂散 `.cfg` 无静态排除——其存在与否属未覆盖边界。
  - 遍历目录内命中黑名单词的文件（备份 `.bak`、日志 `logoutput`、缓存 `/cache/`、interop `/interop/`、游戏数据 `_data/`、`skidrow`、本插件 PDB）会被跳过；根级只探测三个具名引导文件，其余根级文件不被读取。
  - 存档/日志等路径不在脚本的显式顶层选择内（读取面顶层仅为上述具名文件与具名目录，未选中任何存档/日志目录）——此结论只到顶层选择为止：`dotnet/`、`BepInEx/core`、`BepInEx/unity-libs` 为全树递归，排除仅按 `leak_check` 词表命中，遍历目录内未命中词表的存档/日志副本或其它杂散文件静态上仍可能被读入；打包源目录树实况本轮未访问（无测试副本目录访问权），不构成"源树中不存在此类杂散文件"的保证，未知边界保留。
- 入库材料与脚本期望互证：`release/MOD_V4.5.0版本更新说明.txt` 等 4 份文档在基线 tracked；`release-notes-il2cpp.md` 在 c203218 与基线均 tracked 且 blob 相同（`9ff0d7a6…`，内容未纳入本审计读取——在输入范围外）→ 脚本的 INSTALL.md 条件分支在打包时点应已触发，与 receipt `layoutAndAllowlist: PASS`、publication "required docs" 表述无矛盾。
- 仅靠源码无法证明的运行时事实（需实物/当时目录状态）：实际条目数 313 与 ZIP/DLL 字节、CRC、真实目录树里是否存在黑名单未命中的杂散文件（含上述 `.cfg` 边界）、BepInEx 6/dotnet 引导与运行时在目标机的实际可用性、DLL 的实际 IL 内容。脚本本身不含 CRC 或布局白名单校验逻辑 → receipt 的 `crc: PASS`、`layoutAndAllowlist: PASS` 属打包期外部校验结果（publication.md 第 8 行亦载明外部校验修正过一个关于 Microsoft.Extensions.Logging.Abstractions.dll 的 validator 误报；该文件名不命中脚本任何排除词，与"脚本未改动、误报来自外部 validator"的记载一致）。这些 `[记载未复验]`。
- 脚本在 c203218 与基线之间无任何修改（见 §1/§3.1 diff），"Existing packaging code was used unchanged"与版本一致性门兼容。

## 5. 观察项（非不一致，供阅读者知晓）

- O-1（说明性，历史区文字陈旧）：MOD_USER_GUIDE_ZH.txt 历史区段"三、第一次启动"（第 100 行起）示例日志第 112 行仍写 `Loading [KingdomEnhancedMod 3.0.0]`；该文件顶部第 7 行起的 V4.5.0 使用补充区以"以下新说明优先于后文历史数值"显式覆盖（同款显式约定见 CAPABILITIES 第 5 行；UPDATE_LOG 以第 52 行"以下为历史版本记录："分层；V4.5.0 说明无历史区段）→ 属被显式覆盖的历史文字，不构成 v4.5.0 声明矛盾。
- O-2（路径字段，不复制为复现接口）：publication.md "Local full installer" 与 package-receipt.json `zip` 字段记录的是同一 ZIP（摘要相同）在两个个人目录的存放位置，均不在仓库内；产物存在性/路径无法在库内复核。
- O-3（远端状态）：github-release.json 是发布时点的 GitHub API 快照（asset `state: "uploaded"`、`downloadCount: 0`、`isDraft: false`、`isPrerelease: false`、createdAt 2026-09-06T07:57:23Z、publishedAt 2026-09-06T07:57:30Z）；本轮无网络，远端当前状态 `[记载未复验]`。
- O-4（范围外引用）：review.md 引用的 `release-notes-il2cpp.md:52-53` 边界声明与 publication "required docs" 提及的 INSTALL.md 源文件未纳入本审计内容读取（不在任务输入清单），仅做存在性/版本一致性层面确认。

## 6. 结论分类汇总

- 本轮已核验：27 份输入的完整性哈希（材料基线 26 份 + HEAD plan 1 份；表值沿用 R1 冻结表，Operator 全量复算一致）；tag v4.5.0 → commit c203218 剥离一致（tagger 07:57:15Z = 15:57:15+08:00）且为基线祖先；receipt commit/manifest GitCommit/tag 目标三者同值；csproj Version 4.5.0（发布时点与基线均如此）与 dll-metadata/说明文档版本链一致；ZIP/DLL 的 size/hash/entries 在三份材料间逐字段一致；构建 0W/0E、回归计数（95702/27/1462）、startup 时长 48.35 s vs "48.4s"、存档哈希、AA4B/aa4b 大小写同一、时间线 UTC/Z 归一后严格单调、V4.5.0 说明与 release-body 同文、脚本版本门与排除边界静态逻辑、文档版本/日期/安装指引覆盖的按文件精确分布。
- 材料记载、本轮未独立运行/复验（无实物或无授权）：ZIP/DLL 实际哈希、CRC、条目构成、DLL 字节数与 embedded IL 结论（含 "无共享 Dispose patch"、"Samurai cleanup 不受 Enabled 门控"）、构建与全部测试/启动观测本身、存档哈希、远端 GitHub 状态。这些项在材料内部互相支撑、无矛盾，但按要求不升级为"本轮重验"。
- 未发现不一致：全部字段交叉核对无冲突；无任何"未知"级存疑项需返工。R2 修订不改变此结论。
- 待实测边界（按 plan 验收标准 5 保留，不因本审计改变任何状态）：联机、换岛/场景切换、权威迁移、长时间游玩、存档兼容等游戏功能边界维持原有 doing/观察状态；本报告不构成任何游戏版本验收或对独立系统 BSOD 成因的声明。

## 7. 已知限制

- 无产物实物与无网络/外部访问权：一切需 DLL/ZIP/远端/游戏运行的验证均止步于材料互证。
- 输入范围由任务书限定：未读 il2cpp 源码、release-notes-il2cpp.md 内容、harness/checklist/历史任务文件（后者经任务书排除）；对这些文件的任何论断仅来自允许材料内的引用。
- 打包源目录树的实况（测试副本目录中黑名单未覆盖的杂散文件等）本轮未访问、未检查，静态边界只覆盖脚本读取面与已实现排除条件（§4）。
- 哈希为 git 对象规范内容（LF）；若在 autocrlf 检出下直接对工作区文件求哈希会因换行转换而不同，复现须用 §8 的 git 管线。
- 材料中个人绝对路径（构建/部署/存档位置）按任务约束未复制进本报告作为复现接口。

## 8. 复现接口（无个人路径）

- 材料哈希（任一份输入）：`git show 2437c6d28de71849168884cc8e4ff9fb73fe8579:<仓库相对路径> | sha256sum`（本审计 plan 自身用 `847badc…:docs/project-harness/tasks/coordinate-release-audit-20260906/plan.md`）。
- tag 剥离：`git rev-parse v4.5.0^{commit}`；发布提交存在性：`git cat-file -t c2032185a5bc213f085a5831abedbbaacfab8b36`；范围零改动：`git diff-tree -r --name-only 2437c6d HEAD -- scripts/package_release.py il2cpp/KingdomEnhancedMod.csproj docs/project-harness/tasks/release-450-20260906 release`（空）。
- tag 对象时间：`git cat-file -p v4.5.0`（tagger 行 epoch 1788681435 +0800 = 2026-09-06T07:57:15Z）。
- 交叉核对源均为第 3 节所列材料字段；测试记录、构建记录、startup 采样等原始产物不随本报告复制。
