# Palace durable layout CLR tests

Run without plugin deployment:

```sh
python3 run-tests.py /absolute/owned/task-output /absolute/path/to/dotnet
```

The project links the actual PaidJournal, TripContext, LayoutRecipe and ClockReceipt production files. `extract-anchor.py` extracts the current ReturnAnchor DTO, ValidateData and primitive helpers verbatim. Native host methods throw and are not invoked. The temporary physical journal test creates and removes only its own child of the test binary output directory.

The runner records build/run exit codes, raw logs and source SHA256 values. It does not build the plugin, invoke Unity, or write a player save. `CS0649` is suppressed because the full source-linked Clock DTOs are normally populated by excluded native adapters; other compiler warnings remain visible.

Coverage includes legacy codecs, schema-last parsing, strict new layout fields, all paid stages, mixed legacy history, unknown pending refusal, legacy return transitions, canonical/raw SHA/CAS/store readback and v4 anchor/receipt/layout tuples.
