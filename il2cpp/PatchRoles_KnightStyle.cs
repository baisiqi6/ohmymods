using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 骑士随机风格（knight-style-026 + norse-squad-027）：招募骑士（Armor 转职）时，
/// 每个骑士固定为 中世纪/死亡之地/幕府/希腊/北境 五种形象之一
/// （纯外观，不动战斗数值）；其随从士兵（跟随骑士的 Archer，原生
/// ConvertToSoldier 已把它们换成当前世界的士兵控制器）覆盖为"骑士风格对应"的
/// 士兵控制器。北境风格额外联动 PatchRoles_NorseSquad：随从转化为真北境弓箭手
/// 预制体（带盾组件的近战/盾墙原生逻辑）并程序化装盾，见该文件。
/// 缩放（坑11：只动 y）：骑士按风格查表（中世纪 0.95/死地 1.05/幕府 0.95/
/// 希腊 0.9/北境 1.15，Strip 恒回 1）；中世纪随从 1.05、北境随从 1.15
/// （其余含北境 1.0，无骑士/骑士无风格时回 1）。
///
/// 机制要点：
/// - 转职入口：Character.Promote(DroppableTool, IUnitController) postfix，
///   tool.tag == "Armor" → Professions{"Armor","Knight"}。__result 是新骑士
///   （ReplaceBy 内部已做过 biome swap，原生控制器=当前世界骑士皮肤）。
///   Squire（Shield 转职的侍从）按任务书明确不处理——tag != "Knight" 全部早退。
/// - 身份由 KnightIdentityRuntime 管理：已有类型固定；首见零记录装载骑士由功能 A 一次批量均匀分配
///   （取代逐人哈希迁移，见 KnightStylePanel），新招募优先补当前岛数量最少的类型；GUID/style 只写
///   独立附加档。风格面板（KnightStylePanel）经 ApplyPanelRestyle 复用本文件的表现路径。
/// - 读档只在完整原生快照匹配时恢复；客户端只使用主机确认的收据，不自行 hash。
///   缺资产时等待，不按资源池长度重映射已有类型。
/// - OnEnable 清理上一生命的本地身份和渲染；转职与现有 5 秒巡检接入新身份。
/// - 随从联动（反向归属 + 队籍判定 + 翻牌治理）：不枚举 knight._archers——
///   Il2Cpp 非泛型枚举器对 HashSet 运行时不可靠（knightstyle2 实测：纯读快照段
///   的 MoveNext 也抛 InvalidOperationException），改为全场
///   FindObjectsOfType&lt;Archer&gt; 读 _knight 反查骑士状态。写入条件是队籍而非
///   皮肤族（follower diag 实测：原生随从只在 actively 跟队时 ConvertToSoldier，
///   白天分散打猎穿猎人皮，"∈士兵族才写"白天永远不命中）：_knight 指向已风格化
///   骑士且（当前控制器或生根字段）!= 目标即写（统一路径 ApplyFollowerSkinTo）。
///   皮肤生根（2026-09-24 双写者根治，与弩手同构）：ApplyFollowerSkinTo 把随从实例的
///   Archer.soldierAnimator 指到风格士兵控制器——原生 ConvertToSoldier 的 biome 换皮对
///   未注册 original 原样穿透，原生自己写出的就是同一引用（同引用重赋无害），
///   "5s 写 vs ~10s 刷回"的翻牌战争消失；两个转换的 postfix 与 5s 巡检退化为防御与
///   指针校验（稳态零写入）。离队/无风格时字段写回原生 Archer prefab 快照
///   （PatchRoles_Crossbowman.BaseSoldierAnimator），风格皮绝不泄漏给下一个池 life；
///   随从实例没有弩手 marker、池边界不会替它收尾，故无队籍/无风格路径另有
///   RepairLeakedFollowerSkin：身上仍是风格族控制器（上一 life 生根残留）时按原生
///   ConvertToSoldier 的解析式重解析成世界原生士兵皮（北境 prefab 族不碰）；
///   真正离队时原生先置 _knight=null 再 ConvertToHunter，猎人皮正确保留，无需清理。
/// - 死地随从"无标记弩手化"（用户拍板）：骑士风格==死地 → 随从战斗包与弩手
///   一致（ActiveArrowAttack=KEM_CrossbowAttack 克隆 SO、shootRange/扫描器 12、
///   间隔 ×2、y=1.15，Crossbowman.ApplySquadCrossbowPackage），非死地/无队籍/
///   无风格 → RestoreSquadCrossbowPackage（幂等 no-op）。绝不挂
///   CrossbowmanMarker（标记=拒绝骑士招募，随从就是队员）；弩手本体永不入队
///   （IsAvailableForJob 排除），两个群体不相交。死地随从缩放由该包管理，
///   本文件风格缩放对死地跳过。
///
/// 2.4.0 签名验证（Operator 任务书实锤 + interop Assembly-CSharp.dll 复核）：
/// - Character.Promote(DroppableTool, IUnitController) : Character —— 存在（双验证）
/// - Knight._animator : Animator（私有，root 上）/ _mover : Mover（私有，interop 均已暴露）
/// - Knight.OnEnable() —— 私有，用字符串名打补丁（Worker/Crossbowman 的 nameof
///   先例不适用：Knight.OnEnable 非公开）
/// - Archer._animator : Animator —— interop 已暴露（免 GetComponentInChildren）
/// - Knight/士兵五套控制器（2.4.0 资产实测存在）：knight / knight_deadlands /
///   knight_bamboo / knight_greece / knight_norselands；archer_soldier /
///   archer_soldier_deadlands / archer_soldier_bamboo / archer_soldier_greece /
///   archer_soldier_norselands（第五套"北境"由 norse-squad-027 转正进风格池，
///   取代 Reviewer MF-1 时代的"只解析不消费"特殊字段；北境款含 attack/defend/
///   getshield/retreat 全套近战 clip，其他风格族没有——北境随从的近战/盾墙
///   表现来自真北境弓箭手预制体的 NpcShieldUser 组件，士兵皮只管外观）。
///   可用风格池只影响新分配；已有身份保持原类型。
/// - Archer._knight : Knight（私有，interop 已暴露）——随从联动反向归属用
///   （PatchRoles_Crossbowman.cs:687 同字段先例）。不枚举 Knight._archers：
///   Il2CppSystem HashSet 的枚举器运行时不可靠（knightstyle2 实测纯读 MoveNext
///   也抛 InvalidOperationException；泛型 IEnumerator&lt;T&gt; 则缺 MoveNext）
/// - ScaleRegistryHolder.Register(Mover, float) / Unregister(Mover)
///   （PatchRoles_Worker，按 gameObject.GetInstanceID() 键控，Mover.Update postfix
///   每帧守卫 y）。坑11：只动 y，x 是朝向符号。
/// </summary>
public static class PatchRoles_KnightStyle
{
    // ---- 常量 ----
    private const int StyleCount = 5;
    private const int MedievalStyleIndex = 0; // 随从缩放特判用（中世纪随从 1.05）
    private const int DeadlandsStyleIndex = 1; // 死地随从"无标记弩手化"包特判用
    internal const int NorseStyleIndex = 4;   // 北境风格（PatchRoles_NorseSquad 联动判定用）
    private const float IntegrityIntervalSeconds = 5f;
    private const float AssetRetryIntervalSeconds = 30f;

    // 每风格骑士 y 缩放（坑11：只动 y），index 对齐 StyleNames：
    // 中世纪 0.95 / 死地 1.05 / 幕府 0.95 / 希腊 0.9 / 北境 1.15（原"希腊特例"泛化为
    // 表驱动；死地/北境 1.05/1.15 由用户拍板定稿）
    private static readonly float[] KnightStyleScaleY = { 0.95f, 1.05f, 0.95f, 0.9f, 1.15f };
    // 中世纪风格的随从士兵 y 缩放（其余风格含北境 1.0；用户可从身高认出中世纪队）
    private const float FollowerMedievalScaleY = 1.05f;
    private const float FollowerNorseScaleY = 1.15f; // 用户拍板：北境骑士与其随从同步 1.15

    private const uint FnvOffset = 2166136261u;
    private const uint FnvPrime = 16777619u;
    private const uint DesignationSchema = 0x4B535431u; // "KST1"，与 FriendlyTroll 的 schema 区分

    // 风格表：index 0..4 = 中世纪/死亡之地/幕府（bamboo）/希腊/北境
    private static readonly string[] StyleNames = { "medieval", "deadlands", "shogun", "greece", "norse" };
    private static readonly string[] KnightControllerNames =
        { "knight", "knight_deadlands", "knight_bamboo", "knight_greece", "knight_norselands" };
    private static readonly string[] SoldierControllerNames =
        { "archer_soldier", "archer_soldier_deadlands", "archer_soldier_bamboo", "archer_soldier_greece", "archer_soldier_norselands" };

    // ---- 每骑士状态（instanceID 键控，范式同 FriendlyTroll TrollState）----
    private sealed class KnightStyleState
    {
        internal Knight Knight;
        internal int StyleIndex = -1;
        internal bool HasStyle;
        internal RuntimeAnimatorController NativeKnightController; // 首次覆盖前缓存，Strip 恢复用
        internal bool NativeControllerCached;
        internal bool NeedsRederive; // 池对象新生命周期：等待独立身份档或主机收据
        internal bool Logged;
    }

    private static readonly Dictionary<int, KnightStyleState> States = new();

    // ---- 惰性静态资产（解析一次；未解析全时按间隔重试）----
    private static readonly RuntimeAnimatorController[] KnightControllers = new RuntimeAnimatorController[StyleCount];
    private static readonly RuntimeAnimatorController[] SoldierControllers = new RuntimeAnimatorController[StyleCount];
    private static readonly List<int> AvailableStyles = new(); // 新分配可用池；已有收据不重映射
    private static bool _poolBuilt;
    private static bool _assetsComplete;      // 10/10 全解析（5 骑士 + 5 士兵）：停止重试
    private static float _nextAssetRetryAt;
    private static bool _loggedPoolShrunk;
    private static bool _loggedPoolEmpty;
    private static bool _loggedResolution; // [3a] 解析快照一次性日志去重

    // ---- 随从换皮管线诊断（只记录不改行为）----
    // StyleFollowersByLookup 每轮累计各环节数量，与上次"实际输出"的计数缓存比对：
    // 状态有变化且距上次输出 ≥60s 才再输出一行（定位换皮效果在哪一环丢弃）。
    // 世界切换时 SupervisorRoutine 复位基线，新世界首轮立即可输出。
    private const float FollowerDiagMinIntervalSeconds = 60f;
    private static bool _followerDiagHasBaseline;  // 上次输出计数缓存是否有效
    private static float _nextFollowerDiagAt;      // 最早允许下次输出的 Time.time
    private static int _diagLastArchers = -1;
    private static int _diagLastWithKnight = -1;
    private static int _diagLastInStates = -1;
    private static int _diagLastStyled = -1;
    private static int _diagLastSkippedFamily = -1;
    private static int _diagLastSkippedOther = -1;

    // ---- 协程守卫 ----
    private static IntPtr _supervisorWorld;

    // ---- 一次性日志 ----
    private static readonly HashSet<string> LoggedErrors = new();

    private static void LogInfo(string message)
    {
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[KnightStyle] " + message);
    }

    private static void LogWarning(string message)
    {
        KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[KnightStyle] " + message);
    }

    private static void LogErrorOnce(string key, Exception exception)
    {
        if (!LoggedErrors.Add(key)) return;
        KingdomEnhancedPlugin.Instance?.LogSource.LogError("[KnightStyle] " + key + ": " + exception);
    }

    private static void LogError(string message)
    {
        KingdomEnhancedPlugin.Instance?.LogSource.LogError("[KnightStyle] " + message);
    }

    // ============================================================
    // A. 静态资产（惰性、幂等；部分失败 → 风格池收缩 + LogWarning 一次）
    // ============================================================

    private static bool HasUsablePool()
    {
        return _poolBuilt && AvailableStyles.Count > 0;
    }

    /// <summary>
    /// 解析五套骑士 + 五套士兵控制器。先查已加载资产（FindObjectsOfTypeAll），
    /// 仍缺走一次 Resources.LoadAll("") 兜底（强制全量加载，Crossbowman 同款）。
    /// 未解析全时按 AssetRetryIntervalSeconds 重试（LoadAll 是穷举，正常首试即全中；
    /// 重试只兜"资产随世界内容渐进加载"的边角）。解析失败只影响对应风格
    /// （BuildAvailablePool 收缩）；北境款解析失败则北境小队功能随风格池收缩
    /// 一起停用（PatchRoles_NorseSquad 依赖 NorseStyleIndex 在池内）。
    /// </summary>
    private static void EnsureStyleAssets()
    {
        if (_assetsComplete) return;
        if (Time.time < _nextAssetRetryAt && _poolBuilt) return;
        _nextAssetRetryAt = Time.time + AssetRetryIntervalSeconds;
        try
        {
            ResolveFromSet(Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>());
            ResolveFromBiomeSwapPools();
            if (HasMissingControllers())
                ResolveFromSet(Resources.LoadAll<RuntimeAnimatorController>(""));
            BuildAvailablePool();
        }
        catch (Exception e)
        {
            LogErrorOnce("style asset resolution failed", e);
        }
    }

    private static bool HasMissingControllers()
    {
        for (int i = 0; i < StyleCount; i++)
            if (KnightControllers[i] == null || SoldierControllers[i] == null) return true;
        return false;
    }

    private static void ResolveFromSet(RuntimeAnimatorController[] set)
    {
        if (set == null) return;
        for (int i = 0; i < StyleCount; i++)
        {
            if (KnightControllers[i] != null && SoldierControllers[i] != null) continue;
            for (int j = 0; j < set.Length; j++)
            {
                RuntimeAnimatorController candidate = set[j];
                if (candidate == null) continue;
                string candidateName = candidate.name;
                if (KnightControllers[i] == null && candidateName == KnightControllerNames[i])
                    KnightControllers[i] = candidate;
                else if (SoldierControllers[i] == null && candidateName == SoldierControllerNames[i])
                    SoldierControllers[i] = candidate;
            }
        }
    }

    /// <summary>
    /// 跨世界风格的控制器通常不是独立的 Resources 根对象，而是挂在对应
    /// BiomeData 的 animatorSwapPool 中。希腊世界只加载当前 biome 时，单靠
    /// FindObjectsOfTypeAll/Resources.LoadAll 会漏掉死地、幕府和北境控制器，
    /// 于是错误地把风格池收缩成 3/5。BiomeHolder 保留了各 biome 的预加载
    /// swap 表，优先从那里按原生基座控制器取出对应的 AnimatorOverrideController，
    /// 不实例化、不修改原资源；未准备好时下次 30 秒重试。
    /// </summary>
    private static void ResolveFromBiomeSwapPools()
    {
        try
        {
            BiomeHolder holder = BiomeHolder.Inst;
            if (holder == null || holder.biomePathStrings == null) return;

            RuntimeAnimatorController baseKnight = FindControllerByName("knight");
            RuntimeAnimatorController baseSoldier = FindControllerByName("archer_soldier");
            // index 0 is the base medieval controller; the other styles come from
            // their native biome swap tables (Bamboo=1, Deadlands=2, Norse=3,
            // Greece=5).
            int[] sourceBiomeByStyle = { 0, BiomeHolder.DeadlandsBiomeIndex,
                BiomeHolder.BambooBiomeIndex, BiomeHolder.GreeceBiomeIndex,
                BiomeHolder.NorselandsBiomeIndex };

            for (int style = 1; style < StyleCount; style++)
            {
                int biomeIndex = sourceBiomeByStyle[style];
                if (biomeIndex < 0 || biomeIndex >= holder.biomePathStrings.Length) continue;

                BiomeSwapData swapData = holder.GetBiomeSwapDataForIndex(biomeIndex);
                // biomePreloadData can be a lightweight table with no animator
                // entries. In that case load the full BiomeData asset; treating
                // an empty preload table as authoritative was the reason the
                // Deadlands/Bamboo controllers stayed unresolved in Greece.
                if (swapData == null || swapData.animatorSwapPool == null
                    || swapData.animatorSwapPool.Count == 0)
                {
                    string path = holder.biomePathStrings[biomeIndex];
                    if (!string.IsNullOrEmpty(path))
                    {
                        BiomeData data = Resources.Load<BiomeData>(path);
                        swapData = data != null ? data.swapData : null;
                    }
                }
                if (swapData == null || swapData.animatorSwapPool == null) continue;

                for (int j = 0; j < swapData.animatorSwapPool.Count; j++)
                {
                    BiomeSwapData.AnimatorSwapData entry = swapData.animatorSwapPool[j];
                    if (entry == null || entry.original == null || entry.swap == null) continue;
                    bool isKnight = (baseKnight != null && entry.original.Pointer == baseKnight.Pointer)
                        || entry.original.name == "knight";
                    bool isSoldier = (baseSoldier != null && entry.original.Pointer == baseSoldier.Pointer)
                        || entry.original.name == "archer_soldier";
                    if (isKnight && KnightControllers[style] == null)
                        KnightControllers[style] = entry.swap;
                    if (isSoldier && SoldierControllers[style] == null)
                        SoldierControllers[style] = entry.swap;
                }
            }
        }
        catch (Exception e)
        {
            LogErrorOnce("biome swap controller resolution failed", e);
        }
    }

    private static RuntimeAnimatorController FindControllerByName(string name)
    {
        RuntimeAnimatorController[] all = Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>();
        if (all == null) return null;
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null && all[i].name == name) return all[i];
        return null;
    }

    /// <summary>
    /// 风格池收缩：某风格的骑士或士兵控制器缺失 → 该风格从池中剔除，
    /// 哈希对剩余池取模（均匀重映射）。全缺 → 禁用本功能。
    /// 首次到达稳定态（全解析/收缩告警/全缺）时输出解析快照（LogResolutionOnce）。
    /// </summary>
    private static void BuildAvailablePool()
    {
        AvailableStyles.Clear();
        var missing = new System.Text.StringBuilder();
        for (int i = 0; i < StyleCount; i++)
        {
            if (KnightControllers[i] != null && SoldierControllers[i] != null)
            {
                AvailableStyles.Add(i);
                continue;
            }
            if (KnightControllers[i] == null) missing.Append(KnightControllerNames[i]).Append(' ');
            if (SoldierControllers[i] == null) missing.Append(SoldierControllerNames[i]).Append(' ');
        }
        _poolBuilt = true;

        if (AvailableStyles.Count == StyleCount)
        {
            _assetsComplete = true;
            LogResolutionOnce();
            return;
        }
        if (AvailableStyles.Count == 0)
        {
            if (!_loggedPoolEmpty)
            {
                _loggedPoolEmpty = true;
                LogWarning("no style controllers resolved (missing: " + missing
                    + "); knight styling disabled");
            }
            LogResolutionOnce();
            return;
        }
        if (!_loggedPoolShrunk)
        {
            _loggedPoolShrunk = true;
            LogWarning("partial controller resolution (missing: " + missing
                + "); style pool shrunk to " + AvailableStyles.Count
                + "/" + StyleCount + ", hash remapped uniformly");
        }
        LogResolutionOnce();
    }

    /// <summary>
    /// [3a] 解析快照一次性日志：全部 请求名=解析对象名（&lt;null&gt;=未解析，
    /// wrapper 非空即指针非空）+ 北境款。暴露重名/错配——实锤案例：幕府随从
    /// 不换皮疑似 archer_soldier_bamboo 解析到了错误对象（名字与请求不符
    /// 会直接显示出来）。
    /// </summary>
    private static void LogResolutionOnce()
    {
        if (_loggedResolution) return;
        _loggedResolution = true;
        try
        {
            var knightText = new System.Text.StringBuilder();
            for (int i = 0; i < StyleCount; i++)
            {
                if (i > 0) knightText.Append(' ');
                knightText.Append(KnightControllerNames[i]).Append('=')
                    .Append(KnightControllers[i] != null ? KnightControllers[i].name : "<null>");
            }
            var soldierText = new System.Text.StringBuilder();
            for (int i = 0; i < StyleCount; i++)
            {
                if (i > 0) soldierText.Append(' ');
                soldierText.Append(SoldierControllerNames[i]).Append('=')
                    .Append(SoldierControllers[i] != null ? SoldierControllers[i].name : "<null>");
            }
            LogInfo("resolution: knight[" + knightText + "] soldier[" + soldierText + "]");
        }
        catch (Exception e)
        {
            LogErrorOnce("resolution snapshot log failed", e);
        }
    }

    private static bool IsStyleKnightController(RuntimeAnimatorController controller)
    {
        for (int i = 0; i < StyleCount; i++)
            if (KnightControllers[i] != null && controller.Pointer == KnightControllers[i].Pointer)
                return true;
        return false;
    }

    // ============================================================
    // 确定性风格哈希（范式抄 PatchDivine_FriendlyTroll.TryComputeDesignation）
    // ============================================================

    private static uint Mix(uint hash, uint value)
    {
        hash ^= value;
        return unchecked(hash * FnvPrime);
    }

    /// <summary>
    /// 风格身份哈希：mix 当前战役上下文 + 岛起始时间 + 骑士网络身份。
    /// 仅用于没有附加记录的旧档首次迁移；NetID 和 instanceID 均不是永久标识。
    /// 收据建立后不再重算，客户端等待主机确认。
    /// 存档上下文缺失（战役未加载等）→ 返回 false，本轮跳过（绝不随机摇——
    /// 会破坏双端确定性），巡检下轮重试。
    /// </summary>
    private static bool TryComputeIdentity(Knight knight, out uint hash, out bool usedNetId)
    {
        hash = 0u;
        usedNetId = false;
        try
        {
            GlobalSaveData global = GlobalSaveData.loaded;
            CampaignSaveData campaign = CampaignSaveData.current;
            IslandSaveData island = campaign != null ? campaign.CurrentIsland : null;
            if (global == null || campaign == null || island == null) return false;

            uint value = FnvOffset;
            value = Mix(value, DesignationSchema);
            value = Mix(value, unchecked((uint)global.currentCampaign));
            value = Mix(value, unchecked((uint)global.currentChallenge));
            value = Mix(value, unchecked((uint)campaign.CurrentLand));
            value = Mix(value, unchecked((uint)campaign.reign));
            long islandStartTicks = island.realStartDateTime.Ticks;
            value = Mix(value, unchecked((uint)islandStartTicks));
            value = Mix(value, unchecked((uint)(islandStartTicks >> 32)));

            NetworkPostbox postbox = NetworkPostbox.Instance;
            CRPCHeader header = postbox != null
                ? postbox.GetHeaderFromDynamicObject(knight.gameObject, true)
                : null;
            if (header != null)
            {
                // 旧分配公式只用于首次迁移，不能作为跨读档身份。
                value = Mix(value, unchecked((uint)(ushort)header.NetID));
                usedNetId = true;
            }
            else
            {
                value = Mix(value, unchecked((uint)knight.gameObject.GetInstanceID()));
            }

            hash = value;
            return true;
        }
        catch (Exception e)
        {
            LogErrorOnce("style identity computation failed", e);
            return false;
        }
    }

    // ============================================================
    // B. 转职入口（宿主类见文件尾）
    // ============================================================

    internal static void OnKnightPromoted(Character result)
    {
        if (result == null || result.gameObject == null) return;
        try
        {
            Knight knight = result.GetComponent<Knight>();
            if (knight == null || knight.gameObject == null) return;
            // Squire（tag "Squire"）也是 Knight 组件，任务书明确不处理
            if (knight.tag != "Knight") return;

            KnightIdentityRuntime.MarkPromoted(knight);
            if (!ModConfig.Enabled.Value) return;

            EnsureStyleAssets();
            if (!HasUsablePool()) return;
            KnightIdentityRuntime.Poll();
            // PrimeExisting 的唯一职责现在是冻结「本会话已生效风格」（首见哈希迁移已由功能 A 取代）。
            KnightIdentityRuntime.PrimeExisting(UnitScanCache.GetKnights(), GetLiveStyleForFreeze);
            KnightIdentityLoadSeed.Flush();

            // 池复用清污：同实例带旧风格 → 先恢复原生再重摇（对象池 respawn
            // 不重拷序列化字段，不清污会把上一个骑士的皮肤/缩放带进新骑士）
            int id = knight.gameObject.GetInstanceID();
            if (States.TryGetValue(id, out KnightStyleState stale))
                StripKnight(knight, stale);

            ApplyKnightStyle(knight);
        }
        catch (Exception e)
        {
            LogErrorOnce("promote styling failed", e);
        }
    }

    /// <summary>
    /// 给骑士上风格（幂等）：读取固定身份；首次覆盖前缓存原生控制器；
    /// 设风格控制器；希腊 → 缩放守卫 0.9，非希腊确保 y=1 且注销守卫。
    /// </summary>
    private static void ApplyKnightStyle(Knight knight)
    {
        if (knight == null || knight.gameObject == null) return;
        if (!HasUsablePool()) return;
        if (knight.tag != "Knight") return;

        int id = knight.gameObject.GetInstanceID();
        uint migrationHash = 0;
        bool hasReceipt = KnightIdentityRuntime.TryGetReceipt(knight, out _);
        if (!hasReceipt && KnightIdentityRuntime.IsHostAuthority()
            && !TryComputeIdentity(knight, out migrationHash, out _)) return;
        int existingStyle = GetCurrentLifeStyle(knight);
        if (!KnightIdentityRuntime.TryResolve(knight, existingStyle, migrationHash,
                AvailableStyles, out int styleIndex)) return;
        // 固定类型缺资产时等待；不能因可用资源数量变化把已有骑士改成另一类。
        if (styleIndex < 0 || styleIndex >= StyleCount || !AvailableStyles.Contains(styleIndex)) return;

        KnightStyleState state = GetStyleState(knight, id);
        bool styleChanged = state.HasStyle && state.StyleIndex != styleIndex;

        Animator animator = GetKnightAnimator(knight);
        RuntimeAnimatorController target = KnightControllers[styleIndex];
        if (animator != null && target != null)
        {
            // 首次覆盖前缓存原生控制器（Strip 恢复用）。绝不能把风格控制器自身
            // 缓存成"原生"（重入/异常路径下当前可能已是风格控制器）。
            if (!state.NativeControllerCached)
            {
                RuntimeAnimatorController current = animator.runtimeAnimatorController;
                if (current != null && !IsStyleKnightController(current))
                {
                    state.NativeKnightController = current;
                    state.NativeControllerCached = true;
                }
            }
            if (animator.runtimeAnimatorController == null
                || animator.runtimeAnimatorController.Pointer != target.Pointer)
            {
                animator.runtimeAnimatorController = target;
            }
        }

        ApplyScale(knight, styleIndex);

        state.StyleIndex = styleIndex;
        state.HasStyle = true;
        state.NeedsRederive = false;

        if (!state.Logged)
        {
            state.Logged = true;
            LogInfo("knight styled as " + StyleNames[styleIndex] + " (identity assigned)");
        }
        else if (styleChanged)
        {
            // 恢复或主机确认后，渲染与固定收据对齐。
            LogInfo("knight restyled as " + StyleNames[styleIndex]);
        }
    }

    private static int GetCurrentLifeStyle(Knight knight)
    {
        if (knight == null || knight.gameObject == null) return -1;
        if (!States.TryGetValue(knight.gameObject.GetInstanceID(), out KnightStyleState state)
            || !state.HasStyle || state.NeedsRederive || state.Knight == null
            || state.Knight.Pointer != knight.Pointer) return -1;
        return state.StyleIndex;
    }

    /// <summary>
    /// PrimeExisting 的风格探针：只返回本会话已实际生效的风格（冻结语义），没有返回 -1。
    /// 旧「哈希迁移回退」已由功能 A（KnightIdentityRuntime.AssignFirstSeenUniform）取代——首见零记录
    /// 骑士在 PrimeExisting 之前一次批量均匀分配，本回调不再做任何哈希计算（绝不抢先铸哈希风格）。
    /// </summary>
    private static int GetLiveStyleForFreeze(Knight knight)
    {
        return GetCurrentLifeStyle(knight);
    }

    /// <summary>功能 A 的只读探针：该骑士是否已有本会话生效的风格（有则交给冻结路径，不在批次重摇）。</summary>
    private static bool HasLiveStyleForFreeze(Knight knight)
    {
        return GetCurrentLifeStyle(knight) >= 0;
    }

    private static KnightStyleState GetStyleState(Knight knight, int id)
    {
        if (!States.TryGetValue(id, out KnightStyleState state))
        {
            state = new KnightStyleState { Knight = knight };
            States[id] = state;
            return state;
        }

        if (state.Knight != null && state.Knight.Pointer != knight.Pointer)
        {
            // instanceID 被 Unity 复用给了不同对象：旧记录不可信，重置。
            // 缩放守卫残留由本对象的 ApplyScale（按同 ID 重新注册/注销）自然收敛。
            state = new KnightStyleState { Knight = knight };
            States[id] = state;
            return state;
        }

        state.Knight = knight;
        return state;
    }

    private static Animator GetKnightAnimator(Knight knight)
    {
        try
        {
            Animator animator = knight._animator; // root 上的 Animator（RequireComponent）
            if (animator != null) return animator;
            return knight.GetComponent<Animator>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 骑士缩放（坑11：只动 y，x 是朝向符号）：按风格查 KnightStyleScaleY 表
    /// （中世纪 0.95/死地 1.05/幕府 0.95/希腊 0.9/北境 1.15）。y≠1 注册 ScaleRegistry 每帧
    /// 守卫（池 respawn/原生重置能自愈），y=1 注销守卫。Apply/Reassert 共用；
    /// Strip 不走此表，仅归还本 mod 拥有的缩放。
    /// </summary>
    private static void ApplyScale(Knight knight, int styleIndex)
    {
        try
        {
            float targetY = KnightStyleScaleY[styleIndex];
            GreekScaleScope.ApplyY(knight.transform, targetY);

            Mover mover = knight._mover;
            if (mover == null) mover = knight.GetComponent<Mover>();
            if (targetY != 1f) ScaleRegistryHolder.Register(mover, targetY);
            else ScaleRegistryHolder.Unregister(mover);
        }
        catch (Exception e)
        {
            LogErrorOnce("knight scale apply failed", e);
        }
    }

    /// <summary>
    /// 随从缩放（坑11：只动 y）：中世纪风格的随从士兵 y=1.05，其余（含骑士无
    /// 风格/随从无骑士的清理路径传 1）归还原生缩放。y≠1 注册守卫，y=1 注销。
    /// 每轮幂等重算：随从换队（骑士死了改投他人）时缩放自动跟随新骑士风格。
    /// </summary>
    private static void EnsureFollowerScale(Archer archer, float targetY)
    {
        try
        {
            if (targetY != 1f) GreekScaleScope.ApplyY(archer.transform, targetY);
            else GreekScaleScope.Restore(archer.transform);

            Mover mover = archer._mover;
            if (mover == null) mover = archer.GetComponent<Mover>();
            if (targetY != 1f) ScaleRegistryHolder.Register(mover, targetY);
            else ScaleRegistryHolder.Unregister(mover);
        }
        catch (Exception e)
        {
            LogErrorOnce("follower scale apply failed", e);
        }
    }

    /// <summary>
    /// 幂等重断言：骑士控制器被原生重置则重设；希腊缩放补断言。
    /// 原生没有任何路径会换骑士控制器（prefab 即原生，死后池 Despawn 扫不到），
    /// 重断言只兜池路径/未知重置，指针相等时零写入。
    /// </summary>
    private static void ReassertKnight(Knight knight, KnightStyleState state)
    {
        if (!state.HasStyle) return;
        try
        {
            RuntimeAnimatorController target = KnightControllers[state.StyleIndex];
            if (target != null)
            {
                Animator animator = GetKnightAnimator(knight);
                if (animator != null
                    && (animator.runtimeAnimatorController == null
                        || animator.runtimeAnimatorController.Pointer != target.Pointer))
                {
                    animator.runtimeAnimatorController = target;
                }
            }
            ApplyScale(knight, state.StyleIndex);
        }
        catch (Exception e)
        {
            LogErrorOnce("knight reassert failed", e);
        }
    }

    // ============================================================
    // D. Strip（池复用清污）
    // ============================================================

    /// <summary>
    /// 恢复缓存的原生控制器；注销缩放守卫并归还原生缩放；移出状态表。
    /// 死亡/离场不显式清理（inactive 对象 FindObjectsOfType 扫不到），
    /// 池复用由 OnKnightPromoted 的清污与 OnEnable 的 NeedsRederive 兜底。
    /// </summary>
    private static void StripKnight(Knight knight, KnightStyleState state)
    {
        int id = knight.gameObject.GetInstanceID();
        try
        {
            // 指针守卫：同 id 但已换对象（Unity instanceID 复用）时不把旧原生
            // 控制器写到新对象上——记录重置即可，新对象的原生皮肤本就原生。
            if (state.Knight == null || state.Knight.Pointer == knight.Pointer)
            {
                if (state.NativeControllerCached && state.NativeKnightController != null)
                {
                    Animator animator = GetKnightAnimator(knight);
                    if (animator != null)
                        animator.runtimeAnimatorController = state.NativeKnightController;
                }
                Mover mover = knight._mover;
                if (mover == null) mover = knight.GetComponent<Mover>();
                ScaleRegistryHolder.Unregister(mover);
                GreekScaleScope.Restore(knight.transform);
            }
        }
        catch (Exception e)
        {
            LogErrorOnce("knight strip failed", e);
        }
        finally
        {
            States.Remove(id);
        }
    }

    /// <summary>
    /// 池对象新生命周期标记（读档重生/池复用，读档不重跑转职）：
    /// 撤销旧生命的渲染记录，待读档恢复或招募路径取得本次身份。
    /// 注意 OnEnable 先于 Promote postfix（Pool.Spawn 激活在前），
    /// 标记会被随后的 Strip+Apply 覆盖，顺序天然正确。
    /// </summary>
    internal static void OnKnightActivated(Knight knight)
    {
        try
        {
            if (knight == null || knight.gameObject == null) return;
            FleetGreekSquads.ReleaseKnight(knight); // Retire any previous pooled lifetime reservation.
            int id = knight.gameObject.GetInstanceID();
            if (States.TryGetValue(id, out KnightStyleState state))
            {
                StripKnight(knight, state);
            }
        }
        catch (Exception e)
        {
            LogErrorOnce("knight activation mark failed", e);
        }
    }

    // ============================================================
    // C. World 协程：随从联动 + 完整性巡检（5s 一轮）
    // ============================================================

    internal static IEnumerator SupervisorRoutine(World world)
    {
        if (world == null || _supervisorWorld == world.Pointer) yield break;
        _supervisorWorld = world.Pointer;
        PatchRoles_GreekFireAssets.ResetWorld();

        // 新世界：旧骑士已销毁、池重建。尽力注销缩放守卫（防 instanceID 复用后
        // 错误守卫新对象），再清状态表。已销毁包装的成员访问会抛，逐项兜住。
        foreach (KeyValuePair<int, KnightStyleState> pair in States)
        {
            try
            {
                Knight knight = pair.Value.Knight;
                Mover mover = knight != null ? knight._mover : null;
                if (mover == null && knight != null) mover = knight.GetComponent<Mover>();
                if (mover != null) ScaleRegistryHolder.Unregister(mover);
                if (knight != null) GreekScaleScope.Restore(knight.transform);
            }
            catch { }
        }
        States.Clear();

        // 随从诊断基线复位：新世界的计数从零开始，首轮即可输出一次
        _followerDiagHasBaseline = false;
        _nextFollowerDiagAt = 0f;

        // Offset the first 5s integrity pass from DefenseSpacing's 3s pass.
        // The cadence remains 5s; only the initial phase moves off the common
        // level-load boundary so the two full-field scans do not land together.
        yield return new WaitForSeconds(1.5f);

        // 共享扫描缓存（抖动治理）：世界边界整体失效——首轮 IntegrityPass 的
        // 读档恢复路径必须看到全新扫描的全部存量骑士/随从。
        UnitScanCache.InvalidateAll();

        while (world != null && world.gameObject != null)
        {
            IntegrityPass();
            yield return new WaitForSeconds(IntegrityIntervalSeconds);
        }
    }

    /// <summary>
    /// 完整性巡检（两段结构）：
    /// 第一段——处理所有 Knight：
    /// 1) 读档/客户端同步恢复的存量骑士（无 Promote 机会）→ 读取固定收据；
    /// 2) 收据与渲染尚未对齐 → 幂等应用；
    /// 3) 已定风格骑士 → 重断言控制器/缩放（被原生重置则重设）。
    /// 第二段——随从联动（反向归属）：全场扫 Archer 读 _knight 查状态，
    /// 把随从的士兵控制器覆盖成风格对应款。放在骑士段之后：本轮新上风格/
    /// 重算收敛的骑士立即可被反查命中，Reviewer Q-1（重算分支的骑士随从
    /// 不饥饿）由本段天然满足。
    /// </summary>
    private static void IntegrityPass()
    {
        KnightIdentityRuntime.Poll();
        if (!ModConfig.Enabled.Value) return;
        try
        {
            EnsureStyleAssets();
            if (!HasUsablePool()) return;

            // ---- 第一段：骑士 ----
            // 共享缓存，抖动治理：骑士扫描走 UnitScanCache（3s 窗口，与
            // DefenseSpacing 的 3s 拍共用一份；本 5s 巡检的新鲜度要求——新招募
            // 骑士——由 Promote postfix 即时上风格保证，缓存 3s < 原 5s 节奏）。
            Knight[] knights = UnitScanCache.GetKnights();
            // 功能 A（首见均匀分配）：零记录装载骑士在 PrimeExisting 之前一次批量均匀分配（取代逐人
            // 哈希迁移）。MarkedNew（新招募 ChooseBalanced）/已有收据/FailedLoad/已在场风格不在本批范围；
            // unresolved/失配/冲突上下文由本方法内部门拒绝（只能经面板 + 设计 C 处理）。
            KnightIdentityRuntime.AssignFirstSeenUniform(knights, AvailableStyles, HasLiveStyleForFreeze);
            KnightIdentityRuntime.PrimeExisting(knights, GetLiveStyleForFreeze);
            if (knights != null)
            {
                for (int i = 0; i < knights.Length; i++)
                {
                    Knight knight = knights[i];
                    if (knight == null || knight.gameObject == null
                        || !knight.gameObject.activeInHierarchy) continue;
                    if (knight.tag != "Knight") continue; // Squire 不处理

                    int id = knight.gameObject.GetInstanceID();
                    if (!States.TryGetValue(id, out KnightStyleState state))
                    {
                        // 读档恢复或首次迁移；客户端等待主机收据。
                        ApplyKnightStyle(knight);
                        continue;
                    }

                    if (state.NeedsRederive
                        || !KnightIdentityRuntime.TryGetReceipt(knight, out KnightIdentityReceipt receipt)
                        || state.StyleIndex != receipt.Style)
                    {
                        // 仅对齐固定身份与渲染，不因网络头或资源池变化重新抽类型。
                        ApplyKnightStyle(knight);
                        continue;
                    }

                    ReassertKnight(knight, state);
                }
            }

            KnightIdentityLoadSeed.Flush();

            // ---- 第二段：随从联动（反向归属，全场一次扫描）----
            StyleFollowersByLookup(knights);

            // ---- 第三段：北境小队巡检（norse-squad-027，复用本 5s 节奏）----
            // 北境骑士的随从：非北境 prefab → 补转化（读档后骑士重新 Fetch 拉来的
            // 普通随从/存量场景）；北境 prefab 无盾 → 幂等装盾（盾门：带盾组件无盾
            // 不能入队）。实现与失败降级见 PatchRoles_NorseSquad.PatrolPass。
            PatchRoles_NorseSquad.PatrolPass();
            KnightIdentityNetwork.Sync(knights);
        }
        catch (Exception e)
        {
            LogErrorOnce("integrity pass failed", e);
        }
    }

    /// <summary>
    /// 单随从换皮（统一写入路径）：读 archer._knight，队籍骑士在状态表且有风格
    /// → 把（有效风格的，见 ResolveEffectiveFollowerStyleIndex）SoldierController
    /// 写到 archer._animator——跨队回收的北境随从（有 NpcShieldUser）一律用
    /// 北境款，近战行为与士兵皮自洽（Reviewer Q1）。
    /// 写入条件沿用：animator/current 非空、指针不等才写（幂等零写入）。
    ///
    /// 死地随从"无标记弩手化"（用户拍板）：骑士风格==死地 → 追加
    /// Crossbowman.ApplySquadCrossbowPackage（弩矢/伤害/射程/间隔/体型 1.15 与
    /// 弩手一致；deadlands 士兵皮与弩手皮相同，视觉统一）；非死地/无骑士/无风格
    /// → RestoreSquadCrossbowPackage（幂等 no-op）。关键约束：随从绝不挂
    /// CrossbowmanMarker（标记=拒绝骑士招募，它们就是队员）；弩手本体永不入队
    /// （IsAvailableForJob 排除），两个群体不相交，无冲突。死地随从缩放（1.15）
    /// 由该包统一管理，本文件的 EnsureFollowerScale 死地分支跳过。
    ///
    /// 翻牌机制（历史背景，幕府之谜诊断实锤；2026-09-24 已由皮肤生根根治）：夜间 diag curTop=
    /// archer_soldier_greece×56 + archer_soldier×20（期望 med20=archer_soldier✓、
    /// dead28=deadlands✗、shog8=bamboo✗、gree20=greece✓）——56=dead+shog+gree
    /// 全停在原生希腊士兵皮。我们每 5s 写一次，而原生 ConvertToSoldier（跟队例程
    /// 重入时调用，Archer.cs:859/485）每次都把控制器刷回 BiomeData 换皮的世界
    /// 原生皮（~10s 一轮），5s 写 vs ~10s 刷回的翻牌让视觉上绝大多数时间停在
    /// 原生皮。中世纪幸存是因为基底 archer_soldier 恰好不在"被刷回"路径的
    /// 目标集合里。根治（与弩手同构）：本方法把实例的 Archer.soldierAnimator 指到
    /// 风格士兵控制器——原生换皮对未注册 original 原样穿透，原生自己写出的就是
    /// 同一引用（同引用重赋无害），刷回窗口消失；ConvertToSoldier/ConvertToHunter 的
    /// postfix 与 5s 巡检退化为防御与指针校验（见文件尾两个 patch 类）。
    /// </summary>
    /// <summary>
    /// 随从的有效风格 index（Reviewer Q1 修订）：跨队回收的北境随从——北境骑士
    /// 战死后随从保留北境 prefab+盾组件（NpcShieldUser 原生只在 Archer_norselands
    /// 上），被非北境骑士原生路径收编——一律按北境款（NorseStyleIndex）处理：
    /// attack/defend/getshield/retreat 近战 clip 只存在于北境士兵控制器族，写非
    /// 北境皮会在近战时视觉冻结。跨队回收北境随从永远穿北境士兵皮，与近战/盾墙
    /// 行为自洽；死地弩手化包/中世纪缩放等同用有效 index 判定（北境 prefab 不吃
    /// 弩手包——盾墙近战与 12 射程弩投射互斥；缩放回北境原生 1.0）。
    /// 北境款士兵控制器未解析（风格池收缩时）回落骑士风格（现行逻辑）。
    /// 判定成本：GetComponent&lt;NpcShieldUser&gt; 只在骑士风格非北境且控制器已解析
    /// 时才发生（5s×随从数，reviewer 认定可接受；北境骑士名下的随从零额外开销）。
    /// </summary>
    private static int ResolveEffectiveFollowerStyleIndex(Archer archer, KnightStyleState state)
    {
        try
        {
            if (state.StyleIndex != NorseStyleIndex
                && SoldierControllers[NorseStyleIndex] != null
                && archer != null
                && PatchRoles_NorseSquad.IsNorseArcherInstance(archer))
                return NorseStyleIndex;
            return state.StyleIndex;
        }
        catch
        {
            return state.StyleIndex;
        }
    }

    internal static bool ApplyFollowerSkinTo(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return false;
            PatchRoles_GreekFireAssets.RestoreMissing(archer);

            // 真弩手（marker 群体）绝不碰 squad 包：其 ActiveArrowAttack 同样指向
            // 克隆 SO，RestoreSquadCrossbowPackage 的指针判据无法区分群体，会把
            // 弩手战斗包误拆（间隔/扫描器/缩放在 Crossbowman.IntegrityPass 里
            // 无自愈路径）。弩手不入骑士队，正常流程到不了这里；弩手上塔/上船
            // 同样触发 ConvertToSoldier → 本 postfix，此检查是必须的防御。
            if (PatchRoles_Crossbowman.IsCrossbowman(archer)) return false;

            Knight knight = archer._knight;
            if (knight == null || knight.gameObject == null)
            {
                // 无队籍（离队/猎人）：撤弩手化包（幂等 no-op）——离队瞬间在
                // ConvertToHunter postfix 走到这里，战斗数值随猎人身份还原；
                // 生根字段同时写回原生快照，风格皮绝不泄漏给下一个池 life。
                RestoreFollowerSoldierAnimator(archer);
                RepairLeakedFollowerSkin(archer);
                PatchRoles_Crossbowman.RestoreSquadCrossbowPackage(archer);
                return false;
            }
            if (!States.TryGetValue(knight.gameObject.GetInstanceID(), out KnightStyleState state)
                || !state.HasStyle)
            {
                // 骑士未上风格：同样撤包（换队过渡/新骑士未定型期间不持弩手数值）；
                // 生根字段归位（本对象可能带着上一个风格的池 life 残留）。
                RestoreFollowerSoldierAnimator(archer);
                RepairLeakedFollowerSkin(archer);
                PatchRoles_Crossbowman.RestoreSquadCrossbowPackage(archer);
                return false;
            }

            // 有效风格（Reviewer Q1）：跨队回收的北境随从按北境款，近战行为自洽
            int styleIndex = ResolveEffectiveFollowerStyleIndex(archer, state);

            // 死地随从弩手化包：Apply 幂等（SO 指针判重）；非死地随从撤包（换队到
            // 非死地骑士时战斗数值跟随还原）。北境 prefab 随从走有效 index → 永不
            // Apply（近战/盾墙与弩包互斥），回收进死地骑士队时自动撤包
            if (styleIndex == DeadlandsStyleIndex)
                PatchRoles_Crossbowman.ApplySquadCrossbowPackage(archer);
            else
                PatchRoles_Crossbowman.RestoreSquadCrossbowPackage(archer);

            RuntimeAnimatorController target = SoldierControllers[styleIndex];
            if (target == null) return false;

            Animator animator = archer._animator;
            if (animator == null) animator = archer.GetComponentInChildren<Animator>();

            // 表现即时写入（animator 就绪且控制器非空时，语义与旧实现一致）
            bool wrote = false;
            RuntimeAnimatorController current = animator != null
                ? animator.runtimeAnimatorController : null;
            if (current != null && current.Pointer != target.Pointer)
            {
                animator.runtimeAnimatorController = target;
                wrote = true;
            }

            // 生根：字段指向风格皮后，原生 ConvertToSoldier 自己写出的就是同一控制器
            // （未注册 original 原样穿透）——刷回战争消失，postfix/巡检退化为防御。
            if (RootFollowerSoldierAnimator(archer, target)) wrote = true;
            return wrote;
        }
        catch (Exception e)
        {
            LogErrorOnce("follower skin apply failed", e);
            return false;
        }
    }

    /// <summary>
    /// 随从皮肤生根（2026-09-24 双写者根治，与弩手 CrossbowmanLifecycle 同构）：把实例的
    /// Archer.soldierAnimator 指到风格士兵控制器——原生每次 ConvertToSoldier 解析
    /// BiomeData.GetAssetSwapForThis(soldierAnimator)，swap 表对未注册 original 原样穿透
    /// （BiomeSwapData.GetAnimSwap 未命中字典 → 返回入参），原生自己写出的就是同一引用
    /// （同引用重赋无害）→ 单写者、零重绑、零竞态。幂等：指针相等零写入；返回是否发生写入。
    /// </summary>
    private static bool RootFollowerSoldierAnimator(Archer archer, RuntimeAnimatorController target)
    {
        if (target == null) return false;
        RuntimeAnimatorController current = archer.soldierAnimator;
        if (current != null && current.Pointer == target.Pointer) return false;
        archer.soldierAnimator = target;
        return true;
    }

    /// <summary>
    /// 离队/无风格随从的防泄漏还原：soldierAnimator 写回原生 Archer prefab 快照
    /// （PatchRoles_Crossbowman.BaseSoldierAnimator；holder 未就绪 → 跳过，下次入口再试），
    /// 让后续原生 ConvertToSoldier 重新解析出所在世界的原生士兵皮。已在基线上 → 零写入。
    /// </summary>
    private static void RestoreFollowerSoldierAnimator(Archer archer)
    {
        RuntimeAnimatorController baseAnimator = PatchRoles_Crossbowman.BaseSoldierAnimator;
        if (baseAnimator == null) return;
        RootFollowerSoldierAnimator(archer, baseAnimator);
    }

    /// <summary>
    /// 配置关窄还原（审查 P1-1）：不涂任何风格皮，只把可能生根的随从字段还原成
    /// Archer prefab 快照并修复上一 life 的风格皮泄漏；北境实例同 RepairLeakedFollowerSkin
    /// 的门（P2-1 对称性）——字段还原也跳过，由北境语义自管。触发点=两个 Convert
    /// postfix 的配置关分支。
    /// </summary>
    internal static void RestoreRootedFollowerOnDisable(Archer archer)
    {
        try
        {
            if (PatchRoles_NorseSquad.IsNorseArcherInstance(archer)) return;
            RestoreFollowerSoldierAnimator(archer);
            RepairLeakedFollowerSkin(archer);
        }
        catch { }
    }

    /// <summary>
    /// 无队籍对象身上的"风格士兵皮"只可能来自上一 life 的生根残留（随从实例没有弩手
    /// marker，池边界不会替它收尾）：把当前控制器重解析成所在世界的原生士兵皮
    /// （= 原生 ConvertToSoldier 的解析式，此时生根字段已还原成快照）。当前控制器不是
    /// 风格族（猎人皮/世界原生皮）→ 零写入；北境 prefab 近战族由其自身语义管理，不碰。
    /// 触发点：无队籍/无风格分支（事件 postfix 与 5s 巡检共用），随新 life 的首次
    /// ConvertToSoldier 在同一调用栈内收敛。
    /// </summary>
    private static void RepairLeakedFollowerSkin(Archer archer)
    {
        try
        {
            Animator animator = archer._animator;
            if (animator == null) animator = archer.GetComponentInChildren<Animator>();
            RuntimeAnimatorController current = animator != null
                ? animator.runtimeAnimatorController : null;
            if (current == null || !IsStyleSoldierController(current)) return;
            // 北境近战/盾墙族的皮肤由 PatchRoles_NorseSquad 语义管理（非北境皮会冻结
            // attack/defend 近战 clip），泄漏修复不碰北境 prefab 实例。
            if (PatchRoles_NorseSquad.IsNorseArcherInstance(archer)) return;

            RuntimeAnimatorController baseAnimator = PatchRoles_Crossbowman.BaseSoldierAnimator;
            BiomeData biome = BiomeData.Current;
            if (baseAnimator == null || biome == null) return;
            RuntimeAnimatorController native = biome.GetAssetSwapForThis<RuntimeAnimatorController>(baseAnimator);
            if (native == null || native.Pointer == current.Pointer) return;
            animator.runtimeAnimatorController = native;
        }
        catch (Exception e)
        {
            LogErrorOnce("follower skin leak repair failed", e);
        }
    }

    private static bool IsStyleSoldierController(RuntimeAnimatorController controller)
    {
        for (int i = 0; i < StyleCount; i++)
            if (SoldierControllers[i] != null && controller.Pointer == SoldierControllers[i].Pointer)
                return true;
        return false;
    }

    // ---- 夜间随从锚点拉回量（DefenseSpacing 消费 API）-------------------------
    // 依据：死地随从=弩手（射程 12）——站深处不打折，且用户实锤贴墙触发高抛，
    // 应更深 → 6.5f；普通随从是弓（射程 8）——不能再深，锚点 4.2f 让编队前排
    // ≈墙内 2、后排 ≈墙内 6，全部留在射程 8 内（v2.1 "后排射程外" 老问题的
    // 教训：锚点再深会把后排推出射程）。查不到风格/无状态记录一律回落 4.2f。
    internal const float DefaultFollowerAnchorPullback = 4.2f;
    private const float DeadlandsFollowerAnchorPullback = 6.5f;

    /// <summary>
    /// 查该骑士风格对应的夜间随从编队锚点拉回量（墙内步数）：死地 → 6.5f，
    /// 其余（含北境=普通弓随从语义、查不到/无状态记录/入参为空）→ 4.2f。
    /// 北境随从虽是近战/盾墙双模式，默认仍按普通随从站位（用户未提出更深入
    /// 需求前不做特判）。纯读、无副作用、不抛出。
    /// </summary>
    internal static float GetFollowerAnchorPullback(Knight knight)
    {
        try
        {
            if (knight == null || knight.gameObject == null)
                return DefaultFollowerAnchorPullback;
            if (!States.TryGetValue(knight.gameObject.GetInstanceID(),
                out KnightStyleState state) || !state.HasStyle)
                return DefaultFollowerAnchorPullback;
            return state.StyleIndex == DeadlandsStyleIndex
                ? DeadlandsFollowerAnchorPullback
                : DefaultFollowerAnchorPullback;
        }
        catch
        {
            return DefaultFollowerAnchorPullback;
        }
    }

    /// <summary>
    /// 该骑士是否已被定为北境风格（norse-squad-027 联动判定）：状态表有记录、
    /// 已上风格且 StyleIndex==NorseStyleIndex。纯读、无副作用、不抛出。
    /// PatchRoles_NorseSquad 用它决定"随从是否要转化/装盾/巡检"。
    /// </summary>
    // 候选诊断用：北境门的失败原因细化（IsNorseStyleKnight 的字符串版，供
    // ShieldWallTotem 图腾征召诊断逐骑士记录；纯读不改状态）。
    internal static string NorseGateReason(Knight knight)
    {
        try
        {
            if (knight == null || knight.gameObject == null) return "null-knight";
            if (!States.TryGetValue(knight.gameObject.GetInstanceID(), out KnightStyleState state))
                return "no-state";
            if (!state.HasStyle) return "no-style";
            if (state.NeedsRederive) return "needs-derivation";
            if (state.Knight == null) return "state-knight-null";
            if (state.Knight.Pointer != knight.Pointer) return "pointer-mismatch";
            if (state.StyleIndex != NorseStyleIndex) return "style=" + state.StyleIndex;
            return "norse-ok";
        }
        catch (Exception e) { return "gate-error:" + e.GetType().Name; }
    }

    internal static bool IsNorseStyleKnight(Knight knight)
    {
        try
        {
            if (knight == null || knight.gameObject == null) return false;
            if (!States.TryGetValue(knight.gameObject.GetInstanceID(),
                out KnightStyleState state) || !state.HasStyle
                || state.NeedsRederive || state.Knight == null
                || state.Knight.Pointer != knight.Pointer) return false;
            return state.StyleIndex == NorseStyleIndex;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 区分“风格尚未解析”和“已解析且不是北境”。读档恢复阶段不能把前者
    /// 当成非北境，否则随从会在 KnightStyle 首轮之前被错误移出待处理队列。
    /// </summary>
    internal static bool TryGetResolvedStyleIndex(Knight knight, out int styleIndex)
    {
        styleIndex = -1;
        try
        {
            if (knight == null || knight.gameObject == null) return false;
            if (!States.TryGetValue(knight.gameObject.GetInstanceID(), out KnightStyleState state)
                || !state.HasStyle) return false;
            // Instance IDs are recycled across pooled objects and scene loads.  Never
            // let a stale style record authorize a different Knight instance.
            if (state.Knight == null || state.Knight.Pointer != knight.Pointer
                || state.NeedsRederive) return false;
            styleIndex = state.StyleIndex;
            return styleIndex >= 0 && styleIndex < StyleCount;
        }
        catch
        {
            styleIndex = -1;
            return false;
        }
    }

    /// <summary>
    /// 风格面板（KnightStylePanel）门槛：风格资产池是否可用。面板应用前必须为真；
    /// 为假时面板禁用应用并提示（身份写入会等资产，绝不因缺资产重映射已有类型）。
    /// </summary>
    internal static bool HasStylePool()
    {
        return HasUsablePool();
    }

    /// <summary>
    /// 面板重派后的表现应用（复用既有 ApplyKnightStyle：读固定收据 → 设控制器/缩放/状态表）。
    /// 身份已由面板写入；表现失败只等既有巡检重试，不影响收据。
    /// </summary>
    internal static void ApplyPanelRestyle(Knight knight)
    {
        ApplyKnightStyle(knight);
    }

    /// <summary>
    /// 随从联动（反向归属 + 队籍判定，5s 巡检兜底）：彻底放弃枚举 knight._archers——
    /// Il2Cpp 非泛型枚举器对 HashSet 运行时不可靠，纯读快照段的 MoveNext 也抛
    /// InvalidOperationException。改为全场反查：每个 active Archer 读 _knight
    /// （interop 私有字段，PatchRoles_Crossbowman.cs:687 先例），按骑士 gameObject
    /// instanceID（状态表键）查 KnightStyleState，有风格则把随从控制器换成风格款。
    /// 骑士无状态记录则跳过（第一段已负责给无记录骑士上风格，本轮/下轮跟上）。
    /// 写入条件（follower diag 实测修订，队籍判定）：_knight 指向已风格化骑士
    /// 即写——只要当前控制器指针 != 目标就覆盖，无论当前是猎人皮、士兵皮还是
    /// 北境款。原"∈士兵族才写"在白天永远不命中（原生随从只在 actively 跟队时
    /// ConvertToSoldier，白天分散打猎穿猎人皮，diag 实锤 styled=0/skippedFamily=76）。
    /// 队籍判定的代价与收益：白天分散打猎的随从也穿风格士兵皮（用户要能随时
    /// 认出随从归属）；离队时原生 ConvertToHunter 自动恢复猎人皮
    /// （GetAssetSwapForThis(hunterAnimator)），我们无需清理；重新入队时原生先
    /// 换回世界士兵皮、我们 5s 内再盖上风格皮，过渡期 ≤5s 可接受。
    /// 弩手按设计不入骑士队（IsAvailableForJob 已排除），此处防御性跳过。
    /// 注意：_knight 是 Il2Cpp 对象，fake-null 语义下判空可用重载 == null，
    /// 取 ID 前再判一次；只按 instanceID 查表，不做托管等值比较。
    /// </summary>
    private static void StyleFollowersByLookup(Knight[] knights)
    {
        try
        {
            // 共享缓存，抖动治理：随从反查的 Archer 扫描走 UnitScanCache（3s 窗口，
            // 与 DefenseSpacing 的 3s 拍共用一份——同一 IntegrityPass 内本调用与
            // 第一段的 Knight 扫描不再各扫各的）。新鲜度：随从换皮/缩放/弩包的
            // 即时性由 ConvertToSoldier/Hunter postfix 事件路径保证，本 5s 巡检
            // 只是兜底，缓存 3s < 原 5s 节奏。
            Archer[] archers = UnitScanCache.GetArchers();
            if (archers == null) return;
            SquadRosterSnapshot.Observe(knights, archers);

            // 管线诊断计数（只记录不改行为，输出见 LogFollowerDiag）：
            // archers=active Archer 总数；withKnight=_knight 非空；inStates=withKnight
            // 里其骑士在状态表且有风格的；styled=本轮实际写入控制器的；
            // skippedFamily=当前控制器已是目标风格而跳过（幂等零写入；字段名沿用
            // 历史 diag 行结构，语义随队籍判定更新——旧语义"不在士兵族跳过"已废弃）；
            // skippedOther=其他原因跳过（弩手/animator 空/当前控制器空/target 空）
            int diagArchers = 0, diagWithKnight = 0, diagInStates = 0, diagStyled = 0;
            int diagSkippedFamily = 0, diagSkippedOther = 0;
            float diagSampleX = 0f;
            string diagSampleController = "<no withKnight sample>";
            bool diagSampleTaken = false;
            // [3b] per-style 目标分布（各风格骑士名下的随从数）与当前控制器名频次
            // （curTop top2）——定位"某风格随从不换皮"（如幕府解析到错误对象时，
            // shogun 目标随从的 curTop 仍是非幕府士兵皮）
            var diagStyleTargets = new int[StyleCount];
            var diagCurrentNames = new Dictionary<string, int>();

            for (int i = 0; i < archers.Length; i++)
            {
                try
                {
                    Archer archer = archers[i];
                    if (archer == null || archer.gameObject == null
                        || !archer.gameObject.activeInHierarchy) continue;
                    diagArchers++;
                    PatchRoles_GreekFireAssets.RestoreMissing(archer);

                    // 弩手按设计不入骑士队（IsAvailableForJob 已排除），防御性跳过；
                    // 诊断计入 skippedOther（"其他原因"之一）。内部有注册防御，安全。
                    // 注意：弩手缩放（1.15）归 Crossbowman 管，本补丁绝不碰其缩放。
                    if (PatchRoles_Crossbowman.IsCrossbowman(archer))
                    {
                        diagSkippedOther++;
                        continue;
                    }

                    Knight knight = archer._knight;
                    if (knight == null || knight.gameObject == null)
                    {
                        // 无骑士（离队/猎人）：随从缩放确保回 1（幂等；曾随中世纪
                        // 骑士放大到 1.05 的随从离队后在此归位）；同时撤弩手化包
                        // （幂等 no-op，离队主路径在 ConvertToHunter postfix）；
                        // 生根字段归位，风格皮绝不泄漏给下一个池 life。
                        RestoreFollowerSoldierAnimator(archer);
                        RepairLeakedFollowerSkin(archer);
                        PatchRoles_Crossbowman.RestoreSquadCrossbowPackage(archer);
                        EnsureFollowerScale(archer, 1f);
                        continue;
                    }
                    diagWithKnight++;

                    // 当前控制器名：样本与 [3b] 频次共用同一次查询
                    Animator diagAnimator = archer._animator;
                    if (diagAnimator == null)
                        diagAnimator = archer.GetComponentInChildren<Animator>();
                    RuntimeAnimatorController diagController = diagAnimator != null
                        ? diagAnimator.runtimeAnimatorController : null;
                    string controllerName = diagController != null
                        ? diagController.name : "<null>";
                    if (diagCurrentNames.TryGetValue(controllerName, out int nameCount))
                        diagCurrentNames[controllerName] = nameCount + 1;
                    else diagCurrentNames[controllerName] = 1;

                    // 样本 = 第一个 withKnight 弓箭手的 x 坐标 + 当前控制器名：
                    // 队籍判定下主要用于观测皮肤分布（猎人皮=白天分散/离队瞬间；
                    // 士兵皮=跟队或已被我们覆盖）
                    if (!diagSampleTaken)
                    {
                        diagSampleTaken = true;
                        diagSampleX = archer.transform.position.x;
                        diagSampleController = controllerName;
                    }

                    // 状态表键 = 骑士 gameObject.GetInstanceID()（与全部写入点一致）
                    KnightStyleState state;
                    if (!States.TryGetValue(knight.gameObject.GetInstanceID(), out state))
                    {
                        // 骑士未上风格（第一段本轮/下轮会补）：随从缩放先确保回 1；
                        // 弩手化包同撤（幂等 no-op）；生根字段归位（防池 life 泄漏）
                        RestoreFollowerSoldierAnimator(archer);
                        RepairLeakedFollowerSkin(archer);
                        PatchRoles_Crossbowman.RestoreSquadCrossbowPackage(archer);
                        EnsureFollowerScale(archer, 1f);
                        continue;
                    }
                    if (!state.HasStyle)
                    {
                        RestoreFollowerSoldierAnimator(archer);
                        RepairLeakedFollowerSkin(archer);
                        PatchRoles_Crossbowman.RestoreSquadCrossbowPackage(archer);
                        EnsureFollowerScale(archer, 1f);
                        continue;
                    }
                    diagInStates++;

                    // 有效风格（Reviewer Q1）：跨队回收的北境随从（保留北境 prefab+
                    // 盾组件，被非北境骑士收编）按北境款——近战 clip 只在北境族，
                    // 写非北境皮近战时视觉冻结。后续 diag 计数/缩放/弩包/目标控制器
                    // 全部走有效 index；北境款未解析时回落骑士风格（现行逻辑）
                    int effectiveStyleIndex = ResolveEffectiveFollowerStyleIndex(archer, state);
                    if (effectiveStyleIndex >= 0 && effectiveStyleIndex < StyleCount)
                        diagStyleTargets[effectiveStyleIndex]++; // 按实际穿的皮计（reviewer 拍板）

                    // 随从缩放（[2]）：中世纪 1.05，其余 1.0；随从换队（骑士死了
                    // 改投他人）时每轮幂等重算，缩放自动跟随新骑士风格。
                    // 死地随从例外：缩放（1.15）由 ApplySquadCrossbowPackage 作为
                    // 弩手化包的一部分统一管理，此处跳过避免两个写入者互相覆盖。
                    // 回收北境随从走有效 index：缩放 1.15（不吃弩包的独立 1.15 写入者）
                    if (effectiveStyleIndex != DeadlandsStyleIndex)
                    {
                        EnsureFollowerScale(archer,
                            effectiveStyleIndex == MedievalStyleIndex ? FollowerMedievalScaleY
                            : effectiveStyleIndex == NorseStyleIndex ? FollowerNorseScaleY : 1f);
                    }

                    // 死地随从弩手化包：与皮肤写入独立调用（均幂等）——皮肤已是
                    // 目标（skippedFamily）或资产缺失（skippedOther）的分支不会走
                    // ApplyFollowerSkinTo，包仍需按风格上/撤；死地世界里死地随从的
                    // 原生皮恰好就是目标皮，皮写入路径不可靠，包必须独立保证。
                    // 回收北境随从走有效 index → 永不 Apply（近战/盾墙与弩包互斥）
                    if (effectiveStyleIndex == DeadlandsStyleIndex)
                        PatchRoles_Crossbowman.ApplySquadCrossbowPackage(archer);
                    else
                        PatchRoles_Crossbowman.RestoreSquadCrossbowPackage(archer);

                    RuntimeAnimatorController target = SoldierControllers[effectiveStyleIndex];
                    if (target == null) { diagSkippedOther++; continue; }

                    Animator animator = diagAnimator; // 复用上面的查询结果
                    if (animator == null) { diagSkippedOther++; continue; }

                    RuntimeAnimatorController current = diagController;
                    if (current == null) { diagSkippedOther++; continue; }
                    if (current.Pointer == target.Pointer)
                    {
                        // 已是目标风格：控制器幂等零写入（计入 skippedFamily）；
                        // 生根字段只做指针校验（池/原生重置翻回基线时补种），稳态零写入。
                        RootFollowerSoldierAnimator(archer, target);
                        diagSkippedFamily++;
                        continue;
                    }
                    // 队籍判定：不再检查当前皮肤族——猎人皮/世界士兵皮/北境款一律覆盖。
                    // 写入统一走 ApplyFollowerSkinTo（与 ConvertToSoldier/Hunter 的
                    // postfix 即时重涂同一条路径；内部重复做幂等检查，无害）。
                    // 生根后切换由单写者保证：本 5s 巡检与 postfix 只兜挂钩前存量与
                    // 指针校验（原生自己写出的已是同一控制器，不再有刷回窗口）。
                    if (ApplyFollowerSkinTo(archer)) diagStyled++;
                    else diagSkippedOther++;
                }
                catch (Exception e)
                {
                    // 单个随从失败（扫描与写入之间被销毁等）不拖累其余随从
                    LogErrorOnce("follower styling failed", e);
                }
            }

            LogFollowerDiag(diagArchers, diagWithKnight, diagInStates, diagStyled,
                diagSkippedFamily, diagSkippedOther, diagSampleX, diagSampleController,
                "med:" + diagStyleTargets[0] + " dead:" + diagStyleTargets[1]
                + " shog:" + diagStyleTargets[2] + " gree:" + diagStyleTargets[3]
                + " norse:" + diagStyleTargets[4],
                BuildTopControllerNames(diagCurrentNames));
        }
        catch (Exception e)
        {
            LogErrorOnce("follower styling failed", e);
        }
    }

    /// <summary>
    /// 随从换皮管线诊断日志（只记录不改行为）：一行输出各环节数量 +
    /// 首个 withKnight 样本 + per-style 目标分布 + 当前控制器 top2（[3b]），
    /// 定位效果在哪一环丢弃。限频：距上次实际输出 ≥60s，且本轮计数与上次
    /// 输出时的缓存不同才输出（纯读、无行为影响）；世界切换时
    /// SupervisorRoutine 复位基线，新世界首轮立即可输出。
    /// styleTargets/curTop 不参与变化检测：计数变化通常已触发，避免缓存膨胀。
    /// </summary>
    private static void LogFollowerDiag(int archers, int withKnight, int inStates,
        int styled, int skippedFamily, int skippedOther, float sampleX, string sampleController,
        string styleTargets, string curTop)
    {
        try
        {
            // 无基线（世界切换后首轮）视为有变化，允许立即输出一次
            bool changed = !_followerDiagHasBaseline
                || archers != _diagLastArchers
                || withKnight != _diagLastWithKnight
                || inStates != _diagLastInStates
                || styled != _diagLastStyled
                || skippedFamily != _diagLastSkippedFamily
                || skippedOther != _diagLastSkippedOther;
            if (!changed) return;
            if (Time.time < _nextFollowerDiagAt) return;

            _followerDiagHasBaseline = true;
            _nextFollowerDiagAt = Time.time + FollowerDiagMinIntervalSeconds;
            _diagLastArchers = archers;
            _diagLastWithKnight = withKnight;
            _diagLastInStates = inStates;
            _diagLastStyled = styled;
            _diagLastSkippedFamily = skippedFamily;
            _diagLastSkippedOther = skippedOther;
            LogInfo("follower diag: archers=" + archers
                + " withKnight=" + withKnight
                + " inStates=" + inStates
                + " styled=" + styled
                + " skippedFamily=" + skippedFamily
                + " skippedOther=" + skippedOther
                + " sample=archer@" + sampleX.ToString("F1")
                + " controller=" + sampleController
                + " styleTargets=" + styleTargets
                + " curTop=" + curTop);
        }
        catch (Exception e)
        {
            LogErrorOnce("follower diag failed", e);
        }
    }

    /// <summary>
    /// [3b] 当前控制器名频次 top2（"名:xN 名:xN"格式，不足两项则一项，
    /// 无随从则 &lt;none&gt;）——与 styleTargets 交叉定位"某风格随从不换皮"：
    /// 如 shogun 有目标随从但 curTop 全是非幕府皮，即目标分发/解析问题。
    /// </summary>
    private static string BuildTopControllerNames(Dictionary<string, int> counts)
    {
        string firstName = null, secondName = null;
        int firstCount = 0, secondCount = 0;
        foreach (KeyValuePair<string, int> pair in counts)
        {
            if (pair.Value > firstCount)
            {
                secondName = firstName;
                secondCount = firstCount;
                firstName = pair.Key;
                firstCount = pair.Value;
            }
            else if (pair.Value > secondCount)
            {
                secondName = pair.Key;
                secondCount = pair.Value;
            }
        }
        if (firstName == null) return "<none>";
        if (secondName == null) return firstName + ":x" + firstCount;
        return firstName + ":x" + firstCount + " " + secondName + ":x" + secondCount;
    }
}

/// <summary>
/// B. 转职主入口：捡护甲成功转职骑士（Character.Promote → ReplaceBy → Pool.Spawn）
/// 后优先补少并冻结类型。同签名多 postfix 先例：PatchRoles_Worker / Crossbowman。
/// </summary>
[HarmonyPatch(typeof(Character), nameof(Character.Promote),
    new[] { typeof(DroppableTool), typeof(IUnitController) })]
public static class Character_Promote_KnightStyle_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Character __result, DroppableTool tool)
    {
        // 非护甲零开销早退（不碰 try）
        if (tool == null || tool.tag != "Armor") return;
        try
        {
            PatchRoles_KnightStyle.OnKnightPromoted(__result);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[KnightStyle/promote] " + e);
        }
    }
}

/// <summary>
/// 池对象新生命周期标记：Knight.OnEnable（私有，字符串名打补丁）时，若该实例
/// 已带风格记录（读档重生/池复用），标记待重算。客户端 OnEnable 会提前
/// base.enabled=false 返回，postfix 照常执行，标记是纯本地簿记，无害。
/// </summary>
[HarmonyPatch(typeof(Knight), "OnEnable")]
public static class Knight_OnEnable_KnightStyleRederive_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Knight __instance)
    {
        if (__instance == null) return;
        try
        {
            KnightIdentityRuntime.OnEnable(__instance);
            PatchRoles_KnightStyle.OnKnightActivated(__instance);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[KnightStyle/on-enable] " + e);
        }
    }
}

/// <summary>
/// C. World 协程宿主（范式同 PatchWorld_DefenseSpacing / SerpentLeash / Crossbowman）：
/// per-world 指针守卫；world 销毁时协程随宿主自然退出（while 守卫兜底）。
/// </summary>
[HarmonyPatch(typeof(World), nameof(World.OnLevelLoaded))]
public static class World_OnLevelLoaded_KnightStyleHost_Patch
{
    [HarmonyPostfix]
    private static void Postfix(World __instance)
    {
        if (!ModConfig.Enabled.Value || __instance == null) return;
        try
        {
            __instance.StartCoroutine(
                PatchRoles_KnightStyle.SupervisorRoutine(__instance).WrapToIl2Cpp());
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[KnightStyle] supervisor start failed: " + e);
        }
    }
}

/// <summary>
/// 翻牌防御之一（私有方法按名打补丁，先例：Knight.OnEnable 字符串名补丁）：
/// 原生 ConvertToSoldier（跟队例程重入/上塔/上船时调用，Archer.cs:859）会经
/// BiomeData 换皮写控制器——皮肤生根（ApplyFollowerSkinTo 写士兵实例的
/// Archer.soldierAnimator）后原生解析出的已是风格皮本身，本 postfix 只作防御：
/// 兜挂钩前存量、指针校验与风格变更（面板重派/换队）的即时重涂。
/// 弩手由 IsCrossbowman 早退（真弩手走 CrossbowmanLifecycle 自己的生根/还原）；
/// 无队籍随从会做生根字段还原+泄漏修复（池边界不覆盖随从实例）。
/// </summary>
[HarmonyPatch(typeof(Archer), "ConvertToSoldier")]
public static class Archer_ConvertToSoldier_KnightStyleSkin_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Archer __instance)
    {
        if (__instance == null) return;
        try
        {
            // 配置关（审查 P1-1）：随从实例无 marker，弩手池边界不覆盖——这里与巡检是
            // 随从生根字段仅有的还原入口。关后仍走窄还原（字段快照+泄漏修复），否则
            // 已生根随从粘死风格皮并跨池泄漏给下一 life 的普通弓箭手。
            if (!ModConfig.Enabled.Value)
            {
                PatchRoles_KnightStyle.RestoreRootedFollowerOnDisable(__instance);
                return;
            }
            PatchRoles_KnightStyle.ApplyFollowerSkinTo(__instance);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[KnightStyle/convert-soldier] " + e);
        }
    }
}

/// <summary>
/// 翻牌防御之二：原生 ConvertToHunter（白天随从例程 Archer.cs:482 等路径）把
/// 随从刷回猎人皮——白天翻牌路径。postfix 里若 _knight 仍非空且骑士有风格
/// （白天分散打猎但队籍仍在，队籍判定语义）→ 重涂风格士兵皮并重写生根字段；
/// 皮肤生根（士兵实例 soldierAnimator 系风格皮）后原生 ConvertToSoldier 也确定
/// 性写出风格皮，本 postfix 与 5s 巡检只作防御。
/// 真正离队时原生 RemoveFromKnight 先置 _knight=null 再调 ConvertToHunter
/// （Archer.cs:927-938），postfix 查无队籍直接返回并在同一路径还原生根字段，
/// 猎人皮正确保留，不碰。
/// </summary>
[HarmonyPatch(typeof(Archer), "ConvertToHunter")]
public static class Archer_ConvertToHunter_KnightStyleSkin_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Archer __instance)
    {
        if (__instance == null) return;
        try
        {
            if (!ModConfig.Enabled.Value)
            {
                PatchRoles_KnightStyle.RestoreRootedFollowerOnDisable(__instance);
                return;
            }
            PatchRoles_KnightStyle.ApplyFollowerSkinTo(__instance);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[KnightStyle/convert-hunter] " + e);
        }
    }
}

