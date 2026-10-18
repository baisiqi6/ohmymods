// 回岛入口锚工具（源码-only 最小工具，无 hook）：PalaceEntryAnchorData 纯值 DTO + PalaceReturnAnchor
// 只读捕获/复核，供 Root 稍后在既有 trip record / arrival 生命周期接线。本文件不接 Main、不接 trip
// parser、不接 journey；不挂 Harmony hook、无 Update/DontDestroyOnLoad、无任何 position/币/存档写入。
//
// Root 已定契约：
// - TryCapture 要求 paid 调用传入 PalaceVisibleEntry.TryGetCurrentEntry 的 exact payable（内部重新取
//   exact owner/payable 并按 pointer 复核），再核当前 Managers game/kingdom、当前离线希腊源岛（非12）、
//   payer 属当前 Kingdom、各 object 活跃同 scene、entry transform world 坐标有限。付款前资格与结算后
//   复用走同一扇门（方法无状态、可重入）；注册与调用时机由 Root 统一。
// - standX 固定 entryX ± 2 世界单位（大于原 PayDistance=1，小于提示半径 3.5）。side 按 sourcePlayer
//   相对 entry 的左右选择，同点（含相等）确定 side=1；side 仅为旁站方向，不代表来源坐标。首选侧 ground
//   不合法时只读复核另一侧一次；两侧均失败则拒绝 capture，不保存猜测。
// - ground 校验是 PalaceArrivalPlacement.GroundOk 机制的等价私有复刻（契约禁止修改既有 Patch，常量与
//   scene/时序约束一致）：enabled/非trigger/active、与 gameLayer 同 scene、standX±0.5 在 bounds、
//   ClosestPoint world x 误差≤0.05、surfaceY 与玩家 y 差≤0.25、全部 float 有限。
// - ValidateData 只验 DTO 内部不变量（有限/side∈{-1,1}/|(double)StandX-(double)EntryX - Side*2|≤0.05），
//   不能证明回程 scene 仍有原入口。TryValidateDestination 只对当前精确 scene 的 ground/stand 点复核，
//   不写、不读 Start.__instance、不读钱包；Root 在唯一 nativeStart window 之后调用。
// - 没有真实 entry/payable 的免费 QA 路径（Root 显式无锚）；不沿历史 55.87 / source8 默认值补锚。
// - 与 campaignGuid/slot/biome/reign/start/sourceLand/tripId 的绑定由 Root 外层记录负责；DTO 不存
//   native 指针、不把 0 当缺失（0 是合法坐标）；未知锚由 null 引用表示。
// - DTO 六个字段的 JSON 名固定为 lowerCamel（entryX/entryY/entryZ/sourcePlayerX/standX/side），供 Root
//   的 strict System.Text.Json V2 记录（sourceEntryAnchor）嵌套读写；schema 复核在 PalaceTripContext。
// - 工具零日志：TryCapture 可能被 Root 按原生 CanPay 轮询频率调用，全部诊断经 reason 返回，由调用方
//   决定记录，避免状态与输出副作用。
using System;
using System.Globalization;
using System.Text.Json.Serialization;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 回岛入口锚（纯值 DTO）：捕获时刻入口的 world 坐标、付款玩家 x、旁站点与旁站方向。
/// 不存 native 指针；0 是合法坐标值而非“缺失”；未知锚由 null 引用表示。
/// Side 仅为入口旁的站定方向（-1 左 / 1 右），不代表来源坐标。
/// </summary>
internal sealed class PalaceEntryAnchorData
{
    /// <summary>捕获时入口 transform 的 world x。</summary>
    [JsonPropertyName("entryX")] public float EntryX { get; set; }
    /// <summary>捕获时入口 transform 的 world y。</summary>
    [JsonPropertyName("entryY")] public float EntryY { get; set; }
    /// <summary>捕获时入口 transform 的 world z。</summary>
    [JsonPropertyName("entryZ")] public float EntryZ { get; set; }
    /// <summary>捕获（付款资格/结算）时刻付款玩家的 world x。</summary>
    [JsonPropertyName("sourcePlayerX")] public float SourcePlayerX { get; set; }
    /// <summary>旁站点 world x = EntryX + Side * 2。</summary>
    [JsonPropertyName("standX")] public float StandX { get; set; }
    /// <summary>旁站方向，仅 -1 或 1；同点捕获时确定为 1。</summary>
    [JsonPropertyName("side")] public int Side { get; set; }
}

/// <summary>只读锚工具：捕获（TryCapture）、内部不变量（ValidateData）、回程 scene 复核（TryValidateDestination）。</summary>
internal static class PalaceReturnAnchor
{
    private const float StandDistance = 2f;   // 入口旁固定距离：> PayDistance(1)，< 提示半径(3.5)
    private const float StandTol = 0.05f;     // == PalaceArrivalPlacement.AnchorTol
    private const float SurfaceYTol = 0.25f;  // == PalaceArrivalPlacement.NativeTol
    private const float GroundMargin = 0.5f;  // == PalaceArrivalPlacement.GroundOk 的左右余量
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// 只读捕获当前入口锚。paid 调用必须传入与 PalaceVisibleEntry.TryGetCurrentEntry 相同 pointer 的
    /// payable；付款前资格与结算后复用同门。失败时 anchor=null（不保存猜测）。不写 position/币/存档。
    /// </summary>
    internal static bool TryCapture(Game game, Kingdom kingdom, Player payer, PayableComponent payable,
        out PalaceEntryAnchorData anchor, out string reason)
    {
        anchor = null;
        reason = "";
        try
        {
            if (game == null || game.Pointer == IntPtr.Zero || kingdom == null || kingdom.Pointer == IntPtr.Zero
                || payer == null || payer.Pointer == IntPtr.Zero || payable == null || payable.Pointer == IntPtr.Zero)
            { reason = "入参为空"; return false; }
            // exact 当前入口：重新取 owner/payable，传入 payable 必须 pointer 相等。
            if (!PalaceVisibleEntry.TryGetCurrentEntry(out PalaceVisibleEntryOwner owner, out PayableComponent current))
            { reason = "当前无活跃入口（无锚，不生成假来源）"; return false; }
            if (owner == null || owner.Pointer == IntPtr.Zero || current.Pointer != payable.Pointer)
            { reason = "payable 不是当前入口的 exact payable"; return false; }
            Managers m = Managers.Inst;
            if (m == null || m.game == null || m.kingdom == null
                || m.game.Pointer != game.Pointer || m.kingdom.Pointer != kingdom.Pointer)
            { reason = "Game/Kingdom 非当前 Managers 实例"; return false; }
            if (NetworkBigBoss.IsOnline) { reason = "在线世界（非离线源岛）"; return false; }
            if (game.currentLand == PalaceLand12Runtime.LandIndex)
            { reason = "当前已在 land12（非源岛）"; return false; }
            BiomeHolder biome = BiomeHolder.Inst;
            if (biome == null || biome.BiomeIndex != BiomeHolder.GreeceBiomeIndex)
            { reason = "非希腊源岛"; return false; }
            if (!IsKingdomPlayer(kingdom, payer)) { reason = "付款人不属于当前 Kingdom"; return false; }
            GameObject payerObject = payer.gameObject;
            GameObject entryObject = payable.gameObject;
            if (payerObject == null || !payerObject.activeInHierarchy || !payer.isActiveAndEnabled)
            { reason = "付款玩家不可用"; return false; }
            if (entryObject == null || !entryObject.activeInHierarchy || !payable.isActiveAndEnabled)
            { reason = "入口 payable 不可用"; return false; }
            if (payerObject.scene.handle != entryObject.scene.handle)
            { reason = "入口与玩家不在同一场景"; return false; }
            Vector3 entryPos = payable.transform.position;
            Vector3 payerPos = payer.transform.position;
            if (!Finite(entryPos.x) || !Finite(entryPos.y) || !Finite(entryPos.z)
                || !Finite(payerPos.x) || !Finite(payerPos.y) || !Finite(payerPos.z))
            { reason = "entry/玩家 world 坐标非有限"; return false; }
            // side：sourcePlayer 相对 entry 的左右；同点（>= 含相等）确定 side=1，仅为旁站方向。
            int side = payerPos.x >= entryPos.x ? 1 : -1;
            if (!TryStandPoint(m, entryPos.x + side * StandDistance, payerPos.y, out _, out string firstReason))
            {
                int other = -side; // 只读另一侧一次
                if (!TryStandPoint(m, entryPos.x + other * StandDistance, payerPos.y, out _, out string secondReason))
                { reason = "两侧 stand 点均不合法（拒绝，不保存猜测）：" + firstReason + " | " + secondReason; return false; }
                side = other;
            }
            var captured = new PalaceEntryAnchorData
            {
                EntryX = entryPos.x,
                EntryY = entryPos.y,
                EntryZ = entryPos.z,
                SourcePlayerX = payerPos.x,
                StandX = entryPos.x + side * StandDistance,
                Side = side
            };
            if (!ValidateData(captured, out string invariantReason))
            { reason = "捕获结果未过内部不变量：" + invariantReason; return false; }
            anchor = captured;
            reason = "已捕获锚：entry=" + F(entryPos.x) + "/" + F(entryPos.y) + "/" + F(entryPos.z)
                + " sourcePlayerX=" + F(payerPos.x) + " standX=" + F(anchor.StandX) + " side=" + side;
            return true;
        }
        catch (Exception e)
        {
            anchor = null;
            reason = "捕获异常：" + e.GetType().Name;
            return false;
        }
    }

    /// <summary>
    /// 只验 DTO 内部不变量：全坐标有限、side∈{-1,1}、|((double)StandX-(double)EntryX) - Side*2|≤0.05。
    /// 差值用 double 计算：超大 float 下 EntryX+Side*2 会塌缩回 EntryX，只检 float 形式会假通过。
    /// 不能证明回程 scene 已有原入口（入口存在性/重建由 Root 负责）。
    /// </summary>
    internal static bool ValidateData(PalaceEntryAnchorData anchor, out string reason)
    {
        reason = "";
        if (anchor == null) { reason = "锚为 null"; return false; }
        if (!Finite(anchor.EntryX) || !Finite(anchor.EntryY) || !Finite(anchor.EntryZ)
            || !Finite(anchor.SourcePlayerX) || !Finite(anchor.StandX))
        { reason = "坐标含非有限值"; return false; }
        if (anchor.Side != -1 && anchor.Side != 1) { reason = "side 非法：" + anchor.Side; return false; }
        double standDelta = (double)anchor.StandX - (double)anchor.EntryX;
        if (Math.Abs(standDelta - anchor.Side * (double)StandDistance) > StandTol)
        { reason = "standX 与 entryX" + (anchor.Side < 0 ? "-" : "+") + "2 不一致（误差>" + F(StandTol) + "）"; return false; }
        return true;
    }

    /// <summary>
    /// 回程 scene 只读复核：ValidateData + 当前精确 Managers game/kingdom + 当前 scene ground 对 standX
    /// 的同一套脚面校验（surfaceY 对 nativePlayerY 差≤0.25）。不写、不读 Start.__instance、不读钱包。
    /// Root 在唯一 nativeStart window 之后调用。不证明入口已重建。
    /// </summary>
    internal static bool TryValidateDestination(Game game, Kingdom kingdom, PalaceEntryAnchorData anchor,
        float nativePlayerY, out string reason)
    {
        reason = "";
        try
        {
            if (!ValidateData(anchor, out reason)) return false;
            if (game == null || game.Pointer == IntPtr.Zero || kingdom == null || kingdom.Pointer == IntPtr.Zero)
            { reason = "入参为空"; return false; }
            if (!Finite(nativePlayerY)) { reason = "nativePlayerY 非有限"; return false; }
            Managers m = Managers.Inst;
            if (m == null || m.game == null || m.kingdom == null
                || m.game.Pointer != game.Pointer || m.kingdom.Pointer != kingdom.Pointer)
            { reason = "Game/Kingdom 非当前 Managers 实例"; return false; }
            if (!TryStandPoint(m, anchor.StandX, nativePlayerY, out Vector2 surface, out reason))
            { reason = "stand 点复核失败：" + reason; return false; }
            reason = "stand 点复核通过：surface=" + F(surface.x) + "/" + F(surface.y)
                + " standX=" + F(anchor.StandX) + " nativePlayerY=" + F(nativePlayerY);
            return true;
        }
        catch (Exception e)
        {
            reason = "复核异常：" + e.GetType().Name;
            return false;
        }
    }

    /// <summary>
    /// PalaceArrivalPlacement.GroundOk 机制的等价私有复刻（契约禁止修改既有 Patch；常量/顺序/scene 约束
    /// 一致）。只读：enabled/非trigger/active、与 gameLayer 同 scene、x±0.5 在 bounds、ClosestPoint
    /// world x 误差≤0.05、surfaceY 与 y 差≤0.25；x/y 显式有限（NaN 与比较运算会假通过，必须先拒绝）。
    /// </summary>
    private static bool TryStandPoint(Managers m, float x, float y, out Vector2 surface, out string reason)
    {
        surface = Vector2.zero;
        reason = "";
        if (!Finite(x) || !Finite(y)) { reason = "坐标非有限"; return false; }
        BoxCollider2D ground = m.level != null ? m.level.GroundCollider : null;
        if (ground == null || !ground.enabled || ground.isTrigger || !ground.gameObject.activeInHierarchy
            || m.world == null || m.world.gameLayer == null
            || ground.gameObject.scene.handle != m.world.gameLayer.gameObject.scene.handle)
        { reason = "ground collider 不可用或与 gameLayer 不同 scene"; return false; }
        Bounds b = ground.bounds;
        if (!Finite(b.min.x) || !Finite(b.max.x) || !Finite(b.max.y)
            || x - GroundMargin < b.min.x || x + GroundMargin > b.max.x)
        { reason = "stand 超出 ground 范围"; return false; }
        Vector2 closest = ground.ClosestPoint(new Vector2(x, b.max.y + 2f));
        if (!Finite(closest.x) || !Finite(closest.y) || Math.Abs(closest.x - x) > StandTol
            || Math.Abs(y - closest.y) > SurfaceYTol)
        { reason = "ClosestPoint/surfaceY 不合格"; return false; }
        surface = closest;
        return true;
    }

    // 与 PalaceIslandTravel.IsKingdomPlayer 相同语义的私有复刻（该方法为 private，无法直接调用）。
    private static bool IsKingdomPlayer(Kingdom kingdom, Player payer)
    {
        Player p1 = kingdom.playerOne;
        Player p2 = kingdom.playerTwo;
        return (p1 != null && p1.Pointer == payer.Pointer) || (p2 != null && p2.Pointer == payer.Pointer);
    }

    private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    private static string F(float v) => v.ToString("0.###", Inv);
}
