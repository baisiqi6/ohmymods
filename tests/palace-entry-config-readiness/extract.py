from pathlib import Path
import sys,hashlib
raw=Path(sys.argv[1]).read_bytes();s=raw.decode();start=s.index('            if (PalaceLand12Runtime.Enabled && managers.game.currentLand != PalaceLand12Runtime.LandIndex');end=s.index('            Transform layer = managers.world.gameLayer;',start);block=s[start:end]
ready=[x for x in Path(sys.argv[2]).read_text().splitlines() if 'internal static bool Ready =>' in x][0]
out=Path(sys.argv[3]);out.parent.mkdir(parents=True,exist_ok=True)
out.write_text('using System;\nnamespace KingdomEnhancedMod;\ninternal static partial class PalaceLand12Runtime {\n'+ready+'\n}\ninternal static partial class EntryConfigProbe {internal static void Run(Managers managers) {\n'+block+'\n}}\n')
out.with_suffix('.sha256').write_text(hashlib.sha256(raw).hexdigest()+'\n')
