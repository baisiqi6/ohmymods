# Issue205 — BepInEx.Android 独立对照评估

用户授权：保留现有 Android 移植，独立测试是否跑通同APK、实际减少多少适配代码，再决定是否切换。不是恢复Issue196，不自动切换，不触碰既有AVD/真实设备/PC/Mono/源APK。

1. 当前输入与既有成果冻结；读取候选版本、启动模型和历史原始失败证据。进行中。
2. 契约独审与GLM三问，公开Issue对齐与normal Task注册；新AVD具名、端口5584、新userdata无旧存档。
3. Worker私有最小BasePlugin/longhook/component/config例证与适配差异盘点；Root检查产物。
4. 精确安装关口后新AVD验证原版启动基线、NextBep候选原路线；如失败先区分Activity/Java/runtime/hook，源码修复有实际证据才做。
5. 若候选启动路线阻塞，区分BepInEx核心与Launcher；同进程bootstrap仅有输入与可构建证据时做有限对照，不拼造API或复制已运行payload冒充Bep。
6. 冷启与进入game、实际hook调用、组件Update/OnGUI、配置保存读回；没有执行就pending。长/短方法地址风险按原生证据核查。
7. 统计真正可删除/替换/仍需保留的适配代码，行数与功能依赖分开，记录源基线与方法，独审结论给切换建议，normal代码/报告交付收尾。

每窗有界、记录原始日志截图及停止证据；实验只在新AVD，save/config属于全新环境但不无意义覆写。计划不包含玩家真机或联机验收，也不承诺框架统一消除游戏版本差异。

公开Issue：https://github.com/baisiqi6/ohmymods/issues/205。新worktreeHEAD9ea703f8（包含已合并198身份修复），旧Android基线3f2cb37+15冻结owned源分开统计，不将PC改动算loader省代码。Launcher最新commit仅README变动，代码与上次b480无差异；latestartifact11466306634仍待完整下载/CRC。

## 独审精化
所有adb显式-s emulator-5584，启动前核AVD绝对路径/端口与运行serial；stop覆盖com.rawfury.kingdom2crowns与com.bepinex.android及:game全部PID。原版最长12min窗口只对应原游戏已知联网初始化延时，从am start起至force-stop；候选单窗180s从Bootstrap launch起，不自动重试。Hook实际调用、组件Update/OnGUI、配置非默认Save后完整stop冷启读回各独立证据，没有到达阶段是unknown而非该阶段FAIL。LOC每源hash/精确函数范围/去重，不计测试生成docs，实可省要求当前runtime等价且无消费者；仅API/编译的是候选，触屏/游戏版本/保存仍保留。

已下载candidateAPK6222c4e81afceb2e7e2da909afa9c6b0d102cde44f01e30ad357507f2cc09ff1/138624368B，embeddedBep/runtime/nativefusion/libmain/classes.dex逐项与上次官方artifact相同。因latest仅README改动，不宣称更新修复。原始ZIPCRC有效与来源artifact11466306634锁定。
