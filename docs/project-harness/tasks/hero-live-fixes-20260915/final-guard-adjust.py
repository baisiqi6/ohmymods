from pathlib import Path
root=Path(__file__).resolve().parents[4]
p=root/'il2cpp/HeroArcherGuardFacing.cs'
s=p.read_text(encoding='utf-8-sig')
assert 'internal bool KeepGuardLease;' not in s
s=s.replace('internal bool Owned;', 'internal bool Owned;\n        internal bool KeepGuardLease;',1)
# Lifecycle restore drops the lease; a temporary shot/menu suspension keeps it.
s=s.replace('receipt.ReleaseWanted = true;', 'receipt.KeepGuardLease = false;\n            receipt.ReleaseWanted = true;')
start=s.index('private static void Suspend('); end=s.index('private static void Retire(',start)
chunk=s[start:end].replace('receipt.KeepGuardLease = false;', 'receipt.KeepGuardLease = true;')
s=s[:start]+chunk+s[end:]
s=s.replace('if (life < 0) return;', 'if (life <= 0) return;').replace('if (life != 0 && life != receipt.Life)', 'if (life != receipt.Life)')
start=s.index('private static void TryRestore(');end=s.index('private static bool TryReadMode(',start)
chunk=s[start:end]
chunk=chunk.replace('if (!receipt.Owned)\n        {\n            Drop(receipt);', 'if (!receipt.Owned)\n        {\n            FinishRestore(receipt);')
chunk=chunk.replace('LogOnce("release:" + reason, null);\n            Drop(receipt);', 'LogOnce("release:" + reason, null);\n            FinishRestore(receipt);')
marker='if (!TryReadMode(mover, out Mover.FacingMode after)) return;'
pos=chunk.index(marker)+len(marker)
line_end=chunk.index('\n',pos)
chunk=chunk[:line_end+1]+'        if (after == receipt.Written) return; // No-op setter: restoration is still pending.\n'+chunk[line_end+1:]
chunk=chunk.replace('LogOnce("release:" + reason, null);\n        Drop(receipt);', 'LogOnce("release:" + reason, null);\n        FinishRestore(receipt);')
s=s[:start]+chunk+s[end:]
s=s.replace('    private static bool TryReadMode(', '''    private static void FinishRestore(Receipt receipt)
    {
        receipt.Owned = false;
        receipt.ReleaseWanted = false;
        if (!receipt.KeepGuardLease) Drop(receipt);
    }

    internal static bool HasOutstanding(int goId)
        => Receipts.TryGetValue(goId, out Receipt receipt) && receipt.Owned;

    private static bool TryReadMode(''',1)
p.write_text(s,encoding='utf8')
p=root/'il2cpp/HeroArcherRuntime.cs';s=p.read_text(encoding='utf-8-sig')
s=s.replace('HeroArcherGuardFacing.Restore(state.Ref);','HeroArcherGuardFacing.Restore(state.Ref);\n        if (HeroArcherGuardFacing.HasOutstanding(state.GoId)) return false;',1)
p.write_text(s,encoding='utf8')
p=root/'tests/hero-archer/runtime/RangeBoundary.cs';s=p.read_text(encoding='utf-8-sig')
s=s.replace('class HeroArcherGuardFacing {','class HeroArcherGuardFacing { internal static bool HasOutstanding(int id)=>false;',1)
p.write_text(s,encoding='utf8')
print('Applied identity-pending, no-op restore and retained guard-goal fixes.')
