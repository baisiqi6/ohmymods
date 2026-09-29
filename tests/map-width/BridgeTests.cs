using System;
using System.Collections.Generic;
using KingdomEnhancedMod;

namespace MapWidthTests
{
    /// <summary>
    /// MapWidthScope / PatchWorld_Level 的 stub 桥接测试（真实资产形状由 static 取证约束，Stubs.cs
    /// 的 TerrainFactory 按 template-boundary.md 的真实组件组合搭样本）。这里验证适配器行为：
    /// 候选核准（含确切组件组合/持久化/池戳、身份歧义拒绝）、接缝合法、原列表保护、scope 生命周期、日志字段。
    /// </summary>
    internal static class BridgeTests
    {
        private const string ForestName = "Forest_Blocks";
        private const string ClearingSmallName = "Clearing Small_Blocks";
        private const string ClearingName = "Clearing_Blocks";
        private const string ClearingLargeName = "Clearing Large_Blocks";

        internal static void Run()
        {
            Console.WriteLine("bridge:");

            Case.Run("expansion preserves originals and appends only after terrain", () =>
            {
                TestLog.Clear();
                Fixture fixture = MakeFixture();
                Il2CppSystem.Collections.Generic.List<LevelBlock> list = fixture.Result;
                var level = new Level { _levelEdges = new IntRange { min = -66, max = 86 } };

                MapWidthScope.Open(true, 2f, out MapWidthScope.Frame frame);
                try
                {
                    MapWidthScope.TryApply(fixture.Layout, ref list);
                }
                finally
                {
                    MapWidthScope.Close(level, frame);
                }

                Check.NotSame(fixture.Result, list, "expanded list must be a new instance");
                int added = list.Count - fixture.Originals.Length;
                Check.True(added > 0, "must add padding blocks");
                AssertInsertionsLegal(list, fixture.Originals, fixture.CandidateBlocks);

                Check.Equal(152, Sum(list), "planned total width is 2x baseline 76");
                Check.Equal(76, fixture.Layout.TotalWidth(), "layout TotalWidth is untouched and stays W0");
                Check.Equal(fixture.Originals.Length, fixture.Layout.LayoutBlocks.Count, "layout bookkeeping count unchanged");

                Check.True(TestLog.InfoContains("gen baseline=76"), "baseline logged");
                Check.True(TestLog.InfoContains("layoutTotal=76"), "original layout width logged");
                Check.True(TestLog.InfoContains("plannedWidth=152"), "planned width logged");
                Check.True(TestLog.InfoContains("addedBlocks=" + added), "added block count logged");
                Check.True(TestLog.InfoContains("edges min=-66 max=86 width=152"), "native LevelEdges read-back logged");
                Check.Equal(0, TestLog.Warnings.Count, "no warnings on a supported island");
            });

            Case.Run("multiplier 1 leaves the list untouched and silent", () =>
            {
                TestLog.Clear();
                Fixture fixture = MakeFixture();
                Il2CppSystem.Collections.Generic.List<LevelBlock> list = fixture.Result;

                MapWidthScope.Open(true, 1f, out MapWidthScope.Frame frame);
                try
                {
                    MapWidthScope.TryApply(fixture.Layout, ref list);
                }
                finally
                {
                    MapWidthScope.Close(null, frame);
                }

                Check.Same(fixture.Result, list, "m=1 must not replace the list");
                Check.Equal(5, list.Count, "m=1 keeps count");
                Check.Equal(0, TestLog.Info.Count + TestLog.Warnings.Count, "m=1 must be silent");
            });

            Case.Run("disabled mod leaves the list untouched and silent", () =>
            {
                TestLog.Clear();
                Fixture fixture = MakeFixture();
                Il2CppSystem.Collections.Generic.List<LevelBlock> list = fixture.Result;

                MapWidthScope.Open(false, 3f, out MapWidthScope.Frame frame);
                try
                {
                    MapWidthScope.TryApply(fixture.Layout, ref list);
                }
                finally
                {
                    MapWidthScope.Close(null, frame);
                }

                Check.Same(fixture.Result, list, "disabled must not replace the list");
                Check.Equal(0, TestLog.Info.Count + TestLog.Warnings.Count, "disabled must be silent");
            });

            Case.Run("GetBlocks outside a generation scope is ignored", () =>
            {
                TestLog.Clear();
                Fixture fixture = MakeFixture();
                Il2CppSystem.Collections.Generic.List<LevelBlock> list = fixture.Result;
                MapWidthScope.TryApply(fixture.Layout, ref list);
                Check.Same(fixture.Result, list, "no scope must not replace the list");
                Check.Equal(0, TestLog.Info.Count + TestLog.Warnings.Count, "no scope must be silent");
            });

            Case.Run("one scope publishes at most once", () =>
            {
                TestLog.Clear();
                Fixture first = MakeFixture();
                Il2CppSystem.Collections.Generic.List<LevelBlock> firstList = first.Result;
                MapWidthScope.Open(true, 2f, out MapWidthScope.Frame frame);
                try
                {
                    MapWidthScope.TryApply(first.Layout, ref firstList);
                    Fixture second = MakeFixture();
                    Il2CppSystem.Collections.Generic.List<LevelBlock> secondList = second.Result;
                    MapWidthScope.TryApply(second.Layout, ref secondList);
                    Check.Same(second.Result, secondList, "second GetBlocks in the same scope must stay untouched");
                    Check.Equal(5, secondList.Count, "second list count unchanged");
                }
                finally
                {
                    MapWidthScope.Close(null, frame);
                }
            });

            Case.Run("nested scopes each apply with their own multiplier", () =>
            {
                TestLog.Clear();
                Fixture outer = MakeFixture();
                Fixture inner = MakeFixture();
                Il2CppSystem.Collections.Generic.List<LevelBlock> outerList = outer.Result;
                Il2CppSystem.Collections.Generic.List<LevelBlock> innerList = inner.Result;

                MapWidthScope.Open(true, 2f, out MapWidthScope.Frame outerFrame);
                MapWidthScope.Open(true, 3f, out MapWidthScope.Frame innerFrame);
                try
                {
                    MapWidthScope.TryApply(inner.Layout, ref innerList);
                    MapWidthScope.Close(null, innerFrame);
                    MapWidthScope.TryApply(outer.Layout, ref outerList);
                }
                finally
                {
                    MapWidthScope.Close(null, outerFrame);
                    MapWidthScope.Abort(innerFrame);
                }

                Check.Equal(228, Sum(innerList), "inner scope used m=3 (76 -> 228)");
                Check.Equal(152, Sum(outerList), "outer scope used m=2 (76 -> 152)");
                Check.True(TestLog.InfoContains("slider=3"), "inner multiplier logged");
                Check.True(TestLog.InfoContains("slider=2"), "outer multiplier logged");

                // 归还后新 scope 仍能工作。
                Fixture next = MakeFixture();
                Il2CppSystem.Collections.Generic.List<LevelBlock> nextList = next.Result;
                MapWidthScope.Open(true, 2f, out MapWidthScope.Frame nextFrame);
                try
                {
                    MapWidthScope.TryApply(next.Layout, ref nextList);
                }
                finally
                {
                    MapWidthScope.Close(null, nextFrame);
                }
                Check.True(nextList.Count > 5, "scope stack restored after nesting");
            });

            Case.Run("abort restores scope without publishing (helper level)", () =>
            {
                TestLog.Clear();
                Fixture fixture = MakeFixture();
                Il2CppSystem.Collections.Generic.List<LevelBlock> list = fixture.Result;

                MapWidthScope.Open(true, 2f, out MapWidthScope.Frame frame);
                MapWidthScope.Abort(frame);
                MapWidthScope.TryApply(fixture.Layout, ref list);
                Check.Same(fixture.Result, list, "aborted scope must not publish");

                Fixture second = MakeFixture();
                Il2CppSystem.Collections.Generic.List<LevelBlock> secondList = second.Result;
                MapWidthScope.Open(true, 2f, out MapWidthScope.Frame secondFrame);
                try
                {
                    MapWidthScope.TryApply(second.Layout, ref secondList);
                }
                finally
                {
                    MapWidthScope.Close(null, secondFrame);
                }
                Check.True(secondList.Count > 5, "a fresh scope still works after abort");
            });

            Case.Run("no approved templates: explicit unsupported warning", () =>
            {
                TestLog.Clear();
                var layout = new LevelLayout();
                layout.blocks.Add(TerrainFactory.Functional("Castle_Blocks", 20));
                var start = TerrainFactory.Functional("Start_Blocks", 20);
                var end = TerrainFactory.Functional("EndRight_Cliff_Blocks", 12);
                Il2CppSystem.Collections.Generic.List<LevelBlock> list = TerrainFactory.List(start, end);
                Il2CppSystem.Collections.Generic.List<LevelBlock> original = list;

                MapWidthScope.Open(true, 2f, out MapWidthScope.Frame frame);
                try
                {
                    MapWidthScope.TryApply(layout, ref list);
                }
                finally
                {
                    MapWidthScope.Close(null, frame);
                }

                Check.True(TestLog.WarningContains("unsupported reason=no-candidates"), "unsupported warning");
                Check.Same(original, list, "list instance kept");
                Check.Equal(2, list.Count, "no expansion without approved templates");
            });

            Case.Run("candidates but no legal seam: explicit unsupported warning", () =>
            {
                TestLog.Clear();
                Fixture fixture = MakeFixture();
                var start = TerrainFactory.Functional("Start_Blocks", 20);
                var end = TerrainFactory.Functional("EndRight_Cliff_Blocks", 20);
                Il2CppSystem.Collections.Generic.List<LevelBlock> list = TerrainFactory.List(start, end);

                MapWidthScope.Open(true, 2f, out MapWidthScope.Frame frame);
                try
                {
                    MapWidthScope.TryApply(fixture.Layout, ref list);
                }
                finally
                {
                    MapWidthScope.Close(null, frame);
                }

                Check.True(TestLog.WarningContains("unsupported reason=no-seam"), "no-seam warning");
                Check.Equal(2, list.Count, "no expansion without a legal seam");
                Check.Same(start, list[0], "original list untouched");
            });

            Case.Run("non-terrain content child rejects template", () =>
            {
                TestLog.Clear();
                LevelBlock polluted = TerrainFactory.Terrain(ClearingLargeName, 20, LevelBlockGroup.Clearing);
                var layer = polluted.transform.GetChild(0);
                TerrainFactory.Go("Prop", layer);
                AssertRejectedByCandidateVerification(polluted);
            });

            Case.Run("tile with extra functional component rejects template", () =>
            {
                TestLog.Clear();
                LevelBlock block = TerrainFactory.Terrain(ClearingSmallName, 12, LevelBlockGroup.Clearing);
                var tileObject = TerrainFactory.FirstTileObject(block);
                Check.True(tileObject != null, "fixture must contain a tile");
                tileObject.Attach(new UnityEngine.MonoBehaviour());
                AssertRejectedByCandidateVerification(block);
            });

            Case.Run("tile with grandchild rejects template", () =>
            {
                TestLog.Clear();
                LevelBlock block = TerrainFactory.Terrain(ClearingSmallName, 12, LevelBlockGroup.Clearing);
                var tileObject = TerrainFactory.FirstTileObject(block);
                Check.True(tileObject != null, "fixture must contain a tile");
                TerrainFactory.Go("Nested", tileObject.transform);
                AssertRejectedByCandidateVerification(block);
            });

            Case.Run("minimal tile-only object is not a valid template", () =>
            {
                TestLog.Clear();
                var root = TerrainFactory.Go(ClearingName);
                var block = new LevelBlock { Width = 12, groupOne = LevelBlockGroup.Clearing };
                root.Attach(block);
                var layer = TerrainFactory.Go("GameLayer", root.transform);
                var minimal = TerrainFactory.Go("Tile", layer.transform);
                minimal.Attach(new Tile { Left = 0f, Right = 12f });
                AssertRejectedByCandidateVerification(block);
            });

            Case.Run("noGround tile rejects template", () =>
            {
                TestLog.Clear();
                LevelBlock block = TerrainFactory.Terrain(ClearingName, 16, LevelBlockGroup.Clearing);
                TerrainFactory.FirstTileObject(block).GetComponent<Tile>().noGround = true;
                AssertRejectedByCandidateVerification(block);
            });

            Case.Run("center / camera marker / danger source are rejected", () =>
            {
                TestLog.Clear();
                LevelBlock center = TerrainFactory.Terrain(ForestName, 8, LevelBlockGroup.Forest);
                center.absoluteCenter = true;
                AssertRejectedByCandidateVerification(center);

                LevelBlock camera = TerrainFactory.Terrain(ForestName, 8, LevelBlockGroup.Forest);
                camera.cameraBlockMarker = TerrainFactory.Go("CameraBlockMarker");
                AssertRejectedByCandidateVerification(camera);

                LevelBlock danger = TerrainFactory.Terrain(ForestName, 8, LevelBlockGroup.Forest);
                danger.isDangerSource = true;
                AssertRejectedByCandidateVerification(danger);
            });

            Case.Run("group mismatch rejects template", () =>
            {
                TestLog.Clear();
                LevelBlock wrongGroup = TerrainFactory.Terrain(ForestName, 8, LevelBlockGroup.Clearing);
                AssertRejectedByCandidateVerification(wrongGroup);

                LevelBlock extraGroup = TerrainFactory.Terrain(ClearingName, 16, LevelBlockGroup.Clearing);
                extraGroup.groupTwo = LevelBlockGroup.Forest;
                AssertRejectedByCandidateVerification(extraGroup);
            });

            Case.Run("tile coverage gap rejects template", () =>
            {
                TestLog.Clear();
                LevelBlock block = TerrainFactory.Terrain(ClearingSmallName, 12, LevelBlockGroup.Clearing);
                TerrainFactory.MoveTile(TerrainFactory.FirstTileObject(block), -12f, 4f);
                AssertRejectedByCandidateVerification(block);
            });

            Case.Run("tile with wrong persistent path rejects template", () =>
            {
                TestLog.Clear();
                LevelBlock block = TerrainFactory.Terrain(ClearingName, 16, LevelBlockGroup.Clearing);
                TerrainFactory.FirstTileObject(block).GetComponent<Persistent>().path = "Prefabs/Environment/Other";
                AssertRejectedByCandidateVerification(block);
            });

            Case.Run("disabled persistent rejects template", () =>
            {
                TestLog.Clear();
                LevelBlock block = TerrainFactory.Terrain(ClearingName, 16, LevelBlockGroup.Clearing);
                TerrainFactory.FirstTileObject(block).GetComponent<Persistent>().persistObject = false;
                AssertRejectedByCandidateVerification(block);
            });

            Case.Run("grass with wrong pool rejects template", () =>
            {
                TestLog.Clear();
                LevelBlock block = TerrainFactory.Terrain(ClearingSmallName, 12, LevelBlockGroup.Clearing);
                TerrainFactory.GrassObjectOf(block).GetComponent<PoolStamper>().targetPool = PoolStamper.Pool.Ghost;
                AssertRejectedByCandidateVerification(block);
            });

            Case.Run("grass with wrong persistent path rejects template", () =>
            {
                TestLog.Clear();
                LevelBlock block = TerrainFactory.Terrain(ClearingSmallName, 12, LevelBlockGroup.Clearing);
                TerrainFactory.GrassObjectOf(block).GetComponent<Persistent>().path =
                    TerrainFactory.TileForestPath;
                AssertRejectedByCandidateVerification(block);
            });

            Case.Run("duplicate reference of one template is deduped, not ambiguous", () =>
            {
                TestLog.Clear();
                LevelBlock block = TerrainFactory.Terrain(ClearingSmallName, 12, LevelBlockGroup.Clearing);
                var layout = new LevelLayout();
                layout.blocks.Add(block);
                layout.blocks.Add(block);
                var start = TerrainFactory.Functional("Start_Blocks", 20);
                var tail = TerrainFactory.Functional("EndRight_Cliff_Blocks", 20);
                Il2CppSystem.Collections.Generic.List<LevelBlock> list = TerrainFactory.List(start, block, tail);

                MapWidthScope.Open(true, 2f, out MapWidthScope.Frame frame);
                try
                {
                    MapWidthScope.TryApply(layout, ref list);
                }
                finally
                {
                    MapWidthScope.Close(null, frame);
                }

                // W0=52，候选只有 12 宽 → 最近可表示 48（误差 4 ≤ 半块 6）。
                Check.Equal(100, Sum(list), "single deduped candidate still expands");
                Check.True(TestLog.InfoContains("candidates=1"), "duplicate reference counted once");
                Check.Equal(0, TestLog.Warnings.Count, "no ambiguity warning for the same reference");
            });

            Case.Run("same key with two distinct templates is rejected in either order", () =>
            {
                TestLog.Clear();
                var first = TerrainFactory.Terrain(ClearingName, 16, LevelBlockGroup.Clearing);
                var second = TerrainFactory.Terrain(ClearingName, 16, LevelBlockGroup.Clearing);
                AssertAmbiguousKeyRejected(first, second);
            });

            Case.Run("subtle differences do not pick a winner; key is rejected in either order", () =>
            {
                TestLog.Clear();
                var first = TerrainFactory.Terrain(ClearingName, 16, LevelBlockGroup.Clearing);
                var second = TerrainFactory.Terrain(ClearingName, 16, LevelBlockGroup.Clearing);
                // 0.0001 级别的 Grass Y 差异（真实模板允许 Y 差异）：不得被签名抹平后选赢家。
                TerrainFactory.GrassObjectOf(second).transform.localPosition =
                    new UnityEngine.Vector3(0f, 1.0001f, 0f);
                AssertAmbiguousKeyRejected(first, second);
            });

            Case.Run("insertions only at the single legal seam between functional blocks", () =>
            {
                TestLog.Clear();
                Fixture fixture = MakeFixture();
                var functionalA = TerrainFactory.Functional("Bridge_Blocks", 20);
                var functionalB = TerrainFactory.Functional("BeachRight_Blocks", 20);
                var start = TerrainFactory.Functional("Start_Blocks", 20);
                var end = TerrainFactory.Functional("EndRight_Cliff_Blocks", 10);
                Il2CppSystem.Collections.Generic.List<LevelBlock> list =
                    TerrainFactory.List(start, functionalA, functionalB, fixture.ClearingBlock, end);

                MapWidthScope.Open(true, 2f, out MapWidthScope.Frame frame);
                try
                {
                    MapWidthScope.TryApply(fixture.Layout, ref list);
                }
                finally
                {
                    MapWidthScope.Close(null, frame);
                }

                LevelBlock[] originals = { start, functionalA, functionalB, fixture.ClearingBlock, end };
                int inserted = AssertInsertionsLegal(list, originals, fixture.CandidateBlocks);
                int[] matched = MatchOriginals(list, originals);
                Check.Equal(3, matched[3], "terrain original keeps its slot");
                Check.Equal(inserted, matched[4] - matched[3] - 1,
                    "all padding lands directly after the only legal seam (between two functional blocks)");
            });

            Case.Run("fractional multiplier snaps in the bridge", () =>
            {
                TestLog.Clear();
                Fixture fixture = MakeFixture();
                Il2CppSystem.Collections.Generic.List<LevelBlock> list = fixture.Result;
                MapWidthScope.Open(true, 1.25f, out MapWidthScope.Frame frame);
                try
                {
                    MapWidthScope.TryApply(fixture.Layout, ref list);
                }
                finally
                {
                    MapWidthScope.Close(null, frame);
                }

                // W0=76，目标 95，需 +19：20（差1）优于 16（差3）。
                Check.Equal(96, Sum(list), "planned width 96");
                Check.True(TestLog.InfoContains("plannedWidth=96"), "planned width logged");
            });

            Case.Run("unrelated non-content-layer children are ignored like native CloneInto", () =>
            {
                TestLog.Clear();
                LevelBlock block = TerrainFactory.Terrain(ClearingSmallName, 12, LevelBlockGroup.Clearing);
                TerrainFactory.Go("DecoLayer", block.transform);
                var helper = TerrainFactory.Go("Helper", block.transform);
                helper.Attach(new UnityEngine.MonoBehaviour());

                var layout = new LevelLayout();
                layout.blocks.Add(block);
                var start = TerrainFactory.Functional("Start_Blocks", 20);
                var tail = TerrainFactory.Functional("EndRight_Cliff_Blocks", 20);
                Il2CppSystem.Collections.Generic.List<LevelBlock> list = TerrainFactory.List(start, block, tail);

                MapWidthScope.Open(true, 2f, out MapWidthScope.Frame frame);
                try
                {
                    MapWidthScope.TryApply(layout, ref list);
                }
                finally
                {
                    MapWidthScope.Close(null, frame);
                }

                // W0=52，候选只有 12 宽 → 最近可表示 48（误差 4 ≤ 半块 6）；非内容层不参与核验与复制。
                Check.Equal(100, Sum(list), "expansion used the template with ignored foreign layers");
                Check.True(TestLog.InfoContains("candidates=1"), "foreign layers ignored, template accepted");
            });
        }

        private static void AssertAmbiguousKeyRejected(LevelBlock first, LevelBlock second)
        {
            foreach (bool swap in new[] { false, true })
            {
                TestLog.Clear();
                var layout = new LevelLayout();
                layout.blocks.Add(swap ? second : first);
                layout.blocks.Add(swap ? first : second);
                var start = TerrainFactory.Functional("Start_Blocks", 20);
                Il2CppSystem.Collections.Generic.List<LevelBlock> list = TerrainFactory.List(start, start);

                MapWidthScope.Open(true, 2f, out MapWidthScope.Frame frame);
                try
                {
                    MapWidthScope.TryApply(layout, ref list);
                }
                finally
                {
                    MapWidthScope.Close(null, frame);
                }

                Check.True(TestLog.WarningContains("ambiguous duplicate template"), "ambiguity must be logged");
                Check.True(TestLog.WarningContains("unsupported reason=no-candidates"), "key dropped");
                Check.Equal(2, list.Count, "no expansion with an ambiguous key");
                VerifyNoTerrainInserted(list, first, second);
            }
        }

        private static void AssertRejectedByCandidateVerification(LevelBlock block)
        {
            var layout = new LevelLayout();
            layout.blocks.Add(block);
            var start = TerrainFactory.Functional("Start_Blocks", 20);
            Il2CppSystem.Collections.Generic.List<LevelBlock> list = TerrainFactory.List(start, start);

            MapWidthScope.Open(true, 2f, out MapWidthScope.Frame frame);
            try
            {
                MapWidthScope.TryApply(layout, ref list);
            }
            finally
            {
                MapWidthScope.Close(null, frame);
            }

            Check.True(TestLog.WarningContains("unsupported reason=no-candidates"),
                "template must be rejected (block=" + block.name + ")");
            VerifyNoTerrainInserted(list, block);
        }

        private static void VerifyNoTerrainInserted(Il2CppSystem.Collections.Generic.List<LevelBlock> list, params LevelBlock[] forbidden)
        {
            for (int i = 0; i < list.Count; i++)
            {
                for (int f = 0; f < forbidden.Length; f++)
                {
                    Check.False(ReferenceEquals(list[i], forbidden[f]), "rejected template must never be duplicated");
                }
            }
        }

        /// <summary>贪心匹配原块引用为子序列（同一模板引用可能既是原块又是追加块，无法按身份区分）。</summary>
        private static int[] MatchOriginals(Il2CppSystem.Collections.Generic.List<LevelBlock> list, LevelBlock[] originals)
        {
            var matched = new int[originals.Length];
            int cursor = 0;
            for (int k = 0; k < originals.Length; k++)
            {
                while (cursor < list.Count && !ReferenceEquals(list[cursor], originals[k])) cursor++;
                Check.True(cursor < list.Count, "original block missing from result: index " + k);
                matched[k] = cursor;
                cursor++;
            }
            return matched;
        }

        private static int AssertInsertionsLegal(Il2CppSystem.Collections.Generic.List<LevelBlock> list,
            LevelBlock[] originals, LevelBlock[] candidates)
        {
            int[] matched = MatchOriginals(list, originals);
            Check.Same(originals[0], list[0], "no padding before the first original block");
            Check.Same(originals[originals.Length - 1], list[list.Count - 1], "no padding after the last original block");

            int inserted = list.Count - originals.Length;
            Check.True(inserted > 0, "expected padding blocks");

            // 相邻匹配之间是插入段：只能是被核准的原生模板，且左邻必须已被核验为普通地块。
            int seenPadding = 0;
            for (int k = 0; k < originals.Length - 1; k++)
            {
                bool hasRun = matched[k + 1] > matched[k] + 1;
                for (int p = matched[k] + 1; p < matched[k + 1]; p++)
                {
                    Check.True(IsCandidate(candidates, list[p]), "padding must reuse an approved native template");
                    seenPadding++;
                }
                if (hasRun)
                {
                    Check.True(IsTerrainName(originals[k].name),
                        "padding only after verified normal terrain, left=" + originals[k].name);
                }
            }
            Check.Equal(inserted, seenPadding, "all padding lies between original blocks");

            // 功能块（唯一引用）不得被复制或重排：只允许出现一次。
            for (int k = 0; k < originals.Length; k++)
            {
                if (IsTerrainName(originals[k].name)) continue;
                int count = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    if (ReferenceEquals(list[i], originals[k])) count++;
                }
                Check.Equal(1, count, "functional block must appear exactly once: " + originals[k].name);
            }
            return inserted;
        }

        private static bool IsCandidate(LevelBlock[] candidates, LevelBlock block)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (ReferenceEquals(candidates[i], block)) return true;
            }
            return false;
        }

        private static bool IsTerrainName(string name)
        {
            return name == ForestName || name == ClearingSmallName || name == ClearingName || name == ClearingLargeName;
        }

        private static int Sum(Il2CppSystem.Collections.Generic.List<LevelBlock> list)
        {
            int total = 0;
            for (int i = 0; i < list.Count; i++) total += list[i].GetWidth();
            return total;
        }

        internal sealed class Fixture
        {
            internal LevelLayout Layout;
            internal LevelBlock ForestBlock;
            internal LevelBlock ClearingSmallBlock;
            internal LevelBlock ClearingBlock;
            internal LevelBlock ClearingLargeBlock;
            internal LevelBlock Start;
            internal LevelBlock End;
            internal LevelBlock[] Originals;
            internal LevelBlock[] CandidateBlocks;
            internal Il2CppSystem.Collections.Generic.List<LevelBlock> Result;
        }

        internal static Fixture MakeFixture()
        {
            var fixture = new Fixture
            {
                ForestBlock = TerrainFactory.Terrain(ForestName, 8, LevelBlockGroup.Forest),
                ClearingSmallBlock = TerrainFactory.Terrain(ClearingSmallName, 12, LevelBlockGroup.Clearing),
                ClearingBlock = TerrainFactory.Terrain(ClearingName, 16, LevelBlockGroup.Clearing),
                ClearingLargeBlock = TerrainFactory.Terrain(ClearingLargeName, 20, LevelBlockGroup.Clearing),
            };
            fixture.Layout = new LevelLayout();
            fixture.Layout.blocks.Add(TerrainFactory.Functional("Castle_Blocks", 20));
            fixture.Layout.blocks.Add(fixture.ForestBlock);
            fixture.Layout.blocks.Add(fixture.ClearingSmallBlock);
            fixture.Layout.blocks.Add(fixture.ClearingBlock);
            fixture.Layout.blocks.Add(fixture.ClearingLargeBlock);

            fixture.Start = TerrainFactory.Functional("Start_Blocks", 20);
            fixture.End = TerrainFactory.Functional("EndRight_Cliff_Blocks", 12);
            fixture.Originals = new[]
            {
                fixture.Start, fixture.ForestBlock, fixture.ClearingBlock, fixture.ClearingLargeBlock, fixture.End,
            };
            fixture.CandidateBlocks = new[]
            {
                fixture.ForestBlock, fixture.ClearingSmallBlock, fixture.ClearingBlock, fixture.ClearingLargeBlock,
            };
            fixture.Result = TerrainFactory.List(fixture.Originals);
            fixture.Layout.LayoutBlocks.AddRange(fixture.Originals);
            return fixture;
        }
    }
}
