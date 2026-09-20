param(
    [string]$ProductionSource = "$PSScriptRoot/../../il2cpp/ScatterArrowTint.cs",
    [string]$RunName = 'verification'
)
# NOTE: keep this file ASCII-only. Windows PowerShell 5.1 decodes BOM-less scripts with the
# system ANSI codepage; stray multi-byte comment bytes could then decode to a trailing backtick
# and silently swallow the next statement.
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

# Step 1: real production file compiled against Unity/game/Harmony stubs; full regression suite.
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
$inCompileItems = [bool](Get-Content -LiteralPath $itemsLog | Select-String 'ScatterArrowTint.cs')

# Step 5: negative control - the shipped-interop surface must really be required by this gate.
$negLog = Join-Path $PSScriptRoot ($RunName + '-negative-no-interop.log')
& $dotnet build "$PSScriptRoot/actual-interop/ActualInterop.csproj" '-p:InteropDir=Z:\nonexistent' -v:m 2>&1 |
    Out-File -LiteralPath $negLog -Encoding utf8
$negExit = $LASTEXITCODE

$afterHash = Get-Sha256 $sourcePath
$receipt = [ordered]@{
    source = $sourcePath; before = $beforeHash; after = $afterHash
    testExit = $testExit; actualInteropBuildExit = $buildExit; modBuildExit = $modExit
    inCompileItems = $inCompileItems; negativeNoInteropExit = $negExit
    log = $logPath; buildLog = $buildLog; modLog = $modLog; compileItemsLog = $itemsLog; negativeLog = $negLog
}
$receipt | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $PSScriptRoot ($RunName + '-receipt.json')) -Encoding utf8

Get-Content -LiteralPath $logPath | Select-String 'FAIL|RESULT'
Get-Content -LiteralPath $buildLog | Select-String 'error|Build succeeded|succeeded'
Get-Content -LiteralPath $modLog | Select-String 'error|Build succeeded|succeeded'
if ($beforeHash -ne $afterHash) { throw 'Production source changed during the run; do not accept this receipt.' }
if ($buildExit -ne 0) { throw "Actual-interop build failed ($buildExit)." }
if ($modExit -ne 0) { throw "Mod build failed ($modExit)." }
if (-not $inCompileItems) { throw 'ScatterArrowTint.cs is not part of the mod compile items.' }
if ($negExit -eq 0) { throw 'The negative control unexpectedly succeeded.' }
exit $testExit
