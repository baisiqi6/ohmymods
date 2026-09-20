$ErrorActionPreference = 'Stop'
# Read-only metadata check of the built MOD assembly: the retention policy must be the code the
# frame loop actually runs and the code that gates payment (no parallel test-only policy).
# Requires il2cpp/bin/Debug/KingdomEnhancedMod.dll from the normal build; never starts the game.
Add-Type -Path 'E:/mod-dev/KingdomMod/deps/KTC-ModDevLibs/BIE6_IL2CPP/core/Mono.Cecil.dll'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$dll = Join-Path $root 'il2cpp/bin/Debug/KingdomEnhancedMod.dll'
if (!(Test-Path -LiteralPath $dll)) { throw "Build il2cpp first: $dll" }
function AllTypes($types) { foreach ($type in $types) { $type; AllTypes $type.NestedTypes } }
function Calls($method, $declaringType, $name) {
    if (!$method -or !$method.HasBody) { return 0 }
    return @($method.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq $declaringType -and
            $_.Operand.Name -eq $name }).Count
}
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
try {
    $shop = @(AllTypes $assembly.MainModule.Types | Where-Object { $_.FullName -eq 'KingdomEnhancedMod.HeroShop' })[0]
    if (!$shop) { throw 'KingdomEnhancedMod.HeroShop missing' }
    $tick = @($shop.Methods | Where-Object { $_.Name -eq 'Tick' })[0]
    if ((Calls $tick 'KingdomEnhancedMod.HeroShopRetention' 'Decide') -lt 1) { throw 'HeroShop.Tick does not consult HeroShopRetention.Decide' }
    $canPurchase = @($shop.Methods | Where-Object { $_.Name -eq 'CanPurchase' })[0]
    if ((Calls $canPurchase 'KingdomEnhancedMod.HeroShopRetention' 'CanServe') -lt 1) { throw 'HeroShop.CanPurchase does not use HeroShopRetention.CanServe' }
    $observe = @($shop.Methods | Where-Object { $_.Name -eq 'Observe' })[0]
    if ((Calls $observe 'Game' 'get_state') -lt 1) { throw 'HeroShop.Observe no longer reads the native game state' }
    'Wiring OK: Tick -> HeroShopRetention.Decide, CanPurchase -> CanServe, playing read in Observe.'
} finally { $assembly.Dispose() }
