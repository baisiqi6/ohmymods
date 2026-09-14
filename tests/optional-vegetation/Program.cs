using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

// 直接链接 production file（Regression.csproj 的 Compile Include）：
// 本套件在同一程序集里编译 il2cpp/PatchWorld_OptionalVegetation.cs，按 Harmony 的
// prefix/postfix/finalizer 调用顺序驱动边界桩里的原生链路，验证两支默认关闭开关的
// 契约、身份边界、池化生命周期与回收语义。运行方式见 run-tests.ps1（待 operator 执行）。

internal static class Program
{
    private static readonly Type Production = typeof(PatchWorld_OptionalVegetation);
    private static readonly Type CanSpawnHook = Hook(typeof(World), nameof(World.CanSpawnThicket));
    private static readonly Type AddThicketHook = Hook(typeof(World), nameof(World.AddThicket));
    private static readonly Type RemoveThicketHook = Hook(typeof(Grass), nameof(Grass.RemoveThicket));
    private static readonly Type FadeHook = Hook(typeof(ForestItem), nameof(ForestItem.FadeAndRemove));
    private static readonly MethodInfo CanSpawnPrefix = HookMethod(CanSpawnHook, "Prefix");
    private static readonly MethodInfo CanSpawnPostfix = HookMethod(CanSpawnHook, "Postfix");
    private static readonly MethodInfo CanSpawnFinalizer = HookMethod(CanSpawnHook, "Finalizer");
    private static readonly MethodInfo AddThicketPostfix = HookMethod(AddThicketHook, "Postfix");
    private static readonly MethodInfo RemoveThicketPostfix = HookMethod(RemoveThicketHook, "Postfix");
    private static readonly MethodInfo RemoveThicketPrefix = HookMethod(RemoveThicketHook, "Prefix");
    private static readonly MethodInfo FadePrefix = HookMethod(FadeHook, "Prefix");
    private static readonly FieldInfo RecordsField =
        Production.GetField("Records", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly FieldInfo HeldField =
        Production.GetField("Held", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly FieldInfo PendingField =
        Production.GetField("Pending", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly Dictionary<FieldInfo, object> InitialState = Production
        .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
        .ToDictionary(field => field, field => field.GetValue(null));
    private static readonly FieldInfo DeadField = Production
        .GetNestedType("ExtraThicket", BindingFlags.NonPublic)
        .GetField("Dead", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    private static int Passed, Failed;

    // 只统计仍然生效的登记（Dead 标记由 production 在下一次压缩时移除）
    private static int RecordsCount
    {
        get
        {
            int live = 0;
            foreach (object record in (System.Collections.IList)RecordsField.GetValue(null))
            {
                if (!(bool)DeadField.GetValue(record)) live++;
            }
            return live;
        }
    }
    private static int HeldCount => ((System.Collections.IDictionary)HeldField.GetValue(null)).Count;
    private static int PendingCount => ((System.Collections.IDictionary)PendingField.GetValue(null)).Count;

    // ---------------------------------------------------------------- 断言

    private static void Check(bool condition, string why)
    {
        if (!condition) throw new Exception(why);
    }

    private static void Eq<T>(T wanted, T got, string why)
    {
        if (!EqualityComparer<T>.Default.Equals(wanted, got))
            throw new Exception(why + ": expected " + wanted + ", got " + got);
    }

    private static void Near(float wanted, float got, string why)
    {
        if (MathF.Abs(wanted - got) > 0.0001f)
            throw new Exception(why + ": expected " + wanted + ", got " + got);
    }

    // ---------------------------------------------------------------- hook 调用

    private static Type Hook(Type target, string method)
    {
        Type found = Production.Assembly.GetTypes().FirstOrDefault(type => type
            .GetCustomAttributes<HarmonyLib.HarmonyPatch>()
            .Any(attribute => attribute.TargetType == target && attribute.MethodName == method));
        if (found == null) throw new Exception("missing production hook for " + target.Name + "." + method);
        return found;
    }

    private static MethodInfo HookMethod(Type hook, string name)
    {
        MethodInfo method = hook.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null) throw new Exception("missing " + hook.Name + "." + name);
        return method;
    }

    private static Dictionary<string, object> Call(MethodInfo method, params (string Name, object Value)[] arguments)
    {
        var supplied = new Dictionary<string, object>();
        foreach ((string name, object value) in arguments) supplied[name] = value;
        ParameterInfo[] parameters = method.GetParameters();
        var values = new object[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            ParameterInfo parameter = parameters[i];
            if (!supplied.TryGetValue(parameter.Name, out object value))
            {
                if (parameter.Name == "__exception") value = null;
                else if (parameter.ParameterType.IsByRef)
                    value = Activator.CreateInstance(parameter.ParameterType.GetElementType());
                else if (parameter.ParameterType.IsValueType) value = Activator.CreateInstance(parameter.ParameterType);
                else throw new Exception("unsupplied hook parameter " + parameter.Name + " on " + method.Name);
            }
            values[i] = value;
        }
        method.Invoke(null, values);
        var result = new Dictionary<string, object>();
        for (int i = 0; i < parameters.Length; i++) result[parameters[i].Name] = values[i];
        return result;
    }

    private static void Invoke(MethodInfo method, params (string Name, object Value)[] arguments)
    {
        Call(method, arguments);
    }

    private static PatchWorld_OptionalVegetation.CanSpawnLease Prefix(World world)
        => (PatchWorld_OptionalVegetation.CanSpawnLease)Call(CanSpawnPrefix, ("__instance", world))["__state"];

    private static void RunFinalizer(World world, PatchWorld_OptionalVegetation.CanSpawnLease lease)
        => Invoke(CanSpawnFinalizer, ("__exception", (object)null), ("__state", lease));

    // 原生调用 + Harmony hook 顺序（与游戏内一致）
    private static bool CanSpawn(World world, Grass grass)
    {
        PatchWorld_OptionalVegetation.CanSpawnLease lease = Prefix(world);
        bool native;
        try
        {
            native = world.CanSpawnThicket(grass);
        }
        catch (Exception error)
        {
            Dictionary<string, object> finalizer = Call(CanSpawnFinalizer, ("__exception", error), ("__state", lease));
            throw (Exception)finalizer["__exception"];
        }
        Dictionary<string, object> postfix = Call(CanSpawnPostfix,
            ("__instance", world), ("grass", grass), ("__result", native), ("__state", lease));
        return (bool)postfix["__result"];
    }

    private static bool SpawnWithHooks(Grass grass)
    {
        int before = grass.WorldRef.AddThicketCalls;
        grass.SpawnThicket();
        if (grass.WorldRef.AddThicketCalls == before) return false; // 原生 SpawnThicket 早退，不触发 AddThicket
        Invoke(AddThicketPostfix, ("__instance", grass.WorldRef), ("grass", grass));
        return true;
    }

    // 原生 RemoveThicket + Harmony postfix（原生异常时 postfix 不运行，与原版一致）
    private static void RemoveThicket(Grass grass)
    {
        Invoke(RemoveThicketPrefix, ("__instance", grass));
        grass.RemoveThicket();
        Invoke(RemoveThicketPostfix, ("__instance", grass));
    }

    private static void DenseSpawn(World world, Grass grass)
    {
        Check(CanSpawn(world, grass), "原生判定应放行半间距生成");
        Check(SpawnWithHooks(grass), "原生 SpawnThicket 应生成并登记");
    }

    // 原生 GrassUpdate 的 Stage7 分支（测试驱动：不复制生产决策）
    private static void GrassTick(Grass grass)
    {
        if (CanSpawn(grass.WorldRef, grass)) SpawnWithHooks(grass);
        else RemoveThicket(grass);
    }

    // ---------------------------------------------------------------- 场景构造

    private static World NewWorld()
    {
        var layerObject = new GameObject("GameLayer");
        var world = new World(layerObject);
        Managers.Inst = new Managers { world = world };
        OptionalQoLScope.CurrentSceneHandle = layerObject.scene.handle;
        return world;
    }

    private static Grass NewGrass(World world, float x)
    {
        var go = new GameObject("Grass");
        go.scene.handle = world.gameLayer.gameObject.scene.handle;
        go.transform.position = new Vector3(x);
        Grass grass = go.AddComponent<Grass>();
        grass.WorldRef = world;
        return grass;
    }

    // 既有原生灌木：直接走原生 SpawnThicket，不经过 AddThicket postfix
    private static Grass NaturalGrass(World world, float x)
    {
        Grass grass = NewGrass(world, x);
        grass.SpawnThicket();
        return grass;
    }

    // 真实 Thicket prefab 的子层（Back L / Back R）
    private static void AddChildLayer(GameObject thicket, string name, Color color)
    {
        var child = new GameObject(name);
        child.scene.handle = thicket.scene.handle;
        child.transform.SetParent(thicket.transform);
        SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
        renderer.color = color;
    }

    private static Forest NewForest(World world)
    {
        var go = new GameObject("Forest");
        go.scene.handle = world.gameLayer.gameObject.scene.handle;
        go.transform.SetParent(world.gameLayer);
        return go.AddComponent<Forest>();
    }

    private static ForestItem NewItem(World world, Forest forest)
    {
        var go = new GameObject("ForestItem");
        go.scene.handle = world.gameLayer.gameObject.scene.handle;
        ForestItem item = go.AddComponent<ForestItem>();
        item._forest = forest;
        return item;
    }

    // ---------------------------------------------------------------- 测试驱动

    private static void Setup()
    {
        Time.time = 0f;
        Time.timeScale = 1f;
        UnityEngine.Random.Calls = 0;
        UnityEngine.Random.Unit = 0.5f; // Range(0.5f,1.5f) == 1.0f，让 1/3 断言确定
        Managers.Inst = null;
        NetworkBigBoss.HasWorldAuth = true;
        ModConfig.Enabled.Value = true;
        ModConfig.DenseThicketsEnabled.Value = false;
        ModConfig.FastForestRecedeEnabled.Value = false;
        OptionalQoLScope.CurrentWorldIsActive = true;
        OptionalQoLScope.CurrentSceneHandle = 0;
        OptionalQoLScope.IsCurrentCalls = 0;
        KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
        ResetProduction();
    }

    private static void ResetProduction()
    {
        foreach (KeyValuePair<FieldInfo, object> pair in InitialState)
        {
            object value = pair.Key.GetValue(null);
            if (value != null && value is not string)
            {
                MethodInfo clear = value.GetType().GetMethod("Clear", Type.EmptyTypes);
                if (clear != null)
                {
                    clear.Invoke(value, null);
                    continue;
                }
            }
            if (pair.Key.IsLiteral || pair.Key.IsInitOnly) continue;
            pair.Key.SetValue(null, pair.Value);
        }
    }

    private static void Test(string name, Action body)
    {
        Setup();
        try
        {
            body();
            Passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            Failed++;
            Console.WriteLine("FAIL " + name + ": " + error.GetBaseException().Message);
        }
    }

    public static int Main()
    {
        Test("颜色恢复失败后同指针重生的自然灌木不得再被旧记录触碰", () =>
        {
            World world = NewWorld(); ModConfig.DenseThicketsEnabled.Value = true;
            NaturalGrass(world, 0f); Grass extra = NewGrass(world, 3.5f); DenseSpawn(world, extra);
            GameObject thicket = extra._thicket;
            SpriteRenderer sprite = thicket.GetComponent<SpriteRenderer>();
            ModConfig.DenseThicketsEnabled.Value = false;
            Time.time = 1f; PatchWorld_OptionalVegetation.Tick();
            Time.time = 1.2f; PatchWorld_OptionalVegetation.Tick();
            sprite.FailColorWrite = true; RemoveThicket(extra);
            sprite.FailColorWrite = false;
            extra._thicket = thicket; thicket.activeInHierarchy = true;
            sprite.color = new Color(0.2f, 0.3f, 0.4f, 0.9f);
            world.AddThicket(extra);
            Invoke(AddThicketPostfix, ("__instance", world), ("grass", extra));
            int removals = extra.RemoveCalls;
            Time.time = 2f; PatchWorld_OptionalVegetation.Tick();
            Time.time = 3f; PatchWorld_OptionalVegetation.Tick();
            Eq(removals, extra.RemoveCalls, "旧dead记录不能再次调用Remove");
            Check(extra._thicket == thicket && thicket.activeInHierarchy, "新自然生命保留");
            Near(0.9f, sprite.color.a, "旧颜色凭据不能写新生命");
        });
        Test("淡出和归还均保留外部第三色", () =>
        {
            World world = NewWorld(); ModConfig.DenseThicketsEnabled.Value = true;
            NaturalGrass(world, 0f); Grass extra = NewGrass(world, 3.5f); DenseSpawn(world, extra);
            SpriteRenderer sprite = extra._thicket.GetComponent<SpriteRenderer>();
            ModConfig.DenseThicketsEnabled.Value = false;
            Time.time = 1f; PatchWorld_OptionalVegetation.Tick();
            Time.time = 1.2f; PatchWorld_OptionalVegetation.Tick();
            sprite.color = new Color(0.2f, 0.3f, 0.4f, 0.9f);
            Time.time = 1.3f; PatchWorld_OptionalVegetation.Tick();
            Near(0.9f, sprite.color.a, "淡出不覆盖第三色");
            RemoveThicket(extra);
            Near(0.9f, sprite.color.a, "移除归还不覆盖第三色");
            Near(0.2f, sprite.color.r, "RGB外部修改保留");
        });
        Test("原生中途回收先恢复三层颜色，失败的池中归还保留并重试", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            NaturalGrass(world, 0f);
            Grass extra = NewGrass(world, 3.5f);
            DenseSpawn(world, extra);
            GameObject thicket = extra._thicket;
            AddChildLayer(thicket, "Back L", new Color(0.5f, 0.6f, 0.7f, 1f));
            AddChildLayer(thicket, "Back R", new Color(0.3f, 0.4f, 0.5f, 0.8f));
            var layers = thicket.GetComponentsInChildren<SpriteRenderer>(true);
            var original = layers.Select(x => x.color).ToArray();
            ModConfig.DenseThicketsEnabled.Value = false;
            Time.time = 1f; PatchWorld_OptionalVegetation.Tick();
            Time.time = 1.2f; PatchWorld_OptionalVegetation.Tick();
            Near(0.5f, layers[0].color.a, "先实际淡到一半");
            layers[1].FailColorWrite = true;
            RemoveThicket(extra);
            Check(extra._thicket == null, "不拦截原生自然移除");
            Near(original[0].a, layers[0].color.a, "根层在入池前还原");
            Near(original[2].a, layers[2].color.a, "另一子层在入池前还原");
            Check(PatchWorld_OptionalVegetation.IsCleaning, "失败层仍阻止重新开启");
            layers[1].FailColorWrite = false;
            Time.time = 1.5f; PatchWorld_OptionalVegetation.Tick();
            Near(original[1].a, layers[1].color.a, "未复用池对象失败层重试还原");
            Check(!PatchWorld_OptionalVegetation.IsCleaning, "确认全部恢复后结束清理");
        });

        Test("默认关闭：判定与原生一致、零写入、零登记", () =>
        {
            World world = NewWorld();
            Grass anchor = NaturalGrass(world, 0f);
            Grass near = NewGrass(world, 4f);
            Check(!CanSpawn(world, near), "原生间距内仍拒绝");
            Near(6f, world.RawThicketSpacing, "thicketSpacing 未被改写");
            Eq(0, HeldCount, "关闭时不持有临时 scalar");
            Eq(0, PendingCount, "关闭时无待恢复");
            Eq(0, RecordsCount, "未登记额外实例");
            Eq("未激活", PatchWorld_OptionalVegetation.DenseStatus, "面板状态未激活");
            Check(!PatchWorld_OptionalVegetation.IsCleaning, "不在回收期");
            PatchWorld_OptionalVegetation.Tick();
            Eq(0, RecordsCount, "Tick 无副作用");
            Check(PatchWorld_OptionalVegetation.TrySetDenseThickets(false), "关闭请求总是被接受");
        });

        Test("开启：半间距放行且每次还原，不叠乘", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(world, 0f);
            Grass mid = NewGrass(world, 4f);
            Grass close = NewGrass(world, 2f);
            Check(CanSpawn(world, mid), "4 大于半间距 3：放行");
            Near(6f, world.RawThicketSpacing, "调用后立即还原");
            Eq(0, HeldCount, "调用栈已退出");
            Check(CanSpawn(world, mid), "重复判定仍放行");
            Near(6f, world.RawThicketSpacing, "重复判定不叠乘成 1.5");
            Check(!CanSpawn(world, close), "2 小于半间距：仍拒绝");
            Near(6f, world.RawThicketSpacing, "拒绝路径同样还原");
        });

        Test("开启不绕过城墙与 auth", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(world, 0f);
            Grass outside = NewGrass(world, 12f);
            Check(CanSpawn(world, outside), "城墙外正常放行");
            world.LeftBorder = -10f;
            world.RightBorder = 10f;
            Grass inside = NewGrass(world, 5f);
            Check(!CanSpawn(world, inside), "城墙内原生条件不放行");
            Near(6f, world.RawThicketSpacing, "城墙拒绝路径也还原");
            NetworkBigBoss.HasWorldAuth = false;
            Check(!CanSpawn(world, outside), "无世界权威不放行");
            Near(6f, world.RawThicketSpacing, "无权威时不改写 thicketSpacing");
            Eq(0, HeldCount, "无权威时不持有租约");
        });

        Test("额外实例按原始间距与原生锚点判定，自然灌木不误标", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(world, 0f);
            Grass extra = NewGrass(world, 4f);
            DenseSpawn(world, extra);
            Eq(1, RecordsCount, "4 落在原始间距 6 内：登记为额外");
            Check(!PatchWorld_OptionalVegetation.IsCleaning, "开启期间不视为回收期");
            Grass natural = NewGrass(world, 12f);
            DenseSpawn(world, natural);
            Eq(1, RecordsCount, "原始间距外的新灌木不登记");
        });

        Test("无锚点时生成的新灌木不登记为额外", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass lonely = NewGrass(world, 0f);
            DenseSpawn(world, lonely);
            Eq(0, RecordsCount, "没有原生锚点时属于自然灌木");
        });

        Test("开启不删除既有原生灌木", () =>
        {
            World world = NewWorld();
            Grass a = NaturalGrass(world, 0f);
            Grass b = NaturalGrass(world, 6.5f);
            ModConfig.DenseThicketsEnabled.Value = true;
            GrassTick(a);
            GrassTick(b);
            Eq(0, a.RemoveCalls, "既有灌木 A 未被原生移除");
            Eq(0, b.RemoveCalls, "既有灌木 B 未被原生移除");
            Check(a._thicket != null && b._thicket != null, "两组原生灌木都在");
            Eq(0, RecordsCount, "既有灌木不计入额外");
        });

        Test("关闭只回收额外实例并保留原生灌木", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(world, 0f);
            Grass extra = NewGrass(world, 3.5f);
            extra.ThicketHasFx = true;
            DenseSpawn(world, extra);
            Eq(1, RecordsCount, "登记一个额外实例");
            SpriteRendererFX fx = extra._thicket.GetComponent<SpriteRendererFX>();
            Check(fx != null, "本测试的灌木带 FX 组件");
            ModConfig.DenseThicketsEnabled.Value = false;
            Check(PatchWorld_OptionalVegetation.IsCleaning, "关闭后进入回收期");
            Check(CanSpawn(world, anchor), "回收期原生灌木保留判定为真");
            Check(CanSpawn(world, extra), "回收期额外实例自身不被原生提前清除");
            Grass probe = NewGrass(world, 3.6f);
            Check(!CanSpawn(world, probe), "尚无灌木的草仍被未回收的额外实例阻拦");
            Time.time = 1f;
            PatchWorld_OptionalVegetation.Tick();
            Eq(1, fx.FadeCalls, "Tick 启动淡出");
            Near(0.4f, fx.LastFadeSeconds, "淡出时长");
            Eq(0, extra.RemoveCalls, "淡出未完成前不调用原生回收");
            Time.time = 1.5f;
            PatchWorld_OptionalVegetation.Tick();
            Eq(1, extra.RemoveCalls, "淡出结束调用一次原生 RemoveThicket");
            Check(extra._thicket == null, "原生交还 _thicket");
            Check(!world._grassWithThicket.Contains(extra), "原生集合已移除额外实例");
            Eq(0, anchor.RemoveCalls, "原生灌木从未被移除");
            Check(anchor._thicket != null, "原生灌木仍在");
            Eq(0, RecordsCount, "登记已清空");
            Check(!PatchWorld_OptionalVegetation.IsCleaning, "回收期结束");
        });

        Test("淡出覆盖所有子层 renderer 并在回收前整色还原", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(world, 0f);
            Grass extra = NewGrass(world, 3.5f);
            DenseSpawn(world, extra);
            GameObject thicket = extra._thicket;
            AddChildLayer(thicket, "Back L", new Color(0.5f, 0.6f, 0.7f, 1f));
            AddChildLayer(thicket, "Back R", new Color(0.3f, 0.4f, 0.5f, 0.8f));
            SpriteRenderer[] layers = thicket.GetComponentsInChildren<SpriteRenderer>(true);
            Eq(3, layers.Length, "根 + Back L + Back R 三层");
            Color[] baseColors = layers.Select(layer => layer.color).ToArray();
            ModConfig.DenseThicketsEnabled.Value = false;
            Time.time = 1f;
            PatchWorld_OptionalVegetation.Tick();
            Time.time = 1.2f;
            PatchWorld_OptionalVegetation.Tick();
            Near(0.5f, layers[0].color.a, "根层淡到一半");
            Near(0.5f, layers[1].color.a, "Back L 同样淡出");
            Near(0.4f, layers[2].color.a, "Back R 按自身基色淡出");
            for (int i = 0; i < layers.Length; i++)
            {
                Near(baseColors[i].r, layers[i].color.r, "第 " + i + " 层 RGB 未被改动");
                Near(baseColors[i].g, layers[i].color.g, "第 " + i + " 层 RGB 未被改动");
                Near(baseColors[i].b, layers[i].color.b, "第 " + i + " 层 RGB 未被改动");
            }
            Time.time = 1.5f;
            PatchWorld_OptionalVegetation.Tick();
            Check(extra._thicket == null, "对象已回收");
            for (int i = 0; i < layers.Length; i++)
            {
                Check(layers[i].color.a == baseColors[i].a && layers[i].color.r == baseColors[i].r
                    && layers[i].color.g == baseColors[i].g && layers[i].color.b == baseColors[i].b,
                    "第 " + i + " 层整色已交还池化对象");
            }
        });

        Test("存在 SpriteRendererFX 的实例走原生淡出", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(world, 0f);
            Grass extra = NewGrass(world, 3.5f);
            extra.ThicketHasFx = true;
            DenseSpawn(world, extra);
            SpriteRendererFX fx = extra._thicket.GetComponent<SpriteRendererFX>();
            SpriteRenderer root = extra._thicket.GetComponent<SpriteRenderer>();
            Color before = root.color;
            ModConfig.DenseThicketsEnabled.Value = false;
            Time.time = 1f;
            PatchWorld_OptionalVegetation.Tick();
            Eq(1, fx.FadeCalls, "使用原生 FadeOut");
            Near(0.4f, fx.LastFadeSeconds, "淡出时长");
            Time.time = 1.2f;
            PatchWorld_OptionalVegetation.Tick();
            Check(root.color.a == before.a, "FX 路径不改 renderer 颜色");
            Time.time = 1.5f;
            PatchWorld_OptionalVegetation.Tick();
            Eq(1, extra.RemoveCalls, "淡出后调用原生回收");
            Eq(0, RecordsCount, "登记清空");
        });

        Test("回收期内拒绝再次开启并纠正外部强设", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(world, 0f);
            Grass extra = NewGrass(world, 3.5f);
            DenseSpawn(world, extra);
            PatchWorld_OptionalVegetation.Tick(); // 观察一次开启状态
            ModConfig.DenseThicketsEnabled.Value = false;
            Time.time = 1f;
            PatchWorld_OptionalVegetation.Tick(); // 起淡，进入回收批次
            Check(PatchWorld_OptionalVegetation.IsCleaning, "进入回收期");
            Check(!PatchWorld_OptionalVegetation.TrySetDenseThickets(true), "面板再次开启被拒绝");
            Check(!ModConfig.DenseThicketsEnabled.Value, "配置保持关闭");
            ModConfig.DenseThicketsEnabled.Value = true; // 外部强设 false->true
            PatchWorld_OptionalVegetation.Tick();
            Check(!ModConfig.DenseThicketsEnabled.Value, "外部强设被纠正回关闭");
            Grass probe = NewGrass(world, 3.4f);
            Check(!CanSpawn(world, probe), "回收期不以半间距放行");
            Near(6f, world.RawThicketSpacing, "回收期判定后仍然还原");
            Time.time = 1.6f;
            PatchWorld_OptionalVegetation.Tick();
            Eq(0, RecordsCount, "回收完成");
            Check(PatchWorld_OptionalVegetation.TrySetDenseThickets(true), "清完后允许再次开启");
            Check(ModConfig.DenseThicketsEnabled.Value, "配置已开启");
        });

        Test("TrySet 关闭只落配置，回收由 Tick 推进", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(world, 0f);
            Grass extra = NewGrass(world, 3.5f);
            extra.ThicketHasFx = true;
            DenseSpawn(world, extra);
            SpriteRendererFX fx = extra._thicket.GetComponent<SpriteRendererFX>();
            Time.time = 1f;
            Check(PatchWorld_OptionalVegetation.TrySetDenseThickets(false), "关闭请求被接受");
            Check(!ModConfig.DenseThicketsEnabled.Value, "配置已关闭");
            Eq(0, extra.RemoveCalls, "TrySet 不直接调用原生回收");
            Eq(0, fx.FadeCalls, "TrySet 不直接起淡");
            Check(extra._thicket != null, "对象原样保留，等待主线程 Tick");
            PatchWorld_OptionalVegetation.Tick();
            Eq(1, fx.FadeCalls, "Tick 才启动淡出");
        });

        Test("authority 丢失与未知 world 保留所有权，恢复后继续回收", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(world, 0f);
            Grass extra = NewGrass(world, 3.5f);
            extra.ThicketHasFx = true;
            DenseSpawn(world, extra);
            SpriteRendererFX fx = extra._thicket.GetComponent<SpriteRendererFX>();
            ModConfig.DenseThicketsEnabled.Value = false;
            NetworkBigBoss.HasWorldAuth = false;
            Time.time = 1f;
            PatchWorld_OptionalVegetation.Tick();
            Eq(0, fx.FadeCalls, "无权威时不起淡");
            Eq(0, extra.RemoveCalls, "无权威时不调用原生回收");
            Eq(1, RecordsCount, "所有权保留");
            Check(PatchWorld_OptionalVegetation.IsCleaning, "仍处于回收期");
            Managers.Inst = null; // 未知 world
            PatchWorld_OptionalVegetation.Tick();
            Eq(1, RecordsCount, "未知 world 下保留登记");
            Managers.Inst = new Managers { world = world };
            NetworkBigBoss.HasWorldAuth = true;
            PatchWorld_OptionalVegetation.Tick();
            Eq(1, fx.FadeCalls, "恢复后继续起淡");
            Time.time = 1.5f;
            PatchWorld_OptionalVegetation.Tick();
            Eq(1, extra.RemoveCalls, "恢复后完成原生回收");
            Eq(0, RecordsCount, "清空登记");
        });

        Test("关闭 Mod(scope 停用) 后停止新增并清理既有额外实例", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(world, 0f);
            Grass extra = NewGrass(world, 3.5f);
            extra.ThicketHasFx = true;
            DenseSpawn(world, extra);
            SpriteRendererFX fx = extra._thicket.GetComponent<SpriteRendererFX>();
            PatchWorld_OptionalVegetation.Tick(); // 登记观察值
            ModConfig.Enabled.Value = false;      // 总开关关闭：scope 停用（不依赖任何 biome）
            Grass probe = NewGrass(world, 4.5f);
            Check(!CanSpawn(world, probe), "scope 停用后不再按半间距放行");
            Near(6f, world.RawThicketSpacing, "停用后不改写 thicketSpacing");
            Check(PatchWorld_OptionalVegetation.IsCleaning, "既有额外实例进入回收期");
            Time.time = 1f;
            PatchWorld_OptionalVegetation.Tick();
            Check(ModConfig.DenseThicketsEnabled.Value, "同一次持有意图不被改写");
            Eq(1, fx.FadeCalls, "scope 停用也继续回收");
            Time.time = 1.5f;
            PatchWorld_OptionalVegetation.Tick();
            Eq(1, extra.RemoveCalls, "回收完成");
            Eq(0, RecordsCount, "登记清空");
            ModConfig.Enabled.Value = true;
            Check(PatchWorld_OptionalVegetation.TrySetDenseThickets(true), "清完后可再次开启");
        });

        Test("回收未生效时保留登记并重试，不以计时器判定清完", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(world, 0f);
            Grass extra = NewGrass(world, 3.5f);
            DenseSpawn(world, extra);
            extra.RemoveNoOp = true; // 原生守卫未满足：_thicket 原样保留
            ModConfig.DenseThicketsEnabled.Value = false;
            Time.time = 1f;
            PatchWorld_OptionalVegetation.Tick();
            Time.time = 1.5f;
            PatchWorld_OptionalVegetation.Tick();
            Eq(1, extra.RemoveCalls, "已尝试原生回收");
            Check(extra._thicket != null, "原生化未生效");
            Eq(1, RecordsCount, "登记保留");
            Check(PatchWorld_OptionalVegetation.IsCleaning, "仍在回收期");
            extra.RemoveNoOp = false;
            Time.time = 2.1f; // 超过 0.5s 重试间隔
            PatchWorld_OptionalVegetation.Tick();
            Eq(2, extra.RemoveCalls, "重试原生回收");
            Eq(0, RecordsCount, "身份确认后才清空登记");
        });

        Test("换 world 后旧对象不被触碰、密度不叠乘", () =>
        {
            World first = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(first, 0f);
            Grass extra = NewGrass(first, 4f);
            DenseSpawn(first, extra);
            Eq(1, RecordsCount, "旧 world 有额外实例登记");
            PatchWorld_OptionalVegetation.Tick();
            World second = NewWorld(); // 读档/换岛：新 world 接管
            PatchWorld_OptionalVegetation.Tick();
            Eq(0, extra.RemoveCalls, "旧 world 对象不再被调用");
            Eq(0, first.RemoveThicketCalls, "旧 world 集合未被触碰");
            Eq(0, second.RemoveThicketCalls, "新 world 没有收到旧对象的移除调用");
            Near(6f, first.RawThicketSpacing, "旧 world 的 thicketSpacing 保持原值");
            Near(6f, second.RawThicketSpacing, "新 world 密度不叠乘");
            Eq(0, RecordsCount, "旧登记随世界更换作废");
            Check(!PatchWorld_OptionalVegetation.IsCleaning, "换 world 后自动可再次开启");
            Grass newAnchor = NaturalGrass(second, 0f);
            Grass newNear = NewGrass(second, 4f);
            Check(CanSpawn(second, newNear), "新 world 仍按半间距放行");
            Near(6f, second.RawThicketSpacing, "新 world 调用后还原");
        });

        Test("森林退缩：off 原版、on 三分之一、非退缩链路不变", () =>
        {
            World world = NewWorld();
            Forest forest = NewForest(world);
            ForestItem item = NewItem(world, forest);
            Dictionary<string, object> off = Call(FadePrefix, ("__instance", item), ("delay", 0f));
            Eq(0f, (float)off["delay"], "off 时 delay=0 原样");
            Eq(0, UnityEngine.Random.Calls, "off 时不消耗原生随机");
            Dictionary<string, object> offExplicit = Call(FadePrefix, ("__instance", item), ("delay", 2f));
            Eq(2f, (float)offExplicit["delay"], "off 时显式 delay 原样");
            ModConfig.FastForestRecedeEnabled.Value = true;
            Dictionary<string, object> on = Call(FadePrefix, ("__instance", item), ("delay", 0f));
            Near(10f / 3f, (float)on["delay"], "removeDelay(10) * 1.0 / 3");
            Eq(1, UnityEngine.Random.Calls, "按原生公式消耗一次随机");
            Eq(10f, item.removeDelay, "不改 removeDelay 字段");
            Dictionary<string, object> onExplicit = Call(FadePrefix, ("__instance", item), ("delay", 1.5f));
            Near(0.5f, (float)onExplicit["delay"], "显式 delay 除以 3");
            Eq(1f, Time.timeScale, "不改全局时间倍率");
            ForestItem control = NewItem(world, forest);
            control.controlsForestSize = true;
            Dictionary<string, object> limited = Call(FadePrefix, ("__instance", control), ("delay", 0f));
            Eq(0f, (float)limited["delay"], "controlsForestSize 不加速");
            Eq(1, UnityEngine.Random.Calls, "排除路径不消耗原生随机");
            item.removedByForest = true;
            Dictionary<string, object> removed = Call(FadePrefix, ("__instance", item), ("delay", 0f));
            Eq(0f, (float)removed["delay"], "已在退缩链中的重复调用不加速");
        });

        Test("森林退缩：世界与场景身份不符时不缩放", () =>
        {
            World world = NewWorld();
            ModConfig.FastForestRecedeEnabled.Value = true;
            Forest forest = NewForest(world);
            ForestItem item = NewItem(world, forest);
            item.gameObject.scene.handle = OptionalQoLScope.CurrentSceneHandle + 1;
            Dictionary<string, object> otherScene = Call(FadePrefix, ("__instance", item), ("delay", 0f));
            Eq(0f, (float)otherScene["delay"], "item 场景不符不缩放");
            item.gameObject.scene.handle = OptionalQoLScope.CurrentSceneHandle;
            forest.gameObject.scene.handle = OptionalQoLScope.CurrentSceneHandle + 1;
            Dictionary<string, object> otherForestScene = Call(FadePrefix, ("__instance", item), ("delay", 0f));
            Eq(0f, (float)otherForestScene["delay"], "forest 场景不符不缩放");
            forest.gameObject.scene.handle = OptionalQoLScope.CurrentSceneHandle;
            OptionalQoLScope.CurrentSceneHandle += 7;
            Dictionary<string, object> oldWorld = Call(FadePrefix, ("__instance", item), ("delay", 0f));
            Eq(0f, (float)oldWorld["delay"], "旧 world 层不缩放");
            OptionalQoLScope.CurrentSceneHandle -= 7;
            ForestItem orphan = NewItem(world, null);
            Dictionary<string, object> noForest = Call(FadePrefix, ("__instance", orphan), ("delay", 0f));
            Eq(0f, (float)noForest["delay"], "_forest 为空不缩放");
            Managers.Inst = null;
            Dictionary<string, object> noWorld = Call(FadePrefix, ("__instance", item), ("delay", 0f));
            Eq(0f, (float)noWorld["delay"], "无当前 world 不缩放");
        });

        Test("异常路径仍还原临时标量且不覆盖外部写入", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(world, 0f);
            Grass probe = NewGrass(world, 4f);
            world.ThrowOnCanSpawn = true;
            bool threw = false;
            try { CanSpawn(world, probe); }
            catch (InvalidOperationException) { threw = true; }
            Check(threw, "原生异常照常抛出");
            Near(6f, world.RawThicketSpacing, "finalizer 还原临时标量");
            Eq(0, HeldCount, "异常后不留调用栈租约");
            world.ThrowOnCanSpawn = false;
            PatchWorld_OptionalVegetation.CanSpawnLease lease = Prefix(world);
            Near(3f, world.RawThicketSpacing, "调用窗口内为半间距");
            ModConfig.DenseThicketsEnabled.Value = false; // 开关在窗口内被关掉
            RunFinalizer(world, lease);
            Near(6f, world.RawThicketSpacing, "开关状态不影响还原");
            ModConfig.DenseThicketsEnabled.Value = true;
            PatchWorld_OptionalVegetation.CanSpawnLease lease2 = Prefix(world);
            Near(3f, world.RawThicketSpacing, "第二个窗口同样写入半间距");
            world.thicketSpacing = 4.5f; // 外部写入
            RunFinalizer(world, lease2);
            Near(4.5f, world.RawThicketSpacing, "外部写入保持原样");
        });

        Test("Postfix 与 Finalizer 都失败时转入待恢复，下次进入先交还再取新基线", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(world, 0f);
            Grass probe = NewGrass(world, 4f);
            PatchWorld_OptionalVegetation.CanSpawnLease lease = Prefix(world);
            Near(3f, world.RawThicketSpacing, "窗口内写入半间距");
            Eq(1, HeldCount, "调用栈持有租约");
            world.ThrowOnSpacingSet = true; // interop setter 失败
            Invoke(CanSpawnPostfix, ("__instance", world), ("grass", probe), ("__result", true), ("__state", lease));
            Eq(1, PendingCount, "postfix 失败后转入待恢复");
            Eq(0, HeldCount, "调用栈已退出");
            Near(3f, world.RawThicketSpacing, "临时值仍未交还");
            RunFinalizer(world, lease);
            Eq(1, PendingCount, "finalizer 再试仍失败，责任保留");
            world.ThrowOnSpacingSet = false;
            PatchWorld_OptionalVegetation.CanSpawnLease next = Prefix(world);
            Near(3f, world.RawThicketSpacing, "先交还 6 再按 6 取新基线写 3（不是 1.5）");
            Eq(0, PendingCount, "待恢复已清");
            RunFinalizer(world, next);
            Near(6f, world.RawThicketSpacing, "新租约正常交还");
            Eq(0, HeldCount, "无遗留租约");
        });

        Test("Tick 归还失败待恢复的临时 scalar（关闭与失权同样）", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(world, 0f);
            PatchWorld_OptionalVegetation.CanSpawnLease lease = Prefix(world);
            world.ThrowOnSpacingSet = true;
            RunFinalizer(world, lease);
            Eq(1, PendingCount, "失败待恢复已登记");
            ModConfig.DenseThicketsEnabled.Value = false; // 关闭开关
            NetworkBigBoss.HasWorldAuth = false;          // 并且失权
            world.ThrowOnSpacingSet = false;
            PatchWorld_OptionalVegetation.Tick();
            Eq(0, PendingCount, "Tick 兜底交还");
            Near(6f, world.RawThicketSpacing, "原生字段回到原值");
        });

        Test("重入继承外层租约：不重复改写、不提前交还", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(world, 0f);
            PatchWorld_OptionalVegetation.CanSpawnLease outer = Prefix(world);
            Near(3f, world.RawThicketSpacing, "外层写入半间距");
            PatchWorld_OptionalVegetation.CanSpawnLease inner = Prefix(world);
            Eq(0, (int)inner.Written, "重入不再持有 scalar 写入");
            Eq(0UL, inner.Token, "重入不占用 token");
            Eq(1, HeldCount, "仍只持有一个调用栈租约");
            Near(3f, world.RawThicketSpacing, "重入不叠加成 1/4");
            RunFinalizer(world, inner);
            Near(3f, world.RawThicketSpacing, "内层不提前交还");
            RunFinalizer(world, outer);
            Near(6f, world.RawThicketSpacing, "外层最终交还");
        });

        Test("旧租约 finalizer 不覆写更新的 lease", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(world, 0f);
            PatchWorld_OptionalVegetation.CanSpawnLease first = Prefix(world);
            RunFinalizer(world, first);
            Near(6f, world.RawThicketSpacing, "第一租约已交还");
            PatchWorld_OptionalVegetation.CanSpawnLease second = Prefix(world);
            Near(3f, world.RawThicketSpacing, "第二租约持有半间距");
            RunFinalizer(world, first); // 迟到的旧 finalizer
            Near(3f, world.RawThicketSpacing, "旧租约不得改写新窗口");
            RunFinalizer(world, second);
            Near(6f, world.RawThicketSpacing, "新租约正常交还");
        });

        Test("池化复用：原生解除作废登记，Add 事件重建生命周期", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(world, 0f);
            Grass extra = NewGrass(world, 3.5f);
            DenseSpawn(world, extra);
            Eq(1, RecordsCount, "登记额外实例");
            GameObject reused = extra._thicket; // 池化复用：同一 native 对象（指针与 ID 相同）
            RemoveThicket(extra);
            Check(extra._thicket == null, "原生已交还");
            Eq(0, RecordsCount, "解除后登记立即作废");
            extra._thicket = reused;
            world.AddThicket(extra);
            Invoke(AddThicketPostfix, ("__instance", world), ("grass", extra));
            Eq(1, RecordsCount, "Add 事件按本次几何重建登记");
            int callsBefore = extra.RemoveCalls;
            ModConfig.DenseThicketsEnabled.Value = false;
            Time.time = 1f;
            PatchWorld_OptionalVegetation.Tick();
            Time.time = 1.5f;
            PatchWorld_OptionalVegetation.Tick();
            Eq(callsBefore + 1, extra.RemoveCalls, "重建后的登记承担完整回收责任");
            Check(extra._thicket == null, "复用对象已回收");
            Eq(0, RecordsCount, "回收完成");
        });

        Test("池化复用：重建为自然灌木时不误收、不再触碰", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(world, 0f);
            Grass extra = NewGrass(world, 3.5f);
            DenseSpawn(world, extra);
            GameObject reused = extra._thicket;
            // 模拟一次未被钩子看到的解除：登记残留，但对象已被池拿去复用
            extra._thicket = null;
            world.RemoveThicket(extra);
            extra._thicket = reused;
            RemoveThicket(anchor); // 原生锚点消失，重建的灌木落在原生间距之外
            world.AddThicket(extra);
            Invoke(AddThicketPostfix, ("__instance", world), ("grass", extra));
            Eq(0, RecordsCount, "重建判定为自然灌木，残留登记被作废");
            SpriteRenderer root = reused.GetComponent<SpriteRenderer>();
            Color baseline = root.color;
            ModConfig.DenseThicketsEnabled.Value = false;
            for (int i = 0; i < 20; i++)
            {
                Time.time += 0.2f;
                PatchWorld_OptionalVegetation.Tick();
            }
            Eq(0, extra.RemoveCalls, "自然灌木不再被回收调用");
            Check(extra._thicket == reused, "复用对象原样保留");
            Check(root.color.a == baseline.a, "颜色未被改动");
        });

        Test("Stage 低于 7 的原生清除会同步更新登记", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(world, 0f);
            Grass extra = NewGrass(world, 3.5f);
            extra.ThicketHasFx = true;
            DenseSpawn(world, extra);
            Eq(1, RecordsCount, "登记额外实例");
            SpriteRendererFX fx = extra._thicket.GetComponent<SpriteRendererFX>();
            extra.Stage = 0;      // 原生 UpdateHeight 降到 7 以下
            RemoveThicket(extra); // Stage setter / OnDisable 走同一条原生 RemoveThicket
            Eq(0, RecordsCount, "原生清除同步作废登记");
            Check(fx.FadeCalls == 0, "不再对本 mod 已释放的对象起淡");
            ModConfig.DenseThicketsEnabled.Value = false;
            for (int i = 0; i < 10; i++)
            {
                Time.time += 0.2f;
                PatchWorld_OptionalVegetation.Tick();
            }
            Eq(1, extra.RemoveCalls, "登记已作废，不再有回收调用");
            Eq(0, RecordsCount, "无残留登记");
        });

        Test("回收动作有界：多个额外实例分批处理", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            Grass anchor = NaturalGrass(world, 0f);
            var extras = new List<Grass>();
            for (int i = 0; i < 24; i++)
            {
                float naturalX = 6f * (i + 1);
                NaturalGrass(world, naturalX);
                Grass extra = NewGrass(world, naturalX - 3f);
                extra.ThicketHasFx = true;
                DenseSpawn(world, extra);
                extras.Add(extra);
            }
            Eq(24, RecordsCount, "登记 24 个额外实例");
            ModConfig.DenseThicketsEnabled.Value = false;
            Time.time = 1f;
            PatchWorld_OptionalVegetation.Tick();
            int started = extras.Sum(grass => grass._thicket.GetComponent<SpriteRendererFX>().FadeCalls);
            Check(started > 0, "至少启动一个淡出");
            Check(started < extras.Count, "单帧不同时启动全部回收，实际 " + started);
            for (int i = 0; i < 200 && RecordsCount > 0; i++)
            {
                Time.time += 0.1f;
                PatchWorld_OptionalVegetation.Tick();
            }
            Eq(0, RecordsCount, "最终全部回收");
            Check(extras.All(grass => grass.RemoveCalls == 1), "每个额外实例都经原生 RemoveThicket 回收");
            Eq(0, anchor.RemoveCalls, "原生锚点未被移除");
            Check(extras.All(grass => grass._thicket == null), "额外实例全部交还");
        });

        Test("hook 面仅限审计过的目标方法", () =>
        {
            var expected = new HashSet<string>
            {
                "World.CanSpawnThicket", "World.AddThicket", "Grass.RemoveThicket", "ForestItem.FadeAndRemove"
            };
            List<string> actual = Production.Assembly.GetTypes()
                .SelectMany(type => type.GetCustomAttributes<HarmonyLib.HarmonyPatch>())
                .Where(attribute => attribute.TargetType != null)
                .Select(attribute => attribute.TargetType.Name + "." + attribute.MethodName)
                .ToList();
            Check(new HashSet<string>(actual).SetEquals(expected),
                "hook 面应为 " + string.Join(",", expected) + "，实际 " + string.Join(",", actual));
            Check(!actual.Any(name => name.Contains("MoveNext")), "不得 hook 协程 MoveNext");
            Check(actual.Where(name => name.StartsWith("Grass.")).All(name => name == "Grass.RemoveThicket"),
                "Grass 只能 hook RemoveThicket");
            Check(!actual.Any(name => name.Contains("FadeAndDestroy") || name.EndsWith(".SpawnThicket")),
                "不得 hook 协程 factory 或 SpawnThicket");
        });

        Test("无世界权威时不写 world", () =>
        {
            World world = NewWorld();
            ModConfig.DenseThicketsEnabled.Value = true;
            NetworkBigBoss.HasWorldAuth = false;
            Grass anchor = NewGrass(world, 0f);
            anchor.SpawnThicket();
            Check(anchor._thicket == null, "无权威时原生不生成灌木");
            Grass probe = NewGrass(world, 4f);
            Check(!CanSpawn(world, probe), "无权威时不放行");
            Near(6f, world.RawThicketSpacing, "不改写 thicketSpacing");
            Invoke(AddThicketPostfix, ("__instance", world), ("grass", probe));
            Eq(0, RecordsCount, "client 不登记归属");
            PatchWorld_OptionalVegetation.Tick();
            Eq(0, RecordsCount, "Tick 在 client 无副作用");
        });

        Console.WriteLine("RESULT passed=" + Passed + " failed=" + Failed);
        return Failed == 0 ? 0 : 1;
    }
}
