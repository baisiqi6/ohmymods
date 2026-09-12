using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 单次只读资源调查：枚举当前世界骑士实际加载的 AnimatorController、参数和
/// 动画片段名称，确认旧版 Dash/Charge/Leap 资源是否仍存在。不会改动画、位置、
/// 无敌或状态机，也不执行每帧扫描。
/// </summary>
internal static class PatchRoles_KnightAnimatorDiag
{
    internal static IEnumerator Run(World world)
    {
        if (world == null) yield break;
        yield return new WaitForSeconds(3f);
        try
        {
            Knight[] knights = UnityEngine.Object.FindObjectsOfType<Knight>();
            var seen = new HashSet<IntPtr>();
            int samples = 0;
            for (int i = 0; i < knights.Length; i++)
            {
                Knight knight = knights[i];
                if (knight == null || knight.gameObject == null) continue;
                Animator animator = knight.GetComponent<Animator>();
                RuntimeAnimatorController controller = animator != null
                    ? animator.runtimeAnimatorController : null;
                if (controller == null || !seen.Add(controller.Pointer)) continue;
                samples++;

                var clips = controller.animationClips;
                var interesting = new List<string>();
                if (clips != null)
                {
                    for (int j = 0; j < clips.Length; j++)
                    {
                        AnimationClip clip = clips[j];
                        if (clip == null) continue;
                        string name = clip.name ?? string.Empty;
                        string lower = name.ToLowerInvariant();
                        if (lower.Contains("dash") || lower.Contains("charge")
                            || lower.Contains("leap") || lower.Contains("sprint"))
                            interesting.Add(name);
                    }
                }

                var parameters = new List<string>();
                if (animator != null && animator.parameters != null)
                {
                    AnimatorControllerParameter[] values = animator.parameters;
                    for (int j = 0; j < values.Length; j++)
                    {
                        if (values[j] != null) parameters.Add(values[j].name);
                    }
                }

                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[KnightAnimatorDiag] controller=" + controller.name
                    + " clips=" + (clips != null ? clips.Length : 0)
                    + " dashLike=" + (interesting.Count > 0
                        ? string.Join(",", interesting.ToArray()) : "<none>")
                    + " parameters=" + (parameters.Count > 0
                        ? string.Join(",", parameters.ToArray()) : "<none>"));
            }

            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[KnightAnimatorDiag] completed knights=" + knights.Length
                + " uniqueControllers=" + samples);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[KnightAnimatorDiag] " + e);
        }
    }
}

[HarmonyPatch(typeof(World), nameof(World.OnLevelLoaded))]
internal static class World_OnLevelLoaded_KnightAnimatorDiag_Patch
{
    [HarmonyPostfix]
    private static void Postfix(World __instance)
    {
        if (!ModConfig.Enabled.Value || __instance == null) return;
        try
        {
            __instance.StartCoroutine(
                PatchRoles_KnightAnimatorDiag.Run(__instance).WrapToIl2Cpp());
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[KnightAnimatorDiag/start] " + e);
        }
    }
}
