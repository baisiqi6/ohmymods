$ErrorActionPreference = 'Stop'
# User explicitly requested installing the existing candidate on 2026-09-16.
# Installation only: no source rebuild, game launch, configuration or save mutation.
$game = 'E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091'
$target = Join-Path $game 'BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll'
$artifacts = 'C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/hero-center-grip-20260917'
$candidate = Join-Path $artifacts 'candidate/KingdomEnhancedMod.dll'
$expected = '069527B6529AD86C9D29F472186F0B2EE037A91E62956C6E927B34A5CFBEB3C2'
$previous = 'EC80BF3CA26C3DF812C3B66A5C57B0FE7559CB1A575AD0DD522E768C2A344C89'
function AssertClosed {
    if (Get-Process KingdomTwoCrowns -ErrorAction SilentlyContinue) { throw 'Game is running; no DLL replacement allowed.' }
}
function DataHashes {
    $map = [ordered]@{}
    foreach ($folder in @('C:/Users/ADMIN/AppData/LocalLow/noio/KingdomTwoCrowns/Release', (Join-Path $game 'BepInEx/config'))) {
        foreach ($file in (Get-ChildItem -LiteralPath $folder -File -Recurse | Sort-Object FullName)) {
            $map[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    }
    return $map
}
AssertClosed
if ((Get-FileHash -LiteralPath $candidate).Hash -ne $expected) { throw 'Candidate hash mismatch.' }
if ((Get-FileHash -LiteralPath $target).Hash -ne $previous) { throw 'Installed baseline changed; inspect before replacing.' }
$before = DataHashes
$backupDirectory = Join-Path $artifacts ('install-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backupDirectory | Out-Null
$backup = Join-Path $backupDirectory 'KingdomEnhancedMod.dll'
Copy-Item -LiteralPath $target -Destination $backup
if ((Get-FileHash -LiteralPath $backup).Hash -ne $previous) { throw 'Backup verification failed.' }
$staged = $target + '.install-' + [Guid]::NewGuid().ToString('N') + '.tmp'
Copy-Item -LiteralPath $candidate -Destination $staged
if ((Get-FileHash -LiteralPath $staged).Hash -ne $expected) { throw 'Staged copy verification failed.' }
AssertClosed
$atomicBackup = $target + '.before-musketeer-' + [Guid]::NewGuid().ToString('N') + '.bak'
[IO.File]::Replace($staged, $target, $atomicBackup, $true)
if ((Get-FileHash -LiteralPath $target).Hash -ne $expected) { throw 'Installed DLL verification failed.' }
$after = DataHashes
if ($before.Count -ne $after.Count) { throw 'User data file inventory changed during installation.' }
foreach ($path in $before.Keys) {
    if ($before[$path] -ne $after[$path]) { throw "User data changed during installation: $path" }
}
$receipt = [ordered]@{
    build = '8.0.0-hero-center-grip-20260917'
    installedAt = (Get-Date).ToString('o')
    authorization = 'User requested fixes and previously authorized local testing-copy installation; game must remain closed.'
    target = $target
    candidateSha256 = $expected
    previousDllSha256 = $previous
    backup = $backup
    atomicBackup = $atomicBackup
    userDataFileCount = $before.Count
    userDataUnchanged = $true
    userDataHashes = $after
    gameStarted = $false
    published = $false
    scope = 'Hero atlas and build marker only; no gameplay/runtime/save changes.'
}
$receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'receipts/install.json') -Encoding UTF8
"Installed $expected; backup verified; $($before.Count) save/config files unchanged. Game not started."
