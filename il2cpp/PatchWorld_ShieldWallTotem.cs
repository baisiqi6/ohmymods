using System;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 盾墙雕像移植希腊（shieldwall-totem-028）。
///
/// 需求：希腊世界激活北境的盾墙玩法——城墙旗帜旁出现可交互图腾（1 币），
/// 付费后拉一支持盾武士队在墙外结阵并冲锋清怪（原生 Active Shield Wall）。
/// 兵源 = 北境随从（PatchRoles_NorseSquad 转化的带盾弓箭手）。
///
/// 侦查事实引用（任务书实锤 + interop Assembly-CSharp.dll 元数据复核）：
/// - PayableBorder.cs:86-96：Setup(Side) 内部级联
///   GetComponentInChildren&lt;PayableShieldWallActivator&gt;()?.Setup(side)——原生北境
///   把 totem 挂在 PayableBorder（城墙旗帜）子物体上；希腊 border_greece 预制体
///   无 totem 子物体 → 挂 PayableBorder.Setup postfix 补挂（本文件 B 节）。
/// - Kingdom.cs:1953-1975：TrySpawnShieldWall（private）硬门
///   BiomeIndex != 3 return null（仅北境）；方法体 = Instantiate(active/passive
///   ShieldWallPrefab, position, identity, world.gameLayer) → SetSide →
///   StartRecruiting → shieldWalls[side] = formation。绕法 = prefix：希腊 biome
///   托管侧复刻方法体（去掉 biome 门）并 return false，其他 biome return true
///   放行原生（本文件 C 节）。这同时解锁三条原生路径：付费
///   （TrySpawnActiveShieldWall）、被动墙（CheckShouldSpawnShieldWall）、读档重建。
/// - Formation.cs:574：UpdateActiveShieldWall——集结到位 → DoInspire → RushStart
///   冲锋 → RushEnd 回墙解散，全程原生零接线。
/// - 网络注册常量：activator.Setup(side) 内部自动 RegisterObject(gameObject,
///   974/975, CRPCType.Dynamic)（原生常量）——直接调原生 Setup，不手工注册。
/// - 付费语义：付一次拉一队（非开关），墙存活期间 IsPayable 恒 false 不可再付，
///   墙自毁后恢复可付；价格/冷却/交互全部原生继承。
///
/// 与 NorseSquad 的联动：Archer 盾墙入队门 = _npcShieldUser != null &&
/// HasShield()（Archer.cs:748）——NorseSquad 转化的北境随从带 NpcShieldUser 且
/// 程序化装盾，合格兵源；Knight 无盾门也能入（原生行为，接受）；希腊 Worker
/// 未装盾不入。原生 Recruit 遍历 Knights+Archers+Workers 候选，我们的随从在
/// kingdom._archers 在册，天然可见（不加验证钩子，验收观察项走 diag 日志）。
///
/// 已知风险（任务书 §9，验收观察项）：
/// - shieldWalls[side] 判活依赖 Unity fake-null 在 Il2CppInterop 下的行为待实测
///   （若 fake-null 失效表现为墙自毁后图腾仍不可付——不处理，观察）。
/// - 图腾/编队用北境美术（无希腊变体，用户已知情）。
/// - 联机语义待实测（图腾双端注册 974/975 / Formation 同步）；单机优先：
///   prefix/postfix 无 HasWorldAuth 门（Setup/Pay 原生自带权威端逻辑）。
/// </summary>
public static class PatchWorld_ShieldWallTotem
{
    private const string TotemPrefabNamePart = "ActiveShieldWallTotem";
    private const string ActiveFormationNamePart = "ActiveShieldWallFormation";
    private const string PassiveFormationNamePart = "PassiveShieldWallFormation";
    private const float PrefabRetryIntervalSeconds = 30f; // LoadAll 兜底限频（穷举重，NorseSquad 先例）

    // ---- 惰性缓存（Resources 资产引用跨场景稳定，NorseSquad/FarmCats 先例）----
    private static PayableShieldWallActivator _totemPrefab;
    private static Formation _activeFormationPrefab;   // 仅 Kingdom 字段为 null 时的兜底
    private static Formation _passiveFormationPrefab;  // 仅 Kingdom 字段为 null 时的兜底

    // ---- 各自独立的 LoadAll 重试窗口（互不阻塞：图腾解析失败不得静默吞掉一次付费）----
    private static float _nextTotemRetryAt;
    private static float _nextActiveFormationRetryAt;
    private static float _nextPassiveFormationRetryAt;

    // ---- 一次性日志（static 去重）----
    private static bool _loggedTotemLeft;
    private static bool _loggedTotemRight;
    private static bool _loggedTotemUnresolved;
    private static bool _loggedActiveFallback;
    private static bool _loggedPassiveFallback;
    private static bool _loggedNoFormationPrefab;

    private static void LogInfo(string message)
    {
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[ShieldWallTotem] " + message);
    }

    // ============================================================
    // B. PayableBorder.Setup postfix：希腊补挂 totem（放置）
    // ============================================================

    /// <summary>
    /// 希腊 biome 且该旗帜尚无 activator（幂等）→ 解析 totem prefab →
    /// Instantiate 为 border 子物体（worldPositionStays=false 继承 prefab 局部
    /// 坐标，与原生北境 border 预制体层级结构一致）→ activator.Setup(side)
    /// （原生网络注册 974/975 + payable 状态机）。
    /// </summary>
    internal static void HandleBorderSetup(PayableBorder border, Side side)
    {
        // 仅希腊 biome（原生北境自带 totem，级联自处理；其他 biome 零影响）
        if (BiomeHolder.Inst == null
            || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return;
        if (border == null || border.gameObject == null) return;

        // 幂等关键：已有 activator（本旗帜重复 Setup / 原生自带的情形）直接早退
        if (border.GetComponentInChildren<PayableShieldWallActivator>() != null) return;

        PayableShieldWallActivator totem = ResolveTotemPrefab();
        if (totem == null || totem.gameObject == null)
        {
            if (!_loggedTotemUnresolved)
            {
                _loggedTotemUnresolved = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                    "[ShieldWallTotem] totem prefab not resolved yet; will retry every "
                    + PrefabRetryIntervalSeconds + "s on next banner setup");
            }
            return;
        }

        // 原生层级复现：totem 为 PayableBorder 根的子物体（原生
        // GetComponentInChildren 级联语义依赖此结构）。失败即销毁半成品
        // （fail-closed，不留无 Setup 的死图腾）。
        GameObject totemGO = UnityEngine.Object.Instantiate(
            totem.gameObject, border.transform, false);
        PayableShieldWallActivator activator = totemGO != null
            ? totemGO.GetComponent<PayableShieldWallActivator>() : null;
        if (activator == null)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[ShieldWallTotem] totem clone missing PayableShieldWallActivator; destroyed");
            try { if (totemGO != null) UnityEngine.Object.Destroy(totemGO); } catch { }
            return;
        }

        // 原生 Setup：内部 RegisterObject(974/975, Dynamic) + 主机侧
        // UpdatePaystate 协程。时序与原生一致（OnEnable 先于 Setup，见类注释）。
        activator.Setup(side);

        bool logged = side == Side.Left ? _loggedTotemLeft : _loggedTotemRight;
        if (!logged)
        {
            if (side == Side.Left) _loggedTotemLeft = true; else _loggedTotemRight = true;
            LogInfo("totem attached to " + side + " border banner");
        }
    }

    // ============================================================
    // C. Kingdom.TrySpawnShieldWall prefix：biome 门绕过
    // ============================================================

    /// <summary>
    /// 希腊 biome 托管侧复刻原生方法体（Kingdom.cs:1953-1975 逐字，仅去掉
    /// biome 门）：Instantiate(prefab, position, identity, world.gameLayer) →
    /// SetSide → StartRecruiting → shieldWalls[side]=formation，return false。
    /// 其他 biome return true 放行原生（零影响）。prefab 双兜底：Kingdom 字段
    /// （activeShieldWallPrefab/passiveShieldWallPrefab，希腊场景可能未序列化）
    /// → Resources.LoadAll 按名；双兜底都 null → LogError 一次 + return true
    /// （放行原生 = 原生因 biome 门 return null，付费静默失败，降级可接受）。
    /// </summary>
    internal static bool HandleTrySpawnShieldWall(Kingdom kingdom, Vector3 position,
        Side side, Formation.FormationType type, ref Formation result)
    {
        // 只在希腊 biome 改道；其他 biome（含原生北境）零影响放行原生
        if (BiomeHolder.Inst == null
            || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return true;
        if (kingdom == null) return true;

        // ---- 原生方法体逐字复刻（去掉首项 biome 门）----
        // 原生：if (BiomeIndex != 3 || shieldWalls[side] != null || type == Bomb)
        //           return null;
        if (kingdom.shieldWalls[side] != null || type == Formation.FormationType.Bomb)
        {
            result = null;
            return false;
        }

        bool active = type == Formation.FormationType.ActiveShieldWall;
        Formation prefab = active
            ? kingdom.activeShieldWallPrefab : kingdom.passiveShieldWallPrefab;

        // ---- prefab 双兜底（任务书实锤 4：不改 Kingdom 字段，仅本次 Instantiate 用）----
        if (prefab == null || prefab.gameObject == null)
        {
            prefab = active ? ResolveFormationPrefab(true) : ResolveFormationPrefab(false);
        }
        if (prefab == null || prefab.gameObject == null)
        {
            if (!_loggedNoFormationPrefab)
            {
                _loggedNoFormationPrefab = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                    "[ShieldWallTotem] shield wall prefab unresolvable (Kingdom field + "
                    + "Resources fallback both null); falling through to native (returns "
                    + "null in greece) — payment will silently fail");
            }
            return true;
        }

        Managers managers = Managers.Inst;
        World world = managers != null ? managers.world : null;
        if (world == null || world.gameLayer == null)
        {
            // 环境未就绪：放行原生（原生 biome 门 return null），下次付费再试
            return true;
        }

        Formation formation = UnityEngine.Object.Instantiate<Formation>(
            prefab, position, Quaternion.identity, world.gameLayer);
        formation.SetSide(side);
        formation.StartRecruiting();
        kingdom.shieldWalls[side] = formation;
        result = formation;
        return false;
    }

    // ============================================================
    // prefab 解析（Resources.LoadAll + 30s 限频，NorseSquad 先例）
    // ============================================================

    /// <summary>
    /// 解析北境 totem prefab（资产名含 "ActiveShieldWallTotem"，任务书实锤 2）。
    /// 缓存 static；未命中 30s 限频重试（LoadAll 穷举重，跨资产解析先例
    /// FarmCats/KnightStyle/NorseSquad）。
    /// </summary>
    private static PayableShieldWallActivator ResolveTotemPrefab()
    {
        if (_totemPrefab != null) return _totemPrefab;
        if (Time.time < _nextTotemRetryAt) return null;
        _nextTotemRetryAt = Time.time + PrefabRetryIntervalSeconds;
        try
        {
            var all = Resources.LoadAll<PayableShieldWallActivator>("");
            for (int i = 0; i < all.Length; i++)
            {
                PayableShieldWallActivator candidate = all[i];
                if (candidate == null || candidate.gameObject == null) continue;
                if (!candidate.gameObject.name.Contains(TotemPrefabNamePart)) continue;

                _totemPrefab = candidate;
                LogInfo("resolved totem prefab: " + candidate.gameObject.name);
                break;
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[ShieldWallTotem] totem prefab resolution failed: " + e);
        }
        return _totemPrefab;
    }

    /// <summary>
    /// 编队 prefab 兜底解析（任务书实锤 4）：希腊场景 Kingdom 的
    /// activeShieldWallPrefab/passiveShieldWallPrefab 可能为 null（场景序列化
    /// grep 不可验），null 时 Resources.LoadAll&lt;Formation&gt; 按资产名
    /// （"ActiveShieldWallFormation"/"PassiveShieldWallFormation"）赋给本地使用。
    /// 命中时一次性 LogWarning 提示走了兜底。
    /// </summary>
    private static Formation ResolveFormationPrefab(bool active)
    {
        if (active && _activeFormationPrefab != null) return _activeFormationPrefab;
        if (!active && _passiveFormationPrefab != null) return _passiveFormationPrefab;

        if (active && Time.time < _nextActiveFormationRetryAt) return null;
        if (!active && Time.time < _nextPassiveFormationRetryAt) return null;
        if (active) _nextActiveFormationRetryAt = Time.time + PrefabRetryIntervalSeconds;
        else _nextPassiveFormationRetryAt = Time.time + PrefabRetryIntervalSeconds;

        string namePart = active ? ActiveFormationNamePart : PassiveFormationNamePart;
        try
        {
            var all = Resources.LoadAll<Formation>("");
            for (int i = 0; i < all.Length; i++)
            {
                Formation candidate = all[i];
                if (candidate == null || candidate.gameObject == null) continue;
                if (!candidate.gameObject.name.Contains(namePart)) continue;

                if (active) _activeFormationPrefab = candidate;
                else _passiveFormationPrefab = candidate;

                bool logged = active ? _loggedActiveFallback : _loggedPassiveFallback;
                if (!logged)
                {
                    if (active) _loggedActiveFallback = true;
                    else _loggedPassiveFallback = true;
                    KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                        "[ShieldWallTotem] kingdom." + (active ? "active" : "passive")
                        + "ShieldWallPrefab was null in greece scene; resolved via "
                        + "Resources fallback: " + candidate.gameObject.name);
                }
                break;
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[ShieldWallTotem] formation prefab resolution failed: " + e);
        }
        return active ? _activeFormationPrefab : _passiveFormationPrefab;
    }
}

/// <summary>
/// 放置宿主：PayableBorder.Setup(Side) postfix。原生级联
/// GetComponentInChildren&lt;PayableShieldWallActivator&gt;()?.Setup(side) 对希腊
/// border_greece（无 totem 子物体）是 no-op——postfix 在此之后补挂。Setup 为
/// public（nameof 可用）。逻辑与门控在 PatchWorld_ShieldWallTotem.HandleBorderSetup。
/// </summary>
[HarmonyPatch(typeof(PayableBorder), nameof(PayableBorder.Setup))]
public static class PayableBorder_Setup_ShieldWallTotem_Patch
{
    [HarmonyPostfix]
    private static void Postfix(PayableBorder __instance, Side side)
    {
        if (!ModConfig.Enabled.Value || __instance == null) return;
        try
        {
            PatchWorld_ShieldWallTotem.HandleBorderSetup(__instance, side);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[ShieldWallTotem/border-setup] " + e);
        }
    }
}

/// <summary>
/// biome 门绕过宿主：Kingdom.TrySpawnShieldWall prefix（原生 private，
/// 字符串名补丁——PatchWorld_Level.GenerateInternal 同款先例）。原生硬门
/// BiomeIndex != 3 return null；prefix 仅在希腊 biome 托管侧复刻方法体并
/// return false，其他 biome return true 零影响。这同时解锁付费/被动墙/读档
/// 重建三条原生调用路径（都汇聚到本方法）。单机语义优先：无 HasWorldAuth 门
/// （原生 Setup/Pay 自带权威端逻辑）；联机待实测（类注释已知边界）。
/// </summary>
[HarmonyPatch(typeof(Kingdom), "TrySpawnShieldWall")]
public static class Kingdom_TrySpawnShieldWall_ShieldWallTotem_Patch
{
    [HarmonyPrefix]
    private static bool Prefix(Kingdom __instance, Vector3 position, Side side,
        Formation.FormationType type, ref Formation __result)
    {
        if (!ModConfig.Enabled.Value) return true;
        try
        {
            return PatchWorld_ShieldWallTotem.HandleTrySpawnShieldWall(
                __instance, position, side, type, ref __result);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[ShieldWallTotem/spawn] " + e);
            return true; // 异常放行原生（希腊下原生 biome 门 return null，降级不炸）
        }
    }
}
