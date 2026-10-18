// R4 卷轴 native 标定绑定（source-only）：唯一调用处/时机 = PalaceCourtScene.Create → CourtBinding 的创建窗口。
// 同一同步窗口读一次当次数值：Managers.Inst/level/world/gameLayer + exact Level.GroundCollider +
// PalaceTerrainRuntime 登记的 TileOwner 像素网格；产出唯一 PalaceScrollCalibration（三主层共享 1448×252 原点、
// 锚 x=645、脚线 y=187；rear 零视差由消费方显式处理），并以 WorldX(0/645/1448) 做三点显式创建位置断言
// （每点恰一次 ClosestPoint，非场景扫描）。
// 无 cache / 全局可变状态 / wait / retry / fallback：任一门不过即抛 InvalidOperationException 拒绝整批——
// 不缩小 s、不挪锚、不做 cover 补偿；绝不用常数 PPU32 / 45.7142857 / 19px / 112u 反推 s。
// 边界：不读成人 BODY / Camera / 水面数据（骑乘 renderer 不是成年人体标尺），不创建/激活/销毁 GO、不写任何
// Native/存档/玩家/坐骑状态，不安装 hook、不做周期扫描、不启动/操作游戏。Native 屏幕验收不在本文件声明。
// Enabled = PALACE_SCROLL_R4_PRIVATE=1 且 land12 / island-content / terrain 三条既有私有门全部成立；调用处负责 gate。
using System;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class PalaceScrollNativeBinding
{
    internal static bool Enabled
        => PalaceRuntimeProfile.Capability("PALACE_SCROLL_R4_PRIVATE")
            && PalaceLand12Runtime.Enabled
            && PalaceIslandContentPolicy.Enabled
            && PalaceTerrainRuntime.Enabled;

    /// <summary>
    /// 创建处单次标定：只读，无副作用。父层漂移 / 地面不可用 / 像素网格获取失败 / 三点断言不成立 →
    /// InvalidOperationException（整批拒绝，无 fallback；调用处按既有 Create catch 回滚自有对象）。
    /// </summary>
    internal static PalaceScrollCalibration Calibrate(CourtBinding binding, float axisWorldX, float groundWorldY)
    {
        Managers managers = Managers.Inst;
        if (managers == null || managers.level == null || managers.world == null || managers.world.gameLayer == null)
            throw new InvalidOperationException("R4 scroll calibration owner unavailable: Managers/level/world/gameLayer missing");
        if (binding == null || binding.Parent == null)
            throw new InvalidOperationException("R4 scroll calibration binding unavailable: owned parent missing");
        Transform layer = managers.world.gameLayer;
        Transform parent = binding.Parent.parent;
        if (parent == null || parent.Pointer != layer.Pointer)
            throw new InvalidOperationException("R4 scroll calibration parent is not the current gameLayer");
        if (binding.Parent.gameObject.scene.handle != layer.gameObject.scene.handle)
            throw new InvalidOperationException("R4 scroll calibration parent scene differs from the current gameLayer scene");

        if (!Finite(axisWorldX) || !Finite(groundWorldY))
            throw new InvalidOperationException("R4 anchor coordinates are not finite");
        if (!PalaceCourtArea.TryGetSpan(managers.level, out float courtLeft, out float courtRight, out string courtReason))
            throw new InvalidOperationException("R4 requires the verified owned court span; no camp fallback: " + courtReason);
        float courtAxis = PalaceCourtArea.RenderAxis(courtLeft, courtRight);
        if (Math.Abs(courtAxis - axisWorldX) > .02f)
            throw new InvalidOperationException("R4 axis differs from the current owned court span");

        BoxCollider2D ground = managers.level.GroundCollider;
        if (ground == null || !ground.enabled || ground.isTrigger || ground.gameObject == null
            || !ground.gameObject.activeInHierarchy
            || ground.gameObject.scene.handle != layer.gameObject.scene.handle)
            throw new InvalidOperationException("R4 scroll calibration exact ground collider unavailable");
        Bounds bounds = ground.bounds;
        if (!Finite(bounds.min.x) || !Finite(bounds.max.x) || !Finite(bounds.size.x) || bounds.size.x <= 0f
            || !Finite(bounds.min.y) || !Finite(bounds.max.y) || !Finite(bounds.size.y) || bounds.size.y <= 0f)
            throw new InvalidOperationException("R4 scroll calibration ground bounds invalid");

        if (!PalaceTerrainRuntime.TryReadScrollPixelGrid(layer, axisWorldX, out double unitsPerPixel, out string evidence))
            throw new InvalidOperationException("R4 scroll pixel grid unavailable: " + (evidence ?? "(no evidence)"));

        if (PalaceCourtArea.DedicatedWholeEnabled && unitsPerPixel != 1d / 32d)
            throw new InvalidOperationException("Schema2 physical recipe and current native pixel grid disagree");
        var calibration = new PalaceScrollCalibration(unitsPerPixel, axisWorldX, groundWorldY);
        calibration.RequireUnityCanvas();

        float probeY = bounds.max.y + 2f;
        // The complete art includes20px of decorative shore beyond each physical edge in schema2.
        // Probe the owned physical endpoints; retain art scale/origin and viewport exposure guards unchanged.
        double leftPixel = PalaceCourtArea.DedicatedWholeEnabled ? 645d + (courtLeft - axisWorldX) / unitsPerPixel : 0d;
        double rightPixel = PalaceCourtArea.DedicatedWholeEnabled ? 645d + (courtRight - axisWorldX) / unitsPerPixel : 1448d;
        string point0 = Probe(ground, bounds, calibration.WorldX(leftPixel), probeY, groundWorldY, "physicalLeftPixel=" + leftPixel.ToString("R"));
        string pointAxis = Probe(ground, bounds, calibration.WorldX(645d), probeY, groundWorldY, "masterPixelX=645");
        string pointRight = Probe(ground, bounds, calibration.WorldX(rightPixel), probeY, groundWorldY, "physicalRightPixel=" + rightPixel.ToString("R"));

        PalaceCourtVisuals.Log("[PalaceScrollR4] calibrated unitsPerPixel=" + unitsPerPixel.ToString("R")
            + " pixelsPerUnit=" + calibration.PixelsPerUnit.ToString("R")
            + " axisWorldX=" + axisWorldX.ToString("R") + " groundWorldY=" + groundWorldY.ToString("R")
            + " canvas=1448x252 axisPixelX=645 footlinePixelY=187"
            + " probes[" + point0 + " " + pointAxis + " " + pointRight + "]"
            + " evidence={" + (evidence ?? "(no evidence)") + "}"
            + "; environment ground pixel grid; adult BODY/camera world width/native water plane remain unmeasured; no actors placed; Native screen pending");
        return calibration;
    }

    /// <summary>显式创建位置断言（每点恰一次 ClosestPoint）：须在当次 collider.bounds.x 范围内且返回值 finite、贴轴、贴传入 groundWorldY；否则拒绝整批。</summary>
    private static string Probe(BoxCollider2D ground, Bounds bounds, double worldX, float probeY, float groundWorldY, string label)
    {
        if (!(worldX >= bounds.min.x && worldX <= bounds.max.x))
            throw new InvalidOperationException("R4 scroll " + label + " outside current ground: worldX=" + worldX.ToString("R")
                + " groundX=[" + bounds.min.x.ToString("R") + ".." + bounds.max.x.ToString("R") + "]");
        float x = (float)worldX;
        if (!Finite(x))
            throw new InvalidOperationException("R4 scroll " + label + " world X is not representable as a finite float");
        Vector2 point = ground.ClosestPoint(new Vector2(x, probeY));
        if (!Finite(point.x) || !Finite(point.y))
            throw new InvalidOperationException("R4 scroll " + label + " closest point not finite");
        float dx = Math.Abs(point.x - x), dy = Math.Abs(point.y - groundWorldY);
        if (dx > 0.01f || dy > 0.02f)
            throw new InvalidOperationException("R4 scroll " + label + " rejected: point=(" + point.x.ToString("R") + ","
                + point.y.ToString("R") + ") dx=" + dx.ToString("R") + " dy=" + dy.ToString("R")
                + " exceeds dx<=0.01 dy<=0.02; whole batch rejected without shrinking s, moving the anchor or cover compensation");
        return label + "=(" + point.x.ToString("R") + "," + point.y.ToString("R") + ")";
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
