from pathlib import Path
task=Path(__file__).resolve().parent
src=(task.parent/'hero-save-restore-20260915/audit-candidate.ps1').read_text(encoding='utf-8-sig')
src=src.replace('hero-live-fixes-20260915/candidate/KingdomEnhancedMod.dll','tax-collector-batch-20260915/before.dll')
start=src.index('    $allowed = ');end=src.index('\n',start)
src=src[:start]+"    $allowed = 'KingdomEnhancedMod\\.(BankAssistantCoordinator|PatchEconomy_BankAssistants|KingdomEnhancedPlugin)(::|/)'"+src[end:]
src=src.replace('Hero live fixes audit','Tax assistant batch audit')
(task/'audit-candidate.ps1').write_text(src,encoding='utf8')
src=(task.parent/'hero-live-fixes-20260915/install-candidate.ps1').read_text(encoding='utf-8-sig')
src=src.replace('hero-live-fixes','tax-collector-batch').replace('Shop stability hotfix','Tax assistant batch candidate')
(task/'install-candidate.ps1').write_text(src,encoding='utf8')
(task/'receipts').mkdir(exist_ok=True)
print('Prepared bounded audit and game-closed DLL-only installation scripts.')
