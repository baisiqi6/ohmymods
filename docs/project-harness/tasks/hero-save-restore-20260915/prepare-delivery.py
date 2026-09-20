from pathlib import Path
task=Path(__file__).resolve().parent
root=task.parents[3]
src=(task.parent/'hero-live-fixes-20260915/audit-candidate.ps1').read_text(encoding='utf-8-sig')
src=src.replace("hero-live-fixes-20260915/before.dll","hero-live-fixes-20260915/candidate/KingdomEnhancedMod.dll")
start=src.index("    $allowed = ")
end=src.index('\n',start)
src=src[:start]+"    $allowed = 'KingdomEnhancedMod\\.(HeroRecruitment|HeroRecruitmentArchive|HeroRecruitmentArchiveStore|HeroRecruitmentSnapshot|HeroRecruitmentContext|HeroRecruitmentContexts|HeroRecruitmentFingerprint|KingdomEnhancedPlugin)(::|/)'"+src[end:]
src=src.replace(" -or $resource.Name -eq 'KingdomEnhancedMod.HeroArcherAtlas.png'",'')
start=src.index('    $beforeArt = ')
end=src.index('    $newHookTypes = ',start)
src=src[:start]+"    if ($old.MainModule.Resources.Count -ne $new.MainModule.Resources.Count) { throw 'Unexpected resource' }\n"+src[end:]
src=src.replace('prepareObserverAfterBorrow = $true;','')
src=src.replace('three PNGs unchanged, action atlas verified','all four PNGs unchanged')
(task/'audit-candidate.ps1').write_text(src,encoding='utf8')
print('Prepared bounded method/resource audit.')
