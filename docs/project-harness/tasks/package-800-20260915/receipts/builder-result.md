# 8.0.0 local snapshot packaging tools

Ready for Operator invocation. No final package was created by this worker; no canonical file, Git state, game directory, build output, or release was modified.

## Invocation (PowerShell)

```powershell
$task = 'C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/package-800-20260915'
$dll = "$task/build/bin/Debug/KingdomEnhancedMod.dll"
$output = 'C:/Users/ADMIN/projects/ohmymods/release/KingdomEnhancedMod_v8.0.0_IL2CPP.zip'
$baseCommit = '8efd1889a7333b5bbf74a2001262c5928a9c8357'
python "$task/pack-local.py" --dll $dll --source-manifest "$task/source-manifest.json" --base-commit $baseCommit --output $output
python "$task/verify-package.py" --package $output --dll $dll --source-manifest "$task/source-manifest.json" --base-commit $baseCommit
```

The packer requires all five canonical player documents to exist as UTF-8 and mention 8.0.0. It exclusively creates the output ZIP (`x` mode); an existing file causes failure and remains untouched. It does not invoke the canonical release script or change its clean-worktree requirement. Inputs are captured before output creation; an unexpected filesystem/write failure may leave a partial ZIP which must be inspected rather than silently overwritten.

The verifier independently implements the checks and writes `package-receipt.json` beside the scripts. It does not import the packer. The receipt records package SHA256/size/314 entries, runtime/document/source counts, DLL and source-manifest digests, validation results, and local/uncommitted/gameplay boundaries. A failed audit writes a failed receipt and exits nonzero.

## Frozen source manifest contract

```json
{"version":"8.0.0","files":[{"path":"il2cpp/KingdomEnhancedMod.csproj","sha256":"64 lowercase hex digits"}]}
```

Exactly those top-level and per-file keys are accepted. Files use portable repository-relative forward-slash paths inside `il2cpp/`; no absolute paths, traversal, duplicate case-folded paths, bin, or obj inputs are accepted. Every entry is checked against both canonical and task/source bytes, and the manifest must cover all frozen task/source/il2cpp files excluding bin/obj. The Operator's existing 117-entry manifest passes these checks. Its raw bytes are included as `SOURCE-MANIFEST.json`, with their SHA256 in `BUILD-MANIFEST.txt`; the script does not re-encode it.

`SourceState: uncommitted-snapshot`, `Packaging: local-not-published`, and the limited meaning of `BaseCommit` are explicit. No `GitCommit` claim is allowed. Full source bytes and build evidence remain in this local task; the distribution ZIP contains their source manifest, not the source files. Neither BaseCommit nor a list of source hashes by itself reproduces the DLL: preserve the frozen source directory, actual build recipe, and dependency evidence separately. The scripts check the PE header and 8.0.0.0 version-resource byte string as a precheck; the Operator's build/assembly audit supplies the actual managed metadata/build identity evidence.

## Runtime and package boundary

The sole runtime source is `KingdomEnhancedMod_v7.6.5_IL2CPP.zip`, pinned to SHA256 `64e8176f0f9117b225fd8fdd59132567c7392e5f897a3670564acb1bfebe076a`. Only the three root Doorstop files, dotnet/**, BepInEx/core/**, BepInEx/unity-libs/** and BepInEx/config/BepInEx.cfg are copied. All 306 runtime entries must be byte-identical to that baseline. The independent allowlist contains exactly those baseline entries plus one supplied DLL, five supplied documents, and two manifests: 314 entries.

The verifier checks CRC, unsafe names, case-folded duplicates, symlinks, all required loader/core runtime files, exact allowlist, forbidden personal config/game/save/sidecar/log/cache/interop/PDB artifacts, Doorstop IL2CPP target and disabled debug flag, matching input DLL/manifest/archive, UTF-8 versioned documents matching canonical, UTC timestamp, safe source manifest and frozen/canonical source coverage. Mono.Cecil.Pdb.dll is retained as an allowed baseline managed assembly. Any executable is permitted only if its exact dotnet path is pinned by the baseline.

## Verification performed by worker

- Real baseline read-only audit: SHA256 matched; 306 selected runtime entries; Doorstop BepInEx 6 IL2CPP and debug=false.
- Real frozen inputs: all 117 manifest source hashes and complete snapshot tree matched canonical.
- Real supplied build DLL: PE/header and 8.0.0.0 resource byte precheck passed.
- Isolated synthetic roundtrip: 314 entries, 43,339-byte ZIP; no real DLL or final output path used.
- 22 selftest checks passed: successful roundtrip, Mono.Cecil.Pdb.dll allowance, existing output retention, unsafe/absolute/traversal/backslash/case-collision names, unexpected config/interop/log/save/game/PDB files, runtime and DLL tampering, source-manifest tampering, missing/stale document, and false clean-commit claim rejection.

Selftest evidence is retained under `builder-selftest/`, including the script and `selftest-result.json`.
