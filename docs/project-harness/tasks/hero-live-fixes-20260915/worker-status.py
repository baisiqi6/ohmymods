from pathlib import Path
import json
base=Path('C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/hero-live-fixes-20260915')
for name in ('stats','visual','guard'):
    events=[]
    path=base/name/'review-fix.jsonl'
    if not path.exists(): path=base/name/'worker.jsonl'
    for line in path.read_text(encoding='utf-8-sig').splitlines():
        try: event=json.loads(line)
        except ValueError: continue
        if event.get('type')=='agent_end': events.append({'type':'agent_end'})
        if event.get('type')=='message_end':
            msg=event.get('message',{})
            item={'role':msg.get('role'),'stopReason':msg.get('stopReason')}
            if msg.get('role')=='assistant':
                item['toolCalls']=[{'name':c.get('name'),'arguments':str(c.get('arguments',{}))[:160]} for c in msg.get('content',[]) if c.get('type')=='toolCall']
                if msg.get('stopReason')=='stop': item['text']='\n'.join(c.get('text','') for c in msg.get('content',[]) if c.get('type')=='text')[-2000:]
            events.append(item)
    print(name,json.dumps(events[-5:],ensure_ascii=False))
