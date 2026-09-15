using System;
using System.Reflection;
using HarmonyLib;
using KingdomEnhancedMod;
using Xunit;

namespace KingdomGreekImpact.Tests
{
    /// <summary>
    /// 钩子契约：只挂在已核 native 入口（FireArrowInternal / Arrow.OnEnable），
    /// 命中路径不重复挂钩（由既有 HitObject wrapper 调用模块 API）。
    /// </summary>
    public sealed class HookContractTests
    {
        private static HarmonyPatch PatchOf(Type type)
        {
            object[] attributes = type.GetCustomAttributes(typeof(HarmonyPatch), false);
            Assert.Single(attributes);
            return (HarmonyPatch)attributes[0];
        }

        private static MethodInfo Declared(Type type, string name)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return method;
        }

        [Fact]
        public void ShotHooks_TargetAuditedNativeEntries()
        {
            Type scopeHook = typeof(ArrowAttack_FireArrowInternal_GreekImpact_Patch);
            HarmonyPatch scopePatch = PatchOf(scopeHook);
            Assert.Equal(typeof(ArrowAttack), scopePatch.TargetType);
            Assert.Equal("FireArrowInternal", scopePatch.MethodName);
            Assert.NotNull(Declared(scopeHook, "Prefix").GetCustomAttribute<HarmonyPrefix>());
            Assert.NotNull(Declared(scopeHook, "Finalizer").GetCustomAttribute<HarmonyFinalizer>());

            Type damageHook = typeof(Arrow_TryDamage_GreekImpact_Patch);
            HarmonyPatch damagePatch = PatchOf(damageHook);
            Assert.Equal(typeof(Arrow), damagePatch.TargetType);
            Assert.Equal("TryDamage", damagePatch.MethodName);      // 已授权的窄伤害入口
            Assert.NotNull(Declared(damageHook, "Prefix").GetCustomAttribute<HarmonyPrefix>());

            Type lifeHook = typeof(Arrow_OnEnable_GreekImpact_Patch);
            HarmonyPatch lifePatch = PatchOf(lifeHook);
            Assert.Equal(typeof(Arrow), lifePatch.TargetType);
            Assert.Equal("OnEnable", lifePatch.MethodName);
            Assert.NotNull(Declared(lifeHook, "Prefix").GetCustomAttribute<HarmonyPrefix>());
            Assert.NotNull(Declared(lifeHook, "Postfix").GetCustomAttribute<HarmonyPostfix>());
        }

        [Fact]
        public void NoSecondHitObjectHook_AndNoCustomRpcSurface()
        {
            Assembly assembly = typeof(ArrowAttack_FireArrowInternal_GreekImpact_Patch).Assembly;
            foreach (Type type in assembly.GetTypes())
            {
                object[] attributes = type.GetCustomAttributes(typeof(HarmonyPatch), false);
                if (attributes.Length == 0) continue;
                HarmonyPatch patch = (HarmonyPatch)attributes[0];
                bool isHitObject = patch.TargetType == typeof(Arrow) && patch.MethodName == "HitObject";
                if (!isHitObject) continue;
                Assert.NotEqual("KingdomEnhancedMod.Arrow_HitObject_GreekImpact_Patch", type.FullName);
            }
            // 命中路径走既有 wrapper 的 API（签名契约）
            Assert.NotNull(typeof(PatchArcher_GreekImpact).GetMethod("BeginHit", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public));
            Assert.NotNull(typeof(PatchArcher_GreekImpact).GetMethod("EndHit", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public));
            Assert.NotNull(typeof(PatchArcher_GreekImpact).GetMethod("AbortHit", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public));
        }

        [Fact]
        public void OperatorFacingApi_HasExpectedSignatures()
        {
            BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            Assert.Equal(typeof(bool), typeof(PatchArcher_GreekImpact).GetMethod("IsEligibleArrow", flags).ReturnType);
            Assert.NotNull(typeof(PatchArcher_GreekImpact).GetMethod("Tick", flags));
            Assert.NotNull(typeof(PatchArcher_GreekImpact).GetMethod("ReleaseAll", flags));
            Assert.NotNull(typeof(PatchArcher_GreekImpact).GetMethod("OnArcherEnable", flags));
            Assert.NotNull(typeof(PatchArcher_GreekImpact).GetMethod("OnArrowEnable", flags));
            Assert.NotNull(typeof(PatchArcher_GreekImpact).GetMethod("OnArrowSpawned", flags));
            Assert.NotNull(typeof(PatchArcher_GreekImpact).GetMethod("BeginShot", flags));
            Assert.NotNull(typeof(PatchArcher_GreekImpact).GetMethod("EndShot", flags));
            Assert.Equal(typeof(bool), typeof(PatchArcher_GreekImpact).GetMethod("EndHit", flags).ReturnType);
            Assert.NotNull(typeof(PatchArcher_GreekImpact).GetMethod("AbortHit", flags));
            Assert.NotNull(typeof(PatchArcher_GreekImpact).GetMethod("TrySubstituteDirectDamage", flags));
        }
    }
}
