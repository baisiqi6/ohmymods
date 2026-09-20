# 塔基特殊建筑漏检：本机实现与启动验收

当前存档实证：campaign1/currentLand9，KEM_TowerSpot_156.6 x156.6399536 与 Tower Knight_greece x156.6399994 均在Level/GameLayer，独立SemiStatic持久化对象。本机5.0先前扫描native19/generated6/occupied31/retired0，还新增1点。实际resources资产的希腊Knight/Ballista/Fire特殊塔无Tower标签及Tower组件，Bread也不带普通Tower，四类均有WorkableBuilding+ConstructionBuildingComponent。旧代码tagTower列表无法看到它们；精细避障跳Tower，旧清理只nonTower和keptGenerated，又漏普通已建塔。根sprite96px/32PPU宽3；3.34近邻不直接定为重叠，sameX例证明确。

## 修改
既有WorldLoad延迟入口新增WB/Scaffolding各一次局部snapshot，与tagTower去重，包含当前层级/scene的普通和特殊建筑。施工inactive Building只有关联脚手架仍active、同层级且Building引用仍匹配才占用。新点加入snapshot，同轮互相避让。创建候选从实际活nativebase template量footprint，并与Spawn复制同一template的rotation/scale一致；原prefab只作为生成资产和原生metadata。

视觉bounds必须正宽且finite；零宽子renderer不拉向world0，非finite/不可读取返回Unknown。Overlap/Clear/Unknown三态中，Unknown禁新点但不能作为旧塔基删除证据。layer/scene/IsChildOf/boat/玩家层级关系读取异常交由外层按Unknown或不可删除处理，不吞掉错误当空地。旧清理不依赖倍率>1或refCount>=2；新增仍保留这些门。

删除仅可确认marker KEM、level0、active、当前world/layer、Persistent/SemiStatic、PayableUpgrade且无任何付款/选择关联或施工组件的空位。双玩家selectedPayable/_completingPayable使用实际interop属性，非反射。两可删peer按稳定x顺序保留先者；native/已建/不可删除KEM仍是障碍。首个mutation前重验；partial错误按实际失活计数并中止本轮删除。移除一个root只移除一个x记录，保留同址另一建筑。

不修改原生已建塔/原生塔基/存档文件、不新增nativehook或周期全场扫描。正常每次load回调一轮；完整expand有per-world guard，但cleanup-only（1x/refs不足）分支重复回调仍可重复检查。未增加升级完成回调，因此不承诺同场景后续任意升级都不会再次出现间距冲突。

## 证据
ZCode0.16.5 worker session c4fa44e3-4faf-4f5c-8f8a-b0d7b88562e9，native model及response均bigmodel GLM-5.3；requested max未独立证明。其原案36项29过7败，暴露Scaffolding关联丢失、反射读不到真实付款属性和无效bounds误删/放点，未作为最终版本集成。Operator修正后完整生产源码CompileLink最终46/46，独立review PASS。

源码SHA256 381D96464ED3330C4509580F562ED22CEBA3DA1B4E1CC35E512E9C71CF5C5B0C，canonical与候选完全一致。实际依赖build0W/0E；95可达Unity方法无unstrip桩，1个既有World.OnLevelLoaded hook实际wrapper核验。没有新增native detour地址。

本机DLL 73B11F7DCD5F07C99AA01ABD978F371B1E0635147ACC05B738691646BD3C39B0，build=5.0.0-tower-overlap-20260912。检测用户游戏已退出后，备份原正式5.0 DLL0549C166，原子替换E独立副本。启动2026-09-12T00:59:11.5240765+08:00至2026-09-12T00:59:50.3668872+08:00，仅自建PID30188，恢复day63 t8.74 timeScale0，脚本于ClockDiag停止。无新BepInEx Error/Exception；存档前后1D1CA083FC05769C7462292D710969C9E1DEAC6D55DE772F08316F9ADF02C4C9一致。最高私有内存2.18GiB，所有采样响应正常。

## 剩余验收
暂停启动未跑5秒scaled延迟，尚不能称已在实机删除重叠空位。最新存档仍保存同址Knight塔+KEM空位；下一次读档后先走离/取消选择该塔基、恢复运行约5游戏秒，再检查。正在付款或选择的塔基会被保护，此次跳过后需下一次load复查。游戏内清理后正常保存才会持久化该变化；本次没有直接写save。任务保持doing待玩家确认。

附带5.0实际游玩证据：此前日志有SamuraiVisuals ready、SamuraiRetreat3x命中、SquadFollowGuard apply/release、FarmCats retired8 at4farms；最新当前岛存档KEM_FarmCat16，说明猫数量已实际收敛。残影ready只证初始化/触发，不等于肉眼画面排序颜色验收。证据留在observed/before-deployment日志。

公开5.0 ZIP/Release/tag完全未改。未获新发布授权，本轮未commit/push/发5.0.1；当前canonical在已发布分支之后保留未提交本机修复。
