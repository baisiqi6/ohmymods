using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 北境骑士小队（norse-squad-027）：KnightStyle 随机池从四风格扩为五风格（PatchRoles_
/// KnightStyle 本体改造）后，抽中"北境/norse"的骑士：本体穿 knight_norselands 控制器；
/// 其随从在入队时转化为真北境弓箭手预制体 Archer_norselands（带 NpcShieldUser 盾牌
/// 组件 → 原生近战/盾墙逻辑：Archer.SetDesiredAttackMode 在 _npcShieldUser==null 时
/// 早退，无组件的弓箭手永远 Ranged），并程序化装盾（SetShieldEnabled(true,0)，无需
/// 掉落物）。存量北境骑士（读档/收敛）由 5s 巡检补转化+补盾。
///
/// 关键门与时序（任务书侦查实锤，直接采用）：
/// - 入队盾门：Archer.IsAvailableForJob 对 Knight 任务要求 _npcShieldUser==null ||
///   HasShield()——带盾组件无盾不能入队 → 转化后必须装盾且幂等重跑（读档后
///   Knight.Update 重新 FetchArchersForJob，无盾随从会被盾门拦出队）。
/// - 装盾时序：SetShieldEnabled 末尾 SendShieldEnabled 解引用 parentHeaderRef，
///   BeginRegisteringRPCs 之前调用会 NRE。三层兜底：转化后立即尝试（门未过=
///   零副作用 no-op）+ BeginRegisteringRPCs postfix（PatchRoles_Worker 同款先例，
///   本文件独立一份只管随从弓箭手）+ 挂 KnightStyle 5s 巡检尾部的 PatrolPass。
///   regenWait 回填 / Awake 补跑 / shield 子对象归属校验与
///   Worker.TryEquipShieldAfterRegistration 逐条对齐。
/// - 北境 prefab 获取：北境 BiomeData.swapData.prefabSwapPool 里 original==
///   Holder"Archer"基座 prefab 的条目（PatchRoles_Character.GetNorseWarriorPeasant
///   同款机制）；兜底 Resources.LoadAll 按名（Worker 先例，穷举重限频）。运行时
///   校验 tag=="Archer" 且原生带 NpcShieldUser（近战钥匙），不符拒缓存。
/// - 随从转化：Worker 交替转职同款窗口技巧——prefix 内临时替换
///   tagCharacterPairs["Archer"]=北境 prefab，调 Character.Promote("Archer") 走
///   ReplaceBy→Pool.Spawn 池化同步路径换出北境随从并 despawn 旧对象（旧对象
///   OnDisable 原生清 knight 队籍/kingdom 名册/守卫槽），finally 恢复映射；再对
///   新随从 SetKnight(该骑士) 直连（绕过 FetchArchersForJob 的距离筛选）。禁止
///   枚举 Knight._archers（Il2Cpp HashSet 枚举器不可靠，knightstyle2 实锤）——
///   巡检与 KnightStyle 一律全场 FindObjectsOfType&lt;Archer&gt; 读 _knight 反查。
/// - 翻牌治理：北境随从 ConvertToSoldier 被当前世界 swap 刷回原生士兵皮时，
///   KnightStyle 的 ConvertTo postfix 重涂机制自动对冲（第五套目标控制器
///   archer_soldier_norselands，2.4.0 资产实锤含 attack/defend/getshield/retreat
///   全套近战 clip）。
/// - 池：非北境世界没有 Archer_norselands 的池，ReplaceBy→Pool.Spawn 会崩 →
///   窗口内借 PatchRoles_Castle.EnsurePoolForCharacter("Archer")（其内部对非希腊
///   biome 无门控；syncID 走 Castle 分配器，自动跳过银行 30120-30123 与
///   GhostSquads 保留段）。Holder postfix 每世界预注册；PoolManager 重建后的
///   兜底重注册由 Operator 在 ReRegisterModPools 接线（本文件禁改 Castle），
///   转化路径内亦幂等重确保。
/// - 互斥：弩手（CrossbowmanMarker）永不入骑士队（IsAvailableForJob 排除），
///   转化 prefix 与巡检均防御跳过；死地随从弩手化包只在死地风格骑士名下，
///   与北境转化天然不冲突（风格互斥）。
/// - 失败降级：prefab 未解析/池缺失/异常 → 一律放行原生 AssignJob（北境骑士带
///   普通随从，功能降级不炸），LogError 按 key 去重。
///
/// 2.4.0 interop 签名验证（任务书实锤 + interop Assembly-CSharp.dll 元数据复核）：
/// - Archer.AssignJob(GameObject) / SetKnight(Knight) / IsAvailableForJob(GameObject)
/// - Archer._knight : Knight（私有，interop 已暴露，KnightStyle 先例）
/// - Archer._npcShieldUser : NpcShieldUser（私有，interop 已暴露；本文件判定用
///   GetComponent&lt;NpcShieldUser&gt; 等价且不依赖字段缓存时机）
/// - NpcShieldUser.HasShield() / SetShieldEnabled(bool, int=0) /
///   BeginRegisteringRPCs(CRPCHeader) : bool（Worker postfix 同签名先例）
/// - NpcShieldUser.shield/character/damageable/parentHeaderRef/shieldEnabledRpcIndex/
///   regenWait（Worker TryEquipShieldAfterRegistration 逐字段先例）
/// - Character.Promote(string, IUnitController = null) : Character（第二参显式传 null）
/// - Holder.tagCharacterPairs / GetCharacterByTag / Pool.GetPoolFromPrefabAsset
/// - BiomeHolder.Inst.biomePathStrings / NorselandsBiomeIndex /
///   BiomeData.swapData.prefabSwapPool（PatchRoles_Character 先例）
/// </summary>
public static class PatchRoles_NorseSquad
{
    private const string NorseArcherPrefabName = "Archer_norselands";
    private const float PrefabRetryIntervalSeconds = 30f; // LoadAll 兜底限频（穷举重）

    // ---- 惰性缓存 ----
    private static Character _norseArcherPrefab;
    private static float _nextLoadAllRetryAt;

    // ---- 日志限频（Reviewer Minor b）----
    // 盾会被击碎（OnPreReceiveDamage → SetShieldEnabled(false)），巡检每 5s 重装；
    // 血月夜多随从时逐次 LogInfo 会刷屏——装盾成功每世界只记一条，后续静默
    // （重装仍在发生，只是不再逐次打日志；世界切换由 Holder postfix 复位）
    private static bool _loggedShieldEquip;

    // 读档/换岛后先登记，再等原生对象归属、风格状态和同步池完成；不在
    // Archer.OnEnable 的过早调用栈内 Promote，避免对象池/CRPC 未就绪时重入。
    private sealed class LoadRestoreContext
    {
        internal readonly World World;
        internal readonly Transform SceneRoot;
        internal readonly int Generation;
        internal readonly Dictionary<int, Archer> Pending = new();
        internal readonly HashSet<int> UncertainConversions = new();
        internal int Converted;
        internal int ShieldReady;
        internal int Attempts;

        internal LoadRestoreContext(World world, int generation)
        {
            World = world;
            SceneRoot = world != null ? world.gameLayer : null;
            Generation = generation;
        }
    }

    private static int _loadRestoreGeneration;
    private static IntPtr _loadRestoreWorldPointer;
    private static IntPtr _loadRestoreSceneRootPointer;
    private const int LoadRestoreMaxAttempts = 24;
    private const float LoadRestoreRetrySeconds = 0.25f;

    // ---- 一次性日志 ----
    private static readonly HashSet<string> LoggedErrors = new();

    private static void LogInfo(string message)
    {
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[NorseSquad] " + message);
    }

    private static void LogErrorOnce(string key, string message)
    {
        if (!LoggedErrors.Add(key)) return;
        KingdomEnhancedPlugin.Instance?.LogSource.LogError("[NorseSquad] " + key + ": " + message);
    }

    // ============================================================
    // A. 北境弓箭手 prefab 解析
    // ============================================================

    /// <summary>
    /// 解析真北境弓箭手预制体（缓存 static；Resources 资产引用跨场景稳定）。
    /// 机制一：北境 BiomeData 的 prefabSwapPool 匹配 original==Holder"Archer"基座
    /// prefab（PatchRoles_Character 同款）；机制二（兜底）：LoadAll 按名（Worker
    /// 先例）。解析结果做运行时校验（tag=="Archer" + 原生 NpcShieldUser）。
    /// </summary>
    internal static Character ResolveNorseArcherPrefab(Holder holder = null)
    {
        if (_norseArcherPrefab != null) return _norseArcherPrefab;
        try
        {
            if (TryResolveFromBiomeSwapPool(holder)) return _norseArcherPrefab;

            // 机制二：穷举重，限频重试（正常首试即中；失败多为资产未加载，30s 后再试）
            if (Time.time < _nextLoadAllRetryAt) return null;
            _nextLoadAllRetryAt = Time.time + PrefabRetryIntervalSeconds;
            var allChars = Resources.LoadAll<Character>("");
            for (int i = 0; i < allChars.Length; i++)
            {
                Character candidate = allChars[i];
                if (candidate == null || candidate.gameObject == null) continue;
                if (candidate.gameObject.name == NorseArcherPrefabName)
                {
                    CacheNorseArcher(candidate, "loadall");
                    break;
                }
            }
        }
        catch (Exception e)
        {
            LogErrorOnce("prefab resolution failed", e.ToString());
        }
        return _norseArcherPrefab;
    }

    private static bool TryResolveFromBiomeSwapPool(Holder holder)
    {
        var biomePathStrings = BiomeHolder.Inst.biomePathStrings;
        if (biomePathStrings == null) return false;
        int norseIndex = BiomeHolder.NorselandsBiomeIndex;
        if (norseIndex >= biomePathStrings.Length) return false;

        string norsePath = biomePathStrings[norseIndex];
        if (string.IsNullOrEmpty(norsePath)) return false;

        BiomeData norseBiomeData = Resources.Load<BiomeData>(norsePath);
        if (norseBiomeData == null || norseBiomeData.swapData == null) return false;
        var prefabSwapPool = norseBiomeData.swapData.prefabSwapPool;
        if (prefabSwapPool == null) return false;

        if (holder == null)
        {
            var managers = Managers.Inst;
            holder = managers != null ? managers.holder : null;
        }
        if (holder == null || holder.tagCharacterPairs == null) return false;
        Character baseArcher = holder.GetCharacterByTag("Archer");
        if (baseArcher == null || baseArcher.gameObject == null) return false;

        GameObject baseArcherGO = baseArcher.gameObject;
        for (int i = 0; i < prefabSwapPool.Count; i++)
        {
            var swap = prefabSwapPool[i];
            if (swap == null || swap.original == null || swap.swap == null) continue;
            if (swap.original == baseArcherGO)
            {
                CacheNorseArcher(swap.swap.GetComponent<Character>(), "swap-pool");
                return _norseArcherPrefab != null;
            }
        }
        return false;
    }

    /// <summary>运行时校验并缓存：tag 必须=="Archer" 且原生带 NpcShieldUser（近战钥匙）。</summary>
    private static void CacheNorseArcher(Character candidate, string source)
    {
        if (candidate == null || candidate.gameObject == null) return;
        if (!candidate.gameObject.CompareTag("Archer"))
        {
            LogErrorOnce("prefab tag mismatch",
                NorseArcherPrefabName + " (via " + source + ") tag is '"
                + candidate.gameObject.tag + "', expected 'Archer'");
            return;
        }
        if (candidate.GetComponent<NpcShieldUser>() == null)
        {
            LogErrorOnce("prefab missing shield component",
                NorseArcherPrefabName + " (via " + source + ") has no NpcShieldUser; refusing to convert");
            return;
        }
        _norseArcherPrefab = candidate;
        LogInfo("resolved " + NorseArcherPrefabName + " via " + source);
    }

    // ============================================================
    // B. 北境弓箭手 sync 池
    // ============================================================

    /// <summary>
    /// 每世界初始化入口（Holder postfix 调用）：复位世界级日志限频标记
    /// （Reviewer Minor b：新世界重新获得一次装盾存在性日志）+ 预注册北境池。
    /// </summary>
    internal static void OnHolderInitialized(Holder holder)
    {
        _loggedShieldEquip = false;
        EnsureNorseArcherPool(holder);
    }

    /// <summary>
    /// 为 Archer_norselands 注册 sync 池（读档按 syncID 从池复活；ReplaceBy→
    /// Pool.Spawn 需要池）。幂等：池已存在即返回。窗口内把 Holder"Archer"映射
    /// 临时指到北境 prefab 再借 PatchRoles_Castle.EnsurePoolForCharacter("Archer")
    /// （其内部无 biome 门控；syncID 走 Castle 分配器，自动跳过银行 30120-30123
    /// 与 GhostSquads 保留段）。Holder 就绪才动，否则静默跳过。
    /// PoolManager 重建后的兜底重注册：Operator 会在 ReRegisterModPools 里加一行
    /// EnsureNorseArcherPool()（本文件禁改 PatchRoles_Castle）；本文件的 Holder
    /// postfix / 转化路径亦幂等重确保。
    /// </summary>
    internal static void EnsureNorseArcherPool(Holder holder = null)
    {
        try
        {
            var managers = Managers.Inst;
            if (managers == null || managers.pools == null) return;
            if (holder == null) holder = managers.holder;
            if (holder == null || holder.tagCharacterPairs == null) return;

            Character prefab = ResolveNorseArcherPrefab(holder);
            if (prefab == null || prefab.gameObject == null) return;

            if (Pool.GetPoolFromPrefabAsset(prefab.gameObject) != null) return; // 已注册（幂等）

            Character original;
            if (!holder.tagCharacterPairs.TryGetValue("Archer", out original) || original == null) return;

            holder.tagCharacterPairs["Archer"] = prefab;
            try
            {
                PatchRoles_Castle.EnsurePoolForCharacter("Archer");
            }
            finally
            {
                holder.tagCharacterPairs["Archer"] = original;
            }
        }
        catch (Exception e)
        {
            LogErrorOnce("pool registration failed", e.ToString());
        }
    }

    internal static IEnumerator RestoreLoadedFollowersRoutine(World world)
    {
        if (world == null || world.gameObject == null || world.gameLayer == null
            || (_loadRestoreWorldPointer == world.Pointer
                && _loadRestoreSceneRootPointer == world.gameLayer.Pointer)) yield break;
        _loadRestoreWorldPointer = world.Pointer;
        _loadRestoreSceneRootPointer = world.gameLayer.Pointer;
        var context = new LoadRestoreContext(world, ++_loadRestoreGeneration);

        // Native Campaign/Kingdom ApplyToScene and KnightStyle.OnLevelLoaded may still
        // be finishing this frame. One frame is the smallest safe barrier; retries are
        // bounded and only touch queued archers, not a per-frame global scan.
        yield return null;
        if (!IsLoadRestoreContextCurrent(context)) yield break;
        UnitScanCache.InvalidateAll();
        QueueActiveArchers(context);

        for (int attempt = 0; attempt < LoadRestoreMaxAttempts
            && IsLoadRestoreContextCurrent(context); attempt++)
        {
            context.Attempts = attempt + 1;
            if (IsLoadRestoreReady(context)) ProcessPendingLoadRestores(context);
            if (context.Pending.Count == 0) break;
            yield return new WaitForSeconds(LoadRestoreRetrySeconds);
        }

        if (IsLoadRestoreContextCurrent(context))
            LogInfo("load restore summary: converted=" + context.Converted
                + " shieldReady=" + context.ShieldReady
                + " pending=" + context.Pending.Count
                + " attempts=" + context.Attempts);
    }

    private static bool IsLoadRestoreContextCurrent(LoadRestoreContext context)
    {
        try
        {
            return context != null && context.World != null && context.World.gameObject != null
                && context.SceneRoot != null && _loadRestoreWorldPointer == context.World.Pointer
                && _loadRestoreSceneRootPointer == context.SceneRoot.Pointer
                && context.Generation == _loadRestoreGeneration;
        }
        catch { return false; }
    }

    private static bool IsLoadRestoreReady(LoadRestoreContext context)
    {
        if (!IsLoadRestoreContextCurrent(context) || !ModConfig.Enabled.Value
            || !NetworkBigBoss.HasWorldAuth) return false;
        if (NetworkBigBoss.IsOnline && !NetworkBigBoss.HasClientCaughtUp) return false;
        Managers managers = Managers.Inst;
        if (managers == null || managers.game == null || managers.kingdom == null
            || managers.holder == null || managers.pools == null
            || managers.world == null || managers.world.Pointer != context.World.Pointer
            || managers.world.gameLayer == null
            || managers.world.gameLayer.Pointer != context.SceneRoot.Pointer
            || managers.game.state != Game.State.Playing) return false;
        return true;
    }

    private static bool IsCurrentLoadArcher(Archer archer, LoadRestoreContext context)
    {
        try
        {
            return IsLoadRestoreReady(context) && archer != null && archer.gameObject != null
                && archer.gameObject.activeInHierarchy && archer.enabled
                && archer.transform != null && archer.transform.IsChildOf(context.SceneRoot);
        }
        catch { return false; }
    }

    private static bool IsCurrentLoadKnight(Knight knight, LoadRestoreContext context)
    {
        try
        {
            return IsLoadRestoreReady(context) && knight != null && knight.gameObject != null
                && knight.gameObject.activeInHierarchy && knight.enabled
                && knight.transform != null && knight.transform.IsChildOf(context.SceneRoot);
        }
        catch { return false; }
    }

    private static void QueueActiveArchers(LoadRestoreContext context)
    {
        Archer[] archers = UnitScanCache.GetArchers(0f);
        if (archers == null) return;
        for (int i = 0; i < archers.Length; i++)
        {
            Archer archer = archers[i];
            if (archer == null || archer.gameObject == null
                || !archer.gameObject.activeInHierarchy || archer.transform == null
                || context.SceneRoot == null || !archer.transform.IsChildOf(context.SceneRoot)) continue;
            context.Pending[archer.gameObject.GetInstanceID()] = archer;
        }
    }

    private static void ProcessPendingLoadRestores(LoadRestoreContext context)
    {
        if (!IsLoadRestoreReady(context) || context.Pending.Count == 0) return;
        var snapshot = new List<KeyValuePair<int, Archer>>(context.Pending);
        for (int i = 0; i < snapshot.Count; i++)
        {
            int id = snapshot[i].Key;
            Archer archer = snapshot[i].Value;
            try
            {
                if (!IsCurrentLoadArcher(archer, context))
                {
                    if (archer == null || archer.gameObject == null) context.Pending.Remove(id);
                    continue;
                }

                Knight knight = archer._knight;
                if (!IsCurrentLoadKnight(knight, context)) continue;
                if (!PatchRoles_KnightStyle.TryGetResolvedStyleIndex(knight, out int styleIndex))
                    continue; // 风格尚未就绪，保留到下一尝试
                if (styleIndex != PatchRoles_KnightStyle.NorseStyleIndex)
                {
                    context.Pending.Remove(id);
                    continue;
                }

                NpcShieldUser shieldUser = archer.GetComponent<NpcShieldUser>();
                if (shieldUser == null || !IsNorseArcherInstance(archer))
                {
                    if (!context.UncertainConversions.Add(id)) continue;
                    if (!IsLoadRestoreReady(context)) return;
                    Character replacement = ConvertToNorseArcher(archer);
                    if (replacement == null) { context.UncertainConversions.Remove(id); continue; }
                    Archer newArcher = replacement.GetComponent<Archer>();
                    if (newArcher == null) { context.Pending.Remove(id); continue; }
                    if (!IsCurrentLoadArcher(newArcher, context)
                        || !IsCurrentLoadKnight(knight, context)) return;
                    newArcher.SetKnight(knight);
                    if (!IsLoadRestoreReady(context)) return;
                    EquipShieldSafely(newArcher);
                    NpcShieldUser newShield = newArcher.GetComponent<NpcShieldUser>();
                    if (newShield != null && newShield.HasShield()) context.ShieldReady++;
                    context.Converted++;
                    context.Pending.Remove(id);
                    if (newArcher.gameObject != null
                        && newArcher.gameObject.activeInHierarchy
                        && (newShield == null || !newShield.HasShield()))
                        context.Pending[newArcher.gameObject.GetInstanceID()] = newArcher;
                    continue;
                }

                if (!IsLoadRestoreReady(context)) return;
                EquipShieldSafely(archer);
                if (shieldUser.HasShield())
                {
                    context.ShieldReady++;
                    context.Pending.Remove(id);
                }
            }
            catch (Exception e)
            {
                context.UncertainConversions.Add(id);
                LogErrorOnce("load follower restore failed", e.ToString());
            }
        }
    }

    private static bool IsNorseArcherInstance(Archer archer)
    {
        try
        {
            Character prefab = ResolveNorseArcherPrefab(Managers.Inst?.holder);
            if (prefab == null || prefab.gameObject == null || archer == null
                || archer.gameObject == null) return false;
            Pool expected = Pool.GetPoolFromPrefabAsset(prefab.gameObject);
            Pool actual = Pool.GetPoolFromPrefabInstance(archer.gameObject);
            return expected != null && actual != null && expected.Pointer == actual.Pointer;
        }
        catch { return false; }
    }

    // ============================================================
    // C. 随从转化（Worker 交替转职同款窗口技巧）
    // ============================================================

    /// <summary>
    /// 把普通随从换为真北境弓箭手：临时替换 Holder"Archer"映射 → Promote("Archer")
    /// 走 ReplaceBy→Pool.Spawn 池化同步路径（换出北境随从，拷贝位置/缩放/肤色/
    /// 钱包；despawn 旧对象——其 OnDisable 原生清 knight 队籍/kingdom 名册）→
    /// finally 恢复映射。返回新北境 Character；失败返回 null（调用方降级放行原生）。
    /// </summary>
    private static Character ConvertToNorseArcher(Archer oldArcher)
    {
        var managers = Managers.Inst;
        Holder holder = managers != null ? managers.holder : null;
        if (holder == null || holder.tagCharacterPairs == null) return null;

        Character prefab = ResolveNorseArcherPrefab(holder);
        if (prefab == null)
        {
            LogErrorOnce("prefab unresolved",
                NorseArcherPrefabName + " not resolved; follower stays native");
            return null;
        }

        Character oldCharacter = oldArcher.GetComponent<Character>();
        if (oldCharacter == null) return null;

        // 池必须先于 Promote 存在（ReplaceBy→Pool.Spawn 无池会崩）；幂等重确保
        EnsureNorseArcherPool(holder);
        if (Pool.GetPoolFromPrefabAsset(prefab.gameObject) == null)
        {
            LogErrorOnce("pool missing",
                "pool for " + NorseArcherPrefabName + " unavailable; follower stays native");
            return null;
        }

        Character original;
        if (!holder.tagCharacterPairs.TryGetValue("Archer", out original) || original == null) return null;

        holder.tagCharacterPairs["Archer"] = prefab;
        try
        {
            // Promote 内部：ReplaceBy（GetCharacterByTag("Archer") 此刻=北境 prefab，
            // 当前世界的 biome swap 查不到以它为 original 的条目 → 不再换皮）→
            // 池化生成 + 特效/音效 → Pool.Despawn(旧对象, sync)。
            // 窗口只在本次同步调用栈内替换，返回即恢复（Worker 同款约束）。
            return oldCharacter.Promote("Archer", null);
        }
        finally
        {
            holder.tagCharacterPairs["Archer"] = original;
        }
    }

    // ============================================================
    // D. 入队拦截（AssignJob prefix）
    // ============================================================

    /// <summary>
    /// AssignJob prefix 主体：Knight.FetchArchersForJob → AssignJob(骑士 gameObject)
    /// 路径上，"北境风格骑士 + 非北境随从 + 非弩手" → 窗口转化 + SetKnight 直连，
    /// 返回 false 跳过原生 AssignJob（已直连）。其余一切情况（含弩手/北境随从/
    /// 非北境骑士/客户端/异常由外层 catch 兜）放行原生。全链幂等：已是北境随从
    /// （NpcShieldUser 原生只在 Archer_norselands 上）只补盾不再转化。
    /// </summary>
    internal static bool HandleAssignJob(Archer archer, GameObject jobObject)
    {
        if (!NetworkBigBoss.HasWorldAuth) return true; // 主机驱动转化；客户端吃池同步
        if (jobObject == null || archer == null
            || archer.gameObject == null
            || !archer.gameObject.activeInHierarchy) return true;

        Knight knight = jobObject.GetComponent<Knight>();
        if (knight == null) return true; // GuardSlot 路径不碰（原生处理）
        if (!PatchRoles_KnightStyle.IsNorseStyleKnight(knight)) return true;
        // 弩手永不入队（IsAvailableForJob 排除，PatchRoles_Crossbowman 接管其战斗
        // 包），prefix 防御跳过——互斥关键
        if (PatchRoles_Crossbowman.IsCrossbowman(archer)) return true;

        NpcShieldUser shieldUser = archer.GetComponent<NpcShieldUser>();
        if (shieldUser != null)
        {
            // 已是北境随从：幂等补盾（无盾会被盾门拦下次入队）后放行原生
            // AssignJob → SetKnight。重复 prefix 到此不再转化——幂等关键。
            EquipShieldSafely(archer);
            return true;
        }

        Character replacement = ConvertToNorseArcher(archer);
        if (replacement == null) return true; // 降级：北境骑士带普通随从（不炸）

        string replacementName = replacement.gameObject != null
            ? replacement.gameObject.name : "<null>";
        if (!replacementName.Contains(NorseArcherPrefabName))
        {
            // 理论不可达（窗口保证）；只诊断不分支——新对象仍是合法随从
            LogErrorOnce("replacement name mismatch",
                "expected *" + NorseArcherPrefabName + "*, got " + replacementName);
        }

        Archer newArcher = replacement.GetComponent<Archer>();
        if (newArcher == null)
        {
            // 旧对象已被 Promote despawn，原生 AssignJob 会把死对象塞进骑士名册
            // （_archers 计数虚高），跳过原生仅记录（prefab 校验下理论不可达）
            LogErrorOnce("replacement not archer",
                NorseArcherPrefabName + " replacement lacks Archer component");
            return false;
        }

        // 直连入队（绕过 FetchArchersForJob 的距离筛选；新随从池化出生时 Awake
        // 已缓存 _npcShieldUser 并初始化 behaviour，SetKnight 安全）
        newArcher.SetKnight(knight);
        EquipShieldSafely(newArcher); // 门未过=零副作用 no-op，RPC postfix/巡检兜底
        LogInfo("converted follower to " + NorseArcherPrefabName + " for norse knight");
        return false;
    }

    // ============================================================
    // E. 程序化装盾（Worker.TryEquipShieldAfterRegistration 逐条对齐）
    // ============================================================

    /// <summary>
    /// 给北境随从弓箭手装盾（幂等，任意时机可安全调用）：已 HasShield 即返回；
    /// shield 引用必须是当前实例子对象（prefab asset 引用 fail closed，Worker 同款
    /// 判据）；Awake 提前退出的补跑（character/damageable/regenWait 回填，原生
    /// 减伤订阅恢复）；RPC 注册完成（parentHeaderRef/shieldEnabledRpcIndex）前
    /// 绝不调用 SetShieldEnabled——其末尾 SendShieldEnabled 会解引用空引用。
    /// 客户端等主机 RPC 同步，不本地装备。
    /// </summary>
    internal static void EquipShieldSafely(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return;

            NpcShieldUser shieldUser = archer.GetComponent<NpcShieldUser>();
            if (shieldUser == null || shieldUser.HasShield()) return;

            if (shieldUser.shield == null
                || shieldUser.shield.transform == null
                || !shieldUser.shield.transform.IsChildOf(archer.transform)) return;

            Character character = archer.GetComponent<Character>();
            Damageable damageable = archer.GetComponent<Damageable>();
            if (character == null || damageable == null) return;

            // Awake 可能在 HasWorldAuth 尚未就绪时提前退出（base.enabled=false 路径），
            // 网络注册完成后重跑一次原版 Awake：恢复 character/damageable 与减伤订阅
            if (shieldUser.character == null)
                shieldUser.character = character;

            if (shieldUser.damageable == null)
            {
                if (!NetworkBigBoss.HasWorldAuth || shieldUser.parentHeaderRef == null) return;
                shieldUser.enabled = true;
                shieldUser.Awake();
            }

            // SetShieldEnabled 末尾 SendShieldEnabled：BeginRegisteringRPCs 之前调用
            // 会解引用空 parentHeaderRef；门未齐一律返回（RPC postfix/巡检重试）
            if (!NetworkBigBoss.HasWorldAuth
                || shieldUser.character == null
                || shieldUser.damageable == null
                || shieldUser.parentHeaderRef == null
                || shieldUser.shieldEnabledRpcIndex < 0) return;

            if (shieldUser.regenWait == null)
                shieldUser.regenWait = new WaitForSeconds(1f);

            shieldUser.SetShieldEnabled(true, 0);
            if (!_loggedShieldEquip)
            {
                // Reviewer Minor b：每世界一条（含首次转化装盾）；此后击碎重装静默
                _loggedShieldEquip = true;
                LogInfo("norse follower shield equip active (subsequent re-equips silent)");
            }
        }
        catch (Exception e)
        {
            LogErrorOnce("shield equip failed", e.ToString());
        }
    }

    // ============================================================
    // F. 5s 巡检（挂 KnightStyle.IntegrityPass 尾部，宿主=其 World 协程）
    // ============================================================

    /// <summary>
    /// 北境小队巡检（幂等，每 5s，主机侧）：
    /// 1) 北境骑士名下的非北境随从（读档后 Knight.Update 重新 FetchArchersForJob
    ///    拉来的普通随从、或 prefix 转化瞬时失败的）→ 补转化 + SetKnight 直连；
    /// 2) 北境随从无盾（盾被打碎/读档盾门场景）→ 幂等装盾。
    /// 反向归属：全场扫 Archer 读 _knight（绝不枚举 Knight._archers，Il2Cpp
    /// HashSet 枚举器不可靠）；快照迭代内转化安全（新对象出现在下一轮快照，
    /// 旧对象 despawn 的清理由其 OnDisable 原生完成）。
    /// Reviewer Minor a：循环体 per-archer try/catch（StyleFollowersByLookup 先例）
    /// ——单个持续抛异常的随从不中断整轮，其余随从照常处理（异常按 key 去重）。
    /// Reviewer Minor b：盾击碎后每 5s 重装是常态，汇总行只随转化事件
    /// （converted>0，读档/招募收敛期短暂出现）输出；纯重装轮静默
    /// （装盾成功的存在性由 EquipShieldSafely 的每世界一条日志承载）。
    /// </summary>
    internal static void PatrolPass()
    {
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth) return;
        try
        {
            Archer[] archers = UnitScanCache.GetArchers();
            if (archers == null) return;

            int converted = 0, equipped = 0;
            for (int i = 0; i < archers.Length; i++)
            {
                try
                {
                    Archer archer = archers[i];
                    if (archer == null || archer.gameObject == null
                        || !archer.gameObject.activeInHierarchy) continue;

                    Knight knight = archer._knight;
                    if (knight == null || knight.gameObject == null) continue;
                    if (!PatchRoles_KnightStyle.IsNorseStyleKnight(knight)) continue;
                    if (PatchRoles_Crossbowman.IsCrossbowman(archer)) continue;

                    NpcShieldUser shieldUser = archer.GetComponent<NpcShieldUser>();
                    if (shieldUser == null)
                    {
                        // 非北境随从 → 补转化 + 直连（对冲读档重招募扰动）
                        Character replacement = ConvertToNorseArcher(archer);
                        if (replacement == null) continue;
                        Archer newArcher = replacement.GetComponent<Archer>();
                        if (newArcher == null) continue;
                        newArcher.SetKnight(knight);
                        EquipShieldSafely(newArcher);
                        converted++;
                        continue;
                    }

                    if (!shieldUser.HasShield())
                    {
                        EquipShieldSafely(archer);
                        if (shieldUser.HasShield()) equipped++;
                    }
                }
                catch (Exception e)
                {
                    // 单个随从失败（快照与处理之间被销毁等）不拖累其余随从
                    LogErrorOnce("patrol follower failed", e.ToString());
                }
            }

            if (converted > 0)
                LogInfo("patrol: converted=" + converted + " equipped=" + equipped);
        }
        catch (Exception e)
        {
            LogErrorOnce("patrol failed", e.ToString());
        }
    }
}

/// <summary>
/// 入队拦截宿主：Archer.AssignJob（Knight.FetchArchersForJob 的派单出口）。
/// 主体逻辑在 PatchRoles_NorseSquad.HandleAssignJob；异常一律放行原生。
/// </summary>
[HarmonyPatch(typeof(Archer), nameof(Archer.AssignJob))]
public static class Archer_AssignJob_NorseSquad_Patch
{
    [HarmonyPrefix]
    private static bool AssignJob_Prefix(Archer __instance, GameObject jobObject)
    {
        if (!ModConfig.Enabled.Value) return true;
        try
        {
            return PatchRoles_NorseSquad.HandleAssignJob(__instance, jobObject);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[NorseSquad/assign-job] " + e);
            return true; // 失败放行原生（北境骑士带普通随从不炸）
        }
    }
}

/// <summary>
/// 装盾时序兜底之一：NpcShieldUser 网络注册完成的瞬间尝试装盾（PatchRoles_Worker
/// 的同签名 postfix 只处理 Worker，本类只处理北境骑士的随从弓箭手——两个群体
/// 不相交）。注册早于 SetKnight 时 _knight 尚空 → 本层跳过，由 prefix 内的立即
/// 尝试或 5s 巡检收敛；晚于 SetKnight 则本层即时装上。
/// </summary>
[HarmonyPatch(typeof(NpcShieldUser), nameof(NpcShieldUser.BeginRegisteringRPCs))]
public static class NpcShieldUser_RPCRegistration_NorseSquad_Patch
{
    [HarmonyPostfix]
    public static void BeginRegisteringRPCs_Postfix(NpcShieldUser __instance, bool __result)
    {
        if (!ModConfig.Enabled.Value || !__result || __instance == null) return;
        try
        {
            if (!NetworkBigBoss.HasWorldAuth) return; // 客户端等主机 RPC
            Archer archer = __instance.GetComponent<Archer>();
            if (archer == null) return;
            Knight knight = archer._knight;
            if (knight == null || !PatchRoles_KnightStyle.IsNorseStyleKnight(knight)) return;
            PatchRoles_NorseSquad.EquipShieldSafely(archer);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[NorseSquad/rpc-register] " + e);
        }
    }
}

/// <summary>
/// 每世界初始化：Holder.InitializeTagCharacterPairs 之后进入 NorseSquad 的世界级
/// 入口——复位日志限频标记（Minor b）+ 预注册北境弓箭手池（Holder 就绪才动，幂等）。
/// PatchPoolFix 的 force InitPools 重建池后由 Operator 在 ReRegisterModPools 里调
/// EnsureNorseArcherPool() 兜底（本文件禁改 Castle）；转化路径（AssignJob prefix /
/// 巡检）内亦幂等重确保。
/// </summary>
[HarmonyPatch(typeof(Holder), nameof(Holder.InitializeTagCharacterPairs))]
public static class Holder_InitializeTagCharacterPairs_NorseSquadPool_Patch
{
    [HarmonyPostfix]
    public static void Postfix(Holder __instance)
    {
        if (!ModConfig.Enabled.Value || __instance == null) return;
        try
        {
            PatchRoles_NorseSquad.OnHolderInitialized(__instance);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[NorseSquad/holder-init] " + e);
        }
    }
}

/// <summary>
/// 读档/换岛恢复宿主：在原生场景应用完成后的下一帧，把已存在的北境风格骑士
/// 随从收敛到真实 Archer_norselands + 盾牌；不会等待守家雕像交互。
/// </summary>
[HarmonyPatch(typeof(World), nameof(World.OnLevelLoaded))]
public static class World_OnLevelLoaded_NorseSquadRestoreHost_Patch
{
    [HarmonyPostfix]
    private static void Postfix(World __instance)
    {
        if (!ModConfig.Enabled.Value || __instance == null) return;
        try
        {
            __instance.StartCoroutine(
                PatchRoles_NorseSquad.RestoreLoadedFollowersRoutine(__instance).WrapToIl2Cpp());
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[NorseSquad] load restore start failed: " + e);
        }
    }
}
