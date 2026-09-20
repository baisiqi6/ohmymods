$ErrorActionPreference = 'Stop'
Add-Type -Path 'E:/mod-dev/KingdomMod/deps/KTC-ModDevLibs/BIE6_IL2CPP/core/Mono.Cecil.dll'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$oldPath = 'C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/musketeer-implementation-20260916/before.dll'
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
    $allowed = 'KingdomEnhancedMod\.(Musketeer[^:/]*|ModConfig|ModPanel|KingdomEnhancedPlugin|HeroRecruitment|HeroArcherRuntime|MedievalScatterPolicy|PatchArcher_Options|PatchRoles_Crossbowman|Character_Promote_CrossbowmanAlternation_Patch)(::|/)'
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
    $newResources = @($new.MainModule.Resources | Where-Object { $_.Name -notin $resources })
    $expected = @('KingdomEnhancedMod.MusketeerAtlas.png','KingdomEnhancedMod.MusketeerShop.png','KingdomEnhancedMod.MusketeerGun.png','KingdomEnhancedMod.MusketeerBullet.png')
    if (@(Compare-Object $expected @($newResources | ForEach-Object Name)).Count) { throw 'Unexpected resources' }
    foreach ($res in $newResources) {
        $disk = Join-Path $root ('il2cpp/Assets/' + $res.Name.Substring('KingdomEnhancedMod.'.Length))
        if ([Convert]::ToBase64String($res.GetResourceData()) -cne [Convert]::ToBase64String([IO.File]::ReadAllBytes($disk))) { throw "Embedded resource differs: $($res.Name)" }
    }
    $newHookTypes = @(AllTypes $new.MainModule.Types | Where-Object { $_.CustomAttributes.AttributeType.FullName -contains 'HarmonyLib.HarmonyPatch' } | ForEach-Object FullName)
    $oldHookTypes = @(AllTypes $old.MainModule.Types | Where-Object { $_.CustomAttributes.AttributeType.FullName -contains 'HarmonyLib.HarmonyPatch' } | ForEach-Object FullName)
    $newHooks = @($newHookTypes | Where-Object { $_ -notin $oldHookTypes })
    if (@($oldHookTypes | Where-Object { $_ -notin $newHookTypes }).Count) { throw 'Existing Harmony hook removed' }
    if (@($newHooks | Where-Object { $_ -notmatch 'Musketeer' }).Count) { throw 'Unexpected new Harmony hook' }
    $result = @{ baselineSha256 = (Get-FileHash -LiteralPath $oldPath).Hash; candidateSha256 = (Get-FileHash -LiteralPath $newPath).Hash;
        actionAtlasVerified = $true; existingHarmonyPatchTypesPreserved = $true; newHarmonyPatchTypes = $newHooks; 
        unchangedMethods = $same; changedMethods = @($changed | Sort-Object); addedMethods = @($added | Sort-Object); removedMethods = @($removed | Sort-Object);
        unchangedResources = $resources; newResources = $expected; version = $new.Name.Version.ToString() }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'receipts/dll-audit.json') -Encoding UTF8
    "Musketeer audit: $same unchanged methods, $($changed.Count) changed, $($added.Count) added, $($removed.Count) removed; all four PNGs unchanged."
} finally { $old.Dispose(); $new.Dispose() }
