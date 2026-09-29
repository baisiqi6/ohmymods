// ApplyToSceneCallShape.cs — 仅编译门使用：MusketeerPersistence.VirginPatch.After 末尾新增的
// 调用点的精确形状（同参数、同 self-wrapped try/catch）。火枪文件本体连同其依赖链在整树
// 构建里编译；本门单独证明该调用对真实 2.4 interop 的 CampaignSaveData 与我们模块的
// EnsureBoundFromApplyToScene(CampaignSaveData) 签名成立。
using System;
using KingdomEnhancedMod;

internal static class ApplyToSceneCallShape
{
    internal static void After(CampaignSaveData __instance)
    {
        try { CoinCourierPersistence.EnsureBoundFromApplyToScene(__instance); }
        catch (Exception) { /* CoinCourierPersistence 自带隔离与有界诊断 */ }
    }
}
