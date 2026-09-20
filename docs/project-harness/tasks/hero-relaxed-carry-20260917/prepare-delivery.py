from pathlib import Path
import json
task=Path(__file__).resolve().parent
old=task.parent/'musketeer-live-fixes-20260917'
s=(old/'audit-candidate.ps1').read_text(encoding='utf-8-sig')
s=s.replace("'C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/musketeer-live-fixes-20260917/before.dll'", "(Join-Path $PSScriptRoot 'before.dll')")
start=s.index('    $allowed = ');end=s.index('\n',start)
s=s[:start]+r"    $allowed = 'KingdomEnhancedMod\.KingdomEnhancedPlugin(::|/)'"+s[end:]
s=s.replace("@('KingdomEnhancedMod.HeroShop.png','KingdomEnhancedMod.MusketeerShop.png')", "@('KingdomEnhancedMod.HeroArcherAtlas.png')")
s=s.replace("if (@(Compare-Object @('KingdomEnhancedMod.Musketeer_DroppedGun_Patch') $removedHooks).Count)", "if ($removedHooks.Count)")
s=s.replace("if (@($newHooks | Where-Object { $_ -notmatch 'Musketeer|KnightIdentityGenerationPatch' }).Count)", "if ($newHooks.Count)")
s=s.replace('Expected two shop resources to change', 'Expected only hero atlas to change').replace('only two shop PNGs changed.', 'only hero atlas changed.')
(task/'audit-candidate.ps1').write_text(s,encoding='utf-8-sig')
