from pathlib import Path
root = Path(__file__).resolve().parents[4]
task = Path(__file__).resolve().parent
s = (task.parent/'musketeer-20260916/audit-candidate.ps1').read_text(encoding='utf-8-sig')
s = s.replace('musketeer-implementation-20260916/before.dll', 'musketeer-live-fixes-20260917/before.dll')
start=s.index('    $allowed = '); end=s.index('\n',start)
s=s[:start]+r"    $allowed = 'KingdomEnhancedMod\.(Musketeer[^:/]*|Kingdom_DistributeFreeArchers_MusketeerDefense_Patch|KnightIdentity[^:/]*|PatchRoles_KnightStyle|HeroShop|KingdomEnhancedPlugin)(::|/)'"+s[end:]
start=s.index('    $resources = @()'); end=s.index('    $newHookTypes =',start)
s=s[:start]+'''    $resources = @(); $changedResources = @()
    $expected = @('KingdomEnhancedMod.HeroShop.png','KingdomEnhancedMod.MusketeerShop.png')
    foreach ($resource in $old.MainModule.Resources) {
        if ($resource -isnot [Mono.Cecil.EmbeddedResource]) { continue }
        $current = $new.MainModule.Resources | Where-Object Name -eq $resource.Name | Select-Object -First 1
        if (!$current) { throw "Resource removed: $($resource.Name)" }
        if ([Convert]::ToBase64String($resource.GetResourceData()) -cne [Convert]::ToBase64String($current.GetResourceData())) {
            if ($resource.Name -notin $expected) { throw "Unexpected resource change: $($resource.Name)" }
            $changedResources += $resource.Name
        } else { $resources += $resource.Name }
        $disk = Join-Path $root ('il2cpp/Assets/' + $resource.Name.Substring('KingdomEnhancedMod.'.Length))
        if ([Convert]::ToBase64String($current.GetResourceData()) -cne [Convert]::ToBase64String([IO.File]::ReadAllBytes($disk))) { throw "Disk/embedded mismatch: $($resource.Name)" }
    }
    if (@(Compare-Object $expected $changedResources).Count) { throw 'Expected two shop resources to change' }
    if ($old.MainModule.Resources.Count -ne $new.MainModule.Resources.Count) { throw 'Unexpected resource count' }
''' +s[end:]
s=s.replace("    if (@($oldHookTypes | Where-Object { $_ -notin $newHookTypes }).Count) { throw 'Existing Harmony hook removed' }", "    $removedHooks = @($oldHookTypes | Where-Object { $_ -notin $newHookTypes })\n    if (@(Compare-Object @('KingdomEnhancedMod.Musketeer_DroppedGun_Patch') $removedHooks).Count) { throw 'Unexpected hook removal' }")
s=s.replace('existingHarmonyPatchTypesPreserved = $true;', 'removedHarmonyPatchTypes = $removedHooks;')
s=s.replace("$_ -notmatch 'Musketeer'", "$_ -notmatch 'Musketeer|KnightIdentityGenerationPatch'")
s=s.replace('newResources = $expected;', 'changedResources = $changedResources;')
s=s.replace('all four PNGs unchanged.', 'only two shop PNGs changed.')
(task/'audit-candidate.ps1').write_text(s,encoding='utf-8-sig')
