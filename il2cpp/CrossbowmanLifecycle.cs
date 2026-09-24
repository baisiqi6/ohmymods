using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 弩手身份与战斗包的生命周期（可复用 marker + 显式有效身份 + life 边界）。
///
/// 背景（destroy-lifecycle-20260914）：旧实现用 <c>DestroyImmediate(marker)</c> 做池复用清污。
/// 转职发生在物理触发器回调（捡弓 OnTriggerStay/OnTriggerEnter2D）里，Unity 禁止在这些回调
/// 中立即销毁组件，且销毁不生效——旧组件继续被 GetComponent 命中，失效身份（招募排除、
/// 巡检强化、战斗参数）会留在池实例上；改成延迟 Destroy 又会误删同帧重建的新身份。
/// 修复方向：组件复用 + 显式身份（组件存在≠弩手），清污只改状态与字段，绝不销毁组件。
///
/// 身份两层（root/reviewer 定稿）：
/// - <see cref="CrossbowmanMarker.Selected"/>：**本 life** 的模组选择（捡弓第 4 个/读档重算写入）。
/// - <see cref="CrossbowmanMarker.Active"/>：**当前有效身份**（= Selected 且战斗包已提交、
///   未停用、配置开）。所有资格/排除/强化只认它。
/// - <see cref="CrossbowmanMarker.Residue"/>：属性可能留有我们的写入且尚未还原（异常/池残留）。
///
/// life 边界（native 实锤：`Pool.FastSpawn` 的 syncReceipt 分支可能**直接返回已 active 的同
/// NetID 对象**、不触发 OnEnable；只有真正从池 _cache 激活才会 SetActive(true)→OnEnable）：
/// - 新 life = "在 <see cref="BeginPoolSpawnScope"/> 作用域内发生真实 `Archer.OnEnable`"。
///   此时必须在**原生 OnEnable 主体之前**（Harmony prefix）清掉旧选择：原生 OnEnable 内部
///   `AddArcher`/`DistributeFreeArchers` 会立刻做招募判定，晚一步清就会把失效身份喂给判定。
/// - 重复收到已 active 的对象（同 NetID 回执）不触发 OnEnable → 不当作新 life，不动职业。
/// - 普通隐藏→重开（无池作用域）：保留 Selected，在 OnEnable prefix 里先把 Active 恢复回来
///   （让原生 OnEnable 里的招募判定看到正确身份），postfix 再修原生重置后的战斗包/外观。
/// - 原生 OnDisable（含非权威/非主场景的提前 return 分支）在 prefix 就失效 Active，
///   但**不**永久 Strip：OnDisable 只是隐藏，不总是池归还。
///
/// 其余契约：
/// - 战斗包私有写入用借用账本（Options/Defense 同款 before/applied 语义），且每条账本
///   "本实例是否捕获过"单独记账：冷却只乘一次、还原不重复回落 prefab 基线、异常重试不叠加。
/// - 同步重入（Defense/Animator/染衣回调里再进 Strip/Apply）由 <see cref="CrossbowmanMarker.Revision"/>
///   代次闸挡住：任何一步发现代次被更新就让出所有权，绝不覆盖更新的 life 状态。
/// - 配置关：读者即时失效（<see cref="IsCrossbowman"/> 直接读 Enabled），并由 Tick/巡检遍历
///   **自有 registry（含 inactive）** 还原解除，不依赖 5s 扫描窗口。
/// - 皮肤：Archer.soldierAnimator 生根到死地控制器（原生 biome 换皮对未注册 original
///   原样穿透 → 单写者），还原三处 = Strip / 池新 life 边界 / UnwindAll；
///   ConvertToHunter 方向由单写事件纠正兜底；不做每帧重断言（PR#58 皮肤守卫已退役）。
/// - 本文件绝不销毁任何组件；资产构建路径的 Destroy 属于宿主 EnsureAssets。
/// </summary>
internal static class CrossbowmanLifecycle
{
    // ---- 数值单一来源（宿主补丁与随从包共用） ----
    internal const float ShootRange = 12f;         // 基础弓 8
    internal const float ScaleY = 1.15f;           // 本体 y 缩放（坑11：只动 y，x 是朝向符号）
    internal const float IntervalMultiplier = 2f;  // 装填冷却 ×2

    // 日志上限：巡检/Tick 每拍都会跑，异常路径必须封顶（每路径每进程最多 3 条）
    private const int ErrorLogLimitPerPath = 3;
    private static int _applyErrorLogs;
    private static int _stripErrorLogs;
    private static int _readerErrorLogs;
    private static int _scanErrorLogs;
    private static bool _markerRegistered;

    // 自有 registry：仅装过弩手包的实例（数量 ≤ 曾当过弩手的池实例数）。
    // 用途：配置关的即时解除（含 inactive，扫描缓存看不到池中对象）。
    private static readonly List<CrossbowmanMarker> _owned = new List<CrossbowmanMarker>();

    // 皮肤生根（2026-09-24 死地弩手随从走路抽搐根治）：把实例的 Archer.soldierAnimator
    // （public 字段）直接指到死地控制器——原生 ConvertToSoldier 每次解析
    // BiomeData.GetAssetSwapForThis(soldierAnimator)，而 swap 表对未注册 original 原样穿透
    // （BiomeSwapData.GetAnimSwap: 未命中字典 → 返回入参），因此原生自己写出的就是同一引用
    // （同引用重赋无害）→ 单写者、零重绑、零竞态。
    // 原 PR#58 的每帧 _skinGuard/MaintainSkin 守卫已退役：字段生根后它是第三写者，
    // 只会重新引入重绑抖动。一次性穿透实证日志见 LogRootPassThroughOnce。
    private static bool _loggedRootPassThrough;

    // Pool.FastSpawn 作用域深度（prefix 自增 / finalizer 自减，异常安全）。
    private static int _poolSpawnDepth;

    // 缩放漂移诊断（宿主 5s 巡检顺带，只统计不改）
    private static int _scaleDriftCount;
    private static int _loggedScaleDrift = -1;
    private static string _scaleDriftSample;

    // ============================================================
    // 0. 注册、读取器、life 边界钩子
    // ============================================================

    /// <summary>
    /// CrossbowmanMarker 显式 ClassInjector 注册（先例 SpecialTowerRebuild.EnsureMarkerRegistered）。
    /// 不注册则 AddComponent/GetComponent/FindObjectsOfType 抛异常被吞，身份全链失效。
    /// </summary>
    internal static void EnsureMarkerRegistered()
    {
        if (_markerRegistered) return;
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(CrossbowmanMarker)))
        {
            ClassInjector.RegisterTypeInIl2Cpp(typeof(CrossbowmanMarker));
        }
        _markerRegistered = true;
    }

    /// <summary>
    /// 弩手资格唯一判据（组件存在不算）。外部读者：PatchRoles_CrossbowDefense / KnightStyle /
    /// NorseSquad / DefenseSpacing / SquadRefillDiag，全部经宿主转发。
    /// **即时读 ModConfig.Enabled**：全局关闭后任何读者立刻不再授予资格，不等巡检窗口。
    /// 异常一律 fail closed（当作非弩手）。
    /// </summary>
    internal static bool IsCrossbowman(Archer archer)
    {
        try
        {
            if (!ModConfig.Enabled.Value) return false;
            if (archer == null || archer.gameObject == null) return false;
            EnsureMarkerRegistered();
            CrossbowmanMarker marker = archer.GetComponent<CrossbowmanMarker>();
            return marker != null && marker.Active;
        }
        catch (Exception e)
        {
            LogBounded(ref _readerErrorLogs, "[Crossbowman/identity] ", e);
            return false;
        }
    }

    /// <summary>registry 里是否还有需要收尾/解除的实例（宿主 Tick 的 O(1) 早退闸）。</summary>
    internal static bool HasPendingWork => _owned.Count > 0;

    /// <summary>Pool.FastSpawn prefix：进入池生成作用域。</summary>
    internal static void BeginPoolSpawnScope()
    {
        _poolSpawnDepth++;
    }

    /// <summary>Pool.FastSpawn finalizer：退出池生成作用域（异常路径也会执行）。</summary>
    internal static void EndPoolSpawnScope()
    {
        if (_poolSpawnDepth > 0) _poolSpawnDepth--;
    }

    /// <summary>
    /// Archer.OnEnable **prefix**（原生主体之前）。
    /// - 池生成作用域内 = 真正的池激活（native：只有从 _cache 激活才 SetActive(true)→OnEnable；
    ///   同 NetID 回执直接返回已 active 对象不触发 OnEnable）→ 新 life：**在原生主体之前**把旧
    ///   life 的 owned 包完整还原。原生主体里的 AddArcher/DistributeFreeArchers 招募判定、
    ///   以及随从包（Attack/range/skin/scale/interval）都会随后写入这个对象；等主体结束再清
    ///   就会把这些新值当"旧 life 残留"覆盖掉——所以清污必须在主体之前完成。
    /// - 作用域外 = 普通隐藏重开（同一 life）→ 恢复本 life 的选择，供同一判定使用。
    /// </summary>
    internal static void OnArcherEnablePrefix(Archer archer, in CrossbowmanProfile profile)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return;
            EnsureMarkerRegistered();
            CrossbowmanMarker marker = archer.GetComponent<CrossbowmanMarker>();
            if (marker == null) return;

            if (_poolSpawnDepth > 0)
            {
                if (marker.Selected || marker.Active || marker.Residue) Strip(archer, profile);
                // 新 life 边界兜底（Strip 让出到 handoff/无状态时不落空）：池不重拷
                // 序列化字段，旧 life 的生根值绝不能带进新 life（漏还原=普通弓箭手
                // 穿死地皮事故）。已在基线上时零写入。
                RestoreSoldierAnimator(archer, profile);
                marker.PendingPoolHandoff = marker.Residue;
                return;
            }

            if (marker.Selected && !marker.Active)
                marker.Active = !marker.Residue && ModConfig.Enabled.Value;
        }
        catch (Exception e)
        {
            LogBounded(ref _readerErrorLogs, "[Crossbowman/identity] ", e);
        }
    }

    /// <summary>
    /// Archer.OnEnable **postfix**（原生主体之后，主体里可能已经写入了新 life 的战斗包/皮肤/缩放）。
    /// - 配置关 → 解除（此时通常已 settled，零写入）；
    /// - 仍欠还原（旧 life 清污在 prefix 失败）→ 只补**可确认属于我们**的字段（借用账本 +
    ///   SO/数值确认），绝不盲目覆盖主体里新写入的值；
    /// - 本 life 选择仍在且身份有效 → 自愈原生重置后的战斗包/外观；
    /// - 池新 life / 已是普通弓箭手（Selected=false 且无残留）→ **不动任何字段**。
    /// </summary>
    internal static void OnArcherEnablePostfix(Archer archer, in CrossbowmanProfile profile)
    {
        if (archer == null || archer.gameObject == null) return;
        try
        {
            EnsureMarkerRegistered();
            CrossbowmanMarker marker = archer.GetComponent<CrossbowmanMarker>();
            if (marker == null) return;

            if (!ModConfig.Enabled.Value)
            {
                if (marker.Selected || marker.Active || marker.Residue) Strip(archer, profile);
                return;
            }
            if (marker.Residue)
            {
                Strip(archer, profile);
                return;
            }
            if (marker.Active) Reconcile(archer, profile);
        }
        catch (Exception e)
        {
            LogBounded(ref _scanErrorLogs, "[Crossbowman/enable] ", e);
        }
    }

    /// <summary>
    /// Archer.OnDisable **prefix**（原生主体之前）：身份立即失效，但保留 Selected——
    /// OnDisable 只是隐藏，不总是池归还；真正的池新 life 由 OnEnable prefix 判定。
    /// 同时自增代次：在途的 Apply/Strip 让出所有权，绝不覆盖 Disable 后的状态。
    /// （原生 OnDisable 在非权威/非主场景会提前 return，且不恢复 interval/shootRange，
    /// 因此清理责任必须由我们自己的路径承担，不能依赖原生。）
    /// </summary>
    internal static void OnArcherDisablePrefix(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return;
            EnsureMarkerRegistered();
            CrossbowmanMarker marker = archer.GetComponent<CrossbowmanMarker>();
            if (marker == null) return;
            if (!marker.Selected && !marker.Active && !marker.Residue) return;
            marker.Revision++;
            marker.Active = false;
        }
        catch (Exception e)
        {
            LogBounded(ref _readerErrorLogs, "[Crossbowman/identity] ", e);
        }
    }

    // ============================================================
    // 1. Apply：打包（幂等；重入安全；整套写完才算完成）
    // ============================================================

    /// <summary>
    /// 赋弩手战斗包与身份（幂等，可反复调用）。返回是否提交完成。
    /// - 身份先提交再写包：`CrossbowDefense.ReconcileTowerRange` 的塔位增益分支以
    ///   IsCrossbowman 为门（旧实现同样是先挂 marker 再调用）；
    /// - 每步之间核 <see cref="CrossbowmanMarker.Revision"/>：一旦有同步重入（Defense/
    ///   Animator/染衣回调里再进 Strip/Apply）或对象被停用，立即让出，不写回旧状态；
    /// - 中途异常：把**本操作提交**的身份回滚为失效（更新的 life 状态不动），保留 Residue
    ///   让巡检按 Strip 收尾；账本保证冷却不二次乘。
    /// </summary>
    internal static bool Apply(Archer archer, in CrossbowmanProfile profile)
    {
        if (archer == null || archer.gameObject == null) return false;
        if (profile.Attack == null) return false; // 关键资产缺失：绝不半套（宿主负责日志去重）
        CrossbowmanMarker marker = null;
        int revision = 0;
        bool committed = false;
        try
        {
            EnsureMarkerRegistered();
            marker = archer.GetComponent<CrossbowmanMarker>();
            if (marker == null) marker = archer.gameObject.AddComponent<CrossbowmanMarker>();
            if (marker == null) return false;
            EnsureRegistered(marker);

            revision = ++marker.Revision;
            bool already = marker.Active;
            marker.PendingPoolHandoff = false;
            marker.Selected = true;
            if (!already)
            {
                marker.Residue = true;   // 属性即将被我们写脏：异常留给巡检收尾
                marker.Active = true;    // 先提交身份，供 Defense 塔位增益分支
                committed = true;
            }

            archer.ActiveArrowAttack = profile.Attack;
            archer.shootRange = ShootRange;
            Scanner scanner = archer._enemyScanner;
            if (scanner != null)
            {
                scanner.range = ShootRange;
                scanner.rangeBehind = ShootRange;
            }
            if (!Current(marker, revision)) return false;

            PatchRoles_CrossbowDefense.ReconcileTowerRange(archer);
            if (!Current(marker, revision)) return false;

            if (!already) ScaleIntervals(archer, marker);
            if (!Current(marker, revision)) return false;

            if (profile.Skin != null)
            {
                AssignController(archer, profile.Skin);
                // 生根：实例的 soldierAnimator 指到死地控制器。此后原生每次
                // ConvertToSoldier 解析出的都是同一控制器（未注册 original 原样穿透），
                // 原生写入与我们不再打架——这是抽搐的根治点。幂等：指针相等零写入。
                RootSoldierAnimator(archer, profile.Skin);
                LogRootPassThroughOnce(profile.Skin);
            }

            // 士兵皮肤的第二半：王国旗帜色染衣（宿主私有逻辑，自带早退与异常隔离）；
            // 记账"本 life 由我们染过"，Strip 时只清这一次（新 life 的染衣不被抹掉）。
            if (profile.ReapplyBanner != null)
            {
                profile.ReapplyBanner(archer);
                marker.OwnedBanner = true;
            }
            if (!Current(marker, revision)) return false;

            // 本体放大：y 轴绝对值 + ScaleRegistry 每帧守卫（Mover.Update postfix 重断言，
            // 池 respawn/原生重置都能自愈）；Strip 必须 Unregister + Restore。
            GreekScaleScope.ApplyY(archer.transform, ScaleY);
            ScaleRegistryHolder.Register(archer.GetComponent<Mover>(), ScaleY);

            if (!Current(marker, revision)) return false;
            marker.Residue = false;
            return true;
        }
        catch (Exception e)
        {
            LogBounded(ref _applyErrorLogs, "[Crossbowman/apply] ", e);
            if (committed && marker != null && marker.Revision == revision) marker.Active = false;
            return false;
        }
    }

    // ============================================================
    // 2. Strip：身份立即失效 + 还原 owned 属性（不销毁组件）
    // ============================================================

    /// <summary>
    /// 清污并还原（幂等，可在 inactive 对象上执行——只写字段不销毁任何东西）。
    /// 调用点：弓转职前的池复用清污、读档重算的非弩手分支、池新 life / 停用重开的残留收尾、
    /// 配置关解除。
    /// - 第一步清 Selected/Active 并置 Residue：立即失去资格/排除/强化，后续还原即使中途
    ///   异常也不会留下"假弩手"；
    /// - 冷却按借用账本还原：只回收仍是我们写入的值；本实例已归还过（Captured 且不再持有）
    ///   则**绝不**再回落 prefab 基线，避免重试把外部新值/custom before 改掉；
    /// - 每步核代次：同步重入（更新的 life）后立即让出，不覆盖新状态。
    /// </summary>
    internal static void Strip(Archer archer, in CrossbowmanProfile profile)
    {
        if (archer == null || archer.gameObject == null) return;
        try
        {
            EnsureMarkerRegistered();
            CrossbowmanMarker marker = archer.GetComponent<CrossbowmanMarker>();
            if (marker == null) return;
            // A native recruitment during OnEnable can hand this pooled instance to a
            // knight. Its Deadlands package deliberately shares our SO, range and scale;
            // equality of those values cannot establish ownership by the previous life.
            // 生根字段不在本分支还原：新 owner（随从路径）自行重写/还原，池边界
            // （OnArcherEnablePrefix→RestoreSoldierAnimator）负责清掉任何遗留值。
            if (marker.PendingPoolHandoff && !marker.Selected && archer._knight != null)
            {
                marker.Revision++;
                marker.Active = false;
                marker.Residue = false;
                marker.PendingPoolHandoff = false;
                marker.OwnedBanner = false;
                marker.IntervalScaled = false;
                marker.FormationScaled = false;
                UnregisterIfSettled(marker);
                return;
            }
            if (!marker.Selected && !marker.Active && !marker.Residue) return; // 无选择无残留：零写入

            int revision = ++marker.Revision;
            marker.Selected = false;
            marker.Active = false;   // 立即失效（先于一切属性写）
            marker.Residue = true;   // 还原完成前保持；中途异常由巡检继续收尾

            PatchRoles_CrossbowDefense.Remove(archer);

            // 只回收**可确认属于我们**的写入：原生/其他系统（随从包等）在池激活主体里
            // 新写的值一律不动。箭按我们持有的 SO 指针确认；已被原生重置或换成火矢/别的
            // SO 时保持现状。
            ArrowAttack currentAttack = archer.ActiveArrowAttack;
            if (profile.Attack != null && currentAttack != null
                && currentAttack.Pointer == profile.Attack.Pointer)
            {
                archer.ActiveArrowAttack = archer._arrowAttack;
            }
            if (!Current(marker, revision)) return;

            RestoreIntervals(archer, marker, profile);
            RestoreMovement(archer, profile);
            if (!Current(marker, revision)) return;

            // 先还原生根字段、再按原生语义解析猎人皮（RestoreSkin 绝不读被污染的字段）。
            RestoreSoldierAnimator(archer, profile);
            RestoreSkin(archer, profile);

            // 衣服颜色不还原（原生路径会自然重掷），只清我们自己染的那次：
            // 新 life 的染衣（随从包/原生 ConvertToSoldier）绝不被我们抹掉。
            if (marker.OwnedBanner)
            {
                archer._isWearingBannerColor = false;
                marker.OwnedBanner = false;
            }
            if (!Current(marker, revision)) return;

            Mover stripMover = archer.GetComponent<Mover>();
            ScaleRegistryHolder.Unregister(stripMover);
            GreekScaleScope.Restore(archer.transform);

            if (!Current(marker, revision)) return;
            marker.Residue = false;
            marker.PendingPoolHandoff = false;
            UnregisterIfSettled(marker);
        }
        catch (Exception e)
        {
            LogBounded(ref _stripErrorLogs, "[Crossbowman/strip] ", e);
        }
    }

    // ============================================================
    // 3. Reconcile：5s 巡检单拍（强化 / 收尾 / 配置关解除）
    // ============================================================

    /// <summary>
    /// 巡检单拍。返回收尾后身份是否有效（宿主漂移诊断用）。
    /// - 配置关 / 无身份但有选择或残留（含异常中断、池新 life 遗留）：Strip 收尾/解除，
    ///   inactive 也照做（只写字段，不依赖扫描缓存）；
    /// - 有效身份但对象停用：不动（身份已在 OnDisable prefix 失效；重开时 postfix 自愈）；
    /// - 有效身份且启用：只兜原生重置的字段（OnEnable/网络收包/换皮被池路径重置）。
    ///   只碰 ActiveArrowAttack（等于原生基础箭才修复）、shootRange、Animator、生根的
    ///   soldierAnimator（指针校验）、旗帜色与守墙目标；塔位扫描器由 CrossbowDefense 收敛；
    ///   不碰射击间隔（buff/阵形可能合法修改），火矢 buff（_fireArrowAttack）期间绝不动箭。
    /// </summary>
    internal static bool Reconcile(Archer archer, in CrossbowmanProfile profile)
    {
        if (archer == null || archer.gameObject == null) return false;

        CrossbowmanMarker marker;
        try
        {
            EnsureMarkerRegistered();
            marker = archer.GetComponent<CrossbowmanMarker>();
        }
        catch (Exception e)
        {
            LogBounded(ref _readerErrorLogs, "[Crossbowman/identity] ", e);
            return false;
        }
        if (marker == null) return false;

        if (!ModConfig.Enabled.Value)
        {
            if (marker.Selected || marker.Active || marker.Residue) Strip(archer, profile);
            return false;
        }
        if (!archer.gameObject.activeInHierarchy)
        {
            // 隐藏/池中（OnDisable 只是隐藏，不总是池归还）：身份已由 OnDisable prefix 失效，
            // **本 life 的选择必须保留**——重开还是弩手。只有确认还欠还原（Residue）才收尾；
            // 停用 + Selected + 无残留时零写入。（旧 UnitScanCache 数组可能仍含这个 marker，
            // 这里的判定就是为它准备的。）
            if (marker.Residue) Strip(archer, profile);
            return false;
        }
        if (!marker.Active)
        {
            if (marker.Selected || marker.Residue) Strip(archer, profile);
            return false;
        }

        int revision = ++marker.Revision;
        try
        {
            if (profile.Attack != null)
            {
                ArrowAttack current = archer.ActiveArrowAttack;
                ArrowAttack native = archer._arrowAttack;
                // 仅当被重置回原生基础箭（Awake/OnEnable/网络收包路径）时修复；
                // 火矢 buff（_fireArrowAttack）期间绝不动——与原生 buff 的兼容契约。
                if (current != null && native != null && current.Pointer == native.Pointer)
                    archer.ActiveArrowAttack = profile.Attack;
            }

            if (archer.shootRange != ShootRange) archer.shootRange = ShootRange;
            if (!Current(marker, revision)) return false;
            PatchRoles_CrossbowDefense.ReconcileTowerRange(archer);
            if (!Current(marker, revision)) return false;

            if (profile.Skin != null)
            {
                ReassertController(archer, profile.Skin);
                // 生根字段的指针校验（稳态零写入）：池/原生重置把 soldierAnimator
                // 翻回基线时补种，否则下一次原生 ConvertToSoldier 会解析出原生皮。
                RootSoldierAnimator(archer, profile.Skin);
            }

            // 原生 ConvertToHunter（下塔/下船/离队/死亡清理）会重掷随机衣色并清
            // _isWearingBannerColor；标记被清说明衣色丢了，补染回旗帜色（幂等）。
            if (profile.ReapplyBanner != null) profile.ReapplyBanner(archer);
            if (!Current(marker, revision)) return false;

            // 使用同一夜间守墙策略兜底，不与原生/DefenseSpacing 反复拉扯
            PatchRoles_CrossbowDefense.TryPullBack(archer);

            TrackScaleDrift(archer);
        }
        catch (Exception e)
        {
            LogBounded(ref _scanErrorLogs, "[Crossbowman/integrity] ", e);
        }
        return true;
    }

    /// <summary>
    /// 巡检批次：宿主 IntegrityPass 把 UnitScanCache.GetCrossbowmanMarkers() 的结果
    /// （5s 窗口缓存，只含 active 对象）整批交进来。缓存数组里可能有已销毁的假 null、
    /// 宿主组件已消失的孤儿 marker——只清状态标志，绝不销毁组件。
    /// </summary>
    internal static void ReconcileScan(CrossbowmanMarker[] markers, in CrossbowmanProfile profile)
    {
        if (markers == null) return;
        for (int i = 0; i < markers.Length; i++)
        {
            CrossbowmanMarker marker = markers[i];
            if (marker == null) continue;

            Archer archer;
            try
            {
                archer = marker.GetComponent<Archer>();
            }
            catch (Exception e)
            {
                LogBounded(ref _scanErrorLogs, "[Crossbowman/integrity] ", e);
                continue;
            }

            if (archer == null || archer.gameObject == null)
            {
                marker.Revision++;
                marker.Selected = false;
                marker.Active = false;
                marker.Residue = false;
                continue;
            }

            Reconcile(archer, profile);
        }

        if (_scaleDriftCount > 0 && _loggedScaleDrift != _scaleDriftCount)
        {
            _loggedScaleDrift = _scaleDriftCount;
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[Crossbowman] scale drift: marked=" + markers.Length
                + " drifted=" + _scaleDriftCount
                + " sample[" + (_scaleDriftSample ?? "<none>") + "]");
        }
        _scaleDriftCount = 0;
        _scaleDriftSample = null;
    }

    /// <summary>
    /// 全局关闭 / 世界清理：遍历**自有 registry（含 inactive）**把所有带选择/身份/残留的
    /// 实例还原解除。不依赖扫描缓存（池中对象不在缓存里），也不依赖原生 OnDisable
    /// （它在非权威/非主场景会提前 return）。全清后 registry 为空，宿主 Tick 回到 O(1)。
    /// </summary>
    internal static void UnwindAll(in CrossbowmanProfile profile)
    {
        if (_owned.Count == 0) return;
        // 快照遍历：Strip 收尾成功会同步把自己从 registry 移除（UnregisterIfSettled），
        // 边遍历边改 List 会越界/漏项。
        CrossbowmanMarker[] snapshot = _owned.ToArray();
        for (int i = 0; i < snapshot.Length; i++)
        {
            CrossbowmanMarker marker = snapshot[i];
            if (marker == null) continue;

            Archer archer;
            try
            {
                archer = marker.GetComponent<Archer>();
            }
            catch (Exception e)
            {
                LogBounded(ref _scanErrorLogs, "[Crossbowman/unwind] ", e);
                continue;
            }

            if (archer == null || archer.gameObject == null)
            {
                marker.Revision++;
                marker.Selected = false;
                marker.Active = false;
                marker.Residue = false;
                continue;
            }

            Strip(archer, profile);
            // 全局解除/世界清理：strip 让出到 handoff 分支时也不留下生根残留。
            RestoreSoldierAnimator(archer, profile);
        }
        PruneOwned();
    }

    /// <summary>清掉失效/已收尾条目（收尾失败者留在 registry 等下一拍继续）。</summary>
    private static void PruneOwned()
    {
        for (int i = _owned.Count - 1; i >= 0; i--)
        {
            CrossbowmanMarker marker = _owned[i];
            if (marker == null || (!marker.Selected && !marker.Active && !marker.Residue))
                _owned.RemoveAt(i);
        }
    }

    // ============================================================
    // 4. 私有：registry / 冷却借用账本 / 属性还原 / 诊断
    // ============================================================

    private static void EnsureRegistered(CrossbowmanMarker marker)
    {
        if (marker == null) return;
        if (_owned.Contains(marker)) return;
        _owned.Add(marker);
    }

    /// <summary>选择/身份/残留全部清空后才销账；否则留在 registry 里等下一拍收尾。</summary>
    private static void UnregisterIfSettled(CrossbowmanMarker marker)
    {
        if (marker == null) return;
        if (marker.Selected || marker.Active || marker.Residue) return;
        _owned.Remove(marker);
    }

    /// <summary>本操作是否仍是当前所有者（同步重入/停用会让出代次）。</summary>
    private static bool Current(CrossbowmanMarker marker, int revision)
        => marker != null && marker.Revision == revision;

    /// <summary>
    /// 冷却 ×2（借用账本，Options/Defense 同款 before/applied 语义）：读现值乘，记账先于写入。
    /// 已捕获且现值仍等于我们写入的值 → 跳过（重复 Apply/异常重试绝不二次乘）；被外部替换过
    /// → 按现值重新乘一次并覆盖账本。两条 interval 各自独立记账。
    /// </summary>
    private static void ScaleIntervals(Archer archer, CrossbowmanMarker marker)
    {
        Vector2 interval = archer._shootIntervalRange;
        if (!marker.IntervalScaled || !Same(interval, marker.IntervalApplied))
        {
            marker.IntervalBefore = interval;
            marker.IntervalCaptured = true;
            interval.x *= IntervalMultiplier;
            interval.y *= IntervalMultiplier;
            marker.IntervalApplied = interval;
            marker.IntervalScaled = true;
            archer._shootIntervalRange = interval;
        }

        Vector2 formation = archer._shootIntervalRangeFormation;
        if (!marker.FormationScaled || !Same(formation, marker.FormationApplied))
        {
            marker.FormationBefore = formation;
            marker.FormationCaptured = true;
            formation.x *= IntervalMultiplier;
            formation.y *= IntervalMultiplier;
            marker.FormationApplied = formation;
            marker.FormationScaled = true;
            archer._shootIntervalRangeFormation = formation;
        }
    }

    /// <summary>
    /// 冷却还原：持有中且现值仍是我们写入的 → 归还 Before；已被外部替换 → 释放账本、不覆写。
    /// **只有本实例从未捕获过**（legacy/纯外部实例）才回落 prefab 基线：一旦捕获过，重试
    /// Strip 绝不再回落，否则会把已归还的 custom before 或外部新值改回 prefab 默认值。
    /// </summary>
    private static void RestoreIntervals(Archer archer, CrossbowmanMarker marker, in CrossbowmanProfile profile)
    {
        if (marker.IntervalScaled)
        {
            if (Same(archer._shootIntervalRange, marker.IntervalApplied))
                archer._shootIntervalRange = marker.IntervalBefore;
            marker.IntervalScaled = false;
        }
        else if (!marker.IntervalCaptured && profile.BaseIntervalKnown)
        {
            archer._shootIntervalRange = profile.BaseInterval;
        }

        if (marker.FormationScaled)
        {
            if (Same(archer._shootIntervalRangeFormation, marker.FormationApplied))
                archer._shootIntervalRangeFormation = marker.FormationBefore;
            marker.FormationScaled = false;
        }
        else if (!marker.FormationCaptured && profile.BaseIntervalFormationKnown)
        {
            archer._shootIntervalRangeFormation = profile.BaseIntervalFormation;
        }
    }

    /// <summary>
    /// 射程还原：**只在当前值仍是我们的 ShootRange 时**才动（借用语义）——原生/随从包在池
    /// 激活主体里新写的射程绝不被覆盖。地面回基线；扫描器按所在位置还原——塔位 guard slot
    /// 上的原生值是 towerShootRange（Archer.cs:848 EnterGuardSlot；1390 ExitGuardSlot 恢复
    /// shootRange），否则塔上被恢复的弩手索敌范围会被压回地面值。
    /// </summary>
    private static void RestoreMovement(Archer archer, in CrossbowmanProfile profile)
    {
        if (!profile.BaseShootRangeKnown) return;
        if (archer.shootRange != ShootRange) return;
        archer.shootRange = profile.BaseShootRange;
        Scanner scanner = archer._enemyScanner;
        if (scanner == null) return;
        float restoreRange = archer.inGuardSlot || archer._guardSlot != null
            ? archer.towerShootRange
            : profile.BaseShootRange;
        scanner.range = restoreRange;
        scanner.rangeBehind = restoreRange;
    }

    /// <summary>
    /// 皮肤还原：走原生 ConvertToHunter 同款 biome swap（Archer.cs:889），跨世界也能还原对应
    /// 世界的猎人皮肤；swap 不可用（无资产/表未就绪抛异常）时回落缓存基座控制器。
    /// 只在控制器仍是我们的弩手皮肤（或 profile 没给皮肤）时才换——新 life 的随从/风格皮肤
    /// 绝不被覆盖。
    /// </summary>
    private static void RestoreSkin(Archer archer, in CrossbowmanProfile profile)
    {
        Animator animator = archer.GetComponentInChildren<Animator>();
        if (animator == null || animator.runtimeAnimatorController == null) return;
        if (profile.Skin != null
            && animator.runtimeAnimatorController.Pointer != profile.Skin.Pointer) return;

        RuntimeAnimatorController hunter = null;
        try
        {
            hunter = archer.hunterAnimator != null && BiomeData.Current != null
                ? BiomeData.Current.GetAssetSwapForThis<RuntimeAnimatorController>(archer.hunterAnimator)
                : null;
        }
        catch (Exception) { /* swap 表未就绪等：走缓存回落 */ }

        if (hunter != null) animator.runtimeAnimatorController = hunter;
        else if (profile.BaseSkin != null) animator.runtimeAnimatorController = profile.BaseSkin;
    }

    private static void AssignController(Archer archer, RuntimeAnimatorController controller)
    {
        Animator animator = archer.GetComponentInChildren<Animator>();
        if (animator != null && animator.runtimeAnimatorController != null)
            animator.runtimeAnimatorController = controller;
    }

    /// <summary>
    /// Archer.ConvertToHunter **postfix**（native 下塔/下船/离队/死亡清理走本路径）：替代已退役的
    /// 每帧皮肤守卫的**单写事件纠正**——身份仍有效（marker.Active；死亡/离队/停用已由 OnDisable
    /// prefix 先行失效）→ 把控制器与生根字段各写回死地控制器一次（指针相等零写入）；身份无效
    /// （死亡/退队/池中）→ 不碰，维持既有"猎人皮播死亡动画"设计。不做每帧、不翻牌。
    /// </summary>
    internal static void OnConvertToHunterPostfix(Archer archer, in CrossbowmanProfile profile)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return;
            if (profile.Skin == null) return; // 皮肤资产缺失：只管功能，外观走原生
            EnsureMarkerRegistered();
            CrossbowmanMarker marker = archer.GetComponent<CrossbowmanMarker>();
            if (marker == null || !marker.Active) return;
            ReassertController(archer, profile.Skin);
            RootSoldierAnimator(archer, profile.Skin);
        }
        catch (Exception e)
        {
            LogBounded(ref _scanErrorLogs, "[Crossbowman/hunter] ", e);
        }
    }

    /// <summary>
    /// 单次控制器断言（指针相等零写入）。Reconcile 巡检与 ConvertToHunter 事件纠正共用。
    /// </summary>
    private static void ReassertController(Archer archer, RuntimeAnimatorController skin)
    {
        Animator animator = archer.GetComponentInChildren<Animator>();
        if (animator == null || animator.runtimeAnimatorController == null) return;
        if (animator.runtimeAnimatorController.Pointer == skin.Pointer) return;
        animator.runtimeAnimatorController = skin;
    }

    /// <summary>
    /// 生根写入（幂等：指针相等零写入）。skin 为死地控制器=生根；为 prefab 快照=还原。
    /// </summary>
    private static void RootSoldierAnimator(Archer archer, RuntimeAnimatorController skin)
    {
        if (skin == null) return;
        RuntimeAnimatorController current = archer.soldierAnimator;
        if (current != null && current.Pointer == skin.Pointer) return;
        archer.soldierAnimator = skin;
    }

    /// <summary>
    /// 生根字段还原：soldierAnimator 写回原生 Archer prefab 快照（profile.BaseSoldierAnimator）。
    /// 调用点=Strip（先于猎人皮解析）/ 池新 life 边界 / UnwindAll 三处——池不重拷序列化字段，
    /// 漏还原=普通弓箭手穿死地皮事故。快照缺失（holder 未就绪）→ 不写（下次入口再试）；
    /// 已在基线上 → 零写入。
    /// </summary>
    private static void RestoreSoldierAnimator(Archer archer, in CrossbowmanProfile profile)
    {
        RuntimeAnimatorController baseAnimator = profile.BaseSoldierAnimator;
        if (baseAnimator == null) return;
        RootSoldierAnimator(archer, baseAnimator);
    }

    /// <summary>
    /// 一次性穿透实证日志（用户实机排障定稿）：死地控制器不在 biome 动画换皮表中 →
    /// GetAssetSwapForThis(deadlands) 必须原样返回 deadlands，原生 ConvertToSoldier
    /// 才会写出同一引用（单写者语义）。每进程一行。
    /// </summary>
    private static void LogRootPassThroughOnce(RuntimeAnimatorController skin)
    {
        if (_loggedRootPassThrough) return;
        _loggedRootPassThrough = true;
        try
        {
            BiomeData biome = BiomeData.Current;
            if (biome == null)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[Crossbowman] soldierAnimator rooted to " + skin.name
                    + "; swap check skipped (no biome)");
                return;
            }
            RuntimeAnimatorController resolved = biome.GetAssetSwapForThis<RuntimeAnimatorController>(skin);
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[Crossbowman] soldierAnimator rooted to " + skin.name + "; swap pass-through="
                + (resolved != null && resolved.Pointer == skin.Pointer));
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[Crossbowman] swap pass-through check failed: " + e.GetType().Name);
        }
    }

    /// <summary>
    /// 缩放漂移诊断（用户报告"地面弩手有的高有的低"）：只统计不改——守卫每帧都在断言仍有
    /// 漂移，说明存在更晚的写入者（怀疑动画器 scale 曲线，其在 Mover.Update 之后评估）。
    /// </summary>
    private static void TrackScaleDrift(Archer archer)
    {
        if (Mathf.Abs(archer.transform.localScale.y - ScaleY) <= 0.02f) return;
        _scaleDriftCount++;
        if (_scaleDriftSample != null) return;
        Animator a = archer.GetComponentInChildren<Animator>();
        _scaleDriftSample = "y=" + archer.transform.localScale.y.ToString("F3")
            + " animatorOnRoot=" + (a != null && a.transform == archer.transform)
            + " controller="
            + (a != null && a.runtimeAnimatorController != null
                ? a.runtimeAnimatorController.name : "<null>");
    }

    private static void LogBounded(ref int counter, string tag, Exception error)
    {
        if (counter >= ErrorLogLimitPerPath) return;
        counter++;
        KingdomEnhancedPlugin.Instance?.LogSource.LogError(tag + error);
    }

    private static bool Same(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;
}

/// <summary>
/// 弩手标记（挂在弩手 Archer 的 gameObject 上，池实例复用同一组件）。
///
/// 组件存在≠弩手：<see cref="Selected"/> 是"本 life 的选择"，<see cref="Active"/> 是"当前
/// 有效身份"（资格/排除/强化的唯一判据），<see cref="Residue"/> 是"有待还原的私有写入"。
/// 绝不销毁/延迟销毁本组件——池实例下一 life 直接复用同一 marker；真正的 life 边界由
/// `Pool.FastSpawn` 作用域 + 原生 `Archer.OnEnable` 确认（同 NetID 回执的已 active 对象
/// 不触发 OnEnable，因此不会被误当新 life）。
/// 按 SpecialTowerRebuildMarker 先例显式 ClassInjector 注册。
/// </summary>
public sealed class CrossbowmanMarker : MonoBehaviour
{
    public CrossbowmanMarker(IntPtr pointer) : base(pointer)
    {
    }

    /// <summary>本 life 的模组选择（捡弓第 4 个/读档重算写入；池新 life、清污、配置关清除）。</summary>
    internal bool Selected;

    /// <summary>当前有效身份：= Selected 且战斗包已提交、未停用、配置开。唯一资格来源。</summary>
    internal bool Active;

    /// <summary>属性可能留有我们的写入且尚未还原（异常中断/池残留/停用期间的包）：待收尾。</summary>
    internal bool Residue;

    /// <summary>Pool prefix cleanup failed; a later native knight assignment supersedes this receipt.</summary>
    internal bool PendingPoolHandoff;

    /// <summary>代次：每次状态变更自增；同步重入/停用时让在途操作让出所有权。</summary>
    internal int Revision;

    /// <summary>我们染过本 life 的旗帜色（Strip 只清这一次，新 life 的染衣不被抹掉）。</summary>
    internal bool OwnedBanner;

    // 冷却借用账本（先例 PatchArcher_Options Fields / Defense TowerState），两条 interval 独立：
    // Captured 一旦置位永不清除 —— 保证重试 Strip 不会把已归还值/外部新值回落成 prefab 基线。
    internal bool IntervalScaled;
    internal bool IntervalCaptured;
    internal Vector2 IntervalBefore;
    internal Vector2 IntervalApplied;
    internal bool FormationScaled;
    internal bool FormationCaptured;
    internal Vector2 FormationBefore;
    internal Vector2 FormationApplied;
}

/// <summary>
/// 宿主（PatchRoles_Crossbowman）提供的战斗包资产与原生基线快照，值语义便于测试直接构造。
/// </summary>
internal struct CrossbowmanProfile
{
    /// <summary>克隆弩矢 ArrowAttack；null → Apply 放弃（绝不半套）。</summary>
    internal ArrowAttack Attack;

    /// <summary>死地士兵动画控制器；null → 保留当前控制器（只缺皮肤，功能继续）。</summary>
    internal RuntimeAnimatorController Skin;

    /// <summary>旗帜染衣（宿主私有逻辑，自带早退与异常隔离）；null → 跳过。</summary>
    internal Action<Archer> ReapplyBanner;

    internal float BaseShootRange;
    internal bool BaseShootRangeKnown;
    internal Vector2 BaseInterval;
    internal bool BaseIntervalKnown;
    internal Vector2 BaseIntervalFormation;
    internal bool BaseIntervalFormationKnown;

    /// <summary>猎人换皮不可用时的回落控制器。</summary>
    internal RuntimeAnimatorController BaseSkin;

    /// <summary>原生 Archer prefab 的 soldierAnimator 快照（生根还原用；null → 跳过还原）。</summary>
    internal RuntimeAnimatorController BaseSoldierAnimator;
}
