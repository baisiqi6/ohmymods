# Issue #251：坐骑岛首次登岛保存失败

## 目标与基线

玩家确认首次乘船到该岛，正常游戏停在保存失败弹窗。先修复已证明的火枪 placeholder 身份错绑，保留所有保存保护、旧岛身份和已授权补偿；不以诊断复现替代原生验收。代码基线 release/v9.5.13@1f9c6753ef2a87ba994d38c96dc49b111927551c，分支 codex/mount-island-save-251。正常版为私有累计 DLL5fb9c5f8…9352，不能用public DLL覆盖其宫廷/盾卫/地图功能。

## 本阶段实施范围

1. owning campaign 内 exact island 唯一 pointer→slot，live/gen/load共用；非0land分歧、表外、重复与owner不符拒绝。
2. 成功generation后的有界session witness允许同岛默认land0继续购买；file/owner/island/world/slot切换与exact reload撤销，不周期写回native字段。
3. 保留显式Save旧岛来源；-1沿冻结event owner内真实保存对象的slot解析，不能按默认land0建key。旧archive格式、epoch、付费GUID、NativeSave gate和共享Heavy/Bank/Courier wiring不变。
4. canonical12槽fixture，先红后绿；原0+新11分别paid枪兵、first capture/真实gate/模拟coldload返岛，owner/receipt失效与共享wiring异常nested回归。
5. 零购买无GetID及paid漏ID两路真实试验，保留failclosed。无native生命周期证据时不添加target fallback，不宣称该路径已解封。
6. 盾卫同源风险独立Issue #252，本轮不改其实际产品逻辑。

## 验收与交付

- 必要代码审查、真实生产直连回归、准确当前2.4引用下net6/net8完整私有累计编译及输入范围审计分别留证。
- 本阶段只候选与DraftPR；不启动/操作/退出用户游戏、不写正常DLL/配置/存档、不恢复/回滚、不merge/install。
- 后续隔离原生验证需先协调当前运行窗口，明确未保存航行/登岛进度边界；不得与正常版并行。
- 首存是否真实GetID、缺岛11快照冷进入、实际付款保存退出重进是未完成原生边界，不凭managed通过宣称已修。
- 未交齐/未合并不done；正常Git交付可在必要审查验证后完成，代码、安装、原生状态分开记录。

## 状态

GitHub #251开放；用户已确认首次到访。准确存档/ModSave/prefs/config已只读保全，当前主档可解析，不判整档损坏。最小managed机制复现已完成，候选worker正在实现，原生/恢复待后续独立决策。私有完整源码与存档不上传。
