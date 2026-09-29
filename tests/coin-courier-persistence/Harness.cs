// Harness.cs — 确定性夹具：每个 case 从干净状态开始（stub 静态 + 生产静态一起复位），
// 再构造世界/campaign/prefs；保存流程按实际 2.4 顺序驱动生产边界入口。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using KingdomEnhancedMod;

static class Harness
{
    public const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    public const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly Type Persist = typeof(CoinCourierPersistence);
    private static readonly Type Runtime = typeof(CoinCourierRuntime);

    /// <summary>下一次 IslandSaveData.Save 是否走“原生内部 catch”（不触发 marker）。</summary>
    public static bool NextSaveEnumerationFails;
    /// <summary>保存体内（设置 CurrentlySavingIsland 之后、marker 之前）的注入点。</summary>
    public static Action DuringSave;
    public static string LastPayload;

    private static int _passed, _failed;

    public static void Test(string label, Action body)
    {
        Reset();
        try
        {
            body();
            _passed++;
            Console.WriteLine("PASS " + label);
        }
        catch (Exception e)
        {
            _failed++;
            Console.WriteLine("FAIL " + label + ": " + e.Message);
        }
    }

    public static int Finish()
    {
        Console.WriteLine(_passed + " passed, " + _failed + " failed");
        return _failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------ assertions
    public static void True(bool value, string label)
    {
        if (!value) throw new Exception(label + " (expected true)");
    }

    public static void False(bool value, string label)
    {
        if (value) throw new Exception(label + " (expected false)");
    }

    public static void Eq(int expected, int actual, string label)
    {
        if (expected != actual) throw new Exception(label + ": " + actual + ", expected " + expected);
    }

    public static void Eq(long expected, long actual, string label)
    {
        if (expected != actual) throw new Exception(label + ": " + actual + ", expected " + expected);
    }

    public static void Eq(string expected, string actual, string label)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new Exception(label + ": '" + actual + "', expected '" + expected + "'");
    }

    public static void Contains(string haystack, string needle, string label)
    {
        if (haystack == null || !haystack.Contains(needle))
            throw new Exception(label + ": '" + haystack + "' does not contain '" + needle + "'");
    }

    // ------------------------------------------------------------- fixture
    public static void Reset()
    {
        GlobalSaveData._loaded = null;
        CampaignSaveData.current = null;
        IslandSaveData.isSavingGame = false;
        IslandSaveData.CurrentlySavingIsland = null;
        NetworkBigBoss.HasWorldAuth = true;
        NetworkBigBoss.IsOnline = false;
        Game.SavingEnabled = true;
        UnityEngine.Time.timeScale = 1f;
        Managers.Inst = null;
        KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
        CoinCourierRuntime.Reset();
        CoinCourierShop.Reset();
        NextSaveEnumerationFails = false;
        DuringSave = null;
        LastPayload = null;

        SetStatic(Persist, "_bound", null);
        SetStatic(Persist, "_scope", null);
        ClearStatic(Persist, "LoggedOnce");
    }

    public static Managers NewWorld()
    {
        World world = new World { gameLayer = new Transform() };
        Managers managers = new Managers { world = world, game = new Game() };
        Managers.Inst = managers;
        return managers;
    }

    public static GlobalSaveData NewGlobal(int campaignCount, int current = 0, int land = 1)
    {
        GlobalSaveData global = new GlobalSaveData { currentCampaign = current, currentChallenge = 0 };
        for (int i = 0; i < campaignCount; i++) global.campaigns.Add(new CampaignSaveData { CurrentLand = land });
        GlobalSaveData._loaded = global;
        CampaignSaveData.current = campaignCount > 0 ? global.campaigns[current] : null;
        SetSceneLand(land);
        return global;
    }

    /// <summary>把一份已有 global（同 prefs 内容）复制成“读档后的新实例”。</summary>
    public static GlobalSaveData Reload(GlobalSaveData source, int current = 0, int land = 1)
    {
        GlobalSaveData global = new GlobalSaveData { currentCampaign = current, currentChallenge = 0 };
        foreach (KeyValuePair<string, string> pair in source.prefs.contents) global.prefs.contents[pair.Key] = pair.Value;
        for (int i = 0; i < source.campaigns.Count; i++) global.campaigns.Add(new CampaignSaveData { CurrentLand = land });
        GlobalSaveData._loaded = global;
        CampaignSaveData.current = source.campaigns.Count > 0 ? global.campaigns[current] : null;
        SetSceneLand(land);
        return global;
    }

    // ------------------------------------------------------------ drivers
    public static void Tick() => CoinCourierPersistence.Tick();

    /// <summary>驱动一次岛保存（含 scope 与 marker）；默认保存现场岛（Game.currentLand），
    /// 与真实离岛顺序一致（可显式传 land 模拟旁岛/Decay 或 land=-1）。</summary>
    public static void IslandSave(GlobalSaveData global, bool marker = true, int? land = null)
    {
        NextSaveEnumerationFails = !marker;
        IslandSaveData.Save(global.currentCampaign, land ?? SceneLand, global.currentChallenge);
        NextSaveEnumerationFails = false;
    }

    /// <summary>只走 PrepareBeforeSave 的菜单/全局保存（无岛捕获）。</summary>
    public static void GlobalSaveOnly(GlobalSaveData global) => global.SaveAsync(null);

    /// <summary>到达/切换到某岛：campaign 进度与现场（Game.currentLand）一起更新。</summary>
    public static void SetLand(GlobalSaveData global, int land)
    {
        SetCampaignLandOnly(global, land);
        SetSceneLand(land);
    }

    /// <summary>只改 campaign.CurrentLand（真实离岛顺序：先改目的地，现场仍是出发岛）。</summary>
    public static void SetCampaignLandOnly(GlobalSaveData global, int land)
    {
        for (int i = 0; i < global.campaigns.Count; i++) global.campaigns[i].CurrentLand = land;
    }

    public static int SceneLand
    {
        get
        {
            Managers managers = Managers.Inst;
            if (managers != null && managers.game != null) return managers.game.currentLand;
            return CampaignSaveData.current != null ? CampaignSaveData.current.CurrentLand : 1;
        }
    }

    public static void SetSceneLand(int land)
    {
        Managers managers = Managers.Inst;
        if (managers != null && managers.game != null) managers.game.currentLand = land;
    }

    // --------------------------------------------------------- stored data
    public static string StoredRaw(GlobalSaveData global)
    {
        return global.prefs.contents.TryGetValue(CoinCourierSaveSchema.Key, out string raw) ? raw : null;
    }

    public static CoinCourierDocument StoredDocument(GlobalSaveData global)
    {
        string raw = StoredRaw(global);
        if (raw == null) return null;
        if (!CoinCourierSaveCodec.TryParse(raw, out CoinCourierDocument document, out string reason))
            throw new Exception("stored document unparsable: " + reason);
        return document;
    }

    public static CoinCourierCampaignRecord StoredRecord(GlobalSaveData global, string guid)
    {
        CoinCourierDocument document = StoredDocument(global);
        if (document == null) return null;
        foreach (CoinCourierCampaignRecord record in document.Campaigns)
            if (record.Guid == guid) return record;
        return null;
    }

    // ---------------------------------------------------- binding reflection
    public static ICoinCourierCampaignState State => CoinCourierRuntime.State;

    public static ICoinCourierCampaignState BindingFor(CampaignSaveData campaign)
    {
        object bound = GetStatic(Persist, "_bound");
        if (bound == null) return null;
        object map = GetField(bound, "ByCampaign");
        foreach (object entry in (IEnumerable)map)
        {
            Type entryType = entry.GetType();
            IntPtr key = (IntPtr)entryType.GetProperty("Key").GetValue(entry);
            if (key == campaign.Pointer)
                return (ICoinCourierCampaignState)entryType.GetProperty("Value").GetValue(entry);
        }
        return null;
    }

    public static string GuidOf(CampaignSaveData campaign)
    {
        ICoinCourierCampaignState state = BindingFor(campaign);
        return state == null ? null : (string)GetField(state, "Guid");
    }

    public static bool HasStageFault(CampaignSaveData campaign)
    {
        ICoinCourierCampaignState state = BindingFor(campaign);
        return state != null && GetField(state, "StageFault") != null;
    }

    public static bool ScopeIsNull => GetStatic(Persist, "_scope") == null;

    public static bool IsClosed
    {
        get
        {
            object bound = GetStatic(Persist, "_bound");
            return bound != null && (bool)GetField(bound, "Closed");
        }
    }

    // ------------------------------------------------------------- reflection
    public static object GetStatic(Type type, string name)
    {
        FieldInfo field = type.GetField(name, AnyStatic) ?? throw new Exception("missing static " + type.Name + "." + name);
        return field.GetValue(null);
    }

    public static void SetStatic(Type type, string name, object value)
    {
        FieldInfo field = type.GetField(name, AnyStatic) ?? throw new Exception("missing static " + type.Name + "." + name);
        field.SetValue(null, value);
    }

    public static void ClearStatic(Type type, string name)
    {
        object collection = GetStatic(type, name);
        if (collection == null) return;
        collection.GetType().GetMethod("Clear", Type.EmptyTypes).Invoke(collection, null);
    }

    public static object GetField(object instance, string name)
    {
        FieldInfo field = instance.GetType().GetField(name, AnyInstance) ?? throw new Exception("missing field " + instance.GetType().Name + "." + name);
        return field.GetValue(instance);
    }
}
