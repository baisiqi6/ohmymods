// worker 2026-09-15 纯逻辑测试：直接链接 production 的 HeroArcherMotion.cs + HeroArcherAnimation.cs
//（无 Unity/il2cpp 依赖；HeroArcherVisuals 的 Unity 胶水由 Operator 真实 interop 构建审查）。
// 入口：Main 返回失败数（0 = 全过），Operator 用任意 C# host（如 csc/dotnet run）执行即可。
//
// 覆盖契约点：
//   1) _movingToGoal=false 但 SetSpeed 生效也 Walk/Run —— Classify 的签名根本不含 goal 标记
//      （该门已在 HeroArcherVisuals.MotionOf 中移除），测试钉住「判据只吃实际速度」；
//   2) 向左（负速度）合法；3) 停下（死区）；4) NaN/±Inf → Idle；5) walkSpeed 非法时只分 Idle/Walk；
//   6) RepeatedMotion 相位推进 + 同值 SetMotion 不重置（真 HeroArcherAnimationState）；
//   7) 0.9 body/cloth 锚点偏移、flip 符号、纯函数幂等（不乘当前 scale 递减）。

using System;
using KingdomEnhancedMod;

internal static class HeroArcherMotionTests
{
    private static int _failures;

    private static void Check(bool condition, string name)
    {
        if (condition)
        {
            Console.WriteLine("PASS " + name);
        }
        else
        {
            _failures++;
            Console.WriteLine("FAIL " + name);
        }
    }

    private static void CheckFrame(int expected, int actual, string name)
        => Check(actual == expected, name + " (expected " + expected + ", got " + actual + ")");

    public static int Main()
    {
        const float walk = 1f;

        Check(HeroArcherMotion.SelectSpeed(0f, true, 3f) == 0f, "原生暂停Speed=0优先于残留指令速度");
        Check(HeroArcherMotion.SelectSpeed(0.8f, true, 3f) == 0.8f, "原生实际动画速度优先");
        Check(HeroArcherMotion.SelectSpeed(0f, false, -1f) == -1f, "无Animator速度时回退SetSpeed左向指令");
        Check(HeroArcherMotion.SelectSpeed(float.NaN, true, 1f) == 1f, "非法Animator速度回退");
        Check(HeroArcherMotion.SelectSpeed(float.PositiveInfinity, true, float.NaN) == 0f, "双非法速度安全停下");

        // --- 移动判据：不看 goal 标记（签名层面即无该参数），SetSpeed 路径 _movingToGoal=false 也成立 ---
        Check(HeroArcherMotion.Classify(1f, walk) == HeroArcherAnimation.Walk, "SetSpeed 生效（无 goal 门）→ Walk");
        Check(HeroArcherMotion.Classify(2f, walk) == HeroArcherAnimation.Run, "超过 walkSpeed+ε → Run");

        // --- 向左（负速度）合法：取绝对值 ---
        Check(HeroArcherMotion.Classify(-1f, walk) == HeroArcherAnimation.Walk, "负速度向左 → Walk");
        Check(HeroArcherMotion.Classify(-2f, walk) == HeroArcherAnimation.Run, "负速度向左 → Run");

        // --- 停下：死区内（含恰好等于 ε）→ Idle ---
        Check(HeroArcherMotion.Classify(0f, walk) == HeroArcherAnimation.Idle, "速度 0 → Idle");
        Check(HeroArcherMotion.Classify(0.04f, walk) == HeroArcherAnimation.Idle, "死区内 → Idle");
        Check(HeroArcherMotion.Classify(0.05f, walk) == HeroArcherAnimation.Idle, "恰好等于 ε → Idle");

        // --- 边界有界切换：刚超 walkSpeed 但未过 ε 不抖到 Run ---
        Check(HeroArcherMotion.Classify(1.04f, walk) == HeroArcherAnimation.Walk, "walkSpeed 内 → Walk");
        Check(HeroArcherMotion.Classify(1.05f, walk) == HeroArcherAnimation.Walk, "恰好 walkSpeed+ε → Walk");
        Check(HeroArcherMotion.Classify(1.06f, walk) == HeroArcherAnimation.Run, "刚过 walkSpeed+ε → Run");

        // --- 非有限速度 → Idle ---
        Check(HeroArcherMotion.Classify(float.NaN, walk) == HeroArcherAnimation.Idle, "NaN → Idle");
        Check(HeroArcherMotion.Classify(float.PositiveInfinity, walk) == HeroArcherAnimation.Idle, "+Inf → Idle");
        Check(HeroArcherMotion.Classify(float.NegativeInfinity, walk) == HeroArcherAnimation.Idle, "-Inf → Idle");

        // --- walkSpeed 非法/非正：只分 Idle/Walk（绝不除以/比较非法值产生异常） ---
        Check(HeroArcherMotion.Classify(5f, float.NaN) == HeroArcherAnimation.Walk, "walkSpeed=NaN → Walk");
        Check(HeroArcherMotion.Classify(5f, 0f) == HeroArcherAnimation.Walk, "walkSpeed=0 → Walk");
        Check(HeroArcherMotion.Classify(5f, -1f) == HeroArcherAnimation.Walk, "walkSpeed<0 → Walk");
        Check(HeroArcherMotion.Classify(float.NaN, float.NaN) == HeroArcherAnimation.Idle, "双 NaN → Idle");

        // --- RepeatedMotion 相位推进（真 HeroArcherAnimationState，idle 首帧起步） ---
        HeroArcherAnimationState anim = new HeroArcherAnimationState();
        anim.SetMotion(HeroArcherAnimation.Walk, 0d);
        CheckFrame(HeroArcherAtlas.WalkFirstFrame, anim.Tick(0d), "Walk 起步帧 = walk 首帧");
        CheckFrame(HeroArcherAtlas.WalkFirstFrame + 1, anim.Tick(1d / 6d), "Walk 相位推进 → 帧 +1");
        CheckFrame(HeroArcherAtlas.WalkFirstFrame + 2, anim.Tick(2d / 6d), "Walk 相位推进 → 帧 +2");
        // 同值 SetMotion 同帧重复调用 = no-op，不重置相位（每帧 SetMotion+Tick 正是 Sync 的调用形态）。
        anim.SetMotion(HeroArcherAnimation.Walk, 2d / 6d);
        CheckFrame(HeroArcherAtlas.WalkFirstFrame + 2, anim.Tick(2d / 6d), "同值 SetMotion 不重置相位");
        CheckFrame(HeroArcherAtlas.WalkFirstFrame + 3, anim.Tick(3d / 6d), "Walk 相位推进 → 帧 +3");
        CheckFrame(HeroArcherAtlas.WalkFirstFrame, anim.Tick(4d / 6d), "Walk 循环回卷 → walk 首帧");
        // 换 motion（Walk→Run）开新片段：Run 首帧、Run 节奏 8fps。
        anim.SetMotion(HeroArcherAnimation.Run, 4d / 6d);
        CheckFrame(HeroArcherAtlas.RunFirstFrame, anim.Tick(4d / 6d), "切换 Run → run 首帧");
        CheckFrame(HeroArcherAtlas.RunFirstFrame + 1, anim.Tick(4d / 6d + 1d / 8d), "Run 相位按 8fps 推进");
        // 停下回 Idle：同值不重置；非法时间（NaN/回退）保持上一帧。
        anim.SetMotion(HeroArcherAnimation.Idle, 1d);
        CheckFrame(HeroArcherAtlas.IdleFirstFrame, anim.Tick(1d), "切回 Idle → idle 首帧");
        CheckFrame(HeroArcherAtlas.IdleFirstFrame + 1, anim.Tick(1d + 1d / 6d), "Idle 相位推进");
        CheckFrame(HeroArcherAtlas.IdleFirstFrame + 1, anim.Tick(double.NaN), "NaN 时间保持上一帧");
        CheckFrame(HeroArcherAtlas.IdleFirstFrame + 1, anim.Tick(0.5d), "回退时间保持上一帧");
        // 非法 motion 枚举（Draw/Release 等非移动值）被 SetMotion 忽略。
        anim.SetMotion(HeroArcherAnimation.Draw, 2d);
        CheckFrame(HeroArcherAtlas.IdleFirstFrame + 2, anim.Tick(2d), "非移动枚举被 SetMotion 忽略");

        // --- 0.9 缩放：cloth 肩锚偏移（与 Cloth 的像素规则同源：x=-5、肩高 14、1px=1/32） ---
        Check(Math.Abs(HeroArcherMotion.VisualScale - 0.9f) < 1e-6f, "VisualScale == 0.9");
        (float x0, float y0) = HeroArcherMotion.ClothRootLocal(0f, 0f, false);
        Check(Math.Abs(x0 - (-5f / 32f) * 0.9f) < 1e-6f && Math.Abs(y0 - (14f / 32f) * 0.9f) < 1e-6f,
            "cloth 锚点 = 像素偏移 × 0.9（非全尺寸偏移）");
        (float xf, float yf) = HeroArcherMotion.ClothRootLocal(0f, 0f, true);
        Check(Math.Abs(xf - (5f / 32f) * 0.9f) < 1e-6f && Math.Abs(yf - y0) < 1e-6f,
            "flip 时 x 偏移取反（肩部锚点随朝向），y 不变");
        // 参考非零原点：偏移是加法项，不吞掉 body 自身位置。
        (float xn, float yn) = HeroArcherMotion.ClothRootLocal(0.25f, -1f, false);
        Check(Math.Abs(xn - (0.25f + (-5f / 32f) * 0.9f)) < 1e-6f && Math.Abs(yn - (-1f + (14f / 32f) * 0.9f)) < 1e-6f,
            "非零 body 原点：锚点 = 原点 + 偏移×0.9");
        // 幂等：纯函数输出绝对值，重复调用同结果（证明「重断言」不会乘当前 scale 递减）。
        (float x2, float y2) = HeroArcherMotion.ClothRootLocal(x0, y0, false);
        (float x3, float y3) = HeroArcherMotion.ClothRootLocal(x0, y0, false);
        Check(x2 == x3 && y2 == y3, "重复调用幂等（不逐帧递减）");

        Console.WriteLine(_failures == 0 ? "ALL PASS" : _failures + " FAILURES");
        return _failures;
    }
}
