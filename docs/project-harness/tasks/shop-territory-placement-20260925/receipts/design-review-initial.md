# 独立对抗审查：商店全领地选址（shop-territory-placement-20260925）

## 结论：REQUEST_CHANGES（方向=B 正确，但提案按现状有一类必然假阴性，需一处补充；其余契约需钉死浮点/闭区间/有界性细节）

## 证据核对（2.1.0 源码 + 仓库现状）

- 旧 `HeroShopPlacement.Find`（HeroShop.cs:42-63）：中点±30、步长0.75、81样本，`calls==81` 有测试钉死（tests/hero-shop/Program.cs）。用户报告成立：±30 之外的空地**从未被采样**，与空地真假无关。
- 原生 `OverlapsAnyExclusions(pos, dist, false)`（PayableManager.cs:469-502）真实语义，B 的镜像依据：
  1. payable 侧只对 `payablePlacementExclusionDistance > 0` 者生效，且**忽略 Steed/PayablePlayer/Merchant/PayableBush**（ignoreTrees=false 时 PayableTree **算**阻挡）；
  2. `_allBlockers` 全量、**无任何类型过滤**；
  3. 判交用闭区间四条件，**端点相触=冲突**（`num2 >= num3 && num2 <= num4`）；
  4. `RetrievePayablesByExclusion` 有“连续15个不匹配即停走”的近似，只可能让原生**更宽松**。
- 互操作前提：plan.md 已记录 actual 2.4 确认 AllPayables/_allBlockers/PayableExclusionPoint/payablePlacementExclusionDistance/GetExclusionPoint/exclusionDistance 存在；且两店 Create 已有 `payables.AllPayables` 2000槽遍历先例。前提成立。
- 英雄驿站与火铳铺共用 Find，互相排斥靠各自 PayableComponent 进 AllPayables，全领地搜索不破坏此机制（对方店就是一条普通 interval）。

## A vs B

- **A（全领地固定网格）否决**：步长0.75仍漏 <0.75 宽的可行带；缩到0.25则探针数×3且依旧不完整；没有任何一次命中即中的性质。
- **B（边界导出候选）正确**，但提案文本“不复刻忽略类型规则、允许超集候选”若**只**做超集边界候选，存在必然假阴性：当唯一可行带被可忽略类型（商人/坐骑/灌木/玩家payable）的排斥区覆盖时，超集把该带切空 → 无候选 → 原生最终验证**救不了候选集本身的稀疏**。这正是用户抱怨的那类“明明有位置却不生成”的换壳重演。5秒重试不解决（集合不变）。

## 必改（就这两条，其余可接受）

1. **加第二层回退扫描（tier-2）**：tier-1 全部被原生否决时，以 0.25 步长、中点向外、总探针 ≤4096（领地更宽则步长=宽/4096）扫 `[left+hw, right−hw]`，每点仍走原生 `OverlapsAnyExclusions`。覆盖：忽略类型邻接、镜像漂移、原生15连停走近似造成的任何 ≥0.25 宽原生可行带。不复刻忽略表（TryCast 四类型的镜像会随游戏更新漂移，且对 5.5 宽的店 completeness 增益趋零）。
2. **闭区间/浮点契约写死**：blocker 导出候选必须内缩 ε=0.01（≈1/3像素，x≈10³ 时 ulp≈1e-4，余量300×）；相触=不可放（“正好店宽”用例期望**不**生成，与原生 ShopPlacer 同语义）；**领地边界不是排斥区间**，保持含端 inclusive（旧 `Find(10,14,2)` 精确贴边可放的行为与测试语义保留）。

## 最小可实施算法（供 operator 定契约）

- `HeroShopPlacement.Find` 保持纯函数、无 Unity 依赖，签名扩为带排斥区间表：`Find(float left, float right, float halfWidth, IReadOnlyList<(float Lo, float Hi)> exclusions, Func<float,bool> fits, out float x)`；旧81循环删除（净删代码）。两条调用点、两套测试同步改，无别名共存。
- 候选构造（tier-1）：区间按 Lo 排序→合并相触/重叠→相邻合并块之间及领地两端共 M+1 条开放缝；每缝产出 `prevHi+hw+ε`、`mid`、`nextLo−hw−ε` 三点，仅当 `lo<hi` 严格成立；另加 `middle`、`left+hw`、`right−hw`。排序键 `( |c−middle| 升序, c 升序 )`，ε 内去重，tier-1 候选上限 1024。
- 区间采集（店侧 Unity 层，每次 Create 重采、不缓存）：AllPayables 遍历（≤min(len,4096)，沿用现行惯例）取 `d>0` 的 `PayableExclusionPoint()±d`，加 `_allBlockers` 的 `GetExclusionPoint()±exclusionDistance`；**逐元素 try/catch 跳过**（销毁对象/读失败只丢一条候选，方向性安全：最终门是原生）；整体失败则空表退化为 middle+端点+tier-2。
- 探针预算：tier-1 ≤1024 + tier-2 ≤4096 次原生调用，5秒节奏一次性开销（对比现状81次，量级可接受，无逐帧工作）。
- 不动清单确认：五秒重试、Playing/Menu/网络/开关门、Native ground anchor、已建店、`_createStage`、两店互斥、支付与枪架全部保持；两处状态文案改“领地暂时没有商店空位”。ModConfig 帮助文案里的“领地中段”超出本轮授权范围，留待后续（不静默扩scope）。

## 新 bug 风险（实现时核对）

- tier-1 缺回退（上述必改1）——唯一致命项。
- 忽略 ε → 原生闭区间把边界候选判触 → 该候选浪费（midpoint 通常兜住，但别依赖）。
- 非确定序（哈希迭代/不稳定排序）→ 测试抖动；必须显式 comparer。
- 区间采集在遍历中对象被销毁 → 逐元素 catch，不许整轮报废。
- 英雄 halfWidth=8（512px/PPU32→16宽）：全领地搜索后它仍是最难放的，状态文案是“暂时”，行为一致。

## 验收用例（映射 plan.md 清单，全部纯函数可测 + 两套既有套件回归）

1. 中段空 → 命中 middle（旧语义保持）。
2. 远端左/右各留唯一缝（>±30）→ 均命中缝内且无原生重叠；非对称领地同测。
3. 旧采样漏缝：可行带宽 0.5（如 x∈(12.05,12.6), hw=2）→ tier-1 边界候选命中（0.75 网格必漏的反例）。
4. 缝宽恰 2*hw → 不生成（相触=冲突）；缝宽 2*hw+0.1 → 生成。
5. 领地恰 2*hw → 仍于中点生成（边界 inclusive 保持，旧 `exact fit` 语义）。
6. 忽略类型邻接：fits 模拟原生（额外拒绝一段未入表的排斥区），tier-1 全否 → tier-2 以 ≤0.25 步长找到。
7. 非法输入 NaN/±∞/hw≤0/宽<2hw → false 且 fits 调用数为 0。
8. 全占满 → false；探针总数有上界断言（tier-1+tier-2 计数确定）。
9. 大领地（如 ±1000）满阻塞 → 有限探针内返回 false。
10. 双 gap 等距 → 确定性选小 x。
11. 回归：hero-shop Core（placement 段按新契约重写，删 `calls==81` 旧钉扎）、musketeer-side-rack 60 断言、actual 2.4 interop 与主线构建 0W0E；operator 并行 interop 核验结论落 receipts。

**一句话**：B 是对的，但“超集候选+原生最终验证”不完整——候选集稀疏是原生验证管不到的；补 tier-2 有界回退扫描并钉死 ε/闭区间/确定性后即可 APPROVE。
