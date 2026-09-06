**CHANGES_REQUESTED**

审查对象：
- report SHA-256: `29ff365add1aa8dcdde9475756b58a6feae100f12f3f29da3748aded7d231422`（result.md）
- packet SHA-256: `9247c8b919b3d7633baaffbd31c8f17ff4ee5e91ebd98f9d34f597dccdc46ccb`
- plan SHA-256: `eefc016e9f5dcd96a48c704eee7e5e18e866d98c31478b72eada05d31a654b56`（与报告 §2 表末 plan.md 条目一致，交叉成立）

本结论不预先标 task done，不构成 Gate F 或任何游戏版本发布通过证明，也不因尚待 completion 而循环批准。worker 自称"无不一致"未作为证据；以下均基于 packet 内全文的独立复算。

**独立复算通过的部分**（界定返工边界）：startup 时长 15:55:51.5100453→15:56:39.8617666+08:00 = 48.3517213 s，报告 48.351721 s 与 "48.4s" 四舍五入一致；采样点 20 个、全部 responding=true 属实；publication.md 第 4/5/6/7/8/9/10 行定位逐行复核正确；csproj 第 12 行 `<Version>4.5.0</Version>` 正确；receipt/manifest/github-release 的 ZIP/DLL hash、39440865、313、374272、c203218 逐字段一致；UPDATE_LOG 第 4 行、CAPABILITIES 第 3 行、USER_GUIDE 第 5 行定位正确；脚本 leak_check 排除词、doorstop 豁免、config 只放行 BepInEx.cfg、无 CRC/白名单校验逻辑均与源码相符；已核验/记载未复验标注纪律整体成立，无把材料自述当本轮实测的越界。验收标准 1–5 的结构覆盖完整。

**P0**：无。

**P1**（阻断批准的字段级事实错误，最小修复后即可进入后续 completion 步骤）：

1. §3.8 时间线："tag 对象 07:57:15+08:00" 与 §3.1 "tagger 时间 2026-09-06T15:57:15+08:00" 互相矛盾。按 §3.8 字面归一，07:57:15+08:00 = 2026-09-05T23:57:15Z，早于 manifest GeneratedUtc 07:55:01Z，所印序列非单调，该节 "[已核验，内部一致]" 结论不被自己列出的数字支持。15:57:15+08:00 = 07:57:15Z 与全链（startup 止 07:56:39Z → asset createdAt 07:57:23Z → publishedAt 07:57:30Z → receipt commit 07:59:43Z）严丝合缝，§3.8 系时区换算笔误。最小修复：重读 tag 对象确认后，§3.8 改为 "tag 对象 07:57:15Z（= 15:57:15+08:00）"，全序列统一用 Z 表述。

**P2**（精度缺陷，同轮一并修）：

1. §3.9 "四份当前文档顶部均带 4.5.0 标识与 2026-09-06 日期"：日期仅存在于 UPDATE_AND_FIX_LOG（第 4 行）与 CAPABILITIES（第 3 行）；MOD_V4.5.0版本更新说明.txt 与 USER_GUIDE 顶部无日期。修复：版本标识 4/4、日期 2/4 并点名文件。
2. §3.9 "共用同一套'新说明优先于后文历史数值'层级约定"：该明示约定仅 USER_GUIDE、CAPABILITIES 承载；UPDATE_LOG 以 "以下为历史版本记录：" 分隔实现同义分层；V4.5.0 说明无历史区段、约定不适用。修复：按实际承载方式分述。
3. §3.9 "安装指引…在四份当前文档间一致"：CAPABILITIES 无安装指引章节；相同安装文本实见于 3 份（V4.5.0 说明/USER_GUIDE/UPDATE_LOG）。修复：改为三份，并注明 CAPABILITIES 仅在观察项中含联机同版本要求。
4. §4 "个人 KingdomEnhancedMod.cfg 与其它 .cfg 不可能入包"：超出源码可静态证明范围——按名过滤 .cfg 只在 BepInEx/config 分支存在；leak_check 不排除 core/unity-libs/dotnet 下的杂散 .cfg（报告下一条已自认黑名单非白名单）。修复：限定为 "config 目录下除 BepInEx.cfg 外不入包"。
5. §3.1 字段定位 "plan.md 第 3 行"：release-450 plan.md 第 3 行只提 "a new v4.5.0 tag"，不含 commit c203218；tag→commit 绑定的实际定位是审计 plan 问题与目标段（第 9 行）。修复：更正定位或从该处定位清单删除 plan.md。

以上均不改变审计实质结论（材料交叉一致、无实物项未冒称重验、游戏边界保留）。修订后按验收标准 6 生成新 review packet 并重新绑定 review SHA。


Reviewer provenance: Windows OMP 17.3.4, kimi-code/k3, native session 01a077db-df47-7000-ab5c-dccd1d8ac6ef. Read-only review of the supplied complete source texts and frozen packet.
