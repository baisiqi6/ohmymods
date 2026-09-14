using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 弓箭手可选增强（combat 模块）：散射箭矢 + 射速倍率，默认全关、单文件独立。
///
/// 契约（本文件只依赖，不在此实现）：ModConfig.ArcherScatterEnabled(bool)、
/// ArcherVolleyCount(int，1..5 含原箭总数)、ArcherRateEnabled(bool)、
/// ArcherRateMultiplier(float，1..2) 为 ConfigEntry；ArcherOptionsScope.IsActive 决定可用世界；
/// ArcherOptionsScope.IsCurrent(Component) 判断组件属于当前 world 层/场景；
/// ModPanel.Update 每帧调用 <see cref="Tick"/>。火矢特效属于独立 visual 模块，本文件不涉及。
///
/// 原生钩子（五个，均为 operator 已核 unique 目标的既有/新增钩子点）：
/// 1. ArrowAttack.FireArrowInternal（unique 1360B）Postfix —— 原生主箭照跑之后补额外箭。
/// 2. Arrow.OnEnable（unique 528B）Prefix —— 额外箭寿命账本（池复用先结束旧租约）。
/// 3. Archer.OnEnable（unique 1040B）Prefix —— 新生命前归还本模块尚未归还的临时 cadence 回执。
/// 4. Archer.Update（unique 432B）Prefix/Postfix/Finalizer —— 计时器观测折算（不改字段常量）。
/// 5. Archer._Shoot_d__225.MoveNext（既有 Deadlands 目标，同一处新增 prefix/finalizer）——
///    调用期临时 cadence：native 每次读 shootPrepTime/两个 interval 后立即构造 Wait，本模块在
///    prefix 临时按用户倍率缩小、finalizer 归还原值，绝不跨帧常驻（因此弩手 Apply/ApplySquad 永远
///    读到原生间隔，不会把缩短值 ×2 后当新 base）。不新 hook 其它 iterator/Dispose。
/// ShouldPlayerControl（共享 16B/3 slots）只调用不 hook；NetSendVelocity（unique 240B）按原生
/// boolean payload + 冲量序列发送。
///
/// 一、散射 Scatter（ModConfig.ArcherScatterEnabled）
///   范围：只对「当前世界、存活、enabled 的原生 Archer 发出的箭」生效，且必须 HasWorldAuth
///   （主机/离线）；存活含 Damageable 存在且 !isDead（未知 fail-closed），死亡弓箭手不再新增散射，
///   不做阵营等额外限制。在线但客户端尚未 ready（IsOnline 且 !HasClientCaughtUp）时整体跳过：既不多发
///   箭也不发网络同步。客户端（无世界权威）永不多发，避免与主机同步重复。
///   生成：与原生同序 —— Pool.Spawn&lt;Arrow&gt;(原 ActiveArrowAttack._arrowPrefab, 原箭位置,
///   Quaternion.identity, world.gameLayer, true) → archer=source → 完美箭同步 PerfectShot() →
///   先登记账本凭据，再 AddForce(扇射力度) → 仅 HasWorldAuth 且 HasClientCaughtUp 时按原生顺序
///   ByteBuffer.PrepWriteBuffer(); ByteBuffer.Write(perfect); NetworkSoftSimulator.SendVelocity(...)。
///   Spawn 仍传原 prefab（resolved 只用于容量检查，避免二次 swap）。
///   随机：+/-10 度扇形、0.9–1.1 倍力度（私有 System.Random，不动 Unity 全局随机序列），
///   不复刻原生的落水随机与力度误差（额外箭不额外减员，也不重复音效）。
///   依赖：每支额外箭都在「已解析 resolved prefab」上验证实际依赖（Arrow / Rigidbody2D /
///   NetworkSoftSimulator），缺一即不 spawn（fail-closed）；resolved 只用于校验，Spawn 仍传原
///   prefab，避免二次 swap。
///   池容量与依赖（每支额外箭都查，绝不在循环开头只查一次）：按
///   BiomeData.Current.GetAssetSwapForThis&lt;GameObject&gt;(prefabGO) 得到 resolvedGO，在它上面验依赖后，
///   再取 Pool.GetPoolFromPrefabAsset，只有 capacity &lt;= 0 || pool._total &lt; capacity || cache 中确有
///   非激活实例才允许生成。actual Pool.FastSpawn 在容量用尽且 cache 为空时会把 _cycleCache 里的
///   活箭重定位（非 expendable 亦异常），所以此门为硬条件：不满足直接跳过额外箭，绝不挪/删原箭，
///   也绝不新建/注册任何池（未知 pool/世界一律 fail-closed）。
///   预算（压力下少发或不发，绝不触碰原箭）：单次原箭最多 4 支、每帧最多 8 支、每秒最多 40 支
///   （世界唯一，全局窗口等价 per-world）、同时存活租约最多 64 条。
///   账本：只登记自己生成的额外箭；绝不删除任何箭（含原箭与自己的额外箭），清理只退休标记。
///   租约只在「确证事件」时释放：显式退休（池复用 / 无刚体）、已命中(_hasHit)、已销毁、自身
///   activeSelf==false（池回收/despawn）。世界切换导致父层失活、或字段读取瞬态失败时一律**保留**
///   （保守占预算直到确证事件），因此关闭散射只停止新增、账本继续扫描 live 箭，反复开关不会绕过
///   64 alive 硬帽。不给游戏类加字段（没有 _noSplit 之类标记）：额外箭由本模块直接生成，散射只发生在
///   原生 FireArrowInternal 的 postfix 里，额外箭天然不会再触发散射。
///
/// 二、射速 Rate（ModConfig.ArcherRateEnabled，倍率 1..2）
///   （a）调用期临时 cadence：Archer._Shoot_d__225.MoveNext prefix [HarmonyPriority(Priority.Last)]
///   让既有 Deadlands interval prefix（默认 400）先临时 ×0.5，再由本模块按其结果 ÷mult；临时值
///   在 finalizer [HarmonyPriority(Priority.First)] 归还 —— 先恢复到 DL 临时值，随后 DL 自己的
///   finalizer 再归还原值。正常与异常都执行 finalizer；返回 void，绝不吞掉真实游戏异常。
///   幂等凭据：每个字段先记所有权快照再写；前缀写失败或终结器归还失败都会生成回执
///   （token + 已写字段 + 快照），Tick 有界重试直到成功/外部替换/真实销毁；同一 Archer 在有未归还
///   回执、或存在重入 lease（ActiveBorrows&gt;0）时拒绝再缩。新生命（Archer.OnEnable）前先尝试归还。
///   身份边界：借用时固定「对象引用 + native 指针 + 最初 GameObject InstanceID」，终结器与 OnEnable
///   都按这三者复核后才写。iterator 的 owner 被换成别的对象、或指针被另一个 GameObject 复用
///   （InstanceID 不同）时绝不写新对象：原对象仍存活则只归还原对象，原对象已不可达则把真实旧对象的
///   字段责任留成回执（由 Tick 验证销毁或恢复）；只有同 ID 池复用才视为同一对象继续归还。
///   下限：目标 = max(现值/mult, 0.02s) 且绝不超过现值（比原生更快者不会被拖慢），
///   _shootIntervalRange 两分量保持升序。
///   （b）Update 计时器：Prefix [Priority.First] 快照 _cooldown，Postfix [Priority.Last] 读回，
///   只有在「本帧确实发生了原生（含 DL 额外 1 份）自减」时才追加 observed*(mult-1)：
///   要求 observed &gt; 0、observed &lt;= 2.5*dt（明显大于 2dt 视为 native timer 重置/近战写入），
///   因此原生 Shoot 结束写入的冷却值（含编队 override 与 _cooldownReduction）与近战冷却绝不被
///   二次缩放，也不改 shootCooldownTime / shootCooldownWithKnightTime / playerShootCooldownTime
///   （避免平方叠加）。
///   守卫（两处一致性检查）：配置+世界范围+HasWorldAuth+timeScale&gt;0、active/enabled、当前
///   world 层/场景、_attackMode 与 _desiredAttackMode 同时为 Ranged（prefix 与 postfix 各查一次）、
///   !ShouldPlayerControl()（只调用）、inert/grabbed/dead 排除；计时器另加 !shoot.started
///   （协程运行期 _cooldown=5f 是射击哨兵，不能加速它）。
///
/// 未实机验证（operator 需在实际 interop 复核；本文件按 2.4 反编译签名书写）：
/// Pool.Spawn&lt;Arrow&gt; 泛型实例化、Pool.capacity/_total/_cache 私有字段可读性、
/// Il2CppSystem 列表索引、Archer._Shoot_d__225 与 __4__this 可达性、archer.shoot.Cast&lt;Haglet&gt;()
/// 与 Haglet.started、Archer._attackMode/_desiredAttackMode 枚举比较、Harmony 优先级排序
/// （prefix 同 DL 一样高优先先跑、finalizer Priority.First 先跑）。
/// </summary>
internal static class PatchArcher_Options
{
    // ---------- 预算/上限（全部为硬上限，压力下退化为少发/不发） ----------
    /// <summary>单次原箭最多额外箭数（VolleyCount 上限 5 含原箭）。</summary>
    internal const int MaxExtrasPerShot = 4;
    /// <summary>每秒最多额外箭数（当前世界唯一，全局窗口等价 per-world）。</summary>
    internal const int MaxExtrasPerSecond = 40;
    /// <summary>同时存活租约上限（账本容量）。</summary>
    internal const int MaxTrackedExtras = 64;
    /// <summary>每帧最多额外箭数。</summary>
    internal const int MaxExtrasPerFrame = 8;
    private const int VolleyMin = 1;
    private const int VolleyMax = 5;
    /// <summary>每次 Tick 最多重试的归还回执数（常驻开销有界）。</summary>
    private const int MaxReceiptRetriesPerTick = 4;
    private const float FanHalfAngleDegrees = 10f;
    private const float ForceJitterMin = 0.9f;
    private const float ForceJitterMax = 1.1f;
    private const float RateMin = 1f;
    private const float RateMax = 2f;
    /// <summary>临时 cadence 的小正下限：绝不低于原值（比原生更快者不被拖慢）。</summary>
    private const float TimeFloor = 0.02f;
    /// <summary>字段值等价比较的容差（判断外部第三方是否改写过）。</summary>
    private const float ValueEpsilon = 1e-5f;
    /// <summary>计时器自减量上限（2 份原生 + DL 1 份 = 最多 2dt；超出视为 native timer 重置）。</summary>
    private const float MaxObservedDrainFactor = 2.5f;
    private const float ReceiptRetrySeconds = 0.5f;

    private static readonly HashSet<string> Logged = new HashSet<string>();
    private static System.Random Rng = new System.Random();

    private static void Info(string message)
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo("[ArcherOptions] " + message); }
        catch (Exception) { }
    }

    private static void Once(string key, string message)
    {
        try
        {
            if (Logged.Add(key)) KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[ArcherOptions] " + message);
        }
        catch (Exception) { }
    }

    private static void Fail(string key, string message)
    {
        try
        {
            if (Logged.Add(key)) KingdomEnhancedPlugin.Instance?.LogSource?.LogError("[ArcherOptions] " + message);
        }
        catch (Exception) { }
    }

    // ---------- 配置/范围读取（配置未初始化或读取异常一律 fail-closed） ----------

    /// <summary>散射开关 + root 范围。</summary>
    private static bool ScatterOn()
    {
        try
        {
            var entry = ModConfig.ArcherScatterEnabled;
            return entry != null && entry.Value && ArcherOptionsScope.IsActive;
        }
        catch (Exception) { return false; }
    }

    /// <summary>射速开关 + root 范围。</summary>
    private static bool RateOn()
    {
        try
        {
            var entry = ModConfig.ArcherRateEnabled;
            return entry != null && entry.Value && ArcherOptionsScope.IsActive;
        }
        catch (Exception) { return false; }
    }

    internal static int VolleyCount()
    {
        try
        {
            var entry = ModConfig.ArcherVolleyCount;
            return Mathf.Clamp(entry != null ? entry.Value : VolleyMin, VolleyMin, VolleyMax);
        }
        catch (Exception) { return VolleyMin; }
    }

    internal static float RateMultiplier()
    {
        try
        {
            var entry = ModConfig.ArcherRateMultiplier;
            float value = entry != null ? entry.Value : RateMin;
            if (!float.IsFinite(value)) return RateMin;
            return Mathf.Clamp(value, RateMin, RateMax);
        }
        catch (Exception) { return RateMin; }
    }

    private static float Now() => Time.unscaledTime;

    // ============================================================
    // 一、散射
    // ============================================================

    /// <summary>额外箭租约：native 对象身份（指针 + InstanceID）与退休标记；只为本模块自己生成的箭建立。</summary>
    private sealed class ExtraLease
    {
        internal Arrow Arrow;
        internal IntPtr Pointer;
        internal int InstanceId;
        internal bool Retired;
    }

    private static readonly List<ExtraLease> Extras = new List<ExtraLease>(MaxTrackedExtras);

    private static int _frameStamp = -1;
    private static int _frameCount;
    private static int _windowStamp = -1;
    private static int _windowCount;
    private static int _batchLogs;

    /// <summary>
    /// 原生 <c>ArrowAttack.FireArrowInternal</c> postfix：主箭已完整生成，这里只补额外箭。
    /// 任何异常都被吞掉并只记一次日志——postfix 抛出会破坏原生射击链路，绝不允许。
    /// </summary>
    internal static void OnArrowSpawnedByAttack(ArrowAttack data, GameObject source, Vector3 arrowPosition, bool perfectShot, Vector2 shootForce)
    {
        try
        {
            if (data == null || source == null) return;
            if (!ScatterOn()) return;
            int wanted = VolleyCount() - 1;
            if (wanted <= 0) return;

            // 世界权威：客户端不多发（主机同步负责表现）；在线但客户端未 ready 时整体跳过。
            if (!NetworkBigBoss.HasWorldAuth) return;
            if (NetworkBigBoss.IsOnline && !NetworkBigBoss.HasClientCaughtUp) return;
            if (Time.timeScale <= 0f) return;

            if (!TryResolveSource(source, out Archer archer)) return;
            if (!ArcherOptionsScope.IsCurrent(archer)) return;

            Arrow prefab = data._arrowPrefab;
            Transform layer = WorldLayer();
            if (layer == null) return;

            BeginBudgets();
            int allowed = wanted;
            allowed = Mathf.Min(allowed, MaxExtrasPerShot);
            allowed = Mathf.Min(allowed, MaxExtrasPerFrame - _frameCount);
            allowed = Mathf.Min(allowed, MaxExtrasPerSecond - _windowCount);
            allowed = Mathf.Min(allowed, MaxTrackedExtras - Extras.Count);
            if (allowed <= 0)
            {
                Once("budget", "extra arrow budget exhausted; original shot untouched");
                return;
            }

            int spawned = 0;
            for (int i = 0; i < allowed; i++)
            {
                // 每支都要查：resolved prefab 的实际依赖与池容量都可能在本次齐射里变化；
                // 容量用尽且无 cache 时 actual FastSpawn 会重定位活箭，必须在这里就停手。
                if (!ExtraSpawnReady(prefab))
                {
                    Once("gate", "resolved arrow prefab not spawn-ready (deps/pool); extras skipped (no live arrow moved)");
                    break;
                }
                try
                {
                    Vector2 force = Fan(shootForce);
                    Arrow extra = Pool.Spawn<Arrow>(prefab, arrowPosition, Quaternion.identity, layer, true);
                    if (extra == null)
                    {
                        Once("pool-null", "pool returned no extra arrow; original shot untouched");
                        break;
                    }
                    // 先登记凭据再写物理/网络：之后任何异常都留下可清理的证据。
                    RegisterExtra(extra);
                    _frameCount++;
                    _windowCount++;
                    spawned++;
                    extra.archer = source;
                    if (perfectShot) extra.PerfectShot();

                    Rigidbody2D body = extra.GetComponent<Rigidbody2D>();
                    if (body == null)
                    {
                        Once("instance-rigidbody", "spawned arrow has no Rigidbody2D; lease retained and remaining extras skipped");
                        break;
                    }
                    body.AddForce(force, ForceMode2D.Impulse);

                    NetworkSoftSimulator softSim = extra.GetComponent<NetworkSoftSimulator>();
                    if (softSim != null && NetworkBigBoss.HasWorldAuth && NetworkBigBoss.HasClientCaughtUp)
                    {
                        ByteBuffer.PrepWriteBuffer();
                        ByteBuffer.Write(perfectShot);
                        softSim.SendVelocity(force, body.angularVelocity);
                    }
                }
                catch (Exception e)
                {
                    Fail("spawn", "extra arrow spawn failed: " + e);
                    break;
                }
            }

            if (spawned > 0 && _batchLogs < 3)
            {
                _batchLogs++;
                Info("scatter batch: extras=" + spawned + " of wanted=" + wanted + " volley=" + (spawned + 1));
            }
        }
        catch (Exception e)
        {
            Fail("scatter", "scatter postfix failed: " + e);
        }
    }

    /// <summary>
    /// 源必须是当前世界存活且 enabled 的原生 Archer；Damageable 缺失或已死亡一律不算 live（未知 fail-closed），
    /// 死亡弓箭手不再新增散射。类型走 interop typed 查询，不用 CLR is；不做阵营/其他限制。
    /// </summary>
    private static bool TryResolveSource(GameObject source, out Archer archer)
    {
        archer = null;
        try
        {
            if (source.activeInHierarchy == false) return false;
            if (!source.TryGetComponent<Archer>(out archer)) return false;
            if (archer == null || archer.gameObject == null) return false;
            if (!archer.enabled || !archer.gameObject.activeInHierarchy) return false;
            Damageable damageable = archer._damageable;
            if (damageable == null || damageable.isDead) return false;
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// 每支额外箭的门：先按 swap 解析出真正会被 Spawn 使用的 resolved prefab，在该 prefab 上验证实际依赖
    /// （Arrow / Rigidbody2D / NetworkSoftSimulator），再校验它的原生池容量。resolved 只用于校验，Spawn
    /// 仍传原 prefab（避免二次 swap）。任一缺失、字段不可读、未知世界一律 false（fail-closed），
    /// 绝不新建或注册池，也绝不挪/删活箭。
    /// </summary>
    internal static bool ExtraSpawnReady(Arrow prefab)
    {
        try
        {
            GameObject prefabGo = prefab != null ? prefab.gameObject : null;
            if (prefabGo == null) return false;
            BiomeData biome = BiomeData.Current;
            if (biome == null) return false;
            GameObject resolved = biome.GetAssetSwapForThis<GameObject>(prefabGo);
            if (resolved == null) return false;

            if (resolved.GetComponent<Arrow>() == null)
            {
                Once("dep-arrow", "resolved arrow prefab has no Arrow component; extras skipped");
                return false;
            }
            if (resolved.GetComponent<Rigidbody2D>() == null)
            {
                Once("dep-rigidbody", "resolved arrow prefab has no Rigidbody2D; extras skipped");
                return false;
            }
            if (resolved.GetComponent<NetworkSoftSimulator>() == null)
            {
                Once("dep-softsim", "resolved arrow prefab has no NetworkSoftSimulator; extras skipped");
                return false;
            }

            Pool pool = Pool.GetPoolFromPrefabAsset(resolved);
            if (pool == null) return false;
            int capacity = pool.capacity;
            if (capacity <= 0) return true;
            if (pool._total < capacity) return true;
            return CacheHasInactive(pool);
        }
        catch (Exception e)
        {
            Fail("dep", "resolved arrow prefab resolution failed: " + e);
            return false;
        }
    }

    /// <summary>容量已满时只允许用「非激活」的缓存实例（保守：活箭绝不允许被 FastSpawn 重定位）。</summary>
    private static bool CacheHasInactive(Pool pool)
    {
        try
        {
            Il2CppSystem.Collections.Generic.List<GameObject> cache = pool._cache;
            if (cache == null) return false;
            for (int i = 0; i < cache.Count; i++)
            {
                GameObject cached = cache[i];
                if (cached != null && !cached.activeInHierarchy) return true;
            }
            return false;
        }
        catch (Exception) { return false; }
    }

    private static Transform WorldLayer()
    {
        try
        {
            Managers managers = Managers.Inst;
            World world = managers != null ? managers.world : null;
            return world != null ? world.gameLayer : null;
        }
        catch (Exception) { return null; }
    }

    private static void BeginBudgets()
    {
        if (_frameStamp != Time.frameCount)
        {
            _frameStamp = Time.frameCount;
            _frameCount = 0;
        }
        int second = (int)Time.unscaledTime;
        if (_windowStamp != second)
        {
            _windowStamp = second;
            _windowCount = 0;
        }
    }

    /// <summary>扇射：+/-10 度、0.9–1.1 倍力度；只动方向与幅值，不使用 Unity 全局随机序列。</summary>
    private static Vector2 Fan(Vector2 force)
    {
        float degrees = (float)(Rng.NextDouble() * 2.0 - 1.0) * FanHalfAngleDegrees;
        float factor = ForceJitterMin + (float)Rng.NextDouble() * (ForceJitterMax - ForceJitterMin);
        float radians = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        return new Vector2(
            (force.x * cos - force.y * sin) * factor,
            (force.x * sin + force.y * cos) * factor);
    }

    /// <summary>登记租约：同一 native 对象只允许一条未退休租约；优先复用已退休槽位，稳态零分配。</summary>
    private static void RegisterExtra(Arrow extra)
    {
        IntPtr pointer = extra.Pointer;
        int instanceId = extra.gameObject.GetInstanceID();
        ExtraLease slot = null;
        for (int i = 0; i < Extras.Count; i++)
        {
            ExtraLease existing = Extras[i];
            if (!existing.Retired && existing.Pointer == pointer && existing.InstanceId == instanceId)
            {
                existing.Retired = true;                 // 同一对象再次出现：先结束旧租约
                slot = existing;
            }
            else if (slot == null && existing.Retired)
            {
                slot = existing;
            }
        }
        if (slot == null)
        {
            slot = new ExtraLease();
            Extras.Add(slot);
        }
        slot.Arrow = extra;
        slot.Pointer = pointer;
        slot.InstanceId = instanceId;
        slot.Retired = false;
    }

    /// <summary>OnEnable（池复用/新生命）先结束上一份租约，之后新生成才登记新租约。</summary>
    internal static void OnArrowEnable(Arrow arrow)
    {
        try
        {
            if (arrow == null || arrow.gameObject == null) return;
            if (Extras.Count == 0) return;
            IntPtr pointer = arrow.Pointer;
            int instanceId = arrow.gameObject.GetInstanceID();
            for (int i = 0; i < Extras.Count; i++)
            {
                ExtraLease lease = Extras[i];
                if (!lease.Retired && lease.Pointer == pointer && lease.InstanceId == instanceId)
                    lease.Retired = true;
            }
        }
        catch (Exception) { }
    }

    /// <summary>
    /// 退休条件只认「确证事件」：显式退休（池复用）、已命中 `_hasHit`、已销毁（Unity null）、
    /// 自身 `activeSelf == false`（池回收/despawn）。世界切换导致父层失活（activeInHierarchy 为假但
    /// 自身仍 active）不算消失，保守继续占预算；字段读取失败（瞬态）同样保留，绝不因一次读失败丢账。
    /// 只退休账本标记，绝不动 live 箭本身。
    /// </summary>
    private static bool Expired(ExtraLease lease)
    {
        if (lease.Retired) return true;
        try
        {
            Arrow arrow = lease.Arrow;
            if (arrow == null || arrow.gameObject == null) return true;   // 已销毁
            if (!arrow.gameObject.activeSelf) return true;                 // 池回收 / despawn（确证，先判以免多一次字段读）
            if (arrow._hasHit) return true;                               // 已命中
            return false;                                                 // 仍视为 live：继续占 budget
        }
        catch (Exception) { return false; }                                // 瞬态读失败：保留租约
    }

    private static void CompactExtras()
    {
        if (Extras.Count == 0) return;
        int write = 0;
        for (int i = 0; i < Extras.Count; i++)
        {
            ExtraLease lease = Extras[i];
            if (!Expired(lease)) Extras[write++] = lease;
        }
        if (write != Extras.Count) Extras.RemoveRange(write, Extras.Count - write);
    }

    // ============================================================
    // 二、射速 —— (a) 调用期临时 cadence（Archer._Shoot_d__225.MoveNext）
    // ============================================================

    /// <summary>一次 MoveNext 的临时借用记录（正常路径全部在 Harmony __state 里，零分配）。</summary>
    internal struct BorrowFields
    {
        internal bool PrepOwned;
        internal float PrepBefore;
        internal float PrepApplied;

        internal bool IntervalOwned;
        internal Vector2 IntervalBefore;
        internal Vector2 IntervalApplied;

        internal bool FormationOwned;
        internal Vector2 FormationBefore;
        internal Vector2 FormationApplied;

        internal bool OwnsAny => PrepOwned || IntervalOwned || FormationOwned;
    }

    /// <summary>Harmony __state：本次调用是否借用 + 借用的字段凭据 + 借用对象的身份（指针与 InstanceID）。</summary>
    internal struct CadenceBorrow
    {
        internal bool Entered;
        internal IntPtr ArcherPtr;
        internal int Token;
        internal Archer Archer;
        internal int ArcherInstanceId;
        internal BorrowFields Fields;
    }

    /// <summary>归还失败回执（class：仅失败路径分配；Tick/OnEnable 有界重试，绝不丢弃）。</summary>
    private sealed class CadenceReceipt
    {
        internal IntPtr ArcherPtr;
        internal int InstanceId;
        internal int Token;
        internal Archer Archer;
        internal BorrowFields Fields;
        internal float NextRetry;
    }

    private static readonly Dictionary<IntPtr, CadenceReceipt> Receipts = new Dictionary<IntPtr, CadenceReceipt>();
    private static readonly Dictionary<IntPtr, int> ActiveBorrows = new Dictionary<IntPtr, int>();
    private static int _nextToken = 1;

    /// <summary>
    /// MoveNext 前缀（Priority.Last，故 DL 的 interval prefix 已先临时 ×0.5）：按用户倍率临时缩小
    /// prep/两个 interval；留下凭据与重入门标记后才写字段；任何异常都转成回执而绝不外抛。
    /// </summary>
    internal static void OnShootMoveNextEnter(Archer._Shoot_d__225 iterator, ref CadenceBorrow state)
    {
        state = default;
        try
        {
            if (iterator == null || !GameplayActive()) return;
            float mult = RateMultiplier();
            if (mult <= RateMin) return;
            Archer archer = iterator.__4__this;
            if (!CadenceTarget(archer)) return;

            IntPtr pointer = archer.Pointer;
            // 上一笔尚未归还（回执在案）或仍有重入 lease：拒绝再缩，绝不在残留值上二次缩放。
            if (Receipts.ContainsKey(pointer)) return;
            int depth;
            if (ActiveBorrows.TryGetValue(pointer, out depth) && depth > 0) return;
            // 身份必须最早固定：无 InstanceID 就没有归还目标，宁可不借用。
            int instanceId;
            try { instanceId = archer.gameObject.GetInstanceID(); }
            catch (Exception) { return; }

            state.Entered = true;
            state.ArcherPtr = pointer;
            state.Token = _nextToken++;
            state.Archer = archer;
            state.ArcherInstanceId = instanceId;
            ActiveBorrows[pointer] = 1;

            float prepBefore = archer.shootPrepTime;
            float prepTarget = ClampDown(prepBefore, mult);
            if (!Same(prepTarget, prepBefore))
            {
                state.Fields.PrepOwned = true;                  // 先记凭据
                state.Fields.PrepBefore = prepBefore;
                state.Fields.PrepApplied = prepTarget;
                archer.shootPrepTime = prepTarget;
            }

            Vector2 intervalBefore = archer._shootIntervalRange;
            Vector2 intervalTarget = ClampDown(intervalBefore, mult);
            if (!Same(intervalTarget, intervalBefore))
            {
                state.Fields.IntervalOwned = true;
                state.Fields.IntervalBefore = intervalBefore;
                state.Fields.IntervalApplied = intervalTarget;
                archer._shootIntervalRange = intervalTarget;
            }

            Vector2 formationBefore = archer._shootIntervalRangeFormation;
            Vector2 formationTarget = ClampDown(formationBefore, mult);
            if (!Same(formationTarget, formationBefore))
            {
                state.Fields.FormationOwned = true;
                state.Fields.FormationBefore = formationBefore;
                state.Fields.FormationApplied = formationTarget;
                archer._shootIntervalRangeFormation = formationTarget;
            }

            if (!state.Fields.OwnsAny)
            {
                // 没有任何字段需要缩小：不持有 lease，也不留下重入标记。
                state.Entered = false;
                ReleaseBorrow(pointer);
            }
        }
        catch (Exception e)
        {
            // 前缀自身失败：保留已写字段的回执（Tick/OnEnable 重试），不把异常抛回原生协程。
            Fail("cadence-prefix", "temporary cadence prefix failed: " + e);
            RecordReceipt(state, "prefix");
        }
    }

    /// <summary>
    /// MoveNext 终结器（Priority.First，故先恢复到 DL 临时值，再由 DL 自己的 finalizer 归还原值）。
    /// void 返回 = 不吞掉真实异常；归还目标必须通过「指针 + 最初 InstanceID」身份复核——若 iterator
    /// 的 owner 已被换成别的对象，绝不写它，而是把真实旧对象的字段责任留成回执等 Tick 验证/恢复。
    /// </summary>
    internal static void OnShootMoveNextExit(CadenceBorrow state, Archer._Shoot_d__225 iterator)
    {
        if (!state.Entered) return;
        try
        {
            try
            {
                Archer owner = SameOwner(state.Archer, state.ArcherPtr, state.ArcherInstanceId) ? state.Archer : null;
                if (owner == null)
                {
                    // 原对象不可用：只有 iterator 当前 owner 恰好是同一对象（同指针同 ID，池复用同实例）才认它。
                    Archer current = iterator != null ? iterator.__4__this : null;
                    if (SameOwner(current, state.ArcherPtr, state.ArcherInstanceId)) owner = current;
                }

                if (owner == null)
                {
                    // 身份不符：绝不改写新的 iterator owner；真实旧对象的字段责任保留为回执。
                    if (state.Fields.OwnsAny) RecordReceipt(state, "identity");
                }
                else
                {
                    if (state.Fields.OwnsAny)
                    {
                        RestoreBorrow(owner, ref state.Fields);
                        if (state.Fields.OwnsAny) RecordReceipt(state, "return");
                    }
                }
                // 前缀失败留下的同 token 回执按最终状态同步：已归还干净就清掉，仍有残留才保留。
                SyncReceipt(state);
            }
            catch (Exception e)
            {
                Fail("cadence-return", "temporary cadence return failed: " + e);
                RecordReceipt(state, "finalizer");
            }
        }
        finally
        {
            // 无论如何都要释放重入标记；未归还的残留由回执守卫（有回执时前缀拒绝再缩）。
            ReleaseBorrow(state.ArcherPtr);
        }
    }

    /// <summary>身份复核：指针与最初捕获的 GameObject InstanceID 都必须一致（池复用同 ID 视为同一对象）。</summary>
    private static bool SameOwner(Archer archer, IntPtr pointer, int instanceId)
    {
        if (archer == null || archer.gameObject == null) return false;
        try
        {
            return archer.Pointer == pointer && archer.gameObject.GetInstanceID() == instanceId;
        }
        catch (Exception) { return false; }
    }

    /// <summary>把仍归本模块所有的字段还原为借用前的值；外部替换过的字段放弃写入权，绝不覆盖。</summary>
    internal static void RestoreBorrow(Archer archer, ref BorrowFields fields)
    {
        if (archer == null || archer.gameObject == null) return;
        if (fields.PrepOwned)
        {
            float current = archer.shootPrepTime;
            if (Same(current, fields.PrepApplied))
            {
                archer.shootPrepTime = fields.PrepBefore;
                fields.PrepOwned = false;
            }
            else
            {
                fields.PrepOwned = false;
                Once("cadence-foreign-prep", "shootPrepTime replaced externally; borrow released without overwrite");
            }
        }
        if (fields.IntervalOwned)
        {
            Vector2 current = archer._shootIntervalRange;
            if (Same(current, fields.IntervalApplied))
            {
                archer._shootIntervalRange = fields.IntervalBefore;
                fields.IntervalOwned = false;
            }
            else
            {
                fields.IntervalOwned = false;
                Once("cadence-foreign-interval", "_shootIntervalRange replaced externally; borrow released without overwrite");
            }
        }
        if (fields.FormationOwned)
        {
            Vector2 current = archer._shootIntervalRangeFormation;
            if (Same(current, fields.FormationApplied))
            {
                archer._shootIntervalRangeFormation = fields.FormationBefore;
                fields.FormationOwned = false;
            }
            else
            {
                fields.FormationOwned = false;
                Once("cadence-foreign-formation", "_shootIntervalRangeFormation replaced externally; borrow released without overwrite");
            }
        }
    }

    /// <summary>
    /// 记录/刷新归还回执：绑定**借用时的原始对象身份**（state.Archer / state.ArcherInstanceId），
    /// 绝不改成 iterator 当前 owner；保留真实快照与 token，Tick 有界重试直到成功/外部替换/真实销毁。
    /// </summary>
    private static void RecordReceipt(CadenceBorrow state, string reason)
    {
        try
        {
            if (!state.Entered || !state.Fields.OwnsAny) return;
            if (state.Archer == null || state.Archer.gameObject == null) return;   // 对象已销毁：字段随之消失
            CadenceReceipt receipt;
            if (!Receipts.TryGetValue(state.ArcherPtr, out receipt) || receipt.Token != state.Token)
            {
                receipt = new CadenceReceipt { ArcherPtr = state.ArcherPtr, Token = state.Token };
                Receipts[state.ArcherPtr] = receipt;
            }
            receipt.Archer = state.Archer;
            receipt.InstanceId = state.ArcherInstanceId;
            receipt.Fields = state.Fields;
            receipt.NextRetry = Now() + ReceiptRetrySeconds;
            Once("cadence-receipt-" + reason, "temporary cadence return deferred (" + reason + "); retrying from Tick");
        }
        catch (Exception) { }
    }

    /// <summary>按本次 borrow 的最终凭据同步同 token 回执：无残留即删除，仍有未归还字段则保留重试。</summary>
    private static void SyncReceipt(CadenceBorrow state)
    {
        try
        {
            CadenceReceipt receipt;
            if (!Receipts.TryGetValue(state.ArcherPtr, out receipt) || receipt.Token != state.Token) return;
            receipt.Fields = state.Fields;
            if (!receipt.Fields.OwnsAny) Receipts.Remove(state.ArcherPtr);
            else receipt.NextRetry = Now() + ReceiptRetrySeconds;
        }
        catch (Exception) { }
    }

    private static void ReleaseBorrow(IntPtr pointer)
    {
        try
        {
            int depth;
            if (!ActiveBorrows.TryGetValue(pointer, out depth)) return;
            depth--;
            // 归零即移除：残留由回执本身守卫（有回执时前缀拒绝再缩）。
            if (depth <= 0) ActiveBorrows.Remove(pointer);
            else ActiveBorrows[pointer] = depth;
        }
        catch (Exception) { }
    }

    /// <summary>Tick 有界重试：只处理在案回执；成功/外部替换后移除，失败保留到下一次。</summary>
    private static void RetryReceipts(float now)
    {
        if (Receipts.Count == 0) return;
        if (ScratchPointers.Count == 0)
        {
            foreach (var pair in Receipts) ScratchPointers.Add(pair.Key);
        }
        int budget = MaxReceiptRetriesPerTick;
        for (int i = ScratchPointers.Count - 1; i >= 0 && budget > 0; i--)
        {
            CadenceReceipt receipt;
            if (!Receipts.TryGetValue(ScratchPointers[i], out receipt))
            {
                ScratchPointers.RemoveAt(i);
                continue;
            }
            if (now < receipt.NextRetry) continue;
            budget--;
            try
            {
                Archer archer = receipt.Archer;
                if (archer == null || archer.gameObject == null)
                {
                    Receipts.Remove(receipt.ArcherPtr);          // 真实销毁：字段随之消失，无归还责任
                    ScratchPointers.RemoveAt(i);
                    continue;
                }
                if (archer.Pointer != receipt.ArcherPtr || archer.gameObject.GetInstanceID() != receipt.InstanceId)
                {
                    Receipts.Remove(receipt.ArcherPtr);          // 指针/身份已变：旧字段不可达
                    ScratchPointers.RemoveAt(i);
                    continue;
                }
                RestoreBorrow(archer, ref receipt.Fields);
                if (!receipt.Fields.OwnsAny)
                {
                    Receipts.Remove(receipt.ArcherPtr);
                    ScratchPointers.RemoveAt(i);
                    continue;
                }
                receipt.NextRetry = now + ReceiptRetrySeconds;
            }
            catch (Exception e)
            {
                receipt.NextRetry = now + ReceiptRetrySeconds;
                Once("cadence-retry", "temporary cadence retry failed; receipt kept: " + e);
            }
        }
    }

    private static readonly List<IntPtr> ScratchPointers = new List<IntPtr>();

    /// <summary>
    /// 新生命（Archer.OnEnable）之前先归还。身份先行：若 native 指针已被**另一个** GameObject 复用
    /// （InstanceID 不同），旧对象的字段已不存在，直接退休旧回执且绝不动新对象；只有同 ID 池复用
    /// （同一对象的新生命）才执行归还，失败也保留回执（前缀因此拒绝再缩，新生命绝不拿残留当基线）。
    /// </summary>
    internal static void OnArcherEnable(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return;
            IntPtr pointer = archer.Pointer;
            ActiveBorrows.Remove(pointer);                       // 旧协程已死：清掉重入标记
            CadenceReceipt receipt;
            if (!Receipts.TryGetValue(pointer, out receipt)) return;

            int currentId;
            try { currentId = archer.gameObject.GetInstanceID(); }
            catch (Exception) { return; }

            if (receipt.InstanceId != currentId)
            {
                // 指针被新对象复用：旧对象（连同其字段）已不存在，退休旧回执，绝不对新对象写入。
                Receipts.Remove(pointer);
                Once("cadence-reuse", "native pointer reused by another object; stale cadence receipt retired");
                return;
            }

            try
            {
                receipt.Archer = archer;
                RestoreBorrow(archer, ref receipt.Fields);
            }
            catch (Exception e)
            {
                receipt.NextRetry = Now() + ReceiptRetrySeconds;
                Once("cadence-enable", "return before new life failed; receipt kept for Tick retry: " + e);
                return;
            }
            if (!receipt.Fields.OwnsAny) Receipts.Remove(pointer);
            else receipt.NextRetry = Now() + ReceiptRetrySeconds;
        }
        catch (Exception e) { Fail("enable-restore", "restore on new life failed: " + e); }
    }

    // ============================================================
    // 三、射速 —— (b) Update 计时器观测折算
    // ============================================================

    private struct TimerProbe
    {
        internal int Frame;
        internal float Before;
    }

    private static readonly Dictionary<IntPtr, TimerProbe> TimerProbes = new Dictionary<IntPtr, TimerProbe>();

    /// <summary>Update 前缀（Priority.First，先于 DL 与本帧 native 自减）：快照 _cooldown 供 postfix 折算。</summary>
    internal static void OnArcherUpdateEnter(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return;
            if (!GameplayActive()) return;
            float mult = RateMultiplier();
            if (mult <= RateMin) return;
            if (!TimerTarget(archer)) return;
            TimerProbes[archer.Pointer] = new TimerProbe { Frame = Time.frameCount, Before = archer._cooldown };
        }
        catch (Exception e) { Fail("update-prefix", "timer snapshot failed: " + e); }
    }

    /// <summary>Update 后缀（Priority.Last）：按本帧实际自减量追加 (mult-1) 份，native 重置一律不折算。</summary>
    internal static void OnArcherUpdateExit(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return;
            IntPtr pointer = archer.Pointer;
            TimerProbe probe;
            if (!TimerProbes.TryGetValue(pointer, out probe)) return;
            TimerProbes.Remove(pointer);
            if (probe.Frame != Time.frameCount) return;          // 过期快照
            if (!GameplayActive()) return;
            float mult = RateMultiplier();
            if (mult <= RateMin) return;
            if (!TimerTarget(archer)) return;                     // 第二次守卫（含 AttackMode 复核）

            float before = probe.Before;
            float after = archer._cooldown;
            float delta = Time.deltaTime;
            if (delta <= 0f || before <= 0f) return;               // 本帧没有原生自减
            float observed = before - after;
            if (observed <= 0f) return;                            // 冷却被重置/外部写过
            if (observed > MaxObservedDrainFactor * delta) return; // 差值明显大于 2dt：native timer 重置
            archer._cooldown = after - observed * (mult - RateMin);
        }
        catch (Exception e) { Fail("update-postfix", "timer drain failed: " + e); }
    }

    /// <summary>Update 终结器：清掉本帧快照（native 体抛异常时 postfix 不跑）；void 返回不吞真实异常。</summary>
    internal static void OnArcherUpdateFailed(Archer archer, Exception exception)
    {
        try
        {
            if (archer != null && archer.gameObject != null) TimerProbes.Remove(archer.Pointer);
            if (exception != null)
                Once("update-throw", "Archer.Update threw; timer snapshot discarded, no drain applied: " + exception);
        }
        catch (Exception) { }
    }

    // ---------- 共享门控 ----------

    /// <summary>玩法门控：配置+世界范围（root scope）+ 世界权威 + 未暂停（对齐 DL GameplayActive 语义）。</summary>
    private static bool GameplayActive()
    {
        try
        {
            if (!RateOn()) return false;
            if (!NetworkBigBoss.HasWorldAuth) return false;
            return Time.timeScale > 0f;
        }
        catch (Exception) { return false; }
    }

    /// <summary>调用期 cadence 资格：当前世界存活、远程兵种、非玩家控制、非 inert/grabbed/dead。</summary>
    private static bool CadenceTarget(Archer archer)
    {
        if (archer == null || archer.gameObject == null || !archer.gameObject.activeInHierarchy) return false;
        if (!archer.enabled) return false;
        if (!Ranged(archer)) return false;
        Character character = archer._character;
        if (character == null || character.inert || character.grabbed) return false;
        Damageable damageable = archer._damageable;
        if (damageable == null || damageable.isDead) return false;
        if (PlayerControlled(archer)) return false;
        return ArcherOptionsScope.IsCurrent(archer);
    }

    /// <summary>计时器资格：cadence 资格 + 射击协程未运行（_cooldown=5f 是协程哨兵，不能加速）。</summary>
    private static bool TimerTarget(Archer archer)
    {
        if (!CadenceTarget(archer)) return false;
        return !ShootCoroutineRunning(archer);
    }

    /// <summary>当前与目标攻击模式都必须为 Ranged（近战冷却不得被加速）。</summary>
    private static bool Ranged(Archer archer)
    {
        try
        {
            return archer._attackMode == Archer.AttackMode.Ranged
                && archer._desiredAttackMode == Archer.AttackMode.Ranged;
        }
        catch (Exception) { return false; }
    }

    /// <summary>原生 ShouldPlayerControl（共享方法，只调用不 hook）；无法判定时保守视为玩家控制。</summary>
    private static bool PlayerControlled(Archer archer)
    {
        try { return archer.ShouldPlayerControl(); }
        catch (Exception) { return true; }
    }

    /// <summary>射击协程是否运行中（协程哨兵）；无法判定时保守视为运行中。</summary>
    private static bool ShootCoroutineRunning(Archer archer)
    {
        try
        {
            var shoot = archer.shoot;
            if (shoot == null) return false;
            Coatsink.Common.Haglet haglet = shoot.Cast<Coatsink.Common.Haglet>();
            return haglet != null && haglet.started;
        }
        catch (Exception) { return true; }
    }

    // ============================================================
    // 四、Tick：只做回执重试 + 预算/账本清理
    // ============================================================

    /// <summary>
    /// 面板每帧调用：重试未归还的临时 cadence 回执，并维护散射账本/预算。
    /// 关闭散射只停止新增（不再有 RetireAllExtras）：账本继续保留并扫描 live 额外箭，
    /// 直到确证命中/回收/销毁或池复用，因此反复开关不会绕过 64 alive 硬帽。
    /// </summary>
    internal static void Tick()
    {
        try
        {
            float now = Now();
            ScratchPointers.Clear();
            RetryReceipts(now);
            PruneTimerProbes();
            CompactExtras();
        }
        catch (Exception e) { Fail("tick", "tick failed: " + e); }
    }

    /// <summary>丢弃非本帧的计时器快照（postfix/finalizer 未跑到的残留），保持结构有界。</summary>
    private static void PruneTimerProbes()
    {
        if (TimerProbes.Count == 0) return;
        int frame = Time.frameCount;
        ScratchPointers.Clear();
        foreach (var pair in TimerProbes) if (pair.Value.Frame != frame) ScratchPointers.Add(pair.Key);
        for (int i = 0; i < ScratchPointers.Count; i++) TimerProbes.Remove(ScratchPointers[i]);
    }

    // ---------- 数值工具（无 lambda、无每帧分配） ----------

    private static float ClampDown(float value, float mult)
    {
        if (!float.IsFinite(value) || value <= 0f || !float.IsFinite(mult) || mult <= 0f) return value;
        float target = value / mult;
        if (!float.IsFinite(target) || target < TimeFloor) target = TimeFloor;
        if (target > value) target = value;              // 下限绝不拖慢本就更快者
        return target;
    }

    private static Vector2 ClampDown(Vector2 range, float mult)
    {
        float x = ClampDown(range.x, mult);
        float y = ClampDown(range.y, mult);
        if (y < x) y = x;                                // 保持 Random.Range 的升序语义
        return new Vector2(x, y);
    }

    private static bool Same(float a, float b)
    {
        if (a == b) return true;
        float scale = Mathf.Max(1f, Mathf.Max(Mathf.Abs(a), Mathf.Abs(b)));
        return Mathf.Abs(a - b) <= ValueEpsilon * scale;
    }

    private static bool Same(Vector2 a, Vector2 b) => Same(a.x, b.x) && Same(a.y, b.y);

    // ---------- 测试钩子（仅 internal，供 direct-linked 测试设定确定性与隔离） ----------

    /// <summary>固定私有 RNG 种子，让扇射偏移在测试中可复现；生产路径不调用。</summary>
    internal static void SeedForTests(int seed) => Rng = new System.Random(seed);

    /// <summary>清空租约/回执/预算，供测试用例之间隔离；不做任何游戏写入、不销毁任何对象。</summary>
    internal static void ResetForTests()
    {
        for (int i = 0; i < Extras.Count; i++) Extras[i].Retired = true;
        Extras.Clear();
        Receipts.Clear();
        ActiveBorrows.Clear();
        TimerProbes.Clear();
        ScratchPointers.Clear();
        Logged.Clear();
        _nextToken = 1;
        _frameStamp = -1;
        _frameCount = 0;
        _windowStamp = -1;
        _windowCount = 0;
        _batchLogs = 0;
    }
}

// ============================================================
// 钩子包装类：每个类只含对应目标的显式方法，helper 全部在 PatchArcher_Options 里，
// 避免 Harmony 按名字自动发现到不必要的候选方法。
// ============================================================

[HarmonyPatch(typeof(ArrowAttack), "FireArrowInternal")]
internal static class ArrowAttack_FireArrowInternal_Scatter_Patch
{
    [HarmonyPostfix]
    private static void Postfix(ArrowAttack __instance, GameObject source, Vector3 arrowPosition, bool isPerfectShot, Vector2 shootForce)
        => PatchArcher_Options.OnArrowSpawnedByAttack(__instance, source, arrowPosition, isPerfectShot, shootForce);
}

[HarmonyPatch(typeof(Arrow), "OnEnable")]
internal static class Arrow_OnEnable_ExtraLedger_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Arrow __instance) => PatchArcher_Options.OnArrowEnable(__instance);
}

[HarmonyPatch(typeof(Archer), "OnEnable")]
internal static class Archer_OnEnable_RateLifetime_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Archer __instance) => PatchArcher_Options.OnArcherEnable(__instance);
}

/// <summary>
/// 计时器观测：Prefix 高优先（先于 DL 与本帧 native 自减）快照，Postfix 低优先读回；
/// Finalizer 清快照（异常路径不折算，也不吞异常）。
/// </summary>
[HarmonyPatch(typeof(Archer), "Update")]
internal static class Archer_Update_RateCadence_Patch
{
    [HarmonyPriority(Priority.First)]
    [HarmonyPrefix]
    private static void Prefix(Archer __instance) => PatchArcher_Options.OnArcherUpdateEnter(__instance);

    [HarmonyPriority(Priority.Last)]
    [HarmonyPostfix]
    private static void Postfix(Archer __instance) => PatchArcher_Options.OnArcherUpdateExit(__instance);

    [HarmonyFinalizer]
    private static void Finalizer(Archer __instance, Exception __exception) => PatchArcher_Options.OnArcherUpdateFailed(__instance, __exception);
}

/// <summary>
/// 调用期临时 cadence：与既有 Deadlands interval 补丁共用同一 native 目标
/// （Archer._Shoot_d__225.MoveNext）。Prefix Priority.Last 让 DL 先临时 ×0.5；Finalizer
/// Priority.First 先恢复到 DL 临时值，再由 DL 自己的 finalizer 归还原值。
/// </summary>
[HarmonyPatch(typeof(Archer._Shoot_d__225), "MoveNext")]
internal static class Archer_ShootCoroutine_OptionsCadence_Patch
{
    [HarmonyPriority(Priority.Last)]
    [HarmonyPrefix]
    private static void Prefix(ref PatchArcher_Options.CadenceBorrow __state, Archer._Shoot_d__225 __instance)
        => PatchArcher_Options.OnShootMoveNextEnter(__instance, ref __state);

    [HarmonyPriority(Priority.First)]
    [HarmonyFinalizer]
    private static void Finalizer(PatchArcher_Options.CadenceBorrow __state, Exception __exception, Archer._Shoot_d__225 __instance)
        => PatchArcher_Options.OnShootMoveNextExit(__state, __instance);
}
