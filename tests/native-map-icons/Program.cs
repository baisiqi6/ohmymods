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
    /// 地图图标纯函数离线回归（r7：原生地理布局取代 5×2 网格）：
    /// - MapOverviewLayout：真实 native fixture 的统一倍率布局（两两差值同倍率/结构关系/扩展带/
    ///   第 11 簇不影响原 10/重复应用幂等）；
    /// - MapIconRegionPlanner：真实 footprint 的最近岛区域（归属正确/区域不相交/paper clipping）；
    /// - MapResourceIconPlanner：golden 向量逐值、不变量（surface 内/Gutter/RequestIndex/确定性）、
    ///   失败时不得部分展示（failed&gt;0 ⇒ 调用方必须丢弃）；
    /// - --geography-report <out.json>：把真实 native fixture（10 簇原始 anchoredPosition/art）喂给
    ///   **生产纯函数**，导出统一倍率布局、每岛自由区域与真实 fixture 容量裁决（离线证据）。
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

        private static float Sq(float a, float b) => a * a + b * b;

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
                if (args[i] == "--geography-report") _reportOut = args[i + 1];
            }

            if (_reportOut != null)
            {
                WriteGeographyReport(_reportOut);
                Console.WriteLine($"GEOGRAPHY REPORT written to {_reportOut}");
                return 0;
            }

            SelfChecks();
            Console.WriteLine($"{( _fails == 0 ? "ALL PASS" : "FAILURES")} checks={_checks} fails={_fails}");
            return _fails == 0 ? 0 : 1;
        }

        private static string _reportOut;

        // ------------------------------------------------------------------ checks
        private static void SelfChecks()
        {
            Geography();
            Regions();
            Extension();
            DetailCapacity();
            Lifecycle();
            LiveViewport();
            Presentation();
            WorldCapacity();

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
            string syncPath = EvidencePath("custom-icons.json");
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

            // r4: partial 不认 success —— 部分结果必须 failed>0 且返回 false（调用方不得只数 placements）
            var partialSurface = new MapIconSurface(0f, 0f, 30f, 20f);
            var partialOut = new List<MapIconPlacement>();
            bool partialOk = MapResourceIconPlanner.TryPlan(Stress12(), partialSurface, partialOut, out _, out int partialMisses);
            Check(!partialOk && partialMisses > 0 && partialOut.Count > 0 && partialOut.Count < 12,
                "partial/never-success " + partialOut.Count + "/" + partialMisses);
        }

        /// <summary>
        /// 原生地理布局（生产 MapOverviewLayout，r7 取代 5×2 网格）：真实 native fixture 验收：
        /// ① 任意两簇差值（pivot 与 art 盒中心/尺寸）保持同一倍率 s（target_i−target_j = s·(orig_i−orig_j)）；
        /// ② 四角成对岛/中心出生岛/奥林匹斯结构关系与原始 scale 比例保持；
        /// ③ 全部 art 盒落在底部扩展带以上的内容区内（扩展带为空，为第 11 岛预留）；
        /// ④ 统一倍率永不放大且在合理范围；
        /// ⑤ 第 11 个登记簇（扩展）在底部带内、且不影响原 10 的任何目标；
        /// ⑥ 重复 apply/restore（绝对赋值 + 冻结原始快照）无累计缩放。
        /// </summary>
        private static void Geography()
        {
            List<NativeCluster> clusters = LoadNativeClusters(out string error);
            Check(clusters != null && clusters.Count == 10, "geography/fixture-10 " + (error ?? ""));
            if (clusters == null || clusters.Count != 10) return;

            const float PaperW = 314f, PaperH = 208f, Band = 18f;
            var inputs = new List<MapOverviewClusterInput>();
            foreach (NativeCluster c in clusters) inputs.Add(c.ToInput());
            var targets = new List<MapOverviewClusterTarget>();
            bool ok = MapOverviewLayout.TryPlan(inputs, PaperW, PaperH, Band, targets, out float s);
            Check(ok && targets.Count == inputs.Count, "geography/plan-ok");
            if (!ok || targets.Count != inputs.Count) return;

            Check(s > 0f && s <= MapOverviewLayout.MaxUniformScale, "geography/never-upscale " + s);
            Check(s > 0.7f, "geography/scale-not-tiny " + s);
            Check(Math.Abs(MapOverviewLayout.BandHeight(PaperH, Band) - 39.9f) <= 0.05f, "geography/band-height");

            // ① pivot 两两差值同一倍率
            float pivotErr = 0f;
            for (int i = 0; i < inputs.Count; i++)
            {
                for (int j = i + 1; j < inputs.Count; j++)
                {
                    pivotErr = Math.Max(pivotErr,
                        Math.Abs((targets[i].X - targets[j].X) - s * (inputs[i].OrigX - inputs[j].OrigX)));
                    pivotErr = Math.Max(pivotErr,
                        Math.Abs((targets[i].Y - targets[j].Y) - s * (inputs[i].OrigY - inputs[j].OrigY)));
                }
            }
            Check(pivotErr <= 0.01f, "geography/pairwise-uniform " + pivotErr);

            // ① art 盒中心差与尺寸同样统一倍率
            var artBoxes = new List<MapIconBox>();
            var origBoxes = new List<MapIconBox>();
            float artErr = 0f;
            for (int i = 0; i < inputs.Count; i++)
            {
                artBoxes.Add(MapOverviewLayout.ArtBoxOf(inputs[i], targets[i], PaperW, PaperH));
                MapOverviewLayout.TryScale1Box(inputs[i], PaperW, PaperH, out MapIconBox orig);
                origBoxes.Add(orig);
            }
            for (int i = 0; i < origBoxes.Count; i++)
            {
                for (int j = i + 1; j < origBoxes.Count; j++)
                {
                    float dx0 = (origBoxes[i].X0 + origBoxes[i].X1 - origBoxes[j].X0 - origBoxes[j].X1) * 0.5f;
                    float dy0 = (origBoxes[i].Y0 + origBoxes[i].Y1 - origBoxes[j].Y0 - origBoxes[j].Y1) * 0.5f;
                    float dx1 = (artBoxes[i].X0 + artBoxes[i].X1 - artBoxes[j].X0 - artBoxes[j].X1) * 0.5f;
                    float dy1 = (artBoxes[i].Y0 + artBoxes[i].Y1 - artBoxes[j].Y0 - artBoxes[j].Y1) * 0.5f;
                    artErr = Math.Max(artErr, Math.Abs(dx1 - s * dx0));
                    artErr = Math.Max(artErr, Math.Abs(dy1 - s * dy0));
                    artErr = Math.Max(artErr, Math.Abs(artBoxes[i].Width - s * origBoxes[i].Width));
                }
            }
            Check(artErr <= 0.02f, "geography/art-uniform " + artErr);

            // ② scale 比例保持：每簇目标 scale = 原始 scale × s（比例不因岛而异）
            bool ratios = true;
            for (int i = 0; i < inputs.Count; i++)
            {
                if (!Near(targets[i].ScaleX, inputs[i].OrigScaleX * s) ||
                    !Near(targets[i].ScaleY, inputs[i].OrigScaleY * s)) ratios = false;
            }
            Check(ratios, "geography/scale-ratios");

            // ② 四角成对岛 / 中心出生岛（Oracle）/ 奥林匹斯结构
            int qa = clusters.FindIndex(c => c.Name == "Main_Map_Quest_Artemis_Greece");
            int qh = clusters.FindIndex(c => c.Name == "Main_Map_Quest_Hermes_Greece");
            int ta = clusters.FindIndex(c => c.Name == "Main_Map_Quest_Athena_Greece");
            int th = clusters.FindIndex(c => c.Name == "Main_Map_Quest_Hephaestus_Greece");
            int mo = clusters.FindIndex(c => c.Name == "Main_Map_Land_MtOlympus_Greece");
            int or = clusters.FindIndex(c => c.Name == "Main_Map_Land_Oracle_Greece");
            Check(qa >= 0 && qh >= 0 && ta >= 0 && th >= 0 && mo >= 0 && or >= 0, "geography/named-clusters");
            if (qa >= 0 && qh >= 0 && ta >= 0 && th >= 0 && mo >= 0 && or >= 0)
            {
                Check(targets[qa].X < targets[qh].X && targets[ta].X > targets[th].X, "geography/corner-x-order");
                Check(Near(targets[qa].Y, targets[qh].Y, 0.02f) &&
                      targets[qa].Y > targets[or].Y && targets[qh].Y > targets[or].Y, "geography/top-pair-y");
                Check(targets[ta].Y < targets[or].Y && targets[th].Y < targets[or].Y, "geography/bottom-pair-y");
                Check(targets[mo].Y > targets[qa].Y && targets[mo].Y > targets[qh].Y, "geography/olympus-topmost");
                // 中心出生岛：Oracle 距内容区中心唯一最近（原生于 (0,0)，统一变换保持该关系）
                MapIconBox centerBox = MapOverviewLayout.ContentRegion(PaperW, PaperH, Band);
                float centerX = (centerBox.X0 + centerBox.X1) * 0.5f;
                float centerY = (centerBox.Y0 + centerBox.Y1) * 0.5f;
                float oracleDistance = Sq(targets[or].X + PaperW * 0.5f - centerX,
                                          targets[or].Y + PaperH * 0.5f - centerY);
                bool oracleNearest = true;
                for (int i = 0; i < targets.Count; i++)
                {
                    if (i == or) continue;
                    float other = Sq(targets[i].X + PaperW * 0.5f - centerX,
                                     targets[i].Y + PaperH * 0.5f - centerY);
                    if (other <= oracleDistance) oracleNearest = false;
                }
                Check(oracleNearest, "geography/spawn-center-unique");
            }

            // ③ 内容区/扩展带：全部 art 在内容区内、扩展带为空
            MapIconBox content = MapOverviewLayout.ContentRegion(PaperW, PaperH, Band);
            for (int i = 0; i < artBoxes.Count; i++)
            {
                Check(artBoxes[i].X0 >= content.X0 - 0.01f && artBoxes[i].X1 <= content.X1 + 0.01f &&
                      artBoxes[i].Y0 >= content.Y0 - 0.01f && artBoxes[i].Y1 <= content.Y1 + 0.01f,
                      "geography/art-inside-content#" + i);
            }

            // ⑤ 扩展簇（第 11 个登记 UILand）：底部带内、不影响原 10
            var nativeHuge = new MapOverviewClusterInput(0f, 0f, 1f, 1f, 1f, 300f, 120f, 0f, 0f);
            var targetsAfter = new List<MapOverviewClusterTarget>();
            Check(MapOverviewLayout.TryPlan(inputs, PaperW, PaperH, Band, targetsAfter, out float sAgain) &&
                  Near(sAgain, s) && targetsAfter.Count == targets.Count, "geography/repeat-same-inputs");
            for (int i = 0; i < targets.Count && i < targetsAfter.Count; i++)
            {
                Check(Near(targets[i].X, targetsAfter[i].X) && Near(targets[i].Y, targetsAfter[i].Y) &&
                      Near(targets[i].ScaleX, targetsAfter[i].ScaleX), "geography/extension-no-native-effect#" + i);
            }
            Check(MapOverviewLayout.NativeUiClusterCount == 10, "geography/native-ui-count");
            Check(MapOverviewLayout.TryPlanExtension(nativeHuge, s, PaperW, PaperH, Band,
                      out MapOverviewClusterTarget hugeTarget), "geography/extension-huge-planned");
            MapIconBox hugeBox = MapOverviewLayout.ArtBoxOf(nativeHuge, hugeTarget, PaperW, PaperH);
            MapIconBox band = MapOverviewLayout.BandRegion(PaperW, PaperH, Band);
            Check(hugeBox.X0 >= band.X0 - 0.01f && hugeBox.X1 <= band.X1 + 0.01f &&
                  hugeBox.Y0 >= band.Y0 - 0.01f && hugeBox.Y1 <= band.Y1 + 0.01f, "geography/extension-in-band");
            var extInput = new MapOverviewClusterInput(12f, -20f, 1f, 1f, 1f, 90f, 60f, 0f, 0f);
            Check(MapOverviewLayout.TryPlanExtension(extInput, s, PaperW, PaperH, Band,
                      out MapOverviewClusterTarget extTarget), "geography/extension-planned");
            MapIconBox extBox = MapOverviewLayout.ArtBoxOf(extInput, extTarget, PaperW, PaperH);
            Check(extBox.Y0 >= band.Y0 - 0.01f && extBox.Y1 <= band.Y1 + 0.01f, "geography/extension-art-in-band");

            // ⑥ 重复 apply/restore：绝对赋值 + 冻结快照 ⇒ 无累计缩放
            for (int i = 0; i < inputs.Count; i++)
            {
                float stX = inputs[i].OrigX, stY = inputs[i].OrigY, stS = inputs[i].OrigScaleX;
                for (int round = 0; round < 3; round++)
                {
                    stX = targets[i].X; stY = targets[i].Y; stS = targets[i].ScaleX;
                    stX = inputs[i].OrigX; stY = inputs[i].OrigY; stS = inputs[i].OrigScaleX;
                }
                stX = targets[i].X; stY = targets[i].Y; stS = targets[i].ScaleX;
                Check(Near(stX, targets[i].X) && Near(stY, targets[i].Y) && Near(stS, targets[i].ScaleX),
                    "geography/repeat-absolute#" + i);
            }
        }

        /// <summary>
        /// 图标区域（生产 MapIconRegionPlanner）：真实 footprint 的最近岛 Voronoi + 最大矩形；
        /// 归属正确（每矩形中心最近本岛）、区域两两不相交、不与任何 art 相交、在内容区内；
        /// 图标规划只在"全部放下"时才展示（外部行为），且落点全部在本岛区域内、不压 art/不带。
        /// </summary>
        private static void Regions()
        {
            List<NativeCluster> clusters = LoadNativeClusters(out _);
            if (clusters == null || clusters.Count != 10) { Check(false, "regions/fixture"); return; }
            const float PaperW = 314f, PaperH = 208f, Band = 18f;
            var inputs = new List<MapOverviewClusterInput>();
            foreach (NativeCluster c in clusters) inputs.Add(c.ToInput());
            var targets = new List<MapOverviewClusterTarget>();
            MapOverviewLayout.TryPlan(inputs, PaperW, PaperH, Band, targets, out _);
            var artBoxes = new List<MapIconBox>();
            var owners = new List<MapIconRegionOwner>();
            for (int i = 0; i < inputs.Count; i++)
            {
                MapIconBox box = MapOverviewLayout.ArtBoxOf(inputs[i], targets[i], PaperW, PaperH);
                artBoxes.Add(box);
                owners.Add(new MapIconRegionOwner(i, box));
            }
            MapIconBox content = MapOverviewLayout.ContentRegion(PaperW, PaperH, Band);
            var rects = new List<MapIconBox>();
            var rectOwners = new List<int>();
            MapIconRegionPlanner.Build(owners, content, MapIconRegionPlanner.DefaultCell, rects, rectOwners);
            Check(rects.Count > 0 && rects.Count == rectOwners.Count, "regions/nonempty");

            for (int r = 0; r < rects.Count; r++)
            {
                MapIconBox rect = rects[r];
                Check(rect.X0 >= content.X0 - 0.01f && rect.X1 <= content.X1 + 0.01f &&
                      rect.Y0 >= content.Y0 - 0.01f && rect.Y1 <= content.Y1 + 0.01f, "regions/inside#" + r);
                Check(rect.Width >= MapIconRegionPlanner.MinRectSide - 0.01f &&
                      rect.Height >= MapIconRegionPlanner.MinRectSide - 0.01f, "regions/min-side#" + r);
                for (int a = 0; a < artBoxes.Count; a++)
                {
                    Check(!Intersects(rect, artBoxes[a], -0.01f), "regions/no-art-overlap#" + r + "_" + a);
                }
                float own = Distance(rect, artBoxes[rectOwners[r]]);
                for (int o = 0; o < artBoxes.Count; o++)
                {
                    if (o == rectOwners[r]) continue;
                    Check(own <= Distance(rect, artBoxes[o]) + 0.01f, "regions/nearest-owner#" + r + "_" + o);
                }
            }
            for (int i = 0; i < rects.Count; i++)
            {
                for (int j = i + 1; j < rects.Count; j++)
                {
                    if (rectOwners[i] == rectOwners[j]) continue;
                    Check(!Intersects(rects[i], rects[j], -0.01f), "regions/disjoint#" + i + "_" + j);
                }
            }
            foreach (string quest in new[] { "Main_Map_Quest_Artemis_Greece", "Main_Map_Quest_Hermes_Greece",
                                             "Main_Map_Quest_Athena_Greece", "Main_Map_Quest_Hephaestus_Greece" })
            {
                int idx = clusters.FindIndex(c => c.Name == quest);
                Check(rectOwners.Contains(idx), "regions/quest-has-area:" + quest);
            }

            var three = new List<MapIconRequest> { Req(MapIconKind.Steed, 37, 40, 28, 0),
                                                   Req(MapIconKind.Steed, 35, 40, 22, 1),
                                                   Req(MapIconKind.Hermit, 0, 18, 24, 0) };
            int plannedIslands = 0;
            for (int i = 0; i < inputs.Count; i++)
            {
                var regions = new List<MapIconBox>();
                for (int r = 0; r < rects.Count; r++) if (rectOwners[r] == i) regions.Add(rects[r]);
                bool all = PlanAcrossRegions(three, regions, artBoxes, out _, out List<MapIconPlacement> merged);
                if (all)
                {
                    plannedIslands++;
                    for (int p = 0; p < merged.Count; p++)
                    {
                        MapIconBox box = new MapIconBox(merged[p].X, merged[p].Y,
                            merged[p].X + merged[p].Request.Width * merged[p].Scale,
                            merged[p].Y + merged[p].Request.Height * merged[p].Scale);
                        Check(box.X0 >= content.X0 - 0.01f && box.X1 <= content.X1 + 0.01f &&
                              box.Y0 >= content.Y0 - 0.01f && box.Y1 <= content.Y1 + 0.01f,
                              "regions/placement-inside#" + i + "_" + p);
                        for (int a = 0; a < artBoxes.Count; a++)
                        {
                            Check(!Intersects(box, artBoxes[a], -0.01f), "regions/placement-no-art#" + i + "_" + p + "_" + a);
                        }
                        bool inOwnRegion = false;
                        for (int r = 0; r < regions.Count; r++)
                        {
                            if (box.X0 >= regions[r].X0 - 0.01f && box.X1 <= regions[r].X1 + 0.01f &&
                                box.Y0 >= regions[r].Y0 - 0.01f && box.Y1 <= regions[r].Y1 + 0.01f) { inOwnRegion = true; break; }
                        }
                        Check(inOwnRegion, "regions/placement-in-own-region#" + i + "_" + p);
                    }
                }
                else
                {
                    Check(merged == null || merged.Count == 0, "regions/no-partial#" + i);
                }
            }
            Check(plannedIslands >= 1, "regions/at-least-one-island-plans-3");

            // 容量回归锚点（真实 fixture）：四角大岛之一放得下真实最坏 10 图标集（≥0.36 可读档）
            int questArtemis = clusters.FindIndex(c => c.Name == "Main_Map_Quest_Artemis_Greece");
            var questRegions = new List<MapIconBox>();
            for (int r = 0; r < rects.Count; r++) if (rectOwners[r] == questArtemis) questRegions.Add(rects[r]);
            var worstOut = new List<MapIconPlacement>();
            bool worstOk = PlanAcrossRegions(Worst10(), questRegions, artBoxes, out float worstScale);
            Check(worstOk && worstScale >= 0.36f, "regions/quest-artemis-worst10 " + worstScale);

            // 密集中间岛（真实无自由空间）必须"失败且零放置"——不得错归属、不得部分展示
            int godHermes = clusters.FindIndex(c => c.Name == "Main_Map_God_Hermes_Greece");
            var godRegions = new List<MapIconBox>();
            for (int r = 0; r < rects.Count; r++) if (rectOwners[r] == godHermes) godRegions.Add(rects[r]);
            bool heavyOk = PlanAcrossRegions(Worst10(), godRegions, artBoxes, out _, out List<MapIconPlacement> heavyOut);
            Check(!heavyOk && (heavyOut == null || heavyOut.Count == 0) && godRegions.Count == 0,
                "regions/dense-island-fails-clean");
        }

        private static bool PlanAcrossRegions(List<MapIconRequest> requests, List<MapIconBox> regions,
            List<MapIconBox> artBoxes, out float usedScale)
        {
            List<MapIconPlacement> ignored;
            return PlanAcrossRegions(requests, regions, artBoxes, out usedScale, out ignored);
        }

        private static bool PlanAcrossRegions(List<MapIconRequest> requests, List<MapIconBox> regions,
            List<MapIconBox> artBoxes, out float usedScale, out List<MapIconPlacement> result)
        {
            usedScale = 0f;
            result = null;
            var pending = new List<MapIconRequest>();
            var pendingIndex = new List<int>();
            var packed = new List<MapIconPlacement>();
            var merged = new List<MapIconPlacement>();
            for (int s = 0; s < MapResourceIconPlanner.DefaultScaleCount; s++)
            {
                float scale = MapResourceIconPlanner.Scales[s];
                pending.Clear(); pendingIndex.Clear(); merged.Clear();
                for (int i = 0; i < requests.Count; i++) { pending.Add(requests[i]); pendingIndex.Add(i); }
                for (int r = 0; r < regions.Count && pending.Count > 0; r++)
                {
                    var surface = new MapIconSurface(regions[r].X0, regions[r].Y0, regions[r].X1, regions[r].Y1);
                    for (int b = 0; b < artBoxes.Count; b++)
                    {
                        MapIconBox box = artBoxes[b];
                        if (box.X1 <= regions[r].X0 || box.X0 >= regions[r].X1 ||
                            box.Y1 <= regions[r].Y0 || box.Y0 >= regions[r].Y1) continue;
                        surface.AddBlocked(box);
                    }
                    MapResourceIconPlanner.TryPlanAtScale(pending, surface, scale, packed, out _, true);
                    if (packed.Count == 0) continue;
                    for (int p = 0; p < packed.Count; p++)
                    {
                        merged.Add(new MapIconPlacement(packed[p].Request, pendingIndex[packed[p].RequestIndex],
                            packed[p].X, packed[p].Y, packed[p].Scale));
                    }
                    for (int p = packed.Count - 1; p >= 0; p--)
                    {
                        int idx = packed[p].RequestIndex;
                        pending.RemoveAt(idx); pendingIndex.RemoveAt(idx);
                    }
                }
                if (pending.Count == 0) { usedScale = scale; result = merged; return true; }
            }
            merged.Clear();
            result = merged;    // 失败时契约：零放置（调用方不得部分展示）
            return false;
        }

        private static bool Intersects(MapIconBox a, MapIconBox b, float margin)
        {
            return a.X0 - margin < b.X1 && b.X0 < a.X1 + margin &&
                   a.Y0 - margin < b.Y1 && b.Y0 < a.Y1 + margin;
        }

        private static float Distance(MapIconBox rect, MapIconBox art)
        {
            float cx = (rect.X0 + rect.X1) * 0.5f, cy = (rect.Y0 + rect.Y1) * 0.5f;
            float dx = art.X0 - cx; if (dx < 0f) dx = cx > art.X1 ? cx - art.X1 : 0f;
            float dy = art.Y0 - cy; if (dy < 0f) dy = cy > art.Y1 ? cy - art.Y1 : 0f;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private sealed class NativeCluster
        {
            internal string Name;
            internal float Ax, Ay;
            internal float ScaleX = 1f, ScaleY = 1f, ScaleZ = 1f;
            internal float ArtW1, ArtH1, OffX1, OffY1;

            internal MapOverviewClusterInput ToInput()
                => new MapOverviewClusterInput(Ax, Ay, ScaleX, ScaleY, ScaleZ, ArtW1, ArtH1, OffX1, OffY1);
        }

        /// <summary>
        /// 便携 fixture 解析（R6）：只查随项目 copy 到输出目录的 fixtures/ 与仓库内相对路径，
        /// 不依赖 .local/worker 私有目录或绝对 /Users 路径。
        /// </summary>
        private static string EvidencePath(string name)
        {
            foreach (string prefix in new[] { AppContext.BaseDirectory + "fixtures/", AppContext.BaseDirectory,
                                              "fixtures/", "tests/native-map-icons/fixtures/",
                                              "../native-map-icons/fixtures/", "../../tests/native-map-icons/fixtures/" })
            {
                string candidate = prefix + name;
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        /// <summary>真实 native fixture：10 簇原始 anchoredPosition/scale（native-map-analysis）+ art 冻结几何（layout-manifest）。</summary>
        private static List<NativeCluster> LoadNativeClusters(out string error)
        {
            error = null;
            string manifestPath = EvidencePath("layout-manifest.json");
            string analysisPath = EvidencePath("native-map-analysis.json");
            if (manifestPath == null || analysisPath == null)
            {
                error = "fixture missing";
                return null;
            }
            var result = new List<NativeCluster>();
            using (var analysis = JsonDocument.Parse(File.ReadAllText(analysisPath)))
            {
                JsonElement rows = analysis.RootElement.GetProperty("greece").GetProperty("Main_Map_Greece");
                foreach (JsonElement row in rows.EnumerateArray())
                {
                    if (!row.TryGetProperty("path", out JsonElement pathEl) ||
                        !row.TryGetProperty("ap", out JsonElement ap) ||
                        !row.TryGetProperty("scale", out JsonElement scale)) continue;
                    string path = pathEl.GetString();
                    if (path == null || !path.StartsWith("/Main_Map_Greece/", StringComparison.Ordinal)) continue;
                    string name = path.Substring("/Main_Map_Greece/".Length);
                    if (name.Contains("/")) continue;
                    if (!HasComponent(row, "UIMainMapLand")) continue;
                    if (result.Exists(c => c.Name == name)) continue;
                    result.Add(new NativeCluster
                    {
                        Name = name,
                        Ax = (float)ap[0].GetDouble(),
                        Ay = (float)ap[1].GetDouble(),
                        ScaleX = (float)scale[0].GetDouble(),
                        ScaleY = (float)scale[1].GetDouble(),
                        ScaleZ = (float)scale[2].GetDouble(),
                    });
                }
            }
            using (var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath)))
            {
                JsonElement clusters = manifest.RootElement.GetProperty("clusters");
                foreach (NativeCluster c in result)
                {
                    JsonElement entry = clusters.GetProperty(c.Name);
                    c.ArtW1 = (float)entry.GetProperty("artW1").GetDouble();
                    c.ArtH1 = (float)entry.GetProperty("artH1").GetDouble();
                    c.OffX1 = (float)entry.GetProperty("offX1").GetDouble();
                    c.OffY1 = (float)entry.GetProperty("offY1").GetDouble();
                }
            }
            return result;
        }

        private static bool HasComponent(JsonElement row, string className)
        {
            if (!row.TryGetProperty("comps", out JsonElement comps)) return false;
            foreach (JsonElement comp in comps.EnumerateArray())
            {
                if (comp[0].GetString() == className) return true;
            }
            return false;
        }

        /// <summary>
        /// 扩展簇（physical11 / UI10）：登记实例才放底部带（纯函数层以显式 extension 输入表达），
        /// 真实 15/16 最大尺寸资源集在扩展区域必须 ≥0.36 完整显示、不覆盖任何 native art、
        /// 不越出底部带；未登记 extra 的行为由 runtime 桥判定（本层不伪造）。
        /// </summary>
        private static void Extension()
        {
            List<(float W, float H, MapIconKind Kind)> icons16 = MaxResourceIcons(16);
            Check(icons16.Count == 16, "extension/fixture-16-sizes");
            if (icons16.Count < 16) return;
            List<(float W, float H, MapIconKind Kind)> icons15 = MaxResourceIcons(15);
            if (!TryRealNativeLayout(out List<MapIconBox> nativeArt, out _))
            {
                Check(false, "extension/native-layout");
                return;
            }

            const float PaperW = 314f, PaperH = 208f, Band = 18f;
            MapIconBox band = MapOverviewLayout.BandRegion(PaperW, PaperH, Band);
            foreach ((float artW, float artH) in new[] { (76f, 88f), (57f, 41f), (40f, 36f) })
            {
                var ext = new MapOverviewClusterInput(0f, 0f, 1f, 1f, 1f, artW, artH, 0f, 0f);
                Check(MapOverviewLayout.TryPlanExtension(ext, 0.8775f, PaperW, PaperH, Band,
                          out MapOverviewClusterTarget target), "extension/plan-" + artW + "x" + artH);
                MapIconBox extArt = MapOverviewLayout.ArtBoxOf(ext, target, PaperW, PaperH);
                Check(extArt.X0 >= band.X0 - 0.01f && extArt.X1 <= band.X1 + 0.01f &&
                      extArt.Y0 >= band.Y0 - 0.01f && extArt.Y1 <= band.Y1 + 0.01f,
                      "extension/art-in-band-" + artW + "x" + artH);

                var owners = new List<MapIconRegionOwner>(1) { new MapIconRegionOwner(11, extArt) };
                var rects = new List<MapIconBox>();
                var rectOwners = new List<int>();
                MapIconRegionPlanner.Build(owners, band, MapIconRegionPlanner.DefaultCell, rects, rectOwners);
                Check(rects.Count > 0, "extension/regions-" + artW + "x" + artH);

                var blockers = new List<MapIconBox>(nativeArt);
                blockers.Add(extArt);
                foreach ((int count, List<(float W, float H, MapIconKind Kind)> icons) in
                         new[] { (15, icons15), (16, icons16) })
                {
                    var reqs = ToRequests(icons);
                    bool ok = PlanAcrossRegions(reqs, rects, blockers, out float used,
                                                out List<MapIconPlacement> merged);
                    Check(ok && used >= 0.36f, "extension/" + artW + "x" + artH + "-set" + count +
                          " scale=" + used + " placed=" + (merged == null ? 0 : merged.Count));
                    if (!ok || merged == null) continue;
                    for (int p = 0; p < merged.Count; p++)
                    {
                        MapIconBox box = PlacementBox(merged[p]);
                        Check(box.Y0 >= band.Y0 - 0.01f && box.Y1 <= band.Y1 + 0.01f &&
                              box.X0 >= band.X0 - 0.01f && box.X1 <= band.X1 + 0.01f,
                              "extension/set" + count + "/inside-band-" + artW + "-" + p);
                        for (int b = 0; b < blockers.Count; b++)
                        {
                            Check(!Intersects(box, blockers[b], -0.01f),
                                "extension/set" + count + "/no-native-art-" + artW + "-" + p + "_" + b);
                        }
                        for (int q = 0; q < p; q++)
                        {
                            Check(!Intersects(box, PlacementBox(merged[q]), -0.01f),
                                "extension/set" + count + "/no-overlap-" + artW + "-" + p + "_" + q);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 详情完整列表容量（root 决定：总览超容量时详情给完整真实列表）：真实 228×186 detail surface +
        /// 保守遮挡（克隆岛 art 76×88 居中 + 一个船标）下，15/16 最大尺寸资源集必须 ≥0.36 完整显示。
        /// </summary>
        private static void DetailCapacity()
        {
            List<(float W, float H, MapIconKind Kind)> icons16 = MaxResourceIcons(16);
            Check(icons16.Count == 16, "detail/fixture-16-sizes");
            if (icons16.Count < 16) return;
            List<(float W, float H, MapIconKind Kind)> icons15 = MaxResourceIcons(15);

            var surface = new MapIconSurface(-24f, -18f, 204f, 168f);   // layout-manifest 实测 detail surface
            var art = new MapIconBox(-38f, -44f, 38f, 44f);             // 最大原生岛 art（76×88）居中
            var boat = new MapIconBox(-52f, 17f, -20f, 49f);            // 一个原生船标
            surface.AddBlocked(art);
            surface.AddBlocked(boat);

            foreach ((int count, List<(float W, float H, MapIconKind Kind)> icons) in
                     new[] { (15, icons15), (16, icons16) })
            {
                var reqs = ToRequests(icons);
                var placements = new List<MapIconPlacement>();
                bool ok = MapResourceIconPlanner.TryPlan(reqs, surface, placements, out float used, out int failed,
                    false, MapResourceIconPlanner.DefaultScaleCount);
                Check(ok && failed == 0 && placements.Count == count && used >= 0.36f,
                    "detail/set" + count + " scale=" + used + " placed=" + placements.Count);
                if (!ok) continue;
                for (int p = 0; p < placements.Count; p++)
                {
                    MapIconBox box = PlacementBox(placements[p]);
                    Check(box.X0 >= -24f - 0.01f && box.X1 <= 204f + 0.01f &&
                          box.Y0 >= -18f - 0.01f && box.Y1 <= 168f + 0.01f, "detail/inside#" + count + "_" + p);
                    Check(!Intersects(box, art, -0.01f) && !Intersects(box, boat, -0.01f),
                        "detail/no-native-art#" + count + "_" + p);
                }
            }
        }

        /// <summary>真实资源图标尺寸集（icon-rects.json，iconType 0/1/2），按面积降序取前 count 个——不发明尺寸。</summary>
        private static List<(float W, float H, MapIconKind Kind)> MaxResourceIcons(int count)
        {
            var result = new List<(float, float, MapIconKind)>();
            string path = EvidencePath("icon-rects.json");
            if (path == null) return result;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var all = new List<(float Area, float W, float H, MapIconKind Kind)>();
            foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
            {
                int slash = prop.Name.IndexOf('/');
                if (slash <= 0) continue;
                if (!int.TryParse(prop.Name.Substring(0, slash), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int iconType)) continue;
                if (iconType != 0 && iconType != 1 && iconType != 2) continue;
                JsonElement v = prop.Value;
                float w = (float)v.GetProperty("w").GetDouble() *
                          Math.Abs((float)v.GetProperty("scale").GetProperty("x").GetDouble());
                float h = (float)v.GetProperty("h").GetDouble() *
                          Math.Abs((float)v.GetProperty("scale").GetProperty("y").GetDouble());
                MapIconKind kind = iconType == 1 ? MapIconKind.Steed
                    : iconType == 2 ? MapIconKind.Hermit : MapIconKind.Statue;
                all.Add((w * h, w, h, kind));
            }
            all.Sort((a, b) => b.Area.CompareTo(a.Area));
            for (int i = 0; i < all.Count && i < count; i++) result.Add((all[i].W, all[i].H, all[i].Kind));
            return result;
        }

        private static List<MapIconRequest> ToRequests(List<(float W, float H, MapIconKind Kind)> icons)
        {
            var reqs = new List<MapIconRequest>(icons.Count);
            for (int i = 0; i < icons.Count; i++)
            {
                reqs.Add(new MapIconRequest(icons[i].Kind, i, i, icons[i].W, icons[i].H));
            }
            return reqs;
        }

        private static MapIconBox PlacementBox(in MapIconPlacement placement)
            => new MapIconBox(placement.X, placement.Y,
                placement.X + placement.Request.Width * placement.Scale,
                placement.Y + placement.Request.Height * placement.Scale);

        /// <summary>真实 native 布局（10 簇统一变换后的 art 盒）——扩展区域的 native 遮挡基准。</summary>
        private static bool TryRealNativeLayout(out List<MapIconBox> artBoxes, out float scale)
        {
            artBoxes = new List<MapIconBox>();
            scale = 0f;
            List<NativeCluster> clusters = LoadNativeClusters(out _);
            if (clusters == null || clusters.Count != 10) return false;
            var inputs = new List<MapOverviewClusterInput>();
            foreach (NativeCluster c in clusters) inputs.Add(c.ToInput());
            var targets = new List<MapOverviewClusterTarget>();
            if (!MapOverviewLayout.TryPlan(inputs, 314f, 208f, 18f, targets, out scale)) return false;
            for (int i = 0; i < inputs.Count; i++)
            {
                artBoxes.Add(MapOverviewLayout.ArtBoxOf(inputs[i], targets[i], 314f, 208f));
            }
            return true;
        }

        /// <summary>
        /// 生命周期（P2 修正的生产入口决策表 + 场景模型）：把"扩展地图必需几何"与"可关闭的 icons"分开。
        /// 覆盖 reviewer 要求的场景：registered+visited/current+OFF 仍底部定位且原 10 比例保持、
        /// 撤 icons 后原生槽恢复（nativeicons restored）且自有 icons 零显示、未注册/未知 owner 仍恢复、
        /// 重复 menu/重复更新幂等、Teardown 后再打开仍能定位。
        /// </summary>
        private static void Lifecycle()
        {
            // ---- 决策表（与运行时 OnLandUpdated 共用同一函数）----
            Check(MapOverviewLifecycle.DecideForOwnedLand(true, true, true) == MapOverviewAction.LayoutWithIcons,
                "lifecycle/on-icons");
            Check(MapOverviewLifecycle.DecideForOwnedLand(true, false, true) == MapOverviewAction.LayoutWithoutIcons,
                "lifecycle/off-visited-keeps-layout");
            Check(MapOverviewLifecycle.DecideForOwnedLand(true, false, false) == MapOverviewAction.Teardown,
                "lifecycle/off-never-visited-restores");
            Check(MapOverviewLifecycle.DecideForOwnedLand(false, false, false) == MapOverviewAction.Teardown,
                "lifecycle/off-unregistered-restores");
            Check(MapOverviewLifecycle.DecideForOwnedLand(false, false, true) == MapOverviewAction.Teardown,
                "lifecycle/off-unregistered-available-restores");
            Check(MapOverviewLifecycle.DecideForOwnedLand(true, true, false) == MapOverviewAction.LayoutWithIcons,
                "lifecycle/on-unregistered-keeps-on-flow");
            Check(!MapOverviewLifecycle.ShouldTeardownOnForeignLand(true, false), "lifecycle/foreign-on-keeps");
            Check(!MapOverviewLifecycle.ShouldTeardownOnForeignLand(false, true), "lifecycle/foreign-off-cached-keeps");
            Check(MapOverviewLifecycle.ShouldTeardownOnForeignLand(false, false), "lifecycle/foreign-off-no-cache-teardown");

            // ---- 场景模型：真实 fixture + 生产纯函数 + 决策表，逐步模拟入口序列 ----
            List<NativeCluster> clusters = LoadNativeClusters(out _);
            Check(clusters != null && clusters.Count == 10, "lifecycle/fixture");
            if (clusters == null || clusters.Count != 10) return;

            var natives = new List<MapOverviewClusterInput>();
            foreach (NativeCluster c in clusters) natives.Add(c.ToInput());
            var extension = new MapOverviewClusterInput(0f, 0f, 1f, 1f, 1f, 57f, 41f, 0f, 0f);
            var reference = new List<MapOverviewClusterTarget>();
            MapOverviewLayout.TryPlan(natives, 314f, 208f, 18f, reference, out float s);
            MapOverviewLayout.TryPlanExtension(extension, s, 314f, 208f, 18f, out MapOverviewClusterTarget referenceExt);
            MapIconBox referenceExtBox = MapOverviewLayout.ArtBoxOf(extension, referenceExt, 314f, 208f);
            MapIconBox band = MapOverviewLayout.BandRegion(314f, 208f, 18f);

            // 状态模型：Applied == reference 代表"已按统一变换定位"；null 代表原生几何。
            var state = new LifecycleState { Applied = null, IconsDrawn = false, NativeSuppressed = false };

            // 1) OFF 首次打开（registered + visited/current 11）：必需几何 + 撤 icons + 原生槽恢复
            ApplyAction(state, MapOverviewAction.LayoutWithoutIcons, natives, extension, reference, referenceExtBox, s);
            Check(state.Applied != null && TargetsEqual(state.Applied, reference), "lifecycle/off-first-layout");
            Check(state.ExtensionBoxInBand && state.ExtensionBox.X1 <= band.X1 + 0.01f &&
                  state.ExtensionBox.X0 >= band.X0 - 0.01f, "lifecycle/off-extension-bottom");
            Check(!state.IconsDrawn && !state.NativeSuppressed, "lifecycle/off-icons-withdrawn-native-restored");
            Check(PairwiseRatio(natives, state.Applied, s), "lifecycle/off-native-ratio-preserved");

            // 2) 同内容重复更新：几何/状态幂等
            var before = state.Applied;
            ApplyAction(state, MapOverviewAction.LayoutWithoutIcons, natives, extension, reference, referenceExtBox, s);
            Check(TargetsEqual(state.Applied, before) && !state.IconsDrawn, "lifecycle/off-repeat-idempotent");

            // 3) 开关 ON：几何不变，自有 icons 回来（接管原生槽）
            ApplyAction(state, MapOverviewAction.LayoutWithIcons, natives, extension, reference, referenceExtBox, s);
            Check(TargetsEqual(state.Applied, reference) && state.IconsDrawn && state.NativeSuppressed,
                "lifecycle/on-icons-back");

            // 4) 再 OFF：几何仍保持（不被 Teardown 撤回），icons 撤、原生槽恢复
            ApplyAction(state, MapOverviewAction.LayoutWithoutIcons, natives, extension, reference, referenceExtBox, s);
            Check(TargetsEqual(state.Applied, reference) && !state.IconsDrawn && !state.NativeSuppressed,
                "lifecycle/off-again-keeps-geometry");

            // 5) menu 关闭（Teardown）：完整还原；再 OFF 打开仍能定位（与首次目标逐值一致）
            RestoreState(state);
            Check(state.Applied == null && !state.IconsDrawn && !state.NativeSuppressed, "lifecycle/menu-close-restores");
            ApplyAction(state, MapOverviewAction.LayoutWithoutIcons, natives, extension, reference, referenceExtBox, s);
            Check(TargetsEqual(state.Applied, reference) && state.ExtensionBoxInBand, "lifecycle/reopen-relocates");

            // 6) 未注册/从未访问且 OFF（决策 Teardown）：恢复原生几何
            RestoreState(state);
            ApplyAction(state, MapOverviewAction.Teardown, natives, extension, reference, referenceExtBox, s);
            Check(state.Applied == null && !state.IconsDrawn && !state.NativeSuppressed, "lifecycle/unregistered-restores-native");

            // 7) 未登记 extra（数组新增但不是登记实例）：决策表对 owned 也是 Teardown，不画/不重排
            MapOverviewAction extra = MapOverviewLifecycle.DecideForOwnedLand(false, false, true);
            Check(extra == MapOverviewAction.Teardown, "lifecycle/unregistered-extra-teardown");
        }

        private sealed class LifecycleState
        {
            internal List<MapOverviewClusterTarget> Applied;
            internal bool IconsDrawn;
            internal bool NativeSuppressed;
            internal bool ExtensionBoxInBand;
            internal MapIconBox ExtensionBox;
        }

        /// <summary>执行生产动作（模拟运行时 glue）：Teardown=完整还原；无 icons=撤 icons 但保持几何；有 icons=几何+接管。</summary>
        private static void ApplyAction(LifecycleState state, MapOverviewAction action,
            List<MapOverviewClusterInput> natives, in MapOverviewClusterInput extension,
            List<MapOverviewClusterTarget> reference, in MapIconBox referenceExtBox, float scale)
        {
            if (action == MapOverviewAction.Teardown)
            {
                RestoreState(state);
                return;
            }
            // 生产 off 路径顺序 = WithdrawIcons() + EnsureOverviewLayout()；两者都在此模型中反映为：
            state.NativeSuppressed = action == MapOverviewAction.LayoutWithIcons;
            state.IconsDrawn = action == MapOverviewAction.LayoutWithIcons;
            var targets = new List<MapOverviewClusterTarget>();
            if (!MapOverviewLayout.TryPlan(natives, 314f, 208f, 18f, targets, out float s))
            {
                state.Applied = null;
                return;
            }
            state.Applied = targets;
            MapOverviewLayout.TryPlanExtension(extension, s, 314f, 208f, 18f, out MapOverviewClusterTarget extTarget);
            state.ExtensionBox = MapOverviewLayout.ArtBoxOf(extension, extTarget, 314f, 208f);
            MapIconBox band = MapOverviewLayout.BandRegion(314f, 208f, 18f);
            state.ExtensionBoxInBand = state.ExtensionBox.X0 >= band.X0 - 0.01f &&
                state.ExtensionBox.X1 <= band.X1 + 0.01f &&
                state.ExtensionBox.Y0 >= band.Y0 - 0.01f && state.ExtensionBox.Y1 <= band.Y1 + 0.01f;
        }

        private static void RestoreState(LifecycleState state)
        {
            state.Applied = null;
            state.IconsDrawn = false;
            state.NativeSuppressed = false;
            state.ExtensionBoxInBand = false;
            state.ExtensionBox = default;
        }

        private static bool TargetsEqual(List<MapOverviewClusterTarget> a, List<MapOverviewClusterTarget> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!Near(a[i].X, b[i].X) || !Near(a[i].Y, b[i].Y) ||
                    !Near(a[i].ScaleX, b[i].ScaleX) || !Near(a[i].ScaleY, b[i].ScaleY)) return false;
            }
            return true;
        }

        /// <summary>原 10 两两差值同一倍率 s（逐对直接比较差值，避免零差值除零假失败）。</summary>
        private static bool PairwiseRatio(List<MapOverviewClusterInput> inputs, List<MapOverviewClusterTarget> targets,
            float s)
        {
            if (targets == null || targets.Count != inputs.Count) return false;
            for (int i = 0; i < inputs.Count; i++)
            {
                for (int j = i + 1; j < inputs.Count; j++)
                {
                    if (!Near(targets[i].X - targets[j].X, s * (inputs[i].OrigX - inputs[j].OrigX), 0.01f)) return false;
                    if (!Near(targets[i].Y - targets[j].Y, s * (inputs[i].OrigY - inputs[j].OrigY), 0.01f)) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 实机有效视口（r10）：nominal-fit 但实际可见底界被裁的红例；带 X0/Y0 的有效域；较矮视口；
        /// 原 10 同倍率；扩展 art/outline/click 域（clone 候选的真实 snapshot 盒）；视口缓存/稳定/重开/ONOFF。
        /// 真实 screen 投影（相机/像素 mask 交集）只能在 runtime 读回，pure 层验证"输入契约 + 几何不变量"。
        /// </summary>
        private static void LiveViewport()
        {
            List<NativeCluster> clusters = LoadNativeClusters(out _);
            Check(clusters != null && clusters.Count == 10, "viewport/fixture");
            if (clusters == null || clusters.Count != 10) return;
            var natives = new List<MapOverviewClusterInput>();
            foreach (NativeCluster c in clusters) natives.Add(c.ToInput());
            const float PaperW = 314f, PaperH = 208f, Band = 18f;
            // 扩展 clone 候选（既有真实 snapshot 尺寸）：art/outline/click 同盒（button 与 outline 的 sd/scale 一致）
            var extHermes = new MapOverviewClusterInput(0f, 0f, 1f, 1f, 1f, 76f, 88f, 0f, 0f);
            var extOracle = new MapOverviewClusterInput(0f, 0f, 1f, 1f, 1f, 57f, 41f, 0f, 0f);

            // ---- 红例：名义 fit（整张 paper）在"实际可见底界 y=16"下被裁 ----
            var nominalTargets = new List<MapOverviewClusterTarget>();
            MapOverviewLayout.TryPlan(natives, PaperW, PaperH, Band, nominalTargets, out float nominalScale);
            MapOverviewLayout.TryPlanExtension(extOracle, nominalScale, PaperW, PaperH, Band,
                out MapOverviewClusterTarget nominalTarget);
            MapIconBox nominalArt = MapOverviewLayout.ArtBoxOf(extOracle, nominalTarget, PaperW, PaperH);
            var visibleClipped = new MapIconBox(0f, 16f, PaperW, PaperH);
            Check(nominalArt.Y0 < visibleClipped.Y0 - 1f,
                "viewport/red-nominal-fit-clipped y0=" + nominalArt.Y0 + " visibleY0=" + visibleClipped.Y0);
            // 绿：同一可见域输入 → 扩展完整落在可见底带内
            var targetsA = new List<MapOverviewClusterTarget>();
            MapOverviewLayout.TryPlan(natives, PaperW, PaperH, Band, visibleClipped, targetsA, out float scaleA);
            MapOverviewLayout.TryPlanExtension(extOracle, scaleA, PaperW, PaperH, Band, visibleClipped,
                out MapOverviewClusterTarget targetA);
            MapIconBox artA = MapOverviewLayout.ArtBoxOf(extOracle, targetA, PaperW, PaperH);
            MapIconBox bandA = MapOverviewLayout.BandRegion(PaperW, PaperH, Band, visibleClipped);
            Check(artA.Y0 >= visibleClipped.Y0 - 0.01f && artA.Y1 <= visibleClipped.Y1 + 0.01f,
                "viewport/green-extra-visible");
            Check(artA.X0 >= bandA.X0 - 0.01f && artA.X1 <= bandA.X1 + 0.01f &&
                  artA.Y0 >= bandA.Y0 - 0.01f && artA.Y1 <= bandA.Y1 + 0.01f, "viewport/green-extra-in-band");
            CheckArtInRegion(natives, PaperW, PaperH, Band, visibleClipped, scaleA, "clipped");

            // ---- 带 X0/Y0 offset 的有效域 ----
            var visibleOffset = new MapIconBox(10f, 25f, 300f, 180f);
            var targetsB = new List<MapOverviewClusterTarget>();
            MapOverviewLayout.TryPlan(natives, PaperW, PaperH, Band, visibleOffset, targetsB, out float scaleB);
            MapOverviewLayout.TryPlanExtension(extHermes, scaleB, PaperW, PaperH, Band, visibleOffset,
                out MapOverviewClusterTarget targetB);
            MapIconBox artB = MapOverviewLayout.ArtBoxOf(extHermes, targetB, PaperW, PaperH);
            MapIconBox bandB = MapOverviewLayout.BandRegion(PaperW, PaperH, Band, visibleOffset);
            Check(artB.X0 >= bandB.X0 - 0.01f && artB.X1 <= bandB.X1 + 0.01f &&
                  artB.Y0 >= bandB.Y0 - 0.01f && artB.Y1 <= bandB.Y1 + 0.01f &&
                  artB.X0 >= visibleOffset.X0 - 0.01f && artB.X1 <= visibleOffset.X1 + 0.01f,
                "viewport/offset-extra-domain");
            CheckArtInRegion(natives, PaperW, PaperH, Band, visibleOffset, scaleB, "offset");

            // ---- 较矮视口（不同高度）----
            var visibleShort = new MapIconBox(0f, 0f, PaperW, 120f);
            var targetsC = new List<MapOverviewClusterTarget>();
            MapOverviewLayout.TryPlan(natives, PaperW, PaperH, Band, visibleShort, targetsC, out float scaleC);
            MapOverviewLayout.TryPlanExtension(extHermes, scaleC, PaperW, PaperH, Band, visibleShort,
                out MapOverviewClusterTarget targetC);
            MapIconBox artC = MapOverviewLayout.ArtBoxOf(extHermes, targetC, PaperW, PaperH);
            MapIconBox bandC = MapOverviewLayout.BandRegion(PaperW, PaperH, Band, visibleShort);
            Check(artC.Y1 <= bandC.Y1 + 0.01f && artC.Y0 >= bandC.Y0 - 0.01f &&
                  bandC.Y1 < 40f && bandC.Y1 > 10f, "viewport/short-extra-in-band bandTop=" + bandC.Y1);
            CheckArtInRegion(natives, PaperW, PaperH, Band, visibleShort, scaleC, "short");

            // ---- V1：退化/已知薄矩形不再被扩大回整张 paper ----
            // 反向/零面积输入 → invalid（空盒），且 planner 拒绝提交
            MapIconBox degenerate = MapOverviewLayout.EffectiveContent(PaperW, PaperH, Band,
                new MapIconBox(110f, 60f, 100f, 50f));
            Check(degenerate.Width <= 0.01f || degenerate.Height <= 0.01f,
                "viewport/degenerate-invalid(empty)");
            // 小而合法（10×10）→ 原样保留（不扩大、不冒充全纸）
            MapIconBox smallKnown = MapOverviewLayout.EffectiveContent(PaperW, PaperH, Band,
                new MapIconBox(100f, 50f, 110f, 60f));
            Check(Near(smallKnown.Width, 10f) && Near(smallKnown.Height, 10f),
                "viewport/known-small-kept-not-expanded");
            var degenerateTargets = new List<MapOverviewClusterTarget>();
            Check(!MapOverviewLayout.TryPlan(natives, PaperW, PaperH, Band,
                      new MapIconBox(0f, 0f, 0f, 0f), degenerateTargets, out _),
                "viewport/invalid-visible-no-commit");
            MapIconBox thin = MapOverviewLayout.EffectiveContent(PaperW, PaperH, Band,
                new MapIconBox(0f, 0f, PaperW, 6f));
            Check(thin.Y1 <= 6.01f && thin.Y1 < PaperH - Band,
                "viewport/known-thin-not-expanded y1=" + thin.Y1);
            Check(!MapViewportPolicy.AcceptPaperRect(thin), "viewport/known-thin-below-ui-threshold");

            // ---- 扩展 art/outline/click 域：两候选盒都必须在有效带内（真实 snapshot 盒，outline==button 盒）----
            foreach ((float w, float h) in new[] { (76f, 88f), (57f, 41f) })
            {
                var ext = new MapOverviewClusterInput(0f, 0f, 1f, 1f, 1f, w, h, 0f, 0f);
                var targets0 = new List<MapOverviewClusterTarget>();
                MapOverviewLayout.TryPlan(natives, PaperW, PaperH, Band, targets0, out float s0);
                MapOverviewLayout.TryPlanExtension(ext, s0, PaperW, PaperH, Band, visibleClipped,
                    out MapOverviewClusterTarget t);
                MapIconBox art = MapOverviewLayout.ArtBoxOf(ext, t, PaperW, PaperH);
                Check(art.X0 >= visibleClipped.X0 - 0.01f && art.X1 <= visibleClipped.X1 + 0.01f &&
                      art.Y0 >= visibleClipped.Y0 - 0.01f && art.Y1 <= visibleClipped.Y1 + 0.01f,
                    "viewport/extra-box-" + w + "x" + h);
            }

            // ---- 视口缓存/稳定：签名、Defer→ApplyNow、detail 瞬时值不进、ONOFF/重开不累计 ----
            long sigA = MapOverviewCache.Signature(visibleClipped, PaperW, PaperH, 1280, 720);
            long sigB = MapOverviewCache.Signature(visibleOffset, PaperW, PaperH, 1280, 720);
            long sigC = MapOverviewCache.Signature(visibleShort, PaperW, PaperH, 1280, 720);
            Check(sigA != sigB && sigB != sigC && sigA != sigC, "viewport/signature-distinct");
            Check(MapOverviewCache.Signature(visibleClipped, PaperW, PaperH, 1280, 720) == sigA,
                "viewport/signature-stable");
            // V3：不同帧才推进稳定；同帧（11 个 UILand 回调/同帧 tick）不能当连续稳定帧；首次不提前成功
            Check(MapOverviewCache.Decide(false, sigA, long.MinValue, long.MinValue, 10, -1) ==
                  MapOverviewCache.Action.Defer, "viewport/cache-first-defer");
            Check(MapOverviewCache.Decide(false, sigA, long.MinValue, sigA, 10, 10) ==
                  MapOverviewCache.Action.Defer, "viewport/cache-same-frame-not-stable");
            Check(MapOverviewCache.Decide(false, sigA, long.MinValue, sigA, 11, 10) ==
                  MapOverviewCache.Action.ApplyNow, "viewport/cache-two-frames-apply");
            Check(MapOverviewCache.Decide(true, sigA, sigA, sigA, 10, 11) == MapOverviewCache.Action.Keep,
                "viewport/cache-keep");
            Check(MapOverviewCache.Decide(true, sigB, sigA, long.MinValue, 12, -1) ==
                  MapOverviewCache.Action.Defer, "viewport/cache-defer-first-change");
            Check(MapOverviewCache.Decide(true, sigB, sigA, sigB, 12, 12) ==
                  MapOverviewCache.Action.Defer, "viewport/cache-change-same-frame-defer");
            Check(MapOverviewCache.Decide(true, sigB, sigA, sigB, 13, 12) ==
                  MapOverviewCache.Action.ApplyNow, "viewport/cache-change-two-frames-apply");
            Check(MapOverviewCache.Decide(true, sigA, sigA, sigB, 14, 12) ==
                  MapOverviewCache.Action.Keep, "viewport/cache-transient-not-applied");
            Check(MapOverviewCache.Decide(true, sigC, sigA, sigB, 13, 12) ==
                  MapOverviewCache.Action.Defer, "viewport/cache-detail-transient-defer");

            // V1：像素/UI 阈值分离（18px 交集 ≈ 6 UI 单位：像素可通过、UI 必须拒绝）
            Check(!MapViewportPolicy.AcceptScreenIntersection(15f, 200f), "viewport/policy-pixel-thin-rejected");
            Check(MapViewportPolicy.AcceptScreenIntersection(18f, 200f), "viewport/policy-pixel-18-ok");
            Check(!MapViewportPolicy.AcceptPaperRect(new MapIconBox(0f, 0f, 314f, 6f)),
                "viewport/policy-ui-6-rejected");
            Check(MapViewportPolicy.AcceptPaperRect(new MapIconBox(0f, 0f, 314f, 8f)),
                "viewport/policy-ui-8-ok");
            Check(MapViewportPolicy.MaxProbeFrames >= 2 && MapViewportPolicy.MaxProbeFrames <= 8,
                "viewport/policy-bounded-probe-frames");

            // R3：几何提交后的 native 刷新取数（focus→reign）。0=current 仅此一路；历史逆序；无效拒绝。
            Check(MapOverviewRefresh.TryResolvePreviousIndex(0, 5, out int ri0) &&
                  ri0 == MapOverviewRefresh.CurrentReign, "refresh/focus0-current");
            Check(MapOverviewRefresh.TryResolvePreviousIndex(1, 5, out int ri1) && ri1 == 4,
                "refresh/focus1-most-recent");
            Check(MapOverviewRefresh.TryResolvePreviousIndex(5, 5, out int ri5) && ri5 == 0,
                "refresh/focus5-earliest");
            Check(!MapOverviewRefresh.TryResolvePreviousIndex(6, 5, out int ri6) &&
                  ri6 != MapOverviewRefresh.CurrentReign, "refresh/focus6-out-of-range");
            Check(!MapOverviewRefresh.TryResolvePreviousIndex(1, 0, out _), "refresh/empty-previous");
            Check(!MapOverviewRefresh.TryResolvePreviousIndex(-1, 5, out int rim1) &&
                  rim1 != MapOverviewRefresh.CurrentReign, "refresh/negative-no-fallback");
            Check(!MapOverviewRefresh.TryResolvePreviousIndex(int.MinValue, 5, out _),
                "refresh/int-min-no-fallback");
            Check(MapOverviewRefresh.TryResolvePreviousIndex(3, 3, out int ri3) && ri3 == 0,
                "refresh/focus-count-earliest");

            // 场景模型（tick 跨帧语义）：首次两帧一致才提交；detail 瞬时值不进；看过的新值两帧一致才换；
            // 重开（Teardown 清状态）后同视口重算必须逐值一致（无累计）。
            var viewState = new List<MapOverviewClusterTarget>();
            long applied = long.MinValue, pending = long.MinValue;
            int pendingFrame = -1;
            bool HasLayout = false;
            ApplyViewportDecision(viewState, ref HasLayout, ref applied, ref pending, ref pendingFrame,
                sigA, 10, natives, extOracle, visibleClipped, PaperW, PaperH, Band);
            Check(!HasLayout, "viewport/first-measurement-defer-not-early-success");
            ApplyViewportDecision(viewState, ref HasLayout, ref applied, ref pending, ref pendingFrame,
                sigA, 11, natives, extOracle, visibleClipped, PaperW, PaperH, Band);
            Check(HasLayout && applied == sigA, "viewport/two-frames-commit");
            var layoutA = new List<MapOverviewClusterTarget>(viewState);
            ApplyViewportDecision(viewState, ref HasLayout, ref applied, ref pending, ref pendingFrame,
                sigC, 12, natives, extOracle, visibleShort, PaperW, PaperH, Band);
            Check(TargetsEqual(viewState, layoutA) && applied == sigA, "viewport/detail-transient-keeps-layout");
            ApplyViewportDecision(viewState, ref HasLayout, ref applied, ref pending, ref pendingFrame,
                sigA, 13, natives, extOracle, visibleClipped, PaperW, PaperH, Band);
            Check(TargetsEqual(viewState, layoutA) && applied == sigA, "viewport/return-world-keep");
            ApplyViewportDecision(viewState, ref HasLayout, ref applied, ref pending, ref pendingFrame,
                sigB, 14, natives, extOracle, visibleOffset, PaperW, PaperH, Band);
            Check(TargetsEqual(viewState, layoutA) && applied == sigA, "viewport/first-change-deferred");
            ApplyViewportDecision(viewState, ref HasLayout, ref applied, ref pending, ref pendingFrame,
                sigB, 15, natives, extOracle, visibleOffset, PaperW, PaperH, Band);
            Check(applied == sigB && !TargetsEqual(viewState, layoutA), "viewport/second-frame-change-applied");
            var layoutB = new List<MapOverviewClusterTarget>(viewState);
            ApplyViewportDecision(viewState, ref HasLayout, ref applied, ref pending, ref pendingFrame,
                sigB, 16, natives, extOracle, visibleOffset, PaperW, PaperH, Band);
            Check(TargetsEqual(viewState, layoutB), "viewport/stable-keep");
            // 重开（Teardown：HasLayout/applied/pending 归零）→ 同一视口重算必须与上次逐值一致（无累计）
            HasLayout = false; applied = long.MinValue; pending = long.MinValue; pendingFrame = -1;
            ApplyViewportDecision(viewState, ref HasLayout, ref applied, ref pending, ref pendingFrame,
                sigB, 20, natives, extOracle, visibleOffset, PaperW, PaperH, Band);
            ApplyViewportDecision(viewState, ref HasLayout, ref applied, ref pending, ref pendingFrame,
                sigB, 21, natives, extOracle, visibleOffset, PaperW, PaperH, Band);
            Check(TargetsEqual(viewState, layoutB), "viewport/reopen-no-accumulation");

        }

        /// <summary>有效域输入下原 10 art 全部落在有效内容区（带以上），且两两差值同一倍率 s。</summary>
        private static void CheckArtInRegion(List<MapOverviewClusterInput> natives, float paperW, float paperH,
            float band, in MapIconBox visible, float scale, string label)
        {
            var targets = new List<MapOverviewClusterTarget>();
            Check(MapOverviewLayout.TryPlan(natives, paperW, paperH, band, visible, targets, out float s) &&
                  Near(s, scale), "viewport/" + label + "/scale-consistent");
            MapIconBox content = MapOverviewLayout.ContentRegion(paperW, paperH, band, visible);
            for (int i = 0; i < natives.Count; i++)
            {
                MapIconBox art = MapOverviewLayout.ArtBoxOf(natives[i], targets[i], paperW, paperH);
                Check(art.X0 >= content.X0 - 0.01f && art.X1 <= content.X1 + 0.01f &&
                      art.Y0 >= content.Y0 - 0.01f && art.Y1 <= content.Y1 + 0.01f,
                    "viewport/" + label + "/art-in-content#" + i);
            }
            Check(PairwiseRatio(natives, targets, s), "viewport/" + label + "/pairwise-uniform");
        }

        /// <summary>场景模型：按缓存决策执行"重算/保持"（重算 = 用同一有效域从冻结原始值绝对重算）。</summary>
        private static void ApplyViewportDecision(List<MapOverviewClusterTarget> state, ref bool hasLayout,
            ref long applied, ref long pending, ref int pendingFrame, long measured, int measuredFrame,
            List<MapOverviewClusterInput> natives, in MapOverviewClusterInput extension,
            in MapIconBox visible, float paperW, float paperH, float band)
        {
            MapOverviewCache.Action action = MapOverviewCache.Decide(hasLayout, measured, applied, pending,
                measuredFrame, pendingFrame);
            if (action == MapOverviewCache.Action.Keep) return;
            if (action == MapOverviewCache.Action.Defer)
            {
                pending = measured;
                pendingFrame = measuredFrame;
                return;
            }
            var targets = new List<MapOverviewClusterTarget>();
            MapOverviewLayout.TryPlan(natives, paperW, paperH, band, visible, targets, out float s);
            state.Clear();
            state.AddRange(targets);
            hasLayout = true;
            applied = measured;
            pending = measured;
            pendingFrame = measuredFrame;
        }

        /// <summary>
        /// r14/WORLD-OVERVIEW-ONLY 展示决策（纯）：native 只读状态 0/1（无第二阶段值）、未知/读取异常一律"撤"、
        /// 首次建组即落到目标态、已有组只在目标态变化时写（AlreadyApplied ⇒ None）。
        /// </summary>
        private static void Presentation()
        {
            Check(MapOverviewPresentationPolicy.ShowingWorld == 0 &&
                  MapOverviewPresentationPolicy.ShowingSingleIsland == 1, "presentation/native-state-values");
            Check(MapOverviewPresentationPolicy.TryDesiredVisible(0, out bool visWorld) && !visWorld,
                "presentation/world-hides");
            Check(MapOverviewPresentationPolicy.TryDesiredVisible(1, out bool visSingle) && visSingle,
                "presentation/single-shows");
            Check(!MapOverviewPresentationPolicy.TryDesiredVisible(2, out _), "presentation/unknown-no-desire");
            Check(!MapOverviewPresentationPolicy.TryDesiredVisible(-1, out _), "presentation/negative-no-desire");
            Check(MapOverviewPresentationPolicy.NeutralAlpha == 1f &&
                  MapOverviewPresentationPolicy.HiddenAlpha == 0f &&
                  MapOverviewPresentationPolicy.NeutralBlocksRaycasts &&
                  !MapOverviewPresentationPolicy.HiddenBlocksRaycasts, "presentation/neutral-vs-hidden-values");

            MapPresentationAction action = MapOverviewPresentationPolicy.Decide(true, true, true, false, true, 0, false,
                out MapPresentationReason reason);
            Check(action == MapPresentationAction.Withdraw && reason == MapPresentationReason.ReadFault,
                "presentation/read-fault-withdraw");
            action = MapOverviewPresentationPolicy.Decide(false, true, false, false, false, 0, false, out reason);
            Check(action == MapPresentationAction.Withdraw && reason == MapPresentationReason.BindingInvalid,
                "presentation/binding-invalid-withdraw");
            action = MapOverviewPresentationPolicy.Decide(false, true, true, true, false, 0, false, out reason);
            Check(action == MapPresentationAction.Withdraw && reason == MapPresentationReason.ExternalGroup,
                "presentation/external-group-no-takeover");
            action = MapOverviewPresentationPolicy.Decide(false, false, true, false, true, 0, true, out reason);
            Check(action == MapPresentationAction.Withdraw && reason == MapPresentationReason.ScopeInactive,
                "presentation/scope-inactive-withdraw");
            action = MapOverviewPresentationPolicy.Decide(false, true, true, false, true, 7, false, out reason);
            Check(action == MapPresentationAction.Withdraw && reason == MapPresentationReason.UnknownState,
                "presentation/unknown-state-withdraw");
            action = MapOverviewPresentationPolicy.Decide(false, true, true, false, false, 0, false, out reason);
            Check(action == MapPresentationAction.Hide && reason == MapPresentationReason.NewGroup,
                "presentation/new-group-world-hidden");
            action = MapOverviewPresentationPolicy.Decide(false, true, true, false, false, 1, true, out reason);
            Check(action == MapPresentationAction.Show && reason == MapPresentationReason.NewGroup,
                "presentation/new-group-single-neutral");
            action = MapOverviewPresentationPolicy.Decide(false, true, true, false, true, 0, false, out reason);
            Check(action == MapPresentationAction.Hide && reason == MapPresentationReason.Applied,
                "presentation/world-applies-hide");
            action = MapOverviewPresentationPolicy.Decide(false, true, true, false, true, 0, true, out reason);
            Check(action == MapPresentationAction.None && reason == MapPresentationReason.AlreadyApplied,
                "presentation/world-stable-no-write");
            action = MapOverviewPresentationPolicy.Decide(false, true, true, false, true, 1, true, out reason);
            Check(action == MapPresentationAction.Show && reason == MapPresentationReason.Applied,
                "presentation/single-restores");
            action = MapOverviewPresentationPolicy.Decide(false, true, true, false, true, 1, false, out reason);
            Check(action == MapPresentationAction.None && reason == MapPresentationReason.AlreadyApplied,
                "presentation/single-stable-no-write");
        }

        /// <summary>
        /// r15/mount-island-capacity 纯覆盖：world 域共同分区不夹回 paper；原 10 绝对统一 s+t；
        /// 宽岛两行真实 16 尺寸（0.7 目标 161.9×45.1）、降级/失败边界、RequestIndex、船标障碍、17 项不截断。
        /// </summary>
        private static void WorldCapacity()
        {
            // 1) 域不夹回 paper（允许负起点 / X1 > paperW）
            var world = new MapIconBox(-4f, -3f, 318f, 212f);
            Check(MapWorldLayout.ComposeDomains(world, 18f, 52f, out MapIconBox upper, out MapIconBox band),
                "world/domains-ok");
            Check(Near(upper.X0, -2f) && Near(upper.X1, 316f) && Near(band.Y0, -1f) && Near(band.X1, 316f),
                "world/domains-raw-no-clamp upper=" + upper.X0 + "," + upper.X1 + " band=" + band.Y0 + "," + band.X1);
            Check(MapWorldLayout.ComposeDomains(world, 18f, 0f, out MapIconBox noBandUpper, out MapIconBox noBand),
                "world/domains-no-extension");
            Check(noBand.Width <= 0f && noBandUpper.Height > 120f, "world/no-extension-upper-full");

            // 2) 原 10 绝对统一 fit（真实 fixture 输入 + 全纸域）
            List<NativeCluster> clusters = LoadNativeClusters(out _);
            var natives = new List<MapOverviewClusterInput>();
            foreach (NativeCluster c in clusters) natives.Add(c.ToInput());
            var targets2 = new List<MapOverviewClusterTarget>();
            Check(MapWorldLayout.TryFitNatives(natives, 314f, 208f, new MapIconBox(2f, 60f, 312f, 190f),
                targets2, out float scale), "world/native-fit");
            float err = 0f;
            for (int i = 0; i < natives.Count; i++)
            {
                for (int j = i + 1; j < natives.Count; j++)
                {
                    err = Math.Max(err, Math.Abs((targets2[i].X - targets2[j].X) - scale * (natives[i].OrigX - natives[j].OrigX)));
                    err = Math.Max(err, Math.Abs((targets2[i].Y - targets2[j].Y) - scale * (natives[i].OrigY - natives[j].OrigY)));
                }
            }
            Check(scale > 0.3f && err <= 0.01f, "world/native-uniform s=" + scale + " err=" + err);

            // 3) 宽岛两行：真实 16 尺寸（type→尺寸表与 capacity-inputs 一致）
            float[] widths = { 25, 24, 21, 25, 21, 21, 29, 29, 26, 28, 28, 40, 22, 24, 40, 20 };
            float[] heights = { 16, 22, 26, 17, 18, 18, 13, 13, 14, 18, 13, 25, 19, 32, 26, 20 };
            var sixteen = new List<MapIconRequest>();
            for (int i = 0; i < 16; i++) sixteen.Add(new MapIconRequest(MapIconKind.Steed, 100 + i, i, widths[i], heights[i]));
            Check(MapExtensionIslandLayout.TrySplitTwoRows(sixteen, new List<int>(8), new List<int>(8),
                out float wA, out float wB, out float hA, out float hB), "ext/rows-split");
            Check(Math.Abs((wA + wB) - 423f) <= 0.01f && Math.Abs(wA - wB) <= 40f && hA + hB <= 58.1f,
                "ext/rows-balanced wa=" + wA + " wb=" + wB + " ha=" + hA + " hb=" + hB);
            MapExtensionIslandLayout.RequiredSize(0.7f, wA, wB, hA, hB, 8, 8, out float needW, out float needH);
            Check(Near(needW, 161.9f, 0.3f) && Near(needH, 45.1f, 0.3f),
                "ext/need-0.7 " + needW + "x" + needH);

            // 4) 0.7 目标区（含 20 UI 状态车道 + 边距 = 185.9x49.1）→ 16/16 且 RequestIndex 正确
            var banner = new MapIconBox(0f, 0f, 185.9f, 49.1f);
            var placements = new List<MapIconPlacement>();
            Check(MapExtensionIslandLayout.TryPlan(banner, sixteen, new List<MapIconBox>(), placements,
                out float used, out int failed), "ext/plan-16 ok used=" + used + " failed=" + failed);
            Check(placements.Count == 16 && failed == 0 && Near(used, 0.7f, 0.001f), "ext/16-at-0.7 used=" + used);
            bool indexOk = true;
            for (int i = 0; i < placements.Count; i++)
            {
                if (placements[i].RequestIndex < 0 || placements[i].RequestIndex >= 16 ||
                    placements[i].Request.Width != sixteen[placements[i].RequestIndex].Width) indexOk = false;
            }
            Check(indexOk, "ext/request-index-identity");

            // 5) 更窄视口 → 降级但仍全量；过小 → 整体失败（不部分显示）
            var narrow = new MapIconBox(0f, 0f, 150f, 46f);
            Check(MapExtensionIslandLayout.TryPlan(narrow, sixteen, new List<MapIconBox>(), placements,
                out float narrowScale, out _) && placements.Count == 16 && narrowScale < 0.7f &&
                narrowScale >= MapExtensionIslandLayout.MinScale, "ext/narrow-degrade scale=" + narrowScale);
            var tiny = new MapIconBox(0f, 0f, 60f, 20f);
            Check(!MapExtensionIslandLayout.TryPlan(tiny, sixteen, new List<MapIconBox>(), placements,
                out _, out int tinyFailed) && placements.Count == 0 && tinyFailed == 16, "ext/tiny-failclosed");

            // 6) 真实船标障碍（20 车道内 6.4 单位方框）仍可放 16；纯障碍覆盖 → 失败不截断
            var boat = new List<MapIconBox> { new MapIconBox(178f, 20f, 184.4f, 26.4f) };
            Check(MapExtensionIslandLayout.TryPlan(banner, sixteen, boat, placements, out _, out _) &&
                placements.Count == 16, "ext/boat-obstacle-16");
            var blocked = new List<MapIconBox> { new MapIconBox(0f, 0f, 185.9f, 49.1f) };
            Check(!MapExtensionIslandLayout.TryPlan(banner, sixteen, blocked, placements, out _, out _) &&
                placements.Count == 0, "ext/blocked-failclosed");

            // 7) 空/单项/重复/超出容量：不截断、不补假
            Check(MapExtensionIslandLayout.TryPlan(banner, new List<MapIconRequest>(), new List<MapIconBox>(),
                placements, out _, out int emptyFailed) && placements.Count == 0 && emptyFailed == 0, "ext/empty-ok");
            Check(MapExtensionIslandLayout.TryPlan(banner, new List<MapIconRequest> { sixteen[0] },
                new List<MapIconBox>(), placements, out _, out _) && placements.Count == 1, "ext/single-ok");
            var dup = new List<MapIconRequest> { sixteen[0], sixteen[0] };
            Check(MapExtensionIslandLayout.TryPlan(banner, dup, new List<MapIconBox>(), placements, out _, out _) &&
                placements.Count == 2 && placements[0].RequestIndex != placements[1].RequestIndex, "ext/duplicate-distinct");
            var seventeen = new List<MapIconRequest>(sixteen) { new MapIconRequest(MapIconKind.Steed, 199, 16, 24f, 32f) };
            Check(!MapExtensionIslandLayout.TryPlan(banner, seventeen, new List<MapIconBox>(), placements,
                out _, out int overFailed) && placements.Count == 0 && overFailed == 17, "ext/17-failclosed");

            // 8) 空域/退化：整体失败（fail-closed）
            Check(!MapExtensionIslandLayout.TryPlan(new MapIconBox(0f, 0f, 0f, 0f), sixteen,
                new List<MapIconBox>(), placements, out _, out _), "ext/degenerate-failclosed");
        }

        // ------------------------------------------------------- geography report (offline evidence)

        /// <summary>
        /// 离线证据：真实 native fixture → 生产纯函数的统一倍率布局 + 每岛自由区域 + 容量裁决。
        /// 供 REPORT/operator 复核"哪些真实 fixture 放不下、fallback 是什么"，不参与断言。
        /// </summary>
        private static void WriteGeographyReport(string path)
        {
            List<NativeCluster> clusters = LoadNativeClusters(out string error);
            if (clusters == null || clusters.Count != 10)
            {
                throw new InvalidOperationException("native fixture unavailable: " + (error ?? "unknown"));
            }
            const float PaperW = 314f, PaperH = 208f, Band = 18f;
            var inputs = new List<MapOverviewClusterInput>();
            foreach (NativeCluster c in clusters) inputs.Add(c.ToInput());
            var targets = new List<MapOverviewClusterTarget>();
            MapOverviewLayout.TryPlan(inputs, PaperW, PaperH, Band, targets, out float s);
            var artBoxes = new List<MapIconBox>();
            var owners = new List<MapIconRegionOwner>();
            for (int i = 0; i < inputs.Count; i++)
            {
                artBoxes.Add(MapOverviewLayout.ArtBoxOf(inputs[i], targets[i], PaperW, PaperH));
                owners.Add(new MapIconRegionOwner(i, artBoxes[i]));
            }
            MapIconBox content = MapOverviewLayout.ContentRegion(PaperW, PaperH, Band);
            var rects = new List<MapIconBox>();
            var rectOwners = new List<int>();
            MapIconRegionPlanner.Build(owners, content, MapIconRegionPlanner.DefaultCell, rects, rectOwners);

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"paper\": [").Append(F(PaperW)).Append(", ").Append(F(PaperH)).Append("],\n");
            sb.Append("  \"buttonBand\": ").Append(F(Band)).Append(",\n");
            sb.Append("  \"uniformScale\": ").Append(F(s)).Append(",\n");
            sb.Append("  \"bandHeight\": ").Append(F(MapOverviewLayout.BandHeight(PaperH, Band))).Append(",\n");
            sb.Append("  \"contentRegion\": [").Append(F(content.X0)).Append(", ").Append(F(content.Y0)).Append(", ")
              .Append(F(content.X1)).Append(", ").Append(F(content.Y1)).Append("],\n");
            sb.Append("  \"clusters\": [\n");
            for (int i = 0; i < inputs.Count; i++)
            {
                var islandRegions = new List<MapIconBox>();
                float regionArea = 0f;
                for (int r = 0; r < rects.Count; r++)
                {
                    if (rectOwners[r] != i) continue;
                    islandRegions.Add(rects[r]);
                    regionArea += rects[r].Width * rects[r].Height;
                }
                var three = new List<MapIconRequest> { Req(MapIconKind.Steed, 37, 40, 28, 0),
                                                       Req(MapIconKind.Steed, 35, 40, 22, 1),
                                                       Req(MapIconKind.Hermit, 0, 18, 24, 0) };
                var merged = new List<MapIconPlacement>();
                bool threeOk = PlanAcrossRegions(three, islandRegions, artBoxes, out float threeScale);
                bool worstOk = PlanAcrossRegions(Worst10(), islandRegions, artBoxes, out float worstScale);
                bool stressOk = PlanAcrossRegions(Stress12(), islandRegions, artBoxes, out float stressScale);
                sb.Append("    {\"name\":").Append(Json(clusters[i].Name));
                sb.Append(",\"origAnchored\":[").Append(F(inputs[i].OrigX)).Append(",").Append(F(inputs[i].OrigY)).Append("]");
                sb.Append(",\"targetAnchored\":[").Append(F(targets[i].X)).Append(",").Append(F(targets[i].Y)).Append("]");
                sb.Append(",\"targetScale\":[").Append(F(targets[i].ScaleX)).Append(",").Append(F(targets[i].ScaleY)).Append("]");
                sb.Append(",\"artBox\":[").Append(F(artBoxes[i].X0)).Append(",").Append(F(artBoxes[i].Y0)).Append(",")
                  .Append(F(artBoxes[i].X1)).Append(",").Append(F(artBoxes[i].Y1)).Append("]");
                sb.Append(",\"regions\":").Append(islandRegions.Count).Append(",\"regionArea\":").Append(F(regionArea));
                sb.Append(",\"set3\":{\"ok\":").Append(threeOk ? "true" : "false").Append(",\"scale\":").Append(F(threeScale)).Append("}");
                sb.Append(",\"worst10\":{\"ok\":").Append(worstOk ? "true" : "false").Append(",\"scale\":").Append(F(worstScale)).Append("}");
                sb.Append(",\"stress12\":{\"ok\":").Append(stressOk ? "true" : "false").Append(",\"scale\":").Append(F(stressScale)).Append("}");
                if (threeOk)
                {
                    sb.Append(",\"placements3\":[");
                    for (int p = 0; p < merged.Count; p++)
                    {
                        if (p > 0) sb.Append(",");
                        sb.Append("{\"requestIndex\":").Append(merged[p].RequestIndex)
                          .Append(",\"x\":").Append(F(merged[p].X)).Append(",\"y\":").Append(F(merged[p].Y))
                          .Append(",\"scale\":").Append(F(merged[p].Scale)).Append("}");
                    }
                    sb.Append("]");
                }
                sb.Append("}").Append(i + 1 < inputs.Count ? "," : "").Append("\n");
            }
            sb.Append("  ],\n");
            sb.Append("  \"regionRects\": [");
            for (int r = 0; r < rects.Count; r++)
            {
                if (r > 0) sb.Append(",");
                sb.Append("{\"owner\":").Append(rectOwners[r]).Append(",\"rect\":[")
                  .Append(F(rects[r].X0)).Append(",").Append(F(rects[r].Y0)).Append(",")
                  .Append(F(rects[r].X1)).Append(",").Append(F(rects[r].Y1)).Append("]}");
            }
            sb.Append("],\n");

            // ---- extra（physical11 / UI10）关键 fixture：扩展区域与详情在真实 15/16 最大尺寸集下的容量 ----
            List<(float W, float H, MapIconKind Kind)> icons15 = MaxResourceIcons(15);
            List<(float W, float H, MapIconKind Kind)> icons16 = MaxResourceIcons(16);
            TryRealNativeLayout(out List<MapIconBox> nativeArt, out _);
            MapIconBox bandRegion = MapOverviewLayout.BandRegion(PaperW, PaperH, Band);
            sb.Append("  \"extra\": {\n");
            sb.Append("    \"physicalIndex\": 11, \"mapIndex\": 10,\n");
            sb.Append("    \"sizesSource\": \"icon-rects.json iconType 0/1/2 sorted by area desc\",\n");
            sb.Append("    \"bandRegion\": [").Append(F(bandRegion.X0)).Append(",").Append(F(bandRegion.Y0)).Append(",")
              .Append(F(bandRegion.X1)).Append(",").Append(F(bandRegion.Y1)).Append("],\n");
            sb.Append("    \"artFixtures\": [\n");
            float[][] artFixtures = { new[] { 76f, 88f }, new[] { 57f, 41f }, new[] { 40f, 36f } };
            for (int f = 0; f < artFixtures.Length; f++)
            {
                float artW = artFixtures[f][0], artH = artFixtures[f][1];
                var ext = new MapOverviewClusterInput(0f, 0f, 1f, 1f, 1f, artW, artH, 0f, 0f);
                MapOverviewLayout.TryPlanExtension(ext, s, PaperW, PaperH, Band, out MapOverviewClusterTarget extTarget);
                MapIconBox extArt = MapOverviewLayout.ArtBoxOf(ext, extTarget, PaperW, PaperH);
                var extOwners = new List<MapIconRegionOwner>(1) { new MapIconRegionOwner(11, extArt) };
                var extRects = new List<MapIconBox>();
                var extRectOwners = new List<int>();
                MapIconRegionPlanner.Build(extOwners, bandRegion, MapIconRegionPlanner.DefaultCell, extRects, extRectOwners);
                var blockers = new List<MapIconBox>(nativeArt);
                blockers.Add(extArt);
                bool ok15 = PlanAcrossRegions(ToRequests(icons15), extRects, blockers, out float scale15, out List<MapIconPlacement> placed15);
                bool ok16 = PlanAcrossRegions(ToRequests(icons16), extRects, blockers, out float scale16, out List<MapIconPlacement> placed16);
                sb.Append("      {\"art1\":[").Append(F(artW)).Append(",").Append(F(artH)).Append("]");
                sb.Append(",\"displayScale\":").Append(F(extTarget.ScaleX));
                sb.Append(",\"artBox\":[") .Append(F(extArt.X0)).Append(",").Append(F(extArt.Y0)).Append(",")
                  .Append(F(extArt.X1)).Append(",").Append(F(extArt.Y1)).Append("]");
                sb.Append(",\"regions\":").Append(extRects.Count);
                sb.Append(",\"set15\":{\"ok\":").Append(ok15 ? "true" : "false").Append(",\"scale\":").Append(F(scale15))
                  .Append(",\"placed\":").Append(placed15 == null ? 0 : placed15.Count).Append("}");
                sb.Append(",\"set16\":{\"ok\":").Append(ok16 ? "true" : "false").Append(",\"scale\":").Append(F(scale16))
                  .Append(",\"placed\":").Append(placed16 == null ? 0 : placed16.Count).Append("}");
                sb.Append("}").Append(f + 1 < artFixtures.Length ? "," : "").Append("\n");
            }
            sb.Append("    ],\n");
            var detailSurface = new MapIconSurface(-24f, -18f, 204f, 168f);
            detailSurface.AddBlocked(new MapIconBox(-38f, -44f, 38f, 44f));
            detailSurface.AddBlocked(new MapIconBox(-52f, 17f, -20f, 49f));
            var detailOut = new List<MapIconPlacement>();
            bool det15 = MapResourceIconPlanner.TryPlan(ToRequests(icons15), detailSurface, detailOut, out float detScale15, out int detFail15, false, MapResourceIconPlanner.DefaultScaleCount);
            bool det16 = MapResourceIconPlanner.TryPlan(ToRequests(icons16), detailSurface, detailOut, out float detScale16, out int detFail16, false, MapResourceIconPlanner.DefaultScaleCount);
            sb.Append("    \"detail\": {\"surface\":[-24,-18,204,168],\"blocked\":[[-38,-44,38,44],[-52,17,-20,49]],");
            sb.Append("\"set15\":{\"ok\":").Append(det15 && detFail15 == 0 ? "true" : "false").Append(",\"scale\":").Append(F(detScale15)).Append("},");
            sb.Append("\"set16\":{\"ok\":").Append(det16 && detFail16 == 0 ? "true" : "false").Append(",\"scale\":").Append(F(detScale16)).Append("}},\n");
            sb.Append("    \"note\": \"extension art fixtures are representative clone sizes; binding decides final art\"\n");
            sb.Append("  },\n");

            // ---- r10 有效视口 fixture：nominal 红例 / 带 offset 域 / 较矮视口 / 缓存决策 ----
            var reportNatives = new List<MapOverviewClusterInput>();
            foreach (NativeCluster c in clusters) reportNatives.Add(c.ToInput());
            var reportExt = new MapOverviewClusterInput(0f, 0f, 1f, 1f, 1f, 57f, 41f, 0f, 0f);
            var reportNominal = new List<MapOverviewClusterTarget>();
            MapOverviewLayout.TryPlan(reportNatives, PaperW, PaperH, Band, reportNominal, out float nominalS);
            MapOverviewLayout.TryPlanExtension(reportExt, nominalS, PaperW, PaperH, Band, out MapOverviewClusterTarget nominalExt);
            MapIconBox nominalBox = MapOverviewLayout.ArtBoxOf(reportExt, nominalExt, PaperW, PaperH);
            sb.Append("  \"viewport\": {\n");
            sb.Append("    \"nominal\": {\"scale\":").Append(F(nominalS)).Append(",\"extraArtBox\":[")
              .Append(F(nominalBox.X0)).Append(",").Append(F(nominalBox.Y0)).Append(",")
              .Append(F(nominalBox.X1)).Append(",").Append(F(nominalBox.Y1)).Append("]},\n");
            float[][] visibles = { new[] { 0f, 16f, 314f, 208f }, new[] { 10f, 25f, 300f, 180f }, new[] { 0f, 0f, 314f, 120f } };
            string[] labels = { "clippedBottom16", "offsetX0Y0", "short120" };
            sb.Append("    \"effective\": [\n");
            for (int v = 0; v < visibles.Length; v++)
            {
                var visible = new MapIconBox(visibles[v][0], visibles[v][1], visibles[v][2], visibles[v][3]);
                var viewportTargets = new List<MapOverviewClusterTarget>();
                MapOverviewLayout.TryPlan(reportNatives, PaperW, PaperH, Band, visible, viewportTargets,
                    out float sEffective);
                MapOverviewLayout.TryPlanExtension(reportExt, sEffective, PaperW, PaperH, Band, visible,
                    out MapOverviewClusterTarget extTarget);
                MapIconBox extBox = MapOverviewLayout.ArtBoxOf(reportExt, extTarget, PaperW, PaperH);
                MapIconBox bandBox = MapOverviewLayout.BandRegion(PaperW, PaperH, Band, visible);
                bool red = v == 0;
                sb.Append("      {\"name\":").Append(Json(labels[v])).Append(",\"rect\":[")
                  .Append(F(visible.X0)).Append(",").Append(F(visible.Y0)).Append(",").Append(F(visible.X1)).Append(",").Append(F(visible.Y1)).Append("]");
                sb.Append(",\"scale\":").Append(F(sEffective));
                sb.Append(",\"band\":[") .Append(F(bandBox.X0)).Append(",").Append(F(bandBox.Y0)).Append(",")
                  .Append(F(bandBox.X1)).Append(",").Append(F(bandBox.Y1)).Append("]");
                sb.Append(",\"extraArtBox\":[").Append(F(extBox.X0)).Append(",").Append(F(extBox.Y0)).Append(",")
                  .Append(F(extBox.X1)).Append(",").Append(F(extBox.Y1)).Append("]");
                sb.Append(",\"extraInsideVisible\":").Append(extBox.Y0 >= visible.Y0 - 0.01f && extBox.Y1 <= visible.Y1 + 0.01f ? "true" : "false");
                if (red)
                {
                    sb.Append(",\"nominalExtraClippedBelowVisible\":").Append(nominalBox.Y0 < visible.Y0 - 1f ? "true" : "false");
                }
                sb.Append("}").Append(v + 1 < visibles.Length ? "," : "").Append("\n");
            }
            sb.Append("    ],\n");
            long sigClipped = MapOverviewCache.Signature(new MapIconBox(0f, 16f, 314f, 208f), PaperW, PaperH, 1280, 720);
            long sigOffset = MapOverviewCache.Signature(new MapIconBox(10f, 25f, 300f, 180f), PaperW, PaperH, 1280, 720);
            sb.Append("    \"cache\": {\"firstFrameDefer\":\"");
            sb.Append(MapOverviewCache.Decide(false, sigClipped, long.MinValue, long.MinValue, 10, -1) == MapOverviewCache.Action.Defer ? "Defer" : "other");
            sb.Append("\",\"sameFrameDefer\":\"");
            sb.Append(MapOverviewCache.Decide(false, sigClipped, long.MinValue, sigClipped, 10, 10) == MapOverviewCache.Action.Defer ? "Defer" : "other");
            sb.Append("\",\"twoFrameApply\":\"");
            sb.Append(MapOverviewCache.Decide(false, sigClipped, long.MinValue, sigClipped, 11, 10) == MapOverviewCache.Action.ApplyNow ? "ApplyNow" : "other");
            sb.Append("\",\"transientDefer\":\"");
            sb.Append(MapOverviewCache.Decide(true, sigOffset, sigClipped, long.MinValue, 12, -1) == MapOverviewCache.Action.Defer ? "Defer" : "other");
            sb.Append("\",\"stableApply\":\"");
            sb.Append(MapOverviewCache.Decide(true, sigOffset, sigClipped, sigOffset, 13, 12) == MapOverviewCache.Action.ApplyNow ? "ApplyNow" : "other");
            sb.Append("\",\"sameKeep\":\"");
            sb.Append(MapOverviewCache.Decide(true, sigClipped, sigClipped, sigOffset, 14, 13) == MapOverviewCache.Action.Keep ? "Keep" : "other");
            sb.Append("\",\"refreshFocus0\":\"");
            sb.Append(MapOverviewRefresh.TryResolvePreviousIndex(0, 5, out int rp0) &&
                rp0 == MapOverviewRefresh.CurrentReign ? "current" : "other");
            sb.Append("\",\"refreshFocus1\":\"");
            sb.Append(MapOverviewRefresh.TryResolvePreviousIndex(1, 5, out int rp1) && rp1 == 4
                ? "previous[4]" : "other");
            sb.Append("\",\"refreshFocus5\":\"");
            sb.Append(MapOverviewRefresh.TryResolvePreviousIndex(5, 5, out int rp5) && rp5 == 0
                ? "previous[0]" : "other");
            sb.Append("\",\"refreshOutOfRange\":\"");
            sb.Append(!MapOverviewRefresh.TryResolvePreviousIndex(6, 5, out _) ? "rejected" : "other");
            sb.Append("\",\"refreshNegative\":\"");
            sb.Append(!MapOverviewRefresh.TryResolvePreviousIndex(-1, 5, out _) ? "rejected" : "other");
            sb.Append("\",\"pixelThreshold\":").Append(F(MapViewportPolicy.MinVisiblePixels));
            sb.Append(",\"uiThreshold\":").Append(F(MapViewportPolicy.MinVisibleUiSide));
            sb.Append(",\"maxProbeFrames\":").Append(MapViewportPolicy.MaxProbeFrames);
            sb.Append("},\n");
            sb.Append("    \"note\": \"screen-space projection (camera/mask/pixelRect) is runtime-read; pure layer verifies input contract + geometry\"\n");
            sb.Append("  },\n");
            sb.Append("  \"note\": \"pure-function offline evidence; no engine/UI run\"\n}\n");
            File.WriteAllText(path, sb.ToString());
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
