using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 坐骑技能 CD 倍率 patch（2026-08-24 需求：CTRL+F10 面板滑块，0.2~1.0，
/// 最多缩到原生 1/5；倍率统一乘在各坐骑 prefab 序列化的原生 _cooldown 上）。
///
/// 挂钩设计（对任务书"OnEnable postfix"方案的修正，有实证依据）：
/// - 侦查实证：2.4.0 interop 里 SteedAbility 本类【没有】OnEnable（只有
///   Awake/OnDisable/OnDestroy，反射 2.4.0 interop DLL 的 DeclaredOnly 方法表核实）。
///   对不存在的目标打 Harmony patch 会在 PatchAll 抛异常、拖垮整个 mod 的补丁注册
///   （Dog/Banker OnEnable 先例是"那些类自己声明了 OnEnable"，不能平移到 SteedAbility）。
/// - 改用读取点前缀（本仓库 HermesStaff CanActivate/TriggerItemAbility 前缀同款模式）：
///   原版在 SteedAbility.Activate 里消费 _cooldown 排程
///   `_nextActivationTime = Time.time + _cooldown`（2.1.0 源码 SteedAbility.cs:90）。
///   逐一核实 17 个派生类：12 个消费 CD 的 Activate() 重写全部调用 base.Activate()
///   （BuffUnits/ChargeAttack/ChargeFire/ChargeFly/DropCoins/EmpowerGround/JumpAttack/
///   PushAttack/Spit/SummonGhost/Wheelie/WolfAttack），因此 patch 基类
///   SteedAbility.Activate 的 prefix 能覆盖全部真正受 CD 限制的坐骑；
///   其余 5 个（DayNightSpeed/DayNightRecolor/RunningAttack 空实现，GlideMovement/
///   Kelpie 自排 _nextActivationTime 或不排程）根本不读 _cooldown，无需覆盖。
///   prefix 在 base.Activate() 执行前重写 _cooldown → 当次激活即按新倍率排程。
///
/// 原生值缓存（防重复相乘）：
/// - instanceID → (原生值, 上次写入值)。首次见到时字段仍是 prefab 序列化的原生值，记录之；
///   此后每次都写 原生值×当前倍率（幂等，倍率改多少次都不会叠乘）。
/// - 坐骑是池化对象（AGENTS 规则 11），池复用是同实例同 ID，缓存长期有效；
///   极小概率的 instanceID 复用（旧坐骑销毁后新坐骑拿到同 ID）由一致性校验自愈：
///   字段值 ≠ 缓存记录的"上次写入值" → 判定为另一个对象，重录原生值。
///
/// 生效时机：_nextActivationTime 在 Activate 时按当次 _cooldown 排程，
/// 已排程的这一次不追溯（不提前也不推迟），改倍率从下一次激活生效；
/// SettingChanged 回调只把在场实例的字段值刷成一致（并留日志），不做时间轴修正。
///
/// 边界：SummonGhostSteedAbility（地狱三头犬）的 CD 由 PatchDivine_GhostSquads 的
/// 固定 profile（30↔22.5）单独管理，跳过，避免两套规则叠乘。
/// </summary>
[HarmonyPatch(typeof(SteedAbility), nameof(SteedAbility.Activate))]
public static class PatchRide_SteedCooldown
{
    private const string LogPrefix = "[SteedCooldown]";

    // interop 属性透传 IL2CPP float，比较用小量 epsilon（值域 ~1-30s，1e-4 足够）
    private const float FieldEpsilon = 1e-4f;

    private struct CooldownRecord
    {
        public float Native;      // prefab 序列化的原生 _cooldown
        public float LastApplied; // 上次写入实例字段的值（ID 复用自愈校验用）
    }

    // instanceID → 冷却记录。SettingChanged 理论上可能从 BepInEx 文件监视线程触发，
    // 与主线程 Activate 前缀并发 → 加锁保护（激活是秒级低频事件，锁开销可忽略）。
    private static readonly Dictionary<int, CooldownRecord> _nativeCooldowns = new();

    [HarmonyPrefix]
    public static void SteedAbility_Activate_Prefix(SteedAbility __instance)
    {
        try
        {
            if (__instance == null) return;
            ApplyProfile(__instance);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError($"{LogPrefix} Activate prefix failed: {e}");
        }
    }

    /// <summary>
    /// 倍率改动回调（ModConfig.Init 接线）：遍历在场 SteedAbility，用缓存原生值重写字段，
    /// 运行中的坐骑下一次激活即按新倍率（已排程的本次不追溯）。低频事件，
    /// FindObjectsOfType 全量扫可接受。若从非主线程触发，Unity API 抛异常由 try/catch
    /// 兜住 —— 主线程（面板滑块）路径不受影响，且读取点前缀每次激活都重算，
    /// 正确性不依赖本回调。
    /// </summary>
    internal static void OnMultiplierChanged(object sender, EventArgs e)
    {
        try
        {
            var abilities = UnityEngine.Object.FindObjectsOfType<SteedAbility>();
            foreach (var ability in abilities)
            {
                ApplyProfile(ability);
            }
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                $"{LogPrefix} multiplier={ModConfig.SteedCooldownMultiplier.Value:0.##}, reapplied to {abilities.Length} live ability(ies)");
        }
        catch (Exception ex)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError($"{LogPrefix} multiplier change reapply failed: {ex}");
        }
    }

    private static void ApplyProfile(SteedAbility ability)
    {
        if (ability == null) return;

        // 三头犬坐骑由 GhostSquads 固定 CD profile 管理（30↔22.5），不参与通用倍率。
        // TryCast 是 Il2CppInterop 的精确类型判定（GhostSquads 里 IGhostHolder 同款用法），
        // 比托管 is 更可靠：__instance 代理未必是最派生托管类型。
        if (ability.TryCast<SummonGhostSteedAbility>() != null) return;

        // 总开关关闭 → 倍率取 1.0，等价还原各坐骑原生 CD。
        float multiplier = ModConfig.Enabled.Value ? ModConfig.SteedCooldownMultiplier.Value : 1f;

        lock (_nativeCooldowns)
        {
            int id = ability.GetInstanceID();
            if (!_nativeCooldowns.TryGetValue(id, out CooldownRecord record)
                || Mathf.Abs(ability._cooldown - record.LastApplied) > FieldEpsilon)
            {
                // 首次见到（字段还是原生值），或 ID 复用自愈（字段 ≠ 上次写入值 → 新对象）。
                record.Native = ability._cooldown;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    $"{LogPrefix} first apply on {ability.GetType().Name}: native {record.Native:0.##}s × {multiplier:0.##} → {record.Native * multiplier:0.##}s");
            }

            float target = record.Native * multiplier;
            ability._cooldown = target;
            record.LastApplied = target;
            _nativeCooldowns[id] = record;
        }
    }
}
