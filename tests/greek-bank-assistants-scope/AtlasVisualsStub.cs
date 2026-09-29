// AtlasVisualsStub.cs — lane A 的跨 lane 视觉 API 窄 stub（仅测试装配使用）。
//
// 生产文件 il2cpp/BankAssistantAtlasVisuals.cs 属于视觉 lane（B）；本 stub 只提供
// 冻结签名，让 PatchEconomy_BankAssistants.cs 可以在本测试工程编译与驱动：
//   internal static bool BankAssistantAtlasVisuals.EnsureAssets()
//   internal static void BankAssistantAtlasVisuals.Tick(GameObject actor, int slot, bool allowLeisure)
//   internal static void BankAssistantAtlasVisuals.Forget(GameObject actor)
// 根合入真实 B 文件时必须删除本文件与 Regression.csproj 中对应条目，生产不得包含 stub。
using UnityEngine;

namespace KingdomEnhancedMod
{
    internal static class BankAssistantAtlasVisuals
    {
        /// <summary>测试开关：默认 false（模拟图集未就绪 → 新四模板不登记）。</summary>
        internal static bool Available;
        internal static int EnsureCalls;
        internal static int TickCalls;
        internal static int ForgetCalls;
        internal static GameObject LastActor;
        internal static int LastSlot = -1;
        internal static bool LastAllowLeisure;

        internal static bool EnsureAssets()
        {
            EnsureCalls++;
            return Available;
        }

        internal static void Tick(GameObject actor, int slot, bool allowLeisure)
        {
            TickCalls++;
            LastActor = actor;
            LastSlot = slot;
            LastAllowLeisure = allowLeisure;
        }

        internal static void Forget(GameObject actor)
        {
            ForgetCalls++;
        }

        internal static void Reset()
        {
            Available = false;
            EnsureCalls = TickCalls = ForgetCalls = 0;
            LastActor = null;
            LastSlot = -1;
            LastAllowLeisure = false;
        }
    }
}
