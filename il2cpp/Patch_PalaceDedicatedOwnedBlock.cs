// Patch_PalaceDedicatedOwnedBlock.cs — 私有 fresh 专用地面块的唯一构造 helper（inactive owned LevelBlock）。
//
// 合同（worker-dedicated-fresh-block-r1）实现边界：
// - 本文件只提供 internal static TryBuild(...) 这一个入口：不安装 hook、不挂 driver、不启动/发布 layout、
//   不写 campaign/存档/钱包/steed、不发 RPC、不改 Main。Root（调用方）负责：创建 inactive 且独立 DDOL 的
//   owner root、验证 exact source/holder/config/allplacements 非共享/sharedLedger==0 等门外条件、在返回 false
//   时以独立 inactiveOwner 为最终清理权威并按 out owned 确认句柄，并在其 GetBlocks 入口接线。本 helper 不验证、也不声称这些 root 侧门在
//   runtime 通过；不声称 Native.CloneInto / 角色注册 / 对象池行为已验证。
// - 输入只读：campSource=StartingAreaTight_Greece_Blocks（cached _width=40），
//   spawnSource=PlayerSpawn_Blocks_Greece（cached _width=24）。只读 LevelBlock 字段
//   （_dimensionsComputed/_width/_left/_right 等）与 Tile.GetLeft/GetRight（仓内已证纯阅读用法）；
//   不调用 LevelBlock.GetWidth/GetLeft/ComputeDimensions（lazy 写缓存），不 SetActive/hideFlags/Destroy 源；
//   成功末尾有限前后快照（关键字段/cached dimensions/child count/typed transforms/Tile flags）逐字一致；
//   不声称所有字段/失败路径的 exact 不可变验收。
// - 自有 clone：Object.Instantiate(campSource.gameObject, inactiveOwner.transform, false)；返回后立即记录
//   owned 引用与指针并核验与源不相等；仅 owned 子树写 HideFlags.DontUnloadUnusedAsset（只写块根，避免后续
//   Native.CloneInto 把该 flag 复制进活体对象）、absoluteCenter=true、groupOne=StartingAreaTight /
//   groupTwo=PlayerSpawn / groupThree=None（本 draft 役只设这三个字段值；不声称与原生 layout group counts
//   同步）。分配后的任何失败都保留 owned 交 Root（reason 前缀 owned-retained:），不重试、不隐藏/销毁源。
// - 结构：owned 根下只保留一层 GameLayer（其余 content layer 整棵销毁）；GameLayer 直接子保留
//   typed Camp/三店 placeholder 与含 Animator 的 Native Beggar（local x 不动），移除 owned 旧 Tile/Grass/确证 Wall0/Tower0；从
//   spawn GameLayer 全量复制非 Tile/Grass 直接角色树（预算 exact：2×PrefabPlaceholder、1×WreckPlaceholder、
//   1×BoatSailPosition、1×Ghost、1×CrownLostHint；其它未知直接子角色显式拒绝，不静默 drop），
//   复制根 localPosition.x += recipe.SpawnRootShiftX（44u 为 -16、400u 为 -194，保持 9u），y/z/scale/rotation/组件/池/网络
//   依赖原样；不创建静态人物贴图、不写钱包/steed/save。
// - 地面：借用 camp GameLayer 的 exact TileBig（component multiset / Persistent path / rect / scale /
//   geometry 可信门，复用 PalaceCourtArea 的 terrain 模式）作 borrowed prototype，按冻结 recipe 复制 11 或 100 份到 x=Left+4*i
//   （保持 native 组件与物理尺寸，不改 scale/collider/组件），清空全部 owned ground Tile 的 33 个 scatter
//   布尔并丢弃 scatter* versionOverrides，只删各 clone 自身子树内的 Grass；雪/天气/随机 sprite/物理责任保留。
/// - 端部两岸：DraftProducer 在同一 ≤256 候选 walk 内按 exact 名解析 BeachLeft/BeachRight_Blocks_Greece
///   各唯一后传入；未分配前经 PalaceDedicatedEndpoints 校验（唯一 GameLayer → typed Beach 直接子恰 1、
///   根 8 组件 multiset、Persistent path/persistObject=true/persistInactive=false/serializeComponents=false/
///   mode=0、位置 finite 且 y/z=0、镜像 scale.x=+1/-1、子树无 Tile/Grass 且 collider=2），分配后把两 typed
///   Beach 完整根各 Instantiate 一次进 owned GameLayer（exclusive handle、组件 refs 原样），唯一写入是
///   clone 根 localPosition=recipe 两岸（边缘内缩7.3125，400u为待Native验证候选）；壁体/Box/Effector/trigger/镜像
///   保持，不自造隐藏墙、不整块克隆 BeachBlock/Portal/Cliff；两 beach source 同样纳入前后指纹对账。
// - 结果门：owned.BuildIndex()+ComputeDimensions()（只 owned）后 cached left/right/width 严格等于冻结 recipe
//   严格核验；结构计数、组别、hideFlags、absoluteCenter 读回复核；source 快照对账不变。
/// - 源与嵌套遍历上限：≤256 节点 / 深度 ≤8 / 每层直接子 ≤32；最终owned GameLayer
///   直接子精确等于源角色计数+recipe.TileCount+6spawn+2Beach；只遍历 campSource、spawnSource、两 beach source
///   与自有 clone 子树，不新增任何全场景扫描。
// - 无 static 可变状态/缓存：本类字段只有编译期常量与 immutable 名称表。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 把 camp 源克隆成 inactive owned <see cref="LevelBlock"/>（专用地面块）并完成本地结构/几何核验。
/// 只做局部不变式；root 侧门与全部运行时消费者（CloneInto/角色注册/对象池）不在本类职责内。
/// </summary>
internal static class PalaceDedicatedOwnedBlock
{
    private const string Prefix = "[PalaceDedicatedOwnedBlock] ";

    // ------------------------------------------------------------ 输入 exact 身份
    private const string CampSourceName = "StartingAreaTight_Greece_Blocks";
    private const string SpawnSourceName = "PlayerSpawn_Blocks_Greece";
    private const int CampNativeWidth = 40;
    private const int SpawnNativeWidth = 24;

    // ------------------------------------------------------------ owned 目标形态
    private const string GameLayerName = "GameLayer";
    private const string TileBigPath = "Prefabs/Environment/Tile Big";
    private const string GrassName = "Grass";

    // ------------------------------------------------------------ spawn 角色预算（exact）
    private const string RolePrefabPlaceholder = "PrefabPlaceholder";
    private const string RoleWreckPlaceholder = "WreckPlaceholder";
    private const string RoleBoatSailPosition = "BoatSailPosition";
    private const string RoleGhost = "Ghost";
    private const string RoleCrownLostHint = "CrownLostHint";
    private const int ExpectedPrefabPlaceholders = 2;
    private const int ExpectedWreckPlaceholders = 1;
    private const int ExpectedBoatSailPositions = 1;
    private const int ExpectedGhosts = 1;
    private const int ExpectedCrownLostHints = 1;
    private const float ContractWreckSailSpacing = 9f;

    // ------------------------------------------------------------ borrowed prototype 可信门
    private const string TransformName = "Transform";
    private const string SpriteRendererName = "SpriteRenderer";
    private const string RandomizeSpriteName = "RandomizeSprite";
    private const string TileName = "Tile";
    private const string PersistentName = "Persistent";
    private const string BiomeSpriteSwapperName = "BiomeSpriteSwapper";
    private const string FixedTransformName = "FixedTransform";

    private static readonly string[] TileComponentNames =
    {
        TransformName, SpriteRendererName, RandomizeSpriteName, TileName,
        PersistentName, BiomeSpriteSwapperName, FixedTransformName,
    };

    private const string ScatterPrefix = "scatter";

    // ------------------------------------------------------------ 遍历/数值门
    private const int WalkMaxNodes = 256;
    private const int WalkMaxDepth = 8;
    private const int WalkMaxChildren = 32;
    private const int OverrideFingerprintCap = 64;
    private const float Tolerance = 0.001f;

    /// <summary>单次构造的上下文；每次 TryBuild 新建，绝不跨调用保存 source 引用。</summary>
    private sealed class BuildContext
    {
        internal readonly PalaceLayoutRecipe Recipe;
        internal int ExpectedDirectChildren;

        internal BuildContext(PalaceLayoutRecipe recipe) { Recipe = recipe; }
        internal ulong OwnerPtr;
        internal ulong CampBlockPtr, CampGoPtr, SpawnBlockPtr, SpawnGoPtr;
        internal string CampBefore = "";
        internal string SpawnBefore = "";
        internal Transform CampLayer;
        internal Transform SpawnLayer;
        internal Tile Prototype;
        internal string[] PrototypeComponents;
        internal int PrototypeColliders;
        internal Vector3 PrototypeLocalPosition;
        internal Vector3 PrototypeScale;
        internal Quaternion PrototypeRotation;
        internal readonly List<SpawnRoleRef> SpawnRoles = new List<SpawnRoleRef>();
        // 填充自分配前完整有界 source 走读；Beggar 动态门复用此结果，不另扫子树。
        internal readonly Dictionary<ulong, bool> SourceSubtreeAnimator = new Dictionary<ulong, bool>();
        // canonical Beach 端部（PalaceDedicatedEndpoints）：两 source 指纹与已验证端点；clone 计数/明细。
        internal string BeachLeftBefore = "", BeachRightBefore = "";
        internal PalaceDedicatedEndpoints.BeachEndpoint BeachLeft, BeachRight;
        internal int BeachClones;
        internal string BeachDetail = "";
        // 两 exclusive Beach clone 句柄（PalaceDedicatedEndpoints 独占已证后立即写入）；
        // 任何失败/异常路径都原样回传给 TryBuild 的 out，由 Root 单 writer 清理，本类不销毁。
        internal GameObject BeachLeftOwned, BeachRightOwned;

        internal int CampKept, ShopKept, BeggarKept, CampTiles, CampGrass, CampWalls, CampTowers;
        internal int SpawnSkippedTiles, SpawnSkippedGrass;

        internal int RemovedLayers, RemovedOldTiles, RemovedOldGrass, RemovedDefenses;
        internal int TileCopies, TileGrassRemoved, ScatterTiles, ScatterCleared, OverridesDropped;
        internal int RoleCopies;
        internal string RoleDetail = "";
        internal int OwnedCampKept, OwnedShopKept, OwnedBeggarKept;
        internal string GeometryDetail = "";
    }

    private readonly struct SpawnRoleRef
    {
        internal readonly string Token;
        internal readonly Transform Source;

        internal SpawnRoleRef(string token, Transform source)
        {
            Token = token;
            Source = source;
        }
    }

    // ================================================================================
    // 入口
    // ================================================================================

    /// <summary>
    /// 从 campSource 克隆 inactive owned LevelBlock，装配专用结构并核验 cached 几何。
    /// campSource/spawnSource 之外接收两 canonical Beach source（只读；校验与 clone 职责在
    /// PalaceDedicatedEndpoints，本类负责接线与指纹对账）。
    /// 失败返回 false：若已分配，out owned 非空且 reason 前缀为 "owned-retained:"，由调用方销毁或保留；
    /// 未分配时 owned 为 null。leftOwned/rightOwned 为两 exclusive Beach clone 句柄（独占已证即回传，
    /// 成功/失败/异常一律保留给 Root 单 writer 清理，本类不销毁）。源对象零写入；无重试、无跨调用缓存。
    /// </summary>
    internal static bool TryBuild(GameObject inactiveOwner, LevelBlock campSource, LevelBlock spawnSource,
        LevelBlock beachLeftSource, LevelBlock beachRightSource, PalaceLayoutRecipe recipe,
        out LevelBlock owned, out GameObject leftOwned, out GameObject rightOwned, out string reason)
    {
        owned = null;
        leftOwned = null;
        rightOwned = null;
        reason = "";
        if (!PalaceLayoutRecipe.IsKnown(recipe))
        {
            reason = "layout-recipe-unknown";
            return false;
        }
        var ctx = new BuildContext(recipe);
        GameObject ownedGo = null;
        try
        {
            if (!TryPreflight(inactiveOwner, campSource, spawnSource, beachLeftSource, beachRightSource,
                    ctx, out reason))
            {
                LogError("preflight rejected: " + reason);
                return false;
            }

            if (!TryInstantiateOwnedBlock(inactiveOwner, campSource, ctx, out ownedGo, out owned, out reason))
            {
                LogError("instantiate rejected: " + reason);
                return false;
            }

            if (!TryApplyOwnedIdentity(ownedGo, owned, out reason))
            {
                reason = "owned-retained:" + reason;
                LogError("identity rejected: " + reason);
                return false;
            }

            if (!TryPopulateOwnedBlock(ownedGo, owned, campSource, spawnSource, beachLeftSource,
                    beachRightSource, ctx, out reason))
            {
                leftOwned = ctx.BeachLeftOwned;
                rightOwned = ctx.BeachRightOwned;
                reason = "owned-retained:" + reason;
                LogError("populate rejected: " + reason);
                return false;
            }

            leftOwned = ctx.BeachLeftOwned;
            rightOwned = ctx.BeachRightOwned;
            reason = "built";
            return true;
        }
        catch (Exception e)
        {
            if (owned == null && ownedGo != null)
            {
                try { owned = ownedGo.GetComponent<LevelBlock>(); }
                catch (Exception) { }
            }
            string leakGuard = "";
            if (owned == null && ownedGo != null)
            {
                // 拿不到可交付句柄：若这确实是本次 Instantiate 产生的自有对象（非源），就地销毁避免无引用泄漏。
                try
                {
                    if (campSource == null || campSource.gameObject == null
                        || PointerOf(ownedGo) != PointerOf(campSource.gameObject))
                    {
                        UnityEngine.Object.DestroyImmediate(ownedGo);
                        ownedGo = null;
                        leakGuard = ";fresh-clone-destroyed";
                    }
                }
                catch (Exception) { }
            }
            // 异常路径同样保留两 exclusive handle 交 Root（读取均在对象有效期内；本类不销毁）。
            leftOwned = ctx.BeachLeftOwned;
            rightOwned = ctx.BeachRightOwned;
            reason = (owned != null ? "owned-retained:" : "") + "exception:" + e.GetType().Name + ":"
                + e.Message + leakGuard;
            LogError("aborted: " + reason);
            return false;
        }
    }

    // ================================================================================
    // 预检（未分配前；全部失败不涉及 owned）
    // ================================================================================

    private static bool TryPreflight(GameObject inactiveOwner, LevelBlock campSource, LevelBlock spawnSource,
        LevelBlock beachLeftSource, LevelBlock beachRightSource, BuildContext ctx, out string reason)
    {
        reason = "";
        if (!TryValidateOwner(inactiveOwner, ctx, out reason)) return false;
        if (!TryValidateSourceIdentity(campSource, spawnSource, ctx, out reason)) return false;
        if (!TryValidateCachedDimensions(campSource, spawnSource, out reason)) return false;
        // 先完成两棵输入树的全部预算与 Animator 收集，再允许后面的 Instantiate。
        if (!TryScanSourceTree(campSource.transform, ctx, "camp", out reason)) return false;
        if (!TryScanSourceTree(spawnSource.transform, ctx, "spawn", out reason)) return false;
        // GameLayer 名称必须同时是游戏 ContentLayers 枚举成员（与仓内既有 layer 遍历前提一致）。
        if (!IsContentLayerName(GameLayerName))
        {
            reason = "game-layer-name-not-in-content-layers:" + GameLayerName;
            return false;
        }
        if (!TryResolveGameLayer(campSource.transform, "camp", out Transform campLayer, out reason)) return false;
        ctx.CampLayer = campLayer;
        if (!TryValidateCampGameLayer(campLayer, ctx, out Tile prototype, out reason)) return false;
        ctx.Prototype = prototype;
        if (!TryVerifyPrototype(prototype, ctx, out reason)) return false;
        if (!TryResolveGameLayer(spawnSource.transform, "spawn", out Transform spawnLayer, out reason)) return false;
        ctx.SpawnLayer = spawnLayer;
        if (!TryValidateSpawnGameLayer(spawnLayer, ctx, out reason)) return false;
        // 两 canonical Beach source（只读，未分配前）：DraftProducer 已在同一候选 walk 内按 exact 名各唯一解析。
        if (!PalaceDedicatedEndpoints.TryValidateSources(beachLeftSource, beachRightSource,
                ctx.CampBlockPtr, ctx.CampGoPtr, ctx.SpawnBlockPtr, ctx.SpawnGoPtr,
                out ctx.BeachLeft, out ctx.BeachRight, out reason))
            return false;
        // Exact output budget comes only from approved source roots and the frozen recipe,
        // never from the already-expanded clone. Source walks keep their original 32-child cap.
        ctx.ExpectedDirectChildren = checked(ctx.Recipe.TileCount + ctx.CampKept + ctx.ShopKept
            + ctx.BeggarKept + ExpectedPrefabPlaceholders + ExpectedWreckPlaceholders
            + ExpectedBoatSailPositions + ExpectedGhosts + ExpectedCrownLostHints
            + PalaceDedicatedEndpoints.ExpectedEndpoints);
        if (!TryFingerprintSource(campSource, out string campFp, out string fpFailure))
        {
            reason = "camp-fingerprint:" + fpFailure;
            return false;
        }
        ctx.CampBefore = campFp;
        if (!TryFingerprintSource(spawnSource, out string spawnFp, out fpFailure))
        {
            reason = "spawn-fingerprint:" + fpFailure;
            return false;
        }
        ctx.SpawnBefore = spawnFp;
        if (!TryFingerprintSource(beachLeftSource, out string beachLeftFp, out fpFailure))
        {
            reason = "beachLeft-fingerprint:" + fpFailure;
            return false;
        }
        ctx.BeachLeftBefore = beachLeftFp;
        if (!TryFingerprintSource(beachRightSource, out string beachRightFp, out fpFailure))
        {
            reason = "beachRight-fingerprint:" + fpFailure;
            return false;
        }
        ctx.BeachRightBefore = beachRightFp;
        Log("preflight ok: owner=" + Hex(ctx.OwnerPtr)
            + " camp=" + Hex(ctx.CampBlockPtr) + " layerChildren=" + campLayer.childCount
            + " (camp=" + ctx.CampKept + ",shop=" + ctx.ShopKept + ",beggar=" + ctx.BeggarKept
            + ",tiles=" + ctx.CampTiles + ",grass=" + ctx.CampGrass + ")"
            + " spawn=" + Hex(ctx.SpawnBlockPtr) + " layerChildren=" + spawnLayer.childCount
            + " roles=" + ctx.SpawnRoles.Count
            + " (skipped tiles=" + ctx.SpawnSkippedTiles + ",grass=" + ctx.SpawnSkippedGrass + ")"
            + " prototype=" + ctx.PrototypeComponents.Length + "-components"
            + " beaches(left=" + Hex(PointerOf(beachLeftSource)) + ",right=" + Hex(PointerOf(beachRightSource))
            + ",typedRoots=" + (ctx.BeachLeft.Root != null && ctx.BeachRight.Root != null ? "2" : "?") + ")");
        return true;
    }

    private static bool TryValidateOwner(GameObject inactiveOwner, BuildContext ctx, out string reason)
    {
        reason = "";
        if (inactiveOwner == null) { reason = "owner-null"; return false; }
        if (inactiveOwner.transform == null) { reason = "owner-transform-null"; return false; }
        if (inactiveOwner.activeInHierarchy) { reason = "owner-active-in-hierarchy"; return false; }
        if (inactiveOwner.activeSelf) { reason = "owner-active-self"; return false; }
        ulong ownerPtr = PointerOf(inactiveOwner);
        if (ownerPtr == 0) { reason = "owner-pointer-zero"; return false; }
        ctx.OwnerPtr = ownerPtr;
        return true;
    }

    private static bool TryValidateSourceIdentity(LevelBlock campSource, LevelBlock spawnSource,
        BuildContext ctx, out string reason)
    {
        reason = "";
        if (campSource == null || campSource.gameObject == null) { reason = "camp-source-null"; return false; }
        if (spawnSource == null || spawnSource.gameObject == null) { reason = "spawn-source-null"; return false; }
        ctx.CampBlockPtr = PointerOf(campSource);
        ctx.CampGoPtr = PointerOf(campSource.gameObject);
        ctx.SpawnBlockPtr = PointerOf(spawnSource);
        ctx.SpawnGoPtr = PointerOf(spawnSource.gameObject);
        if (ctx.CampBlockPtr == 0 || ctx.CampGoPtr == 0) { reason = "camp-source-pointer-zero"; return false; }
        if (ctx.SpawnBlockPtr == 0 || ctx.SpawnGoPtr == 0) { reason = "spawn-source-pointer-zero"; return false; }
        if (ctx.CampBlockPtr == ctx.SpawnBlockPtr || ctx.CampGoPtr == ctx.SpawnGoPtr)
        {
            reason = "source-alias:camp=spawn";
            return false;
        }
        string campName = SafeName(campSource.gameObject);
        if (!string.Equals(campName, CampSourceName, StringComparison.Ordinal))
        {
            reason = "camp-source-name:" + campName;
            return false;
        }
        string spawnName = SafeName(spawnSource.gameObject);
        if (!string.Equals(spawnName, SpawnSourceName, StringComparison.Ordinal))
        {
            reason = "spawn-source-name:" + spawnName;
            return false;
        }
        return true;
    }

    /// <summary>只读 cached 字段：不触发 lazy ComputeDimensions/GetWidth（那会写 source 的缓存）。</summary>
    private static bool TryValidateCachedDimensions(LevelBlock campSource, LevelBlock spawnSource, out string reason)
    {
        reason = "";
        if (!campSource._dimensionsComputed) { reason = "camp-dimensions-uncomputed"; return false; }
        if (campSource._width != CampNativeWidth)
        {
            reason = "camp-width:" + campSource._width.ToString(CultureInfo.InvariantCulture) + "/" + CampNativeWidth;
            return false;
        }
        if (!spawnSource._dimensionsComputed) { reason = "spawn-dimensions-uncomputed"; return false; }
        if (spawnSource._width != SpawnNativeWidth)
        {
            reason = "spawn-width:" + spawnSource._width.ToString(CultureInfo.InvariantCulture) + "/" + SpawnNativeWidth;
            return false;
        }
        Log("source cached dimensions: camp width=" + campSource._width + " left=" + F(campSource._left)
            + " right=" + F(campSource._right) + " | spawn width=" + spawnSource._width
            + " left=" + F(spawnSource._left) + " right=" + F(spawnSource._right));
        return true;
    }

    private static bool TryResolveGameLayer(Transform root, string label, out Transform layer, out string reason)
    {
        layer = null;
        reason = "";
        if (root == null) { reason = label + "-transform-null"; return false; }
        int count = root.childCount;
        if (count > WalkMaxChildren) { reason = label + "-root-child-budget:" + count; return false; }
        int matches = 0;
        for (int i = 0; i < count; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null || child.gameObject == null) { reason = label + "-null-child:" + i; return false; }
            if (!MatchesToken(SafeName(child.gameObject), GameLayerName)) continue;
            matches++;
            layer = child;
        }
        if (matches == 0) { reason = label + "-game-layer-missing"; return false; }
        if (matches != 1) { reason = label + "-game-layer-ambiguous:" + matches; return false; }
        return true;
    }

    /// <summary>
    /// camp GameLayer 直接子预算：Camp/Shop placeholder 各 ≥1、Beggar ≥1 且子树内含 Animator、
    /// 至少一个 exact TileBig（取第一个作 borrowed prototype）；旧 Tile/Grass 允许存在（clone 侧移除）；
    /// 其余未知直接子显式拒绝。
    /// </summary>
    private static bool TryValidateCampGameLayer(Transform layer, BuildContext ctx, out Tile prototype, out string reason)
    {
        prototype = null;
        reason = "";
        int kids = layer.childCount;
        if (kids > WalkMaxChildren) { reason = "camp-game-layer-child-budget:" + kids; return false; }
        var inventory = new StringBuilder();
        for (int i = 0; i < kids; i++)
        {
            Transform child = layer.GetChild(i);
            if (child == null || child.gameObject == null) { reason = "camp-game-layer-null-child:" + i; return false; }
            GameObject go = child.gameObject;
            string name = SafeName(go);
            if (i > 0) inventory.Append('|');
            inventory.Append(i).Append(':').Append(name);
            if (IsGrassObject(go)) { ctx.CampGrass++; continue; }
            if (IsTileObject(go))
            {
                ctx.CampTiles++;
                if (prototype == null && IsKnownTileBigObject(go)) prototype = go.GetComponent<Tile>();
                continue;
            }
            if (IsKnownBeggarObject(go))
            {
                ctx.BeggarKept++;
                if (!ctx.SourceSubtreeAnimator.TryGetValue(PointerOf(child), out bool animated) || !animated)
                {
                    reason = "camp-beggar-animator:" + name;
                    return false;
                }
                continue;
            }
            if (IsKnownCampObject(go)) { ctx.CampKept++; continue; }
            if (IsKnownShopObject(go)) { ctx.ShopKept++; continue; }
            if (IsKnownWallObject(go)) { ctx.CampWalls++; continue; }
            if (IsKnownTowerObject(go)) { ctx.CampTowers++; continue; }
            reason = "camp-game-layer-unknown-child:" + name + " (inventory=" + inventory + ")";
            return false;
        }
        if (ctx.CampKept < 1) { reason = "camp-placeholder-missing (inventory=" + inventory + ")"; return false; }
        if (ctx.ShopKept < 1) { reason = "shop-placeholder-missing (inventory=" + inventory + ")"; return false; }
        if (ctx.BeggarKept < 1) { reason = "camp-beggar-missing (inventory=" + inventory + ")"; return false; }
        if (prototype == null)
        {
            reason = "camp-tilebig-missing (tiles=" + ctx.CampTiles + ", inventory=" + inventory + ")";
            return false;
        }
        Log("camp game layer inventory: " + inventory);
        return true;
    }

    /// <summary>
    /// spawn GameLayer 直接子预算：非 Tile/Grass 直接角色必须精确等于
    /// 2×PrefabPlaceholder + 1×WreckPlaceholder + 1×BoatSailPosition + 1×Ghost + 1×CrownLostHint；
    /// 其它未知直接子角色显式拒绝（不静默 drop）。Tile/Grass 直接子跳过（owned 地面取自 camp）。
    /// </summary>
    private static bool TryValidateSpawnGameLayer(Transform layer, BuildContext ctx, out string reason)
    {
        reason = "";
        int kids = layer.childCount;
        if (kids > WalkMaxChildren) { reason = "spawn-game-layer-child-budget:" + kids; return false; }
        int prefabPh = 0, wreck = 0, sail = 0, ghost = 0, crown = 0;
        float wreckX = float.NaN, sailX = float.NaN;
        var inventory = new StringBuilder();
        for (int i = 0; i < kids; i++)
        {
            Transform child = layer.GetChild(i);
            if (child == null || child.gameObject == null) { reason = "spawn-game-layer-null-child:" + i; return false; }
            GameObject go = child.gameObject;
            string name = SafeName(go);
            if (i > 0) inventory.Append('|');
            inventory.Append(i).Append(':').Append(name);
            if (IsTileObject(go)) { ctx.SpawnSkippedTiles++; continue; }
            if (IsGrassObject(go)) { ctx.SpawnSkippedGrass++; continue; }
            string token = MatchSpawnRoleToken(go);
            if (token == null)
            {
                reason = "spawn-game-layer-unknown-role:" + name + " (inventory=" + inventory + ")";
                return false;
            }
            if (string.Equals(token, RolePrefabPlaceholder, StringComparison.Ordinal)) prefabPh++;
            else if (string.Equals(token, RoleWreckPlaceholder, StringComparison.Ordinal)) wreck++;
            else if (string.Equals(token, RoleBoatSailPosition, StringComparison.Ordinal)) sail++;
            else if (string.Equals(token, RoleGhost, StringComparison.Ordinal)) ghost++;
            else crown++;
            if (token == RoleWreckPlaceholder) wreckX = child.localPosition.x;
            if (token == RoleBoatSailPosition) sailX = child.localPosition.x;
            ctx.SpawnRoles.Add(new SpawnRoleRef(token, child));
        }
        if (prefabPh != ExpectedPrefabPlaceholders)
        {
            reason = "spawn-prefab-placeholders:" + prefabPh + "/" + ExpectedPrefabPlaceholders
                + " (inventory=" + inventory + ")";
            return false;
        }
        if (wreck != ExpectedWreckPlaceholders)
        {
            reason = "spawn-wreck-placeholders:" + wreck + "/" + ExpectedWreckPlaceholders
                + " (inventory=" + inventory + ")";
            return false;
        }
        if (sail != ExpectedBoatSailPositions)
        {
            reason = "spawn-boat-sail-positions:" + sail + "/" + ExpectedBoatSailPositions
                + " (inventory=" + inventory + ")";
            return false;
        }
        if (ghost != ExpectedGhosts)
        {
            reason = "spawn-ghosts:" + ghost + "/" + ExpectedGhosts + " (inventory=" + inventory + ")";
            return false;
        }
        if (crown != ExpectedCrownLostHints)
        {
            reason = "spawn-crown-lost-hints:" + crown + "/" + ExpectedCrownLostHints
                + " (inventory=" + inventory + ")";
            return false;
        }
        if (!Approx(wreckX, 4f) || !Approx(sailX, -5f)
            || !Approx(wreckX - sailX, ContractWreckSailSpacing))
        {
            reason = "spawn-source-wreck-sail-geometry:wreck=" + F(wreckX) + ",sail=" + F(sailX);
            return false;
        }
        Log("spawn game layer inventory: " + inventory);
        return true;
    }

    /// <summary>
    /// borrowed prototype 可信门：component multiset == 已证 Tile 组合、Persistent 启用且路径为原生 Tile 预制、
    /// rect（GetLeft/GetRight）有限且正宽、geometry(4u) 与 unit scale。只读，不触碰源对象。
    /// </summary>
    private static bool TryVerifyPrototype(Tile prototype, BuildContext ctx, out string reason)
    {
        reason = "";
        if (prototype == null || prototype.gameObject == null) { reason = "prototype-null"; return false; }
        GameObject go = prototype.gameObject;
        if (!TryComponentShape(go, out string[] names, out reason)) return false;
        if (!SameNameMultiset(names, TileComponentNames))
        {
            reason = "camp-prototype-components:" + Join(names);
            return false;
        }
        var components = go.GetComponents<Component>();
        Persistent persistent = null;
        for (int i = 0; components != null && i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null) { reason = "camp-prototype-null-component"; return false; }
            if (string.Equals(IL2CPP.il2cpp_class_get_name_(component.ObjectClass), PersistentName,
                    StringComparison.Ordinal))
                persistent = component.TryCast<Persistent>();
        }
        if (persistent == null || !persistent.persistObject) { reason = "camp-prototype-persistent"; return false; }
        string path = persistent.path;
        if (!string.Equals(path, TileBigPath, StringComparison.Ordinal))
        {
            reason = "camp-prototype-path:" + (path ?? "null");
            return false;
        }
        float left;
        float right;
        try
        {
            left = prototype.GetLeft();
            right = prototype.GetRight();
        }
        catch (Exception e)
        {
            reason = "camp-prototype-rect-exception:" + e.GetType().Name;
            return false;
        }
        if (!float.IsFinite(left) || !float.IsFinite(right) || right <= left)
        {
            reason = "camp-prototype-rect:left=" + F(left) + ",right=" + F(right);
            return false;
        }
        float width = right - left;
        if (Math.Abs(width - ctx.Recipe.TileWidth) > Tolerance)
        {
            reason = "camp-prototype-geometry:width=" + F(width) + "/" + F(ctx.Recipe.TileWidth);
            return false;
        }
        Transform transform = prototype.transform;
        Vector3 scale = transform != null ? transform.localScale : Vector3.zero;
        if (transform == null
            || !Approx(scale.x, 1f) || !Approx(scale.y, 1f) || !Approx(scale.z, 1f))
        {
            reason = "camp-prototype-scale:" + Vec(scale);
            return false;
        }
        ctx.PrototypeComponents = names;
        ctx.PrototypeColliders = CountColliders(names);
        ctx.PrototypeLocalPosition = transform.localPosition;
        ctx.PrototypeScale = scale;
        ctx.PrototypeRotation = transform.localRotation;
        Log("borrowed prototype verified: tile=" + SafeName(go) + " ptr=" + Hex(PointerOf(prototype))
            + " path=" + path + " rect=[" + F(left) + "," + F(right) + "] width=" + F(width)
            + " scale=" + Vec(scale) + " components=" + Join(names) + " colliders=" + ctx.PrototypeColliders
            + " localX=" + F(ctx.PrototypeLocalPosition.x)
            + " rectOffset=" + F(left - ctx.PrototypeLocalPosition.x));
        return true;
    }

    // ================================================================================
    // 分配 + owned-only 身份写入
    // ================================================================================

    private static bool TryInstantiateOwnedBlock(GameObject inactiveOwner, LevelBlock campSource,
        BuildContext ctx, out GameObject ownedGo, out LevelBlock owned, out string reason)
    {
        ownedGo = null;
        owned = null;
        reason = "";
        try
        {
            ownedGo = UnityEngine.Object.Instantiate(campSource.gameObject, inactiveOwner.transform, false);
        }
        catch (Exception e)
        {
            ownedGo = null;
            reason = "instantiate-exception:" + e.GetType().Name + ":" + e.Message;
            return false;
        }
        if (ownedGo == null) { reason = "instantiate-null"; return false; }
        ulong goPtr = PointerOf(ownedGo);
        LevelBlock block = ownedGo.GetComponent<LevelBlock>();
        if (block == null || goPtr == 0)
        {
            // 没有可交付句柄：仅当不是源对象时销毁这次新建的自有对象，避免无引用泄漏。
            if (goPtr != 0 && goPtr != ctx.CampGoPtr)
            {
                try
                {
                    UnityEngine.Object.DestroyImmediate(ownedGo);
                    ownedGo = null;
                }
                catch (Exception) { }
            }
            reason = "clone-levelblock-missing:go=" + Hex(goPtr);
            return false;
        }
        ulong blockPtr = PointerOf(block);
        if (goPtr == ctx.CampGoPtr || blockPtr == ctx.CampBlockPtr)
        {
            // 与源同一对象：绝不触碰；若是本次新建却缺组件句柄的残片，同样就地销毁。
            if (goPtr != ctx.CampGoPtr)
            {
                try
                {
                    UnityEngine.Object.DestroyImmediate(ownedGo);
                    ownedGo = null;
                }
                catch (Exception) { }
            }
            reason = "clone-alias-of-camp:go=" + Hex(goPtr) + ",block=" + Hex(blockPtr);
            return false;
        }
        owned = block;
        Log("owned block instantiated: go=" + Hex(goPtr) + " block=" + Hex(blockPtr)
            + " campSource=" + Hex(ctx.CampGoPtr));
        return true;
    }

    /// <summary>
    /// 仅 owned 写入：HideFlags.DontUnloadUnusedAsset（只写块根，防止 Native.CloneInto 把 flag 复制进活体对象）、
    /// absoluteCenter=true、groupOne=StartingAreaTight / groupTwo=PlayerSpawn / groupThree=None；立即读回复核。
    /// </summary>
    private static bool TryApplyOwnedIdentity(GameObject ownedGo, LevelBlock owned, out string reason)
    {
        reason = "";
        if (ownedGo == null || owned == null) { reason = "identity-null"; return false; }
        HideFlags flagsBefore = ownedGo.hideFlags;
        ownedGo.hideFlags = flagsBefore | HideFlags.DontUnloadUnusedAsset;
        if ((ownedGo.hideFlags & HideFlags.DontUnloadUnusedAsset) == 0)
        {
            reason = "owned-lifetime-readback";
            return false;
        }
        owned.absoluteCenter = true;
        owned.groupOne = LevelBlockGroup.StartingAreaTight;
        owned.groupTwo = LevelBlockGroup.PlayerSpawn;
        owned.groupThree = LevelBlockGroup.None;
        if (!TryVerifyOwnedIdentity(ownedGo, owned, out reason)) return false;
        Log("owned identity written: absoluteCenter=true group=(StartingAreaTight,PlayerSpawn,None) "
            + "hideFlags=DontUnloadUnusedAsset");
        return true;
    }

    private static bool TryVerifyOwnedIdentity(GameObject ownedGo, LevelBlock owned, out string reason)
    {
        reason = "";
        if (ownedGo == null || owned == null) { reason = "identity-null"; return false; }
        if (!owned.absoluteCenter
            || owned.groupOne != LevelBlockGroup.StartingAreaTight
            || owned.groupTwo != LevelBlockGroup.PlayerSpawn
            || owned.groupThree != LevelBlockGroup.None)
        {
            reason = "owned-identity-readback";
            return false;
        }
        if ((ownedGo.hideFlags & HideFlags.DontUnloadUnusedAsset) == 0)
        {
            reason = "owned-lifetime-readback";
            return false;
        }
        return true;
    }

    // ================================================================================
    // 装配（此后失败一律保留 owned 交调用方）
    // ================================================================================

    private static bool TryPopulateOwnedBlock(GameObject ownedGo, LevelBlock owned, LevelBlock campSource,
        LevelBlock spawnSource, LevelBlock beachLeftSource, LevelBlock beachRightSource,
        BuildContext ctx, out string reason)
    {
        reason = "";
        if (!TryResolveGameLayer(ownedGo.transform, "owned", out Transform ownedLayer, out reason)) return false;
        if (!TryPruneOwnedTree(ownedGo, ownedLayer, ctx, out reason)) return false;
        if (!TryCopyTileGrid(ownedLayer, ctx, out reason)) return false;
        if (!TryCopySpawnRoles(ownedLayer, ctx, out reason)) return false;
        // 两岸 canonical Beach 完整根各 clone 一次进 owned GameLayer（唯一写入 clone 根 localPosition=recipe 两岸）。
        if (!PalaceDedicatedEndpoints.TryCloneEndpoints(ownedLayer, ctx.BeachLeft, ctx.BeachRight, ctx.Recipe,
                out ctx.BeachLeftOwned, out ctx.BeachRightOwned, out string beachDetail, out reason))
            return false;
        ctx.BeachClones = PalaceDedicatedEndpoints.ExpectedEndpoints;
        ctx.BeachDetail = beachDetail;
        if (!TryVerifyOwnedStructure(ownedLayer, ctx, out reason)) return false;
        if (!TryVerifyOwnedGeometry(owned, ctx, out reason)) return false;
        if (!TryVerifyOwnedIdentity(ownedGo, owned, out reason)) return false;
        if (!TryCompareFingerprint(campSource, ctx.CampBefore, "camp", out reason)) return false;
        if (!TryCompareFingerprint(spawnSource, ctx.SpawnBefore, "spawn", out reason)) return false;
        if (!TryCompareFingerprint(beachLeftSource, ctx.BeachLeftBefore, "beachLeft", out reason)) return false;
        if (!TryCompareFingerprint(beachRightSource, ctx.BeachRightBefore, "beachRight", out reason)) return false;

        Log("owned dedicated block built: block=" + Hex(PointerOf(owned))
            + " go=" + Hex(PointerOf(ownedGo)) + " layer=" + Hex(PointerOf(ownedLayer))
            + " campSource=" + Hex(ctx.CampGoPtr) + " spawnSource=" + Hex(ctx.SpawnGoPtr)
            + " | tiles=" + ctx.TileCopies + " (tileGrassRemoved=" + ctx.TileGrassRemoved
            + ",scatterFlagsCleared=" + ctx.ScatterCleared + ",scatterOverridesDropped=" + ctx.OverridesDropped + ")"
            + " | roles=" + ctx.RoleCopies + ctx.RoleDetail
            + " | kept(camp=" + ctx.OwnedCampKept + ",shop=" + ctx.OwnedShopKept + ",beggar=" + ctx.OwnedBeggarKept + ")"
            + " | removed(layers=" + ctx.RemovedLayers + ",oldTiles=" + ctx.RemovedOldTiles
            + ",oldGrass=" + ctx.RemovedOldGrass + ")"
            + " | geometry(" + ctx.GeometryDetail + ")"
            + " | beaches=" + ctx.BeachClones + ctx.BeachDetail
            + " | observedSourceFields=unchanged-on-success (not full-byte/failure immutability proof)"
            + " | NOT-validated-here: root wiring, Native.CloneInto, role registration/pool duties, gate of holder/config/sharedLedger");
        return true;
    }

    /// <summary>owned 根下只留一层 GameLayer；GameLayer 直接子的旧 Tile/Grass 只从 own 子树销毁。</summary>
    private static bool TryPruneOwnedTree(GameObject ownedGo, Transform ownedLayer, BuildContext ctx, out string reason)
    {
        reason = "";
        Transform root = ownedGo.transform;
        ulong layerPtr = PointerOf(ownedLayer);
        int rootKids = root.childCount;
        if (rootKids > WalkMaxChildren) { reason = "owned-root-child-budget:" + rootKids; return false; }
        var doomedLayers = new List<GameObject>();
        for (int i = 0; i < rootKids; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null || child.gameObject == null) { reason = "owned-root-null-child:" + i; return false; }
            if (PointerOf(child) == layerPtr) continue;
            doomedLayers.Add(child.gameObject);
        }
        for (int i = 0; i < doomedLayers.Count; i++)
        {
            if (!TryDestroyOwned(doomedLayers[i], out reason)) return false;
            ctx.RemovedLayers++;
        }

        int layerKids = ownedLayer.childCount;
        if (layerKids > WalkMaxChildren) { reason = "owned-layer-child-budget:" + layerKids; return false; }
        var doomedContent = new List<GameObject>();
        var doomedKinds = new List<bool>();   // true = 旧 Tile
        for (int i = 0; i < layerKids; i++)
        {
            Transform child = ownedLayer.GetChild(i);
            if (child == null || child.gameObject == null) { reason = "owned-layer-null-child:" + i; return false; }
            GameObject go = child.gameObject;
            if (IsTileObject(go)) { doomedContent.Add(go); doomedKinds.Add(true); continue; }
            if (IsGrassObject(go)) { doomedContent.Add(go); doomedKinds.Add(false); continue; }
            // 输入分类已证这些 exact source 角色；这里参数均为 owned clone 的直接子。
            if (IsKnownWallObject(go) || IsKnownTowerObject(go))
            {
                doomedContent.Add(go);
                doomedKinds.Add(false);
                ctx.RemovedDefenses++;
            }
        }
        for (int i = 0; i < doomedContent.Count; i++)
        {
            if (doomedKinds[i]) ctx.RemovedOldTiles++;
            else if (IsGrassObject(doomedContent[i])) ctx.RemovedOldGrass++;
            if (!TryDestroyOwned(doomedContent[i], out reason)) return false;
        }

        if (root.childCount != 1) { reason = "owned-root-layers:" + root.childCount + "/1"; return false; }
        Transform only = root.GetChild(0);
        if (only == null || PointerOf(only) != layerPtr) { reason = "owned-layer-pointer-drift"; return false; }
        int remaining = ownedLayer.childCount;
        if (remaining > WalkMaxChildren) { reason = "owned-layer-child-budget:" + remaining; return false; }
        for (int i = 0; i < remaining; i++)
        {
            Transform child = ownedLayer.GetChild(i);
            if (child == null || child.gameObject == null) { reason = "owned-layer-null-child:" + i; return false; }
            if (IsTileObject(child.gameObject) || IsGrassObject(child.gameObject)
                || IsKnownWallObject(child.gameObject) || IsKnownTowerObject(child.gameObject))
            {
                reason = "owned-old-content-remains:" + SafeName(child.gameObject);
                return false;
            }
        }
        Log("pruned owned tree: removedLayers=" + ctx.RemovedLayers
            + " removedOldTiles=" + ctx.RemovedOldTiles + " removedOldGrass=" + ctx.RemovedOldGrass);
        return true;
    }

    /// <summary>
    /// 用 borrowed prototype 按冻结 recipe 复制原宽4u Tile 到 x=Left+4*i（只改 localPosition.x，保留 y/z/scale/rotation/组件），
    /// 每个 clone 只删自身子树内的 Grass，并清空其全部 Tile 的 33 个 scatter 布尔 + scatter* versionOverrides。
    /// </summary>
    private static bool TryCopyTileGrid(Transform ownedLayer, BuildContext ctx, out string reason)
    {
        reason = "";
        Tile prototype = ctx.Prototype;
        GameObject protoGo = prototype.gameObject;
        ulong protoGoPtr = PointerOf(protoGo);
        ulong protoTilePtr = PointerOf(prototype);
        for (int i = 0; i < ctx.Recipe.TileCount; i++)
        {
            float x = ctx.Recipe.TileLeft(i);
            GameObject go;
            try
            {
                go = UnityEngine.Object.Instantiate(protoGo, ownedLayer, false);
            }
            catch (Exception e)
            {
                reason = "tile-instantiate-exception:" + i + ":" + e.GetType().Name;
                return false;
            }
            if (go == null) { reason = "tile-instantiate-null:" + i; return false; }
            ulong goPtr = PointerOf(go);
            if (goPtr == 0 || goPtr == protoGoPtr) { reason = "tile-alias:" + i + ":" + Hex(goPtr); return false; }
            Tile tile = go.GetComponent<Tile>();
            ulong tilePtr = PointerOf(tile);
            if (tile == null || tilePtr == 0 || tilePtr == protoTilePtr)
            {
                reason = "tile-component-alias-or-null:" + i + ":" + Hex(tilePtr);
                return false;
            }
            Transform transform = go.transform;
            if (transform == null || !SamePointer(transform.parent, ownedLayer))
            {
                reason = "tile-parent:" + i;
                return false;
            }
            transform.localPosition = new Vector3(x, ctx.PrototypeLocalPosition.y, ctx.PrototypeLocalPosition.z);
            Vector3 applied = transform.localPosition;
            if (!Approx(applied.x, x)
                || !Approx(applied.y, ctx.PrototypeLocalPosition.y)
                || !Approx(applied.z, ctx.PrototypeLocalPosition.z))
            {
                reason = "tile-position:" + i + ":" + Vec(applied);
                return false;
            }
            if (!ApproxVec(transform.localScale, ctx.PrototypeScale)
                || !ApproxQuat(transform.localRotation, ctx.PrototypeRotation))
            {
                reason = "tile-transform-drift:" + i;
                return false;
            }
            if (!TryComponentShape(go, out string[] names, out string shapeFailure))
            {
                reason = "tile-components:" + i + ":" + shapeFailure;
                return false;
            }
            if (!SameNameMultiset(names, ctx.PrototypeComponents))
            {
                reason = "tile-components:" + i + ":" + Join(names);
                return false;
            }
            if (CountColliders(names) != ctx.PrototypeColliders)
            {
                reason = "tile-collider-count:" + i;
                return false;
            }

            var tiles = new List<Tile>();
            var grass = new List<GameObject>();
            if (!TryScanSubtree(go, tiles, grass, out string scanFailure))
            {
                reason = "tile-scan:" + i + ":" + scanFailure;
                return false;
            }
            if (!TryDestroyGrass(grass, out int removedHere, out reason))
            {
                reason = "tile-grass:" + i + ":" + reason;
                return false;
            }
            ctx.TileGrassRemoved += removedHere;

            var verifyTiles = new List<Tile>();
            var verifyGrass = new List<GameObject>();
            if (!TryScanSubtree(go, verifyTiles, verifyGrass, out scanFailure))
            {
                reason = "tile-rescan:" + i + ":" + scanFailure;
                return false;
            }
            if (verifyGrass.Count != 0)
            {
                reason = "tile-grass-remains:" + i + ":" + SafeName(verifyGrass[0]);
                return false;
            }
            for (int k = 0; k < verifyTiles.Count; k++)
            {
                int cleared = ClearScatterAndOverrides(verifyTiles[k], out int dropped);
                ctx.ScatterTiles++;
                ctx.ScatterCleared += cleared;
                ctx.OverridesDropped += dropped;
                if (AnyScatterSet(verifyTiles[k]) || CountScatterOverrides(verifyTiles[k]) != 0)
                {
                    reason = "tile-scatter-readback:" + i;
                    return false;
                }
            }
            ctx.TileCopies++;
            Log("tile clone " + i + ": x=" + F(x) + " go=" + Hex(goPtr) + " tile=" + Hex(tilePtr)
                + " rect=" + RectOf(verifyTiles) + " grassRemoved=" + removedHere
                + " siblingOrder=after-kept-camp");
        }
        Log("tile grid copied: copies=" + ctx.TileCopies + " grassRemoved=" + ctx.TileGrassRemoved
            + " scatterTiles=" + ctx.ScatterTiles + " flagsCleared=" + ctx.ScatterCleared
            + " scatterOverridesDropped=" + ctx.OverridesDropped);
        return true;
    }

    /// <summary>
    /// 全量复制 spawn GameLayer 的非 Tile/Grass 直接角色树到 owned GameLayer，复制根 localPosition.x += recipe.SpawnRootShiftX
    /// （400u Wreck 4→-190、Sail -5→-199，间距保持），其余 transform/组件/动画/池/网络依赖原样，不额外写任何字段。
    /// </summary>
    private static bool TryCopySpawnRoles(Transform ownedLayer, BuildContext ctx, out string reason)
    {
        reason = "";
        float wreckSrcX = 0f, wreckDstX = 0f, sailSrcX = 0f, sailDstX = 0f;
        bool hasWreck = false, hasSail = false;
        var detail = new StringBuilder();
        for (int i = 0; i < ctx.SpawnRoles.Count; i++)
        {
            SpawnRoleRef role = ctx.SpawnRoles[i];
            if (role.Source == null || role.Source.gameObject == null)
            {
                reason = "role-source-null:" + role.Token;
                return false;
            }
            ulong srcGoPtr = PointerOf(role.Source.gameObject);
            if (srcGoPtr == 0) { reason = "role-source-pointer-zero:" + role.Token; return false; }
            Vector3 srcLocal = role.Source.localPosition;
            Vector3 srcScale = role.Source.localScale;
            Quaternion srcRot = role.Source.localRotation;
            GameObject go;
            try
            {
                go = UnityEngine.Object.Instantiate(role.Source.gameObject, ownedLayer, false);
            }
            catch (Exception e)
            {
                reason = "role-instantiate-exception:" + role.Token + ":" + e.GetType().Name;
                return false;
            }
            if (go == null) { reason = "role-instantiate-null:" + role.Token; return false; }
            ulong goPtr = PointerOf(go);
            if (goPtr == 0 || goPtr == srcGoPtr)
            {
                reason = "role-alias:" + role.Token + ":" + Hex(goPtr);
                return false;
            }
            Transform transform = go.transform;
            if (transform == null || !SamePointer(transform.parent, ownedLayer))
            {
                reason = "role-parent:" + role.Token;
                return false;
            }
            if (!ApproxVec(transform.localPosition, srcLocal))
            {
                reason = "role-copy-position:" + role.Token + ":" + Vec(transform.localPosition)
                    + "!=" + Vec(srcLocal);
                return false;
            }
            if (!ApproxVec(transform.localScale, srcScale) || !ApproxQuat(transform.localRotation, srcRot))
            {
                reason = "role-copy-transform:" + role.Token;
                return false;
            }
            float dstX = srcLocal.x + ctx.Recipe.SpawnRootShiftX;
            transform.localPosition = new Vector3(dstX, srcLocal.y, srcLocal.z);
            Vector3 applied = transform.localPosition;
            if (!Approx(applied.x, dstX) || !Approx(applied.y, srcLocal.y) || !Approx(applied.z, srcLocal.z))
            {
                reason = "role-shift-readback:" + role.Token + ":" + Vec(applied);
                return false;
            }
            if (string.Equals(role.Token, RoleWreckPlaceholder, StringComparison.Ordinal))
            {
                wreckSrcX = srcLocal.x;
                wreckDstX = dstX;
                hasWreck = true;
            }
            else if (string.Equals(role.Token, RoleBoatSailPosition, StringComparison.Ordinal))
            {
                sailSrcX = srcLocal.x;
                sailDstX = dstX;
                hasSail = true;
            }
            detail.Append("|").Append(role.Token)
                .Append(":srcX=").Append(F(srcLocal.x))
                .Append(":dstX=").Append(F(dstX))
                .Append(":y=").Append(F(srcLocal.y))
                .Append(":z=").Append(F(srcLocal.z))
                .Append(":children=").Append(role.Source.childCount)
                .Append(":go=").Append(Hex(goPtr));
            ctx.RoleCopies++;
        }
        if (ctx.RoleCopies != ctx.SpawnRoles.Count)
        {
            reason = "role-copies:" + ctx.RoleCopies + "/" + ctx.SpawnRoles.Count;
            return false;
        }
        if (hasWreck && hasSail)
        {
            float srcDelta = wreckSrcX - sailSrcX;
            float dstDelta = wreckDstX - sailDstX;
            if (!Approx(dstDelta, srcDelta))
            {
                reason = "wreck-sail-spacing-changed:src=" + F(srcDelta) + ",dst=" + F(dstDelta);
                return false;
            }
            Log("wreck/sail spacing: source=" + F(srcDelta) + "u shifted=" + F(dstDelta) + "u (wreck "
                + F(wreckSrcX) + "->" + F(wreckDstX) + ", sail " + F(sailSrcX) + "->" + F(sailDstX) + ")");
            if (!Approx(wreckSrcX, 4f) || !Approx(sailSrcX, -5f)
                || !Approx(srcDelta, ContractWreckSailSpacing))
            {
                reason = "wreck-sail-source-geometry-drift:wreck=" + F(wreckSrcX) + ",sail=" + F(sailSrcX);
                return false;
            }
        }
        ctx.RoleDetail = detail.ToString();
        Log("spawn roles copied: copies=" + ctx.RoleCopies + " shiftX=" + F(ctx.Recipe.SpawnRootShiftX) + ctx.RoleDetail);
        return true;
    }

    /// <summary>
    /// owned GameLayer 直接子终态核验：recipe.TileCount 个 TileBig clone、6 个角色 clone（预算 exact）、恰好 2 个 typed
    /// Beach 端部 clone（recipe 两岸/+1与-1，PalaceDedicatedEndpoints 契约）、Camp/Shop/Beggar
    /// 保留、无 Grass、无未知；各 Tile clone 子树无 Grass 且 scatter 已清。
    /// </summary>
    private static bool TryVerifyOwnedStructure(Transform ownedLayer, BuildContext ctx, out string reason)
    {
        reason = "";
        int prefabPh = 0, wreck = 0, sail = 0, ghost = 0, crown = 0;
        int tiles = 0, grass = 0, camp = 0, shop = 0, beggar = 0, unknown = 0;
        int beaches = 0, beachLeft = 0, beachRight = 0;
        string unknownName = "";
        int kids = ownedLayer.childCount;
        // This final GameLayer is the only expanded direct-child budget. Every source,
        // pre-prune clone and nested subtree retains the original 32-child cap.
        if (kids != ctx.ExpectedDirectChildren)
        { reason = "owned-layer-child-count:" + kids + "/" + ctx.ExpectedDirectChildren; return false; }
        for (int i = 0; i < kids; i++)
        {
            Transform child = ownedLayer.GetChild(i);
            if (child == null || child.gameObject == null) { reason = "owned-layer-null-child:" + i; return false; }
            GameObject go = child.gameObject;
            string name = SafeName(go);
            if (IsGrassObject(go)) { grass++; continue; }
            if (IsTileObject(go))
            {
                tiles++;
                if (!IsKnownTileBigObject(go)) { reason = "owned-tile-name:" + name; return false; }
                var foundTiles = new List<Tile>();
                var foundGrass = new List<GameObject>();
                if (!TryScanSubtree(go, foundTiles, foundGrass, out string scanFailure))
                {
                    reason = "owned-tile-scan:" + i + ":" + scanFailure;
                    return false;
                }
                if (foundGrass.Count != 0)
                {
                    reason = "owned-tile-grass:" + name + ":" + SafeName(foundGrass[0]);
                    return false;
                }
                for (int k = 0; k < foundTiles.Count; k++)
                {
                    if (AnyScatterSet(foundTiles[k]) || CountScatterOverrides(foundTiles[k]) != 0)
                    {
                        reason = "owned-tile-scatter:" + name;
                        return false;
                    }
                }
                continue;
            }
            if (PalaceDedicatedEndpoints.IsBeachEndpoint(go))
            {
                beaches++;
                if (Approx(child.localPosition.x, ctx.Recipe.BeachLeftX)
                    && Approx(child.localScale.x, 1f)) beachLeft++;
                else if (Approx(child.localPosition.x, ctx.Recipe.BeachRightX)
                    && Approx(child.localScale.x, -1f)) beachRight++;
                else
                {
                    reason = "owned-beach-endpoint-geometry:" + name + ":" + Vec(child.localPosition)
                        + "/" + Vec(child.localScale);
                    return false;
                }
                continue;
            }
            string roleToken = MatchSpawnRoleToken(go);
            if (roleToken != null)
            {
                if (string.Equals(roleToken, RolePrefabPlaceholder, StringComparison.Ordinal)) prefabPh++;
                else if (string.Equals(roleToken, RoleWreckPlaceholder, StringComparison.Ordinal)) wreck++;
                else if (string.Equals(roleToken, RoleBoatSailPosition, StringComparison.Ordinal)) sail++;
                else if (string.Equals(roleToken, RoleGhost, StringComparison.Ordinal)) ghost++;
                else crown++;
                continue;
            }
            if (IsKnownBeggarObject(go)) { beggar++; continue; }
            if (IsKnownCampObject(go)) { camp++; continue; }
            if (IsKnownShopObject(go)) { shop++; continue; }
            unknown++;
            unknownName = name;
        }
        if (grass != 0) { reason = "owned-direct-grass:" + grass; return false; }
        if (unknown != 0) { reason = "owned-unknown-child:" + unknownName; return false; }
        if (tiles != ctx.Recipe.TileCount || tiles != ctx.TileCopies)
        {
            reason = "owned-tile-count:" + tiles + "/" + ctx.Recipe.TileCount + "/" + ctx.TileCopies;
            return false;
        }
        if (prefabPh != ExpectedPrefabPlaceholders || wreck != ExpectedWreckPlaceholders
            || sail != ExpectedBoatSailPositions || ghost != ExpectedGhosts || crown != ExpectedCrownLostHints)
        {
            reason = "owned-role-counts:prefab=" + prefabPh + "/" + ExpectedPrefabPlaceholders
                + ",wreck=" + wreck + "/" + ExpectedWreckPlaceholders
                + ",sail=" + sail + "/" + ExpectedBoatSailPositions
                + ",ghost=" + ghost + "/" + ExpectedGhosts
                + ",crown=" + crown + "/" + ExpectedCrownLostHints;
            return false;
        }
        int roleCopies = prefabPh + wreck + sail + ghost + crown;
        if (roleCopies != ctx.RoleCopies)
        {
            reason = "owned-role-copies:" + roleCopies + "/" + ctx.RoleCopies;
            return false;
        }
        if (beaches != PalaceDedicatedEndpoints.ExpectedEndpoints || beachLeft != 1 || beachRight != 1)
        {
            reason = "owned-beach-endpoints:total=" + beaches + "/" + PalaceDedicatedEndpoints.ExpectedEndpoints
                + ",left=" + beachLeft + ",right=" + beachRight;
            return false;
        }
        if (camp != ctx.CampKept || shop != ctx.ShopKept || beggar != ctx.BeggarKept)
        {
            reason = "owned-camp-roles:camp=" + camp + "/" + ctx.CampKept
                + ",shop=" + shop + "/" + ctx.ShopKept + ",beggar=" + beggar + "/" + ctx.BeggarKept;
            return false;
        }
        ctx.OwnedCampKept = camp;
        ctx.OwnedShopKept = shop;
        ctx.OwnedBeggarKept = beggar;
        Log("owned structure verified: tiles=" + tiles + " roles(" + prefabPh + "," + wreck + "," + sail
            + "," + ghost + "," + crown + ") beaches(" + beaches + ",left=" + beachLeft
            + ",right=" + beachRight + ") kept(" + camp + "," + shop + "," + beggar + ") grass=0 unknown=0");
        return true;
    }

    /// <summary>owned.BuildIndex()+ComputeDimensions()（只 owned）后严格核验 cached left/right/width。</summary>
    private static bool TryVerifyOwnedGeometry(LevelBlock owned, BuildContext ctx, out string reason)
    {
        reason = "";
        owned.BuildIndex();
        owned.ComputeDimensions();
        if (!owned._dimensionsComputed) { reason = "owned-dimensions-uncomputed"; return false; }
        int width = owned._width;
        float left = owned._left;
        float right = owned._right;
        if (width != ctx.Recipe.Width) { reason = "owned-width:" + width + "/" + ctx.Recipe.Width; return false; }
        if (!Approx(left, ctx.Recipe.Left)) { reason = "owned-left:" + F(left) + "/" + F(ctx.Recipe.Left); return false; }
        if (!Approx(right, ctx.Recipe.Right)) { reason = "owned-right:" + F(right) + "/" + F(ctx.Recipe.Right); return false; }
        string cross;
        try
        {
            cross = "GetWidth=" + owned.GetWidth().ToString(CultureInfo.InvariantCulture)
                + " GetLeft=" + F(owned.GetLeft());
        }
        catch (Exception e)
        {
            cross = "accessor-exception:" + e.GetType().Name;
        }
        ctx.GeometryDetail = "cached left=" + F(left) + " right=" + F(right) + " width=" + width
            + "; read-only cross-check " + cross;
        Log("owned geometry verified: cached left=" + F(left) + " right=" + F(right) + " width=" + width
            + " (" + cross + ")");
        return true;
    }

    private static bool TryCompareFingerprint(LevelBlock source, string before, string label, out string reason)
    {
        reason = "";
        if (!TryFingerprintSource(source, out string after, out string failure))
        {
            reason = label + "-fingerprint-reread:" + failure;
            return false;
        }
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            // 完整 source-before/after 快照对账失败：两份指纹都写日志，便于定位被改动的字段。
            LogError("source fingerprint drift (" + label + "): beforeLen=" + before.Length
                + " afterLen=" + after.Length + "\nBEFORE " + before + "\nAFTER " + after);
            reason = label + "-source-changed";
            return false;
        }
        Log(label + " source fingerprint unchanged: chars=" + after.Length + " hash="
            + after.GetHashCode().ToString("X", CultureInfo.InvariantCulture));
        return true;
    }

    // ================================================================================
    // source 快照（只读）
    // ================================================================================

    /// <summary>
    /// 关键 source 快照：LevelBlock 关键字段 + cached dimensions + 根直接子（layer）计数 +
    /// GameLayer 直接子（角色/Tile）name/activeSelf/child count/typed positions/Tile flags。
    /// 只读；不得引入任何写入。
    /// </summary>
    private static bool TryFingerprintSource(LevelBlock block, out string fingerprint, out string failure)
    {
        fingerprint = null;
        failure = "";
        try
        {
            var sb = new StringBuilder(768);
            sb.Append("name=").Append(SafeName(block.gameObject));
            sb.Append(";blk=").Append(Hex(PointerOf(block)));
            sb.Append(";go=").Append(Hex(PointerOf(block.gameObject)));
            sb.Append(";validated=").Append(block.validated ? '1' : '0');
            sb.Append(";err=").Append(block.validationError == null ? "-" : "set");
            sb.Append(";warn=").Append(block.validationWarning == null ? "-" : "set");
            sb.Append(";unlock=").Append(block.needsUnlock ? '1' : '0');
            sb.Append(";prio=").Append(block.generationPriority.ToString(CultureInfo.InvariantCulture));
            sb.Append(";min=").Append(F(block.minDistance));
            sb.Append(";max=").Append(F(block.maxDistance));
            sb.Append(";mutual=").Append(block.maximizeMutualDistance ? '1' : '0');
            sb.Append(";side=").Append(((int)block.side).ToString(CultureInfo.InvariantCulture));
            sb.Append(";center=").Append(block.absoluteCenter ? '1' : '0');
            sb.Append(";marker=").Append(block.cameraBlockMarker == null ? "-" : "set");
            sb.Append(";policy=").Append(((int)block.safetyPolicy).ToString(CultureInfo.InvariantCulture));
            sb.Append(";danger=").Append(block.isDangerSource ? '1' : '0');
            sb.Append(";g1=").Append(((int)block.groupOne).ToString(CultureInfo.InvariantCulture));
            sb.Append(";g2=").Append(((int)block.groupTwo).ToString(CultureInfo.InvariantCulture));
            sb.Append(";g3=").Append(((int)block.groupThree).ToString(CultureInfo.InvariantCulture));
            sb.Append(";computed=").Append(block._dimensionsComputed ? '1' : '0');
            sb.Append(";width=").Append(block._width.ToString(CultureInfo.InvariantCulture));
            sb.Append(";left=").Append(F(block._left));
            sb.Append(";right=").Append(F(block._right));

            Transform root = block.transform;
            if (root == null) { failure = "transform-null"; return false; }
            sb.Append(";root=").Append(Vec(root.localPosition)).Append('/').Append(Vec(root.localScale))
                .Append('/').Append(Quat(root.localRotation));
            int layers = root.childCount;
            if (layers > WalkMaxChildren) { failure = "root-child-budget:" + layers; return false; }
            sb.Append(";layers=").Append(layers.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < layers; i++)
            {
                Transform layer = root.GetChild(i);
                if (layer == null || layer.gameObject == null) { failure = "null-layer:" + i; return false; }
                GameObject layerGo = layer.gameObject;
                string layerName = SafeName(layerGo);
                sb.Append("|L").Append(i).Append(':').Append(layerName)
                    .Append(':').Append(layerGo.activeSelf ? '1' : '0')
                    .Append(':').Append(layer.childCount.ToString(CultureInfo.InvariantCulture));
                if (!MatchesToken(layerName, GameLayerName)) continue;
                int kids = layer.childCount;
                if (kids > WalkMaxChildren) { failure = "layer-child-budget:" + kids; return false; }
                for (int c = 0; c < kids; c++)
                {
                    Transform child = layer.GetChild(c);
                    if (child == null || child.gameObject == null) { failure = "null-child:" + c; return false; }
                    GameObject go = child.gameObject;
                    sb.Append("|C").Append(c).Append(':').Append(SafeName(go))
                        .Append(':').Append(go.activeSelf ? '1' : '0')
                        .Append(':').Append(child.childCount.ToString(CultureInfo.InvariantCulture))
                        .Append(':').Append(Vec(child.localPosition))
                        .Append('/').Append(Vec(child.localScale)).Append('/').Append(Quat(child.localRotation));
                    Tile tile = go.GetComponent<Tile>();
                    if (tile != null)
                    {
                        sb.Append('/');
                        AppendTileFlags(sb, tile);
                    }
                }
            }
            fingerprint = sb.ToString();
            return true;
        }
        catch (Exception e)
        {
            failure = "exception:" + e.GetType().Name + ":" + e.Message;
            return false;
        }
    }

    private static void AppendTileFlags(StringBuilder sb, Tile tile)
    {
        AppendBool(sb, tile.noGround);
        AppendBool(sb, tile.noSnow);
        AppendBool(sb, tile.scatterTree);
        AppendBool(sb, tile.scatterShrub);
        AppendBool(sb, tile.scatterShrubBirds);
        AppendBool(sb, tile.scatterFireflies);
        AppendBool(sb, tile.scatterShrubBig);
        AppendBool(sb, tile.scatterShrubBigBG);
        AppendBool(sb, tile.scatterGlowingTree);
        AppendBool(sb, tile.scatterTreeBG);
        AppendBool(sb, tile.scatterTreeCurtain);
        AppendBool(sb, tile.scatterTreeCurtainMid);
        AppendBool(sb, tile.scatterTreeCurtainFar);
        AppendBool(sb, tile.scatterMoss);
        AppendBool(sb, tile.scatterWaterReed);
        AppendBool(sb, tile.scatterWaterStatue);
        AppendBool(sb, tile.scatterRoots);
        AppendBool(sb, tile.scatterHill);
        AppendBool(sb, tile.scatterRocks);
        AppendBool(sb, tile.scatterWall);
        AppendBool(sb, tile.scatterFarmhouse);
        AppendBool(sb, tile.scatterTower);
        AppendBool(sb, tile.scatterHermesShade);
        AppendBool(sb, tile.scatterEggMound);
        AppendBool(sb, tile.scatterBackdropNear);
        AppendBool(sb, tile.scatterBackdropFar);
        AppendBool(sb, tile.scatterExtremeForegroundItems);
        AppendBool(sb, tile.scatterHumanoidTrees);
        AppendBool(sb, tile.scatterLightBeamBG);
        AppendBool(sb, tile.scatterForegroundCloudLayer1);
        AppendBool(sb, tile.scatterForegroundCloudLayer2);
        AppendBool(sb, tile.scatterForegroundCloudLayer3);
        AppendBool(sb, tile.scatterForegroundCloudLayer4);
        AppendBool(sb, tile.scatterForegroundCloudLayer5);
        AppendBool(sb, tile.scatterForegroundCloudLayer6);
        var overrides = tile.versionOverrides;
        int count = overrides != null ? overrides.Count : -1;
        sb.Append('#').Append(count.ToString(CultureInfo.InvariantCulture));
        int cap = count > OverrideFingerprintCap ? OverrideFingerprintCap : count;
        for (int i = 0; overrides != null && i < cap; i++)
        {
            TileOverrideData row = overrides[i];
            if (row == null) { sb.Append(";null"); continue; }
            sb.Append(';').Append(row.version.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(row.toggleName ?? "").Append('=').Append(row.enable ? '1' : '0');
        }
        if (overrides != null && overrides.Count > cap) sb.Append(";+").Append(overrides.Count - cap);
    }

    private static void AppendBool(StringBuilder sb, bool value) => sb.Append(value ? '1' : '0');

    // ================================================================================
    // owned 子树有界遍历 / Grass 处理 / scatter 清理
    // ================================================================================

    private static bool TryScanSubtree(GameObject root, List<Tile> tiles, List<GameObject> grass, out string failure)
    {
        tiles.Clear();
        grass.Clear();
        failure = "";
        Transform transform = root != null ? root.transform : null;
        if (transform == null) { failure = "scan-root-null"; return false; }
        int nodesLeft = WalkMaxNodes;
        ScanNode(transform, 0, ref nodesLeft, tiles, grass, ref failure);
        return failure.Length == 0;
    }

    private static void ScanNode(Transform node, int depth, ref int nodesLeft, List<Tile> tiles,
        List<GameObject> grass, ref string failure)
    {
        if (failure.Length != 0) return;
        if (depth > WalkMaxDepth) { failure = "scan-depth-budget:" + depth; return; }
        if (nodesLeft <= 0) { failure = "scan-node-budget"; return; }
        nodesLeft--;
        GameObject go = node.gameObject;
        if (go == null) { failure = "scan-null-node"; return; }
        Tile tile = go.GetComponent<Tile>();
        if (tile != null) tiles.Add(tile);
        if (IsGrassObject(go)) grass.Add(go);
        int children = node.childCount;
        if (children > WalkMaxChildren) { failure = "scan-children-budget:" + children; return; }
        for (int i = 0; i < children; i++)
        {
            Transform child = node.GetChild(i);
            if (child == null) { failure = "scan-null-child:" + i; return; }
            ScanNode(child, depth + 1, ref nodesLeft, tiles, grass, ref failure);
            if (failure.Length != 0) return;
        }
    }

    private static bool TryDestroyGrass(List<GameObject> grass, out int destroyed, out string reason)
    {
        destroyed = 0;
        reason = "";
        for (int i = 0; i < grass.Count; i++)
        {
            GameObject go = grass[i];
            if (go == null) continue;
            if (!TryDestroyOwned(go, out reason)) return false;
            destroyed++;
        }
        return true;
    }

    private static bool TryDestroyOwned(GameObject go, out string reason)
    {
        reason = "";
        if (go == null) return true;
        string name = SafeName(go);
        try
        {
            // 同步销毁：同一调用内 BuildIndex/ComputeDimensions 必须看不到旧对象（Destroy 延迟到帧末）。
            UnityEngine.Object.DestroyImmediate(go);
        }
        catch (Exception e)
        {
            reason = "destroy-exception:" + name + ":" + e.GetType().Name;
            return false;
        }
        if (go != null) { reason = "destroy-readback:" + name; return false; }
        return true;
    }

    /// <summary>清空 33 个 scatter 布尔并丢弃 scatter* versionOverrides；返回实际清掉的 flag 数。</summary>
    private static int ClearScatterAndOverrides(Tile tile, out int overridesDropped)
    {
        overridesDropped = 0;
        int changed = 0;
        if (tile.scatterTree) { tile.scatterTree = false; changed++; }
        if (tile.scatterShrub) { tile.scatterShrub = false; changed++; }
        if (tile.scatterShrubBirds) { tile.scatterShrubBirds = false; changed++; }
        if (tile.scatterFireflies) { tile.scatterFireflies = false; changed++; }
        if (tile.scatterShrubBig) { tile.scatterShrubBig = false; changed++; }
        if (tile.scatterShrubBigBG) { tile.scatterShrubBigBG = false; changed++; }
        if (tile.scatterGlowingTree) { tile.scatterGlowingTree = false; changed++; }
        if (tile.scatterTreeBG) { tile.scatterTreeBG = false; changed++; }
        if (tile.scatterTreeCurtain) { tile.scatterTreeCurtain = false; changed++; }
        if (tile.scatterTreeCurtainMid) { tile.scatterTreeCurtainMid = false; changed++; }
        if (tile.scatterTreeCurtainFar) { tile.scatterTreeCurtainFar = false; changed++; }
        if (tile.scatterMoss) { tile.scatterMoss = false; changed++; }
        if (tile.scatterWaterReed) { tile.scatterWaterReed = false; changed++; }
        if (tile.scatterWaterStatue) { tile.scatterWaterStatue = false; changed++; }
        if (tile.scatterRoots) { tile.scatterRoots = false; changed++; }
        if (tile.scatterHill) { tile.scatterHill = false; changed++; }
        if (tile.scatterRocks) { tile.scatterRocks = false; changed++; }
        if (tile.scatterWall) { tile.scatterWall = false; changed++; }
        if (tile.scatterFarmhouse) { tile.scatterFarmhouse = false; changed++; }
        if (tile.scatterTower) { tile.scatterTower = false; changed++; }
        if (tile.scatterHermesShade) { tile.scatterHermesShade = false; changed++; }
        if (tile.scatterEggMound) { tile.scatterEggMound = false; changed++; }
        if (tile.scatterBackdropNear) { tile.scatterBackdropNear = false; changed++; }
        if (tile.scatterBackdropFar) { tile.scatterBackdropFar = false; changed++; }
        if (tile.scatterExtremeForegroundItems) { tile.scatterExtremeForegroundItems = false; changed++; }
        if (tile.scatterHumanoidTrees) { tile.scatterHumanoidTrees = false; changed++; }
        if (tile.scatterLightBeamBG) { tile.scatterLightBeamBG = false; changed++; }
        if (tile.scatterForegroundCloudLayer1) { tile.scatterForegroundCloudLayer1 = false; changed++; }
        if (tile.scatterForegroundCloudLayer2) { tile.scatterForegroundCloudLayer2 = false; changed++; }
        if (tile.scatterForegroundCloudLayer3) { tile.scatterForegroundCloudLayer3 = false; changed++; }
        if (tile.scatterForegroundCloudLayer4) { tile.scatterForegroundCloudLayer4 = false; changed++; }
        if (tile.scatterForegroundCloudLayer5) { tile.scatterForegroundCloudLayer5 = false; changed++; }
        if (tile.scatterForegroundCloudLayer6) { tile.scatterForegroundCloudLayer6 = false; changed++; }
        var original = tile.versionOverrides;
        if (original != null)
        {
            var kept = new Il2CppSystem.Collections.Generic.List<TileOverrideData>();
            for (int i = 0; i < original.Count; i++)
            {
                TileOverrideData row = original[i];
                if (row == null) continue;
                string toggle = row.toggleName;
                if (toggle != null && toggle.StartsWith(ScatterPrefix, StringComparison.Ordinal))
                {
                    overridesDropped++;
                    continue;
                }
                kept.Add(row);
            }
            if (kept.Count != original.Count) tile.versionOverrides = kept;
        }
        return changed;
    }

    private static bool AnyScatterSet(Tile tile)
        => tile.scatterTree || tile.scatterShrub || tile.scatterShrubBirds || tile.scatterFireflies
        || tile.scatterShrubBig || tile.scatterShrubBigBG || tile.scatterGlowingTree || tile.scatterTreeBG
        || tile.scatterTreeCurtain || tile.scatterTreeCurtainMid || tile.scatterTreeCurtainFar
        || tile.scatterMoss || tile.scatterWaterReed || tile.scatterWaterStatue
        || tile.scatterRoots || tile.scatterHill || tile.scatterRocks
        || tile.scatterWall || tile.scatterFarmhouse || tile.scatterTower
        || tile.scatterHermesShade || tile.scatterEggMound
        || tile.scatterBackdropNear || tile.scatterBackdropFar || tile.scatterExtremeForegroundItems
        || tile.scatterHumanoidTrees || tile.scatterLightBeamBG
        || tile.scatterForegroundCloudLayer1 || tile.scatterForegroundCloudLayer2
        || tile.scatterForegroundCloudLayer3 || tile.scatterForegroundCloudLayer4
        || tile.scatterForegroundCloudLayer5 || tile.scatterForegroundCloudLayer6;

    private static int CountScatterOverrides(Tile tile)
    {
        var overrides = tile.versionOverrides;
        int count = 0;
        for (int i = 0; overrides != null && i < overrides.Count; i++)
        {
            TileOverrideData row = overrides[i];
            string toggle = row != null ? row.toggleName : null;
            if (toggle != null && toggle.StartsWith(ScatterPrefix, StringComparison.Ordinal)) count++;
        }
        return count;
    }

    // ================================================================================
    // 分类 / 窄工具
    // ================================================================================

    // 身份来自两个固定 source 的 typed class + exact 实际名；仅剥离 Unity 的单次 Clone 后缀。
    private static string ExactCloneName(GameObject go)
    {
        string name = SafeName(go);
        const string suffix = "(Clone)";
        return name.EndsWith(suffix, StringComparison.Ordinal)
            ? name.Substring(0, name.Length - suffix.Length) : name;
    }

    private static bool IsKnownCampObject(GameObject go)
        => go != null && ExactCloneName(go) == "Castle0_greece" && go.GetComponent<Campfire>() != null;

    private static bool IsKnownBeggarObject(GameObject go)
        => go != null && ExactCloneName(go) == "Beggar" && go.GetComponent<Beggar>() != null;

    private static bool IsKnownShopObject(GameObject go)
    {
        if (go == null || go.GetComponent<PrefabPlaceholder>() == null) return false;
        string name = ExactCloneName(go);
        return name == "ShopHammer Placeholder" || name == "ShopBow Placeholder"
            || name == "ShopScythe Placeholder";
    }

    private static bool IsKnownTileBigObject(GameObject go)
    {
        if (go == null || go.GetComponent<Tile>() == null) return false;
        string name = ExactCloneName(go);
        if (name != "Tile Big 2" && name != "Tile Big 7" && name != "Tile Big 6"
            && name != "Tile Big 5" && name != "Tile Big 4" && name != "Tile Big 8") return false;
        Persistent persistent = go.GetComponent<Persistent>();
        return persistent != null && persistent.persistObject
            && string.Equals(persistent.path, TileBigPath, StringComparison.Ordinal);
    }

    private static bool IsKnownWallObject(GameObject go)
    {
        if (go == null || ExactCloneName(go) != "Wall0") return false;
        // 当前 Wall0 没有 Wall 类；完整实际组件 multiset 才是此 source 的角色条件。
        return TryComponentShape(go, out string[] names, out _)
            && SameNameMultiset(names, new[] { "Transform", "SpriteRenderer", "PayableUpgrade",
                "SpriteRendererFX", "FixedTransform", "GAPS", "ScatteredObject", "Persistent",
                "PrefabID", "CRPCStamp", "BiomeSpriteSwapper" });
    }

    private static bool IsKnownTowerObject(GameObject go)
    {
        if (go == null || go.GetComponent<Tower>() == null) return false;
        string name = ExactCloneName(go);
        if (name != "Tower0_greece" && name != "Tower0_greece (1)" && name != "Tower0_greece (2)"
            && name != "Tower0_greece (3)") return false;
        return TryComponentShape(go, out string[] names, out _)
            && SameNameMultiset(names, new[] { "Transform", "SpriteRenderer", "PayableUpgrade",
                "FixedTransform", "SpriteRendererFX", "ScatteredObject", "Persistent", "Tower",
                "PrefabID", "CRPCStamp" });
    }

    private static string MatchSpawnRoleToken(GameObject go)
    {
        if (go == null) return null;
        string name = ExactCloneName(go);
        if (name == "Wreck_Greece" && go.GetComponent<WreckPlaceholder>() != null)
            return RoleWreckPlaceholder;
        if (name == "BoatSailPosition_Greece" && go.GetComponent<BoatSailPosition>() != null)
            return RoleBoatSailPosition;
        if (name == "Ghost" && go.GetComponent<Ghost>() != null) return RoleGhost;
        if ((name == "Player Placeholder (1)" || name == "Player Placeholder (2)")
            && go.GetComponent<PrefabPlaceholder>() != null) return RolePrefabPlaceholder;
        // caller 的 exact source root + 唯一 GameLayer + 直接子条件构成 hierarchy path。
        // 尚无 Persistent.path 字符串读回证据，不猜该字段；InfoText/Persistent 与 exact 名双门。
        if (name == "Hint Crown Loss (1)" && go.GetComponent<InfoText>() != null
            && go.GetComponent<Persistent>() != null) return RoleCrownLostHint;
        return null;
    }

    /// <summary>GameLayer 名称匹配；角色身份不使用该 token helper。</summary>
    private static bool MatchesToken(string name, string token)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return string.Equals(name, token, StringComparison.Ordinal)
            || string.Equals(name, token + "(Clone)", StringComparison.Ordinal);
    }

    private static bool IsTileObject(GameObject go) => go != null && go.GetComponent<Tile>() != null;

    private static bool IsGrassObject(GameObject go) => go != null && go.GetComponent<Grass>() != null;

    private static bool TryScanSourceTree(Transform root, BuildContext ctx, string label, out string reason)
    {
        reason = "";
        int nodesLeft = WalkMaxNodes;
        if (!TryScanSourceNode(root, 0, ref nodesLeft, ctx.SourceSubtreeAnimator, out _, out reason))
        {
            reason = label + "-source-" + reason;
            return false;
        }
        return true;
    }

    private static bool TryScanSourceNode(Transform node, int depth, ref int nodesLeft,
        Dictionary<ulong, bool> subtreeAnimator, out bool hasAnimator, out string reason)
    {
        hasAnimator = false;
        reason = "";
        if (node == null || node.gameObject == null) { reason = "tree-null-node"; return false; }
        if (depth > WalkMaxDepth) { reason = "tree-depth-budget:" + depth; return false; }
        if (nodesLeft <= 0) { reason = "tree-node-budget"; return false; }
        nodesLeft--;
        ulong ptr = PointerOf(node);
        if (ptr == 0 || subtreeAnimator.ContainsKey(ptr)) { reason = "tree-alias-or-zero"; return false; }
        int kids = node.childCount;
        if (kids > WalkMaxChildren) { reason = "tree-child-budget:" + kids; return false; }
        hasAnimator = node.gameObject.GetComponent<Animator>() != null;
        for (int i = 0; i < kids; i++)
        {
            if (!TryScanSourceNode(node.GetChild(i), depth + 1, ref nodesLeft, subtreeAnimator,
                    out bool childAnimator, out reason)) return false;
            hasAnimator |= childAnimator;
        }
        subtreeAnimator.Add(ptr, hasAnimator);
        return true;
    }

    private static bool IsContentLayerName(string name)
    {
        string[] names = Enum.GetNames(typeof(ContentLayers));
        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], name, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static bool TryComponentShape(GameObject go, out string[] names, out string failure)
    {
        names = null;
        failure = "";
        var components = go.GetComponents<Component>();
        if (components == null || components.Length == 0) { failure = "no-components"; return false; }
        names = new string[components.Length];
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null) { failure = "null-component"; return false; }
            names[i] = IL2CPP.il2cpp_class_get_name_(component.ObjectClass);
        }
        return true;
    }

    private static bool SameNameMultiset(string[] actual, string[] expected)
    {
        if (actual == null || expected == null || actual.Length != expected.Length) return false;
        for (int i = 0; i < expected.Length; i++)
        {
            if (CountName(actual, expected[i]) != CountName(expected, expected[i])) return false;
        }
        return true;
    }

    private static int CountName(string[] names, string name)
    {
        int count = 0;
        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], name, StringComparison.Ordinal)) count++;
        }
        return count;
    }

    private static int CountColliders(string[] componentNames)
    {
        int count = 0;
        for (int i = 0; i < componentNames.Length; i++)
        {
            if (componentNames[i] != null && componentNames[i].EndsWith("Collider2D", StringComparison.Ordinal))
                count++;
        }
        return count;
    }

    private static string Join(string[] names) => names == null ? "-" : string.Join(",", names);

    private static string RectOf(List<Tile> tiles)
    {
        if (tiles == null || tiles.Count == 0) return "none";
        float left;
        float right;
        if (!TryReadTileRect(tiles[0], out left, out right)) return "unreadable";
        return "[" + F(left) + "," + F(right) + "]";
    }

    private static bool TryReadTileRect(Tile tile, out float left, out float right)
    {
        left = 0f;
        right = 0f;
        if (tile == null) return false;
        try
        {
            left = tile.GetLeft();
            right = tile.GetRight();
            return float.IsFinite(left) && float.IsFinite(right) && right > left;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool SamePointer(UnityEngine.Object a, UnityEngine.Object b)
    {
        if (a == null || b == null) return false;
        ulong pa = PointerOf(a);
        return pa != 0 && pa == PointerOf(b);
    }

    private static bool Approx(float a, float b) => Math.Abs(a - b) <= Tolerance;

    private static bool ApproxVec(Vector3 a, Vector3 b)
        => Approx(a.x, b.x) && Approx(a.y, b.y) && Approx(a.z, b.z);

    private static bool ApproxQuat(Quaternion a, Quaternion b)
        => Approx(a.x, b.x) && Approx(a.y, b.y) && Approx(a.z, b.z) && Approx(a.w, b.w);

    private static ulong PointerOf(Il2CppObjectBase value)
    {
        if (value == null) return 0;
        try
        {
            return unchecked((ulong)value.Pointer.ToInt64());
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static string Hex(ulong value) => "0x" + value.ToString("X", CultureInfo.InvariantCulture);

    private static string SafeName(UnityEngine.Object subject)
    {
        try { return subject != null ? (subject.name ?? "") : ""; }
        catch (Exception) { return ""; }
    }

    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Vec(Vector3 value) => F(value.x) + "," + F(value.y) + "," + F(value.z);

    private static string Quat(Quaternion value) => F(value.x) + "," + F(value.y) + "," + F(value.z) + "," + F(value.w);

    private static void Log(string message)
    {
        try { UnityEngine.Debug.Log(Prefix + message); }
        catch (Exception) { }
    }

    private static void LogError(string message)
    {
        try { UnityEngine.Debug.LogError(Prefix + message); }
        catch (Exception) { }
    }
}
