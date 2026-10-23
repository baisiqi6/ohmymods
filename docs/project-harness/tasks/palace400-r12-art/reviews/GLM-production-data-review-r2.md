# 结论：修正后通过

架构、语义门禁与已见样本自洽；阻断点只有一个实质问题：validator 对 452 条 placement 只做计数/枚举校验，R12 契约的核心内容（support→world 换算公式、画布内锚点、双声明一致）完全没有逐行验证，而它是冻结前唯一的自动门。修复约 15 行 + 1 条负测，修完重跑即可进入 ready=true 流程。

## 审查三问

**1. 这个决策是否正确？——正确。**
- 语义门禁全部落实且由 validator 强制：`nativeAccepted` 双文件必须显式 false（缺键也拒）、`readyForIsolatedIntegration` 仅为离线齐备、`backgroundMotionRates` 强制 null（未测视差不得冻结）、`offlineReviewPending` 非空不得 ready。Native 未知量清单与任务声明一致，没有被 R7 历史值顶替的迹象。
- 收费归属正确：包内无 paid gate 常量，返程声明 `chargeCoins: 0` + `runtimeTriggerExtent: null` + runtime 责任移交 Native/main；10 金币入口归 main。
- 样本数学自洽：`[20,12820]` 恰为 400u×32px，端点映射 ±200；6420 为中心，`supportWorldX=(6420-6420)/32=0` 成立；gate designTopLeft 可由声明字段重算为 `[6420-218, 224-178]=[6202,46]`，与 `R11SourceRect` 的 ΔY=37 恰等于 224−187，证明 transport 未用旧 187 线或 sourceY 重定位（正是契约禁止项）；返程点 `[218,176]` 与 support `[218,178]` 分离且在 support 上方 2px，符合“门槛、下门沿之前”；54+13+5+10+4=86、358 帧总数、舞女 26/21 均与运行结果一致。
- 5 项单测语义真实：重签 SHA256.json 后仍被帧 readback 与 masterId 校验拒绝，证明帧层不可绕过——这恰好反衬 placement 层没有任何等效深检。

**2. 是否最优？——传输层最优，placement 层与批准覆盖非最优。**
哈希精确覆盖、crop 精确重建、二值 alpha/RGB0/Point/palette 双钉、2048 上限，都是正确的强校验。但 placement 层校验强度与契约篇幅严重不匹配（见阻断项 1），批准环节只钉 layout+86 资产、不钉 registry.json（见建议 3）。

**3. 是否引入新的 bug？如何解决？——不引入运行时 bug。**
包内 90 个索引文件均为数据/元数据 + 只读 Python 校验器，无运行时代码，不可能触共享 runtime/props/save/scene/mount（分支隔离按任务声明，本轮无工具无法复核 repo 状态）。新风险全部是“验证缺口放行笔误进冻结”一类，解决即阻断项。

## 阻断项（修正后才可设 ready=true）

**1. placement 行级一致性校验缺失。**
位置：`validate()` 的 `for p in layout['placements']:` 循环，现仅查 id 去重、assetId 存在、referencePlane 枚举、`len(supportLocalPixel)==2`。需补：
- `supportLocalPixel` 在所引 asset 画布内（`0≤sx≤w, 0≤sy≤h`，用 `byid[p['assetId']]['size']`），并与 registry 侧声明锚一致（street 类 = `support.point`；boat 水线、shore socket 按各自字段；若设计允许实例级覆盖须显式标记并加负测）。目前 gate 的 `[218,178]` 在 registry 与 layout 双声明、零交叉校验。
- `supportWorldX == (designSupportPixel[0]-6420)/32` 且 ∈ [-200,200]；`supportOffsetYUnits` 为数值。两字段冗余共存却不互验，452 行手写/工具产出中任一 typo 静默通过。
- `drawRole` ∈ `layout['drawOrderHints']` 键集、`district` ∈ 声明区集：拼错的非背景 drawRole 现在会静默落入运行时默认排序。
- 补 1 条负测（改 1 条 placement 的 supportWorldX 或令 supportLocalPixel 越界 → 拒绝），与现有 5 项同规格。

**2. 两个一行修复：**
- 资产循环后 `if dancer_cells is None: raise`。现在 court-dancer 整体缺失时 26/21 检查被跳过，只要其余 atlas 补足 358 帧即通过，返回报告还硬编码 `dancerTemporalFrames: 26` 造成假象。
- gate 返程检查补 `returns[0]['chargeCoins'] != 0 → raise`。“返程 0 金币”是本包声明事实，已钉 purpose/坐标系/point 却漏了同一条 interaction 里的这个字段。

## 非阻断建议

**3. offline approval 不钉 registry.json。** `--require-offline-ready` 只复核 layout SHA + 86 资产 SHA；registry 的 support/interaction/pivot/chargeCoins 在批准后修改并重签 SHA256.json 即可通过（负测三、四已证明重签轻而易举，帧层靠深检存活，registry 语义层没有深检）。建议 `offline-approval.json` 增加 registry 规范化哈希（剔除 `readyForIsolatedIntegration`/`offlineReviewPending` 两字段后计算，避开 ready 翻转的次序问题）。另：`fileHashesChecked: 90` = 86 资产 + registry + layout + palette = 89 之外多 1 个文件，身份未确认——若即 approval 文件，请确认生产脚本写的是 validator 实际读取的 `offline-approval.json`，而非仅在证据侧的 `offline-release-approval.json`；名字对不上会在 ready=true 时 FileNotFoundError，是响亮失败而非静默，但会卡冻结。

**未确认（材料不可证，不阻断）：** 294/64 帧拆分未按 kind 分别强制（仅总数 358）；actor originalPpu=null 字段未钉死（样本无 actor atlas 字段名）；dancer deck 锚与 theatre placement 的联动无自动校验（无两者 placement 样本）；契约 palette SHA `f9a2bf…` 与 `registry.paletteSHA256` 的转录一致性（validator 只钉 registry↔文件）。

放行条件：完成阻断项 1、2，重跑 validator（fresh root + `--require-offline-ready` 路径各一次）+ 6 项单测（原 5 + 新 placement 负测）全绿后，按既定流程核验 offline-release-approval.json 三类 SHA 并设 ready=true。
