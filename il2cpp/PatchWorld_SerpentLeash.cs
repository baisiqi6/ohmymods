using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 大蛇离墙缰绳：希腊最终岛城墙推近奥林匹斯山门后，大蛇（WorldEatingSerpent）
/// 的休息位=山门 SerpentAnchor，会落在城墙外很近处——白天墙边小兵直接暴露在
/// 它的警戒扫描（_warnDistance=6）与咬击（ShouldAttack→DynamicTargetChomp）里。
///
/// 修法：锚点向右推到 城墙+MinDistanceFromWall（只向右、幂等）。原生所有
/// 锚点相关逻辑整体随迁，无分支被破坏：
/// - 关卡加载瞬移与 Moving 状态回巢都指向锚点本体（OnLevelLoadedHandler/
///   OnMovingStateRoutine 的 SetGoal）；
/// - 冲锋线 GetMinChargePositionX = max(锚点-0.55, 墙+minCharge+warn)：锚点右移
///   后由锚点项主导，大蛇在墙+14 休息、扫描只够到墙+8，墙边小兵白天不再被骚扰；
///   玩家主动把部队推到墙+8 以外时冲锋照常（boss 战机制保留）；
/// - IsBlockingGate 是"蛇与锚点的相对距离<=8"，蛇贴新锚点=照常挡门；
/// - 弱点锚点按世界跨度均分（CalculateWeakPointAnchors 用 worldBounds），
///   与蛇锚点无关，战斗布局不动。
/// 城墙会持续右扩，用低频协程复扫；OnEnable postfix 保证大蛇一激活就位。
/// </summary>
public static class PatchWorld_SerpentLeash
{
    private const float MinDistanceFromWall = 14f;   // 原生冲锋线=墙+10；墙+14 让警戒(6)够不到墙边
    private const float RescanIntervalSeconds = 10f; // 城墙右扩后复推（只向右，幂等）
    private static IntPtr _supervisorWorld;
    private static bool _loggedLeash;

    internal static void LeashAnchorToBorder(WorldEatingSerpent serpent)
    {
        try
        {
            if (serpent == null || serpent.gameObject == null) return;

            // _mtOlympusGate 是懒加载字段（原生 TryFindGate 私有，不进 interop——
            // 坑25/HasKnight 先例），OnEnable 时机字段还是 null，这里复刻其填充：
            // FindWithTag + GetComponent 写回字段，保证"激活即上缰绳"先于
            // OnLevelLoadedHandler 的关卡加载瞬移执行（reviewer F2）。
            if (serpent._mtOlympusGate == null)
            {
                GameObject gateGo = GameObject.FindWithTag("MtOlympusGate");
                serpent._mtOlympusGate = gateGo != null
                    ? gateGo.GetComponent<MtOlympusGates>()
                    : null;
            }
            MtOlympusGates gate = serpent._mtOlympusGate;
            if (gate == null || gate.SerpentAnchor == null) return;
            Kingdom kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
            if (kingdom == null) return;

            Transform anchor = gate.SerpentAnchor;
            float wallX = kingdom.GetBorderSideIntact(Side.Right);
            float targetX = wallX + MinDistanceFromWall;
            // 上界钳制（reviewer F1）：墙+14 可能越过可玩陆域右界，会破坏 Submerged
            // 跟随点不变量（Min 反转压过 Max）与头部弱点可达性。worldBounds.right
            // 本身已内缩 8（World.cs 世界边界公式），再留原生 WORLD_BORDER_BUFFER=10。
            float worldRight = float.MaxValue;
            World world = Managers.Inst != null ? Managers.Inst.world : null;
            if (world != null) worldRight = world.worldBounds.right - 10f;
            if (targetX > worldRight) targetX = worldRight;
            if (anchor.position.x >= targetX - 0.1f) return;

            Vector3 pos = anchor.position;
            pos.x = targetX;
            anchor.position = pos;
            if (!_loggedLeash)
            {
                _loggedLeash = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[SerpentLeash] anchor pushed to wall+" + MinDistanceFromWall
                    + " (wall=" + wallX.ToString("F1")
                    + " anchor=" + targetX.ToString("F1")
                    + " worldRight=" + worldRight.ToString("F1") + ")");
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[SerpentLeash/leash] " + e);
        }
    }

    /// <summary>
    /// 低频复扫宿主（范式同 PatchWorld_DefenseSpacing）：城墙右扩/读档后保持
    /// 锚点离墙距离。大蛇只在最终岛存在，FindObjectsOfType 每轮最多命中一个。
    /// 墙被毁后 borderIntact 回退内侧 → targetX 下降 → no-op，锚点停在原远处
    /// 不回拉（有意为之：更远只会更安全）。新世界重置首条日志标记。
    /// </summary>
    internal static IEnumerator SupervisorRoutine(World world)
    {
        if (world == null || _supervisorWorld == world.Pointer) yield break;
        _supervisorWorld = world.Pointer;
        _loggedLeash = false;
        while (world != null && world.gameObject != null)
        {
            yield return new WaitForSeconds(RescanIntervalSeconds);
            WorldEatingSerpent[] serpents = UnityEngine.Object.FindObjectsOfType<WorldEatingSerpent>();
            if (serpents == null) continue;
            for (int i = 0; i < serpents.Length; i++)
            {
                if (serpents[i] != null) LeashAnchorToBorder(serpents[i]);
            }
        }
    }
}

/// <summary>
/// 大蛇激活即上缰绳（关卡加载瞬移 OnLevelLoadedHandler 在同帧稍后执行，
/// 会直接落到已右移的锚点上）。
/// </summary>
[HarmonyPatch(typeof(WorldEatingSerpent), nameof(WorldEatingSerpent.OnEnable))]
public static class WorldEatingSerpent_OnEnable_SerpentLeash_Patch
{
    [HarmonyPostfix]
    private static void Postfix(WorldEatingSerpent __instance)
    {
        if (!ModConfig.Enabled.Value || __instance == null) return;
        try
        {
            PatchWorld_SerpentLeash.LeashAnchorToBorder(__instance);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[SerpentLeash/on-enable] " + e);
        }
    }
}

[HarmonyPatch(typeof(World), nameof(World.OnLevelLoaded))]
public static class World_OnLevelLoaded_SerpentLeashHost_Patch
{
    [HarmonyPostfix]
    private static void Postfix(World __instance)
    {
        if (!ModConfig.Enabled.Value || __instance == null) return;
        try
        {
            __instance.StartCoroutine(
                PatchWorld_SerpentLeash.SupervisorRoutine(__instance).WrapToIl2Cpp());
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[SerpentLeash] supervisor start failed: " + e);
        }
    }
}
