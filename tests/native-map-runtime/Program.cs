// 新岸线接线 runtime probe：直链编译真实 src/MapMountIcons.cs + src/MapExtensionIslandArt.cs +
// src/MapResourceIconPlan.cs（不修改、不复制方法体），由真实 patch 入口（UpdateLand/MenuTick/ClearLands/
// OnDisable）驱动控制流；Unity/Il2Cpp 桩来自 oldW R3 entry-probe（原样复制）并按 art 管线（PNG→Sprite）扩展。
//
// 场景：
//  S1 world：扩展簇 terrain/outline 绑定同一 shore/outline；实际 Sprite.rect（228×84）等比框；
//            16 真实项三行铺在 bottom 带且逐像素在岸内；原 10 统一倍率；state1 精确还原。
//  S2 detail：唯一扩展 detail 绑定同一 shore；框 230 宽 @centerX+23、不遮 legend；world suspend 不撤 detail；
//            icons OFF（current/visited 11）仍展示岸线；exact OnDisable 恢复 native 并释放租约。
using System;
using System.Collections.Generic;
using System.IO;
using EntryProbe;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using KingdomEnhancedMod;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RuntimeProbe
{
    internal static class ProbeProgram
    {
        private static int _checks;
        private static int _fails;
        private static int _frame = 7000;

        /// <summary>
        /// test 侧读取生产 `NativeArtShape`（反射；仅测试用）：规划空间 box + 顶点数组 + 读取结论。
        /// 用于"生产顶点 == 独立 uGUI 公式顶点"的逐顶点验证（reviewer v3 要求）。
        /// </summary>
        private static bool TryReadProductionShape(RectTransform planSpace, UILand land, out MapIconBox planBox,
            out float[] vertexX, out float[] vertexY, out string reason)
        {
            planBox = default;
            vertexX = null;
            vertexY = null;
            reason = "reflection-failed";
            const System.Reflection.BindingFlags f = System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
            try
            {
                Type t = typeof(MapMountIcons);
                System.Reflection.MethodInfo read = t.GetMethod("TryReadNativeArtShape",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                Type shapeType = t.GetNestedType("NativeArtShape", System.Reflection.BindingFlags.NonPublic);
                object shape = Activator.CreateInstance(shapeType);
                object[] args = { planSpace, land, shape, null };
                bool ok = (bool)read.Invoke(null, args);
                reason = (string)args[3];
                if (!ok) return false;
                object box = shapeType.GetField("PlanBBox", f).GetValue(shape);
                planBox = new MapIconBox(
                    (float)box.GetType().GetField("X0", f).GetValue(box),
                    (float)box.GetType().GetField("Y0", f).GetValue(box),
                    (float)box.GetType().GetField("X1", f).GetValue(box),
                    (float)box.GetType().GetField("Y1", f).GetValue(box));
                object topo = shapeType.GetField("Topology", f).GetValue(shape);
                if (topo == null) return false;
                int count = (int)topo.GetType().GetField("VertexCount", f).GetValue(topo);
                vertexX = (float[])topo.GetType().GetField("VertexX", f).GetValue(topo);
                vertexY = (float[])topo.GetType().GetField("VertexY", f).GetValue(topo);
                return count == vertexX.Length && count == vertexY.Length;
            }
            catch (Exception e)
            {
                reason = "reflection-failed:" + e.GetType().Name;
                return false;
            }
        }

        private static bool Near(float a, float b, float tolerance) => Math.Abs(a - b) <= tolerance;

        /// <summary>有界日志转储（失败诊断用；只打印已记录的最后 N 行）。</summary>
        private static void DumpLogTail(int lines)
        {
            try
            {
                List<string> sink = KingdomEnhancedPlugin.Instance?.LogSource?.Lines;
                if (sink == null)
                {
                    Console.WriteLine("  log: <none>");
                    return;
                }
                int from = sink.Count > lines ? sink.Count - lines : 0;
                for (int i = from; i < sink.Count; i++) Console.WriteLine("  log| " + sink[i]);
            }
            catch (Exception) { }
        }

        private static void Check(bool condition, string label)
        {
            _checks++;
            if (!condition)
            {
                _fails++;
                Console.WriteLine("FAIL " + label);
            }
        }

        private sealed class Fixture
        {
            internal CampaignSaveData Campaign;
            internal CampaignSaveData.ReignInfo Current;
            internal MapTimelineMenuGreece Greek;
            internal UIMainMap Map;
            internal RectTransform Root;
            internal RectTransform Mask;
            internal RectTransform Paper;
            internal UILand[] Lands;
            internal UILand Extension;
            internal UILand ExtensionDetail;
            internal RectTransform ContentArea;
            internal RectTransform LandsContainer;
            internal RectTransform Scroller;
            internal RectTransform Holder;
            internal Image DetailLandImage;
            internal Sprite DetailNativeSprite;
            internal RectTransform Rocks;
            internal RectTransform OtherDecor;
            internal RectTransform DetailBoat;
            internal readonly List<Vector2> OriginalClusterPositions = new List<Vector2>();
            internal readonly List<float> OriginalClusterScales = new List<float>();
        }

        private static string _factsPath;
        private static string _assetPath;   // 可选显式自有 PNG；默认用已嵌入资源（--facts 的 asset sha 来源）
        private static bool _preserveState;   // 独立 review 口径：B 构造时抑制人工整批清理（不是生产行为）

        private static int Main(string[] args)
        {
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "--facts") _factsPath = args[i + 1];
                if (args[i] == "--asset") _assetPath = args[i + 1];
            }
            try
            {
                ScenarioS1WorldShore();
                ScenarioS2DetailShore();
                ScenarioS3OwnerToken();
                ScenarioS4ColdDetail();
                ScenarioS5DetailSnapshotIsolation();
                ScenarioS6TransactionRollback();
                ScenarioS7PendingGate();
                ScenarioS8RocksAnchor();
                ScenarioS9CommitBarrier();
                ScenarioS10ReviewParity();
                ScenarioS11ExtensionDetailLayout();
                ScenarioS12ReleaseQueueSaturation();
                ScenarioS13IslandSurfaceContract();
                ScenarioS14NativeShapeAndPaperScale();
                ScenarioS15RendererParity();
                if (_factsPath != null) ScenarioFacts();
            }
            catch (Exception e)
            {
                _fails++;
                Console.WriteLine("FAIL probe-exception " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
            }
            Console.WriteLine((_fails == 0 ? "ALL PASS" : "FAILURES") + " checks=" + _checks + " fails=" + _fails);
            return _fails == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------ S1

        private static void ScenarioS1WorldShore()
        {
            Console.WriteLine("== S1 world shore ==");
            int[] sixteen = { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };
            Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            float laneW = f.ContentArea.rect.width;
            Tick(f, 1);
            Tick(f, 1);
            Check(f.ContentArea.rect.width > laneW + 50f, "S1/paper-expanded w=" + f.ContentArea.rect.width);

            // 原 10：一个统一倍率 + 整体平移（两两差向量同倍率）
            var clusters = new RectTransform[10];
            for (int i = 0; i < 10; i++) clusters[i] = f.Lands[i].gameObject.GetComponent<RectTransform>();
            float scale = clusters[0].localScale.x;
            float err = 0f;
            for (int i = 0; i < 10; i++)
            {
                for (int j = i + 1; j < 10; j++)
                {
                    err = Math.Max(err, Math.Abs((clusters[i].anchoredPosition.x - clusters[j].anchoredPosition.x) -
                        scale * (f.OriginalClusterPositions[i].x - f.OriginalClusterPositions[j].x)));
                    err = Math.Max(err, Math.Abs((clusters[i].anchoredPosition.y - clusters[j].anchoredPosition.y) -
                        scale * (f.OriginalClusterPositions[i].y - f.OriginalClusterPositions[j].y)));
                }
            }
            Check(scale > 0.3f && err <= 0.05f, "S1/native-uniform s=" + scale + " err=" + err);

            // 绑定：terrain/outline → 真实 art 管线（PNG → clean shore/outline）
            Sprite shore = MapExtensionIslandArt.Shore;
            Sprite outlineSprite = MapExtensionIslandArt.Outline;
            RectTransform art = ArtOf(f.Extension) as RectTransform;
            Image artImage = art != null ? art.GetComponent<Image>() : null;
            Check(artImage != null && shore != null && ReferenceEquals(artImage.sprite, shore), "S1/terrain-shore-bound");
            RectTransform outline = FindOutline(f.Extension);
            Image outlineImage = outline != null ? outline.GetComponent<Image>() : null;
            Check(outlineImage != null && ReferenceEquals(outlineImage.sprite, outlineSprite), "S1/outline-bound");
            Check(shore != null && Math.Abs(shore.rect.width - MapExtensionIslandArt.Prep.Width) < 0.01f &&
                Math.Abs(shore.rect.height - MapExtensionIslandArt.Prep.Height) < 0.01f, "S1/sprite-rect-is-canvas");

            // 显示几何 = 实际 Sprite.rect（228×84，含 padding）等比；不是 raw bbox 2.8103
            MapIconBox box = PaperArtBox(f.Paper, art);
            float ratio = box.Width / Math.Max(0.001f, box.Height);
            Check(Math.Abs(ratio - 228f / 84f) < 0.02f, "S1/ratio-228x84 got=" + ratio);
            Check(Math.Abs(art.sizeDelta.x - 228f) < 0.5f && Math.Abs(art.sizeDelta.y - 84f) < 0.5f,
                "S1/sizeDelta-sprite-rect " + art.sizeDelta);
            Check(box.Y0 > -2f && box.Y1 < f.Paper.rect.height * 0.75f, "S1/box-in-bottom-band " + Fmt(box));
            Check(outline != null && outline.localScale.x > 0f, "S1/outline-scaled");

            // 16 真实项：全量、逐像素位于**运行期顶面 PlacementMask**（不是 alpha：崖面必须被拒）
            List<RectTransform> icons = CollectIcons(f.Paper, 16);
            Check(icons.Count == 16, "S1/16-icons got=" + icons.Count);
            MapExtensionIslandArt.ShorePrep prep = MapExtensionIslandArt.Prep;
            var runtimeMask = ReadStatic("_placementMask") as MapShoreMask;
            var expectedMask = new MapShoreMask(prep.Width, prep.Height, prep.PlacementMask);
            int maskDiff = -1;
            if (runtimeMask != null)
            {
                maskDiff = 0;
                for (int yy = 0; yy < prep.Height; yy++)
                {
                    for (int xx = 0; xx < prep.Width; xx++)
                    {
                        if (runtimeMask.Sample(xx, yy) != expectedMask.Sample(xx, yy)) maskDiff++;
                    }
                }
            }
            Check(runtimeMask != null && maskDiff == 0, "S1/placement-mask-is-art-prep diff=" + maskDiff);
            bool allInside = true;
            int firstOutside = -1;
            for (int i = 0; i < icons.Count; i++)
            {
                if (!FootprintInside(PaperArtBox(f.Paper, icons[i]), box, runtimeMask))
                {
                    allInside = false;
                    firstOutside = i;
                    break;
                }
            }
            Check(allInside, "S1/16-icons-inside-placement-mask first=" + firstOutside);
            ScatterDiagnostics(icons, f.Paper, box, "S1");
            CliffNegativeOracle(box, prep, runtimeMask, "S1");

            // 幂等：同视口再来一帧不重建 holder、不重复 set_sprite（sprite 身份不变）
            int holders = CountNamed(f.Paper.gameObject, "KEM_MapResourceIcons");
            Sprite before = artImage.sprite;
            Tick(f, 1);
            Check(CountNamed(f.Paper.gameObject, "KEM_MapResourceIcons") == holders &&
                ReferenceEquals(artImage.sprite, before), "S1/stable-tick-idempotent");

            // state1：world override 精确归还（原 10 位置/缩放回原值，shore 租约保留）
            f.Greek._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingSingleIsland;
            Tick(f, 1);
            Check(Math.Abs(clusters[0].anchoredPosition.x - f.OriginalClusterPositions[0].x) < 0.01f &&
                Math.Abs(clusters[0].localScale.x - f.OriginalClusterScales[0]) < 0.001f, "S1/state1-restores-natives");
            Check(ReferenceEquals(artImage.sprite, before), "S1/suspend-keeps-shore-lease");
        }

        // ------------------------------------------------------------------ S2

        private static void ScenarioS2DetailShore()
        {
            Console.WriteLine("== S2 detail shore ==");
            int[] sixteen = { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };
            Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Tick(f, 1);
            Tick(f, 1);
            UpdateDetails(f);   // 真实 menu.UpdateLands 的 detail 路径
            MapTimelineMenuGreece menu = f.Greek;
            Sprite shore = MapExtensionIslandArt.Shore;
            Image landImage = f.DetailLandImage;
            Check(landImage != null && shore != null && ReferenceEquals(landImage.sprite, shore), "S2/detail-shore-bound");

            RectTransform landRect = f.ExtensionDetail.gameObject.GetComponent<RectTransform>();
            MapIconBox box = LandLocalBox(landRect, landImage.transform as RectTransform);
            Rect lr = landRect.rect;
            // box 空间原点 = land rect 左下角 ⇒ 空间中心 = (w/2,h/2)（任意 pivot）
            float relLeft = box.X0 - lr.width * 0.5f;
            float relRight = box.X1 - lr.width * 0.5f;
            float relCenterY = (box.Y0 + box.Y1) * 0.5f - lr.height * 0.5f;
            float ratio = box.Width / Math.Max(0.001f, box.Height);
            Check(Math.Abs(box.Width - 230f) < 1f, "S2/detail-width-230 got=" + box.Width);
            Check(Math.Abs(relLeft + 92f) < 1.5f && Math.Abs(relRight - 138f) < 1.5f,
                "S2/detail-x-range [" + relLeft.ToString("0.#") + "," + relRight.ToString("0.#") + "]");
            Check(Math.Abs(relCenterY + 4f) < 1.5f, "S2/detail-center-y got=" + relCenterY.ToString("0.#"));
            Check(Math.Abs(ratio - 228f / 84f) < 0.02f, "S2/detail-ratio got=" + ratio);
            Check(relLeft > -96f, "S2/detail-keeps-legend-gap left=" + relLeft.ToString("0.#"));

            // 共享 sprite：world/detail 同一 shore/outline（4 个 Image 各一租约）
            RectTransform worldArt = ArtOf(f.Extension) as RectTransform;
            RectTransform worldOutline = FindOutline(f.Extension);
            RectTransform detailOutline = FindOutline(f.ExtensionDetail);
            Check(ReferenceEquals((worldArt.GetComponent<Image>()).sprite, shore), "S2/world-share-same-sprite");
            Check(detailOutline != null && detailOutline.GetComponent<Image>() != null &&
                ReferenceEquals(detailOutline.GetComponent<Image>().sprite, MapExtensionIslandArt.Outline),
                "S2/detail-outline-bound");
            Check(MapExtensionIslandArt.LeaseCount == 4, "S2/four-leases got=" + MapExtensionIslandArt.LeaseCount);

            // world suspend（state1）：detail/共享 shore 不得被撤（sprite + 租约保持）
            menu._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingSingleIsland;
            Tick(f, 1);
            Check(ReferenceEquals(landImage.sprite, shore) && MapExtensionIslandArt.LeaseCount == 4,
                "S2/suspend-keeps-detail-shore leases=" + MapExtensionIslandArt.LeaseCount);

            // icons OFF 但 current/visited 11 仍展示岸线（不随 MapCustomIconAssets.Reset 销毁）
            menu._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingWorld;
            Tick(f, 1);
            Tick(f, 1);
            ModConfig.CrossWorldMountsEnabled.Value = false;
            f.Map.UpdateLandIcons(f.Current);   // 原生刷新 → LayoutWithoutIcons（几何保留）
            UpdateDetails(f);
            Check(ReferenceEquals(landImage.sprite, shore), "S2/icons-off-detail-shore-kept");
            Check(ReferenceEquals(worldArt.GetComponent<Image>().sprite, shore), "S2/icons-off-world-shore-kept");
            Check(CountNamed(f.Paper.gameObject, "KEM_MapResourceIcons") == 0, "S2/icons-off-world-icons-withdrawn");

            // 旧/其它 sender（非 exact owner 的 menu）不得撤当前 owner 的租约
            var otherMenu = new GameObject("OtherMenu").AddComponent<MapTimelineMenuGreece>();
            otherMenu.OnDisable();
            Check(MapExtensionIslandArt.LeaseCount == 4 && ReferenceEquals(landImage.sprite, shore),
                "S2/foreign-sender-keeps-owner-leases leases=" + MapExtensionIslandArt.LeaseCount);

            // exact OnDisable：先恢复 native sprite，再在无责任时释放自有资源
            menu.OnDisable();
            Check(landImage.sprite != null && !ReferenceEquals(landImage.sprite, shore), "S2/disable-restores-native");
            Check(MapExtensionIslandArt.LeaseCount == 0, "S2/disable-releases-leases got=" + MapExtensionIslandArt.LeaseCount);
            Check(MapExtensionIslandArt.Prep == null, "S2/disable-destroys-cache");
        }

        // ------------------------------------------------------------------ S3–S6

        private static void ScenarioS3OwnerToken()
        {
            Console.WriteLine("== S3 owner token ==");
            int[] sixteen = { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };
            Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Tick(f, 1);
            Tick(f, 1);
            UpdateDetails(f);
            Check(MapExtensionIslandArt.LeaseCount == 4, "S3/baseline-leases=" + MapExtensionIslandArt.LeaseCount);

            // （a）外部/非登记 menu：OnDisable 不得撤当前 owner 的租约
            var foreign = new GameObject("ForeignMenu").AddComponent<MapTimelineMenuGreece>();
            foreign.OnDisable();
            Check(MapExtensionIslandArt.LeaseCount == 4, "S3/foreign-menu-no-op leases=" +
                MapExtensionIslandArt.LeaseCount);

            // （b）登记换代（同 menu 新 token）：重绑后旧 token 的释放不得撤新 token
            object token1 = ExtensionIslandMap.VisualOwnerToken;
            object token2 = new object();
            ExtensionIslandMap.VisualOwnerToken = token2;
            f.Greek._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingSingleIsland;
            Tick(f, 1);
            f.Greek._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingWorld;
            Tick(f, 1);
            Tick(f, 1);
            UpdateDetails(f);
            Check(ReadStatic("_shoreOwnerToken") is object bound && ReferenceEquals(bound, token2) &&
                MapExtensionIslandArt.LeaseCount == 4,
                "S3/rebind-new-token leases=" + MapExtensionIslandArt.LeaseCount);
            MapExtensionIslandArt.ReleaseOwner(token1);   // 旧 token 迟到释放
            Check(MapExtensionIslandArt.LeaseCount == 4, "S3/late-old-token-keeps-new leases=" +
                MapExtensionIslandArt.LeaseCount);
            f.Greek.OnDisable();                           // 当前登记 menu 的收尾才释放新 token
            Check(MapExtensionIslandArt.LeaseCount == 0 && ReadStatic("_shoreOwnerToken") == null,
                "S3/exact-menu-releases-new-token leases=" + MapExtensionIslandArt.LeaseCount);
        }

        private static void ScenarioS4ColdDetail()
        {
            Console.WriteLine("== S4 cold detail ==");
            int[] sixteen = { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };
            Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            // 不跑 world commit/Tick：直接 detail 的 UpdateLand（真实 menu.UpdateLands 路径）——冷启动必须能加载
            Check(MapExtensionIslandArt.Prep == null && MapExtensionIslandArt.Shore == null, "S4/no-art-before");
            UpdateDetails(f);
            Check(MapExtensionIslandArt.Shore != null &&
                ReferenceEquals(f.DetailLandImage.sprite, MapExtensionIslandArt.Shore),
                "S4/cold-detail-binds-without-world");
            RectTransform landRect = f.ExtensionDetail.gameObject.GetComponent<RectTransform>();
            MapIconBox box = LandLocalBox(landRect, f.DetailLandImage.transform as RectTransform);
            Check(Math.Abs(box.Width - 230f) < 1f &&
                Math.Abs(box.X0 - (landRect.rect.width * 0.5f - 92f)) < 1.5f,
                "S4/cold-detail-geometry w=" + box.Width.ToString("0.#"));
            f.Greek.OnDisable();
        }

        private static void ScenarioS5DetailSnapshotIsolation()
        {
            Console.WriteLine("== S5 detail snapshot isolation ==");
            int[] sixteen = { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };
            Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Tick(f, 1);
            Tick(f, 1);
            UpdateDetails(f);
            RectTransform detailArt = f.DetailLandImage.transform as RectTransform;
            Vector2 appliedSize = detailArt.sizeDelta;
            Vector3 appliedScale = detailArt.localScale;
            Vector2 appliedPos = detailArt.anchoredPosition;
            Check(appliedSize.x > 200f, "S5/detail-shape-applied size=" + appliedSize.x);

            // world 0→1→0 两轮：detail 几何不得被 world 的 RestoreExpansion 撤掉
            for (int round = 0; round < 2; round++)
            {
                f.Greek._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingSingleIsland;
                Tick(f, 1);
                Check(Math.Abs(detailArt.sizeDelta.x - appliedSize.x) < 0.01f &&
                    Math.Abs(detailArt.localScale.x - appliedScale.x) < 0.001f &&
                    Math.Abs(detailArt.anchoredPosition.x - appliedPos.x) < 0.01f &&
                    Math.Abs(detailArt.anchoredPosition.y - appliedPos.y) < 0.01f,
                    "S5/detail-geometry-survives-suspend#" + round);
                f.Greek._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingWorld;
                Tick(f, 1);
                Tick(f, 1);
            }

            // ClearLands：detail 快照原样归还（sizeDelta/scale/anchoredPosition + sprite）
            f.Greek.ClearLands();
            Check(Math.Abs(detailArt.sizeDelta.x - 114f) < 0.5f && Math.Abs(detailArt.sizeDelta.y - 82f) < 0.5f &&
                Math.Abs(detailArt.anchoredPosition.x - 25f) < 0.5f && Math.Abs(detailArt.anchoredPosition.y - 30f) < 0.5f &&
                Math.Abs(detailArt.localScale.x - 1f) < 0.001f,
                "S5/detail-restored-on-clear size=" + detailArt.sizeDelta + " pos=" + detailArt.anchoredPosition);
            Check(f.DetailLandImage.sprite == f.DetailNativeSprite, "S5/detail-sprite-restored-on-clear");
        }

        private static void ScenarioS6TransactionRollback()
        {
            Console.WriteLine("== S6 transaction ==");
            int[] sixteen = { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };
            Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            RectTransform art = ArtOf(f.Extension) as RectTransform;
            RectTransform outline = FindOutline(f.Extension);
            Image artImage = art.GetComponent<Image>();
            Image outlineImage = outline.GetComponent<Image>();
            Sprite nativeArt = artImage.sprite;
            Sprite nativeOutline = outlineImage.sprite;
            outlineImage.ThrowOnSet = true;      // outline 绑定失败 → **整次** world 提交回滚（四 Image 同准备态）
            Tick(f, 1);
            Tick(f, 1);
            Check(ReferenceEquals(artImage.sprite, nativeArt) && ReferenceEquals(outlineImage.sprite, nativeOutline),
                "S6/outline-failure-rolls-back-terrain");
            Check(!(bool)ReadStatic("_overviewApplied"), "S6/outline-failure-blocks-commit");
            Check(f.DetailLandImage.sprite == f.DetailNativeSprite, "S6/outline-failure-detail-native");
            Check(MapExtensionIslandArt.LeaseCount == 0, "S6/outline-failure-no-partial-lease leases=" +
                MapExtensionIslandArt.LeaseCount);

            outlineImage.ThrowOnSet = false;     // 恢复后重试成功
            f.Greek._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingSingleIsland;
            Tick(f, 1);
            f.Greek._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingWorld;
            Tick(f, 1);
            Tick(f, 1);
            Check(ReferenceEquals(artImage.sprite, MapExtensionIslandArt.Shore) &&
                ReferenceEquals(outlineImage.sprite, MapExtensionIslandArt.Outline),
                "S6/retry-binds-after-recovery");
            f.Greek.OnDisable();
        }

        private static void ScenarioS7PendingGate()
        {
            Console.WriteLine("== S7 pending gate ==");
            int[] sixteen = { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };

            // （a）初开：自有 CanvasGroup alpha=0（**activeSelf 保持、绝不 SetActive**）；两帧 commit 后同帧 reveal
            Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Tick(f, 1);
            Check(f.Map.gameObject.activeSelf, "S7/first-tick-active-self-kept");
            Check(GateAlpha(f) == 0f, "S7/first-tick-gated alpha=" + GateAlpha(f));
            Check(f.Map.gameObject.SetActiveCalls == 0,
                "S7/no-setactive-on-mainmap calls=" + f.Map.gameObject.SetActiveCalls);
            Tick(f, 1);
            Check(f.Map.gameObject.activeSelf && GateAlpha(f) == 1f, "S7/committed-revealed alpha=" + GateAlpha(f));
            Check(f.Map.gameObject.SetActiveCalls == 0, "S7/reveal-still-no-setactive");

            // （b）single：门释放（详情/纸可见）；回 world 重新压制；外部把 alpha 写回也被本帧重压
            f.Greek._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingSingleIsland;
            Tick(f, 1);
            Check(GateAlpha(f) == 1f, "S7/single-visible");
            f.Greek._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingWorld;
            Tick(f, 1);
            Check(GateAlpha(f) == 0f, "S7/return-gated");
            Tick(f, 1);
            Check(GateAlpha(f) == 1f, "S7/return-revealed");

            // （c）外部/旧 sender 的 tick 不得撤新 owner 的门
            Fixture k = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Tick(k, 1);
            Check(GateAlpha(k) == 0f, "S7/foreign-scene-gated");
            var foreignMenu = new GameObject("ForeignTick").AddComponent<MapTimelineMenuGreece>();
            foreignMenu.isAnimating = false;
            foreignMenu.HasScrolledTargetLand = true;
            foreignMenu.Update();   // activeMap != foreign → 不动门
            Check(GateAlpha(k) == 0f, "S7/foreign-tick-keeps-gate");
            Tick(k, 1);
            Check(GateAlpha(k) == 1f, "S7/foreign-scene-revealed");

            // （d）测量永不稳定：有界超时 fail-open；同 tuple 不热循环；**同尺寸新 owner B** 仍正常准备
            Fixture h = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            SetVisible(h, 0f, 0f);   // mask 退化（随后被 ExpandPaper 的 fit 覆盖，仅示意）
            Screen.width = 0;        // 屏幕退化：测量必然失败 → 永不 commit（持续 pending）
            Screen.height = 0;
            Tick(h, 3);
            Check(GateAlpha(h) == 0f, "S7/unstable-gated");
            GateGroup(h).alpha = 1f;      // 外部/原生若把 alpha 写回：下一帧渲染前重新压制（不动 activeSelf）
            Tick(h, 1);
            Check(GateAlpha(h) == 0f, "S7/re-pressed-after-external-alpha");
            Tick(h, 129);                 // 超过 WorldGateMaxFrames → 自有有界超时
            Check(GateAlpha(h) == 1f, "S7/timeout-fail-open");
            bool stayedVisible = true;
            for (int i = 0; i < 5; i++)
            {
                Tick(h, 1);
                if (GateAlpha(h) != 1f) stayedVisible = false;
            }
            Check(stayedVisible, "S7/no-hot-loop-after-timeout");
            Screen.width = 1280;
            Screen.height = 720;
            h.Greek.OnDisable();
            // 新 B：不同 owner token/新登记代际（同 viewport 尺寸）→ 失败 memo 不拖累，正常准备+reveal
            Fixture b = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Tick(b, 1);
            Check(GateAlpha(b) == 0f, "S7/new-owner-same-size-gated");
            Tick(b, 1);
            Check(GateAlpha(b) == 1f, "S7/new-owner-revealed");

            // （d2）重复帧（同一 frameCount 的两次 Update）：不得二次推进/绕过两帧一致测量直接 commit
            Fixture d = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Tick(d, 1);
            Check(GateAlpha(d) == 0f, "S7/dupframe-gated");
            d.Greek.Update();   // 同 frameCount 第二次（cycle.LastTickFrame 去重）
            d.Greek.Update();
            Check(GateAlpha(d) == 0f && !(bool)ReadStatic("_overviewApplied"), "S7/dupframe-no-commit");
            Tick(d, 1);
            Check(GateAlpha(d) == 1f, "S7/dupframe-next-frame-commits");
            d.Greek.OnDisable();

            // （e）ClearLands：门立即释放 + 自有组销毁（activeSelf 保持）
            Fixture m = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Tick(m, 1);
            Check(GateAlpha(m) == 0f, "S7/clear-scene-gated");
            m.Greek.ClearLands();
            Check(GateGroup(m) == null, "S7/clear-destroys-own-group");
            Check(m.Map.gameObject.activeSelf, "S7/clear-active-self-kept");

            // （f）fixture 已有 foreign CanvasGroup：拒绝门（fail-open；绝不 AddComponent、绝不动 foreign 组）
            Fixture n = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            var foreignGroup = n.Map.gameObject.AddComponent<CanvasGroup>();
            foreignGroup.alpha = 1f;
            int addsBefore = n.Map.gameObject.Adds;
            int foreignWritesBefore = foreignGroup.AlphaWrites;
            Tick(n, 1);
            Check(n.Map.gameObject.Adds == addsBefore, "S7/foreign-no-add");
            Check(foreignGroup.AlphaWrites == foreignWritesBefore, "S7/foreign-no-alpha-write");
            Tick(n, 1);
            Check(foreignGroup.alpha == 1f && foreignGroup.AlphaWrites == foreignWritesBefore,
                "S7/foreign-left-untouched");
        }

        // ------------------------------------------------------------------ S8

        private static void ScenarioS8RocksAnchor()
        {
            Console.WriteLine("== S8 rocks anchor ==");
            int[] sixteen = { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };
            Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Vector2 rocksNative = f.Rocks.anchoredPosition;
            Vector2 decorNative = f.OtherDecor.anchoredPosition;
            Tick(f, 1);
            Tick(f, 1);
            Check(f.Map.gameObject.activeSelf && GateAlpha(f) == 1f, "S8/world-revealed");

            MapIconBox shore = PaperArtBox(f.Paper, ArtOf(f.Extension));
            MapIconBox rocks = PaperArtBox(f.Paper, f.Rocks);
            Console.WriteLine("[S8] rocks " + Fmt(rocks) + " pos=" + f.Rocks.anchoredPosition +
                " shore " + Fmt(shore));
            Check(rocks.Y0 >= shore.Y1 + 2f, "S8/rocks-above-shore-gap rocks=" + Fmt(rocks) + " shore=" + Fmt(shore));
            Check(!rocks.Intersects(shore, 0.5f), "S8/rocks-not-over-shore");
            bool nativeClash = false;
            for (int i = 0; i < 10; i++)
            {
                MapIconBox art = PaperArtBox(f.Paper, ArtOf(f.Lands[i]));
                if (rocks.Intersects(art, 0.5f)) nativeClash = true;
            }
            Check(!nativeClash, "S8/rocks-no-native-clash " + Fmt(rocks));
            RectTransform boat = FindChild(f.Paper, "Boat Icon");
            if (boat != null)
            {
                MapIconBox boatBox = PaperArtBox(f.Paper, boat);
                Check(!rocks.Intersects(boatBox, 0.5f), "S8/rocks-no-boat-clash boat=" + Fmt(boatBox));
            }
            Check(SameVec(f.OtherDecor.anchoredPosition, decorNative), "S8/other-decor-untouched");

            // world 子树 Suspend（切详情）→ 礁石与其余 world 几何一并归还
            f.Greek._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingSingleIsland;
            Tick(f, 1);
            Check(SameVec(f.Rocks.anchoredPosition, rocksNative),
                "S8/rocks-restored-on-suspend pos=" + f.Rocks.anchoredPosition);
        }

        private static int DeferredReleaseCount()
        {
            object list = ReadStatic("_deferredReleases");
            return list is System.Collections.ICollection collection ? collection.Count : -1;
        }

        private static CanvasGroup GateGroup(Fixture f)
            => f.Map.gameObject.GetComponent<CanvasGroup>();

        /// <summary>门 alpha：无组时视作中性 1（未 gated）。</summary>
        private static float GateAlpha(Fixture f)
        {
            CanvasGroup group = GateGroup(f);
            return group != null ? group.alpha : 1f;
        }

        private static Image ArtImage(UILand land)
        {
            Transform art = ArtOf(land);
            return art != null ? art.GetComponent<Image>() : null;
        }

        // ------------------------------------------------------------------ S9

        /// <summary>
        /// 四 Image + geometry 同一准备/事务（真实 Commit/OnMenuTick 入口）+ 真实 ClearLands 先后顺序。
        /// 任一 terrain/outline 读/写/写后抛或几何 setter 故障 → 整次提交回滚：不写 applied、不开 reveal、
        /// 两个 view 都不出现"一个原 Oracle 一个 new shape"的混合可见态。
        /// </summary>
        private static void ScenarioS9CommitBarrier()
        {
            Console.WriteLine("== S9 commit barrier ==");
            int[] sixteen = { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };

            // （a）基线：四 Image 同源绑定（overview terrain/outline + detail terrain/outline）
            Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Sprite overviewNative = ArtImage(f.Extension).sprite;
            Image overviewOutline = FindOutline(f.Extension).GetComponent<Image>();
            Sprite overviewOutlineNative = overviewOutline.sprite;
            RectTransform detailArt = f.DetailLandImage.transform as RectTransform;
            Vector2 detailNativeSize = detailArt.sizeDelta;
            Tick(f, 2);
            Check((bool)ReadStatic("_overviewApplied"), "S9/baseline-applied");
            Sprite shore = MapExtensionIslandArt.Shore;
            Check(shore != null && ReferenceEquals(ArtImage(f.Extension).sprite, shore), "S9/baseline-overview-terrain");
            Check(ReferenceEquals(overviewOutline.sprite, MapExtensionIslandArt.Outline), "S9/baseline-overview-outline");
            Check(ReferenceEquals(f.DetailLandImage.sprite, shore), "S9/baseline-detail-terrain");
            Check(ReferenceEquals(FindOutline(f.ExtensionDetail).GetComponent<Image>().sprite,
                MapExtensionIslandArt.Outline), "S9/baseline-detail-outline");
            Check(MapExtensionIslandArt.LeaseCount == 4, "S9/baseline-four-leases=" + MapExtensionIslandArt.LeaseCount);

            // （b）overview terrain 写时抛
            Fixture g = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            ArtImage(g.Extension).ThrowOnSet = true;
            Tick(g, 1);
            Tick(g, 1);
            Check(!(bool)ReadStatic("_overviewApplied"), "S9/overview-terrain-fault-aborts-commit");
            Check(GateAlpha(g) == 0f, "S9/overview-terrain-fault-keeps-gate");
            Check(g.DetailLandImage.sprite == g.DetailNativeSprite, "S9/overview-terrain-fault-detail-native");
            Check(MapExtensionIslandArt.LeaseCount == 0, "S9/overview-terrain-fault-no-lease=" +
                MapExtensionIslandArt.LeaseCount);

            // （c）overview outline 写后抛（字段已写再抛：不得留半提交）
            Fixture c = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Sprite cOverviewNative = ArtImage(c.Extension).sprite;
            FindOutline(c.Extension).GetComponent<Image>().ThrowAfterSet = true;
            Tick(c, 1);
            Tick(c, 1);
            // 写后抛：字段已写入（真实 setter 非原子）→ 租约按 Applied 收敛；提交可以成功，但**两个 view 必须同源**。
            bool cApplied = (bool)ReadStatic("_overviewApplied");
            bool cSameSource = ReferenceEquals(ArtImage(c.Extension).sprite, MapExtensionIslandArt.Shore) ==
                ReferenceEquals(c.DetailLandImage.sprite, MapExtensionIslandArt.Shore);
            Check(cSameSource, "S9/overview-outline-afterthrow-same-source applied=" + cApplied);
            c.Greek.ClearLands();
            Check(ReferenceEquals(ArtImage(c.Extension).sprite, cOverviewNative) &&
                c.DetailLandImage.sprite == c.DetailNativeSprite,
                "S9/overview-outline-afterthrow-release-restores-both");

            // （d）overview terrain 读故障（getter 抛）
            Fixture r = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            ArtImage(r.Extension).ThrowOnGet = true;
            Tick(r, 1);
            Tick(r, 1);
            Check(!(bool)ReadStatic("_overviewApplied"), "S9/overview-terrain-readfault-aborts-commit");
            Check(GateAlpha(r) == 0f, "S9/overview-terrain-readfault-keeps-gate");
            Check(r.DetailLandImage.sprite == r.DetailNativeSprite, "S9/overview-terrain-readfault-detail-native");

            // （e）detail terrain 写时抛：world 侧已写绑定必须一并归还（不给 world 新 shape/detail 旧 Oracle）
            Fixture i = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Sprite iOverviewNative = ArtImage(i.Extension).sprite;
            Image iOverviewOutline = FindOutline(i.Extension).GetComponent<Image>();
            Sprite iOutlineNative = iOverviewOutline.sprite;
            i.DetailLandImage.ThrowOnSet = true;
            Tick(i, 2);
            Check(!(bool)ReadStatic("_overviewApplied"), "S9/detail-terrain-fault-aborts-commit");
            Check(GateAlpha(i) == 0f, "S9/detail-terrain-fault-keeps-gate");
            Check(ReferenceEquals(ArtImage(i.Extension).sprite, iOverviewNative),
                "S9/detail-terrain-fault-world-terrain-restored");
            Check(ReferenceEquals(iOverviewOutline.sprite, iOutlineNative),
                "S9/detail-terrain-fault-world-outline-restored");
            Check(i.DetailLandImage.sprite == i.DetailNativeSprite, "S9/detail-terrain-fault-detail-native");
            Check(MapExtensionIslandArt.LeaseCount == 0, "S9/detail-terrain-fault-no-lease=" +
                MapExtensionIslandArt.LeaseCount);

            // （f）detail outline 写后抛（同理：写已落地 → 允许成功，但两 view 同源；释放必须两个都回 native）
            Fixture o = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Sprite oOverviewNative = ArtImage(o.Extension).sprite;
            Image oDetailOutline = FindOutline(o.ExtensionDetail).GetComponent<Image>();
            Sprite oDetailOutlineNative = oDetailOutline.sprite;
            oDetailOutline.ThrowAfterSet = true;
            Tick(o, 2);
            bool oSameSource = ReferenceEquals(ArtImage(o.Extension).sprite, MapExtensionIslandArt.Shore) ==
                ReferenceEquals(o.DetailLandImage.sprite, MapExtensionIslandArt.Shore);
            Check(oSameSource, "S9/detail-outline-afterthrow-same-source applied=" +
                ReadStatic("_overviewApplied"));
            o.Greek.ClearLands();
            Check(ReferenceEquals(ArtImage(o.Extension).sprite, oOverviewNative) &&
                ReferenceEquals(o.DetailLandImage.sprite, o.DetailNativeSprite) &&
                ReferenceEquals(oDetailOutline.sprite, oDetailOutlineNative),
                "S9/detail-outline-afterthrow-release-restores-all");

            // （g）几何 setter 故障：detail art sizeDelta 抛 / overview art sizeDelta 抛
            Fixture j = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Sprite jOverviewNative = ArtImage(j.Extension).sprite;
            ((RectTransform)j.DetailLandImage.transform).ThrowOnSizeDeltaSet = true;
            Tick(j, 2);
            Check(!(bool)ReadStatic("_overviewApplied"), "S9/detail-geometry-fault-aborts-commit");
            Check(ReferenceEquals(ArtImage(j.Extension).sprite, jOverviewNative),
                "S9/detail-geometry-fault-world-restored");
            Check(j.DetailLandImage.sprite == j.DetailNativeSprite, "S9/detail-geometry-fault-detail-native");
            Fixture q = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Sprite qOverviewNative = ArtImage(q.Extension).sprite;
            ((RectTransform)ArtOf(q.Extension)).ThrowOnSizeDeltaSet = true;
            Tick(q, 2);
            Check(!(bool)ReadStatic("_overviewApplied"), "S9/overview-geometry-fault-aborts-commit");
            Check(ReferenceEquals(ArtImage(q.Extension).sprite, qOverviewNative),
                "S9/overview-geometry-fault-restored");
            Check(q.DetailLandImage.sprite == q.DetailNativeSprite, "S9/overview-geometry-fault-detail-native");

            // （h）真实 ClearLands 顺序（在独立 fixture 上：ExtensionIslandMap.OnLandsCleared 先清 token，
            // 才轮到 MapMountIcons.OnMenuLandsCleared —— registry 查询此刻必然已拒绝，收尾必须靠捕获身份）
            Fixture z = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Sprite zOverviewNative = ArtImage(z.Extension).sprite;
            Image zOverviewOutline = FindOutline(z.Extension).GetComponent<Image>();
            Sprite zOutlineNative = zOverviewOutline.sprite;
            RectTransform zDetailArt = z.DetailLandImage.transform as RectTransform;
            Vector2 zDetailNativeSize = zDetailArt.sizeDelta;
            Tick(z, 2);
            Check(ReferenceEquals(ArtImage(z.Extension).sprite, MapExtensionIslandArt.Shore) &&
                ReferenceEquals(z.DetailLandImage.sprite, MapExtensionIslandArt.Shore),
                "S9/clear-pre-both-views-bound");
            z.Greek.ClearLands();
            Check(ExtensionIslandMap.TryGetVisualOwnerToken() == null, "S9/clear-registry-cleared-first");
            Check(ReferenceEquals(ArtImage(z.Extension).sprite, zOverviewNative), "S9/clear-overview-terrain-restored");
            Check(ReferenceEquals(zOverviewOutline.sprite, zOutlineNative), "S9/clear-overview-outline-restored");
            Check(ReferenceEquals(z.DetailLandImage.sprite, z.DetailNativeSprite), "S9/clear-detail-restored");
            Check(MapExtensionIslandArt.LeaseCount == 0 && MapExtensionIslandArt.PendingRestores == 0,
                "S9/clear-no-leak leases=" + MapExtensionIslandArt.LeaseCount +
                " pending=" + MapExtensionIslandArt.PendingRestores);
            Check(Math.Abs(zDetailArt.sizeDelta.x - zDetailNativeSize.x) < 0.5f &&
                Math.Abs(zDetailArt.sizeDelta.y - zDetailNativeSize.y) < 0.5f,
                "S9/clear-detail-geometry-restored size=" + zDetailArt.sizeDelta);
            Check(GateGroup(z) == null, "S9/clear-gate-group-destroyed");

            // （i）迟到旧 owner A 的 Clear 不得撤新 owner B 的绑定/门
            Fixture a = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Tick(a, 2);
            Fixture b = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Tick(b, 2);
            Sprite bShore = MapExtensionIslandArt.Shore;
            Check(ReferenceEquals(ArtImage(b.Extension).sprite, bShore), "S9/b-baseline");
            ProbeHooks.RunClearLandsPostfix(a.Greek);   // 真实 patch 顺序的 stale A
            Check(ReferenceEquals(ArtImage(b.Extension).sprite, bShore) &&
                ReferenceEquals(b.DetailLandImage.sprite, bShore), "S9/stale-clear-keeps-new-binding");
            Check(GateGroup(b) != null && GateAlpha(b) == 1f, "S9/stale-clear-keeps-new-gate-state");
            Check(MapExtensionIslandArt.LeaseCount == 4, "S9/stale-clear-keeps-new-leases=" +
                MapExtensionIslandArt.LeaseCount);
        }

        /// <summary>真实 16 尺寸表（与 fixture resolver / runtime facts 同源）。</summary>
        private static readonly Dictionary<int, Vector2> SixteenSizes = new Dictionary<int, Vector2>
        {
            { 21, new Vector2(25f, 16f) }, { 22, new Vector2(24f, 22f) }, { 23, new Vector2(21f, 26f) },
            { 24, new Vector2(25f, 17f) }, { 25, new Vector2(21f, 18f) }, { 26, new Vector2(21f, 18f) },
            { 13, new Vector2(29f, 13f) }, { 14, new Vector2(29f, 13f) }, { 15, new Vector2(26f, 14f) },
            { 16, new Vector2(28f, 18f) }, { 2, new Vector2(28f, 13f) }, { 4, new Vector2(40f, 25f) },
            { 7, new Vector2(22f, 19f) }, { 3, new Vector2(24f, 32f) }, { 38, new Vector2(40f, 26f) },
            { 6, new Vector2(20f, 20f) },
        };

        /// <summary>
        /// S11：exact extension detail 的实际布局（actual root 180×150 / page 282×196 / 同顶面 PlacementMask
        /// 自由错落 / native 0..9 不变 / 不可满足时整体 fallback）。
        /// </summary>
        private static void ScenarioS11ExtensionDetailLayout()
        {
            Console.WriteLine("== S11 extension detail layout ==");
            int[] types = { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };
            Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: types);
            // bind 前核 actual 旧 art 中心 (-8,-4)（anchor0/pivot0/ap(25,30)/114×82，180×150 root）
            RectTransform preRect = f.ExtensionDetail.gameObject.GetComponent<RectTransform>();
            MapIconBox nativeArt = LandLocalBox(preRect, f.DetailLandImage.transform as RectTransform);
            float preCx = preRect.rect.width * 0.5f, preCy = preRect.rect.height * 0.5f;
            Check(Math.Abs((nativeArt.X0 + nativeArt.X1) * 0.5f - preCx + 8f) < 0.6f &&
                Math.Abs((nativeArt.Y0 + nativeArt.Y1) * 0.5f - preCy + 4f) < 0.6f,
                "S11/native-art-center(-8,-4) got=(" + ((nativeArt.X0 + nativeArt.X1) * 0.5f - preCx).ToString("0.#") +
                "," + ((nativeArt.Y0 + nativeArt.Y1) * 0.5f - preCy).ToString("0.#") + ")");
            Tick(f, 2);
            UpdateDetails(f);
            RectTransform detailRect = preRect;
            Check(Math.Abs(detailRect.rect.width - 180f) < 0.1f && Math.Abs(detailRect.rect.height - 150f) < 0.1f,
                "S11/actual-root-180x150 got=" + detailRect.rect);

            if (ReadStatic("_detailExcludedRects") is List<RectTransform> shoreRects && shoreRects.Count >= 2 &&
                ReadStatic("_placementMask") is MapShoreMask mask)
            {
                // holder 坐标空间：生产给 holder/icon 写 anchor(0,0)/pivot(0,0)+anchoredPosition；真实 Unity 中
                // icon 的 “land 左下原点框” == placement。本 stub 的 anchoredPosition 是归一化世界 fixture 语义，
                // 少一个“父 rect 原点 - 锚点参考”常量 → 统一把该空间量平移到 holder 空间后再断言。
                RectTransform holderRt = CollectHolder(detailRect, 16);
                Check(holderRt != null, "S11/holder-found");
                MapIconBox holderBox = holderRt != null ? LandLocalBox(detailRect, holderRt) : default;
                float shiftX = holderBox.X0, shiftY = holderBox.Y0;
                MapIconBox shoreBox = LandLocalBox(detailRect, shoreRects[0]);
                float centerX = detailRect.rect.width * 0.5f;
                float centerY = detailRect.rect.height * 0.5f;
                var page = new MapIconBox(centerX - MapExtensionShapePlan.DetailPageHalfWidth,
                    centerY - MapExtensionShapePlan.DetailPageHalfHeight,
                    centerX + MapExtensionShapePlan.DetailPageHalfWidth,
                    centerY + MapExtensionShapePlan.DetailPageHalfHeight);
                List<RectTransform> icons = CollectIcons(detailRect, 16);
                Check(icons.Count == 16, "S11/16-icons got=" + icons.Count);
                var boxes = new List<MapIconBox>(icons.Count);
                foreach (RectTransform icon in icons)
                {
                    MapIconBox raw = LandLocalBox(detailRect, icon);
                    boxes.Add(new MapIconBox(raw.X0 - shiftX, raw.Y0 - shiftY, raw.X1 - shiftX, raw.Y1 - shiftY));
                }
                int placementInside = 0, pageInside = 0;
                foreach (MapIconBox box in boxes)
                {
                    if (MapExtensionIslandLayout.FootprintInsideShore(box, shoreBox, mask)) placementInside++;
                    if (box.X0 >= page.X0 - 0.01f && box.X1 <= page.X1 + 0.01f &&
                        box.Y0 >= page.Y0 - 0.01f && box.Y1 <= page.Y1 + 0.01f) pageInside++;
                }
                Check(placementInside == 16, "S11/all-placement-inside got=" + placementInside + "/16");
                Check(pageInside == 16, "S11/all-page-inside got=" + pageInside + "/16");
                ScatterBoxes(boxes, shoreBox, "S11");

                var blockers = new List<MapIconBox>(24);
                CollectObstaclesViaProduction(detailRect, f.ExtensionDetail.transform, blockers, true, shoreRects);
                int blockedOverlap = 0;
                foreach (MapIconBox box in boxes)
                {
                    foreach (MapIconBox blocker in blockers)
                    {
                        if (box.Intersects(blocker, 0.01f)) { blockedOverlap++; break; }
                    }
                }
                Check(blockedOverlap == 0, "S11/no-blocker-overlap got=" + blockedOverlap);

                // 与 pure 同算法比对（area/blockers/shore/mask 相同 → 相同 boxes + scale ≥ 0.6）
                var requests = new List<MapIconRequest>(16);
                for (int i = 0; i < types.Length; i++)
                {
                    Vector2 size = SixteenSizes[types[i]];
                    requests.Add(new MapIconRequest(MapIconKind.Steed, types[i], i, size.x, size.y));
                }
                float inset = MapExtensionShapePlan.Margin;
                var area = new MapIconBox(
                    Math.Max(shoreBox.X0 + inset, page.X0), Math.Max(shoreBox.Y0 + inset, page.Y0),
                    Math.Min(shoreBox.X1 - inset, page.X1), Math.Min(shoreBox.Y1 - inset, page.Y1));
                var expected = new List<MapIconPlacement>(16);
                bool ok = MapExtensionIslandLayout.TryPlan(area, requests, blockers, shoreBox, mask,
                    expected, out float expectedScale, out int expectedFailed, MapExtensionIslandLayout.PreferScale);
                Check(ok && expectedFailed == 0 && expectedScale >= MapExtensionIslandLayout.PreferScale,
                    "S11/pure-scatter scale=" + expectedScale);
                int matched = 0;
                foreach (MapIconPlacement placement in expected)
                {
                    var expectedBox = new MapIconBox(placement.X, placement.Y,
                        placement.X + placement.Request.Width * placement.Scale,
                        placement.Y + placement.Request.Height * placement.Scale);
                    foreach (MapIconBox box in boxes)
                    {
                        if (Math.Abs(box.X0 - expectedBox.X0) < 0.05f && Math.Abs(box.Y0 - expectedBox.Y0) < 0.05f &&
                            Math.Abs(box.X1 - expectedBox.X1) < 0.05f && Math.Abs(box.Y1 - expectedBox.Y1) < 0.05f)
                        {
                            matched++;
                            break;
                        }
                    }
                }
                Check(matched == 16, "S11/matches-pure-placement matched=" + matched);
            }
            else
            {
                Check(false, "S11/shore-bindings-missing");
            }

            // 精确 native detail boat 框（同一实际 fixture 的静态模板数值；不被任何绘制手挪）：
            // page 空间（相对 root 中心）AABB[-64,2,-32,34] ⇔ root 左下 (26,77,58,109)，32×32@scale1。
            if (f.DetailBoat != null)
            {
                MapIconBox boatRaw = LandLocalBox(detailRect, f.DetailBoat);
                Check(Math.Abs(boatRaw.X0 - 26f) < 0.05f && Math.Abs(boatRaw.Y0 - 77f) < 0.05f &&
                    Math.Abs(boatRaw.X1 - 58f) < 0.05f && Math.Abs(boatRaw.Y1 - 109f) < 0.05f,
                    "S11/native-boat-box(26,77,58,109) got=" + Fmt(boatRaw));
                Check(Math.Abs(f.DetailBoat.sizeDelta.x - 32f) < 1e-3f &&
                    Math.Abs(f.DetailBoat.sizeDelta.y - 32f) < 1e-3f &&
                    Math.Abs(f.DetailBoat.localScale.x - 1f) < 1e-4f &&
                    Math.Abs(f.DetailBoat.localScale.y - 1f) < 1e-4f,
                    "S11/native-boat-32@scale1 size=" + f.DetailBoat.sizeDelta +
                    " scale=" + f.DetailBoat.localScale);
                MapIconBox boatPage = new MapIconBox(boatRaw.X0 - detailRect.rect.width * 0.5f,
                    boatRaw.Y0 - detailRect.rect.height * 0.5f,
                    boatRaw.X1 - detailRect.rect.width * 0.5f,
                    boatRaw.Y1 - detailRect.rect.height * 0.5f);
                Check(Math.Abs(boatPage.X0 + 64f) < 0.05f && Math.Abs(boatPage.Y0 - 2f) < 0.05f &&
                    Math.Abs(boatPage.X1 + 32f) < 0.05f && Math.Abs(boatPage.Y1 - 34f) < 0.05f,
                    "S11/native-boat-page(-64,2,-32,34) got=" + Fmt(boatPage));
            }
            else
            {
                Check(false, "S11/native-boat-fixture-missing");
            }

            // native 0..9（issue-152 岛内契约）：唯一 steed 条目落在 Steed1 原生槽位盒内（位置/尺度保留），
            // 且整个 footprint 位于本岛底图（Land Image）盒内 —— 旧的“land rect + margin 外边距面”已废除。
            UILand nativeDetail = f.Greek.lands[0];
            RectTransform nativeRect = nativeDetail.gameObject.GetComponent<RectTransform>();
            List<RectTransform> nativeIcons = CollectIcons(nativeRect, 1);
            Check(nativeIcons.Count == 1, "S11/native-detail-icons got=" + nativeIcons.Count);
            if (nativeIcons.Count == 1)
            {
                // stub 归一化：holder 的 anchoredPosition(0,0) 落成 localPosition(0,0)（缺“父 rect 原点 - 锚点
                // 参考”常量）→ 图标盒先扣除 holder 盒原点，回到真实 land 左下的 placement 空间。
                RectTransform nativeHolderRt = CollectHolder(nativeRect, 1);
                MapIconBox nativeHolderBox = nativeHolderRt != null
                    ? LandLocalBox(nativeRect, nativeHolderRt) : default;
                MapIconBox rawActual = LandLocalBox(nativeRect, nativeIcons[0]);
                MapIconBox actual = new MapIconBox(rawActual.X0 - nativeHolderBox.X0, rawActual.Y0 - nativeHolderBox.Y0,
                    rawActual.X1 - nativeHolderBox.X0, rawActual.Y1 - nativeHolderBox.Y0);
                MapIconBox slotBox = DetailSlotBox(nativeRect, "Steed1");
                RectTransform landImage = FindChild(nativeRect, "Land Image");
                MapIconBox artBox = landImage != null ? LandLocalBox(nativeRect, landImage) : default;
                Check(Inside(actual, artBox, 0.05f), "S11/native-icon-inside-art actual=" + Fmt(actual) +
                    " art=" + Fmt(artBox));
                bool centered = Math.Abs((actual.X0 + actual.X1) * 0.5f - (slotBox.X0 + slotBox.X1) * 0.5f) < 0.05f &&
                    Math.Abs((actual.Y0 + actual.Y1) * 0.5f - (slotBox.Y0 + slotBox.Y1) * 0.5f) < 0.05f;
                bool nativeSize = Math.Abs(actual.Width - 20f) < 0.05f && Math.Abs(actual.Height - 20f) < 0.05f;
                Check(centered && nativeSize, "S11/native-first-slot-centered actual=" + Fmt(actual) +
                    " slot=" + Fmt(slotBox));
            }
            f.Greek.OnDisable();

            // 不可满足（全遮挡 blocker）→ 整体 fallback：无 holder/无部分图标
            Fixture g = BuildWorld(withExtension: true, steedCount: 16, types: types);
            Tick(g, 2);
            RectTransform cover = MakeRect("Full Blocker", g.ExtensionDetail.transform, new Vector2(300f, 200f),
                new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
            FixedAnchor(cover, g.ExtensionDetail.gameObject.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 0f));
            cover.gameObject.AddComponent<Image>();
            int before = CountNamed(g.ExtensionDetail.gameObject, "KEM_MapResourceIcons");
            UpdateDetails(g);
            int after = CountNamed(g.ExtensionDetail.gameObject, "KEM_MapResourceIcons");
            Check(after == 0, "S11/whole-fallback-no-partial holders=" + before + "->" + after);
            g.Greek.OnDisable();
        }

        private static bool TryLocalBoxOf(RectTransform space, RectTransform target, out MapIconBox box)
            => TryLocalBoxViaReflection(space, target, out box);

        private static bool TryLocalBoxViaReflection(RectTransform space, RectTransform target, out MapIconBox box)
        {
            box = default;
            try
            {
                System.Reflection.MethodInfo method = typeof(MapMountIcons).GetMethod("TryLocalBox",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static, null,
                    new[] { typeof(RectTransform), typeof(RectTransform), typeof(MapIconBox).MakeByRefType() }, null);
                if (method == null) return false;
                object[] args = { space, target, null };
                bool ok = (bool)method.Invoke(null, args);
                if (ok) box = (MapIconBox)args[2];
                return ok;
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// 独立 review 三例（P1 门换代 / P2 失败归还责任保留 / P3 detail 四图快路）——真实 callback 顺序。
        /// </summary>
        private static void ScenarioS10ReviewParity()
        {
            Console.WriteLine("== S10 review parity (P1/P2/P3) ==");
            int[] types = { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };

            // P1：A pending 满 120 帧（无 disable）→ 新 B 首 tick 必须隐藏、A 组中性退休；late A 不撤 B
            Fixture a = BuildWorld(withExtension: true, steedCount: 16, types: types);
            Screen.width = 0;
            Screen.height = 0;
            Tick(a, 120);
            Check(GateAlpha(a) == 0f, "S10/P1-A-pending-gated");
            _preserveState = true;
            Fixture b = BuildWorld(withExtension: true, steedCount: 16, types: types);
            _preserveState = false;
            _frame += 10;
            Tick(b, 1);
            Check(GateAlpha(b) == 0f, "S10/P1-new-owner-first-tick-hidden alpha=" + GateAlpha(b));
            Check(GateAlpha(a) == 1f, "S10/P1-old-owner-neutral alpha=" + GateAlpha(a));
            Tick(b, 1);
            Check((bool)ReadStatic("_overviewApplied") && GateAlpha(b) == 1f, "S10/P1-new-owner-commits");
            Tick(a, 1);
            Check(GateAlpha(b) == 1f, "S10/P1-late-A-tick-keeps-B");
            a.Greek.OnDisable();
            Check(GateAlpha(b) == 1f, "S10/P1-late-A-disable-keeps-B");
            ProbeHooks.RunClearLandsPostfix(a.Greek);
            Check(GateAlpha(b) == 1f && (bool)ReadStatic("_overviewApplied"), "S10/P1-late-A-clear-keeps-B");
            Screen.width = 1280;
            Screen.height = 720;
            b.Greek.OnDisable();

            // P2：committed 后 setter 临时失败 → 第一次 OnDisable 留责任（captured 身份入有界队列）；恢复后第二次
            // exact OnDisable 收尾（只收 A）；期间新 owner E 的绑定/租约不受影响
            Fixture c = BuildWorld(withExtension: true, steedCount: 16, types: types);
            Image img = ArtImage(c.Extension);
            Sprite nativeA = img.sprite;          // commit 前的 native（恢复目标）
            Tick(c, 2);
            img.ThrowOnSet = true;
            int queuedBeforeA = DeferredReleaseCount();
            c.Greek.OnDisable();
            Check(MapExtensionIslandArt.LeaseCount == 1 && DeferredReleaseCount() == queuedBeforeA + 1,
                "S10/P2-first-disable-retains leases=" + MapExtensionIslandArt.LeaseCount +
                " queued=" + DeferredReleaseCount());
            Fixture e = BuildWorld(withExtension: true, steedCount: 16, types: types);
            Tick(e, 2);
            Sprite eShore = MapExtensionIslandArt.Shore;
            Check(ReferenceEquals(ArtImage(e.Extension).sprite, eShore) &&
                ReferenceEquals(e.DetailLandImage.sprite, eShore) &&
                MapExtensionIslandArt.LeaseCount == 5,
                "S10/P2-new-owner-bound leases=" + MapExtensionIslandArt.LeaseCount);
            int leasesBefore = MapExtensionIslandArt.LeaseCount;
            int queuedBefore = DeferredReleaseCount();
            img.ThrowOnSet = false;
            c.Greek.OnDisable();   // A 的迟到 exact retry：只收 A 的租约
            Check(MapExtensionIslandArt.LeaseCount == leasesBefore - 1 &&
                DeferredReleaseCount() == queuedBefore - 1 && ReferenceEquals(img.sprite, nativeA),
                "S10/P2-second-exact-disable-drains leases=" + MapExtensionIslandArt.LeaseCount +
                " queued=" + DeferredReleaseCount() + " A-native=" + ReferenceEquals(img.sprite, nativeA));
            Check(ReferenceEquals(ArtImage(e.Extension).sprite, eShore) &&
                ReferenceEquals(e.DetailLandImage.sprite, eShore),
                "S10/P2-retry-does-not-touch-B");
            e.Greek.OnDisable();

            // P3：committed 后 detail outline 被外部换回 native → single → world 重提交：
            // 四图快路必须复查 outline → 有界重解重绑（或诚实失败，绝不 reveal 混合 pair）
            Fixture d = BuildWorld(withExtension: true, steedCount: 16, types: types);
            Tick(d, 2);
            Image outline = FindOutline(d.ExtensionDetail).GetComponent<Image>();
            outline.sprite = d.DetailNativeSprite;
            d.Greek._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingSingleIsland;
            Tick(d, 1);
            d.Greek._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingWorld;
            Tick(d, 2);
            bool outlineRebound = ReferenceEquals(outline.sprite, MapExtensionIslandArt.Outline);
            Check(outlineRebound, "S10/P3-detail-outline-rebound applied=" + ReadStatic("_overviewApplied"));
            Check(!outlineRebound || (bool)ReadStatic("_overviewApplied"), "S10/P3-no-mixed-pair-reveal");
            Check(ReferenceEquals(d.DetailLandImage.sprite, MapExtensionIslandArt.Shore),
                "S10/P3-detail-terrain-still-ours");
            d.Greek.OnDisable();
        }

        /// <summary>
        /// S12（supervisor 跟进项）：DeferRelease 队列饱和的 fail-closed —— 队列接不下新的归还责任时，
        /// **不得丢 outstanding owner 身份、不得继续为新 owner 接管**；setter 恢复后 exact 回调收尾；
        /// A 清理不碰 B；全有界。全程真实 caller（BuildWorld commit + OnDisable + stub Image.ThrowOnSet 注入），
        /// 不用 global Reset 当解法（Reset 只在 BuildWorld 的既有隔离里出现，且此处以 _preserveState 抑制）。
        /// </summary>
        private static void ScenarioS12ReleaseQueueSaturation()
        {
            Console.WriteLine("== S12 release queue saturation (fail-closed) ==");
            int[] types = { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };
            var gens = new List<Fixture>();
            var stuck = new List<Image>();
            var natives = new List<Sprite>();
            var menus = new List<MapTimelineMenuGreece>();
            int queuedStart = DeferredReleaseCount();
            int maxQueued = queuedStart;
            bool retained = false;
            int refusedGen = -1;
            _preserveState = true;
            try
            {
                // 阶段 1：真实 caller 逐代 commit → setter 失败 → OnDisable；直到队列拒绝新责任（有界 24 代）
                for (int i = 0; i < 24; i++)
                {
                    Fixture g = BuildWorld(withExtension: true, steedCount: 16, types: types);
                    Image pre = ArtImage(g.Extension);
                    Sprite nativeBefore = pre.sprite;          // commit 前的 native（恢复目标）
                    Tick(g, 2);
                    Image img = ArtImage(g.Extension);
                    bool bound = ReferenceEquals(img.sprite, MapExtensionIslandArt.Shore);
                    if (!bound)
                    {
                        // 饱和后新 owner 的第一代：接管必须被拒（native 保持）
                        refusedGen = i;
                        gens.Add(g);
                        Check(MapExtensionIslandArt.LeaseCount <= 64, "S12/bounded-leases");
                        break;
                    }
                    gens.Add(g);
                    natives.Add(nativeBefore);
                    img.ThrowOnSet = true;
                    g.Greek.OnDisable();
                    menus.Add(g.Greek);
                    stuck.Add(img);
                    int queueNow = DeferredReleaseCount();
                    if (queueNow > maxQueued) maxQueued = queueNow;
                    bool identityRetained = ReadStatic("_shoreOwnerToken") != null;
                    if (identityRetained)
                    {
                        // 队列已满：本次归还责任被拒 → 身份保留（fail-closed），不得静默丢弃
                        retained = true;
                        Check(MapExtensionIslandArt.LeaseCount > 0, "S12/retained-has-outstanding");
                        Check(queueNow <= maxQueued, "S12/queue-did-not-grow-on-refusal");
                        break;
                    }
                }

                Check(retained, "S12/saturation-reached queue=" + DeferredReleaseCount());
                // 核心不变量（fail-closed）：绝不允许“有未归还租约但已无 cleanup 身份”（孤儿责任）
                Check(ReadStatic("_shoreOwnerToken") != null || MapExtensionIslandArt.LeaseCount == 0,
                    "S12/no-orphan-lease-without-cleanup-identity leases=" + MapExtensionIslandArt.LeaseCount +
                    " token=" + (ReadStatic("_shoreOwnerToken") != null));
                if (!retained || stuck.Count == 0 || menus.Count == 0)
                {
                    Check(false, "S12/cannot-continue (saturation not reached; queue=" + DeferredReleaseCount() + ")");
                    return;
                }
                Check(maxQueued >= 2 && maxQueued <= 8, "S12/queue-bounded max=" + maxQueued);
                Check(ReadStatic("_shoreOwnerToken") != null && ReadStatic("_shoreOwnerMenu") != null,
                    "S12/identity-retained token=" + (ReadStatic("_shoreOwnerToken") != null));
                int leasesAtRetain = MapExtensionIslandArt.LeaseCount;
                int queuedAtRetain = DeferredReleaseCount();

                // 阶段 2：新 owner 接管被拒（无新租约、native 保持、队列不增长）
                if (refusedGen >= 0)
                {
                    Fixture n = gens[refusedGen];
                    Check(!ReferenceEquals(ArtImage(n.Extension).sprite, MapExtensionIslandArt.Shore),
                        "S12/new-owner-bind-refused");
                    Check(!(bool)ReadStatic("_overviewApplied") || ArtImage(n.Extension) != null,
                        "S12/refused-keeps-native-art");
                    Check(MapExtensionIslandArt.LeaseCount == leasesAtRetain,
                        "S12/refused-no-new-lease leases=" + MapExtensionIslandArt.LeaseCount);
                    Check(DeferredReleaseCount() == queuedAtRetain, "S12/refused-queue-unchanged");
                    Check(ReferenceEquals(stuck[0].sprite, MapExtensionIslandArt.Shore),
                        "S12/earlier-stuck-untouched-during-refusal");
                }

                // 阶段 3：setter 恢复 → 保留身份的 exact 回调收尾（只收它自己；A 清理不碰 B）
                Image retainedImg = stuck[stuck.Count - 1];
                Sprite retainedNative = natives[natives.Count - 1];
                MapTimelineMenuGreece retainedMenu = menus[menus.Count - 1];
                retainedImg.ThrowOnSet = false;
                retainedMenu.OnDisable();
                Check(ReadStatic("_shoreOwnerToken") == null, "S12/exact-callback-clears-identity");
                Check(ReferenceEquals(retainedImg.sprite, retainedNative),
                    "S12/exact-callback-restores-native");
                Check(MapExtensionIslandArt.LeaseCount == leasesAtRetain - 1,
                    "S12/exact-callback-released-one leases=" + MapExtensionIslandArt.LeaseCount);
                Check(DeferredReleaseCount() == queuedAtRetain, "S12/exact-callback-keeps-other-entries");
                Check(ReferenceEquals(stuck[0].sprite, MapExtensionIslandArt.Shore),
                    "S12/A-cleanup-does-not-touch-B");

                // 阶段 4：门解除 → 新 owner 可以重新接管
                Fixture after = BuildWorld(withExtension: true, steedCount: 16, types: types);
                Tick(after, 2);
                Check(ReferenceEquals(ArtImage(after.Extension).sprite, MapExtensionIslandArt.Shore),
                    "S12/gate-lifted-after-release");
                after.Greek.OnDisable();
                gens.Add(after);

                // 阶段 5：收尾全部历史 stuck 责任（有界）；队列与租约归零
                for (int i = 0; i < stuck.Count; i++)
                {
                    if (ReferenceEquals(stuck[i], retainedImg)) continue;
                    stuck[i].ThrowOnSet = false;
                    menus[i].OnDisable();
                }
                for (int i = 0; i < gens.Count; i++) gens[i].Greek.OnDisable();
                Check(DeferredReleaseCount() == 0, "S12/drain-all queue=" + DeferredReleaseCount());
                Check(MapExtensionIslandArt.LeaseCount == 0,
                    "S12/leases-zero leases=" + MapExtensionIslandArt.LeaseCount);
                Check(maxQueued <= 8, "S12/bounded-max=" + maxQueued);
            }
            finally
            {
                _preserveState = false;
                MapExtensionIslandArt.Reset();
                ProbeHooks.RunMenuDisablePostfix(null);
            }
        }

        /// <summary>
        /// S13（incident #152 回归，岛内契约）：native 岛的详情/总览资源图标必须落在**所属岛自己的底图**内，
        /// 详情优先原生动态槽原始位置（位置/尺度保留，extras 不侵入槽位）；底图是 surface 而不是 blocker
        /// （船标等真实原生图形继续避障）；岛内容量不足 → 整体 fallback（无 holder + 原生槽精确还原），
        /// 绝不岛外兜底。未解锁岛簇仍不揭露（原生 gate 回归）。
        /// 对 baseline 红：旧实现详情把整个 land+margin 当 surface、把 Land Image 当 blocker，总览用岛外 RegionRects。
        /// </summary>
        private static void ScenarioS13IslandSurfaceContract()
        {
            Console.WriteLine("== S13 island-surface contract (#152) ==");
            int[] types = { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };
            Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: types);
            // land0：3 个真实条目（1 个匹配 Steed1 原生槽 + 2 个 extras；detail 模板只有 Steed1/Hermit/Statue）
            f.Current.landData[0].steedSpawns = Steeds(21, 22, 38);
            Tick(f, 2);
            UpdateDetails(f);

            // ---- (a) detail：首槽位置/尺度保留 + extras 只在底图内 + 船标避障 ----
            UILand land0 = f.Greek.lands[0];
            RectTransform rect0 = land0.gameObject.GetComponent<RectTransform>();
            List<RectTransform> icons = CollectIcons(rect0, 3);
            if (icons.Count != 3) DumpLogTail(20);
            Check(icons.Count == 3, "S13/detail-icons got=" + icons.Count);
            if (icons.Count == 3)
            {
                RectTransform holderRt = CollectHolder(rect0, 3);
                MapIconBox holderBox = holderRt != null ? LandLocalBox(rect0, holderRt) : default;
                RectTransform landImage = FindChild(rect0, "Land Image");
                MapIconBox artBox = landImage != null ? LandLocalBox(rect0, landImage) : default;
                MapIconBox slotBox = DetailSlotBox(rect0, "Steed1");
                MapIconBox boatBox = f.DetailBoat != null ? LandLocalBox(rect0, f.DetailBoat) : default;
                var seen = new List<int>(3);
                bool allInsideArt = true, allClearBoat = true, slotCentered = false, extrasClearSlot = true;
                foreach (RectTransform icon in icons)
                {
                    MapIconBox raw = LandLocalBox(rect0, icon);
                    MapIconBox box = new MapIconBox(raw.X0 - holderBox.X0, raw.Y0 - holderBox.Y0,
                        raw.X1 - holderBox.X0, raw.Y1 - holderBox.Y0);
                    int type = ParseIconType(icon.gameObject.name);
                    seen.Add(type);
                    if (!Inside(box, artBox, 0.05f)) allInsideArt = false;
                    if (box.Intersects(boatBox, 0.01f)) allClearBoat = false;
                    if (type == 21)
                    {
                        // 首槽：条目的原生尺寸（25×16）等比居中放在 Steed1 槽位盒里
                        slotCentered =
                            Math.Abs((box.X0 + box.X1) * 0.5f - (slotBox.X0 + slotBox.X1) * 0.5f) < 0.05f &&
                            Math.Abs((box.Y0 + box.Y1) * 0.5f - (slotBox.Y0 + slotBox.Y1) * 0.5f) < 0.05f &&
                            Math.Abs(box.Width - 25f) < 0.05f && Math.Abs(box.Height - 16f) < 0.05f &&
                            Inside(box, slotBox, 0.05f);
                    }
                    else if (box.Intersects(slotBox, 0.01f))
                    {
                        extrasClearSlot = false;   // extras 不侵入原生槽位原始占地
                    }
                }
                seen.Sort();
                Check(seen.Count == 3 && seen[0] == 21 && seen[1] == 22 && seen[2] == 38,
                    "S13/detail-source-requestindex types=" + string.Join(",", seen));
                Check(allInsideArt, "S13/detail-icons-inside-art art=" + Fmt(artBox));
                // exact native sprite mesh：新增条目（extras）必须完整在岛形内（v2 review P1-1）
                ProbeMeshMap detailMap = ProbeMeshMapOf(landImage, rect0, out MapIconBox detailDrawn);
                bool detailShapeOk = true;
                foreach (RectTransform icon in icons)
                {
                    MapIconBox raw = LandLocalBox(rect0, icon);
                    var box = new MapIconBox(raw.X0 - holderBox.X0, raw.Y0 - holderBox.Y0,
                        raw.X1 - holderBox.X0, raw.Y1 - holderBox.Y0);
                    if (!MeshBoxInside(detailMap, AthenaMeshX, AthenaMeshY, AthenaMeshIndices, box))
                    {
                        detailShapeOk = false;
                    }
                }
                Check(detailShapeOk, "S13/detail-icons-inside-island-shape drawn=" + Fmt(detailDrawn));
                Check(slotCentered, "S13/detail-first-slot-centered slot=" + Fmt(slotBox));
                Check(extrasClearSlot, "S13/detail-slot-footprint-reserved");
                Check(allClearBoat, "S13/detail-boat-blocker-kept boat=" + Fmt(boatBox));
            }

            // ---- (b) overview：图标只落在**自己岛**的底图内（1:1 归属）；扩展岛仍 16 项/顶面内 ----
            var artBoxes = new List<MapIconBox>(11);
            for (int i = 0; i < 10; i++)
            {
                RectTransform art = ArtOf(f.Lands[i]) as RectTransform;
                artBoxes.Add(art != null ? PaperArtBox(f.Paper, art) : default);
            }
            RectTransform extensionArt = ArtOf(f.Extension) as RectTransform;
            artBoxes.Add(extensionArt != null ? PaperArtBox(f.Paper, extensionArt) : default);

            var perCluster = new int[11];
            int foreign = 0, placedTotal = 0;
            var extensionTypes = new List<int>(16);
            RectTransform[] holders = CollectHolders(f.Paper);
            for (int h = 0; h < holders.Length; h++)
            {
                RectTransform holder = holders[h];
                for (int c = 0; c < holder.childCount; c++)
                {
                    RectTransform icon = holder.GetChild(c) as RectTransform;
                    if (icon == null) continue;
                    placedTotal++;
                    MapIconBox box = PaperArtBox(f.Paper, icon);
                    int owner = -1;
                    for (int i = 0; i < artBoxes.Count; i++)
                    {
                        if (!Inside(box, artBoxes[i], 0.05f)) continue;
                        owner = i;
                        break;
                    }
                    if (owner < 0) { foreign++; continue; }
                    perCluster[owner]++;
                    if (owner == 10) extensionTypes.Add(ParseIconType(icon.gameObject.name));
                }
            }
            Check(foreign == 0, "S13/overview-icons-outside-own-art foreign=" + foreign);

            // 归属判定之外的更强约束：每个 overview 图标必须完整落在**自己岛**的实际绘制形状（sprite mesh）内，
            // 而不只是 art rect。原生簇没有资源槽 ⇒ 全部条目都由 planner 放进岛形。
            var clusterMaps = new ProbeMeshMap[10];
            for (int i = 0; i < 10; i++)
            {
                RectTransform art = ArtOf(f.Lands[i]) as RectTransform;
                clusterMaps[i] = art != null ? ProbeMeshMapOf(art, f.Paper, out _) : default;
            }
            int meshOutside = 0, meshChecked = 0;
            for (int h = 0; h < holders.Length; h++)
            {
                RectTransform holder = holders[h];
                for (int c = 0; c < holder.childCount; c++)
                {
                    RectTransform icon = holder.GetChild(c) as RectTransform;
                    if (icon == null) continue;
                    MapIconBox box = PaperArtBox(f.Paper, icon);
                    int owner = -1;
                    for (int i = 0; i < artBoxes.Count; i++)
                    {
                        if (!Inside(box, artBoxes[i], 0.05f)) continue;
                        owner = i;
                        break;
                    }
                    if (owner < 0 || owner >= 10 || !clusterMaps[owner].Valid) continue;
                    meshChecked++;
                    if (!MeshBoxInside(clusterMaps[owner], AthenaMeshX, AthenaMeshY, AthenaMeshIndices, box))
                    {
                        meshOutside++;
                    }
                }
            }
            Check(meshOutside == 0 && meshChecked >= 9,
                "S13/overview-icons-inside-own-island-shape outside=" + meshOutside + " checked=" + meshChecked);
            bool nativesOwned = true;
            for (int i = 1; i < 10; i++) if (perCluster[i] != 1) nativesOwned = false;
            Check(nativesOwned, "S13/overview-native-ownership per=" + string.Join(",", perCluster));
            extensionTypes.Sort();
            int[] expectedTypes = (int[])types.Clone();
            Array.Sort(expectedTypes);
            Check(extensionTypes.Count == 16 && string.Join(",", extensionTypes) == string.Join(",", expectedTypes),
                "S13/overview-extension-source-requestindex types=" + string.Join(",", extensionTypes));
            Check(placedTotal >= 9 + 16, "S13/overview-icons-present placed=" + placedTotal);

            // ---- (c) 原生 gate 回归：未解锁岛簇不建/即毁自有图标（不提前揭露） ----
            UIMainMapLand locked = f.Lands[1].gameObject.GetComponent<UIMainMapLand>();
            locked.IsUnlocked = false;
            f.Map.UpdateLandIcons(f.Current);
            int lockedIcons = 0;
            holders = CollectHolders(f.Paper);
            for (int h = 0; h < holders.Length; h++)
            {
                for (int c = 0; c < holders[h].childCount; c++)
                {
                    RectTransform icon = holders[h].GetChild(c) as RectTransform;
                    if (icon != null && Inside(PaperArtBox(f.Paper, icon), artBoxes[1], 0.05f)) lockedIcons++;
                }
            }
            Check(lockedIcons == 0, "S13/locked-cluster-not-revealed icons=" + lockedIcons);
            locked.IsUnlocked = true;
            f.Greek.OnDisable();

            // ---- (d) 岛内容量不足：整体 fallback（无 holder、无部分图标、原生槽精确还原），绝不岛外兜底 ----
            Fixture g = BuildWorld(withExtension: true, steedCount: 16, types: types);
            g.Current.landData[0].steedSpawns = Steeds(21, 22, 38);
            Tick(g, 2);
            RectTransform hostile = g.Greek.lands[0].gameObject.GetComponent<RectTransform>();
            // 只盖住*底图*（land 左下 (20,25,144,118)）：旧实现会把图标排到岛外空白（holder 存在），
            // 新实现 extras 必须整体 fail → 无 holder + 原生槽还原。
            RectTransform cover = MakeRect("Art Blocker", hostile, new Vector2(124f, 93f), Vector2.zero,
                new Vector2(0.5f, 0.5f));
            FixedAnchor(cover, hostile, new Vector2(0.5f, 0.5f), new Vector2(-8f, -3.5f));
            cover.gameObject.AddComponent<Image>();
            UpdateDetails(g);
            Check(CountNamed(hostile.gameObject, "KEM_MapResourceIcons") == 0,
                "S13/capacity-fail-no-partial holders=" + CountNamed(hostile.gameObject, "KEM_MapResourceIcons"));
            bool restored = true;
            string[] slotNames = { "Steed1", "Hermit", "Statue" };
            for (int i = 0; i < slotNames.Length; i++)
            {
                RectTransform slot = FindChild(hostile, slotNames[i]);
                RectTransform spawned = slot != null ? FindChild(slot, "Spawned " + slotNames[i]) : null;
                if (spawned == null || !spawned.gameObject.activeSelf) restored = false;
            }
            Check(restored, "S13/capacity-fail-native-slots-restored");
            g.Greek.OnDisable();
        }

        /// <summary>facade：真实 Il2CppStructArray&lt;SteedType&gt;（条目数组覆盖用）。</summary>
        /// <summary>
        /// S14（review v2 回归：native 岛形约束 + 自然 paper 倍率）——
        /// (a) 岛形之外的海角必须被拒：只覆盖 art bbox 的旧规划在"唯一剩余空间在形外"的配置下仍会成功；
        /// (b) overview：原 prefab 尺寸请求必须按实测自然 paper 倍率（art 自身 localScale × 全局 fit）换算，
        ///     4 个真实资源 4/4 落在岛形内，rendered bbox 与 planner 同倍率（implied rung ∈ 阶梯且各图标一致）；
        /// (c) 未知几何（空 mesh）→ 诚实整体保留 native（无 holder、无矩形兜底）；
        /// (d) artist-authored 槽位不因矩形剪影触海被挪动或拒绝。
        /// 断言只用既有入口与观察量（+ test 侧独立几何复核），同一测试可对 frozen v1 源码红、对本候选绿。
        /// </summary>
        private static void ScenarioS14NativeShapeAndPaperScale()
        {
            Console.WriteLine("== S14 native shape + natural paper scale ==");
            int[] sixteen = { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };

            // ---- (a) 形内被占满、只余形外海角 → 必须整体失败（不落海） ----
            NativeIslandSpriteFactory = MakeHalfIslandSprite;
            Fixture a = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            NativeIslandSpriteFactory = () => MakeNativeIslandSprite("athena_tholos_greece");
            a.Current.landData[0].steedSpawns = Steeds(37, 33);   // 1 槽 + 1 extra
            Tick(a, 2);
            RectTransform aLand = a.Greek.lands[0].gameObject.GetComponent<RectTransform>();
            RectTransform aArt = FindChild(aLand, "Land Image");
            MapIconBox aArtBox = LandLocalBox(aLand, aArt);
            // 岛形 = half 变体按 uGUI 公式的真实绘制盒；用覆盖该盒的 blocker 把形内占满
            // ⇒ 只剩 bbox 海角（旧实现只按 bbox 规划会落海）。
            ProbeMeshMap coverMap = ProbeMeshMapOf(aArt, aLand, out MapIconBox aDrawn);
            RectTransform halfCover = MakeRect("Mesh Cover", aLand,
                new Vector2(aDrawn.Width + 4f, aDrawn.Height + 4f), Vector2.zero, new Vector2(0.5f, 0.5f));
            FixedAnchor(halfCover, aLand, new Vector2(0.5f, 0.5f),
                new Vector2((aDrawn.X0 + aDrawn.X1) * 0.5f - aLand.rect.width * 0.5f,
                            (aDrawn.Y0 + aDrawn.Y1) * 0.5f - aLand.rect.height * 0.5f));
            halfCover.gameObject.AddComponent<Image>();
            UpdateDetails(a);
            if (CountNamed(aLand.gameObject, "KEM_MapResourceIcons") != 0) DumpLogTail(12);
            Check(CountNamed(aLand.gameObject, "KEM_MapResourceIcons") == 0,
                "S14/shape-outside-not-used holders=" + CountNamed(aLand.gameObject, "KEM_MapResourceIcons"));
            Check(NativeSlotsRestored(aLand), "S14/shape-fail-native-restored");
            a.Greek.OnDisable();

            // ---- (b) overview 自然 paper 倍率：4 个真实资源 4/4 落在岛形内且只缩放一次 ----
            Fixture b = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            b.Current.landData[0].steedSpawns = Steeds(37, 33);
            b.Current.landData[0].hermit = Hermits(0);
            b.Current.landData[0].statue = Statues(2);
            Tick(b, 2);
            List<RectTransform> icons = CollectIcons(b.Paper, 4);
            Check(icons.Count == 4, "S14/overview-4-icons got=" + icons.Count);
            RectTransform bArt = ArtOf(b.Lands[0]) as RectTransform;
            ProbeMeshMap map = ProbeMeshMapOf(bArt, b.Paper, out MapIconBox drawn);
            Check(map.Valid && drawn.Width > 2f, "S14/overview-drawn-box " + Fmt(drawn));
            float naturalPaperScale = bArt.sizeDelta.x > 0.01f ? drawn.Width / bArt.sizeDelta.x : 0f;
            Check(naturalPaperScale > 0.05f && naturalPaperScale < 0.95f,
                "S14/overview-natural-paper-scale=" + naturalPaperScale.ToString("0.####") +
                " drawn=" + Fmt(drawn) + " sizeDelta=" + bArt.sizeDelta);
            bool allInsideShape = true, allInsideDrawn = true, rungConsistent = icons.Count > 0;
            float impliedRung = -1f;
            for (int i = 0; i < icons.Count; i++)
            {
                MapIconBox box = PaperArtBox(b.Paper, icons[i]);
                if (!Inside(box, drawn, 0.5f)) allInsideDrawn = false;
                if (!MeshBoxInside(map, AthenaMeshX, AthenaMeshY, AthenaMeshIndices, box)) allInsideShape = false;
                if (!TryNativeIconSize(icons[i].gameObject.name, out float nativeW, out float nativeH) || nativeW <= 0f)
                {
                    rungConsistent = false;
                    continue;
                }
                float rung = box.Width / (nativeW * naturalPaperScale);
                if (impliedRung < 0f) impliedRung = rung;
                else if (Math.Abs(rung - impliedRung) > 0.02f) rungConsistent = false;
                if (box.Height > nativeH * naturalPaperScale * (impliedRung + 0.03f) + 0.05f) rungConsistent = false;
            }
            Check(allInsideShape, "S14/overview-icons-inside-island-shape");
            if (!allInsideDrawn) DumpLogTail(8);
            Check(allInsideDrawn, "S14/overview-icons-inside-drawn-box drawn=" + Fmt(drawn));
            Check(rungConsistent && IsLadderRung(impliedRung) && impliedRung >= 0.36f,
                "S14/overview-single-scale-factor rung=" + impliedRung.ToString("0.###") +
                " paperScale=" + naturalPaperScale.ToString("0.####"));
            b.Greek.OnDisable();

            // ---- (c) 未知几何：空 mesh → 无自有图标 + 原生槽还原（绝不矩形兜底） ----
            NativeIslandSpriteFactory = MakeMeshesUnavailableSprite;
            Fixture c = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            NativeIslandSpriteFactory = () => MakeNativeIslandSprite("athena_tholos_greece");
            Tick(c, 2);
            UpdateDetails(c);
            RectTransform cLand = c.Greek.lands[0].gameObject.GetComponent<RectTransform>();
            Check(CountNamed(cLand.gameObject, "KEM_MapResourceIcons") == 0, "S14/unknown-mesh-no-icons");
            Check(NativeSlotsRestored(cLand), "S14/unknown-mesh-native-restored");
            MapIconBox cArtBox = PaperArtBox(c.Paper, ArtOf(c.Lands[0]) as RectTransform);
            RectTransform[] cHolders = CollectHolders(c.Paper);
            int cOverview = 0;
            for (int h = 0; h < cHolders.Length; h++)
            {
                for (int ch = 0; ch < cHolders[h].childCount; ch++)
                {
                    RectTransform icon = cHolders[h].GetChild(ch) as RectTransform;
                    if (icon == null) continue;
                    if (PaperArtBox(c.Paper, icon).Intersects(cArtBox, 0.5f)) cOverview++;
                }
            }
            Check(cOverview == 0, "S14/unknown-mesh-overview-absent icons=" + cOverview);
            c.Greek.OnDisable();

            // ---- (c2) 翻转/镜像 art：轴对齐映射无法表达 ⇒ 同样诚实整体保留 native ----
            Fixture c2 = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            RectTransform c2Art = FindChild(c2.Greek.lands[0].gameObject.GetComponent<RectTransform>(), "Land Image");
            c2Art.localScale = new Vector3(-1f, 1f, 1f);   // 镜像（真实数据不出现；必须失败关闭而非镜像错位）
            Tick(c2, 2);
            UpdateDetails(c2);
            Check(CountNamed(c2.Greek.lands[0].gameObject, "KEM_MapResourceIcons") == 0,
                "S14/flipped-art-no-icons");
            Check(NativeSlotsRestored(c2.Greek.lands[0].gameObject.GetComponent<RectTransform>()),
                "S14/flipped-art-native-restored");
            c2.Greek.OnDisable();

            // ---- (d) authored 槽位保留（槽位剪影在 diamond 的 bbox 海角上也不挪） ----
            NativeIslandSpriteFactory = MakeDiamondIslandSprite;
            Fixture d = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            NativeIslandSpriteFactory = () => MakeNativeIslandSprite("athena_tholos_greece");
            d.Current.landData[0].steedSpawns = Steeds(37);
            Tick(d, 2);
            RectTransform dLand = d.Greek.lands[0].gameObject.GetComponent<RectTransform>();
            RectTransform dSlot = FindChild(dLand, "Steed1");
            FixedAnchor(dSlot, dLand, new Vector2(0.5f, 0.5f),
                new Vector2(-58f, 42f));   // art bbox 左上角（diamond 之外）
            MapIconBox slotBox = LandLocalBox(dLand, dSlot);
            UpdateDetails(d);
            List<RectTransform> dIcons = CollectIcons(dLand, 1);
            Check(dIcons.Count == 1, "S14/authored-slot-kept got=" + dIcons.Count);
            if (dIcons.Count == 1)
            {
                RectTransform holderRt = CollectHolder(dLand, 1);
                MapIconBox holderBox = holderRt != null ? LandLocalBox(dLand, holderRt) : default;
                MapIconBox raw = LandLocalBox(dLand, dIcons[0]);
                var actual = new MapIconBox(raw.X0 - holderBox.X0, raw.Y0 - holderBox.Y0,
                    raw.X1 - holderBox.X0, raw.Y1 - holderBox.Y0);
                Check(Math.Abs((actual.X0 + actual.X1) * 0.5f - (slotBox.X0 + slotBox.X1) * 0.5f) < 0.05f &&
                      Math.Abs((actual.Y0 + actual.Y1) * 0.5f - (slotBox.Y0 + slotBox.Y1) * 0.5f) < 0.05f,
                    "S14/authored-slot-not-moved actual=" + Fmt(actual) + " slot=" + Fmt(slotBox));
            }
            d.Greek.OnDisable();

            // ---- (e) 未访问 extension（landData[11] 为空）：资源保持空白，不提前揭露 ----
            Fixture e = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            e.Current.landData[11].steedSpawns = new Il2CppStructArray<SteedType>(0);   // 未访问 = 无条目
            Tick(e, 2);
            UpdateDetails(e);
            RectTransform eDetail = e.ExtensionDetail.gameObject.GetComponent<RectTransform>();
            Check(CountNamed(eDetail.gameObject, "KEM_MapResourceIcons") == 0,
                "S14/unvisited-extension-detail-blank holders=" +
                CountNamed(eDetail.gameObject, "KEM_MapResourceIcons"));
            MapIconBox eBanner = PaperArtBox(e.Paper, ArtOf(e.Extension) as RectTransform);
            int eIcons = 0;
            RectTransform[] eHolders = CollectHolders(e.Paper);
            for (int h = 0; h < eHolders.Length; h++)
            {
                for (int ch = 0; ch < eHolders[h].childCount; ch++)
                {
                    RectTransform icon = eHolders[h].GetChild(ch) as RectTransform;
                    if (icon == null) continue;
                    if (PaperArtBox(e.Paper, icon).Intersects(eBanner, 0.5f)) eIcons++;
                }
            }
            Check(eIcons == 0, "S14/unvisited-extension-overview-blank icons=" + eIcons);
            e.Greek.OnDisable();
        }

        /// <summary>
        /// issue-152 v3 reviewer 要求：**绘制顶点与 mask 顶点一致，并核验真实变换链**。
        /// - (a) 生产 `NativeArtShape`（反射读出的规划空间顶点）与 test 侧独立实现的
        ///   uGUI `Image` Simple+useSpriteMesh 公式逐顶点一致（含真实 art→paper 变换）；
        /// - (b) `Sprite.bounds != rect/ppu` 反例：两边都按 bounds 归一，且与旧 v2（ppu 归一）位置显著不同；
        /// - (c) pivot(0,0) + preserveAspect 变化：缩框按 pivot 锚定（非居中），旧 v2 居中结果不同；
        /// - (d) overrideSprite：uGUI `activeSprite = overrideSprite ?? sprite`，几何取 override 的 mesh；
        /// - (e) 不支持模式（useSpriteMesh=false / type!=Simple）：读取 fail-closed + 运行时整体保留 native。
        /// </summary>
        private static void ScenarioS15RendererParity()
        {
            Console.WriteLine("== S15 renderer parity (uGUI Simple+useSpriteMesh) ==");
            int[] sixteen = { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };

            // ---- (a) detail art：生产顶点 == 独立公式顶点 ----
            Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Tick(f, 2);
            RectTransform land = f.Greek.lands[0].gameObject.GetComponent<RectTransform>();
            UILand landComp = f.Greek.lands[0];
            RectTransform art = FindChild(land, "Land Image");
            Check(TryReadProductionShape(land, landComp, out MapIconBox prodBox, out float[] prodX, out float[] prodY,
                    out string prodReason), "S15/detail-shape-read " + prodReason);
            ProbeMeshMap detailMap = ProbeMeshMapOf(art, land, out MapIconBox detailDrawn);
            Check(detailMap.Valid, "S15/detail-map-valid");
            float detailErr = 0f;
            int detailMatched = 0;
            if (prodX != null && prodX.Length == AthenaMeshX.Length && detailMap.Valid)
            {
                for (int i = 0; i < AthenaMeshX.Length; i++)
                {
                    float ex = detailMap.PlanXFromUnitX(AthenaMeshX[i]);
                    float ey = detailMap.PlanYFromUnitY(AthenaMeshY[i]);
                    float dx = Math.Abs(ex - prodX[i]), dy = Math.Abs(ey - prodY[i]);
                    detailErr = Math.Max(detailErr, Math.Max(dx, dy));
                    if (dx < 1e-3f && dy < 1e-3f) detailMatched++;
                }
            }
            Check(detailMatched == AthenaMeshX.Length && detailErr < 1e-3f,
                "S15/detail-vertex-parity matched=" + detailMatched + "/" + AthenaMeshX.Length +
                " err=" + detailErr.ToString("0.####"));
            Check(Math.Abs(prodBox.X0 - detailDrawn.X0) < 1e-3f && Math.Abs(prodBox.Y0 - detailDrawn.Y0) < 1e-3f &&
                  Math.Abs(prodBox.X1 - detailDrawn.X1) < 1e-3f && Math.Abs(prodBox.Y1 - detailDrawn.Y1) < 1e-3f,
                "S15/detail-box-parity prod=" + Fmt(prodBox) + " test=" + Fmt(detailDrawn));
            // 规划空间顶点必须与 mesh bbox 自洽（PlanBBox = 变换后顶点包围盒）
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; prodX != null && i < prodX.Length; i++)
            {
                minX = Math.Min(minX, prodX[i]); maxX = Math.Max(maxX, prodX[i]);
                minY = Math.Min(minY, prodY[i]); maxY = Math.Max(maxY, prodY[i]);
            }
            Check(prodX != null && Math.Abs(minX - prodBox.X0) < 1e-3f && Math.Abs(maxX - prodBox.X1) < 1e-3f &&
                  Math.Abs(minY - prodBox.Y0) < 1e-3f && Math.Abs(maxY - prodBox.Y1) < 1e-3f,
                "S15/detail-box-is-vertex-bbox");
            f.Greek.OnDisable();

            // ---- (b) bounds != rect/ppu：strong 反例（bounds = mesh bbox 的 0.5 倍）----
            NativeIslandSpriteFactory = MakeHalfBoundsIslandSprite;
            Fixture g = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Tick(g, 2);
            RectTransform gLand = g.Greek.lands[0].gameObject.GetComponent<RectTransform>();
            RectTransform gArt = FindChild(gLand, "Land Image");
            Check(TryReadProductionShape(gLand, g.Greek.lands[0], out MapIconBox gBox, out float[] gX, out _,
                    out string gReason), "S15/bounds-shape-read " + gReason);
            ProbeMeshMap gMap = ProbeMeshMapOf(gArt, gLand, out _);
            float boundsErr = 0f;
            int boundsMatched = 0;
            for (int i = 0; gX != null && i < AthenaMeshX.Length; i++)
            {
                float ex = gMap.PlanXFromUnitX(AthenaMeshX[i]);
                boundsErr = Math.Max(boundsErr, Math.Abs(ex - gX[i]));
                if (Math.Abs(ex - gX[i]) < 1e-3f) boundsMatched++;
            }
            Check(boundsMatched == AthenaMeshX.Length && boundsErr < 1e-3f,
                "S15/bounds-vertex-parity matched=" + boundsMatched + " err=" + boundsErr.ToString("0.####"));
            // v2 公式（v*ppu + pivot，按 rect 归一到 art rect）会给出不同位置 ⇒ 该反例对旧实现为红
            float legacyX = gMap.PlanXFromLocal(AthenaMeshX[0] * 32f + 42f) * 0f +
                            (gMap.OriginX + (AthenaMeshX[0] * 32f + 42f) / 84f * gMap.ArtW * gMap.ScaleX);
            Check(gX != null && Math.Abs(legacyX - gX[0]) > 1f,
                "S15/bounds-legacy-differs legacy=" + legacyX.ToString("0.####") + " canonical=" +
                (gX == null ? 0f : gX[0]).ToString("0.####"));
            g.Greek.OnDisable();
            NativeIslandSpriteFactory = () => MakeNativeIslandSprite("athena_tholos_greece");

            // ---- (c) pivot(0,0) + preserveAspect：缩框按 pivot 锚定（非居中）----
            // detail art 114×82 vs sprite rect 84×62：spriteRatio(1.3548) < rectRatio(1.3902) ⇒ 宽缩、右侧收
            // （pivot.x=0 ⇒ 左侧不动）。旧 v2 FitAspect 居中 ⇒ 两侧各收一半（红）。
            Check(MapIconNativeArtPlan.TryBuildSimpleMeshDraw(114f, 82f, 0f, 0f, true, 84f, 62f, 42f, 0f,
                    2.59375f, 1.71875f, out MapIconImageDraw detailDraw), "S15/pivot-aspect-build");
            Check(Near(detailDraw.DrawingW, 82f * (84f / 62f), 0.01f) && Near(detailDraw.DrawingH, 82f, 0.01f),
                "S15/pivot-aspect-size " + detailDraw.DrawingW.ToString("0.###") + "x" +
                detailDraw.DrawingH.ToString("0.###"));
            Check(Near(detailDraw.OffsetX, (0f - 42f / 84f) * detailDraw.DrawingW, 0.01f),
                "S15/pivot-aspect-offset " + detailDraw.OffsetX.ToString("0.###"));
            float centeredX0 = 25f + (114f - detailDraw.DrawingW) * 0.5f;   // v2 居中口径
            float anchoredX0 = 25f;                                          // 规范：pivot.x=0 ⇒ 左侧不动
            Check(Math.Abs(anchoredX0 - centeredX0) > 1f,
                "S15/pivot-aspect-legacy-differs anchored=" + anchoredX0 + " centered=" + centeredX0);

            // ---- (c2) preserveAspect=false：生产必须用**读到的** flag（而不是硬编码 true）----
            FixturePreserveAspectOverride = false;
            Fixture j = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Tick(j, 2);
            RectTransform jLand = j.Greek.lands[0].gameObject.GetComponent<RectTransform>();
            RectTransform jArt = FindChild(jLand, "Land Image");
            Check(TryReadProductionShape(jLand, j.Greek.lands[0], out MapIconBox jBox, out float[] jX,
                    out float[] jY, out string jReason), "S15/no-aspect-shape-read " + jReason);
            ProbeMeshMap jMap = ProbeMeshMapOf(jArt, jLand, out MapIconBox jDrawn);
            float jErr = 0f;
            int jMatched = 0;
            for (int i = 0; jX != null && i < AthenaMeshX.Length; i++)
            {
                float ex = jMap.PlanXFromUnitX(AthenaMeshX[i]);
                float ey = jMap.PlanYFromUnitY(AthenaMeshY[i]);
                jErr = Math.Max(jErr, Math.Max(Math.Abs(ex - jX[i]), Math.Abs(ey - jY[i])));
                if (Math.Abs(ex - jX[i]) < 1e-3f && Math.Abs(ey - jY[i]) < 1e-3f) jMatched++;
            }
            Check(jMatched == AthenaMeshX.Length && jErr < 1e-3f,
                "S15/no-aspect-vertex-parity matched=" + jMatched + " err=" + jErr.ToString("0.####"));
            Check(jMap.DrawingW > 0f && Near(jMap.DrawingW, 114f, 0.01f) && Near(jMap.DrawingH, 82f, 0.01f),
                "S15/no-aspect-drawing-size=" + jMap.DrawingW.ToString("0.##") + "x" + jMap.DrawingH.ToString("0.##"));
            Check(jBox.Width > 100f, "S15/no-aspect-box-wider=" + jBox.Width.ToString("0.#"));
            j.Greek.OnDisable();
            FixturePreserveAspectOverride = null;

            // ---- (d) overrideSprite：几何取 override 的 mesh ----
            NativeArtOverrideSpriteFactory = MakeDiamondIslandSprite;
            Fixture h = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Tick(h, 2);
            RectTransform hLand = h.Greek.lands[0].gameObject.GetComponent<RectTransform>();
            Check(TryReadProductionShape(hLand, h.Greek.lands[0], out _, out float[] hX, out _, out string hReason),
                "S15/override-shape-read " + hReason);
            Check(hX != null && hX.Length == 4, "S15/override-mesh-used verts=" + (hX == null ? -1 : hX.Length));
            Image hImage = FindChild(hLand, "Land Image").GetComponent<Image>();
            Check(hImage != null && hImage.overrideSprite != null && hImage.sprite != null &&
                  hImage.overrideSprite.name.StartsWith("counterexample_diamond"),
                "S15/override-sprite-field-kept name=" + (hImage == null || hImage.overrideSprite == null
                    ? "<none>" : hImage.overrideSprite.name));
            h.Greek.OnDisable();
            NativeArtOverrideSpriteFactory = null;

            // ---- (e) 不支持模式：useSpriteMesh=false / type!=Simple ⇒ 读取失败 + 运行时保留 native ----
            Fixture i1 = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Tick(i1, 2);
            RectTransform i1Land = i1.Greek.lands[0].gameObject.GetComponent<RectTransform>();
            Image i1Image = FindChild(i1Land, "Land Image").GetComponent<Image>();
            i1Image.useSpriteMesh = false;
            i1.Current.landData[0].steedSpawns = Steeds(37, 33);
            UpdateDetails(i1);
            Check(TryReadProductionShape(i1Land, i1.Greek.lands[0], out _, out _, out _, out string offReason) == false &&
                  offReason == "sprite-mesh-off", "S15/useSpriteMesh-off-reason=" + offReason);
            Check(CountNamed(i1Land.gameObject, "KEM_MapResourceIcons") == 0 && NativeSlotsRestored(i1Land),
                "S15/useSpriteMesh-off-native-kept");
            i1.Greek.OnDisable();
        }

        /// <summary>反例 sprite：mesh 与 Athena 相同，但 Sprite.bounds 只有 mesh bbox 的一半（bounds≠rect/ppu 的强反例）。</summary>
        private static Sprite MakeHalfBoundsIslandSprite()
        {
            Sprite sprite = MakeNativeIslandSprite("counterexample_half_bounds");
            Bounds full = sprite.bounds;
            sprite.bounds = new Bounds(full.center, new Vector3(full.size.x * 0.5f, full.size.y * 0.5f, 0f));
            return sprite;
        }

        /// <summary>名称 "iconType_type(Clone)" → 原 prefab 尺寸（fixture 的真实尺寸表）。</summary>
        private static bool TryNativeIconSize(string name, out float width, out float height)
        {
            width = 0f;
            height = 0f;
            if (string.IsNullOrEmpty(name)) return false;
            // 形如 "SourceIcon 1_37(Clone)"：最后一个空格后是 "<iconType>_<type>"。
            int underscore = name.LastIndexOf('_');
            int space = name.LastIndexOf(' ');
            if (underscore <= 0 || space < 0 || space + 1 >= underscore) return false;
            if (!int.TryParse(name.Substring(space + 1, underscore - space - 1), out int iconType)) return false;
            int end = name.IndexOf('(', underscore);
            string digits = end > 0 ? name.Substring(underscore + 1, end - underscore - 1)
                : name.Substring(underscore + 1);
            if (!int.TryParse(digits, out int type)) return false;
            if (!NativeIconSizes.TryGetValue(iconType + "/" + type, out Vector2 size)) return false;
            width = size.x;
            height = size.y;
            return true;
        }

        /// <summary>fixture 用的 (iconType/type) 真实尺寸（icon-rects.json 实测值）。</summary>
        private static readonly Dictionary<string, Vector2> NativeIconSizes = new Dictionary<string, Vector2>
        {
            { "1/37", new Vector2(40f, 28f) }, { "1/33", new Vector2(40f, 20f) },
            { "0/2", new Vector2(20f, 36f) }, { "2/0", new Vector2(18f, 24f) },
        };

        private static bool IsLadderRung(float rung)
        {
            for (int i = 0; i < MapResourceIconPlanner.Scales.Length; i++)
            {
                if (Math.Abs(MapResourceIconPlanner.Scales[i] - rung) <= 0.02f) return true;
            }
            return false;
        }

        /// <summary>三个接管槽位的原生 spawned 图标都回到可见（整体 fallback 的可观察证据）。</summary>
        private static bool NativeSlotsRestored(RectTransform land)
        {
            string[] names = { "Steed1", "Hermit", "Statue" };
            for (int i = 0; i < names.Length; i++)
            {
                RectTransform slot = FindChild(land, names[i]);
                if (slot == null) continue;
                RectTransform spawned = FindChild(slot, "Spawned " + names[i]);
                if (spawned == null || !spawned.gameObject.activeSelf) return false;
            }
            return true;
        }

        private static Il2CppStructArray<Hermit.HermitType> Hermits(params int[] values)
        {
            var array = new Il2CppStructArray<Hermit.HermitType>(values.Length);
            for (int i = 0; i < values.Length; i++) array[i] = (Hermit.HermitType)values[i];
            return array;
        }

        private static Il2CppStructArray<Statue.Deity> Statues(params int[] values)
        {
            var array = new Il2CppStructArray<Statue.Deity>(values.Length);
            for (int i = 0; i < values.Length; i++) array[i] = (Statue.Deity)values[i];
            return array;
        }

        private static Il2CppStructArray<SteedType> Steeds(params int[] values)
        {
            var array = new Il2CppStructArray<SteedType>(values.Length);
            for (int i = 0; i < values.Length; i++) array[i] = (SteedType)values[i];
            return array;
        }

        /// <summary>事实导出（SVG 用）：**两个真实视口**（300×200 默认 paper / 400×260 大 paper）各自跑完整 Commit，
        /// 导出 rocks 实际落点 + clash 复核 + shore/band/upper + world 图标（含 TypeId）；detail 走真实 detail
        /// 路径导出 art/boat/16 图标（含 TypeId）。全部来自 production 运行结果，不手摆。
        /// </summary>
        private static void ScenarioFacts()
        {
            Console.WriteLine("== facts export ==");
            int[] sixteen = { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };

            Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: sixteen);
            Tick(f, 2);
            UpdateDetails(f);   // 真实 detail 路径（menu.UpdateLands 的逐岛 UpdateLand）→ 详情 16 图标布局
            WorldFacts fw = CaptureWorldFacts(f);
            DetailFacts fd = CaptureDetailFacts(f);

            // 400×260：大 paper（world = paper+18/+4 = 400×260）真实跑同一 Commit/RocksAnchor
            Fixture big = BuildWorldSized(382f, 256f, true, 16, sixteen);
            Tick(big, 2);
            WorldFacts bw = CaptureWorldFacts(big);

            if (!TryReadOwnAsset(out byte[] ownBytes, out string ownLabel, out string verifiedInput))
            {
                Console.WriteLine("FAIL own asset verification failed (embedded resource missing, or --asset " +
                    "missing/different); no facts written");
                _fails++;
                return;
            }
            var sb = new System.Text.StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"asset\": {\"name\": \"").Append(ownLabel).Append("\", \"sha256\": \"")
              .Append(ShaBytes(ownBytes)).Append("\"");
            if (verifiedInput != null) sb.Append(", \"verifiedInput\": \"").Append(verifiedInput).Append("\"");
            sb.Append("},\n");
            sb.Append("  \"world\": {\n");
            AppendWorld(sb, "300x200", fw, "    ");
            sb.Append(",\n");
            AppendWorld(sb, "400x260", bw, "    ");
            sb.Append("\n  },\n");
            sb.Append("  \"detail\": ");
            AppendDetail(sb, fd);
            sb.Append("\n}\n");
            File.WriteAllText(_factsPath, sb.ToString());
            Console.WriteLine("facts written " + _factsPath + " (300x200 + 400x260 + detail)");
        }

        private struct WorldFacts
        {
            internal string Key;
            internal float PaperW, PaperH;
            internal MapIconBox Rocks, Shore, Domain, Band, Upper;
            internal bool ClashFree;
            internal List<KeyValuePair<int, MapIconBox>> Icons;   // TypeId → paper box
        }

        private struct DetailFacts
        {
            internal float LandW, LandH;
            internal MapIconBox Art, Boat, Keep;
            internal List<KeyValuePair<int, MapIconBox>> Icons;   // TypeId → land-local box
        }

        private static WorldFacts CaptureWorldFacts(Fixture f)
        {
            var facts = new WorldFacts
            {
                PaperW = f.Paper.rect.width,
                PaperH = f.Paper.rect.height,
                Rocks = PaperArtBox(f.Paper, f.Rocks),
                Shore = PaperArtBox(f.Paper, ArtOf(f.Extension)),
                Domain = (MapIconBox)ReadStatic("_worldDomain"),
                Band = (MapIconBox)ReadStatic("_worldBandArea"),
                Upper = (MapIconBox)ReadStatic("_worldUpperArea"),
                Icons = new List<KeyValuePair<int, MapIconBox>>(),
            };
            foreach (RectTransform icon in CollectIcons(f.Paper, 16))
            {
                facts.Icons.Add(new KeyValuePair<int, MapIconBox>(ParseIconType(icon.gameObject.name),
                    PaperArtBox(f.Paper, icon)));
            }
            facts.ClashFree = VerifyRocksClashFree(f, facts.Rocks, facts.Shore, facts.Domain);
            return facts;
        }

        private static DetailFacts CaptureDetailFacts(Fixture f)
        {
            RectTransform detailRect = f.ExtensionDetail.gameObject.GetComponent<RectTransform>();
            // 导出空间 = detail page 空间（相对 land 中心；= native legend/Boat 的坐标语义）：
            // 本模块的 box 空间原点 = land rect 左下（见 TryLocalBox），且 stub 的 anchoredPosition 是
            // 归一化语义（anchor(0,0) 图标少“父 rect 原点 - 锚点参考”常量）→ 统一减去空间中心 (w/2,h/2)，
            // 使 art/boat/sidebar/图标与 native 值、SVG page 同处一个坐标系。
            float cx = detailRect.rect.width * 0.5f;
            float cy = detailRect.rect.height * 0.5f;
            MapIconBox Shift(in MapIconBox box) => new MapIconBox(box.X0 - cx, box.Y0 - cy, box.X1 - cx, box.Y1 - cy);
            var facts = new DetailFacts
            {
                LandW = detailRect.rect.width,
                LandH = detailRect.rect.height,
                Art = Shift(LandLocalBox(detailRect, f.DetailLandImage.transform as RectTransform)),
                Boat = f.DetailBoat != null ? Shift(LandLocalBox(detailRect, f.DetailBoat)) : default,
                Icons = new List<KeyValuePair<int, MapIconBox>>(),
            };
            RectTransform keep = FindChild(detailRect, "Keep");
            facts.Keep = keep != null ? Shift(LandLocalBox(detailRect, keep)) : default;
            foreach (RectTransform icon in CollectIcons(detailRect, 16))
            {
                // 图标由生产写 anchoredPosition（anchor(0,0)/pivot(0,0)）：stub 少 holder 常量 → 先还原真实渲染盒
                // （Shift 一次），再换算到 page 空间（再 Shift 一次）；art 经迭代收敛/keep 等 native 节点各只差一次。
                MapIconBox rendered = Shift(LandLocalBox(detailRect, icon));
                facts.Icons.Add(new KeyValuePair<int, MapIconBox>(ParseIconType(icon.gameObject.name),
                    Shift(rendered)));
            }
            return facts;
        }

        /// <summary>从 clone 名 "SourceIcon 0_21(Clone)" 解析 TypeId；未知返回 -1（导出侧必须报警）。</summary>
        private static int ParseIconType(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            int underscore = name.LastIndexOf('_');
            if (underscore < 0) return -1;
            int end = name.IndexOf('(', underscore);
            string digits = end > 0 ? name.Substring(underscore + 1, end - underscore - 1)
                : name.Substring(underscore + 1);
            return int.TryParse(digits, out int type) ? type : -1;
        }

        /// <summary>runtime 复核：rocks 盒不与任何 native 图形/扩展岸线相交且在 world 域内（同 production 口径）。</summary>
        private static bool VerifyRocksClashFree(Fixture f, in MapIconBox rocks, in MapIconBox shore,
            in MapIconBox domain)
        {
            if (rocks.X0 < domain.X0 || rocks.Y0 < domain.Y0 || rocks.X1 > domain.X1 || rocks.Y1 > domain.Y1)
            {
                return false;
            }
            if (rocks.Intersects(shore, 2f)) return false;
            var excluded = new List<RectTransform>();
            if (ReadStatic("_bannerExcludedRects") is List<RectTransform> banner) excluded.AddRange(banner);
            excluded.Add(f.Rocks);
            var obstacles = new List<MapIconBox>(64);
            CollectObstaclesViaProduction(f.Paper, f.Paper, obstacles, true, excluded);
            for (int i = 0; i < obstacles.Count; i++)
            {
                if (rocks.Intersects(obstacles[i], 1.5f)) return false;
            }
            return true;
        }

        /// <summary>反射调用 production 的 CollectNativeBoxes（探针只读核验用，与生产同一实现）。</summary>
        private static void CollectObstaclesViaProduction(RectTransform space, Transform root,
            List<MapIconBox> into, bool excludeOwn, List<RectTransform> excluded)
        {
            System.Reflection.MethodInfo method = typeof(MapMountIcons).GetMethod("CollectNativeBoxes",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static, null,
                new[] { typeof(RectTransform), typeof(Transform), typeof(List<MapIconBox>), typeof(bool),
                    typeof(List<RectTransform>) }, null);
            method.Invoke(null, new object[] { space, root, into, excludeOwn, excluded });
        }

        private static void AppendWorld(System.Text.StringBuilder sb, string key, WorldFacts w, string indent)
        {
            sb.Append(indent).Append('"').Append(key).Append("\": {\"paper\": [").Append(F(w.PaperW))
              .Append(',').Append(F(w.PaperH)).Append("], \"domain\": ").Append(BoxJson(w.Domain))
              .Append(", \"band\": ").Append(BoxJson(w.Band))
              .Append(", \"upper\": ").Append(BoxJson(w.Upper))
              .Append(", \"shoreBox\": ").Append(BoxJson(w.Shore))
              .Append(", \"rocksBox\": ").Append(BoxJson(w.Rocks))
              .Append(", \"rocksClashFree\": ").Append(w.ClashFree ? "true" : "false")
              .Append(", \"icons\": [");
            for (int i = 0; i < w.Icons.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("{\"type\": ").Append(w.Icons[i].Key).Append(", \"box\": ")
                  .Append(BoxJson(w.Icons[i].Value)).Append('}');
            }
            sb.Append("]}");
        }

        private static void AppendDetail(System.Text.StringBuilder sb, DetailFacts d)
        {
            sb.Append("{\"land\": [").Append(F(d.LandW)).Append(',').Append(F(d.LandH))
              .Append("], \"artBox\": ").Append(BoxJson(d.Art))
              .Append(", \"boatBox\": ").Append(BoxJson(d.Boat))
              .Append(", \"keepBox\": ").Append(BoxJson(d.Keep))
              .Append(", \"icons\": [");
            for (int i = 0; i < d.Icons.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("{\"type\": ").Append(d.Icons[i].Key).Append(", \"box\": ")
                  .Append(BoxJson(d.Icons[i].Value)).Append('}');
            }
            sb.Append("]}");
        }

        private static string F(float value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

        private static string BoxJson(MapIconBox box)
            => "[" + F(box.X0) + ", " + F(box.Y0) + ", " + F(box.X1) + ", " + F(box.Y1) + "]";

        /// <summary>
        /// 自有 PNG 字节来源 = **已嵌入资源**（真正参与布局的字节，任意 cwd 一致）。
        /// `--asset &lt;path&gt;` 是**验证输入**而非替换：路径必须存在、可读，且其 SHA256 必须等于 embedded 自有 PNG
        /// 的 SHA —— 不一致或缺失一律返回 false（调用方非 0 退出且**不写** facts），绝不静默忽略差异；一致时仍使用
        /// embedded bytes 计算 facts SHA。公开 facts 只记 basename/resourceid，绝不写本地绝对路径。
        /// </summary>
        private static bool TryReadOwnAsset(out byte[] bytes, out string label, out string verifiedInput)
        {
            bytes = null;
            label = KingdomEnhancedMod.MapExtensionIslandArt.ResourceName;
            verifiedInput = null;
            try
            {
                bytes = KingdomEnhancedMod.MapExtensionIslandArt.ReadResourceBytes();
                if (bytes == null || bytes.Length == 0)
                {
                    Console.WriteLine("FAIL embedded own asset missing (" +
                        KingdomEnhancedMod.MapExtensionIslandArt.ResourceName + ")");
                    return false;
                }
                if (_assetPath == null) return true;
                verifiedInput = Path.GetFileName(_assetPath);
                if (!File.Exists(_assetPath))
                {
                    Console.WriteLine("FAIL --asset input missing: " + verifiedInput);
                    return false;
                }
                byte[] input = File.ReadAllBytes(_assetPath);
                if (input.Length == 0)
                {
                    Console.WriteLine("FAIL --asset input unreadable/empty: " + verifiedInput);
                    return false;
                }
                string embeddedSha = ShaBytes(bytes);
                string inputSha = ShaBytes(input);
                if (!string.Equals(embeddedSha, inputSha, StringComparison.Ordinal))
                {
                    Console.WriteLine("FAIL --asset input mismatch: " + verifiedInput + " sha256=" + inputSha +
                        " != embedded " + embeddedSha + " (facts always report the embedded bytes actually used)");
                    return false;
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        private static string ShaBytes(byte[] data)
        {
            if (data == null || data.Length == 0) return "";
            using var sha = System.Security.Cryptography.SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
        }

        private static bool SameVec(Vector2 a, Vector2 b)
        {
            return Math.Abs(a.x - b.x) < 0.001f && Math.Abs(a.y - b.y) < 0.001f;
        }

        private static RectTransform FindChild(RectTransform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child != null && child.name == name) return child as RectTransform;
            }
            return null;
        }

        /// <summary>detail land 内某个原生动态槽在 land 左下空间的盒（fixture 用固定名）。</summary>
        private static MapIconBox DetailSlotBox(RectTransform land, string slotName)
        {
            RectTransform slot = FindChild(land, slotName);
            return slot != null ? LandLocalBox(land, slot) : default;
        }

        /// <summary>inner 是否完整落在 outer 内（容差 eps）。</summary>
        private static bool Inside(in MapIconBox inner, in MapIconBox outer, float eps)
        {
            return inner.X0 >= outer.X0 - eps && inner.Y0 >= outer.Y0 - eps &&
                   inner.X1 <= outer.X1 + eps && inner.Y1 <= outer.Y1 + eps;
        }

        // ------------------------------------------------------------------ fixture

        private static Fixture BuildWorld(bool withExtension, int steedCount, int[] types)
            => BuildWorldSized(282f, 196f, withExtension, steedCount, types);

        /// <summary>按 paper 尺寸参数化（世界域 = paper+18/+4；用于 300×200 与 400×260 两个真实视口）。</summary>
        private static Fixture BuildWorldSized(float paperW, float paperH, bool withExtension, int steedCount,
            int[] types)
        {
            float worldW = paperW + 18f, worldH = paperH + 4f;
            float areaW = paperW + 40f, areaH = paperH + 20f;
            const int screenW = 1280, screenH = 720;   // 位置链保持原样（两种视口共用屏幕；仅尺寸参数化）
            if (!_preserveState)
            {
                ProbeHooks.RunMenuDisablePostfix(null);
                KingdomEnhancedMod.MapExtensionIslandArt.Reset();
            }
            ProbeHooks.Reset();
            MapIconSources.ResetCalls = 0;
            MapCustomIconAssets.ResetCalls = 0;

            var f = new Fixture();
            f.Campaign = CampaignSaveData.MakeCurrent();
            BiomeHolder.Inst = new BiomeHolder { BiomeIndex = BiomeHolder.GreeceBiomeIndex };
            ModConfig.Enabled.Value = true;
            ModConfig.CrossWorldMountsEnabled.Value = true;
            ExtensionIslandRuntime.Available = true;
            f.Extension = null;
            f.ExtensionDetail = null;
            ExtensionIslandMap.PhysicalIndexOverride = land =>
                (land != null && (land == f.Extension || land == f.ExtensionDetail)) ? 11 : (int?)null;

            int landCount = withExtension ? 11 : 10;
            f.Current = MakeReign("current", landCount, 35, 1);
            if (withExtension && types != null)
            {
                var steeds = new Il2CppStructArray<SteedType>(types.Length);
                for (int i = 0; i < types.Length; i++) steeds[i] = (SteedType)types[i];
                f.Current.landData[11].steedSpawns = steeds;
            }
            f.Campaign.currentReign = f.Current;
            f.Campaign.previousReigns.Clear();

            f.Greek = new GameObject("GreekMenu").AddComponent<MapTimelineMenuGreece>();
            f.Greek.lands = new Il2CppSystem.Collections.Generic.List<UILand>();
            Menu.Inst = new Menu { ActiveMap = f.Greek };
            // 模拟登记代际：canonical 视觉 owner token（MapMountIcons 的 shore/detail 租约 owner）。
            ExtensionIslandMap.VisualOwnerToken = new object();
            ExtensionIslandMap.VisualOwnerMenu = f.Greek;
            f.Root = MakeRect("MapRoot", f.Greek.transform, new Vector2(screenW, screenH), new Vector2(0f, 0f),
                new Vector2(0f, 0f));
            var canvas = f.Root.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = f.Root.gameObject.AddComponent<Camera>();
            canvas.scaleFactor = 1f;
            canvas.pixelRect = new Rect(0f, 0f, screenW, screenH);
            Screen.width = screenW;
            Screen.height = screenH;
            Screen.safeArea = new Rect(0f, 0f, screenW, screenH);
            f.Mask = MakeRect("Mask", f.Root, new Vector2(screenW, screenH), new Vector2(500f, 360f),
                new Vector2(0.5f, 0.5f));

            RectTransform mapArea = MakeRect("Map Area", f.Mask, new Vector2(areaW, areaH), new Vector2(200f, 100f),
                new Vector2(0f, 0f));
            RectTransform backdrop = MakeRect("Background Objects", mapArea, new Vector2(areaW, areaH),
                new Vector2(0f, 0f), new Vector2(0f, 0f));
            RectTransform paperImage = MakeRect("Image", backdrop, new Vector2(worldW, worldH), new Vector2(0f, 0f),
                new Vector2(0f, 0f));
            Image image = paperImage.gameObject.AddComponent<Image>();
            image.sprite = new Sprite { name = "scroll_map_bg_tiled" };

            f.ContentArea = MakeRect("Content Area", mapArea, new Vector2(180f, 60f), new Vector2(0f, 0f),
                new Vector2(0f, 0f));
            f.ContentArea.gameObject.AddComponent<RectMask2D>();
            f.LandsContainer = MakeRect("Lands Container", f.ContentArea, new Vector2(180f, 60f),
                new Vector2(0f, 0f), new Vector2(0f, 0f));
            f.LandsContainer.gameObject.AddComponent<RectMask2D>();

            f.Scroller = MakeRect("Lands Scroller", f.LandsContainer, new Vector2(paperW, paperH),
                new Vector2(0f, 0f), new Vector2(0f, 0f));
            f.Map = new GameObject("MainMap").AddComponent<UIMainMap>();
            f.Paper = f.Map.gameObject.AddComponent<RectTransform>();
            f.Paper.name = "Main_Map_Greece";
            f.Paper.SetParent(f.Scroller, false);
            f.Paper.pivot = new Vector2(0f, 0f);
            f.Paper.sizeDelta = new Vector2(paperW, paperH);
            f.Paper.anchoredPosition = new Vector2(0f, 0f);

            RectTransform content = MakeRect("Content", f.Paper, new Vector2(paperW + 32f, paperH + 12f),
                new Vector2((paperW + 32f) * 0.5f, (paperH + 12f) * 0.5f), new Vector2(0f, 0f));
            RectTransform container = MakeRect("Lands Container Inner", content, new Vector2(paperW + 32f, paperH + 12f),
                new Vector2(0f, 0f), new Vector2(0f, 0f));

            RectTransform holder = MakeRect("LandsHolder", f.Scroller, new Vector2(paperW + 12f, paperH - 10f),
                new Vector2(0f, 0f), new Vector2(0f, 0f));
            f.Holder = holder;
            f.Greek._landsHolder = holder;
            f.Greek.landScroller = f.Scroller;

            for (int i = 0; i < landCount; i++)
            {
                // actual detail root 原型：/Map_Land_Oracle_Greece = 180×150、pivot(.5,.5)、anchor(.5,.5)，
                // 居中于 282×196 页面（land 本地坐标即"页面中心原点"，sidebar/plan 常量均此坐标系）。
                var detail = new GameObject("Detail Land " + i).AddComponent<UILand>();
                RectTransform detailRect = detail.gameObject.AddComponent<RectTransform>();
                detailRect.SetParent(holder, false);
                detailRect.sizeDelta = new Vector2(180f, 150f);
                detailRect.pivot = new Vector2(0.5f, 0.5f);
                detailRect.localPosition = new Vector3(holder.rect.width * 0.5f, holder.rect.height * 0.5f, 0f);
                // Land Image / Land Outline Highlight：actual anchor(0,0)、pivot(0,0)、ap(25,30)、114×82
                // → 旧 art 中心 (−8,−4) = (−90+25+57, −75+30+41)。
                RectTransform landImageRect = MakeRect("Land Image", detailRect, new Vector2(114f, 82f),
                    new Vector2(0f, 0f), new Vector2(0f, 0f));
                FixedAnchor(landImageRect, detailRect, new Vector2(0f, 0f), new Vector2(25f, 30f));
                Image landImage = landImageRect.gameObject.AddComponent<Image>();
                // exact native island sprite mesh（私有测试事实：Athena tholos 10 顶点/8 三角形，含 pivot/ppu；
                // 不含位图）——"generic 全矩形" fixture 不能冒充岛形验收。
                landImage.sprite = NativeIslandSpriteFactory();
                if (NativeArtOverrideSpriteFactory != null) landImage.overrideSprite = NativeArtOverrideSpriteFactory();
                landImage.type = FixtureImageTypeOverride ?? Image.Type.Simple;
                landImage.useSpriteMesh = FixtureUseSpriteMeshOverride ?? true;
                landImage.preserveAspect = FixturePreserveAspectOverride ?? true;
                RectTransform detailOutline = MakeRect("Land Outline Highlight", detailRect, new Vector2(114f, 82f),
                    new Vector2(0f, 0f), new Vector2(0f, 0f));
                FixedAnchor(detailOutline, detailRect, new Vector2(0f, 0f), new Vector2(25f, 30f));
                detailOutline.gameObject.AddComponent<Image>().sprite = new Sprite { name = "native_detail_outline" };
                // 船标 = 实际 detail 模板（review/detail-boat-native.{md,json}，resources.assets UILand:96685._ship
                // = GameObject 29668 "Boat Icon"，Image96019 sprite sharedassets0:8541 map_boat_0 32×32）：
                // 精确父链 Map Icons(82583) → Generic(81922) → Boat Icon(82198)，两父级 size(0,0)/位置0/scale1/
                // center anchors；船身 32×32、localScale(1,1,1)、anchorMin=anchorMax=pivot(.5,.5)、
                // anchoredPosition(-48.0000114,+18.0000114) ⇒ 相对 root 中心 AABB[-64,2,-32,34]，
                // root 左下坐标 (26,77,58,109)。数值为静态资产/原生切片读取，非实机截图。
                RectTransform boatIcons = MakeRect("Map Icons", detailRect, new Vector2(0f, 0f),
                    new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
                RectTransform boatGeneric = MakeRect("Generic", boatIcons, new Vector2(0f, 0f),
                    new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
                RectTransform detailBoat = MakeRect("Boat Icon", boatGeneric, new Vector2(32f, 32f),
                    new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
                FixedAnchor(detailBoat, boatGeneric, new Vector2(0.5f, 0.5f),
                    new Vector2(-48.0000114f, 18.0000114f));
                detailBoat.gameObject.AddComponent<Image>().sprite =
                    new Sprite { name = "map_boat_0@8541" };   // 模板 sprite 名/索引；位图不进公开包
                if (i == 10) f.DetailBoat = detailBoat;
                // 原生动态槽（resources.assets 实测 /Map_Land_God_* 模板：Steed1 盒 [-19,0,19,26]、
                // Hermit [-11,0,11,20]、Statue [-10,0,10,36]；root 180×150 中心原点、pivot(.5,0)/anchor 中心/ap(0,0)）。
                // host 自身带 Image（锚点占位；接管期必须被判为“非障碍”），_spawnedIcon 子对象在接管期被压制。
                var detailHosts = new Il2CppReferenceArray<UIDynamicMapIcon>(3);
                detailHosts[0] = MakeDetailSlot(detailRect, "Steed1", UIDynamicMapIcon.IconType.Steed,
                    1, new Vector2(38f, 26f));
                detailHosts[1] = MakeDetailSlot(detailRect, "Hermit", UIDynamicMapIcon.IconType.Hermit,
                    1, new Vector2(22f, 20f));
                detailHosts[2] = MakeDetailSlot(detailRect, "Statue", UIDynamicMapIcon.IconType.Statue,
                    1, new Vector2(20f, 36f));
                detail._dynamicMapIcons = detailHosts;
                // legend fixture（实测 Keep 中心 −107.5/43.4、23×13 → 右缘 −96）
                RectTransform keep = MakeRect("Keep", detailRect, new Vector2(23f, 13f),
                    new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
                FixedAnchor(keep, detailRect, new Vector2(0.5f, 0.5f), new Vector2(-107.5f, 43.4f));
                keep.gameObject.AddComponent<Image>();
                f.Greek.lands.Add(detail);
                if (i == 10)
                {
                    f.ExtensionDetail = detail;
                    f.DetailLandImage = landImage;
                    f.DetailNativeSprite = landImage.sprite;
                }
            }

            f.Map._lands = new Il2CppReferenceArray<UILand>(landCount);
            f.Map._mainMapLands = new Il2CppReferenceArray<UIMainMapLand>(landCount);
            f.Lands = new UILand[landCount];
            for (int i = 0; i < landCount; i++)
            {
                bool extension = withExtension && i == 10;
                Vector2 pos = extension ? new Vector2(0f, -110f) : NativePosition(i);
                UILand land = MakeCluster(f, "Land " + i, container, pos, extension);
                if (extension) f.Extension = land;
                f.Lands[i] = land;
                f.Map._lands[i] = land;
                f.Map._mainMapLands[i] = land.gameObject.GetComponent<UIMainMapLand>();
                f.OriginalClusterPositions.Add(land.gameObject.GetComponent<RectTransform>().anchoredPosition);
                f.OriginalClusterScales.Add(land.gameObject.GetComponent<RectTransform>().localScale.x);
            }

            var realSizes = new Dictionary<int, Vector2>
            {
                { 21, new Vector2(25f, 16f) }, { 22, new Vector2(24f, 22f) }, { 23, new Vector2(21f, 26f) },
                { 24, new Vector2(25f, 17f) }, { 25, new Vector2(21f, 18f) }, { 26, new Vector2(21f, 18f) },
                { 13, new Vector2(29f, 13f) }, { 14, new Vector2(29f, 13f) }, { 15, new Vector2(26f, 14f) },
                { 16, new Vector2(28f, 18f) }, { 2, new Vector2(28f, 13f) }, { 4, new Vector2(40f, 25f) },
                { 7, new Vector2(22f, 19f) }, { 3, new Vector2(24f, 32f) }, { 38, new Vector2(40f, 26f) },
                { 6, new Vector2(20f, 20f) },
            };
            // (iconType, type) 双键真实尺寸表（icon-rects.json 实测）；十六坐骑集沿用 type 键。
            var typedSizes = new Dictionary<string, Vector2>
            {
                { "0/2", new Vector2(20f, 36f) }, { "2/0", new Vector2(18f, 24f) },
                { "1/37", new Vector2(40f, 28f) }, { "1/33", new Vector2(40f, 20f) },
            };
            MapIconSources.Resolver = (land, iconType, type) =>
            {
                Vector2 size;
                if (typedSizes.TryGetValue(iconType + "/" + type, out Vector2 typed)) size = typed;
                else if (realSizes.TryGetValue(type, out Vector2 real)) size = real;
                else size = new Vector2(20f, 20f);
                return MakeSourceIcon(iconType + "_" + type, size);
            };

            f.Greek._mainMap = f.Map;
            f.Greek.focusedReign = 0;
            f.Greek.targetFocusedReign = 0;
            f.Greek._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingWorld;
            f.Greek.isAnimating = false;
            f.Greek.HasScrolledTargetLand = true;

            // exact mainMap 直属装饰（actual：sprite 8952、32×28、scale .5、pivot(.5,0)、pos(8,-55)）
            f.Rocks = MakeRect("map_icon_rocks_greece", f.Paper, new Vector2(32f, 28f),
                new Vector2(8f, -55f), new Vector2(0.5f, 0f));
            f.Rocks.localScale = new Vector3(0.5f, 0.5f, 1f);
            f.Rocks.gameObject.AddComponent<Image>().sprite = new Sprite { name = "map_icon_rocks__greece" };
            f.OtherDecor = MakeRect("map_icon_other_decor", f.Paper, new Vector2(20f, 20f),
                new Vector2(-60f, -55f), new Vector2(0.5f, 0f));
            f.OtherDecor.gameObject.AddComponent<Image>().sprite = new Sprite { name = "map_icon_other__greece" };
            return f;
        }

        private static Vector2 NativePosition(int index)
        {
            switch (index)
            {
                case 0: return new Vector2(-110f, 34f);
                case 1: return new Vector2(110f, 34f);
                case 2: return new Vector2(-110f, -40f);
                case 3: return new Vector2(110f, -40f);
                case 4: return new Vector2(0f, 56f);
                case 5: return new Vector2(0f, -6f);
                case 6: return new Vector2(-56f, 40f);
                case 7: return new Vector2(56f, 40f);
                case 8: return new Vector2(-62f, -46f);
                default: return new Vector2(62f, -46f);
            }
        }

        private static RectTransform MakeRect(string name, Transform parent, Vector2 size, Vector2 position,
            Vector2 pivot)
        {
            var go = new GameObject(name);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.pivot = pivot;
            rect.sizeDelta = size;
            if (parent != null) rect.SetParent(parent, false);
            rect.anchoredPosition = position;
            return rect;
        }

        /// <summary>
        /// 真实 Unity 锚点语义的最小实现（仅 detail fixture 使用）：pivotPos = anchorRef + anchoredPosition，
        /// 即 child.localPosition = parentRect.min + parentSize*anchor + ap（child.rect 自身按 pivot 展开）。
        /// 世界 fixture 沿用既有"anchoredPosition == localPosition"归一化语义，不在此改动。
        /// </summary>
        private static void FixedAnchor(RectTransform child, RectTransform parent, Vector2 anchor, Vector2 ap)
        {
            child.anchorMin = anchor;
            child.anchorMax = anchor;
            Rect pr = parent.rect;
            float x = pr.xMin + pr.width * anchor.x + ap.x;
            float y = pr.yMin + pr.height * anchor.y + ap.y;
            child.localPosition = new Vector3(x, y, 0f);
        }

        private static UILand MakeCluster(Fixture f, string name, Transform parent, Vector2 position, bool extension)
        {
            var go = new GameObject(name);
            UILand land = go.AddComponent<UILand>();
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.pivot = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(60f, 44f);
            rect.SetParent(parent, false);
            rect.anchoredPosition = position;

            var cluster = go.AddComponent<UIMainMapLand>();
            cluster.IsUnlocked = true;

            // 真实 native overview 几何：art rect 84×62（Athena 岛图），localScale 0.5（实测），
            // 于是 paper 里岛图 = 84×0.5×全局 fit；图标请求（prefab 尺寸）必须按同一自然倍率换算。
            RectTransform art = MakeRect("Land Button " + name, rect, new Vector2(84f, 62f), new Vector2(2f, 2f),
                new Vector2(0f, 0f));
            if (!extension) art.localScale = new Vector3(0.5f, 0.5f, 1f);
            if (extension)
            {
                art.gameObject.AddComponent<Image>().sprite = new Sprite { name = "native_oracle_island" };
            }
            else
            {
                // 真实 native 字段组合：Simple + useSpriteMesh + sprite rect 84×62(=art rect) + bounds=mesh bbox。
                Image islandImage = art.gameObject.AddComponent<Image>();
                islandImage.sprite = NativeIslandSpriteFactory();
                if (NativeArtOverrideSpriteFactory != null) islandImage.overrideSprite = NativeArtOverrideSpriteFactory();
                islandImage.type = FixtureImageTypeOverride ?? Image.Type.Simple;
                islandImage.useSpriteMesh = FixtureUseSpriteMeshOverride ?? true;
                islandImage.preserveAspect = FixturePreserveAspectOverride ?? true;   // 实测资产 m_PreserveAspect=1
            }
            if (extension)
            {
                // actual 近似：terrain Image（Land Button Oracle）+ outline Image + 船标兄弟子树
                RectTransform outline = MakeRect("Land Outline Highlight", rect, new Vector2(34f, 24f),
                    new Vector2(2f, 2f), new Vector2(0f, 0f));
                outline.gameObject.AddComponent<Image>().sprite = new Sprite { name = "native_oracle_outline" };
                Button button = outline.gameObject.AddComponent<Button>();
                cluster._button = button;
                RectTransform boat = MakeRect("Boat Icon", rect, new Vector2(32f, 32f),
                    new Vector2(-19f, -17f), new Vector2(0.5f, 0.5f));
                boat.localScale = new Vector3(0.5f, 0.5f, 1f);
                boat.gameObject.AddComponent<Image>();
            }

            var hosts = new Il2CppReferenceArray<UIDynamicMapIcon>(1);
            var iconGo = new GameObject("NativeSpawn " + name);
            RectTransform iconRect = iconGo.AddComponent<RectTransform>();
            iconRect.pivot = new Vector2(0f, 0f);
            iconRect.sizeDelta = new Vector2(40f, 28f);
            iconRect.SetParent(rect, false);
            iconRect.anchoredPosition = new Vector2(4f, 4f);
            UIMapIcon spawned = iconGo.AddComponent<UIMapIcon>();
            UIDynamicMapIcon host = iconGo.AddComponent<UIDynamicMapIcon>();
            host._type = UIDynamicMapIcon.IconType.Steed;
            host._steedNum = 1;   // 真实 ABI：Steed 槽 1-based 序号（该槽取 steedSpawns[0]）
            host._spawnedIcon = spawned;
            hosts[0] = host;
            land._dynamicMapIcons = hosts;
            return land;
        }

        /// <summary>
        /// 真实 detail 模板的原生动态槽（见 BuildWorldSized 注释的实测盒）。anchor 中心 + pivot(.5,0)/ap(0,0)
        /// ⇒ 槽位盒 = root 中心 ±(w/2) 起、向上 h。host 带 Image（锚点占位）+ 被压制的 _spawnedIcon 子对象。
        /// </summary>
        private static UIDynamicMapIcon MakeDetailSlot(RectTransform root, string name,
            UIDynamicMapIcon.IconType type, int steedNum, Vector2 size)
        {
            RectTransform slot = MakeRect(name, root, size, Vector2.zero, new Vector2(0.5f, 0f));
            FixedAnchor(slot, root, new Vector2(0.5f, 0.5f), Vector2.zero);
            slot.gameObject.AddComponent<Image>();
            var spawnGo = new GameObject("Spawned " + name);
            RectTransform spawnRect = spawnGo.AddComponent<RectTransform>();
            spawnRect.pivot = new Vector2(0.5f, 0.5f);
            spawnRect.sizeDelta = size;
            spawnRect.SetParent(slot, false);
            spawnGo.AddComponent<Image>();
            UIMapIcon spawned = spawnGo.AddComponent<UIMapIcon>();
            UIDynamicMapIcon host = slot.gameObject.AddComponent<UIDynamicMapIcon>();
            host._type = type;
            host._steedNum = steedNum;
            host._spawnedIcon = spawned;
            return host;
        }

        /// <summary>
        /// exact native island sprite mesh（私有测试事实，取自 resources.assets Land Button Athena，
        /// `native-mesh-fixture.json`：rect 84×62、pivot (42,0)、pixelsPerUnit 32、10 顶点 / 8 三角形）。
        /// 只含网格/元数据，不含任何位图。
        /// </summary>
        private static readonly float[] AthenaMeshX =
            { 1.28125f, 1.15625f, 1.28125f, 1.0625f, 0.3125f, -0.28125f, -1.0625f, -1.125f, -1.3125f, -1.3125f };
        private static readonly float[] AthenaMeshY =
            { 1.25f, 0.25f, 0.4375f, 1.75f, 0.03125f, 1.75f, 1.5625f, 0.03125f, 1.3125f, 0.375f };
        private static readonly ushort[] AthenaMeshIndices =
            { 9, 8, 7, 6, 7, 8, 4, 7, 6, 5, 4, 6, 3, 4, 5, 1, 4, 3, 0, 1, 3, 2, 1, 0 };

        /// <summary>native 岛图 sprite 工厂（默认 = 真实 Athena mesh；反例场景可换成 diamond/空 mesh 变体）。</summary>
        private static Func<Sprite> NativeIslandSpriteFactory = () => MakeNativeIslandSprite("athena_tholos_greece");

        /// <summary>反例开关：overview/detail 的 native art preserveAspect（默认 false = 与实测 art rect 落点一致）。</summary>
        private static bool? FixturePreserveAspectOverride;

        /// <summary>反例开关：给 native art 设 overrideSprite（uGUI activeSprite = override ?? sprite）。</summary>
        private static Func<Sprite> NativeArtOverrideSpriteFactory;

        /// <summary>反例开关：native art Image 的 type（Simple 之外 → 必须整体保留 native）。</summary>
        private static Image.Type? FixtureImageTypeOverride;

        /// <summary>反例开关：useSpriteMesh=false（quad 路径，无法证明岛形 → 整体保留 native）。</summary>
        private static bool? FixtureUseSpriteMeshOverride;

        /// <summary>
        /// 反例用 concave 岛形（diamond 内接于 84×62 sprite rect；pivot (42,0)/ppu 32）：
        /// bbox 四角是海 ⇒ 只按 art bbox 规划会接受"中心在海上"的放置（reviewer P1-1 反例）。
        /// </summary>
        private static Sprite MakeDiamondIslandSprite()
        {
            var vertices = new Il2CppStructArray<Vector2>(4);
            vertices[0] = new Vector2(-42f / 32f, 31f / 32f);
            vertices[1] = new Vector2(0f, 62f / 32f);
            vertices[2] = new Vector2(42f / 32f, 31f / 32f);
            vertices[3] = new Vector2(0f, 0f);
            var triangles = new Il2CppStructArray<ushort>(6);
            triangles[0] = 0; triangles[1] = 1; triangles[2] = 2;
            triangles[3] = 0; triangles[4] = 2; triangles[5] = 3;
            return new Sprite
            {
                name = "counterexample_diamond_island",
                rect = new Rect(0f, 0f, 84f, 62f),
                pivot = new Vector2(42f, 0f),
                pixelsPerUnit = 32f,
                vertices = vertices,
                triangles = triangles,
                bounds = MeshBoundsOf(new float[] { -42f / 32f, 0f, 42f / 32f, 0f },
                    new float[] { 31f / 32f, 62f / 32f, 31f / 32f, 0f }),
            };
        }

        /// <summary>
        /// 反例用 half 岛形（mesh 只覆盖 sprite rect 右半，pivot (42,0)/ppu 32）：
        /// 形内占满后 bbox 只剩形外左半 ⇒ 只按 art bbox 规划仍会在"海"上放置（P1-1 反例的第二形态）。
        /// </summary>
        private static Sprite MakeHalfIslandSprite()
        {
            var vertices = new Il2CppStructArray<Vector2>(4);
            vertices[0] = new Vector2(0f, 0f);
            vertices[1] = new Vector2(0f, 62f / 32f);
            vertices[2] = new Vector2(42f / 32f, 62f / 32f);
            vertices[3] = new Vector2(42f / 32f, 0f);
            var triangles = new Il2CppStructArray<ushort>(6);
            triangles[0] = 0; triangles[1] = 1; triangles[2] = 2;
            triangles[3] = 0; triangles[4] = 2; triangles[5] = 3;
            return new Sprite
            {
                name = "counterexample_half_island",
                rect = new Rect(0f, 0f, 84f, 62f),
                pivot = new Vector2(42f, 0f),
                pixelsPerUnit = 32f,
                vertices = vertices,
                triangles = triangles,
                bounds = MeshBoundsOf(new float[] { 0f, 0f, 42f / 32f, 42f / 32f },
                    new float[] { 0f, 62f / 32f, 62f / 32f, 0f }),
            };
        }

        /// <summary>空 mesh 变体（未知几何 → 必须整体保留 native，不得退回矩形面）。</summary>
        private static Sprite MakeMeshesUnavailableSprite()
        {
            Sprite sprite = MakeNativeIslandSprite("athena_tholos_greece");
            sprite.vertices = new Il2CppStructArray<Vector2>(0);
            return sprite;
        }

        /// <summary>真实字段：Sprite.bounds = mesh 包围盒（sprite 单位，pivot 相对；不是 rect/ppu 假设）。</summary>
        private static Bounds MeshBoundsOf(float[] vx, float[] vy)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < vx.Length; i++)
            {
                if (vx[i] < minX) minX = vx[i];
                if (vx[i] > maxX) maxX = vx[i];
                if (vy[i] < minY) minY = vy[i];
                if (vy[i] > maxY) maxY = vy[i];
            }
            return new Bounds(
                new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f),
                new Vector3(maxX - minX, maxY - minY, 0f));
        }

        private static Sprite MakeNativeIslandSprite(string name)
        {
            var vertices = new Il2CppStructArray<Vector2>(AthenaMeshX.Length);
            for (int i = 0; i < AthenaMeshX.Length; i++)
            {
                vertices[i] = new Vector2(AthenaMeshX[i], AthenaMeshY[i]);
            }
            var triangles = new Il2CppStructArray<ushort>(AthenaMeshIndices.Length);
            for (int i = 0; i < AthenaMeshIndices.Length; i++) triangles[i] = AthenaMeshIndices[i];
            return new Sprite
            {
                name = name,
                rect = new Rect(0f, 0f, 84f, 62f),
                pivot = new Vector2(42f, 0f),
                pixelsPerUnit = 32f,
                vertices = vertices,
                triangles = triangles,
                bounds = MeshBoundsOf(AthenaMeshX, AthenaMeshY),
            };
        }

        /// <summary>
        /// test 侧独立（不依赖候选新增 API）的 native mesh → 规划空间映射，按 primary source
        /// uGUI `Image.cs`（Simple+useSpriteMesh）：drawingSize = GetPixelAdjustedRect 尺寸经
        /// preserveAspect **按 RT.pivot** 缩框；顶点 art-local = v/Sprite.bounds.size*drawingSize
        /// - (rectPivot - spritePivot/Sprite.rect.size)*drawingSize；再经真实 RectTransform 链到规划空间。
        /// </summary>
        private struct ProbeMeshMap
        {
            internal float OriginX, OriginY;      // art rect 在规划空间的左下角（LocalBox 约定，已减 paper rect 原点）
            internal float ScaleX, ScaleY;        // 规划单位 / art-local 单位（含 localScale）
            internal float RectPivotX, RectPivotY;
            internal float ArtW, ArtH;            // art rect 尺寸（art-local）
            internal float BoundsW, BoundsH;      // Sprite.bounds.size
            internal float DrawingW, DrawingH;    // 缩框后的绘制尺寸
            internal float OffsetX, OffsetY;      // (rectPivot - spritePivotNorm) * drawingSize

            internal bool Valid => ScaleX > 0f && ScaleY > 0f && ArtW > 0f && ArtH > 0f &&
                                   BoundsW > 0f && BoundsH > 0f && DrawingW > 0f && DrawingH > 0f;

            internal float LocalX(float planX) => (planX - OriginX) / ScaleX - RectPivotX * ArtW;
            internal float LocalY(float planY) => (planY - OriginY) / ScaleY - RectPivotY * ArtH;
            internal float UnitX(float planX) => (LocalX(planX) + OffsetX) * BoundsW / DrawingW;
            internal float UnitY(float planY) => (LocalY(planY) + OffsetY) * BoundsH / DrawingH;
            internal float PlanXFromLocal(float localX) => OriginX + (localX + RectPivotX * ArtW) * ScaleX;
            internal float PlanYFromLocal(float localY) => OriginY + (localY + RectPivotY * ArtH) * ScaleY;
            // sprite 单位（Sprite.vertices 坐标系）→ 规划空间（uGUI 公式正向）
            internal float PlanXFromUnitX(float vx) => PlanXFromLocal(vx / BoundsW * DrawingW - OffsetX);
            internal float PlanYFromUnitY(float vy) => PlanYFromLocal(vy / BoundsH * DrawingH - OffsetY);
        }

        /// <summary>规划空间 → art-local 的线性映射（art rect 轴对齐；Scale 含 localScale）。</summary>
        private static ProbeMeshMap ProbeMeshMapOf(RectTransform art, RectTransform space, out MapIconBox drawn)
        {
            drawn = default;
            Image image = art != null ? art.GetComponent<Image>() : null;
            Sprite sprite = image != null ? (image.overrideSprite != null ? image.overrideSprite : image.sprite) : null;
            if (image == null || sprite == null) return default;
            Rect artRect = art.rect;
            if (!(artRect.width > 0f) || !(artRect.height > 0f)) return default;
            MapIconBox planRect = LocalBox(space, art);   // art rect 在规划空间的落点（含 localScale）
            if (planRect.Width <= 0f || planRect.Height <= 0f) return default;

            Rect spriteRect = sprite.rect;
            Vector3 bounds = sprite.bounds.size;
            Vector2 spritePivot = sprite.pivot;
            if (!(spriteRect.width > 0f) || !(spriteRect.height > 0f)) return default;
            if (!(bounds.x > 0f) || !(bounds.y > 0f)) return default;

            // mesh 包围盒（sprite 单位）——drawn 必须按真实顶点范围，而不是 sprite pivot。
            Il2CppStructArray<Vector2> verts = sprite.vertices;
            if (verts == null || verts.Length < 3) return default;
            float vMinX = float.MaxValue, vMinY = float.MaxValue, vMaxX = float.MinValue, vMaxY = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector2 v = verts[i];
                if (v.x < vMinX) vMinX = v.x;
                if (v.x > vMaxX) vMaxX = v.x;
                if (v.y < vMinY) vMinY = v.y;
                if (v.y > vMaxY) vMaxY = v.y;
            }
            Rect adjusted = image.GetPixelAdjustedRect();
            float drawingW = adjusted.width > 0f ? adjusted.width : artRect.width;
            float drawingH = adjusted.height > 0f ? adjusted.height : artRect.height;
            if (image.preserveAspect)
            {
                float spriteRatio = spriteRect.width / spriteRect.height;
                float rectRatio = drawingW / drawingH;
                if (spriteRatio > rectRatio) drawingH = drawingW * (1f / spriteRatio);
                else drawingW = drawingH * spriteRatio;
            }
            Vector2 rtPivot = art.pivot;
            var map = new ProbeMeshMap
            {
                OriginX = planRect.X0, OriginY = planRect.Y0,
                ScaleX = planRect.Width / artRect.width, ScaleY = planRect.Height / artRect.height,
                RectPivotX = rtPivot.x, RectPivotY = rtPivot.y,
                ArtW = artRect.width, ArtH = artRect.height,
                BoundsW = bounds.x, BoundsH = bounds.y,
                DrawingW = drawingW, DrawingH = drawingH,
                OffsetX = (rtPivot.x - spritePivot.x / spriteRect.width) * drawingW,
                OffsetY = (rtPivot.y - spritePivot.y / spriteRect.height) * drawingH,
            };
            float lx0 = vMinX / bounds.x * drawingW - map.OffsetX;
            float lx1 = vMaxX / bounds.x * drawingW - map.OffsetX;
            float ly0 = vMinY / bounds.y * drawingH - map.OffsetY;
            float ly1 = vMaxY / bounds.y * drawingH - map.OffsetY;
            drawn = new MapIconBox(map.PlanXFromLocal(lx0), map.PlanYFromLocal(ly0),
                map.PlanXFromLocal(lx1), map.PlanYFromLocal(ly1));
            return map;
        }

        /// <summary>test 侧独立 mesh 点内测试（triangle union，含边界）。</summary>
        private static bool MeshPointInside(ProbeMeshMap map, float[] vx, float[] vy, ushort[] tris,
            float planX, float planY)
        {
            if (!map.Valid || vx == null || vy == null || tris == null) return false;
            float px = map.UnitX(planX), py = map.UnitY(planY);
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                if (a >= vx.Length || b >= vx.Length || c >= vx.Length) continue;
                float d1 = (px - vx[b]) * (vy[a] - vy[b]) - (vx[a] - vx[b]) * (py - vy[b]);
                float d2 = (px - vx[c]) * (vy[b] - vy[c]) - (vx[b] - vx[c]) * (py - vy[c]);
                float d3 = (px - vx[a]) * (vy[c] - vy[a]) - (vx[c] - vx[a]) * (py - vy[a]);
                bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
                bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
                if (!(hasNeg && hasPos)) return true;
            }
            return false;
        }

        /// <summary>test 侧独立"完整足迹在 mesh 内"：四角 + 中心在内，且没有 mesh 边穿过足迹内部（ε 内缩）。</summary>
        private static bool MeshBoxInside(ProbeMeshMap map, float[] vx, float[] vy, ushort[] tris,
            in MapIconBox box)
        {
            if (!map.Valid) return false;
            if (!MeshPointInside(map, vx, vy, tris, box.X0, box.Y0) ||
                !MeshPointInside(map, vx, vy, tris, box.X1, box.Y0) ||
                !MeshPointInside(map, vx, vy, tris, box.X1, box.Y1) ||
                !MeshPointInside(map, vx, vy, tris, box.X0, box.Y1) ||
                !MeshPointInside(map, vx, vy, tris, (box.X0 + box.X1) * 0.5f, (box.Y0 + box.Y1) * 0.5f))
            {
                return false;
            }
            float ex0 = map.UnitX(box.X0 + 0.02f), ey0 = map.UnitY(box.Y0 + 0.02f);
            float ex1 = map.UnitX(box.X1 - 0.02f), ey1 = map.UnitY(box.Y1 - 0.02f);
            if (ex1 <= ex0 || ey1 <= ey0) return false;
            // 只有边界边能分隔岛内外：三角化内部对角线（两三角形共享）穿越 footprint 不算越界。
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                if (a >= vx.Length || b >= vx.Length || c >= vx.Length) continue;
                if (ProbeBoundaryCrosses(vx, vy, tris, a, b, ex0, ey0, ex1, ey1) ||
                    ProbeBoundaryCrosses(vx, vy, tris, b, c, ex0, ey0, ex1, ey1) ||
                    ProbeBoundaryCrosses(vx, vy, tris, c, a, ex0, ey0, ex1, ey1))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>该三角边是边界边（无反向共享）且穿过盒内部 ⇒ true。</summary>
        private static bool ProbeBoundaryCrosses(float[] vx, float[] vy, ushort[] tris, int a, int b,
            float ex0, float ey0, float ex1, float ey1)
        {
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int x = tris[t], y = tris[t + 1], z = tris[t + 2];
                if (x >= vx.Length || y >= vx.Length || z >= vx.Length) continue;
                if ((x == b && y == a) || (y == b && z == a) || (z == b && x == a)) return false;
            }
            return ProbeSegmentCrossesBox(vx[a], vy[a], vx[b], vy[b], ex0, ey0, ex1, ey1);
        }

        private static bool ProbeSegmentCrossesBox(float x1, float y1, float x2, float y2,
            float bx0, float by0, float bx1, float by1)
        {
            float dx = x2 - x1, dy = y2 - y1;
            float t0 = 0f, t1 = 1f;
            if (!ProbeClipAxis(dx, bx0 - x1, bx1 - x1, ref t0, ref t1)) return false;
            if (!ProbeClipAxis(dy, by0 - y1, by1 - y1, ref t0, ref t1)) return false;
            return t1 > t0;
        }

        private static bool ProbeClipAxis(float d, float lo, float hi, ref float t0, ref float t1)
        {
            if (Math.Abs(d) < 1e-9f) return lo <= 0f && hi >= 0f;
            float ta = lo / d, tb = hi / d;
            if (ta > tb) { float swap = ta; ta = tb; tb = swap; }
            if (ta > t0) t0 = ta;
            if (tb < t1) t1 = tb;
            return t0 < t1;
        }

        private static UIMapIcon MakeSourceIcon(string tag, Vector2 size)
        {
            var go = new GameObject("SourceIcon " + tag);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            go.AddComponent<Image>();
            go.AddComponent<Text>();
            return go.AddComponent<UIMapIcon>();
        }

        private static CampaignSaveData.ReignInfo MakeReign(string tag, int landCount, int steedType,
            int iconsPerIsland)
        {
            var reign = new CampaignSaveData.ReignInfo { tag = tag };
            for (int i = 0; i <= landCount; i++)
            {
                var land = new CampaignSaveData.LandMapData();
                var steeds = new Il2CppStructArray<SteedType>(iconsPerIsland);
                for (int s = 0; s < iconsPerIsland; s++) steeds[s] = (SteedType)(steedType + s % 2);
                land.steedSpawns = steeds;
                land.hermit = new Il2CppStructArray<Hermit.HermitType>(0);
                land.statue = new Il2CppStructArray<Statue.Deity>(0);
                reign.landData.Add(land);
            }
            return reign;
        }

        private static void Tick(Fixture f, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                Time.frameCount = ++_frame;
                f.Greek.Update();
            }
        }

        // ------------------------------------------------------------------ helpers

        private static Transform ArtOf(UILand land)
        {
            Transform t = land.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);
                if (child != null && child.name.StartsWith("Land Button")) return child;
            }
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);
                if (child != null && child.name.StartsWith("Land Image")) return child;
            }
            return null;
        }

        private static RectTransform FindOutline(UILand land)
        {
            Transform t = land.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);
                if (child != null && child.name.StartsWith("Land Outline")) return child as RectTransform;
            }
            return null;
        }

        private static List<RectTransform> CollectIcons(RectTransform paper, int expectedChildren)
        {
            var result = new List<RectTransform>();
            RectTransform[] rects = paper.gameObject.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < rects.Length; i++)
            {
                if (rects[i] == null || rects[i].gameObject.name != "KEM_MapResourceIcons") continue;
                if (rects[i].childCount != expectedChildren) continue;
                for (int c = 0; c < rects[i].childCount; c++)
                {
                    if (rects[i].GetChild(c) is RectTransform child) result.Add(child);
                }
            }
            return result;
        }

        /// <summary>取 paper 下期望子节点数的 KEM_MapResourceIcons holder（无则 null）。</summary>
        private static RectTransform CollectHolder(RectTransform paper, int expectedChildren)
        {
            RectTransform[] rects = paper.gameObject.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < rects.Length; i++)
            {
                if (rects[i] != null && rects[i].gameObject.name == "KEM_MapResourceIcons" &&
                    rects[i].childCount == expectedChildren) return rects[i];
            }
            return null;
        }

        /// <summary>paper 下全部 KEM_MapResourceIcons holder（不限子数；归属断言用）。</summary>
        private static RectTransform[] CollectHolders(RectTransform paper)
        {
            RectTransform[] rects = paper.gameObject.GetComponentsInChildren<RectTransform>(true);
            var result = new List<RectTransform>(11);
            for (int i = 0; i < rects.Length; i++)
            {
                if (rects[i] != null && rects[i].gameObject.name == "KEM_MapResourceIcons") result.Add(rects[i]);
            }
            return result.ToArray();
        }

        /// <summary>模拟真实 menu.UpdateLands 对详情 lands 的逐岛 UpdateLand（探针桩 UpdateLands 为空）。</summary>
        private static void UpdateDetails(Fixture f)
        {
            var lands = f.Greek.lands;
            for (int i = 0; i < lands.Count; i++)
            {
                if (lands[i] != null) lands[i].UpdateLand(f.Current, i);
            }
        }

        private static int CountNamed(GameObject root, string name)
        {
            RectTransform[] rects = root.GetComponentsInChildren<RectTransform>(true);
            int count = 0;
            for (int i = 0; i < rects.Length; i++)
            {
                if (rects[i] != null && rects[i].gameObject.name == name) count++;
            }
            return count;
        }

        private static object ReadStatic(string name)
        {
            try
            {
                System.Reflection.FieldInfo field = typeof(MapMountIcons).GetField(name,
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                return field != null ? field.GetValue(null) : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>只改真实 RectMask2D 的尺寸（可见域变化 → 视口 signature 变化 / 可制造测量失败）。</summary>
        private static void SetVisible(Fixture f, float width, float height)
        {
            f.ContentArea.sizeDelta = new Vector2(width, height);
            f.LandsContainer.sizeDelta = new Vector2(width, height);
        }

        /// <summary>paper 本地 art 盒（与真实 TryLocalBox 同口径）。</summary>
        private static MapIconBox PaperArtBox(RectTransform paper, Transform art)
        {
            return LocalBox(paper, art as RectTransform);
        }

        private static MapIconBox LandLocalBox(RectTransform land, RectTransform target)
        {
            return LocalBox(land, target);
        }

        private static MapIconBox LocalBox(RectTransform space, RectTransform target)
        {
            Rect r = target.rect;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float x = (i & 1) == 0 ? r.xMin : r.xMax;
                float y = (i & 2) == 0 ? r.yMin : r.yMax;
                Vector3 local = space.InverseTransformPoint(target.TransformPoint(new Vector3(x, y, 0f)));
                minX = Math.Min(minX, local.x);
                maxX = Math.Max(maxX, local.x);
                minY = Math.Min(minY, local.y);
                maxY = Math.Max(maxY, local.y);
            }
            Rect sr = space.rect;
            return new MapIconBox(minX - sr.xMin, minY - sr.yMin, maxX - sr.xMin, maxY - sr.yMin);
        }

        /// <summary>画布像素矩形 → paper box（0.25/0.75 偏移保证 floor/ceil 覆盖 = [x0..x1]×[y0..y1]）。</summary>
        private static MapIconBox CanvasRectToPaper(in MapIconBox frame, int canvasW, int canvasH,
            int x0, int y0, int x1, int y1)
        {
            float sx = canvasW / frame.Width;
            float sy = canvasH / frame.Height;
            return new MapIconBox(
                frame.X0 + (x0 + 0.25f) / sx, frame.Y0 + (y0 + 0.25f) / sy,
                frame.X0 + (x1 + 0.75f) / sx, frame.Y0 + (y1 + 0.75f) / sy);
        }

        /// <summary>判别性负例（实际 runtime mask 实例）：固定崖面矩形 alpha=true 而顶面=false ——
        /// alpha 对照会放下、运行期顶面 mask 必须整体拒绝（证明不是仅私有图假改）。</summary>
        private static void CliffNegativeOracle(in MapIconBox shoreFrame, MapExtensionIslandArt.ShorePrep prep,
            MapShoreMask runtimeMask, string tag)
        {
            if (runtimeMask == null || prep == null || prep.PlacementMask == null) return;
            int cx0 = 65, cy0 = 34, cx1 = 76, cy1 = 39;
            var alphaMask = new MapShoreMask(prep.Width, prep.Height, prep.Mask);
            Check(prep.Mask[cy0 * prep.Width + cx0] && !prep.PlacementMask[cy0 * prep.Width + cx0],
                tag + "/cliff-sample-alpha-not-placement");
            MapIconBox cliff = CanvasRectToPaper(shoreFrame, prep.Width, prep.Height, cx0, cy0, cx1, cy1);
            Check(FootprintInside(cliff, shoreFrame, alphaMask), tag + "/cliff-alpha-inside");
            Check(!FootprintInside(cliff, shoreFrame, runtimeMask), tag + "/cliff-placement-rejected");
            var probe = new List<MapIconRequest>
            {
                new MapIconRequest(MapIconKind.Steed, 199, 0, 14f, 7f),
            };
            var alphaOut = new List<MapIconPlacement>();
            bool alphaOk = MapExtensionIslandLayout.TryPlan(cliff, probe, new List<MapIconBox>(), shoreFrame,
                alphaMask, alphaOut, out _, out _);
            var liveOut = new List<MapIconPlacement>();
            bool liveOk = MapExtensionIslandLayout.TryPlan(cliff, probe, new List<MapIconBox>(), shoreFrame,
                runtimeMask, liveOut, out _, out int liveFailed);
            Check(alphaOk && alphaOut.Count == 1, tag + "/cliff-alpha-control n=" + alphaOut.Count);
            Check(!liveOk && liveOut.Count == 0 && liveFailed == 1,
                tag + "/cliff-runtime-rejected n=" + liveOut.Count + " failed=" + liveFailed);
        }

        /// <summary>实际 runtime 图标盒的自由错落诊断（paper UI；容差 1.5 UI；与公开 pure 同口径）。</summary>
        private static void ScatterBoxes(List<MapIconBox> boxes, in MapIconBox frame, string tag)
        {
            const float bandTol = 1.5f;
            const float gap = 12f;
            var centers = new List<float>(boxes.Count);
            var xs = new List<float>(boxes.Count);
            foreach (MapIconBox box in boxes)
            {
                centers.Add((box.Y0 + box.Y1) * 0.5f);
                xs.Add((box.X0 + box.X1) * 0.5f);
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
            for (int i = 0; i < boxes.Count; i++)
            {
                if (xs[i] > frame.X0 + frame.Width * 0.5f) rightHalf++;
            }
            var order = new List<int>();
            for (int i = 0; i < boxes.Count; i++) order.Add(i);
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
            Check(rightHalf >= 6, "scatter-" + tag + "/right-half n=" + rightHalf);
            Check(maxBand <= 4, "scatter-" + tag + "/max-band n=" + maxBand);
            Check(longest <= 3, "scatter-" + tag + "/longest-chain n=" + longest);
            Check(bands >= 8, "scatter-" + tag + "/y-bands n=" + bands);
            Check(yMax - yMin >= 20f, "scatter-" + tag + "/y-range r=" + (yMax - yMin).ToString("0.#"));
        }

        /// <summary>实际 runtime 图标的自由错落诊断（从 paper art 盒取）。</summary>
        private static void ScatterDiagnostics(List<RectTransform> icons, RectTransform paper,
            in MapIconBox frame, string tag)
        {
            var boxes = new List<MapIconBox>(icons.Count);
            for (int i = 0; i < icons.Count; i++) boxes.Add(PaperArtBox(paper, icons[i]));
            ScatterBoxes(boxes, frame, tag);
        }

        /// <summary>与生产同一终检（含 1e-4 px 像素边界对齐；曾有的本地无 epsilon 副本会误判量化盒）。</summary>
        private static bool FootprintInside(MapIconBox box, in MapIconBox shoreFrame, MapShoreMask mask)
            => MapExtensionIslandLayout.FootprintInsideShore(box, shoreFrame, mask);

        private static string Fmt(in MapIconBox box)
            => "(" + box.X0.ToString("0.#") + "," + box.Y0.ToString("0.#") + "," +
               box.X1.ToString("0.#") + "," + box.Y1.ToString("0.#") + ")";
    }
}
