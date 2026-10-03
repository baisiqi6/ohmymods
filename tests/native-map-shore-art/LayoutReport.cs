// 真实 PNG → 生产 planner 的布局报告 + mask/clean 导出（供后续同算法 SVG 预览；绝不改写 PNG）。
// 与生产同一算法：MapExtensionIslandArt.BuildPrep（最大连通面/nearest 等比/6档灰 + **顶面 PlacementMask**）→
// MapShoreMask → MapExtensionShapePlan（world 框等比）+ MapWorldLayout（域分区/原 10 统一变换）+
// MapExtensionIslandLayout.TryPlan（自由错落 + 逐像素**顶面**可放置终检）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using KingdomEnhancedMod;
using UnityEngine;

namespace ShoreArtTests
{
    internal static class LayoutReport
    {
        internal static int Run(string assetPath, string outPath, string exportDir)
        {
            if (!File.Exists(assetPath)) { Console.WriteLine("asset missing (explicit): " + Path.GetFileName(assetPath)); return 2; }
            byte[] png = File.ReadAllBytes(assetPath);
            Png.DecodeBottomUp(png, out int sw, out int sh, out Color32[] source);   // Unity 行序：与 fake LoadImage/运行时同序
            MapExtensionIslandArt.ShorePrep prep = MapExtensionIslandArt.BuildPrep(source, sw, sh,
                MapExtensionIslandArt.TargetLogicalWidth);
            if (prep == null) { Console.WriteLine("prep failed"); return 3; }
            float canvasAspect = prep.Width / (float)prep.Height;

            // 真实 16 请求：真实 TypeId（与 runtime probe 同一顺序）+ 真实尺寸表（icon-rects.json 1/*）。
            float[] widths = { 25, 24, 21, 25, 21, 21, 29, 29, 26, 28, 28, 40, 22, 24, 40, 20 };
            float[] heights = { 16, 22, 26, 17, 18, 18, 13, 13, 14, 18, 13, 25, 19, 32, 26, 20 };
            int[] typeIds = { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };
            var sixteen = new List<MapIconRequest>(16);
            for (int i = 0; i < 16; i++)
            {
                sixteen.Add(new MapIconRequest(MapIconKind.Steed, typeIds[i], i, widths[i], heights[i]));
            }

            List<NativeCluster> natives = LoadNativeClusters();
            bool nativesOk = natives != null && natives.Count == MapOverviewLayout.NativeUiClusterCount;

            var sb = new StringBuilder(64 * 1024);
            sb.Append("{\n");
            // 公开报告只记 basename（不导出用户/任务目录的绝对路径）。
            sb.Append("  \"asset\": {\"path\": ").Append(Json(Path.GetFileName(assetPath)))
              .Append(", \"sha256\": ").Append(Json(Sha(png))).Append("},\n");
            int rejectedPixels = 0;
            for (int i = 0; i < prep.Mask.Length; i++)
            {
                if (prep.Mask[i] && (prep.PlacementMask == null || !prep.PlacementMask[i])) rejectedPixels++;
            }
            sb.Append("  \"shore\": {\"canvas\": [").Append(prep.Width).Append(',').Append(prep.Height)
              .Append("], \"bbox\": [").Append(prep.BboxWidth).Append(',').Append(prep.BboxHeight)
              .Append("], \"bboxAspect\": ").Append(F(prep.Aspect, 6))
              .Append(", \"canvasAspect\": ").Append(F(canvasAspect, 6))
              .Append(", \"islandPixels\": ").Append(prep.IslandPixels)
              .Append(", \"placementPixels\": ").Append(prep.PlacementPixels)
              .Append(", \"rejectedAlphaPixels\": ").Append(rejectedPixels)
              .Append(", \"placementCutNodes\": ").Append(MapExtensionIslandArt.PlacementCutX.Length)
              .Append(", \"components\": ").Append(prep.ComponentCount)
              .Append(", \"levels\": ").Append(prep.LevelsUsed).Append("},\n");
            if (prep.PlacementMask == null || prep.PlacementPixels <= 0)
            {
                Console.WriteLine("placement mask missing");
                return 3;
            }
            var mask = new MapShoreMask(prep.Width, prep.Height, prep.PlacementMask);

            sb.Append("  \"requests\": [");
            for (int i = 0; i < sixteen.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("{\"i\": ").Append(sixteen[i].ArrayIndex)
                  .Append(", \"type\": ").Append(sixteen[i].TypeId)
                  .Append(", \"w\": ").Append(F(sixteen[i].Width, 2))
                  .Append(", \"h\": ").Append(F(sixteen[i].Height, 2)).Append('}');
            }
            sb.Append("],\n");
            sb.Append("  \"detail\": {\"pageHalfWidth\": ").Append(F(MapExtensionShapePlan.DetailPageHalfWidth, 3))
              .Append(", \"legendRight\": ").Append(F(MapExtensionShapePlan.DetailLegendRight, 3))
              .Append(", \"waterGap\": ").Append(F(MapExtensionShapePlan.DetailWaterGap, 3));
            if (MapExtensionShapePlan.TryPlanDetailBox(MapExtensionShapePlan.DetailPageHalfWidth,
                    MapExtensionShapePlan.DetailLegendRight, MapExtensionShapePlan.DetailWaterGap,
                    MapExtensionShapePlan.DetailMaxWidth, canvasAspect, -4f, out MapIconBox detailBox, out float gap))
            {
                sb.Append(", \"box\": ").Append(BoxJson(detailBox))
                  .Append(", \"gap\": ").Append(F(gap, 3))
                  .Append(", \"ratio\": ").Append(F(detailBox.Width / detailBox.Height, 6));
            }
            sb.Append("},\n");

            sb.Append("  \"viewports\": [\n");
            var viewports = new[] { new[] { 300f, 200f }, new[] { 400f, 200f }, new[] { 400f, 260f } };
            for (int v = 0; v < viewports.Length; v++)
            {
                if (v > 0) sb.Append(",\n");
                AppendViewport(sb, viewports[v][0], viewports[v][1], prep, mask, canvasAspect, sixteen, natives, nativesOk);
            }
            sb.Append("\n  ]\n}\n");
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine("layout report " + outPath);

            if (!string.IsNullOrEmpty(exportDir))
            {
                Directory.CreateDirectory(exportDir);
                ExportClean(prep, Path.Combine(exportDir, "shore-clean.pgm"));
                ExportMask(prep, Path.Combine(exportDir, "shore-mask.pbm"));
                ExportBits(prep.PlacementMask, prep.Width, prep.Height,
                    Path.Combine(exportDir, "shore-placement.pbm"));
                Console.WriteLine("exported clean/mask to " + exportDir);
            }
            return 0;
        }

        private static void AppendViewport(StringBuilder sb, float worldW, float worldH,
            MapExtensionIslandArt.ShorePrep prep, MapShoreMask mask, float canvasAspect,
            List<MapIconRequest> sixteen, List<NativeCluster> natives, bool nativesOk)
        {
            sb.Append("    {\"world\": [").Append(F(worldW, 1)).Append(',').Append(F(worldH, 1)).Append(']');
            var visible = new MapIconBox(0f, 0f, worldW, worldH);
            if (!MapWorldLayout.ComposeDomains(visible, 18f, MapWorldLayout.DefaultExtensionReserve,
                    out MapIconBox upper, out MapIconBox band))
            {
                sb.Append(", \"error\": \"domains\"}");
                return;
            }
            sb.Append(", \"band\": ").Append(BoxJson(band));
            sb.Append(", \"upper\": ").Append(BoxJson(upper));
            if (!MapExtensionShapePlan.TryPlanWorldBox(band, MapExtensionShapePlan.Margin, canvasAspect,
                    out MapIconBox shape))
            {
                sb.Append(", \"error\": \"shape\"}");
                return;
            }
            sb.Append(", \"shapeCanvasRect\": ").Append(BoxJson(shape));
            sb.Append(", \"shapeRatio\": ").Append(F(shape.Width / shape.Height, 6));
            MapIconBox iconArea = MapWorldLayout.IconAreaOf(shape);
            sb.Append(", \"iconArea\": ").Append(BoxJson(iconArea));

            var placements = new List<MapIconPlacement>();
            bool ok = MapExtensionIslandLayout.TryPlan(iconArea, sixteen, new List<MapIconBox>(), shape, mask,
                placements, out float scale, out int failed);
            sb.Append(", \"plan\": {\"ok\": ").Append(ok ? "true" : "false")
              .Append(", \"scale\": ").Append(F(scale, 4))
              .Append(", \"failed\": ").Append(failed).Append('}');
            if (ok)
            {
                sb.Append(", \"placements\": [");
                for (int i = 0; i < placements.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    MapIconPlacement p = placements[i];
                    sb.Append("{\"i\": ").Append(p.RequestIndex)
                      .Append(", \"x\": ").Append(F(p.X, 2))
                      .Append(", \"y\": ").Append(F(p.Y, 2))
                      .Append(", \"w\": ").Append(F(p.Request.Width * p.Scale, 2))
                      .Append(", \"h\": ").Append(F(p.Request.Height * p.Scale, 2)).Append('}');
                }
                sb.Append(']');
                int inside = 0;
                for (int i = 0; i < placements.Count; i++)
                {
                    if (MapExtensionIslandLayout.FootprintInsideShore(BoxOf(placements[i]), shape, mask)) inside++;
                }
                sb.Append(", \"maskCheck\": {\"inside\": ").Append(inside)
                  .Append(", \"total\": ").Append(placements.Count)
                  .Append(", \"all\": ").Append(inside == placements.Count ? "true" : "false").Append('}');
                AppendScatterDiagnostics(sb, placements, shape, mask);
            }
            if (nativesOk)
            {
                var targets = new List<MapOverviewClusterTarget>(10);
                float nativeScale = 1f;
                var inputs = new List<MapOverviewClusterInput>(10);
                for (int i = 0; i < natives.Count; i++) inputs.Add(natives[i].ToInput());
                bool fitted = MapWorldLayout.TryFitNatives(inputs, worldW, worldH, upper, targets, out nativeScale);
                sb.Append(", \"nativeFit\": {\"ok\": ").Append(fitted ? "true" : "false")
                  .Append(", \"scale\": ").Append(F(nativeScale, 4)).Append('}');
                if (fitted)
                {
                    sb.Append(", \"nativeTargets\": [");
                    for (int i = 0; i < targets.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append("{\"x\": ").Append(F(targets[i].X, 2))
                          .Append(", \"y\": ").Append(F(targets[i].Y, 2))
                          .Append(", \"sx\": ").Append(F(targets[i].ScaleX, 4))
                          .Append(", \"sy\": ").Append(F(targets[i].ScaleY, 4)).Append('}');
                    }
                    sb.Append(']');
                }
            }
            sb.Append('}');
        }

        private static MapIconBox BoxOf(in MapIconPlacement p)
            => new MapIconBox(p.X, p.Y, p.X + p.Request.Width * p.Scale, p.Y + p.Request.Height * p.Scale);

        /// <summary>自由错落诊断（非断言）：y 中心分带（容差 1.5 UI）、最大带、右半计数、最长近水平链
        /// （同带且相邻 x 间隔 &lt; 12 UI）、y 范围与左右最远延伸——供叠图/回归复核"不成行"。</summary>
        private static void AppendScatterDiagnostics(StringBuilder sb, List<MapIconPlacement> placements,
            in MapIconBox shape, MapShoreMask mask)
        {
            float fx = mask.Width / shape.Width;
            float bandTol = 1.5f * fx;
            float gap = 12f * fx;
            var centers = new List<float>(placements.Count);
            var xCenters = new List<float>(placements.Count);
            for (int i = 0; i < placements.Count; i++)
            {
                MapIconBox box = BoxOf(placements[i]);
                centers.Add((box.Y0 + box.Y1) * 0.5f);
                xCenters.Add((box.X0 + box.X1) * 0.5f);
            }
            var sortedY = new List<float>(centers);
            sortedY.Sort();
            var bands = new List<List<float>>();
            for (int i = 0; i < sortedY.Count; i++)
            {
                if (bands.Count == 0 || sortedY[i] - bands[bands.Count - 1][bands[bands.Count - 1].Count - 1] > bandTol)
                {
                    bands.Add(new List<float>());
                }
                bands[bands.Count - 1].Add(sortedY[i]);
            }
            int maxBand = 0;
            foreach (List<float> b in bands) if (b.Count > maxBand) maxBand = b.Count;
            var byX = new List<int>(placements.Count);
            for (int i = 0; i < placements.Count; i++) byX.Add(i);
            byX.Sort((a, b) => xCenters[a] != xCenters[b] ? xCenters[a].CompareTo(xCenters[b])
                : centers[a].CompareTo(centers[b]));
            int longest = 1;
            for (int i = 0; i < byX.Count; i++)
            {
                int chain = 1;
                float lastX = xCenters[byX[i]];
                for (int j = i + 1; j < byX.Count; j++)
                {
                    if (Math.Abs(centers[byX[j]] - centers[byX[i]]) <= bandTol && xCenters[byX[j]] - lastX < gap)
                    {
                        chain++;
                        lastX = xCenters[byX[j]];
                    }
                }
                if (chain > longest) longest = chain;
            }
            int rightHalf = 0;
            float leftmost = float.MaxValue, rightmost = float.MinValue;
            for (int i = 0; i < placements.Count; i++)
            {
                MapIconBox box = BoxOf(placements[i]);
                if (xCenters[i] > shape.X0 + shape.Width * 0.5f) rightHalf++;
                if (box.X0 < leftmost) leftmost = box.X0;
                if (box.X1 > rightmost) rightmost = box.X1;
            }
            float yMin = float.MaxValue, yMax = float.MinValue;
            for (int i = 0; i < centers.Count; i++)
            {
                if (centers[i] < yMin) yMin = centers[i];
                if (centers[i] > yMax) yMax = centers[i];
            }
            sb.Append(", \"diagnostics\": {\"yBands\": ").Append(bands.Count)
              .Append(", \"maxBand\": ").Append(maxBand)
              .Append(", \"rightHalf\": ").Append(rightHalf)
              .Append(", \"longestChain\": ").Append(longest)
              .Append(", \"yRange\": ").Append(F(yMax - yMin, 2))
              .Append(", \"leftmostX0\": ").Append(F(leftmost, 2))
              .Append(", \"rightmostX1\": ").Append(F(rightmost, 2))
              .Append(", \"canvasWidth\": ").Append(mask.Width)
              .Append(", \"canvasHeight\": ").Append(mask.Height).Append('}');
        }

        // ------------------------------------------------------------------ export

        private static void ExportClean(MapExtensionIslandArt.ShorePrep prep, string path)
        {
            var sb = new StringBuilder(prep.Width * prep.Height * 4 + 64);
            sb.Append("P2\n# KEM clean shore (6-tone)\n").Append(prep.Width).Append(' ').Append(prep.Height)
              .Append("\n255\n");
            for (int y = prep.Height - 1; y >= 0; y--)   // 从上到下（PNG 行序）
            {
                for (int x = 0; x < prep.Width; x++)
                {
                    if (x > 0) sb.Append(' ');
                    sb.Append(prep.Shore[y * prep.Width + x].r);
                }
                sb.Append('\n');
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static void ExportMask(MapExtensionIslandArt.ShorePrep prep, string path)
            => ExportBits(prep.Mask, prep.Width, prep.Height, path);

        private static void ExportBits(bool[] bits, int width, int height, string path)
        {
            var sb = new StringBuilder(width * height * 2 + 64);
            sb.Append("P1\n# KEM mask\n").Append(width).Append(' ').Append(height).Append('\n');
            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                {
                    if (x > 0) sb.Append(' ');
                    sb.Append(bits != null && bits[y * width + x] ? '1' : '0');
                }
                sb.Append('\n');
            }
            File.WriteAllText(path, sb.ToString());
        }

        // ------------------------------------------------------------------ fixtures

        private sealed class NativeCluster
        {
            internal string Name;
            internal float Ax, Ay;
            internal float ScaleX = 1f, ScaleY = 1f, ScaleZ = 1f;
            internal float ArtW1, ArtH1, OffX1, OffY1;

            internal MapOverviewClusterInput ToInput()
                => new MapOverviewClusterInput(Ax, Ay, ScaleX, ScaleY, ScaleZ, ArtW1, ArtH1, OffX1, OffY1);
        }

        private static string FixturePath(string name)
        {
            foreach (string prefix in new[] { AppContext.BaseDirectory + "fixtures/", AppContext.BaseDirectory,
                                              "fixtures/", "tests/shore-art/fixtures/", "../shore-art/fixtures/",
                                              "../../tests/shore-art/fixtures/" })
            {
                string candidate = prefix + name;
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static List<NativeCluster> LoadNativeClusters()
        {
            string manifestPath = FixturePath("layout-manifest.json");
            string analysisPath = FixturePath("native-map-analysis.json");
            if (manifestPath == null || analysisPath == null) return null;
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

        // ------------------------------------------------------------------ 行序自检

        /// <summary>
        /// 导出方向自检（真实非对称文件字节）：Encode(文件上半=暗) → DecodeBottomUp（唯一一次转换）
        /// → 生产 BuildPrep → ExportClean/ExportMask（PGM/PBM 行序 = 图像 top-down）。
        /// 断言：prep 底行（Unity index 0 侧）= 图像底部（亮 245 锚点）、导出首数据行 = 图像顶部（暗）。
        /// 任一断言失败返回非零：证明"某 caller 绕过转换再让导出翻一次"的 Y 闭环问题真实存在/已修。
        /// </summary>
        internal static int SelfCheck(string exportDir)
        {
            const int w = 8, h = 8;
            var fileOrder = new Color32[w * h];   // 文件行序：y=0 是图像**顶部**
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    byte tone = y < h / 2 ? (byte)116 : (byte)245;
                    fileOrder[y * w + x] = new Color32(tone, tone, tone, 255);
                }
            }
            byte[] png = Png.Encode(w, h, fileOrder);
            Png.DecodeBottomUp(png, out int dw, out int dh, out Color32[] source);
            MapExtensionIslandArt.ShorePrep prep = MapExtensionIslandArt.BuildPrep(source, dw, dh, 8);
            if (prep == null) { Console.WriteLine("FAIL yfix/self-check prep-null"); return 1; }
            int fails = 0;
            byte bottom = FirstOpaqueTone(prep, upward: true);    // Unity index0 侧 = 图像底部
            byte top = FirstOpaqueTone(prep, upward: false);
            Console.WriteLine("yfix/self-check prep bottom=" + bottom + " top=" + top);
            if (bottom != 245) { Console.WriteLine("FAIL yfix/self-check prep-bottom-not-image-bottom"); fails++; }
            if (top == 245 || top == 0) { Console.WriteLine("FAIL yfix/self-check prep-top-tone"); fails++; }
            Directory.CreateDirectory(exportDir);
            string cleanPath = Path.Combine(exportDir, "ycheck-clean.pgm");
            string maskPath = Path.Combine(exportDir, "ycheck-mask.pbm");
            ExportClean(prep, cleanPath);
            ExportMask(prep, maskPath);
            int firstRow = ReadPgmFirstRowTone(cleanPath);
            Console.WriteLine("yfix/self-check pgm-first-data-row=" + firstRow);
            if (firstRow != top) { Console.WriteLine("FAIL yfix/self-check export-first-row-is-image-top"); fails++; }
            Console.WriteLine(fails == 0 ? "yfix/self-check ALL PASS" : "yfix/self-check FAILURES=" + fails);
            return fails == 0 ? 0 : 1;
        }

        /// <summary>从底/顶扫描首个不透明像素的灰档（prep 画布含透明 padding）。</summary>
        private static byte FirstOpaqueTone(MapExtensionIslandArt.ShorePrep prep, bool upward)
        {
            int start = upward ? 0 : prep.Height - 1;
            int step = upward ? 1 : -1;
            for (int y = start; y >= 0 && y < prep.Height; y += step)
            {
                for (int x = 0; x < prep.Width; x++)
                {
                    if (prep.Shore[y * prep.Width + x].a > 0) return prep.Shore[y * prep.Width + x].r;
                }
            }
            return 0;
        }

        /// <summary>
        /// 旧 caller 行为复现（**故意保留的反例**）：PNG top-down 直喂 BuildPrep（绕过 Unity 行序转换），
        /// 再经 ExportClean。真实资产/非对称资产下三项断言必须失败——证明 Y 闭环缺陷真实存在且被现行
        /// 自检捕获。返回 0 = 反例意外"通过"（说明断言无效），1 = 正确检出（red）。
        /// </summary>
        internal static int SelfCheckLegacy(string exportDir)
        {
            const int w = 8, h = 8;
            var fileOrder = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    byte tone = y < h / 2 ? (byte)116 : (byte)245;
                    fileOrder[y * w + x] = new Color32(tone, tone, tone, 255);
                }
            }
            byte[] png = Png.Encode(w, h, fileOrder);
            Png.Decode(png, out int dw, out int dh, out Color32[] topDown);   // 旧行为：不做转换
            MapExtensionIslandArt.ShorePrep prep = MapExtensionIslandArt.BuildPrep(topDown, dw, dh, 8);
            if (prep == null) { Console.WriteLine("legacy prep-null"); return 1; }
            byte bottom = FirstOpaqueTone(prep, upward: true);
            byte top = FirstOpaqueTone(prep, upward: false);
            Console.WriteLine("red-legacy prep bottom=" + bottom + " top=" + top);
            int fails = 0;
            if (bottom != 245) { Console.WriteLine("RED-DETECTED legacy-prep-bottom-wrong (bottom=" + bottom + ")"); fails++; }
            if (top != 116) { Console.WriteLine("RED-DETECTED legacy-prep-top-wrong (top=" + top + ")"); fails++; }
            Directory.CreateDirectory(exportDir);
            string cleanPath = Path.Combine(exportDir, "ycheck-legacy.pgm");
            ExportClean(prep, cleanPath);
            int firstRow = ReadPgmFirstRowTone(cleanPath);
            if (firstRow != 116) { Console.WriteLine("RED-DETECTED legacy-export-row-wrong (row=" + firstRow + ")"); fails++; }
            Console.WriteLine(fails > 0 ? "red-legacy DETECTED fails=" + fails : "red-legacy NOT-DETECTED (BAD)");
            return fails > 0 ? 1 : 0;
        }

        /// <summary>PGM(P2) 首个**非全透明**数据行的首个非零像素（PGM 行序 = 图像 top-down）。</summary>
        private static int ReadPgmFirstRowTone(string path)
        {
            bool magicSeen = false, dimensionsSeen = false, maxSeen = false;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (!magicSeen) { magicSeen = true; continue; }
                if (!dimensionsSeen) { dimensionsSeen = true; continue; }
                if (!maxSeen) { maxSeen = true; continue; }
                foreach (string token in line.Split(' '))
                {
                    if (int.TryParse(token, out int value) && value > 0) return value;
                }
            }
            return -1;
        }

        // ------------------------------------------------------------------ json helpers

        private static string F(float value, int digits) => value.ToString("0." + new string('#', digits));
        private static string F(double value, int digits) => value.ToString("0." + new string('#', digits));

        private static string Json(string value)
            => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static string BoxJson(in MapIconBox box)
            => "[" + F(box.X0, 2) + ", " + F(box.Y0, 2) + ", " + F(box.X1, 2) + ", " + F(box.Y1, 2) + "]";

        private static string Sha(byte[] data)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
        }
    }
}
