// HeroVisualPriority 窄行为回归（纯 helper + Unity 替身；真实 interop 由 operator 构建）。
// 运行： C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/hero-visual-priority/HeroVisualPriority.Tests.csproj
//
// 局限（源接线级，无法在替身里可靠实测）：
// - 「跨帧参考层失效 → caller RemoveByKey/Rollback 撤销整组自有表现」的完整链路在
//   HeroArcherVisuals（依赖 Archer/Managers interop），本测试只覆盖 helper 层 TryGet/Apply 返回 false；
//   接线正确性以 HeroArcherVisuals.Sync/Apply 中 `if (!ApplyHeroPlane(...)) { RemoveByKey/Rollback }`
//   的源码审查为准，真实行为由 operator 实机构建验证。

using System;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        PlaneZFromPlainOffsetGameLayer();
        XyScaleAndRotationAboutZAccepted();
        NonUnitDepthScaleRejected();
        TiltShearFlipAndNonFiniteRejected();
        TwoBodiesOnOneLayerShareDepthKeepingXYAndScale();
        ClothInvalidRejectedBeforeAnyWrite();
        ClothSetterFailureRestoresBodySnapshot();
        NoClothFutureHeroStillWorks();
        RepeatedApplicationDoesNotAccumulate();

        Console.WriteLine();
        Console.WriteLine(_failed == 0
            ? "ALL PRIORITY TESTS PASSED (" + _passed + ")"
            : _failed + " PRIORITY TEST(S) FAILED, " + _passed + " passed");
        return _failed == 0 ? 0 : 1;
    }

    // 场景骨架：worldRoot → gameLayer（可平移/缩放/旋转）→ anchor（原生 renderer 挂点）→ body / cloth 同级。
    private sealed class Fixture
    {
        internal Transform WorldRoot;
        internal Transform GameLayer;
        internal Transform Anchor;
        internal Transform Body;
        internal Transform Cloth;

        internal static Fixture Create()
        {
            var f = new Fixture();
            f.WorldRoot = new Transform(new GameObject("world"));
            f.GameLayer = new Transform(new GameObject("gameLayer"));
            f.GameLayer.SetParent(f.WorldRoot, false);
            f.Anchor = new Transform(new GameObject("anchor"));
            f.Anchor.SetParent(f.GameLayer, false);
            f.Anchor.localPosition = new Vector3(3f, 1f, -0.4f);
            f.Body = new Transform(new GameObject("body"));
            f.Body.SetParent(f.Anchor, false);
            f.Cloth = new Transform(new GameObject("cloth"));
            f.Cloth.SetParent(f.Anchor, false);
            f.Cloth.localPosition = new Vector3(-0.15625f, 0.4375f, 0f);
            return f;
        }
    }

    private static void PlaneZFromPlainOffsetGameLayer()
    {
        Fixture f = Fixture.Create();
        f.WorldRoot.localPosition = new Vector3(100f, -5f, -3f);
        float plane;
        bool ok = HeroVisualPriority.TryGetHeroPlaneWorldZ(f.GameLayer, out plane);
        Check("PlaneZFromPlainOffsetGameLayer.ok", ok, "rejected a plain offset gameLayer");
        // gameLayer 世界 z = -3；共享平面 = 本地 z=-0.05（英雄最前，优先于普通 NPC z≈0）。
        Check("PlaneZFromPlainOffsetGameLayer.value", Near(plane, -3.05f), "plane=" + plane + " expected -3.05");
        Check("PlaneZFromPlainOffsetGameLayer.inFrontOfZero",
            plane < -3f && plane < 0f, "plane must sit in front of native z=0 prefabs");
    }

    private static void XyScaleAndRotationAboutZAccepted()
    {
        // xy 缩放：深度门不受影响。
        Fixture f = Fixture.Create();
        f.WorldRoot.localScale = new Vector3(3f, 2f, 1f);
        f.GameLayer.localPosition = new Vector3(0f, 0f, -1f);
        float plane;
        bool ok = HeroVisualPriority.TryGetHeroPlaneWorldZ(f.GameLayer, out plane);
        Check("XyScaleAndRotationAboutZAccepted.xyScaleOk", ok, "xy scale must be accepted");
        Check("XyScaleAndRotationAboutZAccepted.xyScaleValue", Near(plane, -1.05f), "plane=" + plane + " expected -1.05");

        // 绕 Z 旋转：横向基向量留在 xy 平面内，接受。
        f = Fixture.Create();
        f.GameLayer.localPosition = new Vector3(0f, 0f, 2f);
        f.GameLayer.localRotation = Quaternion.AngleAxis(45f, new Vector3(0f, 0f, 1f));
        ok = HeroVisualPriority.TryGetHeroPlaneWorldZ(f.GameLayer, out plane);
        Check("XyScaleAndRotationAboutZAccepted.rotZOk", ok, "rotation about Z must be accepted");
        Check("XyScaleAndRotationAboutZAccepted.rotZValue", Near(plane, 1.95f), "plane=" + plane + " expected 1.95");
    }

    private static void NonUnitDepthScaleRejected()
    {
        Fixture f = Fixture.Create();
        f.WorldRoot.localScale = new Vector3(1f, 1f, 2f); // 深度方向非单位缩放
        Check("NonUnitDepthScaleRejected.zScale2",
            !HeroVisualPriority.TryGetHeroPlaneWorldZ(f.GameLayer, out _), "non-unit z depth scale must be rejected");
        f.WorldRoot.localScale = new Vector3(1f, 1f, 0.5f);
        Check("NonUnitDepthScaleRejected.zScaleHalf",
            !HeroVisualPriority.TryGetHeroPlaneWorldZ(f.GameLayer, out _), "non-unit z depth scale (0.5) must be rejected");
    }

    private static void TiltShearFlipAndNonFiniteRejected()
    {
        Fixture f = Fixture.Create();
        f.GameLayer.localPosition = new Vector3(float.NaN, 0f, 0f);
        Check("TiltShearFlipAndNonFiniteRejected.naN",
            !HeroVisualPriority.TryGetHeroPlaneWorldZ(f.GameLayer, out _), "NaN layer must be rejected");
        f.GameLayer.localPosition = Vector3.zero;

        f.GameLayer.localRotation = Quaternion.AngleAxis(30f, new Vector3(0f, 1f, 0f)); // 绕 Y 倾斜
        Check("TiltShearFlipAndNonFiniteRejected.tiltY",
            !HeroVisualPriority.TryGetHeroPlaneWorldZ(f.GameLayer, out _), "rotation about Y must be rejected");
        f.GameLayer.localRotation = Quaternion.AngleAxis(20f, new Vector3(1f, 0f, 0f)); // 绕 X 倾斜/shear
        Check("TiltShearFlipAndNonFiniteRejected.tiltX",
            !HeroVisualPriority.TryGetHeroPlaneWorldZ(f.GameLayer, out _), "rotation about X must be rejected");
        f.GameLayer.localRotation = Quaternion.identity;

        f.GameLayer.localScale = new Vector3(1f, 1f, -1f); // z 翻转
        Check("TiltShearFlipAndNonFiniteRejected.flipped",
            !HeroVisualPriority.TryGetHeroPlaneWorldZ(f.GameLayer, out _), "z-flipped layer must be rejected");
        f.GameLayer.localScale = Vector3.one;
    }

    private static void TwoBodiesOnOneLayerShareDepthKeepingXYAndScale()
    {
        // 未来英雄复用：同一 gameLayer 下、不同世界偏移挂点的两名英雄 → 同一共享平面深度。
        Fixture f = Fixture.Create();
        f.WorldRoot.localPosition = new Vector3(20f, 0f, -3f);
        f.Anchor.localScale = new Vector3(2f, 2f, 1f); // 父层缩放：只许影响局部折算，不许影响世界平面
        Transform anchorB = new Transform(new GameObject("anchorB"));
        anchorB.SetParent(f.GameLayer, false);
        anchorB.localPosition = new Vector3(-2f, 0f, 0.9f);
        Transform bodyB = new Transform(new GameObject("bodyB"));
        bodyB.SetParent(anchorB, false);
        Transform clothB = new Transform(new GameObject("clothB"));
        clothB.SetParent(anchorB, false);
        clothB.localPosition = new Vector3(0.2f, 0.4f, 0f);

        Vector3 body0 = f.Body.position, cloth0 = f.Cloth.position;
        Vector3 bodyB0 = bodyB.position, clothB0 = clothB.position;
        bool okA = HeroVisualPriority.ApplyHeroDepth(f.GameLayer, f.Body, f.Cloth);
        bool okB = HeroVisualPriority.ApplyHeroDepth(f.GameLayer, bodyB, clothB);
        Check("TwoBodiesOnOneLayerShareDepthKeepingXYAndScale.ok", okA && okB, "apply failed");

        float plane = f.GameLayer.TransformPoint(new Vector3(0f, 0f, HeroVisualPriority.HeroPlaneLocalZ)).z;
        Check("TwoBodiesOnOneLayerShareDepthKeepingXYAndScale.sameDepth",
            Near(f.Body.position.z, plane) && Near(bodyB.position.z, plane),
            "bodies not on shared plane: " + f.Body.position.z + " / " + bodyB.position.z + " expected " + plane);
        Check("TwoBodiesOnOneLayerShareDepthKeepingXYAndScale.bodyXY",
            Near(f.Body.position.x, body0.x) && Near(f.Body.position.y, body0.y)
            && Near(bodyB.position.x, bodyB0.x) && Near(bodyB.position.y, bodyB0.y),
            "world xy changed");
        Check("TwoBodiesOnOneLayerShareDepthKeepingXYAndScale.clothXY",
            Near(f.Cloth.position.x, cloth0.x) && Near(f.Cloth.position.y, cloth0.y)
            && Near(clothB.position.x, clothB0.x) && Near(clothB.position.y, clothB0.y),
            "cloth xy changed");
        Check("TwoBodiesOnOneLayerShareDepthKeepingXYAndScale.clothBehindBody",
            Near(f.Cloth.position.z, plane + HeroVisualPriority.ClothBackOffset)
            && Near(clothB.position.z, plane + HeroVisualPriority.ClothBackOffset),
            "cloth must sit at body plane + back offset");
        Check("TwoBodiesOnOneLayerShareDepthKeepingXYAndScale.scaleUntouched",
            Near(f.Cloth.localScale.x, 1f) && Near(f.Body.localScale.x, 1f),
            "local scale was modified");
        Check("TwoBodiesOnOneLayerShareDepthKeepingXYAndScale.nativeAnchorUntouched",
            Near(f.Anchor.localPosition.z, -0.4f), "anchor (native renderer transform) local z changed");
    }

    private static void ClothInvalidRejectedBeforeAnyWrite()
    {
        Fixture f = Fixture.Create();
        f.WorldRoot.localPosition = new Vector3(0f, 0f, -3f);
        f.Cloth.localPosition = new Vector3(0f, 0f, float.NaN); // cloth 位置非有限
        Vector3 body0 = f.Body.position;
        bool ok = HeroVisualPriority.ApplyHeroDepth(f.GameLayer, f.Body, f.Cloth);
        Check("ClothInvalidRejectedBeforeAnyWrite.rejected", !ok, "non-finite cloth must be rejected");
        Check("ClothInvalidRejectedBeforeAnyWrite.bodyUntouched",
            Near(f.Body.position.z, body0.z) && Near(f.Body.position.x, body0.x) && Near(f.Body.position.y, body0.y),
            "body was written even though cloth was invalid pre-write");
    }

    private static void ClothSetterFailureRestoresBodySnapshot()
    {
        Fixture f = Fixture.Create();
        f.WorldRoot.localPosition = new Vector3(0f, 0f, -3f);
        Vector3 body0 = f.Body.position;
        Vector3 cloth0 = f.Cloth.position;
        f.Cloth.ThrowOnNextPositionSet = true; // cloth setter 一次性失败桩
        bool ok = HeroVisualPriority.ApplyHeroDepth(f.GameLayer, f.Body, f.Cloth);
        Check("ClothSetterFailureRestoresBodySnapshot.failed", !ok, "cloth setter failure must report false");
        Check("ClothSetterFailureRestoresBodySnapshot.bodyRestored",
            Near(f.Body.position.x, body0.x) && Near(f.Body.position.y, body0.y) && Near(f.Body.position.z, body0.z),
            "body snapshot not restored: " + f.Body.position + " expected " + body0);
        Check("ClothSetterFailureRestoresBodySnapshot.clothUntouched",
            Near(f.Cloth.position.x, cloth0.x) && Near(f.Cloth.position.y, cloth0.y) && Near(f.Cloth.position.z, cloth0.z),
            "cloth should not have moved (its setter failed)");
    }

    private static void NoClothFutureHeroStillWorks()
    {
        Fixture f = Fixture.Create();
        f.WorldRoot.localPosition = new Vector3(0f, 0f, -3f);
        float plane = f.GameLayer.TransformPoint(new Vector3(0f, 0f, HeroVisualPriority.HeroPlaneLocalZ)).z;
        bool ok = HeroVisualPriority.ApplyHeroDepth(f.GameLayer, f.Body, null);
        Check("NoClothFutureHeroStillWorks.ok", ok, "apply without cloth failed");
        Check("NoClothFutureHeroStillWorks.depth", Near(f.Body.position.z, plane),
            "body z=" + f.Body.position.z + " expected " + plane);
    }

    private static void RepeatedApplicationDoesNotAccumulate()
    {
        Fixture f = Fixture.Create();
        f.WorldRoot.localPosition = new Vector3(0f, 0f, -3f);
        HeroVisualPriority.ApplyHeroDepth(f.GameLayer, f.Body, f.Cloth);
        Vector3 body1 = f.Body.position;
        Vector3 cloth1 = f.Cloth.position;
        HeroVisualPriority.ApplyHeroDepth(f.GameLayer, f.Body, f.Cloth);
        HeroVisualPriority.ApplyHeroDepth(f.GameLayer, f.Body, f.Cloth);
        Check("RepeatedApplicationDoesNotAccumulate.body",
            Near(f.Body.position.x, body1.x) && Near(f.Body.position.y, body1.y) && Near(f.Body.position.z, body1.z),
            "body depth drifted: " + body1 + " -> " + f.Body.position);
        Check("RepeatedApplicationDoesNotAccumulate.cloth",
            Near(f.Cloth.position.z, cloth1.z), "cloth depth drifted: " + cloth1.z + " -> " + f.Cloth.position.z);
    }

    // ============================================================

    private static bool Near(float a, float b) => Math.Abs(a - b) < 1e-4f;

    private static void Check(string name, bool condition, string detail)
    {
        if (condition) { _passed++; Console.WriteLine("PASS " + name); }
        else { _failed++; Console.WriteLine("FAIL " + name + " :: " + detail); }
    }
}
