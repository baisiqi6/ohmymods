## 实现复审结论：APPROVE（无阻塞缺陷；1条测试命名瑕疵 + 交付关口未走）

### 代码核对（直接读当前源码 + scope.diff 逐行）

- **加法cell契约落实**（HeroShop.cs:43-117）：区间只产阈值（区间端点±halfWidth，严格落在合法域内才加入），阈值排序后逐相邻对出 中点/`BitIncrement`左邻/`BitDecrement`右邻 三类**内部**候选（严格 `>a && <b` double 判内），被超集区间覆盖的 cell 照常产候选；领地端点 `(float)low/high` 与中点补入；无固定ε、无0.75网格、无±30半径、无81样本、无截断常量。退化cell `b<=a continue`；非法区间在进 sort 前整轮失败；`middle` 快路径先于采集（测试 `collections==0` 钉死）；fits 调用量测试断言 ≤6N+6 且与领地长度无关（−10⁶..10⁶ 对比）。排序键 (距中点, 小x) 全序确定。
- **adapter**（HeroShop.cs:120-165，`#if !HERO_SHOP_CORE_ONLY`）：完整遍历 `AllPayables`（无4096截断）+ 全量 `_allBlockers`；端点先用 **float** 算 `point±distance` 再进 double 展开——恰好对齐原生 `num3/num4` 的 float 运算，消除我上轮指出的 ≤1ulp 边界漂移；非finite/负距离/读失败一律 throw，异常经 `HeroShopPlacementNative.Find` 直传两店 `Update` 既有 catch（HeroShop.cs:348 / MusketeerShop.cs:158）→ `Clear("error")`+10s重试，不以部分结果宣称全占。纯读，无新 Harmony hook（diff 全文确认）。
- **改动面**：scope.diff 仅 HeroShop.cs（placement重写+adapter+调用+文案）、MusketeerShop.cs（调用+文案）、tests/hero-shop/Program.cs。对照 receipts/*.before.cs.txt 逐锚点核对：Update/支付/grounding/枪架/清理全未动。上轮3条非阻塞注记全部落实（非finite过滤+测试、退化cell规则+测试、旧81钉扎删除）。两店文案统一“完整城墙内暂时没有合适的商店空位”。

### 测试与证据

- hero-shop Core **67断言 PASS**（operator-hero-core.log）；musketeer-shop **18 PASS**、side-rack **60 PASS**、wiring **35 PASS**；主线构建 **0W0E**×2（worker+operator）；tests/hero-shop/Interop.csproj 对真实2.4 interop（interop dll SHA 已录 actual-interop-api.md，AllPayables/_allBlockers/PayableExclusionPoint/payablePlacementExclusionDistance/GetExclusionPoint/exclusionDistance/OverlapsAnyExclusions 全在）编译 **0W0E**——InteropStubs.cs 只stub MOD侧类型（ModConfig/HeroRecruitment/Log），不掩盖被验证的native类型，证据成立。
- 覆盖用例齐：远端左右超±30、0.75间窄缝、巨大忽略区间覆盖唯一窄缝（超集含无序/重复/点区间）、sub-0.01可表示缝、闭区间相触拒绝、领地恰店宽、双店先后不重叠、非法输入、大N线性、巨大边界不溢出（旧实现此例会 NaN 失败，新实现通过）。
- harness-validator FAIL 15条为**既有**schema问题，任务未动 harness-checklist.json，如实记录，非本轮缺陷。

### 瑕疵（非阻塞）

1. tests/hero-shop/Program.cs:60 测试名 "exact width still passes native closed-contact gate" 与断言 `!Search(...)`（相触被拒）语义相反，建议改名，不挡交付。
2. scope.diff 不含未跟踪新测试文件（Interop.csproj/InteropStubs.cs），账目上补记一句即可。

### 交付关口（operator 执行）

- 无 install 回执 → 候选**未安装**，符合“游戏运行则保留候选”。最终交付须：确认游戏进程退出 → 既有 install-candidate.ps1 → 核 source-baseline.json 哈希与旧DLL哈希 → 备份替换E盘指定DLL → 存档/配置hash不变；绝不强退游戏或写Steam。本轮构建已含 samurai 候选全部修复（0W0E 整树编译，baseline 三文件钉住）。
- acceptance.md 未建（交付未完成前正常）。

### 现场待验（保持 pending）

实机全领地远端空地生成、窄缝生成、双店共存互斥落位、密集payables下一次性选址耗时、新状态文案显示；连同 samurai 候选的实机验收。
