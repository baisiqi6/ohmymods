# #260 坐骑岛空岛修复：验证记录

本轮目标是修复扩展坐骑岛首次生成漏授予，并增量恢复已经保存的空岛设施与原生详情剪影。原有建筑、付款状态、地形和边界不重建。Issue #260；独立分支 `codex/mount-island-empty-260`，源码基线 `906da521ec06643af4a3286a8f2a1afd15a777f6`，目标分支 `release/v9.5.13`。

## 根因与修复点

- 当前 2.4 ARM 的 Haglet/reset-point 路径可从 TryLoad 迭代器 PC1 进入真实数据请求。原观察器只在 PC0 建立请求身份，合法热切岛因此没有 fresh envelope。现在在真实请求入口建立代次，旧代次不能消耗新请求的结果。
- 当前生成的 `Routine.Return<SaveLoadResult>.value` wrapper 对枚举进行了错误装箱。读取改为确认 metadata 类型、尺寸、字段和 byref 形状后的 4 字节 native field copy，并在读前后复核同一请求与 owner。
- 已保存的空岛不会再次经过首次生成。恢复在同次原生 TryPop 前记录旧档证据，原方法成功后及 Managers.OnLevelLoaded 后交接一次性 token；原生 pop 会清空 objects，不能把它误当作没有历史。WorldData 恢复会合法更新 cachedSeed，因而只接受已保存目标 seed 的这一次交接，之后继续完整身份校验。
- 只对本岛确实没有设施、标记和消费记录且生产分岛/季节/依赖规则允许的定义补入原生设施。已有实体只缺地图标记时只补标记；已消费或身份不明的定义不猜测重建。原生价格、币种、持久化路径及地图显示逻辑保持原有责任。
- 新设施使用私有 inactive 模板和 staging，整组预检、创建、校验、激活，再一次写入标记。选址要求整个保留区间有合格地面覆盖，并避开桥、海岸、旧建筑、树与边界。失败只撤回本批新增对象；标记提交结果未知时保留实体和诊断，避免盲目重试。

## 已完成的源码与宿主验证

- 独立审查 A 请求入口/结果读取及 C 旧岛恢复通过；复审闭合模板 GameLayer、original 执行证明、Global 与提交身份、合法 seed 交接和部分激活失败清理。
- fresh：177/177；跨世界坐骑目录规则：353/353；请求入口：165/165；runtime、恢复 policy/geometry/marker/transaction 均通过。
- 实际 Observer callbacks 的 6 个场景和实际 native-reader 源码的 6 个 metadata-stub 场景通过。它们不执行游戏 Native。
- 真实当前 interop 表面的 net8 与 Mac net6 累计构建均 0 warning / 0 error。使用与当前累计基线相同的 SDK10；构建禁用部署。
- 与现装累计基线逐项比较，39 项实际 PE 内嵌资源全部字节相同；既有类型仅 FreshObservation 与 CrossWorldMountRuntime 改变，新增 50 个类型，无类型删除。宫廷、商店等既有输入完整保留。
- 旧 `cross-world-mounts-adapter` harness 在原基线和本候选均因同样 5 个缺失 stub 声明无法编译，未把该过时 harness 算作通过；本轮不扩修它。当前生产 interop 编译与上述有效测试分别留证。

## 接入当前发布分支

Draft 创建后已接入 `release/v9.5.13@02758672703a335974e5e2ac16a711098215beeb`；相对原基线仅文档变化。重新运行上述有效套件及两组 6 模式 adapter、累计 net6/net8 编译均通过，277 项构建输入哈希保持。Issue 仍开放，Draft 不代表合并或安装。

## 隔离运行边界与已发生结果

首次生成使用不含恢复逻辑的 A-only 候选，避免 C 掩盖原错误；旧岛恢复使用当前空岛的私有存档副本及累计候选。各次使用独立 bundle、固定输入、私有 Filer 存档与 OS 写入限制，不交换或恢复正常玩家存档。正常存档、配置与运行目录事前备份/哈希，事后比较。

待验证：实际新岛授予、旧岛增量恢复、原有 137 个可升级建筑的身份/位置/等级/付款状态及地形边界保持、保存与冷读幂等、游戏内地图详情剪影。Native、玩家实测与 Windows 验收分别记录；源码/host 通过不等于这些步骤通过。

A1 首次隔离运行在产品验证前遇到启动基础设施失败：Doorstop 已注入，但 BepInEx、候选和私有存档 helper 均未加载。未输入 Continue、未航行、未保存，不能用这次结果判断坐骑修复通过或失败。进程已停止并核验正常六类文件闭包字节不变。系统记录的测试父目录 metadata 拒绝已用 libc 路径解析红/绿对照确认；它只证明一个路径障碍，尚未证明整个托管启动链恢复。后续使用新 case、bundle、输入门和运行窗口，不复用 A1。

第二次隔离 A2 已到达 BepInEx preloader，但仍未加载候选或 helper。实际失败为入口互斥锁的精确叶名与启动路径不匹配；当前 BepInEx DLL 的 IL 显示锁名来自进程名、Doorstop 进程路径及入口类型名的 UTF-8 串联 MD5，推导值与同 PID 异常、kernel 拒绝完全相符。测试权限后续只替换该路径对应的两枚精确叶，不添加目录扫描、通配或清理。A2 没有输入 Continue、航行、保存或付款；进程已实际回收，正常六类闭包保持，私有存档也未改变。它仍是基础设施失败，不能当作首次生成或旧岛恢复的验证结果。

## 2026-10-18 12:01 UTC：实际结果推翻的验证边界

以下结论来自本机当前 2.4 ARM 的独立测试副本，不代表 Windows、Android 或玩家实测通过。原始日志、输入与回执保留在本机 `.local/tasks/mount-island-empty-20261017/`，未复制正常玩家存档到发布材料。

- A3/A4 修正了隔离 CLR 参数识别并到达世界选择；没有完成目标岛授予验证。A5 遭遇 pre-plugin Dobby 分配失败，低层原因仍未定案，不能归因为 ASLR。A6 只观察诊断 trace 下的正常启动，3293 条连续记录无截断；额外 trace image 会改变布局，因此它既不是 Dobby 修复，也不是产品功能验收。
- A7 独立审查发现测试脚本多放了一层 `Release`：原生 `Filer.get_folder` 已给出最终目录，`DotNetAgent.TryLoad` 只做 `Path.Combine(folder, filename)`。当时 helper folder 匹配、fixture 字节未变，仍未实际读取目标 fixture。这个测试交接错误已在未消费的新 case 中统一修正 staging、预检和 postimage，并以真实函数旧红新绿复核；原 A7 保留为无功能结论，不能仅归因于遗漏难度选择页。
- A8 使用 A-only `3fb35bfa…fc9`，正确读取 campaign 0 / land 8 的原副本，观察到 Loading→Playing。probe 在 `hasBoat=False` 时仍调用 `Game.SailAway(Boats, 11)`，之后原生 Game.Run 异常；未到达 land 11、未保存目标岛。当前原生生产者要求母 Boat 与出航准备链，不能凭 FleetBoat 数据换一个猜测的 group。这个测试前置缺陷不证明 A 产品分支通过或失败。
- C1 使用累计 AC `61316ff0…d0d4e` 与原玩家 `2c62f885…e737` 的只读副本。实际观察器封闭 campaign 0 / land 11 请求，原生结果为 Success；但 `BeforePop` 中首次 `TryReadScope` 抛出 `Il2CppException`，没有捕获恢复 token。一次私有保存后，采样为 `sceneSteedSpawn=0`、`markerArray=0`。**旧岛恢复功能失败，不能合并或安装本候选。** 源码/host 审查通过不能替代这个结果。
- 137 建筑的原严格比较失败，前后均 137 个可升级结构和 3133 个对象，边界相同，但 UID 整体改变；另有 Z 和冷却值变化。当前原生 UID 产生与保存缓存清空机制、位置/父子图和字段写入责任正在独立复核。保留原失败证据；不把它直接解释成删除建筑，也不删除字段或放宽容差来宣称通过。
- 下一步 D1 仅在任务私有 overlay 中增加读取 stage 与有界原始异常信息，不改变保护、读取顺序或返回；第一版诊断违反短路读取顺序，已被独审拒绝并保留，修正版另行复审。产品工作树未因此修改。D1 不产生功能验收或 C2 fixture。

恢复 C1 必须取得实际恢复、设施/地图记录及原建筑保存语义证据后，才能准备 C2 的真实保存后冷读输入；当前禁止 C2。A-only 正确首次生成仍是最终验收、合并和安装前置，不以 C1/C2 代替。地图详情、实际冷读幂等与玩家确认仍待完成。

最后一次正常文件核验为 `2026-10-18T12:01:10.817511Z`：六类闭包 SHA `badabb0908ea0c3e5d68c4dbe71bd3444e6127392e2c99c5db6b227e12526b9f`，正常 DLL `8345f086…9d9e1b`，正常存档 `2c62f885…e737`，运行模板 `8b9fc0ab…261287`，全部保持。最后自有 C1 游戏已实际停止/回收，未恢复或替换任何正常文件。PR #264 保持 Draft，Issue #260 仍开放。
