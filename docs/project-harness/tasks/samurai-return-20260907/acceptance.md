# Local Samurai return deployment

- Current dev stamp4.5.0-samurai-return-20260907, plugin metadata4.5.0.
- Canonical/source snapshot compile inputs equal; finalSamurai source4E69DF3FB7598D3E7F9B8966FDB7CF8CB65A5CF6B0346EE292C4C5A340E0C83E.
- E DLL 53E9A1DECFFD02E5E5E7519C331A984409C32E08D1D8F43EB457B89B36BF677E; previous6C6BA423... backed up before atomic replacement. Canonical build0W/0E; independent48tests plus operator rerun PASS; source review PASS;3hook/58Unity auditsPASS.
- Controlled launch 2026-09-07T21:30:11.7871675+08:00 to2026-09-07T21:31:16.1025311+08:00,64.3s, responsive through restored-scene/ClockDiag. Startup log includes correct dev stamp and noSamurai exception. Stop occurred after scene marker, with no intentional UI/gameplay input.
- Save SHA256 95172D158C29DCE1B42790BAF20FD9B416B5D22C66F0A8446BE5AADD73DE00B1 before/after equal. Existingbank HUD and120/4 defaults/config preserved; no configuration or save edits this turn. No actual return combat sequence or online round was visually verified; keep taskdoing pending player feedback.
- Publicv4.5.0 ZIP retains SHA256658c68074e6a618e0eb78b4a8b6a7618b77bbddf71260dd90e25774f43539e06; no commit/push/tag/release for this task.

Behavior: >10 units from a live assigned follower enters return before attack cooldown/enemy search; <=4 ends. Normal gap closes in one <=7-unit/.6s protected dash with white glow/trail and zero return damage. Exceptional distance uses bounded native running after the burst;3s whole-episode limit,0.5s no progress,2s backoff,three failed attempts for same follower. No smoke or teleportation implemented. GreekHammer/anvil research is separate and noGreek skill was added.
