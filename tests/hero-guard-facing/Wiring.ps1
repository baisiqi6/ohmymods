$ErrorActionPreference = 'Stop'
# 只读接线核验：确认生产代码把本 slice 的入口接在既有桥上。
# 需要先构建 il2cpp/bin/Debug/KingdomEnhancedMod.dll；不启动游戏、不写任何游戏/存档/配置文件。
# 必需：HeroArcherRuntime.Tick -> HeroArcherGuardFacing.Tick / Evaluate；可选（只告警）：
# GuardFacing.Restore / GuardFacing.Clear / LiveDiagnostics.OnHeroSetup / LiveDiagnostics.Clear。
Add-Type -Path 'E:/mod-dev/KingdomMod/deps/KTC-ModDevLibs/BIE6_IL2CPP/core/Mono.Cecil.dll'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$dll = Join-Path $root 'il2cpp/bin/Debug/KingdomEnhancedMod.dll'
if (!(Test-Path -LiteralPath $dll)) { throw "Build il2cpp first: $dll" }

function Calls($method, $declaringType, $name) {
    if (!$method -or !$method.HasBody) { return 0 }
    return @($method.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq $declaringType -and
            $_.Operand.Name -eq $name }).Count
}
function AllMethods($type) {
    $found = [Collections.Generic.List[object]]::new()
    foreach ($method in $type.Methods) { $found.Add($method) }
    foreach ($nested in $type.NestedTypes) { foreach ($method in AllMethods $nested) { $found.Add($method) } }
    return $found
}

$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
try {
    $runtime = @($assembly.MainModule.Types | Where-Object { $_.FullName -eq 'KingdomEnhancedMod.HeroArcherRuntime' })[0]
    if (!$runtime) { throw 'KingdomEnhancedMod.HeroArcherRuntime missing' }
    $methods = AllMethods $runtime

    $tickCalls = 0; $evalCalls = 0; $restoreCalls = 0; $guardClearCalls = 0; $setupCalls = 0; $diagClearCalls = 0
    foreach ($method in $methods) {
        $tickCalls += Calls $method 'KingdomEnhancedMod.HeroArcherGuardFacing' 'Tick'
        $evalCalls += Calls $method 'KingdomEnhancedMod.HeroArcherGuardFacing' 'Evaluate'
        $restoreCalls += Calls $method 'KingdomEnhancedMod.HeroArcherGuardFacing' 'Restore'
        $guardClearCalls += Calls $method 'KingdomEnhancedMod.HeroArcherGuardFacing' 'Clear'
        $setupCalls += Calls $method 'KingdomEnhancedMod.HeroArcherLiveDiagnostics' 'OnHeroSetup'
        $diagClearCalls += Calls $method 'KingdomEnhancedMod.HeroArcherLiveDiagnostics' 'Clear'
    }

    if ($tickCalls -lt 1) { throw 'HeroArcherRuntime never calls HeroArcherGuardFacing.Tick' }
    if ($evalCalls -lt 1) { throw 'HeroArcherRuntime never calls HeroArcherGuardFacing.Evaluate' }

    $warnings = [Collections.Generic.List[string]]::new()
    if ($restoreCalls -lt 1) { $warnings.Add('GuardFacing.Restore not wired (retire/pool paths)') }
    if ($guardClearCalls -lt 1) { $warnings.Add('GuardFacing.Clear not wired (ClearInternal)') }
    if ($setupCalls -lt 1) { $warnings.Add('LiveDiagnostics.OnHeroSetup not wired (PromoteHero after Apply)') }
    if ($diagClearCalls -lt 1) { $warnings.Add('LiveDiagnostics.Clear not wired (Remove/Disable)') }

    'Wiring OK: Tick x' + $tickCalls + ', Evaluate x' + $evalCalls +
        ', Restore x' + $restoreCalls + ', GuardClear x' + $guardClearCalls +
        ', OnHeroSetup x' + $setupCalls + ', DiagClear x' + $diagClearCalls + '.'
    foreach ($warning in $warnings) { 'WARN: ' + $warning }
} finally { $assembly.Dispose() }
