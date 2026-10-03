using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
using UnityEngine;

internal sealed class BridgeState : ICoinCourierCampaignState
{
    public bool Owned { get; set; } = true;
    public CoinCourierPurse Purse { get; set; } = PurseWith(0);
    public int RecordCalls { get; set; }

    private CoinCourierAvailabilityInfo _availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Ready);

    /// <summary>详细评估（行为门与状态文案共用）；测试按需设置具体种类。</summary>
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
/// 确定性场景驱动：搭起一个"可玩世界 + 银行 + 骑士"的最小边界，按帧推进真实运行时，
/// 并暴露观察点（视觉/经济/目标/FX 的调用记录都在 Stubs 里）。
/// </summary>
internal sealed class Harness
{
    internal readonly BridgeState State = new BridgeState();
    internal readonly Kingdom Kingdom = new Kingdom();
    internal readonly World World = new World();
    internal readonly List<TraceSample> Trace = new List<TraceSample>();
    internal Banker Banker;
    internal Knight MovingTarget;
    internal float MovingTargetSpeed;
    internal int Ticks;

    internal struct TraceSample
    {
        internal float Time;
        internal float X;
        internal CoinCourierPose Pose;
    }


    internal Harness(float campfire = 0f)
    {
        SimPhysics.Reset();
        Managers.Inst = null;
        ResetPersistence();
        KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();

        // 先解绑并跑一帧：上一个场景被清掉（并顺带清飞行金币/自己的 FX 句柄）。
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

    internal static void ResetRecorders()
    {
        CoinCourierEconomy.Reset();
        CoinCourierTargeting.Reset();
        CoinCourierVisuals.Reset();
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
    }

    /// <summary>清掉真实持久化（生产静态）与存档夹具，保证每个 bridge case 从干净机器开始。</summary>
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

    /// <summary>
    /// 搭起"真实 GlobalSaveData + 真实持久化 owner"夹具：运行时经生产 Tick 绑定到真实
    /// CampaignBinding（可选预置损坏/缺失的键），供持久化↔运行时文案桥接用例使用。
    /// </summary>
    internal GlobalSaveData BuildRealPersistence(int campaignCount = 1, int current = 0, int land = 1,
        string rawKey = null)
    {
        ResetPersistence();
        CoinCourierRuntime.Bind(null);   // 夹具模拟冷启动：不保留任何旧接线
        var layer = new GameObject("PersistLayer");
        var managers = new Managers
        {
            game = new Game
            {
                state = Game.State.Playing,
                currentLand = land,
                _saveGameWithFailurePromptRoutine = new HagletStub(),
                saveGameWithFailurePrompt = new HagletStub(),
            },
            world = new World { gameLayer = layer.transform },
            kingdom = Kingdom,
        };
        Managers.Inst = managers;
        var global = new GlobalSaveData { currentCampaign = current, currentChallenge = 0 };
        if (rawKey != null) global.prefs.contents[CoinCourierSaveSchema.Key] = rawKey;
        for (int i = 0; i < campaignCount; i++)
            global.campaigns.Add(new CampaignSaveData { CurrentLand = land });
        GlobalSaveData._loaded = global;
        CampaignSaveData.current = campaignCount > 0 ? global.campaigns[current] : null;
        CoinCourierPersistence.Tick();
        return global;
    }

    internal void Tick(float dt = 1f / 60f)
    {
        Time.deltaTime = dt;
        Time.time += dt;
        Time.unscaledTime += dt;
        Time.frameCount++;
        Ticks++;
        if (MovingTarget != null && MovingTargetSpeed != 0f)
        {
            Vector3 position = MovingTarget.transform.position;
            MovingTarget.transform.position = new Vector3(position.x + MovingTargetSpeed * dt, position.y, position.z);
        }
        CoinCourierRuntime.Tick();
        CoinCourierPose pose = CoinCourierVisuals.LastPose;
        Trace.Add(new TraceSample { Time = Time.time, X = CoinCourierVisuals.LastPosition.x, Pose = pose });
    }

    /// <summary>trace 中的姿态转换次数（from → to，相邻样本）。</summary>
    internal int TransitionCount(CoinCourierPose from, CoinCourierPose to)
    {
        int count = 0;
        for (int i = 1; i < Trace.Count; i++)
        {
            if (Trace[i - 1].Pose == from && Trace[i].Pose == to) count++;
        }
        return count;
    }

    /// <summary>每条 Run 连续段的（样本数、路径长度、起点偏离最大值），用于"有界追逐"断言。</summary>
    internal void RunStretches(List<(float Path, float MaxDeviation)> result)
    {
        result.Clear();
        float path = 0f;
        float startX = 0f;
        float maxDeviation = 0f;
        bool inRun = false;
        float previousX = 0f;
        for (int i = 0; i < Trace.Count; i++)
        {
            TraceSample sample = Trace[i];
            if (sample.Pose == CoinCourierPose.Run)
            {
                if (!inRun)
                {
                    inRun = true;
                    path = 0f;
                    startX = sample.X;
                    maxDeviation = 0f;
                }
                else
                {
                    path += Math.Abs(sample.X - previousX);
                }
                float deviation = Math.Abs(sample.X - startX);
                if (deviation > maxDeviation) maxDeviation = deviation;
                previousX = sample.X;
            }
            else if (inRun)
            {
                inRun = false;
                result.Add((path, maxDeviation));
            }
        }
        if (inRun) result.Add((path, maxDeviation));
    }

    internal int RunStretchesCount()
    {
        var buffer = new List<(float Path, float MaxDeviation)>();
        RunStretches(buffer);
        return buffer.Count;
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

    /// <summary>造一个指定朝向与位置的骑士；life 由测试选定的 plan 决定（stub 目标层直接给 plan）。</summary>
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
}
