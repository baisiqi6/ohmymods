from pathlib import Path
import re,json,collections
p=Path(__file__).resolve().parent
lines=(p/'LogOutput.log').read_text(encoding='utf-8-sig').splitlines()
events=[];messages=[]
for n,line in enumerate(lines,1):
    if '[Error ' in line or '[Warning:' in line or '[Musketeer] saved:' in line:messages.append({'line':n,'text':line})
    if '[HeroArcherNative]' not in line:continue
    m=re.search(r't=([\d.]+).*?actor=(-?\d+).*?nt=([\d.]+).*?pose=(\d+)',line)
    if m:events.append(dict(line=n,t=float(m[1]),actor=m[2],nt=float(m[3]),pose=int(m[4])))
groups=[]
for actor in set(e['actor'] for e in events):
    run=[]
    for e in [e for e in events if e['actor']==actor]:
        valid=e['pose'] in (10,16) and e['nt']==0
        follows=run and e['pose']!=run[-1]['pose'] and 0<e['t']-run[-1]['t']<.5
        if not(valid and follows):
            if len(run)>=8:groups.append({'actor':actor,'start':run[0],'end':run[-1],'transitions':len(run)-1})
            run=[]
        if valid:run.append(e)
    if len(run)>=8:groups.append({'actor':actor,'start':run[0],'end':run[-1],'transitions':len(run)-1})
text=(p/'Player.log').read_text(encoding='utf-8-sig')
blocks=re.split(r'\r?\n\s*\r?\n',text)
warnings=collections.Counter(b.splitlines()[0] for b in blocks if b.strip() and ('UnityEngine.Debug:LogError' in b or 'UnityEngine.Debug:LogWarning' in b))
result={'keyMessages':messages,'rapidWalkRunZeroPhase':groups,'playerLogErrorWarningHeads':dict(warnings)}
(p/'log-analysis.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(result,ensure_ascii=False))
