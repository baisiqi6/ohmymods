from pathlib import Path
import sys
s=Path(sys.argv[1]).read_text()
def block(marker):
 a=s.index(marker);b=s.index('{',a);depth=1;i=b+1
 while depth:
  depth += (s[i]=='{')-(s[i]=='}');i+=1
 return s[a:i]
out='using System; using System.Globalization; using System.Text.Json.Serialization;\nnamespace KingdomEnhancedMod;\n'
out+=block('internal sealed class PalaceEntryAnchorData')+'\ninternal static class PalaceReturnAnchor {\n'
out+='private const float StandDistance=2f; private const float StandTol=.05f; private static readonly CultureInfo Inv=CultureInfo.InvariantCulture;\n'
out+=block('internal static bool ValidateData(')+'\n'
for marker in ['private static bool Finite(', 'private static string F(']:
 a=s.index(marker);out+=s[a:s.index(';',a)+1]+'\n'
target=Path(sys.argv[2]); target.parent.mkdir(parents=True,exist_ok=True); target.write_text(out+'}\n')
