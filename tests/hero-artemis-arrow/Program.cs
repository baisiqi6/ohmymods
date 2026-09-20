// 原生边界替身测试：用最近似原生顺序的调用序列驱动**生产文件** hero-archer-arrow 视觉模块
// （Tests.csproj 直接编译 ../../il2cpp/HeroArcherArrowVisuals.cs + ../../il2cpp/HeroArcherWallPierce.cs）。
//
// 模拟的原生顺序（2.1/2.4 反编译一致）：
//   FireArrowInternal:
//     [Prefix] HeroArcherArrowVisuals.BeginShot(source)
//     Pool.Spawn → Arrow.OnEnable:
//         [Prefix Priority.First]  ResetArrow → HeroArcherWallPierce.Restore（无条件归还墙碰撞）
//         原生 OnEnable body
//         [Postfix Priority.Last]  OnSpawn → 外观写入成功 → HeroArcherWallPierce.Apply（无视墙碰撞）
//     arrow.archer = source                       ← 原生在 OnEnable 之后才写 owner
//     散射额外箭（本模组 postfix 内）同样走 OnEnable 后再写 archer
//     [Finalizer] EndShot
//   每帧：ModPanel.Update → Tick()
//
// 模式：
//   gold      = 资源为 operator 提供的真实 ArtemisArrow.png（形状/像素/pivot 全核；显示尺寸 = 原生 × 0.65）
//   missing   = csproj 不嵌入资源 → 必须 fail-closed 保持原生外观且不挂穿墙
//   invalid   = 嵌入非 PNG 字节 → 必须 fail-closed 保持原生外观且不挂穿墙
//   wrongsize = 嵌入尺寸不符的 PNG → 必须 fail-closed 保持原生外观且不挂穿墙

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

internal static class Program
{
    private const string ResourceName = "KingdomEnhancedMod.ArtemisArrow.png";

    private static int _passes;
    private static int _failures;
    private static string _mode = "gold";
    private static Sprite _vanilla;
    private static int _nextId = 100;

    private static int Main(string[] args)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith("--mode=", StringComparison.Ordinal)) _mode = arg.Substring("--mode=".Length);
        }
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (Exception) { }
        Console.WriteLine("HeroArcherArrowVisuals stub tests (native-boundary stubs), mode=" + _mode);
        Console.WriteLine("resource embedded: " + (FindResource() != null));
        Console.WriteLine();

        if (_mode == "gold") { RunHookContractTest(); RunGoldSuite(); }
        else if (_mode == "missing") { RunHookContractTest(); RunFailClosedSuite("embedded resource missing"); }
        else if (_mode == "invalid") { RunHookContractTest(); RunFailClosedSuite("ImageConversion.LoadImage returned false"); }
        else if (_mode == "wrongsize") { RunHookContractTest(); RunFailClosedSuite("artemis arrow PNG size"); }
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

        /// <summary>换成同一 native 对象的新 wrapper（箭头/GO/renderer 三个 wrapper 全换）。</summary>
        internal Arrow AliasArrow(int deltaGoInstanceId = 0, int deltaGoPointer = 0)
        {
            GameObject go = new GameObject
            {
                InstanceId = Go.InstanceId + deltaGoInstanceId,
                Pointer = new IntPtr(Go.Pointer.ToInt64() + deltaGoPointer),
                activeSelf = Go.activeSelf,
            };
            SpriteRenderer renderer = new SpriteRenderer
            {
                Pointer = Renderer.Pointer,
                gameObject = go,
                sprite = Renderer.sprite,
                color = Renderer.color,
                sharedMaterial = Renderer.sharedMaterial,
            };
            Arrow arrow = new Arrow
            {
                Pointer = Arrow.Pointer,
                gameObject = go,
                _spriteRenderer = renderer,
                _collider = Arrow._collider,               // 同一 native 对象：别名共享同一碰撞体引用
            };
            go.Components.Add(renderer);
            go.Components.Add(arrow);
            return arrow;
        }
    }

    private static GameObject NewGo(int pointer)
    {
        return new GameObject { InstanceId = ++_nextId, Pointer = new IntPtr(pointer) };
    }

    private static Rig NewRig(int pointerBase, Sprite baseSprite = null, Color? baseColor = null)
    {
        GameObject go = NewGo(pointerBase);
        SpriteRenderer renderer = new SpriteRenderer
        {
            Pointer = new IntPtr(pointerBase + 1),
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
            Pointer = new IntPtr(pointerBase + 2),
            gameObject = go,
            _spriteRenderer = renderer,
            _collider = new Collider2D { Pointer = new IntPtr(pointerBase + 3), gameObject = go, Label = "arrow" },
            colliderEnabled = true,
            trailEnabled = false,
            rootX = 3f,
            rootY = 4f,
        };
        go.Components.Add(renderer);
        go.Components.Add(arrow);
        renderer.Writes.Clear();                                    // 构造期的赋值不算模块写入
        return new Rig { Go = go, Renderer = renderer, Arrow = arrow, BaseSprite = baseSprite, BaseColor = renderer.color };
    }

    /// <summary>renderer 挂在**别的 GO**（子物体）上的箭：模块必须在任何写入前拒绝。</summary>
    private static Rig NewChildRig(int pointerBase, Sprite baseSprite)
    {
        GameObject arrowGo = NewGo(pointerBase);
        GameObject childGo = NewGo(pointerBase + 100);
        SpriteRenderer renderer = new SpriteRenderer
        {
            Pointer = new IntPtr(pointerBase + 1),
            gameObject = childGo,
            sprite = baseSprite,
            color = new Color(1f, 1f, 1f, 1f),
            sharedMaterial = new Material(),
        };
        Arrow arrow = new Arrow
        {
            Pointer = new IntPtr(pointerBase + 2),
            gameObject = arrowGo,
            _spriteRenderer = renderer,
            _collider = new Collider2D { Pointer = new IntPtr(pointerBase + 3), gameObject = arrowGo, Label = "arrow" },
        };
        arrowGo.Components.Add(arrow);
        childGo.Components.Add(renderer);
        renderer.Writes.Clear();
        return new Rig { Go = arrowGo, Renderer = renderer, Arrow = arrow, BaseSprite = baseSprite, BaseColor = renderer.color };
    }

    /// <summary>英雄射手（GO + Archer 组件 + runtime 指针集合登记）。</summary>
    private static GameObject NewHero(int pointer, out Archer archer)
    {
        GameObject go = NewGo(pointer);
        archer = new Archer { Pointer = new IntPtr(pointer), gameObject = go };
        go.Components.Add(archer);
        HeroArcherRuntime.HeroPointers.Add(new IntPtr(pointer));
        return go;
    }

    /// <summary>活动墙 + N 个活动子碰撞体（登记进 FindObjectsOfType 场景替身），供穿墙挂点核对。</summary>
    private static Wall NewWall(int pointerBase, int colliderCount)
    {
        GameObject wallGo = NewGo(pointerBase);
        Wall wall = new Wall { gameObject = wallGo };
        for (int i = 0; i < colliderCount; i++)
        {
            GameObject colliderGo = NewGo(pointerBase + 10 + i);
            wall.Colliders.Add(new Collider2D
            {
                Pointer = new IntPtr(pointerBase + 20 + i),
                gameObject = colliderGo,
                Label = "wall#" + i,
            });
        }
        wallGo.Components.Add(wall);
        UnityEngine.Object.AllObjects.Add(wall);
        return wall;
    }

    private static GameObject NewPlainGo(int pointer) => NewGo(pointer);

    /// <summary>原生顺序：OnEnable Prefix → 原生 body → OnEnable Postfix → 原生写 owner。</summary>
    private static void SimulateSpawn(Rig rig, GameObject owner)
    {
        HeroArcherArrowVisuals.ResetArrow(rig.Arrow);        // [Prefix Priority.First]
        HeroArcherArrowVisuals.OnSpawn(rig.Arrow);           // [Postfix Priority.Last]
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
        Physics2D.Reset();
        UnityEngine.Object.ResetScene();                              // 清场景登记 + FindObjectsOfType 计数 + DestroyCalls
        Time.unscaledTime = 1000f;
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

    private static Stream FindResource() => typeof(HeroArcherArrowVisuals).Assembly.GetManifestResourceStream(ResourceName);

    // ============================================================
    // 钩子契约（反射）：只允许两个已核 native 目标 + 规定的优先级
    // ============================================================

    private static void RunHookContractTest()
    {
        Section("hook contract: exactly the two authorized native entry points, with the agreed priorities");
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
                if (type.Name == "HeroArcherWallPierce")
                {
                    // 穿墙 slice 必须只搭既有作用域的分支，绝不新增 Harmony 入口。
                    if (type.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false).Length > 0) pierceHasHarmony = true;
                    MethodInfo[] pierceMethods = type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    for (int m = 0; m < pierceMethods.Length; m++)
                    {
                        if (pierceMethods[m].GetCustomAttributes(typeof(HarmonyLib.HarmonyPrefix), false).Length > 0) pierceHasHarmony = true;
                        if (pierceMethods[m].GetCustomAttributes(typeof(HarmonyLib.HarmonyPostfix), false).Length > 0) pierceHasHarmony = true;
                        if (pierceMethods[m].GetCustomAttributes(typeof(HarmonyLib.HarmonyFinalizer), false).Length > 0) pierceHasHarmony = true;
                    }
                }
                if (type.Namespace != "KingdomEnhancedMod" || !type.Name.EndsWith("HeroArrowArt_Patch", StringComparison.Ordinal)) continue;
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
            Check(targets.Count == 2, "exactly 2 patched native methods (got " + string.Join(", ", targets.ToArray()) + ")");
            Check(targets.Count == 2 && targets[0] == "Arrow.OnEnable" && targets[1] == "ArrowAttack.FireArrowInternal",
                "targets are exactly Arrow.OnEnable + ArrowAttack.FireArrowInternal (no new native address, no short getter)");
            Check(prefixFirst.Count == 2, "both prefixes are Priority.First (reset before other OnEnable prefixes; scope opened first)");
            Check(postfixLast.Count == 1, "the OnEnable postfix is Priority.Last (our color wins over other postfixes)");
            Check(finalizers.Count == 1, "one finalizer on FireArrowInternal (scope end + ownership recheck, original exception rethrown)");
            Check(!pierceHasHarmony, "HeroArcherWallPierce declares no Harmony patch attributes (pierce rides the existing scope branches)");
        }
        catch (Exception e) { _failures++; Console.WriteLine("  FAIL  hook contract threw: " + e); }
    }

    // ============================================================
    // gold 模式：完整套件
    // ============================================================

    private static void RunGoldSuite()
    {
        Test("gold: hero main + extras painted; scope decides, stale pooled owner ignored; hunting identical", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(1101, out _);
            Rig main = NewRig(2100, _vanilla, new Color(1f, 1f, 1f, 0.6f));
            main.Arrow.archer = NewPlainGo(9901);                       // 池复用后残留的上一任 owner
            Rig extra1 = NewRig(2200, _vanilla);
            Rig extra2 = NewRig(2300, _vanilla);

            HeroArcherArrowVisuals.ShotToken token = HeroArcherArrowVisuals.BeginShot(hero);
            Check(HeroArcherArrowVisuals.ScopeDepth == 1, "scope pushed for the hero shot");
            Check(HeroArcherRuntime.IsHeroCalls == 1, "eligibility = exactly the IsHero seam (no target/combat guard: hunting takes the same path)");

            HeroArcherArrowVisuals.ResetArrow(main.Arrow);              // [Prefix Priority.First]
            HeroArcherArrowVisuals.OnSpawn(main.Arrow);                 // [Postfix Priority.Last]
            Check(main.Arrow.archer != null && main.Arrow.archer.Pointer.ToInt64() == 9901,
                "main arrow still carried the stale pooled owner while OnEnable ran");
            Check(IsGold(main), "main arrow painted although arrow.archer was stale (scope, not arrow.archer)");
            Check(Math.Abs(main.Renderer.color.a - 0.6f) <= 1e-3f, "gold keeps the arrow's original alpha (0.6)");
            main.Arrow.archer = hero;                                   // 原生在 OnEnable 之后才写 owner

            SimulateSpawn(extra1, hero);
            SimulateSpawn(extra2, hero);
            Check(IsGold(extra1) && IsGold(extra2), "extra arrows of the same shot painted (same scope)");
            Check(ReferenceEquals(main.Renderer.sprite, extra1.Renderer.sprite)
                && ReferenceEquals(extra1.Renderer.sprite, extra2.Renderer.sprite), "one shared gold sprite for all arrows");
            Check(HeroArcherArrowVisuals.TrackedCount == 3, "3 receipts tracked");
            Check(KingdomEnhancedPlugin.Instance.LogSource.Contains("sprite ready"), "one-time sprite load logged");

            HeroArcherArrowVisuals.EndShot(token);
            Check(HeroArcherArrowVisuals.ScopeDepth == 0, "scope unwound by the finalizer");
            Check(main.Renderer.Writes.Count == 2, "one sprite write + one color write per arrow");

            int writesBefore = main.Renderer.Writes.Count;
            Advance(5f);
            HeroArcherArrowVisuals.Tick();
            Check(HeroArcherArrowVisuals.TrackedCount == 3 && IsGold(main), "confirmed appearance persists while arrows fly");
            Check(main.Renderer.Writes.Count == writesBefore, "Tick never re-asserts colors/sprites every frame");
        });

        Test("gold: sprite identity — native 30x5 art at the 0.65 display scale (PPU 32/scale / pivot .5,.5 / 52 opaque)", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(1201, out _);
            Rig rig = NewRig(2400, _vanilla);
            SimulateShot(hero, rig);
            Check(IsGold(rig), "arrow painted with the embedded real asset");

            Sprite sprite = rig.Renderer.sprite;
            Check(sprite.rect.width == 30f && sprite.rect.height == 5f, "sprite rect 30x5 (native artemis_bow_arrow)");
            Check(Math.Abs(sprite.pixelsPerUnit - 32f / HeroArcherArrowVisuals.HeroArrowDisplayScale) <= 1e-3f,
                "PPU = 32 / HeroArrowDisplayScale (≈49.23; user-locked 0.65 display scale)");
            Check(Math.Abs(sprite.WorldWidth - (30f / 32f) * HeroArcherArrowVisuals.HeroArrowDisplayScale) <= 1e-4f,
                "world width = 0.9375 * HeroArrowDisplayScale (0.609375; 65% of the native arrow)");
            Check(Math.Abs(sprite.WorldHeight - (5f / 32f) * HeroArcherArrowVisuals.HeroArrowDisplayScale) <= 1e-4f,
                "world height = 5/32 * HeroArrowDisplayScale (65% of native)");
            Check(Math.Abs(sprite.pivot.x - 0.5f) <= 1e-6f && Math.Abs(sprite.pivot.y - 0.5f) <= 1e-6f, "pivot (0.5,0.5)");
            Check(sprite.extrude == 0u && sprite.meshType == SpriteMeshType.FullRect, "no extrude, FullRect");
            Check(sprite.texture.filterMode == FilterMode.Point && sprite.texture.wrapMode == TextureWrapMode.Clamp
                && sprite.texture.anisoLevel == 0, "Point / Clamp / no mip-aniso");
            Check(!sprite.texture.mipChain, "texture created without mipmaps");
            int opaque = 0;
            Color32[] pixels = sprite.texture.decodedPixels;
            for (int i = 0; i < pixels.Length; i++) if (pixels[i].a != 0) opaque++;
            Check(pixels.Length == 150 && opaque == 52, "real asset decodes to 150 px with 52 opaque (native shape)");
            Check(FindResource() != null, "embedded resource present");
        });

        Test("gold: 0.65-scaled hero arrow ignores a registered wall; pool reuse hands the pairs back", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(1251, out _);
            Wall wall = NewWall(2400, 2);                                 // 登记进 FindObjectsOfType 场景替身
            Rig rig = NewRig(2500, _vanilla);
            SimulateShot(hero, rig);
            Check(IsGold(rig), "painted at the display scale");
            Check(Physics2D.CountArrowPair(rig.Arrow._collider, true) == 2, "both wall colliders ignored for this hero arrow");
            Check(Physics2D.CountWallPair(wall.Colliders[0], true) == 1 && Physics2D.CountWallPair(wall.Colliders[1], true) == 1,
                "true pairs are exactly the wall colliders");
            Check(Physics2D.CountArrowPair(rig.Arrow._collider, false) == 0, "fresh spawn: nothing to restore yet");

            HeroArcherArrowVisuals.ResetArrow(rig.Arrow);                 // 池复用归还（OnEnable Prefix，先于原生 body）
            Check(Physics2D.CountArrowPair(rig.Arrow._collider, false) == 2, "pool reuse restores both pairs unconditionally");
            Check(IsBase(rig), "appearance is handed back by the same prefix");
        });

        Test("gold: non-hero / disabled shots stay native; other archers unchanged", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(1301, out _);
            GameObject other = NewPlainGo(1302);
            Rig a = NewRig(2500, _vanilla);
            Rig b = NewRig(2600, _vanilla);

            SimulateShot(other, a);                                     // 其他弓手射的箭
            Check(IsBase(a), "non-hero archer's arrow unchanged");
            Check(HeroArcherArrowVisuals.TrackedCount == 0, "no receipt for a non-hero shot");

            HeroArcherRuntime.EnabledState = false;                     // 功能关闭（含联机门）
            SimulateShot(hero, b);
            Check(IsBase(b), "disabled hero layer leaves arrows native");
            Check(HeroArcherArrowVisuals.TrackedCount == 0, "no receipt while disabled");

            // 反向：GO 上残留 hero 指针也不影响（资格只看作用域）
            HeroArcherRuntime.EnabledState = true;
            Rig c = NewRig(2700, _vanilla);
            c.Arrow.archer = hero;
            SimulateShot(other, c);
            Check(IsBase(c), "arrow whose archer field points at the hero is still untouched outside a hero shot");
        });

        Test("gold: nested non-hero masks outer hero; overflow masks then finalizer restores visibility", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(1401, out _);
            GameObject other = NewPlainGo(1402);

            Rig inner = NewRig(2800, _vanilla);
            Rig outer = NewRig(2900, _vanilla);
            HeroArcherArrowVisuals.ShotToken outerToken = HeroArcherArrowVisuals.BeginShot(hero);
            HeroArcherArrowVisuals.ShotToken innerToken = HeroArcherArrowVisuals.BeginShot(other);
            Check(HeroArcherArrowVisuals.ScopeDepth == 2, "nested scope depth 2");
            SimulateSpawn(inner, other);
            Check(IsBase(inner), "arrow spawned in the nested non-hero shot is NOT painted (mask beats outer hero)");
            HeroArcherArrowVisuals.EndShot(innerToken);
            Check(HeroArcherArrowVisuals.ScopeDepth == 1, "inner scope popped");
            SimulateSpawn(outer, hero);
            Check(IsGold(outer), "arrow spawned after the inner finalizer is painted again (outer hero restored)");
            HeroArcherArrowVisuals.EndShot(outerToken);
            Check(HeroArcherArrowVisuals.ScopeDepth == 0, "depth back to 0");

            HeroArcherArrowVisuals.ShotToken[] stack = new HeroArcherArrowVisuals.ShotToken[HeroArcherArrowVisuals.MaxStack];
            for (int i = 0; i < stack.Length; i++) stack[i] = HeroArcherArrowVisuals.BeginShot(hero);
            Check(HeroArcherArrowVisuals.ScopeDepth == HeroArcherArrowVisuals.MaxStack, "stack filled to MaxStack");

            Rig overflowArrow = NewRig(3000, _vanilla);
            HeroArcherArrowVisuals.ShotToken overflow = HeroArcherArrowVisuals.BeginShot(hero);
            SimulateSpawn(overflowArrow, hero);
            Check(IsBase(overflowArrow), "overflow shot (depth 17) is masked: arrow keeps native appearance");
            HeroArcherArrowVisuals.EndShot(overflow);
            Check(HeroArcherArrowVisuals.ScopeDepth == HeroArcherArrowVisuals.MaxStack, "overflow finalizer pops symmetrically");

            Rig afterOverflow = NewRig(3100, _vanilla);
            SimulateSpawn(afterOverflow, hero);
            Check(IsGold(afterOverflow), "outer scope visible again after the overflow finalizer");
            for (int i = stack.Length - 1; i >= 0; i--) HeroArcherArrowVisuals.EndShot(stack[i]);
            Check(HeroArcherArrowVisuals.ScopeDepth == 0, "all nested scopes unwound");
            HeroArcherArrowVisuals.EndShot(overflow);
            HeroArcherArrowVisuals.EndShot(stack[0]);
            Check(HeroArcherArrowVisuals.ScopeDepth == 0, "stale overflow/first tokens stay inert at depth 0");
        });

        Test("gold: appearance only — material/physics/damage/root/transform untouched, nothing destroyed", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(1501, out _);
            Rig rig = NewRig(3200, _vanilla);
            string before = rig.Arrow.NonVisualSignature();
            Material sharedMaterial = rig.Renderer.sharedMaterial;

            SimulateShot(hero, rig);
            Advance(5f);
            HeroArcherArrowVisuals.Tick();
            Check(IsGold(rig), "painted (control)");
            Check(rig.Arrow.NonVisualSignature() == before, "physics/collider/trail/damage/root transform/component set byte-identical");
            Check(ReferenceEquals(rig.Renderer.sharedMaterial, sharedMaterial), "native shared material instance untouched");
            Check(rig.Renderer.enabled && rig.Renderer.sortingLayerID == 4 && rig.Renderer.sortingOrder == 11, "renderer enable/sorting untouched");
            Check(rig.Renderer.Writes.Count == 2, "only sprite + color were ever written on the renderer");
            Check(UnityEngine.Object.DestroyCalls == 0 && !rig.Go.Destroyed, "no object destroyed");
        });

        Test("gold: unreadable activeSelf/despawn is unknown (backoff), despawn retires without waiting for OnEnable", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(2701, out _);
            Rig rig = NewRig(51000, _vanilla);
            SimulateShot(hero, rig);
            Check(IsGold(rig), "painted (control)");
            int writes = rig.Renderer.Writes.Count;

            rig.Go.ActiveSelfReadThrows = true;
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(HeroArcherArrowVisuals.TrackedCount == 1 && rig.Renderer.Writes.Count == writes,
                "unknown active state is not treated as active/confirmed: receipt kept, nothing written");

            rig.Go.ActiveSelfReadThrows = false;
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(IsGold(rig) && HeroArcherArrowVisuals.TrackedCount == 1, "readable + still active: appearance kept, no churn");

            rig.Go.activeSelf = false;                                  // 池回收
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(IsBase(rig) && HeroArcherArrowVisuals.TrackedCount == 0, "despawn restores on the tick itself (no OnEnable needed)");
        });

        Test("gold: confirm needs a real owner match — transient read failures are retried, not assumed", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(2801, out _);
            GameObject other = NewPlainGo(2802);
            Rig match = NewRig(52000, _vanilla);
            Rig mismatch = NewRig(52100, _vanilla);

            HeroArcherArrowVisuals.ShotToken token = HeroArcherArrowVisuals.BeginShot(hero);
            HeroArcherArrowVisuals.OnSpawn(match.Arrow);
            HeroArcherArrowVisuals.OnSpawn(mismatch.Arrow);
            match.Arrow.ArcherReadThrows = true;
            mismatch.Arrow.ArcherReadThrows = true;
            HeroArcherArrowVisuals.EndShot(token);
            Check(IsGold(match) && IsGold(mismatch) && HeroArcherArrowVisuals.TrackedCount == 2,
                "owner read failed at shot end: neither confirmed nor restored (kept unconfirmed)");

            match.Arrow.ArcherReadThrows = false;
            match.Arrow.archer = hero;                                  // 之后读到相符
            mismatch.Arrow.ArcherReadThrows = false;
            mismatch.Arrow.archer = other;                              // 之后读到别人
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(IsGold(match), "first-failure-then-match confirms (appearance kept)");
            Check(IsBase(mismatch), "first-failure-then-mismatch restores");
            Check(HeroArcherArrowVisuals.TrackedCount == 1, "only the confirmed receipt remains");
        });

        Test("gold: unreadable sprite pointer is unknown, not foreign (receipt kept, then recovered)", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(2901, out _);
            Rig rig = NewRig(53000, _vanilla);
            SimulateShot(hero, rig);
            Sprite gold = rig.Renderer.sprite;
            gold.PointerReadThrows = true;
            HeroArcherArrowVisuals.ResetArrow(rig.Arrow);
            Check(HeroArcherArrowVisuals.TrackedCount == 1, "unknown sprite ownership keeps the receipt (never assumed foreign)");
            Check(ReferenceEquals(rig.Renderer.sprite, gold), "nothing written while unknown");
            gold.PointerReadThrows = false;
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(IsBase(rig) && HeroArcherArrowVisuals.TrackedCount == 0, "readable again -> restore completes");
        });

        Test("gold: module owns RGB only — alpha is the game's (fade preserved, foreign RGB untouched)", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(3001, out _);
            Rig rig = NewRig(54000, _vanilla, new Color(1f, 1f, 1f, 1f));
            SimulateShot(hero, rig);
            rig.Renderer.color = new Color(0.99215686f, 0.90196079f, 0.44705883f, 0.2f);   // 游戏淡出：只改 alpha
            HeroArcherArrowVisuals.ResetArrow(rig.Arrow);
            Check(SameColor(rig.Renderer.color, new Color(rig.BaseColor.r, rig.BaseColor.g, rig.BaseColor.b, 0.2f)),
                "restore writes base RGB with the CURRENT alpha (0.2), not the base alpha");
            Check(HeroArcherArrowVisuals.TrackedCount == 0, "receipt retired after the RGB hand-back");

            Rig b = NewRig(54100, _vanilla);
            SimulateShot(hero, b);
            Color foreign = new Color(0.3f, 0.4f, 0.5f, 0.6f);
            b.Renderer.color = foreign;                                 // 第三方换掉 RGB
            HeroArcherArrowVisuals.ResetArrow(b.Arrow);
            Check(SameColor(b.Renderer.color, foreign), "foreign RGB (and its alpha) untouched");
            Check(ReferenceEquals(b.Renderer.sprite, b.BaseSprite), "our sprite still restored");
            Check(HeroArcherArrowVisuals.TrackedCount == 0, "receipt retired");
        });

        Test("gold: scope tokens are generation-checked (stale / duplicate / out-of-order finalizers are inert)", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(3101, out _);
            GameObject other = NewPlainGo(3102);

            HeroArcherArrowVisuals.ShotToken stale = HeroArcherArrowVisuals.BeginShot(hero);
            HeroArcherArrowVisuals.EndShot(stale);
            HeroArcherArrowVisuals.ShotToken current = HeroArcherArrowVisuals.BeginShot(hero);   // 同一栈下标 0
            HeroArcherArrowVisuals.EndShot(stale);                                               // 旧 finalizer
            Check(HeroArcherArrowVisuals.ScopeDepth == 1, "duplicate stale finalizer does not pop the new scope");

            Rig arrow = NewRig(55000, _vanilla);
            SimulateSpawn(arrow, hero);
            Check(IsGold(arrow) && HeroArcherArrowVisuals.TrackedCount == 1, "scope still live: arrow painted normally");
            HeroArcherArrowVisuals.EndShot(current);
            Check(HeroArcherArrowVisuals.ScopeDepth == 0, "proper finalizer unwinds");

            HeroArcherArrowVisuals.ShotToken outer = HeroArcherArrowVisuals.BeginShot(hero);
            HeroArcherArrowVisuals.ShotToken inner = HeroArcherArrowVisuals.BeginShot(other);
            HeroArcherArrowVisuals.EndShot(outer);                                               // 乱序（非栈顶）
            Check(HeroArcherArrowVisuals.ScopeDepth == 2, "out-of-order finalizer leaves the stack untouched");
            Rig maskedArrow = NewRig(55100, _vanilla);
            SimulateSpawn(maskedArrow, other);
            Check(IsBase(maskedArrow), "masked top still masks the outer hero scope");
            HeroArcherArrowVisuals.EndShot(inner);
            Check(HeroArcherArrowVisuals.ScopeDepth == 1, "masked inner scope pops by serial");
            Rig heroArrow = NewRig(55200, _vanilla);
            SimulateSpawn(heroArrow, hero);
            Check(IsGold(heroArrow), "outer hero scope visible again after the LIFO unwind");
            HeroArcherArrowVisuals.EndShot(outer);
            HeroArcherArrowVisuals.EndShot(inner);                                               // 重复的掩蔽凭据
            Check(HeroArcherArrowVisuals.ScopeDepth == 0, "second stale finalizer inert");
        });

        Test("gold: renderer on a child GameObject is rejected before any write", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(3201, out _);
            Rig child = NewChildRig(56000, _vanilla);
            SimulateShot(hero, child);
            Check(IsBase(child) && child.Renderer.Writes.Count == 0,
                "child renderer never painted (GO identity could not be re-checked)");
            Check(HeroArcherArrowVisuals.TrackedCount == 0, "no receipt for an unverifiable renderer");
        });

        Test("gold: post-shot ownership check (after FireArrowInternal returns) — only a proven foreign owner is undone", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(2601, out _);
            GameObject other = NewPlainGo(2602);
            Rig ours = NewRig(50000, _vanilla);
            Rig foreign = NewRig(50100, _vanilla);
            Rig unknown = NewRig(50200, _vanilla);
            Rig unknownThenOurs = NewRig(50300, _vanilla);

            HeroArcherArrowVisuals.ShotToken token = HeroArcherArrowVisuals.BeginShot(hero);
            SimulateSpawn(ours, hero);                              // 原生写 owner = hero
            SimulateSpawn(foreign, other);                          // 原生写 owner = 别人
            SimulateSpawn(unknown, null);                           // 原生还没写 owner（保持 null）
            HeroArcherArrowVisuals.OnSpawn(unknownThenOurs.Arrow);  // 上色
            unknownThenOurs.Arrow.ArcherReadThrows = true;          // 复核时读 owner 抛异常
            HeroArcherArrowVisuals.EndShot(token);
            Check(IsGold(ours) && HeroArcherArrowVisuals.TrackedCount == 3, "arrow of the shooter stays gold");
            Check(IsBase(foreign), "arrow whose owner is proven foreign is restored");
            Check(IsGold(unknown), "owner still null after the shot: not an incorrect rejection");
            Check(IsGold(unknownThenOurs), "owner read threw: treated as unknown, appearance kept");

            unknown.Arrow.archer = hero;                            // 原生稍后补写 owner
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(IsGold(unknown) && HeroArcherArrowVisuals.TrackedCount == 3, "late owner assignment confirms the receipt (no churn)");

            unknownThenOurs.Arrow.ArcherReadThrows = false;
            unknownThenOurs.Arrow.archer = other;                   // 之后确证是别人的箭
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(IsBase(unknownThenOurs), "proven foreign owner after the shot triggers the restore on the next tick");
            Check(HeroArcherArrowVisuals.TrackedCount == 2, "restored receipt retired, the shooter's receipts remain");
        });

        Test("gold: pool reuse restores in the OnEnable prefix (before native init); second life repaints once", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(1601, out _);
            GameObject other = NewPlainGo(1602);
            Rig rig = NewRig(3300, _vanilla, new Color(1f, 1f, 1f, 0.8f));
            SimulateShot(hero, rig);
            Check(IsGold(rig), "first life painted");
            int writesAfterPaint = rig.Renderer.Writes.Count;

            HeroArcherArrowVisuals.ShotToken token = HeroArcherArrowVisuals.BeginShot(other);   // 非英雄复用
            HeroArcherArrowVisuals.ResetArrow(rig.Arrow);                                        // [Prefix Priority.First]
            Check(IsBase(rig), "prefix already restored sprite+color — i.e. before the native OnEnable body ran");
            Check(rig.Renderer.Writes.Count == writesAfterPaint + 2, "restore = one sprite write + one color write");
            Check(HeroArcherArrowVisuals.TrackedCount == 0, "receipt retired after the restore");
            HeroArcherArrowVisuals.OnSpawn(rig.Arrow);                                          // [Postfix Priority.Last]
            Check(IsBase(rig), "non-hero reuse never repaints");
            HeroArcherArrowVisuals.EndShot(token);

            Rig again = NewRig(3400, _vanilla, new Color(1f, 1f, 1f, 0.8f));
            SimulateShot(hero, again);
            Check(IsGold(again) && HeroArcherArrowVisuals.TrackedCount == 1, "second hero shot paints exactly one receipt (no leak)");
        });

        Test("gold: repeated apply is idempotent (double postfix, same arrow)", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(1701, out _);
            Rig rig = NewRig(3500, _vanilla);
            HeroArcherArrowVisuals.ShotToken token = HeroArcherArrowVisuals.BeginShot(hero);
            SimulateSpawn(rig, hero);
            int writes = rig.Renderer.Writes.Count;
            HeroArcherArrowVisuals.OnSpawn(rig.Arrow);
            HeroArcherArrowVisuals.OnSpawn(rig.Arrow);
            Check(rig.Renderer.Writes.Count == writes, "repeat OnSpawn writes nothing");
            Check(HeroArcherArrowVisuals.TrackedCount == 1, "still exactly one receipt");
            Check(IsGold(rig), "still gold");
            HeroArcherArrowVisuals.EndShot(token);
        });

        Test("gold: toggle off / world retire / context loss / despawn all restore promptly", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(1801, out _);
            Rig a = NewRig(3600, _vanilla);
            Rig b = NewRig(3700, _vanilla);
            SimulateShot(hero, a, b);
            Check(HeroArcherArrowVisuals.TrackedCount == 2, "2 receipts live");

            HeroArcherRuntime.EnabledState = false;
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(IsBase(a) && IsBase(b), "feature off restores arrows already in flight");
            Check(HeroArcherArrowVisuals.TrackedCount == 0, "restored receipts retired");

            HeroArcherRuntime.EnabledState = true;
            Rig c = NewRig(3800, _vanilla);
            SimulateShot(hero, c);
            ArcherOptionsScope.WorldPtr = new IntPtr(7099);
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(IsBase(c), "world change restores");
            Check(HeroArcherArrowVisuals.TrackedCount == 0, "world-changed receipt retired");

            ArcherOptionsScope.WorldPtr = new IntPtr(7001);
            Rig d = NewRig(3900, _vanilla);
            SimulateShot(hero, d);
            ArcherOptionsScope.ContextAvailable = false;
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(IsBase(d), "context loss restores");
            Check(HeroArcherArrowVisuals.TrackedCount == 0, "context-lost receipt retired");

            ArcherOptionsScope.ContextAvailable = true;
            Rig e = NewRig(4000, _vanilla);
            SimulateShot(hero, e);
            Check(IsGold(e), "painted again after the context returns");
            e.Go.activeSelf = false;                                    // 池回收（OnDisable→despawn）
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(IsBase(e) && HeroArcherArrowVisuals.TrackedCount == 0, "despawned arrow restored before reuse");
        });

        Test("gold: foreign sprite/color are never clobbered (per-property CAS)", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(1901, out _);
            Rig a = NewRig(4100, _vanilla);
            SimulateShot(hero, a);
            Sprite foreign = new Sprite { Pointer = new IntPtr(8801), rect = new Rect(0f, 0f, 1f, 1f), pixelsPerUnit = 16f };
            Color foreignColor = new Color(0.1f, 0.2f, 0.3f, 0.4f);
            a.Renderer.sprite = foreign;
            a.Renderer.color = foreignColor;
            int writes = a.Renderer.Writes.Count;
            Advance(5f);
            HeroArcherArrowVisuals.ResetArrow(a.Arrow);                 // 归还尝试（池复用路径）
            Check(ReferenceEquals(a.Renderer.sprite, foreign) && SameColor(a.Renderer.color, foreignColor), "third-party sprite+color untouched");
            Check(a.Renderer.Writes.Count == writes, "no write attempted once both properties are foreign");
            Check(HeroArcherArrowVisuals.TrackedCount == 0, "receipt retired (ownership given up)");

            Rig b = NewRig(4200, _vanilla);
            SimulateShot(hero, b);
            b.Renderer.sprite = foreign;                                // 只被第三方换走 sprite
            HeroArcherArrowVisuals.ResetArrow(b.Arrow);
            Check(ReferenceEquals(b.Renderer.sprite, foreign), "foreign sprite kept");
            Check(SameColor(b.Renderer.color, b.BaseColor), "our color still restored");
            Check(HeroArcherArrowVisuals.TrackedCount == 0, "receipt retired after the property we still owned was restored");
        });

        Test("gold: half-applied paint and failed restore are retried, never silently dropped", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(2001, out _);

            Rig a = NewRig(4300, _vanilla);
            a.Renderer.WriteColorThrows = true;
            HeroArcherArrowVisuals.ShotToken token = HeroArcherArrowVisuals.BeginShot(hero);
            SimulateSpawn(a, hero);
            HeroArcherArrowVisuals.EndShot(token);
            Check(!ReferenceEquals(a.Renderer.sprite, a.BaseSprite), "half apply: sprite went through before the color write threw");
            Check(HeroArcherArrowVisuals.TrackedCount == 1, "half-applied receipt kept for rollback");
            int writes = a.Renderer.Writes.Count;
            Advance(0.1f);
            HeroArcherArrowVisuals.Tick();
            Check(a.Renderer.Writes.Count == writes, "backoff honoured: no retry before RetrySeconds");
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(IsBase(a) && HeroArcherArrowVisuals.TrackedCount == 0, "retry restored the half-applied sprite");

            Rig b = NewRig(4400, _vanilla);
            SimulateShot(hero, b);
            b.Renderer.WriteSpriteThrows = true;
            HeroArcherArrowVisuals.ResetArrow(b.Arrow);                 // 池复用归还失败
            Check(HeroArcherArrowVisuals.TrackedCount == 1, "failed restore keeps the receipt");
            Check(!ReferenceEquals(b.Renderer.sprite, b.BaseSprite), "sprite still gold (write failed)");
            b.Renderer.WriteSpriteThrows = false;
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(IsBase(b) && HeroArcherArrowVisuals.TrackedCount == 0, "later tick completes the ownership hand-back");

            Rig c = NewRig(4500, _vanilla);
            SimulateShot(hero, c);
            int writesBeforeUnknown = c.Renderer.Writes.Count;
            c.Renderer.ReadThrows = true;
            HeroArcherArrowVisuals.ResetArrow(c.Arrow);                 // 读失败 → 未知，保留回执
            Check(HeroArcherArrowVisuals.TrackedCount == 1, "read failure keeps the receipt (unknown != destroyed)");
            Check(c.Renderer.Writes.Count == writesBeforeUnknown, "nothing written while the state was unknown");
            c.Renderer.ReadThrows = false;
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(IsBase(c) && HeroArcherArrowVisuals.TrackedCount == 0, "readable again -> restore completes");
        });

        Test("gold: native wrapper churn (aliases) still matches by GO pointer + InstanceID", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(2101, out _);
            Rig rig = NewRig(4600, _vanilla);
            SimulateShot(hero, rig);
            Check(IsGold(rig), "painted");
            Check(HeroArcherArrowVisuals.TrackedCount == 1, "one receipt");

            Arrow alias = rig.AliasArrow();                             // 同一 native 对象的新 wrapper
            Arrow otherWrapper = rig.AliasArrow(1, 7);                  // 不同 native 对象
            HeroArcherArrowVisuals.ResetArrow(otherWrapper);
            Check(!IsBase(rig) && HeroArcherArrowVisuals.TrackedCount == 1, "unrelated wrapper does not touch/claim the receipt");
            HeroArcherArrowVisuals.ResetArrow(alias);
            Check(IsBase(rig) && HeroArcherArrowVisuals.TrackedCount == 0, "aliased wrapper restores: identity is pointer+InstanceID, not ReferenceEquals");
        });

        Test("gold: receipt cap 128 + fair bounded cleanup (one bad receipt cannot starve the rest)", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(2201, out _);
            Rig[] rigs = new Rig[HeroArcherArrowVisuals.Capacity];
            HeroArcherArrowVisuals.ShotToken token = HeroArcherArrowVisuals.BeginShot(hero);
            for (int i = 0; i < rigs.Length; i++)
            {
                rigs[i] = NewRig(10000 + i * 10, _vanilla);
                SimulateSpawn(rigs[i], hero);
            }
            HeroArcherArrowVisuals.EndShot(token);
            Check(HeroArcherArrowVisuals.TrackedCount == HeroArcherArrowVisuals.Capacity, "cap reached with owned receipts");

            Rig overflow = NewRig(20000, _vanilla);
            SimulateShot(hero, overflow);
            Check(IsBase(overflow), "129th arrow left native (no eviction, no arrow moved)");
            Check(HeroArcherArrowVisuals.TrackedCount == HeroArcherArrowVisuals.Capacity, "cap not exceeded");
            Check(UnityEngine.Object.DestroyCalls == 0 && !overflow.Go.Destroyed, "nothing destroyed");

            rigs[0].Renderer.ReadThrows = true;                         // 一条坏账
            HeroArcherRuntime.EnabledState = false;
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(HeroArcherArrowVisuals.TrackedCount == 1, "bad receipt kept, all others retired");
            Check(IsBase(rigs[1]) && IsBase(rigs[rigs.Length - 1]), "the bad receipt did not starve the rest of the table");

            rigs[0].Renderer.ReadThrows = false;
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(HeroArcherArrowVisuals.TrackedCount == 0 && IsBase(rigs[0]), "bad receipt completed on the next tick");
        });

        Test("gold: Clear() restores everything, keeps what it could not restore", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(2301, out _);
            Rig a = NewRig(30000, _vanilla);
            Rig b = NewRig(30100, _vanilla);
            SimulateShot(hero, a, b);
            b.Renderer.WriteSpriteThrows = true;
            HeroArcherArrowVisuals.Clear();
            Check(IsBase(a), "clean receipt restored by Clear");
            Check(HeroArcherArrowVisuals.TrackedCount == 1, "unrestorable receipt is NOT silently dropped");
            b.Renderer.WriteSpriteThrows = false;
            Advance(1f);
            HeroArcherArrowVisuals.Tick();
            Check(IsBase(b) && HeroArcherArrowVisuals.TrackedCount == 0, "responsibility completed on the retry");
        });

        Test("gold: exceptions never escape the native entry points", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(2401, out Archer heroArcher);
            Rig rig = NewRig(31000, _vanilla);

            HeroArcherRuntime.IsHeroThrows = true;
            HeroArcherArrowVisuals.ShotToken token = HeroArcherArrowVisuals.BeginShot(hero);
            SimulateSpawn(rig, hero);
            HeroArcherArrowVisuals.EndShot(token);
            Check(IsBase(rig) && HeroArcherArrowVisuals.ScopeDepth == 0, "IsHero throwing -> masked shot, no exception, depth symmetric");
            HeroArcherRuntime.IsHeroThrows = false;

            ArcherOptionsScope.Throws = true;
            token = HeroArcherArrowVisuals.BeginShot(hero);
            HeroArcherArrowVisuals.EndShot(token);
            Check(HeroArcherArrowVisuals.ScopeDepth == 0, "context read throwing -> masked shot, no exception");

            ArcherOptionsScope.Throws = false;
            hero.ComponentLookupThrows = true;
            token = HeroArcherArrowVisuals.BeginShot(hero);
            HeroArcherArrowVisuals.EndShot(token);
            Check(HeroArcherArrowVisuals.ScopeDepth == 0, "component lookup throwing -> masked shot, no exception");
            hero.ComponentLookupThrows = false;

            rig.Arrow.PointerReadThrows = true;
            token = HeroArcherArrowVisuals.BeginShot(hero);
            SimulateSpawn(rig, hero);
            HeroArcherArrowVisuals.ResetArrow(rig.Arrow);
            HeroArcherArrowVisuals.EndShot(token);
            Check(IsBase(rig) && HeroArcherArrowVisuals.ScopeDepth == 0, "arrow pointer throwing -> no exception, nothing painted");
            rig.Arrow.PointerReadThrows = false;

            Rig readFails = NewRig(32000, _vanilla);
            readFails.Renderer.ReadThrows = true;
            token = HeroArcherArrowVisuals.BeginShot(hero);
            SimulateSpawn(readFails, hero);
            HeroArcherArrowVisuals.EndShot(token);
            readFails.Renderer.ReadThrows = false;
            Check(IsBase(readFails) && HeroArcherArrowVisuals.TrackedCount == 0, "renderer read throwing -> no exception, no receipt");
            Check(!readFails.Go.Destroyed, "sanity: nothing destroyed");

            HeroArcherArrowVisuals.EndShot(default);                    // 陈旧 ticket
            HeroArcherArrowVisuals.EndShot(token);                      // 重复 ticket
            Check(HeroArcherArrowVisuals.ScopeDepth == 0, "stale/duplicate finalizer tickets are inert");
            Check(heroArcher != null, "sanity");
        });

        Test("gold: shared sprite is loaded once (no per-arrow allocation of texture/sprite)", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(2501, out _);
            Rig a = NewRig(33000, _vanilla);
            SimulateShot(hero, a);
            int spriteCounter = Sprite.SpriteCounter;
            Check(IsGold(a), "first paint loaded the sprite");
            int logLines = KingdomEnhancedPlugin.Instance.LogSource.Lines.Count;
            Rig b = NewRig(33100, _vanilla);
            Rig c = NewRig(33200, _vanilla);
            SimulateShot(hero, b, c);
            Check(Sprite.SpriteCounter == spriteCounter, "no further Sprite.Create — one shared sprite for all arrows");
            Check(ReferenceEquals(a.Renderer.sprite, b.Renderer.sprite)
                && ReferenceEquals(b.Renderer.sprite, c.Renderer.sprite), "same shared instance");
            Check(ReferenceEquals(a.Renderer.sprite.texture, c.Renderer.sprite.texture), "same shared texture");
            Check(KingdomEnhancedPlugin.Instance.LogSource.Lines.Count == logLines, "no per-arrow log spam");
            Check(!KingdomEnhancedPlugin.Instance.LogSource.Contains("unavailable"), "no fail-closed diagnostic (shared sprite stays live)");
        });
    }

    // ============================================================
    // fail-closed 模式：资源缺失 / 非法
    // ============================================================

    private static void RunFailClosedSuite(string expectedLogFragment)
    {
        Test("fail-closed: hero shot keeps native appearance, one diagnostic, no side effects", () =>
        {
            HeroArcherRuntime.EnabledState = true;
            GameObject hero = NewHero(3101, out _);
            Wall wall = NewWall(3150, 2);                                 // 注册面墙：让"不挂穿墙"成为非空断言
            Rig rig = NewRig(41000, _vanilla, new Color(1f, 1f, 1f, 0.7f));
            SimulateShot(hero, rig);
            Check(IsBase(rig), "arrow keeps native sprite and color");
            Check(HeroArcherArrowVisuals.TrackedCount == 0, "no receipt created (nothing to roll back)");
            Check(!rig.Go.Destroyed && !rig.Arrow.Destroyed && !rig.Renderer.Destroyed, "no arrow object destroyed");
            Check(UnityEngine.Object.DestroyCalls <= 1, "at most the module's own throwaway texture is destroyed (got "
                + UnityEngine.Object.DestroyCalls + ")");
            StubLogSource log = KingdomEnhancedPlugin.Instance.LogSource;
            Check(log.Lines.Count == 1, "exactly one diagnostic (FailOnce), got " + log.Lines.Count);
            if (log.Lines.Count > 0) Console.WriteLine("        log: " + log.Lines[0]);
            Check(log.Contains(expectedLogFragment), "log names the cause: " + expectedLogFragment);

            Rig second = NewRig(41100, _vanilla);
            SimulateShot(hero, second);
            Check(IsBase(second), "still fail-closed for later arrows");
            Check(log.Lines.Count == 1, "failure is logged once, not per arrow");

            Rig nonHero = NewRig(41200, _vanilla);
            SimulateShot(NewPlainGo(3102), nonHero);
            Check(IsBase(nonHero), "non-hero shots unaffected");

            // 三条箭（英雄/后续/非英雄）跑完后统一核对：本模式下穿墙一刻都没发生（含登记过的墙）。
            Check(Physics2D.Calls.Count == 0, "no wall pierce without a successful paint");
            Check(Physics2D.CountWallPair(wall.Colliders[0], true) == 0 && Physics2D.CountWallPair(wall.Colliders[1], true) == 0,
                "the registered wall colliders stay untouched");
            Check(HeroArcherWallPierce.CachedColliderCount == 0, "no wall snapshot built");
            Check(!log.Contains("[HeroArcherWallPierce]"), "no pierce activity in the fail-closed logs");
        });
    }
}
