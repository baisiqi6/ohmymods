using System;
using BepInEx.Configuration;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;

namespace KingdomEnhancedMod;

/// <summary>F5 / Ctrl+F10 settings panel. Uses only a private copy of Unity's loaded font/skin.</summary>
public class ModPanel : MonoBehaviour
{
    private static bool _shown;
    internal static bool IsShown => _shown;
    private static GUISkin _skin;
    private static GUIStyle _title, _label, _muted, _value, _tab, _activeTab, _button, _card;
    private static Texture2D _back, _cardBack, _gold, _track, _thumb;
    private static Vector2 _scroll;
    private static float _measuredContentHeight; // DrawControls 上一帧实际累计高度（一帧收敛）
    private static int _category;
    private static readonly string[] Categories = { "王国", "人口", "世界", "战斗", "自动补货", "便捷", "弓箭", "骑士", "MOD角色" };
    private static readonly Color Gold = new Color(0.91f, 0.75f, 0.43f);
    private static readonly Color Text = new Color(0.94f, 0.94f, 0.91f);
    private static readonly Color Muted = new Color(0.65f, 0.71f, 0.77f);
    private const float CardHeight = 122f;

    public ModPanel(IntPtr ptr) : base(ptr) { }

    public static void EnsureCreated()
    {
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(ModPanel)))
            ClassInjector.RegisterTypeInIl2Cpp(typeof(ModPanel));
        var go = new GameObject("KingdomEnhancedMod_Panel");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<ModPanel>();
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Panel] ModPanel created (Ctrl+F10 / F5 to toggle)");
    }

    private void Update()
    {
        // Shortcuts first so a HUD failure can never swallow F5/Ctrl+F10/Esc handling.
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if ((ctrl && Input.GetKeyDown(KeyCode.F10)) || Input.GetKeyDown(KeyCode.F5))
        {
            _shown = !_shown;
            if (_shown)
            {
                // 重新打开时清滚动与实测高度缓存，避免沿用上次分类的旧高度；关闭不清。
                _scroll = Vector2.zero;
                _measuredContentHeight = 0f;
            }
        }
        else if (_shown && Input.GetKeyDown(KeyCode.Escape))
            _shown = false;
        PatchUI_PanelFocus.Tick();
        PatchDiag_FrameWatch.Tick();
        // 外墙工匠持久驱动（issue-86）：不按面板开关或游戏状态过滤；世界/权限/异常与限流日志由 runtime 自理。
        WallEngineerRuntime.Tick(Managers.Inst, Time.time);
        try { CalendarHud.Tick(); }
        catch { /* CalendarHud backs off internally; input toggles have already been handled. */ }
        try { PopulationHud.Tick(); }
        catch { /* Counts are an independent read-only overlay; settings shortcuts remain available. */ }
        PatchRoles_Hermit.Tick();
        PatchRoles_PetGuard.Tick();
        GreekScaleScope.Tick();
        PatchEconomy_Banker.TickOwnedProfiles();
        BankAssistantCoordinator.TickPendingCleanup();
        PatchPlayer_HoldPurchase.Tick();
        PatchWorld_OptionalVegetation.Tick();
        try { MusketeerIdentity.Tick(); MusketeerRuntime.Tick(); MusketeerShop.Tick(); }
        catch { /* One optional career cannot disable the settings panel or unrelated systems. */ }
        HeroRecruitment.Tick();
        HeroArcherRuntime.Tick();
        HeroShop.Tick();
        try { CoinCourierPersistence.Tick(); CoinCourierRuntime.Tick(); }
        catch { /* 可选角色：不得拖垮面板与其他系统 */ }
        // 共享传送 FX 的集中 Tick（哥布林关闭时税收助手等调用方仍能播放）；内部按帧去重。
        try { CoinCourierTeleportFx.Tick(Time.deltaTime); }
        catch { }
        HeroArcherArrowVisuals.Tick();
        PatchArcher_Options.Tick();
        PatchArcher_GreekImpact.Tick();
        PatchArcher_Impact.Tick();
        PatchDivine_HermesHeadwear.Tick();
        HeavyShieldIntegration.Tick();
        PatchRoles_Crossbowman.Tick();
        ShopCleanupQueue.TickPendingCleanup();
    }

    private void OnDestroy()
    {
        // 仅面板对象真正销毁（退出游戏）时收尾持久驱动；隐藏面板或暂停不重置。
        WallEngineerRuntime.Reset();
    }

    private static bool _faultLogged;

    private void LateUpdate()
    {
        // Read the native Animator after its update; the optional hero driver shares the same frame guard.
        HeroArcherVisuals.Sync("panel-late");
        try { MusketeerVisuals.Sync(); MusketeerGunVisuals.Sync(); } catch { }
        DeadlandsFollowerCapture.SampleLate(IsShown);   // issue-79 临时只读录制（非 Armed/Recording 时只布尔早退）
    }

    private void OnGUI()
    {
        if (!_shown)
        {
            KnightStylePanel.SetSectionVisible(false);
            CalendarHud.Draw();
            PopulationHud.Draw();
            return;
        }
        GUISkin savedSkin = GUI.skin;
        Color savedColor = GUI.color;
        Color savedBackground = GUI.backgroundColor;
        Color savedContent = GUI.contentColor;
        Matrix4x4 savedMatrix = GUI.matrix;
        bool savedEnabled = GUI.enabled;
        bool savedChanged = GUI.changed;
        try
        {
            EnsureStyles();
            GUI.skin = _skin;
            GUI.color = GUI.backgroundColor = GUI.contentColor = Color.white;
            GUI.enabled = true;
            // At 1280x720 this keeps 22px text and a 1232x672 panel; smaller screens scale together, larger ones cap at 1240x940.
            float scale = Mathf.Min(1f, Mathf.Min(Screen.width / 1120f, Screen.height / 720f));
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float canvasWidth = Screen.width / scale;
            float canvasHeight = Screen.height / scale;
            float width = Mathf.Min(1240f, canvasWidth - 48f);
            float height = Mathf.Min(940f, canvasHeight - 48f);
            Rect panel = new Rect((canvasWidth - width) / 2f, (canvasHeight - height) / 2f, width, height);
            GUI.BeginGroup(panel);
            try { DrawPanel(width, height); }
            finally { GUI.EndGroup(); }
        }
        catch (Exception ex)
        {
            // Close instead of erroring on every GUI event; F5 still reopens afterwards.
            // 崩溃取证（实机#4）：本机 HarmonyX 栈修正被系统拒绝（Permission
            // denied），异常 ToString/拼接会触发栈符号化 → SIGSEGV 直接带崩
            // 进程（崩溃报告 RuntimeMethodHandle::GetName/IsConstructor 族）。
            // 因此只记 类型+Message（无栈），保留可观测性且不再放大为闪退。
            _shown = false;
            if (!_faultLogged)
            {
                _faultLogged = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                    "[Panel] OnGUI failed, panel closed: " + ex.GetType().FullName
                    + ": " + ex.Message);
            }
        }
        finally
        {
            GUI.skin = savedSkin;
            GUI.color = savedColor;
            GUI.backgroundColor = savedBackground;
            GUI.contentColor = savedContent;
            GUI.matrix = savedMatrix;
            GUI.enabled = savedEnabled;
            GUI.changed = savedChanged;
        }
    }

    private static void EnsureStyles()
    {
        if (_skin != null && _label != null && _button != null && _card != null) return;
        // Build fully into locals and commit only at the end: assigning _skin first would let a
        // partial failure poison the cache (skin present, styles null) and break every retry.
        GUISkin skin = null;
        Texture2D back = null, cardBack = null, gold = null, track = null, thumb = null;
        try
        {
            skin = UnityEngine.Object.Instantiate(GUI.skin);
            skin.hideFlags = HideFlags.HideAndDontSave;
            back = Texture(new Color(0.065f, 0.08f, 0.105f));
            cardBack = Texture(new Color(0.105f, 0.13f, 0.165f));
            gold = Texture(Gold);
            track = Texture(new Color(0.23f, 0.28f, 0.34f));
            thumb = Texture(new Color(0.74f, 0.64f, 0.43f));
            GUIStyle label = Style(skin.label, 22, Text);
            GUIStyle title = Style(skin.label, 30, Gold);
            title.fontStyle = FontStyle.Bold;
            GUIStyle muted = Style(skin.label, 17, Muted);
            muted.wordWrap = true;
            GUIStyle value = Style(skin.box, 21, Gold);
            value.alignment = TextAnchor.MiddleCenter;
            value.normal.background = back;
            GUIStyle tab = Style(skin.button, 22, Muted);
            tab.normal.background = cardBack;
            tab.hover.background = track;
            tab.hover.textColor = Text;
            tab.active.background = gold;
            tab.active.textColor = Color.black;
            GUIStyle activeTab = new GUIStyle(tab);
            activeTab.normal.background = gold;
            activeTab.normal.textColor = new Color(0.10f, 0.11f, 0.13f);
            activeTab.fontStyle = FontStyle.Bold;
            GUIStyle button = new GUIStyle(tab);
            button.fontSize = 19;
            GUIStyle card = new GUIStyle(skin.box);
            card.normal.background = cardBack;
            skin.horizontalSlider.fixedHeight = 8f;
            skin.horizontalSlider.margin = new RectOffset(0, 0, 10, 10);
            skin.horizontalSlider.normal.background = track;
            skin.horizontalSliderThumb.fixedWidth = 20f;
            skin.horizontalSliderThumb.fixedHeight = 28f;
            skin.horizontalSliderThumb.normal.background = gold;
            skin.horizontalSliderThumb.hover.background = gold;
            skin.horizontalSliderThumb.active.background = thumb;
            skin.verticalScrollbar.fixedWidth = 16f;
            skin.verticalScrollbar.normal.background = back;
            skin.verticalScrollbarThumb.normal.background = thumb;
            skin.verticalScrollbarThumb.hover.background = gold;
            skin.verticalScrollbarThumb.active.background = gold;
            skin.verticalScrollbarThumb.fixedWidth = 16f;
            skin.verticalScrollbarThumb.fixedHeight = 0f;
            skin.verticalScrollbarThumb.stretchHeight = true;
            skin.verticalScrollbarThumb.overflow = new RectOffset(0, 0, 0, 0);
            _skin = skin; _back = back; _cardBack = cardBack; _gold = gold; _track = track; _thumb = thumb;
            _label = label; _title = title; _muted = muted; _value = value; _tab = tab;
            _activeTab = activeTab; _button = button; _card = card;
        }
        catch
        {
            UnityEngine.Object.Destroy(skin);
            UnityEngine.Object.Destroy(back); UnityEngine.Object.Destroy(cardBack);
            UnityEngine.Object.Destroy(gold); UnityEngine.Object.Destroy(track); UnityEngine.Object.Destroy(thumb);
            throw;
        }
    }

    private static Texture2D Texture(Color color)
    {
        var texture = new Texture2D(1, 1);
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private static GUIStyle Style(GUIStyle source, int size, Color color)
    {
        var style = new GUIStyle(source);
        style.fontSize = size;
        style.normal.textColor = color;
        style.alignment = TextAnchor.MiddleLeft;
        style.padding = new RectOffset(8, 8, 2, 2);
        return style;
    }

    private static void DrawPanel(float width, float height)
    {
        ImGuiCompat.DrawTexture(new Rect(0, 0, width, height), _back);
        ImGuiCompat.DrawTexture(new Rect(0, 0, width, 3), _gold);
        GUI.Label(new Rect(24, 18, 520, 42), "王国 · 增强设置", _title);
        GUI.Label(new Rect(26, 62, 560, 28), "KINGDOM ENHANCED  /  调整你的王国", _muted);
        if (GreekBankScope.IsActive)
        {
            int stashed = BankAssistantCoordinator.GetStashedCoinsForPanel();
            GUI.Box(new Rect(width - 334, 27, 230, 44),
                stashed < 0 ? "银行 · 未就绪" : "银行 · " + stashed + " 币", _value);
        }
        if (GUI.Button(new Rect(width - 84, 27, 58, 44), "关闭", _button)) _shown = false;

        float tabWidth = (width - 48f - 8f * (Categories.Length - 1)) / Categories.Length;
        for (int i = 0; i < Categories.Length; i++)
        {
            if (GUI.Button(new Rect(24 + i * (tabWidth + 8), 110, tabWidth, 46),
                    Categories[i], i == _category ? _activeTab : _tab) && _category != i)
            {
                _category = i;
                _scroll = Vector2.zero;
                _measuredContentHeight = 0f;
            }
        }

        float viewHeight = height - 224f;
        // 内容高度以 DrawControls 的实测累计为准（估算只作首帧下限）：静态卡片数曾与实际控件
        // 数脱节（弓箭页 5 控件被估成 4），最后一张卡被裁掉且永远滚不到（2026-09-23 玩家反馈）。
        float contentHeight = Mathf.Max(_measuredContentHeight, EstimateCards() * (CardHeight + 12f));
        Rect viewport = new Rect(24, 176, width - 48, viewHeight);
        Rect content = new Rect(0, 0, width - 74, Mathf.Max(viewHeight, contentHeight));
        _scroll = GUI.BeginScrollView(viewport, _scroll, content, false, true);
        KnightStylePanel.SetSectionVisible(_category == 7); // 打开分区时刷新一次（不做实时跟随）
        float drawn;
        try { drawn = DrawControls(content.width); }
        finally { GUI.EndScrollView(); }
        _measuredContentHeight = drawn + 16f;
        GUI.Label(new Rect(26, height - 40, width - 52, 28),
            "修改自动保存  ·  F5 / Ctrl+F10 开关面板  ·  Esc 关闭", _muted);
    }

    private static float DrawControls(float width)
    {
        float y = 0;
        DrawControls(width, ref y);
        return y;
    }

    // 首帧下限估算（真实高度由实测接管后此项不再决定滚动范围）。
    private static int EstimateCards()
    {
        return _category == 7 ? 3
            : (_category == 4 ? 9 : (_category == 3 ? 5 : (_category == 5 ? 5
            : (_category == 6 ? 3 : (_category == 8 ? 8 : (_category == 0 ? 5 : 3))))));
    }

    private static void DrawControls(float width, ref float y)
    {
        switch (_category)
        {
            case 0:
                Toggle(ref y, width, "启用增强 Mod", ModConfig.Enabled, "关闭玩法修改；面板本身与自动暂停不受此开关影响。");
                Toggle(ref y, width, "无限金币", ModConfig.InfiniteMoney, "立即生效 · 君主支付不再消耗金币。");
                IntegerSlider(ref y, width, "君主移动速度", ModConfig.SpeedMultiplier, 1, 5, "倍", "移动时生效。");
                Toggle(ref y, width, "快速建造", ModConfig.FastBuild, "建造时生效 · 建筑约 2 秒建成。");
                // 只读状态（issue-86）：外墙耐久倍率由 WallEngineerRuntime 维护，无开关/滑条/人数重算。
                Card(y, width, "外墙耐久上限", CalendarHud.FormatWallStatus(WallEngineerRuntime.Status),
                    "10名工匠×2，20名及以上×3；新增耐久需维修填满。");
                y += CardHeight + 12;
                break;
            case 1:
                Toggle(ref y, width, "常驻职业与骑士人数", ModConfig.ShowPopulationHud,
                    "单机/主机左上角显示本岛存活人数、五世界骑士和两类弹药数量；客机暂无可靠统计。");
                IntegerSlider(ref y, width, "乞丐刷新间隔", ModConfig.BeggarSpawnIntervalSeconds, 1, 120, "秒",
                    "约 0.5 秒内应用，重新计时；每次补 1 人。原生回退最短约 6 秒。");
                IntegerSlider(ref y, width, "每座乞丐帐篷上限", ModConfig.BeggarCampCapacity, 1, 20, "人",
                    "仅影响后续补员 · 调低或重新读档都不会删除已有乞丐。");
                break;
            case 2:
                Toggle(ref y, width, "常驻时间与银行", ModConfig.ShowCalendarHud,
                    "显示总天数、整点和季节进度；希腊世界额外显示银行金币。");
                FloatSlider(ref y, width, "地图大小", ModConfig.MapSizeMultiplier, 1, 5, false,
                    "新生成岛实际长度倍率，按原生地块取整；已生成岛不变。");
                FloatSlider(ref y, width, "箭塔基底密度", ModConfig.TowerSpotMultiplier, 1, 4, false,
                    "重新载入地图时生效 · 1 倍为原生密度。");
                FloatSlider(ref y, width, "墙基密度", ModConfig.WallSpotMultiplier, 1, 4, false,
                    "重载地图或边界扩张后生效 · 1 倍为原生密度 · 受墙位间距限制可能低于设定值。");
                Toggle(ref y, width, "跨世界坐骑", ModConfig.CrossWorldMountsEnabled,
                    "实验 · 希腊单机普通战役：跨世界坐骑集中在下方新增探索岛；旧岛不追加，彩虹小马新获取需活动资格。");
                break;
            case 3:
                FloatSlider(ref y, width, "每波怪物数量", ModConfig.EnemyCountMultiplier, 1, 5, false,
                    "后续怪物波次生成时生效。");
                FloatSlider(ref y, width, "怪物时间线推进", ModConfig.EnemyTimelineSpeed, 1, 5, false,
                    "后续进攻计算时生效 · 倍率越高，敌军成长越快。");
                FloatSlider(ref y, width, "法杖神器冷却", ModConfig.StaffCooldownMultiplier, 0.2f, 1, true,
                    "当前 " + (30f * ModConfig.StaffCooldownMultiplier.Value).ToString("0.##") + " 秒 / 原生 30 秒 · 使用时生效。");
                Toggle(ref y, width, "法杖轮换头饰", ModConfig.HermesHeadwearEnabled,
                    "新转化按累计 " + ModConfig.HermesHeadwearChancePercent.Value + "% 配额轮流戴44款头饰（30%即每10只3只）；戴头饰免主动选敌，仍可受范围伤害；已有外观保持。");
                FloatSlider(ref y, width, "坐骑技能冷却", ModConfig.SteedCooldownMultiplier, 0.2f, 1, true,
                    "使用时生效 · 原生冷却因坐骑而异。");
                break;
            case 4:
                RestockControl(ref y, width, "工匠", 0, ModConfig.AutoRestockWorkersEnabled, ModConfig.AutoRestockWorkersTarget);
                RestockControl(ref y, width, "弓箭手", 1, ModConfig.AutoRestockArchersEnabled, ModConfig.AutoRestockArchersTarget);
                RestockControl(ref y, width, "火枪手 · 火枪", 8, ModConfig.AutoRestockMusketeersEnabled, ModConfig.AutoRestockMusketeersTarget);
                RestockControl(ref y, width, "忍者", 2, ModConfig.AutoRestockNinjasEnabled, ModConfig.AutoRestockNinjasTarget);
                RestockControl(ref y, width, "狂战士", 3, ModConfig.AutoRestockBerserkersEnabled, ModConfig.AutoRestockBerserkersTarget);
                RestockControl(ref y, width, "无业村民 · 面包", 4, ModConfig.AutoRestockPeasantsEnabled, ModConfig.AutoRestockPeasantsTarget);
                RestockControl(ref y, width, "农民 · 镰刀", 5, ModConfig.AutoRestockFarmersEnabled, ModConfig.AutoRestockFarmersTarget);
                RestockControl(ref y, width, "投石车 · 火药桶", 6, ModConfig.AutoRestockCatapultBarrelsEnabled, ModConfig.AutoRestockCatapultBarrelsTarget);
                RestockControl(ref y, width, "希腊火焰塔 · 弹药", 7, ModConfig.AutoRestockFireTowerAmmoEnabled, ModConfig.AutoRestockFireTowerAmmoTarget);
                break;
            case 5:
                Toggle(ref y, width, "坐骑无限体力", ModConfig.InfiniteSteedStamina,
                    "所有世界 · 本机控制的坐骑奔跑与滑翔不耗体力；关闭恢复自然消耗，技能冷却不变。");
                Toggle(ref y, width, "长按连续购买", ModConfig.HoldPurchaseEnabled,
                    "所有世界 · 起初正常，长按后加速续买；支持火药桶与火塔弹药，松开即停。");
                DenseThicketControl(ref y, width);
                Toggle(ref y, width, "森林快速消退", ModConfig.FastForestRecedeEnabled,
                    "所有世界 · 砍树后的森林消退等待缩至三分之一；关闭后的新消退使用原版速度。");
                Toggle(ref y, width, "宠物与隐士防抓", ModConfig.PetGuardEnabled,
                    "所有世界·单机/主机：怪物不再抓走狗与隐士；开启当刻与每次读档把已被抓走的狗/隐士找回当前岛。关闭恢复原版抓走与赎回路径。");
                break;
            case 6:
                ModConfig.ArcherVolleyCount.Value = (int)ArcherControl(ref y, width, "中世纪随从散射", ModConfig.ArcherScatterEnabled,
                    Mathf.Clamp(ModConfig.ArcherVolleyCount.Value, 1, 3), 1, 3, 1, Mathf.Clamp(ModConfig.ArcherVolleyCount.Value, 1, 3) + " 支 / 发",
                    "所有世界 · 仅中世纪骑士的弓箭手随从对敌散射；打猎单发，额外箭淡金色，总数含主箭。");
                ModConfig.ArcherRateMultiplier.Value = ArcherControl(ref y, width, "弓箭手射速", ModConfig.ArcherRateEnabled,
                    ModConfig.ArcherRateMultiplier.Value, 1, 2, 0.25f, ModConfig.ArcherRateMultiplier.Value.ToString("0.##") + " 倍",
                    "所有世界 · 最高 2 倍，关闭恢复原版；不加快移动和游戏时间。");
                Toggle(ref y, width, "希腊随从火矢爆发", ModConfig.ArcherImpactEnabled,
                    "随从火矢以半径 0.25、一次 1 点范围伤害替代灼烧；直击不叠加，同轮散射去重。关闭恢复原版。");
                break;
            case 7:
                KnightStylePanel.DrawSection(ref y, width, _card, _label, _muted, _value, _button, _activeTab);
                // 临时诊断（issue-79 第一切片）：只 Arm 一次 8 秒只读记录，不修改配置、不改玩法。
                Card(y, width, "记录附近死地随从（临时）", DeadlandsFollowerCapture.StatusText, DeadlandsFollowerCapture.HelpText);
                if (GUI.Button(new Rect(22, y + 51, 220, 31), "开始记录（8秒）", _button))
                    DeadlandsFollowerCapture.ArmFromPanel();
                y += CardHeight + 12;
                break;
            case 8:
                CrossbowRatioSlider(ref y, width);
                Toggle(ref y, width, "火铳铺", ModConfig.MusketeerEnabled,
                    "所有世界·单机：4金币买枪转职，不上塔；举旗另带最多4名火枪手，白天猎鹿、不伤小动物。关闭保留职业记录。");
                GUI.Label(new Rect(190, y - CardHeight - 12 + 51, width - 222, 31), MusketeerShop.StatusText, _muted);
                Toggle(ref y, width, "英雄驿站", ModConfig.HeroArcherEnabled,
                    "所有世界·单机：投8金币训练地面英雄，不上箭塔；每侧1名，死亡才空位。商店红旗表示占位，关闭保留已购名额。");
                GUI.Label(new Rect(190, y - CardHeight - 12 + 51, width - 222, 31), HeroShop.StatusText, _muted);
                Toggle(ref y, width, "宝石盾卫", ModConfig.HeavyShieldEnabled,
                    "所有世界·单机：4宝石解锁盾模，6金币购买盾具；每岛左右各1席，可各花2宝石扩至2席。驻守本岛，不随船，不自动补货。");
                GUI.Label(new Rect(190, y - CardHeight - 12 + 51, width - 222, 31), HeavyShieldIdentity.StatusText, _muted);
                Toggle(ref y, width, "金币哥布林", ModConfig.CoinCourierEnabled,
                    "所有世界·单机：城堡左投币招募；从国库逐枚取币，传送补给骑士金币槽，遇敌带余币撤回。未接存档时不可招募、不收费；关闭保留身份与钱袋。");
                GUI.Label(new Rect(190, y - CardHeight - 12 + 51, width - 222, 31), CoinCourierRuntime.StatusText, _muted);
                // 招募点/国库两行独立状态：局部加高并同步推进 y，避免塞进 31px 固定行溢出或截断。
                float courierStatusTop = y - 6f;
                GUI.Label(new Rect(24, courierStatusTop, width - 48, 44),
                    "招募点：" + CoinCourierShop.StatusText + "\n国库：" + CoinCourierRuntime.TreasuryStatusText, _muted);
                y = courierStatusTop + 44f + 6f;
                IntegerSlider(ref y, width, "哥布林招募价", ModConfig.CoinCourierRecruitPrice, 1, 20, "币",
                    "付款开始即冻结当次价格；改配置不影响进行中的交易。");
                IntegerSlider(ref y, width, "哥布林钱袋容量", ModConfig.CoinCourierPurseCapacity, 1, 40, "币",
                    "调低不裁剪已有余额；回银行只补到目标容量。");
                IntegerSlider(ref y, width, "哥布林每访上限", ModConfig.CoinCourierMaxCoinsPerVisit, 1, 12, "币",
                    "同一骑士每次访问最多补给的枚数，不是钱袋容量。");
                SecondsSlider(ref y, width, "哥布林同骑士间隔", ModConfig.CoinCourierKnightCooldown, 0f, 120f,
                    "同一骑士两次访问的最小间隔（游戏秒）。");
                break;
        }
    }

    private static float ArcherControl(ref float y, float width, string title, ConfigEntry<bool> enabled,
        float value, float min, float max, float step, string display, string help)
    {
        Card(y, width, title, display, help);
        if (GUI.Button(new Rect(22, y + 51, 104, 31), enabled.Value ? "已开启" : "已关闭",
                enabled.Value ? _activeTab : _button)) enabled.Value = !enabled.Value;
        if (GUI.Button(new Rect(138, y + 51, 34, 31), "−", _button)) value = Mathf.Max(min, value - step);
        if (GUI.Button(new Rect(width - 52, y + 51, 34, 31), "+", _button)) value = Mathf.Min(max, value + step);
        EventType type = Event.current.type;
        bool input = type == EventType.MouseDown || type == EventType.MouseDrag || type == EventType.KeyDown;
        bool changed = GUI.changed;
        GUI.changed = false;
        float raw = GUI.HorizontalSlider(new Rect(188, y + 59, width - 258, 28), value, min, max);
        if (input && GUI.changed) value = Mathf.Clamp(Mathf.RoundToInt(raw / step) * step, min, max);
        GUI.changed |= changed;
        y += CardHeight + 12;
        return value;
    }

    private static void DenseThicketControl(ref float y, float width)
    {
        bool cleaning = PatchWorld_OptionalVegetation.IsCleaning;
        bool enabled = ModConfig.DenseThicketsEnabled.Value;
        Card(y, width, "灌木双倍密度", cleaning ? "额外灌木枯萎中" : (enabled ? "已开启" : "已关闭"),
            "所有世界 · 关闭只清理额外生长的灌木，全部枯萎后才能再次开启。");
        bool wasEnabled = GUI.enabled;
        try
        {
            GUI.enabled = wasEnabled && !cleaning;
            if (GUI.Button(new Rect(22, y + 51, 145, 31), cleaning ? "等待枯萎完成" : (enabled ? "关闭" : "开启"),
                    enabled ? _activeTab : _button))
                PatchWorld_OptionalVegetation.TrySetDenseThickets(!enabled);
        }
        finally { GUI.enabled = wasEnabled; }
        y += CardHeight + 12;
    }

    private static void RestockControl(ref float y, float width, string title, int role,
        ConfigEntry<bool> enabled, ConfigEntry<int> target)
    {
        Card(y, width, title + "自动补货", "目标 " + target.Value + (role == 6 || role == 7 ? " 份" : " 人"),
            "双倍金库付款 · " + PatchEconomy_AutoRestock.GetSummary(role));
        if (GUI.Button(new Rect(22, y + 51, 104, 31), enabled.Value ? "已开启" : "已关闭",
                enabled.Value ? _activeTab : _button)) enabled.Value = !enabled.Value;
        if (GUI.Button(new Rect(138, y + 51, 34, 31), "−", _button)) target.Value = Math.Max(1, target.Value - 1);
        if (GUI.Button(new Rect(width - 52, y + 51, 34, 31), "+", _button)) target.Value = Math.Min(200, target.Value + 1);
        EventType eventType = Event.current.type;
        bool input = eventType == EventType.MouseDown || eventType == EventType.MouseDrag || eventType == EventType.KeyDown;
        bool previousChanged = GUI.changed;
        GUI.changed = false;
        float raw = GUI.HorizontalSlider(new Rect(188, y + 59, width - 258, 28), target.Value, 1, 200);
        bool interacted = input && GUI.changed;
        GUI.changed |= previousChanged;
        if (interacted)
        {
            int value = Mathf.Clamp(Mathf.RoundToInt(raw), 1, 200);
            if (value != target.Value) target.Value = value;
        }
        y += CardHeight + 12;
    }

    private static void Card(float y, float width, string title, string value, string help)
    {
        GUI.Box(new Rect(0, y, width, CardHeight), GUIContent.none, _card);
        GUI.Label(new Rect(14, y + 10, width - 210, 34), title, _label);
        GUI.Box(new Rect(width - 180, y + 12, 162, 32), value, _value);
        GUI.Label(new Rect(16, y + 88, width - 32, 28), help, _muted);
    }

    private static void Toggle(ref float y, float width, string title, ConfigEntry<bool> config, string help)
    {
        Card(y, width, title, config.Value ? "已开启" : "已关闭", help);
        if (GUI.Button(new Rect(22, y + 51, 156, 31), config.Value ? "点击关闭" : "点击开启",
                config.Value ? _activeTab : _button))
            config.Value = !config.Value;
        y += CardHeight + 12;
    }

    private static void IntegerSlider(ref float y, float width, string title, ConfigEntry<int> config,
        int min, int max, string unit, string help)
    {
        Card(y, width, title, config.Value + " " + unit, help);
        float raw = Slider(y, width, config.Value, min, max, min + unit, max + unit, out bool interacted);
        if (interacted && !Mathf.Approximately(raw, config.Value))
        {
            int next = Mathf.Clamp(Mathf.RoundToInt(raw), min, max);
            if (next != config.Value) config.Value = next;
        }
        y += CardHeight + 12;
    }

    private static void CrossbowRatioSlider(ref float y, float width)
    {
        ConfigEntry<float> config = ModConfig.CrossbowRecruitmentRatio;
        float current = CrossbowRatioPolicy.Normalize(config.Value);
        Card(y, width, "普通弩手招募比例", CrossbowRatioPolicy.Percent(current) + "%",
            "新捡弓立即生效；已有单位下次读档重算。弩手不参与骑士/举旗补员，已有队员不改；联机双方请使用相同比例。");
        float raw = Slider(y, width, current, .25f, 1f, "25%", "100%", out bool interacted);
        if (interacted)
        {
            float next = CrossbowRatioPolicy.SnapSlider(raw);
            if (next != config.Value) config.Value = next;
        }
        y += CardHeight + 12;
    }

    private static void FloatSlider(ref float y, float width, string title, ConfigEntry<float> config,
        float min, float max, bool percent, string help)
    {
        string value = percent ? PercentText(config.Value) : config.Value.ToString("0.##") + " 倍";
        Card(y, width, title, value, help);
        float raw = Slider(y, width, config.Value, min, max,
            percent ? PercentText(min) : min + "倍", percent ? PercentText(max) : max + "倍", out bool interacted);
        // In particular, the existing 0.375 CD remains untouched on open/Layout/Repaint.
        if (interacted && !Mathf.Approximately(raw, config.Value))
        {
            float next = Mathf.Clamp(Mathf.Round(raw * 20f) / 20f, min, max);
            if (!Mathf.Approximately(next, config.Value)) config.Value = next;
        }
        y += CardHeight + 12;
    }

    private static void SecondsSlider(ref float y, float width, string title, ConfigEntry<float> config,
        float min, float max, string help)
    {
        Card(y, width, title, config.Value.ToString("0.#") + " 秒", help);
        float raw = Slider(y, width, config.Value, min, max,
            min.ToString("0.#") + "秒", max.ToString("0.#") + "秒", out bool interacted);
        if (interacted && !Mathf.Approximately(raw, config.Value))
        {
            float next = Mathf.Clamp(Mathf.Round(raw * 2f) / 2f, min, max);   // 0.5 秒步进
            if (!Mathf.Approximately(next, config.Value)) config.Value = next;
        }
        y += CardHeight + 12;
    }

    private static float Slider(float y, float width, float value, float min, float max,
        string minText, string maxText, out bool interacted)
    {
        GUI.Label(new Rect(18, y + 50, 76, 30), minText, _muted);
        GUI.Label(new Rect(width - 85, y + 50, 76, 30), maxText, _muted);
        EventType eventType = Event.current.type;
        bool input = eventType == EventType.MouseDown || eventType == EventType.MouseDrag
            || eventType == EventType.KeyDown;
        bool previousChanged = GUI.changed;
        GUI.changed = false;
        float result = GUI.HorizontalSlider(new Rect(96, y + 59, width - 196, 28), value, min, max);
        interacted = input && GUI.changed;
        GUI.changed |= previousChanged;
        return result;
    }

    private static string PercentText(float multiplier) => (multiplier * 100f).ToString("0.#") + "%";
}
