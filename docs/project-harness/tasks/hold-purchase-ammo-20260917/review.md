# Independent bounded review — hold-purchase ammo

Verdict: **PASS for the reviewed candidate; no remaining blocking finding in this slice.** This is a source/artifact review, not a claim of actual game or multiplayer acceptance.

Candidate SHA256: `0B1AD4E149A0BF1BBAD90DE62A574A87F68B33E2A607CDBB7B46122EF695E55D`.
Baseline side-rack DLL: `6E2D89F1B0F35A504257CA187828C7B09C01336A7E2E3A2ADFE590D0934AA8BE`.
Reviewed PatchPlayer_HoldPurchase.cs SHA256: `FC85D406A72C896FFF3D59BAAEC7922D5676C26FF947560BC8E0C9F492F79940`.

## Source review

- Compared the actual source with the exact release-900 clean source (the side-rack change did not modify HoldPurchase). The extension recognizes PayableWorkshopBarrel, or a PayableComponent whose same active GameObject contains FireTower and whose owner pointer exactly matches that tower. Generic component tags cannot bypass this owner check. FireTower AI enabled is deliberately not a prerequisite; the payable and GameObject still have to be enabled/active.
- The session retains its ammo owner pointer and rechecks target instance, current scope, activity and owner before acceleration/continuation, while waiting, in Tick, and on PerformPay. Replacing the owner with another valid tower ends the old hold. The observed disable/Tick/re-enable path requires a fresh press.
- Review found a partial Bind exception path that could leave cached goods=true after the later owner/type read failed. The final source explicitly clears ShopIsGoods in that catch (line 910). The added regression throws on the third TryCast after classification succeeds and checks no accelerated interval, no synthetic continuation, and only the original native purchase charge.
- Native CanSelect/CanPay, full-price wallet, range, pause, player authority and existing transaction gates remain. The extension does not debit money, create ammo, set price/capacity, or call native payment completion itself. The existing host Completed-to-None fallback is unchanged; clients still require the existing receipt path and do not treat local completion as server approval. Default false and OptionalQoL world scope are unchanged. Config and panel changes are help text only.

## Evidence checked

- Read final worker logs: all 65 behavioral scenarios pass (25 existing plus 40 ammo scenarios), including both prices, first coin/0.6-second threshold, feature-off equivalence, native refusal/capacity/funds gates, owner changes, client waiting/denial/refund, AI-disabled tower and partial-Bind exception.
- Read the actual-interop project and its final build log: it links the real production HoldPurchase file against the installed 2.4 interop assemblies; 0 warnings / 0 errors. Main-build receipt also reports 0 warnings / 0 errors. Tests were not independently rerun by this read-only reviewer.
- Independently read both exact DLLs with Mono.Cecil and compared method instruction bodies, locals, stack/init flags and exception handlers: **3,618 unchanged, 9 changed, 1 helper added, 0 removed**, matching the receipt. Changed methods are confined to HoldPurchase and plugin/config/panel initialization or text. HarmonyPatch type sets are identical. All **8 embedded PNG resources are byte-identical**, including the side rack. Assembly version remains 9.0.0.0.
- Independently hashed the candidate and all four changed source files; they match the frozen receipts. No production source beyond the four listed files is claimed as part of this change; source-delta receipt reports no new production files.

## Limits

The tests execute production policy against a modeled native transaction boundary, not the actual 2.4 game loop. Real 5-coin barrel / 2-coin fire-ammo repetition, dynamic facility capacity, and main/client RPC behavior still need gameplay observation. This review does not validate unseen pool transitions, prior side-rack pickup behavior, the old full Musketeer archive, or the blocked Hero/Crossbow diagnostic work. No game, save, configuration, installation, Git or publication mutation was performed by this reviewer; only this review evidence was written.
