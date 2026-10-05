using System;
namespace OhMyMods.AndroidProbe;
/// <summary>
/// 公共面板的唯一指针手势与滚动状态（纯逻辑，无 UnityEngine 依赖，host 测试直接编译）。
///
/// 契约（Issue #144）：
/// - Begin 记录二维按下点；任意方向位移达到阈值（调用方传 10*Layout.Scale）后 sticky Moved，
///   取消本次点击：拖回原点、反向、水平移动都保持 Moved，release 一律零动作。
/// - 只有从内容视口开始的按压会滚动（canScroll）；导航/关闭的按压也能被 Moved 取消点击，
///   但从不滚动。Scrolled 由 Moved && canScroll 派生，无独立镜像。
/// - ScrollY 是唯一滚动状态，只有 Move/Wheel 两个生产者写入并按 maxScroll 夹取 [0,max]。
/// - End(x,y,releasedId,maxScroll) 先按实际抬手位置归账位移再判定动作：没有 MouseDrag、
///   仅在抬手时偏离触点的按压同样取消动作；命中与按下同一 tap rect 且未 Moved 才返回动作。
/// </summary>
internal sealed class PanelGesture
{
    internal bool Captured { get; private set; }
    internal bool Moved { get; private set; }
    internal bool Scrolled => Moved && canScroll;
    internal float ScrollY { get; private set; }

    private int pressedAction = -1;
    private bool canScroll;
    private float threshold;
    private float downX, downY, lastY;

    internal bool Begin(float x, float y, int actionId, bool inContentViewport, float scrollThreshold)
    {
        if (Captured) return false;
        Captured = true;
        Moved = false;
        pressedAction = actionId;
        canScroll = inContentViewport;
        threshold = scrollThreshold;
        downX = x;
        downY = y;
        lastY = y;
        return true;
    }

    /// <summary>拖拽位移归账；Scrolled 时按 maxScroll 夹取唯一 ScrollY。</summary>
    internal void Move(float x, float y, float maxScroll)
    {
        if (!Captured) return;
        if (!Moved)
        {
            float dx = x - downX, dy = y - downY;
            if (dx * dx + dy * dy >= threshold * threshold) Moved = true;
        }
        if (Moved && canScroll)
            ScrollY = Clamp(ScrollY + (lastY - y), maxScroll);
        lastY = y;
    }

    /// <summary>
    /// 滚轮：与拖拽共用同一 ScrollY 生产者（delta = 滚轮格数，向上滚动增大 ScrollY）。
    /// 若夹取后实际改变（next != ScrollY）且正有按住的手势，则置 sticky Moved 取消该次点击——
    /// 滚轮改变了可见行布局，旧 pressedAction 必须先失效，避免 MouseUp 触到别的设置；
    /// 无有效改变（夹取后不变）或无按住时，滚动数学与原来一致。
    /// </summary>
    internal void Wheel(float delta, float maxScroll)
    {
        float next = Clamp(ScrollY + delta, maxScroll);
        if (Captured && next != ScrollY) Moved = true;
        ScrollY = next;
    }

    /// <summary>帧末内容收缩/放大后收束唯一 ScrollY（复用同一夹取函数，0 输入也收敛）。</summary>
    internal void ClampToMax(float maxScroll) => ScrollY = Clamp(ScrollY, maxScroll);

    /// <summary>release：先按实际抬手位置归账位移，再返回要触发的动作 id（零动作 = -1）。</summary>
    internal int End(float x, float y, int releasedActionId, float maxScroll)
    {
        if (!Captured) return -1;
        Move(x, y, maxScroll);
        int action = !Moved && releasedActionId >= 0 && releasedActionId == pressedAction ? releasedActionId : -1;
        Captured = false;
        Moved = false;
        pressedAction = -1;
        canScroll = false;
        return action;
    }

    internal void Cancel()
    {
        Captured = false;
        Moved = false;
        pressedAction = -1;
        canScroll = false;
    }

    /// <summary>折叠/换页时清滚动；held TouchClaims 由 FloatInput 按 Ended 自然释放，不在此 Reset。</summary>
    internal void ResetScroll() => ScrollY = 0f;

    private static float Clamp(float value, float maxScroll) => Math.Clamp(value, 0f, Math.Max(0f, maxScroll));
}
