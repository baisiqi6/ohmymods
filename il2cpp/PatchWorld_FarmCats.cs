using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 农舍猫（北境猫移植，Mono 线 Patch_Kingdom.SpawnCatsInGreece 的 IL2CPP 迁移）。
///
/// 语义（照搬 Mono 版）：仅希腊 biome，把北境猫 prefab 补进每个 Farmhouse——
/// 每农舍驯化猫补齐到 3 只（位置农舍 ±4、y+0.5）；驯化 = domesticated=true +
/// farmHouse 绑定 + 白色（Mono 版 SetFromSavedState 兜底先例用 Color.white）。
///
/// IL2CPP 化要点：
/// - **坑26 纪律（不枚举原生集合）**：Mono 版 foreach kingdom.cats 统计每农舍
///   现有猫；kingdom.cats 运行时类型是 Il2CppSystem.Collections.Generic.List&lt;Cat&gt;
///   （interop dump 实证），原生集合枚举器路径运行时不可靠（坑26：knight._archers
///   两轮实锤 MoveNext 本身抛）。改为**反查归属**：FindObjectsOfType&lt;Cat&gt;() 全场扫
///   + 读 cat.farmHouse 回引字段（interop 私有字段直读可靠，坑25 同族）匹配农舍
///   计数。语义等价：kingdom.cats 由 Cat.OnEnable/AddCat、OnDisable/RemoveCat
///   维护，本来就只含 active 猫，与 FindObjectsOfType（不含 inactive）口径一致。
/// - **反射全部换 interop 直访**（interop dump 实证全部存在）：
///   domesticated 为"公有 get + 私有 set"自动属性，interop 代理把私有 setter 暴露
///   为公有属性 setter（native set_domesticated 方法指针在），直接赋值——不需要
///   Mono 版的 backing-field 反射，也不需要 Cat.CatSaveStatusData+SetFromSavedState
///   兜底（该路径保留为备注：若未来 interop 丢弃该 setter，可改走
///   SetFromSavedState（public，参数 CatSaveStatusData 为 struct 值传递）
///   一次性写 domesticated+color）；
///   farmHouse 私有字段 interop 暴露为可读写属性，直读（计数）/直写（绑定）；
///   颜色用 public SetColor(Color)。
/// - **宿主**：World.OnLevelLoaded postfix 延迟协程（PatchWorld_TowerSpots 同款
///   一次性范式）：per-world+gameLayer 指针守卫，且守卫在全部就绪检查通过之后、
///   实际放猫之前才消费（瞬时未就绪只跳过本次，不永久吞掉该世界）。
/// - **幂等**：每农舍按"现存驯化且绑定该农舍"的猫数补齐（上限 3），重放安全。
/// - **联机 fail-closed**（TowerSpots 同款纪律）：NetworkBigBoss.IsOnline 整体
///   跳过——本补丁只有权威端 Instantiate+注册，没有让对端生成同款猫的 RPC 通道，
///   联机会分叉（对端看不到、自己跑出未注册副本）。单机/同机分屏
///   （IsOnline=false）语义完整。
///
/// prefab 解析与生成方式（2026-08-30 返修，实机日志实锤后对齐原生）：
/// - **prefab 主路径 = Holder.catPrefab**（Holder 公开字段）。初版走北境 BiomeData
///   的 uniqueCharacters/prefabSwapPool 双机制（照搬 Mono 线），实机日志实锤
///   "[FarmCats] norse cat prefab not resolved"——2.4.0 资产里两处均解析不到猫。
///   原生消费者正规路径：BombablePortal / GreedBuybackTrader 赎回被偷猫均直接
///   `Pool.Spawn<OrInstantiate><Cat>(Managers.Inst.holder.catPrefab, ...)` 且不做
///   biome swap——猫只有一个变体（_norselands 动画），Holder 字段就是它。
///   原 BiomeData 双机制降为兜底（防御性保留，正常永不应命中）。
/// - **生成 = Pool.SpawnOrInstantiate&lt;Cat&gt;(catPrefab, pos, rot, gameLayer)**
///   （GreedBuybackTrader 原生逐字同款）：池化取出/扩容、NetID 注册
///   （CRPCType.Dynamic，AttemptSpawnSync 原生一体）、激活（activateAfterRegistration）、
///   _activeCache 登记全部由原生处理；无池时 SpawnGO(allowInstantiate:true) 原生
///   回退 Instantiate。初版的 Object.Instantiate + 手动 RegisterObject(Dynamic)
///   已删（手动注册仅在"无池 Instantiate"分支才与原生等价，池化路径反而会与
///   FastSpawn 内建注册重复依赖查重保护）。SpawnGO 会先对 prefab 做
///   GetAssetSwapForThis（当前希腊 biome swap）：希腊 swap 表无猫条目（猫唯一
///   变体），GetPrefabSwap miss 原生返回 originalObject，不变形。AOT 依据：原生
///   GreedBuybackTrader 已实例化 SpawnOrInstantiate&lt;Cat&gt;，interop 泛型可调。
/// - **KEM_FarmCat 命名保留**：池机制不依赖实例名——回收按 Pool.origin 对象引用
///   反查池，存档重建按 prefab 资产名（GetPoolByPrefabName 输入是 Resources.Load
///   出的资产名）+ ObjectData.name 回放；SpawnGO 尾部
///   ImproveCharacterNameReadability 只对带 Character 的 GO 去 "(Clone)"，且我们
///   在拿到返回值之后才命名（覆盖时序安全）。幂等本来就靠 farmHouse 计数，
///   不依赖名字，标记仅用于日志/场景识别。
///
/// 存档语义（侦查结论，Cat.cs / Persistent.cs / IslandSaveData.cs 2.1.0 源 + interop 验证）：
/// - Cat 实现 Persistent.IBehaviour：RetrieveData 存 CatSaveData{farmHouse=
///   PersistentLink, domesticated, color}；Persistent.OnEnable →
///   IslandSaveData.RegisterPersistent 自动登记，存档时 ObjectData 收集
///   全部 registeredPersistents（含运行时实例）。原生猫跨档存活（驯化猫读档仍在
///   农舍）实证猫 prefab 自带该 wiring——因此**运行时 Instantiate 的猫会自然
///   持久化**：存档进 ObjectData（prefabPath 取自 Persistent.path 序列化值），
///   读档 TryCreateOrFind 按 Resources.Load(prefabPath) 重建（无池则 Instantiate，
///   有池 FastSpawn），再经 ApplyData 恢复 farmHouse/domesticated/color。
/// - **双保险**：即使持久化 wiring 对补放实例失效（如 prefab 无 Persistent），
///   本补丁每次读档（OnLevelLoaded 重放）都按现存数补齐到 3，玩家无感——
///   幂等重放本身即兜底，不额外做存档写入。
/// - 网络注册：随生成方式一并原生化（见上）：池化路径由 AttemptSpawnSync 以
///   CRPCType.Dynamic 注册，与原生池化猫同口径；无池回退分支无注册（原生
///   SpawnOrInstantiate 语义如此，未注册时 Cat 本地行为也完整——
///   AnimationSync.SendAnimation 对 parentHeaderRef==null 早退）。
///
/// 已知限制（与 Mono 版对齐，未扩权）：会话中途新买的农舍本次不补猫，
/// 下一次关卡加载（换岛/读档）才补——Mono 版 Kingdom.OnLevelLoaded 一次性
/// 语义相同。
/// </summary>
public static class PatchWorld_FarmCats
{
    private const string MarkerPrefix = "KEM_FarmCat";
    private const float DelaySeconds = 5f;   // 等场景物体/读档重建猫就绪
    private const int CatsPerFarmhouse = 6;  // 用户拍板翻倍（原 Mono 版 3）
    private const float CatScaleY = 1.2f; // 用户拍板：小猫体型 1.2（坑11 只动 y）

    // per-world 指针守卫：在全部就绪检查（biome/联机/kingdom/农舍/prefab）通过
    // 之后、实际放猫之前才消费。换世界/换岛/读档（scene 重建，gameLayer 指针
    // 变化）会重新执行。
    private static IntPtr _stockedWorld;
    private static IntPtr _stockedLayer;
    private static bool _loggedOnlineSkip;
    private static bool _loggedNoPrefab;

    // 北境猫 prefab 缓存：Resources 资产引用跨场景稳定（PatchRoles_NorseSquad
    // ResolveNorseArcherPrefab 同款先例）。
    private static Cat _norseCatPrefab;

    /// <summary>OnLevelLoaded postfix 入口：调度延迟协程。</summary>
    public static void Schedule(World world)
    {
        try
        {
            if (world == null || world.gameObject == null) return;
            world.StartCoroutine(FarmCatsRoutine(world).WrapToIl2Cpp());
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[FarmCats] schedule failed: " + e);
        }
    }

    private static IEnumerator FarmCatsRoutine(World world)
    {
        yield return new WaitForSeconds(DelaySeconds);
        try
        {
            StockFarmCats(world);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[FarmCats] " + e);
        }
    }

    /// <summary>补齐入口（幂等：每农舍按现存数补到 3，每次关卡加载重放）。</summary>
    private static void StockFarmCats(World world)
    {
        // 已就绪检查通过并放过的 world+gameLayer 不重跑（同 TowerSpots）。
        Transform layer = world.gameLayer;
        if (layer == null) return;
        if (_stockedWorld == world.Pointer && _stockedLayer == layer.Pointer) return;

        // 仅希腊 biome（照搬 Mono 版 BiomeIndex==5；IL2CPP 改用静态
        // GreeceBiomeIndex，PatchDivine_GhostSquads/PatchEconomy_Shops 先例，
        // 避免版本间 biome 编号漂移）。biome 不匹配是环境态：不消费守卫。
        if (BiomeHolder.Inst == null
            || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return;

        // 联机 fail-closed（见类注释）。环境态：不消费守卫。
        if (NetworkBigBoss.IsOnline)
        {
            if (!_loggedOnlineSkip)
            {
                _loggedOnlineSkip = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                    "[FarmCats] online session detected; farm cats are " +
                    "single-player/splitscreen only, skipping");
            }
            return;
        }

        // 非权威端不放：Cat.Awake 在 !HasWorldAuth 时 enabled=false（生成的是
        // 停摆空壳）。单机/同机分屏权威端恒真；此态视为未就绪，不消费守卫。
        if (!NetworkBigBoss.HasWorldAuth) return;

        // ---- 就绪检查（瞬时态：未就绪只跳过本次，不消费 per-world 守卫）----
        // kingdom 必须在：Instantiate 触发 Cat.OnEnable → kingdom.AddCat(this)
        // （kingdom 缺失会让原生 OnEnable 内部抛错）。
        Managers managers = Managers.Inst;
        Kingdom kingdom = managers != null ? managers.kingdom : null;
        if (kingdom == null) return;

        var farmhouses = UnityEngine.Object.FindObjectsOfType<Farmhouse>();
        if (farmhouses == null || farmhouses.Length == 0) return; // 本岛暂无农舍，下次加载再试

        Cat catPrefab = ResolveNorseCatPrefab();
        if (catPrefab == null)
        {
            // 资产未就绪等瞬时故障：不消费守卫，下次关卡加载自动重试。
            if (!_loggedNoPrefab)
            {
                _loggedNoPrefab = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                    "[FarmCats] norse cat prefab not resolved; skipping this load");
            }
            return;
        }

        // 全部就绪检查通过：此刻才消费 per-world 守卫（之后的失败属于放猫期
        // 异常，由 TrySpawnFarmCat 的 try/catch 兜底，不吞世界）。
        _stockedWorld = world.Pointer;
        _stockedLayer = layer.Pointer;

        // ---- 坑26 反查：全场扫猫 + farmHouse 回引字段判归属（不枚举
        // kingdom.cats，见类注释）。口径与 kingdom.cats 一致（都只含 active 猫），
        // 读档重建猫经 ApplyData 已恢复 farmHouse/domesticated，计入现存数。----
        var cats = UnityEngine.Object.FindObjectsOfType<Cat>();

        int spawned = 0;
        for (int f = 0; f < farmhouses.Length; f++)
        {
            Farmhouse farmhouse = farmhouses[f];
            if (farmhouse == null || farmhouse.transform == null) continue;

            int existing = 0;
            if (cats != null)
            {
                for (int i = 0; i < cats.Length; i++)
                {
                    Cat cat = cats[i];
                    if (cat != null && cat.domesticated && cat.farmHouse == farmhouse)
                    {
                        existing++;
                        EnsureCatScale(cat); // 读档恢复的猫没走生成路径，幂等补缩放
                    }
                }
            }

            int toSpawn = CatsPerFarmhouse - existing;
            for (int n = 0; n < toSpawn; n++)
            {
                if (TrySpawnFarmCat(catPrefab, farmhouse, layer)) spawned++;
            }
        }

        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[FarmCats] spawned " + spawned + " cats at " + farmhouses.Length
            + " farmhouses (norse prefab)");
    }


    /// <summary>
    /// 小猫 y=1.2（用户拍板）：绝对值写入（坑11 只动 y）+ ScaleRegistry 守卫
    /// （有 Mover 则注册，池复用/原生重置自愈；无 Mover 时绝对值写一次即持久——
    /// 池 respawn 不重拷序列化字段，坑：见 patch-patterns 坑11 相关沉淀）。
    /// </summary>
    private static void EnsureCatScale(Cat cat)
    {
        try
        {
            Vector3 s = cat.transform.localScale;
            if (Mathf.Abs(s.y - CatScaleY) <= 0.0001f) return;
            s.y = CatScaleY;
            cat.transform.localScale = s;
            Mover mover = cat.GetComponent<Mover>();
            if (mover != null) ScaleRegistryHolder.Register(mover, CatScaleY);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[FarmCats/scale] " + e);
        }
    }

    /// <summary>
    /// 生成一只驯化北境猫（Mono 版 SpawnCatsInGreece 内循环体语义 + 原生生成配方）：
    /// 位置农舍 ±4、y+0.5；Pool.SpawnOrInstantiate&lt;Cat&gt;（GreedBuybackTrader.cs
    /// 原生逐字同款——池化/NetID 注册(Dynamic)/激活全部原生一体）；随后
    /// domesticated/farmHouse/白色 interop 直写。任何一步失败销毁半成品猫
    /// （fail-closed，不留非驯化流浪北境猫）。
    /// </summary>
    private static bool TrySpawnFarmCat(Cat prefab, Farmhouse farmhouse, Transform layer)
    {
        Vector3 position = farmhouse.transform.position;
        position.x += UnityEngine.Random.Range(-4f, 4f);
        position.y += 0.5f;

        // 原生生成：池路径 FastSpawn（取出/扩容 + AttemptSpawnSync 注册激活），
        // 无池路径 SpawnGO(allowInstantiate:true) 原生回退 Instantiate。
        // 池复用实例可能带上一场的旧状态（domesticated/farmHouse/颜色），
        // 下面的驯化三件套全量覆盖。
        Cat cat = Pool.SpawnOrInstantiate<Cat>(prefab, position, Quaternion.identity, layer);
        if (cat == null) return false;

        try
        {
            GameObject catGO = cat.gameObject;
            if (catGO == null) return false;

            // 标记名：识别本补丁放的猫（幂等计数走 farmHouse 回引，不依赖名字；
            // 池机制不依赖实例名——回收按 Pool.origin 引用、重建按 prefab 资产名，
            // 见类注释；原生 ImproveCharacterNameReadability 在返回前已跑完）。
            catGO.name = MarkerPrefix + "_" + position.x.ToString("F1");

            // 驯化三件套（interop 直访，见类注释；池化激活时 OnEnable 已把猫
            // 登记进 kingdom.cats 并随机上色，此处覆盖语义与 Mono 版一致）。
            cat.domesticated = true;        // interop 暴露的属性 setter（原生私有 set）
            cat.farmHouse = farmhouse;      // 私有字段 interop 直写 → ShouldFarmCat 立即成立
            cat.SetColor(Color.white);      // 白色标记（Mono 版 SetFromSavedState 兜底同值）
            EnsureCatScale(cat);
            return true;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[FarmCats] spawn setup failed at x=" + position.x.ToString("F1") + ": " + e);
            try { if (cat.gameObject != null) UnityEngine.Object.Destroy(cat.gameObject); } catch { }
            return false;
        }
    }

    /// <summary>
    /// 猫 prefab 解析（2026-08-30 返修：主路径换原生正规路径）。
    /// 主路径：Holder.catPrefab（Holder 公开字段）——原生消费者
    /// BombablePortal / GreedBuybackTrader 赎回被偷猫均直接
    /// Pool.Spawn&lt;OrInstantiate&gt;&lt;Cat&gt;(holder.catPrefab, ...) 不做 biome swap；
    /// 猫只有一个变体（_norselands 动画），Holder 字段就是它。
    /// 兜底（防御性，正常永不应命中）：初版的北境 BiomeData 双机制——实机日志
    /// 实锤 "[FarmCats] norse cat prefab not resolved"，2.4.0 资产里
    /// uniqueCharacters/prefabSwapPool 均解析不到猫，降级保留只为 holder 字段
    /// 意外缺失时有二线。集合全部 for+索引器遍历（坑26：不用枚举器）。
    /// </summary>
    private static Cat ResolveNorseCatPrefab()
    {
        if (_norseCatPrefab != null) return _norseCatPrefab;

        // ---- 主路径：Holder.catPrefab（原生正规路径） ----
        try
        {
            Managers managers = Managers.Inst;
            Holder holder = managers != null ? managers.holder : null;
            Cat holderCat = holder != null ? holder.catPrefab : null;
            if (holderCat != null)
            {
                _norseCatPrefab = holderCat;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[FarmCats] resolved cat prefab via Holder.catPrefab (native path)");
                return _norseCatPrefab;
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[FarmCats] Holder.catPrefab resolution failed: " + e);
        }

        // ---- 兜底：北境 BiomeData 双机制（实机已证空，防御性保留） ----
        try
        {
            var biomePathStrings = BiomeHolder.Inst.biomePathStrings;
            if (biomePathStrings == null) return null;

            int norseIndex = BiomeHolder.NorselandsBiomeIndex;
            if (norseIndex < 0 || norseIndex >= biomePathStrings.Length) return null;

            string norsePath = biomePathStrings[norseIndex];
            if (string.IsNullOrEmpty(norsePath)) return null;

            BiomeData norseBiomeData = Resources.Load<BiomeData>(norsePath);
            if (norseBiomeData == null) return null;

            // 兜底机制一：uniqueCharacters（原生 List<Character>，interop 索引器直访）
            if (norseBiomeData.biomeSpecificAssets != null)
            {
                var uniqueCharacters = norseBiomeData.biomeSpecificAssets.uniqueCharacters;
                if (uniqueCharacters != null)
                {
                    for (int i = 0; i < uniqueCharacters.Count; i++)
                    {
                        Character character = uniqueCharacters[i];
                        if (character == null || character.gameObject == null) continue;
                        if (!character.gameObject.CompareTag("Cat")) continue;

                        Cat cat = character.GetComponent<Cat>();
                        if (cat != null)
                        {
                            _norseCatPrefab = cat;
                            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                                "[FarmCats] resolved cat prefab via uniqueCharacters " +
                                "(fallback; Holder.catPrefab was missing)");
                            return _norseCatPrefab;
                        }
                    }
                }
            }

            // 兜底机制二：prefabSwapPool（List&lt;PrefabSwapData&gt;，swap 是北境变体）
            if (norseBiomeData.swapData != null)
            {
                var prefabSwapPool = norseBiomeData.swapData.prefabSwapPool;
                if (prefabSwapPool != null)
                {
                    for (int i = 0; i < prefabSwapPool.Count; i++)
                    {
                        var swap = prefabSwapPool[i];
                        if (swap == null || swap.swap == null) continue;

                        Cat cat = swap.swap.GetComponent<Cat>();
                        if (cat != null)
                        {
                            _norseCatPrefab = cat;
                            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                                "[FarmCats] resolved cat prefab via prefabSwapPool " +
                                "(fallback; Holder.catPrefab was missing)");
                            return _norseCatPrefab;
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[FarmCats] norse cat prefab resolution failed: " + e);
        }
        return null;
    }
}

/// <summary>
/// World.OnLevelLoaded postfix 宿主：每次关卡加载（新岛/新战役/读档）调度延迟
/// 补猫协程（PatchWorld_TowerSpots 同款范式）。per-world 指针守卫在
/// StockFarmCats 内部、全部就绪检查通过之后才消费。
/// </summary>
[HarmonyPatch(typeof(World), nameof(World.OnLevelLoaded))]
public static class World_FarmCats_Stock_Host_Patch
{
    [HarmonyPostfix]
    private static void Postfix(World __instance)
    {
        if (!ModConfig.Enabled.Value || __instance == null) return;
        PatchWorld_FarmCats.Schedule(__instance);
    }
}
