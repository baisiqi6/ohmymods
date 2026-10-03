// Test-side boundary stand-ins for MOD slices that surround (but are not part of) the
// purchase control chain. The real chain under test is HeroShop.OnPay ->
// HeroRecruitment.TryPurchase -> HeroArcherRuntime.TryActivatePurchased; the identity
// state machine (HeroArcherCore / HeroArcherRuntime) and the archive/persistence slices
// are the real production files. These stand-ins cover the engine-facing effect slices.
using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// Bridges the injected native owner type to the interop owner interface exactly like the
    /// real ClassInjector does by name/arity (HeroShopOwner.IsLocked takes the raw out pointer).
    /// </summary>
    internal static class OwnerInterfaceBridges
    {
        internal static T Cast<T>(object target) where T : class
        {
            if (typeof(T) == typeof(IPayableComponentOwner) && target is HeroShopOwner owner)
                return (T)(object)new HeroShopOwnerInterface(owner);
            return null;
        }

        private sealed class HeroShopOwnerInterface : IPayableComponentOwner
        {
            private readonly HeroShopOwner _owner;
            internal HeroShopOwnerInterface(HeroShopOwner owner) { _owner = owner; }
            public Il2CppSystem.Action<Player> OnPay => _owner.OnPay;
            public bool CanPay(Player player) => _owner.CanPay(player);
            public bool IsLocked(Player player, out LockIndicator.LockReason reason)
            {
                IntPtr pointer = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
                try
                {
                    bool locked = _owner.IsLocked(player, pointer);
                    reason = (LockIndicator.LockReason)System.Runtime.InteropServices.Marshal.ReadInt32(pointer);
                    return locked;
                }
                finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(pointer); }
            }
        }
    }

    /// <summary>
    /// Engine-side visual boundary. Production decides when Apply/Remove/NotifyRelease are
    /// called; the stand-in only answers "can the engine produce the sprite this time".
    /// HasVisual is per-actor: it is true only after production called Apply for that actor
    /// while the engine was able to produce the visual.
    /// </summary>
    internal static class HeroArcherVisuals
    {
        /// <summary>Engine switch for the acceptance scenario: can a visual be produced at all.</summary>
        internal static bool VisualProducible = true;
        private static readonly HashSet<Archer> Applied = new HashSet<Archer>();

        internal static bool AtlasUnavailable => false;
        internal static bool HasVisual(Archer archer) => archer != null && Applied.Contains(archer);
        internal static void Apply(Archer archer) { if (VisualProducible && archer != null) Applied.Add(archer); }
        internal static void Remove(Archer archer) { if (archer != null) Applied.Remove(archer); }
        internal static void NotifyRelease(Archer archer) { }
        internal static void Clear() { Applied.Clear(); }
    }

    /// <summary>Engine-side projectile/range effect boundary (production clone logic not exercised).</summary>
    internal static class HeroArcherRange
    {
        internal static bool Applicable = true;
        internal static bool TickResult = true;
        internal static bool Apply(Archer archer) => Applicable;
        internal static bool Tick(Archer archer) => TickResult;
        internal static void Restore(Archer archer) { }
        internal static void RetryCleanup() { }
        internal static void Clear() { }
    }

    /// <summary>Engine-side movement effect boundary.</summary>
    internal static class HeroArcherMovement
    {
        internal static void Reconcile(Archer archer) { }
        internal static void Restore(Archer archer) { }
        internal static void RetryCleanup() { }
        internal static void Clear() { }
    }

    /// <summary>Engine-side guard-facing effect boundary.</summary>
    internal static class HeroArcherGuardFacing
    {
        internal static void Tick() { }
        internal static void Evaluate(Archer archer) { }
        internal static void Restore(Archer archer) { }
        internal static bool HasOutstanding(int goId) => false;
        internal static void Clear() { }
    }

    /// <summary>Diagnostics boundary (no behaviour).</summary>
    internal static class HeroArcherLiveDiagnostics
    {
        internal static void OnHeroSetup(Archer archer) { }
        internal static void Clear() { }
    }

    /// <summary>Other mod-slice predicates; none of these consume the purchase chain.</summary>
    internal static class HeavyShieldIdentity
    {
        internal static bool ShieldPromotionInProgress;
        internal static bool IsKnownCareerRoot(UnityEngine.GameObject root) => false;
    }

    internal static class HeavyShieldPersistence
    {
        internal static bool ShieldLoadInProgress;
    }

    internal static class MusketeerIdentity
    {
        internal static bool IsUnit(Archer archer) => false;
    }

    internal static class PatchRoles_Crossbowman
    {
        internal static bool IsCrossbowman(Archer archer) => false;
    }

    internal static class PatchRoles_NorseSquad
    {
        internal static bool IsNorseArcherInstance(Archer archer) => false;
    }
}
