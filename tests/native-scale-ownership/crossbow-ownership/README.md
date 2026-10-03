# Crossbow ownership boundary regression

This managed host directly compiles complete production GreekScaleScope,
PatchRoles_Worker, PatchRoles_KnightStyle and CrossbowmanLifecycle. The marker is
the real production CrossbowmanMarker; the facade forwards IsCrossbowman to the
real production identity reader. Game/Unity boundaries and unrelated careers are
stubs. No game or native Unity call is made.

The facade retains the actual old squad cleanup clone-pointer gate. The same-life
regression intentionally uses Fire SO, so that gate returns without cleanup:
OnDisable clears Active while Selected remains true, ConvertToHunter must retain
scale ownership, and real EnablePrefix/EnablePostfix/Reconcile must reopen with
the same Y request. Tests also generate real failed Strip/pool handoff through an
attack-getter fault, then prove that the native new Knight scale is permitted and
the later real Strip handoff leaves that new request intact.

Run Ownership.csproj with independent bin/obj outputs and BepInExPluginsPath
empty. Warmed traversal cost and all gameplay results remain separate concerns;
this suite tests ownership and lifecycle interaction only.
