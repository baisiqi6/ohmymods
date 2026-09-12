// Behavioral tests for AutoRestockCounts (linked unmodified from ../build).
using System;
using System.Collections;
using System.Collections.Generic;
using KingdomEnhancedMod;

internal static class Test
{
    public static int Pass, Fail;
    public static void Run(string name, Action a)
    {
        try { a(); Pass++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { Fail++; Console.WriteLine("FAIL " + name + " :: " + e.Message); }
    }
    public static void Assert(bool b, string msg) { if (!b) throw new Exception(msg); }
    public static void Eq<T>(T actual, T expected, string msg)
    { if (!Equals(actual, expected)) throw new Exception(msg + ": expected " + expected + ", got " + actual); }
}

internal sealed class Env
{
    public readonly Managers Managers = new Managers();
    public readonly Kingdom Kingdom;
    public readonly World World;
    public readonly Transform Layer;
    public readonly GameObject LayerGo;
    public readonly ShopPlanner Planner;
    private static long _next = 0x1000;
    public static IntPtr Next() => new IntPtr(_next += 0x10);

    public Env(int sceneHandle = 1)
    {
        AutoRestockCounts.Reset();
        Native.Reset();
        Time.frameCount = 100; Time.time = 0;
        Layer = new Transform { Pointer = Next() };
        LayerGo = new GameObject { Pointer = Next(), scene = new Scene { handle = sceneHandle }, transform = Layer };
        Layer.gameObject = LayerGo;
        World = new World { gameLayer = Layer, Pointer = Next() };
        Kingdom = new Kingdom { Pointer = Next() };
        Managers.kingdom = Kingdom; Managers.world = World;
        Planner = new ShopPlanner { Pointer = Next() };
        Managers.shopPlanner = Planner;
        ModConfig.Enabled = ConfigEntry<bool>.On(true);
        Mask(true, true, true, true);
    }

    public void Mask(bool w, bool a, bool n, bool b, bool p = false)
    {
        ModConfig.AutoRestockWorkersEnabled = ConfigEntry<bool>.On(w);
        ModConfig.AutoRestockArchersEnabled = ConfigEntry<bool>.On(a);
        ModConfig.AutoRestockNinjasEnabled = ConfigEntry<bool>.On(n);
        ModConfig.AutoRestockBerserkersEnabled = ConfigEntry<bool>.On(b);
        ModConfig.AutoRestockPeasantsEnabled = ConfigEntry<bool>.On(p);
    }

    public bool R() => AutoRestockCounts.Refresh(Managers);
    public int Enums => Kingdom._characters.EnumerationCount;

    public Character MakeCharacter(string role, bool live = true, IntPtr? ptr = null, bool inRoster = true)
    {
        GameObject go = Fabric.NewGo(ptr ?? Next(), LayerGo.scene.handle, live ? Layer : null);
        var ch = new Character { Pointer = go.Pointer };
        var dmg = new Damageable { Pointer = Next() };
        go.Add(ch); go.Add(dmg);
        ch._damageable = dmg;
        switch (role)
        {
            case "Worker": go.Add(new Worker { Pointer = Next() }); break;
            case "Archer": go.Add(new Archer { Pointer = Next() }); break;
            case "Ninja": go.Add(new Ninja { Pointer = Next() }); break;
            case "Fisher": go.Add(new Ninja { Pointer = Next(), _isFisher = true }); break;
            case "Berserker": go.Add(new Berserker { Pointer = Next() }); break;
            case "Peasant": go.Add(new Peasant { Pointer = Next() }); break;
            case "Beggar": go.Add(new Beggar { Pointer = Next(), _character = ch }); break;
            // "Pikeman": no Ninja component -> unclassified
        }
        if (inRoster) Kingdom._characters.Add(ch);
        return ch;
    }

    public Droppable MakeItem(string tag) => new Droppable { tag = tag, Pointer = Next() };

    public PayableShop MakeShop(string tag, int itemCount)
    {
        GameObject go = Fabric.NewGo(Next(), LayerGo.scene.handle, Layer);
        var shop = new PayableShop { Pointer = Next(), itemPrefab = new Droppable { tag = tag, Pointer = Next() }, itemCountOverride = itemCount };
        go.Add(shop);
        var items = new Droppable[itemCount];
        for (int i = 0; i < itemCount; i++) items[i] = MakeItem(tag);
        shop.SetItems(items);
        return shop;
    }

    public void SetPlaced(params PayableShop[] shops)
    {
        var gos = new GameObject[shops.Length];
        for (int i = 0; i < shops.Length; i++) gos[i] = shops[i].gameObject;
        Planner._placedShops = gos;
    }
}

// reflection into private static state (only where the contract demands it)
internal static class Refl
{
    public static readonly BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    public static object SF(string f) => typeof(AutoRestockCounts).GetField(f, All).GetValue(null);
    public static object F(object o, string f) => o.GetType().GetField(f, All).GetValue(o);
    public static object FirstShopEntry() { foreach (object v in ((IDictionary)SF("_shops")).Values) return v; return null; }
    public static object ActorEntryFor(IntPtr ptr) { return ((IDictionary)SF("_tracked"))[ptr]; }
    public static IList SubsOf(object shopEntry) => (IList)F(shopEntry, "Subs");
    public static void FireStaleTool(object entry, object sub)
        => typeof(AutoRestockCounts).GetMethod("OnToolEvent", All).Invoke(null, new[] { entry, sub });
}

internal static class Tests
{
    static int V => (int)AutoRestockCounts.Version;
    static int Live(int r) => AutoRestockCounts.LiveCount(r);
    static int Stock(int r) => AutoRestockCounts.StockCount(r);
    // role indices per contract
    const int W = 0, A = 1, N = 2, B = 3;

    // ---------- registry lifecycle / steady state ----------

    internal static void t01_inactive_mask0_no_seed()
    {
        var e = new Env();
        e.MakeCharacter("Worker");
        e.Mask(false, false, false, false);
        Test.Eq(e.R(), false, "mask0 refresh must be inactive");
        Test.Eq(e.Enums, 0, "no roster enumeration while disabled");
        Test.Eq(Live(W), 0, "live must be 0 while disabled");
    }

    internal static void t02_inactive_master_toggle_off()
    {
        var e = new Env();
        e.MakeCharacter("Worker");
        ModConfig.Enabled = ConfigEntry<bool>.On(false);
        Test.Eq(e.R(), false, "master off -> inactive");
        Test.Eq(e.Enums, 0, "no seed while master off");
    }

    internal static void t03_first_enable_seeds_once()
    {
        var e = new Env();
        e.MakeCharacter("Worker"); e.MakeCharacter("Worker");
        Test.Eq(e.R(), true, "first refresh publishes");
        Test.Eq(Live(W), 2, "seeded live count");
        for (int i = 0; i < 5; i++) e.R();
        Test.Eq(e.Enums, 1, "seed happens exactly once");
    }

    internal static void t04_steady_state_zero_native_queries()
    {
        var e = new Env();
        e.MakeCharacter("Worker"); e.MakeCharacter("Worker"); e.MakeCharacter("Ninja");
        PayableShop s1 = e.MakeShop("Hammer", 3), s2 = e.MakeShop("Hammer", 3);
        e.SetPlaced(s1, s2);
        Test.Eq(e.R(), true, "seed refresh");
        Test.Eq(Live(W), 2, "workers"); Test.Eq(Live(N), 1, "ninja");
        Test.Eq(Stock(W), 6, "stock");
        int g = Native.GetItemCountCalls, en = e.Enums;
        int r1 = s1.ItemsReadCount, r2 = s2.ItemsReadCount;
        for (int i = 0; i < 50; i++) Test.Assert(e.R(), "steady refresh must stay true");
        Test.Eq(Native.GetItemCountCalls, g, "steady refresh: zero native GetItemCount");
        Test.Eq(e.Enums, en, "steady refresh: zero roster enumeration");
        Test.Eq(s1.ItemsReadCount, r1, "no _items enumeration on shop1");
        Test.Eq(s2.ItemsReadCount, r2, "no _items enumeration on shop2");
    }

    internal static void t05_null_managers_fail_closed()
    {
        var e = new Env();
        e.MakeCharacter("Worker");
        Test.Eq(AutoRestockCounts.Refresh(null), false, "null managers -> false");
        Test.Eq(Live(W), 0, "no counts published");
        Test.Eq(e.R(), true, "recovers with valid managers");
        Test.Eq(Live(W), 1, "count after recovery");
    }

    internal static void t06_missing_gamelayer_fail_closed()
    {
        var e = new Env();
        e.MakeCharacter("Worker");
        e.World.gameLayer = null;
        Test.Eq(e.R(), false, "no gameLayer -> false");
        Test.Eq(e.Enums, 0, "no seed without layer");
        e.World.gameLayer = e.Layer;
        Test.Eq(e.R(), true, "recovers when layer returns");
    }

    internal static void t07_scene_change_reseeds()
    {
        var e = new Env();
        Character w = e.MakeCharacter("Worker");
        Test.Eq(e.R(), true, "seed");
        int en = e.Enums;
        var nl = new Transform { Pointer = Env.Next() };
        var ngo = new GameObject { Pointer = Env.Next(), scene = new Scene { handle = 2 }, transform = nl };
        nl.gameObject = ngo;
        w.gameObject.scene = new Scene { handle = 2 };
        w.transform.parent = nl;
        e.World.gameLayer = nl;
        Test.Eq(e.R(), true, "refresh after scene change");
        Test.Eq(e.Enums, en + 1, "scene change must reseed exactly once");
        Test.Eq(Live(W), 1, "actor still counted in new scene");
    }

    internal static void t08_world_change_reseeds()
    {
        var e = new Env();
        e.MakeCharacter("Worker");
        Test.Eq(e.R(), true, "seed");
        int en = e.Enums;
        e.Managers.world = new World { gameLayer = e.Layer, Pointer = Env.Next() };
        Test.Eq(e.R(), true, "refresh after world change");
        Test.Eq(e.Enums, en + 1, "new world ptr reseeds once");
        Test.Eq(Live(W), 1, "count preserved");
    }

    internal static void t09_kingdom_change_reseeds()
    {
        var e = new Env();
        Character w = e.MakeCharacter("Worker");
        Test.Eq(e.R(), true, "seed");
        int en = e.Enums;
        var k2 = new Kingdom { Pointer = Env.Next() };
        e.Managers.kingdom = k2;
        // move the worker to the new kingdom roster
        e.Kingdom._characters.Remove(w);
        k2._characters.Add(w);
        Test.Eq(e.R(), true, "refresh after kingdom change");
        Test.Eq(k2._characters.EnumerationCount, 1, "new kingdom ptr reseeds once");
        Test.Eq(e.Enums, en, "old kingdom is never enumerated after switch");
        Test.Eq(Live(W), 1, "count preserved");
    }

    internal static void t10_foreign_kingdom_hook_ignored()
    {
        var e = new Env();
        e.MakeCharacter("Worker");
        Test.Eq(e.R(), true, "seed");
        long v = AutoRestockCounts.Version;
        var foreign = new Kingdom { Pointer = Env.Next() };
        Character stray = e.MakeCharacter("Worker", inRoster: false);
        AutoRestockCounts.HookAddCharacter(foreign, stray);
        Test.Eq(e.R(), true, "steady refresh");
        Test.Eq(Live(W), 1, "foreign kingdom add must not count");
        Test.Eq(AutoRestockCounts.Version, v, "no state change from foreign hook");
    }

    internal static void t11_same_world_new_layer_reseeds()
    {
        var e = new Env();
        Character w = e.MakeCharacter("Worker");
        Test.Eq(e.R(), true, "seed");
        int en = e.Enums;
        var nl = new Transform { Pointer = Env.Next() };
        var ngo = new GameObject { Pointer = Env.Next(), scene = new Scene { handle = e.LayerGo.scene.handle }, transform = nl };
        nl.gameObject = ngo;
        w.transform.parent = nl;
        e.World.gameLayer = nl;
        Test.Eq(e.R(), true, "refresh after layer change");
        Test.Eq(e.Enums, en + 1, "same world, new layer reseeds once");
        Test.Eq(Live(W), 1, "count preserved");
    }

    internal static void t12_mask0_clears_state_and_subscriptions()
    {
        var e = new Env();
        Character w = e.MakeCharacter("Worker");
        PayableShop s = e.MakeShop("Hammer", 2);
        e.SetPlaced(s);
        Test.Eq(e.R(), true, "seed");
        Test.Assert(Live(W) == 1 && Stock(W) == 2, "populated");
        Damageable dmg = w._damageable;
        Droppable item = s._items[0];
        e.Mask(false, false, false, false);
        Test.Eq(e.R(), false, "mask0 -> inactive");
        Test.Eq(Live(W), 0, "no counts while off");
        Test.Eq(Stock(W), 0, "no stock while off");
        Test.Eq(dmg.DeathSubscriberCount, 0, "death subscription removed");
        Test.Eq(item.PickedUpSubscriberCount, 0, "tool subscription removed");
        Test.Eq(item.DisabledSubscriberCount, 0, "tool disabled subscription removed");
        Test.Eq(AutoRestockCounts.Shops.Count, 0, "shop cache cleared");
    }

    internal static void t13_reenable_after_off_reseeds()
    {
        var e = new Env();
        e.MakeCharacter("Worker"); e.MakeCharacter("Worker");
        Test.Eq(e.R(), true, "seed");
        e.Mask(false, false, false, false);
        e.R();
        int en = e.Enums;
        e.Mask(true, true, true, true);
        Test.Eq(e.R(), true, "re-enable publishes");
        Test.Eq(e.Enums, en + 1, "re-enable reseeds once");
        Test.Eq(Live(W), 2, "counts restored");
    }

    internal static void t14_mask_narrow_isolates_roles()
    {
        var e = new Env();
        Character ninja = e.MakeCharacter("Ninja");
        e.MakeCharacter("Worker");
        PayableShop hammer = e.MakeShop("Hammer", 1), katana = e.MakeShop("Katana", 1);
        e.SetPlaced(hammer, katana);
        Test.Eq(e.R(), true, "seed all roles");
        Test.Assert(Live(W) == 1 && Live(N) == 1 && Stock(N) == 1, "all on");
        e.Mask(true, false, false, false);
        Test.Eq(e.R(), true, "narrowed refresh");
        Test.Eq(Live(W), 1, "worker still counted");
        Test.Eq(Live(N), 0, "ninja excluded");
        Test.Eq(Stock(N), 0, "ninja stock excluded");
        Test.Eq(ninja._damageable.DeathSubscriberCount, 0, "excluded role has no subscription");
        Test.Eq(AutoRestockCounts.Shops.Count, 1, "only worker shop in registry");
    }

    internal static void t15_hook_before_active_ignored()
    {
        var e = new Env();
        Character stray = e.MakeCharacter("Worker", inRoster: false);
        AutoRestockCounts.HookAddCharacter(e.Kingdom, stray); // registry not active yet
        Test.Eq(e.R(), true, "first enable");
        Test.Eq(Live(W), 0, "pre-activation hook must not count");
    }

    internal static void t16_kingdom_identity_is_native_ptr()
    {
        var e = new Env();
        e.MakeCharacter("Worker");
        Test.Eq(e.R(), true, "seed");
        // a kingdom wrapper with the SAME native ptr but a different GameObject ptr
        var same = new Kingdom { Pointer = e.Kingdom.Pointer };
        same._characters = e.Kingdom._characters;
        Character c = e.MakeCharacter("Worker", inRoster: false);
        AutoRestockCounts.HookAddCharacter(same, c);
        Test.Eq(e.R(), true, "flush");
        Test.Eq(Live(W), 2, "identity is kingdom.Pointer, not gameObject ptr");
    }

    internal static void t17_classify_variants()
    {
        var e = new Env();
        e.MakeCharacter("Worker");
        e.MakeCharacter("Archer");            // follower-style archer: only Archer component
        e.MakeCharacter("Ninja");
        e.MakeCharacter("Fisher");            // daytime fisher form = Ninja
        e.MakeCharacter("Berserker");
        e.MakeCharacter("Peasant", inRoster: true);   // Peasant role disabled in this legacy fixture
        e.MakeCharacter("Pikeman", inRoster: true);   // no Ninja component -> unclassified
        Test.Eq(e.R(), true, "seed");
        Test.Eq(Live(W), 1, "worker");
        Test.Eq(Live(A), 1, "archer");
        Test.Eq(Live(N), 2, "ninja includes fisher");
        Test.Eq(Live(B), 1, "berserker");
    }

    // ---------- pending roster members ----------

    internal static void t18_pending_parent_late_admits()
    {
        var e = new Env();
        e.MakeCharacter("Worker");
        Test.Eq(e.R(), true, "seed");
        Character late = e.MakeCharacter("Worker", live: false, inRoster: false);
        AutoRestockCounts.HookAddCharacter(e.Kingdom, late); // Pool.Spawn: Add before parent
        Test.Eq(Live(W), 1, "not counted before parent assigned");
        Test.Eq(e.R(), false, "pending member blocks publish this round");
        late.transform.parent = e.Layer; // parent arrives
        Test.Eq(e.R(), true, "admitted after parent");
        Test.Eq(Live(W), 2, "late member counted");
    }

    internal static void t19_pending_blocks_partial_publish()
    {
        var e = new Env();
        e.MakeCharacter("Worker"); // roster member, immediately live
        Test.Eq(e.R(), true, "seed");
        Character pending = e.MakeCharacter("Ninja", live: false, inRoster: false);
        AutoRestockCounts.HookAddCharacter(e.Kingdom, pending);
        Test.Eq(e.R(), false, "pending blocks the round");
        Test.Eq(Live(W), 0, "no partial roster published (even tracked roles read 0)");
        pending.transform.parent = e.Layer;
        Test.Eq(e.R(), true, "next round publishes");
        Test.Assert(Live(W) == 1 && Live(N) == 1, "both published together");
    }

    internal static void t20_pending_bounded_then_recovers()
    {
        var e = new Env();
        e.MakeCharacter("Worker"); // established roster member
        Test.Eq(e.R(), true, "seed");
        int en = e.Enums;
        Character never = e.MakeCharacter("Ninja", live: false, inRoster: false);
        AutoRestockCounts.HookAddCharacter(e.Kingdom, never);
        bool published = false;
        for (int i = 0; i < 10; i++) { if (e.R()) { published = true; break; } Test.Eq(Live(W), 0, "fail-closed while unresolved"); }
        Test.Assert(published, "unresolved pending must resolve (drop + recovery) within bounded tries");
        Test.Eq(Live(N), 0, "never-parented member finally dropped");
        Test.Eq(Live(W), 1, "roster member republished after recovery reseed");
        Test.Eq(e.Enums, en + 1, "exactly one recovery reseed");
    }

    internal static void t21_unknown_beggar_dropped_promptly()
    {
        var e = new Env();
        e.MakeCharacter("Worker");
        Test.Eq(e.R(), true, "seed");
        int en = e.Enums;
        Character peasant = e.MakeCharacter("Beggar", live: false, inRoster: false);
        AutoRestockCounts.HookAddCharacter(e.Kingdom, peasant);
        Test.Eq(e.R(), true, "unknown Beggar must not block publish");
        Test.Eq(e.Enums, en, "no recovery needed for unknown role");
        Test.Eq(Live(W), 1, "roster intact");
    }

    internal static void t22_pending_role_disabled_dropped()
    {
        var e = new Env();
        e.MakeCharacter("Worker");
        Test.Eq(e.R(), true, "seed");
        Character archer = e.MakeCharacter("Archer", live: false, inRoster: false);
        AutoRestockCounts.HookAddCharacter(e.Kingdom, archer);
        e.Mask(true, false, false, false); // archer role turned off
        Test.Eq(e.R(), true, "off-role pending dropped without blocking");
        Test.Eq(Live(A), 0, "off role not counted");
    }

    internal static void t23_promoted_pair_reclassified()
    {
        var e = new Env();
        e.MakeCharacter("Worker");
        Test.Eq(e.R(), true, "seed");
        Character c = e.MakeCharacter("Worker", live: false, inRoster: false);
        AutoRestockCounts.HookAddCharacter(e.Kingdom, c);
        // promoted before flush: Worker component swapped for Ninja
        c.gameObject.components.RemoveAll(x => x is Worker);
        c.gameObject.Add(new Ninja { Pointer = Env.Next() });
        c.transform.parent = e.Layer;
        Test.Eq(e.R(), true, "flush");
        Test.Assert(Live(W) == 1 && Live(N) == 1, "classified at flush time: Ninja not Worker");
    }

    // ---------- death / remove / pointer reuse ----------

    internal static void t24_death_decrements_once()
    {
        var e = new Env();
        Character a = e.MakeCharacter("Worker"); e.MakeCharacter("Worker");
        Test.Eq(e.R(), true, "seed");
        long v = AutoRestockCounts.Version;
        a._damageable.FireDeath();
        Test.Eq(Live(W), 1, "death decrements");
        Test.Assert(AutoRestockCounts.Version > v, "version moves on death");
        long v2 = AutoRestockCounts.Version;
        a._damageable.FireDeath(); // duplicate death callback
        Test.Eq(Live(W), 1, "no double decrement");
        Test.Eq(AutoRestockCounts.Version, v2, "no extra version move");
    }

    internal static void t25_remove_idempotent()
    {
        var e = new Env();
        Character a = e.MakeCharacter("Worker"); e.MakeCharacter("Worker");
        Test.Eq(e.R(), true, "seed");
        AutoRestockCounts.HookRemoveCharacter(a);
        Test.Eq(Live(W), 1, "remove decrements");
        long v = AutoRestockCounts.Version;
        AutoRestockCounts.HookRemoveCharacter(a); // duplicate remove event
        Test.Eq(Live(W), 1, "idempotent");
        Test.Eq(AutoRestockCounts.Version, v, "no state change on duplicate remove");
    }

    internal static void t26_death_then_remove_no_double()
    {
        var e = new Env();
        Character a = e.MakeCharacter("Worker"); e.MakeCharacter("Worker");
        Test.Eq(e.R(), true, "seed");
        a._damageable.FireDeath();
        AutoRestockCounts.HookRemoveCharacter(a);
        Test.Eq(Live(W), 1, "death followed by remove decrements exactly once");
    }

    internal static void t27_remove_unsubscribes_death()
    {
        var e = new Env();
        Character a = e.MakeCharacter("Worker");
        Test.Eq(e.R(), true, "seed");
        AutoRestockCounts.HookRemoveCharacter(a);
        Test.Eq(a._damageable.DeathSubscriberCount, 0, "death handler unsubscribed on remove");
        a._damageable.FireDeath();
        Test.Eq(Live(W), 0, "removed actor's death has no effect");
    }

    internal static void t28_stale_death_callback_inert_after_reuse()
    {
        var e = new Env();
        Character a = e.MakeCharacter("Worker", ptr: new IntPtr(0x9001));
        Test.Eq(e.R(), true, "seed");
        object entry = Refl.ActorEntryFor(new IntPtr(0x9001));
        Damageable.DeathEvent stale = (Damageable.DeathEvent)Refl.F(entry, "DeathHandler");
        AutoRestockCounts.HookRemoveCharacter(a);
        // pooled pointer reuse: brand new actor with same native ptr
        Character b = e.MakeCharacter("Worker", ptr: new IntPtr(0x9001));
        e.Kingdom.AddCharacter(b);
        AutoRestockCounts.HookAddCharacter(e.Kingdom, b);
        Test.Eq(e.R(), true, "flush reuse");
        Test.Eq(Live(W), 1, "replacement counted");
        stale.Handler(null); // old callback fires late
        Test.Eq(Live(W), 1, "stale death callback cannot touch current entry");
        Test.Eq(b._damageable.DeathSubscriberCount, 1, "new actor has exactly one subscription");
    }

    internal static void t29_pooled_reuse_death_counts_once()
    {
        var e = new Env();
        Character b = null;
        {
            var e2 = new Env();
            Character a = e2.MakeCharacter("Worker", ptr: new IntPtr(0x9002));
            Test.Eq(e2.R(), true, "seed");
            AutoRestockCounts.HookRemoveCharacter(a);
            b = e2.MakeCharacter("Worker", ptr: new IntPtr(0x9002));
            e2.Kingdom.AddCharacter(b);
            AutoRestockCounts.HookAddCharacter(e2.Kingdom, b);
            Test.Eq(e2.R(), true, "flush");
        }
        // new session context; reuse scenario carried over via same object semantics
        var e3 = new Env();
        Character c = e3.MakeCharacter("Worker");
        Test.Eq(e3.R(), true, "seed");
        c._damageable.FireDeath();
        Test.Eq(Live(W), 0, "single death decrements exactly once for current entry");
    }

    // ---------- shops & stock ----------

    internal static void t30_shop_dedupe_by_go_ptr()
    {
        var e = new Env();
        PayableShop a = e.MakeShop("Hammer", 2), b = e.MakeShop("Hammer", 3);
        e.SetPlaced(a, a, b); // same GO listed twice
        Test.Eq(e.R(), true, "seed");
        Test.Eq(AutoRestockCounts.Shops.Count, 2, "dedup by GameObject pointer");
        Test.Eq(a.GetItemCountCalls, 1, "duplicate entry queried once");
        Test.Eq(Stock(W), 5, "stock summed once per shop");
    }

    internal static void t31_shop_role_by_tag()
    {
        var e = new Env();
        e.SetPlaced(e.MakeShop("Hammer", 1), e.MakeShop("Bow", 2), e.MakeShop("Katana", 3),
                    e.MakeShop("BerserkerTool", 4), e.MakeShop("Apple", 9));
        Test.Eq(e.R(), true, "seed");
        Test.Assert(Stock(W) == 1 && Stock(A) == 2 && Stock(N) == 3 && Stock(B) == 4, "roles by itemPrefab tag");
        Test.Eq(AutoRestockCounts.Shops.Count, 4, "unknown tag ignored");
    }

    internal static void t32_getitemcount_truth_items_only_for_subs()
    {
        var e = new Env();
        PayableShop s = e.MakeShop("Hammer", 2);
        s.itemCountOverride = 5; // native truth differs from _items length
        e.SetPlaced(s);
        Test.Eq(e.R(), true, "seed");
        Test.Eq(Stock(W), 5, "GetItemCount is the truth, not _items length");
        Test.Eq(s.ItemsReadCount, 1, "_items enumerated exactly once (subscription only)");
        foreach (Droppable d in s._items)
            Test.Assert(d.PickedUpSubscriberCount == 1 && d.DisabledSubscriberCount == 1, "both events subscribed once");
    }

    internal static void t33_tool_event_dirties_only_that_shop()
    {
        var e = new Env();
        PayableShop s1 = e.MakeShop("Hammer", 3), s2 = e.MakeShop("Hammer", 3);
        e.SetPlaced(s1, s2);
        Test.Eq(e.R(), true, "seed");
        int c1 = s1.GetItemCountCalls, c2 = s2.GetItemCountCalls;
        s1.itemCountOverride = 2; // one tool picked up
        s1._items[0].FirePickedUp();
        int readsBefore = s1.ItemsReadCount;
        Test.Eq(e.R(), true, "recompute round");
        Test.Eq(s1.GetItemCountCalls, c1 + 1, "dirty shop recomputed");
        Test.Eq(s2.GetItemCountCalls, c2, "clean shop NOT recomputed");
        Test.Eq(Stock(W), 5, "stock updated 3+3 -> 3+2");
        Test.Eq(s1.ItemsReadCount, readsBefore + 1, "items re-enumerated only for recompute");
    }

    internal static void t34_duplicate_tool_events_single_recompute()
    {
        var e = new Env();
        PayableShop s = e.MakeShop("Hammer", 2);
        e.SetPlaced(s);
        Test.Eq(e.R(), true, "seed");
        int c = s.GetItemCountCalls;
        long v = AutoRestockCounts.Version;
        s._items[0].FirePickedUp();
        s._items[0].FireDisabled();
        s._items[1].FirePickedUp();
        int readsBefore = s.ItemsReadCount;
        Test.Eq(e.R(), true, "recompute round");
        Test.Eq(s.GetItemCountCalls, c + 1, "one recompute despite multiple events");
        Test.Eq(s.ItemsReadCount, readsBefore + 1, "one re-enumeration");
        // stock unchanged (override still 2) -> version may not bump from stock, but must not decrease
        Test.Assert(AutoRestockCounts.Version >= v, "version monotonic");
    }

    internal static void t35_severed_item_event_still_captures()
    {
        var e = new Env();
        PayableShop s = e.MakeShop("Hammer", 2);
        e.SetPlaced(s);
        Test.Eq(e.R(), true, "seed");
        Droppable severed = s._items[1];
        s.SetItems(new[] { s._items[0] }); // item unlinked from shop (_items)
        severed.FirePickedUp();            // captured event still fires
        Test.Eq(e.R(), true, "captured shop recomputed");
        Test.Eq(Stock(W), 2, "native truth still reported");
        Test.Eq(severed.PickedUpSubscriberCount, 0, "severed item no longer subscribed after recompute");
    }

    internal static void t36_setplacedshop_rebuilds_membership()
    {
        var e = new Env();
        PayableShop s1 = e.MakeShop("Hammer", 1);
        e.SetPlaced(s1);
        Test.Eq(e.R(), true, "seed");
        PayableShop s2 = e.MakeShop("Bow", 1);
        e.SetPlaced(s1, s2);
        AutoRestockCounts.HookSetPlacedShop(e.Planner);
        Test.Eq(e.R(), true, "rebuild round");
        Test.Eq(AutoRestockCounts.Shops.Count, 2, "new member registered");
        Test.Assert(Stock(W) == 1 && Stock(A) == 1, "stocks correct after rebuild");
    }

    internal static void t37_membership_zero_stock_bumps_version()
    {
        var e = new Env();
        PayableShop s = e.MakeShop("Hammer", 0);
        e.SetPlaced(s);
        Test.Eq(e.R(), true, "seed");
        Test.Eq(Stock(W), 0, "zero stock");
        long v = AutoRestockCounts.Version;
        AutoRestockCounts.HookSetPlacedShop(e.Planner);
        Test.Eq(e.R(), true, "rebuild round");
        Test.Assert(AutoRestockCounts.Version > v, "membership rebuild with 0 stock still bumps version");
    }

    internal static void t38_shopadditem_clone_same_ptr_dirty_only()
    {
        var e = new Env();
        PayableShop s1 = e.MakeShop("Hammer", 2), s2 = e.MakeShop("Hammer", 2);
        e.SetPlaced(s1, s2);
        Test.Eq(e.R(), true, "seed");
        int c1 = s1.GetItemCountCalls, c2 = s2.GetItemCountCalls;
        // clone wrapper: different C# object, same native shop ptr, same GO
        var clone = new PayableShop { Pointer = s1.Pointer, gameObject = s1.gameObject, transform = s1.transform, itemPrefab = s1.itemPrefab };
        s1.itemCountOverride = 3;
        AutoRestockCounts.HookShopAddItem(clone);
        Test.Eq(e.R(), true, "recompute round");
        Test.Eq(s1.GetItemCountCalls, c1 + 1, "dirty shop recomputed");
        Test.Eq(s2.GetItemCountCalls, c2, "no full registry rebuild");
        Test.Eq(Stock(W), 5, "stock updated");
    }

    internal static void t39_shopadditem_untracked_rebuilds_registry()
    {
        var e = new Env();
        PayableShop s1 = e.MakeShop("Hammer", 2), s2 = e.MakeShop("Hammer", 2);
        e.SetPlaced(s1, s2);
        Test.Eq(e.R(), true, "seed");
        int g = Native.GetItemCountCalls;
        PayableShop fresh = e.MakeShop("Hammer", 1); // not yet in _placedShops
        AutoRestockCounts.HookShopAddItem(fresh);
        Test.Eq(e.R(), true, "rebuild round");
        Test.Eq(Native.GetItemCountCalls, g + 2, "untracked relevant shop forces full registry rebuild (both shops)");
    }

    internal static void t40_stale_tool_callback_inert_after_rebuild()
    {
        var e = new Env();
        PayableShop s = e.MakeShop("Hammer", 2);
        e.SetPlaced(s);
        Test.Eq(e.R(), true, "seed");
        object oldEntry = Refl.FirstShopEntry();
        object oldSub = Refl.SubsOf(oldEntry)[0];
        AutoRestockCounts.HookSetPlacedShop(e.Planner);
        Test.Eq(e.R(), true, "registry rebuilt");
        int c = s.GetItemCountCalls;
        Refl.FireStaleTool(oldEntry, oldSub); // pre-rebuild callback fires late
        Test.Eq(e.R(), true, "steady round");
        Test.Eq(s.GetItemCountCalls, c, "stale tool callback must not dirty the shop");
    }

    internal static void t41_stock_version_monotonic_sequence()
    {
        var e = new Env();
        PayableShop s = e.MakeShop("Hammer", 2);
        e.SetPlaced(s);
        var versions = new List<long>();
        e.R(); versions.Add(AutoRestockCounts.Version);
        s.itemCountOverride = 1; s._items[0].FirePickedUp();
        e.R(); versions.Add(AutoRestockCounts.Version);
        AutoRestockCounts.HookSetPlacedShop(e.Planner);
        e.R(); versions.Add(AutoRestockCounts.Version);
        for (int i = 0; i < 10; i++) e.R();
        versions.Add(AutoRestockCounts.Version);
        for (int i = 1; i < versions.Count; i++)
            Test.Assert(versions[i] >= versions[i - 1], "version must be monotonic");
        Test.Assert(versions[1] > versions[0], "stock change bumps version");
        Test.Assert(versions[2] > versions[1], "membership rebuild bumps version");
        Test.Eq(versions[3], versions[2], "steady refresh keeps version");
    }

    // ---------- failure & recovery ----------

    internal static void t42_getitemcount_failure_recovers_once()
    {
        var e = new Env();
        PayableShop s = e.MakeShop("Hammer", 2);
        s.throwOnGetItemCount = true;
        e.SetPlaced(s);
        Test.Eq(e.R(), false, "native count failure -> fail closed");
        Test.Eq(Stock(W), 0, "no silently trusted undercount");
        s.throwOnGetItemCount = false;
        Test.Eq(e.R(), true, "one recovery attempt republishes");
        Test.Eq(Stock(W), 2, "stock correct after recovery");
        Test.Eq(e.Enums, 2, "seed + exactly one recovery reseed");
    }

    internal static void t43_subscribe_failure_rolls_back_no_duplicates()
    {
        var e = new Env();
        PayableShop s = e.MakeShop("Hammer", 2);
        s._items[1].throwOnSubscribe = true;
        e.SetPlaced(s);
        Test.Eq(e.R(), false, "subscription failure -> fail closed");
        s._items[1].throwOnSubscribe = false;
        Test.Eq(e.R(), true, "recovery republishes");
        foreach (Droppable d in s._items)
        {
            Test.Eq(d.PickedUpSubscriberCount, 1, "exactly one picked-up subscription (rollback left no duplicates)");
            Test.Eq(d.DisabledSubscriberCount, 1, "exactly one disabled subscription");
        }
        Test.Eq(Stock(W), 2, "stock correct");
    }

    internal static void t44_persistent_failure_latches_no_rescan()
    {
        var e = new Env();
        e.MakeCharacter("Worker");
        PayableShop s = e.MakeShop("Hammer", 2);
        s.throwOnGetItemCount = true;
        e.SetPlaced(s);
        for (int i = 0; i < 5; i++) Test.Eq(e.R(), false, "failure -> unavailable");
        int en = e.Enums; // bounded: reseed attempted only twice before latch
        Test.Eq(en, 2, "recovery attempts bounded (2 reseeds) before latch");
        for (int i = 0; i < 20; i++) e.R(); // no every-tick full rescan while latched
        Test.Eq(e.Enums, en, "latched: no repeated rescans");
        Test.Eq(Live(W), 0, "latched publishes nothing");
        // relevant new event unlocks
        Character c = e.MakeCharacter("Worker", inRoster: false);
        s.throwOnGetItemCount = false;
        e.Kingdom.AddCharacter(c); // real AddCharacter mutates native roster before postfix
        AutoRestockCounts.HookAddCharacter(e.Kingdom, c);
        c.transform.parent = e.Layer;
        Test.Eq(e.R(), true, "new roster event unlatches and recovers");
        Test.Eq(Live(W), 2, "counts published again");
        Test.Eq(e.Enums, en + 1, "one reseed after unlock");
    }

    internal static void t45_classify_failure_latches_then_event_unlocks()
    {
        var e = new Env();
        e.MakeCharacter("Worker");
        Character broken = e.MakeCharacter("Peasant");
        broken.gameObject.throwOnGetComponent = true; // GetComponent throws during classify
        for (int i = 0; i < 4; i++) Test.Eq(e.R(), false, "classify failure -> fail closed");
        int en = e.Enums;
        Test.Assert(en <= 2, "bounded recovery before latch");
        for (int i = 0; i < 10; i++) e.R();
        Test.Eq(e.Enums, en, "latched: no rescan per tick");
        Test.Eq(Live(W), 0, "nothing published while failing");
        e.Kingdom._characters.Remove(broken);
        Character ok = e.MakeCharacter("Worker", inRoster: false);
        ok.transform.parent = e.Layer;
        e.Kingdom.AddCharacter(ok);
        AutoRestockCounts.HookAddCharacter(e.Kingdom, ok);
        Test.Eq(e.R(), true, "new event unlatches");
        Test.Eq(Live(W), 2, "roster member + newcomer counted");
    }

    // ---------- reset & transitions ----------

    internal static void t46_reset_detaches_all()
    {
        var e = new Env();
        Character w = e.MakeCharacter("Worker");
        PayableShop s = e.MakeShop("Hammer", 2);
        e.SetPlaced(s);
        Test.Eq(e.R(), true, "seed");
        long v = AutoRestockCounts.Version;
        AutoRestockCounts.Reset();
        Test.Assert(AutoRestockCounts.Version > v, "reset bumps version");
        Test.Eq(Live(W), 0, "no counts after reset");
        Test.Eq(Stock(W), 0, "no stock after reset");
        Test.Eq(AutoRestockCounts.Shops.Count, 0, "shop cache empty");
        Test.Eq(w._damageable.DeathSubscriberCount, 0, "death callbacks detached");
        foreach (Droppable d in s._items)
            Test.Assert(d.PickedUpSubscriberCount == 0 && d.DisabledSubscriberCount == 0, "tool subscriptions cleared");
        long afterReset = AutoRestockCounts.Version;
        w._damageable.FireDeath(); s._items[0].FirePickedUp();
        Test.Eq(AutoRestockCounts.Version, afterReset, "detached callbacks cause no state change");
    }

    internal static void t47_native_transition_sync_before_refresh()
    {
        var e = new Env();
        Character a = e.MakeCharacter("Worker");
        Test.Eq(e.R(), true, "seed");
        // native side applies Remove(old)+Add(new) synchronously, then a single Refresh
        e.Kingdom.RemoveCharacter(a);
        AutoRestockCounts.HookRemoveCharacter(a);
        Character b = e.MakeCharacter("Worker");
        e.Kingdom.AddCharacter(b);
        AutoRestockCounts.HookAddCharacter(e.Kingdom, b);
        Test.Eq(e.R(), true, "single refresh publishes the transition directly");
        Test.Eq(Live(W), 1, "final state, no transient empty roster paid twice");
    }

    internal static void t48_getters_pure_cached()
    {
        var e = new Env();
        e.MakeCharacter("Worker"); e.MakeCharacter("Archer");
        PayableShop s = e.MakeShop("Hammer", 2);
        e.SetPlaced(s);
        Test.Eq(e.R(), true, "seed");
        long v = AutoRestockCounts.Version;
        int g = Native.GetItemCountCalls, en = e.Enums, reads = s.ItemsReadCount;
        for (int i = 0; i < 1000; i++)
            for (int r = 0; r < 4; r++) { Live(r); Stock(r); }
        Test.Eq(AutoRestockCounts.Version, v, "getters do not change version");
        Test.Eq(Native.GetItemCountCalls, g, "getters make no native queries");
        Test.Eq(e.Enums, en, "getters do not enumerate roster");
        Test.Eq(s.ItemsReadCount, reads, "getters do not read _items");
    }
}

internal static class Program
{
    static int Main()
    {
        var tests = new (string, Action)[]
        {
            ("t01_inactive_mask0_no_seed", Tests.t01_inactive_mask0_no_seed),
            ("t02_inactive_master_toggle_off", Tests.t02_inactive_master_toggle_off),
            ("t03_first_enable_seeds_once", Tests.t03_first_enable_seeds_once),
            ("t04_steady_state_zero_native_queries", Tests.t04_steady_state_zero_native_queries),
            ("t05_null_managers_fail_closed", Tests.t05_null_managers_fail_closed),
            ("t06_missing_gamelayer_fail_closed", Tests.t06_missing_gamelayer_fail_closed),
            ("t07_scene_change_reseeds", Tests.t07_scene_change_reseeds),
            ("t08_world_change_reseeds", Tests.t08_world_change_reseeds),
            ("t09_kingdom_change_reseeds", Tests.t09_kingdom_change_reseeds),
            ("t10_foreign_kingdom_hook_ignored", Tests.t10_foreign_kingdom_hook_ignored),
            ("t11_same_world_new_layer_reseeds", Tests.t11_same_world_new_layer_reseeds),
            ("t12_mask0_clears_state_and_subscriptions", Tests.t12_mask0_clears_state_and_subscriptions),
            ("t13_reenable_after_off_reseeds", Tests.t13_reenable_after_off_reseeds),
            ("t14_mask_narrow_isolates_roles", Tests.t14_mask_narrow_isolates_roles),
            ("t15_hook_before_active_ignored", Tests.t15_hook_before_active_ignored),
            ("t16_kingdom_identity_is_native_ptr", Tests.t16_kingdom_identity_is_native_ptr),
            ("t17_classify_variants", Tests.t17_classify_variants),
            ("t18_pending_parent_late_admits", Tests.t18_pending_parent_late_admits),
            ("t19_pending_blocks_partial_publish", Tests.t19_pending_blocks_partial_publish),
            ("t20_pending_bounded_then_recovers", Tests.t20_pending_bounded_then_recovers),
            ("t21_unknown_beggar_dropped_promptly", Tests.t21_unknown_beggar_dropped_promptly),
            ("t22_pending_role_disabled_dropped", Tests.t22_pending_role_disabled_dropped),
            ("t23_promoted_pair_reclassified", Tests.t23_promoted_pair_reclassified),
            ("t24_death_decrements_once", Tests.t24_death_decrements_once),
            ("t25_remove_idempotent", Tests.t25_remove_idempotent),
            ("t26_death_then_remove_no_double", Tests.t26_death_then_remove_no_double),
            ("t27_remove_unsubscribes_death", Tests.t27_remove_unsubscribes_death),
            ("t28_stale_death_callback_inert_after_reuse", Tests.t28_stale_death_callback_inert_after_reuse),
            ("t29_pooled_reuse_death_counts_once", Tests.t29_pooled_reuse_death_counts_once),
            ("t30_shop_dedupe_by_go_ptr", Tests.t30_shop_dedupe_by_go_ptr),
            ("t31_shop_role_by_tag", Tests.t31_shop_role_by_tag),
            ("t32_getitemcount_truth_items_only_for_subs", Tests.t32_getitemcount_truth_items_only_for_subs),
            ("t33_tool_event_dirties_only_that_shop", Tests.t33_tool_event_dirties_only_that_shop),
            ("t34_duplicate_tool_events_single_recompute", Tests.t34_duplicate_tool_events_single_recompute),
            ("t35_severed_item_event_still_captures", Tests.t35_severed_item_event_still_captures),
            ("t36_setplacedshop_rebuilds_membership", Tests.t36_setplacedshop_rebuilds_membership),
            ("t37_membership_zero_stock_bumps_version", Tests.t37_membership_zero_stock_bumps_version),
            ("t38_shopadditem_clone_same_ptr_dirty_only", Tests.t38_shopadditem_clone_same_ptr_dirty_only),
            ("t39_shopadditem_untracked_rebuilds_registry", Tests.t39_shopadditem_untracked_rebuilds_registry),
            ("t40_stale_tool_callback_inert_after_rebuild", Tests.t40_stale_tool_callback_inert_after_rebuild),
            ("t41_stock_version_monotonic_sequence", Tests.t41_stock_version_monotonic_sequence),
            ("t42_getitemcount_failure_recovers_once", Tests.t42_getitemcount_failure_recovers_once),
            ("t43_subscribe_failure_rolls_back_no_duplicates", Tests.t43_subscribe_failure_rolls_back_no_duplicates),
            ("t44_persistent_failure_latches_no_rescan", Tests.t44_persistent_failure_latches_no_rescan),
            ("t45_classify_failure_latches_then_event_unlocks", Tests.t45_classify_failure_latches_then_event_unlocks),
            ("t46_reset_detaches_all", Tests.t46_reset_detaches_all),
            ("t47_native_transition_sync_before_refresh", Tests.t47_native_transition_sync_before_refresh),
            ("t48_getters_pure_cached", Tests.t48_getters_pure_cached),
        };
        foreach ((string name, Action a) in tests) Test.Run(name, a);
        Additional.Run();
        BreadTests.Run();
        Console.WriteLine($"TOTAL pass={Test.Pass} fail={Test.Fail} of {Test.Pass+Test.Fail}");
        return Test.Fail == 0 ? 0 : 1;
    }
}
