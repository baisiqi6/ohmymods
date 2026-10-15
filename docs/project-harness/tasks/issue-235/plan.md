# Issue #235 — 未发布保存回调观察项撤销

负责：Mac Max / Codex / Android Operator。GitHub https://github.com/baisiqi6/ohmymods/issues/235。
基线6ca5dc24c24eceb2beb5dd7893c65bbacf33d2cc；独立分支codex/native-save-observer-unpublished；复用本会话已完成、clean的独立工作树android-common-save-imports，原导入任务分支与成果保留。

## 范围与责任

主 Operator 已明确交接 Patch_MusketeerNativeSave.cs 中 SaveAsync wrapper 未发布而转换失败的分支及必要生命周期原语、host回归。仅修发布者异常退出责任，撤销本次尚未发布的 observation 与 Managed root；原callback保持，已经发布的其他pending不驱逐。不改正常保存/Harvest/物理writer/角色付款/盾卫玩法，不加时间推测、扫描重试、全局清表或平台第二账本。

PR226仍Draft，库存共享源码由主 Operator 收尾后单向接入；本任务不复制或合并其文件。盾卫#234公共schema/签名另等主线闭合，版本账目与发行不在本窄交接内。仅代码与测试交付，不安装、不写玩家存档、不合并。

## 已确认机制与失败回归

生产BeginSaveObservation先Add入PendingObservations，Prefix随后发布Managed root并转换native Action。转换RHS失败时原ref __0未替换，游戏使用原回调，未发布观察没有完成入口。根因修在该发布者catch；native转换是否在现场失败仍未发生验证。

R私有投影已直接执行真实W生产Prefix：SDK8.0.425、TFMnet8、实际.NET8.0.31；原39用例全绿，新增2例均红，全41仅这2条失败。单次Pending1→2，九次失败后Pending8且下一回调不包装；原callback/原运行选择保持、无同步调用、已发布sibling保留并单次完成已核。独立review通过；fakes仅既有单元测试边界，不冒充native ABI、GC或现场。

## 实施与验证

1. 仅改本任务拥有的生产失败分支；try中的ref __0发布必须是最后可抛业务动作。conversion可能先分配/注册临时Interop桥对象，这与发布给native saver不同；只撤销业务观察，不碰框架注册表。实现条件10的发布边界术语已请求增量复审，未定案前不改生产源。
2. 将R新增回归和默认OFF的test-only转换故障开关整合至原adapter项目；每例finally复位。保留原39测试正文，不用镜像实现或源文本断言。目标红旧/绿新、已发布sibling和原callback/duplicate once保持、9次失败后新callback仍可包装。
3. 跑真实adapter net8套件和net6编译、native-save-pipeline；PC全部源码控制net6/net8纯编译，禁部署；Android172真实引用候选只替换本任务真实源重建，原PrepareWindowObserver未接警告保留，不压制。
4. 独立代码审查首次偏离位置、未发布/已发布界线、锁顺序、引用恒等删除、callback保持、故障开关卫生。worker只改Patch文件和其test slice，Operator接线/文档与最终检查。提交、push、创建关联PR并保持待主线整合，不安装/合并。

## 完成定义与边界

必要代码审查、构建与host回归完成并对应PR合并后才关闭Issue/受管任务；PR未合并则保持开放。原生/AOT/ABI、跨GC、真实callback来源/线程与游戏保存事务尚未验，分别记录，不用此host修复给它们签字。Android#232仍OPEN，仅G0源码前置合并，本任务不代替完整职业批次完成。

正常Coordinate lifecycle登记/审批/接受必须在主 Operator 新鲜写入窗口执行，不能用GitHub认领代替受管lease或裸改checklist/DB；仅允许issue235新增和issue232自有lease续租，其他rows/plans/order不改，validator+doctor回读。原始日志/模型事件/pins留本机R，公开docs只留必要摘要。
