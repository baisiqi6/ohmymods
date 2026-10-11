# issue-212 防御工匠移植实现与验证

基线 release/v9.5.13 merge2fe6c517；独立 codex/android-wall-engineers，既有成果冻结。
批准计划 plan.md 5826B SHA5785a2bfbc9f61f9fbdbbb3c10a1a64c188a73da3b2aa9474cca118e9086408e 原样保留。

共享 Runtime 的 FeatureOn 加 ANDROID 真门（原Enabled AND WallEngineerEnabled），Rules追加唯一纯
状态 formatter，CalendarHud原API委托。Patch完整复用，Probe13精确目标有12新增，Apply复用人口
目标最终两postfix/一finalizer一次日志；TryPop保Priority.First/bool+exception finalizer。
OFF保Tick/load proof/待归还owner，Time.time单driver、injected OnDestroy退出Reset。
现公共Step“外墙耐久上限”沿原样式/滚动/中文。真实entry默认OFF，初始化零Save、flip各Save一次。
不改任何HP/job/load/Scanner算法、不新建重复driver/镜像/重试，不因共享代码存在独立补偿就删除。

最终SDK10 Main0W0E，245248B SHA3aa7bc6256fcb9d82a6ba3ec4fec02672cb99a5a70126a1ecf4ff99d6df4264e。
原2f113产物运行后暴露名单错误，不能沿用初次源码批准称功能通过。
直接共享宿主原247完整保留；PC260/0，Android296/0；新增13formatter与36平台真门场景。
最终adapter default927/0、原四键oldcfg767/0、Wall预存ON927/0，74源pin（旧71全保留，4实改源重新pin，
追加3共享源）。原24真实entry值由开关前后完整捕获比较证明，oldcfg不是24键文件模拟。
首次adapter默认/预存ON各一失败来自测试ready log缺新字段，修正该期望后新attempt通过；产品未为测试改动。

PC完整实际两build0W0E：同名原接口只委托，迁移纯body instructions/header/locals/EH相同，唯一有意差异
为1旧method/1新type。其余所比较metadata/成员/IL/37资源相同，private comparator覆盖局限仍保留，
不声称PE byte-equal。源码独立limited-fresh批准；OMP功能worker实际deepseek/deepseek-flash、requested max、
stop，只写4私有候选，没有构建/安装/Git/SSH；Root检查后统一接线。事前operator实际GLM5.3/max按阶段审查。

13原生body范围由exactFDE/PT_LOAD定位，共9960B，metadata/map/asm/bin相符、零callee读取。
96B unsafe保存x0并送x1尾调，未以map唯一或长度作detour安全证明。damage参数宽度仍只依metadata，
body未消费该三参数；当前postfix只__instance。独立limited-static审查不批准最终writer/异常桥/HP/save/MP。

运行根因与修复：原APK45619c51在ON后每5秒roster NRE/未生效；初次stageAccepted只验UI/配置，
该失败和模拟器QEMU hang及一次45秒loader未ready窗口原记录全部保留。私有诊断仅扩原Fault日志，
首栈定位ReferenceArray(IntPtr)；4个具名IL确证Android CopyRoster/ReadRoster conv.i+IntPtr构造，
PC原产物conv.i8+Int64构造。共享两处显式(long)count，宿主由假int构造改真实long/IntPtr并拒
把人数当指针；根因在数组分配处，不改CopyTo、人数、HP/job/load算法、不加guard/重试。
诊断日志和版本标记不留在产品。原独立责任的Fault限速和失败保护保留。

修后实际两处Int64构造，PC完整两方法IL保持，六build0W0E；PC260/0、AndroidSDK8/SDK10编译
各296/0（同场景不相加），adapter927/0、767/0、927/0。两个运行调用错误保留：SDK10runtime
缺net8框架和误拼--old-cfg；只纠正运行器/参数后通过，未为测试改产品。
私有APK6c792318cea2fa8ebe5c0c4a63f8c148dd93e9a347eda391e907b591dabe43e7，1908911139B，
CRC/22313原条目byteequal/123互操作除16B GUID相同/同测试证书/zipalign已核，保数据install-r一次。
修后UI ON→OFF六输入/八PNG，123.153秒zeroPID；ON持续26.977秒、13.974秒后二次capture，
无WallEngineer warning/ERROR，状态“暂无外墙”证明原名单错误路径已通过，不预付耐久。
最终25键Wallfalse/Rolefalse/旧24保持，CFGde8c441a…；Main3aa7bc62…与所选native7ba5f975…保持。
其后的OFF无输入冷启动，ADB读取8秒/force-stop3秒超时；QEMU主循环及四CPU线程hang，
Mac IPS的fault thread3为CrashReporter.hangDetector→GenerateDumpAndDie，ownedPID3079随后消失。
未确认guest零PID/未完成最终读取，跳读导致的mainSame/nativeSame false不代表观察到hash漂移。
该窗不算冷启动通过，未再boot、未假定GPU根因或mod无因；测试环境有界对照跟进 [#213](https://github.com/baisiqi6/ohmymods/issues/213)；紧急停止检查发现PID已消失，未发信号。

代码PR未创建/未合并，Issue开放，受管状态仍doing/running；完成交付后按规则另行同步。
天然外墙和工匠作用、维修、读档换岛/池/保存/手机多人/长期均pending，不冒充已验。
源码、APK、日志、存档/interop原始材料留本机，不发游戏资产，不启动PC/Mono/Steam正式目录或手机。
