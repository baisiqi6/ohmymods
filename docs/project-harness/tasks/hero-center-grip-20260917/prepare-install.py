from pathlib import Path
import json
task=Path(__file__).resolve().parent
audit=json.loads((task/'receipts/dll-audit.json').read_text(encoding='utf-8-sig'))
s=(task.parent/'musketeer-live-fixes-20260917/install-candidate.ps1').read_text(encoding='utf-8-sig')
s=s.replace('musketeer-live-fixes-20260917',task.name)
import re
s=re.sub(r"\$expected = '[0-9A-F]+'", "$expected = '"+audit['candidateSha256']+"'",s)
s=re.sub(r"\$previous = '[0-9A-F]+'", "$previous = '"+audit['baselineSha256']+"'",s)
s=s.replace("    identityFinalReview = 'Prior full musketeer archive review gap remains; current independent review is bounded to this task changes.'", "    scope = 'Hero atlas and build marker only; no gameplay/runtime/save changes.'")
(task/'install-candidate.ps1').write_text(s,encoding='utf-8-sig')
