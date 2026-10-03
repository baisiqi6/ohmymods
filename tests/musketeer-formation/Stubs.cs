// 生产文件 Patch_MusketeerFormation.cs 的直接源测试替身：只覆盖该文件实际用到的成员。
// 语义按"测试可完全控制"设计：Unity 对象是普通托管对象，InWorld/身份/英雄/行存在与否都由
// 测试开关驱动。真实 interop 签名（Archer._knight 等私有字段经 Il2CppInterop 暴露）由
// Operator 构建核对。
using System;
using System.Collections.Generic;
using KingdomEnhancedMod;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string method) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrefix : Attribute { }
}

namespace UnityEngine
{
    public class Object
    {
        private static int _next = 1;
        public IntPtr Pointer = (IntPtr)_next++;
        public int GetInstanceID() => (int)Pointer;
    }

    public class GameObject : Object
    {
        public bool activeInHierarchy = true;
        public Transform transform;

        public GameObject()
        {
            transform = new Transform();
            transform.gameObject = this;
        }

        public T Add<T>() where T : Component, new()
        {
            var component = new T();
            component.gameObject = this;
            return component;
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject != null ? gameObject.transform : null;
    }

    public class Behaviour : Component
    {
        public bool enabled = true;
    }

    public class Transform : Component
    {
        public Vector3 position;
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    public static class Mathf
    {
        public static float Abs(float value) => Math.Abs(value);
    }

    public static class Time
    {
        public static float timeScale = 1f;
    }
}

public class Character
{
    public bool inert;
    public bool grabbed;
}

public class Damageable
{
    public bool isDead;
}

public class Embarkable : UnityEngine.Component { }

public class Embarkee : UnityEngine.Component
{
    public bool IsEmbarked;
    public Embarkable EmbarkableTarget;
}

public class Knight : UnityEngine.Behaviour { }

public class GuardSlot : UnityEngine.Component { }

public class Archer : UnityEngine.Behaviour
{
    public Character _character = new();
    public Damageable _damageable = new();
    public Embarkee _embarkee = new();
    public Knight _knight;
    public GuardSlot _guardSlot;
    public bool inGuardSlot;
    public bool harmless;

    public Formation formation;
    public bool playerControlled;

    public Formation GetFormation() => formation;
    public bool ShouldPlayerControl() => playerControlled;

    // Harmony patch target only: this stub exists so [HarmonyPatch(typeof(Archer), nameof(Archer.TryRecruit))]
    // compiles; the policy tests never execute it. The pipeline project has the native-like version.
    public bool TryRecruit(Formation formation) => false;
}

public class Formation : UnityEngine.Behaviour
{
    public enum FormationType
    {
        Bomb,
        PassiveShieldWall,
        ActiveShieldWall,
        PlayerFormation
    }

    // 数值与 game-source Formation.cs:957-983 完全一致（数组/类型语义靠数值对齐）。
    public enum UnitTypes
    {
        Archer,
        Knight,
        Squire,
        Pikemen,
        Bomb,
        Gap,
        Player,
        Catapult,
        Ninja,
        Worker,
        AnyShieldedUnit,
        FleetBoat,
        Total
    }

    public FormationType formationType;

    public FormationType GetFormationType => formationType;
}

namespace KingdomEnhancedMod
{
    /// <summary>生产版是 il2cpp/MusketeerRuntime.cs 的 life 读者；策略测试只验证门序。</summary>
    internal static class MusketeerRuntime
    {
        internal static bool MatchesBindingLease(Archer archer, long lease) => lease > 0L && archer != null;
    }

    internal static class NetworkBigBoss
    {
        internal static bool HasWorldAuth = true;
        internal static bool IsOnline;
    }

    internal class Game
    {
        internal enum State { Menu, Playing }
        internal State state = State.Playing;
    }

    internal class World : UnityEngine.Object
    {
        public UnityEngine.Transform gameLayer = new UnityEngine.Transform();
    }

    internal class Managers
    {
        internal static Managers Inst = new Managers();
        internal World world = new World();
        internal Game game = new Game();
    }

    /// <summary>生产版是 il2cpp/Patch_CrossbowFormation.cs；策略测试镜像其 Playing 形状。</summary>
    internal static class PatchCrossbowFormation
    {
        internal static bool PlayingFlag = true;
        internal static bool Playing => PlayingFlag && UnityEngine.Time.timeScale > 0f
            && Managers.Inst != null && Managers.Inst.game != null
            && Managers.Inst.game.state == Game.State.Playing;
    }

    internal static class MusketeerIdentity
    {
        internal static readonly List<Archer> Units = new();
        internal static bool MarkedEnabled = true;

        internal static bool IsUnit(Archer archer) => MarkedEnabled && archer != null && Units.Contains(archer);

        internal static void CopyUnits(List<Archer> destination)
        {
            destination.Clear();
            destination.AddRange(Units);
        }
    }

    internal static class MusketeerAccess
    {
        internal static bool Enabled = true;
        internal static bool InWorldResult = true;

        internal static bool Playing => Enabled && UnityEngine.Time.timeScale > 0f
            && Managers.Inst != null && Managers.Inst.game != null
            && Managers.Inst.game.state == Game.State.Playing;

        internal static bool InWorld(UnityEngine.Component component) =>
            component != null && InWorldResult;
    }

    internal static class HeroArcherRuntime
    {
        internal static readonly HashSet<Archer> Heroes = new();

        internal static bool IsHero(Archer archer) => archer != null && Heroes.Contains(archer);
    }

    // 生产版是 il2cpp/CrossbowmanLifecycle.cs 的身份读者（marker 组件 + 即时读全局 Mod 开关，
    // 读取器自身异常一律 fail closed=当作非弩手）。本测试用可开关替身代替：
    // Crossbowmen=marker 集合，IdentityEnabled=全局开关（默认 true），ThrowOnRead 仅用于验证
    // ShouldBlockNativeRecruit 外层 catch 的容错（生产读者不抛异常，该路径只有桩可达）。
    internal static class CrossbowmanLifecycle
    {
        internal static readonly HashSet<Archer> Crossbowmen = new();
        internal static bool IdentityEnabled = true;
        internal static bool ThrowOnRead;

        internal static bool IsCrossbowman(Archer archer)
        {
            if (ThrowOnRead) throw new InvalidOperationException("scripted identity read failure");
            return IdentityEnabled && archer != null && Crossbowmen.Contains(archer);
        }

        internal static long FormationLife(Archer archer) => 1L;

        internal static bool MatchesFormationLife(Archer archer, long life) => life > 0L;
    }

    // 生产版是 PatchWorld_FleetBoatFormation.cs 的 internal 查询；本测试只编译本文件，
    // 因此用可开关的替身代替（默认 false，测试用 Fixture.Reset/直接赋值控制）。
    internal static class PatchWorld_FleetBoatFormation
    {
        internal static bool MusketeerRow;
        internal static Formation DirtyFormation;   // 有未归还临时 type 的 formation

        internal static bool HasMusketeerRow(Formation formation) => formation != null && MusketeerRow;

        internal static bool HasDirtyRowTypes(Formation formation)
            => formation != null && ReferenceEquals(formation, DirtyFormation);
    }

    internal class Log
    {
        public void LogWarning(string message) { }
    }

    internal class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance = new();
        public Log LogSource = new();
    }
}
