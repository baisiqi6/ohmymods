# Issue #232 — Android 职业身份与弓弩移植

负责：Mac / Codex / Android Operator。GitHub https://github.com/baisiqi6/ohmymods/issues/232。
基线 `b5c2becffbad9a342b4ce214b953a8617475a662`；分支 `codex/android-profession-archery`；已有 Android 0.0.30 保留，沿用 LemonLoader/MelonLoader + Il2CppInterop + Harmony。

## 目标与边界

复用真实职业权益、手动付款、原生保存/恢复和对象池机制移植火铳、重盾、英雄弓箭手与弓弩选项。公共 UI 沿既有小浮球、PC 中文和样式接入。不为编译增加假配置、恒假身份或第二份状态账本；经济/银行/自动采购不顺带启用。根目录 Mono 冻结，Steam/正常 Mac 游戏和无归属改动不动。

本计划分依赖阶段交付，批准范围以每阶段独立审查与实际证据为准。前置源码组已经形成私有候选；仅此前置的可逆源码隔离有实施批准，不能据此预付未来功能/native/安装批准。重要任务通过正常 Coordinate lifecycle 登记，不裸改 canonical checklist/DB。

## 当前真实盘点

- Android 默认产品66源，31项持久设置，SDK10真实引用构建0W0E；现有安装/游戏未改。
- 当前职业/英雄/弓弩/手动producer/六个外部Heavy保存owner的保守静态源集168项，297个真实Android refs（移除两份desktop Bepmetadata）。实际Debug/Release Roslyn诊断均23项CS0117，只有9个真实ModConfig成员缺口。没有加stub或抹掉诊断；静态前沿不等于完整产品闭包、资源池/ABI/运行证明或完成百分比。
- 人口共享消费与中文展示须单独接通：当前Android9类与PC13类不同，不能只补CrossbowmanRole常量。当前选源不含PC PopulationHud，不能据无该诊断宣称此缺口已解决。
- 实际原生metadata审计23个方法名/25个具体方法（含Promote/Drop overload），与当前方法映射一致、无同地址其他method-definition；3个save writer另已核独立地址。未读函数体/未核实际Agent、线程、回调/保存事务；PC证据不代替Android ABI。

## 阶段G0：公共源码边界（本次已冻结实施范围）

1. CoinCourierGround整类原样抽共享源，不改探地/净空/缓存/常量与消费者。
2. CoinCourierPersistence尾部三个只读helper与全部Harmony owner迁同名partial Hooks，保留PC嵌套owner、签名、属性、执行/异常顺序。Android仅保留六个Heavy保存目标及真实state/finalizer：IslandSave、marker、Prefs prepare、campaign create、async delete、d__91.MoveNext；经济字段/通知/取消交易/经济postfix及两个bank-only challenge owner !ANDROID。
3. MusketeerPersistence的六个银行/金币通知仅PC，Heavy/Musketeer原核心与异常收尾只一份，不注册未移植银行门。
4. 五个真正显式链接旧源的测试csproj补必要新源。旧补偿尚有独立迁移/保护责任，本阶段不擅删。

验收：Ground原文、PC所有type/member/attrs/IL/生成类型/resources严格无差异（无允许映射）；真实全树受控PC build、四组现存host测试与旧owner反射检查；默认Android源/引用保持与0W0E；独立代码审查，无功能注册/新pref/版本/安装。私有候选PC1358types/7158methods MATCH，四套54tests/223checks/73checks/15tests已通过。

已发现旧actual-interop独立工程未接SharedBankNative，在未修改基线和候选均同一CS0246，保持失败记录，不补替身；真实完整PC源构建含真实银行并通过。不得将该单独工程记为通过。

## 后续G1：完整职业身份/保存/池基础与真实配置

- 以G0源边界减少文件共置依赖，排除独立MusketeerRestock自动采购partial；保留真实手动Shop付款→原生ToolBow→权益注册producer。
- 沿MobilePlayerConfig真实MelonPreferences创建9个共享消费者需要的entries：MusketeerEnabled/HeavyShieldEnabled/HeroArcherEnabled(bool default false)；CrossbowRecruitmentRatio(float .25，四档.25/.5/.75/1，不能以0伪关闭)；ArcherScatterEnabled/ArcherRateEnabled/ArcherImpactEnabled(bool false)，ArcherVolleyCount(int3，1..3)，ArcherRateMultiplier(float1.5，1..2)。只在真实控制接入时增加，真实加载边界归一化与单次保存，不为全库存错误填设置。
- 完整rights producer/consumer、native Prefs writer与capture/marker/prepare/load/campaign mutation、promotion/drop/ReplaceBy/pool新life与隐藏重开/Tick驱动一起接通；玩法OFF时已有付费身份与保存保护不能消失。
- 共享人口13职业真实分类与Android中文页接入，避免重复身份识别/第二份角色表。
- 编译所选源码不等于注册所有玩法；每组显式注册，auto PatchAll始终禁用。产品边界/真实hooks+native ABI具名证据和必要适配先冻结再过实施门；不把当前仅私有诊断Hooks-main分离许可扩成产品许可。

## 后续G2：弓弩与职业玩法/视觉按真实依赖分组

直接复用既有ArcherOptions/GreekImpact/HeroRuntime/CrossbowLifecycle/Defense、重盾/火铳运行时和手动店源码；相关小功能可合并审查、打包。审完整角色/工具/投射物/技能/死亡撤退FX/召唤/Holder/sync池和InitPools后重注册。火铳自有managed弹丸与visual pool，不能当原生Arrow略过；弩矢真实Arrow克隆池及撞墙归还，原生塔弩不被全局影响。

按真实资源/池责任而非文件数量启用。UI沿现有浮球、PC中文/暗金通用控件做触屏布局，减少遮挡；不等全部功能完成才统一。

## 后续G3：必要验证、独立包与代码收尾

真实Android引用构建、必要host与受控PC语义保持分别留证；静态原生地址、注入组件/具体Hook桥接、保存writer实际Agent/线程/回调/文件事务分别验证。独立Android环境新备份/不并发游戏；分批启用有界实际交互。购买/转职/读档换岛/InitPools重建/攻击技能死亡撤退等没覆盖的如实pending，手机平板/联机/长期稳定不预付。#213模拟器宿主长挂根因独立OPEN。

完成必要审查验证且整个#232范围所有PR合并后关闭代码Issue并正常完成受管任务/消费回执/释放lease；仅G0合并不能提前关闭整批。实机待验不伪造，后续新实机问题另开Issue/PR。

## 跨Operator协调

PR226 draft涉及同一MusketeerPersistence的库存载入/释放hunks，本组通知边界在另一区域。保留它的真实库存producer/生命周期变化，按hunk合并并重新核基线，不做整文件overlay、不擅自合并draft226。主Operator给新的登记写入窗口后操作，仅issue232状态/plan允许变化，其他tasks/plans/order/leases保持，操作后validator与doctor回读；窗口用完合并一次交接归还。

## 完成记录位置

当前进度只同步受影响docs/project-harness/progress.md；G0过程/验证边界写本任务记录；后续根据实际结果修订本plan。详细本机原始证据R=.local/tasks/android-profession-archery-20261014，原错误报告及失败窗口保留，不作为现行通过依据。
