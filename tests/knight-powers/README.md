Managed regression linked directly to production source. Run `dotnet run -c Release --project Regression.csproj`. Stubs verify logic only; actual IL2CPP detours, GC and game startup require separate runtime evidence.

Wind-arc tests cover all 13 vertices, cached reuse, injected indexed failures and cleanup/retry. The forbidden bulk stub models the faulty API boundary, not real IL2CPP garbage collection.
