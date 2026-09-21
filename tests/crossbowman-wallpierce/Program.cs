// 弩手守家高抛修复（方案E）边界替身回归：直链生产文件
// il2cpp/PatchRoles_Crossbowman.cs（EnsureAssets 资产构建 + BoltOriginOffset + 克隆 SO 指针门来源）
// + il2cpp/CrossbowmanBoltWallPierce.cs（穿墙注入组件 + BestShotInternal 低弹道 prefix 主体）
// + il2cpp/HeroArcherWallPierce.cs（被复用的 Apply/Restore）。
//
// 模拟的原生顺序（2.1/2.4 反编译一致）：
//   EnsureAssets: Instantiate(bolt)→数值→ApplyBoltSprite(降级/换皮)→挂穿墙件→Instantiate(SO)→池注册
//   池 Spawn: SetActive(true) →（组件序）穿墙件 OnEnable → HeroArcherWallPierce.Apply
//   池回收: SetActive(false) → 穿墙件 OnDisable → HeroArcherWallPierce.Restore（先于下一次复用）
//   射击: ArrowAttack.BestShotInternal(私有) → [Prefix] SO 指针门 → 低解强制 / 原样执行
//
// 运行：dotnet run -c Release --project tests/crossbowman-wallpierce/Tests.csproj

using System;
using System.Collections.Generic;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

internal static class Program
{
    private static int _passes;
    private static int _failures;
    private static int _assertions;
    private static int _nextId = 100;

    private static readonly Type Host = typeof(PatchRoles_Crossbowman);
    private static readonly BindingFlags StaticAll = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

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

        HeroArcherWallPierce.ResetForTests();
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
        internal Wall Wall;
        internal Collider2D[] Colliders;
    }

    private static WallRig NewWall(string label, int colliderCount)
    {
        GameObject wallGo = NewGo(label);
        Wall wall = wallGo.AddComponent<Wall>();
        Collider2D[] colliders = new Collider2D[colliderCount];
        for (int i = 0; i < colliderCount; i++)
        {
            GameObject colliderGo = NewGo(label + "#c" + i);
            colliders[i] = colliderGo.AddComponent<Collider2D>();
            colliders[i].Label = label + "#" + i;
            wall.Colliders.Add(colliders[i]);
        }
        UnityEngine.Object.AllObjects.Add(wall);
        return new WallRig { Wall = wall, Colliders = colliders };
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

    private static void Main()
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

        Test("pool spawn applies wall pierce, recycle restores it, second life re-applies (true/false/true order)", () =>
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

            prefab.SetActive(false);                                      // 池回收
            Check(Pairs(boltCollider, false) == 3, "restore on recycle hands every pair back");
            Check(Pairs(boltCollider, true) == 3, "apply count unchanged by restore");

            prefab.SetActive(true);                                       // 第二个 life
            Check(Pairs(boltCollider, true) == 6, "second life re-applied the pairs");
            Check(Physics2D.Calls.Count == 9, "exactly 3 apply/restore cycles recorded");
            for (int i = 0; i < 3; i++) Check(Physics2D.Calls[i].Ignore, "call " + i + " = apply (true)");
            for (int i = 3; i < 6; i++) Check(!Physics2D.Calls[i].Ignore, "call " + i + " = restore (false)");
            for (int i = 6; i < 9; i++) Check(Physics2D.Calls[i].Ignore, "call " + i + " = re-apply (true)");
            Check(UnityEngine.Object.DestroyCalls.Count == 0, "nothing destroyed");
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
            prefab.SetActive(false);                                      // false=2

            ArcherOptionsScope.ContextAvailable = false;                  // 无世界上下文 = 不穿墙
            prefab.SetActive(true);                                       // life 2: no writes
            Check(Pairs(boltCollider, true) == 2, "no context: no new pierce (fail-closed)");
            ArcherOptionsScope.ContextAvailable = true;

            Time.unscaledTime += 30f;                                     // TTL 过期 + 查询失败
            UnityEngine.Object.FindObjectsOfTypeThrows = true;
            prefab.SetActive(false);                                      // false=4
            prefab.SetActive(true);                                       // life 3: scan fail, no writes
            Check(Pairs(boltCollider, true) == 2, "wall query failure: pierce skipped this cycle");
            Check(KingdomEnhancedPlugin.Instance.LogSource.Contains("scan failed"), "failure logged once");

            UnityEngine.Object.FindObjectsOfTypeThrows = false;
            Time.unscaledTime += 30f;
            prefab.SetActive(false);                                      // false=6
            prefab.SetActive(true);                                       // life 4: recovered
            Check(Pairs(boltCollider, true) == 4, "recovered on the next attempt");
            Check(Pairs(boltCollider, false) == 6, "every recycle restored via the kept snapshot (restore never rescans)");

            so._arrowPrefab.gameObject.ComponentLookupThrows = true;      // 组件查询异常：绝不外抛
            prefab.SetActive(false);                                      // OnDisable 内部捕获，无归还
            prefab.SetActive(true);                                       // OnEnable 内部捕获，无穿墙
            Check(Pairs(boltCollider, true) == 4 && Pairs(boltCollider, false) == 6,
                "lookup throw: neither apply nor restore wrote anything");
            Check(KingdomEnhancedPlugin.Instance.LogSource.Contains("[CrossbowmanBoltPierce]"),
                "lifecycle failure logged via the pierce module's own prefix");
            so._arrowPrefab.gameObject.ComponentLookupThrows = false;

            GameObject colliderlessGo = NewGo("colliderless");
            colliderlessGo.AddComponent<Arrow>();                         // 无碰撞体：fail-closed 不抛
            colliderlessGo.AddComponent<CrossbowmanBoltWallPierce>();
            colliderlessGo.SetActive(false);
            colliderlessGo.SetActive(true);
            Check(true, "missing collider arrow never threw");
        });

        Test("BestShotInternal gate: cloned SO gets the forced low solution; native/foreign/unbuilt SOs run the original", () =>
        {
            AssetHost assets = BuildNativeAssets();
            RunEnsureAssets();
            ArrowAttack so = HostSo();
            Vector2 target = new Vector2(6f, 1f);
            float gravity = -9.81f;

            Check(CrossbowmanBoltWallPierce.TryForceLowTrajectory(so, target, gravity, false, out Vector2 forced),
                "cloned SO hits the gate");
            Eqv(NativeBestShotInternal(so, target, gravity, false, false, false), forced,
                "forced result == native low solution (same math, ParabolaCast skipped)");
            Vector2 nativeWallBlocked = NativeBestShotInternal(so, target, gravity, false, false, true);
            Check(MathF.Abs(forced.y - nativeWallBlocked.y) > 1e-3f && forced.y < nativeWallBlocked.y,
                "wall-blocked native returns the high arc; the prefix forces the flatter low arc");
            Check(MathF.Abs(forced.magnitude - so._shotMagnitude) <= 1e-3f,
                "result clamped to the shot magnitude");

            Check(!CrossbowmanBoltWallPierce.TryForceLowTrajectory(assets.BaseSo, target, gravity, false, out _),
                "native SO misses the gate");
            ArrowAttack foreign = new ArrowAttack("foreign") { _shotMagnitude = 8f };
            Check(!CrossbowmanBoltWallPierce.TryForceLowTrajectory(foreign, target, gravity, false, out _),
                "foreign SO misses the gate");
            Check(!CrossbowmanBoltWallPierce.TryForceLowTrajectory(null, target, gravity, false, out _),
                "null SO misses the gate");

            so._boostedShotMagnitude = 40f;                               // boosted 读 _boostedShotMagnitude
            Check(CrossbowmanBoltWallPierce.TryForceLowTrajectory(so, target, gravity, true, out Vector2 boosted),
                "boosted shot hits the gate");
            Eqv(NativeBestShotInternal(so, target, gravity, false, true, false), boosted,
                "boosted result uses _boostedShotMagnitude");
            Check(MathF.Abs(boosted.x - forced.x) > 1e-3f, "boosted differs from normal (magnitude honored)");

            Vector2 far = new Vector2(500f, 0f);                          // 无解：45° 回退等价原生
            Check(CrossbowmanBoltWallPierce.TryForceLowTrajectory(so, far, gravity, false, out Vector2 noSolution),
                "unreachable target still returns a vector");
            Eqv(NativeBestShotInternal(so, far, gravity, false, false, true), noSolution,
                "no-solution fallback equals the native 45-degree branch");

            Host.GetField("_crossbowAttackSO", StaticAll).SetValue(null, null);   // 资产未构建
            Check(PatchRoles_Crossbowman.ClonedAttackSoPointer == IntPtr.Zero, "gate is zero without assets");
            Check(!CrossbowmanBoltWallPierce.TryForceLowTrajectory(so, target, gravity, false, out _),
                "unbuilt assets: native BestShotInternal runs");
        });

        Test("BestShotInternal prefix wiring: skips the original and writes the low solution; forceHighShot-independent", () =>
        {
            AssetHost assets = BuildNativeAssets();
            RunEnsureAssets();
            ArrowAttack so = HostSo();
            Vector2 target = new Vector2(6f, 1f);
            float gravity = -9.81f;

            MethodInfo prefix = typeof(ArrowAttack_BestShotInternal_CrossbowmanLowTrajectory_Patch)
                .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
            Check(prefix != null, "prefix method found");
            Check(prefix.ReturnType == typeof(bool), "prefix returns bool (skippable)");

            CrossbowmanBoltWallPierce.TryForceLowTrajectory(so, target, gravity, false, out Vector2 expected);
            object[] args = { so, target, gravity, false, default(Vector2) };
            object skipped = prefix.Invoke(null, args);
            Check(skipped is bool b && !b, "prefix returns false for the cloned SO (original skipped)");
            Eqv(expected, (Vector2)args[4], "prefix wrote the forced solution into __result");

            object ran = prefix.Invoke(null, new object[] { assets.BaseSo, target, gravity, false, default(Vector2) });
            Check(ran is bool ok && ok, "prefix returns true for native SOs (original runs)");
        });

        Test("deadlands follower package shares the cloned SO and therefore the forced low trajectory (expected scope)", () =>
        {
            AssetHost assets = BuildNativeAssets();
            RunEnsureAssets();
            ArrowAttack so = HostSo();
            GameObject followerGo = NewGo("follower");
            Archer follower = followerGo.AddComponent<Archer>();
            follower._arrowAttack = assets.BaseSo;
            follower.ActiveArrowAttack = assets.BaseSo;

            PatchRoles_Crossbowman.ApplySquadCrossbowPackage(follower);
            Check(follower.ActiveArrowAttack == so, "follower package assigns the same cloned SO");
            Check(CrossbowmanBoltWallPierce.TryForceLowTrajectory(follower.ActiveArrowAttack,
                new Vector2(5f, 0.5f), -9.81f, false, out _), "follower shots hit the gate too (same SO, by design)");
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
            Check(!CrossbowmanBoltWallPierce.TryForceLowTrajectory(assets.BaseSo, new Vector2(6f, 1f), -9.81f, false, out _),
                "native tower attack SOs keep native BestShotInternal");
        });

        Test("hook contract: exactly one new BestShotInternal prefix; no other native surface touched", () =>
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
                "Archer.IsAvailableForJob", "Archer.OnDisable", "Archer.OnEnable",
                "ArrowAttack.BestShotInternal", "Character.Promote", "Pool.FastSpawn",
                "PoolManager.Init", "World.OnLevelLoaded",
            };
            Check(targets.Count == expected.Count, "patch count = 7 existing crossbowman hooks + exactly 1 new (got "
                + targets.Count + ": " + string.Join(",", targets) + ")");
            for (int i = 0; i < expected.Count && i < targets.Count; i++)
                Check(targets[i] == expected[i], "patch target[" + i + "] == " + expected[i]);

            Type lowTrajectoryPatch = typeof(ArrowAttack_BestShotInternal_CrossbowmanLowTrajectory_Patch);
            int prefixes = 0, postfixes = 0, finalizers = 0;
            foreach (MethodInfo method in lowTrajectoryPatch.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (method.GetCustomAttributes(typeof(HarmonyLib.HarmonyPrefix), false).Length > 0) prefixes++;
                if (method.GetCustomAttributes(typeof(HarmonyLib.HarmonyPostfix), false).Length > 0) postfixes++;
                if (method.GetCustomAttributes(typeof(HarmonyLib.HarmonyFinalizer), false).Length > 0) finalizers++;
            }
            Check(prefixes == 1 && postfixes == 0 && finalizers == 0,
                "the new patch is exactly one prefix (no postfix/finalizer)");
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

        Console.WriteLine();
        Console.WriteLine((_failures == 0 ? "ALL PASS" : "FAILURES") + ": pass=" + _passes + " fail=" + _failures
            + "; " + _assertions + " assertions");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
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
