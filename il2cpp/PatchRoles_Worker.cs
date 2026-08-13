using System;
using UnityEngine;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace KingdomEnhancedMod;

/// <summary>
/// 单位缩放注册表——"y 轴守护"核心。游戏用 transform.localScale.x 的符号（±1）做朝向翻转，
/// Mover.Update 每帧把整个 localScale 覆盖为 (±1,1,1)，一次性缩放下一帧被清零。
/// 本组在各单位 OnEnable（出生/池复用）登记目标 y 缩放，Mover.Update postfix 每帧恢复。
///
/// IL2CPP 差异：Mono 用 ConditionalWeakTable&lt;Mover,ScaleValue&gt; 弱引用自动清理；
/// IL2CPP 下 Il2CppObjectBase 包装身份不稳定，改用托管 Dictionary&lt;int,float&gt;
/// 以 GameObject.GetInstanceID() 为稳定键，挂在 ClassInjector 注册的
/// ScaleRegistryHolder MonoBehaviour（见 docs §5.3 三步，原生 ClassInjector，无 SharedLib）。
///
/// 2.4.0 签名验证（interop Assembly-CSharp.dll）：
/// - Worker.OnEnable() : void —— 存在（Worker : Actor&lt;Workable&gt;，公开）
/// - Mover.Update() : void —— 存在（公开）
/// - WarriorPeasant.OnEnable()/Deer.OnEnable()/Critter.OnEnable()/Peasant.OnEnable() —— 存在
/// - NpcShieldUser.HasShield() : bool / SetShieldEnabled(bool, int = 0) : void —— 存在
/// - NpcShieldUser.regenWait : WaitForSeconds（公开字段）—— 【差异】原私有反射，2.4.0 公开
/// - Worker.npcShieldUser : NpcShieldUser（公开字段）—— 【差异】原私有反射，2.4.0 公开
/// </summary>

/// <summary>
/// 缩放注册表持有者（自定义 MonoBehaviour）。持有托管 Dictionary，本身挂在
/// DontDestroyOnLoad GameObject 上；真正的每帧恢复由 Mover.Update postfix 完成
/// （保证在每个 Mover 写回 localScale 之后立即恢复，避免 Update 顺序问题）。
/// </summary>
public class ScaleRegistryHolder : MonoBehaviour
{
    public static ScaleRegistryHolder Instance { get; private set; }

    // key = Mover 所在 GameObject 的 instanceID（稳定 int），value = 目标 y 缩放
    private static readonly System.Collections.Generic.Dictionary<int, float> _targets = new();

    public ScaleRegistryHolder(IntPtr ptr) : base(ptr) { }

    public static void EnsureCreated()
    {
        if (Instance != null) return;

        // 三步之一/二：原生 ClassInjector 注册（无 SharedLib），再实例化
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(ScaleRegistryHolder)))
            ClassInjector.RegisterTypeInIl2Cpp(typeof(ScaleRegistryHolder));

        var go = new GameObject("KingdomEnhancedMod_ScaleRegistry");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        Instance = go.AddComponent<ScaleRegistryHolder>();
    }

    public static void Register(Mover mover, float y)
    {
        if (mover == null || mover.gameObject == null) return;
        EnsureCreated();
        _targets[mover.gameObject.GetInstanceID()] = y;
    }

    public static bool TryGet(Mover mover, out float y)
    {
        if (mover != null && mover.gameObject != null && _targets.TryGetValue(mover.gameObject.GetInstanceID(), out y))
            return true;
        y = 1f;
        return false;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        _targets.Clear();
    }
}

/// <summary>
/// 2.4.0 修复：NpcShieldUser.Awake 在希腊 Worker 裸加组件时抛 NRE
/// （GetComponent&lt;Damageable&gt;() 为 null → damageable.OnPreReceiveDamage 订阅 NRE
/// → Unity 回滚 AddComponent → EnsurePickupCapability 每次 OnEnable 死循环刷屏）。
/// 分流：无 Damageable（希腊裸加路径）→ 安全版 Awake（不订阅，仅 character+regenWait）；
/// 有 Damageable（北境 prefab 正常路径）→ 原版 Awake。
/// </summary>
[HarmonyPatch(typeof(NpcShieldUser))]
public static class NpcShieldUser_Awake_Patch
{
    [HarmonyPatch(nameof(NpcShieldUser.Awake))]
    [HarmonyPrefix]
    public static bool Awake_Prefix(NpcShieldUser __instance)
    {
        if (!ModConfig.Enabled.Value) return true;
        try
        {
            // 全接管安全版 Awake（2.4.0 实机：裸加组件场景原版 Awake 必抛，导致
            // AddComponent 回滚 → 死循环）。2.4.0 的 OnPreReceiveDamage 是事件，
            // 订阅签名不兼容，且希腊 worker 无盾牌——跳过订阅。
            __instance.character = __instance.GetComponent<Character>();
            __instance.damageable = __instance.GetComponent<Damageable>();
            __instance.regenWait = new WaitForSeconds(1f);
            return false;  // 跳过原 Awake
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
            return false;  // 原 Awake 已知会抛，安全版失败也不再走原版
        }
    }
}


[HarmonyPatch(typeof(Worker))]
public static class Worker_OnEnable_Patch
{
    [HarmonyPatch(nameof(Worker.OnEnable))]
    [HarmonyPostfix]
    public static void OnEnable_Postfix(Worker __instance)
    {
        if (!ModConfig.Enabled.Value) return;

        try
        {
            ApplyWorkerScale(__instance);
            ScaleRegistryHolder.Register(__instance.GetComponent<Mover>(),
                IsNorselandsWorker(__instance) ? 1.2f : 1.075f);
            EquipShieldIfNorselands(__instance);
            EnsurePickupCapability(__instance);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }



    private static void ApplyWorkerScale(Worker worker)
    {
        if (worker == null) return;
        float s = IsNorselandsWorker(worker) ? 1.175f : 1.075f;
        Vector3 v = worker.transform.localScale;
        v.y = s;
        worker.transform.localScale = v;
    }

    /// <summary>
    /// 北境工匠出生带盾。2.1.0 的 regenWait 未初始化 NRE 问题在 2.4.0 下用公开字段
    /// 直接回填（原反射 SetValue）。
    /// </summary>
    private static void EquipShieldIfNorselands(Worker worker)
    {
        if (worker == null) return;
        try
        {
            // 希腊世界无盾牌商店（12/13 槽位被狂战士商店占用），worker 组件为裸加
            // （缺序列化字段），SetShieldEnabled 内部 NRE——希腊世界直接跳过装备。
            if (BiomeHolder.Inst != null && BiomeHolder.Inst.BiomeIndex == BiomeHolder.GreeceBiomeIndex) return;

            NpcShieldUser shieldUser = worker.GetComponent<NpcShieldUser>();
            if (shieldUser == null || shieldUser.HasShield()) return;

            // 真·北境 prefab 工人有 Damageable；裸加组件的无（判别用，避免 SetShieldEnabled NRE）
            if (worker.GetComponent<Damageable>() == null) return;
            if (shieldUser.shield == null) return;

            if (shieldUser.regenWait == null)
                shieldUser.regenWait = new WaitForSeconds(1f);

            shieldUser.SetShieldEnabled(true, 0);
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Roles] Norselands worker equipped with shield");
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    private static bool IsNorselandsWorker(Worker worker)
    {
        // 名字判别：希腊 worker 被 EnsurePickupCapability 补组件后 GetComponent 会误判，
        // 导致 EquipShieldIfNorselands 给无 Damageable 的希腊 worker 装备盾牌 → SetShieldEnabled NRE。
        return worker != null
            && worker.GetComponent<NpcShieldUser>() != null
            && worker.gameObject.name.Contains("norselands");
    }

    /// <summary>
    /// 希腊原版 Worker prefab 无 NpcShieldUser → 无法拾取 BerserkerTool。OnEnable 补组件。
    /// 2.4.0：裸 AddComponent 触发 Awake 抛 NRE（由 NpcShieldUser_Awake_Patch 分流接管后
    /// 不再抛），组件正常挂上；GetComponent 幂等检查防重复添加。
    /// </summary>
    private static void EnsurePickupCapability(Worker worker)
    {
        if (worker == null) return;
        try
        {
            if (worker.GetComponent<NpcShieldUser>() != null) return;

            NpcShieldUser comp = worker.gameObject.AddComponent<NpcShieldUser>();
            worker.npcShieldUser = comp;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Roles] Added NpcShieldUser to Greek worker (pickup capability)");
        }
        catch (Exception e)
        {
            // Awake 异常回滚时组件未挂上——记录一次，不反复抛。
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}

[HarmonyPatch(typeof(Mover))]
public static class Mover_Update_Patch
{
    [HarmonyPatch(nameof(Mover.Update))]
    [HarmonyPostfix]
    public static void Mover_Update_Postfix(Mover __instance)
    {
        if (!ModConfig.Enabled.Value) return;

        float targetY;
        if (!ScaleRegistryHolder.TryGet(__instance, out targetY)) return;
        if (targetY == 1f) return;

        Vector3 s = __instance.transform.localScale;
        if (Mathf.Abs(s.y - targetY) > 0.0001f)
        {
            s.y = targetY;
            __instance.transform.localScale = s;
        }
    }
}

[HarmonyPatch(typeof(WarriorPeasant))]
public static class WarriorPeasant_OnEnable_Patch
{
    [HarmonyPatch(nameof(WarriorPeasant.OnEnable))]
    [HarmonyPostfix]
    public static void WarriorPeasant_OnEnable_Postfix(WarriorPeasant __instance)
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            if (BiomeHolder.Inst != null && BiomeHolder.Inst.BiomeIndex == BiomeHolder.GreeceBiomeIndex)
            {
                __instance.transform.localScale = new Vector3(1f, 1.2f, 1f);
                ScaleRegistryHolder.Register(__instance.GetComponent<Mover>(), 1.2f);
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}

[HarmonyPatch(typeof(Deer))]
public static class Deer_OnEnable_Patch
{
    [HarmonyPatch(nameof(Deer.OnEnable))]
    [HarmonyPostfix]
    public static void Deer_OnEnable_Postfix(Deer __instance)
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            __instance.transform.localScale = new Vector3(1f, 0.55f, 1f);
            ScaleRegistryHolder.Register(__instance.GetComponent<Mover>(), 0.55f);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}

[HarmonyPatch(typeof(Critter))]
public static class Critter_OnEnable_Patch
{
    [HarmonyPatch(nameof(Critter.OnEnable))]
    [HarmonyPostfix]
    public static void Critter_OnEnable_Postfix(Critter __instance)
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            __instance.transform.localScale = new Vector3(1f, 1.8f, 1f);
            ScaleRegistryHolder.Register(__instance.GetComponent<Mover>(), 1.8f);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}

[HarmonyPatch(typeof(Peasant))]
public static class Peasant_OnEnable_Patch
{
    [HarmonyPatch(nameof(Peasant.OnEnable))]
    [HarmonyPostfix]
    public static void Peasant_OnEnable_Postfix(Peasant __instance)
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            string name = __instance.gameObject.name;
            if (name.Contains("Peasant_norselands"))
            {
                __instance.transform.localScale = new Vector3(1f, 1.125f, 1f);
                ScaleRegistryHolder.Register(__instance.GetComponent<Mover>(), 1.125f);
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}
