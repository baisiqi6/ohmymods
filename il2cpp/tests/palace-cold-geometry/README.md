# Palace cold geometry proof tests

```sh
python3 run-tests.py /absolute/external/output /absolute/path/to/dotnet
```

`extract.py` pulls the actual `PreparePopPrefix` / `PreparePopPostfix` / `PreparePopFinalizer`,
`TryGeometryLayout`, and the CourtArea `BeginSavedGeometryCall` / `CaptureSavedGeometry` /
`TryPublishSavedGeometry` / `TryAcceptColdGeometry` methods verbatim (only access widened for the
host). Host values replace Unity serialization, native pointers and the trip codec; they do not
prove a native Pop, Unity state or game acceptance.

Covered route: valid pre-Pop snapshot -> simulated native list consumption -> postfix/result
observation -> finalizer publication -> CourtArea consumption. Negative cases include missing
Ground, bad collider, duplicate UID, missing tile, wrong recipe, sidecar/owner/Game/Global/
Campaign/Island/Level/Trip/sequence drift, original skip/false/native exception, nested
replacement, pre-finalizer and old receipt. The current failing post-Pop JSON validation stays as
the negative control. Fresh, source, return and legacy44 routes are checked for no proof leakage.
