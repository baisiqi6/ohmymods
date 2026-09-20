from pathlib import Path
import json
p=Path('C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/hero-save-restore-20260915/worker.jsonl')
if p.with_name('review-fix.jsonl').exists():p=p.with_name('review-fix.jsonl')
events=[]
for line in p.read_text(encoding='utf-8-sig').splitlines():
    try:e=json.loads(line)
    except ValueError:continue
    if e.get('type')=='agent_end':events.append({'type':'agent_end'})
    if e.get('type')=='message_end' and e.get('message',{}).get('role')=='assistant':
        msg=e['message'];item={'stopReason':msg.get('stopReason')}
        item['tools']=[{'name':c.get('name'),'args':str(c.get('arguments',{}))[:140]} for c in msg.get('content',[]) if c.get('type')=='toolCall']
        if msg.get('stopReason')=='stop':item['text']='\n'.join(c.get('text','') for c in msg.get('content',[]) if c.get('type')=='text')[-3500:]
        events.append(item)
print(json.dumps(events[-3:],ensure_ascii=False))
