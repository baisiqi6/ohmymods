using System;
using KingdomEnhancedMod;
using UnityEngine;

namespace KingdomGreekImpact.Tests
{
    /// <summary>每个测试独立世界：Env.Reset() + 全新 world layer root + 开启功能。</summary>
    public abstract class ImpactTestBase : IDisposable
    {
        protected GameObject World { get; }

        protected ImpactTestBase()
        {
            Env.Reset();
            Arrow.TryDamageDispatch = Harness.DispatchTryDamage;   // native HitObject → TryDamage 分派
            ModConfig.ArcherImpactEnabled.Value = true;
            CombatTargetLife.Initialize();                         // root 在 Plugin.Init 的接入点（幂等）
            World = Env.NewWorld(1);
        }

        public void Dispose() => Env.Reset();

        protected static void Advance(float seconds) => Time.time += seconds;

        /// <summary>直接把箭摆到命中点（native 命中当刻箭头所在位置）。</summary>
        protected static void PlaceAt(Arrow arrow, float x, float y = 0f)
        {
            arrow.transform.position = new Vector3(x, y, 0f);
        }
    }
}
