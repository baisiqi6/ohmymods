// Patch_PalaceDedicatedEndpoints.cs — 私有 fresh 专用 known recipe 块的两岸 canonical Beach 端部 helper。
//
// 合同（fresh-endpoint-producer-worker-20261009）实现边界：
// - 只做两件事：分配前校验两 canonical Beach source（只读，不产生任何对象），分配后把两 typed Beach
//   完整根各 clone 一次进 owned GameLayer。不安装 hook、不挂 driver、不写 layout/存档/钱包/steed、
//   不发 RPC、不自造隐藏墙或 Box 仿制品、不整块克隆 BeachBlock/Portal/Cliff。调用方
//   PalaceDedicatedOwnedBlock 在其既有 preflight/populate 步骤内接线；失败显式返回，partial clone
//   留在 owned 交 Root 清理，不重试、不触碰源。
// - 两岸证据（只读）：stock-beach-focused-fresh-prepared-r1 bepinex.log + a554 frozen-save-native-beaches.json：
//   BeachLeft/BeachRight_Blocks_Greece 各 1，唯一 GameLayer 直接子中 typed Beach 恰 1；Beach 根 8 组件
//   exact multiset（Transform/SpriteRenderer/Persistent/Beach/SpriteRendererFX/GAPS/PrefabID/FormationBlocker）；
//   Persistent(path="Prefabs/Buildings and Interactive/greece/Beach_greece", persistObject=true,
//   persistInactive=false, serializeComponents=false, mode=0)；Native Beach root 距 level edges 恒 7.3125
//   → recipe 物理边缘内缩7.3125；400u接触仍须Native验证；镜像 scale Left+1 / Right-1；源根 y/z=0、rotation identity；子树无 Tile/Grass、
//   collider=2（Invisible Wall + Crown Catcher）。未读字段不猜配置；不调用源 GetWidth/ComputeDimensions/
//   SetActive/Destroy 等 lazy/破坏方法。
// - 唯一写入：clone 根 localPosition=(recipe 两岸,0,0)；镜像 scale/rotation/内部 Box/Effector/trigger/壁体/
//   组件 refs 原样保留。遍历预算与 OwnedBlock 一致：≤256 节点 / 深度 ≤8 / 每层直接子 ≤32；无 Resources
//   加载、反射搜索、二次重试、共享实例缓存、static 可变状态。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 两岸 canonical Beach 端部的 typed source 校验与完整根 clone。
/// 只做局部不变式；source 名解析（DraftProducer）与全部运行时消费者不在本类职责内。
/// </summary>
internal static class PalaceDedicatedEndpoints
{
    private const string Prefix = "[PalaceDedicatedEndpoints] ";

    // ------------------------------------------------------------ 两岸证据常量（只读契约）
    internal const string BeachLeftSourceName = "BeachLeft_Blocks_Greece";
    internal const string BeachRightSourceName = "BeachRight_Blocks_Greece";
    internal const string BeachRootName = "Beach_greece";
    internal const string BeachPrefabPath = "Prefabs/Buildings and Interactive/greece/Beach_greece";
    private const string GameLayerName = "GameLayer";
    private const string PersistentName = "Persistent";
    // a554 普通岛（land4）Native Beach root 距 level edges 恒定偏移：-326.6875/-334、232.6875/240。
    internal const float BeachEdgeOffset = 7.3125f;
    // Legacy44 aliases retained for the existing admission consumer; cloning uses its recipe.
    internal const float OwnedBeachLeftX = -22f + BeachEdgeOffset;   // -14.6875
    internal const float OwnedBeachRightX = 22f - BeachEdgeOffset;   // 14.6875
    internal const int ExpectedEndpoints = 2;
    internal const int BeachSubtreeColliders = 2; // Invisible Wall + Crown Catcher

    private static readonly string[] BeachRootComponentNames =
    {
        "Transform", "SpriteRenderer", PersistentName, "Beach", "SpriteRendererFX", "GAPS",
        "PrefabID", "FormationBlocker",
    };

    // 与 PalaceDedicatedOwnedBlock 相同的遍历预算（各类私有常量，数值一致）。
    private const int WalkMaxNodes = 256;
    private const int WalkMaxDepth = 8;
    private const int WalkMaxChildren = 32;
    private const float Tolerance = 0.001f;

    /// <summary>已验证的一侧 Beach 端点（source 只读快照；不跨调用保存引用）。</summary>
    internal readonly struct BeachEndpoint
    {
        internal readonly Transform Root;
        internal readonly GameObject RootGo;
        internal readonly ulong RootGoPtr;
        internal readonly int SubtreeNodes;
        internal readonly int SubtreeComponents;
        // 完整已观察 profile（根 localPosition 排除：clone 对账时唯一合法差，另在 clone 前后单独核）。
        internal readonly string SourceProfile;
        // 校验时刻源根 localPosition：clone 时复核对账（源根位置不得漂移）。
        internal readonly Vector3 RootLocal;

        internal BeachEndpoint(Transform root, GameObject rootGo, ulong rootGoPtr,
            int subtreeNodes, int subtreeComponents, string sourceProfile, Vector3 rootLocal)
        {
            Root = root;
            RootGo = rootGo;
            RootGoPtr = rootGoPtr;
            SubtreeNodes = subtreeNodes;
            SubtreeComponents = subtreeComponents;
            SourceProfile = sourceProfile;
            RootLocal = rootLocal;
        }
    }

    private struct SubtreeCounts
    {
        internal int Nodes, Components, Colliders, Tiles, Grass;
    }

    // ================================================================================
    // 分配前：两 source 校验（只读）
    // ================================================================================

    /// <summary>
    /// 校验两 canonical Beach source：exact 名、与 camp/spawn 不 alias、唯一 GameLayer、直接子中 typed
    /// Beach 恰 1、根契约（组件 multiset / Persistent / 位置 finite 且 y,z=0 / 镜像 scale / identity
    /// rotation）、子树有界扫描（无 Tile/Grass、collider=2）。任何缺失/重复/null/malformed 显式 false。
    /// </summary>
    internal static bool TryValidateSources(LevelBlock beachLeftSource, LevelBlock beachRightSource,
        ulong campBlockPtr, ulong campGoPtr, ulong spawnBlockPtr, ulong spawnGoPtr,
        out BeachEndpoint left, out BeachEndpoint right, out string reason)
    {
        left = default;
        right = default;
        reason = "";
        if (!TryValidateSource(beachLeftSource, BeachLeftSourceName, "beachLeft", 1f,
                campBlockPtr, campGoPtr, spawnBlockPtr, spawnGoPtr, out left, out reason))
            return false;
        if (!TryValidateSource(beachRightSource, BeachRightSourceName, "beachRight", -1f,
                campBlockPtr, campGoPtr, spawnBlockPtr, spawnGoPtr, out right, out reason))
            return false;
        if (left.RootGoPtr == right.RootGoPtr || SamePointer(left.Root, right.Root))
        {
            reason = "beach-source-alias:left=right";
            return false;
        }
        Log("beach sources verified: left(" + BeachLeftSourceName + ") subtree(nodes=" + left.SubtreeNodes
            + ",components=" + left.SubtreeComponents + ",colliders=" + BeachSubtreeColliders
            + ",tiles=0,grass=0) right(" + BeachRightSourceName + ") subtree(nodes=" + right.SubtreeNodes
            + ",components=" + right.SubtreeComponents + ",colliders=" + BeachSubtreeColliders
            + ",tiles=0,grass=0) mirrorScale=left+1/right-1");
        return true;
    }

    private static bool TryValidateSource(LevelBlock source, string expectedName, string label,
        float expectedScaleX, ulong campBlockPtr, ulong campGoPtr, ulong spawnBlockPtr, ulong spawnGoPtr,
        out BeachEndpoint endpoint, out string reason)
    {
        endpoint = default;
        reason = "";
        if (source == null || source.gameObject == null) { reason = label + "-source-null"; return false; }
        ulong blockPtr = PointerOf(source);
        ulong goPtr = PointerOf(source.gameObject);
        if (blockPtr == 0 || goPtr == 0) { reason = label + "-source-pointer-zero"; return false; }
        if (blockPtr == campBlockPtr || goPtr == campGoPtr) { reason = label + "-source-alias:camp"; return false; }
        if (blockPtr == spawnBlockPtr || goPtr == spawnGoPtr) { reason = label + "-source-alias:spawn"; return false; }
        string name = SafeName(source.gameObject);
        if (!string.Equals(name, expectedName, StringComparison.Ordinal))
        {
            reason = label + "-source-name:" + name;
            return false;
        }
        Transform root = source.transform;
        if (root == null) { reason = label + "-source-transform-null"; return false; }
        if (!TryResolveGameLayer(root, label, out Transform layer, out reason)) return false;
        int kids = layer.childCount;
        if (kids > WalkMaxChildren) { reason = label + "-layer-child-budget:" + kids; return false; }
        Transform beachRoot = null;
        int typedHits = 0;
        var inventory = new StringBuilder();
        for (int i = 0; i < kids; i++)
        {
            Transform child = layer.GetChild(i);
            if (child == null || child.gameObject == null) { reason = label + "-null-child:" + i; return false; }
            if (i > 0) inventory.Append('|');
            inventory.Append(i).Append(':').Append(SafeName(child.gameObject));
            if (child.gameObject.GetComponent<Beach>() != null)
            {
                typedHits++;
                beachRoot = child;
            }
        }
        if (typedHits != 1)
        {
            reason = label + "-typed-beach-count:" + typedHits + " (inventory=" + inventory + ")";
            return false;
        }
        if (!TryVerifyBeachRoot(beachRoot, expectedScaleX, label, out reason)) return false;
        Vector3 rootLocal = beachRoot.localPosition;
        // 端点 profile：完整已观察配置（根 localPosition 排除，clone 对账时唯一合法差）；根原位置单独捕获。
        if (!TryScanBeachSubtree(beachRoot.gameObject, label, false, out SubtreeCounts counts,
                out string sourceProfile, out reason))
            return false;
        if (counts.Tiles != 0 || counts.Grass != 0)
        {
            reason = label + "-beach-subtree-ground:tiles=" + counts.Tiles + ",grass=" + counts.Grass;
            return false;
        }
        if (counts.Colliders != BeachSubtreeColliders)
        {
            reason = label + "-beach-subtree-colliders:" + counts.Colliders + "/" + BeachSubtreeColliders;
            return false;
        }
        endpoint = new BeachEndpoint(beachRoot, beachRoot.gameObject, PointerOf(beachRoot.gameObject),
            counts.Nodes, counts.Components, sourceProfile, rootLocal);
        Log(label + " source verified: name=" + name + " block=" + Hex(blockPtr)
            + " layerChildren=" + kids + " root=" + SafeName(beachRoot.gameObject)
            + " subtree(nodes=" + counts.Nodes + ",components=" + counts.Components
            + ",colliders=" + counts.Colliders + ")");
        return true;
    }

    /// <summary>
    /// canonical Beach 根契约（两岸证据）：exact 名 Beach_greece、8 组件 exact multiset、
    /// Persistent(path/persistObject=true/persistInactive=false/serializeComponents=false/mode=0)、
    /// 位置 finite 且 y/z=0、scale.x=+1(左)/-1(右) 且 y/z=1、rotation identity。只读。
    /// </summary>
    private static bool TryVerifyBeachRoot(Transform root, float expectedScaleX, string label, out string reason)
    {
        reason = "";
        if (root == null || root.gameObject == null) { reason = label + "-beach-root-null"; return false; }
        GameObject go = root.gameObject;
        string rootName = SafeName(go);
        if (!string.Equals(rootName, BeachRootName, StringComparison.Ordinal))
        {
            reason = label + "-beach-root-name:" + rootName;
            return false;
        }
        if (!TryComponentShape(go, out string[] names, out string shapeFailure))
        {
            reason = label + "-beach-root-components:" + shapeFailure;
            return false;
        }
        if (!SameNameMultiset(names, BeachRootComponentNames))
        {
            reason = label + "-beach-root-components:" + Join(names);
            return false;
        }
        Persistent persistent = FindPersistent(go, out string persistentFailure);
        if (persistent == null)
        {
            reason = label + "-beach-root-persistent:" + persistentFailure;
            return false;
        }
        if (!persistent.persistObject || persistent.persistInactive || persistent.serializeComponents)
        {
            reason = label + "-beach-root-persistent-flags:persistObject=" + persistent.persistObject
                + ",persistInactive=" + persistent.persistInactive
                + ",serializeComponents=" + persistent.serializeComponents;
            return false;
        }
        if (Convert.ToInt32(persistent.mode) != 0)
        {
            reason = label + "-beach-root-persistent-mode:" + Convert.ToInt32(persistent.mode);
            return false;
        }
        if (!string.Equals(persistent.path, BeachPrefabPath, StringComparison.Ordinal))
        {
            reason = label + "-beach-root-path:" + (persistent.path ?? "null");
            return false;
        }
        Vector3 local = root.localPosition;
        if (!float.IsFinite(local.x) || !float.IsFinite(local.y) || !float.IsFinite(local.z)
            || !Approx(local.y, 0f) || !Approx(local.z, 0f))
        {
            reason = label + "-beach-root-position:" + Vec(local);
            return false;
        }
        Vector3 scale = root.localScale;
        if (!Approx(scale.x, expectedScaleX) || !Approx(scale.y, 1f) || !Approx(scale.z, 1f))
        {
            reason = label + "-beach-root-scale:" + Vec(scale) + "/expectedX=" + F(expectedScaleX);
            return false;
        }
        Quaternion rotation = root.localRotation;
        if (!ApproxQuat(rotation, Quaternion.identity))
        {
            reason = label + "-beach-root-rotation:" + Quat(rotation);
            return false;
        }
        Log(label + " beach root contract ok: path=" + BeachPrefabPath + " local=" + Vec(local)
            + " scale=" + Vec(scale) + " components=" + Join(names));
        return true;
    }

    // ================================================================================
    // 分配后：完整根 clone（owned GameLayer 内，各一次；失败保留 owned 交 Root 清理）
    // ================================================================================

    /// <summary>
    /// 把两 typed Beach 完整根各 Instantiate 一次进 owned GameLayer：exclusive handle、与 source 不
    /// alias、组件 refs 保持；唯一写入是 clone 根 localPosition=(recipe 两岸,0,0)。镜像 scale/rotation/
    /// 内部 Box/Effector/trigger/壁体原样；子树复扫节点/组件数与 source 观察一致、无 Tile/Grass、
    /// collider=2。
    /// </summary>
    internal static bool TryCloneEndpoints(Transform ownedLayer, BeachEndpoint left, BeachEndpoint right,
        PalaceLayoutRecipe recipe, out GameObject leftOwned, out GameObject rightOwned, out string detail, out string reason)
    {
        leftOwned = null;
        rightOwned = null;
        detail = "";
        reason = "";
        if (!PalaceLayoutRecipe.IsKnown(recipe))
        {
            reason = "layout-recipe-unknown";
            return false;
        }
        var sb = new StringBuilder();
        if (!TryCloneEndpoint(left, "beachLeft", recipe.BeachLeftX, 1f, ownedLayer, sb,
                out leftOwned, out reason)) return false;
        if (!TryCloneEndpoint(right, "beachRight", recipe.BeachRightX, -1f, ownedLayer, sb,
                out rightOwned, out reason)) return false;
        detail = sb.ToString();
        Log("beach endpoints cloned: copies=" + ExpectedEndpoints + detail
            + " | rootX=" + F(recipe.BeachLeftX) + "/" + F(recipe.BeachRightX)
            + " (layout=" + recipe.LayoutId + ", edge offset=" + F(BeachEdgeOffset)
            + "u;400u endpoint contact requires Native evidence)");
        return true;
    }

    private static bool TryCloneEndpoint(BeachEndpoint endpoint, string label, float targetX,
        float expectedScaleX, Transform ownedLayer, StringBuilder detail, out GameObject ownedHandle,
        out string reason)
    {
        reason = "";
        ownedHandle = null;
        Transform sourceRoot = endpoint.Root;
        if (sourceRoot == null || sourceRoot.gameObject == null)
        {
            reason = label + "-clone-source-null";
            return false;
        }
        GameObject sourceGo = sourceRoot.gameObject;
        ulong sourcePtr = PointerOf(sourceGo);
        if (sourcePtr == 0 || sourcePtr != endpoint.RootGoPtr)
        {
            reason = label + "-clone-source-pointer-drift:" + Hex(sourcePtr);
            return false;
        }
        if (!ApproxVec(sourceRoot.localPosition, endpoint.RootLocal))
        {
            reason = label + "-clone-source-root-position-drift:" + Vec(sourceRoot.localPosition)
                + "/" + Vec(endpoint.RootLocal);
            return false;
        }
        // clone 前完整 source profile（含根 localPosition）：clone 后同一 scan 复扫对账（源零写）。
        if (!TryScanBeachSubtree(sourceGo, label, true, out _, out string sourceBefore, out reason))
            return false;
        Vector3 sourceLocal = sourceRoot.localPosition;
        Vector3 sourceScale = sourceRoot.localScale;
        Quaternion sourceRotation = sourceRoot.localRotation;
        GameObject cloneGo;
        try
        {
            cloneGo = UnityEngine.Object.Instantiate(sourceGo, ownedLayer, false);
        }
        catch (Exception e)
        {
            reason = label + "-beach-instantiate-exception:" + e.GetType().Name + ":" + e.Message;
            return false;
        }
        // 无 handle / 指针无效 / 与 source 同对象：未证明存在可清理的独立自有对象，
        // Native Instantiate 的 unknown 副作用不猜 Destroy 或重生，显式失败交上层处理。
        if (cloneGo == null || PointerOf(cloneGo) == 0)
        {
            reason = label + "-beach-instantiate-null:no-handle;unknown-side-effects-not-guessed";
            return false;
        }
        ulong clonePtr = PointerOf(cloneGo);
        if (clonePtr == sourcePtr)
        {
            reason = label + "-beach-alias:" + Hex(clonePtr) + ";source-untouched";
            return false;
        }
        // 独占已证（clonePtr 非零且 != sourcePtr）：立即交出本侧 out handle（先于任何
        // clone.transform/parent/字段 getter）。此后任何失败/异常都保留该 handle 交 Root 单 writer
        // 清理——本 helper 不销毁、不清理、不重试；所有 reason 读取都在对象有效期内。
        ownedHandle = cloneGo;
        Transform cloneRoot = cloneGo.transform;
        if (cloneRoot == null || !SamePointer(cloneRoot.parent, ownedLayer))
        {
            reason = label + "-beach-parent;handle-retained-for-root";
            return false;
        }
        // Instantiate 保真读回：clone 必须先与 source 一致，才允许唯一的 localPosition 写入。
        if (!ApproxVec(cloneRoot.localPosition, sourceLocal) || !ApproxVec(cloneRoot.localScale, sourceScale)
            || !ApproxQuat(cloneRoot.localRotation, sourceRotation))
        {
            reason = label + "-beach-copy-transform:" + Vec(cloneRoot.localPosition) + "/"
                + Vec(cloneRoot.localScale) + ";handle-retained-for-root";
            return false;
        }
        if (!TryComponentShape(cloneGo, out string[] names, out string shapeFailure))
        {
            reason = label + "-beach-clone-components:" + shapeFailure + ";handle-retained-for-root";
            return false;
        }
        if (!SameNameMultiset(names, BeachRootComponentNames))
        {
            reason = label + "-beach-clone-components:" + Join(names) + ";handle-retained-for-root";
            return false;
        }
        Persistent persistent = FindPersistent(cloneGo, out string persistentFailure);
        if (persistent == null)
        {
            reason = label + "-beach-clone-persistent:" + persistentFailure + ";handle-retained-for-root";
            return false;
        }
        if (!persistent.persistObject || persistent.persistInactive || persistent.serializeComponents
            || Convert.ToInt32(persistent.mode) != 0
            || !string.Equals(persistent.path, BeachPrefabPath, StringComparison.Ordinal))
        {
            reason = label + "-beach-clone-persistent-readback:path=" + (persistent.path ?? "null")
                + ";handle-retained-for-root";
            return false;
        }
        // 唯一写入：clone 根 localPosition → (targetX, 0, 0)。
        cloneRoot.localPosition = new Vector3(targetX, 0f, 0f);
        Vector3 applied = cloneRoot.localPosition;
        if (!Approx(applied.x, targetX) || !Approx(applied.y, 0f) || !Approx(applied.z, 0f))
        {
            reason = label + "-beach-position-readback:" + Vec(applied) + ";handle-retained-for-root";
            return false;
        }
        if (!Approx(cloneRoot.localScale.x, expectedScaleX) || !Approx(cloneRoot.localScale.y, 1f)
            || !Approx(cloneRoot.localScale.z, 1f)
            || !ApproxQuat(cloneRoot.localRotation, Quaternion.identity))
        {
            reason = label + "-beach-transform-drift:scale=" + Vec(cloneRoot.localScale)
                + ";handle-retained-for-root";
            return false;
        }
        // source 零写对账：clone 后同一 scan（含根 localPosition）与 clone 前逐字一致。
        if (!TryScanBeachSubtree(sourceGo, label, true, out _, out string sourceAfter, out reason))
            return false;
        if (!string.Equals(sourceBefore, sourceAfter, StringComparison.Ordinal))
        {
            reason = label + "-source-profile-changed;handle-retained-for-root";
            return false;
        }
        // clone 完整已观察 profile（根 localPosition 排除，唯一合法差）：与 source 端点 profile 逐字一致。
        if (!TryScanBeachSubtree(cloneGo, label + "-clone", false, out SubtreeCounts counts,
                out string cloneProfile, out reason))
            return false;
        if (!string.Equals(endpoint.SourceProfile, cloneProfile, StringComparison.Ordinal))
        {
            reason = label + "-clone-profile-mismatch:chars=" + endpoint.SourceProfile.Length
                + "/" + cloneProfile.Length + ";handle-retained-for-root";
            return false;
        }
        if (counts.Tiles != 0 || counts.Grass != 0 || counts.Colliders != BeachSubtreeColliders
            || counts.Nodes != endpoint.SubtreeNodes || counts.Components != endpoint.SubtreeComponents)
        {
            reason = label + "-beach-clone-subtree:nodes=" + counts.Nodes + "/" + endpoint.SubtreeNodes
                + ",components=" + counts.Components + "/" + endpoint.SubtreeComponents
                + ",colliders=" + counts.Colliders + "/" + BeachSubtreeColliders
                + ",tiles=" + counts.Tiles + ",grass=" + counts.Grass + ";handle-retained-for-root";
            return false;
        }
        detail.Append("|").Append(label)
            .Append(":srcX=").Append(F(sourceLocal.x))
            .Append(":dstX=").Append(F(targetX))
            .Append(":scaleX=").Append(F(cloneRoot.localScale.x))
            .Append(":nodes=").Append(counts.Nodes)
            .Append(":components=").Append(counts.Components)
            .Append(":profileChars=").Append(cloneProfile.Length)
            .Append(":go=").Append(Hex(clonePtr));
        Log(label + " beach endpoint cloned: " + SafeName(cloneGo) + " srcX=" + F(sourceLocal.x)
            + " -> " + F(targetX) + " scale=" + Vec(cloneRoot.localScale) + " go=" + Hex(clonePtr)
            + " subtree(nodes=" + counts.Nodes + ",components=" + counts.Components
            + ",colliders=" + counts.Colliders + ",tiles=0,grass=0)"
            + " profile=equal-source;sourceBefore=After;rootPos=" + Vec(applied));
        return true;
    }

    /// <summary>owned 结构终态核验用：exact 名 Beach_greece（剥离 Unity 单次 Clone 后缀）+ typed Beach。</summary>
    internal static bool IsBeachEndpoint(GameObject go)
        => go != null && ExactCloneName(go) == BeachRootName && go.GetComponent<Beach>() != null;

    // ================================================================================
    // 有界遍历 / GameLayer 解析 / 窄工具
    // ================================================================================

    private static bool TryResolveGameLayer(Transform root, string label, out Transform layer, out string reason)
    {
        layer = null;
        reason = "";
        int count = root.childCount;
        if (count > WalkMaxChildren) { reason = label + "-root-child-budget:" + count; return false; }
        int matches = 0;
        for (int i = 0; i < count; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null || child.gameObject == null) { reason = label + "-null-child:" + i; return false; }
            if (!string.Equals(SafeName(child.gameObject), GameLayerName, StringComparison.Ordinal)) continue;
            matches++;
            layer = child;
        }
        if (matches == 0) { reason = label + "-game-layer-missing"; return false; }
        if (matches != 1) { reason = label + "-game-layer-ambiguous:" + matches; return false; }
        return true;
    }

    /// <summary>
    /// 同一次有界遍历产出 totals 与完整已观察 profile：每节点名（单次 Clone 后缀等价）/local transform
    /// （根 localPosition 按参数排除并打标记，其余节点含 pos）/layer/activeSelf/组件 multiset（排序拼接），
    /// 并对已观察 typed 组件附实值：BoxCollider2D(offset/size/enabled/isTrigger/usedByEffector)、
    /// PlatformEffector2D(enabled/useOneWay/useOneWayGrouping/useSideFriction/useSideBounce/surfaceArc/
    /// sideArc/rotationalOffset/useColliderMask/colliderMask)、Persistent(path/mode/persistObject/
    /// persistInactive/serializeComponents)。只读 plain 字段；不读 bounds 等 lazy 值；不写任何对象。
    /// </summary>
    private static bool TryScanBeachSubtree(GameObject rootGo, string label, bool includeRootLocalPosition,
        out SubtreeCounts counts, out string profile, out string reason)
    {
        counts = default;
        profile = null;
        reason = "";
        Transform root = rootGo != null ? rootGo.transform : null;
        if (root == null) { reason = label + "-scan-root-null"; return false; }
        int nodesLeft = WalkMaxNodes;
        var walked = default(SubtreeCounts);
        var visited = new Dictionary<ulong, bool>();
        var sb = new StringBuilder(512);
        if (!ScanNode(root, 0, true, includeRootLocalPosition, ref nodesLeft, visited, ref walked, sb,
                label, out reason))
            return false;
        counts = walked;
        profile = sb.ToString();
        return true;
    }

    private static bool ScanNode(Transform node, int depth, bool isRoot, bool includeRootLocalPosition,
        ref int nodesLeft, Dictionary<ulong, bool> visited, ref SubtreeCounts counts, StringBuilder profile,
        string label, out string reason)
    {
        reason = "";
        if (node == null || node.gameObject == null) { reason = label + "-scan-null-node"; return false; }
        if (depth > WalkMaxDepth) { reason = label + "-scan-depth-budget:" + depth; return false; }
        if (nodesLeft <= 0) { reason = label + "-scan-node-budget"; return false; }
        nodesLeft--;
        ulong ptr = PointerOf(node);
        if (ptr == 0 || visited.ContainsKey(ptr)) { reason = label + "-scan-alias-or-zero"; return false; }
        visited.Add(ptr, false);
        GameObject go = node.gameObject;
        // profile 名称直接读 go.name 并校验真实 null/异常（异常沿现有 outer handler 传播），不 SafeName 吞错。
        string rawName = go.name;
        if (rawName == null) { reason = label + "-scan-null-name"; return false; }
        int children = node.childCount;
        counts.Nodes++;
        if (go.GetComponent<Tile>() != null) counts.Tiles++;
        if (go.GetComponent<Grass>() != null) counts.Grass++;
        if (!TryComponentShape(go, out string[] names, out string shapeFailure))
        {
            reason = label + "-scan-components:" + shapeFailure;
            return false;
        }
        counts.Components += names.Length;
        counts.Colliders += CountColliders(names);
        profile.Append("|n:").Append(StripCloneSuffix(rawName))
            .Append(":d=").Append(depth.ToString(CultureInfo.InvariantCulture))
            .Append(":kids=").Append(children.ToString(CultureInfo.InvariantCulture))
            .Append(":active=").Append(go.activeSelf ? '1' : '0')
            .Append(":layer=").Append(go.layer.ToString(CultureInfo.InvariantCulture));
        if (isRoot && !includeRootLocalPosition) profile.Append(":pos=<root-managed>");
        else profile.Append(":pos=").Append(Vec(node.localPosition));
        profile.Append(":scale=").Append(Vec(node.localScale))
            .Append(":rot=").Append(Quat(node.localRotation));
        if (!AppendObservedComponents(go, names, profile, out reason))
        {
            reason = label + "-observed-components:" + reason;
            return false;
        }
        if (children > WalkMaxChildren) { reason = label + "-scan-children-budget:" + children; return false; }
        for (int i = 0; i < children; i++)
        {
            if (!ScanNode(node.GetChild(i), depth + 1, false, includeRootLocalPosition, ref nodesLeft,
                    visited, ref counts, profile, label, out reason))
                return false;
        }
        return true;
    }

    /// <summary>
    /// 已观察 typed 组件实值（两岸证据字段集；未读字段不猜）。fail-closed：第二次 GetComponents 与
    /// TryComponentShape 的 count/multiset 漂移、null 组件、typed cast 失败、任何字段读取异常都显式
    /// false 并给出 reason（异常沿现有 outer handler 传播）；不用占位/空串/unknown=known 继续。
    /// </summary>
    private static bool AppendObservedComponents(GameObject go, string[] shapeNames, StringBuilder profile,
        out string reason)
    {
        reason = "";
        var components = go.GetComponents<Component>();
        if (components == null || components.Length == 0)
        {
            reason = "second-getcomponents:null-or-empty:len="
                + (components == null ? "-1" : "0");
            return false;
        }
        if (shapeNames == null || components.Length != shapeNames.Length)
        {
            reason = "component-count-drift:" + components.Length + "/"
                + (shapeNames == null ? "-1" : shapeNames.Length.ToString(CultureInfo.InvariantCulture));
            return false;
        }
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null) { reason = "null-component:" + i; return false; }
            string className = IL2CPP.il2cpp_class_get_name_(component.ObjectClass);
            if (className == null || !string.Equals(className, shapeNames[i], StringComparison.Ordinal))
            {
                reason = "component-multiset-drift:" + i + ":" + (className ?? "null")
                    + "/" + shapeNames[i];
                return false;
            }
        }
        // multiset 可比较：排序名拼接（与首次 TryComponentShape 同信息源，已核无漂移）。
        var sorted = (string[])shapeNames.Clone();
        Array.Sort(sorted, StringComparer.Ordinal);
        profile.Append(":components=").Append(string.Join(",", sorted));
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            string className = shapeNames[i];
            if (string.Equals(className, "BoxCollider2D", StringComparison.Ordinal))
            {
                BoxCollider2D box = component.TryCast<BoxCollider2D>();
                if (box == null) { reason = "box-cast-failed:" + i; return false; }
                profile.Append(":box[offset=").Append(Vec2(box.offset))
                    .Append(",size=").Append(Vec2(box.size))
                    .Append(",enabled=").Append(box.enabled ? '1' : '0')
                    .Append(",isTrigger=").Append(box.isTrigger ? '1' : '0')
                    .Append(",usedByEffector=").Append(box.usedByEffector ? '1' : '0')
                    .Append(']');
            }
            else if (string.Equals(className, "PlatformEffector2D", StringComparison.Ordinal))
            {
                PlatformEffector2D effector = component.TryCast<PlatformEffector2D>();
                if (effector == null) { reason = "effector-cast-failed:" + i; return false; }
                profile.Append(":effector[enabled=").Append(effector.enabled ? '1' : '0')
                    .Append(",oneWay=").Append(effector.useOneWay ? '1' : '0')
                    .Append(",oneWayGrouping=").Append(effector.useOneWayGrouping ? '1' : '0')
                    .Append(",sideFriction=").Append(effector.useSideFriction ? '1' : '0')
                    .Append(",sideBounce=").Append(effector.useSideBounce ? '1' : '0')
                    .Append(",surfaceArc=").Append(F(effector.surfaceArc))
                    .Append(",sideArc=").Append(F(effector.sideArc))
                    .Append(",rotOffset=").Append(F(effector.rotationalOffset))
                    .Append(",useMask=").Append(effector.useColliderMask ? '1' : '0')
                    .Append(",mask=").Append(effector.colliderMask.ToString(CultureInfo.InvariantCulture))
                    .Append(']');
            }
            else if (string.Equals(className, PersistentName, StringComparison.Ordinal))
            {
                Persistent persistent = component.TryCast<Persistent>();
                if (persistent == null) { reason = "persistent-cast-failed:" + i; return false; }
                string persistentPath = persistent.path;
                if (persistentPath == null) { reason = "persistent-path-null:" + i; return false; }
                profile.Append(":persist[path=").Append(persistentPath)
                    .Append(",mode=").Append(Convert.ToInt32(persistent.mode)
                        .ToString(CultureInfo.InvariantCulture))
                    .Append(",obj=").Append(persistent.persistObject ? '1' : '0')
                    .Append(",inactive=").Append(persistent.persistInactive ? '1' : '0')
                    .Append(",serialize=").Append(persistent.serializeComponents ? '1' : '0')
                    .Append(']');
            }
        }
        return true;
    }

    private static string Vec2(Vector2 value) => F(value.x) + "," + F(value.y);

    /// <summary>profile 用：对已校验非 null 的原始 go.name 剥离单次 Unity Clone 后缀（fail-closed 源）。</summary>
    private static string StripCloneSuffix(string name)
    {
        const string suffix = "(Clone)";
        return name.EndsWith(suffix, StringComparison.Ordinal)
            ? name.Substring(0, name.Length - suffix.Length) : name;
    }

    private static Persistent FindPersistent(GameObject go, out string failure)
    {
        failure = "";
        var components = go.GetComponents<Component>();
        for (int i = 0; components != null && i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null) { failure = "null-component"; return null; }
            if (string.Equals(IL2CPP.il2cpp_class_get_name_(component.ObjectClass), PersistentName,
                    StringComparison.Ordinal))
                return component.TryCast<Persistent>();
        }
        failure = "persistent-missing";
        return null;
    }


    private static string ExactCloneName(GameObject go)
    {
        string name = SafeName(go);
        const string suffix = "(Clone)";
        return name.EndsWith(suffix, StringComparison.Ordinal)
            ? name.Substring(0, name.Length - suffix.Length) : name;
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

    private static string Quat(Quaternion value)
        => F(value.x) + "," + F(value.y) + "," + F(value.z) + "," + F(value.w);

    private static void Log(string message)
    {
        try { UnityEngine.Debug.Log(Prefix + message); }
        catch (Exception) { }
    }
}
