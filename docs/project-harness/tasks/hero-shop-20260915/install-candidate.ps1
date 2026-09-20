$ErrorActionPreference = 'Stop'
$game = 'E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091'
$target = Join-Path $game 'BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll'
$candidate = 'C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/hero-shop-20260915/candidate-ground-flags/KingdomEnhancedMod.dll'
$expected = 'A6B5D90B12C46020E844B55AB7D22EB185618A30A868B255173C15E4B32FC9B3'
$expectedOld = '80522BF18952FE609C4F6F91FD6B52C26AB79CBF87EE918159770306953348F4'
function DataHashes {
    $result = [ordered]@{}
    $saveDir = 'C:/Users/ADMIN/AppData/LocalLow/noio/KingdomTwoCrowns/Release'
    $configDir = Join-Path $game 'BepInEx/config'
    foreach ($folder in @($saveDir, $configDir)) {
        if (Test-Path -LiteralPath $folder) {
            foreach ($file in (Get-ChildItem -LiteralPath $folder -File -Recurse | Sort-Object FullName)) {
                $result[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName).Hash
            }
        }
    }
    return $result
}
if (Get-Process -Name KingdomTwoCrowns -ErrorAction SilentlyContinue) { throw 'Game running; do not replace DLL.' }
if ((Get-FileHash -LiteralPath $candidate).Hash -ne $expected) { throw 'Candidate hash does not match reviewed build.' }
$old = (Get-FileHash -LiteralPath $target).Hash
if ($old -ne $expectedOld) { throw 'Installed baseline changed; inspect before replacement.' }
$before = DataHashes
$backup = $target + '.before-hero-shop-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.bak'
Copy-Item -LiteralPath $target -Destination $backup
if ((Get-FileHash -LiteralPath $backup).Hash -ne $old) { throw 'Backup verification failed.' }
if (Get-Process -Name KingdomTwoCrowns -ErrorAction SilentlyContinue) { throw 'Game started during backup; DLL untouched.' }
Copy-Item -LiteralPath $candidate -Destination $target -Force
$installed = (Get-FileHash -LiteralPath $target).Hash
$after = DataHashes
if ($installed -ne $expected) { throw 'Installed DLL hash mismatch.' }
if (($before | ConvertTo-Json -Compress) -cne ($after | ConvertTo-Json -Compress)) { throw 'User data changed during installation; inspect before taking further action.' }
$receipt = @{
    build = '8.0.0-hero-shop-flags-ground-20260915'; installed = $target; dllSha256 = $installed
    previousDllSha256 = $old; backup = $backup; userDataHashes = $after
    userDataUnchanged = $true; gameStarted = $false; published = $false
    scope = 'current-island local candidate; cross-island identity transport incomplete'
    installedAt = (Get-Date).ToString('o')
}
$receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'receipts/local-install.json') -Encoding UTF8
"Candidate installed: $installed. Backup verified; $($after.Count) save/config files unchanged; game not launched."
