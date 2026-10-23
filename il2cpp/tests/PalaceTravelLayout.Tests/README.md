# Palace travel layout managed tests

```sh
python3 run-tests.py /absolute/external/task-output /absolute/path/to/dotnet
```

Output must be outside the product worktree. C# test inputs deliberately use `.cs.txt` with explicit Compile entries: the official plugin project uses a recursive C# glob. Generated C# is written only under the external output supplied to the runner.

The extractor compiles current production method bodies: payment callback, charge admission/Begin/Apply, handle destination getter/matching, cold destination verification, clock ReadTrip/HandleMatches, trip record initializer, visit/return/written-record checks, paid resume eligibility, source-save proof predicate and global-save-to-Reload wrapper. J codecs, recipe, clock receipt and journal Store are real source-linked code. ReturnAnchor's actual DTO/validator is extracted verbatim.

Native identity/capture, destination observation, resources, handoff and Game.Reload are controlled external-boundary adapters. Assertions cover managed ordering, decisions, real temporary Store writes/readback, and whether the production wrapper reaches an external action. They do not establish native first-coin timing, D's actual witness correctness, Unity state, save completion, or actual cold-game hydration. Those remain separate integration/native acceptance.

The runner writes build/run logs, exit codes and hashes of W source, linked dependencies and test inputs. Only its own temporary journal subdirectories under the test binary directory are created and removed. Other native compile-only methods throw and are not invoked.
