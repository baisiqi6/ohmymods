# #248：2.4.3 保存协程编译绑定阶段

## 变更与适用范围

2.4.3 的 `GlobalSaveData` 生成类型编号已变化，原源码直接引用旧编号，正确 IL2CPP 宏下产生 19 条编译错误。四个集成文件改用文件内类型别名，保留现有 typed `MoveNext` 钩子、state 读取、失败返回与异常处理；没有改挂 coroutine factory、增加运行时反射或兜底。文件内按需别名避免破坏显式链接产品文件的既有测试，Hooks 的 Challenge 别名保持 `!ANDROID` 边界。

| 构建目标 | Save | DeleteCampaign | DeleteChallenge |
|---|---|---|---|
| 默认 `GameInteropVersion=2.4.0` | `_Save_d__89` | `__TryDeleteCampaign_d__91` | `__TryDeleteChallenge_d__94` |
| 显式 `GameInteropVersion=2.4.3` | `_Save_d__92` | `__TryDeleteCampaign_d__94` | `__TryDeleteChallenge_d__97` |

`ProjectSettings.shared.props` 追加 `KTC_GAME_243` 选择，CoreCompile 前拒绝其他版本值。现有 TFM 与默认240配置不变。构建243时还必须把 `GameInteropDir` 指向对应新版本引用；该属性不自动下载、选择或安装引用。不声称同一 DLL 跨两版本。

## 输入与静态证据

源码基线 `release/v9.5.13@1f9c6753ef2a87ba994d38c96dc49b111927551c`。目标为 Windows x64 2.4.3r23537、Unity6000.0.66f2：

- GameAssembly SHA256：`cb3f28d73766c9ffb035512161bea1ad82835b0684330ce12f5d83da365e2342`。
- metadata SHA256：`b10a5c6e7ae16c5526c93d6c81cf5b821ef4e62ecbe4c8d7a23a7c8f93ed01c8`。
- 官方 `https://unity.bepinex.dev/libraries/6000.0.66.zip` SHA256：`9fa885ece698dcd65cfb1989643b72be425320c819cafa5c77b278eeeac17e9f`。71 DLL逐件对应ZIP；不据此声称内部每个DLL已证精确f2或原生兼容。

固定公开包中的 Cpp2IL 2022.1.0、Interop1.5.1，使用真实PE路径完成默认静态xref和GC writebarrier能力探测。两组生成均 exit0：无unstrip 101件、官方base库unstrip 131件；各158715非零方法记录、33160432字节xref缓存。默认生成的字段写入引用已由stub转为真实writebarrier符号，只有生成IL证据，不是GC运行时验收。

独立复核六类型161样本（160常规名称、1编译器名称sanitize）、十个重点方法签名/RVA与已发出map中的别名。三对保存协程的MoveNext均bool无参，所用state/this/Return属性形状与240相符；三个243 MoveNext在本map内各无别名。map不覆盖全部泛型实例/原生符号；未据此批准detour。

Interop是原生方法包装与元数据映射，不能当作原方法实现或原C#源码。

## 最终集成验证

纯编译使用独立wrapper，只导入共享属性与资源清单，显式 `BepInExPluginsPath=`，禁用共享compiler/MSBuild server；未导入产品部署target。SDK10.0.401、实际host runtime10.0.12、wrapper TFM net8.0、targeting pack8.0.31分别记录。这不证明.NET8 Windows运行时或公开loader已验。

| 验证 | 实际结果 |
|---|---|
| 243官方base库unstrip引用，全源纯编译 | 0警告、0错误 |
| 240真实Windows引用，默认分支全源纯编译 | 0警告、0错误 |
| 默认240目标配243引用／243目标配240引用 | 固定manifest预检拒绝，未开始编译，无DLL |
| `GameInteropVersion=9.9.9` | 实际MSBuild拒绝，无DLL |
| 四文件alias固定pair-set／Android条件子集 | 16个组合通过；块外无旧/新编号绑定 |
| Courier persistence既有回归 | 54 passed、0 failed |
| Hero recruitment既有回归 | 667 assertions通过，合成fixture |
| Hero/Bank combo及Courier economy旧夹具 | 编译受阻：缺PatchWorld_WallSpots；恢复HEAD原基线后两套同样失败，未修改stub，未声称保存门行为回归通过 |

243无unstrip组在去除协程绑定错误后暴露243个Unity API缺失/签名错误，未改产品迁就引用。该组仅保留为结构产物，不属于完整源码编译验收引用。

最终243 Assembly-CSharp引用 SHA256：`df73df913cd52953fed7fdd082c430388fe7504e44f2fd554f5ed50ae9b6ad30`；240引用为 `c75a7c13c38827bec31396c129381ad0f8a928a9e8895b960feb2c4537cbc37d`。每案对24个实际引用逐件核固定manifest SHA，不使用待测目录自报hash作expected。

本轮私有编译产物（禁止安装）：

- 243 DLL SHA256：`39721882431c1c6eec5f6239aa317b71d8f92012a02f913c82211360061c7d91`。
- 240 DLL SHA256：`7f3d907d5c2539f5776893b1b2ae5089b7f27568f31b12ed91223280d5047083`。

私有证据locator：本机 `windows243-compat-20261017/offline-interop/attempt2`、`compat-binding`、`integrated-validation`。包含源/输入/输出manifest、完整argv/退出码/日志、失败基线对照、独立审查与实际模型事件；原始游戏、生成DLL与存档不入仓。

## 原生验证仍待完成

专用标准测试账户已经创建并在预检后停用。最后一轮仍无可靠child entry/token/profile证据；已停止重复PowerShell探针。最新独立回读为账户Disabled、测试进程0、游戏进程0，原LocalLow 17文件及公共Steam目录2文件SHA保持。公共路由的真实token负向权限测试尚未执行，隔离边界未通过，故没有启动或安装Windows游戏/loader/Mod。

尚未证明243原生state0时序、Return marshalling、同步/异步失败语义、实际保存删除、购买读档、图形/联机与loader兼容。此阶段仅交付编译绑定；Issue保持开放，PR保持Draft，未合并、正常安装或发版。#247的240钱袋修复独立实施与验收，未混入本分支。
