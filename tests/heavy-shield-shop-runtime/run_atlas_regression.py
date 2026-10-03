"""Run the final verifier without sharing Root's shop-runtime evidence outputs."""
from pathlib import Path
import runpy
import sys
source = Path(__file__).resolve().parents[1] / "heavy-shield-art" / "verify_atlases.py"
if "--evidence" not in sys.argv:
    sys.argv.extend(["--evidence", str(source.parent / "evidence" / "shop-regression")])
runpy.run_path(str(source), run_name="__main__")
