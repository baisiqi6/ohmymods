# Current island battle restored — 2026-09-14

User authorized preserving the current town and restoring serpent, built horse and night raids. Completed locally; no commit or release, no permanent mod changes.

## Change and preservation

Latest baseline A3806BCD (3205 objects), backed up before any write. Five scalar edits only (candidate06AFA896): quest9 5→4; secured9 true→false; Kingdom unsafe; serpent11→1; tail5→1. Exact-tree and byte-span validation preserved every other field. No entire-island rollback and no carryForward replay.

Temporary helper used the actual2.4 construction ProjectTemplate → TrojanHorse_MtOlympus_Serpent_Greece, registered SemiStatic NetID1640, validated Inactive0/ShouldPersist/Kingdom reference and exactly one saved horse, then IslandSaveData.Save + GlobalSaveData.SaveAsync reported Save, Success. No final-battle horse, bell trigger, troop spawn or ship spawn.

Native save83863274B0BC4C842BA8E917483C1FA7BCD611FC9B03A126B040AC8BBB7C7CA3 contains one horseState0, serpentMoving2, tailIdle1, secured9false, quest9step4, carryFalse. 132archers/35workers/4fleet boats unchanged; all other campaigns and island records deeply equal baseline. Object count3202 reflects normal native snapshot/world activity, not an offline deletion. User town and population retained. Currency follows normal simulation; bank not rolled back.

## Evidence and runtime validation

OMP deepseek/deepseek-v4-flash max implementation, task-scoped native session/worker artifacts. Root isolated runtime integration plus independent built-in reviewer: stable world identities before new operations, one async save only, pending timeout keeps gate, volatile callback publication, same Global/Game releases save gate, no null FSM, exact-one horse record. Build0warnings/0errors;73 reachable Unity API paths without unstripping stubs. Helper SHA3A937A9B.

Correct visible E Build22992091 PID1308 loaded repaired island. First run saved successfully, then natural time progression produced remote proximity wave. Serpent transitioned Moving2→Spawning3; enemy counts0→12→25 and native-night positive samples15, max125. Mouth portal stayed registered; night Kingdom became unsafe. Daytime isSafe=true is normal temporary daytime safety, not securedIslands; secured9 stayedfalse.

Exited owned game after successful native save; helper removed only after process exit. Restart PID16808 loads only main KingdomEnhancedMod319418A0. Player.log confirms native serpent Inactive→Moving and ordinary horse Inactive→Inactive, then RunningGame. Therefore horse/serpent persisted without repair plugin. Game window197632 correct path, foreground input accepted; ClockDiag t12.87 timeScale0 confirms final pause. No second-night retest needed to verify persisted records; visual attack/weakpoint defeat/full boss progression not separately tested. Screenshot capture unavailable on host; observations are native logs and bounded runtime state, not claimed visual inspection.

## Remaining unrelated issues

Existing first-run shutdown Menu.SetMenuInput/OnDisable NRE and prior game warnings remain recorded. This save repair does not establish a fix for the prior player crash, Hermes missing-original-hat report, or all pending optional-feature/coop acceptance.

## Local evidence and handoff

Operator folder: C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/restore-current-battle-20260914
- write-receipt.json and source-side .before-restore-battle backup
- dry-run.json, build.txt, api-audit.txt, worker/WORKER.md, sessions/
- completed.json, runtime-state.json, observations.jsonl, runtime-first.log
- disk-native-save-check.json, private-native-saved-global-v35, verification-summary.json
- runtime-reload.log and private-player-reload.log

Do not restore olderA380/A588/3DE saves over this repaired83863274 or subsequent user progress. Main DLL319418A0 unchanged; temporary helper removed; no lingering repair worker or extra game. Prior return-normal-island acceptance covered arrival but missed battle-state regression; this follow-up closes that gap.
