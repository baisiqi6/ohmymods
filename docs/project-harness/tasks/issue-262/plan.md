# Issue262 Android 经济、银行助手与九类自动补货

> Agent provenance: Mac Max / Codex · Android Operator

- GitHub: https://github.com/baisiqi6/ohmymods/issues/262
- Base: release/v9.5.13 / 02758672703a335974e5e2ac16a711098215beeb（android/il2cpp/tests树与906相同）
- Branch: codex/android-economy-262
- Owner: mac-codex-ohmymods-android-operator
- Session: codex-android-economy-262-20261018

## 目标与边界

沿用原Lemon/Melon Android架构，复用PC共享驱动、银行账本及真实原生入口，完成经济核心/8助手与9类自动补货的源码、平台配置和触屏接线。#232的具名职业保存/池必要运行验证继续单列，不以它们未验为由重复实现源码，也不冒填通过。未合并Windows2.4.3 PR250不作为本批输入。

## 实现契约

1. 按已核17份共享源闭包接入，注入BankAssistantCoordinator与BankAssistantVisualLifecycle。唯一AutoRestock.Tick仍由原Coordinator.Update驱动；沿用TickOwnedProfiles、TickPendingCleanup和CoinCourierTeleportFx的既有责任，不新增dispatcher或池重试层。
2. MobilePlayerConfig新增18个真实AutoRestock* CLR字段及相应MelonPreferences flat key，默认9bool=false/9target=15，载入边界1..200；原40键及legacy cfg保持。MusketeersEnabled与原MusketeerEnabled独立。一次实现9类，运行按基础3职业→其余弹药/火铳分组。
3. 唯一显式owner表扩为(Type,expectedReplacements)，原106顺序与7个动态TargetMethod owner全部保持。新25owner中Banker期望9、其余24期望1；其33个owner-target关联只含32不同方法（Banker.Update重复）。再接入现有ChallengeDeletePatch/ChallengeDeleteRoutinePatch两owner，共133owner/141关联，不断言全局唯一方法数，不复制属性或拆共享源凑1。
4. 当前CoinCourierPersistence.Hooks中只放开SharedBankNative银行调用及其状态字段：IslandSave Begin/Postfix/Finalizer/Marker、campaign create/delete factory/delete routine，以及challenge delete两类；保留CoinCourier自身Observe/End/CancelPendingShopTransactions等PC guard。Postfix与finalizer须覆盖银行成功及异常清理，Hero/Musketeer等既有相同目标和priority保持。优先共享已有owner，不新建重复Android保存patch。具体共享文件与PR250版本alias改动交接后落地；PC预处理输出必须等值。
5. 完整8助手接入PC现有4atlas及exact LogicalName，保留固定30120..30127 sync池、903 Banker实体与authority作用域。助手非Character，不添加Holder/EnsurePoolForCharacter；复用原CreatePoolFor与sync注册，核InitPools重建，避免额外重复池机制。
6. UI Worker只新增android/Patch_EconomyAutoRestockMenu.cs等已定presentation文件，沿用PC中文/原公共样式与Toggle/Step/Info/PanelGesture；小浮球与遮挡约束保持。Root统一配置、项目、owner表、类型注入、Tick与World子页路由；后续功能复用该公共接线，不做逐键全交付。

## 必要验证与交付

- 实际Android net6候选、源码/真实ReferencePath/资源与产物绑定；必要host配置/导航/动作save-once检查，兼容原40键及非整档值读取不改写。
- 改动前后的PC预处理语义比较与受影响PC net6/net8禁部署构建，相关保存/配置回归；独立源码审查。私有net10闭包诊断仅作编译证据。
- enable前针对具体新增Android target核实当前libil2cpp method pointer/短方法折叠，不由托管名字或聚合map family=1推detour安全；不造通用全ABI扫描门。
- 必要运行按3基础职业→弹药/火铳/全部助手→原生保存/读档/换岛与池重建合并设计，分别记结果。所有测试使用独立/临时测试存储，保护原成果与存档，Mac/Windows正式目录不写；不并行game/VM，资源窗协调。
- 代码范围、必要审查与验证和所有对应PR交齐合并后关Issue。手机/平板、多人、长期自然玩法及完整观感后验单列，不作为唯一关单理由；未验数据不伪造成功。正式发布和版本账目另按VERSIONING。

## 当前证据状态

闭包Worker与独立Reviewer已核真实17源/18配置形状、2组件、25owner/39handler、32不同target与4资源缺口；Reviewer指出的数量、银行保存全站点与challenge删除、动态owner保留等已纳入上方契约。当前正式产品工程尚未实施本批，未装机/未验native交易、detour、池或保存；任务不能据私有诊断标完成。过程与hash/回执留当前任务，公共进展只写实际边界。
