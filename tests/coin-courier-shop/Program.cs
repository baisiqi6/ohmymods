// 金币哥布林商店门套件：链接完整生产 CoinCourierShop（Unity/Payable 装配路径）与生产
// CoinCourierRuntime，复用 runtime-bridge 的边界替身。核心回归：招募/选址/创建不以
// 银行家 AI、kingdom.banker 引用或 903 登记为前提；暂停/保存仍按原样阻断付款。
//
// 运行：C:/Users/ADMIN/dotnet8/dotnet.exe run -c Release --project tests/coin-courier-shop
using System;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

int checks = 0;
void Verify(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}

Type Shop = typeof(CoinCourierShop);
const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

object GetStatic(string name)
{
    FieldInfo field = Shop.GetField(name, StaticFlags) ?? throw new Exception("missing static " + name);
    return field.GetValue(null);
}

void SetStatic(string name, object value)
{
    FieldInfo field = Shop.GetField(name, StaticFlags) ?? throw new Exception("missing static " + name);
    field.SetValue(null, value);
}

void ResetAll()
{
    SimPhysics.Reset();
    Managers.Inst = null;
    NetworkPostbox.Instance = null;
    CoinCourierBankScope.Reset();
    CoinCourierPersistence.Reset();
    CoinCourierVisuals.Reset();
    IslandSaveData.isSavingGame = false;
    NetworkBigBoss.HasWorldAuth = true;
    NetworkBigBoss.IsOnline = false;
    Time.time = 0f;
    Time.unscaledTime = 0f;
    Time.deltaTime = 0f;
    Time.timeScale = 1f;
    ModConfig.Enabled.Value = true;
    ModConfig.CoinCourierEnabled.Value = true;
    KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
    SetStatic("_object", null);
    SetStatic("_payable", null);
    SetStatic("_owner", null);
    SetStatic("_kingdom", null);
    SetStatic("_layer", null);
    SetStatic("_postbox", null);
    SetStatic("_header", null);
    SetStatic("_clearing", false);
    SetStatic("_registered", false);
    SetStatic("_retiring", false);
    SetStatic("_retryAt", 0f);
    SetStatic("_retireAt", 0f);
    SetStatic("_loggedFailure", false);
    SetStatic("_createStage", "idle");
    SetStatic("_status", "尚未创建");
    SetStatic("_attemptKingdom", IntPtr.Zero);
    SetStatic("_attemptLayer", IntPtr.Zero);
    SetStatic("_attemptState", null);
}

(Managers Managers, ShopGateState State, GameObject Layer) BuildWorld()
{
    ResetAll();
    var layer = new GameObject("GameLayer");
    var ground = new GameObject("Ground") { layer = 8 };
    var groundCollider = new BoxCollider2D
    {
        gameObject = ground,
        bounds = new Bounds(new Vector3(-1000f, -10f, 0f), new Vector3(1000f, SimPhysics.GroundTop, 0f)),
    };
    World.GroundCollider = groundCollider;
    SimPhysics.Ground = groundCollider;

    var state = new ShopGateState();
    var managers = new Managers
    {
        game = new Game { state = Game.State.Playing },
        world = new World { gameLayer = layer.transform },
        kingdom = new Kingdom { campfirePosition = 0f, HasBorderLoaded = true },
        payables = new PayableManager(),
    };
    Managers.Inst = managers;
    NetworkPostbox.Instance = new NetworkPostbox();   // 默认注册就绪；缺注册用例显式置空
    CoinCourierRuntime.Bind(state);
    return (managers, state, layer);
}

(Managers Managers, GameObject ShopObject, ShopGateState State) BuildShop()
{
    var (managers, state, layer) = BuildWorld();
    var shopObject = new GameObject("KEM_CoinCourierShop");
    shopObject.transform.SetParent(layer.transform, false);
    var owner = shopObject.AddComponent<CoinCourierShopOwner>();
    var payable = shopObject.AddComponent<PayableComponent>();
    var postbox = new NetworkPostbox();
    NetworkPostbox.Instance = postbox;
    CRPCHeader header = postbox.RegisterObject(shopObject, CRPCType.SemiStatic);

    SetStatic("_object", shopObject);
    SetStatic("_payable", payable);
    SetStatic("_owner", owner);
    SetStatic("_kingdom", managers.kingdom);
    SetStatic("_layer", layer.transform);
    SetStatic("_postbox", postbox);
    SetStatic("_header", header);
    return (managers, shopObject, state);
}

int Count(string text, string needle)
{
    int found = 0;
    for (int i = 0; (i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0; i += needle.Length) found++;
    return found;
}

int WaitLogs()
{
    int found = 0;
    foreach (string message in KingdomEnhancedPlugin.Instance.LogSource.Messages)
        if (message.Contains("create wait")) found++;
    return found;
}

string LastWaitLog()
{
    string found = null;
    foreach (string message in KingdomEnhancedPlugin.Instance.LogSource.Messages)
        if (message.Contains("create wait")) found = message;
    return found;
}

bool LogsContain(string needle)
{
    foreach (string message in KingdomEnhancedPlugin.Instance.LogSource.Messages)
        if (message.Contains(needle)) return true;
    return false;
}

Console.WriteLine("[shop-1] recruitment serves with no banker reference and no 903 registration");
{
    var fixture = BuildShop();
    Verify(fixture.Managers.kingdom.banker == null, "the native banker reference is null");
    Verify(!NetworkPostbox.Instance.DynamicObjects.ContainsKey(903), "no 903 bank registration exists");

    var playerGo = new GameObject("Player");
    var player = playerGo.AddComponent<Player>();
    player.wallet = playerGo.AddComponent<Wallet>();
    fixture.Managers.kingdom.playerOne = player;

    Verify(CoinCourierShop.CanPurchase(), "the shop serves purchases with no banker identity at all");
    Verify(CoinCourierShop.CanPlayerPurchase(player), "the player purchase gate serves as well");
}

Console.WriteLine("[shop-2] pause, saving and lost context still block payment");
{
    var fixture = BuildShop();
    Verify(CoinCourierShop.CanPurchase(), "baseline serves");

    fixture.State.Availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Menu);
    Verify(!CoinCourierShop.CanPurchase(), "a menu-paused campaign cannot purchase");
    fixture.State.Availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Ready);

    fixture.Managers.game.state = Game.State.Menu;
    Verify(!CoinCourierShop.CanPurchase(), "the native menu keeps the shop but blocks payment");
    fixture.Managers.game.state = Game.State.Playing;

    fixture.State.Availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Paused);
    Verify(!CoinCourierShop.CanPurchase(), "playing with zero timescale cannot purchase");
    fixture.State.Availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Ready);

    IslandSaveData.isSavingGame = true;
    Verify(!CoinCourierShop.CanPurchase(), "a save in progress cannot purchase");
    IslandSaveData.isSavingGame = false;

    NetworkBigBoss.HasWorldAuth = false;
    Verify(!CoinCourierShop.CanPurchase(), "a client without world authority cannot purchase");
    NetworkBigBoss.HasWorldAuth = true;

    Verify(CoinCourierShop.CanPurchase(), "restored context serves again");
}

Console.WriteLine("[shop-3] the fixed campfire home creates despite buildings and payment points");
{
    // 用户 2026-09-28 固定主城锚合同（取代旧的窗口/半宽/建筑排除/付款间隔选址）：这里的断言
    // 不是删除保护，而是改为"固定点照常生成 / 缺地面只等待"。
    var fixture = BuildWorld();
    fixture.Managers.payables.AllPayables = new[]
    {
        new Payable { playerPayDistance = 1f, PayPointX = -3f },   // 原生付款点正压固定锚
    };
    SimPhysics.AddObstacle(-3.9f, -2.1f);                          // 建筑/装饰覆盖固定点
    SimPhysics.AddObstacle(-5f, -1f, 8f, 10f);                     // 高处装饰
    SimPhysics.AddObstacle(-4.0f, -3.5f, 0f, 6f, isTrigger: true); // 无关触发体
    CoinCourierShop.Tick();
    object shop = GetStatic("_object");
    Verify(shop != null, "a covered home still creates the shop");
    Verify((string)GetStatic("_createStage") == "ready", "creation reaches ready: " + GetStatic("_createStage"));
    Verify(((GameObject)shop).transform.position.x == -3f,
        "the shop sits exactly at campfire-3: " + ((GameObject)shop).transform.position.x);
    Verify(((GameObject)shop).transform.position.y == SimPhysics.GroundTop,
        "the shop y is the single ground probe at that point");
    Verify((float)GetStatic("_anchorX") == -3f && (float)GetStatic("_anchorY") == SimPhysics.GroundTop,
        "the marker anchor is the same home");
    Verify(CoinCourierVisuals.Created == 1, "the marker view is created exactly once");
    Verify(((PayableComponent)GetStatic("_payable")).payablePlacementExclusionDistance == 0f,
        "the NPC reserves no placement distance for other buildings");
    Verify(((PayableComponent)GetStatic("_payable")).playerPayDistance == 1f,
        "the native payment distance stays 1");

    // 复用：同一固定锚不会重复建第二家。
    int before = CoinCourierVisuals.Created;
    CoinCourierShop.Tick();
    Verify(CoinCourierVisuals.Created == before && ReferenceEquals(GetStatic("_object"), shop),
        "a resident shop is never created twice");

    // 单点地面不可读：等待并如实记入创建历史，不编造高度、不重选点；恢复后仍是同一固定锚。
    var noGround = BuildWorld();
    SimPhysics.Enabled = false;
    CoinCourierShop.Tick();
    Verify(GetStatic("_object") == null, "no readable ground means no shop");
    Verify((string)GetStatic("_status") == "等待营火左侧地面",
        "the creation history names the ground wait: " + GetStatic("_status"));
    Verify(CoinCourierShop.StatusText.Contains("上次创建：等待营火左侧地面"),
        "the panel carries the ground wait as history: " + CoinCourierShop.StatusText);
    Verify(WaitLogs() == 1, "the wait is logged exactly once: " + WaitLogs());
    Verify(LastWaitLog().Contains("stage=home") && LastWaitLog().Contains("reason=home-ground")
        && LastWaitLog().Contains("home=campfire=0.00 home=-3.00"),
        "the bounded log names stage/reason and the fixed home: " + LastWaitLog());
    Verify((float)GetStatic("_retryAt") > 0f, "the retry cadence is armed");
    SimPhysics.Enabled = true;
    Time.unscaledTime = 5f;
    CoinCourierShop.Tick();
    Verify(GetStatic("_object") != null, "restored ground creates at the same fixed home");
    Verify(((GameObject)GetStatic("_object")).transform.position.x == -3f,
        "the recovered shop uses the same home");
    Verify(WaitLogs() == 1, "a recovered ground wait does not add log noise: " + WaitLogs());

    // 网络注册未就绪：停在注册阶段并记为历史。
    var noPostbox = BuildWorld();
    NetworkPostbox.Instance = null;
    CoinCourierShop.Tick();
    Verify((string)GetStatic("_createStage") == "register-owner",
        "creation reaches network registration: " + GetStatic("_createStage"));
    Verify((string)GetStatic("_status") == "等待网络注册就绪",
        "the registration wait is recorded: " + GetStatic("_status"));

    // 招募门未开（未接线）：不记录创建结果，历史停在"尚未创建"。
    var gated = BuildWorld();
    CoinCourierRuntime.Bind(null);
    CoinCourierShop.Tick();
    Verify(GetStatic("_object") == null && (string)GetStatic("_status") == "尚未创建",
        "a closed recruitment gate records no creation result: " + GetStatic("_status"));

    // 共用锚：招募标记与运行时（已招哥布林出生/回家）用同一个 TryResolveHome。
    var shared = BuildWorld();
    Verify(CoinCourierGround.TryResolveHome(shared.Managers.kingdom, out float sharedX, out float sharedY)
        && sharedX == -3f && sharedY == SimPhysics.GroundTop,
        "the shared home is campfire+HomeOffsetX with the real ground probe");
}

Console.WriteLine("[shop-4] panel status lines stay inside the local two-line budget");
{
    BuildShop();
    string character = CoinCourierRuntime.StatusText;
    string recruit = "招募点：" + CoinCourierShop.StatusText;
    string treasury = CoinCourierRuntime.TreasuryStatusText;
    Verify(character.Length <= 40, "the character line fits one row: " + character);
    Verify(recruit.Length <= 40, "the recruitment line fits one row: " + recruit);
    Verify(treasury.Length <= 40, "the treasury line fits one row: " + treasury);
    Verify(!recruit.Contains("招募点：招募点"), "the row never doubles the panel prefix: " + recruit);
}

Console.WriteLine("[shop-5] the panel keeps the live Runtime status and the creation history apart");
{
    // 从未创建：正文是 Runtime 投影，历史给出中性"尚未创建"。
    var fixture = BuildWorld();
    Verify(CoinCourierShop.StatusText == CoinCourierRuntime.StatusText + " · 上次创建：尚未创建",
        "an untouched world shows the live status plus the neutral note: " + CoinCourierShop.StatusText);

    // 地面失败 → 历史记下具体原因（正文仍是 Runtime 投影）。
    SimPhysics.Enabled = false;
    CoinCourierShop.Tick();
    Verify(CoinCourierShop.StatusText.Contains("上次创建：等待营火左侧地面"),
        "the real failure is kept in the history: " + CoinCourierShop.StatusText);

    // Playing + 时标 0（面板兜底暂停）：不创建、不消耗重试。
    Time.unscaledTime = 6f;   // 重试已到期：若误入 Create，_retryAt 会被改写
    Time.timeScale = 0f;
    fixture.State.Availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Paused);
    CoinCourierShop.Tick();
    Verify(GetStatic("_object") == null && (float)GetStatic("_retryAt") == 5f,
        "a zero-timescale playing frame neither creates nor consumes the retry");
    Time.timeScale = 1f;
    fixture.State.Availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Ready);

    // 保存中：同样不创建、不消耗重试。
    IslandSaveData.isSavingGame = true;
    Time.unscaledTime = 7f;
    CoinCourierShop.Tick();
    Verify(GetStatic("_object") == null && (float)GetStatic("_retryAt") == 5f && WaitLogs() == 1,
        "a save in progress neither creates nor consumes the retry");
    IslandSaveData.isSavingGame = false;

    // Menu 暂停：正文说暂停、历史不丢；连续读取稳定、历史附注只出现一次、绝不写回 _status。
    fixture.Managers.game.state = Game.State.Menu;
    Time.timeScale = 0f;
    fixture.State.Availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Menu);
    string paused = null;
    for (int i = 0; i < 5; i++)
    {
        CoinCourierShop.Tick();
        string now = CoinCourierShop.StatusText;
        paused = paused == null ? now : paused;
        Verify(now == paused, "the paused projection is stable: " + now);
    }
    Verify(paused.Contains("游戏已暂停"), "the menu pause is reported as paused: " + paused);
    Verify(paused.Contains("上次创建：等待营火左侧地面"), "the pause keeps the real history: " + paused);
    Verify(Count(paused, "上次创建：") == 1, "the history note is never repeated: " + paused);
    Verify(!paused.Contains("等待可用的单机领地"), "the old generic land text is gone: " + paused);
    Verify((string)GetStatic("_status") == "等待营火左侧地面", "the projection never writes back into _status");

    // 保存中：正文改说保存，历史仍在。
    fixture.Managers.game.state = Game.State.Playing;
    Time.timeScale = 1f;
    IslandSaveData.isSavingGame = true;
    fixture.State.Availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Saving);
    CoinCourierShop.Tick();
    Verify(CoinCourierShop.StatusText.Contains("保存中")
        && CoinCourierShop.StatusText.Contains("上次创建：等待营火左侧地面"),
        "a save gate is named and the history survives: " + CoinCourierShop.StatusText);
    IslandSaveData.isSavingGame = false;
    fixture.State.Availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Ready);

    // 重试未到：不重试、不重复日志、历史不变。
    int logs = WaitLogs();
    Time.unscaledTime = 1f;
    CoinCourierShop.Tick();
    Verify(GetStatic("_object") == null && WaitLogs() == logs
        && (string)GetStatic("_status") == "等待营火左侧地面",
        "the retry wait preserves the history and the log budget");

    // unknown/锁定优先：正文先回答未知，历史只作附注。
    CoinCourierRuntime.LatchRecruitment("test-unknown");
    string locked = CoinCourierShop.StatusText;
    Verify(locked.Contains("已锁定") && locked.Contains("上次创建：等待营火左侧地面"),
        "an unknown/locked state leads and the history stays an annotation: " + locked);
    CoinCourierRuntime.Bind(new ShopGateState());
    CoinCourierRuntime.Bind(fixture.State);   // 回到本夹具身份（新接线解除锁定）

    // 关闭/失权：正文即时变化，绝不写进创建历史。
    ModConfig.CoinCourierEnabled.Value = false;
    string closed = CoinCourierShop.StatusText;
    Verify(closed.StartsWith("已关闭", StringComparison.Ordinal)
        && closed.Contains("上次创建：等待营火左侧地面") && !closed.Contains("上次创建：已关闭"),
        "a closed feature never becomes the creation history: " + closed);
    ModConfig.CoinCourierEnabled.Value = true;
    NetworkBigBoss.HasWorldAuth = false;
    fixture.State.Availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.NoAuthority);
    CoinCourierShop.Tick();
    Verify(CoinCourierShop.StatusText.Contains("无主机权限")
        && CoinCourierShop.StatusText.Contains("上次创建：等待营火左侧地面"),
        "losing authority is named live and never pollutes the history: " + CoinCourierShop.StatusText);
    NetworkBigBoss.HasWorldAuth = true;
    fixture.State.Availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Ready);

    // 恢复 Playing（重试到期）：新结果替换历史；有店时只显示实时 Runtime 状态。
    fixture.Managers.game.state = Game.State.Playing;
    Time.timeScale = 1f;
    Time.unscaledTime = 10f;
    SimPhysics.Enabled = true;
    CoinCourierShop.Tick();
    Verify(GetStatic("_object") != null && (string)GetStatic("_status") == "已创建",
        "the restored world creates and records the success");
    Verify(CoinCourierShop.StatusText == CoinCourierRuntime.StatusText,
        "a resident shop shows only the live status: " + CoinCourierShop.StatusText);
}

Console.WriteLine("[shop-6] creation history needs its full source identity");
{
    var fixture = BuildWorld();
    SimPhysics.Enabled = false;
    CoinCourierShop.Tick();
    Verify(CoinCourierShop.StatusText.Contains("上次创建：等待营火左侧地面"), "the real history is visible first");

    // 同一 scene 换 CampaignState（新 campaign 引用）：真实历史隐藏，只留中性态。
    CoinCourierRuntime.Bind(new ShopGateState());
    Verify(!CoinCourierShop.StatusText.Contains("等待营火左侧地面")
        && CoinCourierShop.StatusText.Contains("上次创建：尚未创建"),
        "a new campaign identity hides the old history: " + CoinCourierShop.StatusText);

    // 回到原接线：历史恢复。
    CoinCourierRuntime.Bind(fixture.State);
    Verify(CoinCourierShop.StatusText.Contains("上次创建：等待营火左侧地面"),
        "the original identity shows its own history again");

    // 换 world：来源不匹配 → 只给中性态，绝不外溢旧原因；换回原 world 后再现。
    var worldA = fixture.Managers.world;
    var kingdomA = fixture.Managers.kingdom;
    var layerB = new GameObject("GameLayerB");
    fixture.Managers.world = new World { gameLayer = layerB.transform };
    fixture.Managers.kingdom = new Kingdom { campfirePosition = 0f, HasBorderLoaded = true };
    fixture.Managers.game.state = Game.State.Menu;
    Time.timeScale = 0f;
    CoinCourierShop.Tick();
    Verify(!CoinCourierShop.StatusText.Contains("等待营火左侧地面")
        && CoinCourierShop.StatusText.Contains("上次创建：尚未创建"),
        "another world never inherits the old history: " + CoinCourierShop.StatusText);
    fixture.Managers.world = worldA;
    fixture.Managers.kingdom = kingdomA;
    Verify(CoinCourierShop.StatusText.Contains("上次创建：等待营火左侧地面"),
        "the original world shows its own history again: " + CoinCourierShop.StatusText);

    // 恢复 Playing：重试到期 → 新结果。
    fixture.Managers.game.state = Game.State.Playing;
    Time.timeScale = 1f;
    Time.unscaledTime = 10f;
    SimPhysics.Enabled = true;
    CoinCourierShop.Tick();
    Verify(GetStatic("_object") != null && (string)GetStatic("_status") == "已创建",
        "the restored world retries and creates at the fixed home");

    // 有店后换 world：Clear 清掉来源与历史；即使旧对象身份被复用也不复活旧文本。
    var residentWorld = fixture.Managers.world;
    var residentKingdom = fixture.Managers.kingdom;
    var otherLayer = new GameObject("GameLayerC");
    fixture.Managers.world = new World { gameLayer = otherLayer.transform };
    fixture.Managers.kingdom = new Kingdom { campfirePosition = 0f, HasBorderLoaded = true };
    CoinCourierShop.Tick();
    Verify(GetStatic("_object") == null, "the world change clears the resident shop");
    Verify((string)GetStatic("_status") == "尚未创建" && GetStatic("_attemptState") == null
        && (IntPtr)GetStatic("_attemptKingdom") == IntPtr.Zero,
        "a completed clear drops every history source");
    fixture.Managers.world = residentWorld;
    fixture.Managers.kingdom = residentKingdom;
    fixture.Managers.game.state = Game.State.Menu;
    Time.timeScale = 0f;
    CoinCourierShop.Tick();
    Verify(CoinCourierShop.StatusText.Contains("上次创建：尚未创建")
        && !CoinCourierShop.StatusText.Contains("已创建"),
        "a cleared history restarts at the neutral note: " + CoinCourierShop.StatusText);

    // 异常路径：Clear 清零来源后重标失败（上下文读取异常）——旧失败 raw 不得外溢到任何
    // 新 campaign/world，面板只回落到中性态；日志照旧保留该故障。
    var broken = BuildWorld();
    broken.Managers.kingdom.ThrowOnBorderRead = true;
    CoinCourierShop.Tick();
    Verify((string)GetStatic("_status") == "招募点创建失败（idle）",
        "the exception is recorded in the raw history: " + GetStatic("_status"));
    Verify(GetStatic("_attemptState") == null && (IntPtr)GetStatic("_attemptKingdom") == IntPtr.Zero,
        "the failed re-mark leaves no history source");
    Verify(!CoinCourierShop.StatusText.Contains("招募点创建失败"),
        "an unsourced raw failure never reaches the panel: " + CoinCourierShop.StatusText);
    Verify(CoinCourierShop.StatusText.Contains("上次创建：尚未创建"),
        "the panel falls back to the neutral note: " + CoinCourierShop.StatusText);
    Verify(LogsContain("unavailable stage="), "the unsourced failure is still logged");

    CoinCourierRuntime.Bind(new ShopGateState());
    broken.Managers.kingdom.ThrowOnBorderRead = false;
    var freshLayer = new GameObject("GameLayerX");
    broken.Managers.world = new World { gameLayer = freshLayer.transform };
    broken.Managers.kingdom = new Kingdom { campfirePosition = 0f, HasBorderLoaded = true };
    Verify(!CoinCourierShop.StatusText.Contains("招募点创建失败")
        && CoinCourierShop.StatusText.Contains("上次创建：尚未创建"),
        "a new campaign/world never inherits the unsourced failure: " + CoinCourierShop.StatusText);
}

Console.WriteLine("ALL PASS — " + checks + " checks");
return 0;

internal sealed class ShopGateState : ICoinCourierCampaignState
{
    public CoinCourierAvailabilityInfo Availability { get; set; } =
        CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Ready);

    public bool Owned { get; set; }
    public CoinCourierPurse Purse { get; set; } = new CoinCourierPurse();
    public bool Ready => Availability.Kind == CoinCourierAvailability.Ready;
    public bool TryRecordRecruitment() => true;
}
