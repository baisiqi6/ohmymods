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
    /// - 岛内契约（incident #152）：native 图标只落在所属岛自己的底图盒内（MapIconRegionPlanner 岛外区域已删除）；
    /// - MapResourceIconPlanner：golden 向量逐值、不变量（surface 内/Gutter/RequestIndex/确定性）、
    ///   失败时不得部分展示（failed&gt;0 ⇒ 调用方必须丢弃）；
    /// - --geography-report <out.json>：把真实 native fixture（10 簇原始 anchoredPosition/art）喂给
    ///   **生产纯函数**，导出统一倍率布局、每岛底图盒与真实 fixture 岛内容量裁决（离线证据）。
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
            NativeShapeAndPaperScale();
            Extension();
            DetailCapacity();
            Lifecycle();
            LiveViewport();
            Presentation();
            WorldCapacity();
            ExtensionShoreShape();

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
        /// <summary>
        /// issue-152 纯函数契约（v3，plan-space identity 映射）：
        /// - native sprite mesh 拓扑预处理（canonical 顶点 / 退化与重复三角形 / 无向边界边 / 非流形 → Unknown）；
        /// - 形状包含（凹形海角、洞、跨洞细长盒、接缝/混合朝向不误判）；
        /// - uGUI `Image` Simple+useSpriteMesh 绘制公式（bounds 归一、pivot 锚定 preserveAspect、rect pivot 偏移）；
        /// - 自然 paper 倍率（art localScale × 全局 fit）与"请求换算到 paper 后 4/4"；
        /// - planner 的 mesh 约束不影响无形状调用的既有 golden 行为。
        /// 反例对旧实现（v2：ppu 归一 + 居中 FitAspect + 索引朝向边界判定）为红：见 shape/legacy-* 断言与
        /// probes/v3-red（编译冻结 v2-snapshot 源码运行同一组规范期望）。
        /// </summary>
        private static void NativeShapeAndPaperScale()
        {
            // ---- 1) 凹形（diamond）：bbox 四角是海 ⇒ 形状约束必须拒绝 ----
            // 顶点直接是规划空间坐标（v3：shape 使用 identity 映射）：40×30 的 bbox 内接 diamond。
            var box = new MapIconBox(0f, 0f, 40f, 30f);
            float[] diamondX = { 0f, 20f, 40f, 20f };
            float[] diamondY = { 15f, 30f, 15f, 0f };
            int[] diamondTris = { 0, 1, 2, 0, 2, 3 };
            Check(MapIconMeshShape.TryBuildTopology(diamondX, diamondY, 4, diamondTris,
                    out MapIconMeshTopology diamond, out string diamondReason),
                "shape/diamond-topology " + diamondReason);

            var seaCorner = new MapIconBox(1.5f, 21.5f, 9.5f, 29.5f);   // bbox 角（海）
            Check(!MapIconMeshShape.FootprintInside(diamond, seaCorner, MapIconMeshShape.DefaultEpsilon),
                "shape/diamond-sea-corner-rejected");
            var islandCenter = new MapIconBox(17f, 13f, 25f, 21f);      // 形内
            Check(MapIconMeshShape.FootprintInside(diamond, islandCenter, MapIconMeshShape.DefaultEpsilon),
                "shape/diamond-center-accepted");

            // planner 级：带形状时落点必须完整在形内（无形状时不施加该约束）
            var plainSurface = new MapIconSurface(0f, 0f, 40f, 30f);
            var plainOut = new List<MapIconPlacement>();
            var one = new List<MapIconRequest> { Req(MapIconKind.Steed, 35, 8, 8) };
            bool plainOk = MapResourceIconPlanner.TryPlan(one, plainSurface, plainOut, out _, out _);
            Check(plainOk && plainOut.Count == 1 &&
                  !MapIconMeshShape.FootprintInside(diamond, PlacementBox(plainOut[0]),
                      MapIconMeshShape.DefaultEpsilon),
                "shape/plain-bbox-accepts-sea-corner");   // v1/v2 行为（反例基线）

            var shapedSurface = new MapIconSurface(0f, 0f, 40f, 30f);
            shapedSurface.SetMeshShape(diamond);
            var shapedOut = new List<MapIconPlacement>();
            bool shapedOk = MapResourceIconPlanner.TryPlan(one, shapedSurface, shapedOut, out _, out int shapedFail);
            Check(shapedOk && shapedFail == 0 && shapedOut.Count == 1 &&
                  MapIconMeshShape.FootprintInside(diamond, PlacementBox(shapedOut[0]),
                      MapIconMeshShape.DefaultEpsilon),
                "shape/shaped-places-inside fail=" + shapedFail);

            // 形内被占满（只余海角）⇒ 带形状必须 fail-closed（绝不岛外兜底）
            float[] halfX = { 0f, 0f, 20f, 20f };
            float[] halfY = { 0f, 30f, 30f, 0f };
            int[] halfTris = { 0, 1, 2, 0, 2, 3 };
            Check(MapIconMeshShape.TryBuildTopology(halfX, halfY, 4, halfTris, out MapIconMeshTopology half,
                    out _), "shape/half-topology");
            var blockedSurface = new MapIconSurface(0f, 0f, 40f, 30f);
            blockedSurface.SetMeshShape(half);
            blockedSurface.AddBlocked(new MapIconBox(0f, 0f, 20f, 30f));   // 形内整半被占
            var blockedOut = new List<MapIconPlacement>();
            bool blockedOk = MapResourceIconPlanner.TryPlan(one, blockedSurface, blockedOut, out _, out int blockedFail);
            Check(!blockedOk && blockedFail > 0 && blockedOut.Count == 0,
                "shape/no-sea-fallback ok=" + blockedOk + " placed=" + blockedOut.Count);
            // 无形状的同一配置：bbox 规划会在形外右半放下（v1/v2 行为；这正是本回归要堵的路径）
            var bboxSurface = new MapIconSurface(0f, 0f, 40f, 30f);
            bboxSurface.AddBlocked(new MapIconBox(0f, 0f, 20f, 30f));
            var bboxOut = new List<MapIconPlacement>();
            bool bboxOk = MapResourceIconPlanner.TryPlan(one, bboxSurface, bboxOut, out _, out _);
            Check(bboxOk && bboxOut.Count == 1 &&
                  !MapIconMeshShape.FootprintInside(half, PlacementBox(bboxOut[0]),
                      MapIconMeshShape.DefaultEpsilon),
                "shape/bbox-fallback-is-outside-shape");

            // ---- 2) trimmed sprite：mesh bbox 与 rect 不一致时仍按真实形状判定 ----
            float[] trimmedX = { 0f, 40f, 40f, 0f };
            float[] trimmedY = { 10f, 10f, 30f, 30f };   // 只画中上部 20 高
            int[] trimmedTris = { 0, 1, 2, 0, 2, 3 };
            Check(MapIconMeshShape.TryBuildTopology(trimmedX, trimmedY, 4, trimmedTris,
                    out MapIconMeshTopology trimmed, out _), "shape/trimmed-topology");
            var bottomBand = new MapIconBox(10f, 1f, 18f, 6f);   // trimmed 外（下半）
            Check(!MapIconMeshShape.FootprintInside(trimmed, bottomBand, MapIconMeshShape.DefaultEpsilon),
                "shape/trimmed-bottom-rejected");
            var trimmedInside = new MapIconBox(14f, 14f, 22f, 24f);
            Check(MapIconMeshShape.FootprintInside(trimmed, trimmedInside, MapIconMeshShape.DefaultEpsilon),
                "shape/trimmed-inside-accepted");

            // ---- 2b) 带洞岛形（湖/内湾）：洞内不得放置，环上可放 ----
            float[] ringX = { 0f, 40f, 40f, 0f, 16f, 24f, 24f, 16f };
            float[] ringY = { 3f, 3f, 27f, 27f, 11f, 11f, 17f, 17f };
            int[] ringTris = { 0, 1, 4, 1, 5, 4, 1, 2, 5, 2, 6, 5, 2, 3, 6, 3, 7, 6, 3, 0, 7, 0, 4, 7 };
            Check(MapIconMeshShape.TryBuildTopology(ringX, ringY, 8, ringTris, out MapIconMeshTopology ring,
                    out _), "shape/ring-topology");
            var holeBox = new MapIconBox(17f, 12.5f, 23f, 15.5f);   // 洞内
            Check(!MapIconMeshShape.FootprintInside(ring, holeBox, MapIconMeshShape.DefaultEpsilon),
                "shape/hole-rejected");
            var ringBox = new MapIconBox(4f, 6f, 12f, 14f);         // 环上（左臂）
            Check(MapIconMeshShape.FootprintInside(ring, ringBox, MapIconMeshShape.DefaultEpsilon),
                "shape/ring-accepted");
            var straddleBox = new MapIconBox(14f, 12.5f, 30f, 15f); // 跨洞边（细长跨越）
            Check(!MapIconMeshShape.FootprintInside(ring, straddleBox, MapIconMeshShape.DefaultEpsilon),
                "shape/hole-boundary-straddle-rejected");

            // ---- 2c) 拓扑规范化：重复坐标接缝 / 混合朝向 / 重复三角形 / 非流形 ----
            // 对角切分的四边形，两个三角形在**共享边上各带一份重复坐标顶点**（v1/v3 = (40,0)，v2/v5 = (0,30)）。
            float[] seamX = { 0f, 40f, 0f, 40f, 40f, 0f };
            float[] seamY = { 0f, 0f, 30f, 0f, 30f, 30f };
            int[] seamTris = { 0, 1, 2, 3, 4, 5 };
            Check(MapIconMeshShape.TryBuildTopology(seamX, seamY, 6, seamTris, out MapIconMeshTopology seam,
                    out string seamReason), "shape/seam-duplicate-coord " + seamReason);
            Check(seam.VertexCount == 4, "shape/seam-canonical-vertices=" + seam.VertexCount);
            // 接缝不当作海岸：跨接缝（对角）的足迹应被接受
            Check(MapIconMeshShape.FootprintInside(seam, new MapIconBox(8f, 6f, 32f, 24f),
                    MapIconMeshShape.DefaultEpsilon), "shape/seam-not-a-coast");

            // 混合朝向（一个三角形反向）：不产生假边界
            float[] mixX = { 0f, 20f, 20f, 0f };
            float[] mixY = { 0f, 0f, 20f, 20f };
            int[] mixTris = { 0, 1, 2, 0, 3, 2 };   // 第二片 (0,3,2) 与 (0,1,2) 共享边 (0,2)，朝向相反
            Check(MapIconMeshShape.TryBuildTopology(mixX, mixY, 4, mixTris, out MapIconMeshTopology mixed, out _),
                "shape/mixed-winding-topology");
            Check(MapIconMeshShape.FootprintInside(mixed, new MapIconBox(2f, 2f, 18f, 18f),
                    MapIconMeshShape.DefaultEpsilon), "shape/mixed-winding-accepted");

            // 重复三角形（完全相同的索引集合）去重后不造成非流形
            float[] dupX = { 0f, 20f, 0f };
            float[] dupY = { 0f, 0f, 20f };
            int[] dupTris = { 0, 1, 2, 2, 1, 0 };
            Check(MapIconMeshShape.TryBuildTopology(dupX, dupY, 3, dupTris, out MapIconMeshTopology dup, out _),
                "shape/duplicate-triangle-dedup");
            Check(dup.Triangles.Length == 3, "shape/duplicate-triangle-count=" + dup.Triangles.Length);

            // 非流形（一条无向边 3 个三角形）⇒ Unknown（fail-closed）
            float[] badX = { 0f, 20f, 0f, 20f, 10f };
            float[] badY = { 0f, 0f, 20f, 20f, -10f };
            int[] badTris = { 0, 1, 2, 1, 0, 3, 0, 1, 4 };
            Check(!MapIconMeshShape.TryBuildTopology(badX, badY, 5, badTris,
                    out MapIconMeshTopology badTopo, out string badReason),
                "shape/nonmanifold-unknown " + badReason);
            Check(badTopo == null && !MapIconMeshShape.FootprintInside(badTopo, new MapIconBox(2f, 2f, 8f, 8f),
                      MapIconMeshShape.DefaultEpsilon),
                "shape/nonmanifold-fail-closed");

            // 全退化（零面积三角形）⇒ Unknown；非法索引被忽略但不产生假边界
            float[] degX = { 0f, 10f, 20f };
            float[] degY = { 0f, 0f, 0f };
            Check(!MapIconMeshShape.TryBuildTopology(degX, degY, 3, new int[] { 0, 1, 2 },
                    out _, out string degReason), "shape/degenerate-unknown " + degReason);
            Check(!MapIconMeshShape.TryBuildTopology(trimmedX, trimmedY, 4, new int[] { 0, 1, 9 },
                    out _, out string idxReason), "shape/invalid-index-ignored " + idxReason);

            // ---- 3) uGUI Simple+useSpriteMesh 公式（primary source Image.GenerateSprite/GetDrawingDimensions） ----
            // (a) bounds != rect/ppu：归一必须用 Sprite.bounds.size（v2 用 rect/ppu ⇒ 位置不同）
            Check(MapIconNativeArtPlan.TryBuildSimpleMeshDraw(100f, 100f, 0f, 0f, false,
                    84f, 62f, 42f, 0f, 0.8f, 0.8f, out MapIconImageDraw smallBounds),
                "shape/draw-bounds");
            float canonicalX = smallBounds.LocalX(0.2f);       // v=0.2, bounds=0.8, drawing=100
            float legacyX = 0.2f * 32f;                        // v2：ppu 归一（0.2*ppu = 6.4）再按 rect/ppu 缩放
            Check(Near(canonicalX, 0.2f / 0.8f * 100f - smallBounds.OffsetX, 1e-3f),
                "shape/draw-bounds-canonical=" + canonicalX);
            Check(Math.Abs(canonicalX - legacyX) > 0.5f, "shape/legacy-ppu-differs canonical=" +
                canonicalX + " legacy=" + legacyX);

            // (b) pivot(0,0) + preserveAspect：缩框按 pivot 锚定（左下），不是居中（v2 FitAspect 居中 ⇒ 红）
            Check(MapIconNativeArtPlan.TryBuildSimpleMeshDraw(100f, 50f, 0f, 0f, true,
                    100f, 100f, 50f, 50f, 1f, 1f, out MapIconImageDraw pivotDraw),
                "shape/aspect-pivot");
            Check(Near(pivotDraw.DrawingW, 50f) && Near(pivotDraw.DrawingH, 50f),
                "shape/aspect-pivot-size=" + pivotDraw.DrawingW + "x" + pivotDraw.DrawingH);
            // 顶点不依赖 GetPixelAdjustedRect 的位置项：只有尺寸参与 ⇒ 与 pivot 无关的部分保持一致
            Check(Near(pivotDraw.LocalX(0f), 0f / 1f * 50f - pivotDraw.OffsetX, 1e-3f),
                "shape/aspect-pivot-vertex");
            // v2 的居中实现会得到 [25,0,75,50]（reviewer v2-map-probe results.txt）；规范值应为 [0,0,50,50]
            float v2X0 = 25f;
            Check(Math.Abs((pivotDraw.DrawingW * 0f - pivotDraw.OffsetX) - v2X0) > 0.5f ||
                  Math.Abs(pivotDraw.OffsetX - (-50f * 0.5f)) < 1e-3f,
                "shape/legacy-fit-aspect-differs v2X0=" + v2X0 + " canonicalOffset=" + pivotDraw.OffsetX);

            // (c) preserveAspect=false：绘制尺寸 = rect 尺寸
            Check(MapIconNativeArtPlan.TryBuildSimpleMeshDraw(100f, 50f, 0f, 0f, false,
                    100f, 100f, 50f, 50f, 1f, 1f, out MapIconImageDraw noAspect) &&
                  Near(noAspect.DrawingW, 100f) && Near(noAspect.DrawingH, 50f),
                "shape/no-aspect-size");

            // (d) 非法输入 fail-closed
            Check(!MapIconNativeArtPlan.TryBuildSimpleMeshDraw(100f, 50f, 0f, 0f, true,
                      0f, 100f, 50f, 50f, 1f, 1f, out _), "shape/draw-invalid-sprite-rect");
            Check(!MapIconNativeArtPlan.TryBuildSimpleMeshDraw(0f, 0f, 0f, 0f, false,
                      100f, 100f, 50f, 50f, 1f, 1f, out _), "shape/draw-invalid-rect");

            // ---- 4) 自然 paper 倍率：drawnInPlan / drawnInOwnerLocal（含 art 自身 scale 与全局 fit） ----
            var planDrawn = new MapIconBox(0f, 0f, 22.218f, 16.399f);      // 84×62 × 0.5 × 0.529
            var ownerDrawn = new MapIconBox(-42f, 0f, 42f, 62f);           // art 本地 rect 84×62
            Check(MapIconNativeArtPlan.TryResolvePaperScale(planDrawn, ownerDrawn, out float natural) &&
                  Near(natural, 0.2645f, 0.0005f), "shape/natural-paper-scale=" + natural);
            Check(!MapIconNativeArtPlan.TryResolvePaperScale(planDrawn, default, out _), "shape/scale-degenerate");

            // ---- 5) 请求换算到 paper 后 4/4（reviewer 反例的 green 侧；同一组代表尺寸） ----
            List<MapIconRequest> representatives = new List<MapIconRequest>
            {
                Req(MapIconKind.Steed, 37, 40f, 28f, 0), Req(MapIconKind.Steed, 33, 40f, 20f, 1),
                Req(MapIconKind.Hermit, 0, 18f, 24f), Req(MapIconKind.Statue, 2, 20f, 36f),
            };
            var unscaledOut = new List<MapIconPlacement>();
            bool unscaledOk = MapResourceIconPlanner.TryPlan(representatives,
                new MapIconSurface(0f, 0f, 22.218f, 16.399f), unscaledOut, out float unscaledUsed,
                out int unscaledFail, true, MapResourceIconPlanner.DefaultScaleCount);
            Check(!unscaledOk && unscaledOut.Count < 4,
                "shape/unscaled-overflows placed=" + unscaledOut.Count + " scale=" + unscaledUsed);
            var scaled = new List<MapIconRequest>(representatives.Count);
            for (int i = 0; i < representatives.Count; i++)
            {
                MapIconRequest r = representatives[i];
                scaled.Add(new MapIconRequest(r.Kind, r.TypeId, r.ArrayIndex, r.Width * natural, r.Height * natural));
            }
            var scaledOut = new List<MapIconPlacement>();
            bool scaledOk = MapResourceIconPlanner.TryPlan(scaled, new MapIconSurface(0f, 0f, 22.218f, 16.399f),
                scaledOut, out float scaledUsed, out int scaledFail, true, MapResourceIconPlanner.DefaultScaleCount);
            Check(scaledOk && scaledFail == 0 && scaledOut.Count == 4 && scaledUsed >= 0.36f,
                "shape/scaled-fits-4of4 ok=" + scaledOk + " used=" + scaledUsed + " placed=" + scaledOut.Count +
                " unscaledFail=" + unscaledFail);
            // Request/RequestIndex 身份：换算只作用于装箱副本，placement 仍指向原请求下标
            for (int i = 0; i < scaledOut.Count; i++)
            {
                Check(scaledOut[i].RequestIndex >= 0 && scaledOut[i].RequestIndex < representatives.Count,
                    "shape/request-index#" + i);
            }
        }

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
        /// <summary>
        /// 扩展簇（physical11 / UI10）world 容量：真实 canvas Sprite.rect 228×84 等比放入底部带（生产
        /// MapExtensionShapePlan.TryPlanWorldBox）后的**岛内图标面**上，真实 15/16 最大尺寸资源集必须在
        /// 生产 planner（MapExtensionIslandLayout，world 路径下限 PreferScale=0.6）下完整放下、不覆盖任何
        /// native 图形、不越出岛内面；未登记 extra 的行为由 runtime 桥判定（本层不伪造）。
        /// </summary>
        private static void Extension()
        {
            List<(float W, float H, MapIconKind Kind)> icons16 = MaxResourceIcons(16);
            Check(icons16.Count == 16, "extension/fixture-16-sizes");
            if (icons16.Count < 16) return;
            List<(float W, float H, MapIconKind Kind)> real16 = RealExtensionIcons();
            Check(real16.Count == 16, "extension/real-16-sizes");
            if (real16.Count < 16) return;
            if (!TryRealNativeLayout(out List<MapIconBox> nativeArt, out _))
            {
                Check(false, "extension/native-layout");
                return;
            }

            const float PaperW = 314f, PaperH = 208f, Band = 18f;
            // 生产 band：world 域共同分区（底部扩展预留带 = DefaultExtensionReserve），不是 BandFraction 名义带。
            Check(MapWorldLayout.ComposeDomains(MapOverviewLayout.FullPaper(PaperW, PaperH), Band,
                      MapWorldLayout.DefaultExtensionReserve, out _, out MapIconBox band), "extension/domains");
            float aspect = 228f / 84f;   // MapExtensionIslandArt 实际 Sprite.rect aspect
            Check(MapExtensionShapePlan.TryPlanWorldBox(band, MapExtensionShapePlan.Margin, aspect,
                      out MapIconBox banner), "extension/world-banner-box");
            Check(banner.X0 >= band.X0 - 0.01f && banner.X1 <= band.X1 + 0.01f &&
                  banner.Y0 >= band.Y0 - 0.01f && banner.Y1 <= band.Y1 + 0.01f, "extension/banner-in-band");

            // 生产 world 路径同口径：岛内图标面（banner 内缩 = IconAreaOf）；blocker 只含与 banner 相交的真实
            // native 图形（扩展自己的底图是 surface，绝不是 blocker）。
            MapIconBox area = MapWorldLayout.IconAreaOf(banner);

            var blockers = new List<MapIconBox>(nativeArt.Count);
            for (int b = 0; b < nativeArt.Count; b++)
            {
                if (Intersects(nativeArt[b], banner, 0.01f)) blockers.Add(nativeArt[b]);
            }
            foreach ((int count, List<(float W, float H, MapIconKind Kind)> icons) in
                     new[] { (16, real16), (16, icons16) })
            {
                var reqs = ToRequests(icons);
                var merged = new List<MapIconPlacement>();
                // 生产 world 路径带顶面 PlacementMask（228×84 画布）；离线用同尺寸"全可放"mask 复现其网格/锚点语义。
                bool ok = MapExtensionIslandLayout.TryPlan(area, reqs, blockers, banner, SolidMask(228, 84),
                    merged, out float used, out int failed, MapExtensionIslandLayout.PreferScale);
                bool real = ReferenceEquals(icons, real16);
                if (real)
                {
                    // 真实 16 坐骑集是扩展岛的**实际内容**：必须在 world 下限（≥0.6）完整放下。
                    Check(ok && failed == 0 && used >= MapExtensionIslandLayout.PreferScale,
                        "extension/real-set scale=" + used + " placed=" + merged.Count);
                }
                else if (ok)
                {
                    // 合成"16 最大"集（超出真实内容）：放得下就必须全部在岛内（同一契约）。
                    Check(failed == 0 && used >= MapExtensionIslandLayout.PreferScale,
                        "extension/max-set scale=" + used + " placed=" + merged.Count);
                }
                if (!ok) continue;
                for (int p = 0; p < merged.Count; p++)
                {
                    MapIconBox box = PlacementBox(merged[p]);
                    Check(box.Y0 >= area.Y0 - 0.01f && box.Y1 <= area.Y1 + 0.01f &&
                          box.X0 >= area.X0 - 0.01f && box.X1 <= area.X1 + 0.01f,
                          "extension/set" + count + "/inside-art-" + p);
                    for (int b = 0; b < blockers.Count; b++)
                    {
                        Check(!Intersects(box, blockers[b], -0.01f),
                            "extension/set" + count + "/no-native-art-" + p + "_" + b);
                    }
                    for (int q = 0; q < p; q++)
                    {
                        Check(!Intersects(box, PlacementBox(merged[q]), -0.01f),
                            "extension/set" + count + "/no-overlap-" + p + "_" + q);
                    }
                }
            }
        }

        /// <summary>
        /// native detail 岛内容量（issue-152 契约）：surface = 本岛真实 Land Image 盒（底图是 placement surface、
        /// 不是 blocker），只避真实原生图形。真实小条目集（1–4）必须 ≥0.36 放下；15/16 压力集在岛内放不下时
        /// 必须 fail-closed（failed&gt;0 → 调用方整体丢弃并还原原生槽，绝不岛外兜底）。
        /// </summary>
        private static void DetailCapacity()
        {
            List<(float W, float H, MapIconKind Kind)> icons16 = MaxResourceIcons(16);
            Check(icons16.Count == 16, "detail/fixture-16-sizes");
            if (icons16.Count < 16) return;
            List<(float W, float H, MapIconKind Kind)> icons15 = MaxResourceIcons(15);

            // 真实 detail 模板 art（resources.assets /Map_Land_* Land Image：138×78 / 120×108 / 152×176 /
            // 114×82）；native 轮廓/图标避障用真实 detail 船标盒（32×32）作代表。
            foreach ((float artW, float artH) in new[] { (138f, 78f), (120f, 108f), (152f, 176f), (114f, 82f) })
            {
                var surface = new MapIconSurface(-artW * 0.5f, -artH * 0.5f, artW * 0.5f, artH * 0.5f);
                var boat = new MapIconBox(-16f, -16f, 16f, 16f);
                surface.AddBlocked(boat);

                var small = new List<MapIconRequest> { Req(MapIconKind.Steed, 37, 40, 28, 0),
                                                       Req(MapIconKind.Steed, 35, 25, 16, 1),
                                                       Req(MapIconKind.Hermit, 0, 18, 24, 0),
                                                       Req(MapIconKind.Statue, 2, 20, 36, 0) };
                var smallOut = new List<MapIconPlacement>();
                bool smallOk = MapResourceIconPlanner.TryPlan(small, surface, smallOut, out float smallScale,
                    out int smallFail, false, MapResourceIconPlanner.DefaultScaleCount);
                Check(smallOk && smallFail == 0 && smallScale >= 0.36f,
                    "detail/small-set-" + artW + "x" + artH + " scale=" + smallScale + " placed=" + smallOut.Count);
                for (int p = 0; p < smallOut.Count; p++)
                {
                    MapIconBox box = PlacementBox(smallOut[p]);
                    Check(box.X0 >= -artW * 0.5f - 0.01f && box.X1 <= artW * 0.5f + 0.01f &&
                          box.Y0 >= -artH * 0.5f - 0.01f && box.Y1 <= artH * 0.5f + 0.01f,
                          "detail/small-inside-art-" + artW + "_" + p);
                    Check(!Intersects(box, boat, -0.01f), "detail/small-clear-boat-" + artW + "_" + p);
                }

                // 15/16 压力集：放得下 ⇒ 必须**全部在岛内**（≥0.36）；放不下 ⇒ 必须如实 failed>0
                // （调用方整体丢弃/还原原生槽，绝不岛外兜底、绝不部分贴图当成功）。
                var stressOut = new List<MapIconPlacement>();
                bool stressOk = MapResourceIconPlanner.TryPlan(ToRequests(icons16), surface, stressOut,
                    out float stressScale, out int stressFail, false, MapResourceIconPlanner.DefaultScaleCount);
                if (stressOk)
                {
                    Check(stressFail == 0 && stressScale >= 0.36f && stressOut.Count == 16,
                        "detail/stress16-fit-" + artW + "x" + artH + " scale=" + stressScale);
                    for (int p = 0; p < stressOut.Count; p++)
                    {
                        MapIconBox box = PlacementBox(stressOut[p]);
                        Check(box.X0 >= -artW * 0.5f - 0.01f && box.X1 <= artW * 0.5f + 0.01f &&
                              box.Y0 >= -artH * 0.5f - 0.01f && box.Y1 <= artH * 0.5f + 0.01f,
                              "detail/stress16-inside-art-" + artW + "_" + p);
                    }
                }
                else
                {
                    Check(stressFail > 0, "detail/stress16-fail-reported-" + artW + "x" + artH +
                        " scale=" + stressScale + " placed=" + stressOut.Count);
                }
            }
        }

        /// <summary>全可放 mask（离线复现生产 PlacementMask 的画布/网格语义；不含 alpha 轮廓约束）。</summary>
        private static MapShoreMask SolidMask(int width, int height)
        {
            var inside = new bool[width * height];
            for (int i = 0; i < inside.Length; i++) inside[i] = true;
            return new MapShoreMask(width, height, inside);
        }

        /// <summary>扩展岛（physical11）真实 16 坐骑集：icon-rects.json 的 (iconType=1, 真实 type) 尺寸。</summary>
        private static readonly int[] ExtensionSteedTypes =
            { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };

        private static List<(float W, float H, MapIconKind Kind)> RealExtensionIcons()
        {
            var result = new List<(float, float, MapIconKind)>();
            string path = EvidencePath("icon-rects.json");
            if (path == null) return result;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (int type in ExtensionSteedTypes)
            {
                if (doc.RootElement.TryGetProperty("1/" + type, out JsonElement v))
                {
                    float w = (float)v.GetProperty("w").GetDouble() *
                              Math.Abs((float)v.GetProperty("scale").GetProperty("x").GetDouble());
                    float h = (float)v.GetProperty("h").GetDouble() *
                              Math.Abs((float)v.GetProperty("scale").GetProperty("y").GetDouble());
                    result.Add((w, h, MapIconKind.Steed));
                    continue;
                }
                // 原生确证缺失、由自有目录补齐的 3/4/38：尺寸来自生产 MapCustomIconCatalog（UiWidth/UiHeight）。
                if (MapCustomIconCatalog.TryGet(1, type, out MapCustomIconDef def) && def.Valid)
                {
                    result.Add((def.UiWidth, def.UiHeight, MapIconKind.Steed));
                }
            }
            return result;
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
            // 生产总览路径同口径：域共同分区后把原 10 拟合进**上部区**（不是名义 BandFraction 带），
            // 否则 native art 会落到扩展带里，离线容量裁决与实机不一致。
            if (!MapWorldLayout.ComposeDomains(MapOverviewLayout.FullPaper(314f, 208f), 18f,
                    MapWorldLayout.DefaultExtensionReserve, out MapIconBox upper, out _)) return false;
            if (!MapWorldLayout.TryFitNatives(inputs, 314f, 208f, upper, targets, out scale)) return false;
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

            // 3) 扩展岛自由错落（2026-10-03 用户直接合同）：真实 16 尺寸在真实 banner 矩形内全量放下、
            //    scale ≥ 0.6、RequestIndex 一一对应；诊断证明不是旧三行（无长水平带、y 分散）。
            float[] widths = { 25, 24, 21, 25, 21, 21, 29, 29, 26, 28, 28, 40, 22, 24, 40, 20 };
            float[] heights = { 16, 22, 26, 17, 18, 18, 13, 13, 14, 18, 13, 25, 19, 32, 26, 20 };
            var sixteen = new List<MapIconRequest>();
            for (int i = 0; i < 16; i++) sixteen.Add(new MapIconRequest(MapIconKind.Steed, 100 + i, i, widths[i], heights[i]));
            // 合成矩形（无 mask）语义：全量放下或整体失败（不部分显示）；正常视口 ≥0.6 的硬门由
            // 真实顶面 mask 的断言覆盖（shore-art live/16-at-ge06-* 与 runtime S1/S11），此处不重复加严。
            var banner = new MapIconBox(0f, 0f, 185.9f, 56f);
            var placements = new List<MapIconPlacement>();
            bool bannerOk = MapExtensionIslandLayout.TryPlan(banner, sixteen, new List<MapIconBox>(), placements,
                out float used, out int failed);
            Check(bannerOk
                    ? placements.Count == 16 && failed == 0 && used >= MapExtensionIslandLayout.MinScale
                    : placements.Count == 0 && failed == 16,
                "ext/16-banner-honest ok=" + bannerOk + " used=" + used + " n=" + placements.Count +
                " failed=" + failed);
            bool indexOk = true;
            for (int i = 0; i < placements.Count; i++)
            {
                if (placements[i].RequestIndex < 0 || placements[i].RequestIndex >= 16 ||
                    placements[i].Request.Width != sixteen[placements[i].RequestIndex].Width) indexOk = false;
            }
            Check(indexOk, "ext/request-index-identity");
            // 错落诊断（含旧三行回归）：无长水平带、y 中心带足够多、y 范围明显（不是微抖动）。
            ScatterCheck(placements, "ext-banner");

            // 4) 确定性：同输入重复调用逐值一致（浮点逐值相等，不是近似）
            var again = new List<MapIconPlacement>();
            Check(MapExtensionIslandLayout.TryPlan(banner, sixteen, new List<MapIconBox>(), again,
                out float used2, out _) && used2 == used && again.Count == placements.Count, "ext/deterministic-recount");
            bool sameValues = again.Count == placements.Count;
            for (int i = 0; sameValues && i < placements.Count; i++)
            {
                if (again[i].X != placements[i].X || again[i].Y != placements[i].Y ||
                    again[i].Scale != placements[i].Scale || again[i].RequestIndex != placements[i].RequestIndex)
                {
                    sameValues = false;
                }
            }
            Check(sameValues, "ext/deterministic-values");

            // 5) 平移等变：整 mask 像素平移（+10,+7）→ 结果同量平移（网格取整不漂移）
            {
                const int mw = 228, mh = 84;
                var solid = new bool[mw * mh];
                for (int i = 0; i < solid.Length; i++) solid[i] = true;
                var maskA = new MapShoreMask(mw, mh, solid);
                var frameA = new MapIconBox(0f, 0f, mw, mh);
                var areaA = new MapIconBox(1.5f, 1.5f, mw - 1.5f, mh - 1.5f);
                var outA = new List<MapIconPlacement>();
                var trio = new List<MapIconRequest> { sixteen[0], sixteen[6], sixteen[13] };
                Check(MapExtensionIslandLayout.TryPlan(areaA, trio, new List<MapIconBox>(), frameA, maskA, outA,
                    out _, out _), "ext/shift-base-ok");
                const float dx = 10f, dy = 7f;
                var frameB = new MapIconBox(frameA.X0 + dx, frameA.Y0 + dy, frameA.X1 + dx, frameA.Y1 + dy);
                var areaB = new MapIconBox(areaA.X0 + dx, areaA.Y0 + dy, areaA.X1 + dx, areaA.Y1 + dy);
                var outB = new List<MapIconPlacement>();
                Check(MapExtensionIslandLayout.TryPlan(areaB, trio, new List<MapIconBox>(), frameB, maskA, outB,
                    out _, out _) && outB.Count == outA.Count, "ext/shift-equivariant-ok");
                bool shifted = outB.Count == outA.Count;
                for (int i = 0; shifted && i < outA.Count; i++)
                {
                    if (Math.Abs(outB[i].X - (outA[i].X + dx)) > 1e-4f ||
                        Math.Abs(outB[i].Y - (outA[i].Y + dy)) > 1e-4f) shifted = false;
                }
                Check(shifted, "ext/shift-equivariant-values");
            }

            // 6) 更窄视口 → 降级仍全量或整体失败（诚实 fallback，绝不部分显示）；过小 → 整体失败
            var narrow = new MapIconBox(0f, 0f, 150f, 46f);
            bool narrowOk = MapExtensionIslandLayout.TryPlan(narrow, sixteen, new List<MapIconBox>(), placements,
                out float narrowScale, out int narrowFailed);
            Check(narrowOk
                    ? placements.Count == 16 && narrowFailed == 0 && narrowScale >= MapExtensionIslandLayout.MinScale
                    : placements.Count == 0 && narrowFailed == 16,
                "ext/narrow-honest ok=" + narrowOk + " scale=" + narrowScale + " n=" + placements.Count);
            var tiny = new MapIconBox(0f, 0f, 60f, 20f);
            Check(!MapExtensionIslandLayout.TryPlan(tiny, sixteen, new List<MapIconBox>(), placements,
                out _, out int tinyFailed) && placements.Count == 0 && tinyFailed == 16, "ext/tiny-failclosed");

            // 7) 真实船标障碍仍可放 16；纯障碍覆盖 → 失败不截断
            var boat = new List<MapIconBox> { new MapIconBox(150f, 18f, 158f, 26f) };
            Check(MapExtensionIslandLayout.TryPlan(banner, sixteen, boat, placements, out _, out _) &&
                placements.Count == 16, "ext/boat-obstacle-16");
            var blocked = new List<MapIconBox> { new MapIconBox(0f, 0f, 185.9f, 49.1f) };
            Check(!MapExtensionIslandLayout.TryPlan(banner, sixteen, blocked, placements, out _, out _) &&
                placements.Count == 0, "ext/blocked-failclosed");

            // 8) 空/单项/重复/容量：不截断、不补假
            Check(MapExtensionIslandLayout.TryPlan(banner, new List<MapIconRequest>(), new List<MapIconBox>(),
                placements, out _, out int emptyFailed) && placements.Count == 0 && emptyFailed == 0, "ext/empty-ok");
            Check(MapExtensionIslandLayout.TryPlan(banner, new List<MapIconRequest> { sixteen[0] },
                new List<MapIconBox>(), placements, out _, out _) && placements.Count == 1, "ext/single-ok");
            var dup = new List<MapIconRequest> { sixteen[0], sixteen[0] };
            Check(MapExtensionIslandLayout.TryPlan(banner, dup, new List<MapIconBox>(), placements, out _, out _) &&
                placements.Count == 2 && placements[0].RequestIndex != placements[1].RequestIndex, "ext/duplicate-distinct");
            var seventeen = new List<MapIconRequest>(sixteen) { new MapIconRequest(MapIconKind.Steed, 199, 16, 24f, 32f) };
            bool seventeenOk = MapExtensionIslandLayout.TryPlan(banner, seventeen, new List<MapIconBox>(), placements,
                out _, out _);
            Check(seventeenOk ? placements.Count == 17 : placements.Count == 0,
                "ext/17-honest ok=" + seventeenOk + " n=" + placements.Count);
            var nineteen = new List<MapIconRequest>(seventeen)
            {
                new MapIconRequest(MapIconKind.Steed, 200, 17, 20f, 20f),
                new MapIconRequest(MapIconKind.Steed, 201, 18, 20f, 20f),
            };
            Check(!MapExtensionIslandLayout.TryPlan(banner, nineteen, new List<MapIconBox>(), placements,
                out _, out int overFailed) && placements.Count == 0 && overFailed == 19, "ext/19-failclosed");

            // 8b) 锚点表一致性：与容量上限同位；primary 全部有限且在 [0,1]；fallback 允许 NaN
            Check(MapExtensionIslandLayout.PreferredAnchorX.Length == MapExtensionIslandLayout.MaxItems &&
                MapExtensionIslandLayout.PreferredAnchorY.Length == MapExtensionIslandLayout.MaxItems &&
                MapExtensionIslandLayout.FallbackAnchorX.Length == MapExtensionIslandLayout.MaxItems &&
                MapExtensionIslandLayout.FallbackAnchorY.Length == MapExtensionIslandLayout.MaxItems,
                "ext/anchor-tables-sized");
            bool anchorsValid = true;
            for (int i = 0; i < MapExtensionIslandLayout.MaxItems; i++)
            {
                float ax = MapExtensionIslandLayout.PreferredAnchorX[i], ay = MapExtensionIslandLayout.PreferredAnchorY[i];
                if (!(ax >= 0f && ax <= 1f) || !(ay >= 0f && ay <= 1f)) anchorsValid = false;
                float fx2 = MapExtensionIslandLayout.FallbackAnchorX[i], fy2 = MapExtensionIslandLayout.FallbackAnchorY[i];
                bool nanPair = float.IsNaN(fx2) && float.IsNaN(fy2);
                bool okPair = fx2 >= 0f && fx2 <= 1f && fy2 >= 0f && fy2 <= 1f;
                if (!nanPair && !okPair) anchorsValid = false;
            }
            Check(anchorsValid, "ext/anchor-tables-valid");

            // 9) 空域/退化：整体失败（fail-closed）
            Check(!MapExtensionIslandLayout.TryPlan(new MapIconBox(0f, 0f, 0f, 0f), sixteen,
                new List<MapIconBox>(), placements, out _, out _), "ext/degenerate-failclosed");
        }

        /// <summary>纯错落诊断（与真实 mask 断言同口径；旧三行输出必然失败）：
        /// y 中心带 ≥6、最大同带 ≤5、最长近水平链 ≤4、右半 ≥6、y 范围 ≥18。</summary>
        private static void ScatterCheck(List<MapIconPlacement> placements, string tag)
        {
            const float bandTol = 1.5f, gap = 12f;
            var centers = new List<float>();
            var xs = new List<float>();
            foreach (MapIconPlacement p in placements)
            {
                centers.Add(p.Y + p.Request.Height * p.Scale * 0.5f);
                xs.Add(p.X + p.Request.Width * p.Scale * 0.5f);
            }
            var sorted = new List<float>(centers);
            sorted.Sort();
            int bands = 0, maxBand = 0, run = 0;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i == 0 || sorted[i] - sorted[i - 1] > bandTol)
                {
                    if (run > maxBand) maxBand = run;
                    bands++;
                    run = 0;
                }
                run++;
            }
            if (run > maxBand) maxBand = run;
            int rightHalf = 0;
            for (int i = 0; i < placements.Count; i++)
            {
                if (xs[i] > 92.95f) rightHalf++;   // banner 185.9 的中线
            }
            var order = new List<int>();
            for (int i = 0; i < placements.Count; i++) order.Add(i);
            order.Sort((a, b) => xs[a] != xs[b] ? xs[a].CompareTo(xs[b]) : centers[a].CompareTo(centers[b]));
            int longest = 1;
            for (int i = 0; i < order.Count; i++)
            {
                int chain = 1;
                float lastX = xs[order[i]];
                for (int j = i + 1; j < order.Count; j++)
                {
                    if (Math.Abs(centers[order[j]] - centers[order[i]]) <= bandTol &&
                        xs[order[j]] - lastX < gap)
                    {
                        chain++;
                        lastX = xs[order[j]];
                    }
                }
                if (chain > longest) longest = chain;
            }
            float yMin = float.MaxValue, yMax = float.MinValue;
            foreach (float c in centers)
            {
                if (c < yMin) yMin = c;
                if (c > yMax) yMax = c;
            }
            Check(bands >= 6, "scatter/" + tag + "-y-bands n=" + bands);
            Check(maxBand <= 5, "scatter/" + tag + "-max-band n=" + maxBand);
            Check(longest <= 4, "scatter/" + tag + "-longest-chain n=" + longest);
            Check(rightHalf >= 6, "scatter/" + tag + "-right-half n=" + rightHalf);
            Check(yMax - yMin >= 18f, "scatter/" + tag + "-y-range r=" + (yMax - yMin).ToString("0.#"));
        }

        /// <summary>
        /// 新岸线形状合同（review/NEW-ART-BINDING-ADDENDUM + 2026-10-03 自由错落直接合同）：
        /// - 布局 = 固定 normalized 锚点 + 有界确定性修正（无三行/6,5,5；旧断言已删除）；
        /// - world/detail 框按**实际 Sprite.rect**（228×84 ⇒ 2.7143，含透明 padding）等比；
        ///   绝不使用 raw alpha bbox（1807/643 ⇒ 2.8103）；
        /// - detail 左缘 −92（legend 右缘 −96 + 4UI 水道）、右缘 138 不出 page282；
        /// - 岸内 mask 终检覆盖整个 footprint（中心 1px 洞必须拒绝，不只查 4 角/bbox）。
        /// </summary>
        private static void ExtensionShoreShape()
        {
            float[] widths = { 25, 24, 21, 25, 21, 21, 29, 29, 26, 28, 28, 40, 22, 24, 40, 20 };
            float[] heights = { 16, 22, 26, 17, 18, 18, 13, 13, 14, 18, 13, 25, 19, 32, 26, 20 };
            var sixteen = new List<MapIconRequest>();
            for (int i = 0; i < 16; i++) sixteen.Add(new MapIconRequest(MapIconKind.Steed, 100 + i, i, widths[i], heights[i]));

            // ---- world 框：实际 Sprite.rect 228×84（含 padding）= 2.7143；不是 2.8103（raw bbox）
            const float canvasAspect = 228f / 84f;
            var band = new MapIconBox(2f, 2f, 298f, 80f);            // 300×200：content 178 → maxReserve 80.1，reserve 78
            Check(MapExtensionShapePlan.TryPlanWorldBox(band, MapExtensionShapePlan.Margin, canvasAspect,
                out MapIconBox worldBox), "shape3/world-box");
            Check(Near(worldBox.Width / worldBox.Height, canvasAspect, 0.0005f),
                "shape3/world-ratio-228x84 " + (worldBox.Width / worldBox.Height));
            Check(worldBox.X0 >= band.X0 - 0.01f && worldBox.X1 <= band.X1 + 0.01f &&
                  worldBox.Y0 >= band.Y0 - 0.01f && worldBox.Y1 <= band.Y1 + 0.01f, "shape3/world-box-in-band");
            Check(Near(worldBox.Height, 78f - 2f * MapExtensionShapePlan.Margin, 0.01f),
                "shape3/world-box-height-limited " + worldBox.Height);
            Check(!Near(worldBox.Width / worldBox.Height, 1807f / 643f, 0.001f), "shape3/world-not-raw-bbox-ratio");
            var band400 = new MapIconBox(2f, 2f, 398f, 80f);
            Check(MapExtensionShapePlan.TryPlanWorldBox(band400, MapExtensionShapePlan.Margin, canvasAspect,
                out MapIconBox world400) && Near(world400.Width / world400.Height, canvasAspect, 0.0005f),
                "shape3/world-400x200-equal-box");
            Check(Near(world400.Width, worldBox.Width, 0.01f) && Near(world400.Height, worldBox.Height, 0.01f),
                "shape3/world-400x200-same-height-limit");
            var bandTall = new MapIconBox(2f, 2f, 398f, 140f);
            Check(MapExtensionShapePlan.TryPlanWorldBox(bandTall, MapExtensionShapePlan.Margin, canvasAspect,
                out MapIconBox worldTall) && worldTall.Width > world400.Width + 1f,
                "shape3/world-400x260-uses-height " + worldTall.Width);

            // ---- detail 框：左 −92 / 右 138 / 宽 230 / 等比 / 4UI 水道（legend 最右 −96）
            Check(MapExtensionShapePlan.TryPlanDetailBox(MapExtensionShapePlan.DetailPageHalfWidth,
                MapExtensionShapePlan.DetailLegendRight, MapExtensionShapePlan.DetailWaterGap,
                MapExtensionShapePlan.DetailMaxWidth, canvasAspect, -4f, out MapIconBox detailBox, out float gap),
                "shape3/detail-box");
            Check(Near(detailBox.X0, -92f, 0.01f) && Near(detailBox.X1, 138f, 0.01f),
                "shape3/detail-x " + detailBox.X0 + "," + detailBox.X1);
            Check(Near(detailBox.Width, 230f, 0.01f) && Near(gap, 4f, 0.01f),
                "shape3/detail-width-230 gap=" + gap);
            Check(Near(detailBox.Width / detailBox.Height, canvasAspect, 0.0005f), "shape3/detail-ratio-228x84");
            Check(detailBox.X0 > MapExtensionShapePlan.DetailLegendRight, "shape3/detail-not-cover-legend");
            Check(detailBox.X1 <= MapExtensionShapePlan.DetailPageHalfWidth, "shape3/detail-inside-page");
            Check(detailBox.Y0 >= -93f && detailBox.Y1 <= 93f,
                "shape3/detail-y-in-page " + detailBox.Y0 + "," + detailBox.Y1);

            // ---- 岸内 mask：整 footprint 覆盖（只查 4 角/bbox 会漏掉中心 1px 水洞）
            const int mw = 256, mh = 120;
            var solid = new bool[mw * mh];
            for (int i = 0; i < solid.Length; i++) solid[i] = true;
            var solidMask = new MapShoreMask(mw, mh, solid);
            Check(solidMask.RectAllInside(0, 0, mw - 1, mh - 1), "shape3/mask-solid-all");
            Check(!solidMask.RectAllInside(-1, 0, 10, 10) && !solidMask.RectAllInside(0, 0, mw, mh - 1),
                "shape3/mask-out-of-canvas-rejected");
            var holed = (bool[])solid.Clone();
            holed[60 * mw + 128] = false;
            var holeMask = new MapShoreMask(mw, mh, holed);
            Check(!holeMask.RectAllInside(124, 56, 132, 64), "shape3/mask-center-hole-rejected");
            Check(holeMask.RectAllInside(0, 0, 10, 10), "shape3/mask-hole-outside-ok");
            Check(holeMask.Sample(124, 56) && holeMask.Sample(132, 64) && holeMask.Sample(124, 64) && holeMask.Sample(132, 56),
                "shape3/mask-hole-corners-inside");

            // ---- planner 岸内终检：窄岸带（只有 y∈[50,70] 是岸）→ 16 项整体 fallback、不得部分/落水
            var bandPixels = new bool[mw * mh];
            for (int y = 50; y <= 70; y++)
            {
                for (int x = 4; x < mw - 4; x++) bandPixels[y * mw + x] = true;
            }
            var bandMask = new MapShoreMask(mw, mh, bandPixels);
            var shoreFrame = new MapIconBox(0f, 0f, mw, mh);
            var area = new MapIconBox(1.5f, 1.5f, mw - 1.5f, mh - 1.5f);
            var maskPlacements = new List<MapIconPlacement>();
            bool bandOk = MapExtensionIslandLayout.TryPlan(area, sixteen, new List<MapIconBox>(), shoreFrame,
                bandMask, maskPlacements, out _, out int bandFailed);
            Check(bandOk
                    ? bandFailed == 0 && maskPlacements.Count == 16 && FootprintsInsideShore(maskPlacements, bandMask)
                    : maskPlacements.Count == 0 && bandFailed == 16,
                "shape3/mask-narrow-band-honest ok=" + bandOk + " n=" + maskPlacements.Count +
                " failed=" + bandFailed);
            bool solidOk = MapExtensionIslandLayout.TryPlan(area, sixteen, new List<MapIconBox>(), shoreFrame,
                solidMask, maskPlacements, out float solidScale, out int solidFailed);
            Check(solidOk && solidFailed == 0 && maskPlacements.Count == 16,
                "shape3/mask-solid-16 ok=" + solidOk + " scale=" + solidScale);
            Check(solidOk && FootprintsInsideShore(maskPlacements, solidMask), "shape3/mask-16-all-inside");
            var gulf = (bool[])solid.Clone();
            for (int y = 40; y < 80; y++)
            {
                for (int x = 64; x < 192; x++) gulf[y * mw + x] = false;
            }
            var gulfMask = new MapShoreMask(mw, mh, gulf);
            bool gulfOk = MapExtensionIslandLayout.TryPlan(area, sixteen, new List<MapIconBox>(), shoreFrame,
                gulfMask, maskPlacements, out _, out int gulfFailed);
            Check(gulfOk ? FootprintsInsideShore(maskPlacements, gulfMask) : gulfFailed == 16,
                "shape3/gulf-never-over-water ok=" + gulfOk + " failed=" + gulfFailed);
        }

        private static bool FootprintsInsideShore(List<MapIconPlacement> placements, MapShoreMask mask)
        {
            for (int i = 0; i < placements.Count; i++)
            {
                MapIconBox box = PlacementBox(placements[i]);
                int x0 = (int)Math.Floor(box.X0);
                int y0 = (int)Math.Floor(box.Y0);
                int x1 = (int)Math.Ceiling(box.X1) - 1;
                int y1 = (int)Math.Ceiling(box.Y1) - 1;
                if (!mask.RectAllInside(x0, y0, x1, y1)) return false;
            }
            return true;
        }

        // ------------------------------------------------------- geography report (offline evidence)

        /// <summary>
        /// 岛内面裁决（issue-152 契约；与生产 native 总览同一调用形状）：图标只放本岛 art 盒内、自顶向下、可读下限 0.36。
        /// 离线 fixture 无每岛原生素材遮挡数据 → blockers 为空（真实遮挡由 runtime probe 覆盖）。
        /// </summary>
        private static bool PlanIslandArt(List<MapIconRequest> requests, in MapIconBox art,
            out float usedScale, out List<MapIconPlacement> placements, float paperScale = 1f)
        {
            placements = new List<MapIconPlacement>();
            var surface = new MapIconSurface(art.X0, art.Y0, art.X1, art.Y1);
            List<MapIconRequest> scaled = requests;
            if (paperScale > 0f && Math.Abs(paperScale - 1f) > 1e-4f)
            {
                scaled = new List<MapIconRequest>(requests.Count);
                for (int i = 0; i < requests.Count; i++)
                {
                    MapIconRequest r = requests[i];
                    scaled.Add(new MapIconRequest(r.Kind, r.TypeId, r.ArrayIndex,
                        r.Width * paperScale, r.Height * paperScale));
                }
            }
            return MapResourceIconPlanner.TryPlan(scaled, surface, placements, out usedScale, out _,
                true, MapResourceIconPlanner.DefaultScaleCount);
        }

        /// <summary>
        /// 离线证据（issue-152 岛内契约）：真实 native fixture → 生产纯函数的统一倍率布局 + **每岛底图盒** + 容量裁决。
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
            for (int i = 0; i < inputs.Count; i++)
            {
                artBoxes.Add(MapOverviewLayout.ArtBoxOf(inputs[i], targets[i], PaperW, PaperH));
            }
            MapIconBox content = MapOverviewLayout.ContentRegion(PaperW, PaperH, Band);

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
                var three = new List<MapIconRequest> { Req(MapIconKind.Steed, 37, 40, 28, 0),
                                                       Req(MapIconKind.Steed, 35, 40, 22, 1),
                                                       Req(MapIconKind.Hermit, 0, 18, 24, 0) };
                // overview 请求按自然 paper 倍率换算（离线只知全局 fit：art 自身 localScale 由运行期 exact
                // 测量决定 ⇒ 这里的倍率是**下界**，裁决因此偏保守）。
                float paperScale = s > 0f ? s : 1f;
                bool threeOk = PlanIslandArt(three, artBoxes[i], out float threeScale,
                    out List<MapIconPlacement> merged, paperScale);
                bool worstOk = PlanIslandArt(Worst10(), artBoxes[i], out float worstScale, out _, paperScale);
                bool stressOk = PlanIslandArt(Stress12(), artBoxes[i], out float stressScale, out _, paperScale);
                sb.Append("    {\"name\":").Append(Json(clusters[i].Name));
                sb.Append(",\"origAnchored\":[").Append(F(inputs[i].OrigX)).Append(",").Append(F(inputs[i].OrigY)).Append("]");
                sb.Append(",\"targetAnchored\":[").Append(F(targets[i].X)).Append(",").Append(F(targets[i].Y)).Append("]");
                sb.Append(",\"targetScale\":[").Append(F(targets[i].ScaleX)).Append(",").Append(F(targets[i].ScaleY)).Append("]");
                sb.Append(",\"artBox\":[").Append(F(artBoxes[i].X0)).Append(",").Append(F(artBoxes[i].Y0)).Append(",")
                  .Append(F(artBoxes[i].X1)).Append(",").Append(F(artBoxes[i].Y1)).Append("]");
                sb.Append(",\"artArea\":").Append(F(artBoxes[i].Width * artBoxes[i].Height));
                sb.Append(",\"paperScaleLowerBound\":").Append(F(paperScale));
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

            // ---- extra（physical11 / UI10）关键 fixture：扩展区域与详情在真实 15/16 最大尺寸集下的容量 ----
            TryRealNativeLayout(out List<MapIconBox> nativeArt, out _);
            MapIconBox nominalBand = MapOverviewLayout.BandRegion(PaperW, PaperH, Band);
            // ---- extra（physical11 / UI10）：生产 world/detail 岛内面在真实 16 坐骑集与最大尺寸压力集下的容量 ----
            List<(float W, float H, MapIconKind Kind)> real16 = RealExtensionIcons();
            List<(float W, float H, MapIconKind Kind)> icons15 = MaxResourceIcons(15);
            List<(float W, float H, MapIconKind Kind)> icons16 = MaxResourceIcons(16);
            sb.Append("  \"extra\": {\n");
            sb.Append("    \"physicalIndex\": 11, \"mapIndex\": 10,\n");
            sb.Append("    \"sizesSource\": \"icon-rects.json iconType 0/1/2（真实集 1/21..1/6 + 自有 3/4/38 取 MapCustomIconCatalog）\",\n");
            sb.Append("    \"world\": ");
            if (MapWorldLayout.ComposeDomains(MapOverviewLayout.FullPaper(PaperW, PaperH), Band,
                    MapWorldLayout.DefaultExtensionReserve, out _, out MapIconBox extBand) &&
                MapExtensionShapePlan.TryPlanWorldBox(extBand, MapExtensionShapePlan.Margin, 228f / 84f,
                    out MapIconBox extArt))
            {
                var extBlockers = new List<MapIconBox>(nativeArt.Count);
                for (int b = 0; b < nativeArt.Count; b++)
                {
                    if (Intersects(nativeArt[b], extArt, 0.01f)) extBlockers.Add(nativeArt[b]);
                }
                MapIconBox iconArea = MapWorldLayout.IconAreaOf(extArt);
                var placed16 = new List<MapIconPlacement>();
                var placedMax = new List<MapIconPlacement>();
                bool ok16 = MapExtensionIslandLayout.TryPlan(iconArea, ToRequests(real16), extBlockers,
                    extArt, SolidMask(228, 84), placed16, out float scale16, out int failed16,
                    MapExtensionIslandLayout.PreferScale);
                bool okMax = MapExtensionIslandLayout.TryPlan(iconArea, ToRequests(icons16), extBlockers,
                    extArt, SolidMask(228, 84), placedMax, out float scaleMax, out int failedMax,
                    MapExtensionIslandLayout.PreferScale);
                sb.Append("{\"art1\":[228,84]");
                sb.Append(",\"artBox\":[").Append(F(extArt.X0)).Append(",").Append(F(extArt.Y0)).Append(",")
                  .Append(F(extArt.X1)).Append(",").Append(F(extArt.Y1)).Append("]");
                sb.Append(",\"iconArea\":[").Append(F(iconArea.X0)).Append(",").Append(F(iconArea.Y0)).Append(",")
                  .Append(F(iconArea.X1)).Append(",").Append(F(iconArea.Y1)).Append("]");
                sb.Append(",\"blockers\":").Append(extBlockers.Count);
                sb.Append(",\"real16\":{\"ok\":").Append(ok16 && failed16 == 0 ? "true" : "false")
                  .Append(",\"scale\":").Append(F(scale16)).Append(",\"placed\":").Append(placed16.Count).Append("}");
                sb.Append(",\"max16\":{\"ok\":").Append(okMax && failedMax == 0 ? "true" : "false")
                  .Append(",\"scale\":").Append(F(scaleMax)).Append(",\"placed\":").Append(placedMax.Count).Append("}}");
            }
            else
            {
                sb.Append("null");
            }
            sb.Append(",\n");
            // native detail（issue-152 契约）：surface = 真实 Land Image 盒（此处取真实 detail 模板 art 尺寸）；
            // 真实小条目集（1–4）必须放下，16 压力集只在放得下时算成功（否则 fail-closed）。
            sb.Append("    \"nativeDetail\": {\"sizesFrom\": \"resources.assets /Map_Land_* Land Image\", \"artFixtures\": [");
            float[][] detailArts = { new[] { 138f, 78f }, new[] { 120f, 108f }, new[] { 152f, 176f }, new[] { 114f, 82f } };
            for (int f = 0; f < detailArts.Length; f++)
            {
                var detailArt = new MapIconBox(-detailArts[f][0] * 0.5f, -detailArts[f][1] * 0.5f,
                    detailArts[f][0] * 0.5f, detailArts[f][1] * 0.5f);
                bool detailSmall = PlanIslandArt(ToRequests(new List<(float W, float H, MapIconKind Kind)>
                    { (40f, 28f, MapIconKind.Steed), (25f, 16f, MapIconKind.Steed), (18f, 24f, MapIconKind.Hermit) }),
                    detailArt, out float smallScale, out _);
                bool detailStress = PlanIslandArt(ToRequests(icons16), detailArt, out float stressScale, out _);
                if (f > 0) sb.Append(",");
                sb.Append("{\"art1\":[").Append(F(detailArts[f][0])).Append(",").Append(F(detailArts[f][1])).Append("]");
                sb.Append(",\"small3\":{\"ok\":").Append(detailSmall ? "true" : "false").Append(",\"scale\":").Append(F(smallScale)).Append("}");
                sb.Append(",\"max16\":{\"ok\":").Append(detailStress ? "true" : "false").Append(",\"scale\":").Append(F(stressScale)).Append("}}");
            }
            sb.Append("]},\n");
            sb.Append("    \"note\": \"world art = real 228×84 Sprite.rect in the reserve band; detail art fixtures are real Land Image sizes\"\n");
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
