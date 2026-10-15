# #232 G0 — 职业保存与探地共享源边界

本组保留现有 Android 0.0.30，给后续职业/弓弩选源去掉经济文件共置耦合。只有公共源码边界改造，没有接通职业、弓弩、经济玩法或新增 Android hook/pref/版本/安装。整个 #232 保持 OPEN；已通过正常登记进入 doing/running，审批仅 Scope-G0，整体未完成。

## 变更与责任

- 原 CoinCourierGround 整类移至同名共享源，字段、探地/净空算法、CoinCourierTiming 依赖和消费者不变，没有新缓存/兜底。
- CoinCourierPersistence 三个只读 helper 与尾部 Harmony owner 原样移同名 partial Hooks。PC 原 namespace/嵌套 owner、签名、属性与异常顺序保留。Android 条件视图保留六个 Heavy Save/marker/prepare/create/delete owner、真实 __state 和 finalizer；经济字段/通知/取消交易及两个仅银行 challenge owner 排除，未替换成空实现。
- MusketeerPersistence 六个银行/金币通知仅 PC。Heavy/Musketeer get-ID、load/create/generation 与 _load 归还原序保持，银行外围 finally 只在其实际模块存在的平台编译。
- 五个显式链接旧源的测试工程补必要共享文件。SafeCurrentCampaign/SafeOnline 仅被经济主文件调用：随连续后缀迁移，Android文本可见但无调用，不伪称 Heavy 职责；没有主动初始化、扫描或重试。
- PR226库存 Capture/失败和拒绝 ReleaseAll 三个 hunk 与 HeavyPersistence 两个保护 hunk另属 Main，本组不改。后续逐 hunk 合流，不能整文件覆盖；PC私有累计端点 NativeKeyReader 不是本组新增依赖。

## 验证与限度

| 层面 | 结果及范围 |
|---|---|
| 受控PC源码 | 本机真实PC refs、同SDK，net6与net8基线/候选均0W0E；各自strict type/member/attrs/生成类型/逐IL/全部resource比对 MATCH；net6为1358types/7158methods，net8为1355types/7154methods，二者各自allowed0/unexpected0。不是Windows实机 |
| 原TFM host回归 | 保存54 tests、运行桥接223 checks、商店73 checks、视觉生命周期15 tests全部通过；候选投影直接编共享生产源，旧嵌套owner反射检查保留。Host doubles不等于Android运行 |
| Android默认产品 | 当前66 Compile与182 ReferencePath已pin，源哈希与改造前全部保持，真实refs基线build0W0E；本组新文件尚未列入Android产品 |
| 私有Android选源 | 168真实源/297真实Androidrefs，移除两份desktop metadata，无fakeconfig/stub。Debug/Release均23个CS0117，9个ModConfig真实接口待接；保守静态前沿不是完整产品闭包或完成比例 |
| 旧独立actual-interop工程 | 未修改基线/候选均缺SharedBankNative字段CS0246；完整PC真实源构建包含真实银行并通过。该独立工程不计通过，不加stub |
| native静态 | 23方法名/25具体method-definition和3save writer固定2.4.0输入对照，具名地址唯一、映射一致。未读native函数体、未验实际Agent/线程/ABI/存档事务，不预付运行 |
| 独立审查 | 源候选无correctness must-fix；成员表的记录误标已订正。计划只approve G0 scope，未来G1-G3另冻真实契约 |

后续9个真实设置、人口13职业分类/中文展示、完整producer/consumer、Tick/原生保存/paid身份OFF保护、Holder/sync/工具弹丸/FX池重建、原生ABI、读档换岛和实际游戏验证仍待接入。#213模拟器宿主长挂根因独立OPEN；手机平板/联机/长期玩法未知不当作通过。

不改根目录Mono、Steam/正常Mac游戏、存档或现有APK；内部前置没有新增对外完整功能条目，按VERSIONING暂不另计版本。单次原始日志/哈希/失败与回执见本机 .local/tasks/android-profession-archery-20261014，注册与PR具体值由同任务进展记录。
