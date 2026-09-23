using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 宠物与隐士防抓（ModConfig.PetGuardEnabled，F5「便捷」页，默认关）。
///
/// 防抓（狗部分）：只改狗 Droppable.CurrentEnemyPolicy → Nobody。Troll.PickupLoot 的
/// 敌人拾取门走 Droppable.CanBePickedUpByEnemy（仅放行 Anybody/EnemyOnly，
/// Droppable.cs:348-352），置 Nobody 后怪物不再抓走狗；玩家/友军拾取走 pickUpPolicy，
/// 不受影响。隐士的同类防护由 PatchRoles_Hermit 在同一个开关相与下执行。
///
/// 找回：开关从关→开当刻、以及开启状态下每次加载/换岛的新世界世代，把仍为 Stolen 的
/// 狗/隐士改写为 Roaming(CurrentLand) 并按原生生成路径召回（狗 = DogRecall 同款
/// holder 预制件 + SpawnNearP1 + SetupDog + 颜色；隐士 = TryApplyHermit 同款
/// holder.hermits 预制件 + SpawnNearP1）。仅 authority；Stolen 之外的任何状态不动。
/// 同 id/同类型实例仍在场时本轮不改写也不生成（见 RecallDogs 注），该延后同样保持 pending，
/// 等实例生命周期结束后由 0.5s 节拍的重试继续找回；条件性失败（playerOne 缺失、状态读取/
/// 生成异常）同样重试而非一次性放弃。开关关闭 = Stolen 原样保留，赎回商人/炸门等原生恢复
/// 路径语义不变。
///
/// receipt 只挂长生命周期方法（Droppable.OnEnable postfix / OnDisable prefix，与
/// PatchRoles_Hermit 同款）；绝不 detour 短 getter（2026-09-13 崩溃先例）。
/// 船上狗还原窗口：Dog.DoJumpOnBoat 会把 CurrentEnemyPolicy 置 Nobody 并把狗挂到
/// Boat.body 下；关闭开关时不回写 Original，等离船后的 Tick 或原生 OnDisable 重置接管。
///
/// 2.4.0 签名验证（game-source/Assembly-CSharp decompile + interop dump）：
/// CampaignSaveData.SetDogStatus/GetDogStatus/SetHermitStatus/GetHermitStatus/CurrentLand/
/// SpawnNearP1&lt;T&gt;、Holder.dogPrefab/wolfPupPrefab/hermits(Il2CppReferenceArray)、
/// Kingdom.dogs/hermits/playerOne、Dog.DogId/SetupDog/color、Hermit.Type、
/// Droppable.OnEnable/OnDisable/CurrentEnemyPolicy、Boat.body 均核对一致。
/// </summary>
public static class PatchRoles_PetGuard
{
    private sealed class Receipt
    {
        internal Droppable Droppable;
        internal Dog Dog;
        internal GameObject Object;
        internal IntPtr DroppablePtr, DogPtr, ObjectPtr;
        internal int DroppableId, DogId, ObjectId;
        internal bool Bound, Owned, Contested;
        internal IntPtr WorldPtr, LayerPtr;
        internal int Scene;
        internal PickUpPolicy Original;
    }

    private struct Scope
    {
        internal Transform Layer;
        internal IntPtr WorldPtr, LayerPtr;
        internal int Scene;
    }

    private static readonly Dictionary<IntPtr, Receipt> Tracked = new();
    private static readonly List<IntPtr> Work = new();
    private static readonly Hermit.HermitType[] HermitTypes =
    {
        Hermit.HermitType.Horse, Hermit.HermitType.Horn, Hermit.HermitType.Ballista,
        Hermit.HermitType.Baker, Hermit.HermitType.Knight, Hermit.HermitType.Persephone,
        Hermit.HermitType.Fire
    };

    private static bool _dirty, _lastEnabled, _lastAuthority, _lastHasScope, _loggedProtection, _loggedFailure;
    private static bool _loggedDeferred;
    private static IntPtr _lastWorld, _lastLayer;
    private static int _lastScene;
    private static float _nextCheck;

    // 找回触发：开关或 authority 丢失后重新武装；离开可用上下文（Loading）后再回到
    // Playing/暂停的同一世界，或世界世代变化（读档/换岛）时执行一次。
    // 条件性失败/延后（playerOne 缺失、状态读取或生成异常、同 id 实例仍场）保持 _recallPending，
    // 按 0.5s 节拍重试直到完成（无上限：同 id 被怪携带可远超 10s；节流保证成本有界）。
    // 开关关闭/失权时连同武装一起丢弃 pending；世界世代变化由 fresh 触发新的完整一轮。
    private static bool _recallArmed, _recallStale, _recallPending;
    private static IntPtr _recallWorld, _recallLayer;
    private static int _recallScene;
    private static float _nextRecallAttempt;
    private const float RecallRetryInterval = 0.5f;

    private static bool IsEnabled()
        => ModConfig.Enabled != null && ModConfig.Enabled.Value
        && ModConfig.PetGuardEnabled != null && ModConfig.PetGuardEnabled.Value;

    internal static void OnEnabled(Droppable droppable)
    {
        try
        {
            if (droppable == null || droppable.gameObject == null) return;
            GameObject go = droppable.gameObject;
            Dog dog = go.GetComponent<Dog>();
            if (dog == null) return; // Real same-object component only; hermit receipts live in PatchRoles_Hermit.
            IntPtr pointer = droppable.Pointer;
            if (pointer == IntPtr.Zero) return;
            if (!Tracked.TryGetValue(pointer, out Receipt receipt) || !SameIdentity(receipt, droppable, dog, go))
            {
                Tracked[pointer] = new Receipt
                {
                    Droppable = droppable, Dog = dog, Object = go,
                    DroppablePtr = pointer, DogPtr = dog.Pointer, ObjectPtr = go.Pointer,
                    DroppableId = droppable.GetInstanceID(), DogId = dog.GetInstanceID(), ObjectId = go.GetInstanceID()
                };
            }
            // Duplicate OnEnable keeps the original receipt, including a suspended ownership claim.
            _dirty = true;
            // 只处理 receipt：不在别的 Droppable.OnEnable 调用栈里生成单位（找回由 ModPanel.Update 的 Tick 驱动）。
            try { TickReceipts(); }
            catch (Exception ex) { LogFailure(ex); }
        }
        catch (Exception ex) { LogFailure(ex); }
    }

    internal static void OnDisabled(Droppable droppable)
    {
        try
        {
            if (droppable == null) return;
            IntPtr pointer = droppable.Pointer;
            if (Tracked.TryGetValue(pointer, out Receipt receipt)
                && droppable.GetInstanceID() == receipt.DroppableId)
                Tracked.Remove(pointer);
            // Native Droppable.OnDisable resets its own enemy policy; do not restore here.
        }
        catch (Exception ex) { LogFailure(ex); }
    }

    /// <summary>Existing main-thread ModPanel.Update calls this; no new driver or scene scan.</summary>
    public static void Tick()
    {
        if (Tracked.Count > 0)
        {
            try { TickReceipts(); }
            catch (Exception ex) { LogFailure(ex); }
        }
        try { TickRecall(); }
        catch (Exception ex) { LogFailure(ex); }
    }

    private static void TickReceipts()
    {
        bool enabled = IsEnabled();
        bool authority = NetworkBigBoss.HasWorldAuth;
        bool hasScope = TryScope(out Scope scope);
        bool changed = enabled != _lastEnabled || authority != _lastAuthority || hasScope != _lastHasScope
            || (hasScope && (scope.WorldPtr != _lastWorld || scope.LayerPtr != _lastLayer || scope.Scene != _lastScene));
        float now = Time.unscaledTime;
        if (!_dirty && !changed && now < _nextCheck) return;
        _dirty = false;
        _lastEnabled = enabled; _lastAuthority = authority;
        _lastHasScope = hasScope;
        // A temporary unavailable/loading context must not erase the identity of a known paused world.
        if (hasScope) { _lastWorld = scope.WorldPtr; _lastLayer = scope.LayerPtr; _lastScene = scope.Scene; }
        _nextCheck = now + 0.5f;

        Work.Clear();
        Work.AddRange(Tracked.Keys);
        foreach (IntPtr pointer in Work)
        {
            if (!Tracked.TryGetValue(pointer, out Receipt receipt)) continue;
            try
            {
                if (!Process(receipt, hasScope, scope, enabled, authority)) Tracked.Remove(pointer);
            }
            catch (Exception ex) { LogFailure(ex); }
        }
    }

    private static bool TryScope(out Scope scope)
    {
        scope = default;
        Managers managers = Managers.Inst;
        World world = managers != null ? managers.world : null;
        Game game = managers != null ? managers.game : null;
        Transform layer = world != null ? world.gameLayer : null;
        if (world == null || game == null || layer == null) return false;
        IntPtr worldPtr = world.Pointer, layerPtr = layer.Pointer;
        int scene = layer.gameObject.scene.handle;
        Game.State state = game.state;
        bool knownPause = state == Game.State.Menu && worldPtr == _lastWorld
            && layerPtr == _lastLayer && scene == _lastScene;
        if (state != Game.State.Playing && state != Game.State.NetworkClientPlaying && !knownPause) return false;
        scope = new Scope { Layer = layer, WorldPtr = worldPtr, LayerPtr = layerPtr, Scene = scene };
        return true;
    }

    // False retires a stale generation without writing to it. Pending initial attachment is retained.
    private static bool Process(Receipt receipt, bool hasScope, Scope scope, bool enabled, bool authority)
    {
        if (!SameIdentity(receipt, receipt.Droppable, receipt.Dog, receipt.Object)
            || !receipt.Object.activeInHierarchy) return false;
        // Missing context is not proof of departure: retain Original without writing until scope returns.
        if (!hasScope) return true;
        GameObject go = receipt.Object;
        bool inScope = go.scene.handle == scope.Scene && go.transform.IsChildOf(scope.Layer);
        if (receipt.Bound)
        {
            if (!inScope || receipt.WorldPtr != scope.WorldPtr || receipt.LayerPtr != scope.LayerPtr
                || receipt.Scene != scope.Scene) return false;
        }
        else
        {
            // Pool enable can precede parenting. A different loaded scene is already outside this world.
            if (go.scene.handle != scope.Scene) return false;
            if (!inScope) return true;
            receipt.Bound = true;
            receipt.WorldPtr = scope.WorldPtr; receipt.LayerPtr = scope.LayerPtr; receipt.Scene = scope.Scene;
        }

        // Authority loss suspends the receipt. Never write native state or forget Original on a client.
        if (!authority) return true;
        PickUpPolicy current = receipt.Droppable.CurrentEnemyPolicy;
        if (receipt.Owned)
        {
            if (current != PickUpPolicy.Nobody)
            {
                receipt.Owned = false;
                receipt.Contested = true; // External policy owns this generation; never overwrite it later.
            }
            else if (!enabled)
            {
                // 船上狗由 Dog.DoJumpOnBoat 原生置 Nobody 并挂到 Boat.body 下：此刻还原成 Original
                // 会让船上的狗重新可被敌人抓。保持原生 Nobody，离船后下一次 Tick 还原；
                // 若一直留在船上直到销毁，则由原生 Droppable.OnDisable 重置接管。
                if (Aboard(go)) return true;
                receipt.Droppable.CurrentEnemyPolicy = receipt.Original;
                receipt.Owned = false;
            }
            return true;
        }
        if (!enabled || receipt.Contested
            || (current != PickUpPolicy.Anybody && current != PickUpPolicy.EnemyOnly)) return true;
        receipt.Original = current;
        receipt.Owned = true;
        receipt.Droppable.CurrentEnemyPolicy = PickUpPolicy.Nobody;
        if (!_loggedProtection)
        {
            _loggedProtection = true;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[PetGuard] Dog enemy pickup protected: id="
                + receipt.ObjectId + " original=" + (int)current + " current=" + (int)PickUpPolicy.Nobody);
        }
        return true;
    }

    /// <summary>船上的狗：自身或任一祖先带 BoatBody 标签，或祖先上挂 Boat 组件（Dog.RecvEmbark 挂 kingdom.boat 根）。</summary>
    private static bool Aboard(GameObject go)
    {
        Transform t = go != null ? go.transform : null;
        for (int depth = 0; t != null && depth < 8; depth++)
        {
            if (t.CompareTag("BoatBody") || t.GetComponent<Boat>() != null) return true;
            t = t.parent;
        }
        return false;
    }

    private static void TickRecall()
    {
        if (!IsEnabled() || !NetworkBigBoss.HasWorldAuth)
        {
            // 关闭或失权：解除武装并丢弃未完成的召回；下一次开启/接管重新走完整的找回触发。
            _recallArmed = false;
            _recallPending = false;
            return;
        }
        if (!TryScope(out Scope scope))
        {
            // Loading/未知上下文不写存档也不生成；回到可用上下文后补做（每次加载完成触发）。
            _recallStale = true;
            return;
        }
        bool fresh = !_recallArmed || _recallStale
            || scope.WorldPtr != _recallWorld || scope.LayerPtr != _recallLayer || scope.Scene != _recallScene;
        _recallArmed = true;
        _recallStale = false;
        _recallWorld = scope.WorldPtr; _recallLayer = scope.LayerPtr; _recallScene = scope.Scene;
        if (!fresh && !_recallPending) return;
        // fresh（开启/接管/换世界/加载完成）立即执行；未完成的条件性失败/延后按 0.5s 节拍重试，
        // 不逐帧扫描、也不因一次失败就此关闭（原缺陷：armed 置位但未完成却不再触发）。
        if (!fresh && Time.unscaledTime < _nextRecallAttempt) return;
        _nextRecallAttempt = Time.unscaledTime + RecallRetryInterval;
        _recallPending = !RecallStolenPets();
    }

    /// <summary>返回 false 表示存在条件性失败/延后，调用方保持 pending 并按节拍重试。</summary>
    private static bool RecallStolenPets()
    {
        Managers managers = Managers.Inst;
        Kingdom kingdom = managers != null ? managers.kingdom : null;
        Holder holder = managers != null ? managers.holder : null;
        CampaignSaveData save = CampaignSaveData.current;
        if (kingdom == null || holder == null || save == null) return false;
        // SpawnNearP1 原生读 playerOne.transform（同款前置）；缺失保持 pending，条件恢复后重试。
        if (kingdom.playerOne == null) return false;
        // 狗/隐士两半互相隔离：一半失败不吞掉另一半的找回。
        bool dogs, hermits;
        try { dogs = RecallDogs(save, kingdom, holder); }
        catch (Exception ex) { LogFailure(ex); dogs = false; }
        try { hermits = RecallHermits(save, kingdom, holder); }
        catch (Exception ex) { LogFailure(ex); hermits = false; }
        return dogs && hermits;
    }

    private static bool RecallDogs(CampaignSaveData save, Kingdom kingdom, Holder holder)
    {
        // 2.4 interop：Dog.DogStatus[] 暴露为 Il2CppStructArray<Dog.DogStatus>（同 notes-roles 的数组漂移）。
        var statuses = save.GetDogStatus();
        if (statuses == null) return false;
        bool complete = true;
        for (int dogId = 0; dogId < statuses.Length; dogId++)
        {
            try
            {
                Dog.DogStatus status = statuses[dogId];
                if (status.position != Dog.DogPosition.Stolen) continue;
                // 同 id 狗仍在场（尚未销毁/被怪携带的瞬时实例）时本轮不改写也不生成：
                // 改写却不生成会让狗在实例销毁后永久丢失，二次生成又会让同 dogId 双注册 RPC 922/964。
                // 延后同样保持 pending：实例生命周期结束后由节拍重试继续召回。
                if (HasDog(kingdom, dogId))
                {
                    LogDeferredOnce("dog " + dogId + " still present; recall deferred");
                    complete = false;
                    continue;
                }
                Dog prefab = status.type == Dog.DogType.Dog ? holder.dogPrefab : holder.wolfPupPrefab;
                if (prefab == null) { complete = false; continue; }
                Dog dog = CampaignSaveData.SpawnNearP1<Dog>(prefab, 1, CampaignSaveData.CarryForwardToolType.None);
                if (dog == null) { complete = false; continue; } // 生成失败保留 Stolen，等下一次重试
                dog.SetupDog(dogId);
                dog.color = status.color;
                save.SetDogStatus(Dog.DogPosition.Roaming, save.CurrentLand,
                    new Il2CppSystem.Nullable<UnityEngine.Color>(status.color),
                    new Il2CppSystem.Nullable<Dog.DogType>(status.type),
                    status.preferedPlayer, dogId);
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[PetGuard] dog " + dogId
                    + " recovered from stolen at land " + status.land + " -> " + save.CurrentLand);
            }
            catch (Exception ex) { LogFailure(ex); complete = false; }
        }
        return complete;
    }

    private static bool RecallHermits(CampaignSaveData save, Kingdom kingdom, Holder holder)
    {
        var prefabs = holder.hermits;
        if (prefabs == null) return false;
        bool complete = true;
        for (int i = 0; i < HermitTypes.Length; i++)
        {
            Hermit.HermitType type = HermitTypes[i];
            try
            {
                Hermit.HermitStatus status = save.GetHermitStatus(type);
                if (status.position != Hermit.HermitPosition.Stolen) continue;
                // 与原生 TryApplyHermit 同款双生守卫：该类型隐士仍在场则本轮不动，保持 pending 重试。
                if (HasHermit(kingdom, type))
                {
                    LogDeferredOnce("hermit " + (int)type + " still present; recall deferred");
                    complete = false;
                    continue;
                }
                Hermit prefab = prefabs[(int)type];
                if (prefab == null) { complete = false; continue; }
                Hermit hermit = CampaignSaveData.SpawnNearP1<Hermit>(prefab, 1, CampaignSaveData.CarryForwardToolType.None);
                if (hermit == null) { complete = false; continue; }
                // 全量覆写（position/player/land）：只把 Stolen 改成原生自由态，player 保留原值，
                // land 统一改写为当前岛（跨岛被偷同样找回）。
                save.SetHermitStatus(type, Hermit.HermitPosition.Roaming, status.player, save.CurrentLand);
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[PetGuard] hermit " + (int)type
                    + " recovered from stolen at land " + status.land + " -> " + save.CurrentLand);
            }
            catch (Exception ex) { LogFailure(ex); complete = false; }
        }
        return complete;
    }

    private static bool HasDog(Kingdom kingdom, int dogId)
    {
        var dogs = kingdom != null ? kingdom.dogs : null;
        if (dogs == null) return false;
        for (int i = 0; i < dogs.Count; i++)
        {
            Dog dog = dogs[i];
            if (dog != null && dog.gameObject != null && dog.gameObject.activeInHierarchy && dog.DogId == dogId) return true;
        }
        return false;
    }

    private static bool HasHermit(Kingdom kingdom, Hermit.HermitType type)
    {
        var hermits = kingdom != null ? kingdom.hermits : null;
        if (hermits == null) return false;
        for (int i = 0; i < hermits.Count; i++)
        {
            Hermit hermit = hermits[i];
            if (hermit != null && hermit.gameObject != null && hermit.gameObject.activeInHierarchy && hermit.Type == type) return true;
        }
        return false;
    }

    private static void LogDeferredOnce(string reason)
    {
        if (_loggedDeferred) return;
        _loggedDeferred = true;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[PetGuard] " + reason); }
        catch { }
    }

    private static bool SameIdentity(Receipt receipt, Droppable droppable, Dog dog, GameObject go)
    {
        return droppable != null && dog != null && go != null
            && droppable.Pointer == receipt.DroppablePtr && dog.Pointer == receipt.DogPtr
            && go.Pointer == receipt.ObjectPtr && droppable.GetInstanceID() == receipt.DroppableId
            && dog.GetInstanceID() == receipt.DogId && go.GetInstanceID() == receipt.ObjectId
            && droppable.gameObject != null && droppable.gameObject.Pointer == receipt.ObjectPtr
            && dog.gameObject != null && dog.gameObject.Pointer == receipt.ObjectPtr;
    }

    private static void LogFailure(Exception ex)
    {
        if (_loggedFailure) return;
        _loggedFailure = true;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[PetGuard] receipt/recall deferred: " + ex.GetType().Name); }
        catch { }
    }
}

[HarmonyPatch(typeof(Droppable), nameof(Droppable.OnEnable))]
internal static class Droppable_OnEnable_PetGuard_Patch
{
    [HarmonyPostfix]
    internal static void Postfix(Droppable __instance) => PatchRoles_PetGuard.OnEnabled(__instance);
}

[HarmonyPatch(typeof(Droppable), nameof(Droppable.OnDisable))]
internal static class Droppable_OnDisable_PetGuard_Patch
{
    [HarmonyPrefix]
    internal static void Prefix(Droppable __instance) => PatchRoles_PetGuard.OnDisabled(__instance);
}
