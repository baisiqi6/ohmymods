# Destination admission drift

Pins the prepare -> charge comparison for `PalaceDestinationDecision`: cross-time identity is the
target/owner/epoch witness set (`SameTarget`), not whole-global raw bytes. A native autosave may
rewrite source progress between arm and charge; it must not be reported as destination drift.
Raw equality stays mandatory inside one capture's double read and against the exact paid
source-save proof.

`extract.py` copies the current production decision type and freeze/refresh/capture methods
verbatim from `il2cpp/Patch_PalaceDestinationSupport.cs`; the source SHA is emitted next to the
generated file. Test sources are `.cs.txt` so the product project glob never sees them.

Build to a private directory so no `bin/`/`obj/` lands in the repository:

```
dotnet build tests/palace-destination-admission-drift/AdmissionDrift.Tests.csproj -c Release \
  -o <private>/bin -p:BaseIntermediateOutputPath=<private>/obj/
dotnet <private>/bin/AdmissionDrift.Tests.dll <new-private-fixture-dir>
```

Covers fresh and saved full `TryFreezeForCharge` after an unrelated global rewrite, raw-only
decision equality, owner/RAM/Null-vs-OutOfRange refusal, first-difference labels, same-capture
double-read rejection with a stable re-read, and exact-vs-stale paid source-save refresh.
Synthetic fixtures only; Native saves, JSONUtility serialization, payment ordering and real
save/load are out of scope.
