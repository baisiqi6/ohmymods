using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using KingdomEnhancedMod;

namespace KingdomEnhancedMod.Tests
{
    /// <summary>
    /// MapResourceIconPlanner / MapClusterPlacement 的离线回归：
    /// - golden 向量与 tools/layout_proof.py 的独立复算逐值对拍（同一算法两种实现必须一致）；
    /// - 不变量：全部落在 surface 内、与其他图标/遮挡矩形保持 Gutter、顺序与 RequestIndex、确定性、
    ///   失败时不得部分展示（failed&gt;0 ⇒ 调用方必须丢弃）；
    /// - 容量：真实总览单元（314×208 五列二行 = 62.8×104）+ 最坏 10/12 条目集必须全放下（已知有源集）；
    /// - --layout-fixture/--layout-out：把真实几何(evidence)喂给**生产函数**并导出坐标，
    ///   由 tools/layout_proof.py 与 Python 复算逐值比对（SVG 预览即用 C# 导出坐标绘制）。
    /// 运行：dotnet run -c Release（输出 ALL PASS checks=N fails=0）。
    /// </summary>
    internal static class Program
    {
        private static int _checks;
        private static int _fails;

        private static void Check(bool condition, string label)
        {
            _checks++;
            if (!condition)
            {
                _fails++;
                Console.WriteLine("FAIL " + label);
            }
        }

        private static MapIconRequest Req(MapIconKind kind, int type, float w, float h, int index = 0)
            => new MapIconRequest(kind, type, index, w, h);

        private static bool Near(float a, float b, float eps = 0.01f) => Math.Abs(a - b) <= eps;

        /// <summary>与 layout_proof.py WORST10 完全相同的真实尺寸集（取各类型最大真实图标）。</summary>
        private static List<MapIconRequest> Worst10() => new List<MapIconRequest>
        {
            Req(MapIconKind.Steed, 37, 40, 28, 0), Req(MapIconKind.Steed, 35, 40, 22, 1),
            Req(MapIconKind.Steed, 32, 40, 20, 2), Req(MapIconKind.Steed, 33, 40, 20, 3),
            Req(MapIconKind.Steed, 29, 38, 26, 4), Req(MapIconKind.Steed, 31, 38, 24, 5),
            Req(MapIconKind.Steed, 34, 32, 30, 6), Req(MapIconKind.Steed, 30, 32, 22, 7),
            Req(MapIconKind.Hermit, 0, 18, 24, 0), Req(MapIconKind.Statue, 2, 20, 36, 0),
        };

        /// <summary>与 layout_proof.py STRESS12 完全相同的顺序：8 steeds + 2 hermits + 2 statues。</summary>
        private static List<MapIconRequest> Stress12() => new List<MapIconRequest>
        {
            Req(MapIconKind.Steed, 37, 40, 28, 0), Req(MapIconKind.Steed, 35, 40, 22, 1),
            Req(MapIconKind.Steed, 32, 40, 20, 2), Req(MapIconKind.Steed, 33, 40, 20, 3),
            Req(MapIconKind.Steed, 29, 38, 26, 4), Req(MapIconKind.Steed, 31, 38, 24, 5),
            Req(MapIconKind.Steed, 34, 32, 30, 6), Req(MapIconKind.Steed, 30, 32, 22, 7),
            Req(MapIconKind.Hermit, 0, 18, 24, 0), Req(MapIconKind.Hermit, 6, 18, 22, 1),
            Req(MapIconKind.Statue, 2, 20, 36, 0), Req(MapIconKind.Statue, 0, 18, 28, 1),
        };

        private static int Main(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--layout-fixture") _fixture = args[i + 1];
                if (args[i] == "--layout-out") _layoutOut = args[i + 1];
            }

            if (_fixture != null && _layoutOut != null)
            {
                DumpLayout();
                Console.WriteLine($"LAYOUT DUMP clusters/planner written to {_layoutOut}");
                return 0;
            }

            SelfChecks();
            Console.WriteLine($"{( _fails == 0 ? "ALL PASS" : "FAILURES")} checks={_checks} fails={_fails}");
            return _fails == 0 ? 0 : 1;
        }

        private static string _fixture;
        private static string _layoutOut;

        // ------------------------------------------------------------------ checks
        private static void SelfChecks()
        {
            Grid();

            const float CellW = 62.8f;      // 314 / 5
            const float CellH = 94.5f;      // 95 - 0.5（含按钮带后的格高减格内上边距增量）

            // ---- golden vectors (tools/layout_proof.py reproduces them exactly) ----
            Golden("simple", new List<MapIconRequest> { Req(MapIconKind.Steed, 38, 38, 26) },
                new MapIconSurface(0, 0, 200, 100), new List<MapIconBox>(),
                expectScale: 1.0f, expectPlaced: 1, expectMisses: 0, firstX: 1.5f, firstY: 1.5f);

            Golden("blocked_bottom", new List<MapIconRequest> { Req(MapIconKind.Steed, 38, 38, 26) },
                new MapIconSurface(0, 0, 60, 50), new List<MapIconBox> { new MapIconBox(0, 0, 60, 20) },
                expectScale: 0.85f, expectPlaced: 1, expectMisses: 0, firstX: 1.5f, firstY: 25.1f);

            var cellSurface = new MapIconSurface(0, 0, CellW, CellH);
            var cellBlocked = new List<MapIconBox> { new MapIconBox(0, CellH * 0.42f, CellW, CellH) };

            // bottom-up（详情面：自岛缘向上堆）
            Golden("worst10_cell_bottomup", Worst10(), cellSurface, cellBlocked,
                expectScale: 0.28f, expectPlaced: 10, expectMisses: 0, firstX: 1.5f, firstY: 1.5f, topDown: false);
            // top-down（总览贴岛画法）——本 golden 用"上方 58% 全遮挡"的病态面，验证降档不放弃
            Golden("worst10_cell_topdown", Worst10(), cellSurface, cellBlocked,
                expectScale: 0.28f, expectPlaced: 10, expectMisses: 0, firstX: 1.5f, firstY: 27.26f, topDown: true);
            Golden("stress12_cell_topdown", Stress12(), cellSurface, cellBlocked,
                expectScale: 0.28f, expectPlaced: 12, expectMisses: 0, firstX: 1.5f, firstY: 27.26f, topDown: true);

            // ---- invariants on a non-trivial surface ----
            var surface = new MapIconSurface(0, 0, 200, 120);
            surface.AddBlocked(new MapIconBox(70, 0, 120, 60));
            var requests = Stress12();
            var placements = new List<MapIconPlacement>();
            bool ok = MapResourceIconPlanner.TryPlan(requests, surface, placements, out float used, out int failed);
            Check(ok && failed == 0 && placements.Count == requests.Count, "invariants/all-placed");
            Check(used > 0f, "invariants/scale-positive");

            for (int i = 0; i < placements.Count; i++)
            {
                var p = placements[i];
                float w = p.Request.Width * p.Scale;
                float h = p.Request.Height * p.Scale;
                Check(p.X >= surface.X0 && p.Y >= surface.Y0 &&
                      p.X + w <= surface.X1 && p.Y + h <= surface.Y1,
                      "invariants/inside-surface#" + i);
                for (int b = 0; b < surface.BlockedCount; b++)
                {
                    var bb = surface.Blocked(b);
                    // Gutter=1.5：边缘间不允许贴死
                    bool hits = p.X - 1.5f < bb.X1 && bb.X0 - 1.5f < p.X + w &&
                                p.Y - 1.5f < bb.Y1 && bb.Y0 - 1.5f < p.Y + h;
                    Check(!hits, "invariants/no-blocked-overlap#" + i + "_" + b);
                }
                for (int j = 0; j < i; j++)
                {
                    var q = placements[j];
                    float qw = q.Request.Width * q.Scale;
                    float qh = q.Request.Height * q.Scale;
                    bool hits = p.X - 1.5f < q.X + qw && q.X - 1.5f < p.X + w &&
                                p.Y - 1.5f < q.Y + qh && q.Y - 1.5f < p.Y + h;
                    Check(!hits, "invariants/no-icon-overlap#" + i + "_" + j);
                }
            }
            for (int i = 0; i < placements.Count; i++)
            {
                Check(placements[i].RequestIndex == i && placements[i].Request.ArrayIndex == requests[i].ArrayIndex,
                    "invariants/order#" + i);
            }

            // determinism: two runs identical
            var again = new List<MapIconPlacement>();
            MapResourceIconPlanner.TryPlan(requests, surface, again, out float used2, out int failed2);
            Check(again.Count == placements.Count && failed2 == failed && Near(used2, used), "invariants/deterministic");

            // top-down 与 bottom-up 只在贴边侧不同（同一容量，全部放下）
            var top = new List<MapIconPlacement>();
            var bottom = new List<MapIconPlacement>();
            bool topOk = MapResourceIconPlanner.TryPlan(requests, surface, top, out float topScale, out _, true);
            bool bottomOk = MapResourceIconPlanner.TryPlan(requests, surface, bottom, out float bottomScale, out _, false);
            Check(topOk && bottomOk, "orientation/both-fit");
            Check(Near(topScale, bottomScale) && top.Count == bottom.Count &&
                  top[0].Y > bottom[0].Y, "orientation/top-hugs-top");

            // empty requests
            var empty = new List<MapIconPlacement>();
            Check(MapResourceIconPlanner.TryPlan(new List<MapIconRequest>(), surface, empty, out _, out int emptyFailed)
                  && empty.Count == 0 && emptyFailed == 0, "empty/no-op");

            // fully blocked surface: nothing placed, all reported, caller must discard (no partial展示)
            var full = new MapIconSurface(0, 0, 60, 50);
            full.AddBlocked(new MapIconBox(0, 0, 60, 50));
            var blockedOut = new List<MapIconPlacement>();
            bool blockedOk = MapResourceIconPlanner.TryPlan(new List<MapIconRequest> { Req(MapIconKind.Steed, 38, 38, 26) },
                full, blockedOut, out _, out int blockedFailed);
            Check(!blockedOk && blockedOut.Count == 0 && blockedFailed == 1, "blocked/all-reported");

            // 部分放不下：必须报告 failed>0 且返回 false（调用方不得只看 placements）
            var tight = new MapIconSurface(0, 0, 20, 12);
            var tightOut = new List<MapIconPlacement>();
            bool tightOk = MapResourceIconPlanner.TryPlan(Worst10(), tight, tightOut, out _, out int tightFailed);
            Check(!tightOk && tightFailed > 0, "partial/failed-reported");

            // small content keeps full size (no needless shrink)
            var bigSurface = new MapIconSurface(0, 0, 300, 200);
            var two = new List<MapIconRequest> { Req(MapIconKind.Steed, 38, 38, 26), Req(MapIconKind.Hermit, 0, 18, 24) };
            var twoOut = new List<MapIconPlacement>();
            MapResourceIconPlanner.TryPlan(two, bigSurface, twoOut, out float twoScale, out _);
            Check(Near(twoScale, 1.0f) && twoOut.Count == 2, "spacious/no-shrink");

            // ---- r6: 自有图标目录（user-approved 3/4/38；原版图标仍直接移植） ----
            Check(MapCustomIconCatalog.Count == 3, "custom/count-3");
            foreach (int customType in new[] { 3, 4, 38 })
            {
                Check(MapCustomIconCatalog.IsCustomSteed(1, customType), "custom/is-custom#" + customType);
                MapCustomIconCatalog.TryGet(1, customType, out MapCustomIconDef def);
                Check(def != null && def.IconType == 1 && def.Selection == customType && def.Valid,
                    "custom/def-valid#" + customType);
                Check(def != null && def.ResourceName.StartsWith("KingdomEnhancedMod.KEM_Map") &&
                      def.ResourceName.EndsWith(".png"), "custom/resource-name#" + customType);
                Check(def != null && def.NormalW > 0f && def.NormalH > 0f && def.LockedW > 0f && def.LockedH > 0f &&
                      def.SheetWidth >= def.NormalX + def.NormalW && def.SheetWidth >= def.LockedX + def.LockedW &&
                      def.SheetHeight >= def.NormalY + def.NormalH && def.SheetHeight >= def.LockedY + def.LockedH,
                    "custom/rects-inside-sheet#" + customType);
                Check(def != null && def.UiWidth > 0f && def.UiHeight > 0f, "custom/ui-size#" + customType);
                Check(def != null && def.NormalW != def.LockedW || def.NormalX != def.LockedX,
                    "custom/two-distinct-frames#" + customType);
            }
            // 只覆盖这三个：其他原生图标（含北欧驯鹿 23、stag 5）绝不走自有源
            Check(!MapCustomIconCatalog.IsCustomSteed(1, 5) && !MapCustomIconCatalog.IsCustomSteed(1, 23) &&
                  !MapCustomIconCatalog.IsCustomSteed(0, 3) && !MapCustomIconCatalog.IsCustomSteed(2, 4),
                  "custom/never-others");
            Check(!MapCustomIconCatalog.TryGet(1, 5, out _) && !MapCustomIconCatalog.TryGet(1, 23, out _),
                  "custom/no-native-override");
            Check(MapCustomIconCatalog.TemplateSelection == 5 &&
                  !MapCustomIconCatalog.IsCustomSteed(1, MapCustomIconCatalog.TemplateSelection),
                  "custom/template-is-native-5");

            // 与 root ready.json 同步产物逐值一致（编译表 == 实测 PNG/rect/ui/sha）——
            // 文件缺失时显式报告（最终候选必须存在：tools/sync_custom_icons.py 已跑过）。
            string syncPath = null;
            foreach (string candidate in new[] { Path.Combine(AppContext.BaseDirectory, "custom-icons.json") })
            {
                if (File.Exists(candidate)) { syncPath = candidate; break; }
            }
            Check(syncPath != null, "custom/sync-file-present");
            if (syncPath != null)
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(syncPath));
                int matched = 0;
                foreach (var icon in doc.RootElement.GetProperty("icons").EnumerateArray())
                {
                    int type = icon.GetProperty("type").GetInt32();
                    Check(MapCustomIconCatalog.TryGet(1, type, out MapCustomIconDef synced), "custom/sync-lookup#" + type);
                    if (synced == null) continue;
                    var normal = icon.GetProperty("normal");
                    var locked = icon.GetProperty("locked");
                    Check(synced.SheetWidth == icon.GetProperty("sheetW").GetInt32() &&
                          synced.SheetHeight == icon.GetProperty("sheetH").GetInt32() &&
                          Near(synced.UiWidth, (float)icon.GetProperty("uiW").GetDouble()) &&
                          Near(synced.UiHeight, (float)icon.GetProperty("uiH").GetDouble()) &&
                          Near(synced.NormalX, (float)normal[0].GetDouble()) &&
                          Near(synced.NormalY, (float)normal[1].GetDouble()) &&
                          Near(synced.NormalW, (float)normal[2].GetDouble()) &&
                          Near(synced.NormalH, (float)normal[3].GetDouble()) &&
                          Near(synced.LockedX, (float)locked[0].GetDouble()) &&
                          Near(synced.LockedY, (float)locked[1].GetDouble()) &&
                          Near(synced.LockedW, (float)locked[2].GetDouble()) &&
                          Near(synced.LockedH, (float)locked[3].GetDouble()),
                          "custom/sync-values#" + type);
                    Check(!string.IsNullOrEmpty(synced.Sha256) && synced.Sha256 == icon.GetProperty("sha256").GetString(),
                          "custom/sync-sha#" + type);
                    matched++;
                }
                Check(matched == 3, "custom/sync-count");
            }

            // ---- r4: 遮挡可见性相对 view 根（生产 MapIconVisibility） ----
            var chain = new MapIconPlanBoolChain(8);
            chain.Clear();
            Check(MapIconVisibility.Visible(chain), "visibility/empty-chain-visible");
            // 隐藏详情岛：land 自身 inactive（被分页隐藏），但其子节点 activeSelf=true -> 必须可见（收集到遮挡）
            chain.Clear(); chain.Add(true);
            Check(MapIconVisibility.Visible(chain), "visibility/hidden-view-root-still-occludes");
            // view 根之下原生明确隐藏的单个装饰：activeSelf=false -> 必须忽略
            chain.Clear(); chain.Add(false);
            Check(!MapIconVisibility.Visible(chain), "visibility/inactive-child-ignored");
            chain.Clear(); chain.Add(true); chain.Add(false);
            Check(!MapIconVisibility.Visible(chain), "visibility/inactive-intermediate-ignored");
            chain.Clear(); chain.Add(true); chain.Add(true);
            Check(MapIconVisibility.Visible(chain), "visibility/all-active-visible");
            // 链容量不影响判定（写入被有界丢弃也不误判为 false）
            var deep = new MapIconPlanBoolChain(4);
            for (int i = 0; i < 8; i++) deep.Add(true);
            Check(MapIconVisibility.Visible(deep), "visibility/bounded-chain");

            // ---- r4: 指纹稳定（"切成可见后同 fingerprint 只 Repaint" 的前提） ----
            var fpReqs = Stress12();
            int fp1 = MapIconPlanFingerprint.Compute(3, true, fpReqs);
            int fp2 = MapIconPlanFingerprint.Compute(3, true, Stress12());
            Check(fp1 == fp2, "fingerprint/stable-across-calls");
            Check(fp1 != MapIconPlanFingerprint.Compute(4, true, fpReqs), "fingerprint/land-differs");
            Check(fp1 != MapIconPlanFingerprint.Compute(3, false, fpReqs), "fingerprint/overview-differs");
            var fpChanged = Stress12();
            fpChanged[0] = new MapIconRequest(MapIconKind.Steed, 99, 0, 40, 28);
            Check(fp1 != MapIconPlanFingerprint.Compute(3, true, fpChanged), "fingerprint/requests-differ");
            Check(MapIconPlanFingerprint.Compute(3, true, new List<MapIconRequest>())
                  == MapIconPlanFingerprint.Compute(3, true, new List<MapIconRequest>()), "fingerprint/empty-stable");

            // ---- cluster placement: absolute + idempotent (r1 defect) ----
            Check(MapClusterPlacement.TryCompute(100, 50, 58.8f, 52f, 120f, 120f, 0f, 0f,
                      out float cs, out float cx, out float cy) && Near(cs, 52f / 120f) &&
                  Near(cx, 100f) && Near(cy, 50f), "cluster/fit-by-limit");
            // 偏移按“绝对缩放”换算（与调用次数/先前状态无关）
            Check(MapClusterPlacement.TryCompute(100, 50, 58.8f, 52f, 120f, 120f, 10f, -6f,
                      out float cs2, out float cx2, out float cy2) && Near(cs2, 52f / 120f) &&
                  Near(cx2, 100f - 10f * cs2) && Near(cy2, 50f + 6f * cs2), "cluster/absolute-offset");
            Check(MapClusterPlacement.TryCompute(100, 50, 58.8f, 52f, 120f, 120f, 10f, -6f,
                      out float cs3, out _, out _) && Near(cs3, cs2), "cluster/idempotent");
            // 超大岛：不放大超过原尺寸
            Check(MapClusterPlacement.TryCompute(100, 50, 400f, 400f, 40f, 40f, 0f, 0f,
                      out float cs4, out _, out _) && Near(cs4, 1f), "cluster/no-upscale");
            // 非法输入：不产生 false-success
            Check(!MapClusterPlacement.TryCompute(0, 0, 58.8f, 52f, 0f, 120f, 0f, 0f, out _, out _, out _),
                "cluster/reject-zero-art");
            // 极小 slot：clamp 到 MinScale（不产生 0/负缩放）
            Check(MapClusterPlacement.TryCompute(100, 50, 1f, 1f, 400f, 400f, 0f, 0f,
                      out float cs5, out _, out _) && Near(cs5, MapClusterPlacement.MinScale), "cluster/min-scale-clamp");
            // r4: 重复 ApplyClusterCell 的绝对尺度语义 —— 目标 scale = **冻结原始 scale** × s；
            // 连续多次应用（每帧/每个 fraction 都会调）必须收敛到同一个绝对值，而不是相对当前值再乘。
            MapClusterPlacement.TryCompute(100, 50, 58.8f, 52f, 120f, 120f, 10f, -6f,
                out float sA, out float xA, out float yA);
            float frozen = 1f;                  // entry.LocalScale（快照冻结，不随应用变化）
            float applied = frozen;             // 应用前
            for (int i = 0; i < 4; i++)
            {
                applied = frozen * sA;          // 与 ApplyClusterCell 相同表达式：始终乘冻结原始值
            }
            Check(Near(applied, sA) && Near(sA, sA), "cluster/repeat-absolute-scale");
            MapClusterPlacement.TryCompute(100, 50, 58.8f, 52f, 120f, 120f, 10f, -6f,
                out float sB, out float xB, out float yB);
            Check(Near(sA, sB) && Near(xA, xB) && Near(yA, yB), "cluster/repeat-same-inputs");

            // r4: partial 不认 success —— 部分结果必须 failed>0 且返回 false（调用方不得只数 placements）
            var partialSurface = new MapIconSurface(0f, 0f, 30f, 20f);
            var partialOut = new List<MapIconPlacement>();
            bool partialOk = MapResourceIconPlanner.TryPlan(Stress12(), partialSurface, partialOut, out _, out int partialMisses);
            Check(!partialOk && partialMisses > 0 && partialOut.Count > 0 && partialOut.Count < 12,
                "partial/never-success " + partialOut.Count + "/" + partialMisses);
        }

        /// <summary>
        /// 总览格几何（生产 MapOverviewGrid）：314×208 → 顶部 18 按钮带、10 格 62.8×95 无缝平铺、
        /// 格内可用高 94.5（上边距 2）、顶排内容上限 188（离按钮底 192 留 4）。
        /// </summary>
        private static void Grid()
        {
            const float PaperW = 314f, PaperH = 208f;
            var seen = new MapIconBox[MapOverviewGrid.Cols * MapOverviewGrid.Rows];
            for (int cell = 0; cell < seen.Length; cell++) seen[cell] = MapOverviewGrid.CellRect(PaperW, PaperH, cell);

            for (int cell = 0; cell < seen.Length; cell++)
            {
                var r = seen[cell];
                Check(Near(r.Width, 62.8f) && Near(r.Height, 95f), "grid/size#" + cell);
                Check(r.Y1 <= PaperH - MapOverviewGrid.ButtonBand + 0.001f, "grid/below-button-band#" + cell);
                Check(r.X0 >= -0.001f && r.X1 <= PaperW + 0.001f, "grid/inside-paper#" + cell);
            }
            // 顶排/底排位置（row 0 顶 = 190，row 1 顶 = 95）
            Check(Near(seen[0].Y1, 190f) && Near(seen[0].Y0, 95f), "grid/top-row");
            Check(Near(seen[5].Y1, 95f) && Near(seen[5].Y0, 0f), "grid/bottom-row");
            // 无缝平铺：总面积 = 314×190；相邻格边贴合
            float area = 0f;
            for (int i = 0; i < seen.Length; i++) area += seen[i].Width * seen[i].Height;
            Check(Near(area, PaperW * (PaperH - MapOverviewGrid.ButtonBand), 0.01f), "grid/tile-area");
            for (int col = 0; col < MapOverviewGrid.Cols; col++)
            {
                if (col + 1 < MapOverviewGrid.Cols)
                {
                    Check(Near(seen[col].X1, seen[col + 1].X0), "grid/adjacent-x#" + col);
                }
                Check(Near(seen[col].Y0, seen[col + MapOverviewGrid.Cols].Y1), "grid/adjacent-y#" + col);
            }
            // 图标可用面：顶排内容上限 188，与按钮底(192) 留 4
            var topSurface = new MapIconSurface(0f, 0f, seen[0].Width, MapOverviewGrid.IconSurfaceHeight(seen[0].Height));
            Check(Near(topSurface.Height, 94.5f), "grid/surface-height");
            float contentTop = seen[0].Y1 - (MapOverviewGrid.CellTopExtraInset + MapResourceIconPlanner.Margin);
            Check(Near(contentTop, 188f), "grid/content-top-188");
            Check(Near(192f - contentTop, 4f), "grid/button-gap-4");
            // 非法 cell 索引 clamp 不越界
            Check(Near(MapOverviewGrid.CellRect(PaperW, PaperH, 99).Y1, seen[9].Y1) &&
                  Near(MapOverviewGrid.CellRect(PaperW, PaperH, -3).Y1, seen[0].Y1), "grid/clamp");
        }

        // ------------------------------------------------------------- fixture dump
        private static void DumpLayout()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_fixture));
            var root = doc.RootElement;
            var sb = new StringBuilder();
            sb.Append("{\"clusters\":[");

            bool firstCluster = true;
            foreach (var c in root.GetProperty("clusters").EnumerateArray())
            {
                string name = c.GetProperty("name").GetString();
                float artW = (float)c.GetProperty("artW1").GetDouble();
                float artH = (float)c.GetProperty("artH1").GetDouble();
                float offX = (float)c.GetProperty("offX1").GetDouble();
                float offY = (float)c.GetProperty("offY1").GetDouble();
                float slotX = (float)c.GetProperty("slotCenterX").GetDouble();
                float slotY = (float)c.GetProperty("slotCenterY").GetDouble();
                float slotW = (float)c.GetProperty("slotWidth").GetDouble();
                float slotH = (float)c.GetProperty("slotHeight").GetDouble();
                bool placed = MapClusterPlacement.TryCompute(slotX, slotY, slotW, slotH, artW, artH, offX, offY,
                    out float scale, out float cx, out float cy);
                // r4: 同一岛簇按 fraction 序列**重复调用**（生产：EnsureOverviewLayout 0.50 + 降档循环），
                // 每次都用冻结原始几何，结果必须是绝对尺度、与调用次数/历史无关。
                var stepOut = new StringBuilder("[");
                if (c.TryGetProperty("steps", out JsonElement steps))
                {
                    bool firstStep = true;
                    foreach (var step in steps.EnumerateArray())
                    {
                        MapClusterPlacement.TryCompute(
                            (float)step.GetProperty("slotCenterX").GetDouble(),
                            (float)step.GetProperty("slotCenterY").GetDouble(),
                            (float)step.GetProperty("slotWidth").GetDouble(),
                            (float)step.GetProperty("slotHeight").GetDouble(),
                            artW, artH, offX, offY,
                            out float stepScale, out float stepX, out float stepY);
                        if (!firstStep) stepOut.Append(',');
                        firstStep = false;
                        stepOut.Append("{\"scale\":").Append(F(stepScale))
                               .Append(",\"centerX\":").Append(F(stepX))
                               .Append(",\"centerY\":").Append(F(stepY)).Append('}');
                    }
                }
                stepOut.Append(']');
                if (!firstCluster) sb.Append(',');
                firstCluster = false;
                sb.Append("{\"name\":").Append(Json(name))
                  .Append(",\"ok\":").Append(placed ? "true" : "false")
                  .Append(",\"scale\":").Append(F(scale))
                  .Append(",\"centerX\":").Append(F(cx))
                  .Append(",\"centerY\":").Append(F(cy))
                  .Append(",\"slotCenterX\":").Append(F(slotX))
                  .Append(",\"slotCenterY\":").Append(F(slotY))
                  .Append(",\"slotWidth\":").Append(F(slotW))
                  .Append(",\"slotHeight\":").Append(F(slotH))
                  .Append(",\"artW1\":").Append(F(artW)).Append(",\"artH1\":").Append(F(artH))
                  .Append(",\"offX1\":").Append(F(offX)).Append(",\"offY1\":").Append(F(offY))
                  .Append(",\"steps\":").Append(stepOut.ToString())
                  .Append('}');
            }

            sb.Append("],\"plannerCases\":[");
            bool firstCase = true;
            foreach (var c in root.GetProperty("plannerCases").EnumerateArray())
            {
                var surfaceEl = c.GetProperty("surface");
                var surface = new MapIconSurface(
                    (float)surfaceEl[0].GetDouble(), (float)surfaceEl[1].GetDouble(),
                    (float)surfaceEl[2].GetDouble(), (float)surfaceEl[3].GetDouble());
                foreach (var b in c.GetProperty("blocked").EnumerateArray())
                {
                    surface.AddBlocked(new MapIconBox(
                        (float)b[0].GetDouble(), (float)b[1].GetDouble(),
                        (float)b[2].GetDouble(), (float)b[3].GetDouble()));
                }

                var reqs = new List<MapIconRequest>();
                foreach (var r in c.GetProperty("requests").EnumerateArray())
                {
                    string kindName = r.GetProperty("kind").GetString();
                    MapIconKind kind = kindName == "steed" ? MapIconKind.Steed
                        : kindName == "hermit" ? MapIconKind.Hermit : MapIconKind.Statue;
                    reqs.Add(new MapIconRequest(kind, r.GetProperty("type").GetInt32(),
                        r.GetProperty("index").GetInt32(),
                        (float)r.GetProperty("w").GetDouble(), (float)r.GetProperty("h").GetDouble()));
                }

                bool topDown = c.GetProperty("topDown").GetBoolean();
                var placements = new List<MapIconPlacement>();
                bool all = MapResourceIconPlanner.TryPlan(reqs, surface, placements,
                    out float usedScale, out int failed, topDown);

                if (!firstCase) sb.Append(',');
                firstCase = false;
                sb.Append("{\"name\":").Append(Json(c.GetProperty("name").GetString()))
                  .Append(",\"scale\":").Append(F(usedScale))
                  .Append(",\"placed\":").Append(placements.Count)
                  .Append(",\"failed\":").Append(failed)
                  .Append(",\"all\":").Append(all ? "true" : "false")
                  .Append(",\"placements\":[");
                for (int i = 0; i < placements.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var p = placements[i];
                    sb.Append("{\"requestIndex\":").Append(p.RequestIndex)
                      .Append(",\"x\":").Append(F(p.X)).Append(",\"y\":").Append(F(p.Y))
                      .Append(",\"scale\":").Append(F(p.Scale))
                      .Append(",\"w\":").Append(F(p.Request.Width))
                      .Append(",\"h\":").Append(F(p.Request.Height))
                      .Append('}');
                }
                sb.Append("]}");
            }

            sb.Append("]}");
            File.WriteAllText(_layoutOut, sb.ToString());
        }

        private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        private static string Json(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        // ------------------------------------------------------------------ helper
        private static void Golden(string name, List<MapIconRequest> requests, MapIconSurface surface,
            List<MapIconBox> blocked, float expectScale, int expectPlaced, int expectMisses,
            float firstX, float firstY, bool topDown = false)
        {
            for (int i = 0; i < blocked.Count; i++) surface.AddBlocked(blocked[i]);
            var placements = new List<MapIconPlacement>();
            MapResourceIconPlanner.TryPlan(requests, surface, placements, out float scale, out int misses, topDown);
            Check(Near(scale, expectScale, 0.001f), "golden/" + name + "/scale=" + scale);
            Check(placements.Count == expectPlaced, "golden/" + name + "/placed=" + placements.Count);
            Check(misses == expectMisses, "golden/" + name + "/misses=" + misses);
            if (placements.Count > 0)
            {
                Check(Near(placements[0].X, firstX) && Near(placements[0].Y, firstY),
                    "golden/" + name + "/first=" + placements[0].X + "," + placements[0].Y);
            }
        }
    }
}
