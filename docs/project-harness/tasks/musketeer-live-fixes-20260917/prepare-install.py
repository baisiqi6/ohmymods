from pathlib import Path
import json
task=Path(__file__).resolve().parent
audit=json.loads((task/'receipts/dll-audit.json').read_text(encoding='utf-8-sig'))
s=(task.parent/'musketeer-20260916/install-candidate.ps1').read_text(encoding='utf-8-sig')
s=s.replace('musketeer-implementation-20260916','musketeer-live-fixes-20260917')
s=s.replace("$expected = '50AA30377A7C1799EDBC84F06E7771F541303E0BEB9A011170C9FBEEDD1ACC94'", "$expected = '"+audit['candidateSha256']+"'")
s=s.replace("$previous = '084ACEC85585D3C8C5CC101F2ABF5D1FF4AFE978E02C80532FD05D6C75BE70A6'", "$previous = '"+audit['baselineSha256']+"'")
s=s.replace('8.0.0-musketeer-20260916','8.0.0-musketeer-live-fixes-20260917')
s=s.replace('User explicitly requested continuing installation after disclosure of the identity review gap.','User requested fixes and previously authorized local testing-copy installation; game must remain closed.')
s=s.replace('Still incomplete; installation authorization does not constitute a review verdict.','Prior full musketeer archive review gap remains; current independent review is bounded to this task changes.')
(task/'install-candidate.ps1').write_text(s,encoding='utf-8-sig')
