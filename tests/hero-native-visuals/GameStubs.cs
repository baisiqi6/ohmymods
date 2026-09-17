// 测试替身：HeroArcherVisuals 依赖的游戏/模组类型（只实现被测控制流需要的表面）。
// 这些替身只存在于 tests/visuals-native-stub；游戏侧类型由 operator 用真实 interop 编译复核。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod
{
    /// <summary>Archer 替身：视觉只需要身份（gameObject/pointer）与 _animator。</summary>
    internal sealed class Archer : Component
    {
        internal Animator _animator;
        internal IntPtr Pointer;
        internal Mover _mover;
        internal float walkSpeed=.975f,runSpeed=2.4f;
    }

    internal sealed class Mover : Behaviour
    {
        internal enum GoalMode { Off, Position, Object }
        internal GoalMode goalMode;
        internal Rigidbody2D rigidbody;
        internal float _goalSpeed,_moveSpeed,_multiplier=1f,_goalPosition,_pauseTimeout;
    }
    internal static class NetworkBigBoss
    {
        internal static bool HasWorldAuth=true,IsOnline;
    }
    internal sealed class Game
    {
        internal enum State { Playing, Menu, Loading }
        internal State state;
    }

    internal sealed class World
    {
        internal Transform gameLayer;
        internal float Wind = 0f;

        internal float WindSpeedCapped(Vector3 position) => Wind;
    }

    internal sealed class Managers
    {
        internal static Managers Inst;
        internal World world;
        internal Game game;
    }

    /// <summary>HeroArcherRuntime 替身：测试可切换 life/IsHero 来模拟池复用、死亡与功能关闭。</summary>
    internal static class HeroArcherRuntime
    {
        internal static int Life = 1;
        internal static bool Hero = true;
        internal static bool LifeReadable = true;

        internal static int CurrentActorLife(Archer archer)
        {
            if (!LifeReadable) throw new InvalidOperationException("life unreadable");
            return Life;
        }

        internal static bool IsHero(Archer archer) => Hero;
    }

    /// <summary>HeroArcherCloth 替身：只记录调用次数/句柄，供「每帧恰好一次」「归还时销毁」断言。</summary>
    internal static class HeroArcherCloth
    {
        internal sealed class Handle
        {
            internal GameObject Root;
            internal Transform RootTransform;
            internal MeshRenderer[] Renderers;
        }

        internal static int Created;
        internal static int Destroyed;
        internal static int TickCalls;
        internal static int LastTickVisible;
        internal static bool FailCreate;

        internal static Handle Create(Transform parent, SpriteRenderer reference)
        {
            if (FailCreate) return null;
            Created++;
            Handle handle = new Handle();
            handle.Root = new GameObject("KEM_Cloth");
            handle.RootTransform = handle.Root.transform;
            handle.Root.transform.SetParent(parent, false);
            handle.Renderers = new[]
            {
                handle.Root.AddComponent<MeshRenderer>(),
                handle.Root.AddComponent<MeshRenderer>(),
            };
            return handle;
        }

        internal static void Tick(Handle handle, float dt, float localVelocity, bool visible)
        {
            TickCalls++;
            LastTickVisible = visible ? 1 : 0;
        }

        internal static void Reorient(Handle handle, float scaleX)
        {
        }

        internal static void SetWind(Handle handle, float signedNormalized)
        {
        }

        internal static void Destroy(Handle handle)
        {
            if (handle == null) return;
            Destroyed++;
        }

        internal static void ResetCounters()
        {
            Created = Destroyed = TickCalls = LastTickVisible = 0;
        }
    }

    internal static class HeroArcherClothMath
    {
        internal static float LocalVelocityXFromWorld(float worldVelocityX, float parentScaleX)
            => parentScaleX == 0f ? 0f : worldVelocityX / parentScaleX;
    }

    /// <summary>HeroVisualPriority 替身：可注入失败（验证 ApplyHeroPlane 失败 → 整组撤销）。</summary>
    internal static class HeroVisualPriority
    {
        internal static bool Fail;
        internal static int Calls;

        internal static bool ApplyHeroDepth(Transform gameLayer, Transform body, Transform clothRoot)
        {
            Calls++;
            return !Fail;
        }
    }

    /// <summary>插件日志替身：捕获行，供「有界诊断」断言。</summary>
    internal sealed class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
        internal LogSourceStub LogSource = new LogSourceStub();
    }

    internal sealed class LogSourceStub
    {
        internal static readonly List<string> Lines = new List<string>();

        internal void LogInfo(string message) => Lines.Add(message);

        internal static int CountContaining(string needle)
        {
            int count = 0;
            for (int i = 0; i < Lines.Count; i++)
            {
                if (Lines[i].Contains(needle)) count++;
            }
            return count;
        }

        internal static void Reset() => Lines.Clear();
    }
}
