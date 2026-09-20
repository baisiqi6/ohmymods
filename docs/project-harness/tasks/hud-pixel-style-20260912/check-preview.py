from pathlib import Path
import re,subprocess
p=Path('C:/Users/ADMIN/.codex/visualizations/2026/09/05/01a06fd5-70a6-7150-bc85-16ea0c87ffb5/kingdom-hud-layout.html')
text=p.read_text(encoding='utf-8');assert '\\"' not in text and '\\n' not in text
js=re.search(r'<script>([\s\S]*?)</script>',text).group(1)
w=Path(__file__).parent
(w/'preview-script.js').write_text(js,encoding='utf-8')
subprocess.run(['C:/Program Files/nodejs/node.exe','--check',str(w/'preview-script.js')],check=True)
print('PASS preview fragment literal markup and JavaScript syntax')
