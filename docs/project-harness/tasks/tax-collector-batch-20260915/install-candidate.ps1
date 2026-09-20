$ErrorActionPreference='Stop'
$game='E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091'
$target=Join-Path $game 'BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll'
$candidate='C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/tax-collector-batch-20260915/candidate/KingdomEnhancedMod.dll'
$audit=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'receipts/dll-audit.json')) | ConvertFrom-Json
function DataHashes {
    $map=[ordered]@{}
    foreach($folder in @('C:/Users/ADMIN/AppData/LocalLow/noio/KingdomTwoCrowns/Release',(Join-Path $game 'BepInEx/config'))) {
        if(Test-Path -LiteralPath $folder) {
            foreach($file in (Get-ChildItem -LiteralPath $folder -File -Recurse | Sort-Object FullName)) { $map[$file.FullName]=(Get-FileHash -LiteralPath $file.FullName).Hash }
        }
    }
    return $map
}
if(Get-Process KingdomTwoCrowns -ErrorAction SilentlyContinue){throw 'Game is running; installation deferred.'}
$old=(Get-FileHash -LiteralPath $target).Hash
$next=(Get-FileHash -LiteralPath $candidate).Hash
if($old -ne $audit.baselineSha256 -or $next -ne $audit.candidateSha256){throw 'DLL hash differs from audited baseline or candidate.'}
$before=DataHashes
$backup=$target+'.before-tax-collector-batch-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'.bak'
Copy-Item -LiteralPath $target -Destination $backup
if((Get-FileHash -LiteralPath $backup).Hash -ne $old){throw 'Backup hash mismatch.'}
if(Get-Process KingdomTwoCrowns -ErrorAction SilentlyContinue){throw 'Game started; no replacement performed.'}
Copy-Item -LiteralPath $candidate -Destination $target -Force
$after=DataHashes
if((Get-FileHash -LiteralPath $target).Hash -ne $next -or ($before|ConvertTo-Json -Compress) -cne ($after|ConvertTo-Json -Compress)){throw 'Post-install verification failed.'}
@{build='8.0.0-tax-collector-batch-20260915';dllSha256=$next;previousDllSha256=$old;target=$target;backup=$backup;
  userDataUnchanged=$true;userDataHashes=$after;gameStarted=$false;installedAt=(Get-Date).ToString('o')} |
  ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'receipts/install.json') -Encoding UTF8
'Tax assistant batch candidate installed, backed up and verified; no game start and user data unchanged.'
