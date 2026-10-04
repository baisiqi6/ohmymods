using System;
using System.Collections.Generic;
using HarmonyLib;
#if ANDROID
// Android（Il2CppInterop namespace-prefix 模式）把 Assembly-CSharp 的全局类型放在 Il2Cpp.* 下；
// 本文件与 PC 共用同一份逻辑，仅在此把文件用到的游戏类型显式映射到实际 interop 类型。
// Managers/World/NetworkBigBoss 由 android/GlobalAliases.cs 的 global alias 提供（本地再 alias 会
// CS1537）；UnityEngine 类型两个平台同名，无需映射。其余代码（含 PC 分支）逐字相同。
using BiomeHolder = Il2Cpp.BiomeHolder;
using Deer = Il2Cpp.Deer;
using Game = Il2Cpp.Game;
using Hind = Il2Cpp.Hind;
using PopulationController = Il2Cpp.PopulationController;
using Steed = Il2Cpp.Steed;
#endif
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Greek ordinary-deer population inputs, borrowed only for one native Update invocation.
/// Native timing, seasonal selection, region limits, spawning, pooling and loot stay native-owned.
/// 平台门：PC 沿用 ModConfig.Enabled；Android 另加默认关闭的 ModConfig.DeerPopulationEnabled
/// （ANDROID 编译的顶部快速通道与 Eligible 前置判断；PC 预处理输出不变）。
/// </summary>
internal static class PatchWorld_DeerPopulation
{
    private const float Multiplier = 3f;
    private static readonly Dictionary<IntPtr, ulong> Active = new();
    // Only failed cleanup fields are retained here; normal Update invocations use the stack's struct state.
    private static readonly Dictionary<IntPtr, Lease> Pending = new();
    private static ulong _nextToken;
    private static bool _loggedApplied, _loggedFailure;

    internal struct Lease
    {
        internal PopulationController Controller;
        internal GameObject Object;
        internal IntPtr ControllerPtr, ObjectPtr;
        internal int ControllerId, ObjectId;
        internal ulong Token;
        internal byte Written;
        internal float Density, WinterDefault, WinterSpecial, Interval;
        internal float AppliedDensity, AppliedWinterDefault, AppliedWinterSpecial, AppliedInterval;
#if ANDROID
        internal ScopeEvidence Scope;
#endif
    }

#if ANDROID
    /// <summary>
    /// 一次应用日志的 ANDROID 证据：只记录同一次 Eligible 实际读取/判定的值
    /// （scope id 用 lease.ObjectId；此处不含任何跨调用状态）。分类真值只在实际
    /// GetComponent/IsChildOf/scene 读取后记录，日志阶段零再读。
    /// </summary>
    internal struct ScopeEvidence
    {
        internal int SceneHandle;
        internal bool ChildOfLayer, PrefabDeer, PrefabSteed, PrefabHind;
    }

    /// <summary>
    /// Android 平台门：会话总开关 + 默认关闭的 DeerPopulationEnabled。恒为 managed 值，
    /// 关闭时后续代码在任何 controller/native 读取之前返回。
    /// </summary>
    private static bool DeerPopulationRequested()
    {
        var master = ModConfig.Enabled;
        var deer = ModConfig.DeerPopulationEnabled;
        return master != null && master.Value && deer != null && deer.Value;
    }
#endif

    internal static void Begin(PopulationController controller, out Lease lease)
    {
        lease = default;
        try
        {
#if ANDROID
            // Android 平台关断快速通道：总开关/鹿开关关闭且无自有状态时，在读取
            // controller.Pointer 之前零 interop 返回。有 owned 状态仍走下面的
            // pointer/token/identity 清理路径，只归还自己已借的 bits。
            if (!DeerPopulationRequested() && Active.Count == 0 && Pending.Count == 0) return;
#endif
            if (controller == null) return;
            IntPtr pointer = controller.Pointer;
            if (pointer == IntPtr.Zero || Active.ContainsKey(pointer)) return;
            // Reentrant native Update must never restore the outer invocation's borrowed inputs.
            // Cleanup of a previous failed invocation precedes config/authority/world eligibility.
            if (Pending.TryGetValue(pointer, out Lease pending))
            {
                if (!SameIdentity(pending, controller)) Pending.Remove(pointer);
                else
                {
                    Restore(pending);
                    if (Pending.ContainsKey(pointer)) return; // Do not treat an unrecovered 3x field as a new baseline.
                }
            }
#if ANDROID
            if (!Eligible(controller, out GameObject go, out GameObject prefab, out ScopeEvidence evidence)) return;
#else
            if (!Eligible(controller, out GameObject go, out GameObject prefab)) return;
#endif

            // Read and validate every input before acquiring ownership or writing any field.
            float density = controller.density, winterDefault = controller.winterDensityDefault;
            float winterSpecial = controller.winterDensitySpecial, interval = controller._actualUpdateInterval;
            float appliedDensity = density * Multiplier, appliedDefault = winterDefault * Multiplier;
            float appliedSpecial = winterSpecial * Multiplier, appliedInterval = interval / Multiplier;
            if (!ValidDensity(density, appliedDensity) || !ValidDensity(winterDefault, appliedDefault)
                || !ValidDensity(winterSpecial, appliedSpecial) || !float.IsFinite(interval) || interval <= 0f
                || !float.IsFinite(appliedInterval) || appliedInterval <= 0f) return;

            ulong token = ++_nextToken;
            if (token == 0) token = ++_nextToken;
            lease = new Lease
            {
                Controller = controller, Object = go, ControllerPtr = pointer, ObjectPtr = go.Pointer,
                ControllerId = controller.GetInstanceID(), ObjectId = go.GetInstanceID(), Token = token,
                Density = density, WinterDefault = winterDefault, WinterSpecial = winterSpecial, Interval = interval,
                AppliedDensity = appliedDensity, AppliedWinterDefault = appliedDefault,
                AppliedWinterSpecial = appliedSpecial, AppliedInterval = appliedInterval
#if ANDROID
                , Scope = evidence
#endif
            };
            Active.Add(pointer, token);
            // Mark before each interop setter so a partial setter failure can still relinquish its own value.
            lease.Written |= 1; controller.density = appliedDensity;
            lease.Written |= 2; controller.winterDensityDefault = appliedDefault;
            lease.Written |= 4; controller.winterDensitySpecial = appliedSpecial;
            lease.Written |= 8; controller._actualUpdateInterval = appliedInterval;
            LogApplied(prefab, lease);
        }
        catch (Exception ex)
        {
            Restore(lease);
            LogFailure(ex);
        }
    }

    private static bool Eligible(PopulationController controller, out GameObject go, out GameObject prefab
#if ANDROID
        , out ScopeEvidence evidence
#endif
        )
    {
        go = null; prefab = null;
#if ANDROID
        evidence = default;
        // Android 平台 config 门在 native eligibility 之前：关闭时零 native 读取。
        if (!DeerPopulationRequested()) return false;
#endif
        if (ModConfig.Enabled == null || !ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth
            || controller == null || !controller.enabled || controller.useBiomeCritters) return false;
        BiomeHolder biome = BiomeHolder.Inst;
        if (biome == null || biome.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return false;
        Managers managers = Managers.Inst;
        World world = managers != null ? managers.world : null;
        Game game = managers != null ? managers.game : null;
        Transform layer = world != null ? world.gameLayer : null;
        if (world == null || game == null || layer == null || !game.playingOrInMenuWithClient) return false;
        go = controller.gameObject;
#if ANDROID
        // 与 PC 相同的短路顺序，逐项在读取后立即判定并记录；失败的排除对象不会多读后续 native 组件。
        if (go == null) return false;
        if (!go.activeInHierarchy) return false;
        evidence.SceneHandle = go.scene.handle;
        if (evidence.SceneHandle != layer.gameObject.scene.handle) return false;
        evidence.ChildOfLayer = go.transform.IsChildOf(layer);
        if (!evidence.ChildOfLayer) return false;
#else
        // Native World.FindOrCreateForest parents the ordinary-deer controller under the current gameLayer.
        if (go == null || !go.activeInHierarchy || go.scene.handle != layer.gameObject.scene.handle
            || !go.transform.IsChildOf(layer)) return false;
#endif
        prefab = controller.prefab;
#if ANDROID
        // 分类真值只在实际 GetComponent 之后记录；成功路径六证据全部来自同一次分类的实际读取。
        if (prefab == null) return false;
        evidence.PrefabDeer = prefab.GetComponent<Deer>() != null;
        if (!evidence.PrefabDeer) return false;
        evidence.PrefabSteed = prefab.GetComponent<Steed>() != null;
        if (evidence.PrefabSteed) return false;
        evidence.PrefabHind = prefab.GetComponent<Hind>() != null;
        return !evidence.PrefabHind;
#else
        return prefab != null && prefab.GetComponent<Deer>() != null
            && prefab.GetComponent<Steed>() == null && prefab.GetComponent<Hind>() == null;
#endif
    }

    private static bool ValidDensity(float original, float applied)
        => float.IsFinite(original) && original >= 0f && float.IsFinite(applied);

    internal static void Restore(Lease lease)
    {
        if (lease.Token == 0) return;
        bool hasActive = Active.TryGetValue(lease.ControllerPtr, out ulong active);
        if (hasActive && active != lease.Token) return; // A stale finalizer cannot touch a newer invocation.
        bool hasPending = Pending.TryGetValue(lease.ControllerPtr, out Lease pending);
        if (!hasActive && (!hasPending || pending.Token != lease.Token)) return;
        if (!hasActive) lease = pending; // Retry only bits still owned after a previous partial cleanup.
        byte unresolved = lease.Written;
        try
        {
            // Restoration belongs to this invocation, regardless of changed config, authority or world.
            // Destroyed/reused native objects are never written. A merely disabled live object still needs cleanup.
            PopulationController controller = lease.Controller;
            if (!SameIdentity(lease, controller)) { unresolved = 0; return; }

            // Separate field boundaries ensure one failing native accessor cannot skip cleanup of the others.
            for (byte bit = 1; bit <= 8; bit <<= 1)
            {
                if ((lease.Written & bit) == 0) continue;
                try
                {
                    switch (bit)
                    {
                        case 1:
                            if (controller.density == lease.AppliedDensity) controller.density = lease.Density;
                            break;
                        case 2:
                            if (controller.winterDensityDefault == lease.AppliedWinterDefault) controller.winterDensityDefault = lease.WinterDefault;
                            break;
                        case 4:
                            if (controller.winterDensitySpecial == lease.AppliedWinterSpecial) controller.winterDensitySpecial = lease.WinterSpecial;
                            break;
                        case 8:
                            if (controller._actualUpdateInterval == lease.AppliedInterval) controller._actualUpdateInterval = lease.Interval;
                            break;
                    }
                    unresolved = (byte)(unresolved & ~bit); // Successfully restored or externally replaced.
                }
                catch (Exception ex) { LogFailure(ex); }
            }
        }
        catch (Exception ex) { LogFailure(ex); }
        finally
        {
            if (Active.TryGetValue(lease.ControllerPtr, out active) && active == lease.Token) Active.Remove(lease.ControllerPtr);
            // A different token owns any newer state; never erase or replace it from an old finalizer.
            if (!Active.ContainsKey(lease.ControllerPtr)
                && (!Pending.TryGetValue(lease.ControllerPtr, out pending) || pending.Token == lease.Token))
            {
                if (unresolved == 0) Pending.Remove(lease.ControllerPtr);
                else { lease.Written = unresolved; Pending[lease.ControllerPtr] = lease; }
            }
        }
    }

    private static bool SameIdentity(Lease lease, PopulationController controller)
    {
        GameObject go = lease.Object;
        return controller != null && go != null && controller.Pointer == lease.ControllerPtr
            && go.Pointer == lease.ObjectPtr && controller.GetInstanceID() == lease.ControllerId
            && go.GetInstanceID() == lease.ObjectId && controller.gameObject != null
            && controller.gameObject.Pointer == lease.ObjectPtr;
    }

    private static void LogApplied(GameObject prefab, Lease lease)
    {
        if (_loggedApplied) return;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[DeerPopulation] prefab=" + prefab.name
                + " density=" + lease.Density + "->" + lease.AppliedDensity
                + " winterDefault=" + lease.WinterDefault + "->" + lease.AppliedWinterDefault
                + " winterSpecial=" + lease.WinterSpecial + "->" + lease.AppliedWinterSpecial
                + " interval=" + lease.Interval + "->" + lease.AppliedInterval
#if ANDROID
                // 全部字段来自同次 Eligible 已计算值/现有 lease；日志阶段零再读 native。
                + " scopeGoId=" + lease.ObjectId + " sceneHandle=" + lease.Scope.SceneHandle
                + " childOf=" + lease.Scope.ChildOfLayer
                + " prefabDeer=" + lease.Scope.PrefabDeer
                + " prefabSteed=" + lease.Scope.PrefabSteed
                + " prefabHind=" + lease.Scope.PrefabHind
#endif
                );
            _loggedApplied = true;
        }
        catch (Exception ex) { LogFailure(ex); }
    }

    private static void LogFailure(Exception ex)
    {
        if (_loggedFailure) return;
        _loggedFailure = true;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[DeerPopulation] temporary input adjustment failed: " + ex.GetType().Name); }
        catch { }
    }
}

[HarmonyPatch(typeof(PopulationController), nameof(PopulationController.Update))]
internal static class PopulationController_Update_DeerPopulation_Patch
{
    [HarmonyPrefix]
    internal static void Prefix(PopulationController __instance, out PatchWorld_DeerPopulation.Lease __state)
        => PatchWorld_DeerPopulation.Begin(__instance, out __state);

    [HarmonyPostfix]
    internal static void Postfix(PatchWorld_DeerPopulation.Lease __state) => PatchWorld_DeerPopulation.Restore(__state);

    [HarmonyFinalizer]
    internal static Exception Finalizer(Exception __exception, PatchWorld_DeerPopulation.Lease __state)
    {
        PatchWorld_DeerPopulation.Restore(__state);
        return __exception;
    }
}
