using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using Harmony;

namespace MyMod
{
    /// <summary>
    /// 单位缩放注册表——"y 轴守护"机制的核心。
    ///
    /// 原理：游戏用 transform.localScale.x 的符号（±1）做朝向翻转，Mover.Update
    /// 每帧把整个 localScale 覆盖为 (±1, 1, 1)——y 被写死成 1，任何一次性缩放
    /// 设置都会在下一帧被清零（这就是之前缩放"不生效"的根因）。
    ///
    /// 解法：各单位 OnEnable（出生/池复用）时把"目标 y 缩放"登记到这里，
    /// Mover.Update 的 postfix 每帧检查并恢复 y 值。x 保持 Mover 写的 ±1（朝向，
    /// 且 Mover.cs:405 velocity.x *= localScale.x 依赖它，动 x 会改变移动速度），
    /// z 不动。
    ///
    /// 用 ConditionalWeakTable：key 弱引用——单位被销毁自动清理，池复用 OnEnable
    /// 重新登记覆盖旧值，转化（ReplaceBy 创建新对象）不影响旧登记。无泄漏。
    /// </summary>
    public static class UnitScaleRegistry
    {
        private sealed class ScaleValue
        {
            public readonly float Y;
            public ScaleValue(float y) { Y = y; }
        }

        private static readonly ConditionalWeakTable<Mover, ScaleValue> Targets =
            new ConditionalWeakTable<Mover, ScaleValue>();

        public static void Register(Mover mover, float y)
        {
            if (mover == null) return;
            Targets.Remove(mover);
            Targets.Add(mover, new ScaleValue(y));
        }

        public static bool TryGet(Mover mover, out float y)
        {
            ScaleValue v;
            if (Targets.TryGetValue(mover, out v))
            {
                y = v.Y;
                return true;
            }
            y = 1f;
            return false;
        }
    }
    public static class Patch_Worker
    {
        public static void Register(HarmonyInstance harmony)
        {
            // Worker 没有 Start 方法！只有 private OnEnable。
            // 对象池游戏：Pool.Spawn 复用对象走 SetActive(true) → OnEnable 每次出生都触发，
            // Awake/Start 只在首次创建时跑一次（池复用不触发）。
            var workerType = typeof(Worker);
            var onEnableMethod = workerType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
            if (onEnableMethod != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_Worker).GetMethod("OnEnable_Postfix"));
                harmony.Patch(onEnableMethod, null, postfix);
                Debug.Log("[MyMod] Patched Worker.OnEnable");
            }
            else
            {
                Debug.LogError("[MyMod] Worker.OnEnable not found!");
            }
        }

        /// <summary>
        /// 北境工匠（Worker_norselands，有 NpcShieldUser 组件）→ 1.3 + 出生带盾
        /// 希腊/通用工匠（无 NpcShieldUser）→ 1.075（原缩放）
        /// </summary>
        private static bool IsNorselandsWorker(Worker worker)
        {
            return worker != null && worker.GetComponent<NpcShieldUser>() != null;
        }

        private static void ApplyWorkerScale(Worker worker)
        {
            if (worker == null) return;
            // 只设 y：x 是 Mover 的朝向符号（动它会改变移动速度），缩放只体现在 y
            float s = IsNorselandsWorker(worker) ? 1.175f : 1.075f;
            Vector3 v = worker.transform.localScale;
            v.y = s;
            worker.transform.localScale = v;
        }

        /// <summary>
        /// 北境工匠出生时自动装备盾牌（SetShieldEnabled(true) 直接启用盾牌对象，
        /// 不需要从盾牌商店购买——希腊 12/13 槽位被狂战士商店占用，没有盾牌商店）。
        /// 2.1.0 修复：NpcShieldUser.Awake 在 NetworkBigBoss.HasWorldAuth 未就绪时提前
        /// return（组件禁用、regenWait 未初始化）→ SetShieldEnabled 内部 StartShieldRegen
        /// 的 ShieldRegenRoutine yield regenWait 为 null → NRE。装备前反射补初始化。
        /// </summary>
        private static void EquipShieldIfNorselands(Worker worker)
        {
            if (worker == null) return;
            try
            {
                NpcShieldUser shieldUser = worker.GetComponent<NpcShieldUser>();
                if (shieldUser == null || shieldUser.HasShield()) return;

                var regenWaitField = typeof(NpcShieldUser).GetField("regenWait",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (regenWaitField != null && regenWaitField.GetValue(shieldUser) == null)
                {
                    regenWaitField.SetValue(shieldUser, new WaitForSeconds(1f));
                }

                shieldUser.SetShieldEnabled(true, 0);
                Debug.Log("[MyMod] Norselands worker equipped with shield");
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] EquipShield error: " + e.Message);
            }
        }

        public static void OnEnable_Postfix(Worker __instance)
        {
            if (!Main.Enabled) return;

            try
            {
                ApplyWorkerScale(__instance);
                // 登记 y 轴守护：Mover.Update 每帧会把 localScale.y 覆盖回 1，
                // 注册表让 postfix 恢复目标值（x 是朝向符号不能动，缩放只体现在 y）
                UnitScaleRegistry.Register(__instance.GetComponent<Mover>(),
                    IsNorselandsWorker(__instance) ? 1.175f : 1.075f);
                EquipShieldIfNorselands(__instance);
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Error in Worker.OnEnable patch: " + e.Message);
            }
        }
    }

    /// <summary>
    /// 单位缩放（性能优化版）：
    /// 原实现 patch Mover.Update 每帧对每个移动单位做 4 次 GetComponent（掉帧源）。
    /// 现改为各单位出生时（Start/Awake）一次性设置缩放，零每帧开销。
    /// 差异：WarriorPeasant 的 1.2 缩放从"首次检查时点在希腊"改为"出生时点在希腊"。
    /// </summary>
    public static class Patch_WorkerScale
    {
        public static void Register(HarmonyInstance harmony)
        {
            // y 轴守护：Mover.Update 每帧把 localScale 覆盖为 (±1, 1, 1)，
            // postfix 按注册表恢复 y 缩放（x 是朝向符号，动它会改变移动速度）。
            var moverType = typeof(Mover);
            var moverUpdate = moverType.GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
            if (moverUpdate != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_WorkerScale).GetMethod("Mover_Update_Postfix"));
                harmony.Patch(moverUpdate, null, postfix);
                Debug.Log("[MyMod] Patched Mover.Update (y-scale guard)");
            }
            else
            {
                Debug.LogError("[MyMod] Mover.Update not found!");
            }

            // Worker 缩放——Patch_Worker.OnEnable_Postfix 已处理（1.075/1.3+盾），这里不重复。
            // 全部用 OnEnable：对象池复用走 SetActive(true)，只有 OnEnable 每次出生都触发。

            // WarriorPeasant：希腊世界 1.2（每次激活判断）
            var wpType = typeof(WarriorPeasant);
            var wpOnEnable = wpType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
            if (wpOnEnable != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_WorkerScale).GetMethod("WarriorPeasant_OnEnable_Postfix"));
                harmony.Patch(wpOnEnable, null, postfix);
            }

            // Deer：0.55
            var deerType = typeof(Deer);
            var deerOnEnable = deerType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
            if (deerOnEnable != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_WorkerScale).GetMethod("Deer_OnEnable_Postfix"));
                harmony.Patch(deerOnEnable, null, postfix);
            }

            // Critter：1.8
            var critterType = typeof(Critter);
            var critterOnEnable = critterType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
            if (critterOnEnable != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_WorkerScale).GetMethod("Critter_OnEnable_Postfix"));
                harmony.Patch(critterOnEnable, null, postfix);
            }

            // Peasant：北境居民 1.2（覆盖初始/读档/招募所有生成路径）
            var peasantType = typeof(Peasant);
            var peasantOnEnable = peasantType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
            if (peasantOnEnable != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_WorkerScale).GetMethod("Peasant_OnEnable_Postfix"));
                harmony.Patch(peasantOnEnable, null, postfix);
            }

            Debug.Log("[MyMod] Patched unit scaling (OnEnable + y-guard)");
        }

        /// <summary>
        /// y 轴守护核心：Mover.Update 每帧写 localScale=(±1,1,1) 后，恢复登记单位的 y 缩放。
        /// x 保持 Mover 写的朝向符号（±1），z 不动。已登记单位每帧一次字典查找 + 一次比较，
        /// 未变则零写入——开销可忽略（远小于旧版每帧 4 次 GetComponent）。
        /// </summary>
        public static void Mover_Update_Postfix(Mover __instance)
        {
            if (!Main.Enabled) return;
            float targetY;
            if (!UnitScaleRegistry.TryGet(__instance, out targetY)) return;
            if (targetY == 1f) return;

            Vector3 s = __instance.transform.localScale;
            if (Mathf.Abs(s.y - targetY) > 0.0001f)
            {
                s.y = targetY;
                __instance.transform.localScale = s;
            }
        }

        public static void WarriorPeasant_OnEnable_Postfix(WarriorPeasant __instance)
        {
            if (!Main.Enabled) return;
            try
            {
                if (BiomeHolder.Inst != null && BiomeHolder.Inst.BiomeIndex == 5)
                {
                    __instance.transform.localScale = new Vector3(1f, 1.2f, 1f);
                    UnitScaleRegistry.Register(__instance.GetComponent<Mover>(), 1.2f);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] WarriorPeasant scaling error: " + e.Message);
            }
        }

        public static void Deer_OnEnable_Postfix(Deer __instance)
        {
            if (!Main.Enabled) return;
            try
            {
                __instance.transform.localScale = new Vector3(1f, 0.55f, 1f);
                UnitScaleRegistry.Register(__instance.GetComponent<Mover>(), 0.55f);
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Deer scaling error: " + e.Message);
            }
        }

        public static void Critter_OnEnable_Postfix(Critter __instance)
        {
            if (!Main.Enabled) return;
            try
            {
                __instance.transform.localScale = new Vector3(1f, 1.8f, 1f);
                UnitScaleRegistry.Register(__instance.GetComponent<Mover>(), 1.8f);
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Critter scaling error: " + e.Message);
            }
        }

        /// <summary>
        /// 北境居民（Peasant_norselands，名字含 norselands）→ 1.2
        /// 希腊居民保持原样。Peasant 没有 NpcShieldUser 那样的组件标识，
        /// 用 GameObject 名字判断（存档恢复可能带 (Clone) 后缀，用 Contains）。
        /// </summary>
        public static void Peasant_OnEnable_Postfix(Peasant __instance)
        {
            if (!Main.Enabled) return;
            try
            {
                string name = __instance.gameObject.name;
                if (name.Contains("Peasant_norselands"))
                {
                    __instance.transform.localScale = new Vector3(1f, 1.125f, 1f);
                    UnitScaleRegistry.Register(__instance.GetComponent<Mover>(), 1.125f);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Peasant scaling error: " + e.Message);
            }
        }
    }
}
