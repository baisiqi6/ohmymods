from pathlib import Path
import sys,hashlib
raw=Path(sys.argv[1]).read_bytes();s=raw.decode();out=Path(sys.argv[2]);out.parent.mkdir(parents=True,exist_ok=True)
def method(marker):
 start=s.index(marker);body=s.index('{',start);depth=0
 for i in range(body,len(s)):
  if s[i]=='{':depth+=1
  elif s[i]=='}':
   depth-=1
   if depth==0:return s[start:i+1]
 raise ValueError(marker)
core=method('    private static bool TryGetReusableClone(BiomeHolder holder, ulong holderPtr, out LevelConfig clone, out string report)')
parts=[core]
if '    private static bool TryGetReusableClone(BiomeHolder holder,ulong holderPtr,PalaceLayoutRecipe recipe,' in s:
 parts.insert(0,method('    private static bool TryGetReusableClone(BiomeHolder holder,ulong holderPtr,PalaceLayoutRecipe recipe,'))
 parts.append(method('    private static bool TryResolveConfigRecipe('))
text='using System;\nnamespace KingdomEnhancedMod;\ninternal static partial class PalaceLand12Runtime {\n'+ '\n'.join(parts)+'\n}'
out.write_text(text);out.with_suffix('.sha256').write_text(hashlib.sha256(raw).hexdigest()+'\n')
