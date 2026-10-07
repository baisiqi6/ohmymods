using System;
using KingdomEnhancedMod;
using UnityEngine;

/// <summary>
/// 公共行为验收：直接调用真实 candidate helper（同源链接），不复制算法。
/// </summary>
internal static class Program
{
    private static int _failures;
    private static readonly Transform SceneA = new Transform();
    private static readonly Transform SceneB = new Transform();

    private static int Main()
    {
        RunAll();
        Console.WriteLine(_failures == 0 ? "ALL PASS" : _failures + " FAILURES");
        return _failures == 0 ? 0 : 1;
    }

    private static void RunAll()
    {
        // 1) 单机 0 显示 0（不显示未知）
        var k = new Kingdom();
        var p1 = MakePlayer(11, 0, SceneA);
        k.playerOne = p1;
        Refresh(k, SceneA);
        CheckBoth("single-zero", "0", "随身钻石");

        // 2) 收集/支付变值：同一钱包读当前值，换钱包读新引用
        p1.wallet.gems = 1234;
        Refresh(k, SceneA);
        CheckBoth("single-1234", "1,234", "随身钻石");
        p1.wallet.gems = 987;
        Refresh(k, SceneA);
        CheckBoth("single-987", "987", "随身钻石");
        p1.wallet = NewWallet(p1, 8, 11117);
        Refresh(k, SceneA);
        CheckBoth("single-wallet-replaced", "8", "随身钻石");

        // 3) 仅 P2 本机：裸数字 + 随身钻石（不标 2P）
        var p2 = MakePlayer(22, 42, SceneA);
        k.playerOne = null;
        k.playerTwo = p2;
        Refresh(k, SceneA);
        CheckBoth("p2-only", "42", "随身钻石");

        // 4) 双本机各自报值，不合计
        p1.wallet.gems = 5;
        k.playerOne = p1;
        Refresh(k, SceneA);
        CheckBoth("dual-independent", "1P 5", "2P 钻石 42");

        // 5) 远程（无本地权威）不混入；全远程则未知
        p2.hasLocalAuthority = false;
        p2.wallet.gems = 99;
        Refresh(k, SceneA);
        CheckBoth("remote-excluded", "5", "随身钻石");
        p1.hasLocalAuthority = false;
        Refresh(k, SceneA);
        CheckBoth("all-remote", "—", "随身钻石");
        p1.hasLocalAuthority = true;
        p2.hasLocalAuthority = true;

        // 6) 缺失引用逐项未知
        k.playerOne = null;
        k.playerTwo = null;
        Refresh(k, SceneA);
        CheckBoth("both-missing", "—", "随身钻石");
        k.playerOne = p1;
        p1.wallet = null;
        Refresh(k, SceneA);
        CheckBoth("wallet-null", "—", "随身钻石");
        p1.wallet = NewWallet(p1, 6, 11117);
        p1.wallet.Pointer = IntPtr.Zero;
        Refresh(k, SceneA);
        CheckBoth("wallet-pointer-zero", "—", "随身钻石");
        p1.wallet = NewWallet(p1, 6, 11117);
        Refresh(k, null);
        CheckBoth("scene-null", "—", "随身钻石");
        p1.transform.Parent = SceneB;
        Refresh(k, SceneA);
        CheckBoth("wrong-scene", "—", "随身钻石");
        p1.transform.Parent = SceneA;
        p1.gameObject.activeInHierarchy = false;
        Refresh(k, SceneA);
        CheckBoth("inactive-player", "—", "随身钻石");
        p1.gameObject.activeInHierarchy = true;
        p1.gameObject = null;
        Refresh(k, SceneA);
        CheckBoth("gameobject-null", "—", "随身钻石");
        p1.gameObject = new GameObject();
        var foreign = MakePlayer(33, 77, SceneA);
        p1.wallet._playerRef = foreign;
        Refresh(k, SceneA);
        CheckBoth("wallet-owner-mismatch", "—", "随身钻石");
        p1.wallet._playerRef = p1;
        Refresh(k, SceneA);
        CheckBoth("owner-restored", "6", "随身钻石");

        // 7) 重复 slot 同指针不双算
        k.playerTwo = null;
        k.playerTwo = p1;
        Refresh(k, SceneA);
        CheckBoth("same-player-pointer", "6", "随身钻石");
        var ownSecondWallet = p2.wallet;
        p2.wallet = p1.wallet; // 一个真实钱包的 owner 不可能同时属于两个不同玩家
        k.playerOne = p1;
        k.playerTwo = p2;
        Refresh(k, SceneA);
        CheckBoth("cross-player-wallet-alias", "1P 6", "2P 钻石 —");
        p2.wallet = ownSecondWallet;

        // 8) 负值不猜 0：该槽未知，另一端本机仍显示
        p1.wallet.gems = -1;
        p2.wallet.gems = 7;
        Refresh(k, SceneA);
        CheckBoth("negative-first-keeps-p2", "1P —", "2P 钻石 7");
        p2.wallet.gems = -5;
        Refresh(k, SceneA);
        CheckBoth("both-negative", "1P —", "2P 钻石 —");

        // 9) Clear / 切 scene 无旧数据；每次 Refresh 取当前 kingdom 引用
        p1.wallet.gems = 6;
        k.playerTwo = null;
        Refresh(k, SceneA);
        CheckBoth("recover-after-negative", "6", "随身钻石");
        PatchUI_CalendarGems.Clear();
        CheckBoth("clear", "—", "随身钻石");
        Refresh(k, SceneA);
        CheckBoth("refresh-after-clear", "6", "随身钻石");
        p1.transform.Parent = SceneB;
        Refresh(k, SceneA);
        CheckBoth("scene-switch-unknown", "—", "随身钻石");
        Refresh(k, SceneB);
        CheckBoth("scene-switch-reads", "6", "随身钻石");
        p1.transform.Parent = SceneA;
        var replacement = MakePlayer(44, 4321, SceneA);
        k.playerOne = replacement;
        Refresh(k, SceneA);
        CheckBoth("player-replaced", "4,321", "随身钻石");
        k.playerOne = null;
        Refresh(k, SceneA);
        CheckBoth("player-removed", "—", "随身钻石");
        k.playerOne = replacement;

        // 10) 读取故障一次 warning；故障槽未知但另一 local 仍显示
        CheckInt("no-warning-before-faults", 0, BepInEx.Logging.ManualLogSource.Warnings.Count);
        k.ThrowOnOne = true;
        var p2b = MakePlayer(55, 3, SceneA);
        k.playerTwo = p2b;
        Refresh(k, SceneA);
        CheckBoth("kingdom-fault-other-shows", "3", "随身钻石");
        CheckInt("fault-warning-once", 1, BepInEx.Logging.ManualLogSource.Warnings.Count);
        Refresh(k, SceneA);
        Refresh(k, SceneA);
        CheckInt("fault-warning-stays-once", 1, BepInEx.Logging.ManualLogSource.Warnings.Count);
        k.ThrowOnOne = false;
        Refresh(k, SceneA);
        CheckBoth("recovered-dual", "1P 4,321", "2P 钻石 3");
        replacement.wallet.ThrowOnGems = true;
        p2b.wallet.gems = 4;
        Refresh(k, SceneA);
        CheckBoth("gems-fault-keeps-p2", "1P —", "2P 钻石 4");
        p2b.wallet.ThrowOnGems = true;
        Refresh(k, SceneA);
        CheckBoth("both-gems-fault", "1P —", "2P 钻石 —");
        CheckInt("gems-fault-no-second-warning", 1, BepInEx.Logging.ManualLogSource.Warnings.Count);
        Check("warning-tagged", BepInEx.Logging.ManualLogSource.Warnings.Count == 1
            && BepInEx.Logging.ManualLogSource.Warnings[0].Contains("[CalendarGems]"));

        // 11) 只读：helper 从未写钱包，且确实走过 getter
        p1.hasLocalAuthority = true;
        p1.TunnelInput = true;
        p2.hasLocalAuthority = true;
        p2.TunnelInput = false;
        p2.wallet.ThrowOnGems = false;
        p2.wallet.gems = 9;
        k.playerOne = p1;
        k.playerTwo = p2;
        Refresh(k, SceneA);
        CheckBoth("local-monarch-wallet-stays-visible-during-input-tunnel", "1P 6", "2P 钻石 9");
        p1.TunnelInput = false;
        CheckInt("no-wallet-writes", 0, Wallet.GemWrites);
        Check("gem-getter-exercised", Wallet.GemReads > 0);
    }

    private static void Refresh(Kingdom kingdom, Transform scene) =>
        PatchUI_CalendarGems.Refresh(kingdom, scene);

    private static Player MakePlayer(long ptr, int gems, Transform scene, bool local = true, bool active = true)
    {
        var player = new Player
        {
            Pointer = new IntPtr(ptr),
            hasLocalAuthority = local,
            gameObject = new GameObject { activeInHierarchy = active },
            transform = new Transform { Parent = scene },
        };
        player.wallet = NewWallet(player, gems, ptr * 10 + 7);
        return player;
    }

    private static Wallet NewWallet(Player owner, int gems, long ptr) => new Wallet
    {
        Pointer = new IntPtr(ptr),
        gems = gems,
        _playerRef = owner,
    };

    private static void CheckBoth(string name, string expectedValue, string expectedCaption)
    {
        string value = PatchUI_CalendarGems.ValueText;
        string caption = PatchUI_CalendarGems.CaptionText;
        bool ok = value == expectedValue && caption == expectedCaption;
        if (!ok) _failures++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + " -> Value=\"" + value + "\" Caption=\"" + caption + "\""
            + (ok ? "" : " expected Value=\"" + expectedValue + "\" Caption=\"" + expectedCaption + "\""));
    }

    private static void Check(string name, bool ok)
    {
        if (!ok) _failures++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
    }

    private static void CheckInt(string name, int expected, int actual)
    {
        bool ok = expected == actual;
        if (!ok) _failures++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + " expected=" + expected + " actual=" + actual);
    }
}
