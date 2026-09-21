// 原生边界替身测试：用最近似原生顺序的调用序列驱动**生产文件**
// il2cpp/HeroArcherArrowVisuals.cs（0.65 显示缩放 + 英雄分支的穿墙挂点与归还挂点）与
// il2cpp/HeroArcherWallPierce.cs（墙碰撞体快照 / IgnoreCollision 应用与归还）。
//
// 模拟的原生顺序（2.1/2.4 反编译一致）：
//   FireArrowInternal:
//     [Prefix] HeroArcherArrowVisuals.BeginShot(source)
//     Pool.Spawn → Arrow.OnEnable:
//         [Prefix Priority.First]  ResetArrow  → 按箭身份兜底归还墙碰撞（权威归还走回执缝合点：外观 + 账本）
//         原生 OnEnable body（重置物理/collider/地面无视对）
//         [Postfix Priority.Last]  OnSpawn     → 上色成功 → HeroArcherWallPierce.Apply（只接管原值 false 的对并记账）
//     arrow.archer = source                       ← 原生在 OnEnable 之后才写 owner
//     [Finalizer] EndShot
//   每帧：ModPanel.Update → Tick()
//
// 模式：
//   gold    = 资源为 operator 提供的真实 ArtemisArrow.png（尺寸/像素/PPU 全核）
//   missing = csproj 不嵌入资源 → 必须 fail-closed（不换外观、不挂穿墙）
//
// 替身附加的异常注入面（用于 B1/B2 边界回归）：
//   Object.NullCheckThrows       → 该对象的 `== null` / `!= null` 抛异常（Unity 原生 null 检查失败的外层异常面）
//   Collider2D.PointerReadThrows → 实际 collider 的原生身份读不到（未知分支）
//   Physics2D.OnGetIgnore / OnIgnore / Throws → 读/写按对或全局失败面

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
            Check(PairsFor(normal, false) == 0, "non-hero arrow has no ledger: zero physics writes on its spawn");

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
            Check(PairsFor(rig, false) == 4 && IsBase(rig), "restore is unconditional even while the feature is disabled");
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
            Check(PairsFor(c, false) == 0, "a fresh arrow owns no ledger: spawn restore writes nothing");

            ArcherOptionsScope.WorldPtr = new IntPtr(7099);                // 换代
            Rig d = NewRig(_vanilla);
            SimulateShot(hero, d);
            Check(UnityEngine.Object.FindObjectsOfTypeCalls == scans + 3, "world generation change forces a rescan even inside the TTL");
            Check(PairsFor(d, true) == 3, "new world snapshot applied");
            Check(PairsFor(d, false) == 0, "still no ledger on a fresh arrow (and no extra scan in ResetArrow)");

            wall2.Go.activeSelf = false;                                   // 停用墙不再进入活动扫描
            Advance(6f);
            Rig e = NewRig(_vanilla);
            SimulateShot(hero, e);
            Check(PairsFor(e, true) == 1, "inactive wall excluded from the refreshed snapshot");
            Check(PairsFor(e, false) == 0, "fresh arrow: no ledger, no physics write");
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
            Check(HeroArcherWallPierce.CachedColliderCount == 2, "old snapshot kept (discovery state preserved)");
            Check(PairsFor(b, false) == 0, "scan failure writes nothing for a fresh arrow (fail-closed)");
            Check(KingdomEnhancedPlugin.Instance.LogSource.Contains("scan failed"), "failure logged once");

            // 归还根本不看快照：账号本（实际写过的 Collider2D）走，扫描失败/停摆也照还。
            HeroArcherArrowVisuals.ResetArrow(a.Arrow);
            Check(PairsFor(a, false) == 2, "the warm arrow's own ledger completes the hand-back without any scan");

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
                "the failing pair recorded no successful write in either direction");

            HeroArcherArrowVisuals.ResetArrow(rig.Arrow);                   // 归还：坏对继续挂账
            Check(Physics2D.CountWallPair(w1.Colliders[0], false) == 1, "restore: the good wall was handed back");
            Check(Physics2D.CountWallPair(bad, false) == 0, "no false written for the pair whose hand-back also failed");
            Check(HeroArcherArrowVisuals.TrackedCount == 1, "the unrestored pair keeps the receipt alive for retry");
            Check(IsBase(rig), "appearance unaffected by pierce failures");

            Physics2D.OnIgnore = null;
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(HeroArcherArrowVisuals.TrackedCount == 0 && Physics2D.CountWallPair(bad, false) == 1,
                "backoff retry completed the hand-back and retired the receipt");

            Physics2D.Throws = true;                                       // 全局写失败面：绝不外抛
            bool escaped = false;
            try { HeroArcherWallPierce.Apply(rig.Arrow); } catch (Exception) { escaped = true; }
            try { HeroArcherWallPierce.Restore(rig.Arrow); } catch (Exception) { escaped = true; }
            Physics2D.Throws = false;
            Check(!escaped, "direct Apply/Restore calls never throw out when every write fails");
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
            Check(PairsFor(rig, false) == 2, "the ledger handed back exactly the pairs it took over (snapshot age irrelevant)");
        });

        Test("gold: arrows this module never applied to are never written (no blanket false; foreign true survives)", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            WallRig wall = NewWall("w", 2);
            Rig warm = NewRig(_vanilla);
            SimulateShot(hero, warm);
            Check(PairsFor(warm, true) == 2, "the hero arrow took both pairs over (warm ledger + snapshot)");

            Rig normal = NewRig(_vanilla);
            Physics2D.IgnoreCollision(normal.Arrow._collider, wall.Colliders[0], true);   // 别的代码设置的 true
            int calls = Physics2D.Calls.Count;
            HeroArcherArrowVisuals.ResetArrow(normal.Arrow);
            Check(Physics2D.Calls.Count == calls && PairsFor(normal, false) == 0,
                "an arrow with no ledger does zero physics writes (no blanket restore)");
            Check(Physics2D.GetIgnoreCollision(normal.Arrow._collider, wall.Colliders[0]), "the foreign true pair is untouched");
            Check(PairsFor(warm, true) == 2, "the unrelated reset left the owner arrow's pairs alone");

            Rig hero2 = NewRig(_vanilla);
            Physics2D.IgnoreCollision(hero2.Arrow._collider, wall.Colliders[0], true);   // 预置他方 true
            int trueBefore = Physics2D.CountWallPair(wall.Colliders[0], true);
            SimulateShot(hero, hero2);
            Check(Physics2D.CountWallPair(wall.Colliders[0], true) == trueBefore,
                "the pre-existing true pair was never rewritten by the hero shot");
            HeroArcherArrowVisuals.ResetArrow(hero2.Arrow);
            Check(Physics2D.GetIgnoreCollision(hero2.Arrow._collider, wall.Colliders[0]), "foreign true survives the hand-back");
            Check(Physics2D.CountWallPair(wall.Colliders[0], false) == 0, "no false written for the foreign pair");
            Check(!Physics2D.GetIgnoreCollision(hero2.Arrow._collider, wall.Colliders[1]),
                "the pair we did take over was handed back");
        });

        Test("gold: an owner-vetoed arrow hands its wall pairs back with the appearance", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            GameObject other = NewPlainGo();
            WallRig wall = NewWall("w", 2);
            Rig rig = NewRig(_vanilla);
            HeroArcherArrowVisuals.ShotToken token = HeroArcherArrowVisuals.BeginShot(hero);
            SimulateSpawn(rig, hero);
            Check(IsGold(rig) && PairsFor(rig, true) == 2, "hero scope painted and pierced the arrow");

            rig.Arrow.archer = other;                                      // 原生写了别的 owner
            HeroArcherArrowVisuals.EndShot(token);
            Check(IsBase(rig), "owner veto restored the appearance");
            Check(PairsFor(rig, false) == 2, "owner veto also handed the wall pairs back");
            Check(!Physics2D.GetIgnoreCollision(rig.Arrow._collider, wall.Colliders[0])
                && !Physics2D.GetIgnoreCollision(rig.Arrow._collider, wall.Colliders[1]), "both pairs are false again");
            Check(HeroArcherArrowVisuals.TrackedCount == 0, "receipt retired only after both responsibilities settled");
        });

        Test("gold: switching the feature off hands back the pairs of arrows already in flight", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            NewWall("w", 2);
            Rig rig = NewRig(_vanilla);
            SimulateShot(hero, rig);
            Check(PairsFor(rig, true) == 2 && HeroArcherArrowVisuals.TrackedCount == 1, "painted and pierced while enabled");

            HeroArcherRuntime.EnabledState = false;
            HeroArcherArrowVisuals.Tick();
            Check(IsBase(rig) && HeroArcherArrowVisuals.TrackedCount == 0, "feature off restored the appearance");
            Check(PairsFor(rig, false) == 2, "feature off handed the wall pairs back too");
        });

        Test("gold: a wall missing from the refreshed snapshot is still handed back (ledger, not snapshot)", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            WallRig wall = NewWall("w", 1);
            Rig old = NewRig(_vanilla);
            SimulateShot(hero, old);
            Check(Physics2D.GetIgnoreCollision(old.Arrow._collider, wall.Colliders[0]), "the old arrow owns the pair");

            wall.Wall.EnumerationThrows = true;                            // 单墙枚举失败 → 快照被替换为空
            Advance(6f);
            Rig fresh = NewRig(_vanilla);
            SimulateShot(hero, fresh);
            Check(HeroArcherWallPierce.CachedColliderCount == 0, "TTL rescan replaced the discovery snapshot with an empty one");
            Check(!Physics2D.GetIgnoreCollision(fresh.Arrow._collider, wall.Colliders[0]), "the fresh arrow never claimed the unreadable wall");

            HeroArcherWallPierce.Restore(old.Arrow);                       // 直接入口（反例同路径）
            Check(!Physics2D.GetIgnoreCollision(old.Arrow._collider, wall.Colliders[0]), "the ledger still reached the dropped wall");
            Check(HeroArcherWallPierce.LedgerCount == 0, "the settled ledger left the table");
        });

        Test("gold: repeated Apply is idempotent and never loses the hand-back accounting", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            NewWall("w", 2);
            Rig rig = NewRig(_vanilla);
            SimulateShot(hero, rig);
            int trueCalls = PairsFor(rig, true);
            Check(trueCalls == 2, "first apply took both pairs over");
            Check(HeroArcherWallPierce.LedgerCount == 1, "one ledger is in the table");

            HeroArcherWallPierce.PierceLedger ledger = HeroArcherWallPierce.Apply(rig.Arrow);
            Check(ledger != null && PairsFor(rig, true) == trueCalls,
                "second Apply wrote nothing new (owned pairs are not touched again)");
            Check(HeroArcherWallPierce.LedgerCount == 1, "the same ledger is reused: no duplicate accounting");

            HeroArcherArrowVisuals.ResetArrow(rig.Arrow);
            Check(PairsFor(rig, false) == 2, "the ledger hands back exactly the pairs it took over");
            Check(HeroArcherWallPierce.LedgerCount == 0, "a settled ledger frees its table slot");
        });

        Test("gold: a native write that throws afterwards keeps the hand-back responsibility (no half-write loss)", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            WallRig wall = NewWall("w", 1);
            Collider2D wallCollider = wall.Colliders[0];
            Rig rig = NewRig(_vanilla);
            Physics2D.OnIgnore = (Collider2D collider1, Collider2D collider2, bool ignore) =>
            {
                if (!ignore || !ReferenceEquals(collider2, wallCollider)) return;
                Physics2D.PairState[(collider1, collider2)] = true;        // native 侧其实已经写进去了
                Physics2D.PairState[(collider2, collider1)] = true;
                throw new InvalidOperationException("stub: native wrote then threw");
            };
            SimulateShot(hero, rig);
            Physics2D.OnIgnore = null;
            Check(Physics2D.GetIgnoreCollision(rig.Arrow._collider, wallCollider), "the native pair really is ignored");
            Check(PairsFor(rig, true) == 0, "the stub recorded no successful true call (it threw after the native write)");

            HeroArcherArrowVisuals.ResetArrow(rig.Arrow);
            Check(!Physics2D.GetIgnoreCollision(rig.Arrow._collider, wallCollider), "the ledger still handed the pair back");
            Check(HeroArcherArrowVisuals.TrackedCount == 0, "receipt retired after the hand-back");
        });

        Test("gold: an unreadable ignore state is unknown — never written, never claimed, retried later", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            WallRig wall = NewWall("w", 2);
            Collider2D unreadable = wall.Colliders[1];
            Physics2D.OnGetIgnore = (Collider2D collider1, Collider2D collider2) =>
            {
                if (ReferenceEquals(collider2, unreadable)) throw new InvalidOperationException("stub: get ignore threw");
            };
            Rig rig = NewRig(_vanilla);
            SimulateShot(hero, rig);
            Check(IsGold(rig), "appearance still painted (the read failure only affects the pierce slice)");
            Check(PairsFor(rig, true) == 1 && Physics2D.CountWallPair(wall.Colliders[0], true) == 1, "the readable wall was taken over");
            Check(Physics2D.CountWallPair(unreadable, true) == 0, "the unreadable pair was never written or claimed");
            Check(HeroArcherWallPierce.LedgerCount == 1, "the ledger keeps the arrow bound for retry");

            Physics2D.OnGetIgnore = null;
            HeroArcherArrowVisuals.Tick();                                 // 巡检重探（不扫描世界）
            Check(Physics2D.CountWallPair(unreadable, true) == 1, "retry read false -> the pair is taken over");
            Check(PairsFor(rig, true) == 2, "both pairs are ignored after the retry");

            HeroArcherArrowVisuals.ResetArrow(rig.Arrow);
            Check(PairsFor(rig, false) == 2, "both pairs are handed back (the retried one included)");
        });

        Test("gold: the per-arrow pair cap holds for unreadable walls too (bounded ledger, no throw)", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            WallRig big = NewWall("big", 256);
            Collider2D unreadable = big.Colliders[0];
            Physics2D.OnGetIgnore = (Collider2D collider1, Collider2D collider2) =>
            {
                if (ReferenceEquals(collider2, unreadable)) throw new InvalidOperationException("stub: get ignore threw");
            };
            Rig rig = NewRig(_vanilla);
            SimulateShot(hero, rig);
            Check(PairsFor(rig, true) == 255 && Physics2D.CountWallPair(unreadable, true) == 0,
                "255 pairs taken over, the unreadable one only probed");
            Check(HeroArcherWallPierce.LedgerCount == 1, "the ledger holds all 256 responsibilities");
            Physics2D.OnGetIgnore = null;

            big.Go.activeSelf = false;                                     // 旧墙退出扫描 → 快照换代
            WallRig extra = NewWall("extra", 1);
            Collider2D extraCollider = extra.Colliders[0];
            Physics2D.OnGetIgnore = (Collider2D collider1, Collider2D collider2) =>
            {
                if (ReferenceEquals(collider2, extraCollider)) throw new InvalidOperationException("stub: get ignore threw");
            };
            Advance(6f);
            bool escaped = false;
            try { HeroArcherWallPierce.Apply(rig.Arrow); } catch (Exception) { escaped = true; }
            Physics2D.OnGetIgnore = null;
            Check(!escaped, "a full ledger plus a newly added unreadable wall never throws out");
            Check(Physics2D.CountWallPair(extraCollider, true) == 0, "the extra wall was neither claimed nor written");

            HeroArcherArrowVisuals.ResetArrow(rig.Arrow);
            Check(PairsFor(rig, false) == 255, "the capped ledger still hands back every pair it owned");
            Check(HeroArcherWallPierce.LedgerCount == 0, "the settled ledger left the table");
        });

        Test("gold: a vanished renderer settles only the appearance; live wall pairs are still handed back", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            WallRig wall = NewWall("w", 2);
            Rig rig = NewRig(_vanilla);
            SimulateShot(hero, rig);
            Check(PairsFor(rig, true) == 2, "both pairs ignored while the renderer is alive");

            rig.Renderer.gameObject = null;                                // renderer 侧明确消失（外观无处可写）
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(PairsFor(rig, false) == 2, "the physical pairs were still handed back (renderer loss does not drop them)");
            Check(HeroArcherArrowVisuals.TrackedCount == 0, "receipt retired only after the ledger settled");
            Check(rig.Arrow._collider != null && !Physics2D.GetIgnoreCollision(rig.Arrow._collider, wall.Colliders[0]),
                "the arrow collider stayed alive and its pair was written back to false");
        });

        Test("gold: a replaced renderer cannot take over while the old receipt is unsettled", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            WallRig wall = NewWall("w", 2);
            Rig rig = NewRig(_vanilla);
            SimulateShot(hero, rig);
            Check(IsGold(rig) && PairsFor(rig, true) == 2, "first life painted + pierced");

            Collider2D stubborn = wall.Colliders[1];
            Physics2D.OnIgnore = (Collider2D collider1, Collider2D collider2, bool ignore) =>
            {
                if (!ignore && ReferenceEquals(collider2, stubborn)) throw new InvalidOperationException("stub: hand-back failed");
            };
            rig.Renderer.WriteSpriteThrows = true;                          // 外观与碰撞归还都失败 → 旧责任挂着
            HeroArcherArrowVisuals.ResetArrow(rig.Arrow);
            Check(HeroArcherArrowVisuals.TrackedCount == 1 && IsGold(rig), "old receipt kept, appearance still ours");
            Check(PairsFor(rig, false) == 1 && HeroArcherWallPierce.LedgerCount == 1, "one pair still owed, ledger kept");

            SpriteRenderer replacement = new SpriteRenderer
            {
                Pointer = new IntPtr(9801),
                gameObject = rig.Go,
                sprite = rig.BaseSprite,
                color = rig.BaseColor,
                sharedMaterial = new Material(),
            };
            rig.Go.Components.Add(replacement);
            rig.Arrow._spriteRenderer = replacement;                        // renderer 换掉（同一支箭）
            replacement.Writes.Clear();
            Sprite replacementSprite = replacement.sprite;
            Color replacementColor = replacement.color;

            SimulateShot(hero, rig);                                        // 新 life：ResetArrow 再试结清 + OnSpawn
            Check(HeroArcherArrowVisuals.TrackedCount == 1, "no second receipt while the old liability is unsettled");
            Check(ReferenceEquals(replacement.sprite, replacementSprite) && SameColor(replacement.color, replacementColor),
                "the replacement renderer stays native (this life is fail-closed)");
            Check(PairsFor(rig, true) == 2 && PairsFor(rig, false) == 1,
                "no new pair written and no wrong-target hand-back for the new life");
            Check(Physics2D.GetIgnoreCollision(rig.Arrow._collider, stubborn), "the owed pair is still owed on the old collider");

            Physics2D.OnIgnore = null;
            rig.Renderer.WriteSpriteThrows = false;
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(HeroArcherArrowVisuals.TrackedCount == 0, "old receipt retired once both liabilities settled");
            Check(PairsFor(rig, false) == 2 && HeroArcherWallPierce.LedgerCount == 0,
                "the owed pair was handed back on the OLD collider and the ledger left the table");

            SimulateShot(hero, rig);                                        // 结清后的新 life：正常接管当前 renderer
            Check(ReferenceEquals(rig.Arrow._spriteRenderer, replacement) && !ReferenceEquals(replacement.sprite, replacementSprite)
                && Math.Abs(replacement.sprite.pixelsPerUnit - ExpectedPpu) <= 1e-3f,
                "after settlement the new life paints the current renderer with the shared 0.65-scaled sprite");
            Check(PairsFor(rig, true) == 4 && HeroArcherArrowVisuals.TrackedCount == 1,
                "after settlement the new life pierces again with exactly one receipt");
        });

        Test("gold: a replaced or unreadable arrow collider refuses new writes until the old ledger settles", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            WallRig wall = NewWall("w", 2);
            Rig rig = NewRig(_vanilla);
            SimulateShot(hero, rig);
            Check(PairsFor(rig, true) == 2, "first life took both pairs over");

            Collider2D stubborn = wall.Colliders[1];
            Physics2D.OnIgnore = (Collider2D collider1, Collider2D collider2, bool ignore) =>
            {
                if (!ignore && ReferenceEquals(collider2, stubborn)) throw new InvalidOperationException("stub: hand-back failed");
            };
            HeroArcherArrowVisuals.ResetArrow(rig.Arrow);
            Check(PairsFor(rig, false) == 1 && HeroArcherWallPierce.LedgerCount == 1, "one pair still owed, ledger kept");

            Collider2D oldCollider = rig.Arrow._collider;
            Collider2D replacement = new Collider2D { Pointer = new IntPtr(9701), gameObject = rig.Go, Label = "arrow-replacement" };
            rig.Go.Components.Add(replacement);
            rig.Arrow._collider = replacement;                              // 实际 collider 换掉
            HeroArcherWallPierce.PierceLedger refused = HeroArcherWallPierce.Apply(rig.Arrow);
            Check(refused != null && Physics2D.CountArrowPair(replacement, true) == 0,
                "identity mismatch: the replaced collider gets zero new writes");
            Check(HeroArcherWallPierce.LedgerCount == 1 && Physics2D.GetIgnoreCollision(oldCollider, stubborn),
                "the old liability stays in the table and is still owed on the OLD collider");

            replacement.PointerReadThrows = true;                           // 原生身份读不到 = 未知 → 同样拒新写
            HeroArcherWallPierce.PierceLedger unknown = HeroArcherWallPierce.Apply(rig.Arrow);
            Check(unknown != null && Physics2D.CountArrowPair(replacement, true) == 0,
                "unknown collider identity: still zero new writes");
            replacement.PointerReadThrows = false;

            Physics2D.OnIgnore = null;
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(!Physics2D.GetIgnoreCollision(oldCollider, stubborn), "retry handed the old pair back on the OLD collider");
            Check(HeroArcherWallPierce.LedgerCount == 0 && HeroArcherArrowVisuals.TrackedCount == 0,
                "old ledger and receipt settled after the retry");

            SimulateShot(hero, rig);                                        // 结清后的新 life：当前 collider 正常接管
            Check(PairsFor(rig, true) == 2, "new life applies both pairs on the current collider");
            Check(Physics2D.CountWallPair(stubborn, true) == 2, "including the wall that used to be owed");
            Check(HeroArcherWallPierce.LedgerCount == 1, "a fresh ledger records the current collider");
        });

        Test("gold: an exception after the first recorded pair still returns the existing liability (no orphan ledger)", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            NewWall("w1", 1);
            WallRig wall2 = NewWall("w2", 1);
            Rig warm = NewRig(_vanilla);
            SimulateShot(hero, warm);                                       // 建立含两面墙的快照
            Check(PairsFor(warm, true) == 2, "warm snapshot covers both walls");

            Collider2D second = wall2.Colliders[0];
            second.NullCheckThrows = true;                                  // 第二个墙的 fake-null 检查在 Apply 外层抛出
            Rig rig = NewRig(_vanilla);
            bool escaped = false;
            try { SimulateShot(hero, rig); } catch (Exception) { escaped = true; }
            second.NullCheckThrows = false;
            Check(!escaped, "the outer failure never escapes the native entry points");
            Check(PairsFor(rig, true) == 1, "the first pair was recorded and written before the outer failure");
            Check(HeroArcherWallPierce.LedgerCount == 2, "the partially applied ledger is in the table");
            Check(HeroArcherArrowVisuals.TrackedCount == 2, "both receipts are alive (warm + partially applied)");

            HeroArcherRuntime.EnabledState = false;
            HeroArcherArrowVisuals.Tick();
            Check(PairsFor(rig, false) == 1, "the already-recorded pair is handed back through the receipt");
            Check(HeroArcherWallPierce.LedgerCount == 0, "no orphan ledger left after the hand-back");
            Check(HeroArcherArrowVisuals.TrackedCount == 0, "every receipt retired");
        });

        Test("gold: the ledger cap rejects new arrows without evicting unrestored pairs", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(out _);
            WallRig wall = NewWall("w", 1);
            int cap = HeroArcherWallPierce.MaxLedgerArrows;
            Rig[] rigs = new Rig[cap];
            for (int i = 0; i < cap; i++)
            {
                rigs[i] = NewRig(_vanilla);
                SimulateShot(hero, rigs[i]);
            }
            Check(HeroArcherWallPierce.LedgerCount == cap, "every pierced arrow holds its own ledger");
            Check(Physics2D.CountWallPair(wall.Colliders[0], true) == cap, "each of them took the wall over");

            Rig overflow = NewRig(_vanilla);
            HeroArcherWallPierce.PierceLedger rejected = HeroArcherWallPierce.Apply(overflow.Arrow);
            Check(rejected == null, "the ledger table is full: the new arrow is rejected");
            Check(PairsFor(overflow, true) == 0, "rejection writes not a single pair");
            Check(HeroArcherWallPierce.LedgerCount == cap, "no unrestored ledger was evicted");

            HeroArcherArrowVisuals.ResetArrow(rigs[0].Arrow);
            Check(HeroArcherWallPierce.LedgerCount == cap - 1, "only the settled ledger left the table");
            Check(!Physics2D.GetIgnoreCollision(rigs[0].Arrow._collider, wall.Colliders[0]), "an earlier lease still hands its pair back");
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
            Check(PairsFor(blocked, false) == 0, "a fresh arrow owns no ledger: restore is context-free and writes nothing");
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
