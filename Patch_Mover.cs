using System;
using System.Reflection;
using UnityEngine;
using Harmony;

namespace MyMod
{
    public static class Patch_Mover
    {
        public static void Register(HarmonyInstance harmony)
        {
            var moverType = typeof(Mover);
            var updateMethod = moverType.GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
            if (updateMethod != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_Mover).GetMethod("Postfix"));
                harmony.Patch(updateMethod, null, postfix);
                Debug.Log("[MyMod] Patched Mover.Update");
            }
            else
            {
                Debug.LogError("[MyMod] Could not find Mover.Update method!");
            }
        }

        private static FieldInfo _moveSpeedField;

        public static void Postfix(Mover __instance)
        {
            if (!Main.Enabled || Main.speedMultiplier <= 1) return;

            try
            {
                var player = __instance.GetComponent<Player>();
                if (player == null) return;

                // 缓存 FieldInfo，避免每帧反射查找（GetField 开销大）
                if (_moveSpeedField == null)
                {
                    _moveSpeedField = typeof(Mover).GetField("_moveSpeed", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (_moveSpeedField == null) return;
                }

                float moveSpeed = (float)_moveSpeedField.GetValue(__instance);
                if (moveSpeed > 0)
                {
                    float newSpeed = moveSpeed;
                    if (newSpeed < 5f)
                    {
                        newSpeed = Mathf.Min(moveSpeed * Main.speedMultiplier, 15f);
                        _moveSpeedField.SetValue(__instance, newSpeed);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Error in Mover patch: " + e.ToString());
            }
        }
    }
}
