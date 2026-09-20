from pathlib import Path
import json
p=Path('C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/tax-collector-worker.jsonl')
for line in p.read_text(encoding='utf-8-sig').splitlines():
    try:e=json.loads(line)
    except ValueError:continue
    if e.get('type')=='message_end' and e.get('message',{}).get('role')=='assistant':
        for c in e['message'].get('content',[]):
            if c.get('type')=='toolCall' and c.get('name') in ('write','edit'):
                args=c.get('arguments',{});print(json.dumps({'tool':c['name'],'args':args},ensure_ascii=False))
