using System;
using System.Collections.Generic;
using KingdomEnhancedMod;

namespace CrossWorldMountTests
{
    internal static class Check
    {
        internal static int Count;

        internal static void True(bool condition, string message)
        {
            Count++;
            if (!condition) throw new Exception(message);
        }

        internal static void False(bool condition, string message) => True(!condition, message);

        internal static void Equal<T>(T expected, T actual, string message)
        {
            Count++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception(message + " [expected=" + expected + " actual=" + actual + "]");
        }
    }

    internal static class Program
    {
        private static readonly CrossWorldMountDefinition Pig = CrossWorldMountCatalog.Definitions[0];
        private static readonly CrossWorldMountDefinition Bear =
            new CrossWorldMountDefinition("royalwoodlands.bear", 0, "prefabs/blockprefabs/steed cave_blocks",
                "Steed Cave_Blocks", 23, 35, "Bearcave", "Bearcave", "Prefabs/Steeds/Bear", 9);

        private static CrossWorldMountIslandState State(
            bool landValid = true, bool markerOnIsland = false, bool markerInCampaign = false,
            bool visited = false, bool receipt = false, double playedDays = 0d, int granted = 0)
            => new CrossWorldMountIslandState(landValid, markerOnIsland, markerInCampaign, visited, receipt, playedDays, granted);

        private static void GrantState()
        {
            var idle = new CrossWorldMountGrantState();
            Check.False(idle.Pending, "fresh state must not be pending");
            Check.Equal(CrossWorldMountGrantOutcome.None, idle.Complete(1234UL, out int noneLand), "idle complete");
            Check.Equal(-1, noneLand, "idle complete land");

            // 生成异常：只终止自己，绝不提交。
            var aborted = new CrossWorldMountGrantState();
            aborted.Begin(3);
            aborted.NotePlaced(5555UL);
            aborted.Abort();
            Check.False(aborted.Pending, "abort must clear pending");
            Check.Equal(CrossWorldMountGrantOutcome.None, aborted.Complete(5555UL, out _), "aborted grant must not commit");

            // 已登记但没观察到放置：不提交（不再用同名前缀扫描兜底）。
            var unproven = new CrossWorldMountGrantState();
            unproven.Begin(5);
            Check.Equal(CrossWorldMountGrantOutcome.AbortNoMarker, unproven.Complete(7777UL, out int unprovenLand),
                "unproven placement must not write marker");
            Check.Equal(5, unprovenLand, "unproven land returned");

            // 观察到的放置必须落在本次目标 Level：别的 Level 放了同实例不算证据。
            var otherLevel = new CrossWorldMountGrantState();
            otherLevel.Begin(7);
            otherLevel.NotePlaced(1111UL);
            Check.Equal(CrossWorldMountGrantOutcome.AbortNoMarker, otherLevel.Complete(2222UL, out _),
                "placement observed in another level must not commit");

            var sameLevel = new CrossWorldMountGrantState();
            sameLevel.Begin(7);
            sameLevel.NotePlaced(2222UL);
            Check.Equal(CrossWorldMountGrantOutcome.CommitMarker, sameLevel.Complete(2222UL, out int sameLand),
                "placement in target level must commit");
            Check.Equal(7, sameLand, "target land returned");

            var once = new CrossWorldMountGrantState();
            once.Begin(11);
            once.NotePlaced(9UL);
            once.Complete(9UL, out _);
            Check.Equal(CrossWorldMountGrantOutcome.None, once.Complete(9UL, out _), "grant is single-shot");

            var stray = new CrossWorldMountGrantState();
            stray.NotePlaced(1UL);
            Check.Equal(CrossWorldMountGrantOutcome.None, stray.Complete(1UL, out _), "note before begin ignored");

            var reused = new CrossWorldMountGrantState();
            reused.Begin(1);
            reused.NotePlaced(42UL);
            reused.Begin(2);
            Check.Equal(CrossWorldMountGrantOutcome.AbortNoMarker, reused.Complete(42UL, out int reusedLand),
                "begin resets placement observation");
            Check.Equal(2, reusedLand, "reset land");
        }

        private static void PolicyAndDefinitions()
        {
            // 两种定义走同一条判定/标记管线，没有任何类型专用分支。
            Check.True(CountDefinitionsWithSharedPipeline() >= 2, "catalog must expose at least two usable definitions");
            var types = new HashSet<int>();
            for (int i = 0; i < CrossWorldMountCatalog.Definitions.Length; i++)
                Check.True(types.Add(CrossWorldMountCatalog.Definitions[i].SteedTypeId),
                    "each SteedType registered once: " + CrossWorldMountCatalog.Definitions[i].SteedTypeId);
            Check.Equal(CrossWorldMountDecision.Skip, CrossWorldMountPolicy.Decide(State(landValid: false)),
                "invalid land must skip");
            Check.Equal(CrossWorldMountDecision.GrantAndInject, CrossWorldMountPolicy.Decide(State()),
                "clean untouched island must grant");
            Check.Equal(CrossWorldMountDecision.InjectOnly, CrossWorldMountPolicy.Decide(State(markerOnIsland: true)),
                "island marker must rebuild only");
            Check.Equal(CrossWorldMountDecision.InjectOnly, CrossWorldMountPolicy.Decide(State(receipt: true)),
                "native receipt must rebuild only");
            Check.Equal(CrossWorldMountDecision.Skip, CrossWorldMountPolicy.Decide(State(markerInCampaign: true)),
                "campaign gate");
            Check.Equal(CrossWorldMountDecision.Skip, CrossWorldMountPolicy.Decide(State(visited: true)), "visited gate");
            Check.Equal(CrossWorldMountDecision.Skip, CrossWorldMountPolicy.Decide(State(playedDays: 0.5d)), "played gate");
            Check.Equal(CrossWorldMountDecision.InjectOnly,
                CrossWorldMountPolicy.Decide(State(markerOnIsland: true, markerInCampaign: true, visited: true)),
                "marker outranks other gates");

            // 稳定分布：固定岛槽位表，与玩家访问顺序无关（纯 land→定义映射）。
            Check.True(CrossWorldMountPolicy.ShouldGrantDefinition(Pig, Pig.IslandSlot), "definition grants on its slot");
            Check.False(CrossWorldMountPolicy.ShouldGrantDefinition(Pig, Pig.IslandSlot + 1), "definition must not grant elsewhere");
            Check.False(CrossWorldMountPolicy.ShouldGrantDefinition(Bear, Pig.IslandSlot), "other definition must not leak to that slot");
            // 固定分布表：允许多条定义同岛，但槽位必须落在希腊岛索引范围内（2..6，见 REPORT 分布表）。
            var slots = new HashSet<int>();
            for (int i = 0; i < CrossWorldMountCatalog.Definitions.Length; i++)
            {
                int slot = CrossWorldMountCatalog.Definitions[i].IslandSlot;
                Check.True(slot >= 2 && slot <= 6, "island slot within Greek range: " + slot);
                slots.Add(slot);
            }
            Check.True(slots.Count >= 3, "definitions spread over several islands");
            for (int order = 0; order < 3; order++)
            {
                // 访问顺序不同（这里只改变遍历顺序）不影响任何一座岛的判定结果。
                for (int i = 0; i < CrossWorldMountCatalog.Definitions.Length; i++)
                {
                    CrossWorldMountDefinition d = CrossWorldMountCatalog.Definitions[(i + order) % CrossWorldMountCatalog.Definitions.Length];
                    Check.True(CrossWorldMountPolicy.ShouldGrantDefinition(d, d.IslandSlot), "slot grant is order independent");
                }
            }
            // 每个已登记定义都必须带完整原生证据字段（地块/设施/prefab/槽位）。
            for (int i = 0; i < CrossWorldMountCatalog.Definitions.Length; i++)
            {
                CrossWorldMountDefinition d = CrossWorldMountCatalog.Definitions[i];
                Check.True(!string.IsNullOrEmpty(d.FacilityName) && !string.IsNullOrEmpty(d.SteedPrefabPath)
                    && d.IslandSlot > 0, "definition must carry native evidence: " + d.Id);
            }
            Check.True(CrossWorldMountCatalog.Definitions.Length >= 12, "Norse + other-world batch registered");
            Check.Equal(16, CrossWorldMountCatalog.Definitions.Length, "registered definition count");

            // Wolf(13/17)：普通北欧第六岛获取块（group81 min1/max1 → WolfRock_norselands → 112476）；
            // 希腊原生表已有 type13/17 别的变体 → 变体覆盖（不写共享表，已授予 scope 内适配）。
            CrossWorldMountDefinition wolf = FindDefinition(13);
            Check.Equal("prefabs/blockprefabs/norselands/steed wolf_norselands_blocks", wolf.BlockResourcePath, "Wolf block path");
            Check.Equal("Steed Wolf_Norselands_Blocks", wolf.BlockObjectName, "Wolf block object");
            Check.Equal(81, wolf.GroupOne, "Wolf block group one");
            Check.Equal(35, wolf.GroupTwo, "Wolf block group two");
            Check.Equal("WolfRock_norselands", wolf.FacilityName, "Wolf facility");
            Check.Equal("Prefabs/Steeds/Wolf_norselands", wolf.SteedPrefabPath, "Wolf prefab path");
            Check.Equal(6, wolf.IslandSlot, "Wolf island slot");
            Check.True(wolf.VariantOverride, "Wolf uses the variant query override (Greek table holds its type13/17)");
            CheckAlias(wolf, 17, "Prefabs/Steeds/Wolf Norselands P2");

            // Kirin(6)：普通非季节 Sakura 块（BlossomTree 子设施 SteedSpawn95695，4宝石）；
            // 与 Unicorn 同一 type6 物种，希腊表已有 Unicorn 精确键 → 变体覆盖，单一份 type6 状态。
            CrossWorldMountDefinition kirin = FindDefinition(6);
            Check.Equal("prefabs/blockprefabs/steed sakura_blocks", kirin.BlockResourcePath, "Kirin block path");
            Check.Equal("Steed Sakura_Blocks", kirin.BlockObjectName, "Kirin block object");
            Check.Equal(24, kirin.GroupOne, "Kirin block group one");
            Check.Equal(35, kirin.GroupTwo, "Kirin block group two");
            Check.Equal("BlossomTree", kirin.FacilityName, "Kirin facility/receipt name");
            Check.Equal("Prefabs/Steeds/Kirin", kirin.SteedPrefabPath, "Kirin prefab path");
            Check.Equal(4, kirin.IslandSlot, "Kirin island slot");
            Check.True(kirin.VariantOverride, "Kirin uses the variant query override (shares type6 with Unicorn)");
            Check.Equal(0, kirin.Aliases.Length, "Kirin keeps the single native type6, no duplicate variant injected");

            // 变体覆盖只限这两条明确冲突的定义；其余登记继续走原有精确键路径。
            Check.False(FindDefinition(21).VariantOverride, "Gullinbursti stays a normal exact-key registration");
            Check.False(FindDefinition(3).VariantOverride, "Santa reindeer stays a normal exact-key registration");
            Check.False(FindDefinition(7).VariantOverride, "Grave warhorse stays a normal exact-key registration");

            // 契约 2：三条被审查指出不存在的路径必须改成原生 GetPrefabPath 字面量。
            Check.Equal("Prefabs/Steeds/Lizard", FindDefinition(2).SteedPrefabPath, "type2 uses native lizard path");
            Check.Equal("Prefabs/Steeds/Spookyhorse", FindDefinition(4).SteedPrefabPath, "type4 uses native spookyhorse path");
            Check.Equal("Prefabs/Steeds/Warhorse P1", FindDefinition(7).SteedPrefabPath, "type7 uses native warhorse p1 path");
            Check.Equal("Prefabs/Steeds/Reindeer", FindDefinition(3).SteedPrefabPath, "Santa uses native reindeer path");
            Check.Equal("Prefabs/Steeds/Rainbow Pony", FindDefinition(38).SteedPrefabPath, "Birthday uses native rainbow pony path");

            // 契约 4：Santa(3) 是用户 2026-10-02 授权的 Mod 新增 Greek 普通获取点（非“已证原生
            // 无条件活动入口”），整块登记、普通新授予（不猜 12 月/日期）；Birthday(38) 新授予复用
            // 原生 Anniversary id10 活动资格。两者原生谜题/价格链都留在块内。
            Check.Equal("prefabs/blockprefabs/santahouse_blocks", FindDefinition(3).BlockResourcePath, "Santa block path");
            Check.Equal("SantaHouse_Blocks", FindDefinition(3).BlockObjectName, "Santa block object");
            Check.Equal("Santa House", FindDefinition(3).ReceiptObjectName, "Santa receipt object");
            Check.Equal(0, FindDefinition(3).SeasonalChallengeId, "Santa stays a plain new grant (no invented date rule)");
            Check.Equal("prefabs/blockprefabs/seasonal/anniversary/steed birthday_blocks",
                FindDefinition(38).BlockResourcePath, "Birthday block path");
            Check.Equal("Steed Birthday_Blocks", FindDefinition(38).BlockObjectName, "Birthday block object");
            Check.Equal("Steed_BirthdayParty_Abandoned", FindDefinition(38).ReceiptObjectName, "Birthday receipt object");
            Check.Equal(10, FindDefinition(38).SeasonalChallengeId, "Birthday new grants use the Anniversary id10 gate");

            // 活动门 = 单个数据字段 + 共用 predicate：只有 Anniversary 一条非零。
            int seasonalGates = 0;
            for (int i = 0; i < CrossWorldMountCatalog.Definitions.Length; i++)
            {
                CrossWorldMountDefinition gate = CrossWorldMountCatalog.Definitions[i];
                if (gate.SeasonalChallengeId == 0) continue;
                seasonalGates++;
                Check.Equal(38, gate.SteedTypeId, "only the Anniversary definition carries a seasonal gate");
                Check.Equal(10, gate.SeasonalChallengeId, "Anniversary gate uses native challenge id 10");
            }
            Check.Equal(1, seasonalGates, "exactly one seasonal grant gate in the catalog");

            // 契约 3：只给证据支持的定义挂原生池集合；Lizard/Gullinbursti 不需要（希腊/particlePools 已含）。
            Check.Equal("norselands", FindDefinition(22).PoolCollection, "Sleipnir pools live in norselands");
            Check.Equal("norselands", FindDefinition(25).PoolCollection, "Kelpie pools live in norselands");
            Check.Equal("deadlands", FindDefinition(14).PoolCollection, "Beetle pools live in deadlands");
            Check.Equal("deadlands", FindDefinition(15).PoolCollection, "Golem pools live in deadlands");
            Check.Equal(null, FindDefinition(2).PoolCollection, "Lizard pool already exists in greece");
            Check.Equal(null, FindDefinition(21).PoolCollection, "Gullinbursti pool already exists in particlePools");

            // 契约 3：P2/进阶别名（原生保存可记录这些类型，读档查询必须解析）。
            CheckAlias(FindDefinition(7), 19, "Prefabs/Steeds/Warhorse P2");
            CheckAlias(FindDefinition(25), 27, "Prefabs/Steeds/Kelpie P2");
            CheckAlias(FindDefinition(23), 28, "Prefabs/Steeds/Reindeer_norselands P2");
            Check.Equal(0, FindDefinition(21).Aliases.Length, "no alias invented for Gullinbursti");

            // 定义 + 别名的 SteedType 全局唯一（否则查询注册会互相覆盖）。
            var allTypes = new HashSet<int>();
            for (int i = 0; i < CrossWorldMountCatalog.Definitions.Length; i++)
            {
                CrossWorldMountDefinition d = CrossWorldMountCatalog.Definitions[i];
                Check.True(allTypes.Add(d.SteedTypeId), "definition type unique: " + d.SteedTypeId);
                for (int a = 0; a < d.Aliases.Length; a++)
                    Check.True(allTypes.Add(d.Aliases[a].SteedTypeId),
                        "alias type unique: " + d.Aliases[a].SteedTypeId + " (" + d.Id + ")");
                Check.True(d.SteedPrefabPath.StartsWith("Prefabs/Steeds/", StringComparison.Ordinal),
                    "prefab path uses the native literal form: " + d.Id);
            }

            // 去重/幂等（原生数组里已有该值时不重复追加）。
            int[] both = CrossWorldMountPolicy.AppendMarker(
                CrossWorldMountPolicy.AppendMarker(new[] { 1 }, Pig.SteedTypeId), Bear.SteedTypeId);
            Check.Equal(3, both.Length, "two definitions append once each");
            Check.Equal(3, CrossWorldMountPolicy.AppendMarker(both, Pig.SteedTypeId).Length,
                "appending an existing definition is a no-op");
            Check.True(CrossWorldMountCatalog.ArrayContainsAny(new[] { Pig.SteedTypeId }), "catalog detects its own steed");
            Check.False(CrossWorldMountCatalog.ArrayContainsAny(new[] { 9 }), "catalog ignores arrays without registered types");
            Check.True(Pig.ReceiptMatches("Prefabs/Environment/MountAreas/BoarAltar_norselands"),
                "pig receipt matches native persistent path");
            Check.False(Pig.ReceiptMatches("Prefabs/Environment/MountAreas/OtherAltar"), "receipt must not match others");
            Check.True(Bear.ReceiptMatches("Island/Objects/BearCave"), "second definition receipt matches");
        }

        private static CrossWorldMountDefinition FindDefinition(int steedTypeId)
        {
            for (int i = 0; i < CrossWorldMountCatalog.Definitions.Length; i++)
            {
                if (CrossWorldMountCatalog.Definitions[i].SteedTypeId == steedTypeId)
                    return CrossWorldMountCatalog.Definitions[i];
            }
            throw new Exception("definition not found for type " + steedTypeId);
        }

        private static void CheckAlias(CrossWorldMountDefinition definition, int steedTypeId, string prefabPath)
        {
            for (int i = 0; i < definition.Aliases.Length; i++)
            {
                if (definition.Aliases[i].SteedTypeId != steedTypeId) continue;
                Check.Equal(prefabPath, definition.Aliases[i].PrefabPath, "alias path for " + definition.Id);
                return;
            }
            throw new Exception("alias " + steedTypeId + " missing on " + definition.Id);
        }

        private static int CountDefinitionsWithSharedPipeline()
        {
            // 两个定义都必须能被同一条管线消费（字段齐全、类型可解析），不依赖任何类型专用判断。
            var list = new List<CrossWorldMountDefinition>(CrossWorldMountCatalog.Definitions) { Bear };
            int usable = 0;
            for (int i = 0; i < list.Count; i++)
            {
                CrossWorldMountDefinition d = list[i];
                if (!string.IsNullOrEmpty(d.Id) && d.SteedTypeId >= 0
                    && !string.IsNullOrEmpty(d.BlockResourcePath)
                    && !string.IsNullOrEmpty(d.BlockObjectName)
                    && !string.IsNullOrEmpty(d.ReceiptObjectName)) usable++;
            }
            return usable;
        }

        private static void MapPlanAndLayout()
        {
            var entries = new List<MountMapEntry>();
            Check.True(MountMapPlan.TryBuild(new[] { 21, 0 }, new[] { 3 }, entries, out int extra),
                "two steeds + one hermit build");
            Check.Equal(0, extra, "native slot counts");
            Check.Equal(MountMapEntryKind.Steed, entries[0].Kind, "steeds first");
            Check.Equal(MountMapEntryKind.Hermit, entries[2].Kind, "hermits after steeds");
            Check.False(MountMapPlan.TryBuild(null, null, entries, out _), "null data must not build");

            var two = new List<MountMapEntry>();
            MountMapPlan.TryBuild(new[] { 21, 0 }, Array.Empty<int>(), two, out _);
            Check.False(MountMapPlan.NeedsExtendedList(two, (k, t) => true),
                "icon-complete native-sized list must not duplicate");
            Check.True(MountMapPlan.NeedsExtendedList(two, (k, t) => t != 21), "missing icon must show list");

            // 真实几何（map-layout.json 原值，面板 180×150、列表可用高 146）：
            // Quest Hephaestus 岛图 [0,0]..[196,110] + 原生动态 Steed1 [57,24]..[95,50]
            // → 只有岛图上方 2 行(32px)可用；其余行被岛图挡住。
            var hephaestus = new List<MountMapRect>
            {
                new MountMapRect(0f, 0f, 196f, 110f),
                new MountMapRect(57f, 24f, 95f, 50f),
            };
            Check.Equal(2, MountMapLayoutPlan.PickFreeRows(146f, 0f, 0f, 172f, hephaestus, 4),
                "real Hephaestus geometry leaves exactly two free rows above the art");
            Check.Equal(0, MountMapLayoutPlan.PickFreeRows(146f, 0f, 0f, 172f, hephaestus, 0),
                "maxRows=0 yields no rows");

            // God Athena 岛图较矮 -> 3 行可用；任一被占行不得返回。
            var athena = new List<MountMapRect> { new MountMapRect(14f, 29f, 98f, 91f) };
            Check.Equal(3, MountMapLayoutPlan.PickFreeRows(146f, 0f, 0f, 172f, athena, 4),
                "shorter art leaves three free rows");
            var blockedTop = new List<MountMapRect> { new MountMapRect(0f, 130f, 172f, 146f) };
            Check.Equal(0, MountMapLayoutPlan.PickFreeRows(146f, 0f, 0f, 172f, blockedTop, 4),
                "occupied top row blocks the band");

            // 全部条目可达：任意条目都落在某一页的某一行列内（无截断行、无硬上限）。
            for (int count = 1; count <= 40; count++)
            {
                int rows = count >= 8 ? 4 : 2;          // 与真实可用行带无关的容量遍历
                int columns = MountMapLayoutPlan.ColumnsFor(172f, 80f);
                Check.Equal(2, columns, "wide page uses two columns");
                int pages = MountMapLayoutPlan.PageCount(count, rows, columns);
                Check.True(pages * rows * columns >= count, "all entries reachable for " + count);
                var seen = new HashSet<int>();
                for (int page = 0; page < pages; page++)
                {
                    for (int slot = 0; slot < rows * columns; slot++)
                    {
                        int index = page * rows * columns + slot;
                        if (index < count) seen.Add(index);
                    }
                }
                Check.Equal(count, seen.Count, "every entry appears exactly once for " + count);
            }
            Check.Equal(0, MountMapLayoutPlan.PageCount(0, 4, 2), "empty list has no pages");
            Check.Equal(1, MountMapLayoutPlan.ColumnsFor(79f, 80f), "narrow page uses one column");
            Check.Equal(0, MountMapLayoutPlan.ColumnOf(5, 0), "zero columns is safe");
            Check.Equal(1, MountMapLayoutPlan.RowOf(3, 2), "row-major index");
        }

        private static int Main()
        {
            try
            {
                GrantState();
                PolicyAndDefinitions();
                MapPlanAndLayout();
                Console.WriteLine("PASS cross-world-mounts checks=" + Check.Count);
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine("FAIL " + e.Message);
                return 1;
            }
        }
    }
}
