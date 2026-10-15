# Issue #235 — 未发布保存观察撤销

## 问题与修复

`Musketeer_SaveAsyncObserver.Prefix` 在转换托管回调之前，已把 `SaveObservation` 加入有界待完成集合。转换异常时，原回调仍执行，但该观察没有完成入口，旧实现仍保留它及 `Managed` 根；反复失败可以占满八个槽位。

异常路径现在在原 `Sync` 锁内清除本次未发布观察的 `Managed` 并按实例引用移除该观察，随后记录异常。`__0` 赋值是 try 最后一条语句：右侧转换失败时原回调引用保持，成功发布之后不进入本撤销路径。合法已发布观察、原 callback、原保存执行和 once 语义保持。没有新增扫描、重试、超时或第二套账本；闭包仍为一个，撤销局部变量未被捕获。

这是框架转换故障的可复现机制修复，不宣称当前设备已发生同一现场故障。Interop 内部注册/分配不等于向 saver 发布，不在本修复中改其注册表或证明其 GC 回收。

## 已执行验证

| 层级 | 结果 | 限制 |
|---|---|---|
| 旧源红回归 | 原 39 用例通过；新增两用例失败。单次 pending 1→2；九次开启故障开关的调用留下 8 个观察，后续正常回调不再包装 | 第九次旧源调用在容量门提前返回，最多八次实际转换；host 故障注入 |
| 修后保存 adapter | 41/41 通过，实际 SDK 8.0.425、runtime 8.0.31 | 使用已有依赖 fixture，只链接真实生产实现 |
| 保存 pipeline | 33 个断言通过 | host producer 集成，不代表原生事务 |
| adapter net6 | 编译零错误；保留 NETSDK1138 生命周期警告 | 未以 runtime roll-forward 冒充 net6 运行 |
| PC 全源 | SDK 10.0.401 控制构建 net6/net8 各零警告、零错误，252 编译输入 | 输出仅任务私有目录，禁用部署路径 |
| Android 私有职业候选 | 172 源/184 真实引用/21 资源；Debug/Release 各零错误、一个原有 CS0649 | 唯一替换输入为本次共享保存源；`PrepareWindowObserver` 后续仍需接线，未启用功能 |

实际源 SHA256：`bacfd259dc37089ad8abe86cd426c9da8e64be38c6269db661c5700593d0c388`。独立代码及文档审查 approved、无 must-fix；GLM 5.3/max 交付决策裁决 `APPROVE_SOURCE_PR_DELIVERY_ONLY`，仅允许提交、push 和 PR，不合并/安装；全部原始命令、日志、pins 和模型事件保留于本机 `.local/tasks/android-profession-archery-20261014/`，重点为 `observer-fix-candidate/`、`observer-fix-final-review/` 与 `observer-fix-build-verification.json`。

## 状态与职责

从 `6ca5dc24c24eceb2beb5dd7893c65bbacf33d2cc` 建立独立任务分支 `codex/native-save-observer-unpublished`。主 Operator 已通过正常入口登记、批准原计划并接受 #235；计划 SHA256 `32e36920e59dbe9ab25bd205ec55d0f3c330f168be11d3ac2ea42439909106e8` 保持不变。本分支提交修复及永久回归测试供主线审查整合，不安装、不合并；PR 未合并前 Issue 和受管任务保持进行中。

不改变 PR226 的库存/释放保存职责、#234 的盾卫玩法与 schema，未启动游戏或操作真机。原生/AOT/ABI、跨 GC、真实 callback 来源/线程、游戏保存事务与 Windows/Android 玩家实机待验。#232 职业身份/弓弩尚未完成，本修复不代替 Android 新功能注册、对象池、保存和玩法验收；正常安装仍保留 Android 0.0.30。
