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
            Check(Context(p.B) == null, "old B context rejected while A active");
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
            Bind(p, p.B); Check(Context(p.A) == null, "old A context rejected while B active");
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
            object pager = typeof(MapMountIcons).GetField("_detailPager", Flags).GetValue(null);
            Button next = Field<Button>(pager, "Next"); Button prev = Field<Button>(pager, "Prev");
            next.onClick.Invoke();
            Check(Field<int>(pager, "PageIndex") == 1 && view.Holder != holder,
                "real Next event reaches next page: view=" + view.PageIndex + " pager=" + Field<int>(pager, "PageIndex") +
                " pagerAlive=" + (typeof(MapMountIcons).GetField("_detailPager", Flags).GetValue(null) != null) +
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
                    typeof(MapMountIcons).GetField("_detailPager", Flags).GetValue(null) == null && pagerRoot.Destroyed,
                    "invalid pair Next cannot rebuild and removes stale pager");
            }
            MapMountIcons.OnLandUpdated(p.B, 13, p.Reign);
            Check(view.Holder == null && currentHolder.Destroyed && pagerRoot.Destroyed &&
                typeof(MapMountIcons).GetField("_detailPager", Flags).GetValue(null) == null,
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
        public static int Main(string[] args)
        {
            try
            {
                if (Array.IndexOf(args, "--missing-b") >= 0) MissingB();
                else if (Array.IndexOf(args, "--callback-only") >= 0) CallbackOnly();
                else { CrossLand(); BrokenPair(false); BrokenPair(true); CallbackOnly(); PagerFault(false); PagerFault(true); Planning(); SecondSpriteFailure(); }
                Console.WriteLine("PASS variant runtime checks=" + checks); return 0;
            }
            catch (Exception e) { Console.WriteLine("FAIL checks=" + checks + " " + e); return 1; }
        }
    }
}
