// 游戏类型 stub（combat-damage slice）：字段/方法签名与 2.4 interop 一致，
// 行为只模拟 CombatDamage.Submit 依赖的原生语义（含回调抛错 = 可能已部分生效）。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod
{
    public enum DamageSource
    {
        Troll = 1, Arrow = 2, BoulderFriendly = 4, BoulderEnemy = 8, Knight = 16, Ogre = 32, Bolt = 64,
        PlayerSteed = 128, Pike = 256, Fire = 512, Stealer = 1024, Boar = 2048, Trap = 4096, Crusher = 8192,
        Fleet = 16384, GreedProjectile = 32768, SerpentAttack = 65536
    }

    public class Damageable : MonoBehaviour
    {
        public bool isDead;
        public bool IgnoreDamage;                       // 测试用：正常返回但不减 HP（Submitted ≠ HP 必减）
        public int ReceivedTotal;
        public int Calls;                               // 原生入口被调用次数（验「不重试」）
        public GameObject LastSource;
        public DamageSource LastKind;
        public readonly List<DamageSource> ReceivedSources = new List<DamageSource>();
        public Action<Damageable> OnReceive;            // 抛错 = 原生回调异常（可能已部分生效）

        public void ReceiveDamage(int amount, GameObject source, DamageSource damageSource)
        {
            Calls++;
            LastSource = source;
            LastKind = damageSource;
            if (!IgnoreDamage)
            {
                ReceivedTotal += amount;
                ReceivedSources.Add(damageSource);
            }
            OnReceive?.Invoke(this);
        }
    }

    public static class NetworkBigBoss
    {
        public static bool HasWorldAuth = true;
        public static bool HasClientCaughtUp = true;
    }

    internal sealed class LogSink
    {
        internal readonly List<string> Lines = new List<string>();
        public void LogInfo(string message) => Lines.Add("I:" + message);
        public void LogWarning(string message) => Lines.Add("W:" + message);
    }

    internal sealed class PluginStub
    {
        internal readonly LogSink LogSource = new LogSink();
    }

    internal static class KingdomEnhancedPlugin
    {
        internal static PluginStub Instance = new PluginStub();
    }
}
