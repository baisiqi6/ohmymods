#requires -Version 5.1
param(
    [switch]$KeepFixtures
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$script:repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$script:collector = Join-Path $script:repo 'release\support-tools\windows\OhMyMods-CollectDiagnostics.ps1'
$script:cmd = Join-Path $script:repo 'release\support-tools\windows\一键收集排查资料.cmd'
$script:readme = Join-Path $script:repo 'release\support-tools\windows\排查资料工具说明.txt'
$script:psExe = Join-Path $PSHOME 'powershell.exe'
if (-not (Test-Path -LiteralPath $script:psExe -PathType Leaf)) {
    $resolvedPowerShell = Get-Command powershell.exe -ErrorAction SilentlyContinue
    if ($null -ne $resolvedPowerShell) {
        $script:psExe = $resolvedPowerShell.Source
    }
    else {
        $script:psExe = 'powershell.exe'
    }
}
$script:total = 0
$script:passed = 0
$script:failed = 0
$tempBase = $env:TEMP
if ([string]::IsNullOrWhiteSpace($tempBase)) {
    $tempBase = [IO.Path]::GetTempPath()
}
$script:root = Join-Path $tempBase ('ohmymods-collector-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($script:root) | Out-Null

if (-not (Test-Path -LiteralPath $script:collector -PathType Leaf)) {
    Write-Host 'RED: collector script is missing:'
    Write-Host ('  ' + $script:collector)
    Write-Host 'No synthetic case can run until the implementation exists.'
    try { Remove-Item -LiteralPath $script:root -Recurse -Force -ErrorAction SilentlyContinue } catch { }
    exit 1
}

$runningGame = @(Get-Process -Name KingdomTwoCrowns -ErrorAction SilentlyContinue)
if ($runningGame.Count -ne 0) {
    Write-Host 'A real KingdomTwoCrowns process is running. Close the game before running these synthetic tests.'
    try { Remove-Item -LiteralPath $script:root -Recurse -Force -ErrorAction SilentlyContinue } catch { }
    exit 1
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -ne $Actual) {
        throw ($Message + ' (expected=' + [string]$Expected + ' actual=' + [string]$Actual + ')')
    }
}

function Invoke-Case([string]$Name, [scriptblock]$Body) {
    $script:total = $script:total + 1
    try {
        & $Body
        $script:passed = $script:passed + 1
        Write-Host ('[PASS] ' + $Name)
    }
    catch {
        $script:failed = $script:failed + 1
        Write-Host ('[FAIL] ' + $Name)
        Write-Host ('       ' + $_.Exception.Message)
    }
}

# Process presence is simulated only inside the child PowerShell host. Never
# create a globally named game process or depend on another task's live game.
$script:probeWrapper = Join-Path $script:root 'invoke-collector.ps1'
$escapedCollector = $script:collector.Replace("'", "''")
$wrapperText = @'
param([string]$GameRoot, [string]$UserDataRoot, [string]$OutputRoot,
      [switch]$NoOpen, [int]$MaxFileBytes, [int]$MaxTotalBytes,
      [int]$MaxFiles, [int]$MaxDepth, [switch]$ProbePresent)
$global:OhMyModsCollectorFixtureProbe = [bool]$ProbePresent
function Get-Process {
    param([string]$Name, $ErrorAction)
    if ($Name -ne 'KingdomTwoCrowns') { throw 'unexpected process query' }
    if ($global:OhMyModsCollectorFixtureProbe) { [pscustomobject]@{ ProcessName = $Name } }
}
$forward = @{}
foreach ($name in $PSBoundParameters.Keys) {
    if ($name -ne 'ProbePresent') { $forward[$name] = $PSBoundParameters[$name] }
}
& '__COLLECTOR__' @forward
exit $LASTEXITCODE
'@
$wrapperText = $wrapperText.Replace('__COLLECTOR__', $escapedCollector)
[IO.File]::WriteAllText($script:probeWrapper, $wrapperText, (New-Object Text.UTF8Encoding($true)))

function Invoke-Collector([string[]]$Arguments) {
    $output = & $script:psExe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $script:probeWrapper @Arguments *>&1 | Out-String
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = $output
    }
}

function New-Bytes([int]$Length, [byte]$Value = 65) {
    $buffer = New-Object byte[] $Length
    for ($i = 0; $i -lt $Length; $i++) { $buffer[$i] = $Value }
    return $buffer
}

function Write-FixtureFile([string]$Path, [byte[]]$Bytes) {
    $parent = [IO.Path]::GetDirectoryName($Path)
    if (-not [IO.Directory]::Exists($parent)) {
        [IO.Directory]::CreateDirectory($parent) | Out-Null
    }
    [IO.File]::WriteAllBytes($Path, $Bytes)
}

function Write-FixtureText([string]$Path, [string]$Text) {
    $encoding = New-Object System.Text.UTF8Encoding($false)
    Write-FixtureFile $Path $encoding.GetBytes($Text)
}

function New-Fixture([string]$Name) {
    $base = Join-Path $script:root $Name
    $game = Join-Path $base '游戏 目录'
    $user = Join-Path $base '用户 数据'
    $output = Join-Path $base '输出 目录'
    [IO.Directory]::CreateDirectory($game) | Out-Null
    [IO.Directory]::CreateDirectory($user) | Out-Null
    Write-FixtureFile (Join-Path $game 'KingdomTwoCrowns.exe') ([Text.Encoding]::ASCII.GetBytes('fake game executable'))
    return [pscustomobject]@{
        Base = $base
        Game = $game
        User = $user
        Output = $output
    }
}

function Add-FullFixture($Fixture) {
    $files = @{
        'BepInEx\LogOutput.log' = "BepInEx log`nline two`n"
        'BepInEx\LogOutput.log.1' = "rotated log`n"
        'BepInEx\LogOutput.log.old' = "old log`n"
        'BepInEx\config\BepInEx.cfg' = "[IL2CPP]`nUpdateInteropAssemblies=false`n"
        'BepInEx\config\KingdomEnhancedMod.cfg' = "[General]`nEnabled=true`n"
        'BepInEx\config\KingdomEnhancedMod\ModSave\knight.dat' = "mod save`n"
        'BepInEx\config\KingdomEnhancedMod\nested\profile.cfg' = "nested`n"
        'BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll' = "fake plugin dll`n"
        'BUILD-MANIFEST.txt' = "build manifest`n"
        'RELEASE_MANIFEST.json' = '{"release":true}'
        'ohmymods-win243-preview.json' = '{"preview":true}'
    }
    foreach ($rel in $files.Keys) {
        Write-FixtureText (Join-Path $Fixture.Game $rel) $files[$rel]
    }
    $userFiles = @{
        'Player.log' = "player log`n"
        'Player-prev.log' = "previous player log`n"
        'Release\global-v35\save.dat' = "native save`n"
        'Release\global-v35\存档 备份.dat' = "unicode save`n"
        'Release\global-v35\backup\save2.dat' = "native save backup`n"
    }
    foreach ($rel in $userFiles.Keys) {
        Write-FixtureText (Join-Path $Fixture.User $rel) $userFiles[$rel]
    }
}

function Get-FullFixtureMap($Fixture) {
    $map = @{}
    $gameRel = @(
        'BepInEx/LogOutput.log',
        'BepInEx/LogOutput.log.1',
        'BepInEx/LogOutput.log.old',
        'BepInEx/config/BepInEx.cfg',
        'BepInEx/config/KingdomEnhancedMod.cfg',
        'BepInEx/config/KingdomEnhancedMod/ModSave/knight.dat',
        'BepInEx/config/KingdomEnhancedMod/nested/profile.cfg',
        'BUILD-MANIFEST.txt',
        'RELEASE_MANIFEST.json',
        'ohmymods-win243-preview.json'
    )
    foreach ($rel in $gameRel) {
        $map[$rel] = Join-Path $Fixture.Game ($rel -replace '/', '\')
    }
    $userRel = @(
        'UserData/Player.log',
        'UserData/Player-prev.log',
        'UserData/Release/global-v35/save.dat',
        'UserData/Release/global-v35/存档 备份.dat',
        'UserData/Release/global-v35/backup/save2.dat'
    )
    foreach ($rel in $userRel) {
        $local = $rel.Substring('UserData/'.Length)
        $map[$rel] = Join-Path $Fixture.User ($local -replace '/', '\')
    }
    return $map
}

function Get-ContentSnapshot([string[]]$Roots) {
    $result = @{}
    foreach ($root in $Roots) {
        if (-not [IO.Directory]::Exists($root)) { continue }
        foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -Force -File)) {
            $key = $file.FullName.ToLowerInvariant()
            $result[$key] = [pscustomobject]@{
                Length = [long]$file.Length
                LastWriteTimeUtc = $file.LastWriteTimeUtc
                Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
    }
    return $result
}

function Assert-SnapshotUnchanged($Before, $After, [string]$Label) {
    Assert-Equal $Before.Count $After.Count ($Label + ': source file count changed')
    foreach ($key in $Before.Keys) {
        Assert-True $After.ContainsKey($key) ($Label + ': source file disappeared: ' + $key)
        Assert-Equal $Before[$key].Length $After[$key].Length ($Label + ': length changed: ' + $key)
        Assert-Equal $Before[$key].Sha256 $After[$key].Sha256 ($Label + ': hash changed: ' + $key)
        Assert-Equal $Before[$key].LastWriteTimeUtc.Ticks $After[$key].LastWriteTimeUtc.Ticks ($Label + ': mtime changed: ' + $key)
    }
}

function Get-ResultZips([string]$OutputRoot) {
    if (-not [IO.Directory]::Exists($OutputRoot)) { return @() }
    return @(Get-ChildItem -LiteralPath $OutputRoot -Recurse -File -Filter '*.zip' | Sort-Object FullName)
}

function Open-ResultZip([string]$Path) {
    return [IO.Compression.ZipFile]::OpenRead($Path)
}

function Get-ZipNames($Archive) {
    return @($Archive.Entries | ForEach-Object { $_.FullName } | Sort-Object)
}

function Get-ZipEntryText($Archive, [string]$Name) {
    $entry = $Archive.GetEntry($Name)
    if ($null -eq $entry) { throw ('ZIP entry missing: ' + $Name) }
    $stream = $entry.Open()
    try {
        $reader = New-Object System.IO.StreamReader($stream, (New-Object System.Text.UTF8Encoding($false)), $true)
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-ZipEntryHash($Archive, [string]$Name) {
    $entry = $Archive.GetEntry($Name)
    if ($null -eq $entry) { return $null }
    $stream = $entry.Open()
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($stream)
    }
    finally {
        $stream.Dispose()
        $sha.Dispose()
    }
    return (($hash | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Read-Manifest($Archive) {
    return (Get-ZipEntryText $Archive 'manifest.json' | ConvertFrom-Json)
}

function Assert-CollectedEntry($Manifest, $Archive, [string]$ZipPath, [string]$SourcePath) {
    $matches = @($Manifest.entries | Where-Object { $_.zipPath -eq $ZipPath })
    Assert-Equal 1 $matches.Count ('manifest entry count for ' + $ZipPath)
    $entry = $matches[0]
    Assert-Equal 'collected' $entry.status ('status for ' + $ZipPath)
    $sourceLength = [long]((Get-Item -LiteralPath $SourcePath).Length)
    Assert-Equal $sourceLength ([long]$entry.size) ('size for ' + $ZipPath)
    $sourceHash = (Get-FileHash -LiteralPath $SourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Equal $sourceHash $entry.sha256 ('source hash for ' + $ZipPath)
    Assert-Equal $sourceHash (Get-ZipEntryHash $Archive $ZipPath) ('ZIP hash for ' + $ZipPath)
    Assert-Equal $false $entry.changed ('changed flag for ' + $ZipPath)
    Assert-True ($null -ne $entry.beforeLastWriteUtc) ('before mtime for ' + $ZipPath)
    Assert-True ($null -ne $entry.afterLastWriteUtc) ('after mtime for ' + $ZipPath)
}

Invoke-Case 'distribution contract: BOM, ASCII CMD, CRLF, launcher arguments' {
    $psBytes = [IO.File]::ReadAllBytes($script:collector)
    Assert-True ($psBytes.Length -gt 3) 'collector is empty'
    Assert-True ($psBytes[0] -eq 0xEF -and $psBytes[1] -eq 0xBB -and $psBytes[2] -eq 0xBF) 'collector must start with UTF-8 BOM'
    $psText = [Text.Encoding]::UTF8.GetString($psBytes, 3, $psBytes.Length - 3)
    Assert-True ($psText.Contains("toolVersion = '1.0.0'")) 'toolVersion 1.0.0 missing'
    Assert-True (-not $psText.Contains('Compress-Archive')) 'Compress-Archive is forbidden'
    Assert-True (-not $psText.Contains('Set-ExecutionPolicy')) 'Set-ExecutionPolicy is forbidden'

    $cmdBytes = [IO.File]::ReadAllBytes($script:cmd)
    Assert-Equal 0 (@($cmdBytes | Where-Object { $_ -ge 128 }).Count) 'CMD must be pure ASCII'
    $lf = @($cmdBytes | Where-Object { $_ -eq 0x0A }).Count
    $crlf = 0
    for ($i = 0; $i -lt $cmdBytes.Length - 1; $i++) {
        if ($cmdBytes[$i] -eq 0x0D -and $cmdBytes[$i + 1] -eq 0x0A) { $crlf = $crlf + 1 }
    }
    Assert-True ($lf -gt 0 -and $lf -eq $crlf) 'CMD must use CRLF for every line'
    $cmdText = [Text.Encoding]::ASCII.GetString($cmdBytes)
    Assert-True ($cmdText.Contains('powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0OhMyMods-CollectDiagnostics.ps1"')) 'CMD does not launch collector with fixed flags'
    Assert-True ($cmdText.Contains('pause')) 'CMD must pause before closing'
    Assert-True ($cmdText.Contains('exit /b')) 'CMD must preserve exit code'
    Assert-True ([IO.File]::Exists($script:readme)) 'player README missing'
}

Invoke-Case 'positive: Chinese/space roots, allowlist, hashes, metadata, no source mutation' {
    $fixture = New-Fixture 'positive'
    Add-FullFixture $fixture
    $before = Get-ContentSnapshot @($fixture.Game, $fixture.User)
    $result = Invoke-Collector @('-GameRoot', $fixture.Game, '-UserDataRoot', $fixture.User, '-OutputRoot', $fixture.Output, '-NoOpen')
    Assert-Equal 0 $result.ExitCode ('positive exit code; output=' + $result.Output)
    $after = Get-ContentSnapshot @($fixture.Game, $fixture.User)
    Assert-SnapshotUnchanged $before $after 'positive'

    $zips = @(Get-ResultZips $fixture.Output)
    Assert-Equal 1 $zips.Count 'expected exactly one result ZIP'
    $zipPath = $zips[0].FullName
    $runDir = Split-Path -Parent $zipPath
    Assert-True (Test-Path -LiteralPath (Join-Path $runDir 'README.txt') -PathType Leaf) 'result README beside ZIP missing'

    $archive = Open-ResultZip $zipPath
    try {
        $map = Get-FullFixtureMap $fixture
        $expected = @($map.Keys) + @('README.txt', 'manifest.json')
        $expected = @($expected | Sort-Object)
        $actual = Get-ZipNames $archive
        Assert-Equal ($expected -join "`n") ($actual -join "`n") 'ZIP entry allowlist mismatch'
        Assert-True (-not ($actual -contains 'KingdomTwoCrowns.exe')) 'game EXE must not be copied'
        Assert-Equal 0 (@($actual | Where-Object { $_ -like '*KingdomEnhancedMod.dll' }).Count) 'plugin DLL must not be copied'

        $manifest = Read-Manifest $archive
        Assert-Equal '1.0.0' $manifest.toolVersion 'toolVersion'
        Assert-Equal $false $manifest.partial 'partial flag'
        Assert-Equal $false $manifest.gameRunningAtStart 'gameRunningAtStart'
        Assert-Equal $false $manifest.gameRunningAtEnd 'gameRunningAtEnd'
        Assert-Equal 0 $manifest.exitCode 'manifest exitCode'
        Assert-Equal 'complete' $manifest.status 'manifest status'
        Assert-Equal $false $manifest.snapshot.atomic 'snapshot must not claim atomicity'
        Assert-True ([string]$manifest.powershellVersion).StartsWith('5.') 'PowerShell version not recorded'
        $coverage = @($manifest.requiredCoverage)
        Assert-Equal 3 $coverage.Count 'required coverage count'
        foreach ($item in $coverage) {
            Assert-True ([bool]$item.collected) ('required coverage failed: ' + $item.category)
        }
        Assert-True ($manifest.metadata.gameExe.present) 'game EXE metadata missing'
        Assert-Equal 64 ([string]$manifest.metadata.gameExe.sha256).Length 'game EXE metadata hash'
        Assert-True ($manifest.metadata.pluginDll.present) 'plugin DLL metadata missing'
        Assert-Equal 'BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll' $manifest.metadata.pluginDll.relativePath 'plugin metadata path'
        $rawManifest = Get-ZipEntryText $archive 'manifest.json'
        Assert-True (-not $rawManifest.Contains($fixture.Base)) 'manifest leaks fixture absolute path'
        Assert-True (-not $rawManifest.Contains('fake game executable')) 'manifest unexpectedly contains file content'

        foreach ($zipRel in $map.Keys) {
            Assert-CollectedEntry $manifest $archive $zipRel $map[$zipRel]
        }
        $readmeText = Get-ZipEntryText $archive 'README.txt'
        Assert-True ($readmeText.Contains('私下发送给维护者')) 'README must tell the player to send the ZIP privately'
        Assert-True ($readmeText.Contains('不会自动上传')) 'README must say the tool does not upload'
        Assert-True ($readmeText.Contains('可能包含')) 'README must mention possible personal identifiers'
    }
    finally {
        $archive.Dispose()
    }
}

Invoke-Case 'repeat run: unique output directory and no overwrite' {
    $fixture = New-Fixture 'repeat'
    Add-FullFixture $fixture
    $before = Get-ContentSnapshot @($fixture.Game, $fixture.User)
    $first = Invoke-Collector @('-GameRoot', $fixture.Game, '-UserDataRoot', $fixture.User, '-OutputRoot', $fixture.Output, '-NoOpen')
    Assert-Equal 0 $first.ExitCode 'first run exit code'
    $firstZips = @(Get-ResultZips $fixture.Output)
    Assert-Equal 1 $firstZips.Count 'first run ZIP count'
    $firstHash = (Get-FileHash -LiteralPath $firstZips[0].FullName -Algorithm SHA256).Hash
    $firstDir = Split-Path -Parent $firstZips[0].FullName

    $second = Invoke-Collector @('-GameRoot', $fixture.Game, '-UserDataRoot', $fixture.User, '-OutputRoot', $fixture.Output, '-NoOpen')
    Assert-Equal 0 $second.ExitCode 'second run exit code'
    $secondZips = @(Get-ResultZips $fixture.Output)
    Assert-Equal 2 $secondZips.Count 'second run must add a new ZIP'
    Assert-Equal $firstHash ((Get-FileHash -LiteralPath $firstZips[0].FullName -Algorithm SHA256).Hash) 'first ZIP was overwritten'
    $newZips = @($secondZips | Where-Object { $_.FullName -ne $firstZips[0].FullName })
    Assert-Equal 1 $newZips.Count 'second run must produce exactly one new ZIP'
    Assert-True ((Split-Path -Parent $newZips[0].FullName) -ne $firstDir) 'second run reused the first run directory'
    $after = Get-ContentSnapshot @($fixture.Game, $fixture.User)
    Assert-SnapshotUnchanged $before $after 'repeat'
}

Invoke-Case 'missing main data: partial ZIP, visible required gaps' {
    $fixture = New-Fixture 'missing-main'
    $result = Invoke-Collector @('-GameRoot', $fixture.Game, '-UserDataRoot', $fixture.User, '-OutputRoot', $fixture.Output, '-NoOpen')
    Assert-Equal 2 $result.ExitCode ('partial exit code; output=' + $result.Output)
    $zips = @(Get-ResultZips $fixture.Output)
    Assert-Equal 1 $zips.Count 'partial run must still produce a ZIP'
    $archive = Open-ResultZip $zips[0].FullName
    try {
        $manifest = Read-Manifest $archive
        Assert-Equal $true $manifest.partial 'partial flag'
        Assert-Equal 'partial' $manifest.status 'manifest status'
        Assert-Equal 2 $manifest.exitCode 'manifest exitCode'
        $coverage = @($manifest.requiredCoverage)
        Assert-Equal 3 $coverage.Count 'required coverage count'
        Assert-Equal 0 (@($coverage | Where-Object { [bool]$_.collected }).Count) 'missing data must not satisfy required coverage'
        $missing = @($manifest.missing)
        Assert-True (@($missing | Where-Object { $_.relativePath -eq 'BepInEx/LogOutput.log' }).Count -eq 1) 'missing BepInEx log not recorded'
        Assert-True (@($missing | Where-Object { $_.relativePath -eq 'Player.log' }).Count -eq 1) 'missing Player.log not recorded'
        Assert-True (@($missing | Where-Object { $_.relativePath -eq 'Release' }).Count -eq 1) 'missing Release tree not recorded'
        Assert-Equal 0 (@($manifest.entries).Count) 'no payload entries expected'
    }
    finally {
        $archive.Dispose()
    }
}

Invoke-Case 'exclusive lock: reported as partial, other files still collected' {
    $fixture = New-Fixture 'exclusive'
    Add-FullFixture $fixture
    $lockedPath = Join-Path $fixture.User 'Release\global-v35\locked.sav'
    Write-FixtureText $lockedPath "locked save`n"
    $lockedHash = (Get-FileHash -LiteralPath $lockedPath -Algorithm SHA256).Hash
    $lock = [IO.File]::Open($lockedPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $result = Invoke-Collector @('-GameRoot', $fixture.Game, '-UserDataRoot', $fixture.User, '-OutputRoot', $fixture.Output, '-NoOpen')
    }
    finally {
        $lock.Dispose()
    }
    Assert-Equal 2 $result.ExitCode ('exclusive lock must be partial; output=' + $result.Output)
    $zips = @(Get-ResultZips $fixture.Output)
    Assert-Equal 1 $zips.Count 'exclusive lock must still produce a ZIP'
    $archive = Open-ResultZip $zips[0].FullName
    try {
        $manifest = Read-Manifest $archive
        Assert-Equal $true $manifest.partial 'partial flag'
        $errors = @($manifest.errors | Where-Object { $_.relativePath -eq 'Release/global-v35/locked.sav' })
        Assert-Equal 1 $errors.Count 'locked file error not recorded'
        Assert-Equal 'IOException' $errors[0].type 'locked file error type'
        Assert-Equal $null ($archive.GetEntry('UserData/Release/global-v35/locked.sav')) 'locked file must not be in ZIP'
        Assert-True ($null -ne $archive.GetEntry('UserData/Player.log')) 'other files must still be collected'
    }
    finally {
        $archive.Dispose()
    }
    Assert-Equal $lockedHash ((Get-FileHash -LiteralPath $lockedPath -Algorithm SHA256).Hash) 'locked source changed'
}

Invoke-Case 'shared reader/writer: source remains readable' {
    $fixture = New-Fixture 'shared'
    Add-FullFixture $fixture
    $playerPath = Join-Path $fixture.User 'Player.log'
    $before = Get-ContentSnapshot @($fixture.Game, $fixture.User)
    $writer = [IO.File]::Open($playerPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::ReadWrite)
    try {
        $result = Invoke-Collector @('-GameRoot', $fixture.Game, '-UserDataRoot', $fixture.User, '-OutputRoot', $fixture.Output, '-NoOpen')
    }
    finally {
        $writer.Dispose()
    }
    Assert-Equal 0 $result.ExitCode ('shared read must succeed; output=' + $result.Output)
    $zips = @(Get-ResultZips $fixture.Output)
    Assert-Equal 1 $zips.Count 'shared read must produce a ZIP'
    $archive = Open-ResultZip $zips[0].FullName
    try {
        $manifest = Read-Manifest $archive
        Assert-CollectedEntry $manifest $archive 'UserData/Player.log' $playerPath
    }
    finally {
        $archive.Dispose()
    }
    $after = Get-ContentSnapshot @($fixture.Game, $fixture.User)
    Assert-SnapshotUnchanged $before $after 'shared'
}

Invoke-Case 'reparse point: not followed and reported' {
    $fixture = New-Fixture 'reparse'
    Add-FullFixture $fixture
    $secretDir = Join-Path $fixture.Base 'outside-secret'
    [IO.Directory]::CreateDirectory($secretDir) | Out-Null
    $secret = Join-Path $secretDir 'secret.txt'
    Write-FixtureText $secret "must not be collected`n"
    $secretHash = (Get-FileHash -LiteralPath $secret -Algorithm SHA256).Hash
    $junction = Join-Path $fixture.User 'Release\linked'
    New-Item -ItemType Junction -Path $junction -Target $secretDir | Out-Null
    $result = Invoke-Collector @('-GameRoot', $fixture.Game, '-UserDataRoot', $fixture.User, '-OutputRoot', $fixture.Output, '-NoOpen')
    Assert-Equal 2 $result.ExitCode ('reparse skip must be partial; output=' + $result.Output)
    $zips = @(Get-ResultZips $fixture.Output)
    Assert-Equal 1 $zips.Count 'reparse run must produce a ZIP'
    $archive = Open-ResultZip $zips[0].FullName
    try {
        $manifest = Read-Manifest $archive
        Assert-True (@($manifest.entries | Where-Object { $_.status -eq 'skipped-reparse' -and $_.sourceRelativePath -eq 'Release/linked' }).Count -eq 1) 'reparse skip not recorded'
        Assert-Equal 0 (@($manifest.entries | Where-Object { $_.zipPath -like 'UserData/Release/linked/*' }).Count) 'reparse target was collected'
    }
    finally {
        $archive.Dispose()
    }
    Assert-Equal $secretHash ((Get-FileHash -LiteralPath $secret -Algorithm SHA256).Hash) 'reparse target changed'
}

Invoke-Case 'limits: per-file, aggregate bytes, and depth' {
    $fixture = New-Fixture 'limits'
    Write-FixtureFile (Join-Path $fixture.Game 'BepInEx\LogOutput.log') (New-Bytes 1 76)
    Write-FixtureFile (Join-Path $fixture.User 'Player.log') (New-Bytes 1 80)
    Write-FixtureFile (Join-Path $fixture.User 'Release\a.bin') (New-Bytes 8 65)
    Write-FixtureFile (Join-Path $fixture.User 'Release\b.bin') (New-Bytes 8 66)
    Write-FixtureFile (Join-Path $fixture.User 'Release\big.bin') (New-Bytes 20 67)
    Write-FixtureFile (Join-Path $fixture.User 'Release\d1\d2\d3\deep.bin') (New-Bytes 4 68)
    $result = Invoke-Collector @('-GameRoot', $fixture.Game, '-UserDataRoot', $fixture.User, '-OutputRoot', $fixture.Output, '-NoOpen', '-MaxFileBytes', '10', '-MaxTotalBytes', '15', '-MaxFiles', '100', '-MaxDepth', '2')
    Assert-Equal 2 $result.ExitCode ('limit run must be partial; output=' + $result.Output)
    $zips = @(Get-ResultZips $fixture.Output)
    Assert-Equal 1 $zips.Count 'limit run must produce a ZIP'
    $archive = Open-ResultZip $zips[0].FullName
    try {
        $manifest = Read-Manifest $archive
        Assert-Equal 10 $manifest.limits.maxFileBytes 'maxFileBytes'
        Assert-Equal 15 $manifest.limits.maxTotalBytes 'maxTotalBytes'
        Assert-Equal 100 $manifest.limits.maxFiles 'maxFiles'
        Assert-Equal 2 $manifest.limits.maxDepth 'maxDepth'
        Assert-True (@($manifest.entries | Where-Object { $_.status -eq 'skipped-size' }).Count -ge 1) 'skipped-size missing'
        Assert-True (@($manifest.entries | Where-Object { $_.status -eq 'skipped-total' }).Count -ge 1) 'skipped-total missing'
        Assert-True (@($manifest.entries | Where-Object { $_.status -eq 'skipped-depth' }).Count -ge 1) 'skipped-depth missing'
        Assert-Equal $null ($archive.GetEntry('UserData/Release/b.bin')) 'aggregate over-limit file must not be collected'
        Assert-Equal $null ($archive.GetEntry('UserData/Release/big.bin')) 'per-file over-limit file must not be collected'
        Assert-Equal 0 (@($archive.Entries | Where-Object { $_.FullName -like '*deep.bin' }).Count) 'depth-over-limit file must not be collected'
    }
    finally {
        $archive.Dispose()
    }
}

Invoke-Case 'limits: file count stops enumeration; larger limits are rejected' {
    $fixture = New-Fixture 'limits-count'
    Write-FixtureFile (Join-Path $fixture.Game 'BepInEx\LogOutput.log') (New-Bytes 1 76)
    Write-FixtureFile (Join-Path $fixture.User 'Player.log') (New-Bytes 1 80)
    Write-FixtureFile (Join-Path $fixture.User 'Release\a.bin') (New-Bytes 1 65)
    Write-FixtureFile (Join-Path $fixture.User 'Release\b.bin') (New-Bytes 1 66)
    $result = Invoke-Collector @('-GameRoot', $fixture.Game, '-UserDataRoot', $fixture.User, '-OutputRoot', $fixture.Output, '-NoOpen', '-MaxFiles', '2')
    Assert-Equal 2 $result.ExitCode ('file count limit must be partial; output=' + $result.Output)
    $zips = @(Get-ResultZips $fixture.Output)
    Assert-Equal 1 $zips.Count 'file count limit must produce a ZIP'
    $archive = Open-ResultZip $zips[0].FullName
    try {
        $manifest = Read-Manifest $archive
        Assert-True (@($manifest.entries | Where-Object { $_.status -eq 'skipped-count' }).Count -ge 1) 'skipped-count missing'
        Assert-Equal 2 $manifest.counts.filesSeen 'filesSeen must be bounded'
        Assert-Equal 0 (@($manifest.entries | Where-Object { $_.zipPath -like 'UserData/Release/*' }).Count) 'enumeration did not stop at file limit'
    }
    finally {
        $archive.Dispose()
    }

    $raised = Invoke-Collector @('-GameRoot', $fixture.Game, '-UserDataRoot', $fixture.User, '-OutputRoot', (Join-Path $fixture.Base 'raised-output'), '-NoOpen', '-MaxFiles', '5000')
    Assert-Equal 1 $raised.ExitCode 'raising MaxFiles above default must fail'
    Assert-Equal 0 (@(Get-ResultZips (Join-Path $fixture.Base 'raised-output')).Count) 'rejected limits must not produce a ZIP'
}

Invoke-Case 'invalid output root and overlapping roots are rejected before writing' {
    $fixture = New-Fixture 'invalid-output'
    Add-FullFixture $fixture
    Write-FixtureText $fixture.Output 'this is a file, not a directory'
    $result = Invoke-Collector @('-GameRoot', $fixture.Game, '-UserDataRoot', $fixture.User, '-OutputRoot', $fixture.Output, '-NoOpen')
    Assert-Equal 1 $result.ExitCode ('file output root must fail; output=' + $result.Output)
    Assert-Equal 0 (@(Get-ResultZips $fixture.Output).Count) 'no ZIP should exist under a file path'

    $overlapOutput = Join-Path $fixture.Game 'diagnostics-out'
    $overlap = Invoke-Collector @('-GameRoot', $fixture.Game, '-UserDataRoot', $fixture.User, '-OutputRoot', $overlapOutput, '-NoOpen')
    Assert-Equal 1 $overlap.ExitCode 'overlapping output root must fail'
    Assert-True (-not (Test-Path -LiteralPath $overlapOutput)) 'overlapping output directory must not be created'
}

Invoke-Case 'partial root override: error with zero collection from defaults' {
    $fixture = New-Fixture 'partial-override'
    Add-FullFixture $fixture
    $fakeProfile = Join-Path $fixture.Base 'fake-profile'
    $canary = Join-Path $fakeProfile 'AppData\LocalLow\noio\KingdomTwoCrowns\Player.log'
    Write-FixtureText $canary "default-profile canary`n"
    $canaryHash = (Get-FileHash -LiteralPath $canary -Algorithm SHA256).Hash
    $oldProfile = $env:USERPROFILE
    try {
        $env:USERPROFILE = $fakeProfile
        $result = Invoke-Collector @('-GameRoot', $fixture.Game, '-NoOpen')
    }
    finally {
        $env:USERPROFILE = $oldProfile
    }
    Assert-Equal 1 $result.ExitCode ('partial override must fail; output=' + $result.Output)
    Assert-Equal $canaryHash ((Get-FileHash -LiteralPath $canary -Algorithm SHA256).Hash) 'default user data was touched'
    Assert-Equal 0 (@(Get-ResultZips $fixture.Output).Count) 'partial override must not create output'
}

Invoke-Case 'wrong game root: rejected without scanning parents' {
    $fixture = New-Fixture 'wrong-game-root'
    Remove-Item -LiteralPath (Join-Path $fixture.Game 'KingdomTwoCrowns.exe') -Force
    $result = Invoke-Collector @('-GameRoot', $fixture.Game, '-UserDataRoot', $fixture.User, '-OutputRoot', $fixture.Output, '-NoOpen')
    Assert-Equal 1 $result.ExitCode ('wrong game root must fail; output=' + $result.Output)
    Assert-Equal 0 (@(Get-ResultZips $fixture.Output).Count) 'wrong game root must not produce a ZIP'
}

Invoke-Case 'controlled process probe: false/true changes snapshot and exit status' {
    foreach ($present in @($false, $true)) {
        $fixture = New-Fixture ('probe-' + [string]$present)
        Add-FullFixture $fixture
        $arguments = @('-GameRoot', $fixture.Game, '-UserDataRoot', $fixture.User, '-OutputRoot', $fixture.Output, '-NoOpen')
        if ($present) { $arguments += '-ProbePresent' }
        $result = Invoke-Collector $arguments
        $expectedExit = $(if ($present) { 2 } else { 0 })
        Assert-Equal $expectedExit $result.ExitCode ('controlled probe exit; output=' + $result.Output)
        $zips = @(Get-ResultZips $fixture.Output)
        Assert-Equal 1 $zips.Count 'probe must produce a ZIP'
        $archive = Open-ResultZip $zips[0].FullName
        try {
            $manifest = Read-Manifest $archive
            Assert-Equal $present $manifest.gameRunningAtStart 'gameRunningAtStart must follow controlled probe'
            Assert-Equal $present $manifest.gameRunningAtEnd 'gameRunningAtEnd must follow controlled probe'
            $expectedStatus = $(if ($present) { 'live-best-effort' } else { 'complete' })
            Assert-Equal $expectedStatus $manifest.status 'manifest status must flip with probe'
            Assert-Equal $false $manifest.partial 'no fixture data should be missing'
            Assert-True (-not $manifest.snapshot.atomic) 'never claim atomic snapshot'
        }
        finally { $archive.Dispose() }
    }
}

Write-Host ''
Write-Host ('Cases: ' + $script:total + ', passed: ' + $script:passed + ', failed: ' + $script:failed)
if ($KeepFixtures) {
    Write-Host ('Fixtures kept at: ' + $script:root)
}
else {
    try { Remove-Item -LiteralPath $script:root -Recurse -Force -ErrorAction SilentlyContinue } catch { }
}
if ($script:failed -ne 0) { exit 1 }
exit 0
