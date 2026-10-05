// 新岸线接线 runtime probe：直链编译真实 src/MapMountIcons.cs + src/MapExtensionIslandArt.cs +
// src/MapResourceIconPlan.cs（不修改、不复制方法体），由真实 patch 入口（UpdateLand/MenuTick/ClearLands/
// OnDisable）驱动控制流；Unity/Il2Cpp 桩来自 oldW R3 entry-probe（原样复制）并按 art 管线（PNG→Sprite）扩展。
//
// 场景（issue-156 后）：
//  S1–S12：扩展/共享几何回归（world/detail shore、owner token、commit barrier、viewport、release queue…）。
//  S13 native resource passthrough：原版十岛零接管（冷开/提交/期满 reign/OFF/state1/换 campaign；
//      原生状态逐值保持、holder=0、压制=0、source resolve/repaint=0、原生 art 读取=0；原十簇只允许统一 fit）。
//  S14 return boundaries：统一归还处只归还已捕获扩展责任；legacy/native 账目只销毁 holder+清账，
//      绝不按旧 OriginalActive 改写原生当次 activeSelf；registry 清后 captured 责任仍归还。
//  S15 extension boundary + refresh parity：未登记 extra/伪 incoming 不接管、未访问 11 不揭露、
//      OFF/locked 边界；focused/current reign 的 native refresh 与 16 项 Kind/TypeId 身份准确。
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
                ScenarioS13NativePassthrough();
                ScenarioS14ReturnBoundaries();
                ScenarioS15ExtensionBoundary();
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

            // native 0..9（issue-156 边界，替换 issue-152 岛内契约）：原版 detail 的 steed 条目完全由原生显示，
            // Mod 不建 holder、不动槽位显隐 —— 旧「槽心居中/岛形重排」接受条件由 S13/S15 的原生零接管断言接替。
            UILand nativeDetail = f.Greek.lands[0];
            RectTransform nativeRect = nativeDetail.gameObject.GetComponent<RectTransform>();
            Check(CountNamed(nativeRect.gameObject, "KEM_MapResourceIcons") == 0,
                "S11/native-detail-no-root given=" + CountNamed(nativeRect.gameObject, "KEM_MapResourceIcons"));
            Check(!ViewsContainsLand(nativeDetail, false), "S11/native-detail-no-view");
            GameObject nativeSpawn = nativeDetail._dynamicMapIcons[0]._spawnedIcon.gameObject;
            Check(nativeSpawn.activeSelf, "S11/native-slot-visible");
            Check(nativeSpawn.SetActiveCalls == 0, "S11/native-slot-untouched calls=" + nativeSpawn.SetActiveCalls);
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
        // ================================================================== issue-156 native passthrough / boundaries

        /// <summary>原生侧可观察状态快照（整棵子树逐节点）：activeSelf / SetActive 次数 / rect 字段 /
        /// Image.sprite 身份。用于对照「Mod 回调后原生资源状态逐值保持」。</summary>
        private sealed class NativeNodeState
        {
            internal GameObject Node;
            internal string Name;
            internal bool RootGeometrySkipped;   // 原十簇根几何属于授权统一变换，不逐值比对
            internal bool Active;
            internal int SetActiveCalls;
            internal Vector2 SizeDelta;
            internal Vector2 AnchoredPosition;
            internal Vector3 LocalScale;
            internal bool HasImage;
            internal Sprite Sprite;
        }

        private static void CaptureNativeNode(RectTransform node, List<NativeNodeState> into, bool skipGeometry)
        {
            if (node == null) return;
            Image image = node.gameObject.GetComponent<Image>();
            into.Add(new NativeNodeState
            {
                Node = node.gameObject,
                Name = node.gameObject.name,
                RootGeometrySkipped = skipGeometry,
                Active = node.gameObject.activeSelf,
                SetActiveCalls = node.gameObject.SetActiveCalls,
                SizeDelta = node.sizeDelta,
                AnchoredPosition = node.anchoredPosition,
                LocalScale = node.localScale,
                HasImage = image != null,
                Sprite = image != null ? image.sprite : null,
            });
            for (int i = 0; i < node.childCount; i++)
            {
                if (node.GetChild(i) is RectTransform child) CaptureNativeNode(child, into, false);
            }
        }

        private static List<NativeNodeState> CaptureNativeState(RectTransform root, bool skipRootGeometry)
        {
            var list = new List<NativeNodeState>(32);
            if (root == null) return list;
            CaptureNativeNode(root, list, skipRootGeometry);
            return list;
        }

        private static List<List<NativeNodeState>> CaptureNativesDetail(Fixture f)
        {
            var result = new List<List<NativeNodeState>>(10);
            for (int i = 0; i < 10; i++)
            {
                result.Add(CaptureNativeState(f.Greek.lands[i].gameObject.GetComponent<RectTransform>(), false));
            }
            return result;
        }

        private static List<List<NativeNodeState>> CaptureNativesCluster(Fixture f)
        {
            var result = new List<List<NativeNodeState>>(10);
            for (int i = 0; i < 10; i++)
            {
                // 原十簇根的位置/缩放由 MapOverviewLayout 统一 fit 授权变更；子节点必须逐值保持。
                result.Add(CaptureNativeState(f.Lands[i].gameObject.GetComponent<RectTransform>(), true));
            }
            return result;
        }

        private static bool NativeStateEquals(List<NativeNodeState> before, List<NativeNodeState> after)
        {
            if (before.Count != after.Count) return false;
            for (int i = 0; i < before.Count; i++)
            {
                if (before[i].Node != after[i].Node) return false;
                if (before[i].Active != after[i].Active) return false;
                if (before[i].SetActiveCalls != after[i].SetActiveCalls) return false;
                if (before[i].HasImage != after[i].HasImage) return false;
                if (before[i].HasImage && !ReferenceEquals(before[i].Sprite, after[i].Sprite)) return false;
                if (before[i].RootGeometrySkipped) continue;
                if (before[i].SizeDelta.x != after[i].SizeDelta.x ||
                    before[i].SizeDelta.y != after[i].SizeDelta.y) return false;
                if (before[i].AnchoredPosition.x != after[i].AnchoredPosition.x ||
                    before[i].AnchoredPosition.y != after[i].AnchoredPosition.y) return false;
                if (before[i].LocalScale.x != after[i].LocalScale.x ||
                    before[i].LocalScale.y != after[i].LocalScale.y ||
                    before[i].LocalScale.z != after[i].LocalScale.z) return false;
            }
            return true;
        }

        /// <summary>原生 art Image 字段读取总量（只读计数字段，不触发任何 getter）——Mod 不得读原生 art。</summary>
        private static int NativeArtReadsTotal(Fixture f)
        {
            int total = 0;
            for (int i = 0; i < 10; i++)
            {
                total += SubtreeImageReads(f.Greek.lands[i].gameObject.GetComponent<RectTransform>());
                total += SubtreeImageReads(f.Lands[i].gameObject.GetComponent<RectTransform>());
            }
            return total;
        }

        private static int SubtreeImageReads(RectTransform root)
        {
            int total = 0;
            var stack = new List<RectTransform>(8);
            if (root != null) stack.Add(root);
            while (stack.Count > 0)
            {
                RectTransform node = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                Image image = node.gameObject.GetComponent<Image>();
                if (image != null) total += image.ReadCalls;
                for (int i = 0; i < node.childCount; i++)
                {
                    if (node.GetChild(i) is RectTransform child) stack.Add(child);
                }
            }
            return total;
        }

        private static List<MapMountIconView> ViewsList() => (List<MapMountIconView>)ReadStatic("Views");

        private static bool ViewsContainsLand(UILand land, bool overview)
        {
            List<MapMountIconView> views = ViewsList();
            for (int i = 0; i < views.Count; i++)
            {
                MapMountIconView view = views[i];
                if (view != null && view.Land == land && view.Overview == overview) return true;
            }
            return false;
        }

        private static void AssertNativesUnchanged(Fixture f, List<List<NativeNodeState>> detailBefore,
            List<List<NativeNodeState>> clusterBefore, string tag)
        {
            for (int i = 0; i < 10; i++)
            {
                RectTransform detail = f.Greek.lands[i].gameObject.GetComponent<RectTransform>();
                RectTransform cluster = f.Lands[i].gameObject.GetComponent<RectTransform>();
                Check(CountNamed(detail.gameObject, "KEM_MapResourceIcons") == 0, tag + "/detail-holder-" + i);
                Check(CountNamed(cluster.gameObject, "KEM_MapResourceIcons") == 0, tag + "/cluster-holder-" + i);
                Check(!ViewsContainsLand(f.Greek.lands[i], false), tag + "/detail-view-" + i);
                Check(!ViewsContainsLand(f.Lands[i], true), tag + "/cluster-view-" + i);
                Check(NativeStateEquals(detailBefore[i], CaptureNativeState(detail, false)),
                    tag + "/detail-state-" + i);
                Check(NativeStateEquals(clusterBefore[i], CaptureNativeState(cluster, true)),
                    tag + "/cluster-state-" + i);
            }
        }

        /// <summary>原十簇统一变换（共同 cluster 空间）：s 一致且两两差值 = s × 原始差值；未提交时保持原值。</summary>
        private static bool NativeFitUniform(Fixture f, out float scale, out string note)
        {
            scale = 0f;
            note = "ok";
            bool applied = ReadStatic("_overviewApplied") is bool appliedFlag && appliedFlag;
            var rects = new List<RectTransform>(10);
            for (int i = 0; i < 10; i++) rects.Add(f.Lands[i].gameObject.GetComponent<RectTransform>());
            if (!applied)
            {
                for (int i = 0; i < 10; i++)
                {
                    if (rects[i].anchoredPosition.x != f.OriginalClusterPositions[i].x ||
                        rects[i].anchoredPosition.y != f.OriginalClusterPositions[i].y)
                    {
                        note = "not-applied-but-moved-" + i;
                        return false;
                    }
                }
                note = "not-applied";
                return true;
            }
            bool have = false;
            for (int i = 0; i < 10; i++)
            {
                for (int j = i + 1; j < 10; j++)
                {
                    float dxO = f.OriginalClusterPositions[i].x - f.OriginalClusterPositions[j].x;
                    float dyO = f.OriginalClusterPositions[i].y - f.OriginalClusterPositions[j].y;
                    float dxT = rects[i].anchoredPosition.x - rects[j].anchoredPosition.x;
                    float dyT = rects[i].anchoredPosition.y - rects[j].anchoredPosition.y;
                    if (Math.Abs(dxO) < 4f && Math.Abs(dyO) < 4f) continue;
                    float candidate = Math.Abs(dxO) >= 4f ? dxT / dxO : dyT / dyO;
                    if (!have) { scale = candidate; have = true; }
                    if (Math.Abs(candidate - scale) > 1e-3f ||
                        Math.Abs(dxT - scale * dxO) > 0.05f || Math.Abs(dyT - scale * dyO) > 0.05f)
                    {
                        note = "pair-" + i + "-" + j + " s=" + candidate + " want=" + scale;
                        return false;
                    }
                }
            }
            if (!have) { note = "no-distinct-pairs"; return false; }
            if (!(scale > 0f) || scale > MapOverviewLayout.MaxUniformScale + 0.001f)
            {
                note = "scale-range " + scale;
                return false;
            }
            for (int i = 0; i < 10; i++)
            {
                float got = rects[i].localScale.x;
                float want = f.OriginalClusterScales[i] * scale;
                if (Math.Abs(got - want) > 1e-3f)
                {
                    note = "scale-" + i + " got=" + got + " want=" + want;
                    return false;
                }
            }
            return true;
        }

        /// <summary>注入 legacy（旧版本遗留/native）view：模拟既有账目，仅用于验证统一归还处的有界账务回收。
        /// 只写测试侧可观察字段，不引入任何新入口授权。</summary>
        private static MapMountIconView InjectLegacyView(UILand land, bool overview, GameObject holder,
            GameObject suppressedTarget, bool originalActive)
        {
            var view = new MapMountIconView { Land = land, Overview = overview, LandIndex = 0, Holder = holder };
            if (suppressedTarget != null)
            {
                view.Suppressed.Add(new SuppressedIcon
                {
                    Target = suppressedTarget,
                    OriginalActive = originalActive,
                });
            }
            ViewsList().Add(view);
            return view;
        }

        private static void RemoveInjectedView(MapMountIconView view) => ViewsList().Remove(view);

        private static GameObject MakeHolder(RectTransform parent)
        {
            var go = new GameObject("KEM_MapResourceIcons");
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            return go;
        }

        /// <summary>扩展资源图标只存在于扩展 banner 内：全部 KEM holder 子图标数 = expected 且都在 banner 盒内。</summary>
        private static bool ExtensionIconsInsideBanner(Fixture f, int expected, string tag)
        {
            RectTransform[] holders = CollectHolders(f.Paper);
            MapIconBox banner = PaperArtBox(f.Paper, ArtOf(f.Extension));
            int total = 0;
            bool inside = banner.Width > 1f;
            for (int h = 0; h < holders.Length; h++)
            {
                for (int c = 0; c < holders[h].childCount; c++)
                {
                    RectTransform icon = holders[h].GetChild(c) as RectTransform;
                    if (icon == null) continue;
                    total++;
                    if (!Inside(PaperArtBox(f.Paper, icon), banner, 0.5f)) inside = false;
                }
            }
            Check(total == expected, tag + "/extension-icons n=" + total);
            Check(inside, tag + "/extension-icons-inside-banner");
            return total == expected && inside;
        }

        /// <summary>未登记 extra detail（owned menu 成员、登记桥拒绝，与真实模板同构）。</summary>
        private static UILand MakeUnregisteredDetail(Fixture f, string name)
        {
            var land = new GameObject(name).AddComponent<UILand>();
            RectTransform rect = land.gameObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(180f, 150f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.SetParent(f.Holder, false);
            RectTransform art = MakeRect("Land Image", rect, new Vector2(114f, 82f), new Vector2(0f, 0f),
                new Vector2(0f, 0f));
            FixedAnchor(art, rect, new Vector2(0f, 0f), new Vector2(25f, 30f));
            art.gameObject.AddComponent<Image>().sprite = MakeNativeIslandSprite("unregistered_extra_island");
            var hosts = new Il2CppReferenceArray<UIDynamicMapIcon>(3);
            hosts[0] = MakeDetailSlot(rect, "Steed1", UIDynamicMapIcon.IconType.Steed, 1, new Vector2(38f, 26f));
            hosts[1] = MakeDetailSlot(rect, "Hermit", UIDynamicMapIcon.IconType.Hermit, 1, new Vector2(22f, 20f));
            hosts[2] = MakeDetailSlot(rect, "Statue", UIDynamicMapIcon.IconType.Statue, 1, new Vector2(20f, 36f));
            land._dynamicMapIcons = hosts;
            return land;
        }

        /// <summary>
        /// S13（issue-156 红→绿核心反例）：原版十岛资源零接管。
        /// 覆盖 detail/overview、冷开与提交后、非空/空集/entry null、当次/历史/越界/null reign、
        /// ON/OFF、state0/1、关菜单与换 campaign；断言原生状态逐值保持、holder=0、压制=0、
        /// source resolve/repaint=0、原生 art 读取=0；原十簇只允许共同空间的统一 fit。
        /// </summary>
        private static void ScenarioS13NativePassthrough()
        {
            Console.WriteLine("== S13 native resource passthrough (issue-156) ==");
            int[] types = { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };

            // (a) 只有原版十岛（无登记扩展）：全流程零资源活动（resolve/repaint/holder/view 全 0）
            Fixture n0 = BuildWorld(withExtension: false, steedCount: 16, types: types);
            var n0Detail = CaptureNativesDetail(n0);
            var n0Cluster = CaptureNativesCluster(n0);
            int n0Reads = NativeArtReadsTotal(n0);
            UpdateDetails(n0);
            n0.Map.UpdateLandIcons(n0.Current);
            Tick(n0, 3);
            Check(MapIconSources.ResolveCalls == 0, "S13/native-only-resolve=" + MapIconSources.ResolveCalls);
            Check(ProbeHooks.RepaintCalls == 0, "S13/native-only-repaint=" + ProbeHooks.RepaintCalls);
            Check(CollectHolders(n0.Paper).Length == 0 && ViewsList().Count == 0, "S13/native-only-no-roots");
            Check(NativeArtReadsTotal(n0) == n0Reads, "S13/native-only-art-reads");
            AssertNativesUnchanged(n0, n0Detail, n0Cluster, "S13/native-only");
            Check(ReadStatic("_overviewApplied") is bool n0Applied && n0Applied,
                "S13/native-only-shared-geometry-committed");
            Check(NativeFitUniform(n0, out float n0Scale, out string n0Note), "S13/native-only-fit " + n0Note);
            Check(n0Scale > 0.3f, "S13/native-only-fit-scale=" + n0Scale);
            n0.Greek.OnDisable();

            // (b) 登记扩展存在：原版十岛仍然零接管（混合 非空/空集/entry=null + 原生当次显隐）
            Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: types);
            f.Current.landData[0].steedSpawns = Steeds(35);
            f.Current.landData[0].hermit = Hermits(0);
            f.Current.landData[0].statue = Statues(2);
            f.Current.landData[1].steedSpawns = new Il2CppStructArray<SteedType>(0);
            f.Current.landData[1].hermit = new Il2CppStructArray<Hermit.HermitType>(0);
            f.Current.landData[1].statue = new Il2CppStructArray<Statue.Deity>(0);
            f.Current.landData[2] = null;

            // cold opening：真实回调先于任何 tick/commit
            var detailBefore = CaptureNativesDetail(f);
            var clusterBefore = CaptureNativesCluster(f);
            UpdateDetails(f);
            AssertNativesUnchanged(f, detailBefore, clusterBefore, "S13/opening");
            Check(CollectHolders(f.Paper).Length == 0, "S13/opening-no-paper-root");

            // 原生当次写入（模拟原生 UpdateLand 的显隐结果；Mod 零写入必须保持这些值）
            GameObject slotHidden = f.Lands[0]._dynamicMapIcons[0]._spawnedIcon.gameObject;
            slotHidden.SetActive(false);
            GameObject slotVisible = f.Lands[3]._dynamicMapIcons[0]._spawnedIcon.gameObject;
            slotVisible.SetActive(false);
            slotVisible.SetActive(true);
            GameObject detailHidden = f.Greek.lands[0]._dynamicMapIcons[0]._spawnedIcon.gameObject;
            detailHidden.SetActive(false);

            detailBefore = CaptureNativesDetail(f);
            clusterBefore = CaptureNativesCluster(f);
            int readsBefore = NativeArtReadsTotal(f);
            int resolveBefore = MapIconSources.ResolveCalls;
            int repaintBefore = ProbeHooks.RepaintCalls;

            // 原生专属回调窗口：只驱动 0..9（扩展不参与）→ 任何 Mod 资源活动都是回归
            for (int i = 0; i < 10; i++) f.Greek.lands[i].UpdateLand(f.Current, i);
            for (int i = 0; i < 10; i++) f.Lands[i].UpdateLand(f.Current, i);
            Check(MapIconSources.ResolveCalls == resolveBefore, "S13/native-window-resolve");
            Check(ProbeHooks.RepaintCalls == repaintBefore, "S13/native-window-repaint");
            Check(NativeArtReadsTotal(f) == readsBefore, "S13/native-window-art-reads");
            AssertNativesUnchanged(f, detailBefore, clusterBefore, "S13/native-window");
            Check(!slotHidden.activeSelf && slotVisible.activeSelf && !detailHidden.activeSelf,
                "S13/native-window-active-kept");

            // OnClusterEnabled：native cluster 不触碰任何 view/holder
            f.Lands[1].gameObject.GetComponent<UIMainMapLand>().OnEnable();
            AssertNativesUnchanged(f, detailBefore, clusterBefore, "S13/cluster-enable");

            // 几何提交 + native refresh：共享几何照常（扩展生效），原十岛仍零接管
            detailBefore = CaptureNativesDetail(f);
            clusterBefore = CaptureNativesCluster(f);
            readsBefore = NativeArtReadsTotal(f);
            Tick(f, 2);
            f.Map.UpdateLandIcons(f.Current);
            Check(NativeArtReadsTotal(f) == readsBefore, "S13/post-commit-art-reads");
            AssertNativesUnchanged(f, detailBefore, clusterBefore, "S13/post-commit");
            Check(ReadStatic("_overviewApplied") is bool applied && applied, "S13/post-commit-applied");
            Check(NativeFitUniform(f, out float fitScale, out string fitNote), "S13/uniform-cluster-space " + fitNote);
            Check(fitScale > 0.3f && fitScale <= MapOverviewLayout.MaxUniformScale + 0.001f,
                "S13/uniform-scale=" + fitScale);
            Check(ExtensionIconsInsideBanner(f, 16, "S13/native-window"), "S13/extension-served");

            // 当前/历史/越界/null reign：native 一律不受影响
            var history = MakeReign("hist", 11, 40, 1);
            history.landData[11].steedSpawns = Steeds(37, 33);
            f.Campaign.previousReigns.Add(history);
            detailBefore = CaptureNativesDetail(f);
            clusterBefore = CaptureNativesCluster(f);
            readsBefore = NativeArtReadsTotal(f);
            f.Greek.focusedReign = 1;
            f.Map.UpdateLandIcons(history);
            f.Greek.focusedReign = 7;                       // 越界：native refresh skip
            f.Map.UpdateLandIcons(history);
            f.Greek.focusedReign = -1;                      // 负值：skip
            f.Map.UpdateLandIcons(f.Current);
            f.Greek.lands[0].UpdateLand(null, 0);           // null reign 直接回调
            f.Lands[0].UpdateLand(null, 0);
            Check(NativeArtReadsTotal(f) == readsBefore, "S13/reign-art-reads");
            AssertNativesUnchanged(f, detailBefore, clusterBefore, "S13/reign-mix");
            f.Greek.focusedReign = 0;
            f.Campaign.previousReigns.Clear();
            Tick(f, 2);                                     // 恢复 current 几何/图标

            // OFF：撤回自有资源但保留扩展必需几何；native 仍零接管
            detailBefore = CaptureNativesDetail(f);
            clusterBefore = CaptureNativesCluster(f);
            readsBefore = NativeArtReadsTotal(f);
            int resolveOff = MapIconSources.ResolveCalls;
            ModConfig.CrossWorldMountsEnabled.Value = false;
            UpdateDetails(f);
            Tick(f, 2);
            Check(MapIconSources.ResolveCalls == resolveOff, "S13/off-resolve");
            Check(NativeArtReadsTotal(f) == readsBefore, "S13/off-art-reads");
            AssertNativesUnchanged(f, detailBefore, clusterBefore, "S13/off");
            Check(CollectHolders(f.Paper).Length == 0 &&
                  CountNamed(f.ExtensionDetail.gameObject, "KEM_MapResourceIcons") == 0, "S13/off-icons-withdrawn");
            Check(ReadStatic("_overviewApplied") is bool offApplied && offApplied, "S13/off-geometry-kept");
            ModConfig.CrossWorldMountsEnabled.Value = true;

            // state1（single）：world 几何撤回到原生；原十簇恢复原始位置/缩放；native 零接管
            detailBefore = CaptureNativesDetail(f);
            clusterBefore = CaptureNativesCluster(f);
            f.Greek._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingSingleIsland;
            Tick(f, 1);
            AssertNativesUnchanged(f, detailBefore, clusterBefore, "S13/single");
            Check(CollectHolders(f.Paper).Length == 0, "S13/single-no-roots");
            for (int i = 0; i < 10; i++)
            {
                RectTransform rect = f.Lands[i].gameObject.GetComponent<RectTransform>();
                Check(rect.anchoredPosition.x == f.OriginalClusterPositions[i].x &&
                      rect.anchoredPosition.y == f.OriginalClusterPositions[i].y,
                    "S13/single-restore-" + i);
            }
            Check(NativeFitUniform(f, out _, out string singleNote), "S13/single-native-baseline " + singleNote);

            // 回 world：重新提交（viewport/geometry 重启），native 仍零接管
            detailBefore = CaptureNativesDetail(f);
            clusterBefore = CaptureNativesCluster(f);
            readsBefore = NativeArtReadsTotal(f);
            f.Greek._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingWorld;
            Tick(f, 4);
            Check(ReadStatic("_overviewApplied") is bool backApplied && backApplied, "S13/back-to-world-applied");
            Check(NativeArtReadsTotal(f) == readsBefore, "S13/back-art-reads");
            AssertNativesUnchanged(f, detailBefore, clusterBefore, "S13/back-to-world");
            Check(NativeFitUniform(f, out float backScale, out string backNote), "S13/back-fit " + backNote);
            Check(backScale > 0.3f, "S13/back-fit-scale=" + backScale);

            // 视口更换：可见域尺寸变化 → 有界复测周期（提交或诚实保持 native 基线），native 仍零接管
            detailBefore = CaptureNativesDetail(f);
            clusterBefore = CaptureNativesCluster(f);
            readsBefore = NativeArtReadsTotal(f);
            SetVisible(f, 170f, 60f);
            Tick(f, 4);
            Check(NativeArtReadsTotal(f) == readsBefore, "S13/viewport-art-reads");
            AssertNativesUnchanged(f, detailBefore, clusterBefore, "S13/viewport-change");
            Check(NativeFitUniform(f, out _, out string viewportNote), "S13/viewport-fit " + viewportNote);

            // 关菜单（正常收尾）：native 零接管；随后换 campaign 重建
            detailBefore = CaptureNativesDetail(f);
            clusterBefore = CaptureNativesCluster(f);
            readsBefore = NativeArtReadsTotal(f);
            f.Greek.OnDisable();
            Check(NativeArtReadsTotal(f) == readsBefore, "S13/disable-art-reads");
            AssertNativesUnchanged(f, detailBefore, clusterBefore, "S13/disable");
            Check(CollectHolders(f.Paper).Length == 0, "S13/disable-roots-cleared");

            Fixture g = BuildWorld(withExtension: true, steedCount: 16, types: types);
            var gDetail = CaptureNativesDetail(g);
            var gCluster = CaptureNativesCluster(g);
            int gReads = NativeArtReadsTotal(g);
            UpdateDetails(g);
            Tick(g, 2);
            Check(NativeArtReadsTotal(g) == gReads, "S13/campaign-art-reads");
            AssertNativesUnchanged(g, gDetail, gCluster, "S13/campaign");
            g.Greek.OnDisable();
        }

        /// <summary>
        /// S14（issue-156 归还边界）：统一归还处只归还「已捕获扩展」责任；legacy/native 账目仅销毁自有
        /// holder + 清账，绝不按旧 OriginalActive 改写原生当次 activeSelf；登记被清后的真实 captured
        /// 扩展责任仍须归还（不依赖当前 registry）。
        /// </summary>
        private static void ScenarioS14ReturnBoundaries()
        {
            Console.WriteLine("== S14 return boundaries (issue-156) ==");
            int[] types = { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };

            // (a) ON：native postfix 不进入资源路径（legacy 账目留给明确归还边界，native 状态零写入）
            {
                Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: types);
                Tick(f, 2);
                UILand land = f.Greek.lands[1];
                GameObject slot = land._dynamicMapIcons[0]._spawnedIcon.gameObject;
                slot.SetActive(false);
                GameObject holder = MakeHolder(land.gameObject.GetComponent<RectTransform>());
                MapMountIconView legacy = InjectLegacyView(land, false, holder, slot, true);
                int callsBefore = slot.SetActiveCalls;
                land.UpdateLand(f.Current, 1);
                Check(!slot.activeSelf, "S14/on-postfix-active-kept");
                Check(slot.SetActiveCalls == callsBefore, "S14/on-postfix-no-write");
                Check(holder != null && legacy.Holder == holder, "S14/on-postfix-legacy-untouched");
                RemoveInjectedView(legacy);
                f.Greek.OnDisable();
            }

            // (b) OFF（LayoutWithoutIcons→WithdrawIcons）：legacy 账目 holder 销毁 + 清账，activeSelf 保留当次值
            {
                Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: types);
                Tick(f, 2);
                UILand land = f.Greek.lands[1];
                GameObject slot = land._dynamicMapIcons[0]._spawnedIcon.gameObject;
                slot.SetActive(false);
                GameObject holder = MakeHolder(land.gameObject.GetComponent<RectTransform>());
                MapMountIconView legacy = InjectLegacyView(land, false, holder, slot, true);
                int callsBefore = slot.SetActiveCalls;
                ModConfig.CrossWorldMountsEnabled.Value = false;
                land.UpdateLand(f.Current, 1);
                Check(!slot.activeSelf, "S14/off-withdraw-active-kept");
                Check(slot.SetActiveCalls == callsBefore, "S14/off-withdraw-no-write");
                Check(holder == null, "S14/off-withdraw-holder-destroyed");
                Check(!ViewsList().Contains(legacy), "S14/off-withdraw-account-cleared");
                ModConfig.CrossWorldMountsEnabled.Value = true;
                f.Greek.OnDisable();
            }

            // (b2) 反向覆盖：OriginalActive=false、native 当次 true → 不得被写回 false
            {
                Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: types);
                Tick(f, 2);
                UILand land = f.Greek.lands[1];
                GameObject slot = land._dynamicMapIcons[0]._spawnedIcon.gameObject;
                slot.SetActive(true);
                GameObject holder = MakeHolder(land.gameObject.GetComponent<RectTransform>());
                MapMountIconView legacy = InjectLegacyView(land, false, holder, slot, false);
                int callsBefore = slot.SetActiveCalls;
                ModConfig.CrossWorldMountsEnabled.Value = false;
                land.UpdateLand(f.Current, 1);
                Check(slot.activeSelf, "S14/off-reverse-active-kept");
                Check(slot.SetActiveCalls == callsBefore, "S14/off-reverse-no-write");
                Check(holder == null && !ViewsList().Contains(legacy), "S14/off-reverse-account-cleared");
                ModConfig.CrossWorldMountsEnabled.Value = true;
                f.Greek.OnDisable();
            }

            // (c) Invalidate（提交路径）：legacy overview 账目同样只清不写
            {
                Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: types);
                UILand cluster = f.Lands[2];
                GameObject slot = cluster._dynamicMapIcons[0]._spawnedIcon.gameObject;
                slot.SetActive(false);
                GameObject holder = MakeHolder(f.Paper);
                MapMountIconView legacy = InjectLegacyView(cluster, true, holder, slot, true);
                int callsBefore = slot.SetActiveCalls;
                Tick(f, 2);
                Check(!slot.activeSelf, "S14/invalidate-active-kept");
                Check(slot.SetActiveCalls == callsBefore, "S14/invalidate-no-write");
                Check(holder == null, "S14/invalidate-holder-destroyed");
                Check(!ViewsList().Contains(legacy), "S14/invalidate-legacy-removed");
                f.Greek.OnDisable();
            }

            // (d) Suspend（state1）：legacy overview 账目销毁 + 移除 view，不写原生
            {
                Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: types);
                Tick(f, 2);
                UILand cluster = f.Lands[3];
                GameObject slot = cluster._dynamicMapIcons[0]._spawnedIcon.gameObject;
                slot.SetActive(false);
                GameObject holder = MakeHolder(f.Paper);
                MapMountIconView legacy = InjectLegacyView(cluster, true, holder, slot, true);
                int callsBefore = slot.SetActiveCalls;
                f.Greek._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingSingleIsland;
                Tick(f, 1);
                Check(!slot.activeSelf, "S14/suspend-active-kept");
                Check(slot.SetActiveCalls == callsBefore, "S14/suspend-no-write");
                Check(holder == null && !ViewsList().Contains(legacy), "S14/suspend-view-removed");
                f.Greek.OnDisable();
            }

            // (e) 已捕获扩展责任：ClearLands 先清登记 → 真实 captured 责任仍归还（四图 + 槽 + view 账）
            {
                Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: types);
                Sprite overviewNative = ArtImage(f.Extension).sprite;
                Sprite detailNative = f.DetailLandImage.sprite;
                GameObject overviewSlot = f.Extension._dynamicMapIcons[0]._spawnedIcon.gameObject;
                GameObject detailSlot = f.ExtensionDetail._dynamicMapIcons[0]._spawnedIcon.gameObject;
                Tick(f, 2);
                UpdateDetails(f);
                Check(!overviewSlot.activeSelf, "S14/captured-suppressed");
                f.Greek.ClearLands();                       // registry 先清 → 再 OnMenuLandsCleared 归还租约
                f.Extension.UpdateLand(f.Current, 11);      // 登记已清：DropUnregisteredView 仍须归还 captured 槽
                f.ExtensionDetail.UpdateLand(f.Current, 10);
                Check(overviewSlot.activeSelf, "S14/captured-overview-restored");
                Check(detailSlot.activeSelf, "S14/captured-detail-restored");
                Check(CountNamed(f.Extension.gameObject, "KEM_MapResourceIcons") == 0 &&
                      CountNamed(f.ExtensionDetail.gameObject, "KEM_MapResourceIcons") == 0,
                    "S14/captured-holders-gone");
                Check(ReferenceEquals(ArtImage(f.Extension).sprite, overviewNative), "S14/captured-overview-native");
                Check(ReferenceEquals(f.DetailLandImage.sprite, detailNative), "S14/captured-detail-native");
                Check(MapExtensionIslandArt.LeaseCount == 0, "S14/captured-lease-returned");
                f.Greek.OnDisable();
            }
        }

        /// <summary>
        /// S15（issue-156 扩展边界 + 刷新身份）：未登记 extra/伪 incoming 不接管；未访问 11 不揭露；
        /// OFF 时已访问扩展几何保持；locked 簇不揭露；focused/current reign 的 native refresh 与
        /// 16 项 Kind/TypeId 身份准确（替代旧 S15 renderer-parity：native 不再有自绘/网格约束）。
        /// </summary>
        private static void ScenarioS15ExtensionBoundary()
        {
            Console.WriteLine("== S15 extension boundary + refresh parity (issue-156) ==");
            int[] types = { 21, 22, 23, 24, 25, 26, 13, 14, 15, 16, 2, 4, 7, 3, 38, 6 };

            // (a) 未登记 extra（owned menu 成员，登记桥拒绝）：伪 incoming 0/11 都不得接管；
            //     同一 fixture 的登记扩展仍照常服务（门没有砍错人）
            {
                Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: types);
                Tick(f, 2);
                UILand extra = MakeUnregisteredDetail(f, "Extra Land");
                f.Greek.lands.Add(extra);
                GameObject spawn = extra._dynamicMapIcons[0]._spawnedIcon.gameObject;
                int callsBefore = spawn.SetActiveCalls;
                int resolveBefore = MapIconSources.ResolveCalls;
                extra.UpdateLand(f.Current, 0);    // 伪 native 前缀
                extra.UpdateLand(f.Current, 11);   // 伪扩展
                Check(CountNamed(extra.gameObject, "KEM_MapResourceIcons") == 0, "S15/extra-no-root");
                Check(!ViewsContainsLand(extra, false), "S15/extra-no-view");
                Check(spawn.activeSelf && spawn.SetActiveCalls == callsBefore, "S15/extra-native-untouched");
                Check(MapIconSources.ResolveCalls == resolveBefore, "S15/extra-no-resolve");
                Check(ExtensionIconsInsideBanner(f, 16, "S15/extra"), "S15/extra-extension-served");
                f.Greek.OnDisable();
            }

            // (b) 未访问 physical11（空资源集）：资源保持空白，不提前揭露
            {
                Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: types);
                f.Current.landData[11].steedSpawns = new Il2CppStructArray<SteedType>(0);
                f.Current.landData[11].hermit = new Il2CppStructArray<Hermit.HermitType>(0);
                f.Current.landData[11].statue = new Il2CppStructArray<Statue.Deity>(0);
                Tick(f, 2);
                UpdateDetails(f);
                Check(CountNamed(f.ExtensionDetail.gameObject, "KEM_MapResourceIcons") == 0,
                    "S15/unvisited-detail-blank");
                Check(CollectHolders(f.Paper).Length == 0, "S15/unvisited-overview-blank");
                f.Greek.OnDisable();
            }

            // (c) OFF 但已访问（current/visited 11）：资源撤回、扩展必需几何与岸线保持，native 仍零接管
            {
                Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: types);
                Tick(f, 2);
                Check(ExtensionIconsInsideBanner(f, 16, "S15/off-pre"), "S15/off-pre-icons");
                ModConfig.CrossWorldMountsEnabled.Value = false;
                var detailBefore = CaptureNativesDetail(f);
                var clusterBefore = CaptureNativesCluster(f);
                int readsBefore = NativeArtReadsTotal(f);
                UpdateDetails(f);
                Tick(f, 2);
                Check(ReadStatic("_overviewApplied") is bool applied && applied, "S15/off-geometry-kept");
                Check(ReadStatic("_shoreOwnerToken") != null, "S15/off-shore-lease-kept");
                Check(CollectHolders(f.Paper).Length == 0 &&
                      CountNamed(f.ExtensionDetail.gameObject, "KEM_MapResourceIcons") == 0,
                    "S15/off-icons-withdrawn");
                Check(ReferenceEquals(ArtImage(f.Extension).sprite, MapExtensionIslandArt.Shore),
                    "S15/off-banner-bound");
                Check(NativeArtReadsTotal(f) == readsBefore, "S15/off-art-reads");
                AssertNativesUnchanged(f, detailBefore, clusterBefore, "S15/off");
                ModConfig.CrossWorldMountsEnabled.Value = true;
                f.Greek.OnDisable();
            }

            // (d) locked 扩展簇：不建/即毁自有资源（不提前揭露）；解锁后恢复
            {
                Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: types);
                Tick(f, 2);
                UIMainMapLand clusterGate = f.Extension.gameObject.GetComponent<UIMainMapLand>();
                clusterGate.IsUnlocked = false;
                f.Extension.UpdateLand(f.Current, 11);
                Check(CollectIcons(f.Paper, 16).Count == 0, "S15/locked-not-revealed");
                clusterGate.IsUnlocked = true;
                f.Extension.UpdateLand(f.Current, 11);
                Check(CollectIcons(f.Paper, 16).Count == 16, "S15/unlocked-revealed");
                f.Greek.OnDisable();
            }

            // (e) 刷新身份：focused 历史 reign（1）→ hist 数据；single→world 重新提交 → current 16 项
            {
                Fixture f = BuildWorld(withExtension: true, steedCount: 16, types: types);
                var history = MakeReign("hist", 11, 40, 1);
                history.landData[11].steedSpawns = Steeds(37, 33);
                f.Campaign.previousReigns.Add(history);
                f.Greek.focusedReign = 1;
                Tick(f, 2);
                Check(ProbeHooks.RefreshReignTags.Contains("hist"),
                    "S15/refresh-focused-hist tags=" + string.Join(",", ProbeHooks.RefreshReignTags));
                List<RectTransform> histIcons = CollectIcons(f.Paper, 2);
                var histTypes = new List<int>(histIcons.Count);
                for (int i = 0; i < histIcons.Count; i++) histTypes.Add(ParseIconType(histIcons[i].gameObject.name));
                histTypes.Sort();
                Check(histTypes.Count == 2 && histTypes[0] == 33 && histTypes[1] == 37,
                    "S15/history-source-identity types=" + string.Join(",", histTypes));
                Check(ExtensionIconsInsideBanner(f, 2, "S15/history"), "S15/history-banner-bound");

                f.Greek.focusedReign = 0;
                f.Greek._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingSingleIsland;
                Tick(f, 1);
                f.Greek._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingWorld;
                Tick(f, 4);
                Check(ReadStatic("_overviewApplied") is bool resubmitApplied && resubmitApplied,
                    "S15/resubmit-applied");
                Check(ProbeHooks.RefreshReignTags.Contains("current"),
                    "S15/refresh-current tags=" + string.Join(",", ProbeHooks.RefreshReignTags));
                List<RectTransform> icons = CollectIcons(f.Paper, 16);
                Check(icons.Count == 16, "S15/current-16-icons n=" + icons.Count);
                var seen = new List<int>(icons.Count);
                for (int i = 0; i < icons.Count; i++) seen.Add(ParseIconType(icons[i].gameObject.name));
                seen.Sort();
                var expected = (int[])types.Clone();
                Array.Sort(expected);
                Check(seen.Count == expected.Length && string.Join(",", seen) == string.Join(",", expected),
                    "S15/current-source-identity types=" + string.Join(",", seen));
                Check(ExtensionIconsInsideBanner(f, 16, "S15/current"), "S15/current-banner-bound");
                f.Greek.OnDisable();
            }
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

        /// <summary>inner 是否完整落在 outer 内（容差 eps）。</summary>
        private static bool Inside(in MapIconBox inner, in MapIconBox outer, float eps)
        {
            return inner.X0 >= outer.X0 - eps && inner.Y0 >= outer.Y0 - eps &&
                   inner.X1 <= outer.X1 + eps && inner.Y1 <= outer.Y1 + eps;
        }

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
            MapIconSources.ResolveCalls = 0;
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
                landImage.sprite = MakeNativeIslandSprite("athena_tholos_greece");
                landImage.type = Image.Type.Simple;
                landImage.useSpriteMesh = true;
                landImage.preserveAspect = true;   // 实测资产 m_PreserveAspect=1
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
                islandImage.sprite = MakeNativeIslandSprite("athena_tholos_greece");
                islandImage.type = Image.Type.Simple;
                islandImage.useSpriteMesh = true;
                islandImage.preserveAspect = true;   // 实测资产 m_PreserveAspect=1
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
