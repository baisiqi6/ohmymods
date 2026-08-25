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
/// 弩矢观感（用户实锤"与普通弓箭手无区别"后的改造定稿）：
/// - 平直快弹：初速 ×2（射程包络 32，索敌钳在 12 → 12 步内用 32 步的力气打）；
/// - 出膛点前移 (2.5,1.0)：此前"平直弹道失败"的真凶=墙后弩手的 ParabolaCast
///   （ArrowAttack.cs:134 低弹道解门槛）被自家墙挡 → 原生主动选高抛解；前移后
///   出膛点≈墙沿，原生选低弹道解 → 真正平直；
/// - 常显 0.25s 光痕拖尾（_alwaysDrawTrail + _notPerfectTrailLength）+ 0.85 醒目体型，
///   与普通箭一眼区分。
///
/// 士兵皮肤与猎人行为不冲突（原生 Archer 本就在两套控制器间来回转：EnterGuardSlot/
/// OnEmbarkStart→ConvertToSoldier，离队/下塔→ConvertToHunter；行为由 _knight==null
/// 的猎人例程驱动，控制器只管外观；打猎用的 idle/walk/run/shoot 士兵动画集齐全）。
/// 原生在塔/船场景会把控制器换成当前世界的士兵皮肤，巡检 5s 内换回死地士兵；
/// 死亡清理切回猎人皮肤播死亡动画（纯观感差异，接受）。
///
/// 与原生契约：
/// - ActiveArrowAttack 可写；原生 Awake/OnEnable/火矢 buff/网络收包都会重置它——
///   完整性巡检只兜"等于原生 _arrowAttack"的实例，火矢 buff（_fireArrowAttack）期间绝不动。
/// - shootRange/扫描器只在 Apply 时设置一次；塔位切换由原生管理（towerShootRange），
///   巡检不碰扫描器。
/// - 射击间隔（_shootIntervalRange/_shootIntervalRangeFormation）只在 Apply 时按现值 ×2，
///   巡检不检查（buff/阵形可能合法修改它们）。
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
/// </summary>
public static class PatchRoles_Crossbowman
{
    // ---- 数值定稿（Operator 裁决，勿改） ----
    private const float CrossbowShootRange = 12f;          // 基础弓 8
    internal const float CrossbowmanScaleY = 1.15f;         // 本体 y 缩放（坑11：只动 y，x 是朝向符号）
    private const float IntervalMultiplier = 2f;           // 装填冷却 ×2
    private const int BoltHitDamage = 2;                   // 原生 1；perfect 自动 ×2 = 4
    private const float RangeMultiplier = 1.5f;            // 射程 ×1.5（8→12）；索敌钳制用（shootRange/扫描器）
    // 初速 ×2（弩矢观感改造）：Range=v²/g → 射程包络=8×4=32，但索敌仍由 shootRange/
    // 扫描器钳在 12——12 步内目标用 32 步的力气打，又平又快。Archer.cs:1116 的
    // 推进判断读 SO Range=32 → 弩手 12 步内站桩狙击不冒进（用户早已接受的旧行为）
    private const float ShotMagnitudeMultiplier = 2f;
    private const float BoltVisualScale = 0.85f;           // 弩矢醒目化（原 0.65 缩小观感弱）；连带碰撞体等比缩放，快弹判定影响可忽略
    // 出膛点前移（弩矢观感改造核心）：原生 _arrowOriginOffset 默认 (0.15,0.5)，
    // 弩手在墙后射击时 ParabolaCast 从出膛点出发被自家墙挡 → BestShotInternal
    // （ArrowAttack.cs:134）被迫选高抛解——这是此前"平直弹道失败"的真凶。
    // 前移到前方 2.5 步（墙后单位≈墙沿）后 ParabolaCast 不再被自家墙挡，
    // 原生选低弹道解 → 真正平直。ArrowAttack.FireArrow（ArrowAttack.cs:60）按
    // 目标方向符号侧移 x，正值=朝目标前方。
    private static readonly Vector2 BoltOriginOffset = new Vector2(2.5f, 1.0f);
    // 常显拖尾长度（秒）：Arrow.EnableTrail（Arrow.cs:67）在 _alwaysDrawTrail 且
    // 非 perfect 时用 _notPerfectTrailLength（原生默认 0.1，火矢用长尾）——0.25s
    // 光痕拖尾让弩矢与普通箭一眼区分
    private const float BoltTrailLength = 0.25f;
    private const int PromoteCycle = 4;                    // 3:1 交替
    private const float RecomputeDelaySeconds = 15f;       // 等单位恢复完成
    private const float IntegrityIntervalSeconds = 5f;

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
    private static bool _markerRegistered;      // CrossbowmanMarker 的 ClassInjector 注册完成标记

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
            // 先于清污检查里的 GetComponent<CrossbowmanMarker>()（未注册即抛异常）
            EnsureMarkerRegistered();

            // 池复用清污：带皮肤/参数的旧弩手实例被池发给普通弓箭手时先恢复原生。
            // DestroyImmediate（而非 Destroy）：清污后本帧可能立即 Apply 重新
            // AddComponent，延迟销毁会让 GetComponent 继续命中旧 marker，导致
            // 新弩手实例丢失标记（骑士排除/巡检随之失效）。
            if (archer.GetComponent<CrossbowmanMarker>() != null) Strip(archer);

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
    // C. Apply：弩手打包（幂等）
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

        try
        {
            // 类型注册必须先于任何 GetComponent<CrossbowmanMarker>()——未注册时
            // GetComponent/AddComponent 都会抛异常被吞，marker 永不存在（MUST-FIX #1）。
            EnsureMarkerRegistered();

            // 幂等标记：RecomputeOnLoad 会对已是弩手（带 marker）的单位重复 Apply。
            // 间隔是"读现值×2"，重复 Apply 会让冷却 ×4、×8 无限膨胀——只在首次
            // （!already）乘系数；其余字段均为绝对赋值，重复执行天然幂等。
            // OnBowPromoted 的 Strip→Apply 路径：Strip 已恢复基础值 → already=false，
            // 仍然 ×2，天然正确。
            bool already = archer.GetComponent<CrossbowmanMarker>() != null;
            if (!already)
                archer.gameObject.AddComponent<CrossbowmanMarker>();

            archer.ActiveArrowAttack = _crossbowAttackSO;
            archer.shootRange = CrossbowShootRange;
            Scanner scanner = archer._enemyScanner;
            if (scanner != null)
            {
                scanner.range = CrossbowShootRange;
                scanner.rangeBehind = CrossbowShootRange;
            }

            if (!already)
            {
                // 读现值乘（不读缓存）：buff 可能已改过冷却
                Vector2 interval = archer._shootIntervalRange;
                interval.x *= IntervalMultiplier;
                interval.y *= IntervalMultiplier;
                archer._shootIntervalRange = interval;
                Vector2 intervalFormation = archer._shootIntervalRangeFormation;
                intervalFormation.x *= IntervalMultiplier;
                intervalFormation.y *= IntervalMultiplier;
                archer._shootIntervalRangeFormation = intervalFormation;
            }

            if (_deadlandsController != null)
            {
                Animator animator = archer.GetComponentInChildren<Animator>();
                if (animator != null && animator.runtimeAnimatorController != null)
                    animator.runtimeAnimatorController = _deadlandsController;
            }

            // 士兵皮肤的第二半：王国旗帜色染衣（骑士随从同款辨识度）
            ApplyBannerColors(archer);

            // 本体放大 1.15：y 轴绝对值 + ScaleRegistry 每帧守卫（Mover.Update postfix
            // 重断言，池 respawn/原生重置都能自愈）；Strip 必须 Unregister，否则池复用
            // 给普通弓箭手时会被错误守卫在 1.15（注册按 gameObject ID 键控）。
            Vector3 scale = archer.transform.localScale;
            scale.y = CrossbowmanScaleY;
            archer.transform.localScale = scale;
            ScaleRegistryHolder.Register(archer.GetComponent<Mover>(), CrossbowmanScaleY);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman/apply] " + e);
        }
    }

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
    // E. Strip：清污，恢复原生（对象池 respawn 不重拷序列化字段，必须显式恢复）
    // ============================================================

    private static void Strip(Archer archer)
    {
        if (archer == null || archer.gameObject == null) return;
        try
        {
            EnsureMarkerRegistered(); // 防御：GetComponent<CrossbowmanMarker> 要求类型已注册
            CrossbowmanMarker marker = archer.GetComponent<CrossbowmanMarker>();
            if (marker != null) UnityEngine.Object.DestroyImmediate(marker);

            // 恢复该实例的原生箭（直接读实例私有字段，不读缓存）
            archer.ActiveArrowAttack = archer._arrowAttack;

            if (_baseIntervalCached) archer._shootIntervalRange = _baseInterval;
            if (_baseIntervalFormationCached) archer._shootIntervalRangeFormation = _baseIntervalFormation;
            if (_baseShootRangeCached)
            {
                archer.shootRange = _baseShootRange;
                Scanner scanner = archer._enemyScanner;
                if (scanner != null)
                {
                    // 塔位 guard slot 上的扫描器由原生设为 towerShootRange
                    // （Archer.cs:848 EnterGuardSlot；1390 ExitGuardSlot 恢复 shootRange），
                    // 清污时按所在位置还原，否则塔上被恢复的弩手索敌范围被压回地面值。
                    float restoreRange = archer.inGuardSlot ? archer.towerShootRange : _baseShootRange;
                    scanner.range = restoreRange;
                    scanner.rangeBehind = restoreRange;
                }
            }

            // 恢复猎人控制器：走原生 ConvertToHunter 同款 biome swap（Archer.cs:889），
            // 跨世界也能还原对应世界的猎人皮肤；swap 不可用时回落缓存基座控制器。
            RuntimeAnimatorController hunter = null;
            try
            {
                hunter = archer.hunterAnimator != null && BiomeData.Current != null
                    ? BiomeData.Current.GetAssetSwapForThis<RuntimeAnimatorController>(archer.hunterAnimator)
                    : null;
            }
            catch (Exception) { /* swap 表未就绪等：走缓存回落 */ }
            Animator stripAnimator = archer.GetComponentInChildren<Animator>();
            if (stripAnimator != null && stripAnimator.runtimeAnimatorController != null)
            {
                if (hunter != null) stripAnimator.runtimeAnimatorController = hunter;
                else if (_baseAnimatorController != null)
                    stripAnimator.runtimeAnimatorController = _baseAnimatorController;
            }

            // 衣服颜色不还原：原生路径会自然重掷（Promote 换装继承来源颜色、
            // ConvertToHunter 重随机），手动复刻反而要拷贝 _useOutfitGradient 分支。
            archer._isWearingBannerColor = false;

            // 缩放还原：先撤守卫再回基准 y=1（原生弓箭手即 1），顺序反了会被守卫顶回。
            ScaleRegistryHolder.Unregister(archer.GetComponent<Mover>());
            Vector3 stripScale = archer.transform.localScale;
            stripScale.y = 1f;
            archer.transform.localScale = stripScale;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman/strip] " + e);
        }
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
            // - 出膛点前移 (2.5,1.0)：原生默认 (0.15,0.5) 时墙后弩手的 ParabolaCast
            //   （ArrowAttack.cs:134，BestShotInternal 低弹道解的门槛）被自家墙挡
            //   → 原生被迫选高抛解——此前"平直弹道失败"的真凶。前移后出膛点≈墙沿，
            //   不被自家墙挡 → 原生选低弹道解 → 真正平直（快弹+前移双管齐下）。
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

    /// <summary>
    /// CrossbowmanMarker 显式 ClassInjector 注册（先例 SpecialTowerRebuild.EnsureMarkerRegistered，
    /// 本仓库 9/9 自定义 MonoBehaviour 全部显式注册）。不注册则 AddComponent/GetComponent/
    /// FindObjectsOfType 抛异常被吞，marker 永不存在，骑士排除/巡检/清污全链失效。
    /// </summary>
    private static void EnsureMarkerRegistered()
    {
        if (_markerRegistered) return;
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(CrossbowmanMarker)))
        {
            ClassInjector.RegisterTypeInIl2Cpp(typeof(CrossbowmanMarker));
        }
        _markerRegistered = true;
    }

    private static void LogCriticalFailure(string detail)
    {
        if (_criticalFailureLogged) return;
        _criticalFailureLogged = true;
        KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman] " + detail);
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
                // 整体等比缩小（碰撞体连带缩小，快弹判定影响可忽略）；
                // 换皮失败的降级路径不缩放（保持原箭比例）。
                boltArrow.transform.localScale *= BoltVisualScale;
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

        // 等单位恢复完成（readback 生成单位 + 原生 promote 流程走完）再重算
        yield return new WaitForSeconds(RecomputeDelaySeconds);
        RecomputeOnLoad();

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
            // 读档重算常是进程内第一个 marker 接触点（尚未发生任何弓转职）：
            // 循环里的 GetComponent<CrossbowmanMarker> 在类型未注册时会抛，
            // 整个重算被吞——必须在遍历前完成注册。
            EnsureMarkerRegistered();
            Archer[] archers = UnityEngine.Object.FindObjectsOfType<Archer>();
            if (archers == null) return;

            var list = new System.Collections.Generic.List<Archer>();
            for (int i = 0; i < archers.Length; i++)
            {
                Archer a = archers[i];
                if (a == null || a.gameObject == null || !a.gameObject.activeInHierarchy) continue;
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
    /// 完整性巡检：兜住一切重置路径（池 respawn/OnEnable/换皮被池路径重置）。
    /// 只碰 ActiveArrowAttack（等于原生基础值才修复）、shootRange、Animator；
    /// 不碰扫描器/射击间隔（原生与 buff/阵形可能合法持有）。
    /// </summary>
    private static void IntegrityPass()
    {
        try
        {
            // 防御：FindObjectsOfType 也要求类型已注册，未注册时抛异常（每 5s 日志刷屏）
            EnsureMarkerRegistered();
            CrossbowmanMarker[] markers = UnityEngine.Object.FindObjectsOfType<CrossbowmanMarker>();
            if (markers == null) return;

            for (int i = 0; i < markers.Length; i++)
            {
                CrossbowmanMarker marker = markers[i];
                if (marker == null) continue;
                Archer archer = marker.GetComponent<Archer>();
                if (archer == null || archer.gameObject == null)
                {
                    UnityEngine.Object.Destroy(marker);
                    continue;
                }

                if (_crossbowAttackSO != null)
                {
                    ArrowAttack current = archer.ActiveArrowAttack;
                    ArrowAttack native = archer._arrowAttack;
                    // 仅当被重置回原生基础箭（OnEnable/Awake/网络收包路径）时修复；
                    // 火矢 buff（_fireArrowAttack）期间绝不动——与原生 buff 的兼容契约。
                    if (current != null && native != null && current.Pointer == native.Pointer)
                        archer.ActiveArrowAttack = _crossbowAttackSO;
                }

                if (archer.shootRange != CrossbowShootRange)
                    archer.shootRange = CrossbowShootRange;

                if (_deadlandsController != null)
                {
                    Animator animator = archer.GetComponentInChildren<Animator>();
                    if (animator != null && animator.runtimeAnimatorController != null
                        && animator.runtimeAnimatorController.Pointer != _deadlandsController.Pointer)
                    {
                        animator.runtimeAnimatorController = _deadlandsController;
                    }
                }

                // 原生 ConvertToHunter（下塔/下船/离队/死亡清理）会重掷随机衣色并清
                // _isWearingBannerColor；标记被清说明衣色丢了，补染回旗帜色（幂等）。
                ApplyBannerColors(archer);

                // 缩放漂移诊断（用户报告"地面弩手有的高有的低"）：只统计不改——
                // 守卫每帧都在断言仍有漂移，说明存在更晚的写入者（怀疑动画器
                // scale 曲线，其在 Mover.Update 之后评估）。记录首个样本的动画器
                // 位置与当前 y，用于定位真正写入者。
                if (Mathf.Abs(archer.transform.localScale.y - CrossbowmanScaleY) > 0.02f)
                {
                    _scaleDriftCount++;
                    if (_scaleDriftSample == null)
                    {
                        Animator a = archer.GetComponentInChildren<Animator>();
                        _scaleDriftSample = "y=" + archer.transform.localScale.y.ToString("F3")
                            + " animatorOnRoot=" + (a != null && a.transform == archer.transform)
                            + " controller="
                            + (a != null && a.runtimeAnimatorController != null
                                ? a.runtimeAnimatorController.name : "<null>");
                    }
                }
            }

            if (_scaleDriftCount > 0 && _loggedScaleDrift != _scaleDriftCount)
            {
                _loggedScaleDrift = _scaleDriftCount;
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                    "[Crossbowman] scale drift: marked=" + markers.Length
                    + " drifted=" + _scaleDriftCount
                    + " sample[" + (_scaleDriftSample ?? "<none>") + "]");
            }
            _scaleDriftCount = 0;
            _scaleDriftSample = null;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman/integrity] " + e);
        }
    }

    private static int _scaleDriftCount;
    private static int _loggedScaleDrift = -1;
    private static string _scaleDriftSample;

    // ============================================================
    // G. 骑士招募排除
    // ============================================================

    internal static bool IsCrossbowman(Archer archer)
    {
        // 防御：GetComponent 要求类型已注册，未注册时抛异常（IsAvailableForJob 每调用一次刷一次）
        EnsureMarkerRegistered();
        return archer != null && archer.GetComponent<CrossbowmanMarker>() != null;
    }

    internal static void LogKnightExclusionOnce()
    {
        if (_loggedKnightExclusion) return;
        _loggedKnightExclusion = true;
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[Crossbowman] excluded from knight recruitment");
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
                archer.shootRange = CrossbowShootRange;
                Scanner scanner = archer._enemyScanner;
                if (scanner != null)
                {
                    scanner.range = CrossbowShootRange;
                    scanner.rangeBehind = CrossbowShootRange;
                }
                // 读现值乘（不读缓存）：buff 可能已改过冷却；重入由 SO 指针判重挡住
                Vector2 interval = archer._shootIntervalRange;
                interval.x *= IntervalMultiplier;
                interval.y *= IntervalMultiplier;
                archer._shootIntervalRange = interval;
                Vector2 intervalFormation = archer._shootIntervalRangeFormation;
                intervalFormation.x *= IntervalMultiplier;
                intervalFormation.y *= IntervalMultiplier;
                archer._shootIntervalRangeFormation = intervalFormation;
            }

            // 体型 1.15（坑11：只动 y）+ ScaleRegistry 每帧守卫；Restore 必须
            // Unregister，否则池复用给普通弓箭手时被错误守卫在 1.15
            Vector3 scale = archer.transform.localScale;
            scale.y = CrossbowmanScaleY;
            archer.transform.localScale = scale;
            ScaleRegistryHolder.Register(archer.GetComponent<Mover>(), CrossbowmanScaleY);
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
            Vector3 scale = archer.transform.localScale;
            scale.y = 1f;
            archer.transform.localScale = scale;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Crossbowman/squad-restore] " + e);
        }
    }
}

/// <summary>
/// 弩手标记（挂在弩手 Archer 的 gameObject 上）：骑士招募排除、完整性巡检遍历、
/// 池复用清污。无字段——存在即身份。按 SpecialTowerRebuildMarker 先例显式
/// ClassInjector 注册（PatchRoles_Crossbowman.EnsureMarkerRegistered）。
/// </summary>
public sealed class CrossbowmanMarker : MonoBehaviour
{
    public CrossbowmanMarker(IntPtr pointer) : base(pointer)
    {
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
