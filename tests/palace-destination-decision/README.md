# Recipe-bound destination producer

`extract.py` copies the current production immutable decision, producer/freeze/refresh/cold resolver methods and their fields verbatim into the host; it also emits the full source SHA. The actual SourceProof, DestinationWitness, Recipe and C coverage source are linked. Native owner/provider/table/court IO adapters are explicit host boundaries. No replacement admission/fingerprint logic is supplied by tests.

Build with `dotnet build tests/palace-destination-decision/DestinationDecision.Tests.csproj -c Release -o <private>/bin -p:BaseIntermediateOutputPath=<private>/obj/ -p:MSBuildProjectExtensionsPath=<private>/obj/`, then run the output DLL with a new private fixture directory.

Covers all four fresh witnesses, saved400/strict old44, legal disk land0 vs hydrated RAM12, same-count content mutation, full freeze after cache-only CanPay, explicit source-save proof refresh including legal container replacement, table/owner/epoch/key drift, exact sidecar/target IO, R12 resource refusal and cold legacy proof caching. Existing H4 remains independently executable and unchanged. This suite does not prove native JSONUtility serialization, exact actual filename, callback/payment ordering or save/load.
