using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using KingdomEnhancedMod;
using UnityEngine;

internal static class Check
{
    internal static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    internal static void False(bool condition, string message)
    {
        if (condition) throw new Exception(message);
    }

    internal static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(message + " [expected=" + expected + " actual=" + actual + "]");
    }

    internal static void Near(double expected, double actual, double tolerance, string message)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new Exception(message + " [expected=" + expected + " actual=" + actual + "]");
    }
}

internal static class Case
{
    internal static int Passed;
    internal static int Failed;
    internal static readonly List<string> Failures = new List<string>();

    internal static void Run(string name, Action body)
    {
        try
        {
            body();
            Passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception e)
        {
            Failed++;
            Failures.Add(name + ": " + e.Message);
            Console.WriteLine("FAIL " + name + ": " + e.Message);
        }
    }
}

namespace Harness
{
    /// <summary>
    /// Wires the stub native entry points to the production Harmony hook bodies (prefix -> body ->
    /// postfix, finalizer with the pending exception), so activation, TopUp, TryDirected and the
    /// cleanup/maintenance paths run as the real PatchWorld_FleetBoatFormation code.
    /// </summary>
    internal static class ProductionHooks
    {
        private static readonly Type FleetPatch = typeof(PatchWorld_FleetBoatFormation);
        private static readonly Type MusketeerPatch = typeof(PatchMusketeerFormation);

        private static MethodInfo _activatePrefix, _activatePostfix, _activateFinalizer;
        private static MethodInfo _unregisterPrefix, _unregisterPostfix;
        private static MethodInfo _disablePostfix;
        private static MethodInfo _guardPrefix;

        private static MethodInfo Hook(Type owner, string nestedName, string methodName)
        {
            Type nested = owner.GetNestedType(nestedName, BindingFlags.NonPublic);
            if (nested == null) throw new InvalidOperationException("missing hook type " + nestedName);
            MethodInfo method = nested.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) throw new InvalidOperationException("missing hook " + nestedName + "." + methodName);
            return method;
        }

        private static Exception Invoke(MethodInfo method, object[] args)
        {
            try
            {
                method.Invoke(null, args);
                return null;
            }
            catch (TargetInvocationException e)
            {
                return e.InnerException ?? e;
            }
        }

        internal static bool ArcherTryRecruitAllowed(Archer archer, Formation formation)
        {
            if (_guardPrefix == null) _guardPrefix = Hook(MusketeerPatch, "ArcherTryRecruitGuard", "Prefix");
            return (bool)_guardPrefix.Invoke(null, new object[] { archer, formation });
        }

        internal static void ActivateFormation(Player player)
        {
            if (_activatePrefix == null)
            {
                _activatePrefix = Hook(FleetPatch, "PlayerActivateFormationPatch", "Prefix");
                _activatePostfix = Hook(FleetPatch, "PlayerActivateFormationPatch", "Postfix");
                _activateFinalizer = Hook(FleetPatch, "PlayerActivateFormationPatch", "Finalizer");
            }
            object[] args = { player, null };
            Exception pending = Invoke(_activatePrefix, args);
            if (pending == null)
            {
                try { player.ActivateBody(); }
                catch (Exception e) { pending = e; }
                if (pending == null)
                    pending = Invoke(_activatePostfix, new object[] { player, args[1] });
            }
            object result = _activateFinalizer.Invoke(null, new object[] { pending, args[1] });
            if (result is Exception rethrow) throw rethrow;
        }

        internal static object UnregisterUnitPrefix(Formation formation, Formation.IFormationUnit unit)
        {
            if (_unregisterPrefix == null)
            {
                _unregisterPrefix = Hook(FleetPatch, "FormationUnregisterUnitPatch", "Prefix");
                _unregisterPostfix = Hook(FleetPatch, "FormationUnregisterUnitPatch", "Postfix");
            }
            object[] args = { formation, unit, null };
            Exception failure = Invoke(_unregisterPrefix, args);
            if (failure != null) throw failure;
            return args[2];
        }

        internal static void UnregisterUnitPostfix(object state)
        {
            Exception failure = Invoke(_unregisterPostfix, new object[] { state });
            if (failure != null) throw failure;
        }

        internal static void FormationOnDisablePostfix(Formation formation)
        {
            if (_disablePostfix == null) _disablePostfix = Hook(FleetPatch, "FormationOnDisablePatch", "Postfix");
            Exception failure = Invoke(_disablePostfix, new object[] { formation });
            if (failure != null) throw failure;
        }
    }

    /// <summary>Fresh world per test with the real 2.4 Player formation baseline and spacing.</summary>
    internal static class Fixture
    {
        internal static readonly Formation.UnitTypes[] Baseline24 =
        {
            Formation.UnitTypes.FleetBoat, Formation.UnitTypes.Gap, Formation.UnitTypes.Gap,
            Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
            Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
            Formation.UnitTypes.Gap,
            Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen,
            Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen
        };

        // Operator-provided 2.4 Player resource spacing; index = Formation.UnitTypes.
        internal static readonly float[] Spacing24 =
        {
            0.21875f, 0.25f, 0.25f, 0.21875f, 1f, 0.34375f, 1f,
            1f, 0.21875f, 0.21875f, 0.21875f, 0f, 0f
        };

        internal static Transform Root;
        internal static Player Player;
        internal static Formation Formation;

        internal static void Reset(float playerX = 10f)
        {
            ReleasePreviousFixture();

            Time.unscaledTime = 0f;
            Time.timeScale = 1f;
            NetworkBigBoss.HasWorldAuth = true;
            NetworkBigBoss.IsOnline = false;
            ModConfig.Enabled.Value = true;
            ModConfig.MusketeerEnabled.Value = true;
            MusketeerAccess.EnabledFlag = true;
            MusketeerAccess.InWorldResult = true;
            MusketeerIdentity.Units.Clear();
            MusketeerIdentity.MarkedEnabled = true;
            HeroArcherRuntime.Heroes.Clear();
            CrossbowmanLifecycle.Crossbowmen.Clear();
            CrossbowmanLifecycle.IdentityEnabled = false;
            Player.ThrowInActivateBody = 0;
            Il2CppStructArray<Formation.UnitTypes>.ThrowBeforeApplyOnAttempt = 0;
            Il2CppStructArray<Formation.UnitTypes>.ApplyThenThrowOnAttempt = 0;
            Il2CppStructArray<Formation.UnitTypes>.FailRestoreFromAttempt = 0;
            Il2CppStructArray<Formation.UnitTypes>.ResetSetAttempts();
            MusketeerRuntime.ResetLeases();
            KingdomEnhancedPlugin.Instance.LogSource.Infos.Clear();
            KingdomEnhancedPlugin.Instance.LogSource.Warnings.Clear();

            Root = new GameObject().transform;
            Managers.Inst = new Managers
            {
                kingdom = new Kingdom(),
                world = new World { gameLayer = Root },
                game = new Game { state = Game.State.Playing }
            };

            Player = Create<Player>(Root, playerX);
            Formation = Create<Formation>(Root, 0f);
            ApplyBaseline24(Formation);
            Formation.enabled = false;
            Player._formation = Formation;
        }

        private static void ReleasePreviousFixture()
        {
            if (Formation == null || Formation.units == null) return;
            for (int i = 0; i < Formation.units.Length; i++)
            {
                if (Formation.units[i] != null) Formation.UnregisterUnit(Formation.units[i]);
            }
            Formation.enabled = false;
        }

        internal static T Create<T>(Transform parent, float x) where T : Component, new()
        {
            var gameObject = new GameObject();
            gameObject.transform.parent = parent;
            T component = gameObject.Add<T>();
            component.transform.position = new Vector3(x, 0f, 0f);
            return component;
        }

        internal static void ApplyBaseline24(Formation formation)
        {
            formation.unitTypes = new Il2CppStructArray<Formation.UnitTypes>(Baseline24.Length);
            for (int i = 0; i < Baseline24.Length; i++) formation.unitTypes[i] = Baseline24[i];
            formation.units = new Il2CppReferenceArray<Formation.IFormationUnit>(Baseline24.Length);
            formation.UnitSpacing = new Il2CppStructArray<float>(Spacing24.Length);
            for (int i = 0; i < Spacing24.Length; i++) formation.UnitSpacing[i] = Spacing24[i];
            formation.startOffset = 0f;
            formation.side = Side.Left;
        }

        internal static Archer AddMusketeer(float x)
        {
            Archer archer = Create<Archer>(Root, x);
            MusketeerIdentity.Units.Add(archer);
            MusketeerRuntime.ArmNewLife(archer);   // an applied package exists: lease > 0
            return archer;
        }

        internal static Archer AddArcher(float x) => Create<Archer>(Root, x);

        internal static FleetBoat AddBoat(int number, Side side)
        {
            FleetBoat boat = Create<FleetBoat>(Root, 0f);
            boat.Side = side;
            boat._boatNumber = number;
            boat._fsm.Current = FleetBoat.State.Idle;
            Managers.Inst.kingdom.FleetBoats.Add(boat);
            return boat;
        }

        internal static FleetBoatFormationCoordinator Coordinator()
            => Formation.GetComponent<FleetBoatFormationCoordinator>();

        internal static void Tick()
        {
            Time.unscaledTime += 1f;
            FleetBoatFormationCoordinator coordinator = Coordinator();
            if (coordinator != null) PatchWorld_FleetBoatFormation.TickCoordinator(coordinator);
        }

        internal static void Furl() => Player.DeactivateFormation();

        internal static bool HasInfo(string needle)
            => KingdomEnhancedPlugin.Instance.LogSource.Infos.Exists(message => message.Contains(needle));

        internal static bool HasWarning(string needle)
            => KingdomEnhancedPlugin.Instance.LogSource.Warnings.Exists(message => message.Contains(needle));
    }
}
