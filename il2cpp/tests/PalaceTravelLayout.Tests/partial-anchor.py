from pathlib import Path
import sys
p=Path(sys.argv[1]);p.write_text(p.read_text().replace("internal static class PalaceReturnAnchor", "internal static partial class PalaceReturnAnchor",1))
