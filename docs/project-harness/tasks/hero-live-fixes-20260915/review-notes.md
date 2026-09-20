# Independent investigation notes, not final verdict

- NativeEventKey excludes normalizedTime/frame; nt~0 transition logs do not prove frozen phase.
- Greek native Prepare clip is 2.1667s vs base 0.5s; shootPrepTime/hero fire-rate can truncate Prepare before quarter phase, so floor(nt*4) stays first frame. Verify actual timing before fix; do not write Animator or invent duration.
- Hero-only no-GuardSlot removes native fixed outward facing normally set in EnterGuardSlot; GoToWall ground movement does not set fixed facing at arrival. If reference itself faces inward, this needs minimal hero ground idle outside-facing behavior rather than atlas flip. If reference faces opposite own atlas, fix convention.
- No direct neighbor scale/material mutation found. Own body/cloth only absolute0.9, depth writesZ only, GreekScaleScope identity keyed absolute targetY. Need bounded rootY/scaleY/native sprite/bounds samples to distinguish true position jump from visual overlap/camera movement.
- Current logs are still grounding build; stability patch was installed after this log. User says new Esc behavior not tested yet and is trying it now.
- Actual DXBC: PowerSprite2 all60 pixelprograms have discard; PowerFire8 have no discard; SpriteMaskAllSprites1 no discard. Never infer alpha-depth from ZWrite alone.
