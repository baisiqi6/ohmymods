// 弩手举旗后排的真实生产组合回归（Issue 108 R3）。
//
// 编译：真实 PatchWorld_FleetBoatFormation + Patch_MusketeerFormation + Patch_CrossbowFormation
// + 真实 CrossbowmanLifecycle（Stubs.cs 只提供 Unity/native/宿主边界替身，按已核 2.4 机器码
// 顺序模拟 RegisterUnit → ConvertToSoldier → formation 写回 与 Unregister → OnLeave → Hunter）。
// 断言覆盖：入队后职业包（弩矢 SO/射程/2× 间隔/生根皮肤/旗色）、发射读取的 ActiveArrowAttack、
// 收旗 Hunter 皮肤纠正与 formation 释放（夜守资格门）、火 buff/合法 interval 不被 OnSeated 清洗、
// R2 same-life（回调换 life 拒 bypass/拒成功、seatless 同 life 清账不读 Active）。
using System;
using System.Collections.Generic;
using Harness;
using KingdomEnhancedMod;
using UnityEngine;

internal static class Program
{
    private static int Main()
    {
        Case.Run("real career package survives the native join through the owner transaction", () =>
        {
            Fixture.Reset(10f);
            ModConfig.MusketeerEnabled.Value = false;
            Archer cross = Fixture.AddCrossbowman(2f);
            Check.True(CrossbowmanLifecycle.IsCrossbowman(cross), "career applied before the banner");
            // Field-path proof: the live Animator holds the base hunter skin while the serialized
            // soldierAnimator holds the rooted crossbow controller, exactly like a hunting archer
            // entering a soldier state. ConvertToSoldier must read the FIELD, not the Animator.
            Animator pre = cross.GetComponent<Animator>();
            pre.runtimeAnimatorController = cross.BaseSkin;
            Check.True(CrossbowmanLifecycleNative.LastSoldierNativeWrite == null, "no native write yet");

            Fixture.Player.ActivateFormation();

            int crossStart = CrossRowStart();
            Check.Equal(1, CountOccupied(crossStart, 4), "one directed crossbow seat");
            Check.True(ReferenceEquals(Fixture.Formation.units[crossStart + 3], cross), "bow-side seat first");
            Check.True(ReferenceEquals(cross.GetFormation(), Fixture.Formation), "bound to the formation");
            Check.True(ReferenceEquals(cross.ActiveArrowAttack, PatchRoles_Crossbowman.AttackSo),
                "the fire action would read the cloned crossbow SO");
            Check.Near(12f, cross.shootRange, 1e-4, "shoot range");
            Check.Near(12f, cross._enemyScanner.range, 1e-4, "scanner range");
            Check.Near(2f, cross._shootIntervalRange.x, 1e-4, "cooldown x2");
            Check.Near(4f, cross._shootIntervalRange.y, 1e-4, "cooldown x2");
            Check.Near(6f, cross._shootIntervalRangeFormation.x, 1e-4, "formation cooldown x2");
            Check.Near(8f, cross._shootIntervalRangeFormation.y, 1e-4, "formation cooldown x2");
            Check.True(cross.soldierAnimator == PatchRoles_Crossbowman.Deadlands,
                "soldierAnimator rooted to the deadlands controller");
            Animator animator = cross.GetComponent<Animator>();
            Check.True(animator.runtimeAnimatorController == PatchRoles_Crossbowman.Deadlands,
                "skin controller after the native ConvertToSoldier");
            Check.Equal(1, CrossbowmanLifecycleNative.SoldierNativeWrites, "native soldier write once");
            Check.True(CrossbowmanLifecycleNative.LastSoldierNativeWrite == PatchRoles_Crossbowman.Deadlands,
                "ConvertToSoldier read the soldierAnimator field (pass-through), not the old hunter controller");
            Check.True(cross._isWearingBannerColor, "banner color from the native convert");
            Check.Equal(1, PatchRoles_Crossbowman.SeatedReconciles, "host reconcile ran once for the seat");
            Check.True(PatchRoles_CrossbowDefense.ReconcileSawIdentity,
                "the host reconcile saw the committed identity");
        });

        Case.Run("native Unregister frees the seat and the hunter postfix keeps the crossbow skin", () =>
        {
            Fixture.Reset(10f);
            ModConfig.MusketeerEnabled.Value = false;
            Archer cross = Fixture.AddCrossbowman(2f);
            Fixture.Player.ActivateFormation();
            int start = CrossRowStart();
            Check.Equal(1, CountOccupied(start, 4), "row armed");

            Fixture.Formation.UnregisterUnit(cross);

            Check.True(cross.GetFormation() == null, "native binding released");
            Check.True(CrossbowmanLifecycle.IsCrossbowman(cross), "career identity preserved after leaving");
            Animator animator = cross.GetComponent<Animator>();
            Check.Equal(1, CrossbowmanLifecycleNative.HunterNativeWrites, "native hunter swap ran");
            Check.True(CrossbowmanLifecycleNative.LastHunterNativeWrite == cross.BaseSkin,
                "native hunter swap wrote the ordinary hunter controller first");
            Check.True(animator.runtimeAnimatorController == PatchRoles_Crossbowman.Deadlands,
                "ConvertToHunter postfix restored the crossbow skin after the native swap");
            Check.Equal(1, PatchRoles_Crossbowman.HunterPostfixes, "hunter postfix ran once");
            Check.Equal(0, CountOccupied(start, 4), "seat released");
            // Night duty gate: production QualifyNightDefender requires GetFormation()==null;
            // CrossbowDefense source is unchanged and only needs the released binding.
            Check.True(CrossbowmanLifecycle.IsCrossbowman(cross) && cross.GetFormation() == null,
                "night-defense eligibility chain restored without touching Defense");
        });

        Case.Run("fire buff and legal formation intervals are not clobbered by the post-seat reconcile", () =>
        {
            Fixture.Reset(10f);
            ModConfig.MusketeerEnabled.Value = false;
            Archer cross = Fixture.AddCrossbowman(2f);
            ArrowAttack fire = new ArrowAttack("greek_fire_attack");
            cross._fireArrowAttack = fire;
            cross.ActiveArrowAttack = fire;                       // active buff SO
            cross._shootIntervalRangeFormation = new Vector2(9f, 9f);   // legal in-formation interval write

            Fixture.Player.ActivateFormation();

            Check.Equal(1, CountOccupied(CrossRowStart(), 4), "joined");
            Check.True(ReferenceEquals(cross.ActiveArrowAttack, fire),
                "OnSeated reconcile must not reset an active fire buff back to the base package");
            Check.Near(9f, cross._shootIntervalRangeFormation.x, 1e-4, "buff/formation interval untouched");
            Check.Near(9f, cross._shootIntervalRangeFormation.y, 1e-4, "buff/formation interval untouched");
            Check.Near(12f, cross.shootRange, 1e-4, "range still maintained");
        });

        Case.Run("a same-pointer pool new life inside the native callback gets no bypass and no success", () =>
        {
            Fixture.Reset(10f);
            ModConfig.MusketeerEnabled.Value = false;
            Archer cross = Fixture.AddCrossbowman(2f);
            long firstLife = CrossbowmanLifecycle.FormationLife(cross);
            Check.True(firstLife > 0L, "career life captured");

            bool attempted = false;
            bool allowed = true;
            cross.OnConvertedToSoldierCallback = archer =>
            {
                // Real pool new life for the same GameObject, inside the native callback window.
                CrossbowmanLifecycle.BeginPoolSpawnScope();
                CrossbowmanLifecycle.OnArcherEnablePrefix(archer, PatchRoles_Crossbowman.ProfileFor(archer));
                CrossbowmanLifecycle.EndPoolSpawnScope();
                attempted = true;
                allowed = archer.TryRecruit(Fixture.Formation);   // nested re-entry
            };

            Fixture.Player.ActivateFormation();

            Check.True(attempted, "callback window exercised");
            Check.False(allowed, "a same-pointer new life never inherits the armed bypass");
            Check.True(CrossbowmanLifecycle.FormationLife(cross) != firstLife, "life rotated in the callback");
            Check.Equal(0, CountOccupied(CrossRowStart(), 4), "the stale seat reference was cleared");
            Check.Equal(0, cross.OnLeaveCalls, "never OnLeave a different life");
            Check.True(ReferenceEquals(cross.GetFormation(), Fixture.Formation),
                "only this owner's stale array reference was dropped; the new life's binding is untouched");
        });

        Case.Run("a temporarily foreign actor keeps the debt and is never left into another world", () =>
        {
            Fixture.Reset(10f);
            ModConfig.MusketeerEnabled.Value = false;
            Archer cross = Fixture.AddCrossbowman(2f);
            cross.OnConvertedToSoldierCallback = a => MusketeerAccess.InWorldResult = false;
            Fixture.Player.ActivateFormation();

            Check.Equal(0, PatchRoles_Crossbowman.SeatedReconciles,
                "a foreign actor never claims the seat as an accepted success");
            Check.Equal(0, cross.OnLeaveCalls, "never a native leave into another world");
            Check.Equal(0, CountOccupied(CrossRowStart(), 4), "the stale array reference was CAS-dropped");
            Check.True(ReferenceEquals(cross.GetFormation(), Fixture.Formation),
                "the same-life binding is kept as debt");

            Fixture.Tick();
            Check.Equal(0, cross.OnLeaveCalls, "maintenance keeps the debt while the actor is foreign");

            MusketeerAccess.InWorldResult = true;
            cross._character.inert = true;                 // do not let the top-up re-seat immediately
            Fixture.Tick();
            Check.Equal(1, cross.OnLeaveCalls, "a lawful window returns the same-life debt exactly once");
            Check.True(cross.GetFormation() == null, "binding cleared after the lawful leave");
            Fixture.Tick();
            Check.Equal(1, cross.OnLeaveCalls, "the receipt is not repeated");
        });

        Case.Run("a Menu switch with timeScale 1 keeps the same-life debt until Playing returns", () =>
        {
            Fixture.Reset(10f);
            ModConfig.MusketeerEnabled.Value = false;
            Archer cross = Fixture.AddCrossbowman(2f);
            long life = CrossbowmanLifecycle.FormationLife(cross);
            cross.ReplaceArraysOnRecruit = formation =>
            {
                // The native recruit leaves the actor bound but seatless (arrays replaced).
                var replacement = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Formation.UnitTypes>(16);
                for (int i = 0; i < 16; i++) replacement[i] = Formation.UnitTypes.Player;
                formation.unitTypes = replacement;
                formation.units = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Formation.IFormationUnit>(16);
            };
            cross.OnConvertedToSoldierCallback = a =>
            {
                // Synchronous native callback switches to Menu while timeScale stays 1.
                Managers.Inst.game.state = Game.State.Menu;
                Time.timeScale = 1f;
            };
            Fixture.Player.ActivateFormation();

            Check.Equal(0, PatchRoles_Crossbowman.SeatedReconciles, "no claim outside Playing");
            Check.Equal(0, cross.OnLeaveCalls, "no native leave into Menu even with timeScale 1");
            Check.True(ReferenceEquals(cross.GetFormation(), Fixture.Formation), "same-life debt is kept");

            Time.unscaledTime += 2f;
            FleetBoatFormationCoordinator coordinator = Fixture.Formation.GetComponent<FleetBoatFormationCoordinator>();
            PatchWorld_FleetBoatFormation.TickCoordinator(coordinator);
            Check.Equal(0, cross.OnLeaveCalls, "Menu maintenance never leaves");

            Managers.Inst.game.state = Game.State.Playing;
            cross._character.inert = true;                  // keep the top-up from re-seating immediately
            Fixture.Tick();
            Check.Equal(1, cross.OnLeaveCalls, "Playing restores the lawful window: exactly one leave");
            Check.True(cross.GetFormation() == null, "binding cleared");
            Check.True(CrossbowmanLifecycle.MatchesFormationLife(cross, life), "same life, no new-actor touch");
            Fixture.Tick();
            Check.Equal(1, cross.OnLeaveCalls, "receipt is not repeated");
        });

        Case.Run("a seatless receipt clears the same life even with the identity switched off", () =>
        {
            Fixture.Reset(10f);
            ModConfig.MusketeerEnabled.Value = false;
            Archer cross = Fixture.AddCrossbowman(2f);
            cross.ThrowOnLeave = 1;                     // the direct leave fails once -> receipt
            cross.ReplaceArraysOnRecruit = formation =>
            {
                var replacement = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Formation.UnitTypes>(16);
                for (int i = 0; i < 16; i++) replacement[i] = Formation.UnitTypes.Player;
                formation.unitTypes = replacement;
                formation.units = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Formation.IFormationUnit>(16);
            };

            Fixture.Player.ActivateFormation();

            long life = CrossbowmanLifecycle.FormationLife(cross);
            Check.True(life > 0L, "receipt captured a real life");
            Check.True(ReferenceEquals(cross.GetFormation(), Fixture.Formation), "still bound after the failed leave");

            CrossbowmanLifecycle.Strip(cross, PatchRoles_Crossbowman.ProfileFor(cross));   // feature off, same life
            Check.False(CrossbowmanLifecycle.IsCrossbowman(cross), "identity off");
            Check.True(CrossbowmanLifecycle.MatchesFormationLife(cross, life),
                "the token is kept for same-life debt clearing and never reads Active/Enabled");

            cross._character.inert = true;              // keep the top-up out
            cross.ReplaceArraysOnRecruit = null;
            // Simulate another owner's array still being the replacement: the actor is seatless.
            Fixture.Tick();
            Check.True(cross.GetFormation() == null, "seatless same-life leave completed");
            Check.Equal(1, cross.OnLeaveCalls, "native leave ran exactly once");
        });

        Case.Run("zero-crossbow activation reserves the row and later careers join by maintenance", () =>
        {
            Fixture.Reset(10f);
            ModConfig.MusketeerEnabled.Value = false;
            Fixture.Player.ActivateFormation();

            int start = CrossRowStart();
            Check.Equal(Fixture.Baseline24.Length + 4, Fixture.Formation.unitTypes.Length,
                "cross row reserved without any crossbowman");
            for (int seat = 0; seat < 4; seat++)
            {
                Check.Equal(Formation.UnitTypes.Squire, Fixture.Formation.unitTypes[start + seat],
                    "empty seat stays Squire (no phantom Gap)");
                Check.True(Fixture.Formation.units[start + seat] == null, "no phantom unit");
            }

            Archer late = Fixture.AddCrossbowman(4f);   // new promotion / load recompute finished
            Fixture.Tick();
            Check.Equal(1, CountOccupied(start, 4), "the existing 0.5s maintenance adds the new crossbowman");
            Check.True(ReferenceEquals(Fixture.Formation.units[start + 3], late), "bow-side seat first");

            Fixture.AddCrossbowman(5f);
            Fixture.AddCrossbowman(6f);
            Fixture.AddCrossbowman(7f);
            Fixture.AddCrossbowman(8f);
            Fixture.Tick();
            Check.Equal(4, CountOccupied(start, 4), "four-seat cap holds");
        });

        Case.Run("ordinary archer and musketeer flows are unchanged beside the cross row", () =>
        {
            Fixture.Reset(10f);
            ModConfig.MusketeerEnabled.Value = true;
            Archer musketeer = Fixture.AddMusketeer(20f);
            Archer plain = Fixture.AddArcher(5f);
            Fixture.Player.ActivateFormation();

            Check.True(ReferenceEquals(musketeer.GetFormation(), Fixture.Formation), "musketeer joined its row");
            Check.True(Fixture.Formation.units[CrossRowStart() + 3] == null, "cross row empty without candidates");
            Check.True(plain.TryRecruit(Fixture.Formation), "ordinary archer keeps a native bow seat");
            Check.Near(4f, plain.transform.position.x, 1f, "fixture sanity");
        });

        Console.WriteLine();
        Console.WriteLine("passed=" + Case.Passed + " failed=" + Case.Failed);
        for (int i = 0; i < Case.Failures.Count; i++) Console.WriteLine("  FAIL " + Case.Failures[i]);
        return Case.Failed == 0 ? 0 : 1;
    }

    /// <summary>Fleet footprint (1, no boats in this suite) plus the two native gaps.</summary>
    private static int CrossRowStart() => 3;

    private static int CountOccupied(int from, int count)
    {
        int total = 0;
        for (int i = from; i < from + count; i++)
        {
            if (Fixture.Formation.units[i] != null) total++;
        }
        return total;
    }
}
