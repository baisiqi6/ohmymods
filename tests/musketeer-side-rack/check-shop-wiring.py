"""Static wiring checks for the production native adapter, separate from policy behavior tests.

This proves dependency/guard placement, not Unity execution or live performance timings.
"""
from pathlib import Path

root = Path(__file__).resolve().parents[2]
shop = (root / "il2cpp/MusketeerShop.cs").read_text(encoding="utf-8-sig")
adapter = (root / "il2cpp/MusketeerRestock.cs").read_text(encoding="utf-8-sig")
identity = (root / "il2cpp/MusketeerIdentity.cs").read_text(encoding="utf-8-sig")

def body(source, signature):
    start = source.index("{", source.index(signature))
    depth = 1
    for end in range(start + 1, len(source)):
        if source[end] == "{":
            depth += 1
        elif source[end] == "}":
            depth -= 1
            if depth == 0:
                return source[start + 1:end]
    raise AssertionError("incomplete method: " + signature)

checks = 0
def check(value, message):
    global checks
    assert value, message
    checks += 1

gate = body(shop, "private static bool RackContextReady()")
read = body(shop, "private static bool ReadRackItems()")
anchor = body(shop, "private static bool AnchorRackGun(")
payment = body(shop, "internal static bool CanPurchase()")
creation = body(shop, "private static bool TryCreateGun(")
check("TryGetRestockCounts" not in shop, "manual layout must not depend on full automatic population coverage")
check("MusketeerIdentity.TryGetRestockCounts" in adapter, "automatic procurement keeps its full population gate")
rackcount = body(shop, "private static int RackCount()")
check("if (item.Unclaimed && !RackLayout.IsPlaced(item)) return MusketeerShopRules.RackCapacity;" in rackcount,
      "rack count: claimed guns occupy slots, unanchored unclaimed stays fail-closed")
check("if (!ReadRackItems()) return MusketeerShopRules.RackCapacity;" in rackcount,
      "unreadable rack identity still fails the count closed")
check("item.Unclaimed = gun.friendlyClaimer == null && gun.enemyClaimer == null && !gun.pickedUp;" in read,
      "any-claimant flag computation unchanged")
check("_nextRackLayoutAt = 0f" in creation and creation.index("if (!marked)") < creation.index("_nextRackLayoutAt = 0f"),
      "successful purchase arms immediate rack reconcile after registration")
check(creation.count("_nextRackLayoutAt") == 1,
      "failure paths never touch the reconcile schedule")
proof_body = body(identity, "internal static bool StockClaimProven(")
check("return !CollectedOrClaimed(career.Tool);" in proof_body,
      "rack proof still rejects through the shared collected/claimed judgment")
collected = body(identity, "private static bool CollectedOrClaimed(")
check("tool.pickedUp" in collected and "tool.enemyClaimer != null" in collected,
      "enemy-claimed/picked-up stock still fails the whole rack read (shop lock)")
for flag in ["!state.Ready", "state.ReadOnly", "state.Unresolved", "!state.HasBaseline", "state.Epoch == null", "state.StockRestores.Count != 0"]:
    check(flag in gate, "manual identity flag: " + flag)
check("state.Careers" not in gate, "context gate never visits historical units")
check("TryContext(out string context, out long world)" in gate, "reuse cached identity context; no forced fresh roster")
check("context == state.ContextKey && world == state.World" in gate, "explicit state/context/world correspondence")
check("_contextFrame == Time.frameCount" in identity, "existing identity cache is frame scoped")
for flag in ["IslandSaveData.isSavingGame", "Time.timeScale <= 0f", "HeroShopRetention.CanServe(Observe("]:
    check(flag in gate, "immediate context guard: " + flag)
skip = read.index("if (career.Kind != MusketeerCareer.KindGun || career.StockSlot == MusketeerCareer.NoStockSlot) continue;")
invalid = read.index("!MusketeerShopRules.ValidSlot(career.StockSlot)")
duplicate = read.index("(occupied & (1 << career.StockSlot)) != 0")
proof = read.index("MusketeerIdentity.StockClaimProven(career)")
check(skip < invalid < duplicate < proof, "units/dropped guns skip before native calls; invalid/duplicate slots stop before proof")
check("occupied |= 1 << career.StockSlot" in read, "three legal distinct slots bound native gun inspections to three")
check(all(name not in read for name in [".Character", ".Archer", "_damageable", "CopyUnits"]), "no per-unit native access in rack read path")
check("!MusketeerIdentity.CanPurchase" in payment, "manual shop retains original eligibility")
check("MusketeerIdentity.TryRegisterPaidGun(gun, slot)" in creation, "paid identity registration remains authoritative")
write = anchor.index("gun.transform.position = target")
for flag in ["RackContextReady()", "career.Life != stamp.Life", "career.StockSlot != slot", "StockClaimProven(career)", "gun.pickedUp", "gun.friendlyClaimer != null", "gun.enemyClaimer != null"]:
    check(0 <= anchor.index(flag) < write, "reanchor checks before actual position write: " + flag)
check("SamePosition(gun.transform.position, target)" in anchor[write:], "placement receipt requires native position readback")
print(f"PASS {checks} production shop wiring checks: no full-unit count dependency; at most three distinct rack gun proofs per read")
