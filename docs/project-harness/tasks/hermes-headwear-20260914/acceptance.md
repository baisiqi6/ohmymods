# 赫尔墨斯随机头饰：本机候选验收

2026-09-14 已实现、构建并安装 E 盘独立测试副本候选，完整游戏验收仍为 doing。
构建 `6.1.5-hermes-headwear-20260914`；DLL SHA-256 `46AD1CF9095353D8F2FC32889AA707E419C41DCD15380B732203E565C1862678`。

## 功能与范围

F5 → 战斗 →「法杖随机头饰」，默认开启。配置 HermesHeadwear.Enabled / ChancePercent，概率默认30%，范围0–100。只在主机的新 HermesStaff 转化上下文首次决定；普通五世界各0–5共30款、周年帽子7款与面具7款共44款等概率选择。未抽中、关闭时转化及无扩展的旧对象保留原生外观，不靠读档或反复Init重抽。

原生 MaskIndex=-1无面具、0为真实第一款的语义不变；不写跨世界编号到MaskIndex。血量、toughTroll、反制、永久控制、伤害与原战斗行为不变。不修改敌方Troll、全局换皮表或其他单位，不新增短getter钩子。原帽消失只是玩家观察，根因仍未确认，本次不声称已修复。

使用独立SpriteRenderer子物体挂真实动画Head，原始pivot/PPU、继承朝向；隐藏及恢复精确接管的原生mask renderer，保留Hermes翼饰/粒子。只遍历已跟踪对象做维护，不逐帧扫描全场；缺资源时全局限频重新枚举已加载Sprite。关闭清理自有视觉但保留抽选收据，重新开启恢复同一选择。死亡/离场/池复用清理，部分创建和Destroy异常保留待清理责任。

## 保存与同步方案

GUID、schema与明确choice放入FriendlyTrollData原生组件JSON的小型扩展 `kemHermesHeadwear`。Save作用域内GetID后缀只临时关联本次快照的Persistent，捕获实际Island引用；Save后缀在随后压缩前附加数据，finalizer恢复嵌套上下文，TryCreateOrFind读取。不是永久NetID/instanceID、不读写用户压缩存档文件。未知扩展透传；关闭不停止保存/恢复收据。

原ObjectData构造器方案因IL2CPP后台不支持而撤回，2F47CD9A仅保留失败诊断，未最终安装。新版Save/GetID/SpawnMask实际原生入口已读到FF25 detour，日志无backend fallback。原生Save自行catch错误后正常返回仍会运行postfix；异常回归测试专指异常逃出原方法时finalizer的清理，不能混称任何中途失败都不注入。

主机决定并同步；原生组件尾部30字节用于同步数据，RPC只在RegisterComponents完成后追加末尾，不占原生槽。待在线、客户端存在且caught-up后才发当前生命期结果，避免原生finalgrab先于poolspawn。token/revision拒绝旧状态，池复用与header Flush独立清理。要求主客双方使用同版本mod；异构未装mod客户端不在支持范围。真实2.4原生证据确认spawn/RPC/despawn共用可靠顺序队列，但这不替代两机验收。

## 已通过的验证

- 55项核心直链回归：抽中/未中、有帽/无帽、原生字段不变、JSON及网络尾部、旧/坏/未知数据、重复初始化、保存桥接/异常、关闭、对象池、换world与host/client模拟。
- 27项视觉直链回归：44映射白名单、Head定位/朝向/排序、native mask恢复、二次SpawnMask、资源失效、复用和失败清理。替身边界测试不等于实际Unity画面。
- 23套原有console加75项archer xUnit，共原24套全部通过。完整IL2CPP强制Rebuild 0警告0错误，337个可达Unity方法无unstripping失败。
- 比对1F111CD5：1666旧方法中1661完全一致，只改Plugin标记/ModConfig.Init/ModPanel三个入口；无旧方法删除，全部旧Harmony属性保持。银行、缩放、小动物、鹿、补货、便捷、弓箭及隐士热修保留。
- 实际资源离线读取44款像素尺寸/pivot/PPU及动画Head，离线叠图检查位置。实际游戏探针找到44/44加载Sprite和native Head/body；使用最终主DLL codec完成42组原生IL2CPP JsonUtility内存往返，原生三字段保持。
- 精确最终候选PID 1772，2026-09-14T06:27:07.9726930+08:00至2026-09-14T06:29:00.4093692+08:00约112秒受控运行，chainloader/RunningGame成功；Save/GetID/SpawnMask与既有生命周期入口FF25，隐士getter17原字节保持。无新异常类型或backend失败；既有NpcShieldUser.SetShieldEnabled NRE仍存在，不计为本功能已修复。
- 游戏退出后原子安装同一候选，临时probe移除。save `3DE1786460A74262F58D2C18772E91722BD7D50CBCF04517F2526BCAD3C5571A` 保持；配置 `4CE570229201E9D6AFA77ED66B89C7628A3C34C2DDF506844A6E77B50C9BD398` 保持；银行5709保持。未覆盖用户存档、未提交/发布、未写D Steam、未改Mono。
- 本机OMP两个worker的session公开元数据确认deepseek/deepseek-v4-flash max、非fallback；独立内置reviewer多轮复核通过。root完成整合及上述验证。

## 待实际游戏验证

| 项目 | 已有证据 | 尚缺证据 |
|---|---|---|
| 有帽/无帽新转化、30%及未中保留 | 代码回归 | 实际法杖转化现场 |
| 五世界/周年帽与面具显示 | 44实际加载资源、离线Head叠图、视觉回归 | 活小怪行走/攻击/左右朝向，排序遮挡和翼饰目视 |
| 同对象稳定、关闭/重开、死亡与池复用 | 生命周期回归 | 连续实际遭遇操作 |
| 保存读档、换岛 | Save桥接回归、原生detour、42原生JSON内存往返 | 游戏正常保存后重读与往返岛屿 |
| 主客机、晚加入、断线重连 | 原生可靠队列审计、协议回归 | 两台真实游戏一致性 |

探针未生成活FriendlyTroll，未执行游戏存档保存。早期离屏camera截图全黑，不作为视觉验收证据。以上待测项目不得标done或宣称实测完成。

## 证据位置

外部工作证据目录：`C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/hermes-headwear-20260914`。含source-manifest.json、verification.json、build.txt、unity-audit.txt、core-tests.txt、visual-tests.txt、clean-test-results.json、previous-test-results/archer-prior.trx、worker-receipts.json、native-disassembly.txt、asset-previews/headwear-head-anchor-preview.png、runtime-proof/assets.txt、runtime-proof/native-json.txt、permanent-receipt.json、permanent-LogOutput.log、final-probe-receipt.json、installation.json。private-*文件是本机状态快照，不纳入项目或发布。
