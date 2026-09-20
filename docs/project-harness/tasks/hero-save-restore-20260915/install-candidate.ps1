$ErrorActionPreference='Stop'
$game='E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091'
$target=Join-Path $game 'BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll'
$sidecar=Join-Path $game 'BepInEx/config/KingdomEnhancedMod/ModSave/hero-identities.v1.json'
$candidateRoot='C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/hero-save-restore-20260915/candidate'
$candidate=Join-Path $candidateRoot 'KingdomEnhancedMod.dll'
$recovery=Join-Path $candidateRoot 'hero-identities.v2-recovered.json'
$audit=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'receipts/dll-audit.json') -Raw | ConvertFrom-Json
$repair=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'receipts/recovery-staged.json') -Raw | ConvertFrom-Json
function Closed { if(Get-Process KingdomTwoCrowns -ErrorAction SilentlyContinue){throw 'Game is running; installation deferred.'} }
function DataHashes {
    $map=[ordered]@{}
    foreach($folder in @('C:/Users/ADMIN/AppData/LocalLow/noio/KingdomTwoCrowns/Release',(Join-Path $game 'BepInEx/config'))) {
        foreach($file in (Get-ChildItem -LiteralPath $folder -File -Recurse | Sort-Object FullName)) { $map[$file.FullName]=(Get-FileHash -LiteralPath $file.FullName).Hash }
    }
    return $map
}
Closed
& python (Join-Path $PSScriptRoot 'verify-current-native.py')
if($LASTEXITCODE -ne 0){throw 'Native evidence verification failed'}
if((Get-FileHash -LiteralPath $target).Hash -ne $audit.baselineSha256 -or (Get-FileHash -LiteralPath $candidate).Hash -ne $audit.candidateSha256){throw 'DLL audit mismatch'}
if((Get-FileHash -LiteralPath $sidecar).Hash -ne $repair.originalSidecarSha256 -or (Get-FileHash -LiteralPath $recovery).Hash -ne $repair.recoveredSidecarSha256){throw 'Sidecar evidence changed'}
$before=DataHashes
$suffix='.before-hero-save-restore-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'.bak'
$dllBackup=$target+$suffix
$sidecarBackup=$sidecar+$suffix
Copy-Item -LiteralPath $target -Destination $dllBackup
Copy-Item -LiteralPath $sidecar -Destination $sidecarBackup
if((Get-FileHash -LiteralPath $dllBackup).Hash -ne $audit.baselineSha256 -or (Get-FileHash -LiteralPath $sidecarBackup).Hash -ne $repair.originalSidecarSha256){throw 'Backup mismatch'}
Closed
Copy-Item -LiteralPath $candidate -Destination $target -Force
if((Get-FileHash -LiteralPath $target).Hash -ne $audit.candidateSha256){throw 'Installed DLL mismatch'}
Closed
& python (Join-Path $PSScriptRoot 'verify-current-native.py')
if($LASTEXITCODE -ne 0 -or (Get-FileHash -LiteralPath $sidecar).Hash -ne $repair.originalSidecarSha256){throw 'Data changed before repair; recovery deferred'}
$temporary=$sidecar+'.repair-'+[Guid]::NewGuid().ToString('N')
Copy-Item -LiteralPath $recovery -Destination $temporary
Closed
[IO.File]::Replace($temporary,$sidecar,$sidecar+'.replace-backup-'+[Guid]::NewGuid().ToString('N'),$true)
if((Get-FileHash -LiteralPath $sidecar).Hash -ne $repair.recoveredSidecarSha256){throw 'Repaired sidecar mismatch'}
$after=DataHashes
$sidecarFull=[IO.Path]::GetFullPath($sidecar)
foreach($path in $before.Keys){if($path -ne $sidecarFull -and $before[$path] -ne $after[$path]){throw "Unrelated user data changed: $path"}}
@{build='8.0.0-hero-save-restore-20260915';dllSha256=$audit.candidateSha256;previousDllSha256=$audit.baselineSha256;
  target=$target;backup=$dllBackup;sidecar=$sidecar;sidecarBackup=$sidecarBackup;sidecarSha256=$repair.recoveredSidecarSha256;
  nativeAndOtherDataUnchanged=$true;userDataHashes=$after;gameStarted=$false;installedAt=(Get-Date).ToString('o')} |
  ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'receipts/install.json') -Encoding UTF8
'Installed audited DLL and repaired only MOD purchase associations; native saves unchanged. Game not started.'
