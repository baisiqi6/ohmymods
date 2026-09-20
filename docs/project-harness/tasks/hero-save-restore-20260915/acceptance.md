# 本机候选交付

2026-09-15 英雄购买读档恢复候选已闭游戏备份安装正确E盘：ad57eb75 / build=8.0.0-hero-save-restore-20260915。根因确认：旧scope使用IslandSaveData.realStartDateTime.Ticks，但该字段未进入原生存档，每次读档重建，已付记录存在却被新scope漏取。英雄附加档升schema2：稳定文件/战役/挑战/land上下文与opaque epoch，legacy来源逐快照保留，精确回退搜索未归属旧scope，v2已确认空记录优先于legacy付费，冲突/身份不明锁槽不收费。新指纹仅排除三个顶层游玩计时字段，完整人物/钱包/建筑和其他字段仍参与；原生异步落盘的时钟漂移是旧最新快照不匹配的候选原因，未称唯一确因。当前旧v1无精确匹配，因此另用冻结原生SHA/精确岛JSON、实际购买与保存日志、唯一NPC记录作一次本机MOD侧修复：保留93c12776与9265e66e两笔原购买及全部旧快照，未改原生进度/生命值/金币。243购买/回退/日期变化/真实fixture绑定回归、9真实岛指纹检查、完整实际2.4构建0警告0错误，独立复核通过。DLL审计2904旧方法保持、27改变、98新增、42签名或闭包替换移除，修改限英雄持久化与build文字，Harmony类型和4PNG保持。备份与安装摘要核对完成，原生存档及其他配置保持；未启动游戏/提交/发布，公开8.0.0不变。实机下次读档找回两英雄仍待验证，跨岛运输仍待，旧透明遮挡/邻居跳动不因此宣称修好。

原版重存造成完整人物数据变化且无精确匹配时保留名额并暂停收费，不按临时ID猜身份；最多64上下文、8epoch/上下文，满额保留历史并拒绝扩张。同指纹不同付费数据拒绝覆盖并保留当前运行身份，进入只读；真实购买会改变原生Wallet，当前未观察该冲突。回归中的角色使用可控stub，实际2.4 DLL编译与真实保存fixture不能代替游戏实测。骑士也使用旧运行期scope helper，此次未扩改骑士身份，留后续专门调查。

独立reviewer hero_store_flags_review最终通过：迁移回退、权威空记录、来源不洗白、单context归属、原子提交与容量、窄指纹、一次性恢复和DLL审计。Operator修正四处测试夹具/期望错误；新增真实当前恢复绑定/日期与时钟变化、同指纹冲突拒绝写入回归。恢复stager补最后saved日志前缀与来源快照一致、空alias baseline一致断言；安装前后重新解压原生JSON并核对原文和SHA。

DLL备份：E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll.before-hero-save-restore-20260915-215829-856.bak

原附加档备份：E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\config\KingdomEnhancedMod\ModSave\hero-identities.v1.json.before-hero-save-restore-20260915-215829-856.bak

安装过程：首次PowerShell空backup参数被拒，未修改sidecar；显式唯一备份路径续接，写前后证据全核通过。
