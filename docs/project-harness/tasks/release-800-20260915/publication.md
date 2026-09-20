# v8.0.0 正式发布

发布页：https://github.com/baisiqi6/ohmymods/releases/tag/v8.0.0
完整包：https://github.com/baisiqi6/ohmymods/releases/download/v8.0.0/KingdomEnhancedMod_v8.0.0_IL2CPP.zip

2026-09-15 正式v8.0.0已发布Latest，tag/source dd5c86f，ZIP 1095ceb2 / DLL 80522bf1。精确提交clean canonical构建0W0E，修正发布工程缺firstpass Input的引用偏差，2675方法与此前8.0候选全同/两PNG保持；68项目重跑通过（56run/4xUnit392/392/8compile）。正式313项/306runtime逐字节匹配7.6.5、CRC/独立审核/远端digest/Latest与tag读回通过。用户明确要求发布后已闭游戏备份同步E独立副本8.0正式DLL，原生存档/个人cfg/ModSave附加文件hash全保持，未启动游戏；此前本机7DD候选已含最新玩法，只版本标记未统一。旧版本与master不改，英雄/围巾/联机等待实机项仍doing。

用户澄清“先上传发布”是本轮明确授权，包含必要的精确release源码提交、新tag/发布和已关闭测试副本正式DLL同步，不延续上一轮“只本地打包”的范围。仅推新v8.0.0标签；当前canonical工作树/索引与master未被重置或推送，原始未提交工作保留。

精确提交：dd5c86fccb132be65aa9fca204d8cd3be1035121；父提交为公开7.6.5源码648ddf0417f58162ef10c42c4bfdfc5faa66edc4。452项选集含117生产文件、323测试源、5素材来源、5玩家文档、原打包脚本与release plan。私人存档/配置/操作日志/临时工具不入新增选集。首个未推送候选a6df601因构建接口差异已废弃。

构建引用修正：旧canonical工程未显式引用Assembly-CSharp-firstpass，导致5处ModPanel.Input调用绑定UnityEngine.Input，与已验证候选的global Input不同。新增GameInteropDir统一实际2.4模块，并补firstpass/Il2CppSystem；没有修改玩法.cs源码或Mono引用。最终Cecil逐方法（含locals/EH）证明与8.0候选全部2675方法一致，2资源相同；候选相对7DD仅版本文字不同。不得将未修引用的首次build当最终产物。

ZIP SHA256：1095ceb2dbc587fc60366835e7b7c360b87f3408a6005184f5325c446d78db8f；39636093字节、313项。
DLL SHA256：80522bf18952fe609c4f6f91fd6b52c26ab79cbf87ee918159770306953348f4；910848字节。
原canonical package_release.py未改，clean工作树运行，BUILD-MANIFEST记录精确GitCommit，正式包没有旧uncommitted-snapshot/SOURCE-MANIFEST。306运行依赖与7.6.5逐字节一致。

68项目在精确提交重新验证：56可执行回归、4个真正xUnit项目392/392、8个Library接口编译。没有把build或dotnet run空执行当xUnit通过。独立只读reviewer另用Cecil与ZIP读取复核源码、引用、资源、CRC、布局及文档。草稿按发布ID388997937读取并核上传size/digest后转公开Latest；草稿按tag端点返回404时改用现有release ID，未重复创建。

本机路径：E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/KingdomTwoCrowns.exe。
安装DLL：E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll。
旧DLL备份：E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll.before-v8.0.0-20260915-170249-856.bak。
安装前两次检查游戏关闭；安装后DLL、原生global-v35、个人KingdomEnhancedMod.cfg及ModSave全部已有文件hash核验。没有启动游戏，精确正式DLL尚未实机启动，英雄/围巾观感与其他玩法验证边界保持。

旧本地快照ZIP571d278b及checksum保留在本轮archives/；仓库release同名ZIP已更新为正式1095ceb2，避免继续分发未提交快照版。版本资料区分历史打包与当前公开版本。
