# Android 世界建筑点位实现记录

计划在 normal registration 时以6773B/SHA638654bc冻结，标题中的“草案/尚未注册”是审定前原文本，不是当前任务状态。当前已在独立 codex/android-world-layout 分支正常认领 issue-217，源基线ea5aa89f；旧168项顺序/对象、10计划和根未归属改动保留。

实现已接入共享塔基/墙基点位与同址重复塔清理：Android增加真实5设置到30，公共岛屿生成页复用暗金中文Step/Toggle、小浮球与滚动。整组默认关闭，开关只影响下次应用启动时的显式注册；当前运行职责不中途卸载，已持久化点位停用后保留。启用并把倍率设为1，沿原加载/边界流程回收可删未建点位；已建、支付与施工保护沿共享源。

SpecialTowerRebuildMarker原有类型、注释和IntPtr构造函数逐字搬到单文件，PC名称/注册不变；Android只在本组启用时先实际注入，然后在已有World.OnLevelLoaded追加两共享postfix，并注册Kingdom.DistributeTowerArchers的事务prefix。共有3handler/2native目标，World末尾统一报告，唯一新增目标1；本组未带特殊塔拆建、付款、资产或独立PC诊断hooks。共享TowerLadderCleanup成功末尾真实true→false写入后，ANDROID追加一次现有Save，不重写算法/新增周期机制。

首轮纯构建边界（历史）：私有Android0.0.29实际64Compile源、Main294400B/SHA5b31deda86d0ad1e48c52dc61c629d2f7aa73a86184fb88325ee6b9b1acbda81，0warning/0error。PC纯基线/候选两build0W0E；现比较器所覆盖1318类型/6934共有方法无差异，37嵌入资源保持；不宣称整个PE或未覆盖元数据逐字相等，未部署PC。

当时验证尚未完成（后续补齐见下）：首次新测试误读替身的属性形状已在测试修正；第二轮adapter六变体实际到旧IL逐字节扫描器时均ReadOutOfBounds、exit-6，不是产品运行错误，未把空FAIL列表当通过。该旧scanner在三个helper会从operand内制造假call/token；正在修一份按实际指令边界的共同reader并重跑真实回归，不增加跳过越界/异常兜底。

初始原生事实（历史）：两具名hook当前metadata地址槽各1/map吻合/exactFDE760+772B，45具名interop成员44exact、1合法optional参数差异；这只声明与范围，不提升为无所有inline/完整接入安全。实际GLM及独审已同意在12KiB总上限内仅5具名function精确span验证receiver与ExitGuardSlot→ExitArcher→AddGuardSlot→Distribute直接链；签名先核，不猜ExitArcher空参数、不以邻接差当边界、不追其它callee。当前尚无此轮最终静态body结论和ON安装批准。

该阶段尚未新打包、安装、启动游戏或设备输入。原#212私有包与恢复后的host环境保留；#213仍开放且正常released、历史QEMU宿主挂死根因未定。自然买建/点位退役/保存读档/换岛/长期稳定、手机和联机验收继续待实际记录；本记录不表示本批代码已交付或Issue可关闭。过程回执留本机.local任务目录，不上传APK/游戏资源。后续真实验证、审查与PR收尾结果追加于本记录。


## 构建与测试补齐

UI帮助调整后的最终 Main 使用 build-main-2：0.0.29、294400B，SHA256 ee8cb2271fac4ab92543f8f64fcc706c5213959fed0dec3da190f9840ae51ca5，64源码纯编译0W0E；前段5b31构建保留为首轮历史，不再作为最终包输入。Reader修复后的实际8变体均通过，typed3handlers/真实instance参数/返回/目标属性及marker状态/接口/IntPtr构造检查已补齐，81强pins无跳过。默认/布局开启/小数/越界/非有限各1026 passed、0 failed；旧配置/角色工匠/组合各866 passed、0 failed，分别消费同一个实际Main DLL。新增 typed 检查曾误调用既有helper名称导致编译CS0103，已改为实际HasClassHarmonyPatchTarget，失败回执保留。

仅5具名函数2924B（760+1008+288+96+772）的原生静态检查确认 x0 receiver 与 ExitGuardSlot→ExitArcher→AddGuardSlot→Distribute 的指定直接链，最后一条为恢复receiver后的尾B。未追其它callee或执行native；未将具名metadata地址槽唯一泛化为全球无折叠，也未将该调用链泛化为所有调用点或玩法安全。明确剩余安装/运行及自然行为边界。

交叉审查又发现纯reader的BCL Nternal保留opcode入表会静默接受F8/FF，正在同阶段按类型拒绝，并追加保留项/合法FE前缀回归；这不改变产品点位算法或原生诊断范围。此前8变体通过只代表当时产物，修后仍须新pin与相同矩阵重跑后才能最终批准。


## 最终源码与私有包边界（2026-10-11）

保留前述失败和中间结果。Nternal 保留项在产生 opcode 表时拒绝；25 个纯用例全部通过，F8/FF 负对照确认旧实现会接受、新实现明确拒绝，合法 FE 前缀保留。三旧逐字节 helper 已统一使用按 OperandType 前进的 IlInstructionReader，无跳过非法 MemberRef 或吞异常补偿。最终 adapter-build-6 实际八组均 exit0：默认/布局开启/小数/越界/非有限各1033/0，旧配置/角色工匠/组合各873/0；81 strong pins 无豁免，原74保留。独立源码与交叉审查均 approved、无剩余 must-fix，限源码及相应静态证据。

原生五个具名函数总2924B直接链检查和 Main ee8cb227…/294400B保持。原生 producer 单 Main refresh 私有 APK b1ada5ff…/1908927523B已生成；原游戏22313条目逐字保持，CRC通过，123 interop 仅GUID堆16B变化，实际 apksigner/zipalign退出0、同现存测试证书，payload单Main refresh及android-arm64/CoreCLR runtime identity核对通过。未上传游戏、APK或签名材料。

此记录时仍未安装或启动候选。鲜备份实际原Main3aa7…、原25设置、选定native7ba5…以及完整Release85504B；首次只读备份比较沿用旧批次排除项导致assert失败，改为全25比较，原捕获保留且无设备写入。当前鲜备份完成。安装与 OFF→ON→OFF 三冷启动注册/触屏保存的有限验证仍受独立运行脚本审查；天然点位数量、建造保护、1倍退役、换岛读档、手机/联机及长期稳定均未预付。


## 有限模拟器验证补齐

同一独占 API35/host 测试副本保数据 install-r 一次成功，实际installed APK SHA=b1ada5ff…；安装前后配置/native哈希保持，原生producer首次启动将Main刷新为0.0.29/ee8cb227…，未手推DLL或伪造配置。三次冷启动 OFF→ON→OFF 唯一目标51→52→51，World postfix1→3→1，完整期望表一致。三个窗口70.418s/79.795s/47.549s，共197.762s，输入3/4/0共7次；每窗实际alive之后force-stop确认零PID。小浮球/生成页中文开关和两密度行、滚动及真实配置保存通过，最终30键中WorldLayoutEnabled=false、RoleScaleEnabled=false、WallEngineerEnabled=false，旧25值保持。最终配置SHA1fceae71…、Main ee8cb227…与所选native7ba5f975…保持，只声明具名回读相等，不声称全过程未自动保存。

开启窗原共享日志记录native25/generatedUnbuilt0、added11 spots(multiplier2.00)、具名模板renderer1/enabled1；这是单次自然加载生成的日志证据，未实测数清每个点位、实际建造/支付保护、1倍退役、弓箭手释放事务、点位持久化/读档换岛、墙基边界扩张或手机/多人。loader已有依赖与scene index警告如实保留，具名产品日志无ERROR或点位异常。最后关闭无输入窗看到森林/小浮球，但197.762秒有限实验不证明长期稳定，#213挂死根因仍未定且保持开放。代码交付/合并与受管完成仍待后续正常关口，不预付。


## 正常代码交付与受管完成

源码32abd66c9a07a2002a176a7bc9902b2969fddc14通过最终独立交付审查后提交；[PR218](https://github.com/baisiqi6/ohmymods/pull/218)正常merge4ced5f675f07c206838ee42c9b2e32dc632afeb6，GitHub Issue217代码关单已读回。15文件精确scope，未上传APK/游戏/签名材料，未做正式tag/release；没有CI记录，不称CI通过。

真实normal packet718d68cd786d5858dd3b0011003d070981cb1ac6abcac353c70bb3e796c7d81b经独立证据/脚本与实际GLM5.3/max审核，再按单任务正常closeout/review-result/mark-done-prepare/files/record完成。receipt2b9aedc7-a8fd-4b04-a9b7-9627725a7d7a已consumed（event653357ff-3752-4c8e-b693-a82c8b9d80f7）；canonical和mirror为done/closed、owner/session清空，lease2026-10-11T15:35:40Z释放。仅217 normal reconcile，validator退出0、doctor0错误0警告/43信息；其他168完整对象与169顺序、11份计划含原own6773B接受计划、本机119和根7未归属改动保持。没有force/repair、裸JSON/DB或全量reconcile。

此完成范围是代码、必要审查/验证与合并，不将自然建造/退役、弓箭手释放事务、持久化读档换岛、墙基边界、phone/MP/long验收提升为通过；#213仍OPEN。现存模拟器新版Main0.0.29/30设置/整组OFF、游戏已停止。上述较早“候选/待安装/交付中”段落是过程快照，最终结果以本节为准。
