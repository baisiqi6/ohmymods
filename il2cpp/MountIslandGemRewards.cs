#if !ANDROID
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Finite native Chest batch. Loot/IDs/rows belong to native code; only issuance is ours.</summary>
internal static partial class MountIslandGemRewards
{
    private const string PrefabPath = "Prefabs/Objects/GemChestFixedAmount";
    private const string NamePrefix = "KEM.MountIslandGems.v1/";
    private static IntPtr _global, _prefs;
    private static MountIslandGemRewardState _state;
    private static string _expectedRaw;
    private static (IntPtr Global, IntPtr Campaign, IntPtr Prefs, int Slot, string Raw, string Guid) _guidCache;
    private static bool _spillUnsafe, _captureUnknown;
    private static readonly HashSet<(string Guid, int Land)> CaptureFaults = new();
    // A skipped native restoration must not be "repaired" by a later empty Save capture.
    private static readonly Dictionary<(string Guid, int Land), long> RestorationFaults = new();
    private static readonly HashSet<MountIslandGemOwner> Attempted = new();
    private static readonly HashSet<MountIslandGemOwner> Inspected = new();
    private static readonly Dictionary<(string Guid, int Land), Batch> Batches = new();
    private static readonly HashSet<(string Guid, int Land)> EconomicEffects = new();
    private static readonly Dictionary<IntPtr, Spill> Spills = new();
    private static readonly HashSet<string> Diagnostics = new(StringComparer.Ordinal);
    private static SaveScope _scope;
    private static bool _tickPending, _lastEnabled;
    private static float _nextTickAt;
    private static void WakeTick() { _tickPending = true; _nextTickAt = 0f; }
    private static string _nativePath, _nativeFile;
    private static bool _prepareFault, _catalogKnown;
    private static int _selectedSlot = -1;
    private static string _selectedGuid;
    private static readonly Dictionary<IntPtr, int> CatalogSlots = new();
    private static readonly HashSet<(string Guid, int Land)> UncapturedSpills = new();
    private static readonly Dictionary<(string Guid, int Land), List<string>> RowProofs = new();
    private sealed class WriterSnapshot
    {
        internal string Path, File;
        internal bool Safe;
        internal MountIslandGemNativeExpectation Expected;
    }
    private static WriterSnapshot _writer;
    private static string NativePath(string folder, string file)
    { try { return Path.GetFullPath(Path.Combine(folder, file)); } catch (Exception) { return null; } }
    private static void PublishWriter()
    {
        if (_state == null || !_state.CanSerialize || (!_state.HasStaged && EconomicEffects.Count == 0
            && CaptureFaults.Count == 0 && RestorationFaults.Count == 0 && !_spillUnsafe && !_captureUnknown)) { Volatile.Write(ref _writer, null); return; }
        var expected = new MountIslandGemNativeExpectation { Slot = _selectedSlot, Guid = _selectedGuid, Batch = _state.SerializeStaged() };
        foreach (var proof in RowProofs)
        {
            if (!Batches.TryGetValue(proof.Key, out var batch) || !CatalogSlots.TryGetValue(batch.Owner.Campaign, out int slot)) continue;
            if (!expected.Campaigns.TryGetValue(proof.Key.Guid, out var campaign))
                expected.Campaigns.Add(proof.Key.Guid, campaign = new MountIslandGemNativeCampaignProof { Slot = slot });
            campaign.Rows[proof.Key.Land] = new List<string>(proof.Value);
        }
        bool safe = !_spillUnsafe && !_captureUnknown && !_prepareFault && CaptureFaults.Count == 0
            && RestorationFaults.Count == 0 && UncapturedSpills.Count == 0 && _catalogKnown;
        Volatile.Write(ref _writer, new WriterSnapshot { Path = _nativePath, File = _nativeFile, Safe = safe, Expected = expected });
    }
    private static void ObserveCatalog()
    {
        _catalogKnown = false; _selectedGuid = null; CatalogSlots.Clear();
        var global = GlobalSaveData.loaded;
        if (global == null || global.Pointer != _global || global.campaigns == null || global.campaigns.Count > 64) return;
        int selected = global.currentCampaign;
        if (selected < -1 || selected >= global.campaigns.Count) return;
        for (int slot = 0; slot < global.campaigns.Count; slot++)
        {
            var campaign = global.campaigns[slot];
            if (campaign != null && !CatalogSlots.TryAdd(campaign.Pointer, slot)) return;
        }
        _selectedSlot = selected; _catalogKnown = true;
        if (selected >= 0 && TryReadDurableGuid(global, global.campaigns[selected], out string guid, out int resolved) && resolved == selected) _selectedGuid = guid;
    }
    private static List<string> JsonRows(List<(string Id, string Name, string Path, string Json)> rows)
    { var result = new List<string>(); if (rows != null) foreach (var row in rows) result.Add(row.Json); return result; }
    private static void PrepareFailed() { _prepareFault = true; PublishWriter(); }

    internal sealed class Box
    {
        internal GameObject Root;
        internal Chest Chest;
        internal Persistent Persistent;
        internal string Name;
        internal readonly List<(Collider2D Collider, bool Enabled)> Colliders = new();
    }
    internal sealed class Batch
    {
        internal MountIslandGemOwner Owner;
        internal long SceneEpoch;
        internal Box[] Boxes;
    }

    private readonly record struct Scene(IntPtr Global, IntPtr Campaign, IntPtr Game, IntPtr World,
        IntPtr Level, IntPtr Layer, IntPtr Config, int Land);
    private static Scene? _scene;
    private static long _loadEpoch, _sceneEpoch;
    internal sealed class LevelLoadScope { internal Level Level; internal long Epoch; }
    internal static LevelLoadScope BeginLevelLoad(Level level)
    {
        var scope = new LevelLoadScope { Level = level, Epoch = ++_loadEpoch };
        _tickPending = false;
        if (_scene.HasValue && level != null && _scene.Value.Level == level.Pointer) _scene = null;
        return scope;
    }
    internal static void EndLevelLoad(LevelLoadScope scope, bool normal)
    {
        if (scope == null || scope.Epoch != _loadEpoch || !normal || scope.Level == null) return;
        try
        {
            if (TryScene(out var scene) && scene.Level == scope.Level.Pointer)
            {
                _scene = scene; _sceneEpoch = scope.Epoch; WakeTick();
                // Cold restoration can receive a native trigger before the first panel Update.
                // Bind existing durable rewards here, without issuing anything before Playing.
                var prefs = GlobalSaveData.loaded?.prefs;
                if (prefs?.contents != null && (_state != null || prefs.contents.ContainsKey(MountIslandGemRewardState.Key)) && Bind())
                {
                    ObserveCatalog();
                    if (SourceMatches() && TryOwner(false, out var owner) && _state.StagedIssued(owner.Guid, owner.Land)) ObserveRestored(owner);
                }
            }
        }
        catch (Exception error) { _captureUnknown = true; Note("level-load:" + error.GetType().Name); }
        finally { PublishWriter(); }
    }
    private static bool TryScene(out Scene scene)
    {
        scene = default;
        var global = GlobalSaveData.loaded; var campaign = CampaignSaveData.current; var managers = Managers.Inst;
        var game = managers?.game; var world = managers?.world; var level = managers?.level; var layer = world?.gameLayer;
        if (global == null || campaign == null || game == null || world == null || level == null || layer == null
            || game.currentLevelConfig == null
            || level.GroundCollider == null || World.GroundCollider == null || level.GroundCollider.Pointer != World.GroundCollider.Pointer) return false;
        scene = new Scene(global.Pointer, campaign.Pointer, game.Pointer, world.Pointer, level.Pointer,
            layer.Pointer, game.currentLevelConfig.Pointer, game.currentLand);
        return true;
    }

    // Read-only reuse of the existing HeavyShield native Prefs GUID map. No money-ready dependency,
    // no Palace runtime dependency, no GUID allocation and no parallel identity authority.
    internal static bool TryReadDurableGuid(GlobalSaveData global, CampaignSaveData campaign, out string guid, out int slot)
    {
        guid = null; slot = -1;
        try
        {
            if (global == null || campaign == null || global.prefs == null
                || !ExtensionIslandRuntime.TryResolveRawOwner(out var first, out int selected, out var owner)
                || first.Pointer != global.Pointer || owner.Pointer != campaign.Pointer) return false;
            var prefs = global.prefs;
            if (prefs.contents == null || !prefs.contents.TryGetValue(HeavyShieldSaveSchema.Key, out string raw)) return false;
            string candidate = null;
            if (_guidCache.Global == global.Pointer && _guidCache.Campaign == campaign.Pointer && _guidCache.Prefs == prefs.Pointer
                && _guidCache.Slot == selected && _guidCache.Raw == raw) candidate = _guidCache.Guid;
            else
            {
                if (!HeavyShieldSaveCodec.TryParse(raw, out var document, out _)) return false;
                int matches = 0;
                foreach (var row in document.Campaigns) if (row.Slot == selected) { candidate = row.Guid; matches++; }
                if (matches != 1) return false;
            }
            if (string.IsNullOrEmpty(candidate)
                || !ExtensionIslandRuntime.TryResolveRawOwner(out var second, out int repeated, out var current)
                || second.Pointer != global.Pointer || current.Pointer != campaign.Pointer || repeated != selected
                || second.prefs == null || second.prefs.Pointer != prefs.Pointer
                || !prefs.contents.TryGetValue(HeavyShieldSaveSchema.Key, out string readback) || raw != readback) return false;
            _guidCache = (global.Pointer, campaign.Pointer, prefs.Pointer, selected, raw, candidate);
            guid = candidate; slot = selected;
            return true;
        }
        catch (Exception) { return false; }
    }

    internal static void Tick()
    {
        bool enabled;
        try { enabled = Enabled(); } catch (Exception) { return; }
        if (enabled != _lastEnabled) { _lastEnabled = enabled; WakeTick(); }
        if (!_tickPending) return;
        if (!_scene.HasValue) { _tickPending = false; return; }
        float now = Time.unscaledTime;
        if (now < _nextTickAt) return;
        _nextTickAt = now + .5f; // Read-only first-GUID/readiness wait; native Prepare wakes it immediately.
        try
        {
            if (!Bind() || !SourceMatches()) { _tickPending = false; return; }
            if (!TryScene(out var scene) || _scene.Value != scene) { _tickPending = false; return; }
            if (!MountIslandGemRewardState.TryPlan(scene.Land, out _, out _)) { _tickPending = false; return; }
            if (!TryOwner(false, out var owner)) return;
            if (_state.StagedIssued(owner.Guid, owner.Land)) { ObserveRestored(owner); _tickPending = false; return; }
            if (!_state.CanIssue(owner.Guid, owner.Land)) { _tickPending = false; return; }
            var managers = Managers.Inst;
            if (Inspected.Add(owner))
            {
                if (Inspected.Count > 128) { _state.Freeze("scene-bound"); _tickPending = false; Note("scene-bound"); return; }
                if (TryReadRawIsland(owner.Land, out var raw) && raw.objects != null)
                    foreach (var row in raw.objects)
                        if (row != null && row.name != null && row.name.StartsWith(NamePrefix, StringComparison.Ordinal))
                        {
                            RowProofs[(owner.Guid, owner.Land)] = JsonRows(ReadOwnedRows(raw));
                            _captureUnknown = true; _state.Freeze("owned-rows-without-marker"); _tickPending = false;
                            Note("owned-rows-without-marker"); return;
                        }
                foreach (var chest in managers.world.gameLayer.GetComponentsInChildren<Chest>(true))
                    if (chest != null && chest.gameObject.name != null && chest.gameObject.name.StartsWith(NamePrefix, StringComparison.Ordinal))
                    { _captureUnknown = true; _state.Freeze("owned-rows-without-marker"); _tickPending = false; Note("owned-rows-without-marker"); return; }
            }
            if (!enabled) { _tickPending = false; return; }
            if (!TryOwner(true, out owner)) return;
            _tickPending = false;
            if (!Attempted.Add(owner)) return;
            // GUID absence is a normal first-save wait. Costly work is bounded per exact scene.
            if (managers.game.currentLevelConfig == null || managers.game.currentLevelConfig.incomeMultiplier != 1f)
            { Note("income-multiplier-not-one"); return; }
            if (!TryPositions(out var positions)) { Note("no-walkable-placement"); return; }
            CreateBatch(owner, positions);
        }
        catch (Exception error) { Note("tick:" + error.GetType().Name); }
        finally { PublishWriter(); }
    }

    private static bool Enabled() => ModConfig.Enabled != null && ModConfig.Enabled.Value
        && ModConfig.CrossWorldMountsEnabled != null && ModConfig.CrossWorldMountsEnabled.Value;

    private static bool Bind()
    {
        var global = GlobalSaveData.loaded;
        if (global == null || global.prefs == null || global.prefs.contents == null) return false;
        if (_state != null && _global == global.Pointer && _prefs == global.prefs.Pointer) return _state.Ready;
        ResetBinding();
        _global = global.Pointer; _prefs = global.prefs.Pointer;
        var rawAgent = Filer.agent as Il2CppObjectBase;
        var agent = rawAgent?.TryCast<Filer.DotNetAgent>();
        _nativeFile = GlobalSaveData.filename;
        if (agent == null || string.IsNullOrEmpty(agent.folder) || agent.folder != Filer.folder
            || string.IsNullOrEmpty(_nativeFile) || Path.GetFileName(_nativeFile) != _nativeFile)
        { Note("native-writer-path-unverified"); return false; }
        _nativePath = NativePath(agent.folder, _nativeFile);
        if (_nativePath == null) return false;
        _expectedRaw = global.prefs.contents.TryGetValue(MountIslandGemRewardState.Key, out string raw) ? raw : null;
        _state = new MountIslandGemRewardState(_expectedRaw);
        if (!_state.Ready) Note("invalid-batch-payload-preserved");
        else ObserveCatalog();
        return _state.Ready;
    }

    private static bool SourceMatches()
    {
        var prefs = GlobalSaveData.loaded?.prefs;
        if (prefs == null || prefs.Pointer != _prefs || prefs.contents == null) return false;
        string raw = prefs.contents.TryGetValue(MountIslandGemRewardState.Key, out string value) ? value : null;
        if (raw == _expectedRaw) return true;
        _state.Freeze("batch-source-changed"); Note("batch-source-changed");
        return false;
    }

    // Capture intentionally accepts Menu: Esc Save/Quit and voyage save still own this exact source scene.
    private static bool TryOwner(bool spawning, out MountIslandGemOwner owner)
    {
        owner = default;
        if (!_scene.HasValue || !TryScene(out var scene) || _scene.Value != scene || Level.isGenerating) return false;
        var global = GlobalSaveData.loaded; var campaign = CampaignSaveData.current; var managers = Managers.Inst;
        var game = managers?.game; var world = managers?.world; var level = managers?.level; var layer = world?.gameLayer;
        if (global == null || global.Pointer != _global || global.prefs == null || global.prefs.Pointer != _prefs
            || campaign == null
            || global.InChallenge || global.currentChallenge != 0 || NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth
            || BiomeHolder.Inst == null || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex
            || game == null || world == null || level == null || layer == null || level.GroundCollider == null
            || World.GroundCollider == null || level.GroundCollider.Pointer != World.GroundCollider.Pointer
            || !MountIslandGemRewardState.TryPlan(game.currentLand, out _, out _)
            || (spawning && (game.state != Game.State.Playing || Time.timeScale <= 0f || IslandSaveData.isSavingGame))) return false;
        if (!TryReadDurableGuid(global, campaign, out string guid, out int slot)
            || slot != global.currentCampaign) return false;
        owner = new MountIslandGemOwner(global.Pointer, campaign.Pointer, game.Pointer, world.Pointer,
            level.Pointer, layer.Pointer, slot, game.currentLand, guid);
        return true;
    }

    private static bool TryPositions(out Vector3[] positions)
    {
        positions = new Vector3[4];
        var managers = Managers.Inst; var ground = managers.level.GroundCollider.bounds;
        float left = ground.min.x + 12f, right = ground.max.x - 12f;
        if (!float.IsFinite(left) || !float.IsFinite(right) || right - left < 28f) return false;
        float[] fractions = { .2f, .4f, .6f, .8f }; float[] offsets = { 0f, -4f, 4f, -8f, 8f, -12f, 12f };
        for (int index = 0; index < 4; index++)
        {
            bool found = false;
            foreach (float offset in offsets)
            {
                float x = left + (right - left) * fractions[index] + offset;
                if (x < left || x > right || !CoinCourierGround.TryStandable(x, out float y) || !float.IsFinite(y)) continue;
                bool overlap = false;
                for (int earlier = 0; earlier < index; earlier++) if (Math.Abs(x - positions[earlier].x) < 7f) overlap = true;
                var kingdom = managers.kingdom;
                if (kingdom == null || managers.payables == null || managers.payables.AllPayables == null) return false;
                if (Near(kingdom.playerOne, x, 5f) || Near(kingdom.playerTwo, x, 5f)) overlap = true;
                foreach (var portal in kingdom.AllPortals) if (Near(portal, x, 12f)) overlap = true;
                foreach (var payable in managers.payables.AllPayables) if (Near(payable, x, 6f)) overlap = true;
                if (overlap) continue;
                positions[index] = new Vector3(x, y, 0f); found = true; break;
            }
            if (!found) return false;
        }
        return true;
    }
    private static bool Near(Component component, float x, float distance)
        => component != null && Math.Abs(component.transform.position.x - x) < distance;
    private static string BoxName(MountIslandGemOwner owner, int index)
        => NamePrefix + owner.Guid + "/" + owner.Land + "/1/" + index;

    private static void CreateBatch(MountIslandGemOwner owner, Vector3[] positions)
    {
        var prefab = Resources.Load<GameObject>(PrefabPath);
        if (prefab == null || prefab.GetComponent<Chest>() == null || prefab.GetComponent<Persistent>() == null
            || prefab.GetComponent<Persistent>().path != PrefabPath) { Note("prefab-contract"); return; }
        var preparation = new GameObject("KEM_MountGems_Preparation");
        preparation.SetActive(false);
        var boxes = new Box[4];
        bool published = false;
        try
        {
            for (int index = 0; index < 4; index++)
            {
                var go = UnityEngine.Object.Instantiate(prefab, preparation.transform, false);
                var box = boxes[index] = new Box { Root = go, Name = BoxName(owner, index) };
                box.Chest = go.GetComponent<Chest>(); box.Persistent = go.GetComponent<Persistent>();
                if (box.Chest == null || box.Persistent == null) throw new InvalidOperationException("components");
                foreach (var collider in go.GetComponentsInChildren<Collider2D>(true))
                { box.Colliders.Add((collider, collider.enabled)); collider.enabled = false; }
                if (box.Colliders.Count == 0) throw new InvalidOperationException("no-collider");
                go.name = box.Name;
                box.Chest.currencyAmount = 7; box.Chest.currencyType = CurrencyType.Gems; box.Chest.isFixedAmount = true;
                go.transform.SetParent(Managers.Inst.world.gameLayer, false);
                go.transform.position = positions[index]; go.SetActive(true);
                var fixedTransform = go.GetComponent<FixedTransform>();
                if (fixedTransform == null) throw new InvalidOperationException("fixed-transform");
                fixedTransform.SetFixY(go.transform.parent.InverseTransformPoint(positions[index]).y);
                if (Math.Abs(go.transform.position.x - positions[index].x) > .02f
                    || Math.Abs(go.transform.position.y - positions[index].y) > .02f) throw new InvalidOperationException("ground-transform");
                box.Persistent.RecalcCache();
                if (!ValidateBox(box)) throw new InvalidOperationException("persistent-registration");
            }
            if (!TryOwner(true, out var current) || current != owner || Managers.Inst.game.currentLevelConfig.incomeMultiplier != 1f
                || !_state.RecordLive(owner)) throw new InvalidOperationException("owner-changed");
            Batches.Add((owner.Guid, owner.Land), new Batch { Owner = owner, SceneEpoch = _sceneEpoch, Boxes = boxes });
            published = true;
            foreach (var box in boxes) foreach (var collider in box.Colliders) collider.Collider.enabled = collider.Enabled;
        }
        catch (Exception error)
        {
            // No interaction was enabled. Remove native persistence before deferred Unity destruction.
            bool rollbackOk = true;
            foreach (var box in boxes)
            {
                if (box == null) continue;
                try { box.Persistent?.DontPersistInstance(false); box.Root?.SetActive(false); if (box.Root != null) UnityEngine.Object.Destroy(box.Root); }
                catch (Exception) { rollbackOk = false; }
            }
            if (published || !rollbackOk) { _spillUnsafe = true; _state.Freeze("batch-rollback-unknown"); }
            Note("create:" + error.GetType().Name);
        }
        finally { UnityEngine.Object.Destroy(preparation); }
    }
    private static bool ValidateBox(Box box)
    {
        if (box.Chest == null || box.Persistent == null || box.Chest.gameObject.name != box.Name
            || box.Chest.currencyAmount != 7 || !box.Chest.isFixedAmount || box.Chest.currencyType != CurrencyType.Gems
            || box.Persistent.path != PrefabPath || !box.Persistent.ShouldPersist()) return false;
        int registered = 0;
        foreach (var persistent in IslandSaveData.registeredPersistents) if (persistent != null && persistent.Pointer == box.Persistent.Pointer) registered++;
        return registered == 1;
    }

    private static void ObserveRestored(MountIslandGemOwner owner)
    {
        try { ObserveRestoredCore(owner); }
        catch (Exception error) { _captureUnknown = true; Note("restored-read:" + error.GetType().Name); }
    }
    private static void ObserveRestoredCore(MountIslandGemOwner owner)
    {
        var key = (owner.Guid, owner.Land);
        bool failed = RestorationFaults.TryGetValue(key, out long failedEpoch);
        if (failed && failedEpoch == _sceneEpoch) return;
        if (!failed && Batches.TryGetValue(key, out var existing) && existing.Owner == owner && existing.SceneEpoch == _sceneEpoch) return;
        if (!TryReadRawIsland(owner.Land, out var savedIsland)) { FailRestoration(key); return; }
        var saved = ReadOwnedRows(savedIsland);
        if (saved == null) { FailRestoration(key); return; }
        if (failed && RowProofs.TryGetValue(key, out var protectedRows) && !SameProtectedRows(protectedRows, saved))
        { FailRestoration(key); return; }
        // Preserve the unpaid saved rows before inspecting a possibly incomplete native scene.
        if (!failed) RowProofs[key] = JsonRows(saved);
        var savedNames = new HashSet<string>(StringComparer.Ordinal);
        var savedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in saved)
        {
            bool owned = false;
            for (int index = 0; index < 4; index++) if (row.Name == BoxName(owner, index)) owned = true;
            if (!owned || row.Path != PrefabPath || string.IsNullOrEmpty(row.Id)
                || !savedNames.Add(row.Name) || !savedIds.Add(row.Id)) { FailRestoration(key); return; }
        }
        var boxes = new List<Box>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chest in Managers.Inst.world.gameLayer.GetComponentsInChildren<Chest>(true))
        {
            if (chest == null) continue;
            bool owned = false;
            for (int index = 0; index < 4; index++) if (chest.gameObject.name == BoxName(owner, index)) owned = true;
            if (!owned) continue;
            var box = new Box { Root = chest.gameObject, Chest = chest, Persistent = chest.GetComponent<Persistent>(), Name = chest.gameObject.name };
            bool completed = TryCompletedSpill(owner, _sceneEpoch, box.Name, chest.Pointer, out _);
            if (!savedNames.Contains(box.Name) || !names.Add(box.Name)
                || (!completed && (!RestoreFixedY(box, owner) || !ValidateBox(box))))
            { FailRestoration(key); return; }
            boxes.Add(box);
        }
        foreach (var row in saved)
        {
            if (names.Contains(row.Name)) continue;
            if (!TryCompletedSpill(owner, _sceneEpoch, row.Name, IntPtr.Zero, out var spill))
            { FailRestoration(key); return; }
            boxes.Add(new Box { Chest = spill.ChestObject, Name = row.Name });
        }
        // Zero rows + zero boxes is an old completed batch; a missing nonzero raw row is not.
        Batches[key] = new Batch { Owner = owner, SceneEpoch = _sceneEpoch, Boxes = boxes.ToArray() };
        RowProofs[key] = JsonRows(saved);
        RestorationFaults.Remove(key);
    }
    private static bool SameProtectedRows(List<string> protectedRows, List<(string Id, string Name, string Path, string Json)> rows)
    {
        if (protectedRows.Count != rows.Count) return false;
        var remaining = new HashSet<string>(protectedRows, StringComparer.Ordinal);
        foreach (var row in rows) if (!remaining.Remove(row.Json)) return false;
        return remaining.Count == 0;
    }
    private static void FailRestoration((string Guid, int Land) key)
    { RestorationFaults[key] = _sceneEpoch; Note("restored-snapshot-incomplete"); }
    private static bool TryCompletedSpill(MountIslandGemOwner owner, long epoch, string name, IntPtr chest, out Spill completed)
    {
        completed = null;
        foreach (var spill in Spills.Values)
        {
            if (spill.Owner != owner || spill.SceneEpoch != epoch || spill.Name != name
                || chest != IntPtr.Zero && spill.Chest != chest || !spill.Guard.Complete || spill.Guard.Unknown) continue;
            if (completed != null) return false;
            completed = spill;
        }
        return completed != null;
    }

    private static bool TryReadRawIsland(int land, out IslandSaveData island)
    {
        island = null;
        var rows = CampaignSaveData.current?._islands;
        if (rows == null || land < 0 || land >= rows.Count || rows[land] == null) return false;
        var candidate = rows[land]; int matches = 0;
        foreach (var row in rows) if (row != null && row.Pointer == candidate.Pointer) matches++;
        if (matches != 1) return false;
        island = candidate; return true;
    }

    private static bool RestoreFixedY(Box box, MountIslandGemOwner owner)
    {
        // Native ObjectData restores localPosition but the prefab's FixedTransform.Start later
        // overwrites Y. Rebind that native instance field to the exact saved Y, even after Start.
        if (!TryReadRawIsland(owner.Land, out var island)) return false;
        if (island.objects == null || box.Root == null || box.Root.transform.parent == null) return false;
        var fixedTransform = box.Root.GetComponent<FixedTransform>();
        if (fixedTransform == null) return false;
        int matches = 0; float y = 0f;
        foreach (var row in island.objects)
            if (row != null && row.name == box.Name && row.prefabPath == PrefabPath)
            { matches++; y = row.localPosition.y; }
        if (matches != 1 || !float.IsFinite(y)) return false;
        fixedTransform.SetFixY(y);
        box.Persistent.RecalcCache();
        return Math.Abs(box.Root.transform.localPosition.y - y) <= .02f;
    }

    internal sealed class SaveScope
    {
        internal SaveScope Previous;
        internal Batch Batch;
        internal IslandSaveData Island;
        internal MountIslandGemRewardState.Capture Capture;
        internal bool Closed, RowsVerified;
        internal List<(string Id, string Name, string Path, string Json)> Rows;
    }

    internal static SaveScope BeginIslandSave(int campaign, int land, int challenge)
    {
        var scope = new SaveScope { Previous = _scope };
        _scope = scope;
        try
        {
            if (_state != null) ObserveCatalog();
            if (_state == null || !_state.Ready || !SourceMatches() || !TryOwner(false, out var owner) || campaign != owner.Slot || challenge != 0) return scope;
            int effectiveLand = land >= 0 ? land : CampaignSaveData.current.CurrentLand;
            if (effectiveLand != owner.Land) return scope;
            if (_state.StagedIssued(owner.Guid, owner.Land)) ObserveRestored(owner);
            if (RestorationFaults.ContainsKey((owner.Guid, owner.Land))) return scope;
            if ((!_state.LiveMatches(owner) && !_state.StagedIssued(owner.Guid, owner.Land))
                || !Batches.TryGetValue((owner.Guid, owner.Land), out var batch) || batch.Owner != owner) return scope;
            if (batch.SceneEpoch != _sceneEpoch) { CaptureFaults.Add((owner.Guid, owner.Land)); return scope; }
            if (!TryReadRawIsland(effectiveLand, out var island))
            { CaptureFaults.Add((owner.Guid, owner.Land)); PublishWriter(); return scope; }
            scope.Batch = batch;
            scope.Island = island; scope.Capture = _state.BeginCapture(owner, island.Pointer);
        }
        catch (Exception error) { _captureUnknown = true; Note("capture-begin:" + error.GetType().Name); }
        finally { PublishWriter(); }
        return scope;
    }
    internal static void ObserveMarker(IslandSaveData island, bool ran)
    {
        var scope = _scope;
        if (!ran || scope?.Capture == null || island == null) return;
        var saving = IslandSaveData.CurrentlySavingIsland;
        if (saving == null || saving.Pointer != island.Pointer || island.Pointer != scope.Island.Pointer
            || island.land != scope.Batch.Owner.Land) return;
        if (!scope.Capture.MarkerSeen)
        {
            try
            {
                // Native Save clears idsByObject/objectsByID after this marker, before returning.
                // Capture the exact ID/row proof here; finalizer must never depend on those cleared maps.
                scope.RowsVerified = NativeRowsComplete(scope.Batch, island);
                scope.Rows = ReadOwnedRows(island);
            }
            catch (Exception error) { scope.RowsVerified = false; Note("marker-rows:" + error.GetType().Name); }
        }
        _state.Marker(scope.Capture, island.Pointer);
    }
    internal static void EndIslandSave(SaveScope scope, bool normal)
    {
        if (scope == null || scope.Closed) return;
        scope.Closed = true;
        if (ReferenceEquals(scope, _scope)) _scope = scope.Previous;
        if (scope.Capture == null) return;
        try
        {
            bool complete = scope.RowsVerified && SameOwnedRows(scope.Rows, scope.Island);
            bool exact = TryOwner(false, out var current);
            bool staged = _state.EndCapture(scope.Capture, current, normal && exact, complete && !_spillUnsafe);
            var key = (scope.Batch.Owner.Guid, scope.Batch.Owner.Land);
            if (staged) { CaptureFaults.Remove(key); UncapturedSpills.Remove(key); RowProofs[key] = JsonRows(scope.Rows); }
            else CaptureFaults.Add(key);
            if (!staged) Note("capture-not-complete");
        }
        catch (Exception error) { _captureUnknown = true; Note("capture-end:" + error.GetType().Name); }
        finally { PublishWriter(); }
    }
    private static List<(string Id, string Name, string Path, string Json)> ReadOwnedRows(IslandSaveData island)
    {
        if (island?.objects == null) return null;
        var rows = new List<(string, string, string, string)>();
        foreach (var row in island.objects)
            if (row != null && row.name != null && row.name.StartsWith(NamePrefix, StringComparison.Ordinal))
                rows.Add((row.uniqueID, row.name, row.prefabPath, JsonUtility.ToJson(row, false)));
        return rows;
    }
    private static bool SameOwnedRows(List<(string Id, string Name, string Path, string Json)> frozen, IslandSaveData island)
    {
        var rows = ReadOwnedRows(island);
        if (frozen == null || rows == null || frozen.Count != rows.Count) return false;
        for (int index = 0; index < rows.Count; index++) if (rows[index] != frozen[index]) return false;
        return true;
    }
    private static bool NativeRowsComplete(Batch batch, IslandSaveData island)
    {
        if (batch?.Boxes == null || batch.SceneEpoch != _sceneEpoch || batch.Boxes.Length > 4 || island == null || island.objects == null) return false;
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var box in batch.Boxes)
        {
            bool completed = TryCompletedSpill(batch.Owner, batch.SceneEpoch, box.Name, box.Chest.Pointer, out _);
            if (completed)
            {
                foreach (var row in island.objects) if (row != null && row.name == box.Name) return false;
                continue;
            }
            if (!ValidateBox(box) || !IslandSaveData.idsByObject.TryGetValue(box.Persistent, out string id) || string.IsNullOrEmpty(id) || !expected.Add(id)) return false;
            int count = 0;
            foreach (var row in island.objects)
                if (row != null && row.uniqueID == id)
                { if (row.name != box.Name || row.prefabPath != PrefabPath) return false; count++; }
            if (count != 1) return false;
        }
        foreach (var row in island.objects)
            if (row != null && row.name != null && row.name.StartsWith(NamePrefix, StringComparison.Ordinal))
            {
                bool known = false;
                foreach (var box in batch.Boxes) if (box.Name == row.name) known = true;
                if (!known) return false;
            }
        return true;
    }

    internal sealed class PrepareScope { internal PrefsSaveData Prefs; internal string Raw; }
    internal static PrepareScope BeginPrepare(PrefsSaveData prefs)
    {
        try { ObserveCatalog(); return BeginPrepareCore(prefs); }
        catch { _prepareFault = true; throw; }
        finally { PublishWriter(); }
    }
    private static PrepareScope BeginPrepareCore(PrefsSaveData prefs)
    {
        if (_state == null || !_state.CanSerialize || !_state.HasStaged || prefs == null || prefs.Pointer != _prefs) return null;
        var global = GlobalSaveData.loaded;
        if (global == null || global.Pointer != _global || global.prefs == null || global.prefs.Pointer != prefs.Pointer) return null;
        bool found = prefs.contents.TryGetValue(MountIslandGemRewardState.Key, out string raw);
        if ((found ? raw : null) != _expectedRaw) throw new InvalidOperationException("Mount gems native Prefs source changed");
        string staged = _state.SerializeStaged();
        try
        {
            prefs.SetString(MountIslandGemRewardState.Key, staged);
            if (!prefs.contents.TryGetValue(MountIslandGemRewardState.Key, out string copied) || copied != staged)
                throw new InvalidOperationException("Mount gems native Prefs readback failed");
        }
        catch
        {
            try { if (found) prefs.SetString(MountIslandGemRewardState.Key, raw); else prefs.contents.Remove(MountIslandGemRewardState.Key); }
            catch (Exception) { Note("prepare-rollback-unknown"); }
            throw;
        }
        _expectedRaw = staged;
        return new PrepareScope { Prefs = prefs, Raw = staged };
    }
    internal static void EndPrepare(PrepareScope scope, bool normal)
    {
        if (normal) WakeTick();
        if (scope == null) return;
        try
        {
            if (!normal) throw new InvalidOperationException("Mount gems native Prefs preparation did not run normally");
            int count = 0;
            if (scope.Prefs.srzEntries != null)
                foreach (var row in scope.Prefs.srzEntries)
                    if (row != null && row.key == MountIslandGemRewardState.Key)
                    { if (row.val != scope.Raw) throw new InvalidOperationException("Mount gems native Prefs copy differs"); count++; }
            if (count != 1) throw new InvalidOperationException("Mount gems native Prefs copy missing or duplicated");
            _prepareFault = false;
        }
        catch { _prepareFault = true; throw; }
        finally { PublishWriter(); }
    }

    internal static bool CanWriteNativePayload(string folder, string filename, byte[] data, out string reason)
    {
        // The physical factory may run off the Unity thread. This boundary reads only this immutable
        // managed publication and its explicit arguments; it never consults native owners or prefs.
        reason = null;
        var snapshot = Volatile.Read(ref _writer);
        if (snapshot == null || filename != snapshot.File) return true;
        string path = NativePath(folder, filename);
        if (path == null) { reason = "gem-native-path-unknown"; return false; }
        if (path != snapshot.Path) return true;
        if (!snapshot.Safe) { reason = "gem-native-capture-unsafe"; return false; }
        return MountIslandGemNativePayload.Verify(data, snapshot.Expected, out reason);
    }

    internal static bool CanSave(GlobalSaveData global)
    {
        if (_state == null || global == null || global.Pointer != _global) return true;
        if (_spillUnsafe || _captureUnknown || CaptureFaults.Count != 0 || RestorationFaults.Count != 0 || UncapturedSpills.Count != 0) return false;
        foreach (var key in EconomicEffects) if (!_state.StagedIssued(key.Guid, key.Land)) return false;
        return true;
    }

    internal sealed class Spill
    {
        internal Chest._SpillCurrency_d__26 Iterator; // Retain native life; never key only by a reusable pointer.
        internal IntPtr Chest;
        internal Chest ChestObject;
        internal MountIslandGemOwner Owner;
        internal long SceneEpoch;
        internal string Name;
        internal int Life;
        internal MountIslandGemDrain Guard = new();
    }
    internal sealed class SpillCall { internal Spill Spill; internal int Initial; }
    internal static SpillCall BeginSpill(Chest._SpillCurrency_d__26 iterator)
    {
        try
        {
            if (iterator == null || iterator.__4__this == null) return null;
            var chest = iterator.__4__this;
            if (chest.gameObject.name == null || !chest.gameObject.name.StartsWith(NamePrefix, StringComparison.Ordinal)) return null;
            if (!TryOwner(false, out var owner)) return null;
            bool owned = false;
            for (int index = 0; index < 4; index++) if (chest.gameObject.name == BoxName(owner, index)) owned = true;
            var persistent = chest.GetComponent<Persistent>();
            if (!owned || persistent == null || persistent.path != PrefabPath || !chest.isFixedAmount || chest.currencyType != CurrencyType.Gems
                || !chest.transform.IsChildOf(Managers.Inst.world.gameLayer)
                || (!_state.LiveMatches(owner) && !_state.StagedIssued(owner.Guid, owner.Land))) return null;
            if (!Spills.TryGetValue(iterator.Pointer, out var spill))
            {
                if (Spills.Count >= 512) { _spillUnsafe = true; Note("iterator-bound"); return null; }
                spill = new Spill { Iterator = iterator, Chest = chest.Pointer, ChestObject = chest, Owner = owner,
                    SceneEpoch = _sceneEpoch, Name = chest.gameObject.name, Life = chest.GetInstanceID() };
                Spills.Add(iterator.Pointer, spill);
            }
            if (spill.Owner != owner || spill.SceneEpoch != _sceneEpoch || spill.Chest != chest.Pointer || spill.Life != chest.GetInstanceID()) { _spillUnsafe = true; spill.Guard.Fail(); return null; }
            if (iterator.__1__state == 0)
            { EconomicEffects.Add((owner.Guid, owner.Land)); UncapturedSpills.Add((owner.Guid, owner.Land)); PublishWriter(); }
            return new SpillCall { Spill = spill, Initial = iterator.__1__state };
        }
        catch (Exception error) { _spillUnsafe = true; Note("spill-begin:" + error.GetType().Name); return null; }
    }
    internal static void EndSpill(SpillCall call, ref bool result)
    {
        if (call?.Spill == null) return;
        var spill = call.Spill;
        result = spill.Guard.AfterMove(true, call.Initial, () => spill.Iterator.__1__state, () => spill.Iterator.MoveNext(), result);
        if (spill.Guard.Unknown) { _spillUnsafe = true; Note("spill-unknown"); }
        PublishWriter();
    }
    internal static void FailSpill(SpillCall call)
    { if (call?.Spill != null) { call.Spill.Guard.Fail(); _spillUnsafe = true; Note("spill-exception"); PublishWriter(); } }

    // Panel destruction only; existing native loot and Prefs are never deleted here.
    internal static void Clear()
    {
        _scene = null; _loadEpoch++; _sceneEpoch = 0;
        _tickPending = _lastEnabled = false; _nextTickAt = 0f;
        ResetBinding();
    }
    private static void ResetBinding()
    {
        _global = _prefs = IntPtr.Zero; _state = null; _expectedRaw = null; _guidCache = default; _scope = null;
        _spillUnsafe = _captureUnknown = _prepareFault = false; CaptureFaults.Clear(); RestorationFaults.Clear();
        _nativePath = _nativeFile = null; RowProofs.Clear();
        CatalogSlots.Clear(); UncapturedSpills.Clear(); _catalogKnown = false; _selectedSlot = -1; _selectedGuid = null; Volatile.Write(ref _writer, null);
        Attempted.Clear(); Inspected.Clear(); Batches.Clear(); EconomicEffects.Clear(); Spills.Clear(); Diagnostics.Clear();
    }
    private static void Note(string key)
    {
        if (Diagnostics.Count < 48 && Diagnostics.Add(key))
            KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[MountIslandGems] " + key + "; native acceptance remains unverified");
    }
}
#endif
