using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 弩手（crossbowman）：居民捡弓转职弓箭手时，每第 4 个（3:1 交替）变成弩手——
/// 死地士兵（archer_soldier_deadlands，骑士小队随从/塔位/上船同款姿态）换装 +
/// 王国旗帜色染衣 + 索敌/射击参数强化 + 独立弩矢。弩手仍是原生
/// Archer（无新兵种、无新池、无新商店），且永远不被骑士编队招募。
///
/// 弩矢观感（用户实锤"与普通弓箭手无区别"后的改造定稿；2026-09-21 方案E更新）：
/// - 平直快弹：初速 ×2（射程包络 32，索敌钳在 12 → 12 步内用 32 步的力气打）；
/// - 平直机制改为「穿墙 + 低弹道强制」（CrossbowmanBoltWallPierce.cs）：弩矢与墙
///   碰撞互相忽略，且 BestShotInternal prefix 对克隆 SO 跳过 ParabolaCast 墙挡判定、
///   强制低弹道解——墙后射击天然平直，不再靠出膛点前移避挡（旧 2.5 前移方案废弃）；
/// - 常显 0.25s 光痕拖尾（_alwaysDrawTrail + _notPerfectTrailLength）+ 0.85 醒目体型，
///   与普通箭一眼区分。
///
/// 士兵皮肤与猎人行为不冲突（原生 Archer 本就在两套控制器间来回转：EnterGuardSlot/
/// OnEmbarkStart→ConvertToSoldier，离队/下塔→ConvertToHunter；行为由 _knight==null
/// 的猎人例程驱动，控制器只管外观；打猎用的 idle/walk/run/shoot 士兵动画集齐全）。
/// 皮肤生根（2026-09-24 双写者根治）：Apply 把实例的 Archer.soldierAnimator 直接指到
/// 死地控制器——原生 ConvertToSoldier 的 biome 换皮对未注册 original 原样穿透，原生
/// 每次调用的解析结果都是同一控制器（同引用重赋无害）：塔/船/跟队路径不再刷回世界皮，
/// 也不需要任何每帧重断言（PR#58 的 Mover.Update 皮肤守卫已退役）。ConvertToHunter
/// 方向（下塔/下船刷猎人皮）由 I-d postfix 单写事件纠正回死地控制器；死亡/退队不碰
/// （猎人皮播死亡动画，纯观感差异，接受）。
///
/// 身份与生命周期（destroy-lifecycle-20260914 修订）：
/// - 身份/战斗包/清污的实现在 CrossbowmanLifecycle：可复用 CrossbowmanMarker +
///   显式身份（Selected=本 life 选择 / Active=当前有效身份）。组件存在不算弩手，
///   读取器一律经 IsCrossbowman（即时读 ModConfig.Enabled）。
///   旧实现 Strip 里的 DestroyImmediate 已删除：它是确认存在的生命周期风险——
///   在物理触发器回调（捡弓转职发生处）里立即销毁组件的行为，Unity 文档明确不允许，
///   且销毁不生效会留下失效身份与已乘冷却。本修复只消除这种风险，不代表已证实玩家闪退根因。
/// - life 边界靠三个钩子（native 实锤见下方签名表）：
///   `Pool.FastSpawn` 作用域（prefix/finalizer）+ 真实 `Archer.OnEnable`（prefix/postfix）
///   = 池激活新 life（同 NetID 回执返回已 active 对象时不触发 OnEnable，因此不误清职业）；
///   `Archer.OnDisable` prefix 只失效 Active、保留 Selected（隐藏≠池归还）。
/// - 本文件另负责资产构建（EnsureAssets）、Harmony 入口、5s 巡检批次交接与面板 Tick 入口。
///
/// 与原生契约：
/// - ActiveArrowAttack 可写；原生 Awake/OnEnable/火矢 buff/网络收包都会重置它——
///   完整性巡检只兜"等于原生 _arrowAttack"的实例，火矢 buff（_fireArrowAttack）期间绝不动。
/// - 地面 shootRange 保持12；塔位射程由 CrossbowDefense 按实例原生值×1.5，
///   进出塔、Apply、巡检和池复用统一收敛，不重复叠加。
/// - 射击间隔（_shootIntervalRange/_shootIntervalRangeFormation）只在 Apply 时按现值 ×2
///   （借用账本保证重试/同帧复用不二次乘），巡检不检查（buff/阵形可能合法修改它们）。
///
/// 2.4.0 签名验证（Operator 侦查：2.1.0 源码 + 2.4.0 interop 二进制双验证，实锤直接采用）：
/// - Character.Promote(DroppableTool, IUnitController) : Character —— 存在；弓映射 {"Bow","Archer"}
/// - Archer.shootRange=8f / towerShootRange=12f —— 实例字段，interop 可读写
/// - Archer.ActiveArrowAttack : ArrowAttack（可写）/ _arrowAttack / _fireArrowAttack —— 存在
/// - Archer._shootIntervalRange / _shootIntervalRangeFormation : Vector2 —— 存在
/// - Archer._enemyScanner : Scanner；Scanner.range / rangeBehind 可写 —— 存在
/// - ArrowAttack：_arrowPrefab(Arrow) / _shotMagnitude / _boostedShotMagnitude /
///   _arrowGravity / _arrowOriginOffset(Vector2，FireArrow 按方向符号侧移) —— 存在
/// - Arrow.hitDamage / perfectDamageMultiplier / _alwaysDrawTrail(bool) /
///   _notPerfectTrailLength(float，EnableTrail 短尾时长) —— 存在（_damageSource 保持 Arrow 不动）
/// - Bolt : MonoBehaviour（DamageSource.Bolt，非 Arrow 子类）—— 仅取 SpriteRenderer.sprite 外观
/// - Archer.IsAvailableForJob(GameObject) : bool —— 实例方法，存在
/// - PoolManager.cachedPools / cachedNamePoolPairs / cachedSyncIdPoolPairs —— 公开属性
/// - Archer.OnEnable() / Archer.OnDisable() —— 存在（actual-api token 100663789 / 100663790；
///   root 核 native 0x4b32d0 / 0x4b2c10）。OnEnable 内部 AddArcher(0x4b34c7)/DistributeFreeArchers
///   (0x4b3604) 会立刻做招募判定；OnDisable 会把 `_arrowAttack` 写回 ActiveArrowAttack(0x4b2ffc)
///   但不恢复 shootRange/interval，且非权威/非主场景会提前 return → 清理责任不能依赖原生。
/// - Pool.FastSpawn(Vector3,Quaternion,Transform,Int16,Boolean) : GameObject —— 存在
///   （actual-api token 100676196；root 核 native 0x6c10a0，唯一）。真实池激活走
///   `_cache` → SetActive(true) → OnEnable；syncReceipt 命中 `_activeCache` 同 NetID 时
///   直接返回已 active 对象（Pool.cs:506）→ 不触发 OnEnable（因此不能只靠 FastSpawn 判定新 life）。
/// </summary>
public static class PatchRoles_Crossbowman
{
    // ---- 数值定稿（Operator 裁决，勿改） ----
    // 弩手包数值（射程 12 / y 缩放 1.15 / 冷却 ×2）单一来源在 CrossbowmanLifecycle
    // （身份与战斗包生命周期归它管，含随从包复用）；本文件不再各自定义一份。
    private const int BoltHitDamage = 2;                   // 原生 1；perfect 自动 ×2 = 4
    private const float RangeMultiplier = 1.5f;            // 射程 ×1.5（8→12）；索敌钳制用（shootRange/扫描器）
    // 初速 ×2（弩矢观感改造）：Range=v²/g → 射程包络=8×4=32，但索敌仍由 shootRange/
    // 扫描器钳在 12——12 步内目标用 32 步的力气打，又平又快。Archer.cs:1116 的
    // 推进判断读 SO Range=32 → 弩手 12 步内站桩狙击不冒进（用户早已接受的旧行为）
    private const float ShotMagnitudeMultiplier = 2f;
    internal const float BoltVisualScale = 0.85f;          // 弩矢醒目化（原 0.65 缩小观感弱）；连带碰撞体等比缩放，快弹判定影响可忽略
    // 出膛点（方案E，2026-09-21）：原生 _arrowOriginOffset 默认 (0.15,0.5)。旧方案
    // 前移 2.5 步让出膛点≈墙沿、靠位置避 ParabolaCast 的墙挡——已废弃：新机制=
    // 弩矢穿墙（CrossbowmanBoltWallPierce 组件 → HeroArcherWallPierce.Apply）+
    // BestShotInternal 低弹道强制（同文件 prefix 跳过 ParabolaCast 墙挡判定），
    // 墙后射击天然平直，与出膛点位置解耦。回调到小前移 (0.6,0.7)：出膛观感贴近
    // 弩手本体，不再依赖与墙的距离关系。ArrowAttack.FireArrow（ArrowAttack.cs:60）
    // 按目标方向符号侧移 x，正值=朝目标前方。
    private static readonly Vector2 BoltOriginOffset = new Vector2(0.6f, 0.7f);
    // 常显拖尾长度（秒）：Arrow.EnableTrail（Arrow.cs:67）在 _alwaysDrawTrail 且
    // 非 perfect 时用 _notPerfectTrailLength（原生默认 0.1，火矢用长尾）——0.25s
    // 光痕拖尾让弩矢与普通箭一眼区分
    private const float BoltTrailLength = 0.25f;
    private const int PromoteCycle = 4;                    // 3:1 交替
    private const float RecomputeDelaySeconds = 15f;       // 等单位恢复完成
    private const float IntegrityIntervalSeconds = 5f;
    // 夜间站位策略集中在 PatchRoles_CrossbowDefense，守墙目标稳定分散到墙内4..7。

    private const string BoltPrefabName = "KEM_CrossbowBolt";
    private const string AttackSoName = "KEM_CrossbowAttack";
    // 死地士兵（骑士小队随从/塔位/上船同款姿态），不是 archer_deadlands（死地猎人）。
    // 原生 Archer.ConvertToSoldier 同款机制：士兵皮肤=动画控制器换装+王国旗帜色染衣。
    private const string SoldierControllerName = "archer_soldier_deadlands";

    // 同步池 id 分配：自建独立计数器（不 import PatchRoles_Castle 的私有分配器）。
    // 起点 31000：Castle 分配器从 30000 单调递增且不查占用，多次岛跳 Init 重建后
    // 会爬进 30130+ 段（单进程约 11-19 次重建即到 30132）；31000 起给它留约 1000
    // 次重建余量，整类碰撞风险消除。银行助手（30120..30123）/幽灵骑士（30130..30131）
    // 保留段跳过逻辑原样保留作防御。
    private const int SyncIdStart = 31000;
    private const int SyncIdMax = 31999;
    private const int BankAssistantSyncIdMin = 30120;
    private const int BankAssistantSyncIdMax = 30123;
    private const int GhostSquadSyncIdMin = 30130;
    private const int GhostSquadSyncIdMax = 30131;

    // ---- 进程级状态 ----
    private static int _bowPromoteCount;        // 弓转职计数：跨岛延续、完整退出重置（狂战士进阶序列同款惯例）
    private static IntPtr _supervisorWorld;     // per-world 巡检守卫（World 指针，范式同 DefenseSpacing）

    // ---- 惰性静态资产（构建一次，DontDestroyOnLoad，跨场景存活） ----
    private static bool _assetsReady;
    private static bool _criticalFailureLogged;
    private static bool _deadlandsResolved;
    private static ArrowAttack _crossbowAttackSO;        // 关键资产：缺失即放弃 Apply（不能半套）
    private static GameObject _crossbowBoltPrefab;
    private static RuntimeAnimatorController _deadlandsController;

    // 原生默认值缓存（Strip 恢复用；来自 Holder["Archer"] prefab）
    private static float _baseShootRange;
    private static bool _baseShootRangeCached;
    private static Vector2 _baseInterval;
    private static bool _baseIntervalCached;
    private static Vector2 _baseIntervalFormation;
    private static bool _baseIntervalFormationCached;
    private static RuntimeAnimatorController _baseAnimatorController;
    // 原生 Archer prefab 的 soldierAnimator（皮肤生根基线）：弩手 Apply 生根到死地控制器，
    // Strip/池新 life/UnwindAll 写回这个快照；随从侧经 BaseSoldierAnimator 访问器共用。
    private static RuntimeAnimatorController _baseSoldierAnimator;

    private static short _nextSyncId = SyncIdStart;

    // ---- 一次性日志去重 ----
    private static bool _loggedPromoteMismatch;
    private static bool _loggedApplyAborted;
    private static bool _loggedBoltSpriteMissing;
    private static bool _loggedKnightExclusion;
    private static bool _loggedSyncIdConflict;
    private static bool _loggedSyncIdExhausted;

    // ============================================================
    // B. 转职交替主入口（Character.Promote postfix 宿主见文件尾）
    // ============================================================

    internal static void OnBowPromoted(Character result)
    {
        if (result == null || result.gameObject == null) return;
        if (MusketeerIdentity.GunPromotionInProgress || MusketeerIdentity.IsMarked(result.gameObject)) return;
        Archer archer = result.GetComponent<Archer>();
        if (archer == null)
        {
            if (!_loggedPromoteMismatch)
            {
                _loggedPromoteMismatch = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                    "[Crossbowman] bow promote result has no Archer component; alternation skipped");
            }
            return;
        }

        try
        {
            // 池复用清污（marker 类型注册由 CrossbowmanLifecycle 各入口自保）。
            // 旧实现在这里 DestroyImmediate(marker)：捡弓转职发生在物理触发器回调
            // （OnTriggerStay/OnTriggerEnter2D）里，Unity 拒绝立即销毁组件——报错
            // 且销毁不生效，旧 marker 仍被 GetComponent 命中（资格/招募排除/巡检
            // 强化继续作用在普通弓箭手上），冷却也已乘过。现在改为"身份立即失效 +
            // 属性还原"，组件留给下一生命周期复用；无身份无残留时零写入。
            Strip(archer);

            _bowPromoteCount++;
            if (_bowPromoteCount % PromoteCycle == 0)
            {
                Apply(archer);
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[Crossbowman] bow promote #" + _bowPromoteCount + " -> crossbowman (25%)");
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman/promote] " + e);
        }
    }

    // ============================================================
    // C. Apply：弩手打包（幂等）——实现在 CrossbowmanLifecycle，
    //    这里只做资产惰性构建 + 组装 profile（身份/账本/属性写都在核心文件里，
    //    便于测试直链生产逻辑）。
    // ============================================================

    private static void Apply(Archer archer)
    {
        if (archer == null || archer.gameObject == null) return;
        EnsureAssets();
        if (_crossbowAttackSO == null)
        {
            if (!_loggedApplyAborted)
            {
                _loggedApplyAborted = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                    "[Crossbowman] apply aborted: cloned ArrowAttack missing; cannot apply half-set");
            }
            return;
        }
        CrossbowmanLifecycle.Apply(archer, BuildProfile());
    }

    /// <summary>
    /// 宿主侧资产/原生基线快照（无堆分配：染衣委托静态持有一次，避免每次转职/巡检都新建委托）。
    /// Strip 不触发资产构建（与旧实现一致：资产没建好时只走 marker 借用账本与实例原生字段，
    /// 回落分支自动跳过）。
    /// </summary>
    private static CrossbowmanProfile BuildProfile() => new CrossbowmanProfile
    {
        Attack = _crossbowAttackSO,
        Skin = _deadlandsController,
        ReapplyBanner = BannerStep,
        BaseShootRange = _baseShootRange,
        BaseShootRangeKnown = _baseShootRangeCached,
        BaseInterval = _baseInterval,
        BaseIntervalKnown = _baseIntervalCached,
        BaseIntervalFormation = _baseIntervalFormation,
        BaseIntervalFormationKnown = _baseIntervalFormationCached,
        BaseSkin = _baseAnimatorController,
        BaseSoldierAnimator = _baseSoldierAnimator,
    };

    private static readonly Action<Archer> BannerStep = ApplyBannerColors;

    /// <summary>
    /// 旗帜色染衣：复刻原生 ConvertToSoldier 的权威端染衣块（Archer.cs:859-867）——
    /// 主/副色随机二选一穿身上、另一色为副。outfitColor/outfitSecondaryColor 是带
    /// spriteFX recolor 刷新的属性，直接写即生效，不需要走 PickOutfitColor 的可空参数。
    /// _isWearingBannerColor 幂等标记与原生共用：原生士兵入队时不会重复染。
    /// </summary>
    private static void ApplyBannerColors(Archer archer)
    {
        if (!NetworkBigBoss.HasWorldAuth || archer._isWearingBannerColor) return;
        try
        {
            Character character = archer.GetComponent<Character>();
            CoatOfArms coatOfArms = CampaignSaveData.current != null
                ? CampaignSaveData.current.coatOfArms
                : null;
            if (character == null || coatOfArms == null) return;
            bool usePrimary = UnityEngine.Random.value < 0.5f;
            character.outfitColor = usePrimary ? coatOfArms.primaryColor : coatOfArms.secondaryColor;
            character.outfitSecondaryColor = usePrimary ? coatOfArms.secondaryColor : coatOfArms.primaryColor;
            archer._isWearingBannerColor = true;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman/banner] " + e);
        }
    }

    // ============================================================
    // E. Strip：清污/还原（实现在 CrossbowmanLifecycle）
    //    身份立即失效 + 还原 owned 属性；绝不 DestroyImmediate/Destroy marker
    //    （池实例复用同一组件；对象池 respawn 不重拷序列化字段，属性必须显式恢复）。
    // ============================================================

    private static void Strip(Archer archer)
    {
        CrossbowmanLifecycle.Strip(archer, BuildProfile());
    }

    // ============================================================
    // A. 静态资产惰性构建（幂等，所有入口先调）。holder 未就绪时安全跳过；
    //    任何关键失败都不缓存半成品，下次入口自动重试。
    // ============================================================

    private static void EnsureAssets()
    {
        if (_assetsReady) return;
        try
        {
            var managers = Managers.Inst;
            var holder = managers != null ? managers.holder : null;
            if (holder == null || holder.tagCharacterPairs == null) return;

            Character character = null;
            if (!holder.tagCharacterPairs.TryGetValue("Archer", out character) || character == null) return;
            Archer prefabArcher = character.GetComponent<Archer>();
            if (prefabArcher == null) return;

            // 1) 缓存原生默认值（Strip 恢复用）
            _baseShootRange = prefabArcher.shootRange;
            _baseShootRangeCached = true;
            _baseInterval = prefabArcher._shootIntervalRange;
            _baseIntervalCached = true;
            _baseIntervalFormation = prefabArcher._shootIntervalRangeFormation;
            _baseIntervalFormationCached = true;
            Animator baseAnimator = prefabArcher.GetComponentInChildren<Animator>();
            _baseAnimatorController = baseAnimator != null ? baseAnimator.runtimeAnimatorController : null;
            // 皮肤生根基线（2026-09-24 双写者根治）：原生 prefab 的 soldierAnimator 快照，
            // 还原三处（Strip / 池新 life / UnwindAll）都写回它。
            _baseSoldierAnimator = prefabArcher.soldierAnimator;

            ArrowAttack baseSO = prefabArcher._arrowAttack;
            if (baseSO == null)
            {
                LogCriticalFailure("Archer prefab _arrowAttack is null; crossbowman disabled");
                return;
            }
            Arrow baseArrowPrefab = baseSO._arrowPrefab;
            if (baseArrowPrefab == null || baseArrowPrefab.gameObject == null)
            {
                LogCriticalFailure("base ArrowAttack._arrowPrefab is null; crossbowman disabled");
                return;
            }

            // 2) 克隆弩矢 prefab：数值 + 外观，其余组件（TrailRenderer/碰撞/音效等）原样保留
            GameObject boltGo = UnityEngine.Object.Instantiate(baseArrowPrefab.gameObject);
            if (boltGo == null)
            {
                LogCriticalFailure("bolt prefab clone failed; crossbowman disabled");
                return;
            }
            boltGo.name = BoltPrefabName;
            UnityEngine.Object.DontDestroyOnLoad(boltGo);
            boltGo.SetActive(false); // 池 prefab 惯例：非激活，由 Pool.Spawn 激活
            Arrow boltArrow = boltGo.GetComponent<Arrow>();
            if (boltArrow == null)
            {
                LogCriticalFailure("cloned bolt has no Arrow component; crossbowman disabled");
                UnityEngine.Object.Destroy(boltGo);
                return;
            }
            boltArrow.hitDamage = BoltHitDamage;
            // 弩矢观感强化（Arrow.cs 拖尾语义）：_alwaysDrawTrail=true 让 OnEnable
            // （Arrow.cs:39 isFireArrow || _alwaysDrawTrail → EnableTrail）常开拖尾；
            // EnableTrail（Arrow.cs:67）在 alwaysDraw 且非 perfect 时用
            // _notPerfectTrailLength（原生默认 0.1）而非 _originalTrailTime——设 0.25s
            // 光痕拖尾，与普通箭一眼区分。
            boltArrow._alwaysDrawTrail = true;
            boltArrow._notPerfectTrailLength = BoltTrailLength;
            // 重力保持原生：弹道形状由 SO 参数决定（见下方克隆段），prefab 侧只做外观。
            ApplyBoltSprite(boltArrow);
            // 弩矢穿墙件（方案E）：克隆成功即挂——不放进 ApplyBoltSprite 的换皮成功
            // 分支，降级无 sprite 模式同样必须穿墙。池 Spawn 激活 → OnEnable →
            // HeroArcherWallPierce.Apply；池回收 → OnDisable → Restore（先于下一次
            // 复用归还墙碰撞）。实现与低弹道 prefix 同在 CrossbowmanBoltWallPierce.cs。
            CrossbowmanBoltWallPierce.EnsureOn(boltArrow);

            // 3) 克隆 ArrowAttack SO（禁止改原资产——全体弓箭手共享，改了就全弓生效）
            ArrowAttack clonedSO = UnityEngine.Object.Instantiate(baseSO) as ArrowAttack;
            if (clonedSO == null)
            {
                LogCriticalFailure("ArrowAttack SO clone failed; crossbowman disabled");
                UnityEngine.Object.Destroy(boltGo);
                return;
            }
            clonedSO.name = AttackSoName;
            UnityEngine.Object.DontDestroyOnLoad(clonedSO);
            // 弹道（弩矢观感改造定稿）：
            // - 初速 ×2：Range=v²/g → 射程包络 8×4=32（SO 内部 Range=32），索敌仍由
            //   shootRange/扫描器钳在 12——12 步内目标用 32 步的力气打，又平又快；
            //   Archer.cs:1116 推进判断读 SO Range → 12 步内站桩狙击不冒进（旧行为）。
            //   _boosted 同乘保持原生比例。
            // - 出膛点 (0.6,0.7)（方案E，2026-09-21）：平直不再靠出膛点前移避墙——
            //   弩矢穿墙件（CrossbowmanBoltWallPierce，克隆成功即挂）+ BestShotInternal
            //   低弹道强制 prefix 已让墙后场景走低解；出膛点只保留小前移的观感
            //   （快弹+穿墙+低解三管齐下）。
            clonedSO._shotMagnitude *= ShotMagnitudeMultiplier;
            clonedSO._boostedShotMagnitude *= ShotMagnitudeMultiplier;
            clonedSO._arrowOriginOffset = BoltOriginOffset;
            clonedSO._arrowPrefab = boltArrow;

            // 4) 死地动画控制器（可选：解析失败只缺皮肤，弩手功能继续）
            ResolveDeadlandsController();

            // 5) 同步池注册（PoolManager.Init 会清掉运行时池；Init postfix 幂等重注册）
            _crossbowBoltPrefab = boltGo;
            _crossbowAttackSO = clonedSO;
            _assetsReady = true;
            EnsureBoltPoolRegistered();
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman/assets] " + e);
        }
    }

    private static void LogCriticalFailure(string detail)
    {
        if (_criticalFailureLogged) return;
        _criticalFailureLogged = true;
        KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman] " + detail);
    }

    /// <summary>
    /// 克隆 SO（KEM_CrossbowAttack）的 native 指针：BestShotInternal 低弹道强制 prefix
    /// 的唯一资格门（见 CrossbowmanBoltWallPierce）——与 ApplySquadCrossbowPackage 的
    /// SO 判重同源。资产未构建/已销毁 → IntPtr.Zero（永不命中，全部原生 SO 一律走原生
    /// BestShotInternal）。指针读取失败也按未构建处理（fail-closed 到原生行为）。
    /// </summary>
    internal static IntPtr ClonedAttackSoPointer
    {
        get
        {
            try { return _crossbowAttackSO != null ? _crossbowAttackSO.Pointer : IntPtr.Zero; }
            catch (Exception) { return IntPtr.Zero; }
        }
    }

    /// <summary>
    /// 原生 Archer prefab 的 soldierAnimator 快照（随从侧 PatchRoles_KnightStyle 的生根/还原
    /// 共用同一来源；弩手侧走 BuildProfile 的 BaseSoldierAnimator 字段）。EnsureAssets 已构建时
    /// 直接返回缓存；未构建/lazy 首次访问时按 holder["Archer"] 现读一次——holder/prefab 未就绪
    /// → null（调用方跳过还原，下次入口再试），成功捕获后不再重读。
    /// </summary>
    internal static RuntimeAnimatorController BaseSoldierAnimator
    {
        get
        {
            if (_baseSoldierAnimator != null) return _baseSoldierAnimator;
            try
            {
                var managers = Managers.Inst;
                var holder = managers != null ? managers.holder : null;
                if (holder == null || holder.tagCharacterPairs == null) return null;
                Character character = null;
                if (!holder.tagCharacterPairs.TryGetValue("Archer", out character) || character == null)
                    return null;
                Archer prefabArcher = character.GetComponent<Archer>();
                if (prefabArcher == null) return null;
                _baseSoldierAnimator = prefabArcher.soldierAnimator;
            }
            catch (Exception)
            {
                // holder 未就绪/类型未注册等：保持 null，下一次入口再试。
            }
            return _baseSoldierAnimator;
        }
    }

    /// <summary>
    /// 弩矢外观：取原生 Bolt（DamageSource.Bolt，非 Arrow 子类，SO 塞不进去）
    /// 的 SpriteRenderer.sprite 换皮。取不到 LogWarning 并保留原箭外观（降级可用）。
    /// </summary>
    private static void ApplyBoltSprite(Arrow boltArrow)
    {
        try
        {
            var bolts = Resources.LoadAll<Bolt>("");
            if (bolts == null) return;
            for (int i = 0; i < bolts.Length; i++)
            {
                Bolt bolt = bolts[i];
                if (bolt == null || bolt.gameObject == null) continue;
                if (bolt.gameObject.name.IndexOf("Bolt", StringComparison.OrdinalIgnoreCase) < 0) continue;
                SpriteRenderer renderer = bolt.GetComponent<SpriteRenderer>();
                if (renderer == null || renderer.sprite == null) continue;
                SpriteRenderer target = boltArrow.GetComponent<SpriteRenderer>();
                if (target == null) return; // 无外观可换：保留原样
                target.sprite = renderer.sprite;
                // 弩炮弹矢原生 sprite 比箭大：只换皮会渲染成超大箭。
                // The cached template stays neutral across worlds. Only successfully
                // reskinned runtime clones receive the Greek visual compensation.
                if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(CrossbowBoltScaleLifecycle)))
                    ClassInjector.RegisterTypeInIl2Cpp<CrossbowBoltScaleLifecycle>();
                boltArrow.gameObject.AddComponent<CrossbowBoltScaleLifecycle>();
                return;
            }
            if (!_loggedBoltSpriteMissing)
            {
                _loggedBoltSpriteMissing = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                    "[Crossbowman] no native Bolt sprite found; crossbow bolt keeps arrow look (degraded)");
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman/bolt-sprite] " + e);
        }
    }

    /// <summary>
    /// 死地动画控制器解析（先例：PatchEconomy_BankAssistants.TryResolveControllers）：
    /// FindObjectsOfTypeAll 按名匹配 + LoadAll 兜底。解析失败只缺皮肤。
    /// </summary>
    private static void ResolveDeadlandsController()
    {
        if (_deadlandsResolved) return;
        _deadlandsResolved = true;
        try
        {
            RuntimeAnimatorController found = null;
            var all = Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>();
            if (all != null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].name == SoldierControllerName) { found = all[i]; break; }
                }
            }
            if (found == null)
            {
                var loaded = Resources.LoadAll<RuntimeAnimatorController>("");
                if (loaded != null)
                {
                    for (int i = 0; i < loaded.Length; i++)
                    {
                        if (loaded[i] != null && loaded[i].name == SoldierControllerName) { found = loaded[i]; break; }
                    }
                }
            }
            _deadlandsController = found;
            if (found == null)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                    "[Crossbowman] " + SoldierControllerName + " controller not found; crossbowmen keep native skin");
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman/controller] " + e);
        }
    }

    // ============================================================
    // 同步池注册（弩矢独立池必需，否则 Pool.Spawn 报错/联机 desync，AGENTS 坑 11/14）
    // ============================================================

    internal static void EnsureBoltPoolRegistered()
    {
        if (_crossbowBoltPrefab == null) return; // 资产未构建（holder 未就绪/构建失败）：下次 Init/Apply 再试
        try
        {
            var managers = Managers.Inst;
            var pm = managers != null ? managers.pools : null;
            if (pm == null) return;

            Pool existing = Pool.GetPoolFromPrefabAsset(_crossbowBoltPrefab);
            // 幂等：池已存在且已入 syncID 映射（含 Init 重建前的注册）
            if (existing != null && existing.sync && pm.cachedSyncIdPoolPairs != null
                && pm.cachedSyncIdPoolPairs.ContainsKey((int)existing.syncID))
                return;

            short syncId = AllocateSyncId(pm);
            if (syncId < 0)
            {
                if (!_loggedSyncIdExhausted)
                {
                    _loggedSyncIdExhausted = true;
                    KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                        "[Crossbowman] no free syncID in " + SyncIdStart + ".." + SyncIdMax
                        + "; bolt pool NOT registered");
                }
                return;
            }

            if (existing == null)
            {
                DestroyOrphanPools(pm, _crossbowBoltPrefab);
                existing = pm.CreatePoolFor(_crossbowBoltPrefab);
                if (existing == null)
                {
                    KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                        "[Crossbowman] CreatePoolFor failed for " + BoltPrefabName + "; bolt pool NOT registered");
                    return;
                }
            }

            existing.sync = true;
            existing.syncID = syncId;
            existing.preload = 0;
            existing.capacity = 0;
            existing.expendable = false;

            if (pm.cachedPools != null && !pm.cachedPools.Contains(existing)) pm.cachedPools.Add(existing);
            if (pm.cachedNamePoolPairs != null) pm.cachedNamePoolPairs[_crossbowBoltPrefab.name] = existing;
            if (pm.cachedSyncIdPoolPairs != null) pm.cachedSyncIdPoolPairs[syncId] = existing;

            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[Crossbowman] registered synced pool for " + BoltPrefabName + " (syncID=" + syncId + ")");
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman/pool] " + e);
        }
    }

    /// <summary>
    /// 分配 syncID：自持计数器 31000 起，跳过银行助手/幽灵骑士保留段；
    /// 已被其他 prefab 占用时拒绝该 id（绝不覆盖原生池）+ 报错一次，继续找下一个空闲 id。
    /// </summary>
    private static short AllocateSyncId(PoolManager pm)
    {
        for (int guard = 0; guard < 200; guard++)
        {
            int candidate = _nextSyncId;
            _nextSyncId++;
            if (candidate >= BankAssistantSyncIdMin && candidate <= BankAssistantSyncIdMax)
            {
                _nextSyncId = (short)(BankAssistantSyncIdMax + 1);
                continue;
            }
            if (candidate >= GhostSquadSyncIdMin && candidate <= GhostSquadSyncIdMax)
            {
                _nextSyncId = (short)(GhostSquadSyncIdMax + 1);
                continue;
            }
            if (candidate > SyncIdMax) return -1;

            if (pm.cachedSyncIdPoolPairs == null) return (short)candidate;
            if (!pm.cachedSyncIdPoolPairs.ContainsKey(candidate)) return (short)candidate;

            Pool occupant = pm.cachedSyncIdPoolPairs[candidate];
            if (occupant != null && occupant.prefab != _crossbowBoltPrefab && !_loggedSyncIdConflict)
            {
                _loggedSyncIdConflict = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                    "[Crossbowman] syncID " + candidate + " already used by "
                    + (occupant.prefab != null ? occupant.prefab.name : "<null>") + "; allocating next id");
            }
            // 占用者是本 prefab（重建前的旧 id）或冲突已记录：直接前进
        }
        return -1;
    }

    private static void DestroyOrphanPools(PoolManager pm, GameObject prefab)
    {
        Pool[] physical = pm.GetComponentsInChildren<Pool>();
        if (physical == null) return;
        foreach (var p in physical)
        {
            if (p != null && p.prefab == prefab && Pool.GetPoolFromPrefabAsset(prefab) == null)
                UnityEngine.Object.Destroy(p.gameObject);
        }
    }

    // ============================================================
    // D+F. World 协程：15s 读档重算（数量守恒 25%）+ 每 5s 完整性巡检
    // ============================================================

    /// <summary>
    /// 范式同 PatchWorld_DefenseSpacing.SupervisorRoutine：per-world 指针守卫；
    /// world 销毁时协程随宿主自然退出（while 守卫兜底）。
    /// </summary>
    internal static IEnumerator SupervisorRoutine(World world)
    {
        if (world == null || _supervisorWorld == world.Pointer) yield break;
        _supervisorWorld = world.Pointer;

        // 共享扫描缓存（抖动治理）：世界边界整体失效，新世界首轮巡检拿全新扫描。
        UnitScanCache.InvalidateAll();

        // 等单位恢复完成（readback 生成单位 + 原生 promote 流程走完）再重算
        yield return new WaitForSeconds(RecomputeDelaySeconds);
        RecomputeOnLoad();

        // Keep the 5s cadence, but offset its first integrity pass from the
        // 3s DefenseSpacing heartbeat and the KnightStyle phase.
        yield return new WaitForSeconds(2.5f);
        IntegrityPass();

        while (world != null && world.gameObject != null)
        {
            yield return new WaitForSeconds(IntegrityIntervalSeconds);
            IntegrityPass();
        }
    }

    /// <summary>
    /// 读档重算：按场上弓箭手排序每第 4 个重新换皮（弩手数量守恒 25%，皮肤不进存档）。
    /// 骑士小队成员（小队关系随存档恢复）跳过：不进分母、不可被选中；已在队里的弩手
    /// 保持现状，等小队解散后下轮重算收口（Reviewer 裁决——骑士 overrideShootCooldown
    /// 会抹掉弩手射击节奏，与"弩手永远不被骑士招募"矛盾）。
    /// 联机说明：客户端与服务端各自本地重算，客户端选择可能与服务端有外观级分歧；
    /// 伤害/射程判定在权威端，外观分歧已知并接受（设计定稿）。
    /// </summary>
    private static void RecomputeOnLoad()
    {
        try
        {
            // 配置关：不复算、不补回（否则关模组仍会被"重算"回弩手），改走解除路径。
            if (!ModConfig.Enabled.Value)
            {
                CrossbowmanLifecycle.UnwindAll(BuildProfile());
                return;
            }
            // 读档重算常是进程内第一个 marker 接触点（尚未发生任何弓转职）：
            // 循环里的 GetComponent<CrossbowmanMarker> 在类型未注册时会抛，
            // 整个重算被吞——必须在遍历前完成注册。
            CrossbowmanLifecycle.EnsureMarkerRegistered();
            Archer[] archers = UnityEngine.Object.FindObjectsOfType<Archer>();
            if (archers == null) return;

            var list = new System.Collections.Generic.List<Archer>();
            for (int i = 0; i < archers.Length; i++)
            {
                Archer a = archers[i];
                if (a == null || a.gameObject == null || !a.gameObject.activeInHierarchy) continue;
                if (MusketeerIdentity.IsUnit(a)) continue;
                // 骑士小队成员（关系随存档恢复）：跳过——不进 25% 分母、不可被选中；
                // 已在队里的弩手不动，等小队解散后下轮重算收口。
                // HasKnight() 是私有方法不进 interop，等价判 _knight 字段
                // （HasKnight 即 _knight != null，Archer.cs:289-292；私有字段 interop 暴露）。
                if (a._knight != null) continue;
                list.Add(a);
            }
            list.Sort((x, y) => x.GetInstanceID().CompareTo(y.GetInstanceID()));

            int crossbowmen = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (i % PromoteCycle == PromoteCycle - 1)
                {
                    Apply(list[i]);
                    crossbowmen++;
                }
                else if (list[i].GetComponent<CrossbowmanMarker>() != null)
                {
                    // 非弩手分位：清掉旧身份/残余参数（无身份无残留时零写入）。
                    // 组件存在即调用——身份无效也可能带着未完成还原（Residue）。
                    Strip(list[i]);
                }
            }

            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[Crossbowman] recompute on load: total=" + list.Count + " crossbowmen=" + crossbowmen);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman/recompute] " + e);
        }
    }

    /// <summary>
    /// 完整性巡检（5s 一拍，兜住一切重置路径：池 respawn/OnEnable/换皮被池路径重置）。
    /// 逐 marker 的判定与写入都在 CrossbowmanLifecycle.Reconcile：有效身份只兜原生
    /// 重置的字段（火矢 buff 期间绝不动箭、不碰射击间隔；塔位射程归 CrossbowDefense），
    /// 配置关或有未完成写入（Residue）则 Strip 收尾/解除——失效 marker 不会得到
    /// 巡检强化；停用/池中对象既不强化也不清理。本方法只做扫描缓存与批次交接。
    /// </summary>
    private static void IntegrityPass()
    {
        try
        {
            // 配置关：不强化、只解除（registry 含 inactive；不依赖扫描缓存与 5s 窗口）。
            if (!ModConfig.Enabled.Value)
            {
                CrossbowmanLifecycle.UnwindAll(BuildProfile());
                return;
            }
            // 防御：FindObjectsOfType 要求类型已注册，未注册时抛异常（每 5s 日志刷屏）
            CrossbowmanLifecycle.EnsureMarkerRegistered();
            // 共享缓存，抖动治理：marker 扫描走 UnitScanCache（5s 窗口=原巡检节奏；
            // 新弩手的 marker+战斗包在 OnBowPromoted/Apply 即时挂好，本巡检只兜底，
            // 5s 缓存新鲜度等价于原 5s 节奏）。
            // 注：RecomputeOnLoad 自己的 Archer 全量扫有意保持直扫——25% 数量
            // 守恒重算依赖精确的当下快照，不吃缓存新鲜度。
            CrossbowmanMarker[] markers = UnitScanCache.GetCrossbowmanMarkers();
            if (markers == null) return;
            CrossbowmanLifecycle.ReconcileScan(markers, BuildProfile());
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman/integrity] " + e);
        }
    }

    // ============================================================
    // G. 骑士招募排除
    // ============================================================

    /// <summary>
    /// 宿主转发（PatchRoles_CrossbowDefense / KnightStyle / NorseSquad /
    /// DefenseSpacing / SquadRefillDiag 的既有调用点保持不变）。真实判据在
    /// <see cref="CrossbowmanLifecycle.IsCrossbowman"/>：marker 组件存在不算，
    /// 必须带有效身份（Active）。
    /// </summary>
    internal static bool IsCrossbowman(Archer archer) => CrossbowmanLifecycle.IsCrossbowman(archer);

    internal static void LogKnightExclusionOnce()
    {
        if (_loggedKnightExclusion) return;
        _loggedKnightExclusion = true;
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[Crossbowman] excluded from knight recruitment");
    }

    // ============================================================
    // I. life 边界钩子（Pool.FastSpawn 作用域 / Archer.OnEnable / Archer.OnDisable）
    //    与面板 Tick。全部经 CrossbowmanLifecycle 的同一状态机，无新扫描、无逐帧遍历。
    // ============================================================

    /// <summary>Pool.FastSpawn prefix：进入池生成作用域（供 OnEnable 判定"真池激活"）。</summary>
    internal static void BeginPoolSpawnScope() => CrossbowmanLifecycle.BeginPoolSpawnScope();

    /// <summary>Pool.FastSpawn finalizer：退出作用域（异常路径也会执行）。</summary>
    internal static void EndPoolSpawnScope() => CrossbowmanLifecycle.EndPoolSpawnScope();

    /// <summary>
    /// Archer.OnEnable prefix：池作用域内=新 life，**在原生主体之前**用 profile 清掉旧 life 的
    /// owned 包（原生 AddArcher/DistributeFreeArchers 随后可能给这个对象分配骑士随从并写新的
    /// 战斗包/皮肤/缩放，晚一步清就会覆盖它们）；作用域外=普通隐藏重开，按本 life 选择恢复 Active。
    /// </summary>
    internal static void OnArcherEnablePrefix(Archer archer)
    {
        if (!ModConfig.Enabled.Value) return; // 配置关：读者已即时失效，交由 Tick/巡检解除
        CrossbowmanLifecycle.OnArcherEnablePrefix(archer, BuildProfile());
    }

    /// <summary>Archer.OnEnable postfix：原生重置后收尾（配置关解除 / 残留还原 / 有效身份自愈）。</summary>
    internal static void OnArcherEnablePostfix(Archer archer)
        => CrossbowmanLifecycle.OnArcherEnablePostfix(archer, BuildProfile());

    /// <summary>Archer.OnDisable prefix：身份立即失效、保留本 life 选择（隐藏≠池归还）。</summary>
    internal static void OnArcherDisablePrefix(Archer archer)
    {
        if (!ModConfig.Enabled.Value) return;
        CrossbowmanLifecycle.OnArcherDisablePrefix(archer);
    }

    /// <summary>
    /// Archer.ConvertToHunter postfix：身份仍有效的**单写事件纠正**（退役每帧皮肤守卫的
    /// 替代）——原生下塔/下船把控制器刷成猎人皮时，同一调用栈内写回死地控制器并重写生根
    /// 字段；死亡/退队（Active 已失效）不碰，猎人皮播死亡动画的既有设计保留。
    /// </summary>
    internal static void OnConvertToHunterPostfix(Archer archer)
        => CrossbowmanLifecycle.OnConvertToHunterPostfix(archer, BuildProfile());

    /// <summary>
    /// 面板 Tick（root 接线：`ModPanel.Update()` 里与其它 `X.Tick()` 同列）。
    /// 常态 O(1)：配置开或 registry 为空直接返回；全局关闭时遍历**自有 registry（含
    /// inactive 池中实例）**立即还原解除，不等 5s 巡检窗口。唯一新增的工作入口，
    /// 不做逐帧全场扫描。
    /// </summary>
    internal static void Tick()
    {
        try
        {
            if (ModConfig.Enabled.Value || !CrossbowmanLifecycle.HasPendingWork) return;
            CrossbowmanLifecycle.UnwindAll(BuildProfile());
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman/tick] " + e);
        }
    }

    // ============================================================
    // H. 死地骑士随从"无标记弩手化"轻量包（knight-style-026 联动，消费方
    //    PatchRoles_KnightStyle.ApplyFollowerSkinTo）
    // ============================================================

    private static bool _loggedSquadPackageAborted;

    /// <summary>
    /// 死地风格骑士的随从专用"无标记弩手化"战斗包：弩矢/伤害/射程/间隔/体型与
    /// 弩手一致（ActiveArrowAttack=克隆 SO KEM_CrossbowAttack、shootRange=12+
    /// 扫描器 12、间隔 ×2、y=1.15），但绝不挂 CrossbowmanMarker——标记语义=
    /// 拒绝骑士招募，而这些随从就是骑士队员；弩手本体永不入队
    /// （IsAvailableForJob 排除），两个群体不相交，无冲突。不复用 Apply()
    /// 的 marker/旗帜染色完整路径，只动战斗数值与缩放。
    /// 幂等：ActiveArrowAttack 已是克隆 SO → 只补缩放；间隔仅首次 ×2
    /// （SO 指针判重防 ×4/×8 叠加，同 Apply 的 already 判据）。
    /// </summary>
    internal static void ApplySquadCrossbowPackage(Archer archer)
    {
        if (archer == null || archer.gameObject == null) return;
        try
        {
            // A Greek follower may change squads while its native fire buff is still active.
            // Defer the crossbow package until expiry, preserving fire arrows and avoiding a
            // premature interval multiplier when the active attack is the temporary fire SO.
            if ((archer._buffable != null && archer._buffable.IsBuffActive(BuffType.FireAttacks))
                || (archer.ActiveArrowAttack != null && archer._fireArrowAttack != null
                    && archer.ActiveArrowAttack.Pointer == archer._fireArrowAttack.Pointer))
                return;
            EnsureAssets();
            if (_crossbowAttackSO == null)
            {
                if (!_loggedSquadPackageAborted)
                {
                    _loggedSquadPackageAborted = true;
                    KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                        "[Crossbowman] squad crossbow package skipped: cloned ArrowAttack missing");
                }
                return;
            }

            bool already = archer.ActiveArrowAttack != null
                && archer.ActiveArrowAttack.Pointer == _crossbowAttackSO.Pointer;
            if (!already)
            {
                archer.ActiveArrowAttack = _crossbowAttackSO;
                archer.shootRange = CrossbowmanLifecycle.ShootRange;
                Scanner scanner = archer._enemyScanner;
                if (scanner != null)
                {
                    scanner.range = CrossbowmanLifecycle.ShootRange;
                    scanner.rangeBehind = CrossbowmanLifecycle.ShootRange;
                }
                // 读现值乘（不读缓存）：buff 可能已改过冷却；重入由 SO 指针判重挡住
                Vector2 interval = archer._shootIntervalRange;
                interval.x *= CrossbowmanLifecycle.IntervalMultiplier;
                interval.y *= CrossbowmanLifecycle.IntervalMultiplier;
                archer._shootIntervalRange = interval;
                Vector2 intervalFormation = archer._shootIntervalRangeFormation;
                intervalFormation.x *= CrossbowmanLifecycle.IntervalMultiplier;
                intervalFormation.y *= CrossbowmanLifecycle.IntervalMultiplier;
                archer._shootIntervalRangeFormation = intervalFormation;
            }

            // 体型 1.15（坑11：只动 y）+ ScaleRegistry 每帧守卫；Restore 必须
            // Unregister，否则池复用给普通弓箭手时被错误守卫在 1.15
            GreekScaleScope.ApplyY(archer.transform, CrossbowmanLifecycle.ScaleY);
            ScaleRegistryHolder.Register(archer.GetComponent<Mover>(), CrossbowmanLifecycle.ScaleY);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman/squad-apply] " + e);
        }
    }

    /// <summary>
    /// 撤随从弩手化包（幂等 no-op）：仅当 ActiveArrowAttack 指向克隆 SO 才动
    /// （否则说明不是我们写的包/SO 未构建，直接返回）。恢复原生箭/射程/扫描器/
    /// 间隔（基值缓存来自 EnsureAssets 的 Archer prefab 读取），注销缩放守卫并
    /// 回 y=1。塔位恢复分支与 Strip 同款（防御：随从理论上不上塔，但按所在
    /// 位置还原无害）。
    /// </summary>
    internal static void RestoreSquadCrossbowPackage(Archer archer)
    {
        if (archer == null || archer.gameObject == null) return;
        try
        {
            if (_crossbowAttackSO == null
                || archer.ActiveArrowAttack == null
                || archer.ActiveArrowAttack.Pointer != _crossbowAttackSO.Pointer)
                return;

            archer.ActiveArrowAttack = archer._arrowAttack;
            if (_baseShootRangeCached)
            {
                archer.shootRange = _baseShootRange;
                Scanner scanner = archer._enemyScanner;
                if (scanner != null)
                {
                    float restoreRange = archer.inGuardSlot ? archer.towerShootRange : _baseShootRange;
                    scanner.range = restoreRange;
                    scanner.rangeBehind = restoreRange;
                }
            }
            if (_baseIntervalCached) archer._shootIntervalRange = _baseInterval;
            if (_baseIntervalFormationCached) archer._shootIntervalRangeFormation = _baseIntervalFormation;

            ScaleRegistryHolder.Unregister(archer.GetComponent<Mover>());
            GreekScaleScope.Restore(archer.transform);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman/squad-restore] " + e);
        }
    }
}

/// <summary>Local pooled visual lifecycle; attached only to our reskinned arrow clone.</summary>
public sealed class CrossbowBoltScaleLifecycle : MonoBehaviour
{
    public CrossbowBoltScaleLifecycle(IntPtr pointer) : base(pointer) { }

    private void OnEnable()
    {
        GreekScaleScope.ApplyScale(transform,
            GreekScaleScope.NativeScale(transform) * PatchRoles_Crossbowman.BoltVisualScale);
    }

    private void OnDisable()
    {
        GreekScaleScope.Restore(transform);
    }
}

/// <summary>
/// B. 转职交替主入口：居民捡弓成功转职（Character.Promote → ReplaceBy → Pool.Spawn）
/// 后，每第 4 个变成弩手。先例：PatchRoles_Worker/Berserker 同签名挂钩。
/// </summary>
[HarmonyPatch(typeof(Character), nameof(Character.Promote), new[] { typeof(DroppableTool), typeof(IUnitController) })]
public static class Character_Promote_CrossbowmanAlternation_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Character __result, DroppableTool tool)
    {
        if (!ModConfig.Enabled.Value) return;
        // 非弓工具零开销早退（不碰 try）
        if (tool == null || tool.tag != "Bow") return;
        if (MusketeerIdentity.GunPromotionInProgress || MusketeerIdentity.IsGun(tool)) return;
        try
        {
            PatchRoles_Crossbowman.OnBowPromoted(__result);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman/promote] " + e);
        }
    }
}

/// <summary>
/// G. 骑士招募排除：带 marker 的弩手拒绝 knight.gameObject 作为 job 的招募
/// （Kingdom.FetchArchersForJob 逐个调 IsAvailableForJob；knight 随从 AssignJob
/// 的 jobObject 正是 knight.gameObject，GuardSlot/塔位入口无 Knight 组件不受影响）。
/// </summary>
[HarmonyPatch(typeof(Archer), nameof(Archer.IsAvailableForJob))]
public static class Archer_IsAvailableForJob_CrossbowmanExclusion_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Archer __instance, GameObject jobObject, ref bool __result)
    {
        if (!ModConfig.Enabled.Value || !__result) return;
        try
        {
            if (__instance == null || jobObject == null) return;
            if (!PatchRoles_Crossbowman.IsCrossbowman(__instance)) return;
            if (jobObject.GetComponent<Knight>() == null) return;
            __result = false;
            PatchRoles_Crossbowman.LogKnightExclusionOnce();
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman/knight-exclusion] " + e);
        }
    }
}

/// <summary>
/// D+F. World 协程宿主（范式同 PatchWorld_DefenseSpacing）。
/// </summary>
[HarmonyPatch(typeof(World), nameof(World.OnLevelLoaded))]
public static class World_OnLevelLoaded_CrossbowmanSupervisorHost_Patch
{
    [HarmonyPostfix]
    private static void Postfix(World __instance)
    {
        if (!ModConfig.Enabled.Value || __instance == null) return;
        try
        {
            __instance.StartCoroutine(
                PatchRoles_Crossbowman.SupervisorRoutine(__instance).WrapToIl2Cpp());
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman] supervisor start failed: " + e);
        }
    }
}

/// <summary>
/// 弩矢同步池重注册：PoolManager.Init 会清掉运行时注册的池（PatchPoolFix 已证），
/// 每次 Init 后幂等重注册（资产未构建时静默跳过，由 Apply 构建路径补注册）。
/// </summary>
[HarmonyPatch(typeof(PoolManager), nameof(PoolManager.Init))]
public static class PoolManager_Init_CrossbowBoltPool_Patch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            PatchRoles_Crossbowman.EnsureBoltPoolRegistered();
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman/pool] " + e);
        }
    }
}

/// <summary>
/// I-a. 池生成作用域：Pool.FastSpawn（native 唯一，0x6c10a0）prefix 进入、finalizer 退出
/// （异常路径也退出）。作用域本身**不**清职业：native 的 syncReceipt 命中 `_activeCache`
/// 同 NetID 时会直接返回已 active 对象、不触发 OnEnable，若仅凭"返回值"或"进入作用域"判定
/// 新 life 就会误清重复回执对象的职业。真实新 life 由作用域内发生的 `Archer.OnEnable` 确认。
/// 成本：仅一次静态深度自增/自减，不做 GetComponent、不扫描。
/// </summary>
[HarmonyPatch(typeof(Pool), nameof(Pool.FastSpawn), new[]
{
    typeof(Vector3), typeof(Quaternion), typeof(Transform), typeof(short), typeof(bool)
})]
public static class Pool_FastSpawn_CrossbowmanLifecycleScope_Patch
{
    [HarmonyPrefix]
    private static void Prefix() => PatchRoles_Crossbowman.BeginPoolSpawnScope();

    [HarmonyFinalizer]
    private static void Finalizer() => PatchRoles_Crossbowman.EndPoolSpawnScope();
}

/// <summary>
/// I-b. Archer.OnEnable prefix/postfix（native 0x4b32d0）：
/// - prefix：池作用域内 = 真池激活 → 在原生 `AddArcher`/`DistributeFreeArchers` 做招募判定
///   之前清掉旧选择；作用域外 = 普通隐藏重开 → 按本 life 选择恢复有效身份；
/// - postfix：原生重置之后收尾（配置关解除 / 残留还原 / 有效身份自愈）。
/// </summary>
[HarmonyPatch(typeof(Archer), "OnEnable")]
public static class Archer_OnEnable_CrossbowmanLifecycle_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Archer __instance) => PatchRoles_Crossbowman.OnArcherEnablePrefix(__instance);

    [HarmonyPostfix]
    private static void Postfix(Archer __instance) => PatchRoles_Crossbowman.OnArcherEnablePostfix(__instance);
}

/// <summary>
/// I-c. Archer.OnDisable prefix（native 0x4b2c10）：身份立即失效，保留本 life 选择。
/// （原生 OnDisable 会把 `_arrowAttack` 写回 ActiveArrowAttack、ConvertToHunter，但不恢复
/// shootRange/interval，且非权威/非主场景会提前 return——所以失效必须由我们自己在
/// prefix 完成，不能依赖原生主体执行。）
/// </summary>
[HarmonyPatch(typeof(Archer), "OnDisable")]
public static class Archer_OnDisable_CrossbowmanLifecycle_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Archer __instance) => PatchRoles_Crossbowman.OnArcherDisablePrefix(__instance);
}

/// <summary>
/// I-d. Archer.ConvertToHunter postfix（下塔/下船/离队/死亡清理）：退役每帧皮肤守卫后的
/// 单写事件纠正——身份仍有效 → 控制器与生根字段各写回死地控制器一次（指针相等零写入）；
/// 死亡/退队（OnDisable prefix 已失效 Active）→ 不碰，猎人皮播死亡动画的既有设计保留。
/// ConvertToSoldier 方向不需要钩子：生根字段让原生解析出的就是死地控制器（同引用重赋无害）。
/// </summary>
[HarmonyPatch(typeof(Archer), "ConvertToHunter")]
public static class Archer_ConvertToHunter_CrossbowmanSkin_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Archer __instance)
    {
        if (!ModConfig.Enabled.Value || __instance == null) return;
        try
        {
            PatchRoles_Crossbowman.OnConvertToHunterPostfix(__instance);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman/convert-hunter] " + e);
        }
    }
}
