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
/// 士兵控制器）覆盖为"骑士风格对应"的士兵控制器；希腊风格骑士 y 缩放 0.9。
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
/// - 随从联动：原生 ConvertToSoldier（入队/上船）会把随从控制器设回当前世界的
///   士兵皮肤，巡检 5s 内换回风格皮肤；只在当前控制器属于 archer_soldier 系时才写
///   （离队/死亡清理切到的猎人皮肤不碰，也避免每轮重复写）。
///
/// 2.4.0 签名验证（Operator 任务书实锤 + interop Assembly-CSharp.dll 复核）：
/// - Character.Promote(DroppableTool, IUnitController) : Character —— 存在（双验证）
/// - Knight._animator : Animator（私有，root 上）/ _mover : Mover / _archers :
///   Il2CppSystem.Collections.Generic.HashSet&lt;Archer&gt;（私有，interop 均已暴露）
/// - Knight.OnEnable() —— 私有，用字符串名打补丁（Worker/Crossbowman 的 nameof
///   先例不适用：Knight.OnEnable 非公开）
/// - Archer._animator : Animator —— interop 已暴露（免 GetComponentInChildren）
/// - Knight/士兵四套控制器（resources.assets 实测存在）：knight / knight_deadlands /
///   knight_bamboo / knight_greece；archer_soldier / archer_soldier_deadlands /
///   archer_soldier_bamboo / archer_soldier_greece。
///   另解析 archer_soldier_norselands 作第 5 个"只识别不选中"的士兵皮肤——北境世界
///   随从的原生士兵控制器（ConvertToSoldier 按 biome swap 得到），仅进
///   IsSoldierFamilyController 识别集，绝不进 AvailableStyles 风格池（Reviewer MF-1）
/// - Il2CppSystem HashSet 不实现托管 IEnumerable，无 foreach（泛型
///   IEnumerator&lt;T&gt; 在 interop 里缺 MoveNext，不可用）：用公开的非泛型
///   System_Collections_IEnumerable_GetEnumerator() 迭代，Current TryCast 回 Archer
/// - ScaleRegistryHolder.Register(Mover, float) / Unregister(Mover)
///   （PatchRoles_Worker，按 gameObject.GetInstanceID() 键控，Mover.Update postfix
///   每帧守卫 y）。坑11：只动 y，x 是朝向符号。
/// </summary>
public static class PatchRoles_KnightStyle
{
    // ---- 常量 ----
    private const int StyleCount = 4;
    private const int GreeceStyleIndex = 3;
    private const float GreeceKnightScaleY = 0.9f;
    private const float IntegrityIntervalSeconds = 5f;
    private const float AssetRetryIntervalSeconds = 30f;

    private const uint FnvOffset = 2166136261u;
    private const uint FnvPrime = 16777619u;
    private const uint DesignationSchema = 0x4B535431u; // "KST1"，与 FriendlyTroll 的 schema 区分

    // 风格表：index 0..3 = 中世纪/死亡之地/幕府（bamboo）/希腊
    private static readonly string[] StyleNames = { "medieval", "deadlands", "shogun", "greece" };
    private static readonly string[] KnightControllerNames =
        { "knight", "knight_deadlands", "knight_bamboo", "knight_greece" };
    private static readonly string[] SoldierControllerNames =
        { "archer_soldier", "archer_soldier_deadlands", "archer_soldier_bamboo", "archer_soldier_greece" };
    // 北境士兵皮肤（Reviewer MF-1）：只用于 IsSoldierFamilyController 识别——北境世界
    // 随从的原生士兵控制器是它，不识别则 StyleFollowers 每轮跳过、随从联动静默失效。
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
    // 第 5 个"只识别不选中"的士兵皮肤（MF-1）：解析失败仅降级（北境随从联动失效），
    // 不告警、不阻塞 _assetsComplete
    private static RuntimeAnimatorController _norselandsSoldierController;
    private static readonly List<int> AvailableStyles = new(); // 收缩后的可用风格池（hash % count 均匀重映射）
    private static bool _poolBuilt;
    private static bool _assetsComplete;      // 8/8 全解析：停止重试
    private static float _nextAssetRetryAt;
    private static bool _loggedPoolShrunk;
    private static bool _loggedPoolEmpty;

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
            return;
        }
        if (!_loggedPoolShrunk)
        {
            _loggedPoolShrunk = true;
            LogWarning("partial controller resolution (missing: " + missing
                + "); style pool shrunk to " + AvailableStyles.Count
                + "/" + StyleCount + ", hash remapped uniformly");
        }
    }

    private static bool IsStyleKnightController(RuntimeAnimatorController controller)
    {
        for (int i = 0; i < StyleCount; i++)
            if (KnightControllers[i] != null && controller.Pointer == KnightControllers[i].Pointer)
                return true;
        return false;
    }

    /// <summary>
    /// 士兵系判定：四套可选风格士兵 + 北境 archer_soldier_norselands（MF-1）。
    /// 北境世界随从的原生士兵皮肤就是北境款——不在判定集里的话 StyleFollowers
    /// 每轮 continue，随从联动静默失效。北境款只可识别、不可被选为风格。
    /// </summary>
    private static bool IsSoldierFamilyController(RuntimeAnimatorController controller)
    {
        for (int i = 0; i < StyleCount; i++)
            if (SoldierControllers[i] != null && controller.Pointer == SoldierControllers[i].Pointer)
                return true;
        return _norselandsSoldierController != null
            && controller.Pointer == _norselandsSoldierController.Pointer;
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
    /// 缩放（坑11：只动 y，x 是朝向符号）。希腊注册 ScaleRegistry 每帧守卫
    /// （池 respawn/原生重置能自愈），非希腊注销守卫并确保 y=1。
    /// </summary>
    private static void ApplyScale(Knight knight, int styleIndex)
    {
        try
        {
            bool greek = styleIndex == GreeceStyleIndex;
            float targetY = greek ? GreeceKnightScaleY : 1f;
            Vector3 scale = knight.transform.localScale;
            if (Mathf.Abs(scale.y - targetY) > 0.0001f)
            {
                scale.y = targetY;
                knight.transform.localScale = scale;
            }

            Mover mover = knight._mover;
            if (mover == null) mover = knight.GetComponent<Mover>();
            if (greek) ScaleRegistryHolder.Register(mover, GreeceKnightScaleY);
            else ScaleRegistryHolder.Unregister(mover);
        }
        catch (Exception e)
        {
            LogErrorOnce("knight scale apply failed", e);
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

        while (world != null && world.gameObject != null)
        {
            IntegrityPass();
            yield return new WaitForSeconds(IntegrityIntervalSeconds);
        }
    }

    /// <summary>
    /// 完整性巡检：
    /// 1) 读档/客户端同步恢复的存量骑士（无 Promote 机会）→ 直接按哈希上风格；
    /// 2) NeedsRederive（池复用重生）或身份未收敛（NetID 后到）→ 重算，幂等；
    /// 3) 已定风格骑士 → 重断言控制器/缩放（被原生重置则重设）；
    /// 4) 随从联动：把随从的士兵控制器覆盖成风格对应款。
    /// </summary>
    private static void IntegrityPass()
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            EnsureStyleAssets();
            if (!HasUsablePool()) return;

            Knight[] knights = UnityEngine.Object.FindObjectsOfType<Knight>();
            if (knights == null) return;

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
                    // Q-1（Reviewer）：重算分支也要跑随从联动——若某环境网络头永不可得，
                    // 本分支会每轮命中，随从联动不能跟着骑士风格一起饥饿（幂等零风险）
                    StyleFollowers(knight, state);
                    continue;
                }

                ReassertKnight(knight, state);
                StyleFollowers(knight, state);
            }
        }
        catch (Exception e)
        {
            LogErrorOnce("integrity pass failed", e);
        }
    }

    /// <summary>
    /// 随从联动：遍历 knight._archers，把随从的士兵控制器换成风格对应款。
    /// 两段式（实机 must-fix）：第一段纯枚举做快照，循环体内零写入——枚举器
    /// 循环内写 animator.runtimeAnimatorController 会触发原生侧对集合的修改
    /// （控制器重置引发的原生回调重入），HashSet 版本检查抛
    /// InvalidOperationException。原生同场景的标准范式也是先快照：
    /// Knight.cs:312 foreach (Archer archer in new List&lt;Archer&gt;(this._archers))。
    /// 第二段遍历托管快照做过滤+写入——已脱离原生枚举器，写入即使引发原生
    /// 集合修改也不再影响本轮。
    /// 写入条件：当前控制器属于 archer_soldier 系（四套可选 + 北境款，见
    /// IsSoldierFamilyController）且不等于目标——入队/上船的原生 ConvertToSoldier
    /// 会设回当前世界士兵皮肤（会被换回），而离队/死亡清理切到的猎人皮肤是
    /// 合法状态，不碰；指针相等时零写入。
    /// 弩手按设计不入骑士队（IsAvailableForJob 已排除），此处防御性跳过。
    /// </summary>
    private static void StyleFollowers(Knight knight, KnightStyleState state)
    {
        if (!state.HasStyle) return;
        RuntimeAnimatorController target = SoldierControllers[state.StyleIndex];
        if (target == null) return;

        // ---- 第一段：纯读快照（零写入）----
        var snapshot = new List<Archer>();
        try
        {
            var archers = knight._archers;
            if (archers == null) return;

            // Il2CppSystem HashSet 无托管 foreach：走公开的非泛型枚举器
            // （泛型 IEnumerator<T> 在 interop 里缺 MoveNext，不可用）；
            // Current 是 Il2CppSystem.Object，TryCast 回 Archer
            Il2CppSystem.Collections.IEnumerator enumerator =
                archers.System_Collections_IEnumerable_GetEnumerator();
            if (enumerator == null) return;
            while (enumerator.MoveNext())
            {
                Il2CppSystem.Object currentObject = enumerator.Current;
                Archer archer = currentObject != null ? currentObject.TryCast<Archer>() : null;
                if (archer == null || archer.gameObject == null
                    || !archer.gameObject.activeInHierarchy) continue;
                snapshot.Add(archer);
            }
        }
        catch (Exception e)
        {
            // 快照段纯读，理论上不应抛；抛了本轮放弃，等下一轮（5s）重试，
            // 不影响本方法之外的任何状态
            LogErrorOnce("follower styling failed", e);
            return;
        }

        // ---- 第二段：遍历托管快照做过滤 + 控制器写入 ----
        for (int i = 0; i < snapshot.Count; i++)
        {
            try
            {
                Archer archer = snapshot[i];
                if (archer == null) continue;
                if (PatchRoles_Crossbowman.IsCrossbowman(archer)) continue;

                Animator animator = archer._animator;
                if (animator == null) animator = archer.GetComponentInChildren<Animator>();
                if (animator == null) continue;

                RuntimeAnimatorController current = animator.runtimeAnimatorController;
                if (current == null || current.Pointer == target.Pointer) continue;
                if (!IsSoldierFamilyController(current)) continue;
                animator.runtimeAnimatorController = target;
            }
            catch (Exception e)
            {
                // 单个随从失败（快照与写入之间被销毁等）不拖累其余随从
                LogErrorOnce("follower styling failed", e);
            }
        }
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
