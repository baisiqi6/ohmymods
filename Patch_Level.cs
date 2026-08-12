using System;
using System.Reflection;
using UnityEngine;
using Harmony;

namespace MyMod
{
    /// <summary>
    /// 岛宽度放大（Main.mapSizeMultiplier）——用户需求的正确定位。
    ///
    /// 背景（2026-08-12 用户澄清 + 源码核实）：
    /// 需求"增大每个岛的地图大小"= 增大岛的宽度 → 塔基/墙基/泉眼等
    /// （LevelBlock 内容 + 散落物）随机均匀分布 → 岛越宽内容越多。
    /// 岛宽度的真实控制链：
    ///   Level.GenerateInternal（Level.cs:361）
    ///     → new LevelLayout(list, config, ...)（Level.cs:367）
    ///       → 反复 AddBlock 直到 TotalWidth() >= config.minLevelWidth + levelSizeAddition
    ///     → _levelEdges 总宽度 → GroundCollider.size = (总宽度, ...)（Level.cs:406-409）
    ///     → World.worldBounds（World.cs:165）= Ground 碰撞体尺寸
    /// 即 minLevelWidth 决定块数 → 总宽度 → 地面 → 世界边界，全部自动连锁。
    /// 之前误改 minKingdomExtents（王国初始安全区边界，场景序列化值）——与岛宽度无关，
    /// 且每岛累积乘法 + 存档污染（旧档值无法区分场景值），方向错误，已回退。
    ///
    /// 实现：patch Level.GenerateInternal——prefix 把 config.minLevelWidth 临时放大
    /// （记录原值），postfix 恢复（防污染共享 LevelConfig 资产）。每岛生成一次，幂等。
    /// </summary>
    public static class Patch_Level
    {
        private static FieldInfo _minLevelWidthField;
        private static int _originalWidth;
        private static bool _modified;

        public static void Register(HarmonyInstance harmony)
        {
            var levelType = typeof(Level);
            var generateMethod = levelType.GetMethod("GenerateInternal",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (generateMethod == null)
            {
                Debug.LogError("[MyMod] Level.GenerateInternal not found!");
                return;
            }

            _minLevelWidthField = typeof(LevelConfig).GetField("minLevelWidth",
                BindingFlags.Public | BindingFlags.Instance);
            if (_minLevelWidthField == null)
            {
                Debug.LogError("[MyMod] LevelConfig.minLevelWidth not found!");
                return;
            }

            var prefix = new HarmonyMethod(typeof(Patch_Level).GetMethod("GenerateInternal_Prefix"));
            var postfix = new HarmonyMethod(typeof(Patch_Level).GetMethod("GenerateInternal_Postfix"));
            harmony.Patch(generateMethod, prefix, postfix);
            Debug.Log("[MyMod] Patched Level.GenerateInternal (island width x" + Main.mapSizeMultiplier + ")");
        }

        /// <summary>
        /// 生成前把 minLevelWidth 放大（记录原值）。config 是方法参数（LevelConfig）。
        /// </summary>
        public static void GenerateInternal_Prefix(Level __instance, LevelConfig config)
        {
            if (!Main.Enabled || Main.mapSizeMultiplier <= 1f || config == null) return;

            try
            {
                _originalWidth = (int)_minLevelWidthField.GetValue(config);
                _modified = true;
                int scaled = (int)(_originalWidth * Main.mapSizeMultiplier);
                _minLevelWidthField.SetValue(config, scaled);
                Debug.Log("[MyMod] Island width: " + _originalWidth + " -> " + scaled
                    + " (x" + Main.mapSizeMultiplier + ")");
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Level width patch prefix error: " + e.Message);
                _modified = false;
            }
        }

        /// <summary>
        /// 生成完成后恢复原值，防止污染共享的 LevelConfig 资产（下次进岛重新放大）。
        /// </summary>
        public static void GenerateInternal_Postfix(Level __instance, LevelConfig config)
        {
            if (!_modified || config == null) return;
            _modified = false;
            try
            {
                _minLevelWidthField.SetValue(config, _originalWidth);
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Level width patch postfix error: " + e.Message);
            }
        }
    }
}
