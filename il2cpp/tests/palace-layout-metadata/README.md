# Palace layout metadata / coverage checks

`Metadata.Tests.csproj` source-links the actual recipe and pure metadata/geometry reader. It covers closed schemas 1/2/3, old canonical wire bytes, schema-last ordering, malformed fields, exact tile grids, actual archived 2.4 GroundData/Tile schema, and negative geometry/ID cases.

`run-live-coverage.py --output <outside-repo-output> --dotnet <SDK-dotnet>` extracts the current production `TryLiveTileCoverage` method and compiles it against the explicit host types in `LiveCoverageStubs.cs.txt`. It tests adapter wiring with 100 spans, both beaches, and scale/count failures. The host supplies component/Tile span values and does not prove Unity cloning, saved IDs, native timing, scatter lifecycle, or collider physics.

The public native44 fixture uses synthetic IDs and only the Ground/Tile schema fields consumed by the reader; it contains no extracted save rows, campaign data or runtime identifiers. An optional first argument to the test executable reads a separately retained private native gzip to verify the complete real object array. That private run is separate evidence; neither fixture is a 400u Native result.

Keep all build/intermediate paths outside this source directory. No test deploys the plugin or writes a save.
