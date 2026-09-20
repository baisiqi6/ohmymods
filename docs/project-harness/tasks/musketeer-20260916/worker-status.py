from pathlib import Path
import json,sys
root=Path('C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/musketeer-implementation-20260916')
name=sys.argv[1] if len(sys.argv)>1 else 'scout'
p=root/(name+'.jsonl');items=[]
for line in p.read_text(encoding='utf-8-sig',errors='replace').splitlines():
    try:e=json.loads(line)
    except ValueError:continue
    if e.get('type')=='agent_end':items.append({'event':'agent_end'})
    if e.get('type')=='message_end' and e.get('message',{}).get('role')=='assistant':
        msg=e['message'];item={'stop':msg.get('stopReason'),'tools':[{'name':c.get('name'),'args':str(c.get('arguments',{}))[:120]} for c in msg.get('content',[]) if c.get('type')=='toolCall']}
        if msg.get('stopReason')=='stop':item['text']='\n'.join(c.get('text','') for c in msg.get('content',[]) if c.get('type')=='text')[-2600:]
        items.append(item)
print(json.dumps(items[-3:],ensure_ascii=False))
for p in (root/(name+'-sessions')).glob('*.jsonl'):
    for line in p.read_text(encoding='utf-8-sig',errors='replace').splitlines():
        try:e=json.loads(line)
        except ValueError:continue
        if e.get('type') in ('session','model_change','thinking_level_change'):print(json.dumps(e,ensure_ascii=False))
