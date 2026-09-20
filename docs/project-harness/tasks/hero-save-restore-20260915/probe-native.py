from pathlib import Path
import gzip,json,hashlib,sys
task=Path(__file__).resolve().parent
source=Path('C:/Users/ADMIN/AppData/LocalLow/noio/KingdomTwoCrowns/Release/global-v35')
text=gzip.decompress(source.read_bytes()).decode('utf8')
global_data=json.loads(text);index=global_data['_currentCampaign'];decoder=json.JSONDecoder()
pos=text.index('[',text.index('"campaigns"'))+1
campaign_text=None
for n in range(index+1):
    while text[pos].isspace() or text[pos]==',':pos+=1
    data,end=decoder.raw_decode(text,pos)
    if n==index:campaign_text=text[pos:end];campaign=data
    pos=end
pos=campaign_text.index('[',campaign_text.index('"_islands"'))+1
while True:
    while campaign_text[pos].isspace() or campaign_text[pos]==',':pos+=1
    if campaign_text[pos]==']':break
    island,end=decoder.raw_decode(campaign_text,pos)
    if island['land']==campaign['currentLand']:
        raw=campaign_text[pos:end];break
    pos=end
prefix='latest-' if '--latest' in sys.argv else ''
archive_file=task/'receipts/hero-identities-before.json'
if prefix:
    archive_file=task/'receipts/latest-hero-identities.json'
    archive_file.write_bytes(Path('E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/BepInEx/config/KingdomEnhancedMod/ModSave/hero-identities.v1.json').read_bytes())
archive=json.loads(archive_file.read_text(encoding='utf-8-sig'))
matches=[]
for context in archive['scopes']:
    sha=hashlib.sha256((context['scope']+'\n'+raw).encode()).hexdigest()
    for snapshot in context['snapshots']:
        if snapshot['hash']==sha: matches.append({'scope':context['scope'],'hash':sha,'seats':snapshot['seats']})
result={'nativeFileSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'campaign':index,'challenge':global_data['_currentChallenge'],
        'land':island['land'],'biome':island['biome'],'islandSerializedFields':list(island),
        'islandRuntimeCreationTimePersisted':'realStartDateTime' in island,'exactRawIslandMatches':matches,
        'matchingPaidOwnerIds':[seat['nativeId'] for m in matches for seat in m['seats'] if any(o.get('uniqueID')==seat['nativeId'] for o in island['objects'])]}
(task/('receipts/'+prefix+'native-proof.json')).write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
(task/('receipts/'+prefix+'current-island.json')).write_text(raw,encoding='utf8')
print(json.dumps(result,ensure_ascii=False))
