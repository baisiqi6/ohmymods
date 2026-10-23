namespace KingdomEnhancedMod;
// Only contracts which previously had no literal. Loader literals always take precedence.
internal static class PalaceResourceHashGaps
{
    internal static string Resolve(string name) => name switch
    {
        "KingdomEnhancedMod.KEM_PalaceCourt/ground/brick-surface.png" => "af2f16c92a83e99a2d6245f6a9baabac2d5383d01973b670eb4a770097d3a5ee",
        "KingdomEnhancedMod.KEM_PalaceCourt/ground/stone-bank.png" => "ed15bb0d39ea41c1085ef3ef1a8b849c6d55d957da6abac5c711ce6e6f7e3d55",
        "KingdomEnhancedMod.KEM_PalaceWorldFull/actors/patrol-guard-lance/atlas.png" => "04aad4956b4d430bffb0e1c46ff7bcdfc45fa8f20e22f3dd659bbf8b4ad6158f",
        "KingdomEnhancedMod.KEM_PalaceWorldFull/actors/patrol-guard-shield/atlas.png" => "2962c544c06990137508884b58be76cd906dc6aca5a55ee7125c91a66008e2c1",
        "KingdomEnhancedMod.KEM_PalaceWorldFull/actors/court-dancer/atlas-8x.png" => "cec513cf72bf5521820e1e9317cdc5be9f69c70d3aaa508ad271cfecda314ade",
        "KingdomEnhancedMod.KEM_PalaceEntryIdle.png" => "67ee0742e3474d3a6e5cd9a97e5cee623dbaf74bfc45390b49caec87fc5f1ead",
        "KingdomEnhancedMod.KEM_PalaceWorldFull/actors/manifest.json" => "c6841660fd8e241f74e849abf361061aec0e197aa2044370e78120a186c4ce57",
        _ => null,
    };
}
