using System;
using System.Collections.Generic;
using System.Text.Json;
using Coatsink.Common;
using HarmonyLib;
using PrivateBankR3;
using UnityEngine;

namespace KingdomEnhancedMod;

// H-side override of the bank file (B copy, SHA 4d83443f06f0875e54015ae648b47bb340719a78cfdeb4a22f741953311aa01d).
// Only the AsyncGate prefix differs from B: it joins the shared HarmonyX callback-once convention
// with HeroNativeRights so two gates on GlobalSaveData.SaveAsync cannot each emit a failure
// callback. Every other method, field and nested patch class is byte-identical to B.
// The sole native owner of the R3 bank state. Scopes below retain evidence only.
internal static class SharedBankNative
{
    private const string Key = "MyMod_SharedBankNative_v1";
    private static GlobalSaveData _global;
    private static PrefsSaveData _prefs;
    private static CampaignSaveData[] _campaignRefs;
    private static object _token;
    private static SharedBankState _state;
    private static string _reason;
    private static Scope _save;
    private static Pop _pop;
    internal static Pop CurrentPop => _pop;
    private static int _bankSerial;
    private sealed class ReadyScene
    {
        internal object Owner;
        internal nint Account;
        internal int Land, Scene;
        internal IntPtr World, Layer;
    }
    private static ReadyScene _ready;

    internal sealed class Scope
    {
        internal Scope Previous;
        internal object Owner;
        internal nint Account;
        internal int Land, Campaign, Challenge, Serial;
        internal IntPtr Game, World, Layer;
        internal int Scene, Coins;
        internal IslandSaveData Island;
        internal IntPtr Root;
        internal int RootInstance;
        internal string RootId;
        internal bool Marker, Physical, NoBank, Closed;
        internal bool Participating;
        internal bool BadId, PrimeReady;
    }

    internal sealed class Pop
    {
        internal Pop Previous;
        internal IslandSaveData Island;
        internal object Owner;
        internal nint Account;
        internal int Land;
        internal IslandSaveData.ObjectData Row;
        internal Persistent Root;
        internal Banker Banker;
        internal bool Expected, Applied;
        internal bool Participating;
    }

    private static bool Same(GlobalSaveData value)
        => value != null && _global != null && value.Pointer != IntPtr.Zero
            && value.Pointer == _global.Pointer && value.prefs != null
            && _prefs != null && value.prefs.Pointer == _prefs.Pointer;

    private static bool Catalog(GlobalSaveData value, out BankCatalogEntry[] entries,
        out CampaignSaveData[] refs)
    {
        var all = new List<BankCatalogEntry>();
        var held = new List<CampaignSaveData>();
        entries = null; refs = null;
        if (value.campaigns == null || value.challenges == null) return false;
        for (int group = 0; group < 2; group++)
        {
            var list = group == 0 ? value.campaigns : value.challenges;
            for (int i = 0; i < list.Count; i++)
            {
                CampaignSaveData item = list[i];
                if (item == null || item.Pointer == IntPtr.Zero) return false;
                all.Add(new BankCatalogEntry((nint)item.Pointer,
                    group == 0 ? BankCategory.Normal : BankCategory.Challenge, i));
                held.Add(item);
            }
        }
        entries = all.ToArray(); refs = held.ToArray();
        return true;
    }

    private static bool Ensure(bool reconcile = false)
    {
        try
        {
            GlobalSaveData raw = GlobalSaveData._loaded;
            if (raw == null || raw.prefs == null || raw.prefs.contents == null) return false;
            if (!Same(raw))
            {
                _global = raw; _prefs = raw.prefs; _token = new object();
                _state = new SharedBankState(_token); _reason = null;
                _campaignRefs = null; _ready = null;
                if (!Catalog(raw, out var initial, out var refs))
                    return Fault("initial catalog");
                string document = null;
                if (raw.prefs.contents.TryGetValue(Key, out string found)) document = found;
                if (!_state.BindOnce(_token, initial, document)) return Fault("initial bank document");
                _campaignRefs = refs;
                return true;
            }
            if (_reason != null && !reconcile) return false;
            if (!reconcile) return true;
            if (_reason != null && !Recoverable(_reason)) return false;
            if (_campaignRefs == null && !_state.IsCorrupt)
            {
                if (!Catalog(raw, out var initial, out var initialRefs)) return Fault("initial catalog");
                string original = raw.prefs.contents.TryGetValue(Key, out string found) ? found : null;
                if (!_state.BindOnce(_token, initial, original)) return Fault("initial bank document");
                _campaignRefs = initialRefs;
            }
            if (!Catalog(raw, out var entries, out var current)) return Fault("catalog");
            if (!_state.ReconcileCatalog(_token, entries)) return Fault("catalog reconcile");
            _campaignRefs = current;
            _reason = null;
            return true;
        }
        catch (Exception e) { return Fault("owner read " + e.GetType().Name); }
    }

    private static bool Fault(string reason) { _reason = reason; return false; }

    // Read faults recover through the Global gate's full catalog re-read; every other reason keeps rejecting.
    private static bool Recoverable(string reason)
        => reason.StartsWith("catalog")
            || reason.StartsWith("initial catalog")
            || reason.StartsWith("owner read")
            || reason.StartsWith("account read");

    private static bool AccountReadFaulted()
        => _reason != null && _reason.StartsWith("account read");

    private static bool Account(out nint account)
    {
        account = 0;
        try
        {
            if (!Ensure() || _campaignRefs == null) return false;
            CampaignSaveData current = _global.GetCurrentCampaign();
            if (current == null) return false;
            int normalCount = _global.campaigns.Count;
            int slot = _global.currentChallenge == 0
                ? _global.currentCampaign
                : normalCount + _global.currentChallenge - 1;
            if (slot < 0 || slot >= _campaignRefs.Length) return false;
            CampaignSaveData held = _campaignRefs[slot];
            if (held == null || held.Pointer != current.Pointer) return false;
            account = (nint)held.Pointer;
            return true;
        }
        catch (Exception e) { return Fault("account read " + e.GetType().Name); }
    }

    private static bool Scene(out IntPtr game, out IntPtr world, out IntPtr layer,
        out int handle, out int land)
    {
        game = world = layer = IntPtr.Zero; handle = land = -1;
        Managers m = Managers.Inst;
        Game g = m?.game; World w = m?.world; Transform l = w?.gameLayer;
        if (g == null || w == null || l == null || l.gameObject == null) return false;
        game = g.Pointer; world = w.Pointer; layer = l.Pointer;
        handle = l.gameObject.scene.handle; land = g.currentLand;
        return game != IntPtr.Zero && world != IntPtr.Zero && layer != IntPtr.Zero && land >= 0;
    }

    // False means absent only after the native registry and kingdom claim were both read.
    private static bool Physical(out Banker banker, out Persistent root)
    {
        banker = null; root = null;
        var m = Managers.Inst;
        var postbox = NetworkPostbox.Instance;
        if (m?.kingdom == null || postbox?.DynamicObjects == null) throw new InvalidOperationException("bank registry");
        bool registered = postbox.DynamicObjects.TryGetValue((short)903, out CRPCHeader header);
        if (!registered)
        {
            if (m.kingdom.banker != null) throw new InvalidOperationException("unregistered bank claim");
            return false;
        }
        if (header == null || header.HeaderType != CRPCType.Dynamic || header.NetID != 903
            || header.referencedGO == null) throw new InvalidOperationException("bank header");
        banker = header.referencedGO.GetComponent<Banker>();
        Transform layer = m.world?.gameLayer;
        if (banker == null || banker.gameObject == null
            || banker.gameObject.Pointer != header.referencedGO.Pointer
            || banker.parentHeaderRef == null || banker.parentHeaderRef.Pointer != header.Pointer
            || layer == null || layer.gameObject == null
            || !banker.transform.IsChildOf(layer)
            || banker.gameObject.scene.handle != layer.gameObject.scene.handle
            || (m.kingdom.banker != null && m.kingdom.banker.Pointer != banker.Pointer))
            throw new InvalidOperationException("bank identity");
        root = banker.gameObject.GetComponent<Persistent>();
        if (root == null) throw new InvalidOperationException("bank root");
        return true;
    }

    internal static bool TryLive(out object owner, out nint account, out int coins)
    {
        owner = null; account = 0; coins = 0;
        if (!Account(out account)) return false;
        owner = _token;
        return _state.TryReadLive(owner, account, out coins);
    }

    internal static bool Observe(Banker banker, object owner, nint account, int coins)
    {
        if (!Ensure() || !ReferenceEquals(owner, _token) || coins < 0) return false;
        if (!_state.ObserveLive(owner, account, coins)) return Fault("observe live");
        return true;
    }

    internal static bool Claim(Banker banker, out object owner, out nint account, out int land)
    {
        owner = null; account = 0; land = -1;
        try
        {
            if (!Account(out account) || !Scene(out _, out _, out _, out _, out land)) return false;
            if (!Physical(out Banker current, out _) || current.Pointer != banker.Pointer) return false;
            owner = _token;
            return true;
        }
        catch (Exception e)
        {
            if (account != 0 && land >= 0 && _state.CaptureFailed(_token, land, account))
                return false;
            return Fault("claim " + e.GetType().Name);
        }
    }

    internal static bool SeedOrPrime(Banker banker, int? applied)
    {
        if (!Claim(banker, out object owner, out nint account, out int land)) return false;
        if (!_state.TryReadLive(owner, account, out int live))
        {
            if (!CanFirstSeed() || !applied.HasValue || applied.Value < 0) return false;
            live = applied.Value;
            if (!_state.ObserveLive(owner, account, live)) return Fault("apply seed");
        }
        try
        {
            banker._stashedCoins = live;
            if (banker._stashedCoins == live) return true;
        }
        catch (Exception) { /* this source remains unresolved until its complete capture */ }
        if (!_state.CaptureFailed(owner, land, account)) Fault("prime fault");
        return false;
    }

    private static bool CanFirstSeed()
        => GreekBankScope.IsActive && NetworkBigBoss.HasWorldAuth;

    internal static Pop BeginPop(IslandSaveData island)
    {
        var pop = new Pop { Previous = _pop, Island = island, Land = island != null ? island.land : -1 };
        _pop = pop;
        try
        {
            if (island == null || !Account(out pop.Account))
            {
                if (island != null && !AccountReadFaulted()) Fault("pop account");
                return pop;
            }
            pop.Owner = _token;
            pop.Participating = _state.TryReadLive(_token, pop.Account, out _) || CanFirstSeed();
            if (island.objects == null) return pop;
            foreach (var row in island.objects)
                if (row != null && row.netID == 903)
                {
                    pop.Expected = true;
                    if (pop.Row != null) { pop.Row = null; break; }
                    pop.Row = row;
                }
        }
        catch (Exception e) { Fault("pop begin " + e.GetType().Name); }
        return pop;
    }

    internal static void Created(IslandSaveData.ObjectData row, Persistent root)
    {
        Pop pop = _pop;
        if (pop == null || row == null || row.netID != 903) return;
        try
        {
            if (pop.Row == null || pop.Row.Pointer != row.Pointer || root == null
                || !RowAmount(row, out _)) return;
            Banker banker = root.gameObject.GetComponent<Banker>();
            if (banker != null && Physical(out Banker current, out Persistent exact)
                && current.Pointer == banker.Pointer && exact.Pointer == root.Pointer)
            {
                pop.Root = root;
                pop.Banker = banker;
            }
        }
        catch (Exception e)
        {
            if (pop.Owner == null || !_state.CaptureFailed(pop.Owner, pop.Land, pop.Account))
                Fault("create bank " + e.GetType().Name);
        }
    }

    internal static void Applied(Banker banker, Il2CppSystem.Object data)
    {
        Pop pop = _pop;
        if (pop == null || !pop.Participating || !pop.Expected || pop.Applied || pop.Banker == null) return;
        try
        {
            BankerData typed = data?.TryCast<BankerData>();
            if (typed == null || banker.Pointer != pop.Banker.Pointer
                || !ReferenceEquals(pop.Owner, _token) || !Account(out nint account)
                || account != pop.Account || !RowAmount(pop.Row, out int rowCoins)
                || rowCoins != typed.stashedCoins
                || !Physical(out Banker current, out Persistent root)
                || current.Pointer != banker.Pointer || root.Pointer != pop.Root.Pointer)
                return;
            if (PatchEconomy_Banker.AfterNativeApply(banker, typed.stashedCoins))
            {
                pop.Applied = true;
                _bankSerial++;
            }
        }
        catch (Exception e)
        {
            if (pop.Owner == null || !_state.CaptureFailed(pop.Owner, pop.Land, pop.Account))
                Fault("bank apply " + e.GetType().Name);
        }
    }

    internal static void EndPop(Pop pop, bool normal)
    {
        if (pop == null) return;
        if (ReferenceEquals(_pop, pop)) _pop = pop.Previous;
        if (!Ensure())
        {
            if (pop.Participating && pop.Expected && ReferenceEquals(pop.Owner, _token))
                _state.CaptureFailed(pop.Owner, pop.Land, pop.Account);
            return;
        }
        if (pop.Owner != null && !ReferenceEquals(pop.Owner, _token)) return;
        if (pop.Participating && pop.Expected && (!normal || !pop.Applied))
        {
            if (pop.Owner == null || !ReferenceEquals(pop.Owner, _token)
                || !_state.CaptureFailed(pop.Owner, pop.Land, pop.Account))
                Fault("bank pop failed");
        }
        else if (normal) SceneApplied();
    }

    [HarmonyPatch(typeof(Banker), nameof(Banker.Persistent_IBehaviour_ApplyData))]
    private static class ApplyPatch
    {
        [HarmonyPrefix] private static void Prefix(Banker __instance)
            => PatchEconomy_Banker.BeforeNativeApply(__instance);
        [HarmonyPostfix] private static void Postfix(Banker __instance, Il2CppSystem.Object __0)
            => Applied(__instance, __0);
    }

    internal static void Retire(object owner, nint account, int land, Banker banker, bool captured)
    {
        _bankSerial++;
        if (!Ensure() || !ReferenceEquals(owner, _token)) return;
        bool stillPresent = false;
        foreach (var item in _campaignRefs) if ((nint)item.Pointer == account) stillPresent = true;
        if (!stillPresent) return; // A deleted account has no remaining balance or source fault.
        try
        {
            int last = banker._stashedCoins;
            if (last < 0 || !_state.ObserveLive(owner, account, last)) Fault("retire observe");
        }
        catch
        {
            if ((!captured || _state.HasUncapturedBalance(owner, account))
                && !_state.CaptureFailed(owner, land, account)) Fault("retire fault");
        }
    }

    internal static void RetireFailed(object owner, nint account, int land)
    {
        _bankSerial++;
        if (!Ensure() || !ReferenceEquals(owner, _token)) return;
        foreach (var item in _campaignRefs)
            if ((nint)item.Pointer == account)
            {
                if (!_state.CaptureFailed(owner, land, account)) Fault("retire source");
                return;
            }
    }

    internal static void BankLifecycleChanged() => _bankSerial++;

    internal static void BeforeMutation(GlobalSaveData value)
    {
        try { if (value != null && GlobalSaveData._loaded?.Pointer == value.Pointer) Ensure(); }
        catch (Exception e) { Fault("mutation " + e.GetType().Name); }
    }

    internal static void SceneApplied()
    {
        try
        {
            if (Account(out nint account)
                && Scene(out _, out IntPtr world, out IntPtr layer, out int scene, out int land))
                _ready = new ReadyScene { Owner = _token, Account = account, Land = land,
                    World = world, Layer = layer, Scene = scene };
        }
        catch (Exception e) { Fault("scene ready " + e.GetType().Name); }
    }

    internal static Scope BeginSave(int campaign, int land, int challenge)
    {
        var scope = new Scope { Previous = _save, Campaign = campaign, Challenge = challenge, Land = -1 };
        _save = scope;
        try
        {
            if (!Account(out scope.Account))
            {
                if (!AccountReadFaulted()) Fault("save account");
                return scope;
            }
            scope.Owner = _token;
            if (_global.currentCampaign != campaign || _global.currentChallenge != challenge
                || !Scene(out scope.Game, out scope.World, out scope.Layer,
                    out scope.Scene, out scope.Land)
                || (land >= 0 && land != scope.Land)) return scope;
            bool known = _state.TryReadLive(scope.Owner, scope.Account, out _);
            scope.Participating = known || CanFirstSeed(); // retain responsibility if 903 read throws
            bool hasBank = Physical(out Banker banker, out Persistent root);
            scope.Participating = known || (hasBank && CanFirstSeed());
            if (!scope.Participating) return scope;
            if (hasBank)
            {
                scope.Physical = true;
                scope.Root = root.Pointer;
                scope.RootInstance = root.GetInstanceID();
                scope.PrimeReady = !known || PatchEconomy_Banker.EnsurePublicPrime(banker);
            }
            else if (_ready != null && ReferenceEquals(_ready.Owner, scope.Owner)
                && _ready.Account == scope.Account && _ready.Land == scope.Land
                && _ready.World == scope.World && _ready.Layer == scope.Layer
                && _ready.Scene == scope.Scene && !PatchEconomy_Banker.HasPrimeClaim()
                && _state.TryReadLive(scope.Owner, scope.Account, out scope.Coins))
                scope.NoBank = true;
            // Freeze after this scope's own preparation: the prefix-owned retirement of the old
            // claim must not veto the same native capture. Foreign lifecycle changes after this
            // point still bump the serial and reject.
            scope.Serial = _bankSerial;
        }
        catch (Exception e)
        {
            if (scope.Owner == null || scope.Land < 0
                || !_state.CaptureFailed(scope.Owner, scope.Land, scope.Account))
                Fault("save begin " + e.GetType().Name);
        }
        return scope;
    }

    internal static void ObserveId(Persistent root, string id)
    {
        Scope scope = _save;
        if (scope == null || !scope.Physical || root == null || root.Pointer != scope.Root) return;
        try
        {
            if (root.GetInstanceID() != scope.RootInstance || string.IsNullOrEmpty(id)
                || (scope.RootId != null && scope.RootId != id)) scope.BadId = true;
            else scope.RootId = id; // GetID may repeat for the same parent link.
        }
        catch (Exception) { scope.BadId = true; }
    }

    internal static void Marker(IslandSaveData island)
    {
        try
        {
            if (_save != null && island != null && IslandSaveData.CurrentlySavingIsland != null
                && IslandSaveData.CurrentlySavingIsland.Pointer == island.Pointer)
            {
                _save.Island = island;
                _save.Marker = true;
            }
        }
        catch (Exception e) { Fault("bank marker " + e.GetType().Name); }
    }

    private static bool SameScene(Scope scope)
        => ReferenceEquals(scope.Owner, _token)
            && _global.currentCampaign == scope.Campaign
            && _global.currentChallenge == scope.Challenge
            && Account(out nint account) && account == scope.Account
            && Scene(out IntPtr game, out IntPtr world, out IntPtr layer,
                out int handle, out int land)
            && game == scope.Game && world == scope.World && layer == scope.Layer
            && handle == scope.Scene && land == scope.Land && scope.Serial == _bankSerial;

    private static bool RowAmount(IslandSaveData.ObjectData row, out int coins)
    {
        coins = 0;
        if (row == null || row.netID != 903 || row.componentData2 == null) return false;
        int found = 0;
        foreach (var component in row.componentData2)
        {
            if (component == null || component.name != "Banker" || component.type != "BankerData")
                continue;
            if (++found != 1 || string.IsNullOrEmpty(component.data)) return false;
            using var json = JsonDocument.Parse(component.data);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("stashedCoins", out var value)
                || !value.TryGetInt32(out coins) || coins < 0) return false;
        }
        return found == 1;
    }

    internal static void EndSave(Scope scope, bool normal)
    {
        if (scope == null || scope.Closed) return;
        scope.Closed = true;
        if (ReferenceEquals(_save, scope)) _save = scope.Previous;
        if (!scope.Participating) return;
        if (!Ensure())
        {
            if (ReferenceEquals(scope.Owner, _token))
                _state.CaptureFailed(scope.Owner, scope.Land, scope.Account);
            return;
        }
        if (scope.Owner != null && !ReferenceEquals(scope.Owner, _token)) return;
        if (scope.Owner == null) { Fault("unbound save"); return; }
        try
        {
            bool valid = normal && scope.Marker && scope.Island != null && SameScene(scope);
            int amount = 0;
            if (valid && scope.NoBank)
            {
                int live;
                valid = !Physical(out _, out _) && !PatchEconomy_Banker.HasPrimeClaim()
                    && _state.TryReadLive(scope.Owner, scope.Account, out live)
                    && live == scope.Coins;
                amount = scope.Coins;
            }
            else if (valid && scope.Physical)
            {
                valid = scope.PrimeReady && !scope.BadId && Physical(out _, out Persistent root)
                    && root.Pointer == scope.Root && root.GetInstanceID() == scope.RootInstance
                    && scope.RootId != null;
                int count = 0;
                if (valid && scope.Island.objects != null)
                    foreach (var row in scope.Island.objects)
                        if (row != null && row.uniqueID == scope.RootId)
                        {
                            count++;
                            if (!RowAmount(row, out amount)) valid = false;
                        }
                valid &= count == 1;
            }
            else valid = false;
            if (valid && scope.Physical && !_state.TryReadLive(scope.Owner, scope.Account, out _))
                valid = CanFirstSeed() && _state.ObserveLive(scope.Owner, scope.Account, amount);
            if (valid)
                valid = _state.CaptureSucceeded(scope.Owner, scope.Land,
                    new[] { new BankCapture(scope.Account, amount) }, !scope.NoBank);
            if (valid && scope.Physical)
                PatchEconomy_Banker.CaptureClosed(scope.Owner, scope.Account, scope.Land,
                    scope.Root, scope.RootInstance, amount);
            if (!valid && !_state.CaptureFailed(scope.Owner, scope.Land, scope.Account))
                Fault("capture failed");
        }
        catch (Exception e)
        {
            if (!_state.CaptureFailed(scope.Owner, scope.Land, scope.Account))
                Fault("capture exception " + e.GetType().Name);
        }
    }

    private static bool Prepare(GlobalSaveData value)
    {
        try
        {
            if (value == null || GlobalSaveData._loaded == null
                || GlobalSaveData._loaded.Pointer != value.Pointer || !Ensure(true)) return false;
            if (_reason != null) return false;
            var status = _state.BuildSaveDocument(_token, out string document);
            if (status == SaveDocumentStatus.NoKey) return true;
            if (status != SaveDocumentStatus.Ready) return false;
            try
            {
                _prefs.contents[Key] = document;
                if (!_prefs.contents.TryGetValue(Key, out string readback) || readback != document
                    || !_state.MarkWriteVerified(_token, readback)) return false;
            }
            catch (Exception) { return false; } // same staged document may be retried
            return true;
        }
        catch (Exception e) { return Fault("save " + e.GetType().Name); }
    }

    [HarmonyPatch(typeof(GlobalSaveData), nameof(GlobalSaveData.SaveAsync))]
    private static class AsyncGate
    {
        [HarmonyPrefix] private static bool Prefix(GlobalSaveData __instance,
            Il2CppSystem.Action<SaveLoadResult> __0, ref bool __runOriginal)
        {
            // HarmonyX 2.10.2 runs every prefix and ANDs their results (WritePrefixes IL): the
            // hero rights gate on the same native method may refuse too. Only the first gate
            // that refuses completes the native failure callback; the shared __runOriginal flag
            // (by-ref injected) tells a later gate that the refusal was already reported.
            bool first = __runOriginal;
            if (Prepare(__instance)) return true;
            if (first)
            {
                try { __0?.Invoke(SaveLoadResult.Save | SaveLoadResult.Failure); }
                catch (Exception e)
                {
                    try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                        "[BankNative] save failure callback threw: " + e.GetType().Name); }
                    catch (Exception) { }
                }
            }
            __runOriginal = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(GlobalSaveData._Save_d__89), nameof(GlobalSaveData._Save_d__89.MoveNext))]
    private static class SyncGate
    {
        [HarmonyPrefix] private static bool Prefix(GlobalSaveData._Save_d__89 __instance, ref bool __result)
        {
            if (__instance.__1__state != 0 || Prepare(__instance.__4__this)) return true;
            try
            {
                var boxed = __instance.@return;
                boxed.value = SaveLoadResult.Save | SaveLoadResult.Failure;
                __instance.@return = boxed;
                if ((int)__instance.@return.value != 0x88)
                    KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[BankNative] boxed readback failed");
            }
            catch (Exception) { /* this call remains blocked; no owner fault */ }
            __instance.__1__state = -1;
            __result = false;
            return false;
        }
    }
}
