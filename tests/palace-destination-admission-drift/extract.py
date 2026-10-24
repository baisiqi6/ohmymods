from pathlib import Path
import sys,hashlib,json
source=Path(sys.argv[1]);raw=source.read_bytes();s=raw.decode('utf-8');out=Path(sys.argv[2]);out.parent.mkdir(parents=True,exist_ok=True)
decision=s[s.index('internal sealed class PalaceDestinationDecision'):s.index('/// <summary>目的地分类与固定 profile')]
start=s.index('    // New fee path.');end=s.index('    /// <summary>源岛准备窗口调用：精确资源契约预检',start)
methods=s[start:end]
fields='\n'.join(line for line in s[s.index('internal static class PalaceDestinationAdmission'):].splitlines() if line.startswith(('    private const float Tolerance','    private const string BeachPrefabPath','    private static readonly object Gate'))) + '\n' + s[s.index('    private static PalaceDestinationDecision _destinationDecision;'):s.index('    // 资源预检缓存',s.index('internal static class PalaceDestinationAdmission'))]
text='using System;\nusing System.IO;\nusing System.Collections.Generic;\nusing UnityEngine;\nnamespace KingdomEnhancedMod;\n'+decision+'\ninternal static partial class PalaceDestinationAdmission {\n'+fields+'\n'+methods+'\n}'
out.write_text(text);out.with_suffix('.sha256').write_text(hashlib.sha256(raw).hexdigest()+'\n')
print('Extracted exact D producer methods from '+str(source)+' SHA256='+hashlib.sha256(raw).hexdigest())
