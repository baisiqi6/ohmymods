// 金币哥布林持久化套件：链接未修改的生产核（CoinCourierPersistence / CoinCourierSaveData /
// CoinCourierPurse / CoinCourierCampaignState），对 Stubs.cs 的原生边界模型驱动
// IslandSaveData.Save → UpdateSavedWithRevisions → PrefsSaveData.PrepareBeforeSave 全链。
//
// 运行：C:/Users/ADMIN/dotnet8/dotnet.exe run -c Release --project tests/coin-courier-persistence
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KingdomEnhancedMod;

// =========================================================================
// 纯 codec
// =========================================================================

Harness.Test("codec roundtrips every purse/fault field", () =>
{
    var fault = new CoinCourierFault(CoinCourierFaultKind.DeliveryUnknown, CoinCourierReason.WalletWriteUnknown,
        -1, 7, 3, -1, 91);
    var snapshot = new CoinCourierPurseSnapshot(7, true, 91, fault);
    CoinCourierDocument document = CoinCourierSaveCodec.CreateDocument();
    document.Campaigns.Add(new CoinCourierCampaignRecord
    {
        Slot = 3,
        Guid = "0f6f6a54-8df1-4c37-9a4a-5cbb0f2a1b2c",
        Owned = true,
        Purse = CoinCourierSaveCodec.ToPurseRecord(snapshot),
    });
    document.Campaigns.Add(new CoinCourierCampaignRecord
    {
        Slot = 0,
        Guid = "9c4a9d3c-1111-2222-3333-444455556666",
        Owned = false,
        Purse = CoinCourierSaveCodec.ToPurseRecord(CoinCourierSaveCodec.EmptyPurseSnapshot),
    });
    Harness.True(CoinCourierSaveCodec.TrySerialize(document, out string json, out string serializeReason),
        "serialize ok: " + serializeReason);
    Harness.True(CoinCourierSaveCodec.TryParse(json, out CoinCourierDocument parsed, out string parseReason),
        "parse ok: " + parseReason);
    Harness.Eq(1, parsed.Version, "version roundtrip");
    Harness.Eq(2, parsed.Campaigns.Count, "record count");
    CoinCourierCampaignRecord first = parsed.Campaigns[0];
    Harness.Eq(3, first.Slot, "slot");
    Harness.Eq("0f6f6a54-8df1-4c37-9a4a-5cbb0f2a1b2c", first.Guid, "guid");
    Harness.True(first.Owned, "owned");
    Harness.True(CoinCourierSaveCodec.TryReadPurse(first.Purse, out CoinCourierPurseSnapshot roundtrip, out string purseReason),
        "purse parses: " + purseReason);
    Harness.Eq(7, roundtrip.Coins, "coins");
    Harness.True(roundtrip.PendingDelivery, "pending");
    Harness.Eq(91, roundtrip.PendingLife, "pending life");
    Harness.Eq((int)CoinCourierFaultKind.DeliveryUnknown, (int)roundtrip.Fault.Kind, "fault kind");
    Harness.Eq((int)CoinCourierReason.WalletWriteUnknown, (int)roundtrip.Fault.Reason, "fault reason");
    Harness.Eq(-1, roundtrip.Fault.BankBefore, "bank before");
    Harness.Eq(7, roundtrip.Fault.BankAfter, "bank after");
    Harness.Eq(3, roundtrip.Fault.WalletBefore, "wallet before");
    Harness.Eq(-1, roundtrip.Fault.WalletAfter, "wallet after");
    Harness.Eq(91, roundtrip.Fault.ExpectedLife, "fault life");
    Harness.True(parsed.Campaigns[1].Purse.Fault == null, "no-fault record omits the fault object");
    Harness.False(parsed.Campaigns[1].Owned, "second record owned=false");
});

Harness.Test("codec rejects unknown version, malformed json and duplicate identity", () =>
{
    Harness.False(CoinCourierSaveCodec.TryParse("{\"version\":2,\"campaigns\":[]}", out _, out string version),
        "unknown version refused");
    Harness.Eq("version", version, "version reason");
    Harness.False(CoinCourierSaveCodec.TryParse("{not json", out _, out string malformed), "malformed refused");
    Harness.Eq("json", malformed, "malformed reason");
    Harness.False(CoinCourierSaveCodec.TryParse("{\"version\":1,\"campaigns\":\"x\"}", out _, out _), "wrong shape refused");
    const string guidA = "0f6f6a54-8df1-4c37-9a4a-5cbb0f2a1b2c";
    const string guidB = "9c4a9d3c-1111-2222-3333-444455556666";
    string duplicateSlot = "{\"version\":1,\"campaigns\":["
        + "{\"slot\":1,\"guid\":\"" + guidA + "\",\"owned\":false,\"purse\":{\"coins\":0,\"pendingDelivery\":false,\"pendingLife\":0}},"
        + "{\"slot\":1,\"guid\":\"" + guidB + "\",\"owned\":false,\"purse\":{\"coins\":0,\"pendingDelivery\":false,\"pendingLife\":0}}]}";
    Harness.False(CoinCourierSaveCodec.TryParse(duplicateSlot, out _, out string slotReason), "duplicate slot refused");
    Harness.Eq("slot-duplicate", slotReason, "duplicate-slot reason");
    string duplicateGuid = "{\"version\":1,\"campaigns\":["
        + "{\"slot\":0,\"guid\":\"" + guidA + "\",\"owned\":false,\"purse\":{\"coins\":0,\"pendingDelivery\":false,\"pendingLife\":0}},"
        + "{\"slot\":1,\"guid\":\"" + guidA + "\",\"owned\":false,\"purse\":{\"coins\":0,\"pendingDelivery\":false,\"pendingLife\":0}}]}";
    Harness.False(CoinCourierSaveCodec.TryParse(duplicateGuid, out _, out string guidReason), "duplicate guid refused");
    Harness.Eq("guid-duplicate", guidReason, "duplicate-guid reason");
});

Harness.Test("codec rejects out-of-range and inconsistent purse/fault fields", () =>
{
    static bool Reject(string json, string expected, string label)
    {
        bool ok = CoinCourierSaveCodec.TryParse(json, out _, out string reason);
        Harness.False(ok, label + " refused");
        Harness.Eq(expected, reason, label + " reason");
        return true;
    }
    const string guid = "0f6f6a54-8df1-4c37-9a4a-5cbb0f2a1b2c";
    Reject("{\"version\":1,\"campaigns\":[{\"slot\":0,\"guid\":\"bad\",\"owned\":false,\"purse\":{\"coins\":0,\"pendingDelivery\":false,\"pendingLife\":0}}]}",
        "guid", "bad guid");
    Reject("{\"version\":1,\"campaigns\":[{\"slot\":-1,\"guid\":\"" + guid + "\",\"owned\":false,\"purse\":{\"coins\":0,\"pendingDelivery\":false,\"pendingLife\":0}}]}",
        "slot-range", "negative slot");
    Reject("{\"version\":1,\"campaigns\":[{\"slot\":0,\"guid\":\"" + guid + "\",\"owned\":false,\"purse\":{\"coins\":-2,\"pendingDelivery\":false,\"pendingLife\":0}}]}",
        "coins-range", "negative coins");
    Reject("{\"version\":1,\"campaigns\":[{\"slot\":0,\"guid\":\"" + guid + "\",\"owned\":false,\"purse\":{\"coins\":1,\"pendingDelivery\":true,\"pendingLife\":0}}]}",
        "pending-life", "pending without life");
    Reject("{\"version\":1,\"campaigns\":[{\"slot\":0,\"guid\":\"" + guid + "\",\"owned\":false,\"purse\":{\"coins\":1,\"pendingDelivery\":false,\"pendingLife\":4}}]}",
        "pending-flag", "life without pending flag");
    Reject("{\"version\":1,\"campaigns\":[{\"slot\":0,\"guid\":\"" + guid + "\",\"owned\":false,\"purse\":{\"coins\":0,\"pendingDelivery\":false,\"pendingLife\":0,\"fault\":{\"kind\":9,\"reason\":0,\"bankBefore\":-1,\"bankAfter\":-1,\"walletBefore\":-1,\"walletAfter\":-1,\"expectedLife\":0}}}]}",
        "fault-kind", "unknown fault kind");
    Reject("{\"version\":1,\"campaigns\":[{\"slot\":0,\"guid\":\"" + guid + "\",\"owned\":false,\"purse\":{\"coins\":0,\"pendingDelivery\":false,\"pendingLife\":0,\"fault\":{\"kind\":1,\"reason\":999,\"bankBefore\":-1,\"bankAfter\":-1,\"walletBefore\":-1,\"walletAfter\":-1,\"expectedLife\":0}}}]}",
        "fault-reason", "unknown fault reason");
    Reject("{\"version\":1,\"campaigns\":[{\"slot\":0,\"guid\":\"" + guid + "\",\"owned\":false,\"purse\":{\"coins\":0,\"pendingDelivery\":false,\"pendingLife\":0,\"fault\":{\"kind\":1,\"reason\":0,\"bankBefore\":-2,\"bankAfter\":-1,\"walletBefore\":-1,\"walletAfter\":-1,\"expectedLife\":0}}}]}",
        "fault-range", "fault band < -1");
    Reject("{\"version\":1,\"campaigns\":[{\"slot\":0,\"guid\":\"" + guid + "\",\"owned\":false,\"purse\":null}]}",
        "purse-missing", "missing purse");
});

Harness.Test("codec refuses over-cap documents and reports empty-state content rules", () =>
{
    CoinCourierDocument document = CoinCourierSaveCodec.CreateDocument();
    for (int i = 0; i < CoinCourierSaveSchema.MaxCampaigns + 1; i++)
    {
        document.Campaigns.Add(new CoinCourierCampaignRecord
        {
            Slot = i,
            Guid = CoinCourierSaveCodec.CreateGuid(),
            Purse = CoinCourierSaveCodec.ToPurseRecord(CoinCourierSaveCodec.EmptyPurseSnapshot),
        });
    }
    Harness.False(CoinCourierSaveCodec.TrySerialize(document, out _, out string serializeReason), "over-cap serialize refused");
    Harness.Eq("too-many-campaigns", serializeReason, "serialize cap reason");
    Harness.False(CoinCourierSaveCodec.HasContent(false, CoinCourierSaveCodec.EmptyPurseSnapshot), "unowned empty has no content");
    Harness.True(CoinCourierSaveCodec.HasContent(true, CoinCourierSaveCodec.EmptyPurseSnapshot), "owned has content");
    var coins = new CoinCourierPurseSnapshot(1, false, 0, default);
    Harness.True(CoinCourierSaveCodec.HasContent(false, coins), "coins alone have content");
    var faulted = new CoinCourierPurseSnapshot(0, false, 0,
        new CoinCourierFault(CoinCourierFaultKind.BagUnknown, CoinCourierReason.BankWriteUnknown, 1, -1, -1, -1, 0));
    Harness.True(CoinCourierSaveCodec.HasContent(false, faulted), "fault alone has content");
});

// =========================================================================
// 绑定 / Ready 门
// =========================================================================

static GlobalSaveData LiveWorld(int campaignCount = 1, int current = 0, int land = 1)
{
    Harness.NewWorld();
    GlobalSaveData global = Harness.NewGlobal(campaignCount, current, land);
    Harness.Tick();
    return global;
}

Harness.Test("first use binds native campaigns without writing a key", () =>
{
    GlobalSaveData global = LiveWorld(2);
    Harness.True(Harness.State != null, "runtime bound to the current campaign");
    Harness.False(Harness.State.Owned, "not owned on first use");
    Harness.Eq(0, Harness.State.Purse.Coins, "zero purse on first use");
    Harness.True(Harness.State.Ready, "ready in the live context");
    Harness.Eq(null, Harness.StoredRaw(global), "no key written before any capture");
    ICoinCourierCampaignState other = Harness.BindingFor(global.campaigns[1]);
    Harness.True(other != null, "second campaign bound too");
    Harness.False(Harness.State == other, "distinct states per campaign");
});

Harness.Test("ready requires the exact live/native gates", () =>
{
    GlobalSaveData global = LiveWorld();
    Harness.True(Harness.State.Ready, "baseline ready");
    Managers managers = Managers.Inst;
    managers.world = null;
    Harness.False(Harness.State.Ready, "no world => not ready");
    managers.world = new World { gameLayer = new Transform() };
    Harness.True(Harness.State.Ready, "world restored");
    managers.game.state = Game.State.Menu;
    Harness.False(Harness.State.Ready, "menu state => not ready");
    managers.game.state = Game.State.Playing;
    UnityEngine.Time.timeScale = 0f;
    Harness.False(Harness.State.Ready, "paused => not ready");
    UnityEngine.Time.timeScale = 1f;
    Game.SavingEnabled = false;
    Harness.False(Harness.State.Ready, "SavingEnabled=false => not ready");
    Game.SavingEnabled = true;
    IslandSaveData.isSavingGame = true;
    Harness.False(Harness.State.Ready, "island capture in progress => not ready");
    IslandSaveData.isSavingGame = false;
    NetworkBigBoss.IsOnline = true;
    Harness.False(Harness.State.Ready, "online => not ready");
    NetworkBigBoss.IsOnline = false;
    NetworkBigBoss.HasWorldAuth = false;
    Harness.False(Harness.State.Ready, "no authority => not ready");
    NetworkBigBoss.HasWorldAuth = true;
    Harness.True(Harness.State.Ready, "all gates restored");
});

Harness.Test("ready reads both native prompt haglets and refuses unknown casts", () =>
{
    GlobalSaveData global = LiveWorld();
    Game game = Managers.Inst.game;
    var outer = new Coatsink.Common.Haglet();
    var inner = new Coatsink.Common.Haglet();
    outer.state = Coatsink.Common.Haglet.State.Completed;
    inner.state = Coatsink.Common.Haglet.State.Stopped;
    game._saveGameWithFailurePromptRoutine = outer;
    game.saveGameWithFailurePrompt = inner;
    Harness.True(Harness.State.Ready, "stopped/completed haglets are idle");
    outer.state = Coatsink.Common.Haglet.State.Started;
    Harness.False(Harness.State.Ready, "outer started => not ready");
    outer.state = Coatsink.Common.Haglet.State.Paused;
    Harness.False(Harness.State.Ready, "outer paused => not ready");
    outer.state = Coatsink.Common.Haglet.State.Stopped;
    inner.state = Coatsink.Common.Haglet.State.Started;
    Harness.False(Harness.State.Ready, "inner started => not ready");
    inner.state = Coatsink.Common.Haglet.State.Completed;
    Harness.True(Harness.State.Ready, "inner completed => ready");
    game._saveGameWithFailurePromptRoutine = new Coatsink.Common.ForeignCallable();
    Harness.False(Harness.State.Ready, "uncastable haglet => not ready (explicit refusal)");
    game._saveGameWithFailurePromptRoutine = null;
    Harness.False(Harness.State.Ready, "missing haglet => not ready");
    _ = global;
});

Harness.Test("ready is scoped to the current normal campaign", () =>
{
    GlobalSaveData global = LiveWorld(2, 1);
    ICoinCourierCampaignState first = Harness.BindingFor(global.campaigns[0]);
    ICoinCourierCampaignState second = Harness.BindingFor(global.campaigns[1]);
    Harness.False(first.Ready, "non-current campaign is not ready");
    Harness.True(second.Ready, "current campaign is ready");
    Harness.True(ReferenceEquals(Harness.State, second), "runtime bound to the current campaign");
    GlobalSaveData._loaded.currentChallenge = 3;
    Harness.False(second.Ready, "challenge mode is not open in this first version");
});

Harness.Test("recruitment is live-only until the next island capture", () =>
{
    GlobalSaveData global = LiveWorld();
    Harness.True(Harness.State.TryRecordRecruitment(), "first recruitment recorded");
    Harness.True(Harness.State.Owned, "owned flips live");
    Harness.Eq(null, Harness.StoredRaw(global), "nothing staged before the island capture");
    Harness.GlobalSaveOnly(global);
    Harness.Eq(null, Harness.StoredRaw(global), "a menu save must not create the key before any island capture");
    Harness.True(Harness.State.TryRecordRecruitment(), "repeat call is idempotent");
    Harness.True(Harness.State.Owned, "still a single identity");
    Harness.IslandSave(global);
    CoinCourierCampaignRecord staged = Harness.StoredRecord(global, Harness.GuidOf(global.campaigns[0]));
    Harness.True(staged != null, "capture persists the identity");
    Harness.True(staged.Owned, "staged owned");
    GlobalSaveData reloaded = Harness.Reload(global);
    Harness.Tick();
    Harness.True(Harness.State.Owned, "reload restores ownership");
});

Harness.Test("recruitment refuses while not ready and changes nothing", () =>
{
    GlobalSaveData global = LiveWorld();
    Managers.Inst.game.state = Game.State.Menu;
    Harness.False(Harness.State.TryRecordRecruitment(), "not ready => deterministic refusal");
    Harness.False(Harness.State.Owned, "ownership untouched");
    Harness.Eq(null, Harness.StoredRaw(global), "nothing written");
});

Harness.Test("bound module leaves the runtime unbound when the stored key is corrupt", () =>
{
    Harness.NewWorld();
    GlobalSaveData global = Harness.NewGlobal(1);
    global.prefs.contents[CoinCourierSaveSchema.Key] = "{broken";
    Harness.Tick();
    Harness.True(Harness.IsClosed, "module closed");
    Harness.True(Harness.State == null, "runtime not bound");
    int setCalls = global.prefs.SetCalls;
    Harness.IslandSave(global);
    Harness.Eq(setCalls, global.prefs.SetCalls, "closed module never rewrites the key");
    Harness.Eq("{broken", Harness.StoredRaw(global), "corrupt value untouched");
});

Harness.Test("null stored value and unknown version close the module without overwrite", () =>
{
    Harness.NewWorld();
    GlobalSaveData global = Harness.NewGlobal(1);
    global.prefs.contents[CoinCourierSaveSchema.Key] = null;
    Harness.Tick();
    Harness.True(Harness.IsClosed, "null value closed");
    Harness.True(Harness.State == null, "runtime not bound");
    Harness.True(global.prefs.contents.ContainsKey(CoinCourierSaveSchema.Key), "key still present");

    Harness.Reset();
    Harness.NewWorld();
    GlobalSaveData versioned = Harness.NewGlobal(1);
    versioned.prefs.contents[CoinCourierSaveSchema.Key] = "{\"version\":2,\"campaigns\":[]}";
    Harness.Tick();
    Harness.True(Harness.IsClosed, "unknown version closed");
    Harness.Eq("{\"version\":2,\"campaigns\":[]}", Harness.StoredRaw(versioned), "unknown version untouched");
});

Harness.Test("out-of-range stored slot closes the module", () =>
{
    Harness.NewWorld();
    GlobalSaveData global = Harness.NewGlobal(1);
    global.prefs.contents[CoinCourierSaveSchema.Key] =
        "{\"version\":1,\"campaigns\":[{\"slot\":5,\"guid\":\"0f6f6a54-8df1-4c37-9a4a-5cbb0f2a1b2c\",\"owned\":true,"
        + "\"purse\":{\"coins\":1,\"pendingDelivery\":false,\"pendingLife\":0}}]}";
    Harness.Tick();
    Harness.True(Harness.IsClosed, "out-of-range record closed");
    Harness.True(Harness.State == null, "runtime not bound");
});

// =========================================================================
// capture → staged → reload
// =========================================================================

Harness.Test("capture stages live purse and later live changes stay live until reload", () =>
{
    GlobalSaveData global = LiveWorld();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    CoinCourierCampaignRecord staged = Harness.StoredRecord(global, Harness.GuidOf(global.campaigns[0]));
    Harness.Eq(2, staged.Purse.Coins, "two coins staged");
    Harness.True(staged.Purse.PendingDelivery == false, "no pending staged");
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.Eq(3, Harness.State.Purse.Coins, "live advanced");
    Harness.Eq(2, Harness.StoredRecord(global, Harness.GuidOf(global.campaigns[0])).Purse.Coins,
        "staged unchanged without a new capture");
    string guidBeforeReload = Harness.GuidOf(global.campaigns[0]);
    GlobalSaveData reloaded = Harness.Reload(global);
    Harness.Tick();
    Harness.Eq(2, Harness.State.Purse.Coins, "reload restores the staged snapshot");
    Harness.Eq(guidBeforeReload, Harness.GuidOf(reloaded.campaigns[0]), "guid stable across reload");
});

Harness.Test("cross-island capture keeps the same guid and purse", () =>
{
    GlobalSaveData global = LiveWorld();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    string guid = Harness.GuidOf(global.campaigns[0]);
    Harness.SetLand(global, 2);
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    CoinCourierCampaignRecord staged = Harness.StoredRecord(global, guid);
    Harness.Eq(3, staged.Purse.Coins, "second island capture updates the same record");
    Harness.Eq(1, Harness.StoredDocument(global).Campaigns.Count, "single campaign remains a single record");
    GlobalSaveData reloaded = Harness.Reload(global, land: 2);
    Harness.Tick();
    Harness.Eq(guid, Harness.GuidOf(reloaded.campaigns[0]), "same guid after island change");
    Harness.Eq(3, Harness.State.Purse.Coins, "purse follows the campaign");
});

Harness.Test("outgoing island save stages while the campaign land already points at the destination", () =>
{
    GlobalSaveData global = LiveWorld();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    string guid = Harness.GuidOf(global.campaigns[0]);
    Harness.State.Purse.TryCreditTakenCoin(8);      // live=2, staged=1
    Harness.SetCampaignLandOnly(global, 2);         // 真实离岛顺序：进度先指目的地
    Harness.IslandSave(global, land: 1);            // 仍保存出发现场
    Harness.Eq(2, Harness.StoredRecord(global, guid).Purse.Coins,
        "outgoing scene save must stage the live purse (P1)");
    Harness.True(Harness.State.Ready, "the normal outgoing order is not a failure");
});

Harness.Test("an explicit save of another island is rejected and not a failure", () =>
{
    GlobalSaveData global = LiveWorld();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global, land: 2);            // 旁岛/Decay：目标 != 现场
    Harness.Eq(1, Harness.StoredRecord(global, Harness.GuidOf(global.campaigns[0])).Purse.Coins,
        "side-island save never stages the live purse");
    Harness.True(Harness.State.Ready, "side-island save is rejected without freezing");
});

Harness.Test("land=-1 resolves exactly like the native fallback", () =>
{
    GlobalSaveData global = LiveWorld();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global, land: -1);           // 现场=进度=1 → 原生目标 1 → stage
    Harness.True(Harness.StoredRecord(global, Harness.GuidOf(global.campaigns[0])) != null,
        "land=-1 with a matching campaign land stages");
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.SetCampaignLandOnly(global, 2);         // 进度已指目的地，现场仍 1
    Harness.IslandSave(global, land: -1);           // 原生会保存目的岛 → 不得 stage
    Harness.Eq(1, Harness.StoredRecord(global, Harness.GuidOf(global.campaigns[0])).Purse.Coins,
        "land=-1 resolving to a non-scene island is rejected");
});

Harness.Test("a world swap during the save scope is rejected and freezes", () =>
{
    GlobalSaveData global = LiveWorld();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.DuringSave = () => { Managers.Inst.world = new World { gameLayer = new Transform() }; };
    Harness.IslandSave(global);
    Harness.DuringSave = null;
    Harness.Eq(1, Harness.StoredRecord(global, Harness.GuidOf(global.campaigns[0])).Purse.Coins,
        "mid-save world swap never stages");
    Harness.False(Harness.State.Ready, "rejected capture freezes the character");
});

Harness.Test("a scene land change during the save scope is rejected and freezes", () =>
{
    GlobalSaveData global = LiveWorld();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.DuringSave = () => { Managers.Inst.game.currentLand = 2; };
    Harness.IslandSave(global);
    Harness.DuringSave = null;
    Harness.Eq(1, Harness.StoredRecord(global, Harness.GuidOf(global.campaigns[0])).Purse.Coins,
        "mid-save scene land change never stages");
    Harness.False(Harness.State.Ready, "rejected capture freezes the character");
});

Harness.Test("a campaign data swap during the save scope is rejected and freezes", () =>
{
    GlobalSaveData global = LiveWorld(2, 0);
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.DuringSave = () => { CampaignSaveData.current = global.campaigns[1]; };
    Harness.IslandSave(global);
    Harness.DuringSave = null;
    Harness.Eq(1, Harness.StoredRecord(global, Harness.GuidOf(global.campaigns[0])).Purse.Coins,
        "mid-save campaign data swap never stages");
    Harness.False(Harness.State.Ready, "rejected capture freezes the character");
});

Harness.Test("menu Prepare never samples the live purse", () =>
{
    GlobalSaveData global = LiveWorld();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.GlobalSaveOnly(global);
    CoinCourierCampaignRecord staged = Harness.StoredRecord(global, Harness.GuidOf(global.campaigns[0]));
    Harness.Eq(1, staged.Purse.Coins, "menu save keeps the last staged snapshot");
    Harness.Eq(3, Harness.State.Purse.Coins, "live purse untouched by the save");
});

Harness.Test("capture without the marker keeps old staged and freezes that character", () =>
{
    GlobalSaveData global = LiveWorld();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global, marker: false);
    Harness.Eq(1, Harness.StoredRecord(global, Harness.GuidOf(global.campaigns[0])).Purse.Coins,
        "no marker => old staged kept");
    Harness.False(Harness.State.Ready, "capture failure freezes the character");
    Harness.IslandSave(global);
    Harness.True(Harness.State.Ready, "next successful capture recovers");
    Harness.Eq(2, Harness.StoredRecord(global, Harness.GuidOf(global.campaigns[0])).Purse.Coins, "recovery staged the live purse");
});

Harness.Test("a capture write failure survives a Prepare-only rewrite", () =>
{
    GlobalSaveData global = LiveWorld();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    string first = Harness.StoredRaw(global);
    Harness.State.Purse.TryCreditTakenCoin(8);
    global.prefs.ThrowOnSet = true;
    Harness.IslandSave(global);
    Harness.False(Harness.State.Ready, "capture write failure freezes the character");
    Harness.True(Harness.HasStageFault(global.campaigns[0]), "capture fault latched on the affected campaign");
    global.prefs.ThrowOnSet = false;
    Harness.GlobalSaveOnly(global);                 // 仅 Prepare：重排旧 staged
    Harness.False(Harness.State.Ready, "Prepare-only must not clear the capture failure (P2)");
    Harness.True(Harness.HasStageFault(global.campaigns[0]), "capture fault still latched after Prepare");
    Harness.Eq(first, Harness.StoredRaw(global), "staged still the old snapshot");
    Harness.IslandSave(global);                     // 新的完整成功捕获链
    Harness.True(Harness.State.Ready, "a new successful capture clears the failure");
    Harness.False(Harness.HasStageFault(global.campaigns[0]), "capture fault cleared by the full chain");
    Harness.Eq(2, Harness.StoredRecord(global, Harness.GuidOf(global.campaigns[0])).Purse.Coins,
        "live purse staged on recovery");
});

Harness.Test("a markerless save does not clear a capture write failure", () =>
{
    GlobalSaveData global = LiveWorld();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    string first = Harness.StoredRaw(global);
    Harness.State.Purse.TryCreditTakenCoin(8);
    global.prefs.ThrowOnSet = true;
    Harness.IslandSave(global);
    global.prefs.ThrowOnSet = false;
    Harness.IslandSave(global, marker: false);      // 无 marker：不是成功捕获链
    Harness.False(Harness.State.Ready, "markerless save keeps the capture failure");
    Harness.True(Harness.HasStageFault(global.campaigns[0]), "capture fault still latched without a marker");
    Harness.Eq(first, Harness.StoredRaw(global), "no new stage without a marker");
    Harness.IslandSave(global);
    Harness.True(Harness.State.Ready, "later real capture recovers");
});

Harness.Test("a capture write failure latches only the affected campaign", () =>
{
    GlobalSaveData global = LiveWorld(2, 0);
    string guidA = Harness.GuidOf(global.campaigns[0]);
    // A：招募并 stage 1 币
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    // B：同样招募并 stage 1 币
    GlobalSaveData._loaded.currentCampaign = 1;
    CampaignSaveData.current = global.campaigns[1];
    Harness.Tick();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    string guidB = Harness.GuidOf(global.campaigns[1]);
    Harness.IslandSave(global);
    // 回到 A：live 2 币，SetString 失败
    GlobalSaveData._loaded.currentCampaign = 0;
    CampaignSaveData.current = global.campaigns[0];
    Harness.Tick();
    Harness.State.Purse.TryCreditTakenCoin(8);
    global.prefs.ThrowOnSet = true;
    Harness.IslandSave(global);
    Harness.False(Harness.State.Ready, "A frozen by its own capture write failure");
    Harness.True(Harness.HasStageFault(global.campaigns[0]), "A latch set");
    Harness.Eq(1, Harness.StoredRecord(global, guidA).Purse.Coins, "A staged unchanged");
    // B 的新捕获成功：只解除 B，不能解 A
    global.prefs.ThrowOnSet = false;
    GlobalSaveData._loaded.currentCampaign = 1;
    CampaignSaveData.current = global.campaigns[1];
    Harness.Tick();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    Harness.True(Harness.State.Ready, "B ready after its own successful capture");
    Harness.True(Harness.HasStageFault(global.campaigns[0]), "B success must not clear A's latch");
    Harness.Eq(1, Harness.StoredRecord(global, guidA).Purse.Coins, "A staged still old after B success");
    Harness.Eq(2, Harness.StoredRecord(global, guidB).Purse.Coins, "B staged its live purse");
    // 切回 A：B 成功与 Prepare 重排都不能解除 A
    GlobalSaveData._loaded.currentCampaign = 0;
    CampaignSaveData.current = global.campaigns[0];
    Harness.Tick();
    Harness.False(Harness.State.Ready, "A still frozen after B success");
    Harness.GlobalSaveOnly(global);
    Harness.False(Harness.State.Ready, "Prepare reorder must not clear A's latch");
    Harness.True(Harness.HasStageFault(global.campaigns[0]), "A latch survives the reorder");
    // 只有 A 自己的新 marker + stage + 写成功链恢复
    Harness.IslandSave(global);
    Harness.True(Harness.State.Ready, "A recovers only through its own new capture");
    Harness.False(Harness.HasStageFault(global.campaigns[0]), "A latch cleared by its own capture");
    Harness.Eq(2, Harness.StoredRecord(global, guidA).Purse.Coins, "A live purse staged on recovery");
});

Harness.Test("the real save prefix cancels pending shop transactions before the scope", () =>
{
    GlobalSaveData global = LiveWorld();
    MethodInfo prefix = typeof(CoinCourierPersistence.IslandSaveScopePatch)
        .GetMethod("Prefix", Harness.AnyStatic) ?? throw new Exception("missing save prefix");
    object[] args = { global.currentCampaign, Harness.SceneLand, global.currentChallenge, null };
    prefix.Invoke(null, args);
    Harness.Eq(1, CoinCourierShop.CancelCalls, "shop cancel invoked exactly once by the prefix");
    Harness.True(CoinCourierShop.ScopeWasNullAtCancel, "cancel runs before the scope is established");
    Harness.True(args[3] != null, "scope is established after the cancel");
    Harness.False(Harness.ScopeIsNull, "the established scope is the active one");
});

Harness.Test("a throwing shop cancel never blocks the save scope", () =>
{
    GlobalSaveData global = LiveWorld();
    CoinCourierShop.ThrowOnCancel = true;
    MethodInfo prefix = typeof(CoinCourierPersistence.IslandSaveScopePatch)
        .GetMethod("Prefix", Harness.AnyStatic) ?? throw new Exception("missing save prefix");
    object[] args = { global.currentCampaign, Harness.SceneLand, global.currentChallenge, null };
    prefix.Invoke(null, args); // 不得向原生 Save 抛出
    Harness.Eq(1, CoinCourierShop.CancelCalls, "cancel attempted");
    Harness.True(args[3] != null, "scope still established when the cancel throws");
});

Harness.Test("reordering staged records never clears the capture failure", () =>
{
    GlobalSaveData global = LiveWorld(2, 0);
    Harness.State.TryRecordRecruitment();
    Harness.IslandSave(global);
    GlobalSaveData._loaded.currentCampaign = 1;
    CampaignSaveData.current = global.campaigns[1];
    Harness.Tick();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    Harness.State.Purse.TryCreditTakenCoin(8);
    global.prefs.ThrowOnSet = true;
    Harness.IslandSave(global);
    Harness.True(Harness.HasStageFault(global.campaigns[1]), "campaign1 capture fault latched");
    global.prefs.ThrowOnSet = false;
    CampaignSaveData survivor = global.campaigns[1];
    Managers.Inst = null;
    global.TryDeleteCampaignAsync(0, null);         // 幸存者重排 + Prepare 成功写
    Harness.True(Harness.BindingFor(survivor) != null, "survivor still bound");
    Harness.True(Harness.HasStageFault(survivor), "successful reorder must not clear the capture failure (P2)");
});

Harness.Test("purse pending/fault state is independent from save faults", () =>
{
    GlobalSaveData global = LiveWorld();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.State.Purse.TryReserveOneForDelivery(71);
    var fault = new CoinCourierFault(CoinCourierFaultKind.DeliveryUnknown, CoinCourierReason.WalletWriteUnknown,
        3, 2, -1, -1, 71);
    Harness.State.Purse.MarkUnknown(fault);
    Harness.IslandSave(global);
    GlobalSaveData reloaded = Harness.Reload(global);
    Harness.Tick();
    Harness.True(Harness.State.Purse.IsFaulted, "purse fault survives the save roundtrip");
    Harness.True(Harness.State.Purse.HasPendingDelivery, "pending survives");
    reloaded.prefs.ThrowOnSet = true;
    Harness.IslandSave(reloaded);                   // 持久化写故障
    reloaded.prefs.ThrowOnSet = false;
    Harness.GlobalSaveOnly(reloaded);               // Prepare 重排
    Harness.True(Harness.State.Purse.IsFaulted, "persistence faults never clear the purse fault");
    Harness.True(Harness.State.Purse.HasPendingDelivery, "pending untouched by persistence faults");
    Harness.Eq(2, Harness.State.Purse.Coins, "coins untouched by persistence faults");
});

Harness.Test("SetString fault keeps the old string, freezes, and recovery rewrites", () =>
{
    GlobalSaveData global = LiveWorld();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    string first = Harness.StoredRaw(global);
    Harness.State.Purse.TryCreditTakenCoin(8);
    global.prefs.ThrowOnSet = true;
    Harness.IslandSave(global);
    Harness.Eq(first, Harness.StoredRaw(global), "failed stage keeps the old string");
    Harness.False(Harness.State.Ready, "write fault freezes the character");
    global.prefs.ThrowOnSet = false;
    Harness.IslandSave(global);
    Harness.True(Harness.State.Ready, "successful rewrite recovers");
    Harness.Eq(2, Harness.StoredRecord(global, Harness.GuidOf(global.campaigns[0])).Purse.Coins, "recovered string has the live purse");
});

Harness.Test("prepare failure freezes every character of that global", () =>
{
    GlobalSaveData global = LiveWorld(2, 0);
    Harness.State.TryRecordRecruitment();
    Harness.IslandSave(global);
    GlobalSaveData._loaded.currentCampaign = 1;
    CampaignSaveData.current = global.campaigns[1];
    Harness.Tick();
    Harness.State.TryRecordRecruitment();
    Harness.IslandSave(global);
    Harness.True(Harness.State.Ready, "second character ready");
    global.prefs.ThrowOnSet = true;
    Harness.GlobalSaveOnly(global);
    Harness.False(Harness.State.Ready, "prepare failure freezes the current character");
    Harness.False(Harness.BindingFor(global.campaigns[0]).Ready, "and the other character of the same key");
});

Harness.Test("a same-global re-entry never re-reads the key over live state", () =>
{
    GlobalSaveData global = LiveWorld();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    Harness.State.Purse.TryCreditTakenCoin(8);
    harnessReplaceStored(global, "{\"version\":1,\"campaigns\":[{\"slot\":0,\"guid\":\"" + Harness.GuidOf(global.campaigns[0])
        + "\",\"owned\":true,\"purse\":{\"coins\":99,\"pendingDelivery\":false,\"pendingLife\":0}}]}");
    Harness.Tick();
    Harness.Eq(2, Harness.State.Purse.Coins, "live purse survives a stale stored value on the same global");
    Harness.Contains(Harness.StoredRaw(global), "\"coins\":99", "module did not overwrite the stored value");
});

// =========================================================================
// 身份：删除 / 新战役 / 多 campaign
// =========================================================================

Harness.Test("delete compresses survivors keeping their guids and purses", () =>
{
    GlobalSaveData global = LiveWorld(3, 0);
    string[] guids = OwnAllThree(global);
    Harness.IslandSave(global);
    Managers.Inst = null; // 菜单删除：无岛捕获（不采 live）
    global.TryDeleteCampaignAsync(1, null);
    CoinCourierDocument stored = Harness.StoredDocument(global);
    Harness.Eq(2, stored.Campaigns.Count, "deleted record dropped");
    Harness.True(Harness.StoredRecord(global, guids[0]) != null, "slot0 survivor kept");
    Harness.True(Harness.StoredRecord(global, guids[1]) == null, "deleted slot1 gone");
    Harness.True(Harness.StoredRecord(global, guids[2]) != null, "slot2 survivor kept");
    Harness.Eq(0, Harness.StoredRecord(global, guids[0]).Slot, "slot0 stays 0");
    Harness.Eq(1, Harness.StoredRecord(global, guids[2]).Slot, "slot2 compressed to 1");
    Harness.Eq(1, Harness.StoredRecord(global, guids[2]).Purse.Coins, "survivor purse kept");
});

Harness.Test("delete before any tick binds from the prefix and remaps survivors", () =>
{
    // 上一世代的键：两个 owned 记录（slot0 guidA 3 币，slot1 guidB 2 币）。
    GlobalSaveData seeded = LiveWorld(2, 0);
    string guidA = Harness.GuidOf(seeded.campaigns[0]);
    string guidB = Harness.GuidOf(seeded.campaigns[1]);
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(seeded);
    GlobalSaveData._loaded.currentCampaign = 1;
    CampaignSaveData.current = seeded.campaigns[1];
    Harness.Tick();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(seeded);

    // 新会话：同一 prefs 内容 + 新实例；删除前一次 Tick 都没有（正是 GLM 反例）。
    Harness.NewWorld();
    GlobalSaveData next = Harness.Reload(seeded, current: 0);
    Managers.Inst = null;
    next.TryDeleteCampaignAsync(0, null);
    CoinCourierDocument stored = Harness.StoredDocument(next);
    Harness.Eq(1, stored.Campaigns.Count, "single survivor");
    Harness.True(Harness.StoredRecord(next, guidA) == null, "deleted first campaign gone");
    Harness.Eq(guidB, stored.Campaigns[0].Guid, "survivor guid preserved without a prior tick");
    Harness.Eq(0, stored.Campaigns[0].Slot, "survivor remapped to slot 0");
    Harness.Eq(2, stored.Campaigns[0].Purse.Coins, "survivor purse preserved");

    GlobalSaveData reloaded = Harness.Reload(next, current: 0);
    Harness.NewWorld();
    Harness.Tick();
    Harness.Eq(guidB, Harness.GuidOf(reloaded.campaigns[0]), "reload binds the survivor guid");
});

Harness.Test("an unused feature never creates the key, even through native saves", () =>
{
    GlobalSaveData global = LiveWorld();
    Harness.GlobalSaveOnly(global);
    Harness.IslandSave(global);
    Harness.Eq(null, Harness.StoredRaw(global), "no key for a never-owned campaign");
    Harness.Eq(0, global.prefs.SetCalls, "no SetString at all");
});

Harness.Test("repeated ticks are idempotent and write nothing", () =>
{
    GlobalSaveData global = LiveWorld();
    string guidBefore = Harness.GuidOf(global.campaigns[0]);
    int setCalls = global.prefs.SetCalls;
    Harness.Tick();
    Harness.Tick();
    Harness.Eq(guidBefore, Harness.GuidOf(global.campaigns[0]), "guid stable across ticks");
    Harness.Eq(setCalls, global.prefs.SetCalls, "ticks never write the key");
});

Harness.Test("a global that is not yet loaded keeps its own identity (no old-key grafting)", () =>
{
    GlobalSaveData live = LiveWorld();
    Harness.State.TryRecordRecruitment();
    ICoinCourierCampaignState liveState = Harness.State;
    // 一个尚未安装为 loaded 的新 Global：创建 campaign 不得把旧 pref 键套到新对象上。
    GlobalSaveData pending = new GlobalSaveData { currentCampaign = 0, currentChallenge = 0 };
    pending.campaigns.Add(new CampaignSaveData { CurrentLand = 1 });
    CampaignSaveData created = pending.CreateNewCampaign(0, 0);
    Harness.True(ReferenceEquals(Harness.State, liveState), "runtime still bound to the installed global");
    Harness.True(Harness.BindingFor(created) == null, "uninstalled campaign gets no binding");
    Harness.Eq(null, Harness.StoredRaw(pending), "no write against the uninstalled global");
});

Harness.Test("the delete routine patch targets MoveNext and nothing targets the factory", () =>
{
    Type patch = typeof(CoinCourierPersistence.CampaignDeleteRoutinePatch);
    HarmonyLib.HarmonyPatch attr = patch.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false)
        .Cast<HarmonyLib.HarmonyPatch>().Single();
    Harness.True(attr.TargetType == typeof(GlobalSaveData.__TryDeleteCampaign_d__91),
        "target type is the delete state machine");
    Harness.Eq(nameof(GlobalSaveData.__TryDeleteCampaign_d__91.MoveNext), attr.TargetMethod, "targets MoveNext");
    MethodInfo prefix = patch.GetMethod("Prefix", Harness.AnyStatic) ?? throw new Exception("missing prefix");
    ParameterInfo[] ps = prefix.GetParameters();
    Harness.Eq(1, ps.Length, "prefix takes the state machine instance only");
    Harness.True(ps[0].ParameterType == typeof(GlobalSaveData.__TryDeleteCampaign_d__91),
        "prefix never takes a Return<T> or other by-value machine arg");

    Type[] patches =
    {
        typeof(CoinCourierPersistence.IslandSaveScopePatch),
        typeof(CoinCourierPersistence.IslandCaptureMarkerPatch),
        typeof(CoinCourierPersistence.PrefsPreparePatch),
        typeof(CoinCourierPersistence.CampaignCreatePatch),
        typeof(CoinCourierPersistence.CampaignDeleteAsyncPatch),
        typeof(CoinCourierPersistence.CampaignDeleteRoutinePatch),
    };
    var targets = new List<string>();
    foreach (Type p in patches)
    {
        HarmonyLib.HarmonyPatch[] attrs = p.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false)
            .Cast<HarmonyLib.HarmonyPatch>().ToArray();
        Harness.Eq(1, attrs.Length, p.Name + " has exactly one patch target");
        HarmonyLib.HarmonyPatch a = attrs[0];
        Harness.False(a.TargetType == typeof(GlobalSaveData) && a.TargetMethod == nameof(GlobalSaveData._TryDeleteCampaign),
            p.Name + " must not target the delete coroutine factory");
        targets.Add(a.TargetType.FullName + "." + a.TargetMethod);
    }
    Harness.Eq(patches.Length, targets.Distinct().Count(), "patch targets are unique");
});

Harness.Test("a state-read fault in the delete prefix never reaches the native body", () =>
{
    Harness.NewWorld();
    GlobalSaveData global = Harness.NewGlobal(2);
    var machine = new GlobalSaveData.__TryDeleteCampaign_d__91
        { Owner = global, campaignIndex = 0, __1__state = 0, ThrowOnStateRead = true };
    MethodInfo prefix = typeof(CoinCourierPersistence.CampaignDeleteRoutinePatch)
        .GetMethod("Prefix", Harness.AnyStatic) ?? throw new Exception("missing prefix");
    prefix.Invoke(null, new object[] { machine }); // 不得向原生 Save/协程抛出
    Harness.True(Harness.BindingFor(global.campaigns[0]) == null, "unreadable state performs no binding");
    machine.ThrowOnStateRead = false;
    machine.MoveNext(); // native body（state0）随后照常执行
    Harness.True(Harness.BindingFor(global.campaigns[0]) != null, "native body still runs after the isolated fault");
});

Harness.Test("delete routine state 1 never binds; state 0 binds before the removal", () =>
{
    Harness.NewWorld();
    GlobalSaveData global = Harness.NewGlobal(2);
    var late = new GlobalSaveData.__TryDeleteCampaign_d__91 { Owner = global, campaignIndex = 0, __1__state = 1 };
    CoinCourierPersistence.ObserveDeleteRoutineState(late);
    Harness.True(Harness.BindingFor(global.campaigns[0]) == null, "state 1 performs no binding");
    var first = new GlobalSaveData.__TryDeleteCampaign_d__91 { Owner = global, campaignIndex = 0, __1__state = 0 };
    CoinCourierPersistence.ObserveDeleteRoutineState(first);
    Harness.True(Harness.BindingFor(global.campaigns[0]) != null, "state 0 binds before RemoveAt");
});

Harness.Test("coroutine delete entry also binds before the mutation", () =>
{
    GlobalSaveData seeded = LiveWorld(2, 0);
    string guidA = Harness.GuidOf(seeded.campaigns[0]);
    string guidB = Harness.GuidOf(seeded.campaigns[1]);
    Harness.State.TryRecordRecruitment();
    Harness.IslandSave(seeded);
    GlobalSaveData._loaded.currentCampaign = 1;
    CampaignSaveData.current = seeded.campaigns[1];
    Harness.Tick();
    Harness.State.TryRecordRecruitment();
    Harness.IslandSave(seeded);

    Harness.NewWorld();
    GlobalSaveData next = Harness.Reload(seeded, current: 0);
    Managers.Inst = null;
    System.Collections.Generic.IEnumerator<Coatsink.Common.Routine.Return<Coatsink.Common.SaveLoadResult>> routine =
        next._TryDeleteCampaign(0, default);
    routine.MoveNext(); // 协程首帧内 RemoveAt
    CoinCourierDocument stored = Harness.StoredDocument(next);
    Harness.Eq(1, stored.Campaigns.Count, "coroutine delete keeps one survivor");
    Harness.True(Harness.StoredRecord(next, guidA) == null, "deleted first campaign gone (coroutine)");
    Harness.Eq(guidB, stored.Campaigns[0].Guid, "survivor guid preserved (coroutine)");
});

Harness.Test("same-slot replacement gets a new guid and never inherits the old purse", () =>
{
    GlobalSaveData global = LiveWorld(2, 0);
    string guidA = Harness.GuidOf(global.campaigns[0]);
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    Harness.Eq(2, Harness.StoredRecord(global, guidA).Purse.Coins, "old campaign staged before replacement");
    // 同槽新战役（原生替换 campaigns[0] 引用）。
    CampaignSaveData replaced = global.CreateNewCampaign(0, 0);
    CampaignSaveData.current = replaced;
    Harness.Tick();
    Harness.IslandSave(global);
    CoinCourierDocument stored = Harness.StoredDocument(global);
    Harness.Eq(0, stored.Campaigns.Count, "replaced old campaign leaves no record; the new one is unowned");
    GlobalSaveData reloaded = Harness.Reload(global);
    Harness.Tick();
    Harness.False(Harness.State.Owned, "reload of the new campaign is unowned (no cross-slot inheritance)");
    Harness.Eq(0, Harness.State.Purse.Coins, "new campaign purse empty");
    Harness.False(Harness.GuidOf(reloaded.campaigns[0]) == guidA, "new campaign got a fresh guid");
    _ = replaced;
});

Harness.Test("expansion create appends a new empty identity next to the owned one", () =>
{
    GlobalSaveData global = LiveWorld(1, 0);
    string guidA = Harness.GuidOf(global.campaigns[0]);
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    CampaignSaveData appended = global.CreateNewCampaign(2, 0);
    Harness.Eq(3, global.campaigns.Count, "native expansion appended slots 1..2");
    Harness.IslandSave(global);
    CoinCourierDocument stored = Harness.StoredDocument(global);
    Harness.Eq(1, stored.Campaigns.Count, "only the owned campaign is persisted");
    Harness.Eq(guidA, stored.Campaigns[0].Guid, "owned guid untouched");
    Harness.Eq(0, stored.Campaigns[0].Slot, "owned slot unchanged");
    Harness.True(Harness.BindingFor(appended) != null, "appended campaign has a binding");
});

Harness.Test("two campaigns keep independent purses and identities", () =>
{
    GlobalSaveData global = LiveWorld(2, 0);
    string guidA = Harness.GuidOf(global.campaigns[0]);
    string guidB = Harness.GuidOf(global.campaigns[1]);
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    GlobalSaveData._loaded.currentCampaign = 1;
    CampaignSaveData.current = global.campaigns[1];
    Harness.Tick();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    CoinCourierDocument stored = Harness.StoredDocument(global);
    Harness.Eq(2, stored.Campaigns.Count, "two records");
    Harness.Eq(2, Harness.StoredRecord(global, guidA).Purse.Coins, "campaign A purse");
    Harness.Eq(1, Harness.StoredRecord(global, guidB).Purse.Coins, "campaign B purse");

    GlobalSaveData reloaded = Harness.Reload(global, current: 1);
    Harness.Tick();
    Harness.Eq(guidB, Harness.GuidOf(reloaded.campaigns[1]), "current guid after reload");
    Harness.Eq(1, Harness.State.Purse.Coins, "campaign B purse after reload");
    ICoinCourierCampaignState first = Harness.BindingFor(reloaded.campaigns[0]);
    Harness.Eq(2, first.Purse.Coins, "campaign A purse after reload");
    Harness.Eq(guidA, Harness.GuidOf(reloaded.campaigns[0]), "campaign A guid after reload");
});

// =========================================================================
// pending / fault 状态
// =========================================================================

Harness.Test("unknown pending delivery restores completely", () =>
{
    GlobalSaveData global = LiveWorld();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.State.Purse.TryReserveOneForDelivery(71);
    var fault = new CoinCourierFault(CoinCourierFaultKind.DeliveryUnknown, CoinCourierReason.WalletWriteUnknown,
        4, 3, 0, -1, 71);
    Harness.State.Purse.MarkUnknown(fault);
    Harness.IslandSave(global);
    GlobalSaveData reloaded = Harness.Reload(global);
    Harness.Tick();
    Harness.True(Harness.State.Purse.HasPendingDelivery, "pending restored");
    Harness.Eq(71, Harness.State.Purse.PendingDeliveryLife, "pending life restored");
    Harness.Eq(2, Harness.State.Purse.Coins, "purse coins restored");
    Harness.True(Harness.State.Purse.IsFaulted, "fault restored");
    Harness.Eq((int)CoinCourierFaultKind.DeliveryUnknown, (int)Harness.State.Purse.Fault.Kind, "fault kind restored");
    Harness.Eq((int)CoinCourierReason.WalletWriteUnknown, (int)Harness.State.Purse.Fault.Reason, "fault reason restored");
    Harness.Eq(-1, Harness.State.Purse.Fault.WalletAfter, "fault wallet-after restored");
});

Harness.Test("persisted pending without a fault becomes an explicit delivery fault", () =>
{
    Harness.NewWorld();
    GlobalSaveData global = Harness.NewGlobal(1);
    global.prefs.contents[CoinCourierSaveSchema.Key] =
        "{\"version\":1,\"campaigns\":[{\"slot\":0,\"guid\":\"0f6f6a54-8df1-4c37-9a4a-5cbb0f2a1b2c\",\"owned\":true,"
        + "\"purse\":{\"coins\":2,\"pendingDelivery\":true,\"pendingLife\":71}}]}";
    Harness.Tick();
    Harness.True(Harness.State.Purse.HasPendingDelivery, "pending kept");
    Harness.True(Harness.State.Purse.IsFaulted, "silent pending surfaces as a fault");
    Harness.Eq((int)CoinCourierFaultKind.DeliveryUnknown, (int)Harness.State.Purse.Fault.Kind, "delivery-unknown kind");
    Harness.Eq(2, Harness.State.Purse.Coins, "coins intact");
    _ = global;
});

// =========================================================================
// 接线入口 / 关闭保持
// =========================================================================

Harness.Test("ApplyToScene binds the runtime to the confirmed campaign", () =>
{
    Harness.NewWorld();
    GlobalSaveData global = Harness.NewGlobal(2, 0);
    global.campaigns[0].ApplyToScene();
    Harness.True(Harness.State != null, "bound through ApplyToScene");
    Harness.True(Harness.State.Ready, "ready right after ApplyToScene");
    GlobalSaveData._loaded.currentCampaign = 1;
    CampaignSaveData.current = global.campaigns[1];
    global.campaigns[1].ApplyToScene();
    Harness.True(ReferenceEquals(Harness.State, Harness.BindingFor(global.campaigns[1])), "runtime follows the applied campaign");
});

Harness.Test("feature-off save cycles preserve the existing mapping", () =>
{
    GlobalSaveData global = LiveWorld();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    string guid = Harness.GuidOf(global.campaigns[0]);
    string before = Harness.StoredRaw(global);
    // “关闭功能”= 不执行任何经济/招募，只跑原生保存与 Prepare 维护。
    Harness.GlobalSaveOnly(global);
    Harness.IslandSave(global);
    Harness.Eq(before, Harness.StoredRaw(global), "no blanking and no drift while the feature is off");
    Harness.True(Harness.StoredRecord(global, guid) != null, "mapping kept");
});

Harness.Test("old staged snapshot wins after a reload even when the live purse was richer", () =>
{
    GlobalSaveData global = LiveWorld();
    Harness.State.TryRecordRecruitment();
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.State.Purse.TryCreditTakenCoin(8);
    GlobalSaveData reloaded = Harness.Reload(global);
    Harness.Tick();
    Harness.Eq(1, Harness.State.Purse.Coins, "reload restores staged, not the post-save live changes");
});

static void harnessReplaceStored(GlobalSaveData global, string raw)
{
    global.prefs.contents[CoinCourierSaveSchema.Key] = raw;
}

static string[] OwnAllThree(GlobalSaveData global)
{
    string[] guids = new string[3];
    for (int i = 0; i < 3; i++)
    {
        GlobalSaveData._loaded.currentCampaign = i;
        CampaignSaveData.current = global.campaigns[i];
        Harness.Tick();
        Harness.State.TryRecordRecruitment();
        Harness.State.Purse.TryCreditTakenCoin(8);
        guids[i] = Harness.GuidOf(global.campaigns[i]);
        Harness.IslandSave(global);
    }
    GlobalSaveData._loaded.currentCampaign = 0;
    CampaignSaveData.current = global.campaigns[0];
    return guids;
}

// =========================================================================
// 单一详细就绪投影 + 身份即时核对（状态文案的唯一来源）
// =========================================================================

Harness.Test("availability classifies the live gates without throwing", () =>
{
    GlobalSaveData global = LiveWorld();
    ICoinCourierCampaignState state = Harness.State;
    Harness.Eq((int)CoinCourierAvailability.Ready, (int)state.Availability.Kind, "healthy global is Ready");

    Managers managers = Managers.Inst;
    managers.game.state = Game.State.Menu;
    Harness.Eq((int)CoinCourierAvailability.Menu, (int)state.Availability.Kind, "native menu is Menu");
    Harness.False(state.Ready, "Menu is not tradable");
    managers.game.state = Game.State.Playing;

    UnityEngine.Time.timeScale = 0f;
    Harness.Eq((int)CoinCourierAvailability.Paused, (int)state.Availability.Kind, "playing with zero timescale is Paused");
    UnityEngine.Time.timeScale = 1f;

    managers.game.state = Game.State.Intro;
    Harness.Eq((int)CoinCourierAvailability.Loading, (int)state.Availability.Kind, "intro is Loading, never pause");
    managers.game.state = Game.State.Playing;

    IslandSaveData.isSavingGame = true;
    Harness.Eq((int)CoinCourierAvailability.Saving, (int)state.Availability.Kind, "island-saving is Saving");
    IslandSaveData.isSavingGame = false;

    Game.SavingEnabled = false;
    Harness.Eq((int)CoinCourierAvailability.Saving, (int)state.Availability.Kind, "saving-disabled is Saving");
    Game.SavingEnabled = true;

    var busyPrompt = new Coatsink.Common.Haglet { state = Coatsink.Common.Haglet.State.Started };
    Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase idlePrompt = managers.game.saveGameWithFailurePrompt;
    managers.game.saveGameWithFailurePrompt = busyPrompt;
    Harness.Eq((int)CoinCourierAvailability.Saving, (int)state.Availability.Kind, "a busy save prompt is Saving");
    managers.game.saveGameWithFailurePrompt = idlePrompt;

    Harness.Eq((int)CoinCourierAvailability.Ready, (int)state.Availability.Kind, "all gates restored");

    NetworkBigBoss.IsOnline = true;
    Harness.Eq((int)CoinCourierAvailability.Online, (int)state.Availability.Kind, "online");
    NetworkBigBoss.IsOnline = false;
    NetworkBigBoss.HasWorldAuth = false;
    Harness.Eq((int)CoinCourierAvailability.NoAuthority, (int)state.Availability.Kind, "no authority");
    NetworkBigBoss.HasWorldAuth = true;

    Managers live = Managers.Inst;
    Managers.Inst = null;
    Harness.Eq((int)CoinCourierAvailability.WorldUnavailable, (int)state.Availability.Kind, "missing world managers");
    Managers.Inst = live;
    Harness.Eq((int)CoinCourierAvailability.Ready, (int)state.Availability.Kind, "recovered");
    _ = global;
});

Harness.Test("availability is identity-checked before any stored fault or balance is exposed", () =>
{
    GlobalSaveData global = LiveWorld();
    ICoinCourierCampaignState state = Harness.State;
    Harness.True(state.TryRecordRecruitment(), "recruited");
    Harness.State.Purse.TryCreditTakenCoin(8);
    Harness.IslandSave(global);

    global.prefs.ThrowOnSet = true;
    Harness.IslandSave(global);
    Harness.Eq((int)CoinCourierAvailability.PersistenceFault, (int)state.Availability.Kind,
        "the current binding exposes its real capture fault");
    UnityEngine.Time.timeScale = 0f;
    Harness.Eq((int)CoinCourierAvailability.PersistenceFault, (int)state.Availability.Kind,
        "a real fault outranks a pause");
    UnityEngine.Time.timeScale = 1f;

    // 换到另一份 native global 但不 Tick：旧绑定立即 NotBound，旧故障/余额绝不可见。
    var fresh = new GlobalSaveData { currentCampaign = 0, currentChallenge = 0 };
    fresh.campaigns.Add(new CampaignSaveData { CurrentLand = 1 });
    GlobalSaveData._loaded = fresh;
    CampaignSaveData.current = fresh.campaigns[0];
    Harness.Eq((int)CoinCourierAvailability.NotBound, (int)state.Availability.Kind,
        "a stale binding is NotBound at read time");
    Harness.False(state.Ready, "a stale binding is never tradable");

    Harness.Tick();
    Harness.True(Harness.State != null && !ReferenceEquals(Harness.State, state), "Tick binds the new global's owner");
    Harness.Eq((int)CoinCourierAvailability.Ready, (int)Harness.State.Availability.Kind, "the new global is clean");
});

Harness.Test("challenge mode is Unsupported and a foreign campaign is NotBound", () =>
{
    GlobalSaveData global = LiveWorld(2, 0);
    ICoinCourierCampaignState first = Harness.State;
    GlobalSaveData._loaded.currentChallenge = 3;
    Harness.Eq((int)CoinCourierAvailability.Unsupported, (int)first.Availability.Kind, "challenge is unsupported");
    Harness.False(first.Ready, "challenge is not tradable");
    GlobalSaveData._loaded.currentChallenge = 0;
    Harness.Eq((int)CoinCourierAvailability.Ready, (int)first.Availability.Kind, "challenge cleared");

    ICoinCourierCampaignState second = Harness.BindingFor(global.campaigns[1]);
    Harness.True(second != null, "the second campaign is bound too");
    Harness.Eq((int)CoinCourierAvailability.NotBound, (int)second.Availability.Kind,
        "the non-current campaign identity is NotBound");
});

Harness.Test("closed-global diagnostics are read-time identity-checked", () =>
{
    Harness.NewWorld();
    GlobalSaveData global = Harness.NewGlobal(1);
    global.prefs.contents[CoinCourierSaveSchema.Key] = "{broken";
    Harness.Tick();
    Harness.True(Harness.IsClosed, "corrupt key closes the module");
    Harness.True(CoinCourierPersistence.TryGetCurrentClosedDiagnostic(out CoinCourierAvailabilityInfo info),
        "the closed reason is readable while the global is current");
    Harness.Eq((int)CoinCourierAvailability.Closed, (int)info.Kind, "closed kind");
    Harness.Contains(info.Detail, "corrupt", "the reason names the record fault");

    var fresh = new GlobalSaveData { currentCampaign = 0, currentChallenge = 0 };
    fresh.campaigns.Add(new CampaignSaveData { CurrentLand = 1 });
    GlobalSaveData._loaded = fresh;
    CampaignSaveData.current = fresh.campaigns[0];
    Harness.False(CoinCourierPersistence.TryGetCurrentClosedDiagnostic(out _),
        "a different native global is never reported as the closed owner");

    Harness.Tick();
    Harness.True(Harness.State != null, "the fresh global binds");
    Harness.False(Harness.IsClosed, "the fresh global is not closed");
    _ = global;
});

return Harness.Finish();
