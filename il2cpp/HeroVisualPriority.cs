// 英雄视觉置前·共享深度规则（operator 2026-09-15 契约 + 同日纠正轮）。
//
// 目标：多个角色重叠时英雄显示在普通单位之前；规则对全部英雄通用（弓箭手是第一个使用方）。
// 用户偏好「英雄最前」：共享平面取 gameLayer 本地 z=-0.05（越小越靠前），优先于已查到的普通 NPC
// （含 Knight_greece prefab z=0）；会盖住重叠场景中的建筑前景，不宣称保证任何未知第三方都在英雄之后。
// 原生 Default 唯一 sortingLayer、多数 sortingOrder=0，靠 z + depth shader（QUEUE Transparent / ZWrite1 /
// ZTest4）排序；因此不动 sortingLayer/order、不建新材质/全局排序、不动 UI，只挪「MOD 自有表现
// transform」的世界 z。
//
// 契约要点：
// - 纯数值规则 + 安全应用：只写 MOD 自有的 body/cloth transform 的 z；绝不写 actor、原生 renderer
//   transform、x/y、物理/AI、sortingLayer/order、材质、UI；不新增 native 钩子/扫描。
// - 坐标门（仅支持轴对齐、单位深度缩放的参考层）：读 X/Y/Z 三个基向量的世界映射——
//   拒绝 z 翻转（pz.z<=0）、倾斜/shear（pz 漏 x/y 或 px/py 漏 z）、非单位深度缩放（pz.z≠1）；
//   接受 xy 缩放与绕 Z 旋转。参考层自身 x/y 位移不影响目标位置（只取世界 z）。
// - 写入语义：先读并校验**全部** body/cloth 世界位置（非有限 → 写前拒绝，一个都不动），再写；
//   写前保存快照，任一 setter 异常立即尝试恢复所有已动过的自有 z；恢复也失败 → 返回 false 交调用方
//   整组撤销自有表现（caller 走 Rollback/RemoveByKey，原生还原由既有逻辑保证），不建无界账本。
// - 幂等：重复应用结果稳定相等（同英雄多帧、多英雄共用同一平面），不随机抖动。
// - 布料跟随：cloth root 世界 z = body 世界 z + 微小后偏移（更大 = 更靠后）；caller 在 Cloth.Tick /
//   ApplyFlip 之后每帧覆盖，同时在 Visuals 接线处把自有 cloth 渲染器的 sorting 与自有 body 对齐
//   （同队列内按深度排序，见 HeroArcherVisuals.ApplyHeroPlane）。

using System;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 英雄共享视觉深度：所有 MOD 英雄表现统一压到 gameLayer 本地 z=HeroPlaneLocalZ 的世界平面。
/// 无状态、无 Unity 场景访问、无 native 钩子；只依赖传入的 Transform（只读 gameLayer、只写自有对象）。
/// </summary>
internal static class HeroVisualPriority
{
    /// <summary>共享英雄视觉平面的参考层本地 z（gameLayer 本地坐标；越小越靠前。原 .25 备选可恢复）。</summary>
    internal const float HeroPlaneLocalZ = -0.05f;

    /// <summary>布料相对身体的世界 z 后偏移：更大 = 更靠后，保证飘带永远画在身体之后。</summary>
    internal const float ClothBackOffset = 0.002f;

    /// <summary>基向量分量/深度缩放的容差（倾斜、shear、非单位深度缩放的判定门限）。</summary>
    private const float BasisTolerance = 1e-3f;

    /// <summary>
    /// 纯数值规则：参考层本地 (0,0,HeroPlaneLocalZ) 映射出的世界 z。
    /// 坐标门：只接受轴对齐、单位深度缩放的参考层（xy 缩放/绕 Z 旋转可接受）。
    /// 读 X/Y/Z 基向量映射：pz 漏 x/y（倾斜/绕X/绕Y/shear）、px/py 漏 z（倾斜/shear）、pz.z 非正（翻转）、
    /// pz.z≠1（非单位深度缩放）任一命中即拒绝。结果非有限同样拒绝。失败 → false，调用方保持原外观深度。
    /// </summary>
    internal static bool TryGetHeroPlaneWorldZ(Transform gameLayer, out float planeWorldZ)
    {
        planeWorldZ = 0f;
        if (gameLayer == null) return false;
        try
        {
            Vector3 origin = gameLayer.TransformPoint(Vector3.zero);
            Vector3 px = gameLayer.TransformPoint(new Vector3(1f, 0f, 0f));
            Vector3 py = gameLayer.TransformPoint(new Vector3(0f, 1f, 0f));
            Vector3 pz = gameLayer.TransformPoint(new Vector3(0f, 0f, 1f));
            if (!IsFinite(origin) || !IsFinite(px) || !IsFinite(py) || !IsFinite(pz)) return false;

            // 倾斜 / 绕X / 绕Y / shear：深度基向量不得横漏，横向基向量不得纵漏。
            if (Math.Abs(pz.x - origin.x) > BasisTolerance || Math.Abs(pz.y - origin.y) > BasisTolerance) return false;
            if (Math.Abs(px.z - origin.z) > BasisTolerance || Math.Abs(py.z - origin.z) > BasisTolerance) return false;

            // z 翻转与非单位深度缩放（xy 缩放不影响深度门）。
            float depthScale = pz.z - origin.z;
            if (!(depthScale > BasisTolerance) || Math.Abs(depthScale - 1f) > BasisTolerance) return false;

            planeWorldZ = origin.z + HeroPlaneLocalZ * depthScale;
            return float.IsFinite(planeWorldZ);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 安全应用：把 MOD 自有 body 的**世界 z** 设为共享英雄平面 z（世界 x/y 原样保留），并把 MOD 自有
    /// cloth root 的世界 z 设为 body 平面 z + ClothBackOffset（xy/缩放/旋转不动）。
    /// 先读并校验全部目标位置（非有限 → 写前拒绝、不动任何目标），写前保存快照，任一 setter 异常立即
    /// 尽力恢复所有已动过的自有 z；任何失败返回 false，调用方（Visuals）对 false 走 Rollback/RemoveByKey
    /// 整组撤销自有表现并归还原生可见性——绝不带着半套深度/被移动过的自有对象继续跑。
    /// body 可单独使用（无布料的未来英雄传 clothRoot=null）。
    /// </summary>
    internal static bool ApplyHeroDepth(Transform gameLayer, Transform body, Transform clothRoot)
    {
        if (!TryGetHeroPlaneWorldZ(gameLayer, out float planeZ)) return false;
        try
        {
            if (body == null) return false;

            // 写前读全 + 校验全：cloth 非有限时 body 也不写。
            Vector3 bodyOld = body.position;
            if (!IsFinite(bodyOld)) return false;
            bool hasCloth = clothRoot != null;
            Vector3 clothOld = Vector3.zero;
            if (hasCloth)
            {
                clothOld = clothRoot.position;
                if (!IsFinite(clothOld)) return false;
            }

            bool bodyMoved = false;
            bool clothMoved = false;
            try
            {
                bodyMoved = true; // 先立旗再写：setter 部分生效也按「已动过」恢复
                body.position = new Vector3(bodyOld.x, bodyOld.y, planeZ);
                if (hasCloth)
                {
                    clothMoved = true;
                    clothRoot.position = new Vector3(clothOld.x, clothOld.y, planeZ + ClothBackOffset);
                }
                return true;
            }
            catch (Exception)
            {
                // 快照恢复所有已动过的自有 z；恢复失败无法在此完成归还 → 返回 false，
                // 由调用方整组销毁自有表现（原生还原由既有 Rollback/RemoveByKey 逻辑保证），不建账本。
                TryRestore(body, bodyOld, bodyMoved);
                TryRestore(clothRoot, clothOld, clothMoved);
                return false;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void TryRestore(Transform target, Vector3 snapshot, bool moved)
    {
        if (!moved || target == null) return;
        try
        {
            target.position = snapshot;
        }
        catch (Exception)
        {
            // 恢复失败：保持「已失败」语义交调用方整组撤销；这里不再向上抛（避免掩盖原始失败）。
        }
    }

    private static bool IsFinite(Vector3 v)
    {
        return float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
