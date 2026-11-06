param([switch]$CheckOnly)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$root=$PSScriptRoot
$manifest=ConvertFrom-Json ([IO.File]::ReadAllText((Join-Path $root 'ohmymods-win243-preview.json')))
if (@(Get-Process -Name KingdomTwoCrowns -ErrorAction SilentlyContinue).Count -ne 0) { throw 'A KingdomTwoCrowns game is already running. Close it before starting this preview.' }
foreach ($entry in @($manifest.game) + @($manifest.payload | Where-Object { $_.path -ne 'BepInEx/config/BepInEx.cfg' })) {
    $path=Join-Path $root ([string]$entry.path)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw ('Missing file: '+$entry.path) }
    $item=Get-Item -LiteralPath $path
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or [long]$item.Length -ne [long]$entry.size) { throw ('Unexpected file: '+$entry.path) }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne [string]$entry.sha256) { throw ('File hash mismatch: '+$entry.path) }
}
function TestFixedInteropConfig([string]$cfg) {
    $expected=@{
        UpdateInteropAssemblies='false'
        UnityBaseLibrariesSource='6000.0.66.zip'
        IL2CPPInteropAssembliesPath='{BepInEx}'
        GlobalMetadataPath='{GameDataPath}/il2cpp_data/Metadata/global-metadata.dat'
    }
    $seen=@{};$section=''
    foreach ($line in ($cfg -split '\r?\n')) {
        $text=$line.Trim()
        if (-not $text -or $text.StartsWith('#') -or $text.StartsWith(';')) { continue }
        if ($text -match '^\[([^\]]+)\]$') {
            $section=$Matches[1].Trim()
            if ($section -ieq 'IL2CPP' -and $section -cne 'IL2CPP') { throw 'Ambiguous IL2CPP section case.' }
            continue
        }
        if ($section -cne 'IL2CPP') { continue }
        if ($text -notmatch '^([^=]+)=(.*)$') { throw 'Invalid IL2CPP configuration line.' }
        $key=$Matches[1].Trim();$value=$Matches[2].Trim()
        if ($expected.ContainsKey($key)) {
            if (-not ($expected.Keys -ccontains $key) -or $seen.ContainsKey($key) -or $value -cne $expected[$key]) { throw ('Fixed IL2CPP configuration changed or duplicated: '+$key) }
            $seen[$key]=$true
        }
    }
    if ($seen.Count -ne $expected.Count) { throw 'Fixed IL2CPP configuration keys missing.' }
}
$cfgPath=Join-Path $root 'BepInEx/config/BepInEx.cfg'
if ((Get-Item -LiteralPath $cfgPath).Length -gt 1048576) { throw 'Oversized loader configuration.' }
TestFixedInteropConfig ([IO.File]::ReadAllText($cfgPath))
Write-Host ('OhMyMods '+$manifest.version+' / game 2.4.3 preview')
Write-Host 'Manual gameplay validation pending. Current Windows profile saves will be used.'
if ($CheckOnly) { Write-Host 'CHECK_ONLY PASS; game not started.'; exit 0 }
Start-Process -FilePath (Join-Path $root 'KingdomTwoCrowns.exe') -WorkingDirectory $root
