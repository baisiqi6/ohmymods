# One-shot config preparation readiness

Extracts the actual VisibleEntry.Update config block and the actual Land12Runtime.Ready property verbatim. Host checks prove that table/generation readiness false waits before `_configPreparationGame` is consumed, readiness true permits exactly one call, and a terminal preparation failure is not retried in the same Game. The original bounded Playing probe/deadline is unchanged.

Use isolated `-o`, `BaseIntermediateOutputPath` and `MSBuildProjectExtensionsPath`; `-p:VisibleSource=<frozen-before.cs>` runs the old production block as RED. No game, native calls, payment or file writes occur.
