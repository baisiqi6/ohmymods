using System;
using System.Collections.Generic;
using System.IO;
using KingdomEnhancedMod;

namespace KnightIdentityRuntimeTests
{
    internal static class ContextFollowupTests
    {
        internal static void Run()
        {
            foreach (bool nested in new[] { false, true })
            foreach (bool throws in new[] { false, true })
                FailedFreshLoad(nested, throws);

            Case.Run("pending_seed_cannot_write_through_unresolved_or_replaced_context", () =>
            {
                using var f = new Fixture();
                var unit = NativeSim.NewKnight(40501);
                var island = new IslandSaveData { land = 1, objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> { unit.ToRecord() } };
                NativeSim.RunLoad(island, (i, id) => unit);
                Check.True(KnightIdentityRuntime.TryResolve(unit.Knight, 2, 0, Styles.All, out _), "receipt ready");
                Check.Equal(1, KnightIdentityLoadSeed.PendingCount, "seed pending");
                KnightIdentityRuntime.ConfirmContext(true);
                KnightIdentityLoadSeed.Flush();
                Check.False(f.SidecarExists, "unresolved blocks existing pending seed");
                KnightIdentityContexts.RememberBinding(Sidecar.ContextKey(1), KnightIdentityArchive.NewScope(), false, true);
                KnightIdentityRuntime.ConfirmContext(false);
                KnightIdentityLoadSeed.Flush();
                Check.False(f.SidecarExists, "obsolete seed cannot overwrite replacement epoch");
                Check.Equal(0, KnightIdentityLoadSeed.PendingCount, "obsolete batch dropped");
            });

            Case.Run("generation_requires_matching_native_facts_and_world", () =>
            {
                using var f = new Fixture();
                var island = new IslandSaveData { land = 1, isNew = false };
                var campaign = new CampaignSaveData { CurrentIsland = island };
                CampaignSaveData.current = campaign;
                Check.True(KnightIdentityGeneration.Begin(campaign) == null, "existing island not a generation");
                island.isNew = true; island.playTimeDays = 1;
                Check.True(KnightIdentityGeneration.Begin(campaign) == null, "played island not a generation");
                island.playTimeDays = 0;
                var capture = KnightIdentityGeneration.Begin(campaign);
                Check.True(capture != null, "eligible generation captured before world change");
                NativeSim.ResetWorld(0x9999);
                KnightIdentityGeneration.End(campaign, capture, true);
                Check.False(f.SidecarExists, "world mismatch cannot confirm generation");
                Check.False(KnightIdentityGeneration.Active, "scope unwound");
                capture = KnightIdentityGeneration.Begin(campaign);
                Check.True(capture != null, "eligible generation captured before layer change");
                var oldLayer = Managers.Inst.world.gameLayer;
                Managers.Inst.world.gameLayer = new UnityEngine.Transform { Pointer = new IntPtr(0xAAAA), gameObject = oldLayer.gameObject };
                KnightIdentityGeneration.End(campaign, capture, true);
                Check.False(f.SidecarExists, "same World with replaced gameLayer cannot confirm generation");
                Managers.Inst.world.gameLayer = oldLayer;
                oldLayer.gameObject.activeInHierarchy = false;
                Check.True(KnightIdentityGeneration.Begin(campaign) == null, "inactive layer cannot begin generation");
            });

            Case.Run("failed_load_preserves_binding_and_rolls_back_only_its_receipts", () =>
            {
                using var f = new Fixture();
                var old = NativeSim.NewKnight(41001);
                Check.True(KnightIdentityRuntime.TryResolve(old.Knight, 3, 0, Styles.All, out _), "old resolved");
                KnightIdentityRuntime.TryGetReceipt(old.Knight, out var original);
                var island = new IslandSaveData { land = 2 };
                NativeSim.RunSave(island, 0, 2, 0, new List<KnightUnit> { old });
                string context = Sidecar.ContextKey(2);
                Check.True(KnightIdentityContexts.TryGetBinding(context, out string epoch, out _, out _), "prior binding");
                byte[] before = File.ReadAllBytes(f.SidecarPath);
                var attempted = NativeSim.NewKnight(41002);
                var scope = KnightIdentityLoadBridge.Begin(NativeSim.CloneIsland(island));
                KnightIdentityLoadBridge.HandleTryCreateOrFind(island.objects[0], attempted.Persistent);
                Check.True(KnightIdentityRuntime.TryGetReceipt(attempted.Knight, out _), "tentatively restored");
                KnightIdentityLoadBridge.End(new InvalidOperationException("native load failed"), scope);
                Check.False(KnightIdentityRuntime.TryGetReceipt(attempted.Knight, out _), "failed load receipt rolled back");
                Check.False(KnightIdentityRuntime.TryResolve(attempted.Knight, -1, 4, Styles.All, out _), "failed owner cannot acquire new receipt");
                Check.True(KnightIdentityRuntime.TryGetReceipt(old.Knight, out var kept) && kept.Equals(original), "preexisting identity retained");
                Check.True(KnightIdentityContexts.TryGetBinding(context, out string after, out bool unresolved, out _) && after == epoch && !unresolved, "binding untouched");
                Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "no failed load write");
                var changed = NativeSim.CloneIsland(island); changed.biome++;
                scope = KnightIdentityLoadBridge.Begin(changed);
                KnightIdentityLoadBridge.End(null, scope, false);
                Check.True(KnightIdentityContexts.TryGetBinding(context, out after, out unresolved, out _) && after == epoch && !unresolved, "false load does not overwrite binding with unresolved");
                NativeSim.RunSave(island, 0, 2, 0, new List<KnightUnit> { old });
                var saved = KnightIdentityArchiveStore.Load(f.SidecarPath);
                string hash = KnightIdentityFingerprint.Normalized(island.Json(), epoch);
                Check.True(saved.Archive.TryRestore(epoch, hash, island.objects[0].uniqueID, out var savedReceipt) && savedReceipt.Equals(original), "old world save keeps original identity and epoch");
            });

            Case.Run("successful_native_generation_starts_new_epoch_and_preserves_history", () =>
            {
                using var f = new Fixture();
                var old = NativeSim.NewKnight(42001);
                Check.True(KnightIdentityRuntime.TryResolve(old.Knight, 3, 0, Styles.All, out _), "old resolved");
                var oldIsland = new IslandSaveData { land = 2 };
                NativeSim.RunSave(oldIsland, 0, 2, 0, new List<KnightUnit> { old });
                string context = Sidecar.ContextKey(2);
                KnightIdentityContexts.TryGetBinding(context, out string oldEpoch, out _, out _);
                var virgin = new IslandSaveData { land = 2, isNew = true, playTimeDays = 0 };
                var campaign = new CampaignSaveData { CurrentIsland = virgin };
                CampaignSaveData.current = campaign;
                byte[] before = File.ReadAllBytes(f.SidecarPath);
                var failed = KnightIdentityGeneration.Begin(campaign);
                Check.True(failed != null && KnightIdentityGeneration.Active, "native generation captured");
                var recruit = NativeSim.NewKnight(42002);
                Check.False(KnightIdentityRuntime.TryResolve(recruit.Knight, 1, 0, Styles.All, out _), "no allocation while generating");
                KnightIdentityGeneration.End(campaign, failed, false);
                Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "failed generation leaves history");
                KnightIdentityContexts.TryGetBinding(context, out string failedEpoch, out _, out _);
                Check.Equal(oldEpoch, failedEpoch, "failed generation leaves binding");
                var success = KnightIdentityGeneration.Begin(campaign);
                KnightIdentityGeneration.End(campaign, success, true);
                var disk = KnightIdentityArchiveStore.Load(f.SidecarPath);
                Check.True(disk.Archive.TryGetContext(context, out var mapping), "new mapping persisted");
                Check.NotEqual(oldEpoch, mapping.Active, "opaque new epoch");
                Check.True(mapping.Owns(oldEpoch), "rollback history retained");
                Check.True(KnightIdentityRuntime.TryResolve(recruit.Knight, 1, 0, Styles.All, out _), "confirmed generation allocates once");
                byte[] confirmed = File.ReadAllBytes(f.SidecarPath);
                Check.True(KnightIdentityGeneration.Begin(campaign) == null, "repeat apply cannot create another epoch");
                Check.True(confirmed.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "repeat generation does not write");
                NativeSim.RunSave(virgin, 0, 2, 0, new List<KnightUnit> { recruit });
                KnightIdentityRuntime.TryGetReceipt(recruit.Knight, out var receipt);
                var restored = NativeSim.NewKnight(42003);
                NativeSim.RunLoad(NativeSim.CloneIsland(virgin), (i, id) => restored);
                Check.True(KnightIdentityRuntime.TryGetReceipt(restored.Knight, out var again) && again.Equals(receipt), "new generation save reload keeps style and GUID");
                var prior = NativeSim.NewKnight(42004);
                NativeSim.RunLoad(NativeSim.CloneIsland(oldIsland), (i, id) => prior);
                Check.True(KnightIdentityRuntime.TryGetReceipt(prior.Knight, out var rollback) && rollback.Style == 3, "old native rollback restores older epoch");
            });

            Case.Run("future_or_corrupt_load_stays_closed_after_load_ends", () =>
            {
                foreach (string data in new[] { "{\"schemaVersion\":3}", "broken" })
                {
                    using var f = new Fixture();
                    Directory.CreateDirectory(Path.GetDirectoryName(f.SidecarPath));
                    File.WriteAllText(f.SidecarPath, data);
                    var island = new IslandSaveData { land = 1, objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData>() };
                    NativeSim.RunLoad(island, (i, id) => null);
                    var unit = NativeSim.NewKnight(43001);
                    int callbacks = 0;
                    KnightIdentityRuntime.PrimeExisting(new[] { unit.Knight }, k => { callbacks++; return 2; });
                    Check.False(KnightIdentityRuntime.TryResolve(unit.Knight, 2, 3, Styles.All, out _), "read-only load cannot allocate");
                    Check.Equal(0, callbacks, "no migration calculation");
                    Check.Equal(data, File.ReadAllText(f.SidecarPath), "unreadable archive left untouched");
                }
            });
        }

        private static void FailedFreshLoad(bool nested, bool throws)
        {
            Case.Run("fresh_load_" + (nested ? "nested" : "single") + "_" + (throws ? "exception" : "false") + "_quarantines_all_new_owners", () =>
            {
                using var f = new Fixture();
                var original = NativeSim.NewKnight(44001);
                Check.True(KnightIdentityRuntime.TryResolve(original.Knight, 3, 0, Styles.All, out _), "old world identity exists");
                var oldIsland = new IslandSaveData { land = 9 };
                NativeSim.RunSave(oldIsland, 0, 9, 0, new List<KnightUnit> { original });
                KnightIdentityRuntime.TryGetReceipt(original.Knight, out var oldReceipt);
                KnightIdentityContexts.TryGetBinding(Sidecar.ContextKey(9), out var oldEpoch, out _, out _);
                byte[] before = File.ReadAllBytes(f.SidecarPath);
                var first = NativeSim.NewKnight(44002);
                var outerIsland = new IslandSaveData { land = 1, objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> { first.ToRecord() } };
                var outer = KnightIdentityLoadBridge.Begin(outerIsland);
                KnightIdentityRuntime.OnEnable(first.Knight);
                KnightIdentityLoadBridge.HandleTryCreateOrFind(outerIsland.objects[0], first.Persistent);
                var units = new List<KnightUnit> { first };
                if (nested)
                {
                    var second = NativeSim.NewKnight(44003);
                    var innerIsland = new IslandSaveData { land = 2, objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> { second.ToRecord() } };
                    var inner = KnightIdentityLoadBridge.Begin(innerIsland);
                    KnightIdentityRuntime.OnEnable(second.Knight);
                    KnightIdentityLoadBridge.HandleTryCreateOrFind(innerIsland.objects[0], second.Persistent);
                    KnightIdentityLoadBridge.End(null, inner, true);
                    Check.Equal(1, KnightIdentityLoadSeed.PendingCount, "inner mapped but parent not committed");
                    Check.False(KnightIdentityContexts.TryGetBinding(Sidecar.ContextKey(2), out _, out _, out _), "inner binding deferred to parent");
                    units.Add(second);
                }
                KnightIdentityLoadBridge.End(throws ? new InvalidOperationException("native failure") : null, outer, false);
                Check.Equal(0, KnightIdentityLoadSeed.PendingCount, "failed transaction leaves no pending descendants");
                int callbacks = 0;
                KnightIdentityRuntime.PrimeExisting(units.ConvertAll(x => x.Knight).ToArray(), k => { callbacks++; return 4; });
                foreach (var unit in units)
                {
                    Check.False(KnightIdentityRuntime.TryResolve(unit.Knight, 2, 4, Styles.All, out _), "failed fresh owner cannot mint GUID");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out _), "failed fresh owner has no style receipt");
                }
                Check.Equal(0, callbacks, "failed owners never reach legacy callback");
                KnightIdentityLoadSeed.Flush();
                NativeSim.RunSave(outerIsland, 0, 1, 0, units);
                Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "Prime Flush Save leave prior archive bytes untouched");
                Check.True(KnightIdentityRuntime.TryGetReceipt(original.Knight, out var kept) && kept.Equals(oldReceipt), "old world receipt untouched");
                Check.True(KnightIdentityContexts.TryGetBinding(Sidecar.ContextKey(9), out var epoch, out bool unresolved, out _) && epoch == oldEpoch && !unresolved, "old world binding untouched");
            });
        }
    }
}
