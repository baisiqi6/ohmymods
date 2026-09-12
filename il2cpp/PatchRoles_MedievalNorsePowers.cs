using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 骑士风格战斗/经济强化（knight-range-wallet）：
/// - 中世纪（style 0）：近战劈砍范围 ×1.5 + 匹配命中盒（只扩正向 xMax，xMin/
///   Y 不变）+ 纯视觉剑风弧线（LineRenderer，0.2s 淡出，不新增动画资产）。
/// - 北境（style 4）：骑士自身钱包（_originalWallet，已验证挂在骑士 GO 上）
///   容量 ×2、纳税阈值 ×2，含神像目标 ×2；不送币、绝不动玩家当前钱包
///  （只写 _originalWallet，不读写 _wallet）、绝不改 _statueBuffMaxCoins。
///
/// 关键约束（任务书 + CORRECTIONS）：
/// - 基线只在世界首次 Awake postfix 捕获一次，跨读档/池复用保留（指针校验的
///   活基线）；巡检只清理已销毁条目，绝不清空全表，绝不把强化值再当基线。
/// - OnDisable 无条件恢复序列化基线（钱包容量/阈值 + 范围，不用神像目标）；
///   风格丢失/关模组的恢复用"当前神像或序列化目标 max 现有币"。钱包池
///   Enable/Disable 不重置容量/阈值，基线保留即池复用安全。
/// - 客户端原生 OnEnable 提前返回、statueBuffActive 未赋值：神像态用确定性
///   CampaignSaveData.GetDeityStatus(Statue.Deity.Knight) 判定（战役缺失时
///   安全回落 statueBuffActive）。
/// - ShouldSlash 放宽仅在：原生返回 false + Enabled + 世界权威 + style 0 +
///   强化已生效；先写 _enemy 再翻转结果。
/// - Wallet.Persistent_IBehaviour_ApplyData 原生直写 _currencyAmount 无 clamp：
///   不补 SetCurrency（防任意抬容量）；ApplyData postfix 对自身钱包重算目标
///   （容量 = max(目标, Coins)，绝不写币数）。
/// - 节奏：事件 + 主机 Knight.Update 逐帧幂等收敛（关模组也走恢复路径）+
///   双端 2s 巡检（UnitScanCache 共享缓存）；日志一次性去重。
/// </summary>
internal static class PatchRoles_MedievalNorsePowers
{
    private const int MedievalStyleIndex = 0;
    private const int NorseStyleIndex = 4;
    private const float SlashRangeMultiplier = 1.5f;
    private const float PatrolIntervalSeconds = 2f;

    // ---- 剑风弧线（纯视觉）----
    private const float ArcDuration = 0.2f;          // 任务书允许 0.15-0.25s
    private const int ArcSegments = 12;
    private const float ArcBaseColorAlpha = 0.85f;
    private const int ArcFallbackSortingOrder = 60;

    // ---- 每骑士状态（instanceID 键控；跨读档/池复用保留，只清已销毁条目）----
    internal sealed class KnightPowerState
    {
        internal Knight Knight;
        internal bool BaseCaptured;

        // 中世纪（Awake 捕获的序列化基线）
        internal float BaseSlashRange;
        internal Rect BaseHitBox;
        internal bool RangeBoostApplied;
        internal bool ScannerRangeBoosted;
        internal float ScannerRangeBefore;

        // 北境（Awake 时 _originalWallet 的未强化基线）
        internal int BaseCapacity = -1;
        internal int BaseTaxes = -1;
        internal bool WalletBoostApplied;
        internal bool WalletNeedsBaselineReset;

        // 剑风视觉（自持有子对象，随骑士 GO 销毁；失败时原子清理无孤儿）
        internal GameObject ArcObject;
        internal KnightWindArcBehaviour ArcBehaviour;
    }

    private static readonly Dictionary<int, KnightPowerState> States = new();
    // ApplyData postfix 反查：自身钱包指针 → 骑士状态键（instanceID）
    private static readonly Dictionary<IntPtr, int> WalletOwners = new();
    private static readonly List<int> PruneBuffer = new(); // 复用缓冲，无逐轮分配
    private static readonly List<IntPtr> WalletPruneBuffer = new();

    // ---- 协程守卫 / 一次性日志 ----
    private static World _patrolWorld;
    private static readonly HashSet<string> LoggedOnce = new();
    private static Material _arcFallbackMaterial;
    private static bool _arcTypeRegisterFailed;

    internal static void LogInfoOnce(string key, string message)
    {
        if (!LoggedOnce.Add(key)) return;
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[MedievalNorse] " + message);
    }

    internal static void LogWarnOnce(string key, string message)
    {
        if (!LoggedOnce.Add(key)) return;
        KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[MedievalNorse] " + message);
    }

    internal static void LogErrorOnce(string key, Exception exception)
    {
        if (!LoggedOnce.Add(key)) return;
        KingdomEnhancedPlugin.Instance?.LogSource.LogError("[MedievalNorse] " + key + ": " + exception);
    }

    internal static void LogHookError(string hook, Exception exception)
    {
        LogErrorOnce("hook:" + hook, exception);
    }

    /// <summary>饱和翻倍：基线非正原样保留（含哨兵值），溢出钳到 int.MaxValue。</summary>
    private static int SaturatingDouble(int value)
    {
        if (value <= 0) return value;
        return value > int.MaxValue / 2 ? int.MaxValue : value * 2;
    }

    // ============================================================
    // 状态表
    // ============================================================

    private static KnightPowerState GetState(Knight knight)
    {
        int id = knight.gameObject.GetInstanceID();
        if (!States.TryGetValue(id, out KnightPowerState state)
            || state.Knight == null
            || state.Knight.Pointer != knight.Pointer)
        {
            // 新骑士，或 instanceID 被 Unity 复用给了不同实例：旧记录不可信，重建
            //（基线随 Awake 重新捕获；池复用同实例同指针 → 命中保留分支，基线不丢）
            state = new KnightPowerState { Knight = knight };
            States[id] = state;
        }
        else
        {
            state.Knight = knight;
        }
        RegisterWalletOwner(knight, state, id);
        return state;
    }

    private static void RegisterWalletOwner(Knight knight, KnightPowerState state, int id)
    {
        try
        {
            Wallet own = knight._originalWallet;
            if (own != null && (!WalletOwners.TryGetValue(own.Pointer, out int ownerId) || ownerId != id))
                WalletOwners[own.Pointer] = id;
        }
        catch (Exception e)
        {
            LogErrorOnce("wallet owner register failed", e);
        }
    }

    /// <summary>验证 _originalWallet 确实挂在骑士自己的 GO 上（防指针串门）。</summary>
    private static bool OwnsWallet(Knight knight, Wallet wallet)
    {
        try
        {
            return wallet.gameObject != null
                && knight.gameObject != null
                && wallet.gameObject.GetInstanceID() == knight.gameObject.GetInstanceID();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 神像态判定（双端确定性）：客户端原生 OnEnable 提前返回、不写
    /// statueBuffActive，用 CampaignSaveData.GetDeityStatus(Statue.Deity.Knight)
    /// 判定（与原生 OnEnable 同一表达式）；战役未加载等异常时安全回落
    /// knight.statueBuffActive（主机侧原生维护，始终正确）。
    /// </summary>
    private static bool IsStatueActive(Knight knight)
    {
        try
        {
            if (CampaignSaveData.current != null)
                return CampaignSaveData.GetDeityStatus(Statue.Deity.Knight, null)
                    == Statue.DeityStatus.Activated;
        }
        catch { }
        try { return knight.statueBuffActive; }
        catch { return false; }
    }

    // ============================================================
    // 生命周期：Awake 捕获基线 / OnDisable 无条件恢复序列化基线
    // ============================================================

    /// <summary>
    /// 基线捕获（每实例仅首次生成时执行；池复用不重跑 Awake，基线跨生命周期
    /// 保留）：_slashRange/_hitBox 原生序列化值 + _originalWallet 的未强化
    /// 容量/阈值（早于 OnEnable 神像逻辑）。OnDisable 恢复保证下一生命周期
    /// 起点为纯序列化基线，池 Enable/Disable 不重置钱包值也不会累积。
    /// </summary>
    internal static void OnKnightAwake(Knight knight)
    {
        try
        {
            KnightPowerState state = GetState(knight);
            if (state.BaseCaptured) return;
            state.BaseSlashRange = knight._slashRange;
            state.BaseHitBox = knight._hitBox;
            Wallet own = knight._originalWallet;
            if (own != null && OwnsWallet(knight, own))
            {
                state.BaseCapacity = own.TotalCapacity;
                state.BaseTaxes = own.payTaxesAbove;
            }
            else
            {
                LogWarnOnce("original-wallet-missing",
                    "Knight._originalWallet unavailable or not on knight GO; norse wallet boost disabled for this instance");
            }
            state.BaseCaptured = true;
        }
        catch (Exception e)
        {
            LogErrorOnce("awake baseline capture failed", e);
        }
    }

    /// <summary>
    /// 池复用/离场清理：无条件恢复序列化基线（钱包容量/阈值与范围都用 Awake
    /// 基线，不用神像目标——下次 OnEnable 原生神像逻辑会自行重设），并收起
    /// 剑风。Enabled=false 也走这里。只写自身钱包，不碰 _wallet。
    /// </summary>
    internal static void OnKnightDisabled(Knight knight)
    {
        try
        {
            KnightPowerState state = GetState(knight);
            RestoreRangeBoost(knight, state);
            RestoreWalletToBaseline(knight, state);
            HideArc(state);
        }
        catch (Exception e)
        {
            LogErrorOnce("on-disable restore failed", e);
        }
    }

    // ============================================================
    // 收敛入口：幂等 Reconcile（关模组也执行，走恢复路径）
    // ============================================================

    internal static void Reconcile(Knight knight)
    {
        if (knight == null || knight.gameObject == null) return;
        try
        {
            KnightPowerState state = GetState(knight);
            if (!state.BaseCaptured) return;

            int style = -1;
            bool hasStyle = ModConfig.Enabled.Value
                && PatchRoles_KnightStyle.TryGetResolvedStyleIndex(knight, out style);

            bool wantRange = hasStyle && style == MedievalStyleIndex;
            bool wantWallet = hasStyle && style == NorseStyleIndex;

            ApplyRangeBoost(knight, state, wantRange);
            ApplyWalletBoost(knight, state, wantWallet);
            if (!wantRange) HideArc(state); // 风格丢失/关模组：收起活动剑风
        }
        catch (Exception e)
        {
            LogErrorOnce("reconcile failed", e);
        }
    }

    // ============================================================
    // A. 中世纪：劈砍范围 ×1.5 + 命中盒正向延伸 + 扫描器覆盖
    // ============================================================

    private static void ApplyRangeBoost(Knight knight, KnightPowerState state, bool want)
    {
        if (want && !state.RangeBoostApplied)
        {
            state.RangeBoostApplied = true; // retain cleanup ownership if a native setter throws
            float boosted = state.BaseSlashRange * SlashRangeMultiplier;
            knight._slashRange = boosted;

            // 只扩正向边：xMax ×1.5（相对 0 缩放，后缘为负时不受影响），
            // xMin/Y 不变。Slash 协程启动时读 _hitBox，持续保持强化值即可
            // 覆盖玩家手动劈砍等所有入口，不触碰协程本体。
            Rect hb = state.BaseHitBox;
            if (hb.xMax > 0f)
            {
                knight._hitBox = Rect.MinMaxRect(
                    hb.xMin, hb.yMin, hb.xMax * SlashRangeMultiplier, hb.yMax);
            }
            else
            {
                LogWarnOnce("hitbox-front-not-positive:" + knight.gameObject.GetInstanceID(),
                    "hitbox xMax <= 0; slash range boosted, hitbox left at native value");
            }

            // 扫描器覆盖：感知范围本应远大于近战距离，仅在不足时才扩
            //（Scanner.range 可写：PatchRoles_Crossbowman 先例）。只扩一次，
            // RangeBoostApplied 标志保证绝不把强化值再当基线。
            Scanner scanner = knight._enemyScanner;
            if (scanner != null && boosted > 0f && knight._awarenessRange < boosted)
            {
                state.ScannerRangeBefore = scanner.range;
                state.ScannerRangeBoosted = true;
                scanner.range = boosted;
            }

            state.RangeBoostApplied = true;
            LogInfoOnce("range-boost-applied",
                "medieval range boost applied (slash " + state.BaseSlashRange.ToString("F2")
                + " -> " + boosted.ToString("F2") + ", hitbox xMax x"
                + SlashRangeMultiplier.ToString("F1") + ")");
        }
        else if (!want && state.RangeBoostApplied)
        {
            RestoreRangeBoost(knight, state);
        }
    }

    private static void RestoreRangeBoost(Knight knight, KnightPowerState state)
    {
        if (!state.RangeBoostApplied) return;
        try
        {
            knight._slashRange = state.BaseSlashRange;
            knight._hitBox = state.BaseHitBox;
            if (state.ScannerRangeBoosted)
            {
                Scanner scanner = knight._enemyScanner;
                if (scanner != null) scanner.range = state.ScannerRangeBefore;
                state.ScannerRangeBoosted = false;
            }
            state.RangeBoostApplied = false;
        }
        catch (Exception e)
        {
            LogErrorOnce("range restore failed", e);
        }
    }

    /// <summary>
    /// 原生 ShouldSlash 返回 false 时的放宽判定（不改 Formation 资产）：
    /// 要求 Enabled + 世界权威 + style 0 + 强化已生效；重复原生无害门
    /// （_harmless/_cooldown/_pusher.enabled），复用原生 _enemyScanner/
    /// Damageable.IsDamagedBy 门，用有效距离 max(编队 knightSlashRange,
    /// 自身强化范围) 复核；命中则先写 _enemy 供原生 Slash 使用、再翻转
    /// __result。原生返回 true 时不做任何事。
    /// </summary>
    internal static void WidenShouldSlash(Knight knight, ref bool __result)
    {
        if (__result) return; // 原生已命中，保持
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth) return;
        if (!PatchRoles_KnightStyle.TryGetResolvedStyleIndex(knight, out int style)
            || style != MedievalStyleIndex) return;
        KnightPowerState state = GetState(knight);
        if (!state.RangeBoostApplied) return;

        if (knight._harmless || knight._cooldown > 0f) return;
        PushablePusher pusher = knight._pusher;
        if (pusher != null && pusher.enabled) return;

        try
        {
            Scanner scanner = knight._enemyScanner;
            if (scanner == null) return;
            GameObject closest = scanner.GetClosest();
            if (closest == null) return;

            Damageable enemy = closest.GetComponent<Damageable>();
            if (enemy == null || !enemy.IsDamagedBy(DamageSource.Knight)) return;

            float effective = knight._slashRange; // 已是 ×1.5 的持久值
            Formation formation = knight._currentFormation;
            if (formation != null && formation.knightSlashRange > effective)
                effective = formation.knightSlashRange;

            float dx = Mathf.Abs(enemy.transform.position.x - knight.transform.position.x);
            if (dx >= effective) return;

            knight._enemy = enemy; // 先写目标再翻转结果，供原生 Slash 朝向逻辑使用
            __result = true;
        }
        catch (Exception e)
        {
            LogErrorOnce("should-slash widen failed", e);
        }
    }

    // ============================================================
    // B. 北境：自身钱包容量/纳税阈值 ×2（含神像目标 ×2）
    // ============================================================

    /// <summary>
    /// 只写 _originalWallet（已验证挂骑士 GO），不读写 _wallet——玩家操控期间
    /// 恢复/强化照常落在自身钱包上，玩家当前钱包永不被碰。绝不改
    /// _statueBuffMaxCoins，绝不写 Coins/AddCurrency/SetCurrency。
    /// </summary>
    private static void ApplyWalletBoost(Knight knight, KnightPowerState state, bool want)
    {
        if (state.BaseCapacity < 0) return; // Awake 未取到基线，本实例禁用
        Wallet own = knight._originalWallet;
        if (own == null || !OwnsWallet(knight, own)) return;

        if (want)
        {
            state.WalletNeedsBaselineReset = true;
            WriteWalletTargets(knight, own, state, true);
            if (!state.WalletBoostApplied)
            {
                state.WalletBoostApplied = true;
                LogInfoOnce("wallet-boost-applied",
                    "norse wallet boost applied (capacity base " + state.BaseCapacity
                    + ", taxes base " + state.BaseTaxes + "; targets x2 incl statue)");
            }
        }
        else if (state.WalletBoostApplied)
        {
            // 风格丢失/关模组：恢复"当前神像或序列化目标 max 现有币"
            WriteWalletTargets(knight, own, state, false);
            state.WalletBoostApplied = false;
        }
    }

    /// <summary>
    /// 统一写入路径（幂等，值不变零写）：
    /// - 强化（norse=true）：无神像 → 基线 ×2；神像激活 → _statueBuffMaxCoins ×2。
    /// - 恢复（norse=false）：无神像 → 基线；神像激活 → 原生神像目标
    ///   （_statueBuffMaxCoins，镜像原生 ActivateStatueBuff 语义）。
    /// 容量取 max(目标, Coins) 保住既有溢出币；若此前因满包关闭拾币而容量已
    /// 变大，重开 CanGrabCoins（其余原生收集门不动）。
    /// </summary>
    private static void WriteWalletTargets(Knight knight, Wallet wallet, KnightPowerState state, bool norse)
    {
        int capBase, taxBase;
        if (IsStatueActive(knight))
        {
            int statue = knight._statueBuffMaxCoins;
            capBase = taxBase = norse ? SaturatingDouble(statue) : statue;
        }
        else
        {
            capBase = norse ? SaturatingDouble(state.BaseCapacity) : state.BaseCapacity;
            taxBase = norse ? SaturatingDouble(state.BaseTaxes) : state.BaseTaxes;
        }

        if (wallet.payTaxesAbove != taxBase)
            wallet.payTaxesAbove = taxBase;

        int targetCapacity = capBase > wallet.Coins ? capBase : wallet.Coins;
        if (wallet.TotalCapacity != targetCapacity)
            wallet.TotalCapacity = targetCapacity;

        if (!wallet.CanGrabCoins && wallet.Coins < wallet.TotalCapacity)
            wallet.CanGrabCoins = true;
    }

    /// <summary>
    /// OnDisable 恢复：直接回 Awake 序列化基线（不用神像目标——下次 OnEnable
    /// 原生神像逻辑会自行重设）。只改容量，不写币数；离场时必须清掉存量币
    /// 暂时撑高的容量，避免 Wallet.OnDisable 清币后将容量带到下一次池复用。
    /// </summary>
    private static void RestoreWalletToBaseline(Knight knight, KnightPowerState state)
    {
        if (!state.WalletNeedsBaselineReset || state.BaseCapacity < 0) return;
        try
        {
            Wallet own = knight._originalWallet;
            if (own == null || !OwnsWallet(knight, own)) return;
            if (own.payTaxesAbove != state.BaseTaxes)
                own.payTaxesAbove = state.BaseTaxes;
            int targetCapacity = state.BaseCapacity;
            if (own.TotalCapacity != targetCapacity)
                own.TotalCapacity = targetCapacity;
            state.WalletBoostApplied = false;
            state.WalletNeedsBaselineReset = false;
        }
        catch (Exception e)
        {
            LogErrorOnce("wallet baseline restore failed", e);
        }
    }

    /// <summary>
    /// Wallet.Persistent_IBehaviour.ApplyData postfix（原生直写 _currencyAmount，
    /// 无 clamp）：存档恢复后对"已注册为某骑士自身钱包"的钱包重算目标——
    /// 北境则容量 ≥ Coins 的 ×2 目标兜住刚恢复的存量币；非北境且此前强化过
    /// 则恢复。绝不写币数。玩家钱包不在反查表，天然 no-op。
    /// </summary>
    internal static void OnWalletDataApplied(Wallet wallet)
    {
        try
        {
            if (wallet == null) return;
            if (!WalletOwners.TryGetValue(wallet.Pointer, out int id)) return;
            if (!States.TryGetValue(id, out KnightPowerState state)) return;
            Knight knight = state.Knight;
            if (knight == null || knight.gameObject == null) return; // 已销毁，待巡检清理
            if (!OwnsWallet(knight, wallet)) return;

            int style = -1;
            bool hasStyle = ModConfig.Enabled.Value
                && PatchRoles_KnightStyle.TryGetResolvedStyleIndex(knight, out style);
            ApplyWalletBoost(knight, state, hasStyle && style == NorseStyleIndex);
        }
        catch (Exception e)
        {
            LogErrorOnce("wallet apply-data reconcile failed", e);
        }
    }

    // ============================================================
    // C. 中世纪剑风（纯视觉 LineRenderer 弧线）
    // ============================================================

    internal static void OnKnightSlashAnim(Knight knight)
    {
        try
        {
            if (!ModConfig.Enabled.Value || knight == null || knight.gameObject == null) return;
            if (!PatchRoles_KnightStyle.TryGetResolvedStyleIndex(knight, out int style)
                || style != MedievalStyleIndex) return;
            KnightPowerState state = GetState(knight);
            if (!state.BaseCaptured) return;
            TriggerArc(knight, state);
        }
        catch (Exception e)
        {
            LogErrorOnce("wind arc trigger failed", e);
        }
    }

    private static void TriggerArc(Knight knight, KnightPowerState state)
    {
        if (state.ArcBehaviour != null)
        {
            KnightWindArcBehaviour behaviour = state.ArcBehaviour;
            if (behaviour.gameObject == null || !behaviour.transform.IsChildOf(knight.transform))
            {
                state.ArcBehaviour = null;
                state.ArcObject = null;
            }
        }
        if (state.ArcBehaviour == null) EnsureArcBuilt(knight, state);
        KnightWindArcBehaviour active = state.ArcBehaviour;
        if (active == null || active.Renderer == null) return;
        active.Age = 0f;
        active.Renderer.startColor = new Color(1f, 1f, 1f, 0f);
        active.Renderer.endColor = new Color(1f, 1f, 1f, 0f);
        active.Renderer.enabled = true;
        active.gameObject.SetActive(true);
    }

    /// <summary>
    /// 原子构建（每骑士一次）：先注册组件类型（失败则整体跳过，无伪回退），
    /// 再建独立子 GO（局部原点、localScale 恒 1，绝不碰骑士根缩放/精灵）；
    /// 构建中任一步失败仅销毁新建子对象，无孤儿。几何一次构建缓存在组件里，
    /// 弧线终点 = 强化命中盒前沿（基线 xMax ×1.5）。材质只读复用
    /// knight._trail.sharedMaterial（setter 赋引用，不修改原生材质内容），
    /// 不可用则自建缓存 Shader.Find("Sprites/Default") 材质，两者皆缺 →
    /// 记录限制并跳过。无碰撞体/池/RPC；层/排序跟随骑士 SpriteRenderer。
    /// </summary>
    private static void EnsureArcBuilt(Knight knight, KnightPowerState state)
    {
        EnsureArcTypeRegistered();
        if (_arcTypeRegisterFailed) return;

        Material material = ResolveArcMaterial(knight);
        if (material == null)
        {
            LogWarnOnce("arc-material-unavailable",
                "no usable material (trail.sharedMaterial missing and Sprites/Default shader unavailable); wind arc disabled as stated limitation");
            return;
        }

        GameObject go = null;
        try
        {
            go = new GameObject("KEM_MedievalWindArc");
            go.layer = knight.gameObject.layer;
            go.transform.SetParent(knight.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.SetActive(false); // 完成前保持隐藏，失败即销毁无残留

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = false;
            lr.sharedMaterial = material; // 只赋引用，不实例化共享材质
            bool sortingCopied = false;
            SpriteRenderer knightSprite = knight.GetComponent<SpriteRenderer>();
            if (knightSprite != null)
            {
                // Numeric native API avoids the broken ReadOnlySpan string shim in this game runtime.
                lr.sortingLayerID = knightSprite.sortingLayerID;
                lr.sortingOrder = knightSprite.sortingOrder + 1;
                sortingCopied = true;
            }
            if (!sortingCopied) lr.sortingOrder = ArcFallbackSortingOrder;
            lr.widthMultiplier = 0.07f;
            lr.numCapVertices = 0;
            lr.numCornerVertices = 0;

            // 局部朝向坐标（+x 为前；骑士翻转由根 localScale.x 承担，子对象自动跟随）。
            // 终点与强化命中盒前沿一致：基线 xMax ×1.5（无任意上限）。
            float front = state.BaseHitBox.xMax > 0f
                ? state.BaseHitBox.xMax * SlashRangeMultiplier
                : state.BaseSlashRange * SlashRangeMultiplier;
            // Indexed native calls avoid the SetPositions array-to-Span interop path.
            lr.positionCount = ArcSegments + 1;
            float startX = state.BaseHitBox.xMin > 0f ? state.BaseHitBox.xMin : 0.25f;
            for (int i = 0; i <= ArcSegments; i++)
            {
                float t = (float)i / ArcSegments;
                lr.SetPosition(i, new Vector3(
                    Mathf.Lerp(startX, front, t),
                    -0.05f + 0.55f * Mathf.Sin(Mathf.PI * t),
                    0f));
            }
            lr.enabled = false;

            KnightWindArcBehaviour behaviour = go.AddComponent<KnightWindArcBehaviour>();
            behaviour.Renderer = lr;
            behaviour.State = state;
            behaviour.Age = -1f;

            state.ArcObject = go;
            state.ArcBehaviour = behaviour;
            LogInfoOnce("arc-built-scalar", "wind arc built: 13 points via SetPosition");
        }
        catch (Exception e)
        {
            // 原子清理：只销毁本次新建的子对象
            if (go != null)
            {
                try { UnityEngine.Object.Destroy(go); } catch { }
            }
            state.ArcObject = null;
            state.ArcBehaviour = null;
            LogErrorOnce("arc build failed", e);
        }
    }

    private static Material ResolveArcMaterial(Knight knight)
    {
        try
        {
            TrailRenderer trail = knight._trail;
            Material shared = trail != null ? trail.sharedMaterial : null;
            if (shared != null) return shared;
        }
        catch { }
        try
        {
            if (_arcFallbackMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null) _arcFallbackMaterial = new Material(shader);
            }
            return _arcFallbackMaterial;
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureArcTypeRegistered()
    {
        if (_arcTypeRegisterFailed) return;
        try
        {
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(KnightWindArcBehaviour)))
                ClassInjector.RegisterTypeInIl2Cpp(typeof(KnightWindArcBehaviour));
        }
        catch (Exception e)
        {
            _arcTypeRegisterFailed = true;
            LogWarnOnce("arc-type-register-failed",
                "KnightWindArcBehaviour injection failed; wind arc skipped entirely: " + e.Message);
        }
    }

    private static void HideArc(KnightPowerState state)
    {
        try
        {
            KnightWindArcBehaviour behaviour = state.ArcBehaviour;
            if (behaviour == null) return;
            behaviour.Age = -1f;
            if (behaviour.Renderer != null) behaviour.Renderer.enabled = false;
            if (behaviour.gameObject != null && behaviour.gameObject.activeSelf)
                behaviour.gameObject.SetActive(false);
        }
        catch { }
    }

    /// <summary>
    /// 注入组件的每帧回调（客户端 Knight.Update 被原生禁用，淡出必须独立）。
    /// 状态/渲染器均缓存在组件内，闲置（Age&lt;0）零工作零 GetComponent；
    /// 几何静止，仅推 Age 与顶点色透明度，无逐帧分配。
    /// </summary>
    internal static void TickArc(KnightWindArcBehaviour behaviour)
    {
        try
        {
            if (behaviour == null || behaviour.Age < 0f) return;
            if (!ModConfig.Enabled.Value || behaviour.State == null
                || !PatchRoles_KnightStyle.TryGetResolvedStyleIndex(behaviour.State.Knight, out int style)
                || style != MedievalStyleIndex)
            {
                if (behaviour.State != null) HideArc(behaviour.State);
                return;
            }
            LineRenderer lr = behaviour.Renderer;
            if (lr == null) { HideArc(behaviour.State); return; }
            behaviour.Age += Time.deltaTime;
            float t = behaviour.Age / ArcDuration;
            if (t >= 1f)
            {
                HideArc(behaviour.State);
                return;
            }
            Color c = new Color(1f, 1f, 1f, Mathf.Sin(Mathf.PI * t) * ArcBaseColorAlpha);
            lr.startColor = c;
            lr.endColor = c;
        }
        catch (Exception e)
        {
            LogErrorOnce("arc tick failed", e);
        }
    }

    // ============================================================
    // D. 低频巡检（双端；关模组也巡检走恢复路径；共享扫描缓存）
    // ============================================================

    /// <summary>
    /// 巡检协程（每 World 一份；旧 World 销毁后新 World 可重新启动）：
    /// 只清理已销毁条目（复用缓冲），活骑士的基线跨读档/池复用保留——
    /// 读档重生路径不重跑 Awake，清表会让基线永久丢失、功能永不生效。
    /// </summary>
    internal static IEnumerator PatrolRoutine(World world)
    {
        if (world == null || world.gameObject == null) yield break;
        try
        {
            // 旧 World 已销毁（访问抛异常/无效）→ 允许新 World 重启巡检
            if (_patrolWorld != null && _patrolWorld.gameObject != null
                && _patrolWorld.Pointer == world.Pointer)
                yield break;
        }
        catch { }
        _patrolWorld = world;

        PruneDestroyedStates();

        yield return new WaitForSeconds(1f);
        while (world != null)
        {
            bool worldAlive;
            try { worldAlive = world.gameObject != null; }
            catch { break; } // World 已销毁，协程随宿主结束
            if (!worldAlive) break;
            try
            {
                PruneDestroyedStates();
                Knight[] knights = UnitScanCache.GetKnights();
                if (knights != null)
                    for (int i = 0; i < knights.Length; i++)
                        Reconcile(knights[i]);
            }
            catch (Exception e)
            {
                LogErrorOnce("patrol failed", e);
            }
            yield return new WaitForSeconds(PatrolIntervalSeconds);
        }
    }

    /// <summary>
    /// 清理已销毁骑士的状态与钱包反查（复用缓冲，无逐轮分配）；
    /// 活骑士（含池中 inactive 的）一律保留——池复用还要用它们的基线。
    /// </summary>
    private static void PruneDestroyedStates()
    {
        PruneBuffer.Clear();
        foreach (KeyValuePair<int, KnightPowerState> pair in States)
        {
            try
            {
                Knight knight = pair.Value.Knight;
                if (knight != null && knight.gameObject != null) continue;
            }
            catch { }
            PruneBuffer.Add(pair.Key);
        }
        WalletPruneBuffer.Clear();
        for (int i = 0; i < PruneBuffer.Count; i++)
        {
            int id = PruneBuffer[i];
            States.Remove(id);
            foreach (KeyValuePair<IntPtr, int> walletPair in WalletOwners)
                if (walletPair.Value == id) WalletPruneBuffer.Add(walletPair.Key);
        }
        for (int i = 0; i < WalletPruneBuffer.Count; i++)
            WalletOwners.Remove(WalletPruneBuffer[i]);
        PruneBuffer.Clear();
        WalletPruneBuffer.Clear();
    }
}

/// <summary>
/// 剑风淡出驱动（状态/渲染器缓存在组件内；见
/// PatchRoles_MedievalNorsePowers.TickArc）。
/// </summary>
public class KnightWindArcBehaviour : MonoBehaviour
{
    public KnightWindArcBehaviour(IntPtr ptr) : base(ptr) { }

    internal LineRenderer Renderer;
    internal PatchRoles_MedievalNorsePowers.KnightPowerState State;
    internal float Age = -1f;

    private void Update()
    {
        PatchRoles_MedievalNorsePowers.TickArc(this);
    }

    private void OnDisable()
    {
        try
        {
            Age = -1f;
            if (Renderer != null) Renderer.enabled = false;
        }
        catch { }
    }
}

// ============================================================
// Harmony 补丁宿主（全部 LogErrorOnce 有界）
// ============================================================

/// <summary>基线捕获：Knight.Awake postfix（私有方法按名字符串补丁，先例：KnightStyle.OnEnable）。
/// 不受开关影响——关模组也要有正确基线可恢复。</summary>
[HarmonyPatch(typeof(Knight), "Awake")]
public static class Knight_Awake_MedievalNorsePowers_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Knight __instance)
    {
        if (__instance == null) return;
        try { PatchRoles_MedievalNorsePowers.OnKnightAwake(__instance); }
        catch (Exception e) { PatchRoles_MedievalNorsePowers.LogHookError("awake", e); }
    }
}

/// <summary>新生命周期收敛：OnEnable postfix 在原生神像逻辑之后执行，直接对齐目标值
///（关模组也走恢复路径，不做开关早退）。</summary>
[HarmonyPatch(typeof(Knight), "OnEnable")]
public static class Knight_OnEnable_MedievalNorsePowers_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Knight __instance)
    {
        if (__instance == null) return;
        try { PatchRoles_MedievalNorsePowers.Reconcile(__instance); }
        catch (Exception e) { PatchRoles_MedievalNorsePowers.LogHookError("on-enable", e); }
    }
}

/// <summary>池复用/离场：无条件恢复序列化基线（含 Enabled=false），并收起剑风。</summary>
[HarmonyPatch(typeof(Knight), "OnDisable")]
public static class Knight_OnDisable_MedievalNorsePowers_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Knight __instance)
    {
        if (__instance == null) return;
        try { PatchRoles_MedievalNorsePowers.OnKnightDisabled(__instance); }
        catch (Exception e) { PatchRoles_MedievalNorsePowers.LogHookError("on-disable", e); }
    }
}

/// <summary>主机/单机逐帧幂等收敛（客户端 Knight.Update 原生禁用，由巡检/事件覆盖）。
/// 关模组不早退——恢复路径必须在 Update 上可达。</summary>
[HarmonyPatch(typeof(Knight), "Update")]
public static class Knight_Update_MedievalNorsePowers_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Knight __instance)
    {
        if (__instance == null) return;
        try { PatchRoles_MedievalNorsePowers.Reconcile(__instance); }
        catch (Exception e) { PatchRoles_MedievalNorsePowers.LogHookError("update", e); }
    }
}

/// <summary>原生判定为 false 时的中世纪放宽（仅 Enabled+世界权威+style 0+强化已生效）。</summary>
[HarmonyPatch(typeof(Knight), "ShouldSlash")]
public static class Knight_ShouldSlash_MedievalNorsePowers_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Knight __instance, ref bool __result)
    {
        if (__instance == null) return;
        try { PatchRoles_MedievalNorsePowers.WidenShouldSlash(__instance, ref __result); }
        catch (Exception e) { PatchRoles_MedievalNorsePowers.LogHookError("should-slash", e); }
    }
}

/// <summary>神像激活后重算（主机原生 ActivateStatueBuff 已按自身钱包写过一轮，
/// postfix 叠加北境 ×2 目标；客户端由确定性神像判定覆盖）。</summary>
[HarmonyPatch(typeof(Knight), "ActivateStatueBuff")]
public static class Knight_ActivateStatueBuff_MedievalNorsePowers_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Knight __instance)
    {
        if (__instance == null) return;
        try { PatchRoles_MedievalNorsePowers.Reconcile(__instance); }
        catch (Exception e) { PatchRoles_MedievalNorsePowers.LogHookError("statue-on", e); }
    }
}

/// <summary>剑风触发：OnAnimSlash 是动画事件，双端播放劈砍动画时都会命中。</summary>
[HarmonyPatch(typeof(Knight), "OnAnimSlash")]
public static class Knight_OnAnimSlash_MedievalNorsePowers_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Knight __instance)
    {
        if (__instance == null) return;
        try { PatchRoles_MedievalNorsePowers.OnKnightSlashAnim(__instance); }
        catch (Exception e) { PatchRoles_MedievalNorsePowers.LogHookError("anim-slash", e); }
    }
}

/// <summary>玩家操控释放后收敛：自身钱包目标值在 ReleaseControl 后立即对齐
///（操控期间写入也只落自身钱包，见 ApplyWalletBoost）。</summary>
[HarmonyPatch(typeof(Knight), "IUnitControllable_ReleaseControl")]
public static class Knight_ReleaseControl_MedievalNorsePowers_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Knight __instance)
    {
        if (__instance == null) return;
        try { PatchRoles_MedievalNorsePowers.Reconcile(__instance); }
        catch (Exception e) { PatchRoles_MedievalNorsePowers.LogHookError("release-control", e); }
    }
}

/// <summary>存档恢复后自身钱包重算：Wallet.Persistent_IBehaviour.ApplyData postfix
///（签名 (Il2CppSystem.Object)；原生直写币数无 clamp，绝不改币数）。</summary>
[HarmonyPatch(typeof(Wallet), "Persistent_IBehaviour_ApplyData")]
public static class Wallet_ApplyData_MedievalNorsePowers_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Wallet __instance)
    {
        try { PatchRoles_MedievalNorsePowers.OnWalletDataApplied(__instance); }
        catch (Exception e) { PatchRoles_MedievalNorsePowers.LogHookError("apply-data", e); }
    }
}

/// <summary>双端低频巡检宿主（范式同 KnightStyle/DefenseSpacing；共享 UnitScanCache）。</summary>
[HarmonyPatch(typeof(World), nameof(World.OnLevelLoaded))]
public static class World_OnLevelLoaded_MedievalNorsePowers_Patch
{
    [HarmonyPostfix]
    private static void Postfix(World __instance)
    {
        if (__instance == null) return;
        try
        {
            __instance.StartCoroutine(
                PatchRoles_MedievalNorsePowers.PatrolRoutine(__instance).WrapToIl2Cpp());
        }
        catch (Exception e) { PatchRoles_MedievalNorsePowers.LogHookError("patrol-start", e); }
    }
}
