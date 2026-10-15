// CustomShopLighting：自定义商店的原生灯具 helper（Issue #225 lighting leaf）。
//
// 契约（integration-contract-v1.md，固定 API）：
//   internal sealed class CustomShopLighting : IDisposable
//     ctor(string ownerName); bool Attach(UnityEngine.Transform owner, int biome);
//     void Tick(bool canReadWorld); void Dispose(); string Status
//
// 职责边界（单写者原则）：
//   * 本 helper 只拥有"一盏 donor 灯具克隆"的 GameObject active 标志；由
//     Blender.ShouldLightsBeOn（native，灯自身位置）驱动，采样每实例至多 0.25s 一次。
//   * 灯的注册/退注册、火焰/光晕/强度全部保持 engine-owned：Fire.OnEnable 0x64f8e4 在
//     burnsAtNightOnly==false 时每次 OnEnable StartFire 并 RegisterNewLight，OnDisable
//     0x64f9a8 StopFire 并 DeregisterLight（ARM64 反汇编证据，见任务 lighting/ 目录）。
//     因此 SetActive(true/false) 是对称、引擎自洽的生命周期，绝不直接调用
//     LightFaker.RegisterNewLight/DeregisterLight 或 Fire 的 OnEnable/OnDisable。
//   * 契约原文 "FirePlaceholder.onlyOnAtNight" 在当前 2.4 interop 中不存在；实际字段为
//     burnsAtNightOnly（interop 实测，FirePlaceholder/Fire 均有）。在首次激活前把它置
//     false（单次写）：SwapNow 会把该值传给 Fire，Fire.Start 因此不订阅
//     Kingdom.OnDayStart/OnDayEnd，日夜控制只剩本 helper 一个所有者，避免双控制器。
//   * 竹林 donor（Torch_Bamboo）没有 FirePlaceholder；首次激活前要求恰一个现有
//     AutoLightRegister，并将克隆的 nightLightOnly=false 后读回确认，防止其日夜回调
//     另写 Light.enabled。BambooLamp 仍原生自查 ShouldLightsBeOn 并管理光晕/子根，
//     AutoLightRegister 保留原生注册/退注册，本 helper 只控制灯具根 active。
//   * donor 为独立原生灯具 prefab（prefabs/objects/torch、prefabs/objects/oillamp、
//     prefabs/buildings and interactive/torch_bamboo，审计核定的 ResourceManager 真实
//     条目），不带 ShopTag/Payable/Persistent/CRPC——克隆后逐项校验，命中即销毁并拒绝。
//     不克隆整店、不做场景/资源全量扫描、不改全局 ambient/材质、不新建资产。
//   * Attach 前置条件（Root 责任）：owner 为未激活的自有店根、资产材质校验已过。
//     本 helper 强制校验 inactive，违规 fail-closed 返回 false（防止 FirePlaceholder.Start
//     或 AutoLightRegister.OnEnable 在对应日夜策略写入前触发）。
//   * pause：Tick(false) 完全 no-op（保持当前亮灯状态）；恢复后到期立即补采样。
//   * Dispose：销毁自己创建的 prop（anti-rootreuse：按创建时实例 ID + 仍在本 owner
//     之下判定，绝不误毁新 owner 的灯）；幂等；世界/清退各分支由 Root 调用。
//   * 故障有界：每个不同故障文本至多记一条日志，每实例日志预算 3 条；Status 始终反映
//     最新故障；不做每帧日志、不做无限重试（失败后 Root 可再次 Attach，fire 模板缺失等
//     依赖问题按 0.25s 节拍之外的重试由 Root 决定）。
//   * 无 Harmony 钩子、无 Main/注册改动、无网络/存档/池副作用；native Fire/Light 注册
//     占用（LightFaker 100 槽）由引擎生命周期自然增减，运行时占用观察另行门控验收。
//
// 纯策略（本文件上半部分，无 Unity/interop 依赖）可用 CUSTOM_SHOP_LIGHTING_CORE_ONLY
// 单独编译进 tests/custom-shop-lighting；native 封装类包在 #if !CORE_ONLY 内。
// 时间源：Tick 的节拍用 Time.time（native）；纯策略以注入的 float now 计算，测试确定性。

#if !CUSTOM_SHOP_LIGHTING_CORE_ONLY
using System;
using UnityEngine;
#endif

namespace KingdomEnhancedMod;

/// <summary>原生 donor 灯具种类；与 resources.assets 审计条目一一对应。</summary>
internal enum CustomShopLightDonor : byte
{
    None = 0,
    Torch = 1,       // prefabs/objects/torch（基础/其他 biome，BiomeSpriteSwapper 原生适配）
    OilLamp = 2,     // prefabs/objects/oillamp（希腊）
    TorchBamboo = 3  // prefabs/buildings and interactive/torch_bamboo（竹林）
}

/// <summary>Attach 结果：Invalid=拒绝；AttachedNew=新建；AlreadyAttached=幂等命中；RebuildInPlace=先拆旧再建新。</summary>
internal enum CustomShopLightAttachOutcome : byte
{
    Invalid = 0,
    AttachedNew = 1,
    AlreadyAttached = 2,
    RebuildInPlace = 3
}

/// <summary>Tick 返回的当帧动作；SampleWorld 表示 native 层应在本帧做一次 ShouldLightsBeOn 采样。</summary>
internal enum CustomShopLightTickOp : byte
{
    None = 0,
    SampleWorld = 1
}

/// <summary>实例生命周期相位；故障文本独立跟踪（见 <see cref="CustomShopLightPolicy.LastFault"/>）。</summary>
internal enum CustomShopLightPhase : byte
{
    Created = 0,
    Attached = 1,
    Disposed = 2
}

/// <summary>donor 元数据表（审计核定字面量；不要凭旧源码改写）。</summary>
internal static class CustomShopLightDonorTable
{
    internal const string TorchResourcePath = "prefabs/objects/torch";
    internal const string TorchPrefabName = "Torch";
    internal const string OilLampResourcePath = "prefabs/objects/oillamp";
    internal const string OilLampPrefabName = "OilLamp";
    internal const string TorchBambooResourcePath = "prefabs/buildings and interactive/torch_bamboo";
    internal const string TorchBambooPrefabName = "Torch_Bamboo";

    /// <summary>donor 克隆必须保持纯灯具；出现任一组件视为错误克隆，销毁并拒绝（契约：no ShopTag/Payable/Persistent/CRPC clone）。</summary>
    internal static readonly string[] ForbiddenCloneComponents = { "ShopTag", "PayableComponent", "Persistent", "CRPCStamp" };

    /// <summary>biome → donor。负数编号（BiomeHolder 未就绪语义）→ None；希腊→OilLamp；竹林→Torch_Bamboo；其余正数→Torch。</summary>
    internal static CustomShopLightDonor Select(int biome, int bambooBiomeIndex, int greeceBiomeIndex)
    {
        if (biome < 0) return CustomShopLightDonor.None;
        if (greeceBiomeIndex >= 0 && biome == greeceBiomeIndex) return CustomShopLightDonor.OilLamp;
        if (bambooBiomeIndex >= 0 && biome == bambooBiomeIndex) return CustomShopLightDonor.TorchBamboo;
        return CustomShopLightDonor.Torch;
    }

    internal static string ResourcePath(CustomShopLightDonor donor)
    {
        switch (donor)
        {
            case CustomShopLightDonor.Torch: return TorchResourcePath;
            case CustomShopLightDonor.OilLamp: return OilLampResourcePath;
            case CustomShopLightDonor.TorchBamboo: return TorchBambooResourcePath;
            default: return null;
        }
    }

    internal static string PrefabName(CustomShopLightDonor donor)
    {
        switch (donor)
        {
            case CustomShopLightDonor.Torch: return TorchPrefabName;
            case CustomShopLightDonor.OilLamp: return OilLampPrefabName;
            case CustomShopLightDonor.TorchBamboo: return TorchBambooPrefabName;
            default: return null;
        }
    }

    /// <summary>Torch/OilLamp donor 经 FirePlaceholder.SwapNow 换成 Fire；需在首次激活前置 burnsAtNightOnly=false。</summary>
    internal static bool UsesFirePlaceholder(CustomShopLightDonor donor)
        => donor == CustomShopLightDonor.Torch || donor == CustomShopLightDonor.OilLamp;

    /// <summary>Torch_Bamboo donor 依赖 BambooLamp 组件（原生自查 ShouldLightsBeOn + 淡入淡出协程）。</summary>
    internal static bool UsesBambooLamp(CustomShopLightDonor donor) => donor == CustomShopLightDonor.TorchBamboo;
}

/// <summary>
/// 每实例纯策略：0.25s 采样节拍（请求时推进，保证每实例至多 4Hz，无论 native 是否回写）、
/// owner+world+biome 归属与重建判定、故障有界记账、状态文本。无任何 I/O。
/// </summary>
internal sealed class CustomShopLightPolicy
{
    internal const float SampleIntervalSeconds = 0.25f;
    internal const int MaxFaultLogs = 3;

    private float _nextSampleAt;
    private int _faultLogCount;

    internal CustomShopLightPolicy(string ownerName)
    {
        OwnerName = ownerName ?? "";
        _nextSampleAt = float.NegativeInfinity;
    }

    internal string OwnerName { get; }
    internal CustomShopLightPhase Phase { get; private set; }
    internal CustomShopLightDonor Donor { get; private set; }
    internal long Owner { get; private set; }        // Transform.Pointer（稳定身份，不受 wrapper 实例影响）
    internal long WorldId { get; private set; }      // Managers.Inst.world.Pointer
    internal int Biome { get; private set; }
    internal bool? LightsOn { get; private set; }    // 最近一次成功采样的期望 active
    internal int SampleCount { get; private set; }
    internal string LastFault { get; private set; }

    internal string ResourcePath => CustomShopLightDonorTable.ResourcePath(Donor);
    internal string PrefabName => CustomShopLightDonorTable.PrefabName(Donor);
    internal bool UsesFirePlaceholder => CustomShopLightDonorTable.UsesFirePlaceholder(Donor);
    internal bool UsesBambooLamp => CustomShopLightDonorTable.UsesBambooLamp(Donor);

    /// <summary>
    /// 归属判定：owner==0 表示空身份。同 owner+同 world+同 biome → AlreadyAttached（幂等，
    /// 不重置节拍/状态）；任一变化 → RebuildInPlace（native 先拆旧 prop 再建新）。
    /// 负数 biome（BiomeHolder 未就绪）拒绝；disposed 后拒绝。
    /// </summary>
    internal CustomShopLightAttachOutcome TryAttach(long owner, long worldId, int biome, int bambooBiomeIndex, int greeceBiomeIndex)
    {
        if (Phase == CustomShopLightPhase.Disposed)
        {
            MarkFault("attach-after-dispose");
            return CustomShopLightAttachOutcome.Invalid;
        }
        if (owner == 0L)
        {
            MarkFault("owner-null");
            return CustomShopLightAttachOutcome.Invalid;
        }
        var donor = CustomShopLightDonorTable.Select(biome, bambooBiomeIndex, greeceBiomeIndex);
        if (donor == CustomShopLightDonor.None)
        {
            MarkFault("biome-unknown");
            return CustomShopLightAttachOutcome.Invalid;
        }
        if (Phase == CustomShopLightPhase.Attached && Owner == owner && WorldId == worldId && Biome == biome)
            return CustomShopLightAttachOutcome.AlreadyAttached;

        Owner = owner;
        WorldId = worldId;
        Biome = biome;
        Donor = donor;
        bool firstAttach = Phase != CustomShopLightPhase.Attached;
        Phase = CustomShopLightPhase.Attached;
        ResetLifecycleState();   // 新周期：节拍重置（立即采样）、清状态、清故障文本（日志预算保留）
        return firstAttach ? CustomShopLightAttachOutcome.AttachedNew : CustomShopLightAttachOutcome.RebuildInPlace;
    }

    private void ResetLifecycleState()
    {
        _nextSampleAt = float.NegativeInfinity;
        LightsOn = null;
        LastFault = null;
    }

    /// <summary>
    /// native 半途失败（donor 缺失/占位符缺失/禁组件等）→ 回滚为未挂接，允许 Root 之后重试。
    /// 旧 prop（若为重建路径）已由 native 拆除，回滚后状态自洽（Attached=false，无灯）。
    /// </summary>
    internal void MarkAttachAborted(string fault)
    {
        Phase = CustomShopLightPhase.Created;
        Owner = 0L;
        WorldId = 0L;
        Biome = -1;
        Donor = CustomShopLightDonor.None;
        ResetLifecycleState();
        MarkFault(fault);
    }

    /// <summary>
    /// 当帧动作。请求时即推进节拍（而非回写时），保证无论 native 后续如何，每 0.25s 至多
    /// 返回一次 SampleWorld。canReadWorld=false（pause）完全不触达节拍：保持当前状态，
    /// 恢复后若已到期立即补采样。
    /// </summary>
    internal CustomShopLightTickOp Tick(float now, bool canReadWorld)
    {
        if (Phase != CustomShopLightPhase.Attached || !canReadWorld) return CustomShopLightTickOp.None;
        if (!(now >= _nextSampleAt)) return CustomShopLightTickOp.None;
        _nextSampleAt = now + SampleIntervalSeconds;
        return CustomShopLightTickOp.SampleWorld;
    }

    /// <summary>native 采样成功回写：记录期望 active。节拍已在请求时推进，此处不动调度。</summary>
    internal void ApplySample(float now, bool lightsOn)
    {
        LightsOn = lightsOn;
        SampleCount++;
    }

    /// <summary>native 采样异常：保留旧状态（pause 语义的失败变体），节拍已推进，故障有界记录。</summary>
    internal void SampleFailed(float now, string fault)
    {
        MarkFault(fault);
    }

    internal void MarkDisposed()
    {
        Phase = CustomShopLightPhase.Disposed;
    }

    /// <summary>
    /// 有界故障记账：同一文本只请求一次日志；不同文本各一次；每实例共 MaxFaultLogs 条。
    /// 返回 true 表示 native 应当记录这一条。预算用尽后 LastFault 仍持续更新（Status 可见）。
    /// </summary>
    internal bool MarkFault(string fault)
    {
        if (string.IsNullOrEmpty(fault)) return false;
        if (fault == LastFault) return false;
        LastFault = fault;
        if (_faultLogCount >= MaxFaultLogs) return false;
        _faultLogCount++;
        return true;
    }

    /// <summary>归属判定（anti-rootreuse：延迟清理只允许作用于当前 owner 的 prop）。</summary>
    internal bool OwnsOwner(long owner) => Phase == CustomShopLightPhase.Attached && Owner == owner && owner != 0L;

    internal string Status()
    {
        return "phase=" + Phase
            + " owner=" + OwnerName
            + " donor=" + Donor
            + " lightsOn=" + (LightsOn.HasValue ? LightsOn.Value.ToString() : "n/a")
            + " samples=" + SampleCount
            + " fault=" + (LastFault ?? "none");
    }
}

#if !CUSTOM_SHOP_LIGHTING_CORE_ONLY
/// <summary>
/// native 封装（契约固定 API）。线程约定：Attach/Tick/Dispose 均由 Root 在 Unity 主线程
/// 的既有商店 cadence 中调用；本类不自建 Update/协程/钩子。
/// </summary>
internal sealed class CustomShopLighting : IDisposable
{
    private readonly CustomShopLightPolicy _policy;
    private GameObject _prop;
    private int _propInstanceId;   // 创建时 GetInstanceID；Dispose 仅销毁仍匹配且仍在本 owner 下的实例
    private Transform _owner;
    private readonly System.Collections.Generic.HashSet<string> _loggedFaults = new(StringComparer.Ordinal);

    internal CustomShopLighting(string ownerName)
    {
        _policy = new CustomShopLightPolicy(ownerName);
    }

    internal string Status => _policy.Status();

    /// <summary>
    /// 在未激活的自有店根下挂接 donor 灯具克隆。前置：Root 已完成资产/材质校验、owner 根
    /// 尚未激活（本方法强制校验，违规 fail-closed）。AlreadyAttached → 幂等 true；
    /// Invalid → false；AttachedNew/RebuildInPlace → 先拆旧 prop 再建新。
    /// </summary>
    internal bool Attach(Transform owner, int biome)
    {
        try
        {
            if (owner == null || owner.gameObject == null)
            {
                Fault("owner-null");
                return false;
            }
            if (owner.gameObject.activeInHierarchy)
            {
                // FirePlaceholder.Start/AutoLightRegister.OnEnable 在激活态触发会先于
                // 日夜策略写入，产生原生第二控制器。Root 必须先于激活调用本方法。
                Fault("attach-owner-active");
                return false;
            }

            long ownerId;
            try { ownerId = owner.Pointer.ToInt64(); }
            catch (Exception e) { Fault("owner-id-failed:" + e.Message); return false; }
            long worldId = 0L;
            try
            {
                var managers = Managers.Inst;
                var world = managers != null ? managers.world : null;
                if (world != null) worldId = world.Pointer.ToInt64();
            }
            catch { /* world 未就绪按 0 记，不影响挂接正确性；变化时触发重建 */ }

            int bambooBiome = -1, greeceBiome = -1;
            try { bambooBiome = BiomeHolder.BambooBiomeIndex; greeceBiome = BiomeHolder.GreeceBiomeIndex; }
            catch { /* 常量读取失败按 -1：Select 对负索引安全降级为 Torch/None */ }

            var outcome = _policy.TryAttach(ownerId, worldId, biome, bambooBiome, greeceBiome);
            if (outcome == CustomShopLightAttachOutcome.AlreadyAttached)
            {
                if (_prop == null)
                {
                    _policy.MarkAttachAborted("attach-state-mismatch");
                    return false;
                }
                return true;
            }
            if (outcome == CustomShopLightAttachOutcome.Invalid)
            {
                LogFault();
                return false;
            }

            // RebuildInPlace / AttachedNew：先拆旧（anti-rootreuse：只拆自己创建、仍归本 owner 的）。
            DestroyProp();

            var path = _policy.ResourcePath;
            var expectedName = _policy.PrefabName;
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null || prefab.name != expectedName)
            {
                Abort("donor-missing:" + path);
                return false;
            }

            if (_policy.UsesFirePlaceholder)
            {
                // SwapNow 从 Managers/Holder 的 firePrefab 链取 Fire；模板未就绪则首次激活必失败。
                var managers = Managers.Inst;
                var holder = managers != null ? managers.holder : null;
                if (holder == null || holder.firePrefab == null)
                {
                    Abort("fire-template-missing");
                    return false;
                }
            }

            var prop = UnityEngine.Object.Instantiate(prefab, owner, false);
            if (prop == null)
            {
                Abort("instantiate-failed:" + path);
                return false;
            }
            // Take cleanup responsibility immediately, while the owner is inactive.
            _prop = prop; _owner = owner; _propInstanceId = prop.GetInstanceID();
            // Independent donor roots carry their own ground offset; a shop root
            // already sits on that ground line. Preserve only the donor's children.
            prop.transform.localPosition = new Vector3(0f, 0f, 0f);

            if (HasForbiddenComponent(prop))
            {
                Abort("forbidden-component");
                return false;
            }

            if (_policy.UsesFirePlaceholder)
            {
                var placeholder = prop.GetComponentInChildren<FirePlaceholder>(true);
                if (placeholder == null)
                {
                    Abort("fire-placeholder-missing");
                    return false;
                }
                // 契约 "onlyOnAtNight=false" 的实际字段（interop 实测）。首次激活前单次写：
                // SwapNow 把它传给 Fire，Fire.Start 不订阅日夜事件 → 单一 ambient 控制器（本 helper）。
                placeholder.burnsAtNightOnly = false;
            }
            else if (_policy.UsesBambooLamp)
            {
                var lamp = prop.GetComponentInChildren<BambooLamp>(true);
                if (lamp == null)
                {
                    Abort("bamboo-lamp-missing");
                    return false;
                }
                var registers = prop.GetComponentsInChildren<AutoLightRegister>(true);
                if (registers == null || registers.Length != 1)
                {
                    Abort("bamboo-register-count-mismatch");
                    return false;
                }
                // 当前 donor 审计恰一个注册器。只配置自有 inactive 克隆，首次 OnEnable/Start
                // 前关闭日夜写者；注册/退注册与 BambooLamp 光晕/子根仍交给原生生命周期。
                foreach (var register in registers)
                {
                    register.nightLightOnly = false;
                    if (register.nightLightOnly)
                    {
                        Abort("bamboo-night-policy-write-unconfirmed");
                        return false;
                    }
                }
            }

            try { prop.name = "KEM_ShopLight_" + _policy.OwnerName; }
            catch { /* 命名仅用于场景识别，失败不阻断 */ }

            // active 标志归本 helper 所有：初始显式关闭，首个采样再按 ShouldLightsBeOn 打开，
            // 避免 Root 激活店根瞬间出现未经采样的亮灯帧。
            prop.SetActive(false);
            return true;
        }
        catch (Exception e)
        {
            Abort("attach-exception:" + e.Message);
            return false;
        }
    }

    /// <summary>Root 既有商店 cadence 每帧调用；内部至多 0.25s 一次 native 采样。</summary>
    internal void Tick(bool canReadWorld)
    {
        try
        {
            if (_policy.Phase != CustomShopLightPhase.Attached || _prop == null) return;
            if (_prop.GetInstanceID() != _propInstanceId || _prop.transform.parent != _owner)
            {
                // Another owner adopted/replaced this prop; no subsequent sample or
                // active write may follow it into that owner's hierarchy.
                _prop = null; _propInstanceId = 0; _owner = null;
                _policy.MarkAttachAborted("light-owner-changed"); LogFault(); return;
            }
            if (_owner == null)
            {
                // owner 根已被销毁而 Root 未 Dispose：有界可见，不重建、不逐帧日志。
                Fault("owner-lost");
                return;
            }
            var op = _policy.Tick(Time.time, canReadWorld);
            if (op != CustomShopLightTickOp.SampleWorld) return;

            bool lightsOn;
            try { lightsOn = Blender.ShouldLightsBeOn(_prop.transform.position); }
            catch (Exception e)
            {
                _policy.SampleFailed(Time.time, "sample-failed:" + e.Message);
                LogFault();
                return;
            }
            _policy.ApplySample(Time.time, lightsOn);
            if (_prop != null && _prop.activeSelf != lightsOn) _prop.SetActive(lightsOn);
        }
        catch (Exception e)
        {
            Fault("tick-exception:" + e.Message);
        }
    }

    /// <summary>清退：销毁自己创建、仍归本 owner 的 prop；幂等；绝不触碰新 owner 的灯。</summary>
    public void Dispose()
    {
        try
        {
            DestroyProp();
            _policy.MarkDisposed();
        }
        catch (Exception e)
        {
            Fault("dispose-exception:" + e.Message);
            throw; // Root keeps teardown responsibility and retries without activating a new owner.
        }
    }

    private void DestroyProp()
    {
        var prop = _prop;
        if (prop != null)
        {
            if (_propInstanceId == 0) _propInstanceId = prop.GetInstanceID();
            if (prop.GetInstanceID() == _propInstanceId && prop.transform.parent == _owner)
            {
                prop.SetActive(false); // native light deregisters before deferred Destroy
                UnityEngine.Object.Destroy(prop);
            }
        }
        _prop = null; _propInstanceId = 0; _owner = null;
    }

    private static bool HasForbiddenComponent(GameObject prop)
    {
        try
        {
            if (prop.GetComponentInChildren<ShopTag>(true) != null) return true;
            if (prop.GetComponentInChildren<PayableComponent>(true) != null) return true;
            if (prop.GetComponentInChildren<Persistent>(true) != null) return true;
            if (prop.GetComponentInChildren<CRPCStamp>(true) != null) return true;
        }
        catch { return true; }   // 校验本身失败按"不可证明纯灯具"处理
        return false;
    }

    private void Abort(string fault)
    {
        DestroyProp();
        _policy.MarkAttachAborted(fault);
        LogFault();
    }

    private void Fault(string fault)
    {
        if (_policy.MarkFault(fault)) LogFault();
    }

    private void LogFault()
    {
        if (_loggedFaults.Count >= CustomShopLightPolicy.MaxFaultLogs
            || string.IsNullOrEmpty(_policy.LastFault) || !_loggedFaults.Add(_policy.LastFault)) return;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[CustomShopLighting] " + _policy.OwnerName + ": " + _policy.LastFault);
        }
        catch { }
    }
}
#endif
