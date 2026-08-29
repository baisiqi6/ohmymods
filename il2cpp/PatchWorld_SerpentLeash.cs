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
///   后由锚点项主导，大蛇在墙+60 休息、警戒+咬击只覆盖墙+46 外，墙边小兵
///   白天不再被骚扰；玩家主动把部队推到墙+46 以外时冲锋照常（boss 战机制保留）；
/// - IsBlockingGate 是"蛇与锚点的相对距离<=8"，蛇贴新锚点=照常挡门；
/// - 弱点锚点按世界跨度均分（CalculateWeakPointAnchors 用 worldBounds），
///   与蛇锚点无关，战斗布局不动。
/// 城墙会持续右扩，用低频协程复扫；OnEnable postfix 保证大蛇一激活就位。
/// </summary>
public static class PatchWorld_SerpentLeash
{
    private const float MinDistanceFromWall = 100f;  // 用户两次要求再远：60→100（世界右界336余量足，蛇警戒圈只覆盖墙+86外）
    private const float RescanIntervalSeconds = 10f; // 城墙右扩后复推（只向右，幂等）
    private static IntPtr _supervisorWorld;
    private static bool _loggedLeash;
    private static bool _loggedClampUnavailable;

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

            float wallX = kingdom.GetBorderSideIntact(Side.Right);
            float targetX = wallX + MinDistanceFromWall;
            // 上界钳制（reviewer F1）：墙+60 偏移量大、比早期墙+14 方案更容易
            // 越过可玩陆域右界，钳制必要性更高——越界会破坏 Submerged 跟随点
            // 不变量（Min 反转压过 Max）与头部弱点可达性。
            // 注意不能用 world.worldBounds.right —— Sided<float> 泛型结构体经
            // interop marshal 出来是垃圾值（实测 4.7e19），改用原生公式复刻：
            // worldBounds.right = ground.x + collider.size.x/2 - 8（World.cs OnLevelLoaded）。
            float worldRight = float.MaxValue;
            try
            {
                BoxCollider2D groundCol = World.GroundCollider;
                if (groundCol != null && groundCol.transform != null)
                {
                    worldRight = groundCol.transform.position.x + groundCol.size.x / 2f - 8f;
                    if (targetX > worldRight) targetX = worldRight;
                }
            }
            catch (Exception)
            {
                // 钳制不可用就不钳：只向右推的语义仍然安全于当前锚点；
                // 但完全静默会掩盖 ground collider 解析长期失效——一次性告警
                // （static bool 去重，范式同 _loggedLeash）。
                if (!_loggedClampUnavailable)
                {
                    _loggedClampUnavailable = true;
                    KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                        "[SerpentLeash] worldRight clamp unavailable; pushing without upper bound this pass");
                }
            }

            Transform anchor = gate.SerpentAnchor;
            if (anchor.position.x < targetX - 0.1f)
            {
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
                        + " worldRight=" + (worldRight == float.MaxValue ? -1f : worldRight).ToString("F1") + ")");
                }
            }

            LeashBodyToAnchor(serpent, targetX);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[SerpentLeash/leash] " + e);
        }
    }

    /// <summary>
    /// 蛇本体归位：读档蛇保留存档位置（fromSave 不瞬移），原生回巢是 Moving 状态
    /// 慢速爬行（实测窗口 10s+），期间蛇还趴在墙边。休息态（Idle=1/Moving=2，
    /// State 是私有嵌套类拿不到常量引用，按源码数值比较）且仍在目标位左侧时
    /// 直接 RepX 拉回——与原生 UpdatePosition 的 transform.x 写法等价。
    /// 充电/攻击/下潜/眩晕等状态绝不碰（冲锋贴墙是 boss 战机制本身）。
    /// </summary>
    private static bool _loggedBodySnap;

    private static void LeashBodyToAnchor(WorldEatingSerpent serpent, float targetX)
    {
        try
        {
            StateMachine fsm = serpent._fsm;
            int state = fsm != null ? fsm.Current : -1;
            if (state != 1 && state != 2) return; // 仅 Idle / Moving

            // 行为判据双保险（与状态编号无关）：状态硬编码 {1,2} 按 2.1.0 源码
            // 正确且失败开放（2.4.0 状态数值漂移只会导致"不拉"，不会误拉），
            // 但若漂移后的真实攻击态恰好落在 {1,2}，纯数值判断会错触发——
            // 警戒圈扫描器（_warnDistance）内有目标=可能正在攻击/冲锋，绝不拉。
            // 原生用法：WorldEatingSerpent.cs:349 ShouldPrepareCharge 的
            // this._longRangeScanner.IsAny()（私有字段，interop 暴露，同 _fsm 先例）。
            if (serpent._longRangeScanner != null && serpent._longRangeScanner.IsAny()) return;
            float currentX = serpent.transform.position.x;
            if (currentX >= targetX - 0.5f) return;

            Vector3 pos = serpent.transform.position;
            pos.x = targetX;
            serpent.transform.position = pos;
            if (!_loggedBodySnap)
            {
                _loggedBodySnap = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[SerpentLeash] body snapped from x=" + currentX.ToString("F1")
                    + " to " + targetX.ToString("F1") + " (state=" + state + ")");
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[SerpentLeash/body] " + e);
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
        _loggedBodySnap = false;
        bool loggedDiag = false;

        while (world != null && world.gameObject != null)
        {
            WorldEatingSerpent[] serpents = UnityEngine.Object.FindObjectsOfType<WorldEatingSerpent>();
            if (serpents != null)
            {
                for (int i = 0; i < serpents.Length; i++)
                {
                    if (serpents[i] == null) continue;
                    LeashAnchorToBorder(serpents[i]);
                    // 一次性诊断：蛇实际停位 + 2.4.0 真实状态值（State 常量按 2.1.0 源码
                    // 硬编码比较，此行用于验证 2.4.0 数值是否一致，防止本体归位静默失效）
                    if (!loggedDiag && serpents[i]._fsm != null)
                    {
                        loggedDiag = true;
                        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                            "[SerpentLeash] diag serpent x=" + serpents[i].transform.position.x.ToString("F1")
                            + " state=" + serpents[i]._fsm.Current);
                    }
                }
            }
            yield return new WaitForSeconds(RescanIntervalSeconds);
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
