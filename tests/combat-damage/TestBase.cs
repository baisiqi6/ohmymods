using System;
using KingdomEnhancedMod;
using UnityEngine;

namespace KingdomCombatDamage.Tests
{
    /// <summary>每个测试独立环境：重置生产静态统计/日志/authority，并按 root 接入点注册 marker（幂等）。</summary>
    public abstract class CombatTestBase : IDisposable
    {
        protected CombatTestBase()
        {
            Reset();
            CombatTargetLife.Initialize();      // root 在 Plugin.Init 的接入点
        }

        public void Dispose() => Reset();

        protected static void Reset()
        {
            CombatDamage.StatSubmitted = 0;
            CombatDamage.StatSkipped = 0;
            CombatDamage.StatFaulted = 0;
            CombatTargetLife.StatResolved = 0;
            CombatTargetLife.StatMarkerCreated = 0;
            CombatTargetLife.StatDegraded = 0;
            NetworkBigBoss.HasWorldAuth = true;
            NetworkBigBoss.HasClientCaughtUp = true;
            KingdomEnhancedPlugin.Instance = new PluginStub();
            UnityEngine.Object.HideFlagsWrites = 0;      // 任何 hideFlags 写入都会计数（生产不得写）
        }

        protected static Damageable NewTarget(out GameObject go)
        {
            go = new GameObject("foe");
            return go.AddComponent<Damageable>();
        }
    }
}
