using System;
using System.Reflection;
using UnityEngine;
using Harmony;

namespace MyMod
{
    /// <summary>
    /// 希腊神器弓箭（ArtemisArrow）——单发箭伤害次数 = 20 次。
    ///
    /// ============ 上限由什么控制（ArtemisArrow.cs，游戏 2.0.1） ============
    ///
    /// DamageAffectedEnemies(GameObject hitEnemy = null)（101-130 行）是唯一结算点：
    /// 箭碰到碰撞体（OnTriggerEnter2D，97 行）或落地（FixedUpdate，86 行）都会调用它。
    /// 每次调用内：
    ///   1) 若 hitEnemy 是有效的火系受击目标：num = 1，直接伤害它；
    ///   2) 遍历 enemies.AllEnemies：每回合先检查 `if (num >= this._maxHitsPerArrow + 20f) break;`
    ///      （119 行），命中范围内的敌人则 num += 1 并造成伤害；
    ///   3) Wildlife 循环和 Boar 循环不检查 num、也不累加 num——它们不受上限约束。
    ///
    /// 所以"单支箭单次结算最多伤害多少个敌人"完全由 119 行的上限表达式
    /// `_maxHitsPerArrow + 20f` 决定；`_maxHitsPerArrow` 字段在整个游戏源码里只有
    /// 这一处被读取（grep 确认）。直接命中占第一个 num，循环补足其余，总数恒等于
    /// 上限值：原版 _maxHitsPerArrow = 2f → 上限 2 + 20 = 22，即原版单次结算最多
    /// 伤害 22 个敌人。
    ///
    /// ============ 与 +20f 的相互作用（为什么设 0 而不是 20） ============
    ///
    /// legacy 需求："单发弓箭的伤害次数提升至 20 次"（目标 = 恰好 20 次）。
    ///   - 若把 _maxHitsPerArrow 从 2f 改为 20f → 上限变成 20 + 20 = 40，
    ///     单发箭会伤害 40 个敌人，超出用户要的 20 次——不可取。
    ///   - 要得到"恰好 20 次"，必须让上限表达式等于 20，即 _maxHitsPerArrow = 0f
    ///     （0 + 20 = 20）。这个 +20f 是游戏内置的保底量（基线），_maxHitsPerArrow
    ///     是叠加在保底量之上的调节旋钮；legacy 版本（旧代码无 +20f，字段 20
    ///     → 20 次）迁移到 2.0.1 的等价形式就是字段 = 0。
    ///   - 不改 119 行里的 +20f 字面量：改常量需要 IL transpiler（脆且复杂），
    ///     而按本 mod 的"设置型 SetValue"契约，设字段 = 0f 已能精确达到目标 20。
    ///
    /// 注意：Wildlife / Boar 不受上限约束的行为与原版、legacy 完全一致（那两处
    /// 本就没有计数），本次不做改动；每触发一次结算上限都是 20，与原版"每次结算
    /// 上限 22"的结构相同，legacy mod 也只控制了这一个字段。
    ///
    /// ============ 实现选择 ============
    ///
    /// 用 prefix 挂在 DamageAffectedEnemies 上，结算前把 _maxHitsPerArrow 设为 0f。
    /// 选 prefix 而不是 OnEnable/Awake postfix：字段只在 DamageAffectedEnemies
    /// 内部被读取，在读取点原地设置对任何生成/池化路径都成立，不依赖 OnEnable
    /// 时序；每次结算一次 SetValue，开销可忽略。FieldInfo 缓存为 static readonly，
    /// 避免每次反射查找。
    /// </summary>
    public static class Patch_Artemis
    {
        private static readonly FieldInfo MaxHitsPerArrowField =
            typeof(ArtemisArrow).GetField("_maxHitsPerArrow", BindingFlags.NonPublic | BindingFlags.Instance);

        public static void Register(HarmonyInstance harmony)
        {
            var damageMethod = typeof(ArtemisArrow).GetMethod(
                "DamageAffectedEnemies", BindingFlags.NonPublic | BindingFlags.Instance);
            if (damageMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_Artemis).GetMethod("DamageAffectedEnemies_Prefix"));
                harmony.Patch(damageMethod, prefix, null);
                Debug.Log("[MyMod] Patched ArtemisArrow.DamageAffectedEnemies (20 hits per arrow)");
            }
            else
            {
                Debug.LogError("[MyMod] ArtemisArrow.DamageAffectedEnemies not found!");
            }
        }

        /// <summary>
        /// 结算前把 _maxHitsPerArrow 设为 0f → 119 行上限 = 0 + 20 = 20（恰好 20 次）。
        /// </summary>
        public static void DamageAffectedEnemies_Prefix(ArtemisArrow __instance)
        {
            if (!Main.Enabled) return;

            try
            {
                if (MaxHitsPerArrowField != null)
                {
                    MaxHitsPerArrowField.SetValue(__instance, 0f);
                }
                else
                {
                    Debug.LogError("[MyMod] ArtemisArrow._maxHitsPerArrow field not found!");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] ArtemisArrow prefix error: " + e.Message);
            }
        }
    }
}
