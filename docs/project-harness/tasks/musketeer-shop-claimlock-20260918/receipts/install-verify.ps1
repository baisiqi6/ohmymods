$ErrorActionPreference = 'Stop'
$game = Get-Process -Name 'KingdomTwoCrowns' -ErrorAction SilentlyContinue
if ($game) { Write-Output 'GAME_RUNNING_ABORT'; exit 1 }
Write-Output 'game closed - proceeding'

$taskDir = 'C:/Users/ADMIN/projects/ohmymods/docs/project-harness/tasks/musketeer-shop-claimlock-20260918/receipts'
$saveDir = "$env:USERPROFILE\AppData\LocalLow\noio\KingdomTwoCrowns"
$pluginDll = 'E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll'
$backupDll = "$taskDir/backup-prev-candidate-KingdomEnhancedMod.dll"
$candidateDll = 'C:/Users/ADMIN/projects/ohmymods/il2cpp/bin/Debug/KingdomEnhancedMod.dll'

function HashTree($dir) {
    $files = Get-ChildItem $dir -Recurse -File
    $sb = New-Object System.Text.StringBuilder
    foreach ($f in $files) {
        [void]$sb.Append($f.FullName); [void]$sb.Append(':')
        [void]$sb.Append((Get-FileHash $f.FullName -Algorithm SHA256).Hash); [void]$sb.Append("`n")
    }
    $txt = $sb.ToString()
    $ms = [IO.MemoryStream]::new([Text.Encoding]::UTF8.GetBytes($txt))
    @{ Hash = (Get-FileHash -InputStream $ms -Algorithm SHA256).Hash; Count = $files.Count }
}

$preSaves = HashTree $saveDir
Write-Output ("saves baseline: " + $preSaves.Hash.Substring(0,8) + " files=" + $preSaves.Count)

$installedBefore = (Get-FileHash $pluginDll -Algorithm SHA256).Hash
Write-Output ("installed before: " + $installedBefore.Substring(0,8))
Copy-Item $pluginDll $backupDll -Force
$backupHash = (Get-FileHash $backupDll -Algorithm SHA256).Hash
if ($backupHash -ne $installedBefore) { Write-Output 'BACKUP_MISMATCH_ABORT'; exit 1 }
Write-Output ("backup ok: " + $backupHash.Substring(0,8))

Copy-Item $candidateDll $pluginDll -Force
$installedAfter = (Get-FileHash $pluginDll -Algorithm SHA256).Hash
Write-Output ("installed after: " + $installedAfter.Substring(0,8))

$postSaves = HashTree $saveDir
Write-Output ("saves after: " + $postSaves.Hash.Substring(0,8) + " files=" + $postSaves.Count)

if ($preSaves.Hash -ne $postSaves.Hash) { Write-Output 'SAVES_CHANGED_ABORT_INVESTIGATE'; exit 1 }
Write-Output 'RESULT: installed, user data untouched'
