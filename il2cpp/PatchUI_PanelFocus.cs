using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// F5 面板自动暂停 v3.2（菜单留后设计）+ 输入门。
///
/// 用户指令：F5 单键 = 自动打开原生 ESC 菜单 + 面板叠加（暂停/光标/背景由原生菜单带出）；
/// 面板期间菜单留在背后但完全不可交互；关面板游戏恢复。
///
/// A 节·菜单/暂停状态机（Tick 由 ModPanel.Update 每帧驱动，boot/前端场景也照跑，全程空安全）：
/// 状态门 G = game!=null && state∈{Playing,NetworkClientPlaying,Menu}
/// && ProgramDirector.IsMainSceneActive() && Menu.InstExists，开门沿/每帧闸/关面板菜单收尾
/// 三处统一使用（P0-1：前端标题/SailingAway 选岛地图/coop 提示态全程不碰菜单）。
/// - 开门沿（_shown false→true，仅 G 内）：菜单未开且 state∈{Playing,NetworkClientPlaying} →
///   game.TryShowMenu(0,true) 并记 _menuOpenAttemptAt（unscaledTime；不立刻认领归属，等菜单
///   显示且在 0.5s 时效窗内才置 _menuOpenedByUs）。菜单已开 → ActiveMap 非 Closed/Closing 时
///   单次 HideOne()（2→1：地图按 targetDepth 降幅自退、菜单留后，与原生 ESC 在菜单+地图形态
///   下同构，MapTimelineMenu.cs:424；地图的裸 Rewired 输入随地图关闭而消失，不闸地图输入
///   ——hook 面大）；此形态菜单归用户（_menuOpenedByUs=false）。state==Menu 且菜单未开
///   （退出尾巴瞬态）：不动作，尾部结束后兜底判定接管（自愈）。
/// - 每帧（面板开着）：G && IsMenuShown → Menu.Inst.interactable=false（幂等 setter，对冲菜单
///   打开协程 Menu.cs:724 的写 true）+ 置 _menuGatedByUs；attempt 在 0.5s 时效窗内且菜单已
///   显示 → _menuOpenedByUs=true（时效门防陈旧 attempt 认领外部打开的菜单，如邀请通知）。
/// - 关面板（无条件评估）：兜底 disengage（v2.1 三分支）→ 光标恢复 → 菜单收尾按归属二分
///   （P0-2，防手工菜单软锁）：我方菜单（_menuOpenedByUs）只 Hide 不恢复 interactable（原生
///   退出路径 :968-972 恢复时标/音频；Menu.Update :2733 被闸读不到 true → 我方单次绝对写 0，
///   无 targetDepth=-1 竞态；P1-1：恢复步骤删除——菜单打开协程 :724 自己写 true、退出路径
///   :986 裸写 false，恢复无功能必要）。用户菜单（!ours && gated && IsMenuShown）只恢复
///   interactable=true 不 Hide（用户的菜单留给他自己 ESC 逐层关；本分支零 targetDepth 写入
///   → 同帧 Menu.Update 即使读到 true+ESC 也只 HideOne 一次，>=0 恒成立——-1 竞态成因是
///   "恢复+我方 Hide 同帧叠加"，本分支无 Hide 不复活）。随后三字段无条件清零（收尾动作抛
///   异常时不清零、下一帧重试：Hide/restore 均幂等，清零会把用户弃于被闸菜单）。
/// - 兜底 Engage（菜单拒开/未开时的保险暂停）：v2.1 五条件（!Engaged && shown && game 判空
///   先行 && !Menu.IsMenuShown && 可玩 && HasWorldAuth && !IsClientPresent && ts&gt;0）+ 第六条件
///   （_menuOpenAttemptAt==0 || unscaledTime-attempt&gt;=0.5s：菜单尝试期让路；菜单成功打开时
///   state=Menu 自然不触发）。Disengage 三分支不变（离开可玩态/game==null/原生已改写 ts →
///   只清标志不写）。
///
/// B 节·Player 输入门（手动安装，不走 PatchAll）：显式接口实现 IControllable.ReceiveInput 在
/// 2.4 interop 里的名字是 IControllable_ReceiveInput（Il2CppInterop 把点号改下划线；operator 已
/// 对 interop DLL 字符串堆独立复核）。PatchAll 对解析失败的 attribute 会抛异常拖垮整个插件的
/// 补丁注册，与 fail-closed 目标相反，故 Init 在 PatchAll 之后按候选集解析 MethodInfo 再
/// harmony.Patch 手动安装；全部失败 → ERROR 日志 + 不装 hook（面板其余功能不受影响）。
/// prefix 返回 false 只是跳过方法体：Game.Update 输入泵照常调用（Game.cs:2032-2044），不动泵、
/// 不动 SendInputTo/ReleaseInput，两本地玩家（含客机 tunnel 分支）移动/冲刺/付币全停。
/// 兜底态必需（面板开着但世界未冻结）；菜单态无害（state=Menu 时原生本就不派发输入）。
/// v2.1 的 C 节（TryShowMenu prefix + 0.25s Esc 抑制窗）已删除（P1-3①）：兜底模式 Esc 语义
/// 改为"面板关 + 菜单按原生语义打开"，菜单模式下 state==Menu 时 CheckForPause 本就不再开菜单。
///
/// 已知边界（留档汇总）：v2.1 保留（ts=0 期间音频 fade 冻结的原生 LogWarning；皇冠失落慢动作
/// tween 只认原生 IsMenuShown 可能在面板背后推进；Hide 后 ~0.6s 菜单尾巴期间兜底延迟；
/// _currentControllable 为 TeleporterExit 时输入门不覆盖；Haglet 弹窗不主动关；联机带客机
/// 不冻结但闸照常）+ v3.2 新增：statsPanel/DLC 弹窗裸轮询绕闸（Menu.Update:2721-2727 不查
/// _interactable）；coop 提示裸字段写 _interactable（:2050/:2110）；Slider 拖拽/ScrollRect
/// 滚轮不受 CanvasGroup.interactable 约束（options 面板先开后 F5 才可达）；快速 F5-F5（刚发
/// 信号即关）菜单会在面板关后弹出；兜底 Engage 期间外部开菜单 → 菜单 pts 捕获 0、退出恢复 1
/// （原 ts 非 1 则丢失）；playerId 硬编码 0（coopStopButton 不显示，观感）；state==Menu 瞬态
/// 由兜底 0.5s 后接管自愈；游戏内不存在 M 键纯地图形态（仅菜单地图按钮与航行自动展示两入口，
/// 后者被 G 排除），若未来出现则开门沿走 step2 分支 HideOne 1→0=原生关图语义、兜底随后接管，
/// 两世界下动作均正确；基类（非希腊）地图 Closing 动画个别帧退栈键为按住型持续触发
/// （MapTimelineMenu.cs:193-194 无 Closing 守卫）→ F5 收图后按住退栈键可越过动画尾再收一层，
/// 希腊版有守卫不受影响，罕见+自愈；前端标题菜单不在 G 内不被闸（面板叠标题菜单为已知边界）。
/// </summary>
internal static class PatchUI_PanelFocus
{
    /// <summary>attempt 认领窗（秒，unscaledTime 域）：窗内菜单首次显示才归我方打开。</summary>
    private const float MenuClaimWindowSeconds = 0.5f;

    private const int FaultBudget = 8;

    internal static bool Engaged { get; private set; }

    private static float _pts;               // Engage 时刻的原生时标（条件含 ts>0，恒>0）
    private static bool _lastShown;
    private static string _inputGateName;    // B 节解析到的 MethodInfo.Name；未装=null
    private static int _faults;
    private static bool _cursorForcedByUs;   // 我们是否调过 SetForceVisibleCursor(true)
    private static bool _prevForceCursor;    // 开面板前的原值（原生登录/错误屏也可能置过）
    private static bool _menuOpenedByUs;     // 本面板会话由我们的 TryShowMenu 打开且菜单已显示
    private static float _menuOpenAttemptAt; // 我们发起 TryShowMenu 的 unscaledTime
    private static bool _menuAttemptArmed;   // 上面时间戳是否有效（unscaledTime==0 首帧不可当哨兵）
    private static bool _menuGatedByUs;      // 本面板会话我们闸过菜单交互

    /// <summary>
    /// B 节 prefix：面板打开期间返回 false，跳过 Player.IControllable_ReceiveInput 方法体。
    /// 与开关无关（面板基础设施），关闭总开关后输入门同样生效。
    /// </summary>
    internal static bool Prefix() => !ModPanel.IsShown;

    /// <summary>A 节状态机。空上下文（boot/前端/game==null/Menu 未建）只是空转。</summary>
    internal static void Tick()
    {
        bool shown = ModPanel.IsShown;
        bool openEdge = shown && !_lastShown;
        _lastShown = shown;

        Game game = CurrentGame();

        if (openEdge)
        {
            // 开门沿各动作互不拖累：菜单收尾/光标/暂停判定各自独立 try/catch，
            // 一侧读原生失败不得丢掉本帧的其他处理。
            try { MenuWorkOnOpenEdge(game); }
            catch (Exception e) { Note("open edge", e); }
            try { ForceCursorForPanel(); }
            catch (Exception e) { Note("cursor", e); }
        }

        // 光标恢复独立于暂停状态机：客机等不 Engage 的场景面板同样需要光标，关闭同样要还原。
        if (_cursorForcedByUs && !shown)
            RestoreCursor();

        // 每帧菜单闸（面板开着才闸；对冲菜单打开协程 :724 的写 true）。
        if (shown)
        {
            try { GateMenuThisFrame(game); }
            catch (Exception e) { Note("menu gate", e); }
        }

        try
        {
            // Engage：每帧评估，全真才挂（不门 ModConfig.Enabled）。!Engaged 守卫不可省：
            // Engaged 期间原生可能改写时标（如皇冠失落慢动作 tween 只认原生 IsMenuShown），
            // 若 ts>0 即重入会把 _pts 覆写成慢动作值，关面板时恢复错值；一次 Engage 只记一次 pts，
            // 原生写入期间不再碰 ts（不踩原生，Disengage 的 ts==0 恢复门自然让位）。
            // game 判空先于 Menu.IsMenuShown：主场景外 Menu.Inst getter 会 FindObjectOfType 场景扫描，
            // 且 IsMenuShown 第二支无 InstExists 保护（标题界面每帧 NRE→吞进 catch 还耗日志预算）。
            // 第六条件（v3.2）：菜单尝试期（0.5s 内）让路给原生菜单路径，不抢先冻结。
            if (!Engaged && shown && game != null && Playable(game) && !Menu.IsMenuShown
                && MenuAttemptSettled()
                && NetworkBigBoss.HasWorldAuth && !NetworkBigBoss.IsClientPresent
                && Time.timeScale > 0f)
            {
                _pts = Time.timeScale;
                Time.timeScale = 0f;
                Engaged = true;
                Info("engaged: timeScale " + _pts.ToString("0.###") + " -> 0, input gate "
                    + (_inputGateName ?? "unresolved"));
            }

            // Disengage：无条件评估，不门任何开关（总开关关闭时的恢复也走这里）。
            if (Engaged && (!shown || game == null || !Playable(game)))
            {
                if (game != null && Playable(game) && Time.timeScale == 0f)
                    Time.timeScale = Mathf.Approximately(_pts, 0f) ? 1f : _pts;
                // 离开可玩态 / game==null / 原生已动 ts：只清标志，不写时标。
                Engaged = false;
            }
        }
        catch (Exception e)
        {
            Note("tick", e);
        }

        // 关面板：菜单收尾按归属二分（!shown 期间每帧评估；字段首次清零后即幂等空转）。
        if (!shown)
        {
            try { MenuWorkOnClose(game); }
            catch (Exception e) { Note("menu close", e); }
        }
    }

    /// <summary>状态门 G：开门沿/每帧闸/关面板菜单收尾三处共用（P0-1）。</summary>
    private static bool PanelMenuGate(Game game)
    {
        if (game == null) return false;
        Game.State state = game.state;
        if (state != Game.State.Playing && state != Game.State.NetworkClientPlaying
            && state != Game.State.Menu)
            return false;
        return ProgramDirector.IsMainSceneActive() && Menu.InstExists;
    }

    /// <summary>第六条件的 attempt 结算：无 attempt 或已过 0.5s 认领窗。</summary>
    private static bool MenuAttemptSettled()
        => !_menuAttemptArmed
           || Time.unscaledTime - _menuOpenAttemptAt >= MenuClaimWindowSeconds;

    private static void MenuWorkOnOpenEdge(Game game)
    {
        if (!PanelMenuGate(game)) return;
        Game.State state = game.state;
        if (!Menu.IsMenuShown)
        {
            // state==Menu 的退出尾巴瞬态：不动作（尾部结束后兜底判定接管）。
            if (state == Game.State.Playing || state == Game.State.NetworkClientPlaying)
            {
                game.TryShowMenu(0, true);
                _menuOpenAttemptAt = Time.unscaledTime;
                _menuAttemptArmed = true; // unscaledTime==0 的首帧不能当"无 attempt"哨兵
                Info("open edge: native menu open requested (ownership claim window "
                    + MenuClaimWindowSeconds.ToString("0.#") + "s)");
            }
            return;
        }
        // 菜单已开（用户先前 ESC / 其他路径）：地图未收则单次 HideOne（2→1，地图自退、菜单留后）。
        Menu menu = Menu.Inst;
        if (menu == null) return;
        MapTimelineMenu map = menu.ActiveMap;
        if (map != null && map.CurrentState != MapTimelineMenu.State.Closed
            && map.CurrentState != MapTimelineMenu.State.Closing)
        {
            menu.HideOne();
            Info("open edge: map pulled back once via HideOne (menu stays behind)");
        }
        _menuOpenedByUs = false; // 此形态菜单归用户：关面板只恢复交互、不 Hide
    }

    private static void GateMenuThisFrame(Game game)
    {
        if (!PanelMenuGate(game) || !Menu.IsMenuShown) return;
        Menu menu = Menu.Inst;
        if (menu == null) return;
        menu.interactable = false; // 幂等 setter；对冲 _ShowMainPanel:724 的写 true
        if (!_menuGatedByUs)
        {
            _menuGatedByUs = true;
            Info("menu gate active (native menu interactable=false behind panel)");
        }
        // 归属认领：我们的 attempt 之后 0.5s 窗内菜单首次显示 → 我方菜单。
        if (_menuAttemptArmed
            && Time.unscaledTime - _menuOpenAttemptAt < MenuClaimWindowSeconds)
            _menuOpenedByUs = true;
    }

    private static void MenuWorkOnClose(Game game)
    {
        if (PanelMenuGate(game))
        {
            Menu menu = Menu.Inst;
            if (menu != null && Menu.IsMenuShown)
            {
                if (_menuOpenedByUs)
                {
                    // 我方菜单整体退场：原生退出路径恢复时标/音频；不恢复 interactable（P1-1，
                    // Menu.Update 被闸 → 我方单次绝对写 0，无 -1 竞态）。
                    menu.Hide();
                    Info("close: our menu hidden (native exit path restores)");
                }
                else if (_menuGatedByUs)
                {
                    // 用户的菜单留给他自己 ESC 逐层关（P0-2）：只恢复交互，零 targetDepth 写入。
                    menu.interactable = true;
                    Info("close: user menu left open, interactable restored");
                }
            }
        }
        // 收尾动作抛异常时（上面的 try 由调用方兜）不清零：下一帧重试，Hide/restore 均幂等。
        _menuOpenedByUs = false;
        _menuOpenAttemptAt = 0f;
        _menuAttemptArmed = false;
        _menuGatedByUs = false;
    }

    // 光标（玩家反馈 2026-09-23：收起原生菜单后 CursorSystem 每帧只在菜单激活时显示光标，
    // 面板变得盲点）。SetForceVisibleCursor 是游戏自带正式入口（存档/错误屏同款），
    // 记原值防踩原生登录屏自己置过的 force 位。v3.2 全局无条件 force（含 G 外的标题/航行
    // 等场景）：菜单路径下 force 位只让光标秒显，零成本不回退。
    private static void ForceCursorForPanel()
    {
        if (!CursorSystem.InstExists) return;
        _prevForceCursor = ReadForceCursorFlag();
        CursorSystem.Inst.SetForceVisibleCursor(true);
        _cursorForcedByUs = true;
    }

    private static void RestoreCursor()
    {
        if (CursorSystem.InstExists)
            CursorSystem.Inst.SetForceVisibleCursor(_prevForceCursor);
        _cursorForcedByUs = false;
    }

    private static bool ReadForceCursorFlag()
    {
        try
        {
            var field = typeof(CursorSystem).GetField("forceVisibleCursor",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic);
            return field != null && CursorSystem.Inst != null
                && field.GetValue(CursorSystem.Inst) is bool value && value;
        }
        catch
        {
            return false;
        }
    }

    private static Game CurrentGame()
    {
        try
        {
            Managers managers = Managers.Inst;
            return managers != null ? managers.game : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool Playable(Game game)
    {
        Game.State state = game.state;
        return state == Game.State.Playing || state == Game.State.NetworkClientPlaying;
    }

    // ------------------------------------------------- B 节：输入门手动安装

    /// <summary>Init 在 PatchAll 之后调用（harmony 与 PatchAll 同一实例）。</summary>
    internal static void InstallInputGate(Harmony harmony)
        => InstallInputGate(harmony, typeof(Player));

    /// <summary>带类型参数的安装体（测试用替身类型驱动解析失败路径）。</summary>
    internal static void InstallInputGate(Harmony harmony, Type playerType)
    {
        try
        {
            MethodInfo method = ResolveInputGateMethod(playerType);
            if (method == null)
            {
                Error("input gate target not found on " + playerType.Name
                    + " (expected IControllable_ReceiveInput); panel input blocking disabled,"
                    + " all other features unchanged");
                return;
            }
            MethodInfo prefix = typeof(PatchUI_PanelFocus).GetMethod(nameof(Prefix),
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (prefix == null)
            {
                Error("input gate prefix not found; panel input blocking disabled");
                return;
            }
            harmony.Patch(method, prefix: new HarmonyMethod(prefix));
            _inputGateName = method.Name;
            // O-3：解析名只在启动日志记这一次。
            Info("input gate installed on Player." + method.Name);
        }
        catch (Exception e)
        {
            Error("input gate install failed: " + e.GetType().Name + " " + e.Message);
        }
    }

    /// <summary>
    /// 候选集解析：精确下划线名（2.4 interop 实测）→ 精确点号名 → 双 token 模糊。
    /// 反射异常与未命中同口径返回 null（fail-closed 交给调用方日志）。
    /// </summary>
    internal static MethodInfo ResolveInputGateMethod(Type playerType)
    {
        try
        {
            MethodInfo best = null;
            int bestRank = int.MaxValue;
            foreach (MethodInfo method in playerType.GetMethods(BindingFlags.Public
                     | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                string name = method.Name;
                int rank = name == "IControllable_ReceiveInput" ? 1
                    : name == "IControllable.ReceiveInput" ? 2
                    : name.Contains("IControllable") && name.Contains("ReceiveInput") ? 3
                    : 0;
                if (rank == 0 || rank >= bestRank) continue;
                best = method;
                bestRank = rank;
            }
            return best;
        }
        catch
        {
            return null;
        }
    }

    // ----------------------------------------------------------------- 日志

    private static void Info(string message) => TryLog(message, false);
    private static void Error(string message) => TryLog(message, true);

    private static void Note(string what, Exception e)
    {
        if (_faults >= FaultBudget) return;
        _faults++;
        TryLog(what + " fault: " + e.GetType().Name + " " + e.Message, true);
    }

    private static void TryLog(string message, bool error)
    {
        try
        {
            var source = KingdomEnhancedPlugin.Instance?.LogSource;
            if (source == null) return;
            if (error) source.LogError("[PanelFocus] " + message);
            else source.LogInfo("[PanelFocus] " + message);
        }
        catch
        {
            // 日志失败不得改变暂停/输入门/菜单决策。
        }
    }
}
