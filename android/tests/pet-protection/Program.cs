// Issue #194 Android 保护 host 场景检查：直接驱动薄 handler（AndroidPetProtectionHooks）与两份
// 共享生产源（PatchRoles_PetGuard.cs / PatchRoles_Hermit.cs）的 ANDROID 预处理输出。
// 只覆盖保护控制流（plan「验证」第 2 条矩阵）、Android 未 Bound 修复与双组件唯一 Dog receipt
// 归属两类场景；召回不在此列（生产源里已由 #if !ANDROID 隔离，替身也没有任何召回 API）。
// Tick 顺序与 Probe 相同：HermitPolicy.Tick() → Policy.Tick()。
// host 只证控制流，不冒充 native/Unity/Harmony 运行证据。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KingdomEnhancedMod;
using UnityEngine;
using Policy = KingdomEnhancedMod.PatchRoles_PetGuard;
using HermitPolicy = KingdomEnhancedMod.PatchRoles_Hermit;

static class Program
{
    static int passed, failed, assertions;
    static IDictionary Tracked(Type type) => (IDictionary)type.GetField("Tracked", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    static Logger Log => KingdomEnhancedPlugin.Instance.LogSource;

    static void Eq<T>(T expected, T actual, string label)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{label}: expected {expected}, got {actual}");
    }
    static void Check(bool value, string label) { assertions++; if (!value) throw new Exception(label); }

    static void ResetType(Type type)
    {
        foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (field.IsInitOnly || field.IsLiteral) continue;
            if (field.FieldType == typeof(bool)) field.SetValue(null, false);
            else if (field.FieldType == typeof(int)) field.SetValue(null, 0);
            else if (field.FieldType == typeof(float)) field.SetValue(null, 0f);
            else if (field.FieldType == typeof(IntPtr)) field.SetValue(null, IntPtr.Zero);
        }
    }

    static void Test(string name, Action action)
    {
        Tracked(typeof(Policy)).Clear();
        Tracked(typeof(HermitPolicy)).Clear();
        ResetType(typeof(Policy));
        ResetType(typeof(HermitPolicy));
        Managers.Inst = new Managers();
        NetworkBigBoss.HasWorldAuth = true;
        ModConfig.PetGuardEnabled = new ModConfig.Setting<bool>(true);
        Time.unscaledTime = 0;
        KingdomEnhancedPlugin.ResetForTest();
        try { action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }

    static Droppable NewDroppable()
    {
        var go = new GameObject();
        go.transform.parent = Managers.Inst.world.gameLayer;
        go.scene = Managers.Inst.world.gameLayer.gameObject.scene;
        return go.AddComponent<Droppable>();
    }

    static Droppable NewDog(PickUpPolicy policy = PickUpPolicy.Anybody)
    {
        var d = NewDroppable();
        d.NativePolicy(policy); d.NativeOriginal(policy);
        d.gameObject.AddComponent<Dog>();
        return d;
    }

    static Droppable NewHermit(PickUpPolicy policy = PickUpPolicy.Anybody)
    {
        var d = NewDroppable();
        d.NativePolicy(policy); d.NativeOriginal(policy);
        d.gameObject.AddComponent<Hermit>();
        return d;
    }

    // 生产环境：OnEnable 两个 postfix / OnDisable 两个 prefix 由 Probe 注册的薄 handler 聚合；
    // 场景只经薄 handler 调用（不镜像算法）。
    static void Enable(Droppable d) { d.OnEnable(); AndroidPetProtectionHooks.OnEnablePostfix(d); }
    static void Disable(Droppable d) { AndroidPetProtectionHooks.OnDisablePrefix(d); d.OnDisable(); }
    // 生产驱动：Probe 的 Update 每帧调两个共享 Tick。
    static void Tick(float time) { Time.unscaledTime = time; HermitPolicy.Tick(); Policy.Tick(); }
    static void Parent(Droppable d, Transform parent) => d.transform.parent = parent;
    static void MoveToWorld(Droppable d) { Parent(d, Managers.Inst.world.gameLayer); d.gameObject.scene = Managers.Inst.world.gameLayer.gameObject.scene; }
    static void PoolEnableOutOfWorld(Droppable d) { d.gameObject.scene = new Scene { handle = 999 }; d.transform.parent = null; }
    static Transform NewBoatBody(out Boat boat)
    {
        var boatGo = new GameObject();
        boatGo.transform.parent = Managers.Inst.world.gameLayer;
        boat = boatGo.AddComponent<Boat>();
        // 狗在船上时挂在船根之下（Boat 组件在任一祖先上即算在船）；替身只建模这一祖先判定。
        var bodyGo = new GameObject();
        bodyGo.transform.parent = boatGo.transform;
        return bodyGo.transform;
    }

    static void Main()
    {
        Test("Android handler surface: two static Droppable methods, no scan attribute, no exception handler", () =>
        {
            var t = typeof(AndroidPetProtectionHooks);
            var methods = t.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            Eq(2, methods.Length, "exactly two handler methods");
            Check(methods.All(m => m.ReturnType == typeof(void)), "void returns");
            Check(methods.All(m => m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Droppable)), "(Droppable) parameters");
            Check(methods.Select(m => m.Name).OrderBy(n => n).SequenceEqual(new[] { "OnDisablePrefix", "OnEnablePostfix" }), "expected names");
            foreach (var m in methods)
            {
                Check(m.GetCustomAttributes(false).All(a => a.GetType().Name != "HarmonyPatch"), m.Name + " carries no HarmonyPatch scan attribute");
                Check(m.GetMethodBody().ExceptionHandlingClauses.Count == 0, m.Name + " has no catch/finalizer clause");
            }
            Check(t.GetCustomAttributes(false).All(a => a.GetType().Name != "HarmonyPatch"), "handler type carries no HarmonyPatch attribute");
            Eq(0, t.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly).Length, "no handler state");
        });

        Test("Shared long-lifecycle hook pair intact: OnEnable postfix / OnDisable prefix on Droppable", () =>
        {
            foreach (var h in new[] { typeof(Droppable_OnEnable_HermitPickupPolicy_Patch), typeof(Droppable_OnEnable_PetGuard_Patch) })
            {
                var attr = h.GetCustomAttributes(typeof(HarmonyPatch), false).Cast<HarmonyPatch>().Single();
                Check(attr.Target == typeof(Droppable) && attr.Method == "OnEnable", h.Name + " targets Droppable.OnEnable");
                var postfix = h.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
                Check(postfix != null && postfix.GetCustomAttributes(typeof(HarmonyPostfix), false).Length == 1, h.Name + " exposes a HarmonyPostfix");
            }
            foreach (var h in new[] { typeof(Droppable_OnDisable_HermitPickupPolicy_Patch), typeof(Droppable_OnDisable_PetGuard_Patch) })
            {
                var attr = h.GetCustomAttributes(typeof(HarmonyPatch), false).Cast<HarmonyPatch>().Single();
                Check(attr.Target == typeof(Droppable) && attr.Method == "OnDisable", h.Name + " targets Droppable.OnDisable");
                var prefix = h.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
                Check(prefix != null && prefix.GetCustomAttributes(typeof(HarmonyPrefix), false).Length == 1, h.Name + " exposes a HarmonyPrefix");
            }
        });

        Test("Plain droppable registers nothing; hermit droppable protects through the handler", () =>
        {
            var plain = NewDroppable();
            Enable(plain); Tick(.5f);
            Eq(0, Tracked(typeof(Policy)).Count, "no dog receipt for a plain droppable");
            Eq(0, Tracked(typeof(HermitPolicy)).Count, "no hermit receipt for a plain droppable");
            Eq(0, plain.EnemyWrites, "no write for a plain droppable");
            var hermit = NewHermit(PickUpPolicy.EnemyOnly);
            Enable(hermit); Tick(.51f);
            Eq(1, Tracked(typeof(HermitPolicy)).Count, "hermit receipt registered");
            Eq(0, Tracked(typeof(Policy)).Count, "no dog receipt for a hermit");
            Eq(PickUpPolicy.Nobody, hermit.CurrentEnemyPolicy, "hermit protected through the thin handler");
            Eq(1, hermit.EnemyWrites, "single hermit write");
        });

        Test("Android default OFF: receipt registers with zero writes, ON protects once on the same receipt", () =>
        {
            ModConfig.PetGuardEnabled = new ModConfig.Setting<bool>(false); // 真实默认值（MobilePlayerConfig CreateEntry false）
            var d = NewDog();
            Enable(d); Tick(.5f);
            Eq(1, Tracked(typeof(Policy)).Count, "receipt tracking prepared while OFF");
            Eq(0, d.EnemyWrites, "no policy write while OFF");
            ModConfig.PetGuardEnabled.Value = true; Tick(1.01f);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "ON protects the already-tracked receipt");
            Eq(1, d.EnemyWrites, "single write on ON");
        });

        Test("Dog Anybody/EnemyOnly -> Nobody one write; OFF restores exact original; native original/general untouched; disable retires and native resets", () =>
        {
            foreach (var original in new[] { PickUpPolicy.Anybody, PickUpPolicy.EnemyOnly })
            {
                ModConfig.PetGuardEnabled = new ModConfig.Setting<bool>(true);
                var d = NewDog(original);
                Enable(d);
                Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "protected");
                Eq(1, d.EnemyWrites, "single protection write");
                ModConfig.PetGuardEnabled.Value = false; Tick(.5f);
                Eq(original, d.CurrentEnemyPolicy, "exact original restored");
                Eq(2, d.EnemyWrites, "one restore write");
                Eq(original, d._originalEnemyPolicy, "native original unchanged");
                Eq(0, d.OriginalWrites, "never writes the native original slot");
                Eq(0, d.GeneralWrites, "general player pickup untouched");
                Eq(1, Tracked(typeof(Policy)).Count, "receipt retained until OnDisable");
                Disable(d);
                Eq(0, Tracked(typeof(Policy)).Count, "retired after disable");
                Eq(1, d.NativeResetCount, "native OnDisable reset ran");
            }
        });

        Test("Hermit shares the switch: OFF restores the exact original, ON re-protects once", () =>
        {
            var d = NewHermit(PickUpPolicy.EnemyOnly);
            Enable(d);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "hermit protected by the shared switch");
            Eq(1, d.EnemyWrites, "single protection write");
            ModConfig.PetGuardEnabled.Value = false; Tick(.5f);
            Eq(PickUpPolicy.EnemyOnly, d.CurrentEnemyPolicy, "hermit restored");
            Eq(2, d.EnemyWrites, "one restore write");
            ModConfig.PetGuardEnabled.Value = true; Tick(1.01f);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "re-protected on the same receipt");
            Eq(3, d.EnemyWrites, "single re-protection write");
            Eq(0, d.GeneralWrites, "general pickup untouched");
        });

        Test("Duplicate enables and pool generations keep one receipt and its own original", () =>
        {
            var d = NewDog(PickUpPolicy.EnemyOnly);
            Enable(d);
            for (int i = 0; i < 20; i++) Enable(d);
            Eq(1, Tracked(typeof(Policy)).Count, "one receipt");
            Eq(1, d.EnemyWrites, "duplicate enable no extra writes");
            ModConfig.PetGuardEnabled.Value = false; Tick(.5f);
            Eq(PickUpPolicy.EnemyOnly, d.CurrentEnemyPolicy, "original kept through duplicates");
            Eq(2, d.EnemyWrites, "one restore write");
            Disable(d);
            Eq(0, Tracked(typeof(Policy)).Count, "retired on disable");
            Eq(PickUpPolicy.EnemyOnly, d.CurrentEnemyPolicy, "native reset restored its own original");
            d.NativePolicy(PickUpPolicy.EnemyOnly); d.NativeOriginal(PickUpPolicy.EnemyOnly);
            ModConfig.PetGuardEnabled.Value = true;
            Enable(d);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "new pool generation protects");
            ModConfig.PetGuardEnabled.Value = false; Tick(1.01f);
            Eq(PickUpPolicy.EnemyOnly, d.CurrentEnemyPolicy, "new generation captured its own original");
        });

        Test("External policy ownership (contested) is never overwritten or reclaimed", () =>
        {
            var d = NewDog();
            Enable(d);
            Eq(1, d.EnemyWrites, "protected");
            d.NativePolicy(PickUpPolicy.Blocked); // 外部写方接管
            ModConfig.PetGuardEnabled.Value = false; Tick(.5f);
            Eq(PickUpPolicy.Blocked, d.CurrentEnemyPolicy, "external policy preserved");
            ModConfig.PetGuardEnabled.Value = true; Tick(1.01f);
            ModConfig.PetGuardEnabled.Value = false; Tick(1.52f);
            Eq(1, d.EnemyWrites, "contested generation never reclaims or overwrites");
        });

        Test("Client registers a receipt but never reads or writes; host takeover then protects", () =>
        {
            NetworkBigBoss.HasWorldAuth = false;
            Managers.Inst.game.state = Game.State.NetworkClientPlaying;
            var d = NewDog();
            d.ThrowRead = true; // host fault：任何策略读取都会抛出，client 必须零读取
            Enable(d); Tick(.5f);
            Eq(1, Tracked(typeof(Policy)).Count, "client registration");
            Eq(0, d.EnemyReads, "no client reads");
            Eq(0, d.EnemyWrites, "no client writes");
            d.ThrowRead = false;
            NetworkBigBoss.HasWorldAuth = true;
            Managers.Inst.game.state = Game.State.Playing;
            Tick(1.01f);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "host takeover protects");
            Eq(1, d.EnemyWrites, "single write after takeover");
        });

        Test("Uninitialized PetGuard entry is inert instead of throwing", () =>
        {
            ModConfig.PetGuardEnabled = null; // Initialize 之前的真实边界：条目尚不存在
            var d = NewDog();
            Enable(d); Tick(.5f);
            Eq(0, d.EnemyWrites, "no writes with a null entry");
            Check(d.EnemyReads > 0, "tracking still observes the policy while inert (only writes are gated, not zero native access)");
            Eq(1, Tracked(typeof(Policy)).Count, "receipt still registered");
            ModConfig.PetGuardEnabled = new ModConfig.Setting<bool>(true);
            Tick(1.01f);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "protection resumes once the entry exists");
        });

        Test("Loading and unseen pause keep zero writes; bound receipt retires on world change without restore", () =>
        {
            var d = NewDog();
            Enable(d);
            Eq(1, d.EnemyWrites, "protected before the context changes");
            int reads = d.EnemyReads;
            Managers.Inst.game.state = Game.State.Loading; Tick(.5f);
            Eq(reads, d.EnemyReads, "no policy reads while loading");
            Eq(1, d.EnemyWrites, "no writes while loading");
            Eq(1, Tracked(typeof(Policy)).Count, "receipt retained while loading");
            Managers.Inst.world = new World(); // 尚未见过的世界 + 暂停
            Managers.Inst.game.state = Game.State.Menu; Tick(1.01f);
            Eq(reads, d.EnemyReads, "no policy reads on an unseen paused world");
            Eq(1, d.EnemyWrites, "no writes on an unseen paused world");
            Eq(1, Tracked(typeof(Policy)).Count, "receipt retained across the pause");
            Managers.Inst.game.state = Game.State.Playing; Tick(1.52f);
            Eq(0, Tracked(typeof(Policy)).Count, "bound receipt retires on world change");
            Eq(1, d.EnemyWrites, "no restore write on world-change retirement");
        });

        Test("Bound receipt leaving the game layer retires without a restore", () =>
        {
            var d = NewDog();
            Enable(d);
            Parent(d, new GameObject().transform); // 世界层外的挂点
            ModConfig.PetGuardEnabled.Value = false; Tick(.5f);
            Eq(0, Tracked(typeof(Policy)).Count, "retired on layer departure");
            Eq(1, d.EnemyWrites, "never restores outside the world layer");
        });

        Test("Owned dog aboard a boat (Boat component) keeps native Nobody when the switch turns off, restores after leaving", () =>
        {
            var d = NewDog();
            Enable(d);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "protected");
            Parent(d, NewBoatBody(out _));
            ModConfig.PetGuardEnabled.Value = false; Tick(.5f);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "aboard: no restore");
            Eq(1, d.EnemyWrites, "no restore write");
            Eq(1, Tracked(typeof(Policy)).Count, "receipt retained for the deferred restore");
            Parent(d, Managers.Inst.world.gameLayer);
            Tick(1.01f);
            Eq(PickUpPolicy.Anybody, d.CurrentEnemyPolicy, "off boat restores the original");
            Eq(2, d.EnemyWrites, "one restore write");
        });

        Test("BoatBody tag alone also defers the restore until the dog leaves", () =>
        {
            var d = NewDog();
            Enable(d);
            var taggedBoat = new GameObject();
            taggedBoat.transform.parent = Managers.Inst.world.gameLayer;
            var body = new GameObject();
            body.tag = "BoatBody"; // 只靠原生 BoatBody 标签（无 Boat 组件）
            body.transform.parent = taggedBoat.transform;
            Parent(d, body.transform);
            ModConfig.PetGuardEnabled.Value = false; Tick(.5f);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "tagged body defers");
            Eq(1, Tracked(typeof(Policy)).Count, "receipt still retained");
            Parent(d, Managers.Inst.world.gameLayer);
            Tick(1.01f);
            Eq(PickUpPolicy.Anybody, d.CurrentEnemyPolicy, "restored after leaving the tagged body");
            Eq(2, d.EnemyWrites, "one restore write");
        });

        Test("Aboard with the switch off: native OnDisable resets to the native original and retires the receipt", () =>
        {
            var d = NewDog(PickUpPolicy.EnemyOnly);
            Enable(d);
            Parent(d, NewBoatBody(out _));
            ModConfig.PetGuardEnabled.Value = false; Tick(.5f);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "deferred while aboard");
            Eq(1, d.EnemyWrites, "no restore write while aboard");
            Disable(d);
            Eq(0, Tracked(typeof(Policy)).Count, "receipt retired");
            Eq(PickUpPolicy.EnemyOnly, d.CurrentEnemyPolicy, "native reset applied on disable");
            Eq(1, d.NativeResetCount, "native reset ran exactly once");
        });

        Test("Native OnDisable order: optional callback first, then reset; a callback fault means reset not reached", () =>
        {
            var d = NewDog(PickUpPolicy.EnemyOnly);
            Enable(d);
            int callbacks = 0;
            d.NativeDisableCallback = () => callbacks++;
            Disable(d);
            Eq(1, callbacks, "callback invoked once before the reset");
            Eq(1, d.NativeResetCount, "reset after the callback");
            Eq(PickUpPolicy.EnemyOnly, d.CurrentEnemyPolicy, "native original restored");
            Eq(0, Tracked(typeof(Policy)).Count, "receipt retired by the prefix");

            var f = NewDog(PickUpPolicy.EnemyOnly);
            Enable(f);
            f.NativeDisableCallback = () => throw new InvalidOperationException("host-injected callback fault");
            try { AndroidPetProtectionHooks.OnDisablePrefix(f); f.OnDisable(); } catch (InvalidOperationException) { }
            Eq(0, f.NativeResetCount, "reset not reached after the callback fault (modeled per ASM order)");
            Eq(PickUpPolicy.Nobody, f.CurrentEnemyPolicy, "no fake reset: policy stays as the mod left it");
            Eq(1, f.EnemyWrites, "no compensating write after the callback fault");
            Eq(0, Tracked(typeof(Policy)).Count, "prefix still retired the receipt");
        });

        Test("Pool enable in another scene: zero writes and retained dog receipt until real scene+layer binding, then one write", () =>
        {
            var d = NewDog();
            PoolEnableOutOfWorld(d);
            Enable(d);
            Eq(1, Tracked(typeof(Policy)).Count, "identity receipt retained (the PC #else branch would retire here)");
            Eq(0, d.EnemyWrites, "zero writes before binding");
            Eq(0, d.EnemyReads, "zero policy reads before binding");
            Tick(.5f);
            Eq(1, Tracked(typeof(Policy)).Count, "still retained across a tick");
            Eq(0, d.EnemyWrites, "still zero writes");
            MoveToWorld(d);
            Tick(1.01f);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "bound and protected after real scene+layer match");
            Eq(1, d.EnemyWrites, "exactly one write after binding");
        });

        Test("Same Android unbound fix for the hermit source: retained in another scene, one write after binding", () =>
        {
            var d = NewHermit(PickUpPolicy.EnemyOnly);
            PoolEnableOutOfWorld(d);
            Enable(d);
            Eq(1, Tracked(typeof(HermitPolicy)).Count, "hermit identity receipt retained");
            Eq(0, d.EnemyWrites, "zero writes before binding");
            MoveToWorld(d);
            Tick(.5f);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "bound and protected after real scene+layer match");
            Eq(1, d.EnemyWrites, "exactly one write after binding");
        });

        Test("Unbound receipt that never enters the world stays quiet across ticks and retires on OnDisable", () =>
        {
            var d = NewDog();
            PoolEnableOutOfWorld(d);
            Enable(d); Tick(.5f); Tick(1.01f);
            Eq(1, Tracked(typeof(Policy)).Count, "unbound receipt retained");
            Eq(0, d.EnemyWrites, "zero writes");
            Eq(0, d.EnemyReads, "zero policy reads");
            Disable(d);
            Eq(0, Tracked(typeof(Policy)).Count, "retired by the real lifecycle event");
            Eq(0, d.EnemyWrites, "still zero writes");
            Eq(1, d.NativeResetCount, "native reset on disable");
        });

        Test("Policy read fault: one bounded warning, healthy receipts continue, faulty receipt not lost (host-observed)", () =>
        {
            var bad = NewDog(); bad.ThrowRead = true;
            var good = NewDog();
            Enable(bad);
            Eq(1, Log.Warnings.Count, "one bounded warning");
            Enable(good); Tick(.5f);
            Eq(1, Log.Warnings.Count, "still one bounded warning across receipts and passes");
            Eq(0, bad.EnemyWrites, "no write for the faulty receipt");
            Eq(PickUpPolicy.Nobody, good.CurrentEnemyPolicy, "healthy receipt served in the same suite");
            Eq(1, good.EnemyWrites, "single healthy write");
            Eq(2, Tracked(typeof(Policy)).Count, "faulty receipt not lost");
            bad.ThrowRead = false;
            Tick(1.01f);
            Eq(PickUpPolicy.Nobody, bad.CurrentEnemyPolicy, "receipt continues after the fault clears");
            Eq(1, bad.EnemyWrites, "single write after recovery");
        });

        Test("Policy write fault: one bounded warning, failed write not counted, healthy receipts continue (host-observed)", () =>
        {
            var bad = NewDog(); bad.ThrowWrite = true;
            var good = NewDog();
            Enable(bad);
            Eq(1, Log.Warnings.Count, "one bounded warning");
            Enable(good); Tick(.5f);
            Eq(1, Log.Warnings.Count, "still one bounded warning");
            Eq(0, bad.EnemyWrites, "failed write is not counted or applied (host fault; no native success claim)");
            Eq(PickUpPolicy.Anybody, bad.CurrentEnemyPolicy, "policy unchanged on the failed write");
            Eq(0, bad.OriginalWrites, "no native original write either");
            Eq(PickUpPolicy.Nobody, good.CurrentEnemyPolicy, "healthy receipt protected in the same suite");
            Eq(1, good.EnemyWrites, "single healthy write");
            Tick(1.01f);
            Eq(1, Log.Warnings.Count, "warning stays bounded on later passes");
            Eq(0, bad.EnemyWrites, "no retry write for the failed receipt");
        });

        // 合同修订（2026-10-08）：同一 GameObject 同时带真实 Dog+Hermit 时，Android 由真实同对象 Dog
        // 的 receipt 唯一负责本 Droppable；Hermit 在登记处让位，避免双 receipt 在外部写入后互相覆写。
        Test("Thin handler order Hermit->PetGuard: a dual-component droppable keeps the unique Dog receipt", () =>
        {
            var d = NewDroppable();
            d.NativePolicy(PickUpPolicy.Anybody); d.NativeOriginal(PickUpPolicy.Anybody);
            d.gameObject.AddComponent<Dog>();
            d.gameObject.AddComponent<Hermit>();
            Enable(d); Tick(.5f);
            Eq(1, Tracked(typeof(Policy)).Count, "unique Dog receipt");
            Eq(0, Tracked(typeof(HermitPolicy)).Count, "hermit yields: no second receipt for the same Droppable");
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "protected by the Dog receipt");
            Eq(1, d.EnemyWrites, "Dog protects exactly once");
            Eq(1, Log.Info.Count(l => l.Contains("Dog enemy pickup protected")), "the Dog receipt owns the single protection write");
            Eq(0, Log.Info.Count(l => l.Contains("Hermit enemy pickup protected")), "the hermit side never registers or writes");
        });

        foreach (var external in new[] { PickUpPolicy.Anybody, PickUpPolicy.EnemyOnly })
        {
            Test("Dual-component external policy takeover remains owned by native: " + external, () =>
            {
                var d = NewDroppable();
                d.NativePolicy(PickUpPolicy.Anybody); d.NativeOriginal(PickUpPolicy.Anybody);
                d.gameObject.AddComponent<Dog>(); d.gameObject.AddComponent<Hermit>();
                Enable(d); Tick(.5f);
                Eq(1, d.EnemyWrites, "initial protection writes once");
                Eq(0, Tracked(typeof(HermitPolicy)).Count, "no hermit receipt exists to fight the Dog receipt");
                d.NativePolicy(external); // 外部写方接管（native 直写，不计入 host 写计数）
                Tick(1.01f);
                Eq(external, d.CurrentEnemyPolicy, "external writer owns its value before any downstream correction");
                Eq(1, d.EnemyWrites, "neither side rewrites external ownership");
                Tick(1.52f);
                Eq(external, d.CurrentEnemyPolicy, "external value still holds on later tick");
                Eq(1, d.EnemyWrites, "no later overwrite either");
            });
        }

        Test("Dual-component aboard a boat: OFF deferral, off-boat restore, hermit absent throughout", () =>
        {
            var d = NewDroppable();
            d.NativePolicy(PickUpPolicy.Anybody); d.NativeOriginal(PickUpPolicy.Anybody);
            d.gameObject.AddComponent<Dog>();
            d.gameObject.AddComponent<Hermit>();
            Enable(d);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "protected once");
            Eq(1, d.EnemyWrites, "single protection write");
            Parent(d, NewBoatBody(out _));
            ModConfig.PetGuardEnabled.Value = false; Tick(.5f);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "aboard: OFF restore deferred");
            Eq(1, d.EnemyWrites, "no restore write while aboard");
            Eq(1, Tracked(typeof(Policy)).Count, "receipt retained for the deferred restore");
            Eq(0, Tracked(typeof(HermitPolicy)).Count, "hermit never registers");
            Parent(d, Managers.Inst.world.gameLayer);
            Tick(1.01f);
            Eq(PickUpPolicy.Anybody, d.CurrentEnemyPolicy, "off boat restores the original");
            Eq(2, d.EnemyWrites, "one restore write");
        });

        Test("Dual-component native OnDisable reset and pool reuse keep the unique Dog receipt", () =>
        {
            var d = NewDroppable();
            d.NativePolicy(PickUpPolicy.EnemyOnly); d.NativeOriginal(PickUpPolicy.EnemyOnly);
            d.gameObject.AddComponent<Dog>();
            d.gameObject.AddComponent<Hermit>();
            Enable(d);
            Eq(1, d.EnemyWrites, "single protection write");
            Disable(d);
            Eq(0, Tracked(typeof(Policy)).Count, "Dog receipt retired on disable");
            Eq(0, Tracked(typeof(HermitPolicy)).Count, "hermit never registered");
            Eq(PickUpPolicy.EnemyOnly, d.CurrentEnemyPolicy, "native reset applied on disable");
            Eq(1, d.NativeResetCount, "native reset ran exactly once");
            d.NativePolicy(PickUpPolicy.EnemyOnly); d.NativeOriginal(PickUpPolicy.EnemyOnly);
            Enable(d);
            Eq(1, Tracked(typeof(Policy)).Count, "reused generation registers the Dog receipt only");
            Eq(0, Tracked(typeof(HermitPolicy)).Count, "hermit still yields on reuse");
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "reused generation protects");
            Eq(2, d.EnemyWrites, "one protection write for the reused generation");
        });

        Test("Android build has no recall surface: no recall members, no spawn/save APIs, Tick stays protection-only", () =>
        {
            var t = typeof(Policy);
            foreach (string name in new[]
            {
                "_recallArmed", "_recallStale", "_recallPending", "_recallWorld", "_recallLayer", "_recallScene",
                "_nextRecallAttempt", "RecallRetryInterval", "HermitTypes", "_loggedDeferred",
                "TickRecall", "RecallStolenPets", "RecallDogs", "RecallHermits", "HasDog", "HasHermit", "LogDeferredOnce"
            })
                Eq(0, t.GetMember(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly).Length, "absent: " + name);
            foreach (string name in new[]
            {
                "OnEnabled", "OnDisabled", "Tick", "TickReceipts", "Process", "TryScope", "SameIdentity", "Aboard", "LogFailure", "IsEnabled"
            })
                Check(t.GetMember(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly).Length > 0, "present: " + name);
            var types = t.Assembly.GetTypes();
            foreach (string name in new[] { "SetupDog", "SpawnNearP1", "SetDogStatus", "SetHermitStatus", "GetDogStatus", "GetHermitStatus" })
                Eq(0, types.SelectMany(x => x.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)).Count(m => m.Name == name), "no member " + name);
            foreach (string fullName in new[] { "Il2CppSystem.Nullable`1", "Il2Cpp.CampaignSaveData", "Il2Cpp.Holder", "Il2Cpp.Kingdom", "Il2Cpp.DogType", "Il2Cpp.DogPosition", "Il2Cpp.HermitType", "Il2Cpp.HermitPosition" })
                Eq(0, types.Count(x => x.FullName == fullName), "no type " + fullName);
            var d = NewDog();
            Enable(d); Tick(.5f);
            Eq(1, d.EnemyWrites, "Tick only performs the protection write");
            Eq(0, Log.Info.Count(l => l.Contains("recovered")), "no recall/recovery log line");
        });

        Console.WriteLine($"RESULT: {passed} passed, {failed} failed, {assertions} checks");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }
}
