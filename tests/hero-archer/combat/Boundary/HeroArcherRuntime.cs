// 编译/测试夹具：HeroArcherRuntime 的**锁定契约**替身（不是生产实现，也不含任何玩法逻辑）。
//
// 生产实现由 runtime slice 提供（cwd runtime-worker → il2cpp/HeroArcherRuntime.cs，operator 统一接入）；
// 本文件只让本 slice 的三个生产文件在没有该文件的目录下也能编译、可验证接线与门控：
//   bool Enabled { get; }  bool IsHero(Archer)  bool IsCombatEligible(Archer)
//   void Observe(Archer)   void OnEnable(Archer)  void Tick()  void OnShot(Archer)  void Clear()
// 语义刻意保持「纯成员集合」：IsHero/IsCombatEligible **不**看 Enabled，
// 这样「开关关闭时英雄覆盖层必须失效」只能由生产侧（HeroArcherCombat / PatchArcher_*）的门成立，
// 测试才能真的证伪；生产 runtime 自己怎么合并 Enabled 与本夹具无关。
using System;
using System.Collections.Generic;
using KingdomEnhancedMod;

namespace KingdomEnhancedMod
{
    internal static class HeroArcherRuntime
    {
        internal static bool Enabled;

        private static readonly HashSet<IntPtr> HeroSet = new HashSet<IntPtr>();
        private static readonly HashSet<IntPtr> CombatSet = new HashSet<IntPtr>();

        internal static int ObserveCalls, OnEnableCalls, OnShotCalls, TickCalls;
        /// <summary>非 null 时所有入口抛出：用于验证生产侧的异常隔离。</summary>
        internal static Exception ThrowFrom;

        internal static bool IsHero(Archer archer)
        {
            MaybeThrow();
            return archer != null && archer.gameObject != null && HeroSet.Contains(archer.Pointer);
        }

        internal static bool IsCombatEligible(Archer archer)
        {
            MaybeThrow();
            return archer != null && archer.gameObject != null && CombatSet.Contains(archer.Pointer);
        }

        internal static void Observe(Archer archer) { ObserveCalls++; MaybeThrow(); }
        internal static void OnEnable(Archer archer) { OnEnableCalls++; MaybeThrow(); }
        internal static void Tick() { TickCalls++; MaybeThrow(); }
        internal static void OnShot(Archer archer) { OnShotCalls++; MaybeThrow(); }

        internal static void Clear()
        {
            HeroSet.Clear();
            CombatSet.Clear();
        }

        internal static void MarkHero(Archer archer, bool combatEligible = true)
        {
            HeroSet.Add(archer.Pointer);
            if (combatEligible) CombatSet.Add(archer.Pointer);
        }

        internal static void MarkCombatEligible(Archer archer, bool value)
        {
            if (value) CombatSet.Add(archer.Pointer);
            else CombatSet.Remove(archer.Pointer);
        }

        internal static void ResetForTests()
        {
            Enabled = false;
            HeroSet.Clear();
            CombatSet.Clear();
            ObserveCalls = OnEnableCalls = OnShotCalls = TickCalls = 0;
            ThrowFrom = null;
        }

        private static void MaybeThrow()
        {
            if (ThrowFrom != null) throw ThrowFrom;
        }
    }
}
