# 整合候选独立复核：有界通过

2026-09-17。只读 canonical 生产代码、测试链接和现有证据；未启动游戏或安装。鹿精确 slice 复核见 `deer-review.md`。

**最终结论：PASS，无剩余已知阻断。** 实际重算 `il2cpp/bin/Debug/KingdomEnhancedMod.dll` 与 operator 绝对路径 `C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/musketeer-banner-deer-20260917/candidate/KingdomEnhancedMod.dll`，两者 SHA256 均为 **`E9D4971E62E48F19BCF3251EC61C2D4D756549883AA0A92FDA93E03FF7607FA1`**。此结论绑定该精确交付副本。

## 最后一项回执问题已闭环

最终 owner SHA256 `9bda1a1bc8165811f2efeea76ed07b8c18265beb33d3c2746264950ea59eabd7`。原数组替换后直接离队失败责任丢失的问题已修：回执保留 SeatlessActor 与 lease，RetryPendingMusketeerRollbacks 完成槽位步骤后仍处理 seatless 步骤（第 728–748 行），不会因空槽误删责任。

第 849–870 行重试检查原 formation/world/authority、同 lease、仍未重新入座且 currentFormation 仍归本 formation；调用后回读绑定，失败保持责任。已离队、foreign 或新 life 不再调用 OnLeave。首次直接离队第 1019–1035 行亦核同 lease 并记录失败。原生 2.1 OnLeaveFormation 先清 currentFormation 后执行 ConvertToHunter；当前重试以实际绑定判断，避免将后段异常一概解释为未离队。

新增生产接线反例 `PipelineTests.cs:372–425` 覆盖数组替换 + 首次 OnLeave 在清绑定前抛异常 → 后续维护归还；另一例在失败后同 GO 重武装为新 lease，确认不调用新 life 的 OnLeave。此两例与原有用例合计 22 项通过。

## 已核通过的修订

- 原生 TryRecruit 已置回 temporary types 和定向 bypass 的同一个 try 内；首个临时写入起即受 finally 保护，部分写入/恢复失败有回执。
- dirty types 回执存在或定向窗口打开时，其他普通 Archer 招募被拦截；精确定向 pair 放行。feature-off 不解除 dirty 保护。普通弓不会因恢复失败进入额外槽。
- 半完成招募无 currentFormation 时，清理需 captured BindingLease；新 life 只移除旧数组引用，不调用其 OnLeave。鹿与编队使用同一生产 Runtime lease helper，成功 Apply/既有 OnEnable 更新、归还清零、生产计数不重置。
- 关闭功能在现维护边界使用原生 Unregister，不热缩仍有成员的队伍；原 4 弓/4 重步和船的数组 owner 保持唯一。prepend Gap 与 startOffset 补偿保留原槽坐标，两侧和 0–4 船均有测试。
- e2e csproj 直接编译两个生产 class；Harness 反射调用真实 Harmony prefix/postfix/finalizer，Archer stub 执行 guard 与从尾部匹配的 RegisterUnit。不是复制布局算法冒充 owner 接线。Runtime/Identity/FleetGreekSquads 仍是协作 stub；因此不等于全原生游戏执行。
- 实际 2.4 新 Archer.TryRecruit hook 入口先前独立映射：RVA `0x4b80b0`，单地址槽，至下一入口跨度 560 字节。RegisterUnit / Recruit / Player.ActivateFormation 的只读证据亦保存在 native 目录。不新增短 getter hook；跨度不冒称精确函数体长度或实机 hook 验证。

## 冻结证据及范围

已逐项读取最终日志：runtime 99/0、formation-policy 41/0、formation-pipeline 22/0、fleet 91/0、damage 26/0，共 **279 项通过**。两套 interop 和完整实际 2.4 build 均为 **0 warning / 0 error**；SignatureProbe 的 using 缺失已修，最终 formation-interop 日志通过。此处为复核 root 执行日志，未声称 reviewer 再次运行测试。

独立重算构建 DLL hash 与 audit 一致；source-delta.json 的 25 项逐一重算无漂移。已读 Cecil 审计：相对已装 BCA6AAEB，3627 方法保持、24 改变、75 新增、11 移除；变化集中于本轮模块、必要 Visuals 目标判定和帮助/build 文案，移除项为替换签名及编译器闭包。唯一新 Harmony type 为已核长入口的 ArcherTryRecruitGuard，无旧 hook 删除。8 项 PNG 资源全部保持。

真实举旗两侧站位、收旗/重载、逃跑鹿命中、原生掉币和游戏帧耗时均未实机验证；本审查不会将模拟用例或无编译错误等同于玩法验收。不覆盖历史未审存档或此前被排除的英雄/弩手诊断，也不构成公开发布动作。

已只读核本 task 的 install-candidate.ps1：目标为既定 E 盘独立副本；安装前及原子替换前检查游戏关闭；校验 candidate E9D4971E 与 previous BCA6AAEB，先备份并验证旧 DLL，再验证临时副本并 File.Replace，安装后核 DLL hash；前后计算存档/配置目录文件清单与 hash，脚本不写这些文件、不启动游戏。candidate 更新说明保留实机未验边界。reviewer 未执行安装脚本，实际安装结果由 root 另行记录。
