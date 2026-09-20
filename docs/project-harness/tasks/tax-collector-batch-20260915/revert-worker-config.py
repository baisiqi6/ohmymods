from pathlib import Path
import json,hashlib
root=Path('C:/Users/ADMIN/projects/ohmymods')
global_path=Path('C:/Users/ADMIN/.omp/agent/config.yml')
added='composer:\n  shape: band\ntools:\n  approval:\n    bash: allow\n    eval: allow\n    hub: allow\n    task: allow'
raw=global_path.read_bytes();text=raw.decode('utf8');fixed=text.replace(added,'composer:\n  shape: band',1)
if text!=fixed:global_path.write_bytes(fixed.encode('utf8'))
project=root/'.omp/config.yml'
expected='# Project-local OMP settings for the ohmymods operator workflow.\n# Narrow per-tool approvals so the operator session (headless, no interactive\n# approval UI) can run builds/tests and launch supervised worker processes.\ntools:\n  approval:\n    bash: allow\n    eval: allow\n    hub: allow\n    task: allow\n'
deleted=[]
if project.exists():
    assert project.read_text()==expected,'project config changed concurrently; do not delete'
    project.unlink();deleted.append(str(project))
probe=root/'.tmp-tool-probe.txt'
if probe.exists():
    assert probe.read_text()=='probe\n';probe.unlink();deleted.append(str(probe))
receipt={'workerStopped':True,'globalExactAddedBlockRemoved':text!=fixed,'deletedExactWorkerFiles':deleted,'noGameWrites':True}
(Path(__file__).parent/'scope-incident.json').write_text(json.dumps(receipt,indent=2))
print(json.dumps(receipt))
