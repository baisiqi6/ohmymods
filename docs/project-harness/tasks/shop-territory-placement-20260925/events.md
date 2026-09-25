# Review / worker events

- 2026-09-25 15:47 CST: GLM design review, OMP session 01a0d788-777f-7416-9e35-9283cb76b2bc; native model_change=zhipu-coding-plan/glm-5.3, resolvedModelIsFallback=false; thinking=max. Initial REQUEST_CHANGES assumed subtraction of the exclusion superset. Operator clarified additive boundary-cell sampling; no subtraction, no fixed epsilon or fixed-grid fallback.
- 2026-09-25 15:56 CST: same independent GLM session reviewed correction and returned APPROVE; initial false-negative objection explicitly withdrawn. Non-finite boundaries, degenerate cells, removal of implementation-pinned tests included in worker contract.
- 2026-09-25 15:57 CST: built-in inherited-model worker /root/shop_placement_worker authorized for HeroShop.cs core/adapter/Create, MusketeerShop.cs Create, tests/hero-shop/Program.cs only. Follows weekday 14:00–18:00 routing. No commit/push/deploy/game/save/config authority.

- 2026-09-25 16:07 CST: independent GLM5.3/max implementation review APPROVE, same session/model/max with no fallback. Worker 67+18+60+35 checks passed; real2.4 interop and final full build 0W0E. Operator rechecked core67/wiring35 and exact protected samurai source hashes.
- Non-substantive review closeout: renamed one test description to match its existing rejection assertion; no logic changed, no new build needed. Interop.csproj and InteropStubs.cs are already tracked and unchanged (git ls-files verified), contrary to reviewer note. acceptance.md was created while review was running; operator verified current contents. These bookkeeping corrections do not change verdict or code contract.
- Current candidate 94CC219A / SHA256 9E56BF4BB873DC68F7D65A5492D52196D76CB5D8E9F2A7E6231C512F636411C4, build9.14.24-choreo-shopland-20260925. Game PID45240 still running; current E DLL8E994A98 verified, installation deferred under handoff rule. No commit/push/deploy/save/config/game mutation.

- 2026-09-25 23:55 CST: user confirmed game exit. Executed previously reviewed installation plan without scope change; zero Kingdom processes, candidate/source/protected-samurai hashes matched. Installed 94CC219A to E test copy after verified backup of 8E994A98. saveSnapshotUnchanged=true, configSnapshotUnchanged=true; gameStarted=false. Receipt receipts/install.json. No commit/push/publish.

- 2026-09-26 用户在安装94CC219A后反馈目前没有明显问题，并要求整理说明交原会话发布。本次重新核对已安装DLL摘要匹配，游戏日志build=9.14.24-choreo-shopland-20260925已出现。记录为当前单机游玩的整体反馈，不能等同联机/池化复生/密集场景专项验收。发布交接材料见release-handoff.md；本会话未提交或发布。
