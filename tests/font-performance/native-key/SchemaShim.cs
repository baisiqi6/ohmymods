namespace KingdomEnhancedMod;

// 纯测试/编译检查用 shim：本 cwd 不编译真实 HeavyShieldPersistence 源。
// 漂移防护：Tests.csproj 的 SchemaKeyGuard 构建前核对真实
// ../../../il2cpp/HeavyShieldSaveData.cs 仍含同一 key 字面量，不一致直接构建失败。
internal static class HeavyShieldSaveSchema
{
    internal const string Key = "KEM.HeavyShield.Campaigns.v1";
}
