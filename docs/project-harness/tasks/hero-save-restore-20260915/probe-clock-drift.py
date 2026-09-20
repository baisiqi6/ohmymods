from pathlib import Path
import json,re,hashlib
p=Path(__file__).resolve().parent/'receipts'
text=(p/'latest-current-island.json').read_text(encoding='utf8')
archive=json.loads((p/'latest-hero-identities.json').read_text(encoding='utf-8-sig'))
scope=next(x for x in archive['scopes'] if x['scope'].startswith('7035'))
wanted={s['hash'] for s in scope['snapshots']}
number=json.loads(text)['_islandTimePlayed']
matches=[]
for value in range(number-32,number+33):
    changed=re.sub(r'"_islandTimePlayed":\d+',lambda m:'"_islandTimePlayed":'+str(value),text)
    h=hashlib.sha256((scope['scope']+'\n'+changed).encode()).hexdigest()
    if h in wanted:matches.append({'counter':value,'hash':h})
print(json.dumps(matches))
knight=Path('E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/BepInEx/config/KingdomEnhancedMod/ModSave/knight-identities.v1.json')
d=json.loads(knight.read_text(encoding='utf-8-sig'))
print('knight schema keys',list(d))
