// 原生边界替身测试：用最近似原生顺序的调用序列驱动**生产文件**
// il2cpp/HeroArcherArrowVisuals.cs（0.65 显示缩放 + 英雄分支的穿墙挂点与归还挂点）与
// il2cpp/HeroArcherWallPierce.cs（墙碰撞体快照 / IgnoreCollision 应用与归还）。
//
// 模拟的原生顺序（2.1/2.4 反编译一致）：
//   FireArrowInternal:
//     [Prefix] HeroArcherArrowVisuals.BeginShot(source)
//     Pool.Spawn → Arrow.OnEnable:
//         [Prefix Priority.First]  ResetArrow  → HeroArcherWallPierce.Restore（无条件归还墙碰撞）
//         原生 OnEnable body（重置物理/collider/地面无视对）
//         [Postfix Priority.Last]  OnSpawn     → 上色成功 → HeroArcherWallPierce.Apply（无视墙碰撞）
//     arrow.archer = source                       ← 原生在 OnEnable 之后才写 owner
//     [Finalizer] EndShot
//   每帧：ModPanel.Update → Tick()
//
// 模式：
//   gold    = 资源为 operator 提供的真实 ArtemisArrow.png（尺寸/像素/PPU 全核）
//   missing = csproj 不嵌入资源 → 必须 fail-closed（不换外观、不挂穿墙）

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

internal static class Program
{
    private const string ResourceName = "KingdomEnhancedMod.ArtemisArrow.png";
    private const float ExpectedPpu = 32f / 0.65f;          // ≈ 49.23077

    private static int _passes;
    private static int _failures;
    private static string _mode = "gold";
    private static Sprite _vanilla;
    private static int _nextId = 100;
    private static int _nextPtr = 1000;

    private static int Main(string[] args)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith("--mode=", StringComparison.Ordinal)) _mode = arg.Substring("--mode=".Length);
        }
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (Exception) { }
        Console.WriteLine("HeroArcherWallPierce + HeroArcherArrowVisuals stub tests (native-boundary stubs), mode=" + _mode);
        bool embedded = FindResource() != null;
        Console.WriteLine("resource embedded: " + embedded);
        Console.WriteLine();

        if (_mode == "missing" && embedded)
        {
            // 构建/增量问题（例如换属性但 MSBuild 复用了旧输出）会让 missing 模式静默跑在 gold 程序集上，
            // 产出误导性的失败。这里直接硬失败（退出码 2），把问题归因到构建而不是模块行为。
            Console.WriteLine("INFRA: --mode=missing was requested but this build still embeds " + ResourceName + ".");
            Console.WriteLine("INFRA: rebuild without the resource first, e.g.:");
            Console.WriteLine("INFRA:   dotnet build -c Release -t:Rebuild -p:EmbedArtemis=false");
            Console.WriteLine("INFRA:   dotnet run -c Release --no-build -- --mode=missing");
            return 2;
        }

        if (_mode == "gold") { RunHookContractTest(); RunGoldSuite(); }
        else if (_mode == "missing") { RunHookContractTest(); RunFailClosedSuite(); }
        else { Console.WriteLine("unknown mode: " + _mode); return 2; }

        Console.WriteLine();
        Console.WriteLine((_failures == 0 ? "ALL PASS" : "FAILURES") + ": pass=" + _passes + " fail=" + _failures);
        return _failures == 0 ? 0 : 1;
    }

    // ============================================================
    // 夹具
    // ============================================================

    private sealed class Rig
    {
        internal GameObject Go;
        internal SpriteRenderer Renderer;
        internal Arrow Arrow;
        internal Sprite BaseSprite;
        internal Color BaseColor;
    }

    private sealed class WallRig
    {
        internal GameObject Go;
        internal Wall Wall;
        internal Collider2D[] Colliders;
    }

    private static GameObject NewGo() => new GameObject { InstanceId = ++_nextId, Pointer = new IntPtr(_nextPtr++) };

    private static Rig NewRig(Sprite baseSprite = null, Color? baseColor = null)
    {
        GameObject go = NewGo();
        SpriteRenderer renderer = new SpriteRenderer
        {
            Pointer = new IntPtr(_nextPtr++),
            gameObject = go,
            sprite = baseSprite,
            color = baseColor ?? new Color(1f, 1f, 1f, 1f),
            sharedMaterial = new Material(),
            enabled = true,
            sortingLayerID = 4,
            sortingOrder = 11,
        };
        Arrow arrow = new Arrow
        {
            Pointer = new IntPtr(_nextPtr++),
            gameObject = go,
            _spriteRenderer = renderer,
            _collider = new Collider2D { Pointer = new IntPtr(_nextPtr++), gameObject = go, Label = "arrow" },
            colliderEnabled = true,
            rootX = 3f,
            rootY = 4f,
        };
        go.Components.Add(renderer);
        go.Components.Add(arrow);
        renderer.Writes.Clear();                                    // 构造期的赋值不算模块写入
        return new Rig { Go = go, Renderer = renderer, Arrow = arrow, BaseSprite = baseSprite, BaseColor = renderer.color };
    }

    /// <summary>活动墙 + 活动子碰撞体（登记进 FindObjectsOfType 场景替身）。</summary>
    private static WallRig NewWall(string label, int colliderCount)
    {
        GameObject wallGo = NewGo();
        Wall wall = new Wall { gameObject = wallGo };
        Collider2D[] colliders = new Collider2D[colliderCount];
        for (int i = 0; i < colliderCount; i++)
        {
            GameObject colliderGo = NewGo();
            colliders[i] = new Collider2D
            {
                Pointer = new IntPtr(_nextPtr++),
                gameObject = colliderGo,
                Label = label + "#" + i,
            };
            wall.Colliders.Add(colliders[i]);
        }
        wallGo.Components.Add(wall);
        UnityEngine.Object.AllObjects.Add(wall);
        return new WallRig { Go = wallGo, Wall = wall, Colliders = colliders };
    }

    private static GameObject NewHero(out Archer archer)
    {
        GameObject go = NewGo();
        archer = new Archer { Pointer = go.Pointer, gameObject = go };
        go.Components.Add(archer);
        HeroArcherRuntime.HeroPointers.Add(go.Pointer);
        return go;
    }

    private static GameObject NewPlainGo() => NewGo();

    /// <summary>原生顺序：OnEnable Prefix → 原生 body → OnEnable Postfix → 原生写 owner。</summary>
    private static void SimulateSpawn(Rig rig, GameObject owner)
    {
        HeroArcherArrowVisuals.ResetArrow(rig.Arrow);        // [Prefix Priority.First]（含穿墙归还）
        HeroArcherArrowVisuals.OnSpawn(rig.Arrow);           // [Postfix Priority.Last]（含穿墙挂载）
        rig.Arrow.archer = owner;                           // 原生在 OnEnable 之后写 owner
    }

    /// <summary>一次 FireArrowInternal：BeginShot → 逐箭 OnEnable → EndShot。</summary>
    private static void SimulateShot(GameObject source, params Rig[] arrows)
    {
        HeroArcherArrowVisuals.ShotToken token = HeroArcherArrowVisuals.BeginShot(source);
        for (int i = 0; i < arrows.Length; i++) SimulateSpawn(arrows[i], source);
        HeroArcherArrowVisuals.EndShot(token);
    }

    private static void Advance(float seconds) => Time.unscaledTime += seconds;

    private static int PairsFor(Rig rig, bool ignore) => Physics2D.CountArrowPair(rig.Arrow._collider, ignore);

    private static Stream FindResource() => typeof(HeroArcherArrowVisuals).Assembly.GetManifestResourceStream(ResourceName);

    // ============================================================
    // 断言
    // ============================================================

    private static void Check(bool ok, string what)
    {
        if (ok) { _passes++; Console.WriteLine("  PASS  " + what); }
        else { _failures++; Console.WriteLine("  FAIL  " + what); }
    }

    private static void Section(string name) => Console.WriteLine("== " + name);

    private static void Test(string name, Action body)
    {
        ResetWorld();
        Console.WriteLine("== " + name);
        try { body(); }
        catch (Exception e) { _failures++; Console.WriteLine("  FAIL  threw: " + e.GetType().Name + ": " + e.Message); }
    }

    private static void ResetWorld()
    {
        HeroArcherArrowVisuals.ResetForTests();
        HeroArcherWallPierce.ResetForTests();
        HeroArcherRuntime.Reset();
        ArcherOptionsScope.Reset();
        KingdomEnhancedPlugin.Instance = new KingdomEnhancedPluginStub();
        UnityEngine.Object.ResetScene();
        Physics2D.Reset();
        Time.unscaledTime = 1000f;
        _nextId = 100;
        _nextPtr = 1000;
        _vanilla = new Sprite
        {
            Pointer = new IntPtr(9000 + (++Sprite.SpriteCounter)),
            rect = new Rect(0f, 0f, 0.9f, 0.15f),
            pixelsPerUnit = 32f,
        };
    }

    private static bool SameColor(Color a, Color b)
    {
        return Math.Abs(a.r - b.r) <= 1e-3f && Math.Abs(a.g - b.g) <= 1e-3f
            && Math.Abs(a.b - b.b) <= 1e-3f && Math.Abs(a.a - b.a) <= 1e-3f;
    }

    private static bool IsBase(Rig rig)
    {
        return ReferenceEquals(rig.Renderer.sprite, rig.BaseSprite) && SameColor(rig.Renderer.color, rig.BaseColor);
    }

    /// <summary>黄金外观：换了 sprite，且颜色 = 原生 _Highlight 金（保持该箭原 alpha）。</summary>
    private static bool IsGold(Rig rig)
    {
        Sprite sprite = rig.Renderer.sprite;
        if (sprite == null || ReferenceEquals(sprite, rig.BaseSprite)) return false;
        Color c = rig.Renderer.color;
        return Math.Abs(c.r - 0.99215686f) <= 1e-3f
            && Math.Abs(c.g - 0.90196079f) <= 1e-3f
            && Math.Abs(c.b - 0.44705883f) <= 1e-3f
            && Math.Abs(c.a - rig.BaseColor.a) <= 1e-3f;
    }

    // ============================================================
    // 钩子契约（反射）：穿墙 slice 绝不新增任何 Harmony 入口
    // ============================================================

    private static void RunHookContractTest()
    {
        Section("hook contract: the pierce slice adds no native hooks; the two authorized arrow entry points are unchanged");
        try
        {
            Type[] types = typeof(HeroArcherArrowVisuals).Assembly.GetTypes();
            List<string> targets = new List<string>();
            List<string> prefixFirst = new List<string>();
            List<string> postfixLast = new List<string>();
            List<string> finalizers = new List<string>();
            bool pierceHasHarmony = false;
            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (type.Namespace != "KingdomEnhancedMod") continue;
                if (type.Name == "HeroArcherWallPierce")
                {
                    if (type.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false).Length > 0) pierceHasHarmony = true;
                    MethodInfo[] pierceMethods = type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    for (int m = 0; m < pierceMethods.Length; m++)
                    {
                        if (pierceMethods[m].GetCustomAttributes(typeof(HarmonyLib.HarmonyPrefix), false).Length > 0) pierceHasHarmony = true;
                        if (pierceMethods[m].GetCustomAttributes(typeof(HarmonyLib.HarmonyPostfix), false).Length > 0) pierceHasHarmony = true;
                        if (pierceMethods[m].GetCustomAttributes(typeof(HarmonyLib.HarmonyFinalizer), false).Length > 0) pierceHasHarmony = true;
                    }
                }
                if (!type.Name.EndsWith("HeroArrowArt_Patch", StringComparison.Ordinal)) continue;
                object[] patches = type.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false);
                for (int p = 0; p < patches.Length; p++)
                {
                    HarmonyLib.HarmonyPatch patch = (HarmonyLib.HarmonyPatch)patches[p];
                    targets.Add(patch.DeclaringType.Name + "." + patch.MethodName);
                }
                MethodInfo[] methods = type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                for (int m = 0; m < methods.Length; m++)
                {
                    MethodInfo method = methods[m];
                    string label = type.Name + "." + method.Name;
                    bool prefix = method.GetCustomAttributes(typeof(HarmonyLib.HarmonyPrefix), false).Length > 0;
                    bool postfix = method.GetCustomAttributes(typeof(HarmonyLib.HarmonyPostfix), false).Length > 0;
                    if (method.GetCustomAttributes(typeof(HarmonyLib.HarmonyFinalizer), false).Length > 0) finalizers.Add(label);
                    object[] priorities = method.GetCustomAttributes(typeof(HarmonyLib.HarmonyPriority), false);
                    HarmonyLib.Priority priority = priorities.Length > 0 ? ((HarmonyLib.HarmonyPriority)priorities[0]).Value : HarmonyLib.Priority.Normal;
                    if (prefix && priority == HarmonyLib.Priority.First) prefixFirst.Add(label);
                    if (postfix && priority == HarmonyLib.Priority.Last) postfixLast.Add(label);
                }
            }

            targets.Sort(StringComparer.Ordinal);
            Check(targets.Count == 2 && targets[0] == "Arrow.OnEnable" && targets[1] == "ArrowAttack.FireArrowInternal",
                "patched native methods are still exactly Arrow.OnEnable + ArrowAttack.FireArrowInternal");
            Check(!pierceHasHarmony, "HeroArcherWallPierce declares no Harmony patch attributes (it only rides the existing scope branches)");
            Check(prefixFirst.Count == 2, "both prefixes are Priority.First (pierce restore runs in the reset prefix)");
            Check(postfixLast.Count == 1, "the OnEnable postfix is Priority.Last (pierce apply rides the paint success path)");
            Check(finalizers.Count == 1, "one finalizer on FireArrowInternal (unchanged)");
        }
        catch (Exception e) { _failures++; Console.WriteLine("  FAIL  hook contract threw: " + e); }
    }

    // ============================================================
    // gold 模式
    // ============================================================

    private static void RunGoldSuite()
    {
        Test("gold: hero shot paints gold at 0.65 and ignores every active wall collider; non-hero/disabled stay native", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            WallRig wall1 = NewWall("w1", 2);
            WallRig wall2 = NewWall("w2", 1);
            Rig rig = NewRig(_vanilla);
            SimulateShot(hero, rig);

            Check(IsGold(rig), "hero arrow painted with the shared gold sprite");
            Check(Math.Abs(HeroArcherArrowVisuals.HeroArrowDisplayScale - 0.65f) <= 1e-6f, "display scale constant is 0.65");
            Sprite sprite = rig.Renderer.sprite;
            Check(Math.Abs(sprite.pixelsPerUnit - ExpectedPpu) <= 1e-3f, "sprite PPU = 32/0.65 (≈49.23), pure display path");
            Check(Math.Abs(sprite.WorldWidth - 0.9375f * 0.65f) <= 1e-4f, "world width = 65% of the native 30px/PPU32 arrow (0.609375)");
            Check(sprite.rect.width == 30f && sprite.rect.height == 5f, "rect stays 30x5 (native shape, no crop/resample)");
            Check(sprite.texture != null && sprite.texture.width == 30 && sprite.texture.height == 5, "texture stays 30x5");
            Check(Math.Abs(sprite.pivot.x - 0.5f) <= 1e-6f && Math.Abs(sprite.pivot.y - 0.5f) <= 1e-6f, "pivot (0.5,0.5) unchanged");

            Check(PairsFor(rig, true) == 3, "all 3 active wall colliders ignored for this arrow");
            Check(Physics2D.CountWallPair(wall1.Colliders[0], true) == 1 && Physics2D.CountWallPair(wall1.Colliders[1], true) == 1
                && Physics2D.CountWallPair(wall2.Colliders[0], true) == 1, "true pairs are exactly the wall colliders");
            Check(PairsFor(rig, false) == 0, "fresh spawn: no restore calls before any snapshot existed");
            Check(UnityEngine.Object.DestroyCalls == 0, "nothing destroyed");
            Check(KingdomEnhancedPlugin.Instance.LogSource.Contains("sprite ready") && KingdomEnhancedPlugin.Instance.LogSource.Contains("ppu=")
                && KingdomEnhancedPlugin.Instance.LogSource.Contains("scale="), "one-time sprite load logs the display ppu/scale");

            Rig normal = NewRig(_vanilla);
            SimulateShot(NewPlainGo(), normal);
            Check(IsBase(normal), "non-hero arrow keeps native appearance");
            Check(PairsFor(normal, true) == 0, "non-hero arrow is never given wall pierce");
            Check(PairsFor(normal, false) == 3, "yet its spawn still runs the unconditional restore (same snapshot)");

            HeroArcherRuntime.EnabledState = false;
            Rig disabled = NewRig(_vanilla);
            SimulateShot(hero, disabled);
            Check(IsBase(disabled) && PairsFor(disabled, true) == 0, "feature disabled: no paint, no pierce");
            HeroArcherRuntime.EnabledState = true;
        });

        Test("gold: pool reuse restores wall collisions in the OnEnable prefix; next hero life re-pierces at the same scale", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            WallRig wall = NewWall("w", 2);
            Rig rig = NewRig(_vanilla);
            SimulateShot(hero, rig);
            Check(PairsFor(rig, true) == 2, "first life: both wall pairs ignored (true)");
            Sprite firstSprite = rig.Renderer.sprite;
            int writesAfterPaint = rig.Renderer.Writes.Count;
            Check(writesAfterPaint == 2, "paint still writes exactly sprite + color (scale came from the sprite, not the transform)");

            int callsBeforeReset = Physics2D.Calls.Count;
            HeroArcherArrowVisuals.ResetArrow(rig.Arrow);                 // [Prefix Priority.First] 池复用归还
            Check(PairsFor(rig, false) == 2, "restore wrote false for both cached wall pairs");
            Check(Physics2D.Calls[callsBeforeReset].Ignore == false && Physics2D.Calls[callsBeforeReset + 1].Ignore == false,
                "restore calls follow the apply calls (true…false order on the same pairs)");
            Check(IsBase(rig), "appearance is handed back by the same prefix (no residual)");

            SimulateShot(hero, rig);                                      // 同一支箭的第二个英雄 life
            Check(IsGold(rig) && ReferenceEquals(rig.Renderer.sprite, firstSprite),
                "second life reuses the identical scaled sprite (no scale stacking)");
            Check(Math.Abs(rig.Renderer.sprite.pixelsPerUnit - ExpectedPpu) <= 1e-3f, "same PPU after reuse");
            Check(PairsFor(rig, true) == 4, "second life re-applied the wall pairs (2 more true calls)");
            Check(rig.Renderer.Writes.Count == writesAfterPaint + 4, "each life writes only sprite + color twice (paint + restore)");

            HeroArcherRuntime.EnabledState = false;
            HeroArcherArrowVisuals.ResetArrow(rig.Arrow);                 // 关闭状态下复用：仍要归还
            Check(PairsFor(rig, false) == 6 && IsBase(rig), "restore is unconditional even while the feature is disabled");
            HeroArcherRuntime.EnabledState = true;
        });

        Test("gold: snapshot lifecycle — one scan within TTL, rescan on TTL/world generation; restore never rescans", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            NewWall("w1", 1);
            int scans = UnityEngine.Object.FindObjectsOfTypeCalls;

            Rig a = NewRig(_vanilla);
            SimulateShot(hero, a);
            Check(UnityEngine.Object.FindObjectsOfTypeCalls == scans + 1, "first hero shot scans the walls exactly once");
            Check(PairsFor(a, true) == 1, "applied to the only wall collider");
            Check(PairsFor(a, false) == 0, "fresh spawn had no snapshot to restore against");

            Advance(2f);
            Rig b = NewRig(_vanilla);
            SimulateShot(hero, b);
            Check(UnityEngine.Object.FindObjectsOfTypeCalls == scans + 1, "within TTL: snapshot reused, no rescan");
            Check(PairsFor(b, true) == 1, "reused snapshot still applies");

            WallRig wall2 = NewWall("w2", 2);
            Advance(6f);                                                   // 超过 5s TTL
            Rig c = NewRig(_vanilla);
            SimulateShot(hero, c);
            Check(UnityEngine.Object.FindObjectsOfTypeCalls == scans + 2, "TTL expiry triggers exactly one rescan");
            Check(PairsFor(c, true) == 3, "rescan picked up the new wall (1 + 2 colliders)");
            Check(PairsFor(c, false) == 1, "its spawn restore used the pre-refresh snapshot (restore never scans)");

            ArcherOptionsScope.WorldPtr = new IntPtr(7099);                // 换代
            Rig d = NewRig(_vanilla);
            SimulateShot(hero, d);
            Check(UnityEngine.Object.FindObjectsOfTypeCalls == scans + 3, "world generation change forces a rescan even inside the TTL");
            Check(PairsFor(d, true) == 3, "new world snapshot applied");
            Check(PairsFor(d, false) == 3, "restore before the rescan used the previous snapshot, no extra scan");

            wall2.Go.activeSelf = false;                                   // 停用墙不再进入活动扫描
            Advance(6f);
            Rig e = NewRig(_vanilla);
            SimulateShot(hero, e);
            Check(PairsFor(e, true) == 1, "inactive wall excluded from the refreshed snapshot");
            Check(PairsFor(e, false) == 3, "its spawn restore still reached the previous snapshot (no rescan in ResetArrow)");
        });

        Test("gold: scan failure is fail-closed (no pierce, no throw, bounded log) and keeps the old snapshot for restore", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            NewWall("w", 2);
            Rig a = NewRig(_vanilla);
            SimulateShot(hero, a);
            Check(PairsFor(a, true) == 2, "warm snapshot applied");

            Advance(6f);
            UnityEngine.Object.FindObjectsOfTypeThrows = true;             // 查询失败
            Rig b = NewRig(_vanilla);
            SimulateShot(hero, b);
            Check(PairsFor(b, true) == 0, "query failure: no pierce this cycle (native collisions kept)");
            Check(HeroArcherWallPierce.CachedColliderCount == 2, "old snapshot kept (restore path still reachable)");
            Check(PairsFor(b, false) == 2, "restore still reached the kept snapshot");
            Check(KingdomEnhancedPlugin.Instance.LogSource.Contains("scan failed"), "failure logged once");

            UnityEngine.Object.FindObjectsOfTypeThrows = false;
            Advance(6f);
            Rig c = NewRig(_vanilla);
            SimulateShot(hero, c);
            Check(PairsFor(c, true) == 2, "recovered on the next attempt");
            Check(KingdomEnhancedPlugin.Instance.LogSource.CountContaining("[HeroArcherWallPierce]") <= 8,
                "pierce logs bounded per world generation");
        });

        Test("gold: per-pair failures are isolated in both directions and never escape", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            WallRig w1 = NewWall("w1", 1);
            WallRig w2 = NewWall("w2", 1);
            Collider2D bad = w2.Colliders[0];
            Physics2D.OnIgnore = (Collider2D collider1, Collider2D collider2, bool ignore) =>
            {
                if (ReferenceEquals(collider2, bad)) throw new InvalidOperationException("stub: this pair failed");
            };

            Rig rig = NewRig(_vanilla);
            SimulateShot(hero, rig);                                       // 不抛
            Check(PairsFor(rig, true) == 1 && Physics2D.CountWallPair(w1.Colliders[0], true) == 1,
                "apply: failing pair skipped, the other wall still ignored");
            Check(Physics2D.CountWallPair(bad, true) == 0 && Physics2D.CountWallPair(bad, false) == 0,
                "the failing pair was never recorded in either direction");

            Rig second = NewRig(_vanilla);
            SimulateShot(hero, second);
            Check(PairsFor(second, false) == 1 && Physics2D.CountWallPair(w1.Colliders[0], false) == 1,
                "restore: failing pair skipped, the other wall restored");
            Check(IsGold(second) && IsGold(rig), "appearance unaffected by pierce failures");

            bool escaped = false;
            try { HeroArcherWallPierce.Apply(rig.Arrow); } catch (Exception) { escaped = true; }
            try { HeroArcherWallPierce.Restore(rig.Arrow); } catch (Exception) { escaped = true; }
            Check(!escaped, "direct Apply/Restore calls never throw out");
        });

        Test("gold: collider cap — at most 256 pairs per arrow, truncation logged once", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            NewWall("big", 300);
            Rig rig = NewRig(_vanilla);
            SimulateShot(hero, rig);
            Check(HeroArcherWallPierce.CachedColliderCount == 256, "snapshot capped at 256 colliders");
            Check(PairsFor(rig, true) == 256, "apply calls bounded to the cap");
            Check(KingdomEnhancedPlugin.Instance.LogSource.CountContaining("capped at 256") == 1, "truncation logged exactly once");

            Rig second = NewRig(_vanilla);
            SimulateShot(hero, second);
            Check(PairsFor(second, true) == 256 && KingdomEnhancedPlugin.Instance.LogSource.CountContaining("capped at 256") == 1,
                "cap holds for later arrows without extra logging");
        });

        Test("gold: no pierce without a fully written appearance (fail-closed coupling)", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            NewWall("w", 2);
            Rig rig = NewRig(_vanilla);
            rig.Renderer.WriteColorThrows = true;
            SimulateShot(hero, rig);
            Check(!ReferenceEquals(rig.Renderer.sprite, rig.BaseSprite), "sprite half-applied before the color write threw");
            Check(PairsFor(rig, true) == 0, "no wall pierce when the appearance was not fully written");
            Check(PairsFor(rig, false) == 0, "and no snapshot was ever built by this arrow");

            rig.Renderer.WriteColorThrows = false;
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(IsBase(rig), "half-applied appearance still rolled back by the existing machinery");
        });

        Test("gold: restore is a no-op before any snapshot and uses TTL-expired snapshots without rescanning", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            Rig fresh = NewRig(_vanilla);
            int scans = UnityEngine.Object.FindObjectsOfTypeCalls;
            HeroArcherArrowVisuals.ResetArrow(fresh.Arrow);
            Check(Physics2D.Calls.Count == 0 && UnityEngine.Object.FindObjectsOfTypeCalls == scans,
                "no snapshot: restore is a no-op and never scans");

            GameObject hero = NewHero(out _);
            NewWall("w", 2);
            Rig rig = NewRig(_vanilla);
            SimulateShot(hero, rig);
            Advance(30f);                                                  // TTL 早已过期
            int scansAfterWarm = UnityEngine.Object.FindObjectsOfTypeCalls;
            HeroArcherArrowVisuals.ResetArrow(rig.Arrow);
            Check(UnityEngine.Object.FindObjectsOfTypeCalls == scansAfterWarm, "restore never rescans");
            Check(PairsFor(rig, false) == 2, "stale snapshot still restored the pairs it could have written");
        });

        Test("gold: pierce touches nothing but the wall pairs (physics/damage/root untouched, nothing destroyed)", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            NewWall("w", 2);
            Rig rig = NewRig(_vanilla);
            string before = rig.Arrow.NonVisualSignature();
            Material sharedMaterial = rig.Renderer.sharedMaterial;

            SimulateShot(hero, rig);
            Advance(1f);
            HeroArcherArrowVisuals.ResetArrow(rig.Arrow);
            Check(rig.Arrow.NonVisualSignature() == before, "physics/collider/trail/damage/root transform/component set byte-identical");
            Check(ReferenceEquals(rig.Renderer.sharedMaterial, sharedMaterial), "native shared material instance untouched");
            Check(rig.Renderer.enabled && rig.Renderer.sortingLayerID == 4 && rig.Renderer.sortingOrder == 11,
                "renderer enable/sorting untouched");
            Check(rig.Renderer.SpriteWrites == 2 && rig.Renderer.ColorWrites == 2, "paint + restore wrote only sprite/color (2 each)");
            Check(UnityEngine.Object.DestroyCalls == 0 && !rig.Go.Destroyed, "nothing destroyed");
            Check(rig.Arrow.rootScaleX == 1f && rig.Arrow.rootScaleY == 1f, "arrow root scale stays native 1 (no transform route)");
        });

        Test("gold: main + extra arrows of one shot are all pierced; pierce logs stay bounded per world", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            NewWall("w", 2);
            Rig main = NewRig(_vanilla);
            Rig extra1 = NewRig(_vanilla);
            Rig extra2 = NewRig(_vanilla);
            SimulateShot(hero, main, extra1, extra2);
            Check(PairsFor(main, true) == 2 && PairsFor(extra1, true) == 2 && PairsFor(extra2, true) == 2,
                "main + extras all ignore the walls (same shot scope)");

            int afterFirst = KingdomEnhancedPlugin.Instance.LogSource.CountContaining("[HeroArcherWallPierce]");
            Check(afterFirst >= 1 && afterFirst <= 8, "bounded pierce logs after the first shot (got " + afterFirst + ")");
            for (int i = 0; i < 5; i++)
            {
                Rig again = NewRig(_vanilla);
                SimulateShot(hero, again);
            }
            Check(KingdomEnhancedPlugin.Instance.LogSource.CountContaining("[HeroArcherWallPierce]") == afterFirst,
                "no per-arrow log spam within a generation");

            ArcherOptionsScope.WorldPtr = new IntPtr(7100);                // 换代 → 新预算
            Rig later = NewRig(_vanilla);
            SimulateShot(hero, later);
            Check(PairsFor(later, true) == 2, "new world generation still pierced");
            Check(KingdomEnhancedPlugin.Instance.LogSource.CountContaining("[HeroArcherWallPierce]") > afterFirst,
                "a new generation may log its own snapshot summary (budget reset)");
        });

        Test("gold: fail-closed without a world context or without a readable arrow collider", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            NewWall("w", 2);
            Rig warm = NewRig(_vanilla);
            SimulateShot(hero, warm);
            Check(PairsFor(warm, true) == 2, "warm snapshot applied");

            ArcherOptionsScope.ContextAvailable = false;                   // 上下文不可用 = 不进入英雄分支
            Rig blocked = NewRig(_vanilla);
            SimulateShot(hero, blocked);
            Check(IsBase(blocked), "no world context: arrow keeps native appearance");
            Check(PairsFor(blocked, true) == 0, "no world context: no pierce (fail-closed)");
            Check(PairsFor(blocked, false) == 2, "restore is context-free and still hands the pairs back");
            ArcherOptionsScope.ContextAvailable = true;

            Rig noCollider = NewRig(_vanilla);
            noCollider.Arrow._collider = null;
            SimulateShot(hero, noCollider);
            Check(IsGold(noCollider), "arrow without a readable collider still paints");
            Check(PairsFor(noCollider, true) == 0, "no collider: no pierce, no exception");
        });

        Test("gold: native artemis asset identity unchanged (30x5 / 52 opaque / Point-Clamp)", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            NewWall("w", 1);
            Rig rig = NewRig(_vanilla);
            SimulateShot(hero, rig);
            Sprite sprite = rig.Renderer.sprite;
            Check(FindResource() != null, "embedded resource present");
            Check(sprite.extrude == 0u && sprite.meshType == SpriteMeshType.FullRect, "no extrude, FullRect");
            Check(sprite.texture.filterMode == FilterMode.Point && sprite.texture.wrapMode == TextureWrapMode.Clamp
                && sprite.texture.anisoLevel == 0, "Point / Clamp / no mip-aniso");
            Check(!sprite.texture.mipChain, "texture created without mipmaps");
            int opaque = 0;
            Color32[] pixels = sprite.texture.decodedPixels;
            for (int i = 0; i < pixels.Length; i++) if (pixels[i].a != 0) opaque++;
            Check(pixels.Length == 150 && opaque == 52, "real asset still decodes to 150 px with 52 opaque pixels");
        });
    }

    // ============================================================
    // fail-closed 模式：资源缺失
    // ============================================================

    private static void RunFailClosedSuite()
    {
        Test("fail-closed: missing gold sprite means no pierce either; exactly one diagnostic, no side effects", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            NewWall("w", 2);
            Rig rig = NewRig(_vanilla, new Color(1f, 1f, 1f, 0.7f));
            SimulateShot(hero, rig);
            Check(IsBase(rig), "arrow keeps native sprite and color");
            Check(PairsFor(rig, true) == 0, "no wall pierce without a successful paint");
            Check(PairsFor(rig, false) == 0, "no snapshot built at all");
            Check(HeroArcherArrowVisuals.TrackedCount == 0, "no appearance receipt created");
            Check(HeroArcherWallPierce.CachedColliderCount == 0, "no wall snapshot created");
            Check(UnityEngine.Object.DestroyCalls <= 1, "at most the module's own throwaway texture is destroyed (got "
                + UnityEngine.Object.DestroyCalls + ")");
            StubLogSource log = KingdomEnhancedPlugin.Instance.LogSource;
            Check(log.CountContaining("[HeroArcherWallPierce]") == 0, "zero pierce activity in the logs");
            Check(log.CountContaining("[HeroArrowArt]") == 1, "exactly one appearance diagnostic, got "
                + log.CountContaining("[HeroArrowArt]"));
        });
    }
}
