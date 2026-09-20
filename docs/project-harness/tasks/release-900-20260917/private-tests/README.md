# Local private test archive — exclude from public release

`RealFixtureCases.cs.txt` preserves only the removed real-user-fixture methods and their fixture reader; `PrivateFixtureReferences.xml.txt` preserves the four original local fixture references. These text files are not compiled or referenced by the portable project. They still refer to existing local historical fixtures and one recovery candidate; no save/config/fixture file was changed or copied.

The public `tests/hero-recruitment` project runs only its existing synthetic cases. Keep this entire private-tests directory out of the release source whitelist and public archives.
