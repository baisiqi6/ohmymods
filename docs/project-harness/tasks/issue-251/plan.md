# Issue #251：首次登坐骑岛保存与冷读修复

## 当前契约 R4（2026-10-17，替代下方历史实施边界）

R1/R2 原生隔离仍失败：generation正确slot11/raw0，显式Save(0,11,0)的有效Capture均在raw/index比较处拒绝，Apply无captured Island而触发CaptureFault；仅first raw0已记录，其余raw原值及Save后最终值未知。native已绑定land为ARM64+0x48，加载协程按请求land重写该字段；不能写raw11或改hash来迎合检查。正常游戏已按用户授权结束；R1/R2退出后完整五通道恢复、当前manifest复核通过，正常DLL未变。

### 本轮代码与验证

- 增量生产仅MusketeerPersistence、Patch_MusketeerNativeSave、MusketeerArchive；保留此前R3 Identity源码及全部GenerationWitness/live raw0权限/非virgin nonhome raw0 Load拒绝。既有Virgin lifecycle、onceguard、unknown-paid/reservations/收费权限保留，不新增epoch创建入口。
- explicit与-1统一冻结file/global/eventowner，以owner表内唯一同pointer位置定slot；explicit与实参一致，旧/后台岛不借当前场景。任何Capture观察到nonhome raw0即锁存同一generation witness/world要求至Apply；冻结来源漂移sticky。其它负land、duplicate/outside/zeropointer/nonzero raw冲突和zeroGetID/missed-active-paid保护不变。
- 物理proof只校验frozenowner的目标数组slot，禁止全表rawland/hash替代位置；完整context/epoch/scope/hash/native rows/kind校验保留。
- 原IslandHash算法、域、schema/hashKind、旧context/epoch/hash/GUID及补偿账目不变。只读兼容政策：可信slot>0且raw==slot的加载JSON可仅投影顶层land数字token为0，匹配旧完整指纹；不是历史转换鉴证。所有其它bytes保留，不写回投影，不用于新Save/Stage。收齐同context eligible epochs全部exact/preimage命中后先Agree/quarantine，不择优丢弃paid；HistoricalOnly/compensation排除不变。
- 先对R3跑实际红例并冻证，再实现；修复前后用同一用例验证raw0完整首存、显式旧岛与challenge来源、source漂移/sticky、wrong-slot同hash、完整hash反例、真正accepted host payload重建的cold/native0→slot模型、exact/preimage冲突、unknown-paid/hidden reservations、旧0/4/8与complete comp8历史不变。保留原四套source-linked回归及net6/net8完整私有累计构建、代码/二进制独审。
- 原生启动、共享档事务恢复需新singleuse包与精确候选关口；已授权隔离无需重复向人类询问。记录实际raw各点，只有实际accepted raw0才声称0→11兼容验证；退出重进必须同epoch/GUID/kind/NativeID/StockSlot恢复，不把模型、退出码或旧pair当native保存通过。

### R4复审收紧（2026-10-17）

- raw0 witness首次SourceWorld锁存，任何后续合法capture/Apply都不能切换world；VirginCapture仍exact-only，第三个preimage入口已删除。
- physical仅在实际使用preimage时对同context全部eligible epochs作纯托管记录一致性检查（最多8）；跳过自身epoch与持久化HistoricalOnly，不读实时attempt/runtime/scene。其他epoch不替代本proof。exact-used旧路径不新增扫描，读侧保守quarantine不对称保留，旧authority问题另#256。
- 最终独立源码审查与operator四套回归已通过，完整累计候选net6/net8各0W0E；native需要新的C3/C4单次controller，C4只用实际C3验收产物，不能回退shared/旧pair。controller修正是既有C1–C12执行，未获native启动批准前不运行。

### 边界与状态

PR253保持Draft、Issue251开放；尚未合并安装。正常私有累计宫廷/盾卫/地图等全闭包保留，不用公共DLL覆盖。不改Main/build/Heavy/Bank/Courier/shared wiring、原生字段、配置、原档、hash/schema、补偿或fault生命周期，不增加扫描/重试。盾卫同源风险#252独立。既有raw==slot legacy adoption与同key更换island/world后的收费授权另跟踪 #256，本轮测试钉定行为未变，不声称已修。

实施＋offline方案已通过真实GLM5.3/max修订复审和独立技术复审；native/merge/install均未获本阶段技术批准。用户当前明确授权继续隔离。源码基线与分支不变；R4最终源码/构建/原生结果分别留证，全部交齐且PR合并后才同步代码任务完成。

---

## 历史 R3 计划（已由上文 R4 替代，仅保留演进证据）

# Issue #251：坐骑岛首次登岛保存失败

## 目标与基线

玩家确认首次乘船到该岛，正常游戏停在保存失败弹窗。先修复已证明的火枪 placeholder 身份错绑，保留所有保存保护、旧岛身份和已授权补偿；不以诊断复现替代原生验收。代码基线 release/v9.5.13@1f9c6753ef2a87ba994d38c96dc49b111927551c，分支 codex/mount-island-save-251。正常版为私有累计 DLL5fb9c5f8…9352，不能用public DLL覆盖其宫廷/盾卫/地图功能。

## 本阶段实施范围

1. owning campaign 内 exact island 唯一 pointer→slot，live/gen/load共用；非0land分歧、表外、重复与owner不符拒绝。
2. 成功generation后的有界session witness允许同岛默认land0继续购买；file/owner/island/world/slot切换与exact reload撤销，不周期写回native字段。
3. 保留显式Save旧岛来源；-1沿冻结event owner内真实保存对象的slot解析，不能按默认land0建key。旧archive格式、epoch、付费GUID、NativeSave gate和共享Heavy/Bank/Courier wiring不变。
4. canonical12槽fixture，先红后绿；原0+新11分别paid枪兵、first capture/真实gate/模拟coldload返岛，owner/receipt失效与共享wiring异常nested回归。
5. 零购买无GetID及paid漏ID两路真实试验，保留failclosed。无native生命周期证据时不添加target fallback，不宣称该路径已解封。
6. 盾卫同源风险独立Issue #252，本轮不改其实际产品逻辑。

## 验收与交付

- 必要代码审查、真实生产直连回归、准确当前2.4引用下net6/net8完整私有累计编译及输入范围审计分别留证。
- 本阶段只候选与DraftPR；不启动/操作/退出用户游戏、不写正常DLL/配置/存档、不恢复/回滚、不merge/install。
- 后续隔离原生验证需先协调当前运行窗口，明确未保存航行/登岛进度边界；不得与正常版并行。
- 首存是否真实GetID、缺岛11快照冷进入、实际付款保存退出重进是未完成原生边界，不凭managed通过宣称已修。
- 未交齐/未合并不done；正常Git交付可在必要审查验证后完成，代码、安装、原生状态分开记录。

## 状态

GitHub #251开放；用户已确认首次到访。准确存档/ModSave/prefs/config已只读保全，当前主档可解析，不判整档损坏。最小managed机制复现已完成，候选worker正在实现，原生/恢复待后续独立决策。私有完整源码与存档不上传。
