# 举旗火枪后排与白天猎鹿：本机候选

最终DLL：E9D4971E62E48F19BCF3251EC61C2D4D756549883AA0A92FDA93E03FF7607FA1，build=9.0.0-musketeer-banner-deer-20260917，公开版本仍9.0.0。已在游戏关闭时备份BCA6AAEB并安装至既定E独立测试副本，28份存档/附加档/配置hash保持，详见receipts/install.json。未启动游戏，未写存档配置，未提交发布。

## 行为

- 举旗沿PlayerFormation，原4重装步兵+4弓手与0..4船保持；额外最多4名已有火枪手排在后方，不凭空生成。只取职业名册的合格单位，原生TryRecruit/跟随/射击/离队，空位可补员。后排Gap预留，定向入队时暂开一个槽；原弓位保持，普通弓不能占后排。全部数组仍由原FleetBoatFormation单一owner管理，左右原槽坐标保留。
- 收旗走原生释放。关闭功能释放自有后排，不缩仍有人的数组；死亡、失活、池复用、世界/权威变化沿现有维护清理。类型临时写入和native招募同一事务，部分写入、Register后转换失败、数组替换、离队异常保留必要回执；source同life用新进程内单调BindingLease证明。无法完成类型恢复时阻止误招，不误改外来owner/新life。未改持久档。
- 白天只猎普通Deer，原生wildlife scanner先验AND保留。兔/其他小动物/鹿坐骑拒绝，不伤也不挡；发射和命中复核昼夜、即时身份、已装包、活动/生命、编队/骑士/登船、世界及同life。敌方夜战旧分支不加这些鹿专属门。
- 希腊现有鹿y=.55导致水平枪线可能越顶：只对鹿按根自身有效Collider2D.bounds确定一次直线方向，原可水平命中仍水平；不追踪、不扩碰撞体、不移枪口、不改全局缩放。二维距离和真实地面截断正确，最近有效目标/Crusher/32→256→完整List保持。伤害2、射速、射程、原生鹿掉钱不变。
- 沿现有火铳铺所有世界单机开关；没有新增配置或网络支持。鹿发射/提交尝试日志被动、每世界最多8条，预算耗尽入口早退；不是HP扣减证明。无新全场扫描。五帧红焰、0.9大小、白天自动补货及其他已完成修复保持。

## 验证

- 279项：musketeer-runtime 99、formation policy/layout 41、直接生产formation pipeline 22、既有fleet-greek-squads 91、共同combat-damage真实xUnit 26。完整main及两个actual2.4接口构建0警告0错误，禁用自动部署。
- pipeline直接编译两生产文件，替身native入口复现原生倒序选槽和TryRecruit顺序，执行真正Harmony方法体；不是只测复制算法。覆盖0..4船/0..4及超额火枪、左右原坐标、原4+4保护、关闭/暂停/收旗/死亡/重新举旗、partial写入/注册/返回false、dirty窗口、数组替换、失权/场景变化、旧life与离队重试。
- root修正了测试夹具的缺body、初始Formation未disabled、Finalizer返回丢失、静态Plugin声明、2人却要求4满槽和probe缺using System；未通过放宽生产门来迎合测试。root另按review加即时身份/archer.enabled门、日志预算早退与attempted文案；同帧身份释放反例不再预先Runtime.Tick。
- DLL对BCA6AAEB：3627旧方法体保持、24改、75新增、11旧签名移除/替换，均限定本任务范围；8张嵌入PNG全保持。唯一新增Harmony类型为Archer.TryRecruitGuard，实际2.4入口0x4B80B0单method slot、非短getter；原生地址/资源几何见native与geometry-notes。
- OMP两隔离session实际provider=deepseek/model=deepseek-flash/thinking=max，只有read/grep/edit/write。详见receipts/worker.json；无审批配置变更、无shell worker、无Git/安装权限。root统一测试/集成，独立review见deer-review.md及final-review.md。

## 实机边界

没有启动游戏；上述为代码、资源、原生入口和替身回归验证。真实4+4+4站位/射击、鹿两侧近距离命中/逃跑/掉钱、猎后黄昏回防、保存读档/换岛仍待用户游戏验证，不宣称实测完成。

首次测试开启火铳铺后，先收旗再举旗。若读档时旗已举起，原激活入口在非Playing阶段可能没有扩展槽；收旗重举即可在正常Playing入口建后排，未热扩带人队伍。联机仍按现有火枪功能整项关闭。

本机候选目录：C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/musketeer-banner-deer-20260917/candidate。安装只允许既定E独立测试副本，安装记录写receipts/install.json；不替换运行中的DLL，不覆盖用户存档。
