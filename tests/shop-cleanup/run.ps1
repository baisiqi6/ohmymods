param(
    [string]$CastleSource = "$PSScriptRoot/../../il2cpp/PatchRoles_Castle.cs",
    [string]$QueueSource = "$PSScriptRoot/../../il2cpp/ShopCleanupQueue.cs",
    [string]$RunName = 'verification'
)
$ErrorActionPreference = 'Stop'
if ($RunName -notmatch '^[a-zA-Z0-9_-]+$') { throw 'RunName must be a simple file name.' }
$castlePath = (Resolve-Path -LiteralPath $CastleSource).Path
$queuePath = (Resolve-Path -LiteralPath $QueueSource).Path
$castleBefore = (Get-FileHash -LiteralPath $castlePath -Algorithm SHA256).Hash
$queueBefore = (Get-FileHash -LiteralPath $queuePath -Algorithm SHA256).Hash
$logPath = Join-Path $PSScriptRoot ($RunName + '.log')
& 'C:/Users/ADMIN/dotnet8/dotnet.exe' run --project "$PSScriptRoot/Regression.csproj" "-p:CastleSource=$castlePath" "-p:QueueSource=$queuePath" --no-launch-profile 2>&1 | Out-File -LiteralPath $logPath -Encoding utf8
$testExit = $LASTEXITCODE
$castleAfter = (Get-FileHash -LiteralPath $castlePath -Algorithm SHA256).Hash
$queueAfter = (Get-FileHash -LiteralPath $queuePath -Algorithm SHA256).Hash
$receipt = [ordered]@{
    castle = $castlePath; castleBefore = $castleBefore; castleAfter = $castleAfter
    queue = $queuePath; queueBefore = $queueBefore; queueAfter = $queueAfter
    exit = $testExit; log = $logPath
}
$receipt | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $PSScriptRoot ($RunName + '-receipt.json')) -Encoding utf8
Get-Content -LiteralPath $logPath | Select-String 'FAIL|RESULT|error '
if ($castleBefore -ne $castleAfter -or $queueBefore -ne $queueAfter) { throw 'Production source changed during the run; do not accept this receipt.' }
exit $testExit
