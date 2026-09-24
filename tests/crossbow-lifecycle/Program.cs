// 弩手 marker 生命周期回归（destroy-lifecycle-20260914 worker）。
//
// 直链真生产文件 il2cpp/CrossbowmanLifecycle.cs（csproj Compile Include，未改一行），
// 边界替身见 Stubs.cs。覆盖：
// - 身份（Active）是唯一资格来源：失效 marker 不再赋资格/招募排除/巡检强化；
// - Strip 立即失效 + 还原 owned 属性，且**绝不销毁组件**（Destroy/DestroyImmediate
//   全进程零调用，由 Test 统一断言）；
// - 同帧 Strip→Apply、重复 Apply、Apply/Strip 中途异常：冷却只乘一次；
// - 死亡/掉弓（池复用）/普通停用重开/配置关/巡检缓存含孤儿与 null 的边界；
// - 火矢 buff、塔位射程、无基线降级、无 marker 群体（死地随从同群体）零写入。
//
// 运行：dotnet run --project tests/crossbow-lifecycle
using System;
using KingdomEnhancedMod;
using UnityEngine;

static class Program
{
    private static int _passed;
    private static int _failed;
    private static int _assertions;
    private static bool _bannerThrows;
    private static int _bannerCalls;

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

    // ============================================================
    // 夹具
    // ============================================================

    private sealed class Unit
    {
        internal Archer Archer;
        internal Animator Animator;
        internal ArrowAttack Native;
        internal ArrowAttack Fire;
        internal ArrowAttack Crossbow;
        internal RuntimeAnimatorController Deadlands;
        internal RuntimeAnimatorController Hunter;
        internal RuntimeAnimatorController BaseSkin;
        internal CrossbowmanProfile Profile;

        internal GameObject Go => Archer.gameObject;
    }

    // 进程级资产（与生产一致：克隆 SO/死地控制器全进程一份，所有弩手共用）——
    // 因此"按 profile 确认所有权"在多单位场景下成立。
    private static readonly ArrowAttack CrossbowAsset = new ArrowAttack("KEM_CrossbowAttack");
    private static readonly RuntimeAnimatorController DeadlandsAsset =
        new RuntimeAnimatorController("archer_soldier_deadlands");

    private static Unit NewUnit(string tag)
    {
        var go = new GameObject();
        var archer = go.AddComponent<Archer>();
        var animator = go.AddComponent<Animator>();
        go.AddComponent<Mover>();

        var unit = new Unit { Archer = archer, Animator = animator };
        unit.Native = new ArrowAttack("native-" + tag);
        unit.Fire = new ArrowAttack("fire-" + tag);
        unit.Crossbow = CrossbowAsset;
        unit.Deadlands = DeadlandsAsset;
        unit.Hunter = new RuntimeAnimatorController("hunter-" + tag);
        unit.BaseSkin = new RuntimeAnimatorController("base-" + tag);

        archer._arrowAttack = unit.Native;
        archer._fireArrowAttack = unit.Fire;
        archer.ActiveArrowAttack = unit.Native;
        archer.hunterAnimator = unit.Hunter;
        archer._enemyScanner.range = 8f;                       // 原生地面扫描器=towerShootRange 之外的 shootRange
        archer._enemyScanner.rangeBehind = 8f;
        archer._shootIntervalRange = new Vector2(1f, 2f);       // 原生序列化冷却（池实例默认值）
        archer._shootIntervalRangeFormation = new Vector2(3f, 4f);
        animator.runtimeAnimatorController = unit.BaseSkin;

        unit.Profile = BuildProfile(unit);
        return unit;
    }

    private static CrossbowmanProfile BuildProfile(Unit unit) => new CrossbowmanProfile
    {
        Attack = unit.Crossbow,
        Skin = unit.Deadlands,
        ReapplyBanner = archer =>
        {
            _bannerCalls++;
            if (_bannerThrows) throw new InvalidOperationException("banner step failed");
            archer._isWearingBannerColor = true;
        },
        BaseShootRange = 8f,
        BaseShootRangeKnown = true,
        BaseInterval = new Vector2(1f, 2f),
        BaseIntervalKnown = true,
        BaseIntervalFormation = new Vector2(3f, 4f),
        BaseIntervalFormationKnown = true,
        BaseSkin = unit.BaseSkin,
    };

    private static bool Apply(Unit unit) => CrossbowmanLifecycle.Apply(unit.Archer, unit.Profile);

    private static void Strip(Unit unit) => CrossbowmanLifecycle.Strip(unit.Archer, unit.Profile);

    private static CrossbowmanMarker Marker(Unit unit) => unit.Archer.GetComponent<CrossbowmanMarker>();

    private static int CountErrors(string tag)
    {
        int count = 0;
        var errors = KingdomEnhancedPlugin.Logger.Errors;
        for (int i = 0; i < errors.Count; i++)
        {
            if (errors[i].StartsWith(tag, StringComparison.Ordinal)) count++;
        }
        return count;
    }

    private static void ResetWorld()
    {
        // Each case starts a separate simulated process/world; production-owned records
        // and process log quotas must not leak into the following fixture.
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var lifecycle = typeof(CrossbowmanLifecycle);
        ((System.Collections.IList)lifecycle.GetField("_owned", flags).GetValue(null)).Clear();
        lifecycle.GetField("_poolSpawnDepth", flags).SetValue(null, 0);
        foreach (string name in new[] { "_applyErrorLogs", "_stripErrorLogs", "_readerErrorLogs", "_scanErrorLogs" })
            lifecycle.GetField(name, flags).SetValue(null, 0);
        KingdomEnhancedPlugin.Logger.Errors.Clear();
        KingdomEnhancedPlugin.Logger.Warnings.Clear();
        ModConfig.Enabled.Value = true;
        PatchRoles_CrossbowDefense.Reset();
        GreekScaleScope.Reset();
        ScaleRegistryHolder.Reset();
        BiomeData.Current = new BiomeData();
        UnityEngine.Object.ResetDestroyLog();
        _bannerCalls = 0;
        _bannerThrows = false;
    }

    private static void Test(string name, Action body, int expectErrors = 0)
    {
        ResetWorld();
        int errorsBefore = KingdomEnhancedPlugin.Logger.Errors.Count;
        try
        {
            body();
            int produced = KingdomEnhancedPlugin.Logger.Errors.Count - errorsBefore;
            if (expectErrors >= 0)
                Check(produced == expectErrors, $"error log count: expected {expectErrors}, got {produced}");
            Check(UnityEngine.Object.DestroyCalls.Count == 0, "lifecycle must never call Destroy");
            Check(UnityEngine.Object.DestroyImmediateCalls.Count == 0, "lifecycle must never call DestroyImmediate");
            _passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception e)
        {
            _failed++;
            Console.WriteLine("FAIL " + name + ": " + e.Message);
        }
    }

    // ============================================================
    // 用例
    // ============================================================

    private static void Main()
    {
        Test("apply commits identity and full package once", () =>
        {
            var u = NewUnit("a");
            Check(Apply(u), "apply commits");
            Check(CrossbowmanLifecycle.IsCrossbowman(u.Archer), "identity granted");
            Check(u.Archer.ActiveArrowAttack == u.Crossbow, "crossbow arrow");
            Eq(12f, u.Archer.shootRange, "shoot range");
            Eq(12f, u.Archer._enemyScanner.range, "scanner range");
            Eq(12f, u.Archer._enemyScanner.rangeBehind, "scanner rear range");
            Eqv(new Vector2(2f, 4f), u.Archer._shootIntervalRange, "cooldown x2");
            Eqv(new Vector2(6f, 8f), u.Archer._shootIntervalRangeFormation, "formation cooldown x2");
            Check(u.Animator.runtimeAnimatorController == u.Deadlands, "deadlands skin");
            Check(u.Archer._isWearingBannerColor, "banner applied");
            Eq(1.15f, u.Archer.transform.localScale.y, "scale y");
            Check(ScaleRegistryHolder.RegisterCalls == 1, "scale guard registered");
            Eq(1.15f, ScaleRegistryHolder.LastRegisteredY, "scale guard value");
            Check(PatchRoles_CrossbowDefense.ReconcileCalls == 1, "tower range reconciled");
            Check(PatchRoles_CrossbowDefense.ReconcileSawIdentity,
                "tower boost gate sees the committed identity (first-boost order)");
            Check(u.Go.CountComponents<CrossbowmanMarker>() == 1, "single reusable marker");
            Check(_bannerCalls == 1, "banner step ran once");
        });

        Test("second apply is idempotent (no stacked cooldown, same marker)", () =>
        {
            var u = NewUnit("b");
            Check(Apply(u), "first apply");
            var marker = Marker(u);
            var interval = u.Archer._shootIntervalRange;
            var formation = u.Archer._shootIntervalRangeFormation;
            Check(Apply(u), "second apply");
            Eqv(interval, u.Archer._shootIntervalRange, "cooldown not stacked");
            Eqv(formation, u.Archer._shootIntervalRangeFormation, "formation not stacked");
            Check(Marker(u) == marker, "marker instance reused");
            Check(u.Go.CountComponents<CrossbowmanMarker>() == 1, "no duplicate marker");
            Check(CrossbowmanLifecycle.IsCrossbowman(u.Archer), "identity intact");
        });

        Test("same-frame strip then apply activates with one multiplier", () =>
        {
            var u = NewUnit("c");
            Check(Apply(u), "apply");
            Strip(u);
            Check(!CrossbowmanLifecycle.IsCrossbowman(u.Archer), "strip revokes immediately");
            Eqv(new Vector2(1f, 2f), u.Archer._shootIntervalRange, "intervals restored before re-apply");
            Check(Apply(u), "re-apply same frame");
            Check(CrossbowmanLifecycle.IsCrossbowman(u.Archer), "identity back");
            Eqv(new Vector2(2f, 4f), u.Archer._shootIntervalRange, "cooldown exactly x2 (not x4)");
            Eqv(new Vector2(6f, 8f), u.Archer._shootIntervalRangeFormation, "formation exactly x2");
            Check(u.Archer.ActiveArrowAttack == u.Crossbow, "crossbow arrow");
            Check(u.Go.CountComponents<CrossbowmanMarker>() == 1, "marker reused, never duplicated");
            Check(PatchRoles_CrossbowDefense.RemoveCalls == 1, "tower state released once");
        });

        Test("strip immediately revokes identity and restores owned fields", () =>
        {
            var u = NewUnit("d");
            Check(Apply(u), "apply");
            var marker = Marker(u);
            Strip(u);
            Check(!CrossbowmanLifecycle.IsCrossbowman(u.Archer), "identity revoked");
            Check(Marker(u) == marker, "marker component kept for reuse (not destroyed)");
            Check(u.Archer.ActiveArrowAttack == u.Native, "native arrow restored");
            Eq(8f, u.Archer.shootRange, "ground shoot range restored");
            Eq(8f, u.Archer._enemyScanner.range, "scanner restored");
            Eq(8f, u.Archer._enemyScanner.rangeBehind, "scanner rear restored");
            Eqv(new Vector2(1f, 2f), u.Archer._shootIntervalRange, "interval restored");
            Eqv(new Vector2(3f, 4f), u.Archer._shootIntervalRangeFormation, "formation restored");
            Check(u.Animator.runtimeAnimatorController == u.Hunter, "hunter skin restored via biome swap");
            Check(!u.Archer._isWearingBannerColor, "banner flag cleared");
            Eq(1f, u.Archer.transform.localScale.y, "owned scale released");
            Check(ScaleRegistryHolder.UnregisterCalls == 1, "scale guard released");
            Check(PatchRoles_CrossbowDefense.RemoveCalls == 1, "tower range released");
        });

        Test("invalid marker gets no reinforcement and no writes", () =>
        {
            var u = NewUnit("e");
            Check(Apply(u), "apply");
            Strip(u);

            // 失效后原生写入的值（换皮/换箭/其他功能）绝不能被巡检改写
            u.Archer.ActiveArrowAttack = u.Native;
            u.Archer.shootRange = 5f;
            u.Archer._shootIntervalRange = new Vector2(7f, 8f);
            u.Animator.runtimeAnimatorController = u.BaseSkin;
            PatchRoles_CrossbowDefense.Reset();

            Check(!CrossbowmanLifecycle.Reconcile(u.Archer, u.Profile), "reconcile reports inactive");
            CrossbowmanLifecycle.ReconcileScan(new[] { Marker(u) }, u.Profile);

            Check(!CrossbowmanLifecycle.IsCrossbowman(u.Archer), "still not a crossbowman");
            Check(u.Archer.ActiveArrowAttack == u.Native, "arrow untouched");
            Eq(5f, u.Archer.shootRange, "range untouched");
            Eqv(new Vector2(7f, 8f), u.Archer._shootIntervalRange, "interval untouched");
            Check(u.Animator.runtimeAnimatorController == u.BaseSkin, "skin untouched");
            Eq(1f, u.Archer.transform.localScale.y, "scale untouched");
            Check(PatchRoles_CrossbowDefense.ReconcileCalls == 0, "no tower reinforcement");
            Check(PatchRoles_CrossbowDefense.PullBackCalls == 0, "no night pullback reinforcement");
        });

        Test("reconcile heals native resets but leaves intervals alone", () =>
        {
            var u = NewUnit("f");
            Check(Apply(u), "apply");
            var interval = u.Archer._shootIntervalRange;
            var formation = u.Archer._shootIntervalRangeFormation;

            // 原生 OnEnable/网络收包重置战斗包 + 衣色丢失 + 皮肤被塔/船路径换掉 + 更晚的缩放写入者
            u.Archer.ActiveArrowAttack = u.Archer._arrowAttack;
            u.Archer.shootRange = 8f;
            u.Archer._isWearingBannerColor = false;
            u.Animator.runtimeAnimatorController = u.BaseSkin;
            u.Archer.transform.localScale.y = 1f;
            PatchRoles_CrossbowDefense.Reset();
            int scaleWritesAfterApply = GreekScaleScope.ApplyYCalls;

            // 走宿主真实调用路径（IntegrityPass → ReconcileScan）：漂移汇总日志在批次出口
            CrossbowmanLifecycle.ReconcileScan(new[] { Marker(u) }, u.Profile);
            Check(CrossbowmanLifecycle.IsCrossbowman(u.Archer), "still a crossbowman");
            Check(u.Archer.ActiveArrowAttack == u.Crossbow, "arrow healed");
            Eq(12f, u.Archer.shootRange, "range healed");
            Eqv(interval, u.Archer._shootIntervalRange, "interval not re-multiplied by the pass");
            Eqv(formation, u.Archer._shootIntervalRangeFormation, "formation not re-multiplied");
            Check(u.Animator.runtimeAnimatorController == u.Deadlands, "skin healed");
            Check(u.Archer._isWearingBannerColor, "banner re-applied");
            Check(PatchRoles_CrossbowDefense.ReconcileCalls == 1, "tower range reconciled");
            Check(PatchRoles_CrossbowDefense.PullBackCalls == 1, "night pullback heartbeat");
            Check(GreekScaleScope.ApplyYCalls == scaleWritesAfterApply,
                "scale writes stay with the per-frame guard");
            Check(KingdomEnhancedPlugin.Logger.Warnings.Exists(
                w => w.StartsWith("[Crossbowman] scale drift", StringComparison.Ordinal)),
                "scale drift diagnosed without rewriting the transform");
        });

        Test("fire arrow buff is never overwritten by the pass", () =>
        {
            var u = NewUnit("g");
            Check(Apply(u), "apply");
            u.Archer.ActiveArrowAttack = u.Fire;                 // 火矢 buff 生效中
            u.Archer.shootRange = 8f;                            // 同时原生重置了射程
            Check(CrossbowmanLifecycle.Reconcile(u.Archer, u.Profile), "still a crossbowman");
            Check(u.Archer.ActiveArrowAttack == u.Fire, "fire arrow kept");
            Eq(12f, u.Archer.shootRange, "range still healed");
            u.Archer.ActiveArrowAttack = u.Archer._arrowAttack;  // buff 结束 → 原生回基础箭
            CrossbowmanLifecycle.Reconcile(u.Archer, u.Profile);
            Check(u.Archer.ActiveArrowAttack == u.Crossbow, "crossbow arrow restored after buff");
        });

        Test("tower position restores scanner to tower value", () =>
        {
            var u = NewUnit("h");
            Check(Apply(u), "apply");
            u.Archer.inGuardSlot = true;
            u.Archer.towerShootRange = 18f;
            u.Archer._enemyScanner.range = 18f;
            u.Archer._enemyScanner.rangeBehind = 18f;
            Strip(u);
            Eq(8f, u.Archer.shootRange, "ground shoot range restored");
            Eq(18f, u.Archer._enemyScanner.range, "tower scanner keeps tower value");
            Eq(18f, u.Archer._enemyScanner.rangeBehind, "tower rear scanner kept");

            var pending = NewUnit("i");
            Check(Apply(pending), "apply pending-slot unit");
            pending.Archer._guardSlot = new GuardSlot();          // 客户端待上塔：槽位已给、inGuardSlot 未置
            pending.Archer.towerShootRange = 18f;
            Strip(pending);
            Eq(18f, pending.Archer._enemyScanner.range, "pending guard slot uses tower value");
        });

        Test("config off unwinds the package and never resurrects identity", () =>
        {
            var u = NewUnit("j");
            Check(Apply(u), "apply");
            ModConfig.Enabled.Value = false;
            Check(!CrossbowmanLifecycle.Reconcile(u.Archer, u.Profile), "config off is not honored");
            Check(!CrossbowmanLifecycle.IsCrossbowman(u.Archer), "identity cleared");
            Check(u.Archer.ActiveArrowAttack == u.Native, "arrow unwound");
            Eq(8f, u.Archer.shootRange, "range unwound");
            Eqv(new Vector2(1f, 2f), u.Archer._shootIntervalRange, "cooldown unwound");
            Eq(1f, u.Archer.transform.localScale.y, "scale unwound");

            ModConfig.Enabled.Value = true;
            Check(!CrossbowmanLifecycle.Reconcile(u.Archer, u.Profile), "reconcile never grants identity");
            Check(u.Archer.ActiveArrowAttack == u.Native, "no resurrection writes");
            Check(PatchRoles_CrossbowDefense.PullBackCalls == 0, "no crossbow behavior while unwound");
        });

        Test("plain disable invalidates identity but keeps the life selection", () =>
        {
            var u = NewUnit("k");
            Check(Apply(u), "apply");
            var interval = u.Archer._shootIntervalRange;

            // 原生 OnDisable prefix：身份立即失效；Selected（本 life 选择）保留——隐藏≠池归还。
            // 原生主体会重置 ActiveArrowAttack 并 ConvertToHunter（不恢复 interval/shootRange）。
            u.Go.activeInHierarchy = false;
            CrossbowmanLifecycle.OnArcherDisablePrefix(u.Archer);
            Check(!CrossbowmanLifecycle.IsCrossbowman(u.Archer), "identity invalidated at disable");
            u.Archer.ActiveArrowAttack = u.Archer._arrowAttack;
            u.Archer._isWearingBannerColor = false;

            // 普通隐藏重开（无池作用域）：prefix 恢复 Active 供原生 OnEnable 内的招募判定，
            // postfix 修原生重置后的战斗包；冷却不重乘。
            u.Go.activeInHierarchy = true;
            CrossbowmanLifecycle.OnArcherEnablePrefix(u.Archer, u.Profile);
            Check(CrossbowmanLifecycle.IsCrossbowman(u.Archer), "identity restored before native body");
            u.Archer.ActiveArrowAttack = u.Archer._arrowAttack;
            u.Archer.shootRange = 8f;
            CrossbowmanLifecycle.OnArcherEnablePostfix(u.Archer, u.Profile);
            Check(u.Archer.ActiveArrowAttack == u.Crossbow, "package healed after re-enable");
            Eq(12f, u.Archer.shootRange, "range healed after re-enable");
            Eqv(interval, u.Archer._shootIntervalRange, "cooldown not re-multiplied");
            Check(CrossbowmanLifecycle.IsCrossbowman(u.Archer), "still a crossbowman");
        });

        Test("config off invalidates readers immediately and unwinds without the scan", () =>
        {
            CrossbowmanLifecycle.UnwindAll(default);   // 先清掉此前用例留在 registry 里的实例
            Check(!CrossbowmanLifecycle.HasPendingWork, "registry starts empty");

            var u = NewUnit("k2");
            Check(Apply(u), "apply");

            // 池中（inactive）实例：不在 5s 扫描缓存里，registry 仍必须能解除它
            u.Go.activeInHierarchy = false;
            ModConfig.Enabled.Value = false;
            Check(!CrossbowmanLifecycle.IsCrossbowman(u.Archer), "reader invalidates immediately");
            Check(CrossbowmanLifecycle.HasPendingWork, "owned registry still holds the instance");

            CrossbowmanLifecycle.UnwindAll(u.Profile);

            CrossbowmanMarker marker = Marker(u);
            Check(marker != null && !marker.Selected && !marker.Active && !marker.Residue,
                "instance fully unwound");
            Check(!CrossbowmanLifecycle.HasPendingWork, "registry settled");
            Check(u.Archer.ActiveArrowAttack == u.Native, "pooled instance arrow restored");
            Eq(8f, u.Archer.shootRange, "pooled instance range restored");
            Eqv(new Vector2(1f, 2f), u.Archer._shootIntervalRange, "pooled instance cooldown restored");
            Eq(1f, u.Archer.transform.localScale.y, "pooled instance scale released");

            ModConfig.Enabled.Value = true;
            Check(!CrossbowmanLifecycle.IsCrossbowman(u.Archer), "no resurrection after unwind");
            CrossbowmanLifecycle.UnwindAll(u.Profile);
            Check(!CrossbowmanLifecycle.HasPendingWork, "second unwind is a no-op");
        });

        Test("pool reuse (death/bow drop) clears the old life before re-designation", () =>
        {
            var u = NewUnit("l");
            Check(Apply(u), "first life");

            // 死亡/掉弓 → 池归还：OnDisable prefix 失效身份，FastDespawn 只 SetActive(false)
            u.Go.activeInHierarchy = false;
            CrossbowmanLifecycle.OnArcherDisablePrefix(u.Archer);
            Check(!CrossbowmanLifecycle.IsCrossbowman(u.Archer), "old life identity invalidated");

            // 真池激活 = FastSpawn 作用域内的 Archer.OnEnable：prefix **在原生主体之前**清旧 life，
            // 主体（AddArcher/DistributeFreeArchers 招募判定 + 随从包写入）随后跑
            u.Go.activeInHierarchy = true;
            CrossbowmanLifecycle.BeginPoolSpawnScope();
            CrossbowmanLifecycle.OnArcherEnablePrefix(u.Archer, u.Profile);
            Check(!CrossbowmanLifecycle.IsCrossbowman(u.Archer), "new life starts unqualified (recruit-safe)");
            Check(u.Archer.ActiveArrowAttack == u.Native, "old package arrow cleared before native body");
            Eq(8f, u.Archer.shootRange, "old package range cleared before native body");
            Eqv(new Vector2(1f, 2f), u.Archer._shootIntervalRange, "old package cooldown cleared before native body");
            Eq(1f, u.Archer.transform.localScale.y, "old package scale released before native body");
            CrossbowmanMarker settledMarker = Marker(u);
            Check(settledMarker != null && !settledMarker.Selected && !settledMarker.Active && !settledMarker.Residue,
                "old-life selection and residue settled");

            CrossbowmanLifecycle.OnArcherEnablePostfix(u.Archer, u.Profile);
            CrossbowmanLifecycle.EndPoolSpawnScope();
            Check(!CrossbowmanLifecycle.IsCrossbowman(u.Archer), "plain archer until re-designated");

            // 新 life 被选为弩手：marker 复用、冷却只乘一次
            Check(Apply(u), "new life designated");
            Check(u.Go.CountComponents<CrossbowmanMarker>() == 1, "marker reused across lives");
            Check(CrossbowmanLifecycle.IsCrossbowman(u.Archer), "identity granted for new life");
            Eqv(new Vector2(2f, 4f), u.Archer._shootIntervalRange, "cooldown scaled exactly once");
            Eqv(new Vector2(6f, 8f), u.Archer._shootIntervalRangeFormation, "formation scaled once");
            Eq(1.15f, u.Archer.transform.localScale.y, "scale not stacked");
            Eq(12f, u.Archer.shootRange, "fresh range");
        });

        Test("hidden selected crossbowman survives a stale scan cache", () =>
        {
            var u = NewUnit("l6");
            Check(Apply(u), "apply");
            var interval = u.Archer._shootIntervalRange;

            // 隐藏：OnDisable prefix 失效身份（原生主体随后把箭写回基础箭）
            CrossbowmanLifecycle.OnArcherDisablePrefix(u.Archer);
            u.Go.activeInHierarchy = false;
            u.Archer.ActiveArrowAttack = u.Archer._arrowAttack;
            Check(!CrossbowmanLifecycle.IsCrossbowman(u.Archer), "identity invalidated at disable");

            // 旧 UnitScanCache 数组仍含这个 marker（5s 窗口快照）→ 巡检不得清掉本 life 选择、
            // 也不得在停用对象上做任何强化/还原写入
            CrossbowmanLifecycle.ReconcileScan(new[] { Marker(u) }, u.Profile);
            CrossbowmanLifecycle.Reconcile(u.Archer, u.Profile);
            CrossbowmanMarker marker = Marker(u);
            Check(marker.Selected && !marker.Active && !marker.Residue, "hidden selection kept, not stripped");
            Eqv(interval, u.Archer._shootIntervalRange, "hidden package untouched by the pass");
            Eq(1.15f, u.Archer.transform.localScale.y, "hidden scale untouched by the pass");

            // 重开（无池作用域）：prefix 恢复身份、postfix 自愈包 → 仍是弩手且冷却不重乘
            u.Go.activeInHierarchy = true;
            CrossbowmanLifecycle.OnArcherEnablePrefix(u.Archer, u.Profile);
            Check(CrossbowmanLifecycle.IsCrossbowman(u.Archer), "crossbowman restored on re-enable");
            CrossbowmanLifecycle.OnArcherEnablePostfix(u.Archer, u.Profile);
            Check(u.Archer.ActiveArrowAttack == u.Crossbow, "package healed");
            Eqv(interval, u.Archer._shootIntervalRange, "cooldown not re-multiplied");
            Check(CrossbowmanLifecycle.IsCrossbowman(u.Archer), "still a crossbowman");
        });

        Test("pool new life keeps what the native body writes after the prefix clear", () =>
        {
            var u = NewUnit("l7");
            Check(Apply(u), "first life");
            CrossbowmanLifecycle.OnArcherDisablePrefix(u.Archer);
            u.Go.activeInHierarchy = false;

            // 池激活：prefix 先清旧 life
            u.Go.activeInHierarchy = true;
            CrossbowmanLifecycle.BeginPoolSpawnScope();
            CrossbowmanLifecycle.OnArcherEnablePrefix(u.Archer, u.Profile);

            // 原生主体：招募给这个 newlife 分配骑士随从，随从包/原生写入新的 Attack/range/skin/scale/interval
            var newAttack = new ArrowAttack("newlife-squad-so");
            var newSkin = new RuntimeAnimatorController("newlife-soldier-skin");
            u.Archer._arrowAttack = newAttack;
            u.Archer.ActiveArrowAttack = newAttack;
            u.Archer.shootRange = 12f;
            u.Archer._shootIntervalRange = new Vector2(0.7f, 0.8f);
            u.Animator.runtimeAnimatorController = newSkin;
            u.Archer.transform.localScale.y = 1.2f;
            u.Archer._isWearingBannerColor = true;

            // postfix 必须原样保留 newlife 的字段（不盲目 Strip 旧 life）
            CrossbowmanLifecycle.OnArcherEnablePostfix(u.Archer, u.Profile);
            CrossbowmanLifecycle.EndPoolSpawnScope();
            Check(u.Archer.ActiveArrowAttack == newAttack, "new-life attack preserved");
            Eq(12f, u.Archer.shootRange, "new-life range preserved");
            Eqv(new Vector2(0.7f, 0.8f), u.Archer._shootIntervalRange, "new-life cooldown preserved");
            Check(u.Animator.runtimeAnimatorController == newSkin, "new-life skin preserved");
            Eq(1.2f, u.Archer.transform.localScale.y, "new-life scale preserved");
            Check(u.Archer._isWearingBannerColor, "new-life banner flag preserved");
            Check(!CrossbowmanLifecycle.IsCrossbowman(u.Archer), "no crossbowman identity for the new life");
        });

        Test("retry cleanup after a failed pool clear restores only confirmable ownership", () =>
        {
            var u = NewUnit("l8");
            Check(Apply(u), "first life");
            u.Go.activeInHierarchy = false;
            CrossbowmanLifecycle.OnArcherDisablePrefix(u.Archer);

            // 池激活 prefix：清污中途失败（缩放释放步）→ 冷却已借用归还，Residue 保留
            ScaleRegistryHolder.FailUnregisterAfterCalls = 0;
            u.Go.activeInHierarchy = true;
            CrossbowmanLifecycle.BeginPoolSpawnScope();
            CrossbowmanLifecycle.OnArcherEnablePrefix(u.Archer, u.Profile);
            Eqv(new Vector2(1f, 2f), u.Archer._shootIntervalRange, "cooldown returned before the failure");
            Check(Marker(u).Residue, "residue kept for retry");

            // 原生主体随后写入一个不同的 SO；共享 SO 的真实随从包另有独立回归。
            var newAttack = new ArrowAttack("newlife-squad-so");
            var newSkin = new RuntimeAnimatorController("newlife-soldier-skin");
            u.Archer._arrowAttack = newAttack;
            u.Archer.ActiveArrowAttack = newAttack;
            u.Archer._shootIntervalRange = new Vector2(0.7f, 0.8f);
            u.Animator.runtimeAnimatorController = newSkin;
            u.Archer._isWearingBannerColor = true;

            ScaleRegistryHolder.FailUnregisterAfterCalls = -1;
            CrossbowmanLifecycle.OnArcherEnablePostfix(u.Archer, u.Profile);   // Residue → 重试收尾
            CrossbowmanLifecycle.EndPoolSpawnScope();

            Check(!Marker(u).Residue, "retry settled the residue");
            Eqv(new Vector2(0.7f, 0.8f), u.Archer._shootIntervalRange, "retry does not touch the new-life cooldown");
            Check(u.Animator.runtimeAnimatorController == newSkin, "retry does not touch the new-life skin");
            Check(u.Archer._isWearingBannerColor, "retry does not clear the new-life banner");
            Check(u.Archer.ActiveArrowAttack == newAttack, "retry does not touch a foreign attack SO");
        }, expectErrors: 1);

        Test("failed pool cleanup yields to a knight package sharing the exact same SO and scale", () =>
        {
            var u = NewUnit("shared-squad-handoff");
            Check(Apply(u), "old life applied");
            CrossbowmanLifecycle.OnArcherDisablePrefix(u.Archer);
            ScaleRegistryHolder.FailUnregisterAfterCalls = 0;
            CrossbowmanLifecycle.BeginPoolSpawnScope();
            CrossbowmanLifecycle.OnArcherEnablePrefix(u.Archer, u.Profile);
            Check(Marker(u).Residue && Marker(u).PendingPoolHandoff, "failed prefix retains a handoff receipt");

            // Actual Deadlands squad package reuses _crossbowAttackSO, 12, and 1.15.
            u.Archer._knight = new object();
            u.Archer.ActiveArrowAttack = u.Profile.Attack;
            u.Archer.shootRange = 12f;
            u.Archer._shootIntervalRange = new Vector2(2f, 4f);
            u.Animator.runtimeAnimatorController = u.Profile.Skin;
            u.Archer.transform.localScale.y = 1.15f;
            u.Archer._isWearingBannerColor = true;
            ScaleRegistryHolder.FailUnregisterAfterCalls = -1;
            ScaleRegistryHolder.Register(u.Archer.GetComponent<Mover>(), 1.15f);
            int unregisters = ScaleRegistryHolder.UnregisterCalls;

            CrossbowmanLifecycle.OnArcherEnablePostfix(u.Archer, u.Profile);
            CrossbowmanLifecycle.EndPoolSpawnScope();
            CrossbowmanLifecycle.ReconcileScan(new[] { Marker(u) }, u.Profile);
            CrossbowmanLifecycle.UnwindAll(u.Profile);
            Check(u.Archer.ActiveArrowAttack == u.Profile.Attack, "same shared SO belongs to the new knight owner");
            Eq(12f, u.Archer.shootRange, "new squad range retained");
            Eqv(new Vector2(2f, 4f), u.Archer._shootIntervalRange, "new squad interval retained");
            Check(u.Animator.runtimeAnimatorController == u.Profile.Skin, "shared skin retained");
            Eq(1.15f, u.Archer.transform.localScale.y, "shared scale retained");
            Check(u.Archer._isWearingBannerColor, "new banner retained");
            Eq(unregisters, ScaleRegistryHolder.UnregisterCalls, "old cleanup never removes the new scale registration");
            Check(!Marker(u).Residue && !Marker(u).Active && !Marker(u).Selected, "old identity relinquished");
        }, expectErrors: 1);

        Test("repeated active NetID receipt is not a new life", () =>
        {
            var u = NewUnit("l2");
            Check(Apply(u), "apply");
            var interval = u.Archer._shootIntervalRange;

            // native 实锤：FastSpawn 的 syncReceipt 命中 _activeCache 同 NetID 时直接返回已 active
            // 对象（Pool.cs:506）→ 不触发 OnEnable → 不能被当作新 life，职业与包都不动
            CrossbowmanLifecycle.BeginPoolSpawnScope();
            CrossbowmanLifecycle.EndPoolSpawnScope();

            Check(CrossbowmanLifecycle.IsCrossbowman(u.Archer), "profession untouched");
            Eqv(interval, u.Archer._shootIntervalRange, "package untouched");
            Eqv(new Vector2(6f, 8f), u.Archer._shootIntervalRangeFormation, "formation untouched");
            Check(u.Go.CountComponents<CrossbowmanMarker>() == 1, "marker untouched");
        });

        Test("ledger close state blocks repeated baseline fallback", () =>
        {
            var u = NewUnit("l3");
            u.Archer._shootIntervalRange = new Vector2(1.5f, 2.5f);   // custom before（外部功能写入）
            Check(Apply(u), "apply");
            Eqv(new Vector2(3f, 5f), u.Archer._shootIntervalRange, "custom value scaled once");

            ScaleRegistryHolder.FailUnregisterAfterCalls = 0;          // 归还 interval 之后的步骤失败
            Strip(u);
            Eqv(new Vector2(1.5f, 2.5f), u.Archer._shootIntervalRange, "custom before returned before failure");
            Eqv(new Vector2(3f, 4f), u.Archer._shootIntervalRangeFormation, "formation returned independently");

            u.Archer._shootIntervalRange = new Vector2(0.9f, 0.9f);    // 重试前外部改写
            ScaleRegistryHolder.FailUnregisterAfterCalls = -1;
            CrossbowmanLifecycle.Reconcile(u.Archer, u.Profile);       // Residue 收尾 = 重试 Strip
            Eqv(new Vector2(0.9f, 0.9f), u.Archer._shootIntervalRange,
                "external value survives the retry (no baseline fallback)");
            Eqv(new Vector2(3f, 4f), u.Archer._shootIntervalRangeFormation, "formation untouched by retry");
        }, expectErrors: 1);

        Test("synchronous re-entry: inner life wins and outer failure cannot clobber it", () =>
        {
            var u = NewUnit("l4");
            bool first = true;
            var profile = u.Profile;
            profile.ReapplyBanner = archer =>
            {
                _bannerCalls++;
                if (!first) return;
                first = false;
                CrossbowmanLifecycle.Strip(archer, u.Profile);   // 回调里同步重入（旧实现在此会互相覆盖）
                CrossbowmanLifecycle.Apply(archer, u.Profile);
            };
            Check(!CrossbowmanLifecycle.Apply(u.Archer, profile), "outer apply yields ownership");
            Check(CrossbowmanLifecycle.IsCrossbowman(u.Archer), "inner life identity intact");
            Eqv(new Vector2(2f, 4f), u.Archer._shootIntervalRange, "cooldown scaled once by inner life");
            Eqv(new Vector2(6f, 8f), u.Archer._shootIntervalRangeFormation, "formation scaled once");
            Check(u.Go.CountComponents<CrossbowmanMarker>() == 1, "single marker");
            CrossbowmanMarker marker = Marker(u);
            Check(marker != null && marker.Selected && marker.Active && !marker.Residue,
                "inner life owns the marker state");
        });

        Test("outer throw after re-entry does not clear the inner life", () =>
        {
            var u = NewUnit("l5");
            bool first = true;
            var profile = u.Profile;
            profile.ReapplyBanner = archer =>
            {
                if (!first) return;
                first = false;
                CrossbowmanLifecycle.Strip(archer, u.Profile);
                CrossbowmanLifecycle.Apply(archer, u.Profile);
                throw new InvalidOperationException("host step failed after re-entry");
            };
            Check(!CrossbowmanLifecycle.Apply(u.Archer, profile), "outer apply fails");
            Check(CrossbowmanLifecycle.IsCrossbowman(u.Archer), "inner life survives outer failure");
            Eqv(new Vector2(2f, 4f), u.Archer._shootIntervalRange, "cooldown still x2, not x4");
        }, expectErrors: 1);

        Test("strip borrows only its own interval, foreign write survives", () =>
        {
            var u = NewUnit("m");
            Check(Apply(u), "apply");
            u.Archer._shootIntervalRange = new Vector2(0.5f, 0.6f); // 外部功能（buff/Options）替换
            Strip(u);
            Eqv(new Vector2(0.5f, 0.6f), u.Archer._shootIntervalRange, "foreign interval preserved");
            Eqv(new Vector2(3f, 4f), u.Archer._shootIntervalRangeFormation, "own formation released");
            Check(Apply(u), "next life apply");
            Eqv(new Vector2(1f, 1.2f), u.Archer._shootIntervalRange, "fresh multiplier on current value");
            Eqv(new Vector2(6f, 8f), u.Archer._shootIntervalRangeFormation, "formation scaled once again");
        });

        Test("partial write failure rolls back identity and does not double multiply", () =>
        {
            var u = NewUnit("n");
            ScaleRegistryHolder.FailRegisterAfterCalls = 0;         // 冷却×2 与缩放之后失败
            Check(!Apply(u), "apply fails");
            Check(!CrossbowmanLifecycle.IsCrossbowman(u.Archer), "no half identity");
            Eqv(new Vector2(2f, 4f), u.Archer._shootIntervalRange, "cooldown scaled once before failure");
            Check(u.Archer.ActiveArrowAttack == u.Crossbow, "partial package present");
            Eq(1.15f, u.Archer.transform.localScale.y, "scale written before failure");

            Check(!CrossbowmanLifecycle.Reconcile(u.Archer, u.Profile), "pass finishes the cleanup");
            Check(u.Archer.ActiveArrowAttack == u.Native, "residue rolled back");
            Eq(8f, u.Archer.shootRange, "range rolled back");
            Eqv(new Vector2(1f, 2f), u.Archer._shootIntervalRange, "interval rolled back");
            Eqv(new Vector2(3f, 4f), u.Archer._shootIntervalRangeFormation, "formation rolled back");
            Eq(1f, u.Archer.transform.localScale.y, "scale rolled back");
            Check(Marker(u) != null, "marker kept for reuse");

            ScaleRegistryHolder.FailRegisterAfterCalls = -1;
            Check(Apply(u), "retry commits");
            Check(CrossbowmanLifecycle.IsCrossbowman(u.Archer), "identity after retry");
            Eqv(new Vector2(2f, 4f), u.Archer._shootIntervalRange, "cooldown still x2 (not x4)");
            Eq(1.15f, u.Archer.transform.localScale.y, "scale re-asserted once");
        }, expectErrors: 1);

        Test("failure before scaling then later apply multiplies once", () =>
        {
            var u = NewUnit("o");
            PatchRoles_CrossbowDefense.ReconcileThrows = true;     // 冷却×2 之前抛出
            Check(!Apply(u), "apply fails early");
            Check(!CrossbowmanLifecycle.IsCrossbowman(u.Archer), "no identity");
            Eqv(new Vector2(1f, 2f), u.Archer._shootIntervalRange, "cooldown untouched");
            PatchRoles_CrossbowDefense.ReconcileThrows = false;
            CrossbowmanLifecycle.Reconcile(u.Archer, u.Profile);   // Residue 收尾
            Eqv(new Vector2(1f, 2f), u.Archer._shootIntervalRange, "cleanup leaves base");
            Check(Apply(u), "later apply commits");
            Eqv(new Vector2(2f, 4f), u.Archer._shootIntervalRange, "exactly one multiplier");
        }, expectErrors: 1);

        Test("interrupted strip keeps residue and the next pass completes it", () =>
        {
            var u = NewUnit("p");
            Check(Apply(u), "apply");
            ScaleRegistryHolder.FailUnregisterAfterCalls = 0;      // 还原中途异常
            Strip(u);
            Check(!CrossbowmanLifecycle.IsCrossbowman(u.Archer), "identity revoked before failing step");
            Check(u.Archer.ActiveArrowAttack == u.Native, "steps before failure landed");
            Eqv(new Vector2(1f, 2f), u.Archer._shootIntervalRange, "intervals restored");

            ScaleRegistryHolder.FailUnregisterAfterCalls = -1;
            Check(!CrossbowmanLifecycle.Reconcile(u.Archer, u.Profile), "pass finishes cleanup");
            Eq(1f, u.Archer.transform.localScale.y, "scale finally released");
            Check(ScaleRegistryHolder.UnregisterCalls == 2, "cleanup retried once");

            int unregisterBefore = ScaleRegistryHolder.UnregisterCalls;
            PatchRoles_CrossbowDefense.Reset();
            CrossbowmanLifecycle.Reconcile(u.Archer, u.Profile);
            Check(ScaleRegistryHolder.UnregisterCalls == unregisterBefore
                && PatchRoles_CrossbowDefense.RemoveCalls == 0,
                "settled state is a strict no-op");
        }, expectErrors: 1);

        Test("markerless archers (deadlands follower group) are untouched", () =>
        {
            var u = NewUnit("q");                                  // 随从群体：从不挂 marker
            Strip(u);
            Check(!CrossbowmanLifecycle.IsCrossbowman(u.Archer), "no identity");
            Check(u.Archer.ActiveArrowAttack == u.Native, "arrow untouched");
            Eq(8f, u.Archer.shootRange, "range untouched");
            Eqv(new Vector2(1f, 2f), u.Archer._shootIntervalRange, "cooldown untouched");
            Check(u.Animator.runtimeAnimatorController == u.BaseSkin, "skin untouched");
            Eq(1f, u.Archer.transform.localScale.y, "scale untouched");
            Check(ScaleRegistryHolder.UnregisterCalls == 0, "no scale release");
            Check(PatchRoles_CrossbowDefense.RemoveCalls == 0, "no defense call");
            Check(!CrossbowmanLifecycle.Reconcile(u.Archer, u.Profile), "pass ignores markerless archers");
            Check(u.Go.CountComponents<CrossbowmanMarker>() == 0, "no marker created");
        });

        Test("cached scan neutralizes nulls and orphan markers without destroying them", () =>
        {
            var u = NewUnit("r");
            Check(Apply(u), "apply");
            var orphanGo = new GameObject();
            var orphan = orphanGo.AddComponent<CrossbowmanMarker>();
            orphan.Active = true;
            orphan.Residue = true;

            CrossbowmanLifecycle.ReconcileScan(
                new[] { null, orphan, Marker(u) }, u.Profile);

            Check(!orphan.Active && !orphan.Residue, "orphan identity neutralized");
            Check(orphanGo.GetComponent<CrossbowmanMarker>() == orphan, "orphan component kept (no destroy)");
            Check(CrossbowmanLifecycle.IsCrossbowman(u.Archer), "valid entry untouched");
            Check(PatchRoles_CrossbowDefense.PullBackCalls == 1, "valid entry kept its heartbeat");
            Check(u.Archer.ActiveArrowAttack == u.Crossbow, "valid entry kept its package");
        });

        Test("config off unwinds every cached marker", () =>
        {
            var a = NewUnit("s");
            var b = NewUnit("t");
            Check(Apply(a), "apply a");
            Check(Apply(b), "apply b");
            ModConfig.Enabled.Value = false;
            CrossbowmanLifecycle.ReconcileScan(new[] { Marker(a), Marker(b) }, a.Profile);
            Check(!CrossbowmanLifecycle.IsCrossbowman(a.Archer), "a unwound");
            Check(!CrossbowmanLifecycle.IsCrossbowman(b.Archer), "b unwound");
            Check(a.Archer.ActiveArrowAttack == a.Native, "a arrow restored");
            Check(b.Archer.ActiveArrowAttack == b.Native, "b arrow restored");
            Eq(8f, b.Archer.shootRange, "b range restored");
            Eq(1f, b.Archer.transform.localScale.y, "b scale released");
        });

        Test("missing base values degrade safely (assets not ready)", () =>
        {
            var u = NewUnit("u");
            var profile = u.Profile;
            profile.Skin = null;
            profile.BaseShootRangeKnown = false;
            profile.BaseIntervalKnown = false;
            profile.BaseIntervalFormationKnown = false;
            profile.BaseSkin = null;

            Check(CrossbowmanLifecycle.Apply(u.Archer, profile), "apply without skin but with arrow");
            Eqv(new Vector2(2f, 4f), u.Archer._shootIntervalRange, "cooldown scaled");
            Check(u.Animator.runtimeAnimatorController == u.BaseSkin, "controller kept when no skin");

            u.Archer.shootRange = 12f;
            CrossbowmanLifecycle.Strip(u.Archer, profile);
            Eqv(new Vector2(1f, 2f), u.Archer._shootIntervalRange, "ledger restores intervals without base cache");
            Eqv(new Vector2(3f, 4f), u.Archer._shootIntervalRangeFormation, "formation restored");
            Check(u.Archer.ActiveArrowAttack == u.Native, "arrow restored");
            Eq(12f, u.Archer.shootRange, "no base snapshot: range left alone");
            Check(u.Animator.runtimeAnimatorController == u.Hunter, "hunter skin via biome swap");
            Eq(1f, u.Archer.transform.localScale.y, "scale released");
        });

        Test("missing crossbow asset refuses to half apply", () =>
        {
            var u = NewUnit("v");
            var profile = u.Profile;
            profile.Attack = null;
            Check(!CrossbowmanLifecycle.Apply(u.Archer, profile), "aborts");
            Check(!CrossbowmanLifecycle.IsCrossbowman(u.Archer), "no identity");
            Check(u.Go.CountComponents<CrossbowmanMarker>() == 0, "no marker created");
            Eqv(new Vector2(1f, 2f), u.Archer._shootIntervalRange, "zero writes");
            Check(u.Archer.ActiveArrowAttack == u.Native, "arrow untouched");
            Check(GreekScaleScope.ApplyYCalls == 0, "no scale write");
        });

        Test("skin restore survives missing or throwing biome swap", () =>
        {
            var u = NewUnit("w");
            Check(Apply(u), "apply");
            u.Archer.hunterAnimator = null;                        // swap 无资产可用
            u.Animator.runtimeAnimatorController = u.BaseSkin;
            Strip(u);
            Check(u.Animator.runtimeAnimatorController == u.BaseSkin, "falls back to cached base skin");

            Check(Apply(u), "apply again");
            u.Archer.hunterAnimator = u.Hunter;                    // swap 资产可用但表未就绪
            BiomeData.Current.SwapThrows = true;
            u.Animator.runtimeAnimatorController = u.BaseSkin;
            Strip(u);
            Check(u.Animator.runtimeAnimatorController == u.BaseSkin, "throwing swap falls back");
            Check(!CrossbowmanLifecycle.IsCrossbowman(u.Archer), "identity still revoked");
        });

        Test("per-frame skin guard reasserts a native-flipped controller", () =>
        {
            var u = NewUnit("g1");
            Check(Apply(u), "apply commits");
            Check(u.Animator.runtimeAnimatorController == u.Deadlands, "skin assigned");
            u.Animator.runtimeAnimatorController = u.Hunter;      // native flips it back
            CrossbowmanLifecycle.MaintainSkin(u.Archer.GetComponent<Mover>());
            Check(u.Animator.runtimeAnimatorController == u.Deadlands, "same-frame reassert (walk twitch fix)");
        });
        Test("skin guard is a no-op while the controller is intact", () =>
        {
            var u = NewUnit("g2");
            Check(Apply(u), "apply commits");
            CrossbowmanLifecycle.MaintainSkin(u.Archer.GetComponent<Mover>());
            Check(u.Animator.runtimeAnimatorController == u.Deadlands, "no writes when pointers match");
        });
        Test("skin guard self-cleans after strip", () =>
        {
            var u = NewUnit("g3");
            Check(Apply(u), "apply commits");
            CrossbowmanLifecycle.Strip(u.Archer, u.Profile);
            u.Animator.runtimeAnimatorController = u.Hunter;      // post-strip native skin is native's business
            CrossbowmanLifecycle.MaintainSkin(u.Archer.GetComponent<Mover>());
            Check(u.Animator.runtimeAnimatorController == u.Hunter, "stripped unit is no longer guarded");
        });
        Test("skin guard ignores movers that were never crossbowmen", () =>
        {
            var go = new GameObject();
            var mover = go.AddComponent<Mover>();
            CrossbowmanLifecycle.MaintainSkin(mover);             // O(1) early-out path
            Check(true, "no throw, no registration");
        });
        Test("error logs stay bounded per path", () =>
        {
            // 50 次失败也不得刷屏：每条路径每进程最多 3 条（计数据是进程级，
            // 因此只断言总量上界，不做每测试增量的脆弱断言）。
            var u = NewUnit("x");
            _bannerThrows = true;
            for (int i = 0; i < 50; i++) Apply(u);

            var v = NewUnit("y");
            _bannerThrows = false;
            Check(Apply(v), "apply the strip subject");
            ScaleRegistryHolder.FailUnregisterAfterCalls = 0;
            for (int i = 0; i < 50; i++) Strip(v);

            int applyLogs = CountErrors("[Crossbowman/apply]");
            int stripLogs = CountErrors("[Crossbowman/strip]");
            Check(applyLogs <= 3, "apply path log cap: got " + applyLogs);
            Check(stripLogs <= 3, "strip path log cap: got " + stripLogs);
            Check(applyLogs >= 1 && stripLogs >= 1, "failures are reported at least once");
            Check(!CrossbowmanLifecycle.IsCrossbowman(u.Archer), "failed bursts leave no identity");
            Check(!CrossbowmanLifecycle.IsCrossbowman(v.Archer), "failed strip leaves no identity");
        }, expectErrors: -1);

        Console.WriteLine($"RESULT: {_passed} passed, {_failed} failed; {_assertions} assertions");
        Environment.ExitCode = _failed > 0 ? 1 : 0;
    }
}
