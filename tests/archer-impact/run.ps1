param(
    [string]$ProductionSource = "$PSScriptRoot/../../il2cpp/PatchArcher_Impact.cs",
    [string]$RunName = 'verification'
)
$ErrorActionPreference = 'Stop'
if ($RunName -notmatch '^[a-zA-Z0-9_-]+$') { throw 'RunName must be a simple file name.' }
$sourcePath = (Resolve-Path -LiteralPath $ProductionSource).Path
$beforeHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
$logPath = Join-Path $PSScriptRoot ($RunName + '.log')
& 'C:/Users/ADMIN/dotnet8/dotnet.exe' run --project "$PSScriptRoot/Regression.csproj" "-p:ProductionSource=$sourcePath" --no-launch-profile 2>&1 | Out-File -LiteralPath $logPath -Encoding utf8
$testExit = $LASTEXITCODE
$afterHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
$receipt = [ordered]@{ source = $sourcePath; before = $beforeHash; after = $afterHash; exit = $testExit; log = $logPath }
$receipt | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $PSScriptRoot ($RunName + '-receipt.json')) -Encoding utf8
Get-Content -LiteralPath $logPath | Select-String 'FAIL|RESULT|error '
if ($beforeHash -ne $afterHash) { throw 'Production source changed during the run; do not accept this receipt.' }
exit $testExit
