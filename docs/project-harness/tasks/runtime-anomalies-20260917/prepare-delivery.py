from pathlib import Path
import re,json
task=Path(__file__).resolve().parent
s=(task.parent/'musketeer-hud-restock-20260917/audit-candidate.ps1').read_text(encoding='utf-8-sig')
start=s.index('    $allowed = ');end=s.index('\n',start)
s=s[:start]+r"    $allowed = 'KingdomEnhancedMod\.(KingdomEnhancedPlugin|PopulationPerformanceCoordinator|PopulationGrounding)(::|/)'"+s[end:]
start=s.index('    $resources = @()');end=s.index('    $newHookTypes =',start)
s=s[:start]+'''    $resources = @(); $changedResources = @()
    foreach ($resource in $old.MainModule.Resources) {
        if ($resource -isnot [Mono.Cecil.EmbeddedResource]) { continue }
        $current = $new.MainModule.Resources | Where-Object Name -eq $resource.Name | Select-Object -First 1
        if (!$current -or [Convert]::ToBase64String($resource.GetResourceData()) -cne [Convert]::ToBase64String($current.GetResourceData())) { throw "Unexpected resource change: $($resource.Name)" }
        $resources += $resource.Name
    }
    if ($old.MainModule.Resources.Count -ne $new.MainModule.Resources.Count) { throw 'Unexpected resource count' }
''' +s[end:]
s=s.replace('only authorized hero carry atlas may change.','all eight PNGs unchanged; blocked Hero/Crossbow production changes excluded.')
(task/'audit-candidate.ps1').write_text(s,encoding='utf-8-sig')
a=task/'receipts/dll-audit.json'
if a.exists():
    audit=json.loads(a.read_text(encoding='utf-8-sig'))
    install=(task.parent/'hero-center-grip-20260917/install-candidate.ps1').read_text(encoding='utf-8-sig')
    install=install.replace('hero-center-grip-20260917',task.name)
    install=install.replace('8.0.0-'+task.name,'8.0.0-population-ground-diagnostics-20260917')
    install=re.sub(r"\$expected = '[0-9A-F]+'", "$expected = '"+audit['candidateSha256']+"'",install)
    install=re.sub(r"\$previous = '[0-9A-F]+'", "$previous = '"+audit['baselineSha256']+"'",install)
    install=install.replace('Hero atlas and build marker only; no gameplay/runtime/save changes.','Population read-only diagnostics only; no physics, position, population setting, Hero/Crossbow or save changes.')
    (task/'install-candidate.ps1').write_text(install,encoding='utf-8-sig')
