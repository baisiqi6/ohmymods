using System;
using KingdomEnhancedMod;

// 纯策略回归：cadence（0.25s 采样上限）/ pause 保持 / owner+world 归属与重建 /
// dispose 语义 / 有界故障 / donor 元数据（审计核定的原生资源字面量）。
// 不证明：native 实例化、FirePlaceholder.SwapNow、LightFaker 注册占用、暗处阈值、渲染。
internal static class Program
{
    private const int BambooBiome = 3;   // 测试用任意编号；native 侧传 BiomeHolder 常量
    private const int GreeceBiome = 5;
    private const int BaseBiome = 0;

    private static int _checks;
    private static int _failures;

    private static void Check(bool condition, string what)
    {
        _checks++;
        if (!condition)
        {
            _failures++;
            Console.WriteLine("FAIL  " + what);
        }
    }

    private static void CheckEqual<T>(T actual, T expected, string what) =>
        Check(object.Equals(actual, expected), what + " (expected " + expected + ", got " + actual + ")");

    private static int Main()
    {
        DonorSelection();
        DonorMetadata();
        Cadence();
        PauseRetention();
        SampleApplyAndFailure();
        OwnershipAndReuse();
        DisposeSemantics();
        FaultBounding();
        StatusText();
        Console.WriteLine("checks=" + _checks + " failures=" + _failures);
        return _failures == 0 ? 0 : 1;
    }

    private static void DonorSelection()
    {
        CheckEqual(CustomShopLightDonorTable.Select(GreeceBiome, BambooBiome, GreeceBiome), CustomShopLightDonor.OilLamp,
            "greece biome selects OilLamp donor");
        CheckEqual(CustomShopLightDonorTable.Select(BambooBiome, BambooBiome, GreeceBiome), CustomShopLightDonor.TorchBamboo,
            "bamboo biome selects TorchBamboo donor");
        CheckEqual(CustomShopLightDonorTable.Select(BaseBiome, BambooBiome, GreeceBiome), CustomShopLightDonor.Torch,
            "base biome selects Torch donor");
        CheckEqual(CustomShopLightDonorTable.Select(7, BambooBiome, GreeceBiome), CustomShopLightDonor.Torch,
            "other positive biome falls back to Torch donor (BiomeSpriteSwapper 原生适配)");
        CheckEqual(CustomShopLightDonorTable.Select(-1, BambooBiome, GreeceBiome), CustomShopLightDonor.None,
            "negative biome (unknown) selects None");
    }

    private static void DonorMetadata()
    {
        // 审计核定字面量（resources.assets pathID 对应的 ResourceManager 真实条目路径）。
        CheckEqual(CustomShopLightDonorTable.ResourcePath(CustomShopLightDonor.Torch), "prefabs/objects/torch",
            "torch resource path");
        CheckEqual(CustomShopLightDonorTable.PrefabName(CustomShopLightDonor.Torch), "Torch", "torch prefab name");
        CheckEqual(CustomShopLightDonorTable.ResourcePath(CustomShopLightDonor.OilLamp), "prefabs/objects/oillamp",
            "oillamp resource path");
        CheckEqual(CustomShopLightDonorTable.PrefabName(CustomShopLightDonor.OilLamp), "OilLamp", "oillamp prefab name");
        CheckEqual(CustomShopLightDonorTable.ResourcePath(CustomShopLightDonor.TorchBamboo),
            "prefabs/buildings and interactive/torch_bamboo", "torch_bamboo resource path");
        CheckEqual(CustomShopLightDonorTable.PrefabName(CustomShopLightDonor.TorchBamboo), "Torch_Bamboo",
            "torch_bamboo prefab name");

        Check(CustomShopLightDonorTable.UsesFirePlaceholder(CustomShopLightDonor.Torch)
              && CustomShopLightDonorTable.UsesFirePlaceholder(CustomShopLightDonor.OilLamp),
            "torch/oillamp donors use FirePlaceholder (需在首次激活前置 burnsAtNightOnly=false)");
        Check(!CustomShopLightDonorTable.UsesFirePlaceholder(CustomShopLightDonor.TorchBamboo),
            "torch_bamboo donor has no FirePlaceholder (BambooLamp 原生自门控)");
        Check(!CustomShopLightDonorTable.UsesFirePlaceholder(CustomShopLightDonor.None), "none donor has no placeholder");
        Check(CustomShopLightDonorTable.ResourcePath(CustomShopLightDonor.None) == null
              && CustomShopLightDonorTable.PrefabName(CustomShopLightDonor.None) == null,
            "none donor has no metadata");

        // 契约不变量：donor 克隆必须保持纯灯具（整店克隆会带登记副作用）。
        CheckEqual(string.Join(",", CustomShopLightDonorTable.ForbiddenCloneComponents),
            "ShopTag,PayableComponent,Persistent,CRPCStamp", "forbidden clone component set");
    }

    private static CustomShopLightPolicy AttachedPolicy(out long owner)
    {
        owner = 1001;
        var policy = new CustomShopLightPolicy("shop-test");
        var outcome = policy.TryAttach(owner, 77L, GreeceBiome, BambooBiome, GreeceBiome);
        CheckEqual(outcome, CustomShopLightAttachOutcome.AttachedNew, "fresh attach outcome");
        return policy;
    }

    private static void Cadence()
    {
        long owner;
        var policy = AttachedPolicy(out owner);

        CheckEqual(policy.Tick(10f, true), CustomShopLightTickOp.SampleWorld, "first tick after attach samples immediately");
        policy.ApplySample(10f, true);
        CheckEqual(policy.LightsOn, true, "applied sample recorded");

        CheckEqual(policy.Tick(10.10f, true), CustomShopLightTickOp.None, "tick inside interval does not sample");
        CheckEqual(policy.Tick(10.2499f, true), CustomShopLightTickOp.None, "tick just before boundary does not sample");
        CheckEqual(policy.Tick(10.25f, true), CustomShopLightTickOp.SampleWorld, "tick at exact boundary samples");
        policy.ApplySample(10.25f, true);

        // 60fps × 10s：时间轴 10.25+(i+1)/60 覆盖到 20.25，循环内采样点 10.50..20.25 每 0.25
        // 一拍（二进制精确四分位，浮点无漂移）= 40 拍；累计回写另含本组前两次手工采样。
        int requested = 0;
        for (int i = 0; i < 600; i++)
        {
            float now = 10.25f + (i + 1) / 60f;
            if (policy.Tick(now, true) == CustomShopLightTickOp.SampleWorld) { requested++; policy.ApplySample(now, false); }
        }
        CheckEqual(requested, 40, "60fps over 10s samples exactly at 0.25s cadence");
        CheckEqual(policy.SampleCount, 42, "sample count tracks applies");
        CheckEqual(policy.LightsOn, false, "last applied sample wins");
    }

    private static void PauseRetention()
    {
        long owner;
        var policy = AttachedPolicy(out owner);
        CheckEqual(policy.Tick(1f, true), CustomShopLightTickOp.SampleWorld, "initial sample before pause");
        policy.ApplySample(1f, true);

        // canReadWorld=false：不采样、不清状态、不消费节拍。
        for (int i = 0; i < 120; i++) CheckEqual(policy.Tick(1f + i / 60f, false), CustomShopLightTickOp.None, "paused tick is no-op");
        CheckEqual(policy.LightsOn, true, "pause retains current state");
        CheckEqual(policy.Tick(60f, false), CustomShopLightTickOp.None, "long pause still no sample while paused");
        CheckEqual(policy.LightsOn, true, "long pause still retains state");

        // 恢复：暂停期间未消费节拍 → 立即补采样。
        CheckEqual(policy.Tick(60f, true), CustomShopLightTickOp.SampleWorld, "resume after overdue pause samples immediately");
        policy.ApplySample(60f, false);
        CheckEqual(policy.LightsOn, false, "resumed sample applied");
        CheckEqual(policy.Tick(60.1f, true), CustomShopLightTickOp.None, "cadence re-arms after resume");
    }

    private static void SampleApplyAndFailure()
    {
        long owner;
        var policy = AttachedPolicy(out owner);
        policy.Tick(0f, true);
        policy.ApplySample(0f, true);

        // 采样失败（native Blender 调用异常路径）：保留旧状态，节拍已推进，故障有界。
        CheckEqual(policy.Tick(0.25f, true), CustomShopLightTickOp.SampleWorld, "sample due before failure path");
        policy.SampleFailed(0.25f, "sample-failed:engine-busy");
        CheckEqual(policy.LightsOn, true, "failed sample retains previous state");
        CheckEqual(policy.Tick(0.30f, true), CustomShopLightTickOp.None, "failed sample does not retry every frame");
        CheckEqual(policy.Tick(0.50f, true), CustomShopLightTickOp.SampleWorld, "next cadence slot samples again");
    }

    private static void OwnershipAndReuse()
    {
        var policy = new CustomShopLightPolicy("shop-own");
        Check(policy.TryAttach(0L, 1L, GreeceBiome, BambooBiome, GreeceBiome) == CustomShopLightAttachOutcome.Invalid,
            "null owner id rejected");

        CheckEqual(policy.TryAttach(10L, 1L, -1, BambooBiome, GreeceBiome), CustomShopLightAttachOutcome.Invalid,
            "unknown biome rejected");
        CheckEqual(policy.LastFault, "biome-unknown", "unknown biome fault text");
        Check(policy.Phase != CustomShopLightPhase.Attached, "rejected attach leaves phase unattached");

        CheckEqual(policy.TryAttach(10L, 1L, GreeceBiome, BambooBiome, GreeceBiome), CustomShopLightAttachOutcome.AttachedNew,
            "valid attach");
        Check(policy.OwnsOwner(10L) && !policy.OwnsOwner(11L), "ownership by owner id");

        CheckEqual(policy.TryAttach(10L, 1L, GreeceBiome, BambooBiome, GreeceBiome),
            CustomShopLightAttachOutcome.AlreadyAttached, "same owner+world+biome attach is idempotent");
        policy.Tick(5f, true);
        policy.ApplySample(5f, true);
        policy.TryAttach(10L, 1L, GreeceBiome, BambooBiome, GreeceBiome);
        CheckEqual(policy.Tick(5.1f, true), CustomShopLightTickOp.None,
            "idempotent re-attach does not reset cadence");
        CheckEqual(policy.LightsOn, true, "idempotent re-attach keeps state");

        CheckEqual(policy.TryAttach(10L, 1L, BambooBiome, BambooBiome, GreeceBiome),
            CustomShopLightAttachOutcome.RebuildInPlace, "biome change forces rebuild");
        CheckEqual(policy.Donor, CustomShopLightDonor.TorchBamboo, "rebuild selects new donor");
        CheckEqual(policy.Tick(5.2f, true), CustomShopLightTickOp.SampleWorld,
            "rebuild resets cadence for fresh sample");
        CheckEqual(policy.LightsOn, null, "rebuild clears stale light state");

        CheckEqual(policy.TryAttach(10L, 2L, BambooBiome, BambooBiome, GreeceBiome),
            CustomShopLightAttachOutcome.RebuildInPlace, "world change forces rebuild");

        CheckEqual(policy.TryAttach(11L, 2L, BambooBiome, BambooBiome, GreeceBiome),
            CustomShopLightAttachOutcome.RebuildInPlace, "new owner forces rebuild");
        Check(!policy.OwnsOwner(10L) && policy.OwnsOwner(11L),
            "old owner no longer owns after re-bind (延迟清理不得作用于新 owner)");
    }

    private static void DisposeSemantics()
    {
        long owner;
        var policy = AttachedPolicy(out owner);
        policy.MarkDisposed();
        CheckEqual(policy.Phase, CustomShopLightPhase.Disposed, "dispose marks phase");
        CheckEqual(policy.Tick(999f, true), CustomShopLightTickOp.None, "disposed policy never samples");
        CheckEqual(policy.TryAttach(owner, 77L, GreeceBiome, BambooBiome, GreeceBiome), CustomShopLightAttachOutcome.Invalid,
            "disposed policy refuses attach");
        CheckEqual(policy.LastFault, "attach-after-dispose", "disposed attach fault text");
        policy.MarkDisposed();
        CheckEqual(policy.Phase, CustomShopLightPhase.Disposed, "second dispose idempotent");
        Check(!policy.OwnsOwner(owner), "disposed policy owns nothing");
    }

    private static void FaultBounding()
    {
        long owner;
        var policy = AttachedPolicy(out owner);

        Check(policy.MarkFault("donor-missing:prefabs/objects/oillamp"), "first distinct fault requests one log");
        Check(!policy.MarkFault("donor-missing:prefabs/objects/oillamp"), "same fault repeated does not re-log");
        CheckEqual(policy.LastFault, "donor-missing:prefabs/objects/oillamp", "last fault text");

        Check(policy.MarkFault("fire-placeholder-missing"), "different fault logs once more");
        Check(policy.MarkFault("bamboo-lamp-missing"), "third distinct fault logs once more");
        Check(!policy.MarkFault("sample-failed:x"), "log budget exhausted at 3 per instance");
        CheckEqual(policy.LastFault, "sample-failed:x", "status still tracks newest fault after budget exhausted");

        // 成功重建 → 健康重置（故障文本清空），日志预算仍按实例生命周期封顶。
        CheckEqual(policy.TryAttach(owner, 77L, BambooBiome, BambooBiome, GreeceBiome),
            CustomShopLightAttachOutcome.RebuildInPlace, "rebuild after faults");
        CheckEqual(policy.LastFault, null, "successful rebuild clears fault text");
        Check(!policy.MarkFault("sample-failed:y"), "log budget stays consumed after health reset");

        // native 半途失败 → attach 回滚，可重试。
        var p2 = new CustomShopLightPolicy("shop-abort");
        p2.TryAttach(20L, 5L, GreeceBiome, BambooBiome, GreeceBiome);
        p2.MarkAttachAborted("donor-missing:x");
        Check(p2.Phase != CustomShopLightPhase.Attached, "aborted attach rolls back phase");
        CheckEqual(p2.Tick(1f, true), CustomShopLightTickOp.None, "aborted policy does not sample");
        CheckEqual(p2.TryAttach(20L, 5L, GreeceBiome, BambooBiome, GreeceBiome), CustomShopLightAttachOutcome.AttachedNew,
            "attach retries after abort");
    }

    private static void StatusText()
    {
        long owner;
        var policy = AttachedPolicy(out owner);
        string status = policy.Status();
        Check(status.Contains("shop-test") && status.Contains("OilLamp"), "status carries owner name and donor");
        Check(status.Contains("fault="), "status reports fault field");

        policy.Tick(1f, true);
        policy.ApplySample(1f, true);
        Check(policy.Status().Contains("lightsOn=True"), "status reports applied light state");

        policy.MarkFault("probe-fault");
        Check(policy.Status().Contains("probe-fault"), "status reports fault text");
        policy.MarkDisposed();
        Check(policy.Status().Contains("Disposed"), "status reports disposed phase");
    }
}
