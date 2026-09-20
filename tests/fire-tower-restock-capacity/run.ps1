param(
    [string]$ProductionSource = "$PSScriptRoot/../../il2cpp/FireTowerRestockCapacity.cs",
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

# Step 1: real helper compiled against stubs, boundary regression.
$logPath = Join-Path $PSScriptRoot ($RunName + '.log')
& $dotnet run --project "$PSScriptRoot/Regression.csproj" "-p:ProductionSource=$sourcePath" --no-launch-profile 2>&1 |
    Out-File -LiteralPath $logPath -Encoding utf8
$testExit = $LASTEXITCODE

# Step 2: compile gate against the shipped 2.4 interop (no deploy: no CopyOutput target here).
$buildLog = Join-Path $PSScriptRoot ($RunName + '-actual-interop.log')
& $dotnet build "$PSScriptRoot/actual-interop/ActualInterop.csproj" "-p:ProductionSource=$sourcePath" -v:m 2>&1 |
    Out-File -LiteralPath $buildLog -Encoding utf8
$buildExit = $LASTEXITCODE

$afterHash = Get-Sha256 $sourcePath
$receipt = [ordered]@{
    source = $sourcePath; before = $beforeHash; after = $afterHash
    testExit = $testExit; actualInteropBuildExit = $buildExit
    log = $logPath; buildLog = $buildLog
}
$receipt | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $PSScriptRoot ($RunName + '-receipt.json')) -Encoding utf8
Get-Content -LiteralPath $logPath | Select-String 'FAIL|RESULT'
Get-Content -LiteralPath $buildLog | Select-String 'error|Build succeeded|succeeded'
if ($beforeHash -ne $afterHash) { throw 'Production source changed during the run; do not accept this receipt.' }
if ($buildExit -ne 0) { throw "Actual-interop build failed ($buildExit)." }
exit $testExit
