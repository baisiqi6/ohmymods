// 原生边界替身（游戏/模组侧）：
//   * 全局命名空间游戏类型（Managers/Holder/Character/Archer/ArrowAttack/Arrow/Bolt/Pool/
//     PoolManager/Util…）只暴露被编入的生产文件真正读写的成员；Arrow 另补 HeroArcherArrowVisuals
//     需要的 _spriteRenderer/archer 编译面（本套件不驱动英雄外观分支）；
//   * Util.ComputeTrajectoryAngle 逐字镜像 Util.cs:429（2.1.0 反编译，算法面 2.4 相同）；
//   * KingdomEnhancedMod 命名空间的模组联动（CrossbowmanLifecycle/UnitScanCache/
//     GreekScaleScope/…）用记账空壳——真实行为由各自直链测试套件负责
//     （tests/crossbow-lifecycle 等），这里只验证接线契约；
//   * ArcherOptionsScope / HeroArcherRuntime / KingdomEnhancedPlugin 是 HeroArcherWallPierce 与
//     HeroArcherArrowVisuals 的世界上下文/英雄身份/日志缝（与 tests/hero-arrow-pierce 同款）。

using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 全局命名空间：游戏类型（与真实 Assembly-CSharp 一致）
// ============================================================

public class Managers
{
    public static Managers Inst;
    public Holder holder;
    public PoolManager pools;
}

public class Holder
{
    public Dictionary<string, Character> tagCharacterPairs = new Dictionary<string, Character>();
}

public interface IUnitController { }

public class DroppableTool : UnityEngine.Component
{
    public string tag;
}

public class Character : UnityEngine.Component
{
    public Color outfitColor;
    public Color outfitSecondaryColor;

    public Character Promote(DroppableTool tool, IUnitController controller) => this;
}

public enum BuffType { FireAttacks }

public class Buffable
{
    public bool IsBuffActive(BuffType type) => false;
}

public class Scanner
{
    public float range = 8f;
    public float rangeBehind = 8f;
}

public class Mover : UnityEngine.Component { }

public class Knight : UnityEngine.Component { }

public class GuardSlot { }

public class CoatOfArms
{
    public Color primaryColor;
    public Color secondaryColor;
}

public class CampaignSaveData
{
    public static CampaignSaveData current;
    public CoatOfArms coatOfArms;
}

public static class NetworkBigBoss
{
    public static bool HasWorldAuth = true;
}

public class World : UnityEngine.Component
{
    public void OnLevelLoaded() { }

    public void StartCoroutine(System.Collections.IEnumerator routine) { }
}

public class Archer : UnityEngine.Component
{
    public float shootRange = 8f;
    public float towerShootRange = 12f;
    public Vector2 _shootIntervalRange = new Vector2(1f, 2f);
    public Vector2 _shootIntervalRangeFormation = new Vector2(3f, 4f);
    public Scanner _enemyScanner = new Scanner();
    public ArrowAttack _arrowAttack;
    public ArrowAttack _fireArrowAttack;
    public ArrowAttack ActiveArrowAttack;
    public RuntimeAnimatorController hunterAnimator;
    public RuntimeAnimatorController soldierAnimator;
    public bool _isWearingBannerColor;
    public bool inGuardSlot;
    public GuardSlot _guardSlot;
    public Knight _knight;
    public Buffable _buffable = new Buffable();

    public bool IsAvailableForJob(GameObject jobObject) => true;
}

/// <summary>原生 ArrowAttack（ScriptableObject 替身）：只暴露被测代码读写的序列化字段。</summary>
public class ArrowAttack : UnityEngine.Object
{
    public Arrow _arrowPrefab;
    public float _shotMagnitude = 12f;
    public float _boostedShotMagnitude = 12f;
    public Vector2 _arrowOriginOffset = new Vector2(0.15f, 0.5f);

    public ArrowAttack() { }
    public ArrowAttack(string assetName) { name = assetName; }

    /// <summary>只为 HeroArcherArrowVisuals 编译面存在（[HarmonyPatch(typeof(ArrowAttack), "FireArrowInternal")]）；
    /// 本套件不执行英雄发射作用域。</summary>
    public void FireArrowInternal(GameObject source) { }
}

/// <summary>原生 Arrow：弩矢本体（KEM 弩矢=原生 Arrow 克隆，非 Bolt 类）。弩手 slice 只读写
/// _collider；HeroArcherArrowVisuals（同程序集生产文件）另读 _spriteRenderer/archer——本套件不驱动
/// 英雄外观分支（作用域恒空），这两个成员只保编译面与身份复核语义。</summary>
public class Arrow : UnityEngine.Component
{
    public int hitDamage = 1;
    public bool _alwaysDrawTrail;
    public float _notPerfectTrailLength = 0.1f;
    public Collider2D _collider;
    public SpriteRenderer _spriteRenderer;
    public GameObject archer { get; set; }
}

/// <summary>原生 Bolt（弩箭塔弹矢，非 Arrow 子类）：只借 SpriteRenderer.sprite 外观。</summary>
public class Bolt : UnityEngine.Component { }

public class Pool : UnityEngine.Component
{
    public bool sync;
    public short syncID;
    public int preload;
    public int capacity;
    public bool expendable;
    public GameObject prefab;

    public GameObject FastSpawn(Vector3 position, Quaternion rotation, Transform parent, short netId, bool authority) => null;

    public static Pool GetPoolFromPrefabAsset(GameObject prefab) => null;
}

public class PoolManager : UnityEngine.Component
{
    public List<Pool> cachedPools = new List<Pool>();
    public Dictionary<string, Pool> cachedNamePoolPairs = new Dictionary<string, Pool>();
    public Dictionary<int, Pool> cachedSyncIdPoolPairs = new Dictionary<int, Pool>();

    public Pool CreatePoolFor(GameObject prefab) => new Pool { prefab = prefab, gameObject = new GameObject() };

    public void Init() { }
}

/// <summary>原生 Wall（全局命名空间）：HeroArcherWallPierce 只允许对活动子碰撞体做
/// GetComponentsInChildren(includeInactive=false)，及测试注入的枚举异常。</summary>
public class Wall : UnityEngine.Component
{
    internal readonly List<UnityEngine.Collider2D> Colliders = new List<UnityEngine.Collider2D>();
    internal bool EnumerationThrows;

    public T[] GetComponentsInChildren<T>(bool includeInactive) where T : UnityEngine.Component
    {
        if (EnumerationThrows) throw new InvalidOperationException("stub: wall collider enumeration threw");
        List<T> found = new List<T>();
        for (int i = 0; i < Colliders.Count; i++)
        {
            UnityEngine.Collider2D collider = Colliders[i];
            if (collider == null) continue;
            if (!includeInactive)
            {
                UnityEngine.GameObject go = collider.gameObject;
                if (go == null || !go.activeSelf) continue;
            }
            if (collider is T match) found.Add(match);
        }
        return found.ToArray();
    }
}

/// <summary>
/// 原生 Util：ComputeTrajectoryAngle 逐字镜像 Util.cs:429（含无解 45° 回退语义）。
/// 被测 prefix 只用这一个成员；BestShotInternal 的 ParabolaCast 墙挡判定只在测试的
/// 原生参照实现里出现（生产 prefix 恰好要跳过它）。
/// </summary>
public static class Util
{
    public static bool ComputeTrajectoryAngle(Vector2 target, float force, out Vector2 lowShot, out Vector2 highShot, float gravity = -1f)
    {
        float num = force * force;
        float num2 = num * num;
        float num3 = ((gravity == -1f) ? (-Physics2D.gravity.y) : (-gravity));
        float x = target.x;
        float y = target.y;
        float num4 = num2 - num3 * (num3 * x * x + 2f * y * num);
        if (num4 < 0f)
        {
            highShot.x = ((x > 0f) ? 0.70710677f : (-0.70710677f));
            highShot.y = 0.70710677f;
            lowShot = highShot;
            return false;
        }
        float num5 = Mathf.Sqrt(num4);
        lowShot = new Vector2(num3 * x, num - num5).normalized;
        highShot = new Vector2(num3 * x, num + num5).normalized;
        return true;
    }
}

// ============================================================
// KingdomEnhancedMod：模组侧联动（真实行为由各自直链套件覆盖，此处只保编译面）
// ============================================================

namespace KingdomEnhancedMod
{
    public static class ModConfig
    {
        public sealed class BoolConfig { public bool Value = true; }
        public static readonly BoolConfig Enabled = new BoolConfig();
    }

    public sealed class StubLogSource
    {
        public readonly List<string> Lines = new List<string>();
        public void LogInfo(string message) => Lines.Add("INFO " + message);
        public void LogWarning(string message) => Lines.Add("WARN " + message);
        public void LogError(string message) => Lines.Add("ERROR " + message);
        public bool Contains(string fragment) => Lines.Exists(l => l.Contains(fragment));
        public int CountContaining(string fragment)
        {
            int n = 0;
            for (int i = 0; i < Lines.Count; i++) if (Lines[i].Contains(fragment)) n++;
            return n;
        }
    }

    public sealed class PluginStub
    {
        public StubLogSource LogSource = new StubLogSource();
    }

    public static class KingdomEnhancedPlugin
    {
        public static PluginStub Instance = new PluginStub();
    }

    /// <summary>英雄身份缝（HeroArcherArrowVisuals 编译面；本套件不驱动英雄分支——EnabledState 恒 false、
    /// HeroPointers 恒空，ResetArrow 因此只走穿墙归还路径，不产生任何外观回执）。</summary>
    internal static class HeroArcherRuntime
    {
        internal static bool EnabledState;
        internal static readonly HashSet<IntPtr> HeroPointers = new HashSet<IntPtr>();
        internal static int IsHeroCalls;
        internal static bool IsHeroThrows;

        internal static bool Enabled => EnabledState;

        internal static bool IsHero(Archer archer)
        {
            IsHeroCalls++;
            if (IsHeroThrows) throw new InvalidOperationException("stub: IsHero threw");
            if (archer == null) return false;
            try { return HeroPointers.Contains(archer.Pointer); }
            catch (Exception) { return false; }
        }

        internal static void Reset()
        {
            EnabledState = false;
            HeroPointers.Clear();
            IsHeroCalls = 0;
            IsHeroThrows = false;
        }
    }

    /// <summary>HeroArcherWallPierce 的世界上下文缝（与 tests/hero-arrow-pierce 同款）。</summary>
    internal static class ArcherOptionsScope
    {
        internal static bool ContextAvailable = true;
        internal static IntPtr WorldPtr = new IntPtr(7001);
        internal static IntPtr LayerPtr = new IntPtr(7002);
        internal static int SceneHandle = 7;
        internal static bool Throws;

        internal static bool TryGetContext(out IntPtr world, out IntPtr layer, out int scene)
        {
            if (Throws) throw new InvalidOperationException("stub: TryGetContext threw");
            world = WorldPtr;
            layer = LayerPtr;
            scene = SceneHandle;
            return ContextAvailable;
        }

        internal static void Reset()
        {
            ContextAvailable = true;
            WorldPtr = new IntPtr(7001);
            LayerPtr = new IntPtr(7002);
            SceneHandle = 7;
            Throws = false;
        }
    }

    /// <summary>火铳身份边界（资格早退用，恒否）。</summary>
    internal static class MusketeerIdentity
    {
        internal static bool GunPromotionInProgress;
        internal static bool IsMarked(GameObject go) => false;
        internal static bool IsGun(DroppableTool tool) => false;
        internal static bool IsUnit(Archer archer) => false;
    }

    /// <summary>profile 形状与生产逐字段一致（BuildProfile 的载体）。</summary>
    internal struct CrossbowmanProfile
    {
        internal ArrowAttack Attack;
        internal RuntimeAnimatorController Skin;
        internal Action<Archer> ReapplyBanner;
        internal float BaseShootRange;
        internal bool BaseShootRangeKnown;
        internal Vector2 BaseInterval;
        internal bool BaseIntervalKnown;
        internal Vector2 BaseIntervalFormation;
        internal bool BaseIntervalFormationKnown;
        internal RuntimeAnimatorController BaseSkin;
        internal RuntimeAnimatorController BaseSoldierAnimator;
    }

    public sealed class CrossbowmanMarker : UnityEngine.MonoBehaviour { }

    /// <summary>真实 CrossbowmanLifecycle 的契约壳（行为由 tests/crossbow-lifecycle 直链覆盖）。</summary>
    internal static class CrossbowmanLifecycle
    {
        internal static void Apply(Archer archer, in CrossbowmanProfile profile) { }
        internal static void Strip(Archer archer, in CrossbowmanProfile profile) { }
        internal static bool IsCrossbowman(Archer archer) => false;
        internal static void UnwindAll(in CrossbowmanProfile profile) { }
        internal static void EnsureMarkerRegistered() { }
        internal static void ReconcileScan(CrossbowmanMarker[] markers, in CrossbowmanProfile profile) { }
        internal static void OnArcherEnablePrefix(Archer archer, in CrossbowmanProfile profile) { }
        internal static void OnArcherEnablePostfix(Archer archer, in CrossbowmanProfile profile) { }
        internal static void OnArcherDisablePrefix(Archer archer) { }
        internal static void OnConvertToHunterPostfix(Archer archer, in CrossbowmanProfile profile) { }
        internal static void BeginPoolSpawnScope() { }
        internal static void EndPoolSpawnScope() { }
        internal static bool HasPendingWork => false;
        internal const float ShootRange = 12f;
        internal const float IntervalMultiplier = 2f;
        internal const float ScaleY = 1.15f;
    }

    internal static class UnitScanCache
    {
        internal static void InvalidateAll() { }
        internal static CrossbowmanMarker[] GetCrossbowmanMarkers() => Array.Empty<CrossbowmanMarker>();
    }

    internal static class GreekScaleScope
    {
        internal static void ApplyY(Transform transform, float y) { }
        internal static void ApplyScale(Transform transform, float scale) { }
        internal static float NativeScale(Transform transform) => 1f;
        internal static void Restore(Transform transform) { }
    }

    internal static class ScaleRegistryHolder
    {
        internal static void Register(Mover mover, float y) { }
        internal static void Unregister(Mover mover) { }
    }
}
