from pathlib import Path
import gzip,json,hashlib
task=Path(__file__).resolve().parent
source=Path('C:/Users/ADMIN/AppData/LocalLow/noio/KingdomTwoCrowns/Release/global-v35')
proof=json.loads((task/'receipts/latest-native-proof.json').read_text())
raw_bytes=source.read_bytes()
assert hashlib.sha256(raw_bytes).hexdigest()==proof['nativeFileSha256'],'Native save changed; stop and restage.'
text=gzip.decompress(raw_bytes).decode('utf8');data=json.loads(text)
assert data['_currentCampaign']==proof['campaign'] and data['_currentChallenge']==proof['challenge']
decoder=json.JSONDecoder();pos=text.index('[',text.index('"campaigns"'))+1
for i in range(proof['campaign']+1):
    while text[pos].isspace() or text[pos]==',':pos+=1
    campaign,end=decoder.raw_decode(text,pos)
    if i==proof['campaign']:fragment=text[pos:end]
    pos=end
assert campaign['currentLand']==proof['land']
pos=fragment.index('[',fragment.index('"_islands"'))+1
while True:
    while fragment[pos].isspace() or fragment[pos]==',':pos+=1
    island,end=decoder.raw_decode(fragment,pos)
    if island['land']==proof['land']:
        assert fragment[pos:end]==(task/'receipts/latest-current-island.json').read_text(encoding='utf8'),'Island fixture changed'
        break
    pos=end
print('Current native SHA and exact island JSON match frozen recovery evidence.')
