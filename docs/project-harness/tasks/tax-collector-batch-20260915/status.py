from pathlib import Path
import json
p=Path('C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/tax-collector-worker.jsonl')
if p.with_name('tax-collector-retry.jsonl').exists():p=p.with_name('tax-collector-retry.jsonl')
entries=[]
for line in p.read_text(encoding='utf-8-sig').splitlines():
    try:e=json.loads(line)
    except ValueError:continue
    if e.get('type')=='agent_end':entries.append({'event':'agent_end'})
    if e.get('type')=='message_end' and e.get('message',{}).get('role')=='assistant':
        msg=e['message'];entry={'stop':msg.get('stopReason'),'tools':[{'name':c.get('name'),'args':str(c.get('arguments',{}))[:130]} for c in msg.get('content',[]) if c.get('type')=='toolCall']}
        if msg.get('stopReason')=='stop':entry['text']='\n'.join(c.get('text','') for c in msg.get('content',[]) if c.get('type')=='text')[-2200:]
        entries.append(entry)
print(json.dumps(entries[-3:],ensure_ascii=False))
for p in Path('C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/tax-collector-batch-20260915/retry-sessions').glob('*.jsonl'):
    for line in p.read_text(encoding='utf-8-sig').splitlines():
        try:e=json.loads(line)
        except ValueError:continue
        if e.get('type') in ('session','model_change','thinking_level_change'):print(json.dumps(e,ensure_ascii=False))
