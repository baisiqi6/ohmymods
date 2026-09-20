# Authorized author pixel fire — local candidate

User confirms permission to use supplied author's effect, prefers its actual appearance over7.5 halo. Implemented and installed locally after verifying game closed; no commit or public-release change. Public7.5.0 ZIP/tag stays unchanged.

## Author evidence

Supplied DLL SHA256 b3f4148c37d18a7d3887c349f112d00005022b46480526f1a71edd90ca9ad088; zero embedded resources. Its Arrow.PixelFireAnimator differs from supplied txt FireAnimator. Actual DLL uses3 layers of5x5 grid cells (36vertices/150indices each), generated1x1 white Point Texture2D, Sprites/Default fallbackUnlit/Texture. Text describes37vertex radial fan, and was not used as final visual target. Root stopped/resumed same OMP session after reviewer found mismatch.

Pixel geometry/UV/triangles/jitter, Perlin noise at5x time, .22-grid rounding, EaseOutQuad growth,40%→100% EaseOutCubic fade/brightness/scale, layer size/color/duration ranges follow actual DLL. Original tearSizeMultiplier assignments occur afterspawn, so spawning really uses1; preserved rather than applying those misleading post-assignments early.

## Deliberate compatibility adaptations

Current independent all-world on/off switch retained (defaultoff, user's existingtrue retained), host/local visuals only. Orange-red fire is enabled by switch, no author's statue gate/purple statue color link. Only visuals ported: no isFireArrow/trail/nativeimpactSpawner/FireSplash/damage/bounce/despawn changes. No external image files. Sorting inherits arrow renderer and z stays at hitpoint rather than author's global30000/randomz. Fixed16 slots,24/sec,4/frame; cached native arrays, owned mesh/materials, shared ownedwhiteTexture; upper-bound1/2sec slotretirement, each layer hides at its actual duration. PrivateRNG avoids perturbing native combat randomness.

Independent review fixes: assign36vertices beforeUV/indices; hidefinishedlayer even when frame skipsduration; immediately registermaterial and cleantexture on initialization failure; cacheHitKind inPrefix before nativekill/pooling; one successful nativegeometry log. These protect realUnity resource/lifecycle semantics rather than only stub consistency.

## Verification

Root forced rebuilt impact test executable (Copy2 retained oldtimestamps could otherwise run old36cases; discarded that stale result). Final44impact cases pass: accepted nativehit, no nativebattle mutations, hierarchy/world/client gates, limits,5x5topology, pixelanim, hiddenfinishedlayers, killedtarget category, constructor failure cleanup and reuse. All28 current regression projects pass. Build0warnings/0errors;95 actual2.4 reachableUnity methods without unstripping stubs. Compared to public7.5,1884 unrelated methods unchanged; only impactmodule/wrapper, threeexpected Init/tooltip/stamp methods change.

Candidate DLL34182546EE8E4CDC682C13ED3E02C8F10123B1F9D951D73AE5B83760CDF97308, build7.5.0-author-pixel-impact-20260914, version7.5.0. BeforeDLL319418A0 backed up. Installed exactcorrectE Build22992091 with game closed; no savedata/config mutations during install.

Actual game PID26760 loaded candidate, native accepted arrow hit produced:
`pixel fire ready: shader=Sprites/Default vertexCount=36 indices=150 texture=1x1 filter=Point`.
Subsequent naturalnight operation emitted no impactfailure/invalidindices/ObjectCollected/unstripping logs. FinalClockDiag t0.35/day71/timeScale0, correct visiblewindow1451806 paused. Configvalues exactlyunchanged, ImpactEnabled=true; save9404B148 unchanged during this task. No temporaryruntimeprobe needed.

## Remaining validation

Aesthetic/occlusion match needs user visual confirmation. Windows screenshot capture fails SetIsBorderRequired0x80004002; accessibility pause plusnative logs establish runtimepath, not screenshot appearance. Actual repeatedworld/off-on and two-client visuals not newly tested; automatedlifecycle cases cover them atcodelevel. Keepdoing until visualacceptance, do not label identicalpixel-for-pixel or fullgame/coopverified. Earlier crash and unrelated feature pending items remain unchanged.

## Evidence

Operator author-impact-20260914: author-source.json, privateauthor-Arrow.cs extraction, mesh-interop.cs, original/resumed OMP session JSONL, worker/WORKER.md, impact-test-build.txt, impact-tests.txt, test-results.json, build.txt, api-audit.txt, unrelated-methods.json, install-receipt.json, runtime-receipt.json, runtime.log. No authorDLL or save shipped. User-facing explanation: no external fire image/sequence animation;1x1white generated at runtime, visible motion from gridvertices/scale/color/alpha.
