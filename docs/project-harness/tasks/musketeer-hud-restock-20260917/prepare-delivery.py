from pathlib import Path
import re,json
task=Path(__file__).resolve().parent
s=(task.parent/'hero-relaxed-carry-20260917/audit-candidate.ps1').read_text(encoding='utf-8-sig')
start=s.index('    $allowed = ');end=s.index('\n',start)
s=s[:start]+r"    $allowed = 'KingdomEnhancedMod\.(KingdomEnhancedPlugin|PopulationCounts|PopulationHud|ModConfig|ModPanel|PatchEconomy_AutoRestock|AutoRestockCounts|MusketeerShop[^:/]*|MusketeerIdentity|MusketeerRestock[^:/]*)(::|/)'"+s[end:]
start=s.index('    $resources = @()');end=s.index('    $newHookTypes =',start)
s=s[:start]+'''    $resources = @(); $changedResources = @()
    foreach ($resource in $old.MainModule.Resources) {
        if ($resource -isnot [Mono.Cecil.EmbeddedResource]) { continue }
        $current = $new.MainModule.Resources | Where-Object Name -eq $resource.Name | Select-Object -First 1
        if (!$current) { throw "Resource removed: $($resource.Name)" }
        if ([Convert]::ToBase64String($resource.GetResourceData()) -cne [Convert]::ToBase64String($current.GetResourceData())) {
            if ($resource.Name -ne 'KingdomEnhancedMod.HeroArcherAtlas.png') { throw "Unexpected resource change: $($resource.Name)" }
            $disk = Join-Path $root 'il2cpp/Assets/HeroArcherAtlas.png'
            if ([Convert]::ToBase64String($current.GetResourceData()) -cne [Convert]::ToBase64String([IO.File]::ReadAllBytes($disk))) { throw 'Hero atlas disk/embedded mismatch' }
            $changedResources += $resource.Name
        } else { $resources += $resource.Name }
    }
    if ($old.MainModule.Resources.Count -ne $new.MainModule.Resources.Count) { throw 'Unexpected resource count' }
''' +s[end:]
s=s.replace('only hero atlas changed.','only authorized hero carry atlas may change.')
(task/'audit-candidate.ps1').write_text(s,encoding='utf-8-sig')

audit=task/'receipts/dll-audit.json'
if audit.exists():
    a=json.loads(audit.read_text(encoding='utf-8-sig'))
    install=(task.parent/'hero-relaxed-carry-20260917/install-candidate.ps1').read_text(encoding='utf-8-sig')
    install=install.replace('hero-relaxed-carry-20260917',task.name)
    install=re.sub(r"\$expected = '[0-9A-F]+'", "$expected = '"+a['candidateSha256']+"'",install)
    install=re.sub(r"\$previous = '[0-9A-F]+'", "$previous = '"+a['baselineSha256']+"'",install)
    install=install.replace('Hero atlas and build marker only; no gameplay/runtime/save changes.','Musketeer HUD/restock integration; old archive full-review gap unchanged.')
    (task/'install-candidate.ps1').write_text(install,encoding='utf-8-sig')
