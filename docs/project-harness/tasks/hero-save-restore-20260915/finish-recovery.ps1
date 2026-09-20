$ErrorActionPreference='Stop'
$game='E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091'
$target=Join-Path $game 'BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll'
$sidecar=Join-Path $game 'BepInEx/config/KingdomEnhancedMod/ModSave/hero-identities.v1.json'
$suffix='.before-hero-save-restore-20260915-215829-856.bak'
$temporary=$sidecar+'.repair-472ebf68f5ef474187b8f5e9661fd271'
$audit=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'receipts/dll-audit.json') -Raw | ConvertFrom-Json
$repair=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'receipts/recovery-staged.json') -Raw | ConvertFrom-Json
function Closed { if(Get-Process KingdomTwoCrowns -ErrorAction SilentlyContinue){throw 'Game running; no recovery.'} }
function DataHashes {
    $map=[ordered]@{}
    foreach($folder in @('C:/Users/ADMIN/AppData/LocalLow/noio/KingdomTwoCrowns/Release',(Join-Path $game 'BepInEx/config'))) {
        foreach($file in (Get-ChildItem -LiteralPath $folder -File -Recurse | Sort-Object FullName)) { $map[$file.FullName]=(Get-FileHash -LiteralPath $file.FullName).Hash }
    }
    return $map
}
Closed
& python (Join-Path $PSScriptRoot 'verify-current-native.py')
if($LASTEXITCODE -ne 0){throw 'Native evidence changed'}
if((Get-FileHash -LiteralPath $target).Hash -ne $audit.candidateSha256 -or (Get-FileHash -LiteralPath ($target+$suffix)).Hash -ne $audit.baselineSha256){throw 'DLL or original backup mismatch'}
if((Get-FileHash -LiteralPath $sidecar).Hash -ne $repair.originalSidecarSha256 -or (Get-FileHash -LiteralPath ($sidecar+$suffix)).Hash -ne $repair.originalSidecarSha256){throw 'Original sidecar or backup mismatch'}
if((Get-FileHash -LiteralPath $temporary).Hash -ne $repair.recoveredSidecarSha256){throw 'Staged replacement mismatch'}
$before=DataHashes
$before | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'receipts/pre-recovery-hashes.json') -Encoding UTF8
Closed
$atomicBackup=$sidecar+'.replace-backup-'+[Guid]::NewGuid().ToString('N')
[IO.File]::Replace($temporary,$sidecar,$atomicBackup,$true)
if((Get-FileHash -LiteralPath $sidecar).Hash -ne $repair.recoveredSidecarSha256 -or (Get-FileHash -LiteralPath $atomicBackup).Hash -ne $repair.originalSidecarSha256){throw 'Post replacement hash mismatch'}
$after=DataHashes
$sidecarFull=[IO.Path]::GetFullPath($sidecar)
$tempFull=[IO.Path]::GetFullPath($temporary)
foreach($path in $before.Keys){if($path -ne $sidecarFull -and $path -ne $tempFull -and $before[$path] -ne $after[$path]){throw "Unrelated user data changed: $path"}}
& python (Join-Path $PSScriptRoot 'verify-current-native.py')
if($LASTEXITCODE -ne 0){throw 'Native data changed during recovery'}
@{build='8.0.0-hero-save-restore-20260915';dllSha256=$audit.candidateSha256;previousDllSha256=$audit.baselineSha256;
  target=$target;backup=($target+$suffix);sidecar=$sidecar;sidecarBackup=($sidecar+$suffix);atomicBackup=$atomicBackup;sidecarSha256=$repair.recoveredSidecarSha256;
  nativeAndOtherDataUnchanged=$true;userDataHashes=$after;gameStarted=$false;installedAt=(Get-Date).ToString('o');
  recoveryNote='Initial PowerShell null backup path rejected before sidecar mutation; resumed with explicit unique backup path and fresh checks.'} |
  ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'receipts/install.json') -Encoding UTF8
'Recovered two paid receipts in MOD sidecar; DLL, backups and unchanged native data verified. No game start.'
