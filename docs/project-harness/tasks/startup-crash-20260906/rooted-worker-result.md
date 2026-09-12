# Rooted Slash lease candidate

This independent candidate starts from canonical `il2cpp/PatchRoles_DeadlandsPowers.cs`, plus the original `knight-powers-tests/Program.cs` and `Stubs.cs`. No ZCode intermediate source was used. Only this worker directory was written.

## Production change

- Removed the complete `Knight_SlashDispose_Deadlands_Patch` class, including its Harmony attribute. No Dispose hook is discovered or installed.
- `ActiveSlash` contains at most one `SlashLease` per knight. The lease strongly references the actual `Knight._Slash_d__168` wrapper received by MoveNext. No raw pointer is used to reconstruct an object.
- The lease records `Time.time` on each owner MoveNext. A different iterator starting at state 0 may take over when the last heartbeat is at least 2 scaled seconds old. Paused scaled time does not expire it.
- Retirement marks the captured lease `Retired`, writes the rooted iterator state to -1, then removes only that exact lease from the dictionary. Old completion/exception paths cannot remove a replacement owner.
- A terminal iterator returns false in Prefix without executing native MoveNext. There is no tombstone list, grace period, timer, scan, or second persistent root per knight.
- Prefix captures its lease in Harmony `__state`. If OnDisable or takeover retires it while native MoveNext is executing, postfix writes -1 again and forces `ref __result=false`. This covers native code writing state 1 after a reentrant callback.
- Normal false return, native exception and OnDisable retire/remove the owned lease. Existing registered guards survive config, style and authority loss while the owner is still active. An ineligible replacement can proceed vanilla after retiring an expired owner, without acquiring a lease.
- Movement, attack cadence, shoot intervals and animation multiplier behavior is unchanged. Unregistered running vanilla iterators get no new lease or retirement behavior. The early terminal-state false return matches native terminal semantics.

The two-second interval is **takeover eligibility**, not unconditional cancellation. If the original owner resumes after a long silence before a competing start, it refreshes its heartbeat and continues. An abandoned iterator with no further calls remains rooted until a new start or OnDisable: storage stays one lease per knight. This avoids periodic scanning and does not create a conflicting new owner.

The strong-wrapper design relies on the locally inspected Il2CppInterop lifetime contract: `Il2CppObjectBase` construction creates `il2cpp_gchandle_new(ptr, false)`, where false means unpinned, not weak; finalization frees the handle. The managed regression stub cannot itself validate IL2CPP GC behavior. The independent reviewer verified the actual installed runtime contract.

## Verification

Command run from this directory:

```powershell
& 'C:/Users/ADMIN/dotnet8/dotnet.exe' run --project './Regression.csproj' -c Release
```

**27 passed, 0 failed.** Captured output: `test-results.txt`.

The stub's `NativeMoveNext` executes a callback and then writes state 1, so reentrant tests exercise the late native state write rather than merely invoking the hooks on manually corrected fields.

Coverage includes:

- Original movement/animation/shoot multipliers, restoration, Norse wallets and Medieval geometry regressions.
- A actually enters native and suspends at state 1; B takes over after 2 seconds; B completes; A's first late call is rejected without any test mutation of A's fields. B's native body asserts A is already terminal.
- Config-off, style-loss and authority-loss replacements that never acquire a lease; late A is still rejected.
- OnDisable and knight pool reuse, with late A both during B ownership and after B completion.
- Native body invokes OnDisable, optionally starts B, then native writes A's state 1 and returns true; postfix forces A terminal/false while retaining B.
- Old A throws after retirement and B registration; finalizer preserves the original exception and B's lease.
- Normal owner completion, overlap suppression, per-step heartbeat, paused scaled clock and renewal before any takeover.
- Non-deadlands vanilla execution remains unregistered and can resume after its own callback invokes OnDisable.

`production.diff` documents the bounded source change. `receipt.json` records source/candidate/test hashes.

Independent reviewer `startup_crash_review` reported no blocking production-source finding in its first pass. Operator owns final review, compilation against actual game dependencies, and controlled in-game validation. No game was launched, no DLL was deployed, no canonical source was changed, and no commit/push/install was performed.
