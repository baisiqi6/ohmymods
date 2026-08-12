using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using Harmony;

namespace MyMod
{
    /// <summary>
    /// 玩家移动速度倍率（Main.speedMultiplier）。
    ///
    /// 实现原理（重要，勿改回 _moveSpeed 方案）：
    /// Mover.Update 每帧从 _goalSpeed 用 Lerp 重算 _moveSpeed（Mover.cs:190），
    /// 再以 _moveSpeed 计算 velocity。若在 Update postfix 里写 _moveSpeed，
    /// 写入值在下一帧计算 velocity 前就被 Lerp 覆盖——速度倍率永远不会生效
    /// （这是 ArchReviewer 2026-08-12 审查发现的 P0 缺陷）。
    ///
    /// 正确入口：patch 所有设置 _goalSpeed 的方法（SetGoal x2 / SetGoalNoHaglet /
    /// SetGoalSpeed），在 prefix 里把 speed 参数乘以倍率。幂等：每次设置目标只乘
    /// 一次，无累积（每帧乘才会指数放大）。只对 Player 生效（单位速度不受影响）。
    /// </summary>
    public static class Patch_Mover
    {
        private const float MaxSpeedCap = 15f;

        // 缓存 Mover → Player 身份，避免每次 SetGoal 都 GetComponent
        private static readonly ConditionalWeakTable<Mover, Player> PlayerCache =
            new ConditionalWeakTable<Mover, Player>();

        public static void Register(HarmonyInstance harmony)
        {
            var moverType = typeof(Mover);
            var prefix = new HarmonyMethod(typeof(Patch_Mover).GetMethod("SetGoal_Prefix"));
            int patched = 0;

            var methods = moverType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (var m in methods)
            {
                if (m.Name == "SetGoal" || m.Name == "SetGoalNoHaglet" || m.Name == "SetGoalSpeed")
                {
                    // 只 patch 带 float speed 参数的重载（SetGoal 有 GameObject 重载也有 speed）
                    var pars = m.GetParameters();
                    bool hasSpeedParam = false;
                    foreach (var p in pars)
                    {
                        if (p.Name == "speed" && p.ParameterType == typeof(float)) hasSpeedParam = true;
                    }
                    if (!hasSpeedParam) continue;

                    harmony.Patch(m, prefix, null);
                    patched++;
                    Debug.Log("[MyMod] Patched Mover." + m.Name);
                }
            }

            if (patched == 0)
            {
                Debug.LogError("[MyMod] No Mover.SetGoal methods found!");
            }
        }

        /// <summary>
        /// 在目标速度写入 _goalSpeed 前乘上倍率（仅玩家；speed 为 0 或负数不动）。
        /// </summary>
        public static void SetGoal_Prefix(Mover __instance, ref float speed)
        {
            if (!Main.Enabled || Main.speedMultiplier <= 1) return;
            if (speed <= 0f) return;

            try
            {
                Player player;
                if (!PlayerCache.TryGetValue(__instance, out player))
                {
                    player = __instance.GetComponent<Player>();
                    if (player == null) return;
                    PlayerCache.Add(__instance, player);
                }

                speed = Mathf.Min(speed * Main.speedMultiplier, MaxSpeedCap);
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Error in Mover speed patch: " + e.Message);
            }
        }
    }
}
