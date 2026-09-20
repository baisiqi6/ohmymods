# worker-brief：v9.5.12 发布文档六件套（2026-09-20，用户已授权发布）

角色：worker（可写，范围严格受限）。cwd：`C:/Users/ADMIN/projects/ohmymods`。
必读：VERSIONING.md（§七账目定稿）、release/MOD_V9.4.5版本更新说明.txt（结构参照）、四任务回执（tasks/hero-arrow-pierce-20260917/worker-result.md、tasks/musketeer-shop-claimlock-20260918/worker-result.md、tasks/musketeer-banner-rowgap-20260918/worker-result.md、tasks/identity-restore-hardening-20260920/worker-result.md 的改动清单节）。

## 本批五条目（对外措辞，数值以此为准）
1. 英雄弓箭手箭矢无视城墙碰撞（穿墙攻击墙外目标）[仅单机，随英雄开关]——金箭直飞穿墙，池复用归还碰撞。
2. 英雄金箭显示缩小至 65%（纯显示，碰撞/弹道不变）。
3. 火铳铺：平民认领枪支期间可继续购买（架上最多 3 把含认领中；敌人抢枪仍锁店保护）。
4. 举旗编队：火枪手行紧接弓箭手后排（任意船数恒 1 名弓手间距；不满员队列收紧）。
5. 骑士/火枪手身份恢复可靠性加固：附加档读取失败自动回退备份并告警；写入遇文件锁自动重试；骑士身份"吸收态"自愈（一次性失配不再永久失效，严格证据门防串人）。
版本账：9.4.5 + 0.1.0 + 0.0.7 = **9.5.12**。

## 六份交付（allowlist 内）
1. `release/MOD_UPDATE_AND_FIX_LOG_ZH.txt`：顶部新增 9.5.12 段（五条目+合计表述），历史段保留。
2. `release/MOD_V9.5.12版本更新说明.txt`：新建，参照 V9.4.5 版结构；**待实测清单第一条必须是"身份自愈在真实旧档上的效果待验；首次进入旧存档前请先备份存档目录"**，其余待测：穿墙实际命中与归还、金箭65%观感、认领窗口连买/敌人抢枪、举旗行距目测/多船/收旗还原、联机同版本；明确四项均未实机验收。
3. `release-notes-il2cpp.md`：头部与汇总改 9.5.12、四组更新（穿墙+缩箭 / 商店认领锁 / 举旗行距 / 身份可靠性），包名 KingdomEnhancedMod_v9.5.12_IL2CPP.zip。
4. `release/MOD_CAPABILITIES_AND_ROADMAP_ZH.txt`：头部版本 9.5.12；英雄段补穿墙+65%；**限制节身份条目改写为"已加固（读回退/写重试/吸收态自愈），真实旧档效果待验；首次进旧档前建议备份"**；跨岛/联机边界保留。
5. `release/MOD_USER_GUIDE_ZH.txt`：头部版本；英雄段补一句穿墙行为；默认值确认不变（本批无新配置项）。
6. `release/MOD_FEATURES_OVERVIEW_ZH.txt`：头部 9.5.12；268 行附近安装自检日志示例改 `Loading [KingdomEnhancedMod 9.5.12]`；速览与英雄图鉴补穿墙+65%；修复要点节补三条（认领锁/行距/身份加固含备份提醒）；安装节版本号同步。

## 铁律
- 数值/行为只以上述条目与四回执为准，不引入未经审查的新承诺；Mac 未验证不得暗示可用。
- 完成后对 OVERVIEW 重跑泄漏 grep（候选关键词 65%/穿墙——注意本批"穿墙/65%"是**已发布功能**可以出现；内部痕迹 docs//tasks//VERSIONING/il2cpp/ 仍零容忍）。
- 不改其他文件、不 commit/push。

## 交付
docs/project-harness/tasks/release-9512-20260920/worker-result.md：六文件改动摘要+泄漏检查结果。
