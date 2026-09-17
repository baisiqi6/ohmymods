using System;
using KingdomEnhancedMod;
using UnityEngine;

namespace MedievalScatterPolicyTests
{
    /// <summary>
    /// 中世纪散射资格边界用例（生产策略源 + 边界替身）：五种风格/未决/无队籍/换队/死 leader/
    /// 异 world、野生动物/无目标/敌方建筑/友军巨魔/独立弩手/北境随从、attack mode/死亡/失败闭合。
    /// 每个断言同时核对参数不被写（Evidence.Writes 调用前后不变）。
    /// </summary>
    internal static class PolicyTests
    {
        private sealed class Rig
        {
            internal Archer Archer;
            internal Knight Knight;
            internal GameObject Target;
        }

        internal static void Run()
        {
            Case.Run("musketeer_never_inherits_medieval_scatter", () =>
            {
                Rig rig = NewRig();
                MusketeerIdentity.Marked = rig.Archer;
                try { AssertEligible(rig, false, "paid musketeer remains single bullet"); }
                finally { MusketeerIdentity.Marked = null; }
            });
            MedievalOnlyScatters();
            UnresolvedStyleNeverScatters();
            KnightlessArcherNeverScatters();
            SquadSwitchUsesCurrentKnight();
            DeadLeaderNeverScatters();
            ForeignWorldNeverScatters();
            WildlifeStaysSingleShot();
            NoTargetNeverScatters();
            EnemyStructureScatters();
            FriendlyTrollExcluded();
            CrossbowmanExcluded();
            NorseFollowerExcluded();
            RangedModeRequired();
            DeadHarmlessGrabbedArcherNeverScatters();
            TargetDamageableGates();
            FailClosedOnReadError();
        }

        // ------------------------------------------------------------ 队籍（style0）

        private static void MedievalOnlyScatters()
        {
            Case.Run("medieval_style0_scatters_other_styles_do_not", () =>
            {
                Rig medieval = NewRig();
                AssertEligible(medieval, true, "style 0 (medieval) follower archer vs world enemy");

                for (int style = 1; style < 5; style++)
                {
                    Rig other = NewRig();
                    PatchRoles_KnightStyle.Styles[other.Knight] = style;
                    AssertEligible(other, false, "style " + style + " knight follower");
                }
            });
        }

        private static void UnresolvedStyleNeverScatters()
        {
            Case.Run("unresolved_identity_never_scatters", () =>
            {
                Rig rig = NewRig();
                PatchRoles_KnightStyle.Styles.Remove(rig.Knight); // 固定身份未决：不 hash、不重推
                AssertEligible(rig, false, "style not resolved yet");
            });
        }

        private static void KnightlessArcherNeverScatters()
        {
            Case.Run("knightless_archer_never_scatters", () =>
            {
                Rig rig = NewRig();
                rig.Archer._knight = null;
                AssertEligible(rig, false, "hunter / independent archer without knight");
            });
        }

        private static void SquadSwitchUsesCurrentKnight()
        {
            Case.Run("squad_switch_reads_current_knight_only", () =>
            {
                Rig rig = NewRig(); // 旧骑士仍是已解析的 style0
                var replacementGo = new GameObject("KnightShogun");
                replacementGo.tag = "Knight";
                Knight replacement = replacementGo.AddComponentForTests(new Knight());
                replacement._damageable = replacementGo.AddComponentForTests(new Damageable());
                PatchRoles_KnightStyle.Styles[replacement] = 2; // 改投幕府骑士
                rig.Archer._knight = replacement;
                AssertEligible(rig, false, "archer reassigned to a shogun knight");
            });
        }

        private static void DeadLeaderNeverScatters()
        {
            Case.Run("dead_leader_never_scatters", () =>
            {
                Rig rig = NewRig();
                rig.Knight._damageable.isDead = true; // 死骑士仍在 _knight 上
                AssertEligible(rig, false, "dead knight leader");
            });
        }

        private static void ForeignWorldNeverScatters()
        {
            Case.Run("foreign_world_units_never_scatter", () =>
            {
                Rig archerForeign = NewRig();
                ArcherOptionsScope.Foreign.Add(archerForeign.Archer);
                AssertEligible(archerForeign, false, "archer in another world");

                Rig knightForeign = NewRig();
                ArcherOptionsScope.Foreign.Add(knightForeign.Knight);
                AssertEligible(knightForeign, false, "knight in another world");

                Rig targetForeign = NewRig();
                ArcherOptionsScope.Foreign.Add(targetForeign.Target.transform);
                AssertEligible(targetForeign, false, "target in another world");
            });
        }

        // ------------------------------------------------------------ 目标

        private static void WildlifeStaysSingleShot()
        {
            Case.Run("wildlife_target_stays_single_shot", () =>
            {
                Rig wildLayer = NewRig();
                wildLayer.Target.layer = LayerMask.WildlifeLayer; // 兔子/鹿不在 Enemies 层
                AssertEligible(wildLayer, false, "wildlife-layer target");

                Rig wildTag = NewRig();
                wildTag.Target.tag = "Wildlife"; // 防御：层被改也单发
                AssertEligible(wildTag, false, "enemy-layer object tagged Wildlife");
            });
        }

        private static void NoTargetNeverScatters()
        {
            Case.Run("no_shooting_target_never_scatters", () =>
            {
                Rig rig = NewRig();
                rig.Archer._shootingTarget = null;
                AssertEligible(rig, false, "shot without _shootingTarget (no scanner fallback)");
            });
        }

        private static void EnemyStructureScatters()
        {
            Case.Run("enemy_structure_without_enemy_component_scatters", () =>
            {
                // stub 程序集里不存在 Enemy 类型：命中只靠 Enemies 层 + Damageable，
                // 原生敌方建筑（无 Enemy 组件）不会被遗漏。
                Rig rig = NewRig();
                rig.Target.tag = "QuestStructure";
                AssertEligible(rig, true, "enemy structure on Enemies layer");
            });
        }

        private static void FriendlyTrollExcluded()
        {
            Case.Run("friendly_troll_target_excluded", () =>
            {
                Rig onSelf = NewRig();
                onSelf.Target.AddComponentForTests(new FriendlyTroll());
                AssertEligible(onSelf, false, "FriendlyTroll on the target");

                Rig onParent = NewRig();
                var parent = new GameObject("TrollRoot");
                parent.AddComponentForTests(new FriendlyTroll());
                onParent.Target.transform.parent = parent.transform;
                AssertEligible(onParent, false, "FriendlyTroll on a parent");
            });
        }

        private static void CrossbowmanExcluded()
        {
            Case.Run("crossbowman_archer_excluded", () =>
            {
                Rig rig = NewRig();
                PatchRoles_Crossbowman.Crossbows.Add(rig.Archer);
                AssertEligible(rig, false, "independent crossbowman (IsCrossbowman)");
            });
        }

        private static void NorseFollowerExcluded()
        {
            Case.Run("transferred_norse_follower_excluded", () =>
            {
                Rig rig = NewRig();
                PatchRoles_NorseSquad.NorseFollowers.Add(rig.Archer);
                AssertEligible(rig, false, "Norse melee follower recycled into a medieval squad");
            });
        }

        // ------------------------------------------------------------ 射手守卫

        private static void RangedModeRequired()
        {
            Case.Run("ranged_mode_required_for_current_and_desired", () =>
            {
                Rig currentMelee = NewRig();
                currentMelee.Archer._attackMode = Archer.AttackMode.Melee;
                AssertEligible(currentMelee, false, "current attack mode is melee");

                Rig desiredMelee = NewRig();
                desiredMelee.Archer._desiredAttackMode = Archer.AttackMode.Melee;
                AssertEligible(desiredMelee, false, "desired attack mode is melee");
            });
        }

        private static void DeadHarmlessGrabbedArcherNeverScatters()
        {
            Case.Run("dead_harmless_or_grabbed_archer_never_scatters", () =>
            {
                Rig dead = NewRig();
                dead.Archer._damageable.isDead = true;
                AssertEligible(dead, false, "archer damageable dead");

                Rig harmless = NewRig();
                harmless.Archer.harmless = true;
                AssertEligible(harmless, false, "hidden/harmless archer");

                Rig grabbed = NewRig();
                grabbed.Archer._character.grabbed = true;
                AssertEligible(grabbed, false, "grabbed archer");
            });
        }

        private static void TargetDamageableGates()
        {
            Case.Run("target_damageable_gates", () =>
            {
                Rig noDamageable = NewRig();
                noDamageable.Target = new GameObject("EnemyNoDamageable");
                noDamageable.Target.layer = LayerMask.EnemiesLayer;
                noDamageable.Archer._shootingTarget = noDamageable.Target;
                AssertEligible(noDamageable, false, "target without Damageable");

                Rig deadTarget = NewRig();
                deadTarget.Target.GetComponent<Damageable>().isDead = true;
                AssertEligible(deadTarget, false, "dead target damageable");

                Rig arrowImmune = NewRig();
                arrowImmune.Target.GetComponent<Damageable>().DamagedBy = _ => false;
                AssertEligible(arrowImmune, false, "target immune to DamageSource.Arrow");
            });
        }

        private static void FailClosedOnReadError()
        {
            Case.Run("read_failure_fails_closed", () =>
            {
                Rig rig = NewRig();
                ArcherOptionsScope.Throwing.Add(rig.Archer);
                AssertEligible(rig, false, "exception while resolving world membership");
            });
        }

        // ------------------------------------------------------------ helpers

        private static Rig NewRig()
        {
            var archerGo = new GameObject("Archer");
            Archer archer = archerGo.AddComponentForTests(new Archer());
            archer._character = archerGo.AddComponentForTests(new Character());
            archer._damageable = archerGo.AddComponentForTests(new Damageable());

            var knightGo = new GameObject("Knight");
            knightGo.tag = "Knight";
            Knight knight = knightGo.AddComponentForTests(new Knight());
            knight._damageable = knightGo.AddComponentForTests(new Damageable());
            archer._knight = knight;

            var targetGo = new GameObject("Enemy");
            targetGo.layer = LayerMask.EnemiesLayer;
            targetGo.AddComponentForTests(new Damageable());
            archer._shootingTarget = targetGo;

            PatchRoles_KnightStyle.Styles[knight] = 0; // 默认：固定身份已解析为中世纪
            return new Rig { Archer = archer, Knight = knight, Target = targetGo };
        }

        private static void AssertEligible(Rig rig, bool expected, string scenario)
        {
            int writesBefore = Evidence.Writes;
            bool actual = MedievalScatterPolicy.IsEligible(rig.Archer);
            Check.Equal(expected, actual, scenario + " -> IsEligible");
            Check.Equal(writesBefore, Evidence.Writes, scenario + " -> policy must not write any parameter");
        }
    }
}
