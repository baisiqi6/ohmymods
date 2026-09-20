$ErrorActionPreference = 'Stop'
Add-Type -Path 'E:/mod-dev/KingdomMod/deps/KTC-ModDevLibs/BIE6_IL2CPP/core/Mono.Cecil.dll'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$oldPath = (Join-Path $PSScriptRoot 'before.dll')
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
    $allowed = 'KingdomEnhancedMod\.(KingdomEnhancedPlugin|ModPanel|ModConfig|MusketeerRuntime|MusketeerVisuals|MusketeerCombat|MusketeerFoeFilter|MusketeerBullet|PatchWorld_FleetBoatFormation|PatchMusketeerFormation|MusketeerFormationLayout)(::|/)'
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
    $resources = @(); $changedResources = @()
    foreach ($resource in $old.MainModule.Resources) {
        if ($resource -isnot [Mono.Cecil.EmbeddedResource]) { continue }
        $current = $new.MainModule.Resources | Where-Object Name -eq $resource.Name | Select-Object -First 1
        if (!$current) { throw "Missing resource: $($resource.Name)" }
        if ([Convert]::ToBase64String($resource.GetResourceData()) -cne [Convert]::ToBase64String($current.GetResourceData())) {
            throw "Unexpected resource change: $($resource.Name)"
            $changedResources += $resource.Name; continue
        }
        $resources += $resource.Name
    }
    if ($old.MainModule.Resources.Count -ne $new.MainModule.Resources.Count) { throw 'Unexpected resource count' }
    $newHookTypes = @(AllTypes $new.MainModule.Types | Where-Object { $_.CustomAttributes.AttributeType.FullName -contains 'HarmonyLib.HarmonyPatch' } | ForEach-Object FullName)
    $oldHookTypes = @(AllTypes $old.MainModule.Types | Where-Object { $_.CustomAttributes.AttributeType.FullName -contains 'HarmonyLib.HarmonyPatch' } | ForEach-Object FullName)
    $newHooks = @($newHookTypes | Where-Object { $_ -notin $oldHookTypes })
    $removedHooks = @($oldHookTypes | Where-Object { $_ -notin $newHookTypes })
    if ($removedHooks.Count) { throw 'Unexpected hook removal' }
    foreach($hook in $newHooks) { if($hook -ne 'KingdomEnhancedMod.PatchMusketeerFormation/ArcherTryRecruitGuard') { throw "Unexpected new hook: $hook" } }
    $result = @{ baselineSha256 = (Get-FileHash -LiteralPath $oldPath).Hash; candidateSha256 = (Get-FileHash -LiteralPath $newPath).Hash;
        unchangedArtExpected = $true; removedHarmonyPatchTypes = $removedHooks; newHarmonyPatchTypes = $newHooks; 
        unchangedMethods = $same; changedMethods = @($changed | Sort-Object); addedMethods = @($added | Sort-Object); removedMethods = @($removed | Sort-Object);
        unchangedResources = $resources; changedResources = $changedResources; version = $new.Name.Version.ToString() }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'receipts/dll-audit.json') -Encoding UTF8
    "Banner/deer audit: $same unchanged methods, $($changed.Count) changed, $($added.Count) added, $($removed.Count) removed; all embedded PNGs and other gameplay preserved."
} finally { $old.Dispose(); $new.Dispose() }
