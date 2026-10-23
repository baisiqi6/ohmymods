using System;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace KingdomEnhancedMod;

// R7 到达控制只读状态样本（source-only 诊断：单文件、无 hook、无 MonoBehaviour、无 driver、无周期/扫描/等待/
// 重试、无 static 可变字段、无 UnityObject 分配、无任何 Native 写、不缓存 Native 引用）。
// 背景：Native 无 Root 移动键却持续左行；auto-goal 只是待区分假设。本样本只输出一次可比较状态，
// 不默认清目标/释放输入/改 Intro/Player/Camp，不解释 raw bit 含义，两个 snapshot 间逐 getter 一致也
// 不冒称 manual/auto。
// 调用契约（仅 Root）：Root 在已核 exact private12 / ActualPlaying / 非 Saving / 同 GameKingdomScene 的
// P1 / borrowedReference 边界之后调用，最多两次（初次、位移>=2u）；时点/latch/refs/退出全由 Root 负责，
// helper 不 tick 任何东西，也不重复大范围身份扫描（Root 已 exact）。
// 读取清单（当前 interop typed 入口，receipts/palace-whole-island-generation-boundary-review-r1.md 第三部分）：
// Player.CurrentUnitController/_sendInput/_horizontalInputTunnel/_inputTunnel/_inputChannel/mover/steed、
// Game._introSequence/_abortIntro、IntroSequence._introEnded/_cameraTarget/introMain、
// Mover.goalMode/movingToGoal/_goalPosition/_goalObject/_moveSpeed；每个 getter/字段只读一次并冻结 local。
// 不调用 GetGoal/SetGoal/SetInputRouting/AbortIntro 等任何 writer；steed 实际 mover 只经其 gameObject 的
// GetComponent<Mover>() 一次获取（不 GetComponentInChildren、不 Parent 扫描、不找名字），无 mover 明确
// none（unknown），不用 player.mover 默认填充；无 intro 明确 none（不解释 none 等于无自动控制）；
// 本轮无传入 exact Wreck：不扫描/不选 Wreck、不输出猜测的 _shouldMovePlayer。
// 输出：每 snapshot 最多 4 行、prefix [PalaceArrivalControlSample]、经 PalaceCourtVisuals.Log；
// 异常只在本次调用边界捕获一次并记 unknown，不写状态不重试。未编译/未 Native 运行（Root 统一 copy/wire/build）。
internal static class PalaceArrivalControlSample
{
    private const string Tag = "[PalaceArrivalControlSample] ";
    private const int MaxLines = 4;

    internal static void Sample(Game game, Player player, SpriteRenderer reference, string phase)
    {
        int lines = 0;
        try
        {
            SampleCore(game, player, reference, phase, ref lines);
        }
        catch (Exception e)
        {
            Log(ref lines, "unknown: sample fault (" + e.GetType().Name + "): " + e.Message
                + "; read-only call boundary, no state changed");
        }
    }

    private static void SampleCore(Game game, Player player, SpriteRenderer reference, string phase, ref int lines)
    {
        if (game == null || player == null || !(bool)player || reference == null || !(bool)reference)
        {
            Log(ref lines, "unknown: passed game/player/reference unavailable; sample closed (no fallback pick)");
            return;
        }

        // 行1：玩家锚点 + 输入路由 raw 值（不解释 bit 含义）；每 getter 冻结一次 local。
        Vector3 playerPos = player.transform.position;
        IUnitController controller = player.CurrentUnitController;
        IntPtr controllerClassPtr = controller is Il2CppObjectBase controllerBase
            ? IL2CPP.il2cpp_object_get_class(controllerBase.Pointer)
            : IntPtr.Zero;
        int sendInput = player._sendInput;
        int horizontalTunnel = player._horizontalInputTunnel;
        int inputTunnel = (int)player._inputTunnel;
        IUnitControllable inputChannel = player._inputChannel;
        Log(ref lines, "phase=" + (phase ?? "null")
            + " ref=" + PtrOf(reference)
            + " player=" + PtrOf(player) + " px=" + playerPos.x.ToString("F3")
            + " ctrl=" + PtrOf(controller) + " ctrlClass=" + controllerClassPtr.ToInt64()
            + " send=" + sendInput + " horizTunnel=" + horizontalTunnel
            + " tunnel=" + inputTunnel + " channel=" + PtrOf(inputChannel));

        // 行2：player.mover 目标状态（read 一遍）。
        Log(ref lines, DescribeMover(player.mover));

        // 行3：steed 身份/位置 + 其 gameObject.GetComponent<Mover>() 一次读实际坐骑 mover 同字段。
        Steed steed = player.steed;
        string steedLine;
        if (steed == null || !(bool)steed)
        {
            steedLine = "steed=none";
        }
        else
        {
            float steedX = steed.transform.position.x;
            Mover steedMover = steed.gameObject.GetComponent<Mover>();
            steedLine = "steed=" + PtrOf(steed) + " sx=" + steedX.ToString("F3") + " " + DescribeMover(steedMover);
        }
        Log(ref lines, steedLine);

        // 行4：intro 状态；无 intro 明确 none（不解释 none 等于无自动控制）。
        IntroSequence intro = game._introSequence;
        bool abortIntro = game._abortIntro;
        string introLine;
        if (intro == null || !(bool)intro)
        {
            introLine = "intro=none abortIntro=" + abortIntro;
        }
        else
        {
            bool introEnded = intro._introEnded;
            GameObject cameraTarget = intro._cameraTarget;
            var introMain = intro.introMain;
            introLine = "intro=" + PtrOf(intro) + " ended=" + introEnded
                + " camTarget=" + DescribeTarget(cameraTarget)
                + " introMain=" + PtrOf(introMain) + " abortIntro=" + abortIntro;
        }
        Log(ref lines, introLine);
    }

    // 单个 Mover 的目标状态（只读一遍；无 mover / destroyed 明确 none，不拿 player.mover 默认填充）。
    private static string DescribeMover(Mover mover)
    {
        if (mover == null || !(bool)mover) return "mover=none";
        int mode = (int)mover.goalMode;
        bool moving = mover.movingToGoal;
        float goalPosition = mover._goalPosition;
        GameObject goalObject = mover._goalObject;
        float moveSpeed = mover._moveSpeed;
        return "mover=" + PtrOf(mover) + " mode=" + mode + " moving=" + moving
            + " goalPos=" + goalPosition.ToString("F3")
            + " goalObj=" + DescribeTarget(goalObject)
            + " moveSpeed=" + moveSpeed.ToString("F3");
    }

    // GameObject 引用：ptr + 存活时 live 世界 x（position 只读一次）；null/destroyed 明确 none。
    private static string DescribeTarget(GameObject target)
    {
        if (target == null || !(bool)target) return "none";
        return target.Pointer.ToInt64() + "/" + target.transform.position.x.ToString("F3");
    }

    // 统一 ptr 表达：null=none；接口取其 Il2CppObjectBase proxy 的 Pointer（Il2CppInterop 接口代理继承基类）。
    private static string PtrOf(object o)
        => o == null ? "none" : o is Il2CppObjectBase b ? b.Pointer.ToInt64().ToString() : "notobject";

    // 有界日志：每 snapshot 最多 MaxLines 行，超出静默丢弃（数据路径恰好 4 行；异常行占用同一预算）。
    private static void Log(ref int lines, string message)
    {
        if (lines >= MaxLines) return;
        lines++;
        PalaceCourtVisuals.Log(Tag + message);
    }
}
