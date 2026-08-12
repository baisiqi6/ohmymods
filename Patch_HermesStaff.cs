using System;
using System.Reflection;
using UnityEngine;
using Harmony;

namespace MyMod
{
    /// <summary>
    /// 神器权杖（HermesStaff）数值 patch —— 迁移自 legacy DLL 直改（docs/legacy-dll-mod.md 希腊清单）：
    ///   1. 控制数量：8 → 16（_maximumConvertedTrolls）
    ///   2. 控制时间：**2.0.1 原生已永久，无需 patch**——FriendlyTroll.ShouldRevertToTroll()
    ///      反编译源码就是 return false（FriendlyTroll.cs:129-131），_expirationTime 只赋值从未
    ///      被读取；legacy"控制时间提升至永久"在 2.0.1 已对齐（旧版本才需要改）。
    ///
    /// ================= 数量上限分析（_maximumConvertedTrolls vs +8）=================
    /// HermesStaff.StartAbilityRoutine（HermesStaff.cs:70-73）：
    ///     GameObject[] array;
    ///     int num2 = this._trollScanner.GetAll(out array);
    ///     num2 = Mathf.Min(num2, this._maximumConvertedTrolls + 8);
    /// 实际转换数 = min(扫描到的巨魔数, _maximumConvertedTrolls + 8)。
    /// 注意代码里还有一个固定的 "+8"：这是能力代码自带的溢出余量（与序列化字段独立），
    /// 不是字段的一部分。因此：
    ///   - 原生默认 _maximumConvertedTrolls = 8（HermesStaff.cs:138）→ 有效上限 = 8 + 8 = 16，
    ///     即游戏原生其实已能控 16 个（+8 余量把名义 8 抬到 16）。
    ///   - legacy 需求"神器权杖控制数量增至 16 个"：docs/legacy-dll-mod.md 的状态对照结论
    ///     "权杖控 16 个（HermesStaff._maximumConvertedTrolls=8）→ 控 16 = 改 16"，
    ///     即 legacy 直改是把字段 _maximumConvertedTrolls 从 8 改成 16（余量 +8 未动）。
    ///   - 本 patch 忠实迁移该数值编辑：Awake 后 SetValue(_maximumConvertedTrolls, 16)
    ///     → 有效上限 = 16 + 8 = 24，保证至少控 16 个。
    ///   - 反证：若要让"有效上限恰好 = 16"，字段应取 8（= 原生默认，等于不 patch），
    ///     与"增至 16 个"的需求目的不符。+8 余量属游戏设计，legacy 直改没动，我们也不动。
    ///
    /// ================= 控制时间分析（2.0.1 已对齐，不实现）=================
    /// FriendlyTroll.Init（FriendlyTroll.cs:12）：_expirationTime = Time.time + _duration；
    /// Awake（FriendlyTroll.cs:30）注册 FSM 条件转换 AddTransition(ShouldRevertToTroll → state 2)，
    /// 条件成立 → RevertToTrollRoutine → RevertToTrollInstant() 还原成敌人。
    /// 但 2.0.1 反编译源码中 ShouldRevertToTroll()（FriendlyTroll.cs:129-131）就是 return false，
    /// 且 _expirationTime 全库只赋值（:12）从未被读取（:503 仅字段声明）——
    /// **原生行为 = 友好巨魔永不变回**。legacy"控制时间提升至永久"在 2.0.1 已满足，
    /// 无需任何 patch。若未来游戏版本恢复 revert，再在 FriendlyTroll.ShouldRevertToTroll
    /// 挂 prefix 强制 return false（Harmony 1.2 支持 ref bool __result）。
    /// </summary>
    public static class Patch_HermesStaff
    {
        public static void Register(HarmonyInstance harmony)
        {
            // ---- 1) 控制数量：HermesStaff 初始化时把 _maximumConvertedTrolls 8 → 16 ----
            // 时机选 Awake：序列化字段在 Awake 前由 Unity 赋值，postfix 在 Awake 之后写 16
            // 不会被覆盖；权杖是拾取型神器，实例由世界生成，首次激活前 Awake 必然已跑，
            // 字段值全程生效（读取点只有 StartAbilityRoutine:73 一处）。
            var staffType = typeof(HermesStaff);
            var awakeMethod = staffType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            if (awakeMethod != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_HermesStaff).GetMethod("HermesStaff_Awake_Postfix"));
                harmony.Patch(awakeMethod, null, postfix);
                Debug.Log("[MyMod] Patched HermesStaff.Awake (control count 8 -> 16)");
            }
            else
            {
                Debug.LogError("[MyMod] HermesStaff.Awake not found!");
            }
            // 2.1.0 修复：ShouldRevertToTroll 实现为 `_expirationTime <= Time.time`
            // （_duration=5f，控制 5 秒后变回敌人；2.0.1 是恒 return false 才"原生永久"）。
            // legacy"控制时间永久"在此版本需要 patch：prefix 强制返回 false。
            var trollType = typeof(FriendlyTroll);
            var shouldRevertMethod = trollType.GetMethod("ShouldRevertToTroll",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (shouldRevertMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_HermesStaff).GetMethod("FriendlyTroll_ShouldRevertToTroll_Prefix"));
                harmony.Patch(shouldRevertMethod, prefix, null);
                Debug.Log("[MyMod] Patched FriendlyTroll.ShouldRevertToTroll (permanent control)");
            }
            else
            {
                Debug.LogError("[MyMod] FriendlyTroll.ShouldRevertToTroll not found!");
            }
        }

        /// <summary>
        /// 控制永久：mod 启用时强制返回 false 并跳过原方法（revert 永不触发）；
        /// mod 关闭时返回 true 走原逻辑（可开关）。Harmony 1.2 支持 ref bool __result
        /// （与本仓库 Patch_Castle.CreateItem_Prefix 一致）。
        /// </summary>
        public static bool FriendlyTroll_ShouldRevertToTroll_Prefix(ref bool __result)
        {
            if (!Main.Enabled) return true;  // 未启用：走原方法（5 秒控制）

            __result = false;                // 永不 revert → 控制时间永久
            return false;                    // 跳过原方法
        }

        /// <summary>
        /// HermesStaff 初始化后写入目标值（设置型，不做乘法叠加）。
        /// </summary>
        public static void HermesStaff_Awake_Postfix(HermesStaff __instance)
        {
            if (!Main.Enabled) return;

            try
            {
                var field = typeof(HermesStaff).GetField("_maximumConvertedTrolls", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(__instance, 16);
                }
                else
                {
                    Debug.LogError("[MyMod] HermesStaff._maximumConvertedTrolls not found!");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] HermesStaff.Awake patch error: " + e.Message);
            }
        }

    }
}
