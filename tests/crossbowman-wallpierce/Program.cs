// 弩手守家高抛修复（方案E）边界替身回归：直链生产文件
// il2cpp/PatchRoles_Crossbowman.cs（EnsureAssets 资产构建 + BoltOriginOffset + 克隆 SO 指针门来源）
// + il2cpp/CrossbowmanBoltWallPierce.cs（穿墙注入组件；低弹道 prefix 已按 2026-09-24 用户裁定移除）
// + il2cpp/HeroArcherWallPierce.cs（被复用的逐箭 owned-pair 账本 Apply/Restore）
// + il2cpp/HeroArcherArrowVisuals.cs（账本容量常量与 ResetArrow 前缀归还入口的生产来源）。
//
// 模拟的原生顺序（2.1/2.4 反编译一致）：
//   EnsureAssets: Instantiate(bolt)→数值→ApplyBoltSprite(降级/换皮)→挂穿墙件→Instantiate(SO)→池注册
//   池 Spawn: SetActive(true) →（组件序）穿墙件 OnEnable → HeroArcherWallPierce.Apply
//   池回收: SetActive(false) → 引擎清除该 GO 碰撞体的 ignore 状态 + 穿墙件 OnDisable →
//            HeroArcherWallPierce.Restore（IsEngineCleared 短路：退休账本，零写入）
//   池复用: [Arrow.OnEnable 前缀] HeroArcherArrowVisuals.ResetArrow（旧账本按箭身份兜底归还）
//            → 组件 OnEnable → Apply（新接管）
//   射击: ArrowAttack.BestShotInternal(私有) → 完全原生（低/高弹道选择回归原生）
//
// 夹具两族（Issue #10 对齐 #7 owned-pair 语义）：
//   SetActive 族      = 引擎清除拓扑：GO 停用即移除该 GO 碰撞体的 PairState 条目（镜像 Unity
//                       「停用即丢 ignore 状态」），生产走 IsEngineCleared 退休、**一个 false 都不写**；
//   直派族（DispatchDisableOnly/EnableOnly）= GO 保持活动、只派发消息：引擎未清除，生产必须
//                       逐对写真 false（归还责任的可观察证据）。
//
// 模式：
//   gold    = csproj 默认嵌入 il2cpp/Assets/ArtemisArrow.png（资源契约与英雄套件一致）
//   missing = csproj 不嵌入资源 → 资源缺失必须 exit code 2 硬失败（INFRA），弩手行为不依赖英雄资源
//
// 运行：dotnet run -c Release --project tests/crossbowman-wallpierce/Tests.csproj
// 资源两模式：dotnet build -c Release -t:Rebuild -p:EmbedArtemis=false → dotnet run -c Release --no-build -- --mode=missing

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

internal static class Program
{
    private const string ArtemisResourceName = "KingdomEnhancedMod.ArtemisArrow.png";

    private static int _passes;
    private static int _failures;
    private static int _assertions;
    private static int _nextId = 100;
    private static string _mode = "gold";

    private static readonly Type Host = typeof(PatchRoles_Crossbowman);
    private static readonly BindingFlags StaticAll = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static int Main(string[] args)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith("--mode=", StringComparison.Ordinal)) _mode = arg.Substring("--mode=".Length);
        }
        Console.WriteLine("crossbowman bolt wall-pierce stub tests (owned-pair ledger + engine-clear fixture), mode=" + _mode);
        bool embedded = FindArtemisResource() != null;
        Console.WriteLine("artemis resource embedded: " + embedded);
        Console.WriteLine();

        if (_mode == "missing" && embedded)
        {
            // 构建/增量问题（换属性但 MSBuild 复用旧输出）会让 missing 模式静默跑在 gold 程序集上。
            Console.WriteLine("INFRA: --mode=missing was requested but this build still embeds " + ArtemisResourceName + ".");
            Console.WriteLine("INFRA: rebuild without the resource first, e.g.:");
            Console.WriteLine("INFRA:   dotnet build -c Release -t:Rebuild -p:EmbedArtemis=false");
            Console.WriteLine("INFRA:   dotnet run -c Release --no-build -- --mode=missing");
            return 2;
        }
        if (_mode == "gold" && !embedded)
        {
            // 本套件编入真实 HeroArcherArrowVisuals.cs：gold 模式缺资源同样归因到构建，不误报成行为失败。
            Console.WriteLine("INFRA: --mode=gold requires the embedded " + ArtemisResourceName + " (the hero-visuals file is compiled into this suite).");
            Console.WriteLine("INFRA: rebuild with the resource, e.g.:");
            Console.WriteLine("INFRA:   dotnet build -c Release -t:Rebuild");
            Console.WriteLine("INFRA:   dotnet run -c Release --no-build");
            return 2;
        }
        if (_mode != "gold" && _mode != "missing")
        {
            Console.WriteLine("unknown mode: " + _mode);
            return 2;
        }

        RunSuite();

        Console.WriteLine();
        Console.WriteLine((_failures == 0 ? "ALL PASS" : "FAILURES") + ": pass=" + _passes + " fail=" + _failures
            + "; " + _assertions + " assertions");
        return _failures == 0 ? 0 : 1;
    }

    private static Stream FindArtemisResource()
        => typeof(HeroArcherArrowVisuals).Assembly.GetManifestResourceStream(ArtemisResourceName);

    // ============================================================
    // 断言与夹具
    // ============================================================

    private static void Check(bool value, string label)
    {
        _assertions++;
        if (!value) throw new Exception(label);
    }

    private static void Eq(float expected, float actual, string label)
    {
        _assertions++;
        if (MathF.Abs(expected - actual) > 1e-4f)
            throw new Exception($"{label}: expected {expected}, got {actual}");
    }

    private static void Eqv(Vector2 expected, Vector2 actual, string label)
    {
        Eq(expected.x, actual.x, label + ".x");
        Eq(expected.y, actual.y, label + ".y");
    }

    private static void Test(string name, Action body)
    {
        ResetWorld();
        Console.WriteLine("== " + name);
        try
        {
            body();
            _passes++;
            Console.WriteLine("  PASS");
        }
        catch (Exception e)
        {
            _failures++;
            Console.WriteLine("  FAIL  " + e.Message);
        }
    }

    private static void ResetWorld()
    {
        // 宿主静态状态归零（readonly 的 BoltOriginOffset 与 const 常量保留初值——正是被测常量）
        foreach (FieldInfo field in Host.GetFields(StaticAll))
        {
            if (field.IsInitOnly || field.IsLiteral) continue;
            field.SetValue(null, null);
        }
        // _nextSyncId 是带初始值的 short 静态字段：恢复生产初始 31000，保持池注册路径真实
        Host.GetField("_nextSyncId", StaticAll).SetValue(null, (short)31000);

        HeroArcherArrowVisuals.ResetForTests();
        HeroArcherWallPierce.ResetForTests();
        HeroArcherRuntime.Reset();
        ArcherOptionsScope.Reset();
        KingdomEnhancedPlugin.Instance = new PluginStub();
        UnityEngine.Object.ResetScene();
        Physics2D.Reset();
        Resources.Reset();
        Il2CppInterop.Runtime.Injection.ClassInjector.Registered.Clear();
        Time.unscaledTime = 1000f;
        UnityEngine.Random.value = 0.5f;
        Managers.Inst = null;
        CampaignSaveData.current = null;
        _nextId = 100;
    }

    private static GameObject NewGo(string name) => new GameObject { name = name, InstanceId = _nextId++ };

    private sealed class AssetHost
    {
        internal ArrowAttack BaseSo;
        internal Arrow BaseArrow;
        internal GameObject ArrowGo;
        internal GameObject PrefabGo;
        internal Sprite BaseSprite;
    }

    /// <summary>原生基线：Holder["Archer"] prefab（Character+Archer+Animator）+ _arrowAttack SO
    /// + 原生箭 prefab（Arrow+SpriteRenderer+Collider2D）。弩手模块从这里克隆。</summary>
    private static AssetHost BuildNativeAssets()
    {
        GameObject arrowGo = NewGo("ArrowPrefab");
        Arrow baseArrow = arrowGo.AddComponent<Arrow>();
        SpriteRenderer arrowRenderer = arrowGo.AddComponent<SpriteRenderer>();
        arrowRenderer.sprite = new Sprite { name = "arrowSprite" };
        Collider2D arrowCollider = arrowGo.AddComponent<Collider2D>();
        arrowCollider.Label = "native-arrow";
        baseArrow._collider = arrowCollider;
        baseArrow._spriteRenderer = arrowRenderer;

        ArrowAttack baseSo = new ArrowAttack("Arrows")
        {
            _shotMagnitude = 8f,
            _boostedShotMagnitude = 10f,
            _arrowPrefab = baseArrow,
        };

        GameObject prefabGo = NewGo("ArcherPrefab");
        prefabGo.AddComponent<Character>();
        Archer archer = prefabGo.AddComponent<Archer>();
        prefabGo.AddComponent<Animator>();
        archer._arrowAttack = baseSo;

        Managers.Inst = new Managers
        {
            holder = new Holder { tagCharacterPairs = { ["Archer"] = prefabGo.GetComponent<Character>() } },
            pools = new PoolManager { gameObject = NewGo("pools") },
        };
        return new AssetHost { BaseSo = baseSo, BaseArrow = baseArrow, ArrowGo = arrowGo, PrefabGo = prefabGo, BaseSprite = arrowRenderer.sprite };
    }

    /// <summary>登记一个可换皮的原生 Bolt（Resources.LoadAll&lt;Bolt&gt; 命中）。</summary>
    private static void RegisterBoltSprite(string name)
    {
        GameObject boltGo = NewGo(name);
        Bolt bolt = boltGo.AddComponent<Bolt>();
        SpriteRenderer renderer = boltGo.AddComponent<SpriteRenderer>();
        renderer.sprite = new Sprite { name = name + "-sprite" };
        Resources.LoadAllRegistry.Add(bolt);
    }

    private sealed class WallRig
    {
        internal GameObject Go;
        internal Wall Wall;
        internal Collider2D[] Colliders;
        internal GameObject[] ColliderGos;
    }

    private static WallRig NewWall(string label, int colliderCount)
    {
        GameObject wallGo = NewGo(label);
        Wall wall = wallGo.AddComponent<Wall>();
        Collider2D[] colliders = new Collider2D[colliderCount];
        GameObject[] colliderGos = new GameObject[colliderCount];
        for (int i = 0; i < colliderCount; i++)
        {
            GameObject colliderGo = NewGo(label + "#c" + i);
            colliderGos[i] = colliderGo;
            colliders[i] = colliderGo.AddComponent<Collider2D>();
            colliders[i].Label = label + "#" + i;
            wall.Colliders.Add(colliders[i]);
        }
        UnityEngine.Object.AllObjects.Add(wall);
        return new WallRig { Go = wallGo, Wall = wall, Colliders = colliders, ColliderGos = colliderGos };
    }

    /// <summary>独立弩矢（非 EnsureAssets 克隆）：池 prefab 惯例先停用再挂件，SetActive(true) 才是
    /// 组件 OnEnable → Apply 的激活点。用于容量/未接管一族需要大量互异身份箭头的场景。</summary>
    private static GameObject NewBoltRig(string label, out Collider2D collider)
    {
        GameObject go = NewGo(label);
        go.SetActive(false);                                  // 此刻无组件：无消息派发
        Arrow arrow = go.AddComponent<Arrow>();
        collider = go.AddComponent<Collider2D>();
        collider.Label = label + "#collider";
        arrow._collider = collider;
        go.AddComponent<CrossbowmanBoltWallPierce>();
        return go;
    }

    private static void RunEnsureAssets() => Host.GetMethod("EnsureAssets", StaticAll).Invoke(null, null);

    private static ArrowAttack HostSo() => (ArrowAttack)Host.GetField("_crossbowAttackSO", StaticAll).GetValue(null);

    private static GameObject HostBoltPrefab() => (GameObject)Host.GetField("_crossbowBoltPrefab", StaticAll).GetValue(null);

    private static int Pairs(Collider2D arrow, bool ignore) => Physics2D.CountArrowPair(arrow, ignore);

    /// <summary>原生 BestShotInternal 参照实现（ArrowAttack.cs:127-139 逐字语义）：
    /// wallBlocks=ParabolaCast(Obstacles) 的测试可控注入——生产 prefix 恰好要跳过它。</summary>
    private static Vector2 NativeBestShotInternal(ArrowAttack so, Vector2 targetPos, float gravity,
        bool forceHighShot, bool isBoostedShot, bool wallBlocks)
    {
        float num = isBoostedShot ? so._boostedShotMagnitude : so._shotMagnitude;
        bool solved = Util.ComputeTrajectoryAngle(targetPos, num, out Vector2 low, out Vector2 high, gravity);
        Vector2 chosen = high;
        if (solved && !forceHighShot && !wallBlocks) chosen = low;
        return Vector2.ClampMagnitude(chosen * num, num);
    }

    // ============================================================
    // 用例
    // ============================================================

    private static void RunSuite()
    {
        Test("origin offset (0.6,0.7) reaches the cloned SO; degraded (no bolt sprite) build still attaches the pierce component", () =>
        {
            AssetHost assets = BuildNativeAssets();
            RunEnsureAssets();
            ArrowAttack so = HostSo();
            GameObject prefab = HostBoltPrefab();
            Check(so != null && prefab != null, "assets built");

            Vector2 offset = (Vector2)Host.GetField("BoltOriginOffset", StaticAll).GetValue(null);
            Check(MathF.Abs(offset.x - 0.6f) <= 1e-6f && MathF.Abs(offset.y - 0.7f) <= 1e-6f,
                "BoltOriginOffset == (0.6,0.7) (旧 2.5 前移已废弃)");
            Eqv(offset, so._arrowOriginOffset, "cloned SO carries the origin offset");
            Check(so != assets.BaseSo && so.Pointer != assets.BaseSo.Pointer, "cloned SO is a distinct native object");
            Eq(so._shotMagnitude, assets.BaseSo._shotMagnitude * 2f, "shot magnitude x2");
            Eq(so._boostedShotMagnitude, assets.BaseSo._boostedShotMagnitude * 2f, "boosted magnitude x2");

            Check(so._arrowPrefab != null && so._arrowPrefab != assets.BaseArrow, "cloned bolt arrow");
            Eq(so._arrowPrefab.hitDamage, 2, "bolt damage 2");
            Check(so._arrowPrefab._alwaysDrawTrail, "always-draw trail on");
            Eq(so._arrowPrefab._notPerfectTrailLength, 0.25f, "trail length 0.25s");
            Eq(assets.BaseArrow.hitDamage, 1, "native arrow prefab untouched (no shared-instance leak)");
            Check(so._arrowPrefab._collider != null && so._arrowPrefab._collider.gameObject == prefab,
                "the cloned arrow collider is remapped onto the cloned GO (Unity Instantiate semantics)");
            Check(!prefab.activeSelf, "bolt prefab inactive (pool convention)");

            Check(prefab.GetComponent<CrossbowmanBoltWallPierce>() != null,
                "pierce component attached in degraded no-sprite mode");
            Check(prefab.GetComponent<CrossbowBoltScaleLifecycle>() == null,
                "degraded mode has no reskin lifecycle (pierce must not depend on the reskin branch)");
            Check(Il2CppInterop.Runtime.Injection.ClassInjector.Registered.Contains(typeof(CrossbowmanBoltWallPierce)),
                "pierce type registered in il2cpp");
            Check(KingdomEnhancedPlugin.Instance.LogSource.Contains("no native Bolt sprite found"),
                "degraded mode logged once");

            IntPtr gate = PatchRoles_Crossbowman.ClonedAttackSoPointer;
            Check(gate != IntPtr.Zero && gate == so.Pointer, "ClonedAttackSoPointer == the cloned SO pointer");
            Check(assets.BaseSo.Pointer != gate, "native SO is outside the gate");
        });

        Test("reskin mode attaches both lifecycles exactly once; EnsureOn is idempotent", () =>
        {
            BuildNativeAssets();
            RegisterBoltSprite("BallistaBolt");
            RunEnsureAssets();
            ArrowAttack so = HostSo();
            GameObject prefab = HostBoltPrefab();
            Check(prefab.GetComponent<CrossbowBoltScaleLifecycle>() != null, "reskin lifecycle attached");
            Check(prefab.GetComponent<CrossbowmanBoltWallPierce>() != null, "pierce component attached alongside");
            Check(prefab.GetComponent<SpriteRenderer>().sprite != null
                && prefab.GetComponent<SpriteRenderer>().sprite.name == "BallistaBolt-sprite",
                "bolt skin swapped from the native Bolt sprite");

            int before = CountComponents<CrossbowmanBoltWallPierce>(prefab);
            CrossbowmanBoltWallPierce.EnsureOn(so._arrowPrefab);          // 幂等：重复挂件不得叠第二个
            Check(CountComponents<CrossbowmanBoltWallPierce>(prefab) == before && before == 1,
                "EnsureOn attaches exactly one component (idempotent)");
            CrossbowmanBoltWallPierce.EnsureOn(null);                      // null 安全
            Check(true, "EnsureOn(null) is a safe no-op");
        });

        Test("pool spawn applies wall pierce; a deactivating recycle retires via the engine (zero writes); the keep-alive recycle writes false", () =>
        {
            BuildNativeAssets();
            RunEnsureAssets();
            ArrowAttack so = HostSo();
            GameObject prefab = HostBoltPrefab();
            Collider2D boltCollider = so._arrowPrefab._collider;
            WallRig w1 = NewWall("w1", 2);
            WallRig w2 = NewWall("w2", 1);

            prefab.SetActive(true);                                       // 池 Spawn 激活
            Check(Pairs(boltCollider, true) == 3, "apply ignored all 3 active wall colliders");
            Check(Pairs(boltCollider, false) == 0, "no restore before recycle");
            Check(Physics2D.CountWallPair(w1.Colliders[0], true) == 1
                && Physics2D.CountWallPair(w1.Colliders[1], true) == 1
                && Physics2D.CountWallPair(w2.Colliders[0], true) == 1, "true pairs are exactly the wall colliders");
            Check(KingdomEnhancedPlugin.Instance.LogSource.Contains("[HeroArcherWallPierce]"),
                "apply logs ride the HeroArcherWallPierce prefix (known cosmetic mislabel)");
            Check(HeroArcherWallPierce.LedgerCount == 1, "one owned ledger in the table");

            prefab.SetActive(false);                                      // 池回收（引擎清除拓扑）
            Check(Pairs(boltCollider, false) == 0,
                "deactivating recycle: the engine cleared the pairs, the ledger retired with zero writes");
            Check(HeroArcherWallPierce.LedgerCount == 0, "no ledger survives the engine-cleared recycle");
            Check(!Physics2D.GetIgnoreCollision(boltCollider, w1.Colliders[0])
                && !Physics2D.GetIgnoreCollision(boltCollider, w2.Colliders[0]),
                "stub engine semantics: ignore state is gone once the collider's GO deactivates");
            Check(Pairs(boltCollider, true) == 3, "the recycle itself added no writes");

            prefab.SetActive(true);                                       // 第二个 life
            Check(Pairs(boltCollider, true) == 6, "second life re-claimed the pairs from the engine-cleared state");
            Check(Physics2D.Calls.Count == 6, "exactly the two apply passes were recorded (true calls only)");
            for (int i = 0; i < 3; i++) Check(Physics2D.Calls[i].Ignore, "call " + i + " = apply (true)");
            for (int i = 3; i < 6; i++) Check(Physics2D.Calls[i].Ignore, "call " + i + " = re-apply (true)");
            Check(UnityEngine.Object.DestroyCalls.Count == 0, "nothing destroyed");

            prefab.DispatchDisableOnly();                                 // 直派族：引擎未清除 → 真归还写
            Check(Pairs(boltCollider, false) == 3, "keep-alive recycle: the ledger hands all three pairs back");
            Check(Physics2D.Calls.Count == 9, "exactly three restore calls recorded on top of the two apply passes");
            for (int i = 6; i < 9; i++) Check(!Physics2D.Calls[i].Ignore, "call " + i + " = restore (false)");
            Check(HeroArcherWallPierce.LedgerCount == 0, "the settled ledger left the table");
        });

        Test("fail-closed: no world context / wall query failure / missing collider / component lookup throw", () =>
        {
            BuildNativeAssets();
            RunEnsureAssets();
            ArrowAttack so = HostSo();
            GameObject prefab = HostBoltPrefab();
            Collider2D boltCollider = so._arrowPrefab._collider;
            NewWall("w", 2);

            prefab.SetActive(true);                                       // life 1: warm snapshot
            Check(Pairs(boltCollider, true) == 2, "warm snapshot applied");
            Check(HeroArcherWallPierce.LedgerCount == 1, "life 1 holds one owned ledger");
            prefab.SetActive(false);                                      // 引擎清除：退休 0 写
            Check(Pairs(boltCollider, false) == 0 && HeroArcherWallPierce.LedgerCount == 0,
                "deactivating recycle retired the ledger with zero writes (engine cleared)");

            ArcherOptionsScope.ContextAvailable = false;                  // 无世界上下文 = 不穿墙
            prefab.SetActive(true);                                       // life 2: no writes
            Check(Pairs(boltCollider, true) == 2, "no context: no new pierce (fail-closed)");
            Check(HeroArcherWallPierce.LedgerCount == 0, "no ledger rented without a world context");
            ArcherOptionsScope.ContextAvailable = true;

            Time.unscaledTime += 30f;                                     // TTL 过期 + 查询失败
            UnityEngine.Object.FindObjectsOfTypeThrows = true;
            prefab.SetActive(false);                                      // 0 写
            prefab.SetActive(true);                                       // life 3: scan fail, no writes
            Check(Pairs(boltCollider, true) == 2, "wall query failure: pierce skipped this cycle");
            Check(KingdomEnhancedPlugin.Instance.LogSource.Contains("scan failed"), "failure logged once");

            UnityEngine.Object.FindObjectsOfTypeThrows = false;
            Time.unscaledTime += 30f;
            prefab.SetActive(false);                                      // 0 写
            prefab.SetActive(true);                                       // life 4: recovered
            Check(Pairs(boltCollider, true) == 4, "recovered on the next attempt");
            Check(Pairs(boltCollider, false) == 0,
                "every recycle so far retired with zero writes (engine-cleared; restore never rescans)");
            Check(HeroArcherWallPierce.LedgerCount == 1, "life 4 holds one ledger again");

            so._arrowPrefab.gameObject.ComponentLookupThrows = true;      // 组件查询异常：绝不外抛
            prefab.SetActive(false);                                      // OnDisable 内部捕获，无归还
            prefab.SetActive(true);                                       // OnEnable 内部捕获，无穿墙
            Check(Pairs(boltCollider, true) == 4 && Pairs(boltCollider, false) == 0,
                "lookup throw: neither apply nor restore wrote anything");
            Check(KingdomEnhancedPlugin.Instance.LogSource.Contains("[CrossbowmanBoltPierce]"),
                "lifecycle failure logged via the pierce module's own prefix");
            so._arrowPrefab.gameObject.ComponentLookupThrows = false;

            prefab.DispatchDisableOnly();                                 // 结清 life 4 的责任（直派族）
            Check(Pairs(boltCollider, false) == 2 && HeroArcherWallPierce.LedgerCount == 0,
                "the blocked life-4 ledger settles through the direct hand-back");

            GameObject colliderlessGo = NewGo("colliderless");
            colliderlessGo.AddComponent<Arrow>();                         // 无碰撞体：fail-closed 不抛
            colliderlessGo.AddComponent<CrossbowmanBoltWallPierce>();
            colliderlessGo.SetActive(false);
            colliderlessGo.SetActive(true);
            Check(HeroArcherWallPierce.LedgerCount == 0 && Pairs(boltCollider, true) == 4,
                "missing collider arrow never threw, wrote nothing, and rented no ledger");
        });

        Test("full pool chain in production order: ResetArrow hands the old ledger back, the next component OnEnable re-claims, no double accounting", () =>
        {
            BuildNativeAssets();
            RunEnsureAssets();
            Arrow bolt = HostSo()._arrowPrefab;
            Collider2D boltCollider = bolt._collider;
            NewWall("w", 2);
            GameObject prefab = HostBoltPrefab();

            prefab.SetActive(true);                                       // 首次发射（池 Spawn）
            Check(Pairs(boltCollider, true) == 2 && HeroArcherWallPierce.LedgerCount == 1, "first life claimed both pairs");

            prefab.DispatchDisableOnly();                                 // 回收（引擎未清除：真归还写）
            Check(Pairs(boltCollider, false) == 2 && HeroArcherWallPierce.LedgerCount == 0,
                "the old ledger handed both pairs back");

            int calls = Physics2D.Calls.Count;
            HeroArcherArrowVisuals.ResetArrow(bolt);                      // [Arrow.OnEnable 前缀] 幂等 no-op
            Check(Physics2D.Calls.Count == calls, "ResetArrow on a settled ledger writes nothing");

            prefab.DispatchEnableOnly();                                  // 组件 OnEnable → Apply（新接管）
            Check(Pairs(boltCollider, true) == 4, "the next life claims both pairs again");
            Check(HeroArcherWallPierce.LedgerCount == 1, "exactly one ledger after the re-take");

            calls = Physics2D.Calls.Count;
            HeroArcherArrowVisuals.ResetArrow(bolt);                      // 生产复用的前缀：不等 OnDisable 就归还
            Check(Pairs(boltCollider, false) == 4 && Physics2D.Calls.Count == calls + 2,
                "ResetArrow itself hands the live ledger back exactly once per pair");
            Check(HeroArcherWallPierce.LedgerCount == 0, "the handed-back ledger left the table");

            prefab.DispatchEnableOnly();                                  // 干净重接管
            prefab.DispatchDisableOnly();
            Check(Pairs(boltCollider, false) == 6,
                "the re-claimed ledger hands back exactly its own two pairs (no double accounting)");
            Check(HeroArcherWallPierce.LedgerCount == 0, "nothing left owing after the chain");
        });

        Test("lifecycles the module never took over write nothing (no walls / no world context)", () =>
        {
            BuildNativeAssets();
            RunEnsureAssets();
            GameObject prefab = HostBoltPrefab();
            Collider2D boltCollider = HostSo()._arrowPrefab._collider;

            prefab.SetActive(true);                                       // 无墙场景：扫描成功但零碰撞体
            Check(Pairs(boltCollider, true) == 0 && Pairs(boltCollider, false) == 0,
                "empty world: the scan found no walls, nothing was written");
            Check(HeroArcherWallPierce.CachedColliderCount == 0 && HeroArcherWallPierce.LedgerCount == 0,
                "no snapshot colliders and no ledger were created");
            prefab.SetActive(false);
            prefab.SetActive(true);
            Check(Pairs(boltCollider, true) == 0 && Pairs(boltCollider, false) == 0
                && HeroArcherWallPierce.LedgerCount == 0,
                "recirculating an unclaimed lifecycle stays write-free");

            NewWall("w", 2);
            ArcherOptionsScope.ContextAvailable = false;                  // 世界上下文不可用
            Time.unscaledTime += 30f;                                     // 快照必然过期，仍不得写、不得重扫
            prefab.SetActive(false);
            prefab.SetActive(true);
            Check(Pairs(boltCollider, true) == 0 && Pairs(boltCollider, false) == 0,
                "no world context: the whole lifecycle wrote nothing even with walls present");
            Check(HeroArcherWallPierce.LedgerCount == 0, "still no ledger");
            Check(HeroArcherWallPierce.CachedColliderCount == 0,
                "the context check precedes discovery: no wall snapshot was ever built for the blocked life");
            ArcherOptionsScope.ContextAvailable = true;
        });

        Test("foreign ignore pairs are never claimed and never written back to false", () =>
        {
            BuildNativeAssets();
            RunEnsureAssets();
            GameObject prefab = HostBoltPrefab();
            Collider2D boltCollider = HostSo()._arrowPrefab._collider;
            WallRig w = NewWall("w", 2);
            Collider2D foreignWall = w.Colliders[1];
            // 模拟其他逻辑写过的 ignore=true（不经本模块记账/不经调用账本）
            Physics2D.PairState[(boltCollider, foreignWall)] = true;
            Physics2D.PairState[(foreignWall, boltCollider)] = true;

            prefab.SetActive(true);
            Check(Pairs(boltCollider, true) == 1, "only the pair that read false got claimed");
            Check(Physics2D.GetIgnoreCollision(boltCollider, foreignWall), "the foreign true pair still ignores");
            Check(HeroArcherWallPierce.LedgerCount == 1, "the ledger holds exactly one owned pair");

            prefab.DispatchDisableOnly();
            Check(Pairs(boltCollider, false) == 1, "the hand-back wrote false only for the owned pair");
            Check(Physics2D.GetIgnoreCollision(boltCollider, foreignWall), "the foreign true pair survives the hand-back");
            Check(HeroArcherWallPierce.LedgerCount == 0, "the one-pair ledger retired");

            prefab.SetActive(false);                                      // 引擎清除（含外来状态）
            prefab.SetActive(true);
            Check(Pairs(boltCollider, true) == 3, "the next life claims both pairs from the engine-cleared state");
        });

        Test("a wall collider deactivated while owned is settled by the engine (no false write)", () =>
        {
            BuildNativeAssets();
            RunEnsureAssets();
            GameObject prefab = HostBoltPrefab();
            Collider2D boltCollider = HostSo()._arrowPrefab._collider;
            WallRig w1 = NewWall("w1", 1);
            WallRig w2 = NewWall("w2", 1);

            prefab.SetActive(true);
            Check(Pairs(boltCollider, true) == 2, "both wall pairs were claimed");

            w2.ColliderGos[0].SetActive(false);                           // 墙碰撞体停用：引擎清除该对
            Check(!Physics2D.GetIgnoreCollision(boltCollider, w2.Colliders[0]),
                "the deactivated wall's pair state is gone (engine semantics)");

            prefab.DispatchDisableOnly();                                 // 归还：活墙写 false，停用墙不写
            Check(Physics2D.CountWallPair(w1.Colliders[0], false) == 1, "the live wall was handed back (false written)");
            Check(Physics2D.CountWallPair(w2.Colliders[0], false) == 0, "no false written for the engine-cleared wall");
            Check(HeroArcherWallPierce.LedgerCount == 0, "the ledger retired with both entries settled");
        });

        Test("an unreadable ignore state is probe-only (never written) and claimable after recovery", () =>
        {
            BuildNativeAssets();
            RunEnsureAssets();
            GameObject prefab = HostBoltPrefab();
            Arrow bolt = HostSo()._arrowPrefab;
            Collider2D boltCollider = bolt._collider;
            WallRig w = NewWall("w", 2);
            Collider2D unreadable = w.Colliders[1];
            Physics2D.OnGetIgnore = (Collider2D collider1, Collider2D collider2) =>
            {
                if (ReferenceEquals(collider2, unreadable)) throw new InvalidOperationException("stub: get ignore threw");
            };

            prefab.SetActive(true);
            Check(Pairs(boltCollider, true) == 1, "the readable wall was claimed, the unreadable one only probed");
            Check(Physics2D.CountWallPair(unreadable, true) == 0, "no true write for the unreadable pair");
            Check(HeroArcherWallPierce.LedgerCount == 1, "the ledger holds the probe entry");

            Physics2D.OnGetIgnore = null;
            int calls = Physics2D.Calls.Count;
            int scans = UnityEngine.Object.FindObjectsOfTypeCalls;
            HeroArcherWallPierce.PierceLedger ledger = HeroArcherWallPierce.Apply(bolt);
            Check(ledger != null && Physics2D.Calls.Count == calls,
                "re-applying finds the live ledger and writes nothing new (owned pairs are not rewritten)");
            HeroArcherWallPierce.RetryPending(ledger);
            Check(Physics2D.CountWallPair(unreadable, true) == 1, "after recovery the retry reads false and claims the pair");
            Check(UnityEngine.Object.FindObjectsOfTypeCalls == scans, "the retry never rescans the world");
            Check(Pairs(boltCollider, true) == 2, "both pairs are now owned");

            prefab.DispatchDisableOnly();
            Check(Pairs(boltCollider, false) == 2, "the settled ledger hands both pairs back");
            Check(HeroArcherWallPierce.LedgerCount == 0, "ledger retired after the hand-back");
        });

        Test("ledger table full: the new Apply is rejected fail-closed; existing ledgers still hand back", () =>
        {
            BuildNativeAssets();
            RunEnsureAssets();
            WallRig w = NewWall("w", 1);
            Collider2D wall = w.Colliders[0];
            int cap = HeroArcherWallPierce.MaxLedgerArrows;
            Check(cap == HeroArcherArrowVisuals.Capacity && cap == 128,
                "the ledger cap is the visuals receipt capacity (128)");

            GameObject[] rigs = new GameObject[cap];
            Collider2D[] colliders = new Collider2D[cap];
            for (int i = 0; i < cap; i++)
            {
                rigs[i] = NewBoltRig("ledger" + i, out colliders[i]);
                rigs[i].SetActive(true);
            }
            Check(HeroArcherWallPierce.LedgerCount == cap, "every bolt holds a ledger");
            Check(Physics2D.CountWallPair(wall, true) == cap, "each of them claimed the wall");

            GameObject overflow = NewBoltRig("overflow", out Collider2D overflowCollider);
            overflow.SetActive(true);
            Check(HeroArcherWallPierce.LedgerCount == cap, "the full table was not evicted");
            Check(Pairs(overflowCollider, true) == 0, "the rejected Apply wrote not a single pair");

            rigs[0].DispatchDisableOnly();
            Check(Pairs(colliders[0], false) == 1, "an existing ledger still hands its pair back");
            Check(HeroArcherWallPierce.LedgerCount == cap - 1, "the settled ledger freed its slot");

            overflow.DispatchEnableOnly();
            Check(Pairs(overflowCollider, true) == 1, "the freed slot lets the next Apply claim");
            Check(HeroArcherWallPierce.LedgerCount == cap, "table full again");
        });

        Test("double hand-back is idempotent: the component OnDisable restore then ResetArrow writes nothing more", () =>
        {
            BuildNativeAssets();
            RunEnsureAssets();
            GameObject prefab = HostBoltPrefab();
            Arrow bolt = HostSo()._arrowPrefab;
            Collider2D boltCollider = bolt._collider;
            NewWall("w", 2);

            prefab.SetActive(true);
            prefab.DispatchDisableOnly();
            Check(Pairs(boltCollider, false) == 2, "the component OnDisable handed both pairs back");

            int calls = Physics2D.Calls.Count;
            HeroArcherArrowVisuals.ResetArrow(bolt);                      // 生产时序里紧随其后的前缀
            Check(Physics2D.Calls.Count == calls, "the second hand-back writes nothing (idempotent)");
            Check(HeroArcherWallPierce.LedgerCount == 0, "no ledger was re-rented or rewritten");
        });

        Test("Forced low trajectory is retired (2026-09-24 user ruling): native arcs run", () =>
        {
            // The BestShotInternal prefix and its direct-link entry are gone from production;
            // compile-time absence is the assertion — native arc selection is fully restored.
            // (Wall pierce itself keeps its own suite below.)
            Check(true, "native BestShotInternal runs for every crossbow shot again");
        });

        Test("deadlands follower package shares the cloned SO (pierce scope unchanged)", () =>
        {
            AssetHost assets = BuildNativeAssets();
            RunEnsureAssets();
            ArrowAttack so = HostSo();
            GameObject followerGo = NewGo("follower");
            Archer follower = followerGo.AddComponent<Archer>();
            follower._arrowAttack = assets.BaseSo;
            follower.ActiveArrowAttack = assets.BaseSo;

            PatchRoles_Crossbowman.ApplySquadCrossbowPackage(follower);
            Check(follower.ActiveArrowAttack == so, "follower package assigns the same cloned SO (pierce pool shared, by design)");
        });

        Test("tower ballista boundary: native Bolt never gets the component and its SO never hits the gate", () =>
        {
            AssetHost assets = BuildNativeAssets();
            RunEnsureAssets();
            GameObject towerBoltGo = NewGo("TowerBolt");
            towerBoltGo.AddComponent<Bolt>();
            Check(towerBoltGo.GetComponent<CrossbowmanBoltWallPierce>() == null,
                "tower Bolt has no pierce component (EnsureOn is only called on the KEM clone)");
            Check(towerBoltGo.GetComponent<Arrow>() == null,
                "tower Bolt is a different class entirely (no Arrow component)");
        });

        Test("hook contract: 8 crossbowman hooks + 2 hero-visuals hooks; no other native surface touched", () =>
        {
            List<string> targets = new List<string>();
            Type[] types = typeof(PatchRoles_Crossbowman).Assembly.GetTypes();
            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                object[] patches = type.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false);
                if (patches.Length == 0) continue;
                for (int p = 0; p < patches.Length; p++)
                {
                    HarmonyLib.HarmonyPatch patch = (HarmonyLib.HarmonyPatch)patches[p];
                    targets.Add(patch.DeclaringType.Name + "." + patch.MethodName);
                }
            }
            targets.Sort(StringComparer.Ordinal);
            List<string> expected = new List<string>
            {
                "Archer.ConvertToHunter", "Archer.IsAvailableForJob", "Archer.OnDisable", "Archer.OnEnable",
                "Arrow.OnEnable", "ArrowAttack.FireArrowInternal",
                "Character.Promote", "Pool.FastSpawn", "PoolManager.Init", "World.OnLevelLoaded",
            };
            Check(targets.Count == expected.Count, "patch count = 8 crossbowman hooks + 2 hero-visuals hooks (BestShotInternal retired 2026-09-24; got "
                + targets.Count + ": " + string.Join(",", targets) + ")");
            for (int i = 0; i < expected.Count && i < targets.Count; i++)
                Check(targets[i] == expected[i], "patch target[" + i + "] == " + expected[i]);

            Check(HasNoHarmonyAttributes(typeof(CrossbowmanBoltWallPierce)),
                "the pierce component declares no Harmony attributes (rides the existing component lifecycle)");
            Check(HasNoHarmonyAttributes(typeof(HeroArcherWallPierce)),
                "HeroArcherWallPierce still declares no Harmony attributes");

            // 注入组件契约（照抄 CrossbowBoltScaleLifecycle）：MonoBehaviour + (IntPtr) 构造器 +
            // 无参私有 OnEnable/OnDisable 消息方法
            Check(typeof(CrossbowmanBoltWallPierce).BaseType == typeof(MonoBehaviour),
                "pierce component derives MonoBehaviour (injection pattern)");
            Check(typeof(CrossbowmanBoltWallPierce).GetConstructor(new[] { typeof(IntPtr) }) != null,
                "pierce component has the (IntPtr) injection constructor");
            Check(typeof(CrossbowmanBoltWallPierce).GetMethod("OnEnable",
                BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null) != null
                && typeof(CrossbowmanBoltWallPierce).GetMethod("OnDisable",
                BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null) != null,
                "OnEnable/OnDisable are parameterless message methods");
        });
    }

    private static int CountComponents<T>(GameObject go) where T : Component
    {
        int n = 0;
        for (int i = 0; i < go.Components.Count; i++) if (go.Components[i] is T) n++;
        return n;
    }

    private static bool HasNoHarmonyAttributes(Type type)
    {
        if (type.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false).Length > 0) return false;
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (method.GetCustomAttributes(typeof(HarmonyLib.HarmonyPrefix), false).Length > 0) return false;
            if (method.GetCustomAttributes(typeof(HarmonyLib.HarmonyPostfix), false).Length > 0) return false;
            if (method.GetCustomAttributes(typeof(HarmonyLib.HarmonyFinalizer), false).Length > 0) return false;
        }
        return true;
    }
}
