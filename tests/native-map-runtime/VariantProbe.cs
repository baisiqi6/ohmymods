// Source-linked host integration: actual MapMountIcons methods, with explicit two-tuple test bridge.
// This does not exercise native IL2CPP wrappers, current player LandMapData, or live map rendering.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Text.Json;
using KingdomEnhancedMod;
using UnityEngine;
using UnityEngine.UI;
namespace RuntimeProbe
{
    internal static class VariantProbe
    {
        private const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static int checks;
        private static object Call(string name, params object[] args)
            => typeof(MapMountIcons).GetMethod(name, Flags).Invoke(null, args);
        private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Fields).GetValue(target);
        private static void Check(bool ok, string name) { checks++; if (!ok) throw new Exception(name); }
        private static object Context(UILand land)
        {
            object[] args = { land, null };
            return (bool)Call("TryBuildDetailPageContext", args) ? args[1] : null;
        }
        private static Image Art(UILand land) => ((RectTransform)Call("FindArtTransform", land)).GetComponent<Image>();
        private static Image Outline(UILand land)
            => ((RectTransform)Call("FindNamedDescendant", land.transform, "Land Outline", 4)).GetComponent<Image>();
        private sealed class Pair
        {
            internal UILand A, B, WorldA, WorldB;
            internal MapTimelineMenuGreece Menu;
            internal RectTransform Paper;
            internal CampaignSaveData.ReignInfo Reign;
        }
        private static Pair NewPair()
        {
            ExtensionIslandMap.WorldTokens.Clear(); ExtensionIslandMap.DetailTokens.Clear();
            object fixture = typeof(ProbeProgram).GetMethod("BuildWorld", Flags).Invoke(null,
                new object[] { true, 1, new[] { 14 } });
            var p = new Pair {
                A = Field<UILand>(fixture, "ExtensionDetail"),
                WorldA = Field<UILand>(fixture, "Extension"),
                Menu = Field<MapTimelineMenuGreece>(fixture, "Greek"),
                Paper = Field<RectTransform>(fixture, "Paper"),
                Reign = Field<CampaignSaveData.ReignInfo>(fixture, "Current")
            };
            // Explicit B test tuple. Detail uses an existing fully built host node; the world
            // fixture creates another extension cluster (native hosts lack the required outline).
            // These are bridge inputs, not production island reclassification.
            p.B = p.Menu.lands[9];
            p.WorldB = (UILand)typeof(ProbeProgram).GetMethod("MakeCluster", Flags).Invoke(null,
                new object[] { fixture, "B extension", p.Paper, new Vector2(0, 0), true });
            ExtensionIslandMap.PhysicalIndexOverride = land => land == p.A || land == p.WorldA ? 11 :
                land == p.B || land == p.WorldB ? 13 : (int?)null;
            ExtensionIslandMap.DetailTokens[11] = new object(); ExtensionIslandMap.DetailTokens[13] = new object();
            ExtensionIslandMap.WorldTokens[11] = new object(); ExtensionIslandMap.WorldTokens[13] = new object();
            while (p.Reign.landData.Count <= 13) p.Reign.landData.Add(new CampaignSaveData.LandMapData());
            p.Reign.landData[13] = p.Reign.landData[11];
            p.Menu._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingSingleIsland;
            return p;
        }
        private static void Bind(Pair p, UILand land)
            => Check((bool)Call("EnsureDetailShape", land, p.Menu), "bind detail");
        private static void CheckWorld(Pair p, UILand land, int physical)
        {
            Type entryType = typeof(MapMountIcons).GetNestedType("OverviewEntry", BindingFlags.NonPublic);
            object entry = Activator.CreateInstance(entryType, true);
            entryType.GetField("Cluster", Fields).SetValue(entry, land);
            entryType.GetField("LandIndex", Fields).SetValue(entry, physical);
            object[] args = { entry, new MapIconBox(30, 20, 258, 104), p.Paper, p.Menu, physical, null };
            Check((bool)Call("ApplyExtensionBanner", args), "world bind " + physical);
            Check(ReferenceEquals(Art(land).sprite, MapExtensionIslandArt.ShoreFor(physical)), "world shore " + physical);
            Check(ReferenceEquals(Outline(land).sprite, MapExtensionIslandArt.OutlineFor(physical)), "world outline " + physical);
        }
        private static void CrossLand()
        {
            Pair p = NewPair();
            Bind(p, p.B);
            Check(MapExtensionIslandArt.Shore == null, "cold B does not decode A");
            object b = Context(p.B); Check(b != null, "B context");
            Check(ReferenceEquals(Field<MapIconAnchorSet>(b, "Anchors"), MapExtensionIslandArtVariant.AnchorsFor(13)), "B anchors");
            var pressure = CatalogRequests(true);
            int bCapacity = (int)Call("FindUniformDetailCapacity", b, pressure);
            Check(bCapacity > 0, "B context has readable capacity");
            var bPage = pressure.GetRange(0, Math.Min(bCapacity, pressure.Count));
            var beforeA = new List<MapIconPlacement>();
            object[] beforePlan = { b, bPage, beforeA, 0f, 0 };
            Check((bool)Call("TryPlanDetailSubset", beforePlan), "B pressure planned before A bind");
            Bind(p, p.A);
            Check(Context(p.B) != null && ReferenceEquals(Art(p.B).sprite, MapExtensionIslandArt.ShoreFor(13)),
                "sibling A bind keeps B pair and context");
            object a = Context(p.A); Check(a != null, "A context");
            Check(Field<MapIconAnchorSet>(a, "Anchors") == null, "A legacy anchors");
            Check(!ReferenceEquals(Field<MapShoreMask>(a, "Mask"), Field<MapShoreMask>(b, "Mask")), "distinct masks");
            // Cached context captures its own mask/anchors; another island's bind cannot change it.
            var placements = new List<MapIconPlacement>();
            object[] plan = { b, bPage, placements, 0f, 0 };
            Check((bool)Call("TryPlanDetailSubset", plan) && placements.Count == beforeA.Count && (int)plan[4] == 0,
                "B saved context pressure plan after A bind");
            for (int i = 0; i < placements.Count; i++)
                Check(placements[i].RequestIndex == beforeA[i].RequestIndex && placements[i].Request.TypeId == beforeA[i].Request.TypeId &&
                    BitConverter.SingleToInt32Bits(placements[i].X) == BitConverter.SingleToInt32Bits(beforeA[i].X) &&
                    BitConverter.SingleToInt32Bits(placements[i].Y) == BitConverter.SingleToInt32Bits(beforeA[i].Y) &&
                    BitConverter.SingleToInt32Bits(placements[i].Scale) == BitConverter.SingleToInt32Bits(beforeA[i].Scale),
                    "captured B mask/profile produces identical placement after A bind");
            Bind(p, p.B); Check(Context(p.A) != null && ReferenceEquals(Art(p.A).sprite, MapExtensionIslandArt.ShoreFor(11)),
                "sibling B bind keeps A pair and context");
            Check(ReferenceEquals(Art(p.B).sprite, MapExtensionIslandArt.ShoreFor(13)), "B selected pair");
            Bind(p, p.A); Check(Context(p.A) != null, "A-B-A restored context");
            CheckWorld(p, p.WorldA, 11); CheckWorld(p, p.WorldB, 13);
            Check(ReferenceEquals(Art(p.WorldA).sprite, MapExtensionIslandArt.ShoreFor(11)), "world A remains A after B bind");
            p.Menu.OnDisable();
        }
        private static void BrokenPair(bool afterWrite)
        {
            Pair p = NewPair(); Bind(p, p.B);
            Image outline = Outline(p.B);
            // A foreign sprite invalidates fast path; the next exact B rebind fails at outline.
            outline.sprite = new Sprite { name = "foreign-outline" };
            outline.ThrowOnSet = !afterWrite; outline.ThrowAfterSet = afterWrite;
            outline.ReadFaultAfterWriteThrow = afterWrite;
            Check(!(bool)Call("EnsureDetailShape", p.B, p.Menu), "faulted outline returns false");
            Check(Context(p.B) == null, "faulted pair has no context");
            var view = new MapMountIconView { Land = p.B, LandIndex = 13, Overview = false,
                Holder = new GameObject("stale B icons"), Fingerprint = 123, CapturedExtension = true };
            var views = (List<MapMountIconView>)typeof(MapMountIcons).GetField("Views", Flags).GetValue(null);
            views.Add(view);
            MapMountIcons.OnLandUpdated(p.B, 13, p.Reign);
            Check(view.Holder == null && view.Fingerprint == int.MinValue, "failed callback withdraws cached holder");
            outline.ThrowOnSet = outline.ThrowAfterSet = outline.ReadFaultAfterWriteThrow = outline.ThrowOnGet = false;
            MapExtensionIslandArt.ReleaseAllBorrowed();
            Bind(p, p.A); Check(Context(p.A) != null, "A opens after B failure");
            Bind(p, p.B); Check(Context(p.B) != null, "B recovers on ordinary bind after fault removed");
            p.Menu.OnDisable();
        }
        private static void CallbackOnly()
        {
            Pair p = NewPair(); Bind(p, p.B);
            object[] read = { p.Reign, 13, 0, 0, 0 };
            Check((bool)Call("TryReadLand", read), "read actual B data before buffered requests");
            object[] built = { p.B, read[2], read[3], read[4], null, null };
            Call("BuildRequests", built);
            var requests = (List<MapIconRequest>)built[4];
            var sources = (List<UIMapIcon>)built[5];
            Check(requests.Count == 1 && requests[0].TypeId == 14, "callback fingerprint uses actual type14");
            var holder = new GameObject("cached valid B holder");
            var view = new MapMountIconView { Land = p.B, LandIndex = 13, Overview = false,
                Holder = holder, CapturedExtension = true,
                Fingerprint = (int)Call("Fingerprint", 13, false, requests) };
            view.Icons.AddRange(sources);
            ((List<MapMountIconView>)typeof(MapMountIcons).GetField("Views", Flags).GetValue(null)).Add(view);
            Image outline = Outline(p.B); outline.sprite = new Sprite { name = "foreign-outline" };
            outline.ThrowOnSet = true;
            MapMountIcons.OnLandUpdated(p.B, 13, p.Reign);
            Check(view.Holder == null && holder.Destroyed && view.Fingerprint == int.MinValue,
                "first failed shape callback withdraws matching-fingerprint cached holder");
            outline.ThrowOnSet = false; p.Menu.OnDisable();
        }

        private static void PagerFault(bool clickFirst)
        {
            Pair p = NewPair(); Bind(p, p.B);
            p.Reign.landData[13].steedSpawns = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<SteedType>(2);
            p.Reign.landData[13].steedSpawns[0] = (SteedType)14;
            p.Reign.landData[13].steedSpawns[1] = (SteedType)15;
            object[] read = { p.Reign, 13, 0, 0, 0 }; Check((bool)Call("TryReadLand", read), "pager fresh actual requests");
            object[] built = { p.B, read[2], read[3], read[4], null, null }; Call("BuildRequests", built);
            var requests = (List<MapIconRequest>)built[4]; var sources = (List<UIMapIcon>)built[5];
            Check(requests.Count == 2 && requests[0].TypeId == 14 && requests[1].TypeId == 15, "pager exact data identities");
            var style = new GameObject("native button style");
            p.Menu.leftButton = style.AddComponent<Button>();
            new GameObject("native text style").transform.SetParent(style.transform, false);
            style.transform.GetChild(0).gameObject.AddComponent<Text>().text = "native";
            p.Menu.focusedLand = 11;
            var holder = new GameObject("KEM_MapResourceIcons"); holder.transform.SetParent(p.B.transform, false);
            int baseFingerprint = (int)Call("Fingerprint", 13, false, requests);
            int fingerprint = unchecked(baseFingerprint * 31 + 1);
            var view = new MapMountIconView { Land = p.B, LandIndex = 13, Overview = false, Holder = holder,
                CapturedExtension = true, Fingerprint = fingerprint };
            view.Icons.Add(sources[0]);
            ((List<MapMountIconView>)typeof(MapMountIcons).GetField("Views", Flags).GetValue(null)).Add(view);
            Check((bool)Call("CreateDetailPager", view, p.B, 13, p.Menu, requests, sources, 2, 0, 2, 1,
                baseFingerprint, fingerprint), "real source pager created from native style");
            object pager = PagerAt(13);
            Button next = Field<Button>(pager, "Next"); Button prev = Field<Button>(pager, "Prev");
            next.onClick.Invoke();
            Check(Field<int>(pager, "PageIndex") == 1 && view.Holder != holder,
                "real Next event reaches next page: view=" + view.PageIndex + " pager=" + Field<int>(pager, "PageIndex") +
                " pagerAlive=" + (PagerAt(13) != null) +
                " logs=" + string.Join(" | ", EntryProbe.ProbeHooks.IconLogs));
            prev.onClick.Invoke(); Check(Field<int>(pager, "PageIndex") == 0, "real Prev event returns to first page");
            GameObject currentHolder = view.Holder; GameObject pagerRoot = Field<GameObject>(pager, "Root");
            Image outline = Outline(p.B); outline.sprite = new Sprite { name = "invalid external outline" };
            outline.ThrowOnSet = true;
            int leases = MapExtensionIslandArt.LeaseCount;
            if (clickFirst)
            {
                next.onClick.Invoke();
                Check(ReferenceEquals(view.Holder, currentHolder) && Field<int>(pager, "PageIndex") == 0 &&
                    PagerAt(13) == null && pagerRoot.Destroyed,
                    "invalid pair Next cannot rebuild and removes stale pager");
            }
            MapMountIcons.OnLandUpdated(p.B, 13, p.Reign);
            Check(view.Holder == null && currentHolder.Destroyed && pagerRoot.Destroyed &&
                PagerAt(13) == null,
                "failed data callback withdraws actual holder and pager");
            Check(MapExtensionIslandArt.LeaseCount <= leases, "holder/pager removal adds no sprite lease");
            outline.ThrowOnSet = false; p.Menu.OnDisable();
        }

        private static List<MapIconRequest> CatalogRequests(bool bOnly)
        {
            // Native sizes are a historical extracted fixture; custom sizes and membership use
            // current production catalogs. This is a pressure envelope, not a player's live array.
            using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "icon-rects.json")));
            var requests = new List<MapIconRequest>();
            foreach (var definition in CrossWorldMountCatalog.Definitions)
            {
                if (bOnly && MountIslandSplitPolicy.GetTargetLand(definition.Id) != 13) continue;
                float width, height;
                if (MapCustomIconCatalog.TryGet(1, definition.SteedTypeId, out var custom))
                { width = custom.UiWidth; height = custom.UiHeight; }
                else
                {
                    var row = fixture.RootElement.GetProperty("1/" + definition.SteedTypeId);
                    width = row.GetProperty("w").GetSingle() * Math.Abs(row.GetProperty("scale").GetProperty("x").GetSingle());
                    height = row.GetProperty("h").GetSingle() * Math.Abs(row.GetProperty("scale").GetProperty("y").GetSingle());
                }
                requests.Add(new MapIconRequest(MapIconKind.Steed, definition.SteedTypeId, requests.Count, width, height));
            }
            Check(requests.Count == (bOnly ? 8 : 16), "source catalog pressure count");
            return requests;
        }
        private static void Planning()
        {
            Pair p = NewPair(); Bind(p, p.B);
            object context = Context(p.B); Check(context != null, "pager context ready");
            foreach (var requests in new[] { CatalogRequests(true), CatalogRequests(false) })
            {
                int capacity = (int)Call("FindUniformDetailCapacity", context, requests);
                Check(capacity > 0 && capacity <= MapExtensionIslandLayout.MaxItems, "capacity retains all requests");
                int reached = 0;
                var identities = new HashSet<int>();
                for (int offset = 0; offset < requests.Count; offset += capacity)
                {
                    int length = Math.Min(capacity, requests.Count - offset);
                    var page = requests.GetRange(offset, length);
                    var placements = new List<MapIconPlacement>();
                    object[] args = { context, page, placements, 0f, 0 };
                    Check((bool)Call("TryPlanDetailSubset", args) && placements.Count == length && (int)args[4] == 0 &&
                        (float)args[3] >= .6f, "complete readable page");
                    var shore = Field<MapIconBox>(context, "ShoreBox");
                    var mask = Field<MapShoreMask>(context, "Mask");
                    foreach (var placed in placements)
                    {
                        float sx = mask.Width / shore.Width, sy = mask.Height / shore.Height;
                        int x = (int)Math.Round((placed.X - shore.X0) * sx);
                        int y = (int)Math.Round((placed.Y - shore.Y0) * sy);
                        Check(mask.RectAllInside(x, y, x + Math.Max(1, (int)Math.Ceiling(placed.Request.Width * placed.Scale * sx)) - 1,
                            y + Math.Max(1, (int)Math.Ceiling(placed.Request.Height * placed.Scale * sy)) - 1), "entire footprint on own top");
                        Check(placed.RequestIndex >= 0 && placed.RequestIndex < length &&
                            identities.Add(offset + placed.RequestIndex), "each request identity appears once");
                        var expected = page[placed.RequestIndex];
                        Check(placed.Request.Kind == expected.Kind && placed.Request.TypeId == expected.TypeId &&
                            placed.Request.ArrayIndex == expected.ArrayIndex && placed.Request.Width == expected.Width &&
                            placed.Request.Height == expected.Height, "placement identity equals actual page request");
                    }
                    reached += length;
                }
                Check(reached == requests.Count && identities.Count == requests.Count, "all catalog entries reachable including legacy16");
            }
            var one = new List<MapIconRequest> { new MapIconRequest(0, 14, 0, 5, 5) };
            var area = new MapIconBox(0, 0, 228, 84); var result = new List<MapIconPlacement>();
            foreach (var bad in new[] {
                new MapIconAnchorSet(new[] { float.PositiveInfinity }, new[] { .5f }, new[] { .5f }, new[] { .5f }),
                new MapIconAnchorSet(new[] { 1.1f }, new[] { .5f }, new[] { .5f }, new[] { .5f }),
                new MapIconAnchorSet(new[] { .5f }, Array.Empty<float>(), new[] { .5f }, new[] { .5f }) })
                Check(!MapExtensionIslandLayout.TryPlan(area, one, null, default, null, result, out _, out int failed,
                    .6f, bad) && result.Count == 0 && failed == 1, "corrupt profile fails whole plan");
            var legal = new MapIconAnchorSet(new[] { float.NaN }, new[] { float.NaN }, new[] { .5f }, new[] { .5f });
            Check(MapExtensionIslandLayout.TryPlan(area, one, null, default, null, result, out _, out _, .6f, legal),
                "NaN disables only primary and permits legal secondary");
            var none = new MapIconAnchorSet(new[] { float.NaN }, new[] { float.NaN }, new[] { float.NaN }, new[] { float.NaN });
            Check(!MapExtensionIslandLayout.TryPlan(area, one, null, default, null, result, out _, out int noneFailed,
                .6f, none) && noneFailed == 1 && result.Count == 0, "disabled passes never fall back to global anchors");
            p.Menu.OnDisable();
        }
        private static void MissingB()
        {
            MapExtensionIslandArt.Reset();
            Check(MapExtensionIslandArt.TryEnsure(11), "A works without B resource");
            Sprite a = MapExtensionIslandArt.Shore;
            var image = new GameObject("live A lease").AddComponent<Image>(); image.sprite = new Sprite();
            object owner = new object(); MapExtensionIslandArt.TryBorrow(image, a, owner);
            Check(!MapExtensionIslandArt.TryEnsure(13) && MapExtensionIslandArt.ShoreFor(13) == null &&
                MapExtensionIslandArt.OutlineFor(13) == null && MapExtensionIslandArt.PrepFor(13) == null,
                "missing B never draws A");
            Check(ReferenceEquals(a, image.sprite) && ReferenceEquals(a, MapExtensionIslandArt.Shore) && !a.Destroyed,
                "missing B preserves live A lease");
            MapExtensionIslandArt.ReleaseOwner(owner); MapExtensionIslandArt.Reset();
        }
        private static void SecondSpriteFailure()
        {
            MapExtensionIslandArt.Reset(); MapExtensionIslandArt.TryEnsure(11);
            Sprite a = MapExtensionIslandArt.Shore;
            int before = Sprite.CreatedSprites.Count; Sprite.FailCreateCountdown = 2;
            Check(!MapExtensionIslandArt.TryEnsure(13) && MapExtensionIslandArt.ShoreFor(13) == null &&
                MapExtensionIslandArt.OutlineFor(13) == null && MapExtensionIslandArt.PrepFor(13) == null,
                "B second Sprite failure rejects entire pair");
            Check(Sprite.CreatedSprites.Count == before + 1 && Sprite.CreatedSprites[before].Destroyed &&
                Sprite.CreatedSprites[before].texture.Destroyed, "partial B sprite and texture destroyed");
            Check(ReferenceEquals(a, MapExtensionIslandArt.Shore) && !a.Destroyed, "B failure leaves A intact");
            MapExtensionIslandArt.Reset();
        }

        // ---------------------------------------------------------------- issue-272 UI slice

        private static object PagerAt(int physical)
        {
            var pagers = (Array)typeof(MapMountIcons).GetField("_detailPagers", Flags).GetValue(null);
            return pagers != null ? pagers.GetValue(physical == 11 ? 0 : 1) : null;
        }

        private static MapMountIconView ViewOf(UILand land) => (MapMountIconView)Call("FindView", land, false);

        private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<SteedType> Steeds(
            params int[] types)
        {
            var array = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<SteedType>(types.Length);
            for (int i = 0; i < types.Length; i++) array[i] = (SteedType)types[i];
            return array;
        }

        /// <summary>一次批量 prepare A→B 后，两 pair 的贴图/上下文必须同时读回有效（不能只累加各次 Ensure=true）。</summary>
        private static void DualPrepare()
        {
            Pair p = NewPair();
            Bind(p, p.A);
            Bind(p, p.B);   // 兄弟 tuple 绑定不得释放 A 的 detail 租约
            Check(ReferenceEquals(Art(p.A).sprite, MapExtensionIslandArt.ShoreFor(11)), "batch keeps A terrain");
            Check(ReferenceEquals(Outline(p.A).sprite, MapExtensionIslandArt.OutlineFor(11)), "batch keeps A outline");
            Check(ReferenceEquals(Art(p.B).sprite, MapExtensionIslandArt.ShoreFor(13)), "batch keeps B terrain");
            Check(ReferenceEquals(Outline(p.B).sprite, MapExtensionIslandArt.OutlineFor(13)), "batch keeps B outline");
            Check(MapExtensionIslandArt.LeaseCount == 4,
                "four detail leases live after batch leases=" + MapExtensionIslandArt.LeaseCount);
            object a = Context(p.A), b = Context(p.B);
            Check(a != null && b != null, "both per-slot page contexts valid");
            Check(!ReferenceEquals(Field<MapShoreMask>(a, "Mask"), Field<MapShoreMask>(b, "Mask")),
                "both contexts keep distinct masks");
            // A→B→A 反复切换：兄弟绑定后前者仍存活
            Bind(p, p.A); Bind(p, p.B);
            Check(ReferenceEquals(Art(p.A).sprite, MapExtensionIslandArt.ShoreFor(11)) && Context(p.A) != null,
                "A survives repeated sibling bind");
            Check(ReferenceEquals(Art(p.B).sprite, MapExtensionIslandArt.ShoreFor(13)) && Context(p.B) != null,
                "B survives repeated sibling bind");
            p.Menu.OnDisable();
        }

        /// <summary>world 态两 tuple 预建（真实 UpdateLand 回调）→ 无 UpdateLand 直达 A → 分页 A/B/A，
        /// 以及错 focus/错 state/旧回调/OFF/OnDisable 反例。焦点契约是 UI 域 10/11 + ShowingSingleIsland。</summary>
        private static void WorldPrebuildPagers()
        {
            Pair p = NewPair();
            // 压力源：固定 18×18（实测本 context 18 项超单页容量 → 必分 2 页；resolver 是 probe 桥控，
            // 与 fixture MakeSourceIcon 同构：pivot(.5,.5)+sizeDelta+Image+Text+UIMapIcon）。
            MapIconSources.Resolver = (land, iconType, type) =>
            {
                var go = new GameObject("SourceIcon pager_pressure");
                RectTransform rect = go.AddComponent<RectTransform>();
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(18f, 18f);
                go.AddComponent<Image>();
                go.AddComponent<Text>();
                return go.AddComponent<UIMapIcon>();
            };
            int[] cycle = new int[18];
            for (int i = 0; i < cycle.Length; i++) cycle[i] = 21;
            p.Reign.landData[11].steedSpawns = Steeds(cycle);   // [13] 与 [11] 同实例：两 tuple 同压测数据
            Sprite aNative = Art(p.A).sprite, bNative = Art(p.B).sprite;
            var style = new GameObject("native button style");
            p.Menu.leftButton = style.AddComponent<Button>();
            var styleText = new GameObject("native text style");
            styleText.transform.SetParent(style.transform, false);
            styleText.AddComponent<Text>().text = "native";
            // world 态预建（真实 patch 入口；world 焦点停在原生 UI9）
            p.Menu._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingWorld;
            p.Menu.focusedLand = 9;
            p.A.UpdateLand(p.Reign, 10);
            p.B.UpdateLand(p.Reign, 11);
            Check(ReferenceEquals(Art(p.A).sprite, MapExtensionIslandArt.ShoreFor(11)), "prebuild keeps A pair");
            Check(ReferenceEquals(Art(p.B).sprite, MapExtensionIslandArt.ShoreFor(13)), "prebuild keeps B pair");
            MapMountIconView viewA = ViewOf(p.A), viewB = ViewOf(p.B);
            object pagerA = PagerAt(11), pagerB = PagerAt(13);
            Check(pagerA != null && pagerB != null, "prebuild holds one pager per tuple");
            Check(viewA != null && viewA.Holder != null && viewB != null && viewB.Holder != null,
                "prebuild builds both detail views");
            // world→A：state1 + focus UI10，无 UpdateLand —— 形状/分页上下文仍有效，点击可翻页
            p.Menu._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingSingleIsland;
            p.Menu.focusedLand = 10;
            Check(ReferenceEquals(Art(p.A).sprite, MapExtensionIslandArt.ShoreFor(11)),
                "world->A without UpdateLand keeps A pair");
            GameObject holderA0 = viewA.Holder;
            Field<Button>(pagerA, "Next").onClick.Invoke();
            Check(Field<int>(pagerA, "PageIndex") == 1 && !ReferenceEquals(viewA.Holder, holderA0),
                "A pager steps page under actual focus10/state1");
            GameObject holderA1 = viewA.Holder;
            // 分页→B：focus UI11，B 自己的 pager 翻页，不串 A 的页
            p.Menu.focusedLand = 11;
            GameObject holderB0 = viewB.Holder;
            Field<Button>(pagerB, "Next").onClick.Invoke();
            Check(Field<int>(pagerB, "PageIndex") == 1 && !ReferenceEquals(viewB.Holder, holderB0),
                "B pager steps page under actual focus11");
            Check(ReferenceEquals(viewA.Holder, holderA1) && !Field<GameObject>(pagerA, "Root").Destroyed,
                "B paging leaves A pager and page intact");
            // A→B→A：回 A（focus UI10）翻回第一页
            p.Menu.focusedLand = 10;
            Field<Button>(pagerA, "Prev").onClick.Invoke();
            Check(Field<int>(pagerA, "PageIndex") == 0, "A returns to first page via Prev");
            // P2/r3：看 B 时迟到点 A 的按钮 → 只是本次不具交互资格：视图/页码不变，两 pager 均存活
            p.Menu.focusedLand = 11;
            GameObject holderAStay = viewA.Holder;
            Field<Button>(pagerA, "Next").onClick.Invoke();
            Check(Field<int>(pagerA, "PageIndex") == 0 && ReferenceEquals(viewA.Holder, holderAStay) &&
                ReferenceEquals(PagerAt(11), pagerA) && PagerAt(13) != null &&
                !Field<GameObject>(pagerA, "Root").Destroyed && !Field<GameObject>(pagerB, "Root").Destroyed,
                "late A click while viewing B is rejected without removing cached pager");
            // 数据回调重建 A pager（新内容 → 新 fingerprint → Rebuild）
            p.Menu.focusedLand = 10;
            int[] trimmed = new int[cycle.Length - 1];   // 17 项 > 单页容量 16 → 必分 2 页重建 pager
            Array.Copy(cycle, trimmed, trimmed.Length);
            p.Reign.landData[11].steedSpawns = Steeds(trimmed);
            p.A.UpdateLand(p.Reign, 10);
            object pagerA2 = PagerAt(11);
            Check(pagerA2 != null && !ReferenceEquals(pagerA2, pagerA), "fresh callback rebuilds A pager");
            // P2/r3：world 态（state0）迟到点击 → 同样只拒本次，pager 保留
            p.Menu._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingWorld;
            Field<Button>(pagerA2, "Next").onClick.Invoke();
            Check(ReferenceEquals(PagerAt(11), pagerA2) && !Field<GameObject>(pagerA2, "Root").Destroyed,
                "late click under world state is rejected without removing cached pager");
            // 无 UpdateLand 回 A（state1+focus10）→ 缓存 pager 仍可翻页
            p.Menu._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingSingleIsland;
            p.Menu.focusedLand = 10;
            Field<Button>(pagerA2, "Next").onClick.Invoke();
            Check(Field<int>(pagerA2, "PageIndex") == 1, "returning to A pages without UpdateLand");
            Field<Button>(pagerA2, "Prev").onClick.Invoke();
            // 旧回调反例：重建后旧 pager 的按钮无权（不得销毁/改动新 pager）
            p.Menu._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingSingleIsland;
            p.Reign.landData[11].steedSpawns = Steeds(cycle);   // 换回 18 项全集：新 fingerprint 再重建
            p.A.UpdateLand(p.Reign, 10);
            object pagerA3 = PagerAt(11);
            Check(pagerA3 != null && !ReferenceEquals(pagerA3, pagerA2), "second rebuild creates new A pager");
            int pageBefore = Field<int>(pagerA3, "PageIndex");
            Field<Button>(pagerA2, "Next").onClick.Invoke();
            Check(ReferenceEquals(PagerAt(11), pagerA3) && Field<int>(pagerA3, "PageIndex") == pageBefore,
                "old pager callback has no authority");
            // OFF：整代撤 pager（两 slot 全销），贴图/租约保留
            ModConfig.CrossWorldMountsEnabled.Value = false;
            p.B.UpdateLand(p.Reign, 11);
            Check(PagerAt(11) == null && PagerAt(13) == null, "OFF withdraws both tuple pagers");
            Check(ReferenceEquals(Art(p.A).sprite, MapExtensionIslandArt.ShoreFor(11)) &&
                ReferenceEquals(Art(p.B).sprite, MapExtensionIslandArt.ShoreFor(13)),
                "OFF keeps both detail pairs");
            Check(MapExtensionIslandArt.LeaseCount == 4, "OFF keeps detail leases leases=" +
                MapExtensionIslandArt.LeaseCount);
            // OnDisable：两 tuple 全归还 native，租约清零
            ModConfig.CrossWorldMountsEnabled.Value = true;
            p.Menu.OnDisable();
            Check(ReferenceEquals(Art(p.A).sprite, aNative) && ReferenceEquals(Art(p.B).sprite, bNative),
                "disable restores both native pairs");
            Check(MapExtensionIslandArt.LeaseCount == 0, "disable releases all leases");
        }

        // ---------------------------------------------------------------- detail ship + 缺剪影诊断

        private static RectTransform ChildRect(string name, RectTransform parent, Vector2 size)
        {
            var go = new GameObject(name);
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        /// <summary>真实 detail clone 形状（root 180×150 位于带缩放的父级；Map Icons→Generic→Boat Icon）。</summary>
        private static UILand ShipLand(float genericScale, out RectTransform boat, out RectTransform ripple)
        {
            var land = new GameObject("detail ship clone").AddComponent<UILand>();
            RectTransform root = (RectTransform)land.transform;
            root.sizeDelta = new Vector2(180f, 150f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            var holderGo = new GameObject("detail holder");
            RectTransform holder = (RectTransform)holderGo.transform;
            holder.sizeDelta = new Vector2(320f, 320f);
            holder.localPosition = new Vector3(37f, -51f, 0f);
            holder.localScale = new Vector3(2f, 2f, 1f);
            root.SetParent(holder, false);
            root.localPosition = new Vector3(11f, -7f, 0f);

            RectTransform icons = ChildRect("Map Icons", root, Vector2.zero);
            RectTransform generic = ChildRect("Generic", icons, Vector2.zero);
            generic.localScale = new Vector3(genericScale, genericScale, 1f);
            boat = ChildRect("Boat Icon", generic, new Vector2(32f, 32f));
            boat.anchoredPosition = new Vector2(-48.0000114f, 18.0000114f);
            boat.gameObject.AddComponent<Image>().sprite = new Sprite { name = "map_boat_0" };
            ripple = ChildRect("Ripple", boat, new Vector2(32f, 32f));
            ripple.gameObject.AddComponent<Image>();
            ripple.gameObject.SetActive(false);   // 原生涟漪可处于 inactive：footprint 仍须计入
            land._ship = boat.gameObject;
            land.gameObject.SetActive(false);     // producer 在 RebuildStaticIconCache 后先隐藏
            return land;
        }

        private static MapIconBox ShipFootprint(UILand land)
        {
            object[] args = { (RectTransform)land.transform, (RectTransform)land._ship.transform, null };
            Check((bool)Call("TryMeasureShipFootprint", args), "ship footprint readable");
            return (MapIconBox)args[2];
        }

        private static MapMountIcons.DetailShipInit InitShip(UILand land, int physical)
            => (MapMountIcons.DetailShipInit)Call("TryInitializeExtensionDetailShip", land, physical);

        private static MapIconBox DetailFrame(int physical)
        {
            Check(MapExtensionIslandArt.TryEnsure(physical), "ship art ready " + physical);
            Sprite shore = MapExtensionIslandArt.ShoreFor(physical);
            Check(shore != null, "ship shore " + physical);
            float aspect = shore.rect.width / shore.rect.height;
            Check(MapExtensionShapePlan.TryPlanDetailBox(MapExtensionShapePlan.DetailPageHalfWidth,
                MapExtensionShapePlan.DetailLegendRight, MapExtensionShapePlan.DetailWaterGap,
                MapExtensionShapePlan.DetailMaxWidth, aspect, MapExtensionShapePlan.DetailCenterY,
                out MapIconBox frame, out _), "ship frame plan " + physical);
            return frame;
        }

        private static void ShipPlacement(int physical, float genericScale, string tag)
        {
            MapIconBox frame = DetailFrame(physical);
            UILand land = ShipLand(genericScale, out RectTransform boat, out _);
            RectTransform root = (RectTransform)land.transform;
            Rect rootSize = root.rect;
            MapIconBox before = ShipFootprint(land);
            float beforeCenterX = (before.X0 + before.X1) * 0.5f - rootSize.width * 0.5f;
            Vector2 sizeBefore = boat.sizeDelta;
            Vector3 scaleBefore = boat.localScale;
            Quaternion rotationBefore = boat.localRotation;
            int subtreeBefore = boat.GetComponentsInChildren<RectTransform>(true).Length;

            Check(InitShip(land, physical) == MapMountIcons.DetailShipInit.Applied, tag + "/applied");
            MapIconBox after = ShipFootprint(land);
            float x0 = after.X0 - rootSize.width * 0.5f;
            float x1 = after.X1 - rootSize.width * 0.5f;
            float y0 = after.Y0 - rootSize.height * 0.5f;
            float y1 = after.Y1 - rootSize.height * 0.5f;
            float centerX = (x0 + x1) * 0.5f;
            float centerY = (y0 + y1) * 0.5f;
            float expectedCenterY = frame.Y1 + MapExtensionShapePlan.DetailWaterGap + after.Height * 0.5f;
            Check(Math.Abs(centerY - expectedCenterY) < 0.06f,
                tag + "/centerY=" + centerY.ToString("0.###") + " expected=" + expectedCenterY.ToString("0.###"));
            Check(Math.Abs(centerX - beforeCenterX) < 0.01f,
                tag + "/keeps-x=" + centerX.ToString("0.###") + " before=" + beforeCenterX.ToString("0.###"));
            Check(y0 >= frame.Y1 + MapExtensionShapePlan.DetailWaterGap - 0.05f,
                tag + "/above-shore y0=" + y0.ToString("0.###") + " shoreTop=" + frame.Y1.ToString("0.###"));
            Check(x0 >= -MapExtensionShapePlan.DetailPageHalfWidth - 0.05f &&
                x1 <= MapExtensionShapePlan.DetailPageHalfWidth + 0.05f &&
                y0 >= -MapExtensionShapePlan.DetailPageHalfHeight - 0.05f &&
                y1 <= MapExtensionShapePlan.DetailPageHalfHeight + 0.05f,
                tag + "/inside-page x=" + x0.ToString("0.#") + ".." + x1.ToString("0.#") +
                " y=" + y0.ToString("0.#") + ".." + y1.ToString("0.#"));
            Check(Math.Abs(boat.sizeDelta.x - sizeBefore.x) < 1e-6f &&
                Math.Abs(boat.sizeDelta.y - sizeBefore.y) < 1e-6f &&
                Math.Abs(boat.localScale.x - scaleBefore.x) < 1e-6f &&
                Math.Abs(boat.localScale.y - scaleBefore.y) < 1e-6f &&
                boat.localRotation.Equals(rotationBefore) &&
                boat.GetComponentsInChildren<RectTransform>(true).Length == subtreeBefore,
                tag + "/size-scale-rotation-subtree-unchanged");

            // 重复初始化（同 clone 同规划）不漂移。
            Check(InitShip(land, physical) == MapMountIcons.DetailShipInit.Applied, tag + "/reapplied");
            MapIconBox again = ShipFootprint(land);
            Check(Math.Abs((again.Y0 + again.Y1) * 0.5f - (after.Y0 + after.Y1) * 0.5f) < 0.01f &&
                Math.Abs((again.X0 + again.X1) * 0.5f - (after.X0 + after.X1) * 0.5f) < 0.01f,
                tag + "/idempotent");
        }

        private static bool SameBox(in MapIconBox a, in MapIconBox b)
            => Math.Abs(a.X0 - b.X0) < 0.01f && Math.Abs(a.Y0 - b.Y0) < 0.01f &&
               Math.Abs(a.X1 - b.X1) < 0.01f && Math.Abs(a.Y1 - b.Y1) < 0.01f;

        private static void ShipFallbacks()
        {
            MapExtensionIslandArt.TryEnsure(11);

            // 已激活 clone（已发布/未隐藏）：绝不写。
            UILand active = ShipLand(1f, out RectTransform activeBoat, out _);
            active.gameObject.SetActive(true);
            MapIconBox activeBefore = ShipFootprint(active);
            Check(InitShip(active, 11) == MapMountIcons.DetailShipInit.Unavailable, "ship/active-refused");
            Check(SameBox(activeBefore, ShipFootprint(active)), "ship/active-untouched");

            // _ship 不是本 clone 后代：外部对象 transform 原样。
            UILand externalLand = ShipLand(1f, out _, out _);
            var external = new GameObject("external boat").AddComponent<RectTransform>();
            external.sizeDelta = new Vector2(32f, 32f);
            Vector3 externalLocal = external.localPosition;
            externalLand._ship = external.gameObject;
            Check(InitShip(externalLand, 11) == MapMountIcons.DetailShipInit.Unavailable, "ship/external-refused");
            Check(external.localPosition.Equals(externalLocal), "ship/external-untouched");

            // 缺失 _ship：诊断 + 不阻断，clone transform 不变。
            UILand missing = ShipLand(1f, out _, out _);
            missing._ship = null;
            Check(InitShip(missing, 11) == MapMountIcons.DetailShipInit.Unavailable, "ship/missing-refused");

            // 完整 footprint 无法同时满足岸线外 + page 内：不写。
            UILand tooBig = ShipLand(1f, out RectTransform bigBoat, out _);
            RectTransform huge = ChildRect("Huge Ripple", bigBoat, new Vector2(400f, 400f));
            huge.gameObject.AddComponent<Image>();
            huge.gameObject.SetActive(false);
            MapIconBox tooBigBefore = ShipFootprint(tooBig);
            Check(InitShip(tooBig, 11) == MapMountIcons.DetailShipInit.Unavailable, "ship/infeasible-refused");
            Check(SameBox(tooBigBefore, ShipFootprint(tooBig)), "ship/infeasible-untouched");

            // 该 physical 没有岸线资源：保 native fallback。
            UILand noArt = ShipLand(1f, out RectTransform noArtBoat, out _);
            Vector3 noArtLocal = noArtBoat.localPosition;
            Check(InitShip(noArt, 12) == MapMountIcons.DetailShipInit.Unavailable, "ship/no-art-refused");
            Check(noArtBoat.localPosition.Equals(noArtLocal), "ship/no-art-untouched");

            // 写后抛异常但恢复成功 → Unavailable，原 transform 精确恢复；随后正常调用可应用。
            UILand recover = ShipLand(1f, out RectTransform recoverBoat, out _);
            MapIconBox recoverBefore = ShipFootprint(recover);
            Vector3 recoverLocal = recoverBoat.localPosition;
            recoverBoat.ThrowAfterLocalPositionSet = true;
            Check(InitShip(recover, 11) == MapMountIcons.DetailShipInit.Unavailable, "ship/write-restored");
            Check(recoverBoat.localPosition.Equals(recoverLocal), "ship/write-restored-exact");
            Check(SameBox(recoverBefore, ShipFootprint(recover)), "ship/write-restored-footprint");
            Check(InitShip(recover, 11) == MapMountIcons.DetailShipInit.Applied, "ship/retry-after-write-fault");

            // 写与恢复都失败 → Failed（调用方销毁未发布 clone）。
            UILand broken = ShipLand(1f, out RectTransform brokenBoat, out _);
            Vector3 brokenLocal = brokenBoat.localPosition;
            brokenBoat.ThrowOnLocalPositionSet = true;
            Check(InitShip(broken, 11) == MapMountIcons.DetailShipInit.Failed, "ship/unrecoverable-failed");
            brokenBoat.ThrowOnLocalPositionSet = false;
            Check(brokenBoat.localPosition.Equals(brokenLocal), "ship/unrecoverable-untouched");

            // 写前 getter 异常：完整 preflight catch → Unavailable，零写。
            UILand getter = ShipLand(1f, out RectTransform getterBoat, out _);
            Vector3 getterLocal = getterBoat.localPosition;
            getterBoat.ThrowOnLocalPositionGet = true;
            Check(InitShip(getter, 11) == MapMountIcons.DetailShipInit.Unavailable, "ship/preflight-getter-fault");
            getterBoat.ThrowOnLocalPositionGet = false;
            Check(getterBoat.localPosition.Equals(getterLocal), "ship/preflight-getter-untouched");

            // NaN 图形：逐角 finite 拒测（不能只看 union）。
            UILand nan = ShipLand(1f, out RectTransform nanBoat, out _);
            Vector3 nanLocal = nanBoat.localPosition;
            nanBoat.sizeDelta = new Vector2(float.NaN, 32f);
            Check(InitShip(nan, 11) == MapMountIcons.DetailShipInit.Unavailable, "ship/nan-refused");
            Check(nanBoat.localPosition.Equals(nanLocal), "ship/nan-untouched");

            // Inf 父级变换：转换/bbox 非有限 → Unavailable，零写。
            UILand inf = ShipLand(1f, out RectTransform infBoat, out _);
            Vector3 infLocal = infBoat.localPosition;
            ((RectTransform)infBoat.parent).localPosition = new Vector3(float.PositiveInfinity, 0f, 0f);
            Check(InitShip(inf, 11) == MapMountIcons.DetailShipInit.Unavailable, "ship/inf-refused");
            Check(infBoat.localPosition.Equals(infLocal), "ship/inf-untouched");

            // 静默忽略写：验证失败且 restore 读回原值 → Unavailable（绝不声称 Applied）。
            UILand silent = ShipLand(1f, out RectTransform silentBoat, out _);
            Vector3 silentLocal = silentBoat.localPosition;
            silentBoat.SilentLocalPositionSet = true;
            Check(InitShip(silent, 11) == MapMountIcons.DetailShipInit.Unavailable, "ship/silent-setter-refused");
            silentBoat.SilentLocalPositionSet = false;
            Check(silentBoat.localPosition.Equals(silentLocal), "ship/silent-setter-untouched");

            // restore 读回异常 → Failed（调用方销毁未发布 clone）。
            UILand readback = ShipLand(1f, out RectTransform readbackBoat, out _);
            readbackBoat.ThrowAfterLocalPositionSet = true;
            readbackBoat.ThrowOnLocalPositionGetAfterSet = true;
            Check(InitShip(readback, 11) == MapMountIcons.DetailShipInit.Failed, "ship/restore-readback-throw");
            readbackBoat.ThrowAfterLocalPositionSet = false;
            readbackBoat.ThrowOnLocalPositionGetAfterSet = false;

            // restore 读回 scale 不一致 → Failed，且绝不重写 scale 伪装恢复。
            UILand drift = ShipLand(1f, out RectTransform driftBoat, out _);
            Vector3 driftScale = driftBoat.localScale;
            driftBoat.ScaleDriftOnLocalPositionSet = true;
            Check(InitShip(drift, 11) == MapMountIcons.DetailShipInit.Failed, "ship/restore-scale-mismatch");
            driftBoat.ScaleDriftOnLocalPositionSet = false;
            Check(Math.Abs(driftBoat.localScale.x - driftScale.x) > 0.5f, "ship/scale-not-falsely-restored");
        }

        private static bool LogsContain(string needle)
            => KingdomEnhancedPlugin.Instance.LogSource.Lines.Exists(line => line.Contains(needle));

        private static int LogsCount(string needle)
            => KingdomEnhancedPlugin.Instance.LogSource.Lines.FindAll(line => line.Contains(needle)).Count;

        /// <summary>仅测试：清空 MapIconLog.Once 的 key 集与桩日志，保证 Once 断言可重复。</summary>
        private static void ResetDetailLogs()
        {
            Type logType = typeof(MapMountIcons).GetNestedType("MapIconLog", BindingFlags.NonPublic);
            var keys = logType != null
                ? (HashSet<string>)logType.GetField("Keys", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)
                : null;
            keys?.Clear();
            KingdomEnhancedPlugin.Instance.LogSource.Lines.Clear();
        }

        private sealed class ReadFacts
        {
            internal bool Ok;
            internal MapMountIcons.LandReadStatus Status;
            internal int DataCount, Steed, Hermit, Statue;
        }

        private static ReadFacts ReadWithStatus(CampaignSaveData.ReignInfo reign, int land)
        {
            object[] args = { reign, land, 0, 0, 0, null, 0 };
            var facts = new ReadFacts { Ok = (bool)Call("TryReadLandWithStatus", args) };
            facts.Status = (MapMountIcons.LandReadStatus)args[5];
            facts.DataCount = (int)args[6];
            facts.Steed = (int)args[2];
            facts.Hermit = (int)args[3];
            facts.Statue = (int)args[4];
            return facts;
        }

        private static void ReadStatuses()
        {
            ReadFacts f = ReadWithStatus(null, 13);
            Check(!f.Ok && f.Status == MapMountIcons.LandReadStatus.NullReign && f.DataCount == -1,
                "read-status/null-reign " + f.Status + "/" + f.DataCount);
            var reign = new CampaignSaveData.ReignInfo();
            reign.landData = null;
            f = ReadWithStatus(reign, 13);
            Check(!f.Ok && f.Status == MapMountIcons.LandReadStatus.NullLandData && f.DataCount == -1,
                "read-status/null-list " + f.Status);
            reign.landData = new Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData>();
            f = ReadWithStatus(reign, 13);
            Check(!f.Ok && f.Status == MapMountIcons.LandReadStatus.LandOutOfRange && f.DataCount == 0,
                "read-status/out-of-range " + f.Status + "/" + f.DataCount);
            while (reign.landData.Count <= 13) reign.landData.Add(new CampaignSaveData.LandMapData());
            reign.landData[13] = null;
            f = ReadWithStatus(reign, 13);
            Check(!f.Ok && f.Status == MapMountIcons.LandReadStatus.NullEntry && f.DataCount == 14,
                "read-status/null-entry " + f.Status + "/" + f.DataCount);
            reign.landData[13] = new CampaignSaveData.LandMapData();
            f = ReadWithStatus(reign, 13);
            Check(f.Ok && f.Status == MapMountIcons.LandReadStatus.SuccessEmpty && f.DataCount == 14 &&
                f.Steed == 0 && f.Hermit == 0 && f.Statue == 0, "read-status/success-empty " + f.Status);
            reign.landData[13].steedSpawns = Steeds(14);
            f = ReadWithStatus(reign, 13);
            Check(f.Ok && f.Status == MapMountIcons.LandReadStatus.Success && f.Steed == 1,
                "read-status/success " + f.Status + " counts=" + f.Steed);
            object[] legacy = { reign, 13, 0, 0, 0 };
            Check((bool)Call("TryReadLand", legacy) && (int)legacy[2] == 1,
                "read-status/legacy-wrapper-unchanged");
            var faulted = new Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData>();
            faulted.ThrowCount = true;
            reign.landData = faulted;
            f = ReadWithStatus(reign, 13);
            Check(!f.Ok && f.Status == MapMountIcons.LandReadStatus.ReadFault && f.DataCount == -1,
                "read-status/read-fault " + f.Status);
            faulted.ThrowCount = false;
        }

        private static void DetailDiagnostics()
        {
            ReadStatuses();
            ResetDetailLogs();
            Pair p = NewPair();
            Bind(p, p.B);
            p.Menu._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingSingleIsland;

            // 当次 success-empty：logs data=SuccessEmpty/counts/requests，且 Once 只写一次。
            var data = new Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData>();
            while (data.Count <= 13) data.Add(new CampaignSaveData.LandMapData());
            p.Reign.landData = data;
            MapMountIcons.OnLandUpdated(p.B, 11, p.Reign);
            Check(LogsContain("reason=empty") && LogsContain("data=SuccessEmpty") &&
                LogsContain("counts=0/0/0") && LogsContain("requests=0"), "diag/empty-facts");
            int emptyLines = LogsCount("reason=empty");
            MapMountIcons.OnLandUpdated(p.B, 11, p.Reign);
            Check(LogsCount("reason=empty") == emptyLines, "diag/once-per-key");
            Check(emptyLines == 1, "diag/empty-once lines=" + emptyLines);

            // 非空但 requests=0（source 缺失）与读取失败 unknown。
            data[13].steedSpawns = Steeds(14);
            Func<UILand, int, int, UIMapIcon> resolver = MapIconSources.Resolver;
            MapIconSources.Resolver = null;
            MapMountIcons.OnLandUpdated(p.B, 11, p.Reign);
            Check(LogsContain("reason=no-requests") && LogsContain("data=Success") &&
                LogsContain("counts=1/0/0") && LogsContain("requests=0") && LogsContain("sources=0"),
                "diag/no-requests-facts");
            MapIconSources.Resolver = resolver;

            MapMountIcons.OnLandUpdated(p.B, 11, null);
            Check(LogsContain("reason=no-reign") && LogsContain("data=NullReign") &&
                LogsContain("counts=unknown") && LogsContain("requests=unknown") &&
                LogsContain("dataCount=unknown"), "diag/read-failure-unknown");
            int noReignLines = LogsCount("reason=no-reign");
            MapMountIcons.OnLandUpdated(p.B, 11, null);
            Check(LogsCount("reason=no-reign") == noReignLines && noReignLines == 1,
                "diag/read-failure-once lines=" + noReignLines);

            // Feature OFF：不读数据时明确 unknown，不冒充 0。
            ModConfig.CrossWorldMountsEnabled.Value = false;
            MapMountIcons.OnLandUpdated(p.B, 11, p.Reign);
            Check(LogsContain("reason=feature-off") && LogsContain("data=Unknown") &&
                LogsContain("counts=unknown"), "diag/feature-off-facts");
            ModConfig.CrossWorldMountsEnabled.Value = true;

            // 简单 holder 事实：present/inactive/inHierarchy/hiddenGate；不扫 parent/Canvas/Mask。
            var holder = new GameObject("KEM_MapResourceIcons");
            holder.transform.SetParent(p.B.transform, false);
            holder.SetActive(false);
            var view = new MapMountIconView { Land = p.B, LandIndex = 13, Holder = holder, HiddenByNativeGate = true };
            Call("DiagnoseDetail", p.Menu, p.B, 13, 11, "holder-test", p.Reign, view,
                MapMountIcons.LandReadStatus.Success, 1, 0, 0, 1, 1, 14);
            Check(LogsContain("reason=holder-test") && LogsContain("holder=present") &&
                LogsContain("active=false") && LogsContain("inHierarchy=false") &&
                LogsContain("hiddenGate=true"), "diag/holder-facts");
            Call("DiagnoseDetail", p.Menu, p.B, 13, 11, "holder-none-test", p.Reign, null,
                MapMountIcons.LandReadStatus.Unknown, -1, -1, -1, -1, -1, -1);
            Check(LogsContain("reason=holder-none-test") && LogsContain("view=absent") &&
                LogsContain("holder=unknown"), "diag/holder-unknown");

            p.Menu.OnDisable();
            ModConfig.CrossWorldMountsEnabled.Value = true;
        }

        private static void ShipPlanner()
        {
            Check(MapExtensionShapePlan.TryPlanDetailShip(MapExtensionShapePlan.DetailPageHalfWidth,
                    MapExtensionShapePlan.DetailPageHalfHeight, 38.36842f, MapExtensionShapePlan.DetailWaterGap,
                    16f, 16f, -48f, out float centerY) && Math.Abs(centerY - 58.36842f) < 1e-3f,
                "ship-plan/known-center=" + centerY.ToString("0.#####"));
            Check(!MapExtensionShapePlan.TryPlanDetailShip(141f, 98f, 38.36842f, 4f, 16f, 60f, -48f, out _),
                "ship-plan/page-height-rejected");
            Check(!MapExtensionShapePlan.TryPlanDetailShip(141f, 98f, 38.36842f, 4f, 30f, 16f, 130f, out _),
                "ship-plan/page-width-rejected");
            Check(!MapExtensionShapePlan.TryPlanDetailShip(141f, 98f, float.NaN, 4f, 16f, 16f, -48f, out _),
                "ship-plan/nan-rejected");
            Check(!MapExtensionShapePlan.TryPlanDetailShip(141f, 98f, 38.36842f, -1f, 16f, 16f, -48f, out _),
                "ship-plan/negative-gap-rejected");
            Check(!MapExtensionShapePlan.TryPlanDetailShip(float.PositiveInfinity, 98f, 38.36842f, 4f, 16f, 16f, -48f, out _),
                "ship-plan/inf-page-rejected");
            Check(!MapExtensionShapePlan.TryPlanDetailShip(141f, 98f, 38.36842f, float.PositiveInfinity, 16f, 16f, -48f, out _),
                "ship-plan/inf-gap-rejected");
            Check(!MapExtensionShapePlan.TryPlanDetailShip(141f, 98f, float.MaxValue, 4f, 16f, 16f, -48f, out _),
                "ship-plan/overflow-rejected");
        }

        public static int Main(string[] args)
        {
            try
            {
                if (Array.IndexOf(args, "--missing-b") >= 0) MissingB();
                else if (Array.IndexOf(args, "--callback-only") >= 0) CallbackOnly();
                else { CrossLand(); BrokenPair(false); BrokenPair(true); CallbackOnly(); PagerFault(false); PagerFault(true); Planning(); SecondSpriteFailure(); DualPrepare(); WorldPrebuildPagers(); ShipPlanner(); ShipPlacement(11, 1f, "ship/A"); ShipPlacement(13, 1.4f, "ship/B-scaled"); ShipFallbacks(); DetailDiagnostics(); }
                Console.WriteLine("PASS variant runtime checks=" + checks); return 0;
            }
            catch (Exception e) { Console.WriteLine("FAIL checks=" + checks + " " + e); return 1; }
        }
    }
}
