[CmdletBinding()]
param(
    [string]$SourceGame = 'E:\QQ\QQ下载文件\Kingdom Two Crowns (1)\Kingdom Two Crowns',
    [string]$BuildDll = (Join-Path $PSScriptRoot 'il2cpp\bin\Debug\KingdomEnhancedMod.dll'),
    [string]$OutputZip = (Join-Path $PSScriptRoot 'release\KingdomEnhancedMod_v2.4.0_IL2CPP.zip')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Require-File([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label missing: $Path"
    }
}

function Copy-RequiredFile([string]$Source, [string]$Destination) {
    Require-File $Source 'Required package file'
    $parent = Split-Path -Parent $Destination
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

$releaseDir = Split-Path -Parent $OutputZip
$stage = Join-Path $releaseDir 'stage-il2cpp-candidate'
$pluginEntry = 'BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll'

Require-File $BuildDll 'Build output'
Require-File (Join-Path $SourceGame 'dotnet\coreclr.dll') 'Root dotnet runtime'
Require-File (Join-Path $SourceGame 'BepInEx\core\BepInEx.Unity.IL2CPP.dll') 'BepInEx IL2CPP core'
Require-File (Join-Path $SourceGame 'BepInEx\config\KingdomEnhancedMod.cfg') 'Plugin config'

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
if (Test-Path -LiteralPath $stage) {
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    $resolvedRelease = [IO.Path]::GetFullPath($releaseDir) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedStage.StartsWith($resolvedRelease, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove staging path outside release directory: $resolvedStage"
    }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stage | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'BepInEx') | Out-Null

try {
    Copy-RequiredFile (Join-Path $SourceGame 'winhttp.dll') (Join-Path $stage 'winhttp.dll')
    Copy-RequiredFile (Join-Path $SourceGame 'doorstop_config.ini') (Join-Path $stage 'doorstop_config.ini')
    Copy-RequiredFile (Join-Path $SourceGame '.doorstop_version') (Join-Path $stage '.doorstop_version')
    Copy-Item -LiteralPath (Join-Path $SourceGame 'dotnet') -Destination (Join-Path $stage 'dotnet') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $SourceGame 'BepInEx\core') -Destination (Join-Path $stage 'BepInEx\core') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $SourceGame 'BepInEx\unity-libs') -Destination (Join-Path $stage 'BepInEx\unity-libs') -Recurse -Force
    Copy-RequiredFile $BuildDll (Join-Path $stage ($pluginEntry -replace '/', '\'))
    Copy-RequiredFile (Join-Path $SourceGame 'BepInEx\config\KingdomEnhancedMod.cfg') (Join-Path $stage 'BepInEx\config\KingdomEnhancedMod.cfg')
    Copy-RequiredFile (Join-Path $PSScriptRoot 'release-notes-il2cpp.md') (Join-Path $stage 'INSTALL.md')
    Copy-RequiredFile (Join-Path $PSScriptRoot 'release\MOD_USER_GUIDE_ZH.txt') (Join-Path $stage 'MOD_USER_GUIDE_ZH.txt')
    Copy-RequiredFile (Join-Path $PSScriptRoot 'release\MOD_CAPABILITIES_AND_ROADMAP_ZH.txt') (Join-Path $stage 'MOD_CAPABILITIES_AND_ROADMAP_ZH.txt')
    Copy-RequiredFile (Join-Path $PSScriptRoot 'release\MOD_UPDATE_AND_FIX_LOG_ZH.txt') (Join-Path $stage 'MOD_UPDATE_AND_FIX_LOG_ZH.txt')

    $dllHash = (Get-FileHash -LiteralPath $BuildDll -Algorithm SHA256).Hash
    $gitCommit = (& git -C $PSScriptRoot rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not $gitCommit) { $gitCommit = 'unknown' }
    $gitDirty = (& git -C $PSScriptRoot status --porcelain 2>$null)
    if ($LASTEXITCODE -ne 0) { $gitDirty = 'unknown' }
    elseif ($gitDirty) { $gitDirty = 'true' }
    else { $gitDirty = 'false' }
    $manifest = @(
        'Package: KingdomEnhancedMod',
        'GameCompatibility: Kingdom Two Crowns 2.4.0 IL2CPP',
        'ModVersion: 2.4.0',
        ('BuildId: ' + $dllHash.Substring(0, 12)),
        ('GitCommit: ' + $gitCommit.Trim()),
        ('GitWorkingTreeDirty: ' + $gitDirty),
        ('DllSHA256: ' + $dllHash),
        'Loader: BepInEx 6 IL2CPP + root dotnet runtime',
        ('GeneratedUtc: ' + [DateTime]::UtcNow.ToString('o'))
    ) -join "`n"
    [IO.File]::WriteAllText((Join-Path $stage 'BUILD-MANIFEST.txt'), $manifest + "`n", [Text.UTF8Encoding]::new($false))

    if (Test-Path -LiteralPath $OutputZip) { Remove-Item -LiteralPath $OutputZip -Force }
    $utf8 = [Text.UTF8Encoding]::new($false)
    [IO.Compression.ZipFile]::CreateFromDirectory(
        $stage,
        $OutputZip,
        [IO.Compression.CompressionLevel]::Optimal,
        $false,
        $utf8
    )

    $expected = $dllHash
    $required = @(
        '.doorstop_version',
        'doorstop_config.ini',
        'winhttp.dll',
        'dotnet/coreclr.dll',
        'BepInEx/core/BepInEx.Unity.IL2CPP.dll',
        $pluginEntry,
        'BepInEx/config/KingdomEnhancedMod.cfg',
        'INSTALL.md',
        'MOD_USER_GUIDE_ZH.txt',
        'MOD_CAPABILITIES_AND_ROADMAP_ZH.txt',
        'MOD_UPDATE_AND_FIX_LOG_ZH.txt',
        'BUILD-MANIFEST.txt'
    )

    $archive = [IO.Compression.ZipFile]::OpenRead($OutputZip)
    try {
        $names = @($archive.Entries | ForEach-Object { $_.FullName })
        foreach ($entry in $required) {
            if ($names -notcontains $entry) { throw "Archive entry missing: $entry" }
        }
        if ($names -contains 'BepInEx/dotnet/coreclr.dll') {
            throw 'Duplicate BepInEx/dotnet runtime is forbidden; doorstop uses root dotnet.'
        }
        if ($names | Where-Object { $_ -match '^KingdomEnhancedMod_v[^/]+/' }) {
            throw 'Archive contains a staging/version top-level directory instead of game-root content.'
        }

        $entry = $archive.GetEntry($pluginEntry)
        $stream = $entry.Open()
        try {
            $sha = [Security.Cryptography.SHA256]::Create()
            try {
                $actual = ($sha.ComputeHash($stream) | ForEach-Object { $_.ToString('x2') }) -join ''
            } finally {
                $sha.Dispose()
            }
        } finally {
            $stream.Dispose()
        }
        if ($actual.ToUpperInvariant() -ne $expected) {
            throw "Archive DLL hash mismatch: expected $expected, actual $actual"
        }

        $installEntry = $archive.GetEntry('INSTALL.md')
        $reader = [IO.StreamReader]::new($installEntry.Open(), [Text.UTF8Encoding]::new($false), $true)
        try {
            $firstLine = $reader.ReadLine()
        } finally {
            $reader.Dispose()
        }
        if ($firstLine -notlike '# Kingdom Enhanced Mod*') {
            throw 'INSTALL.md UTF-8 smoke check failed.'
        }
    } finally {
        $archive.Dispose()
    }

    Write-Host "Candidate package verified: $OutputZip"
    Write-Host "KingdomEnhancedMod.dll SHA256: $expected"
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
