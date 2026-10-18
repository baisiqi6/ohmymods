// Patch_PalaceRuntimeProfile.cs — 唯一固定业务 profile（DedicatedR7V1）。
//
// 依据 #101 生产激活合同 §1（28 项 PALACE_* 迁移清单，固定 true 18 / false 10）与
// exact-gate A–F 批准范围：
// - 正常 PC 与受保护隔离共用同一 profile；不存在"正常/私有"两套业务行为。
// - 本表是代码内固定能力，不是向 launcher 导出 env 的指令；任何 PALACE_*（含未知名与
//   launcher 残留值）都不能改变本表结果——本类绝不读取环境变量。
// - 诊断/QA（ROUNDTRIP_QA、各类 OBSERVE/METRICS/TEMPLATE_READ）永远 false：编译依赖保留，
//   但 TryStartQaFree 等入口在固定 false 下永远拒绝，无法从 env 恢复。
// - legacy 分支（CONTENT_PRIVATE / STAGE_PRIVATE / FIGURE_LAYER_R2）固定 false，不是遗漏功能。
// - R4 native binding、R5 环境、R6 单位深度/局部动画、R7 actors、dedicated island 成套开启。
// - profile 能力与动态资格分开：ModConfig.Enabled 仍是新业务动作门；安装路径按固定能力注册，
//   由各 runtime gate 保护（见各 InstallOnce 的 C 修正）。
// 纯查表、零环境读取、零副作用；未知名称一律 false。
using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod;

internal static class PalaceRuntimeProfile
{
    internal const string Id = "DedicatedR7V1";

    /// <summary>
    /// 冻结能力表：恰 28 项，与合同清单一一对应（18 true / 10 false）。任何修改都必须同步合同与
    /// tests/check_profile_table.py 的期望集。
    /// </summary>
    private static readonly Dictionary<string, bool> Capabilities = new Dictionary<string, bool>(StringComparer.Ordinal)
    {
        // ---- 固定 true（18）----
        ["PALACE_ACTORS_R7_PRIVATE"] = true,
        ["PALACE_ARRIVAL_PRIVATE"] = true,
        ["PALACE_COURT_AREA_PRIVATE"] = true,
        ["PALACE_COURT_PRIVATE"] = true,
        ["PALACE_DEDICATED_ISLAND_PRIVATE"] = true,
        ["PALACE_DEPTH_R5_PRIVATE"] = true,
        ["PALACE_ENTRY_PAYMENT_PRIVATE"] = true,
        ["PALACE_ISLAND_OWNED_PRIVATE"] = true,
        ["PALACE_LAND12_PRIVATE"] = true,
        ["PALACE_LOCAL_MOTION_R6_PRIVATE"] = true,
        ["PALACE_NATIVE_BG_NONE_PRIVATE"] = true,
        ["PALACE_NODE1"] = true,
        ["PALACE_RETURN_ANCHOR_PRIVATE"] = true,
        ["PALACE_RETURN_PRIVATE"] = true,
        ["PALACE_SCROLL_R4_PRIVATE"] = true,
        ["PALACE_SORT_DEPTH_R6_PRIVATE"] = true,
        ["PALACE_TERRAIN_PRIVATE"] = true,
        ["PALACE_WORLD_FULL_PRIVATE"] = true,
        // ---- 固定 false（10）：诊断/QA/legacy，不可被 env 打开 ----
        ["PALACE_ADULT_BODY_METRICS_PRIVATE"] = false,
        ["PALACE_ARRIVAL_CONTROL_OBSERVE_PRIVATE"] = false,
        ["PALACE_CONTENT_PRIVATE"] = false,
        ["PALACE_DEDICATED_TEMPLATE_READ_PRIVATE"] = false,
        ["PALACE_FIGURE_LAYER_R2"] = false,
        ["PALACE_FIGURE_OBSERVE"] = false,
        ["PALACE_NATIVE_METRICS_PRIVATE"] = false,
        ["PALACE_ROUNDTRIP_QA"] = false,
        ["PALACE_SKY_SORT_OBSERVE_PRIVATE"] = false,
        ["PALACE_STAGE_PRIVATE"] = false,
    };

    /// <summary>固定能力查询（纯查表；未知 false；绝不读环境）。</summary>
    internal static bool Capability(string name)
    {
        try { return Capabilities.TryGetValue(name, out bool value) && value; }
        catch (Exception) { return false; }
    }

    /// <summary>已知能力名（测试/日志用；未知名称不因查表而变成已知）。</summary>
    internal static bool IsKnownCapability(string name)
        => name != null && Capabilities.ContainsKey(name);

    /// <summary>表项总数（合同核对值：28）。</summary>
    internal static int KnownCount => Capabilities.Count;

    /// <summary>一次性日志描述（不承诺任何运行验收）。</summary>
    internal static string Describe()
        => Id + ": 28 fixed capabilities (18 true / 10 false); env-independent; unknown=false; QA/diagnostics sealed";
}
