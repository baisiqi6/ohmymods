#requires -Version 5.1
param(
    [Parameter(Mandatory = $true)][string]$CollectorPath,
    [Parameter(Mandatory = $true)][string]$WorkRoot
)

# Regression cases derived from independent review; explicit synthetic roots only.
# Uses only explicit synthetic roots; no default-profile collection, Explorer,
# game-named process, game operation, network, or automatic fixture deletion.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$psExe = Join-Path $PSHOME 'powershell.exe'
$CollectorPath = [IO.Path]::GetFullPath($CollectorPath)
$WorkRoot = [IO.Path]::GetFullPath($WorkRoot)
$runRoot = Join-Path $WorkRoot ('review-edges-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($runRoot) | Out-Null
$utf8 = New-Object Text.UTF8Encoding($false)
$failed = 0
$total = 0

function Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Case([string]$Name, [scriptblock]$Body) {
    $script:total++
    try {
        & $Body
        Write-Host ('[PASS] ' + $Name)
    }
    catch {
        $script:failed++
        Write-Host ('[FAIL] ' + $Name + ': ' + $_.Exception.Message)
    }
}

function Put([string]$Path, [string]$Text) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Text, $script:utf8)
}

function Fixture([string]$Name) {
    $base = Join-Path $script:runRoot $Name
    $game = Join-Path $base 'game'
    $user = Join-Path $base 'user'
    $output = Join-Path $base 'output'
    Put (Join-Path $game 'KingdomTwoCrowns.exe') 'marker'
    Put (Join-Path $game 'BepInEx\LogOutput.log') 'b'
    Put (Join-Path $user 'Player.log') 'p'
    Put (Join-Path $user 'Release\save.dat') 's'
    return [pscustomobject]@{ Base = $base; Game = $game; User = $user; Output = $output }
}

function Collect($Fixture, [string]$Output, [string[]]$Extra = @()) {
    $arguments = @('-GameRoot', $Fixture.Game, '-UserDataRoot', $Fixture.User, '-OutputRoot', $Output, '-NoOpen') + $Extra
    $log = & $script:psExe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $script:CollectorPath @arguments *>&1 | Out-String
    $code = $LASTEXITCODE
    return [pscustomobject]@{ ExitCode = $code; Output = $log }
}

function Manifest([string]$Output) {
    $zips = @(Get-ChildItem -LiteralPath $Output -Recurse -File -Filter '*.zip')
    Check ($zips.Count -eq 1) ('Expected one ZIP, got ' + $zips.Count)
    $zip = [IO.Compression.ZipFile]::OpenRead($zips[0].FullName)
    try {
        $entry = $zip.GetEntry('manifest.json')
        Check ($null -ne $entry) 'Missing manifest.json'
        $reader = New-Object IO.StreamReader($entry.Open(), $script:utf8, $true)
        try { return ($reader.ReadToEnd() | ConvertFrom-Json) }
        finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }
}

Case 'reject an OutputRoot whose ancestor junction points into GameRoot' {
    $f = Fixture 'ancestor-output-alias'
    $alias = Join-Path $f.Base 'alias'
    New-Item -ItemType Junction -Path $alias -Target $f.Game | Out-Null
    $aliasedOutput = Join-Path $alias 'diagnostics'
    $result = Collect $f $aliasedOutput
    $writtenInsideGame = Test-Path -LiteralPath (Join-Path $f.Game 'diagnostics')
    Check ($result.ExitCode -eq 1 -and -not $writtenInsideGame) (
        'Collector accepted an aliased source/output overlap: exit=' + $result.ExitCode +
        ', created GameRoot/diagnostics=' + $writtenInsideGame + '; ' + $result.Output)
}

Case 'metadata hashing observes the lowered per-file read limit' {
    $f = Fixture 'metadata-limit'
    # Copy a valid OS binary to a synthetic marker; it is never executed.
    [IO.File]::Copy($script:psExe, (Join-Path $f.Game 'KingdomTwoCrowns.exe'), $true)
    $result = Collect $f $f.Output @('-MaxFileBytes', '8', '-MaxTotalBytes', '16')
    Check ($result.ExitCode -ne 1) ('No usable report: ' + $result.Output)
    $m = Manifest $f.Output
    Check (-not [bool]$m.metadata.gameExe.present -and [string]::IsNullOrEmpty([string]$m.metadata.gameExe.sha256)) (
        'Metadata hashed a binary larger than maxFileBytes: size=' + $m.metadata.gameExe.size +
        ', limit=' + $m.limits.maxFileBytes + ', present=' + $m.metadata.gameExe.present)
}

Case 'changed flag detects a differing pre-copy length when mtime is preserved' {
    # Load only the two original function definitions. The collector entry point
    # is not dot-sourced or executed for this function-level regression.
    $text = [IO.File]::ReadAllText($script:CollectorPath)
    $tokens = $null
    $parseErrors = $null
    $ast = [Management.Automation.Language.Parser]::ParseInput($text, [ref]$tokens, [ref]$parseErrors)
    Check ($parseErrors.Count -eq 0) 'Collector parse errors'
    foreach ($functionName in @('ConvertTo-Hex', 'Copy-SourceToStaging')) {
        $nodes = @($ast.FindAll({
            param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $functionName
        }, $true))
        Check ($nodes.Count -eq 1) ('Missing function ' + $functionName)
        . ([scriptblock]::Create($nodes[0].Extent.Text))
    }
    $base = Join-Path $script:runRoot 'changed-before-length'
    $source = Join-Path $base 'source.log'
    $stage = Join-Path $base 'stage'
    Put $source '12345678'
    [IO.Directory]::CreateDirectory($stage) | Out-Null
    $before = Get-Item -LiteralPath $source
    $before.Refresh()
    $beforeLength = [long]$before.Length
    $beforeTime = $before.LastWriteTimeUtc
    [IO.File]::AppendAllText($source, 'abcd', $script:utf8)
    [IO.File]::SetLastWriteTimeUtc($source, $beforeTime)
    $state = [pscustomobject]@{
        StageDir = $stage; StageIndex = 0; TotalBytes = [long]0; ReadBytes = [long]0; MetadataBytes = [long]0
        Changed = $false; Partial = $false
        Limits = [pscustomobject]@{ MaxFileBytes = [long]64; MaxTotalBytes = [long]64 }
    }
    $copy = Copy-SourceToStaging $state $source $before
    Check ($copy.Outcome -eq 'collected') ('Unexpected outcome ' + $copy.Outcome)
    Check ([long]$copy.Size -ne $beforeLength) 'Fixture did not change the source length'
    Check ([bool]$copy.Changed -and [bool]$state.Changed) (
        'Known size change was not reported: before=' + $beforeLength +
        ', copied=' + $copy.Size + ', changed=' + $copy.Changed)
}


Case 'directory-only skipped entries obey the traversal cap' {
    $f = Fixture 'entry-cap'
    [IO.File]::Delete((Join-Path $f.User 'Release/save.dat'))
    foreach ($n in 1..40) {
        [IO.Directory]::CreateDirectory((Join-Path $f.User ('Release/d' + $n))) | Out-Null
    }
    $result = Collect $f $f.Output @('-MaxFiles', '2', '-MaxDepth', '1')
    Check ($result.ExitCode -eq 2) ('Expected partial bounded ZIP: ' + $result.Output)
    $m = Manifest $f.Output
    $skips = @($m.entries | Where-Object { $_.category -eq 'save-release' -and $_.status -eq 'skipped-depth' })
    Check ($skips.Count -le 4) ('Directory skips bypassed the traversal budget: ' + $skips.Count)
    Check (@($m.errors | Where-Object {$_.code -eq 'entry-limit'}).Count -eq 1) 'Expected one bounded entry-limit diagnostic'
    Check ([long]$m.counts.bytesRead -le [long]$m.limits.maxTotalBytes) 'Read budget exceeded'
}

Write-Host ('Review cases=' + $total + ', failed=' + $failed)
Write-Host ('Fixtures retained for inspection: ' + $runRoot)
if ($failed -gt 0) { exit 1 }
exit 0
