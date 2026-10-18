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

## 2026-10-18：D1 根因闭环与相位修复

D1 R2 只增加诊断信息，实际原生与托管栈均定位到 `BeforePop` 内的 `Level.GetLayer(ContentLayers)` 空引用。当前 ARM 的该方法读取尚未建立的字典；正常生产者是随后原版 `TryPopObjectsToScene` 调用的 `ClearLevel → ValidateLayers`。因此确认的是本次冷加载中消费者早于生产者，不能泛化为所有热加载必抛。

产品修复只把这项依赖字典的层关系校验留到原版 pop 完成后。两次 Prefix 仍捕获完整原始存档证据与身份；后续发布 token、恢复、激活和写 marker 均执行完整校验。`ScopeReadPhase` 的零值为完整校验，未知值拒绝，未提前初始化原生缓存，也没有新增重试或清理旧对象。

独立 R2 审查接受 source/host 范围：真实生产方法抽取的旧失败/新成功、默认与非法 phase、getter 短路、两遍稳定身份、身份漂移和调用点变异验证通过；fresh 177、catalog 353、request-entry 165、Recovery/runtime、Observer 六模式、NativeResult 六模式，以及 C3 的 37 个 pure mocks 与 10 个 checker mocks 通过。旧 crossworld adapter 的五个过期 stub 编译错误继续单列，未修或计作通过。277 个累计源码输入只有 Recovery 改变，39 个资源保持。累计 net6 SHA `4bc8ea13de74fce953b27cd4d1ca2426ccaa9b3d788c964927a20a3ebb7fe37e`；net8 SHA `3317ed8d21ba29eae5b83d1086b3413f57e2f7f2d73053c5ece7139a216f20f7`。

D1 实际进程已回收，`2026-10-18T12:28:09.521574Z` 再次核验正常六类闭包、DLL、存档及模板保持，未恢复或替换正常文件。D1 的一次私有保存发生在控制器自动结束前，只是诊断证据，不能作为 C2 输入。

C3 是新的功能验证 case，尚未运行。原 C1 功能失败、旧严格 UID 比较失败均保留；原生 UID、层级顺序、Z 与冷却写入责任已经独立取证，新的结构比较规则单独审查，不声称完整 3133 对象或整份存档无损已证明。A9 正准备正确的原版自然登船路径，首次生成、旧岛恢复、地图详情、保存及冷读仍是合并安装前的待验项目。

## 2026-10-18 15:11 UTC：实际 C3/A9 结果、暂停与恢复

本节依据本机已固定日志、输入与退出回执重建事实。原隔离工作树路径在暂停后缺失，原因未知；受管入口已从同一 `7d65a29` 分支重建新工作树。此前两份未提交的事实更新没有按原字节找回，不把本节称为原文件恢复；正常 DLL 与存档未因此回写。

C3 已使用累计 net6 `4bc8ea13…fe37e` 与原存档副本 `2c62f885…e737` 实际运行。前像、合法 seed 交接和原 pop 后完整 scope 通过，计划 `creates=8 / markerOnly=0 / rejects=0`；placement 在收集 Tile 与占用前因 `LevelEdges invalid` 停止。后续发生一次私有保存与采样，`sceneSteedSpawn=0 / markerArray=0`。旧岛恢复功能失败；不能称测试止于保存前，也不能把这个后像作为 C2 输入。

当前静态证据确认 `IntRange` 的 8 字节 inline payload 及 `Level._levelEdges` 生成写者，不支持把其 getter 错误装箱作为本次结论。原版 `World.OnLevelLoaded` 在加载地面后重算 `worldBoundsStrict`，`worldBounds` 再向内缩 8；它们不取 Level 生成 cache。C3 没有实际 min/max，故没有定案为 0/0、坏档或单纯时序。runtime 子字段 raw offsets 与分析器归一化坐标也不能混为一谈。

A9 用 A-only `3fb35bfa…fc9`、未派生旧备份 `cb1e545a…deca` 实际加载 campaign0/land0，原版母船、P1、选中 SailPosition、乘客和 NaturalReady 均成立。Root 没有执行付款、移动或地图选择；source90 到界后控制器回收进程，未 Sailing/Loading/到 land11/请求保存，私有 fixture 保持。它没有到达首次生成验证，不记为 A 产品 PASS/FAIL。

两次游戏均已实际回收。用户于 14:35 UTC 要求暂停后，完整 fresh 回执确认正常六类闭包 `badabb0908…26b9f`、DLL `8345f086…9d9e1b`、存档 `2c62f885…e737`、模板 `8b9fc0ab…261287` 保持，游戏/VM 零；没有正常文件恢复、替换或安装。用户随后明确恢复任务，指定 GPT-6.1 Sol 内置 worker；14:49 UTC 实际正常 DLL/存档 hash 再核保持。

旧 D2 R1/R2 均未进入 Native，独审保留了真实 producer 与 collector 的身份格式/LIVE 输出不匹配、完整 fence 与读取责任等阻塞，不能以其 mock 绿色结论放行。新 D3 在独立私有源包中只使用一个 candidate-owned bridge，probe 调该 bridge，读取既有 raw slot 和 TryPop token witness；不调用可懒建岛屿的 `CurrentIsland/GetIsland`，不扫描 Tile/Bridge、不生成或触发保存。WORLD/PHYSICAL 仅是边界诊断对照，不能证明最终 placement 安全。双 TF 累计构建与实际源方法日志重放已交独审，尚未运行。

A10 新包为 runner-only 首次生成验证，准确原版 typed runner 保留 Prepare/flags/yield/Boat/Game 链，跳过货币交易及 Pay bookkeeping，不作为付款验收。其 R1 缺 parent 健康屏障与真正调用前负控，已由新 worker 修正并交独审；未来 Root 必须真实核验七项 bootstrap、进程与私有目录后才能发布单次健康许可，许可文件格式本身不证明游戏初始化健康。当前无实际许可、stage 或运行。

存档保护工具 V2 R2 独审只接受固定 C1-only 有限范围：137 旧结构 exact core、原生 Z/createOrder/cooldown 约束及权益历史事实分别记录；39 负控与 5 拒写检查通过。它仍禁止对象增量，不能换后像 SHA 冒充恢复后比较器。C3 实际对象总数 3133→3132，少一个 Coin 类别尚未归因；全 3133 与 wholeSave 保持未证明。8 个候选设施的资源闭包含 10 个 Persistent（Sleipnir 两只 Fireflies 另有持久化），后续须以 clone 前冻结计划及实际持久化责任核对新增对象与窄 marker 差异，不能只按总数判无损。

PR #264 保持 Draft、Issue #260 开放；旧岛实际恢复、原建筑保持、首次生成、详情剪影和真实保存冷读仍待通过。代码、host、Native、安装与玩家验收继续分别留证。

## 2026-10-18 16:19 UTC：D3/A10 实际结果与占地修正

D3 的新单次测试在 bootstrap 前被其测试控制器中止。控制器对非身份 console 使用全局 `fault` 子串判断，误中了真实加载日志中的 `default` 内存映射行与 `MTLCreateSystemDefaultDevice` 符号行，共 69 条。保留原失败回执，不把它算成边界采样、原生加载通过或场景根因；私有存档字节仍为原输入 `2c62f885…e737`。D4 在新私有包修 origin/event 分类并重放完整真实日志，仍待独立审查及新的单次运行。

A10 实际通过七项 bootstrap，Root 新鲜健康回执和原子单次许可后，唯一 typed runner 调用产生原生登船 flags 与 `SailingAway`。120 秒内没有选岛后的 `Loading/Playing11`，零测试保存、零采样。Root 只观察并 Raise 窗口，没有按方向键或选岛。用户随后指出航行画面需要左右键才出现地图选项；本次测试步骤不完整，不能据此认定航行故障。后续 A11 必须使用新 case，补有界自然方向键交互，不放宽计时或重放已消费 A10。

两个控制器及各自子进程均实际退出并回收；16:03 fresh handoff 证实正常六类闭包、模板、游戏/VM、端口、原 ADB 与 IPC 边界保持，未安装或恢复正常文件。17:00:19 原任务租约随后通过官方同身份入口续至 18:16:19，184 其他任务与 169 计划保持，独立读回与官方 after 一致；validator 通过，原 doctor 3 errors/56 findings 未冒称全绿。

另有一个独立、已确认的 source predicate 缺口：原 `CollectOccupancy` 聚合未标记装饰父组的整树美术 bounds，实际方法 host 反例会排空整段地面。修正保留 marked 对象原保护路径；unmarked 对象只逐个保护直挂的有效实体 Collider，再递归保护后代，未知碰撞区间拒绝恢复。旧方法同一 25 案中 11 案失败，新方法 25/25 通过；policy/transaction、scope 与独立重跑均通过，独立审查仅接受 source/host。实际 C3 更早停止，本缺口是否触发当前存档仍 Unknown，不能取代尚缺的边界采样。该改动尚未进入真实恢复候选，D3/D4/A10/A11 固定诊断包均不夹带它。

当前仍未证明旧岛恢复、首次生成、详情剪影、恢复后保存冷读或玩家效果；PR #264 保持 Draft，Issue #260 保持开放。

## 2026-10-18 16:54 UTC：D4/A11 实际结果与资源边界修订

用户最新资源规则取代此前未开始的全局 Native/VM 排队和跨专项整轮归还前置。新私有 launcher、checker、许可发布器及 fresh 只移除与本任务无交集的 QEMU/ADB 活动和模拟器端口空闲判据；数据闭包、真实 owner/PID/birth/argv/Filer、未知同游戏冲突、协议 partial/critical、只停止自有 child 等保护保留。独立重跑 113 项测试及 15 组实际函数/AST 控制通过，独审接受仅 source/host。既有同名 Mac 实例尚无足够证据保证私有数据与 CUA 画面归属，仍拒绝未知冲突；这不代表互斥锁文件存在或进程存活就整轮持锁。没有新建全局调度器，也不操作 Android AVD/ADB。

D4 使用新单次 case、原累计诊断候选 `756f5bf0…42012` 和原空岛副本。七项 bootstrap 全部成立，实际 controller `71977` 与 child `72032` 随后因 `BEFORE status=Unknown reason=after-fence` 停止；真实回执 SHA `9f6cf8b5…60665`。尚未输出 BEGIN、边界各 channel 或 END，零测试保存、零设施/marker 写入，私有存档仍为 `2c62f885…e737`。控制器按未知拒绝是正确的，不能通过放松它补成成功采样。

新独立调查绑定当前 interop 与反编译原件，确认 `currentReign` 是每次 getter 都调用 `il2cpp_value_box` 的值类型。D3/D4 诊断却比较并编码这个临时 box 指针；产品 core 本身用 campaign、reign index 和 landData 等真实身份，不比较 box。原 host 将 getter 桩成固定引用，故未覆盖该缺口。D4 的合并失败原因未记录具体变化字段，这仍是一个明确的诊断源码错误，不能称唯一实测差异。D5 仅在新私有包移除该伪身份，保留全部稳定身份和未知拒绝，并增加真实 getter 语义的旧红新绿及字段漂移负控；不提前修改产品边界或重跑已消费 D4。

A11 使用 A-only `3fb35bfa…fc9`，实际七项 bootstrap、新鲜健康回执及单次 273 字节原子许可通过，唯一 typed runner 产生登船 flags 与 `SailingAway`。按用户指引，Root 在实际 SAILING-OBSERVED 后按右键 100ms，画面从出航封面切到原生世界地图，证明上一轮遗漏这一步。随后两次鼠标点击左侧 A（第二次按住 100ms），新截图均未见岛屿选中、详情或确认；120 秒截止，实际回执 SHA `745c0ee1…ecf5b`，没有 Loading/Playing11、测试保存或采样。没有发送左方向键、下方向键、付款、游戏内移动或 API 确认。原生选岛接线与命中区域仍需核对，不能直接认定岛未解锁或原版不支持鼠标。

Root 事后独立确认 D4 两 PID 和 A11 controller `73584`/child `73697` 均不存在，正常六类闭包 `badabb0908…26b9f`、正常 DLL `8345f086…9d9e1b`、正常存档 `2c62f885…e737`、431 文件模板 `8b9fc0ab…261287` 保持；A11 私有 fixture 仍为 `cb1e545a…deca`。原始失败、旧 prepared gate 与源码包原件保留，新规则下旧未消费 gate 标为 UNUSED。没有替换、恢复或安装正常文件，Mac CUA 短焦点已实际释放。

旧岛恢复、首次生成、137 旧建筑及权益的恢复后保存保护、地图详情和真实冷读仍未通过；C2 仍不能从这些失败或只读 case 派生。当前占地修正仍仅 source/host 通过，尚未进入实际恢复候选。Issue #260 开放、PR #264 Draft 的边界不变。
