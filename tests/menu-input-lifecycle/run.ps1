param(
    [string]$ProductionSource = "$PSScriptRoot/../../il2cpp/PatchLifecycle_MenuInput.cs",
    [string]$RunName = 'verification'
)
# NOTE: keep this file ASCII-only. Windows PowerShell 5.1 decodes BOM-less scripts with the
# system ANSI codepage; stray multi-byte comment bytes could then decode to a trailing backtick
# and silently swallow the next statement (observed: null $buildLog at Out-File).
$ErrorActionPreference = 'Stop'
if ($RunName -notmatch '^[a-zA-Z0-9_-]+$') { throw 'RunName must be a simple file name.' }
$dotnet = 'C:/Users/ADMIN/dotnet8/dotnet.exe'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false
$OutputEncoding = [Console]::OutputEncoding

function Get-Sha256([string]$path) {
    $sha = New-Object System.Security.Cryptography.SHA256Managed
    try { return ([System.BitConverter]::ToString($sha.ComputeHash([System.IO.File]::ReadAllBytes($path)))).Replace('-', '') }
    finally { $sha.Dispose() }
}

$sourcePath = (Resolve-Path -LiteralPath $ProductionSource).Path
$beforeHash = Get-Sha256 $sourcePath

# Step 1: real production prefix compiled against Unity/Rewired/Harmony stubs, lifecycle regression.
$logPath = Join-Path $PSScriptRoot ($RunName + '.log')
& $dotnet run --project "$PSScriptRoot/Regression.csproj" "-p:ProductionSource=$sourcePath" --no-launch-profile 2>&1 |
    Out-File -LiteralPath $logPath -Encoding utf8
$testExit = $LASTEXITCODE

# Step 2: compile gate against the shipped 2.4 interop (no deploy: this project has no copy step).
$buildLog = Join-Path $PSScriptRoot ($RunName + '-actual-interop.log')
& $dotnet build "$PSScriptRoot/actual-interop/ActualInterop.csproj" "-p:ProductionSource=$sourcePath" -v:m 2>&1 |
    Out-File -LiteralPath $buildLog -Encoding utf8
$buildExit = $LASTEXITCODE

# Step 3: whole mod against the dev-deps 2.4 interop; BepInExPluginsPath= keeps CopyOutput inert.
$modLog = Join-Path $PSScriptRoot ($RunName + '-mod-build.log')
& $dotnet build "$PSScriptRoot/../../il2cpp/KingdomEnhancedMod.csproj" -c BIE6_IL2CPP '-p:BepInExPluginsPath=' -v:m 2>&1 |
    Out-File -LiteralPath $modLog -Encoding utf8
$modExit = $LASTEXITCODE

# Step 4: prove the new file really is part of that build's compile items.
$itemsLog = Join-Path $PSScriptRoot ($RunName + '-compile-items.log')
& $dotnet msbuild "$PSScriptRoot/../../il2cpp/KingdomEnhancedMod.csproj" -p:Configuration=BIE6_IL2CPP -getItem:Compile 2>&1 |
    Out-File -LiteralPath $itemsLog -Encoding utf8
$inCompileItems = [bool](Get-Content -LiteralPath $itemsLog | Select-String 'PatchLifecycle_MenuInput.cs')

# Step 5: negative controls - both shipped-interop surfaces must really be required by this gate.
$negLog = Join-Path $PSScriptRoot ($RunName + '-negative-controls.log')
$negNoInterop = Join-Path $PSScriptRoot ($RunName + '-negative-no-interop.log')
$negNoRewired = Join-Path $PSScriptRoot ($RunName + '-negative-no-rewired.log')
& $dotnet build "$PSScriptRoot/actual-interop/ActualInterop.csproj" '-p:InteropDir=Z:\nonexistent' -v:m 2>&1 |
    Out-File -LiteralPath $negNoInterop -Encoding utf8
$negNoInteropExit = $LASTEXITCODE
& $dotnet build "$PSScriptRoot/actual-interop/ActualInterop.csproj" '-p:RewiredRefDir=Z:\nonexistent' -v:m 2>&1 |
    Out-File -LiteralPath $negNoRewired -Encoding utf8
$negNoRewiredExit = $LASTEXITCODE
$negLines = New-Object System.Collections.Generic.List[string]
$negLines.Add('InteropDir=Z:\nonexistent -> exit ' + $negNoInteropExit)
foreach ($m in (Get-Content -LiteralPath $negNoInterop | Select-String 'error CS' | Select-Object -First 2)) { $negLines.Add($m.Line.Trim()) }
$negLines.Add('RewiredRefDir=Z:\nonexistent -> exit ' + $negNoRewiredExit)
foreach ($m in (Get-Content -LiteralPath $negNoRewired | Select-String 'error CS' | Select-Object -First 2)) { $negLines.Add($m.Line.Trim()) }
$negLines | Out-File -LiteralPath $negLog -Encoding utf8

$afterHash = Get-Sha256 $sourcePath
$receipt = [ordered]@{
    source = $sourcePath; before = $beforeHash; after = $afterHash
    testExit = $testExit; actualInteropBuildExit = $buildExit; modBuildExit = $modExit
    inCompileItems = $inCompileItems
    negativeNoInteropExit = $negNoInteropExit; negativeNoRewiredExit = $negNoRewiredExit
    log = $logPath; buildLog = $buildLog; modLog = $modLog; compileItemsLog = $itemsLog
    negativeLog = $negLog; negativeNoInteropLog = $negNoInterop; negativeNoRewiredLog = $negNoRewired
}
$receipt | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $PSScriptRoot ($RunName + '-receipt.json')) -Encoding utf8

Get-Content -LiteralPath $logPath | Select-String 'FAIL|RESULT'
Get-Content -LiteralPath $buildLog | Select-String 'error|Build succeeded|succeeded'
Get-Content -LiteralPath $modLog | Select-String 'error|Build succeeded|succeeded'
Get-Content -LiteralPath $negLog
if ($beforeHash -ne $afterHash) { throw 'Production source changed during the run; do not accept this receipt.' }
if ($buildExit -ne 0) { throw "Actual-interop build failed ($buildExit)." }
if ($modExit -ne 0) { throw "Mod build failed ($modExit)." }
if (-not $inCompileItems) { throw 'PatchLifecycle_MenuInput.cs is not part of the mod compile items.' }
if ($negNoInteropExit -eq 0 -or $negNoRewiredExit -eq 0) { throw 'A negative control unexpectedly succeeded.' }
exit $testExit
