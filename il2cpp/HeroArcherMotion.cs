using System;

namespace KingdomEnhancedMod;

/// <summary>
/// 纯逻辑移动判据 + 0.9 视觉缩放常量（worker 2026-09-15 slice，无 Unity 依赖，可被测试直接链接）。
///
/// 背景（已核代码事实）：原生 Mover.SetSpeed 主动写 _movingToGoal=false，但 _moveSpeed 仍非 0，
/// ActualSpeed = _moveSpeed * _multiplier —— goal 标记不能代表「实际是否在移动」。
/// 旧 HeroArcherVisuals.MotionOf 以该标记为门，存在移动中误判 Idle 的路径；实测现象仍需帧日志验证。
/// 优先使用原生动画 Speed（Mover 暂停时会归零），读取失败才用指令速度；不使用 goal 标记。
/// </summary>
internal static class HeroArcherMotion
{
    /// <summary>速度死区：|speed| &lt;= 该值视为停下（配合动画状态机的同值 no-op，walk/run 边界不抖动重置相位）。</summary>
    internal const float WalkSpeedEpsilon = 0.05f;

    /// <summary>自有 body/cloth 的整体表现缩放：绝对值断言用（脚点 pivot 缩小；z 保持 1 不动深度）。</summary>
    internal const float VisualScale = 0.9f;

    /// <summary>布料像素→单位换算，与 HeroArcherClothMath.PixelsPerUnit 同源规则（1px = 1/32）。</summary>
    internal const float ClothPixelsPerUnit = 32f;

    /// <summary>肩部锚点像素偏移，与 HeroArcherCloth 的 ShoulderOffsetPixelsX（-5）/肩高 14px 同源规则。</summary>
    internal const float ShoulderOffsetPixelsX = -5f;
    internal const float ShoulderHeightPixels = 14f;

    internal static float SelectSpeed(float animationSpeed, bool animationAvailable, float commandSpeed)
        => animationAvailable && float.IsFinite(animationSpeed) ? animationSpeed
            : float.IsFinite(commandSpeed) ? commandSpeed : 0f;

    /// <summary>
    /// 由实际速度分 Idle/Walk/Run：不看 _movingToGoal（SetSpeed 路径下它为 false 但速度仍有效）。
    /// NaN/±Inf → Idle；负速度（向左移动）合法，取绝对值；walkSpeed 非法/非正时只分 Idle/Walk。
    /// 有界切换：超过 walkSpeed + epsilon 才切 Run，同值 SetMotion 由动画状态机 no-op，不重置走路相位。
    /// </summary>
    internal static HeroArcherAnimation Classify(float actualSpeed, float walkSpeed)
    {
        if (!float.IsFinite(actualSpeed)) return HeroArcherAnimation.Idle;
        float magnitude = Math.Abs(actualSpeed);
        if (magnitude <= WalkSpeedEpsilon) return HeroArcherAnimation.Idle;
        if (float.IsFinite(walkSpeed) && walkSpeed > 0f && magnitude > walkSpeed + WalkSpeedEpsilon)
            return HeroArcherAnimation.Run;
        return HeroArcherAnimation.Walk;
    }

    /// <summary>
    /// 0.9 缩放下 cloth root 的 local x/y（z 沿用 body 的 z）：以 body root 的 localPosition 为参考，
    /// 肩部像素偏移 × VisualScale —— 身体缩小后飘带锚点同步上移/内收，不悬空。
    /// x 偏移符号随 flip（与 HeroArcherCloth.Create/ApplyFlip 同规则：flip 时取 -ShoulderOffsetPixelsX）。
    /// 纯函数、输出绝对值：重复调用同结果（幂等，绝不乘当前 scale 造成逐帧递减）。
    /// </summary>
    internal static (float X, float Y) ClothRootLocal(float originX, float originY, bool flipX)
    {
        float offsetX = (flipX ? -ShoulderOffsetPixelsX : ShoulderOffsetPixelsX) / ClothPixelsPerUnit * VisualScale;
        float offsetY = ShoulderHeightPixels / ClothPixelsPerUnit * VisualScale;
        return (originX + offsetX, originY + offsetY);
    }
}
