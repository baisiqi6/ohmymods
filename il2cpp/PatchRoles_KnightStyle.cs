using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 骑士随机风格（knight-style-026）：招募骑士（Armor 转职）时，每个骑士按确定性哈希
/// 随机定为 中世纪/死亡之地/幕府/希腊 四种形象之一（纯外观，不动战斗数值）；
/// 其随从士兵（跟随骑士的 Archer，原生 ConvertToSoldier 已把它们换成当前世界的
/// 士兵控制器）覆盖为"骑士风格对应"的士兵控制器。缩放（坑11：只动 y）：
/// 骑士按风格查表（中世纪 0.95/死地 1.0/幕府 1.0/希腊 0.9，Strip 恒回 1）；
/// 中世纪风格的随从士兵 y=1.05（其余 1.0，无骑士/骑士无风格时回 1）。
///
/// 机制要点：
/// - 转职入口：Character.Promote(DroppableTool, IUnitController) postfix，
///   tool.tag == "Armor" → Professions{"Armor","Knight"}。__result 是新骑士
///   （ReplaceBy 内部已做过 biome swap，原生控制器=当前世界骑士皮肤）。
///   Squire（Shield 转职的侍从）按任务书明确不处理——tag != "Knight" 全部早退。
/// - 确定性风格：照抄 PatchDivine_FriendlyTroll.TryComputeDesignation 的 FNV 哈希
///   mix(campaign/challenge/land/reign/islandTicks + NetID)。读档/联机双端各自算出
///   同一风格（外观级一致，无需网络同步）。NetID 不可用时退化 GetInstanceID()
///   （联机读档后可能换风格，任务书接受），并保持 NetIdResolved=false 让巡检在
///   网络头出现后重算收敛双端。
/// - 读档恢复：读档不重跑转职，World 巡检（OnLevelLoaded postfix 起协程，5s 一轮）
///   首轮扫描全部 Knight：状态表无记录的直接按哈希上风格（客户端无 Promote 机会，
///   同样靠这条路收敛到与服务端一致的风格）。
/// - 池复用清污：对象池 respawn 不重拷序列化字段，带风格皮肤的旧骑士实例被复用时，
///   Promote postfix 先 StripKnight（恢复缓存的原生控制器 + 注销缩放守卫 + y=1）
///   再重摇；Knight.OnEnable postfix 标记 NeedsRederive 兜住"复用不经 Promote"
///   的路径（读档重生），巡检重算。
/// - 随从联动（反向归属 + 队籍判定 + 翻牌治理）：不枚举 knight._archers——
///   Il2Cpp 非泛型枚举器对 HashSet 运行时不可靠（knightstyle2 实测：纯读快照段
///   的 MoveNext 也抛 InvalidOperationException），改为全场
///   FindObjectsOfType&lt;Archer&gt; 读 _knight 反查骑士状态。写入条件是队籍而非
///   皮肤族（follower diag 实测：原生随从只在 actively 跟队时 ConvertToSoldier，
///   白天分散打猎穿猎人皮，"∈士兵族才写"白天永远不命中）：_knight 指向已风格化
///   骑士且当前控制器 != 目标即写（统一路径 ApplyFollowerSkinTo）。翻牌治理
///   （幕府之谜实锤：原生 ConvertToSoldier/Hunter 每次把控制器刷回 BiomeData
///   世界原生皮，5s 写 vs ~10s 刷回）：两个转换的 postfix 在刷回的同一调用栈内
///   即时重涂风格皮，5s 巡检只兜底。代价与收益：白天分散的随从也穿风格士兵皮
///   （随时认出归属）；真正离队时原生先置 _knight=null 再 ConvertToHunter，
///   猎人皮正确保留，无需清理。
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
/// - Knight/士兵四套控制器（resources.assets 实测存在）：knight / knight_deadlands /
///   knight_bamboo / knight_greece；archer_soldier / archer_soldier_deadlands /
///   archer_soldier_bamboo / archer_soldier_greece。
///   另解析 archer_soldier_norselands（北境世界随从的原生士兵控制器，
///   ConvertToSoldier 按 biome swap 得到）——队籍判定后写入条件不再消费
///   识别集，但解析保留（资产存在性实锤、供回归诊断与未来判定），绝不进
///   AvailableStyles 风格池，也不计入完整性/收缩判定（Reviewer MF-1 沿革）
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
    private const int StyleCount = 4;
    private const int MedievalStyleIndex = 0; // 随从缩放特判用（中世纪随从 1.05）
    private const int DeadlandsStyleIndex = 1; // 死地随从"无标记弩手化"包特判用
    private const float IntegrityIntervalSeconds = 5f;
    private const float AssetRetryIntervalSeconds = 30f;

    // 每风格骑士 y 缩放（坑11：只动 y），index 对齐 StyleNames：
    // 中世纪 0.95 / 死地 1.05 / 幕府 1.0 / 希腊 0.9（原"希腊特例"泛化为表驱动；
    // 死地 1.05 由 Operator 2026-08 实测定稿）
    private static readonly float[] KnightStyleScaleY = { 0.95f, 1.05f, 1f, 0.9f };
    // 中世纪风格的随从士兵 y 缩放（其余风格 1.0；用户可从身高认出中世纪队）
    private const float FollowerMedievalScaleY = 1.05f;

    private const uint FnvOffset = 2166136261u;
    private const uint FnvPrime = 16777619u;
    private const uint DesignationSchema = 0x4B535431u; // "KST1"，与 FriendlyTroll 的 schema 区分

    // 风格表：index 0..3 = 中世纪/死亡之地/幕府（bamboo）/希腊
    private static readonly string[] StyleNames = { "medieval", "deadlands", "shogun", "greece" };
    private static readonly string[] KnightControllerNames =
        { "knight", "knight_deadlands", "knight_bamboo", "knight_greece" };
    private static readonly string[] SoldierControllerNames =
        { "archer_soldier", "archer_soldier_deadlands", "archer_soldier_bamboo", "archer_soldier_greece" };
    // 北境士兵皮肤（Reviewer MF-1 沿革）：北境世界随从的原生士兵控制器。
    // 队籍判定（follower diag 实测修订）后写入条件不再按皮肤族判定，本解析
    // 无行为消费点，仅保留资产解析（存在性已实锤，供回归诊断与未来判定）。
    // 绝不加入 AvailableStyles（可选风格仍四种），也不计入完整性/收缩判定。
    private const string NorselandsSoldierControllerName = "archer_soldier_norselands";

    // ---- 每骑士状态（instanceID 键控，范式同 FriendlyTroll TrollState）----
    private sealed class KnightStyleState
    {
        internal Knight Knight;
        internal int StyleIndex = -1;
        internal bool HasStyle;
        internal RuntimeAnimatorController NativeKnightController; // 首次覆盖前缓存，Strip 恢复用
        internal bool NativeControllerCached;
        internal bool NetIdResolved; // 哈希身份是否已用上 NetID；false 时巡检持续尝试收敛
        internal bool NeedsRederive; // 池对象新生命周期（OnEnable 标记）：旧风格记录待重算
        internal bool Logged;
    }

    private static readonly Dictionary<int, KnightStyleState> States = new();

    // ---- 惰性静态资产（解析一次；未解析全时按间隔重试）----
    private static readonly RuntimeAnimatorController[] KnightControllers = new RuntimeAnimatorController[StyleCount];
    private static readonly RuntimeAnimatorController[] SoldierControllers = new RuntimeAnimatorController[StyleCount];
    // 北境士兵皮肤（MF-1 沿革）：解析保留（队籍判定后无行为消费点，见常量区
    // 注释）；解析失败仅静默降级，不告警、不阻塞 _assetsComplete
    private static RuntimeAnimatorController _norselandsSoldierController;
    private static readonly List<int> AvailableStyles = new(); // 收缩后的可用风格池（hash % count 均匀重映射）
    private static bool _poolBuilt;
    private static bool _assetsComplete;      // 8/8 全解析：停止重试
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
    /// 解析四套骑士 + 四套士兵控制器。先查已加载资产（FindObjectsOfTypeAll），
    /// 仍缺走一次 Resources.LoadAll("") 兜底（强制全量加载，Crossbowman 同款）。
    /// 未解析全时按 AssetRetryIntervalSeconds 重试（LoadAll 是穷举，正常首试即全中；
    /// 重试只兜"资产随世界内容渐进加载"的边角）。解析失败只影响对应风格。
    /// </summary>
    private static void EnsureStyleAssets()
    {
        if (_assetsComplete) return;
        if (Time.time < _nextAssetRetryAt && _poolBuilt) return;
        _nextAssetRetryAt = Time.time + AssetRetryIntervalSeconds;
        try
        {
            ResolveFromSet(Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>());
            // 兜底触发条件含北境皮肤（MF-1）：四套全中但北境未加载时也要 LoadAll，
            // 否则 _assetsComplete 置位后再不重试，北境随从识别永久缺失
            if (HasMissingControllers() || _norselandsSoldierController == null)
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

        // 北境士兵皮肤（MF-1）：只识别不选中，同两遍解析里捎带查找；失败静默降级
        if (_norselandsSoldierController == null)
        {
            for (int j = 0; j < set.Length; j++)
            {
                RuntimeAnimatorController candidate = set[j];
                if (candidate != null && candidate.name == NorselandsSoldierControllerName)
                {
                    _norselandsSoldierController = candidate;
                    break;
                }
            }
        }
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
            LogInfo("resolution: knight[" + knightText + "] soldier[" + soldierText
                + "] norse[" + NorselandsSoldierControllerName + "="
                + (_norselandsSoldierController != null ? _norselandsSoldierController.name : "<null>")
                + "]");
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
    /// NetID（CRPCHeader，池同步槽位身份）不可用时退化 GetInstanceID()——
    /// 不跨存档稳定，联机/读档后可能换风格（任务书接受）；usedNetId=false
    /// 让上层保持可收敛状态，等网络头出现后重算。
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
                // 动态 NetID 视为稳定同步槽位身份（FriendlyTroll 同款取舍）
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

            EnsureStyleAssets();
            if (!HasUsablePool()) return;

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
    /// 给骑士上风格（幂等）：算哈希 → 风格 index；首次覆盖前缓存原生控制器；
    /// 设风格控制器；希腊 → 缩放守卫 0.9，非希腊确保 y=1 且注销守卫。
    /// </summary>
    private static void ApplyKnightStyle(Knight knight)
    {
        if (knight == null || knight.gameObject == null) return;
        if (!HasUsablePool()) return;
        if (knight.tag != "Knight") return;

        int id = knight.gameObject.GetInstanceID();
        if (!TryComputeIdentity(knight, out uint hash, out bool usedNetId)) return;

        int slot = (int)(hash % (uint)AvailableStyles.Count);
        int styleIndex = AvailableStyles[slot];

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
        if (usedNetId) state.NetIdResolved = true;
        state.NeedsRederive = false;

        if (!state.Logged)
        {
            state.Logged = true;
            LogInfo("knight styled as " + StyleNames[styleIndex]
                + (usedNetId ? string.Empty : " (identity fallback: instance id)"));
        }
        else if (styleChanged)
        {
            // 身份收敛（网络头后到）或池复用重摇导致的换风格
            LogInfo("knight restyled as " + StyleNames[styleIndex]);
        }
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
    /// （中世纪 0.95/死地 1.0/幕府 1.0/希腊 0.9）。y≠1 注册 ScaleRegistry 每帧
    /// 守卫（池 respawn/原生重置能自愈），y=1 注销守卫。Apply/Reassert 共用；
    /// Strip 不走此表，恒回 1（恢复原生身材）。
    /// </summary>
    private static void ApplyScale(Knight knight, int styleIndex)
    {
        try
        {
            float targetY = KnightStyleScaleY[styleIndex];
            Vector3 scale = knight.transform.localScale;
            if (Mathf.Abs(scale.y - targetY) > 0.0001f)
            {
                scale.y = targetY;
                knight.transform.localScale = scale;
            }

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
    /// 风格/随从无骑士的清理路径传 1）回 1.0。y≠1 注册守卫，y=1 注销。
    /// 每轮幂等重算：随从换队（骑士死了改投他人）时缩放自动跟随新骑士风格。
    /// </summary>
    private static void EnsureFollowerScale(Archer archer, float targetY)
    {
        try
        {
            Vector3 scale = archer.transform.localScale;
            if (Mathf.Abs(scale.y - targetY) > 0.0001f)
            {
                scale.y = targetY;
                archer.transform.localScale = scale;
            }

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
    /// 恢复缓存的原生控制器；注销缩放守卫并回 y=1；移出状态表。
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
                Vector3 scale = knight.transform.localScale;
                if (Mathf.Abs(scale.y - 1f) > 0.0001f)
                {
                    scale.y = 1f;
                    knight.transform.localScale = scale;
                }
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
    /// 旧风格记录不可信，待巡检按哈希重算（身份源可能已变）。
    /// 注意 OnEnable 先于 Promote postfix（Pool.Spawn 激活在前），
    /// 标记会被随后的 Strip+Apply 覆盖，顺序天然正确。
    /// </summary>
    internal static void OnKnightActivated(Knight knight)
    {
        try
        {
            if (knight == null || knight.gameObject == null) return;
            int id = knight.gameObject.GetInstanceID();
            if (States.TryGetValue(id, out KnightStyleState state))
            {
                state.Knight = knight;
                state.NeedsRederive = true;
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
            }
            catch { }
        }
        States.Clear();

        // 随从诊断基线复位：新世界的计数从零开始，首轮即可输出一次
        _followerDiagHasBaseline = false;
        _nextFollowerDiagAt = 0f;

        while (world != null && world.gameObject != null)
        {
            IntegrityPass();
            yield return new WaitForSeconds(IntegrityIntervalSeconds);
        }
    }

    /// <summary>
    /// 完整性巡检（两段结构）：
    /// 第一段——处理所有 Knight：
    /// 1) 读档/客户端同步恢复的存量骑士（无 Promote 机会）→ 直接按哈希上风格；
    /// 2) NeedsRederive（池复用重生）或身份未收敛（NetID 后到）→ 重算，幂等；
    /// 3) 已定风格骑士 → 重断言控制器/缩放（被原生重置则重设）。
    /// 第二段——随从联动（反向归属）：全场扫 Archer 读 _knight 查状态，
    /// 把随从的士兵控制器覆盖成风格对应款。放在骑士段之后：本轮新上风格/
    /// 重算收敛的骑士立即可被反查命中，Reviewer Q-1（重算分支的骑士随从
    /// 不饥饿）由本段天然满足。
    /// </summary>
    private static void IntegrityPass()
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            EnsureStyleAssets();
            if (!HasUsablePool()) return;

            // ---- 第一段：骑士 ----
            Knight[] knights = UnityEngine.Object.FindObjectsOfType<Knight>();
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
                        // 读档恢复：读档不重跑转职，按确定性哈希直接上风格（双端一致）
                        ApplyKnightStyle(knight);
                        continue;
                    }

                    if (state.NeedsRederive || !state.NetIdResolved)
                    {
                        // 重算收敛：池复用新生命周期，或首次哈希用的退化身份（网络头后到）。
                        // 哈希不变时 ApplyKnightStyle 内部零写入，天然幂等。
                        ApplyKnightStyle(knight);
                        continue;
                    }

                    ReassertKnight(knight, state);
                }
            }

            // ---- 第二段：随从联动（反向归属，全场一次扫描）----
            StyleFollowersByLookup();
        }
        catch (Exception e)
        {
            LogErrorOnce("integrity pass failed", e);
        }
    }

    /// <summary>
    /// 单随从换皮（统一写入路径）：读 archer._knight，队籍骑士在状态表且有风格
    /// → 把 SoldierControllers[styleIndex] 写到 archer._animator。
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
    /// 翻牌机制（治本背景，幕府之谜诊断实锤）：夜间 diag curTop=
    /// archer_soldier_greece×56 + archer_soldier×20（期望 med20=archer_soldier✓、
    /// dead28=deadlands✗、shog8=bamboo✗、gree20=greece✓）——56=dead+shog+gree
    /// 全停在原生希腊士兵皮。我们每 5s 写一次，而原生 ConvertToSoldier（跟队例程
    /// 重入时调用，Archer.cs:859/485）每次都把控制器刷回 BiomeData 换皮的世界
    /// 原生皮（~10s 一轮），5s 写 vs ~10s 刷回的翻牌让视觉上绝大多数时间停在
    /// 原生皮。中世纪幸存是因为基底 archer_soldier 恰好不在"被刷回"路径的
    /// 目标集合里。治本：ConvertToSoldier/ConvertToHunter 的 postfix 即时重涂
    /// （见文件尾两个 patch 类），本方法就是它们的重涂实现；5s 巡检仅兜底。
    /// </summary>
    internal static bool ApplyFollowerSkinTo(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return false;

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
                // ConvertToHunter postfix 走到这里，战斗数值随猎人身份还原
                PatchRoles_Crossbowman.RestoreSquadCrossbowPackage(archer);
                return false;
            }
            if (!States.TryGetValue(knight.gameObject.GetInstanceID(), out KnightStyleState state)
                || !state.HasStyle)
            {
                // 骑士未上风格：同样撤包（换队过渡/新骑士未定型期间不持弩手数值）
                PatchRoles_Crossbowman.RestoreSquadCrossbowPackage(archer);
                return false;
            }

            // 死地随从弩手化包：Apply 幂等（SO 指针判重）；非死地随从撤包（换队到
            // 非死地骑士时战斗数值跟随还原）
            if (state.StyleIndex == DeadlandsStyleIndex)
                PatchRoles_Crossbowman.ApplySquadCrossbowPackage(archer);
            else
                PatchRoles_Crossbowman.RestoreSquadCrossbowPackage(archer);

            RuntimeAnimatorController target = SoldierControllers[state.StyleIndex];
            if (target == null) return false;

            Animator animator = archer._animator;
            if (animator == null) animator = archer.GetComponentInChildren<Animator>();
            if (animator == null) return false;

            RuntimeAnimatorController current = animator.runtimeAnimatorController;
            if (current == null) return false;
            if (current.Pointer == target.Pointer) return false;

            animator.runtimeAnimatorController = target;
            return true;
        }
        catch (Exception e)
        {
            LogErrorOnce("follower skin apply failed", e);
            return false;
        }
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
    private static void StyleFollowersByLookup()
    {
        try
        {
            Archer[] archers = UnityEngine.Object.FindObjectsOfType<Archer>();
            if (archers == null) return;

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
                        // （幂等 no-op，离队主路径在 ConvertToHunter postfix）
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
                        // 弩手化包同撤（幂等 no-op）
                        PatchRoles_Crossbowman.RestoreSquadCrossbowPackage(archer);
                        EnsureFollowerScale(archer, 1f);
                        continue;
                    }
                    if (!state.HasStyle)
                    {
                        PatchRoles_Crossbowman.RestoreSquadCrossbowPackage(archer);
                        EnsureFollowerScale(archer, 1f);
                        continue;
                    }
                    diagInStates++;
                    if (state.StyleIndex >= 0 && state.StyleIndex < StyleCount)
                        diagStyleTargets[state.StyleIndex]++;

                    // 随从缩放（[2]）：中世纪 1.05，其余 1.0；随从换队（骑士死了
                    // 改投他人）时每轮幂等重算，缩放自动跟随新骑士风格。
                    // 死地随从例外：缩放（1.15）由 ApplySquadCrossbowPackage 作为
                    // 弩手化包的一部分统一管理，此处跳过避免两个写入者互相覆盖
                    if (state.StyleIndex != DeadlandsStyleIndex)
                    {
                        EnsureFollowerScale(archer,
                            state.StyleIndex == MedievalStyleIndex ? FollowerMedievalScaleY : 1f);
                    }

                    // 死地随从弩手化包：与皮肤写入独立调用（均幂等）——皮肤已是
                    // 目标（skippedFamily）或资产缺失（skippedOther）的分支不会走
                    // ApplyFollowerSkinTo，包仍需按风格上/撤；死地世界里死地随从的
                    // 原生皮恰好就是目标皮，皮写入路径不可靠，包必须独立保证
                    if (state.StyleIndex == DeadlandsStyleIndex)
                        PatchRoles_Crossbowman.ApplySquadCrossbowPackage(archer);
                    else
                        PatchRoles_Crossbowman.RestoreSquadCrossbowPackage(archer);

                    RuntimeAnimatorController target = SoldierControllers[state.StyleIndex];
                    if (target == null) { diagSkippedOther++; continue; }

                    Animator animator = diagAnimator; // 复用上面的查询结果
                    if (animator == null) { diagSkippedOther++; continue; }

                    RuntimeAnimatorController current = diagController;
                    if (current == null) { diagSkippedOther++; continue; }
                    if (current.Pointer == target.Pointer)
                    {
                        // 已是目标风格：幂等零写入（计入 skippedFamily，见上方语义注释）
                        diagSkippedFamily++;
                        continue;
                    }
                    // 队籍判定：不再检查当前皮肤族——猎人皮/世界士兵皮/北境款一律覆盖。
                    // 写入统一走 ApplyFollowerSkinTo（与 ConvertToSoldier/Hunter 的
                    // postfix 即时重涂同一条路径；内部重复做幂等检查，无害）。
                    // 治本在 postfix：原生刷回原生皮的瞬间就被重涂，本 5s 巡检只兜
                    // postfix 覆盖不到的窗口（postfix 挂钩前已刷回的存量等）
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
                + " shog:" + diagStyleTargets[2] + " gree:" + diagStyleTargets[3],
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
/// 后按确定性哈希上风格。同签名多 postfix 先例：PatchRoles_Worker / Crossbowman。
/// </summary>
[HarmonyPatch(typeof(Character), nameof(Character.Promote),
    new[] { typeof(DroppableTool), typeof(IUnitController) })]
public static class Character_Promote_KnightStyle_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Character __result, DroppableTool tool)
    {
        if (!ModConfig.Enabled.Value) return;
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
        if (!ModConfig.Enabled.Value || __instance == null) return;
        try
        {
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
/// 翻牌治本之一（私有方法按名打补丁，先例：Knight.OnEnable 字符串名补丁）：
/// 原生 ConvertToSoldier（跟队例程重入/上塔/上船时调用，Archer.cs:859）把随从
/// 控制器刷回 BiomeData 换皮的世界原生士兵皮——这是 5s 写 vs ~10s 刷回翻牌的
/// 刷回源（幕府之谜诊断实锤，详见 ApplyFollowerSkinTo 注释）。postfix 在刷回
/// 的同一调用栈内立即重涂风格士兵皮，随从视觉上恒为风格款。
/// 弩手/无队籍随从 _knight 为 null，ApplyFollowerSkinTo 直接返回，不碰。
/// </summary>
[HarmonyPatch(typeof(Archer), "ConvertToSoldier")]
public static class Archer_ConvertToSoldier_KnightStyleSkin_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Archer __instance)
    {
        if (!ModConfig.Enabled.Value || __instance == null) return;
        try
        {
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
/// 翻牌治本之二：原生 ConvertToHunter（白天随从例程 Archer.cs:482 等路径）把
/// 随从刷回猎人皮——白天翻牌路径。postfix 里若 _knight 仍非空且骑士有风格
/// （白天分散打猎但队籍仍在，队籍判定语义）→ 重涂风格士兵皮。
/// 真正离队时原生 RemoveFromKnight 先置 _knight=null 再调 ConvertToHunter
/// （Archer.cs:927-938），postfix 查无队籍直接返回，猎人皮正确保留，不碰。
/// </summary>
[HarmonyPatch(typeof(Archer), "ConvertToHunter")]
public static class Archer_ConvertToHunter_KnightStyleSkin_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Archer __instance)
    {
        if (!ModConfig.Enabled.Value || __instance == null) return;
        try
        {
            PatchRoles_KnightStyle.ApplyFollowerSkinTo(__instance);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[KnightStyle/convert-hunter] " + e);
        }
    }
}

