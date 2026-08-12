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
    /// 实现原理（重要，两版失败的教训）：
    /// v1 写 _moveSpeed（Update postfix）：Mover.Update 每帧从 _goalSpeed Lerp 重算 _moveSpeed，
    ///     写入值在算 velocity 前被覆盖——从不生效（ArchReviewer P0）。
    /// v2 patch SetGoal/SetGoalSpeed：玩家（Player）根本不走 SetGoal！玩家是 Rewired 输入
    ///     直接控制（Player.cs:1152-1160 `mover.SetSpeed(walkSpeed/runSpeed, direction)`）——
    ///     速度仍不生效（用户实测反馈）。
    /// v3（当前）patch SetSpeed/SetSpeedToGoal：Mover.SetSpeed 直接写 _moveSpeed
    ///     （Mover.cs:309 `this._moveSpeed = moveSpeed`，GoalMode.Off，不走 Lerp），
    ///     玩家每帧（或输入变化时）调 SetSpeed——prefix 缩放 speed 参数即真实生效。
    ///     幂等：每次调用只乘一次，无累积。只对 Player 生效（单位速度不受影响）。
    /// </summary>
    public static class Patch_Mover
    {
        private const float MaxSpeedCap = 15f;

        // 缓存 Mover → Player 身份，避免每次 SetSpeed 都 GetComponent
        private static readonly ConditionalWeakTable<Mover, Player> PlayerCache =
            new ConditionalWeakTable<Mover, Player>();

        public static void Register(HarmonyInstance harmony)
        {
            var moverType = typeof(Mover);
            var prefix = new HarmonyMethod(typeof(Patch_Mover).GetMethod("SetSpeed_Prefix"));
            int patched = 0;

            var methods = moverType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (var m in methods)
            {
                if (m.Name == "SetSpeed" || m.Name == "SetSpeedToGoal")
                {
                    // 只 patch 带 float moveSpeed 参数的方法（SetSpeed 有 (float) 和 (float,int) 重载）
                    bool hasSpeedParam = false;
                    foreach (var p in m.GetParameters())
                    {
                        if (p.Name == "moveSpeed" && p.ParameterType == typeof(float)) hasSpeedParam = true;
                    }
                    if (!hasSpeedParam) continue;

                    harmony.Patch(m, prefix, null);
                    patched++;
                    Debug.Log("[MyMod] Patched Mover." + m.Name);
                }
            }

            if (patched == 0)
            {
                Debug.LogError("[MyMod] No Mover.SetSpeed methods found!");
            }
        }

        /// <summary>
        /// 在速度写入 _moveSpeed 前乘上倍率（仅玩家；speed 为 0 或负数不动）。
        /// </summary>
        public static void SetSpeed_Prefix(Mover __instance, ref float moveSpeed)
        {
            if (!Main.Enabled || Main.speedMultiplier <= 1) return;
            if (moveSpeed <= 0f) return;

            try
            {
                Player player;
                if (!PlayerCache.TryGetValue(__instance, out player))
                {
                    player = __instance.GetComponent<Player>();
                    if (player == null) return;
                    PlayerCache.Add(__instance, player);
                }

                moveSpeed = Mathf.Min(moveSpeed * Main.speedMultiplier, MaxSpeedCap);
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Error in Mover speed patch: " + e.Message);
            }
        }
    }
}
