# Issue 213：Android 模拟器有界对照与基线恢复

2026-10-11。基线 `e074a7cb3ad0e23f7a277c6ea17d96bd36ad577b`；此前防御工匠 #212 的代码、审查、PR 与 normal completion 已完成。本任务不修改 Mod、loader 或原游戏源码，也没有新增游戏资源发布。`plan.md` 是正常注册时冻结的草案文本，4302B / `47003af3…`；实际注册 accepted、owner/session 及阶段回执保存在本机任务证据中。

## 本轮结果与边界

| 条件 | 实际结果 | 可接受的结论 |
|---|---|---|
| API35 / arm64 / host / MoltenVK，noMod 第一窗 | 174.129493s 停至零 game PID；真实 0 Mods、0 Plugins、Main/state 缺失、empty revision；CFG/native 保持 | 本窗口未复现历史宿主挂死 |
| 同一 host，noMod 第二窗 | 174.136740s 停至零 PID，控制及数据门保持；森林游戏画面可见 | 两个有限窗口通过，不代表长期稳定或 Mod 无因 |
| 同 AVD/数据/APK，SwiftShader / ANGLE，noMod 第一窗 | 174.212968s 机械停止/读回通过；实际人物、地面、森林变黑剪影；月、水、灯部分可见 | **渲染失败**，不能以机械 `limitedClean=true` 冒充视觉验收 |
| SwiftShader 后续窗口与 Mod 臂 | 未执行；本相位 gate 已退役 | 第一窗失败后停止此相位 |
| 原 host/MoltenVK 与冻结 Mod 包恢复 | 一次实际 boot、一次保数据 install-r；1 Mod / 0.0.28 / 51 exact targets / Main、revision、refresh state 恢复；11.519504s 停至零 PID | 原架构和包加载恢复；截图仍 logo、日志仅 preloader，未声明森林画面、全 UI 或长期稳定 |

历史 QEMU mainloop 与多 CPU stall / IPS 两例的原因仍未确定；两次 noMod host 窗口没有复现，不能改称历史判断已排除 Mod 或已定位 GPU。宿主日志两条 ColorBuffer/texture ERROR 保留，不宣称 host 零 ERROR，也不把它们自动归为同 hang。软件后端黑图可在未加载本 Mod 时出现，但不能据此认定最早黑块全部同因，也没有排除 loader。

SwiftShader 实际启动日志包含 Vulkan 1.3 / SwiftShader / ANGLE；当前 PID3064 的限定 logcat 有 `EGL_BAD_CONFIG: no ES3.2 support`、`EGL_BAD_ATTRIBUTE`、rendernode 诊断。这些是观察事实，尚未证明黑图根因；本轮没有 shader 修复或原游戏资源改造。恢复验证实际图形记录为 Vulkan / Apple M1 Max。

## 对照控制与数据保护

noMod 仅通过原 producer 省略 deployment 与 Main exact policy，由现有 native deployment 实际退休 Main。不是 feature OFF、手写 Loader.disable、移动或推送 DLL。首窗真实 native removed 1，Main 与 deployment state 物理缺失、empty revision、真实 0 Mods / 0 Plugins 三门成立。

私有 noMod APK `a1b9d81de12e8e05ee1bbf3b7b9423f10b08d72eab8aa5290f6af6a636f2db2b`（1908800441B）：CRC 22686 项有效，原包 22313 项 byte-equal；原 manifest 不变，同包名 / versionCode 23485 / 实际证书 `bc5de503…`。empty descriptor / revision 现算核验、loader/runtime 身份保持；123 interop 仅各 16B GUID heap 不同，非 native binary 全等声明。

最终恢复冻结 APK `6c792318cea2fa8ebe5c0c4a63f8c148dd93e9a347eda391e907b591dabe43e7`；Main `3aa7bc6256fcb9d82a6ba3ec4fec02672cb99a5a70126a1ecf4ff99d6df4264e` / 0.0.28。25 项 CFG `de8c441a…`（Wall / Role false、旧24项保持）；selected native `7ba5f975…`。各阶段启动前真实 fullRelease 85504B 备份、SHA `77c18e3d…`，最终 CFG/native 字节保持。APK 仅本机、没有部署 PC/Mono/正式 Steam 或手机。自然工匠 HP/jobs/修复归还、读档池/跨岛、手机与联机验收继续待验。

## 工具与失败记录

原执行 observer 保留冻结哈希 `9fd6888c…`。共同采样、noMod 控制、175/180s 停机和读回只抽一次，AST 核验除 directory/gate/installReceipt 三处参数化外主体相同；软件入口没有复制算法。

两次不同 owned QEMU 停止（50030、55482）后，ADB transport 已断开而列表条目尚未清除，原脚本在任何下一 boot 前安全停止。保存原失败/源码/回执；实际稍后 devices 空、旧 PID 消失，独立脚本审查及同一实际 GLM/read 门确认后，只沿 exclusive continuation 完成尚未尝试的下一 boot，不默认重跑、重演 stop、kill 公共 adb server、wipe/snapshot 或数据复制。native wait-for-disconnect 事件不等于列表条目即时消失；不据此增加新游戏兜底。

每个实际执行关口均有独立当前 source pins 审查，以及 GLM 5.3 / max / noFallback 的 provider-native 记录。审查误读 token 拼写曾拒绝执行，经同 session 实际只读字面量明确撤回；未虚构代码修复、未跳过最终批准。软件机械通过与视觉失败两个事实原样保留；后续人工 force-stop 在自动窗已结束约5秒之后，不冒充提前 abort。

本机详细证据根：`.local/tasks/android-emulator-stability-20261011/`；阶段 `no-mod-package/`、`host-comparison/`、`software-comparison/`、`restore-original/`。独立 runtime reviews、原始 PNG/log/IPS、source/gate manifests 与真实 receipts 分开保存；迟报 IPS 和未观察到的长期情况仍未知。

## 状态

Issue #213 保持 OPEN，未完成“历史宿主挂死根因定位/可靠环境修复”，不因为有限窗口通过或本文交付而关单。后续移植保持原架构；本轮没有切换 BepInEx.Android，也没有用渲染失败的软件后端做新功能验收。
