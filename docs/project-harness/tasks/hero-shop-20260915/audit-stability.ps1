$ErrorActionPreference = 'Stop'
Add-Type -Path 'E:/mod-dev/KingdomMod/deps/KTC-ModDevLibs/BIE6_IL2CPP/core/Mono.Cecil.dll'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$oldPath = 'C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/hero-shop-20260915/candidate-grounding/KingdomEnhancedMod.dll'
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
    $before = Index $old; $after = Index $new; $same = 0; $changed = @(); $added = @(); $removed = @()
    $allowed = 'KingdomEnhancedMod\.(HeroShop|HeroShopOwner|HeroShopRetention|KingdomEnhancedPlugin)(::|/)'
    foreach ($name in $before.Keys) {
        if (!$after.ContainsKey($name)) {
            if ($name -notmatch $allowed) { throw "Unrelated method removed: $name" }
            $removed += $name; continue
        }
        if ((Body $before[$name]) -ceq (Body $after[$name])) { $same++; continue }
        if ($name -notmatch $allowed) { throw "Unrelated method changed: $name" }
        $changed += $name
    }
    foreach ($name in $after.Keys) {
        if (!$before.ContainsKey($name)) {
            if ($name -notmatch $allowed) { throw "Unexpected new method: $name" }
            $added += $name
        }
    }
    $resources = @()
    foreach ($resource in $old.MainModule.Resources) {
        if ($resource -isnot [Mono.Cecil.EmbeddedResource]) { continue }
        $current = $new.MainModule.Resources | Where-Object Name -eq $resource.Name | Select-Object -First 1
        if (!$current -or [Convert]::ToBase64String($resource.GetResourceData()) -cne [Convert]::ToBase64String($current.GetResourceData())) { throw "Resource changed: $($resource.Name)" }
        $resources += $resource.Name
    }
    $result = @{ baselineSha256 = (Get-FileHash -LiteralPath $oldPath).Hash; candidateSha256 = (Get-FileHash -LiteralPath $newPath).Hash;
        unchangedMethods = $same; changedMethods = @($changed | Sort-Object); addedMethods = @($added | Sort-Object); removedMethods = @($removed | Sort-Object);
        unchangedResources = $resources; version = $new.Name.Version.ToString() }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'receipts/stability/dll-audit.json') -Encoding UTF8
    "Stability audit: $same unchanged methods, $($changed.Count) changed, $($added.Count) added, $($removed.Count) removed; all artwork unchanged."
} finally { $old.Dispose(); $new.Dispose() }
