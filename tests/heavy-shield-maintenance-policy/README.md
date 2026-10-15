# Heavy shield maintenance policy host checks

Run with a .NET 8 SDK:

```sh
dotnet run -c Release --project tests/heavy-shield-maintenance-policy
```

The executable links the two production policy files directly. It has no NuGet packages,
Unity/native stand-ins, or deployment targets. The current suite has 36 scenarios covering:

- v1/v2/v3 proportional migration, v4 identity, broken zero and original-format bounds;
- wear bands, six seconds of quiet, FIFO/receipt ordering, travel versus eight seconds of work;
- pause/resume, threat timestamps, changed durability/life, shop/world changes and malformed rosters;
- one-shot completion, definite rejection, accepted stale-snapshot suppression, uncertain results;
- barriers surviving ordinary Tick calls, Reset invalidation, and serial exhaustion without reuse.
- exact accepted receipt/life/full40 settlement, stale and repeated confirmations, preservation
  of unknown barriers, and bounded accepted history across 256 distinct completed careers.

Reflection sets the private counter to `long.MaxValue` for exhaustion and observes the private
accepted-fence count for bounded history; behavior uses the production completion API. No
production test hook is added. `ConfirmSettled` requires the synchronous adapter to prove both
the current policy and identity claim are full40 before acceptance and cleanup. Row absence
does not clear an accepted result.

These are value-policy tests. The caller must still prove the current native career and life,
world, shop, nearby-threat observation and arrival before supplying a snapshot, and revalidate
them before applying a completion. `LastThreatAt` is the latest actual threat/hit in scaled game
time: a changed timestamp cancels existing work even when the threat ended between observations.
Pause uses `playing:false` with truthful snapshots; it is not career invalidation.

This suite does not verify native movement, combat, shops, animation, save codec integration,
installation, or player acceptance.
