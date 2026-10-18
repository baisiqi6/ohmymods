using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ScopeAdapter
{
    internal static class Program
    {
        private static int failures;

        private static void Check(bool ok, string name)
        {
            if (ok) return;
            failures++;
            Console.WriteLine("FAIL: " + name);
        }

        private static void Setup()
        {
            Level.GetLayerCalls = 0;
            Level.LayerFactory = null;
            ExtensionIslandRuntime.Calls = 0;
            IslandSaveData.LandReads = 0;
            IslandSaveData.ThrowLand = false;
            CampaignSaveData.CurrentLandReads = 0;
            CampaignSaveData.ThrowCurrentLand = false;
            var island = new IslandSaveData { land = 11 };
            var landData = new Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData>();
            var islands = new Il2CppSystem.Collections.Generic.List<IslandSaveData>();
            for (int i = 0; i < 12; i++)
            {
                landData.Add(new CampaignSaveData.LandMapData());
                islands.Add(i == 11 ? island : new IslandSaveData { land = i });
            }
            var campaign = new CampaignSaveData
            {
                CurrentLand = 11,
                reign = 0,
                currentReign = new CampaignSaveData.ReignInfo { landData = landData },
                _islands = islands,
            };
            CampaignSaveData.current = campaign;
            var global = new GlobalSaveData();
            ExtensionIslandRuntime.GlobalFactory = () => global;
            ExtensionIslandRuntime.OwnerFactory = () => campaign;
            ExtensionIslandRuntime.SelectedFactory = () => 0;
            Managers.Inst = new Managers();
            BiomeHolder.Inst = new BiomeHolder { BiomeIndex = BiomeHolder.GreeceBiomeIndex };
        }

        private static bool NewRead(MountIslandRecoveryRuntime.ScopeReadPhase phase, out string reason,
            out MountIslandRecoveryScope scope)
        {
            return MountIslandRecoveryRuntime.TryReadScope(phase, 0, 11,
                CampaignSaveData.current._islands[11], out scope, out _, out _, out _, out reason);
        }

        private static int BodyStart(string source, string signature)
        {
            int i = source.IndexOf(signature, StringComparison.Ordinal);
            return i < 0 ? -1 : source.IndexOf('{', i);
        }

        private static int BodyEnd(string source, int braceAt)
        {
            int depth = 0;
            for (int i = braceAt; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private static string ReplaceFirst(string source, string from, string to)
        {
            int i = source.IndexOf(from, StringComparison.Ordinal);
            return i < 0 ? source : source.Substring(0, i) + to + source.Substring(i + from.Length);
        }

        private static List<int> Occurrences(string source, string needle)
        {
            var list = new List<int>();
            int at = 0;
            while ((at = source.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
            {
                list.Add(at);
                at += needle.Length;
            }
            return list;
        }

        private static List<string> CheckCallsites(string source)
        {
            var errors = new List<string>();
            int buildStart = BodyStart(source, "private static bool TryBuildContext(");
            int buildEnd = BodyEnd(source, buildStart);
            if (buildStart < 0 || buildEnd < 0) return new List<string> { "TryBuildContext not found" };
            var before = Occurrences(source, "TryReadScope(ScopeReadPhase.BeforeNativePop");
            if (before.Count != 2) errors.Add("Before callsite count " + before.Count);
            foreach (int at in before)
            {
                if (at < buildStart || at > buildEnd) errors.Add("Before callsite outside TryBuildContext");
                int pstart = Math.Max(0, at - 16);
                string prefix = source.Substring(pstart, at - pstart);
                if (!prefix.Contains("if (!")) errors.Add("Before callsite result not checked");
            }
            int postCalls = 0;
            foreach (int at in Occurrences(source, "TryReadScope("))
            {
                if (before.Contains(at)) continue;
                int semi = source.IndexOf(';', at);
                string stmt = semi < 0 ? source.Substring(at) : source.Substring(at, semi - at);
                if (stmt.Contains("ScopeReadPhase.BeforeNativePop"))
                    errors.Add("Before used outside the two checked Prefix callsites");
                if (at > buildEnd) postCalls++;
            }
            if (postCalls < 3) errors.Add("post/consume calls missing: " + postCalls);
            int defaults = Occurrences(source, "=> TryReadScope(ScopeReadPhase.AfterNativePop").Count;
            if (defaults != 2) errors.Add("default overloads not FULL: " + defaults);
            return errors;
        }

        private static void Main()
        {
            // 1. Old production RED on early cache.
            Setup();
            Level.LayerFactory = _ => throw new NullReferenceException("early cache");
            Check(!OldMountIslandRecoveryRuntime.OldTryReadScope(0, 11,
                CampaignSaveData.current._islands[11], out _, out _, out _, out _, out _),
                "old production rejects early cache (RED)");
            Check(Level.GetLayerCalls == 1, "old production calls GetLayer once");

            // 2. Default enum is FULL; invalid rejected before cache.
            Setup();
            Level.LayerFactory = _ => throw new NullReferenceException("cold cache");
            Check(!NewRead(default(MountIslandRecoveryRuntime.ScopeReadPhase), out _, out _),
                "default phase is FULL and rejects cold cache");
            Check(Level.GetLayerCalls == 1, "default phase accessed Full cache");
            Setup();
            Check(!NewRead((MountIslandRecoveryRuntime.ScopeReadPhase)99, out _, out _),
                "invalid phase rejects");
            Check(Level.GetLayerCalls == 0, "invalid phase rejects before cache");

            // 3. Two Before reads: cache 0, production SameLoad true.
            Setup();
            bool readOne = NewRead(MountIslandRecoveryRuntime.ScopeReadPhase.BeforeNativePop, out _, out var first);
            bool readTwo = NewRead(MountIslandRecoveryRuntime.ScopeReadPhase.BeforeNativePop, out _, out var second);
            Check(readOne && readTwo, "two Before reads succeed");
            Check(Level.GetLayerCalls == 0, "two Before reads cache calls 0");
            Check(MountIslandRecoveryScope.SameLoad(first, second), "two Before reads SameLoad");

            // 4. Fake original produces cache on same identity; FULL succeeds.
            Level.LayerFactory = _ => Managers.Inst.world.gameLayer.gameObject;
            Check(NewRead(MountIslandRecoveryRuntime.ScopeReadPhase.AfterNativePop, out _, out _),
                "FULL succeeds after producer");
            Check(Level.GetLayerCalls == 1, "FULL checks cache once");

            // 5. Full absent / foreign pointer reject.
            Setup();
            Level.LayerFactory = _ => null;
            Check(!NewRead(MountIslandRecoveryRuntime.ScopeReadPhase.AfterNativePop, out _, out _),
                "FULL null cache rejects");
            Setup();
            Level.LayerFactory = _ => new GameObject();
            Check(!NewRead(MountIslandRecoveryRuntime.ScopeReadPhase.AfterNativePop, out _, out _),
                "FULL foreign layer rejects");

            // 6. Getter counters / throws / short circuit.
            Setup();
            CampaignSaveData.current.CurrentLand = 9;
            Check(!NewRead(MountIslandRecoveryRuntime.ScopeReadPhase.BeforeNativePop, out _, out _),
                "CurrentLand mismatch rejects");
            Check(IslandSaveData.LandReads == 0, "mismatch skips island.land getter");
            Setup();
            Check(NewRead(MountIslandRecoveryRuntime.ScopeReadPhase.BeforeNativePop, out _, out _),
                "matched Before succeeds");
            // Production reads island.land in the guard and again for the captured scope; the
            // mismatch path above proves the right operand is skipped before that.
            Check(IslandSaveData.LandReads == 2, "matched reads island.land in guard+capture only");
            Setup();
            CampaignSaveData.ThrowCurrentLand = true;
            Check(!NewRead(MountIslandRecoveryRuntime.ScopeReadPhase.BeforeNativePop, out string currentReason, out _),
                "CurrentLand throw rejects");
            Check(IslandSaveData.LandReads == 0, "CurrentLand throw skips island.land");
            Check(currentReason.Contains("phase=BeforeNativePop"), "current throw phase attribution");
            Setup();
            IslandSaveData.ThrowLand = true;
            Check(!NewRead(MountIslandRecoveryRuntime.ScopeReadPhase.BeforeNativePop, out string islandReason, out _),
                "island.land throw rejects");
            Check(CampaignSaveData.CurrentLandReads == 1, "island throw after one current read");
            Check(islandReason.Contains("phase=BeforeNativePop") && islandReason.Contains("island boom"),
                "island throw attribution");

            // 7. Raw identity / scene / holder drift rejects; production SameLoad catches seed/level drift.
            Setup();
            Check(!MountIslandRecoveryRuntime.TryReadScope(
                MountIslandRecoveryRuntime.ScopeReadPhase.BeforeNativePop, 1, 11,
                CampaignSaveData.current._islands[11], out _, out _, out _, out _, out _),
                "selected slot drift rejects");
            Setup();
            Managers.Inst.level.gameObject.scene.handle = 2;
            Check(!NewRead(MountIslandRecoveryRuntime.ScopeReadPhase.BeforeNativePop, out _, out _),
                "scene drift rejects");
            Setup();
            Managers.Inst.world.gameLayer.gameObject.scene.handle = 3;
            Check(!NewRead(MountIslandRecoveryRuntime.ScopeReadPhase.BeforeNativePop, out _, out _),
                "game layer scene drift rejects");
            Setup();
            BiomeHolder.Inst.BiomeIndex = 9;
            Check(!NewRead(MountIslandRecoveryRuntime.ScopeReadPhase.BeforeNativePop, out _, out _),
                "holder drift rejects");
            Setup();
            NewRead(MountIslandRecoveryRuntime.ScopeReadPhase.BeforeNativePop, out _, out var a);
            Managers.Inst.level = new Level();
            Level.LayerFactory = _ => Managers.Inst.world.gameLayer.gameObject;
            NewRead(MountIslandRecoveryRuntime.ScopeReadPhase.AfterNativePop, out _, out var b);
            Check(!MountIslandRecoveryScope.SameLoad(a, b), "production SameLoad rejects Level drift");

            // 8. Structural callsite fence + mutations.
            string source = File.ReadAllText(SourceAdapterGenerated.ProductionSource);
            Check(SourceAdapterGenerated.ProductionSHA256
                == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(source))).ToLowerInvariant(),
                "generated source hash matches actual file");
            Check(CheckCallsites(source).Count == 0, "clean source callsite fence");
            string moved = ReplaceFirst(source,
                "if (!TryReadScope(ScopeReadPhase.BeforeNativePop, selected, land, island,",
                "if (!TryReadScope(ScopeReadPhase.AfterNativePop, selected, land, island,");
            moved = ReplaceFirst(moved, "TryReadScope(capture.Scope.Land, island,",
                "TryReadScope(ScopeReadPhase.BeforeNativePop, capture.Scope.Land, island,");
            Check(CheckCallsites(moved).Count > 0, "mutation: moved Before caller turns RED");
            string ignored = ReplaceFirst(source, "if (!TryReadScope(ScopeReadPhase.BeforeNativePop",
                "bool ignored = TryReadScope(ScopeReadPhase.BeforeNativePop");
            Check(CheckCallsites(ignored).Count > 0, "mutation: unchecked Before result turns RED");

            Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILURE(S)");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
