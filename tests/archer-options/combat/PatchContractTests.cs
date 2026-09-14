using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KingdomEnhancedMod;
using Xunit;

namespace KingdomArcherOptions.Combat.Tests
{
    /// <summary>
    /// Integration contract: Harmony discovers patch methods by name on attributed classes and binds
    /// every parameter by name. A silent mismatch there disables a hook with no in-game error, so the
    /// wrapper surface (targets, attributes, parameter names, priorities) is pinned. The nested
    /// Deadlands ordering itself is verified by the operator against the real Harmony runtime.
    /// </summary>
    public class PatchContractTests
    {
        private const BindingFlags StaticPrivate = BindingFlags.NonPublic | BindingFlags.Static;

        [Theory]
        [InlineData(typeof(ArrowAttack_FireArrowInternal_Scatter_Patch), "ArrowAttack", "FireArrowInternal", "Postfix")]
        [InlineData(typeof(Arrow_OnEnable_ExtraLedger_Patch), "Arrow", "OnEnable", "Prefix")]
        [InlineData(typeof(Archer_OnEnable_RateLifetime_Patch), "Archer", "OnEnable", "Prefix")]
        [InlineData(typeof(Archer_Update_RateCadence_Patch), "Archer", "Update", "Prefix")]
        [InlineData(typeof(Archer_Update_RateCadence_Patch), "Archer", "Update", "Postfix")]
        [InlineData(typeof(Archer_Update_RateCadence_Patch), "Archer", "Update", "Finalizer")]
        [InlineData(typeof(Archer_ShootCoroutine_OptionsCadence_Patch), "_Shoot_d__225", "MoveNext", "Prefix")]
        [InlineData(typeof(Archer_ShootCoroutine_OptionsCadence_Patch), "_Shoot_d__225", "MoveNext", "Finalizer")]
        public void WrapperDeclaresAHarmonyDiscoverablePatchMethod(Type patchClass, string declaringType, string nativeMethod, string patchMethod)
        {
            HarmonyPatchAttribute attribute = patchClass
                .GetCustomAttributes(typeof(HarmonyPatchAttribute), false)
                .Cast<HarmonyPatchAttribute>()
                .Single();

            Assert.Equal(declaringType, attribute.DeclaringType.Name);
            Assert.Equal(nativeMethod, attribute.MethodName);

            MethodInfo method = patchClass.GetMethod(patchMethod, StaticPrivate);
            Assert.NotNull(method);
            Assert.True(HasPatchAttribute(method, patchMethod), patchClass.Name + "." + patchMethod + " lacks its Harmony attribute");
        }

        [Fact]
        public void PatchParametersUseTheNamesHarmonyBinds()
        {
            Assert.Equal(
                new[] { "__instance", "source", "arrowPosition", "isPerfectShot", "shootForce" },
                ParameterNames(typeof(ArrowAttack_FireArrowInternal_Scatter_Patch), "Postfix"));
            Assert.Equal(new[] { "__instance" }, ParameterNames(typeof(Arrow_OnEnable_ExtraLedger_Patch), "Prefix"));
            Assert.Equal(new[] { "__instance" }, ParameterNames(typeof(Archer_OnEnable_RateLifetime_Patch), "Prefix"));
            Assert.Equal(new[] { "__instance" }, ParameterNames(typeof(Archer_Update_RateCadence_Patch), "Prefix"));
            Assert.Equal(new[] { "__instance" }, ParameterNames(typeof(Archer_Update_RateCadence_Patch), "Postfix"));
            Assert.Equal(new[] { "__instance", "__exception" }, ParameterNames(typeof(Archer_Update_RateCadence_Patch), "Finalizer"));
            Assert.Equal(new[] { "__state", "__instance" }, ParameterNames(typeof(Archer_ShootCoroutine_OptionsCadence_Patch), "Prefix"));
            Assert.Equal(
                new[] { "__state", "__exception", "__instance" },
                ParameterNames(typeof(Archer_ShootCoroutine_OptionsCadence_Patch), "Finalizer"));
        }

        [Fact]
        public void PatchPrioritiesImplementTheDeadlandsNestingContract()
        {
            // DL's interval prefix keeps the default priority (400): ours must run after it...
            Assert.Equal(Priority.Last, PriorityOf(typeof(Archer_ShootCoroutine_OptionsCadence_Patch), "Prefix"));
            // ...and our finalizer must run before DL's default-priority finalizer restores its snapshot.
            Assert.Equal(Priority.First, PriorityOf(typeof(Archer_ShootCoroutine_OptionsCadence_Patch), "Finalizer"));
            // Timer: snapshot before the DL/native decrements, drain the observed amount after both.
            Assert.Equal(Priority.First, PriorityOf(typeof(Archer_Update_RateCadence_Patch), "Prefix"));
            Assert.Equal(Priority.Last, PriorityOf(typeof(Archer_Update_RateCadence_Patch), "Postfix"));
        }

        [Fact]
        public void ModuleClassExposesNoBarePatchMethodNamesAndKeepsItsTickEntryPoint()
        {
            MethodInfo[] helpers = typeof(PatchArcher_Options)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(m => m.Name == "Prefix" || m.Name == "Postfix" || m.Name == "Finalizer")
                .ToArray();

            Assert.Empty(helpers);
            Assert.NotNull(typeof(PatchArcher_Options).GetMethod("Tick", StaticPrivate));
        }

        private static string[] ParameterNames(Type patchClass, string patchMethod)
            => patchClass.GetMethod(patchMethod, StaticPrivate)
                .GetParameters()
                .Select(p => p.Name)
                .ToArray();

        private static int PriorityOf(Type patchClass, string patchMethod)
            => patchClass.GetMethod(patchMethod, StaticPrivate)
                .GetCustomAttribute<HarmonyPriorityAttribute>()
                ?.Priority ?? Priority.Normal;

        private static bool HasPatchAttribute(MethodInfo method, string patchMethod)
        {
            if (patchMethod == "Prefix") return method.GetCustomAttribute<HarmonyPrefixAttribute>() != null;
            if (patchMethod == "Postfix") return method.GetCustomAttribute<HarmonyPostfixAttribute>() != null;
            return method.GetCustomAttribute<HarmonyFinalizerAttribute>() != null;
        }
    }
}
