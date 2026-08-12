using System;
using System.Reflection;
using UnityEngine;
using Harmony;

namespace MyMod
{
    /// <summary>
    /// 乞丐帐篷（BeggarCamp）——乞丐生成间隔调整为 90 秒。
    ///
    /// == spawnInterval / -119f 语义分析（结论先行）==
    ///
    /// 1. 生成循环（BeggarCamp.SlowUpdate，game-source BeggarCamp.cs:44-61）：
    ///        for(;;) {
    ///            等 5 秒（游戏时间）;
    ///            重数帐篷周围 5 单位内的乞丐（_beggars）;
    ///            if (_beggars.Count < maxBeggars + 20) {       // 默认 40
    ///                等 (spawnInterval - 119f) 秒;             // 关键魔法数（56 行）
    ///                if (仍 < 40 且教程允许) SpawnBeggar();    // 生成 1 个
    ///            }
    ///        }
    ///    即：每个帐篷在低于上限时，以 (5s 巡检 + 间隔) 的节奏生成 1 个乞丐。
    ///
    /// 2. -119f 是"真 IL"，不是反编译失真：
    ///    反编译器只忠实还原 IL 算术（ldfld spawnInterval; ldc.r4 119; sub），
    ///    不会凭空造出减法；源码里确实存在 spawnInterval - 119f 这个 hack。
    ///    默认 spawnInterval=120f → 实际等待 = 120-119 = 1 秒。
    ///
    /// 3. 语义推断（最合理）：spawnInterval 是 public 序列化字段（Unity Inspector /
    ///    场景预制体里烘焙了 120），开发者不想动场景里已序列化的旧值，于是在代码里
    ///    用 "-119f" 做运行时补偿，把行为从"120 秒"热修成"1 秒"——典型的平衡性 hack
    ///    （保留旧序列化值、只改运行时行为）。即：有效等待 = spawnInterval - 119f 秒。
    ///
    /// 4. 负数等待的后果（Haglet.cs:1354-1362）：ForSeconds 子句每帧 tallySecs += deltaTime，
    ///    当 tallySecs >= totalSecs 时达成；负数 totalSecs 第一帧即达成 → 等于不等待。
    ///    因此若直接把 spawnInterval 改成 90 → 90-119 = -29 → 乞丐瞬间疯狂生成（错误）。
    ///
    /// 5. 本 patch 方案：设置 spawnInterval = 90 + 119 = 209f（SetValue 设置型，不做乘法叠加）。
    ///    保持游戏自身公式不变，有效等待精确 = 209 - 119 = 90.0 秒。
    ///    - 不 patch 协程：SlowUpdate 迭代器 MoveNext 是编译器生成的状态机，
    ///      Harmony v1.2 无 Transpiler，改协程内参数不可行/不可靠；字段设置最稳。
    ///    - 目标"90 秒"折算回游戏内部字段值 209，语义在注释中写清。
    ///    - maxBeggars = 20 不动（需求明确不用改；上限判定 maxBeggars+20 也一并保留）。
    ///
    /// 6. Patch 点选择：BeggarCamp.Awake postfix。
    ///    - Awake 在构造（字段初始化器 =120f）之后、Start（创建并启动 SlowUpdate
    ///      协程）之前执行，保证 SlowUpdate 首次读 spawnInterval 前已被改写；
    ///    - 每个实例（场景加载 / 生成 / 对象池）都会走 Awake，全覆盖；
    ///    - 全库 grep 确认 spawnInterval 只在 SlowUpdate:56 被读取，无其他写入者。
    ///
    /// 7. 已知语义细节：外层 5 秒巡检是独立节奏，故实际每帐篷生成节奏 ≈ 5s + 90s；
    ///    "90 秒刷新"对应的是任务框架定义的 spawnInterval 驱动的等待段。
    /// </summary>
    public static class Patch_BeggarCamp
    {
        // 目标：生成间隔 90 秒。因游戏公式为 (spawnInterval - 119f) 秒，
        // 需设置 spawnInterval = 90 + 119 = 209f（设置型，非乘法叠加）。
        private const float TargetSpawnInterval = 209f;

        private static FieldInfo _spawnIntervalField;

        public static void Register(HarmonyInstance harmony)
        {
            var campType = typeof(BeggarCamp);
            var awake = campType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            if (awake == null)
            {
                Debug.LogError("[MyMod] BeggarCamp.Awake not found!");
                return;
            }

            _spawnIntervalField = campType.GetField("spawnInterval", BindingFlags.Public | BindingFlags.Instance);
            if (_spawnIntervalField == null)
            {
                Debug.LogError("[MyMod] BeggarCamp.spawnInterval not found!");
                return;
            }

            var postfix = new HarmonyMethod(typeof(Patch_BeggarCamp).GetMethod("Awake_Postfix"));
            harmony.Patch(awake, null, postfix);
            Debug.Log("[MyMod] Patched BeggarCamp.Awake");
        }

        /// <summary>
        /// Awake 之后立刻把 spawnInterval 设为 209f（有效等待 = 209 - 119 = 90 秒）。
        /// </summary>
        public static void Awake_Postfix(BeggarCamp __instance)
        {
            if (!Main.Enabled) return;
            if (__instance == null || _spawnIntervalField == null) return;
            _spawnIntervalField.SetValue(__instance, TargetSpawnInterval);
        }
    }
}
