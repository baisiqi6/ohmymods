using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
using UnityEngine;

internal sealed class LifecycleState : ICoinCourierCampaignState
{
    public bool Owned { get; set; } = true;
    public CoinCourierPurse Purse { get; set; } = PurseWith(0);
    public int RecordCalls { get; set; }

    private CoinCourierAvailabilityInfo _availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Ready);

    public CoinCourierAvailabilityInfo Availability
    {
        get => _availability;
        set => _availability = value;
    }

    public bool Ready => _availability.Kind == CoinCourierAvailability.Ready;

    public bool TryRecordRecruitment()
    {
        RecordCalls++;
        return true;
    }

    internal static CoinCourierPurse PurseWith(int coins)
    {
        if (!CoinCourierPurse.TryRestore(new CoinCourierPurseSnapshot(coins, false, 0, default),
                out CoinCourierPurse purse, out CoinCourierReason reason))
            throw new Exception("purse restore failed: " + reason);
        return purse;
    }
}

/// <summary>
/// 确定性场景驱动（自 runtime-bridge 套件收敛）：搭起"可玩世界 + 银行"的最小边界，
/// 按帧推进真实运行时；视觉观察点直接读真实 CoinCourierVisuals 建出的对象。
/// </summary>
internal sealed class Harness
{
    internal readonly LifecycleState State = new LifecycleState();
    internal readonly Kingdom Kingdom = new Kingdom();
    internal readonly World World = new World();
    internal Banker Banker;

    internal Harness(float campfire = 0f)
    {
        SimPhysics.Reset();
        Managers.Inst = null;
        ResetPersistence();
        KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();

        // 先解绑并跑一帧：清掉上一个场景（并顺带清飞行金币/自己的 FX 句柄）。
        CoinCourierRuntime.Bind(null);
        BuildWorld(campfire);
        Tick();
        ResetRecorders();
        CoinCourierRuntime.Bind(State);
    }

    private void BuildWorld(float campfire)
    {
        var ground = new GameObject("Ground") { layer = 8 };
        var groundCollider = new BoxCollider2D
        {
            gameObject = ground,
            bounds = new Bounds(new Vector3(-1000f, -10f, 0f),
                new Vector3(1000f, SimPhysics.GroundTop, 0f)),
        };
        World.GroundCollider = groundCollider;
        SimPhysics.Ground = groundCollider;

        var layer = new GameObject("GameLayer");
        World.gameLayer = layer.transform;

        Banker = new GameObject("Banker").AddComponent<Banker>();
        Kingdom.campfirePosition = campfire;
        Kingdom.banker = Banker;
        CoinCourierBankScope.Current = Banker;
        CoinCourierBankScope.Reason = CoinCourierBankReason.Ready;

        Managers.Inst = new Managers
        {
            game = new Game { state = Game.State.Playing },
            world = World,
            kingdom = Kingdom,
        };
    }

    /// <summary>每用例前的记录器/共享状态复位；真实 Visuals 走 ShutdownModule 释放图集。</summary>
    internal static void ResetRecorders()
    {
        CoinCourierEconomy.Reset();
        CoinCourierTargeting.Reset();
        CoinCourierVisuals.ShutdownModule();
        CoinCourierCoinFlight.Clear();
        CoinCourierTeleportFx.Reset();
        CoinCourierShop.Reset();
        PatchEconomy_Banker.Reset();
        ResetPersistence();
        Scanner.Enemy = null;
        Scanner.ScanCalls = 0;
        Scanner.ThrowOnScan = false;
        GameObject.FailCourierProbeCreation = false;
        NetworkBigBoss.HasWorldAuth = true;
        NetworkBigBoss.IsOnline = false;
        IslandSaveData.isSavingGame = false;
        Time.timeScale = 1f;
        ModConfig.Enabled.Value = true;
        ModConfig.CoinCourierEnabled.Value = true;
        CourierVisualProbe.Reset();
    }

    /// <summary>清掉真实持久化（生产静态）与存档夹具，保证每个用例从干净机器开始。</summary>
    internal static void ResetPersistence()
    {
        GlobalSaveData._loaded = null;
        CampaignSaveData.current = null;
        IslandSaveData.isSavingGame = false;
        IslandSaveData.CurrentlySavingIsland = null;
        Game.SavingEnabled = true;
        Type type = typeof(CoinCourierPersistence);
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Static;
        type.GetField("_bound", Flags)?.SetValue(null, null);
        type.GetField("_scope", Flags)?.SetValue(null, null);
        object logged = type.GetField("LoggedOnce", Flags)?.GetValue(null);
        logged?.GetType().GetMethod("Clear", Type.EmptyTypes)?.Invoke(logged, null);
    }

    internal void Tick(float dt = 1f / 60f)
    {
        Time.deltaTime = dt;
        Time.time += dt;
        Time.unscaledTime += dt;
        Time.frameCount++;
        CoinCourierRuntime.Tick();
        CourierVisualProbe.Capture();
    }

    internal void Step(float seconds, float dt = 1f / 60f)
    {
        int frames = Math.Max(1, (int)Math.Ceiling(seconds / dt));
        for (int i = 0; i < frames; i++) Tick(dt);
    }

    internal bool RunUntil(Func<bool> predicate, float maxSeconds, float dt = 1f / 60f)
    {
        int frames = Math.Max(1, (int)Math.Ceiling(maxSeconds / dt));
        for (int i = 0; i < frames; i++)
        {
            if (predicate()) return true;
            Tick(dt);
        }
        return predicate();
    }

    /// <summary>造一个指定朝向与位置的骑士（stub 目标层直接给 plan）。</summary>
    internal Knight MakeKnight(Side side, float x)
    {
        var knight = new GameObject("Knight").AddComponent<Knight>();
        knight.side = side;
        knight.Wallet = new Wallet { Coins = 0, TotalCapacity = 8 };
        knight.transform.position = new Vector3(x, SimPhysics.GroundTop, 0f);
        return knight;
    }

    /// <summary>让目标层在下一次查询时返回这个骑士与计划。</summary>
    internal void ArmVisit(Knight knight, long life, int side, int quota)
    {
        CoinCourierTargeting.Target = knight;
        CoinCourierTargeting.Plan = new CoinCourierRules.VisitPlan(life, side, quota);
        CoinCourierTargeting.SelectResult = true;
    }

    internal void DisarmVisit()
    {
        CoinCourierTargeting.SelectResult = false;
    }

    /// <summary>当前存活的自有 view 根（真实 Visuals 不暴露句柄；按对象名与 fake-null 过滤）。</summary>
    internal GameObject LiveViewRoot() => CourierVisualProbe.LiveRoot;

    /// <summary>本用例累计创建过的 KEM_CoinCourierView 对象数（含已销毁）。</summary>
    internal static int ViewObjectsCreated()
    {
        int count = 0;
        for (int i = 0; i < GameObject.All.Count; i++)
        {
            if (GameObject.All[i].name == "KEM_CoinCourierView") count++;
        }
        return count;
    }
}

namespace KingdomEnhancedMod
{
    /// <summary>测试观察点：每 tick 后读当前存活的 courier view 根并记录其位置。</summary>
    internal static class CourierVisualProbe
    {
        internal static Vector3 LastPosition { get; private set; }

        internal static GameObject LiveRoot { get; private set; }

        internal static void Capture()
        {
            GameObject root = null;
            for (int i = GameObject.All.Count - 1; i >= 0; i--)
            {
                GameObject candidate = GameObject.All[i];
                if (!candidate.Destroyed && candidate.name == "KEM_CoinCourierView")
                {
                    root = candidate;
                    break;
                }
            }
            LiveRoot = root;
            if (root != null) LastPosition = root.transform.position;
        }

        internal static void Reset()
        {
            LastPosition = default;
            LiveRoot = null;
        }
    }
}
