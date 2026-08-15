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
    private static readonly System.Collections.Generic.Dictionary<int, int> _pendingShieldEquip = new();

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

    public static void Unregister(Mover mover)
    {
        if (mover == null || mover.gameObject == null) return;
        _targets.Remove(mover.gameObject.GetInstanceID());
    }

    public static bool TryGet(Mover mover, out float y)
    {
        if (mover != null && mover.gameObject != null && _targets.TryGetValue(mover.gameObject.GetInstanceID(), out y))
            return true;
        y = 1f;
        return false;
    }

    public static void QueueShieldEquip(NpcShieldUser shieldUser)
    {
        if (shieldUser == null || shieldUser.gameObject == null) return;
        EnsureCreated();
        _pendingShieldEquip[shieldUser.gameObject.GetInstanceID()] = Time.frameCount + 1;
    }

    private void Update()
    {
        if (_pendingShieldEquip.Count == 0) return;

        var ready = new System.Collections.Generic.List<int>();
        foreach (var item in _pendingShieldEquip)
        {
            if (Time.frameCount >= item.Value) ready.Add(item.Key);
        }

        // IL2CPP wrapper identity 可变，所以队列只保存 Unity instance ID；
        // 同一帧的所有 ready item 共享一次场景扫描。
        NpcShieldUser[] all = UnityEngine.Object.FindObjectsOfType<NpcShieldUser>();
        for (int i = 0; i < ready.Count; i++)
        {
            int instanceId = ready[i];
            _pendingShieldEquip.Remove(instanceId);
            for (int j = 0; j < all.Length; j++)
            {
                NpcShieldUser shieldUser = all[j];
                if (shieldUser == null || shieldUser.gameObject == null
                    || shieldUser.gameObject.GetInstanceID() != instanceId) continue;

                if (!ModConfig.Enabled.Value || !shieldUser.isActiveAndEnabled
                    || !NetworkBigBoss.HasWorldAuth) break;

                Worker worker = shieldUser.GetComponent<Worker>();
                if (worker != null)
                    Worker_OnEnable_Patch.TryEquipShieldAfterRegistration(worker);
                break;
            }
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        _targets.Clear();
        _pendingShieldEquip.Clear();
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
            // 【SteamFixReviewer P1】真 prefab 路径（有 Damageable）放行原版 Awake——
            // 2.4.0 原版 Awake 的 OnPreReceiveDamage 订阅（盾牌减伤/破碎/再生）必须保留；
            // NRE 证据只在裸加路径（希腊拾取能力，GetComponent<Damageable>()==null）。
            if (__instance.GetComponent<Damageable>() != null) return true;

            // 裸加路径：安全版 Awake（跳过 damageable 订阅——无 Damageable 无从订阅）
            __instance.character = __instance.GetComponent<Character>();
            __instance.regenWait = new WaitForSeconds(1f);
            return false;  // 跳过原 Awake
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
            return true;
        }
    }
}


[HarmonyPatch(typeof(Character))]
public static class Worker_HammerPromotion_Toggle_Patch
{
    // 只有有效 Hammer 工具调用才参与交替；第一次北境，第二次希腊。
    // 映射只在本次 Promote(DroppableTool, ...) 的同步调用栈内替换，
    // Postfix/Finalizer 都会恢复，因此两次锤子之间的普通 Worker 生成不受污染。
    private static bool _nextPromotionUsesNorselands = true;
    private static int _holderInstanceId;
    private static int _prefabCacheHolderInstanceId;
    private static Character _greekWorkerPrefab;
    private static Character _norselandsWorkerPrefab;

    private sealed class PromotionState
    {
        public Holder Holder;
        public Character Original;
        public string TargetName;
        public bool Applied;
    }

    private static Character FindWorkerPrefab(Holder holder, string prefabName)
    {
        int holderInstanceId = holder.gameObject.GetInstanceID();
        if (_prefabCacheHolderInstanceId == holderInstanceId)
        {
            return prefabName == "Worker_norselands"
                ? _norselandsWorkerPrefab
                : _greekWorkerPrefab;
        }

        // Slow fallback: at most once per Holder/world. Holder.InitializeTagCharacterPairs
        // normally primes this cache before any peasant can pick up a hammer.
        var allChars = Resources.LoadAll<Character>("");
        Character greek = null;
        Character norselands = null;
        for (int i = 0; i < allChars.Length; i++)
        {
            Character character = allChars[i];
            if (character == null || character.gameObject == null) continue;
            if (character.gameObject.name == "Worker") greek = character;
            else if (character.gameObject.name == "Worker_norselands") norselands = character;

            if (greek != null && norselands != null) break;
        }

        PrimePrefabCache(holder, greek, norselands);
        return prefabName == "Worker_norselands" ? norselands : greek;
    }

    internal static void PrimePrefabCache(Holder holder, Character greek, Character norselands)
    {
        if (holder == null || holder.gameObject == null) return;
        _prefabCacheHolderInstanceId = holder.gameObject.GetInstanceID();
        _greekWorkerPrefab = greek;
        _norselandsWorkerPrefab = norselands;
    }

    [HarmonyPatch(nameof(Character.Promote), new[] { typeof(DroppableTool), typeof(IUnitController) })]
    [HarmonyPrefix]
    private static void Promote_Prefix(Character __instance, DroppableTool tool, out PromotionState __state)
    {
        __state = null;
        if (!ModConfig.Enabled.Value) return;
        if (!NetworkBigBoss.HasWorldAuth) return;
        // Peasant.HandleToolPickup 在调 Promote 前先把 pickedUp 设为 true；
        // 这些条件把任意直接调用/无效工具排除在交替序列之外。
        if (__instance == null
            || __instance.GetComponent<Peasant>() == null
            || tool == null
            || !tool.pickedUp
            || !tool.CompareTag("Hammer")
            || tool.gameObject == null
            || !tool.gameObject.activeInHierarchy) return;

        try
        {
            var holder = Managers.Inst != null ? Managers.Inst.holder : null;
            if (holder == null || holder.tagCharacterPairs == null) return;

            // 新 Holder 表示新的世界/读档运行时：从“第一次北境”重新开始。
            // 同一 Holder 下的分屏玩家共享一条成功转职序列。
            int holderInstanceId = holder.gameObject.GetInstanceID();
            if (_holderInstanceId != holderInstanceId)
            {
                _holderInstanceId = holderInstanceId;
                _nextPromotionUsesNorselands = true;
            }

            string prefabName = _nextPromotionUsesNorselands ? "Worker_norselands" : "Worker";
            Character target = FindWorkerPrefab(holder, prefabName);
            if (target == null)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[Roles] Alternate worker prefab not found: " + prefabName);
                return;
            }

            Character original = null;
            if (!holder.tagCharacterPairs.TryGetValue("Worker", out original) || original == null) return;

            __state = new PromotionState
            {
                Holder = holder,
                Original = original,
                TargetName = prefabName,
                Applied = true
            };
            holder.tagCharacterPairs["Worker"] = target;

        }
        catch (Exception e)
        {
            Restore(__state);
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    [HarmonyPatch(nameof(Character.Promote), new[] { typeof(DroppableTool), typeof(IUnitController) })]
    [HarmonyPostfix]
    private static void Promote_Postfix(Character __result, PromotionState __state)
    {
        if (__state == null || !__state.Applied) return;

        Restore(__state);
        if (__result == null || __result.gameObject == null) return;

        string resultName = __result.gameObject.name;
        if (resultName != __state.TargetName
            && !resultName.StartsWith(__state.TargetName + " P", StringComparison.Ordinal))
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[Roles] Alternate hammer promotion result mismatch: expected "
                + __state.TargetName + ", got " + resultName);
            return;
        }

        _nextPromotionUsesNorselands = !_nextPromotionUsesNorselands;
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[Roles] Alternate hammer promotion -> " + __state.TargetName);
    }

    [HarmonyPatch(nameof(Character.Promote), new[] { typeof(DroppableTool), typeof(IUnitController) })]
    [HarmonyFinalizer]
    private static Exception Promote_Finalizer(Exception __exception, PromotionState __state)
    {
        Restore(__state);
        return __exception;
    }

    private static void Restore(PromotionState state)
    {
        if (state == null || !state.Applied) return;
        state.Applied = false;

        try
        {
            if (state.Holder != null && state.Holder.tagCharacterPairs != null)
                state.Holder.tagCharacterPairs["Worker"] = state.Original;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}

[HarmonyPatch(typeof(Holder))]
public static class WorkerPrefabPoolWarmup_Patch
{
    [HarmonyPatch(nameof(Holder.InitializeTagCharacterPairs))]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    public static void Postfix(Holder __instance)
    {
        if (!ModConfig.Enabled.Value || __instance == null || __instance.tagCharacterPairs == null) return;

        Character original = null;
        if (!__instance.tagCharacterPairs.TryGetValue("Worker", out original) || original == null) return;

        try
        {
            string[] names = { "Worker", "Worker_norselands" };
            var allChars = Resources.LoadAll<Character>("");
            Character greek = null;
            Character norselands = null;
            for (int n = 0; n < names.Length; n++)
            {
                Character target = null;
                for (int i = 0; i < allChars.Length; i++)
                {
                    Character candidate = allChars[i];
                    if (candidate != null && candidate.gameObject.name == names[n])
                    {
                        target = candidate;
                        break;
                    }
                }
                if (target == null) continue;

                if (names[n] == "Worker") greek = target;
                else norselands = target;

                __instance.tagCharacterPairs["Worker"] = target;
                PatchRoles_Castle.EnsurePoolForCharacter("Worker");
            }

            Worker_HammerPromotion_Toggle_Patch.PrimePrefabCache(__instance, greek, norselands);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
        finally
        {
            __instance.tagCharacterPairs["Worker"] = original;
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
            TryEquipShieldAfterRegistration(__instance);
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
    public static void TryEquipShieldAfterRegistration(Worker worker)
    {
        if (worker == null) return;
        try
        {
            // 交替转职后希腊形态不带盾（无 norselands 名）；北境形态（prefab 或名字含 norselands）带盾
            if (!IsNorselandsWorker(worker)) return;

            NpcShieldUser shieldUser = worker.GetComponent<NpcShieldUser>();
            if (shieldUser == null || shieldUser.HasShield()) return;

            // shield 必须属于当前池实例。把 prefab 子对象引用直接赋给实例
            // 会操作 asset 而不是当前 Worker，因此缺引用时 fail closed。
            if (shieldUser.shield == null
                || shieldUser.shield.transform == null
                || !shieldUser.shield.transform.IsChildOf(worker.transform)) return;

            Damageable damageable = worker.GetComponent<Damageable>();
            Character character = worker.GetComponent<Character>();
            if (damageable == null || character == null) return;

            // Awake 可能在 HasWorldAuth 尚未就绪时提前退出。网络注册完成后
            // 重跑一次原版 Awake，恢复 character/damageable 与减伤事件订阅。
            if (shieldUser.character == null)
                shieldUser.character = character;

            if (shieldUser.damageable == null)
            {
                if (!NetworkBigBoss.HasWorldAuth || shieldUser.parentHeaderRef == null) return;
                shieldUser.enabled = true;
                shieldUser.Awake();
            }

            // SetShieldEnabled 末尾会 SendShieldEnabled；在 BeginRegisteringRPCs 之前调用
            // 会解引用空 parentHeaderRef。客户端等待主机同步，不本地装备。
            if (!NetworkBigBoss.HasWorldAuth
                || shieldUser.character == null
                || shieldUser.damageable == null
                || shieldUser.parentHeaderRef == null
                || shieldUser.shieldEnabledRpcIndex < 0) return;

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
        // 避免给无 Damageable 的希腊 worker 装备盾牌 → SetShieldEnabled NRE。
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

[HarmonyPatch(typeof(NpcShieldUser))]
public static class NpcShieldUser_RPCRegistration_Patch
{
    [HarmonyPatch(nameof(NpcShieldUser.BeginRegisteringRPCs))]
    [HarmonyPostfix]
    public static void BeginRegisteringRPCs_Postfix(NpcShieldUser __instance, bool __result)
    {
        if (!ModConfig.Enabled.Value || !__result || __instance == null) return;

        try
        {
            if (__instance.GetComponent<Worker>() != null)
                ScaleRegistryHolder.QueueShieldEquip(__instance);
        }
        catch (Exception e)
        {
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
