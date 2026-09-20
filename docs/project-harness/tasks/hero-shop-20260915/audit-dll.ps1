$ErrorActionPreference = 'Stop'
Add-Type -Path 'E:/mod-dev/KingdomMod/deps/KTC-ModDevLibs/BIE6_IL2CPP/core/Mono.Cecil.dll'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$oldPath = 'C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/release-800-20260915/embedded-release.dll'
$newPath = Join-Path $root 'il2cpp/bin/Debug/KingdomEnhancedMod.dll'
function AllTypes($types) { foreach ($type in $types) { $type; AllTypes $type.NestedTypes } }
function Index($assembly) {
    $map = @{}
    foreach ($type in (AllTypes $assembly.MainModule.Types)) {
        foreach ($method in $type.Methods) { if ($method.HasBody) { $map[$method.FullName] = $method } }
    }
    return $map
}
function Body($method) {
    $lines = @($method.Body.Variables | ForEach-Object { $_.VariableType.FullName })
    $lines += @($method.Body.Instructions | ForEach-Object { $_.ToString() })
    $lines += @($method.Body.ExceptionHandlers | ForEach-Object { "$($_.HandlerType)|$($_.TryStart)|$($_.TryEnd)|$($_.HandlerStart)|$($_.HandlerEnd)|$($_.CatchType)" })
    return [string]::Join("`n", $lines)
}
$old = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($oldPath)
$new = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($newPath)
try {
    $before = Index $old; $after = Index $new
    $changed = @(); $same = 0; $added = @()
    foreach ($name in $before.Keys) {
        if (!$after.ContainsKey($name)) { throw "Removed existing method: $name" }
        if ((Body $before[$name]) -ceq (Body $after[$name])) { $same++; continue }
        if ($name -notmatch 'KingdomEnhancedMod\.(HeroArcherRuntime|ModPanel|ModConfig|KingdomEnhancedPlugin)::') {
            throw "Unexpected old behavior changed: $name"
        }
        $changed += $name
    }
    foreach ($name in $after.Keys) { if (!$before.ContainsKey($name)) { $added += $name } }
    $resources = @()
    foreach ($resource in $old.MainModule.Resources) {
        if ($resource -isnot [Mono.Cecil.EmbeddedResource]) { continue }
        $current = $new.MainModule.Resources | Where-Object Name -eq $resource.Name | Select-Object -First 1
        if (!$current -or [Convert]::ToBase64String($resource.GetResourceData()) -cne [Convert]::ToBase64String($current.GetResourceData())) {
            throw "Existing artwork changed: $($resource.Name)"
        }
        $resources += $resource.Name
    }
    $inputScopes = @($after['System.Void KingdomEnhancedMod.ModPanel::Update()'].Body.Instructions |
        Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq 'Input' } |
        ForEach-Object { $_.Operand.DeclaringType.Scope.Name })
    if ($inputScopes.Count -ne 5 -or @($inputScopes | Where-Object { $_ -ne 'Assembly-CSharp-firstpass' }).Count -ne 0) {
        throw 'Actual game Input binding drifted'
    }
    $result = @{ baseline = (Get-FileHash -LiteralPath $oldPath).Hash; candidate = (Get-FileHash -LiteralPath $newPath).Hash;
        unchangedMethods = $same; changedMethods = @($changed | Sort-Object); addedMethods = @($added | Sort-Object);
        unchangedResources = $resources; inputScopes = $inputScopes; assemblyVersion = $new.Name.Version.ToString() }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'receipts/dll-audit.json') -Encoding UTF8
    "Audit passed: $same old methods unchanged, $($changed.Count) reviewed integration changes; original artwork and Input binding preserved."
} finally { $old.Dispose(); $new.Dispose() }
