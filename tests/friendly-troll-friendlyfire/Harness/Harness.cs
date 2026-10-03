using System;
using System.Collections.Generic;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

namespace FriendlyTrollFriendlyFireTests
{
    internal static class Check
    {
        internal static void True(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        internal static void False(bool condition, string message)
        {
            if (condition) throw new Exception(message);
        }

        internal static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception(message + " (expected=" + expected + " actual=" + actual + ")");
        }
    }

    /// <summary>一条用例：共享静态逐个复位后串行执行。</summary>
    internal static class Case
    {
        internal static int Passed;
        internal static int Failed;
        internal static readonly List<string> Failures = new List<string>();

        internal static void Run(string name, Action body)
        {
            Fixture.Reset();
            try
            {
                body();
                Passed++;
                Console.WriteLine("  ok   " + name);
            }
            catch (Exception exception)
            {
                Failed++;
                Failures.Add(name + ": " + exception.Message);
                Console.WriteLine("  FAIL " + name + " -> " + exception.Message);
            }
        }
    }

    internal sealed class FriendlyUnit
    {
        internal GameObject Object;
        internal FriendlyTroll Troll;
        internal Damageable Damageable;
    }

    /// <summary>被测生产路径的反射入口（生产方法全部 private static，测试不复制逻辑）。</summary>
    internal static class Production
    {
        private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

        internal static void RegisterFriendly(FriendlyTroll troll)
        {
            Method("RegisterFriendly", typeof(FriendlyTroll)).Invoke(null, new object[] { troll });
        }

        internal static void ReconcileInvulnerability()
        {
            Method("ReconcileFriendlyInvulnerability").Invoke(null, null);
        }

        internal static void DeregisterFriendly(FriendlyTroll troll)
        {
            Method("DeregisterFriendly", typeof(FriendlyTroll)).Invoke(null, new object[] { troll });
        }

        internal static void PruneFriendlyRegistries()
        {
            Method("PruneFriendlyRegistries").Invoke(null, null);
        }

        /// <summary>FriendlyResetPatch.Prefix（ResetAndDespawn 前缀）真实路径。</summary>
        internal static void ResetAndDespawnPrefix(FriendlyTroll troll)
        {
            NestedMethod("FriendlyResetPatch", "Prefix", troll);
        }

        /// <summary>FriendlyInitPatch.Prefix（Init 新 life 边界）真实路径。</summary>
        internal static void FriendlyInitPrefix(FriendlyTroll troll)
        {
            NestedMethod("FriendlyInitPatch", "Prefix", troll);
        }

        /// <summary>FriendlyInitPatch.Postfix（Init 后重新登记）真实路径。</summary>
        internal static void FriendlyInitPostfix(FriendlyTroll troll)
        {
            NestedMethod("FriendlyInitPatch", "Postfix", troll);
        }

        internal static void TickPursuit(FriendlyTrollPursuitCoordinator coordinator)
        {
            PatchDivine_FriendlyTroll.TickPursuit(coordinator);
        }

        internal static void DisableCoordinator(FriendlyTrollPursuitCoordinator coordinator)
        {
            PatchDivine_FriendlyTroll.DisablePursuitCoordinator(coordinator);
        }

        private static void NestedMethod(string nestedType, string method, FriendlyTroll troll)
        {
            Type patch = typeof(PatchDivine_FriendlyTroll).GetNestedType(
                nestedType, BindingFlags.NonPublic);
            patch.GetMethod(method, StaticPrivate).Invoke(null, new object[] { troll });
        }

        private static MethodInfo Method(string name, params Type[] parameters)
        {
            return typeof(PatchDivine_FriendlyTroll).GetMethod(
                name, StaticPrivate, null, parameters, null);
        }
    }

    /// <summary>
    /// 模拟 actual 2.4 Boulder.HitObject 的资格顺序：先 IsDamagedBy，再提交 ReceiveDamage。
    /// 拒收时只在观察袋记录分类、不提交（离线 pipeline，不是实机原生伤害执行）。
    /// </summary>
    internal static class BoulderPipeline
    {
        internal static void Hit(Damageable target, DamageSource source, int amount,
            GameObject boulder)
        {
            if (!target.IsDamagedBy(source))
            {
                target.RejectedSources.Add(source);
                return;
            }

            target.ReceiveDamage(amount, boulder, source);
        }
    }

    internal static class Fixture
    {
        internal static void Reset()
        {
            ResetProductionStatics();
            ModConfig.Enabled.Value = true;
            NetworkBigBoss.HasWorldAuth = true;
            NetworkBigBoss.IsClientPresent = false;
            NetworkBigBoss.HasClientCaughtUp = false;
            FriendlyTrollDisguise.Protected = false;
            KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
            Time.time = 100f;
            Time.timeScale = 1f;

            World world = NewWorld();
            Managers.Inst = new Managers
            {
                world = world,
                enemies = world.gameObject.AddComponent<EnemyManager>(),
                game = new Game { state = Game.State.Playing }
            };
        }

        /// <summary>清空被测类的全部静态登记表/一次性标记，用例间互相隔离。</summary>
        private static void ResetProductionStatics()
        {
            FieldInfo[] fields = typeof(PatchDivine_FriendlyTroll).GetFields(
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            foreach (FieldInfo field in fields)
            {
                if (field.IsLiteral) continue;
                if (field.FieldType == typeof(bool))
                {
                    field.SetValue(null, false);
                    continue;
                }

                object value = field.GetValue(null);
                if (value == null) continue;

                MethodInfo clear = field.FieldType.GetMethod("Clear", Type.EmptyTypes);
                if (clear != null)
                {
                    clear.Invoke(value, null);
                    continue;
                }

                if (!field.IsInitOnly && !field.FieldType.IsValueType)
                    field.SetValue(null, null);
            }
        }

        internal static GameObject NewObject(bool active = true, float x = 0f)
        {
            var gameObject = new GameObject { activeInHierarchy = active, name = "go" };
            gameObject.Transform = gameObject.AddComponent<Transform>();
            gameObject.Transform.position = new Vector3(x, 0f, 0f);
            return gameObject;
        }

        internal static World NewWorld()
        {
            return NewObject().AddComponent<World>();
        }

        /// <summary>
        /// 友好巨魔单位。默认：无敌 true + prefab 基线 true + mask 含位4/8/1，
        /// 与“受影响玩家档”的目标形态一致。
        /// </summary>
        internal static FriendlyUnit Friendly(bool invulnerable = true,
            bool initially = true, DamageSource? damagedBy = null, int hitPoints = 100)
        {
            var unit = new FriendlyUnit();
            unit.Object = NewObject();
            unit.Troll = unit.Object.AddComponent<FriendlyTroll>();
            unit.Damageable = unit.Object.AddComponent<Damageable>();
            unit.Damageable.invulnerable = invulnerable;
            unit.Damageable.isInvulnerableInitially = initially;
            unit.Damageable.SeedDamagedBy(damagedBy
                ?? (DamageSource.BoulderFriendly | DamageSource.BoulderEnemy | DamageSource.Troll));
            unit.Damageable.hitPoints = hitPoints;
            unit.Damageable.InvulnerableWrites = 0;
            unit.Damageable.MaskWhenInvulnerabilityDisabled = default;
            return unit;
        }

        /// <summary>未登记普通目标（无 FriendlyTroll 组件）。</summary>
        internal static Damageable PlainDamageable(DamageSource damagedBy, int hitPoints = 100)
        {
            GameObject gameObject = NewObject();
            Damageable damageable = gameObject.AddComponent<Damageable>();
            damageable.SeedDamagedBy(damagedBy);
            damageable.hitPoints = hitPoints;
            return damageable;
        }

        /// <summary>RegisterFriendly 在 world 上装的当前协调器。</summary>
        internal static FriendlyTrollPursuitCoordinator Coordinator()
        {
            return Managers.Inst.world.GetComponent<FriendlyTrollPursuitCoordinator>();
        }
    }
}
