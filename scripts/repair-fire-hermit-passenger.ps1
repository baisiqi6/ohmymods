#requires -Version 7.0

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $InputPath,

    [Parameter(Mandatory = $true)]
    [string] $BackupPath,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string] $ExpectedSHA256,

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, [long]::MaxValue)]
    [long] $ExpectedLength,

    [switch] $Apply
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ApprovedInputSHA256 = 'C3A8CEF5B3B59B0C4A763235B138381ED6327ABAAA2311F95530624AC17E55E8'
$ApprovedInputLength = 817111
$ExpectedVersion = 16
$ExpectedCampaign = 1
$ExpectedCampaignCount = 2
$ExpectedBiome = 5
$ExpectedChallengeId = 0
$ExpectedReign = 1
$ExpectedLand = 6
$FireHermitIndex = 6
$OriginalPosition = 0
$PassengerPosition = 5
$ExpectedPlayer = 0
$ExpectedStatusLand = 0
$ExpectedHermitStatusCount = 7
$FireNetId = 980
$AllowedPaths = @(
    '/campaigns/1/hermitStatuses/6/position',
    '/campaigns/1/currentReign/hermitStatuses/6/position')
$FireObjectMarkers = @('HermitFire', 'Hermit Fire', 'Fire Hermit')

function Assert-GameNotRunning {
    if (Get-Process -Name 'KingdomTwoCrowns' -ErrorAction SilentlyContinue) {
        throw 'KingdomTwoCrowns.exe is running. Exit the game before repairing the save.'
    }
}

function Assert-SourceIdentity {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $SHA256,
        [Parameter(Mandatory = $true)][long] $Length
    )

    $item = Get-Item -LiteralPath $Path
    if ($item.Length -ne $Length) {
        throw "Source length=$($item.Length), expected $Length."
    }
    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actualHash -ne $SHA256.ToUpperInvariant()) {
        throw "Source SHA256=$actualHash, expected $($SHA256.ToUpperInvariant())."
    }
    $actualHash
}

function Read-GzipJson {
    param([Parameter(Mandatory = $true)][string] $Path)

    $file = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    try {
        $gzip = [System.IO.Compression.GZipStream]::new(
            $file,
            [System.IO.Compression.CompressionMode]::Decompress,
            $false)
        try {
            $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
            $reader = [System.IO.StreamReader]::new($gzip, $utf8, $true)
            try { $text = $reader.ReadToEnd() }
            finally { $reader.Dispose() }
        }
        finally { $gzip.Dispose() }
    }
    finally { $file.Dispose() }

    try { $root = [System.Text.Json.Nodes.JsonNode]::Parse($text) }
    catch { throw "'$Path' is not valid strict UTF-8 JSON in gzip: $($_.Exception.Message)" }
    if ($root -isnot [System.Text.Json.Nodes.JsonObject]) {
        throw "'$Path' root is not a JSON object."
    }
    [pscustomobject]@{ Text = $text; Root = $root }
}

function Get-RequiredNode {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Object,
        [Parameter(Mandatory = $true)][string] $Name
    )
    if (-not $Object.ContainsKey($Name) -or $null -eq $Object[$Name]) {
        throw "Missing required JSON property '$Name'."
    }
    Write-Output -NoEnumerate $Object[$Name]
}

function Get-RequiredObject {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Object,
        [Parameter(Mandatory = $true)][string] $Name
    )
    $node = Get-RequiredNode $Object $Name
    if ($node -isnot [System.Text.Json.Nodes.JsonObject]) {
        throw "Property '$Name' is not an object."
    }
    Write-Output -NoEnumerate $node
}

function Get-RequiredArray {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Object,
        [Parameter(Mandatory = $true)][string] $Name
    )
    $node = Get-RequiredNode $Object $Name
    if ($node -isnot [System.Text.Json.Nodes.JsonArray]) {
        throw "Property '$Name' is not an array."
    }
    Write-Output -NoEnumerate $node
}

function Get-RequiredInt {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Object,
        [Parameter(Mandatory = $true)][string] $Name
    )
    try { (Get-RequiredNode $Object $Name).GetValue[int]() }
    catch { throw "Property '$Name' is not an Int32." }
}

function Get-RequiredBool {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Object,
        [Parameter(Mandatory = $true)][string] $Name
    )
    try { (Get-RequiredNode $Object $Name).GetValue[bool]() }
    catch { throw "Property '$Name' is not Boolean." }
}

function Get-RequiredString {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Object,
        [Parameter(Mandatory = $true)][string] $Name
    )
    try { (Get-RequiredNode $Object $Name).GetValue[string]() }
    catch { throw "Property '$Name' is not a string." }
}

function Assert-ExactStatusShape {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Status,
        [Parameter(Mandatory = $true)][string] $Context
    )

    $expected = @('position', 'player', 'land')
    if ($Status.Count -ne $expected.Count) {
        throw "$Context must contain exactly position/player/land."
    }
    foreach ($name in $expected) {
        if (-not $Status.ContainsKey($name)) {
            throw "$Context is missing '$name'."
        }
        [void] (Get-RequiredInt $Status $name)
    }
}

function Test-ContainsFireMarker {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string] $Value
    )
    foreach ($marker in $FireObjectMarkers) {
        if ($Value.IndexOf($marker, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }
    return $false
}

function Get-RepairContext {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Root,
        [Parameter(Mandatory = $true)][int] $ExpectedFirePosition,
        [Parameter(Mandatory = $true)][bool] $RequireNoPassenger
    )

    if ((Get-RequiredInt $Root 'serializedSaveDataVersion') -ne $ExpectedVersion) {
        throw 'serializedSaveDataVersion changed.'
    }
    if ((Get-RequiredInt $Root '_currentCampaign') -ne $ExpectedCampaign) {
        throw '_currentCampaign changed.'
    }

    $campaigns = Get-RequiredArray $Root 'campaigns'
    if ($campaigns.Count -ne $ExpectedCampaignCount -or
        $campaigns[$ExpectedCampaign] -isnot [System.Text.Json.Nodes.JsonObject]) {
        throw "Expected exactly $ExpectedCampaignCount campaigns with campaign $ExpectedCampaign present."
    }
    $campaign = [System.Text.Json.Nodes.JsonObject] $campaigns[$ExpectedCampaign]
    if ((Get-RequiredInt $campaign 'biomeIndex') -ne $ExpectedBiome -or
        (Get-RequiredInt $campaign 'challengeId') -ne $ExpectedChallengeId -or
        (Get-RequiredInt $campaign 'reign') -ne $ExpectedReign -or
        (Get-RequiredInt $campaign 'currentLand') -ne $ExpectedLand) {
        throw 'Target campaign identity changed.'
    }

    $currentReign = Get-RequiredObject $campaign 'currentReign'
    if (-not (Get-RequiredBool $currentReign 'isCurrent') -or
        (Get-RequiredInt $currentReign 'currentLand') -ne $ExpectedLand) {
        throw 'currentReign identity changed.'
    }

    $campaignStatuses = Get-RequiredArray $campaign 'hermitStatuses'
    $reignStatuses = Get-RequiredArray $currentReign 'hermitStatuses'
    if ($campaignStatuses.Count -ne $ExpectedHermitStatusCount -or
        $reignStatuses.Count -ne $ExpectedHermitStatusCount) {
        throw "Both hermitStatuses arrays must contain exactly $ExpectedHermitStatusCount entries."
    }
    if (-not [System.Text.Json.Nodes.JsonNode]::DeepEquals($campaignStatuses, $reignStatuses)) {
        throw 'Campaign and currentReign hermitStatuses are not identical.'
    }

    for ($index = 0; $index -lt $campaignStatuses.Count; $index++) {
        if ($campaignStatuses[$index] -isnot [System.Text.Json.Nodes.JsonObject]) {
            throw "hermitStatuses[$index] is not an object."
        }
        $position = Get-RequiredInt ([System.Text.Json.Nodes.JsonObject] $campaignStatuses[$index]) 'position'
        if ($RequireNoPassenger -and $position -eq $PassengerPosition) {
            throw "Existing Passenger found at hermitStatuses[$index]."
        }
        if (-not $RequireNoPassenger -and $index -ne $FireHermitIndex -and
            $position -eq $PassengerPosition) {
            throw "Unexpected non-Fire Passenger found at hermitStatuses[$index]."
        }
    }

    $campaignFire = [System.Text.Json.Nodes.JsonObject] $campaignStatuses[$FireHermitIndex]
    $reignFire = [System.Text.Json.Nodes.JsonObject] $reignStatuses[$FireHermitIndex]
    Assert-ExactStatusShape $campaignFire 'campaign Fire status'
    Assert-ExactStatusShape $reignFire 'currentReign Fire status'
    foreach ($status in @($campaignFire, $reignFire)) {
        if ((Get-RequiredInt $status 'position') -ne $ExpectedFirePosition -or
            (Get-RequiredInt $status 'player') -ne $ExpectedPlayer -or
            (Get-RequiredInt $status 'land') -ne $ExpectedStatusLand) {
            throw "Fire status must be position=$ExpectedFirePosition/player=$ExpectedPlayer/land=$ExpectedStatusLand."
        }
    }

    $islands = Get-RequiredArray $campaign '_islands'
    $currentLandMatches = 0
    $objectCount = 0
    $dynamicFireNetIds = 0
    $nonDynamicFireNetIds = 0
    $markerMatches = 0
    foreach ($islandNode in $islands) {
        if ($islandNode -isnot [System.Text.Json.Nodes.JsonObject]) {
            throw 'Target campaign contains a non-object island.'
        }
        $island = [System.Text.Json.Nodes.JsonObject] $islandNode
        if ((Get-RequiredInt $island 'land') -eq $ExpectedLand) { $currentLandMatches++ }
        $objects = Get-RequiredArray $island 'objects'
        foreach ($objectNode in $objects) {
            if ($objectNode -isnot [System.Text.Json.Nodes.JsonObject]) {
                throw 'Target campaign contains a non-object Persistent entry.'
            }
            $object = [System.Text.Json.Nodes.JsonObject] $objectNode
            $objectCount++
            $crpcType = Get-RequiredInt $object 'crpcType'
            $netId = Get-RequiredInt $object 'netID'
            if ($netId -eq $FireNetId) {
                if ($crpcType -eq 1) { $dynamicFireNetIds++ }
                else { $nonDynamicFireNetIds++ }
            }
            $hasFireMarker = $false
            foreach ($name in @('name', 'prefabPath', 'uniqueID')) {
                if (Test-ContainsFireMarker (Get-RequiredString $object $name)) {
                    $hasFireMarker = $true
                }
            }
            if ($hasFireMarker) { $markerMatches++ }
        }
    }
    if ($currentLandMatches -ne 1) {
        throw "Expected exactly one island with land=$ExpectedLand; found $currentLandMatches."
    }
    if ($dynamicFireNetIds -ne 0 -or $nonDynamicFireNetIds -ne 0) {
        throw "Found forbidden netID 980 objects: Dynamic=$dynamicFireNetIds nonDynamic=$nonDynamicFireNetIds."
    }
    if ($markerMatches -ne 0) {
        throw "Found $markerMatches forbidden Hermit Fire object marker match(es)."
    }

    [pscustomobject]@{
        Campaign = $campaign
        CurrentReign = $currentReign
        CampaignStatuses = $campaignStatuses
        ReignStatuses = $reignStatuses
        CampaignFire = $campaignFire
        ReignFire = $reignFire
        ObjectCount = $objectCount
        DynamicFireNetIds = $dynamicFireNetIds
        NonDynamicFireNetIds = $nonDynamicFireNetIds
        MarkerMatches = $markerMatches
    }
}

function Set-FirePosition {
    param(
        [Parameter(Mandatory = $true)][pscustomobject] $Context,
        [Parameter(Mandatory = $true)][int] $Position
    )
    $Context.CampaignFire['position'] = [System.Text.Json.Nodes.JsonValue]::Create($Position)
    $Context.ReignFire['position'] = [System.Text.Json.Nodes.JsonValue]::Create($Position)
}

function Get-StatusArrays {
    param([Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Root)
    $campaigns = Get-RequiredArray $Root 'campaigns'
    $campaign = [System.Text.Json.Nodes.JsonObject] $campaigns[$ExpectedCampaign]
    $currentReign = Get-RequiredObject $campaign 'currentReign'
    [pscustomobject]@{
        Campaign = Get-RequiredArray $campaign 'hermitStatuses'
        Reign = Get-RequiredArray $currentReign 'hermitStatuses'
    }
}

function Assert-OnlyApprovedChanges {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $BeforeRoot,
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $AfterRoot
    )

    $beforeArrays = Get-StatusArrays $BeforeRoot
    $afterArrays = Get-StatusArrays $AfterRoot
    $pairs = @(
        [pscustomobject]@{
            Before = $beforeArrays.Campaign
            After = $afterArrays.Campaign
        },
        [pscustomobject]@{
            Before = $beforeArrays.Reign
            After = $afterArrays.Reign
        })
    foreach ($pair in $pairs) {
        $before = [System.Text.Json.Nodes.JsonArray] $pair.Before
        $after = [System.Text.Json.Nodes.JsonArray] $pair.After
        if ($before.Count -ne $ExpectedHermitStatusCount -or
            $after.Count -ne $ExpectedHermitStatusCount) {
            throw 'hermitStatuses count changed.'
        }
        for ($index = 0; $index -lt $before.Count; $index++) {
            if ($index -ne $FireHermitIndex -and
                -not [System.Text.Json.Nodes.JsonNode]::DeepEquals(
                    $before[$index], $after[$index])) {
                throw "Non-Fire hermitStatuses[$index] changed."
            }
        }
        $beforeFire = [System.Text.Json.Nodes.JsonObject] $before[$FireHermitIndex].DeepClone()
        $afterFire = [System.Text.Json.Nodes.JsonObject] $after[$FireHermitIndex].DeepClone()
        $beforeFire['position'] = [System.Text.Json.Nodes.JsonValue]::Create(0)
        $afterFire['position'] = [System.Text.Json.Nodes.JsonValue]::Create(0)
        if (-not [System.Text.Json.Nodes.JsonNode]::DeepEquals($beforeFire, $afterFire)) {
            throw 'A Fire status field other than position changed.'
        }
    }

    $beforeClone = [System.Text.Json.Nodes.JsonObject] $BeforeRoot.DeepClone()
    $afterClone = [System.Text.Json.Nodes.JsonObject] $AfterRoot.DeepClone()
    $beforeCloneArrays = Get-StatusArrays $beforeClone
    $afterCloneArrays = Get-StatusArrays $afterClone
    $beforeCampaignFire = [System.Text.Json.Nodes.JsonObject] `
        $beforeCloneArrays.Campaign[$FireHermitIndex]
    $beforeReignFire = [System.Text.Json.Nodes.JsonObject] `
        $beforeCloneArrays.Reign[$FireHermitIndex]
    $afterCampaignFire = [System.Text.Json.Nodes.JsonObject] `
        $afterCloneArrays.Campaign[$FireHermitIndex]
    $afterReignFire = [System.Text.Json.Nodes.JsonObject] `
        $afterCloneArrays.Reign[$FireHermitIndex]
    $beforeCampaignFire['position'] = [System.Text.Json.Nodes.JsonValue]::Create(0)
    $beforeReignFire['position'] = [System.Text.Json.Nodes.JsonValue]::Create(0)
    $afterCampaignFire['position'] = [System.Text.Json.Nodes.JsonValue]::Create(0)
    $afterReignFire['position'] = [System.Text.Json.Nodes.JsonValue]::Create(0)
    if (-not [System.Text.Json.Nodes.JsonNode]::DeepEquals($beforeClone, $afterClone)) {
        throw 'JSON changed outside the two approved Fire position paths.'
    }
}

function Write-GzipJson {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Root,
        [Parameter(Mandatory = $true)][string] $Path
    )

    $options = [System.Text.Json.JsonSerializerOptions]::new()
    $bytes = [System.Text.UTF8Encoding]::new($false, $true).GetBytes(
        $Root.ToJsonString($options))
    $file = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        $gzip = [System.IO.Compression.GZipStream]::new(
            $file,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $true)
        try { $gzip.Write($bytes, 0, $bytes.Length) }
        finally { $gzip.Dispose() }
        $file.Flush($true)
    }
    finally { $file.Dispose() }
}

function Copy-Verified {
    param(
        [Parameter(Mandatory = $true)][string] $Source,
        [Parameter(Mandatory = $true)][string] $Destination,
        [Parameter(Mandatory = $true)][string] $SHA256,
        [Parameter(Mandatory = $true)][long] $Length
    )

    $sourceStream = $null
    $destinationStream = $null
    try {
        $sourceStream = [System.IO.File]::Open(
            $Source, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::Read)
        $destinationStream = [System.IO.File]::Open(
            $Destination, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        $sourceStream.CopyTo($destinationStream)
        $destinationStream.Flush($true)
    }
    catch {
        if ($destinationStream) { $destinationStream.Dispose(); $destinationStream = $null }
        if ($sourceStream) { $sourceStream.Dispose(); $sourceStream = $null }
        if (Test-Path -LiteralPath $Destination) {
            Remove-Item -LiteralPath $Destination -Force
        }
        throw
    }
    finally {
        if ($destinationStream) { $destinationStream.Dispose() }
        if ($sourceStream) { $sourceStream.Dispose() }
    }
    try { [void] (Assert-SourceIdentity $Destination $SHA256 $Length) }
    catch {
        Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue
        throw
    }
}

function Assert-SourceStillOriginal {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $SHA256,
        [Parameter(Mandatory = $true)][long] $Length,
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Baseline
    )

    Assert-GameNotRunning
    [void] (Assert-SourceIdentity $Path $SHA256 $Length)
    $fresh = Read-GzipJson $Path
    [void] (Get-RepairContext $fresh.Root $OriginalPosition $true)
    if (-not [System.Text.Json.Nodes.JsonNode]::DeepEquals($Baseline, $fresh.Root)) {
        throw 'Source JSON changed despite matching the approved identity.'
    }
}

function Restore-Original {
    param(
        [Parameter(Mandatory = $true)][string] $Input,
        [Parameter(Mandatory = $true)][string] $Rollback,
        [Parameter(Mandatory = $true)][bool] $RollbackValid,
        [Parameter(Mandatory = $true)][string] $Backup,
        [Parameter(Mandatory = $true)][string] $SHA256,
        [Parameter(Mandatory = $true)][long] $Length
    )

    $directory = [System.IO.Path]::GetDirectoryName($Input)
    if ($RollbackValid) {
        try {
            $failed = [System.IO.Path]::Combine(
                $directory,
                ([System.IO.Path]::GetFileName($Input) + '.fire-hermit.failed.' +
                    [guid]::NewGuid().ToString('N') + '.tmp'))
            [System.IO.File]::Replace($Rollback, $Input, $failed, $false)
            [void] (Assert-SourceIdentity $Input $SHA256 $Length)
            Remove-Item -LiteralPath $failed -Force -ErrorAction SilentlyContinue
            return
        }
        catch { }
    }

    [void] (Assert-SourceIdentity $Backup $SHA256 $Length)
    $restore = [System.IO.Path]::Combine(
        $directory,
        ([System.IO.Path]::GetFileName($Input) + '.fire-hermit.restore.' +
            [guid]::NewGuid().ToString('N') + '.tmp'))
    Copy-Verified $Backup $restore $SHA256 $Length
    $failed = [System.IO.Path]::Combine(
        $directory,
        ([System.IO.Path]::GetFileName($Input) + '.fire-hermit.failed-backup.' +
            [guid]::NewGuid().ToString('N') + '.tmp'))
    [System.IO.File]::Replace($restore, $Input, $failed, $false)
    [void] (Assert-SourceIdentity $Input $SHA256 $Length)
    Remove-Item -LiteralPath $failed -Force -ErrorAction SilentlyContinue
}

$input = (Resolve-Path -LiteralPath $InputPath).ProviderPath
$backup = [System.IO.Path]::GetFullPath($BackupPath)
if ($ExpectedSHA256.ToUpperInvariant() -ne $ApprovedInputSHA256 -or
    $ExpectedLength -ne $ApprovedInputLength) {
    throw 'ExpectedSHA256/ExpectedLength do not match this one-time repair input.'
}
if ([string]::Equals($input, $backup, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'BackupPath must differ from InputPath.'
}
if (Test-Path -LiteralPath $backup) {
    throw "BackupPath already exists: '$backup'."
}
$backupDirectory = [System.IO.Path]::GetDirectoryName($backup)
if ([string]::IsNullOrEmpty($backupDirectory) -or
    -not (Test-Path -LiteralPath $backupDirectory -PathType Container)) {
    throw 'BackupPath parent directory does not exist.'
}

$inputDirectory = [System.IO.Path]::GetDirectoryName($input)
$candidateDirectory = if ($Apply) { $inputDirectory } else { [System.IO.Path]::GetTempPath() }
$candidatePath = [System.IO.Path]::Combine(
    $candidateDirectory,
    ([System.IO.Path]::GetFileName($input) + '.fire-hermit-passenger.' +
        [guid]::NewGuid().ToString('N') + '.tmp'))
$rollbackPath = [System.IO.Path]::Combine(
    $inputDirectory,
    ([System.IO.Path]::GetFileName($input) + '.fire-hermit-rollback.' +
        [guid]::NewGuid().ToString('N') + '.tmp'))

Assert-GameNotRunning
$inputHash = Assert-SourceIdentity $input $ExpectedSHA256 $ExpectedLength
$candidateCreated = $false
$resultMessage = $null
try {
    $source = Read-GzipJson $input
    $baseline = [System.Text.Json.Nodes.JsonObject] $source.Root.DeepClone()
    $before = Get-RepairContext $source.Root $OriginalPosition $true
    Set-FirePosition $before $PassengerPosition

    Write-GzipJson $source.Root $candidatePath
    $candidateCreated = $true
    $candidateHash = (Get-FileHash -LiteralPath $candidatePath -Algorithm SHA256).Hash
    $candidateLength = (Get-Item -LiteralPath $candidatePath).Length
    $candidate = Read-GzipJson $candidatePath
    $after = Get-RepairContext $candidate.Root $PassengerPosition $false
    Assert-OnlyApprovedChanges $baseline $candidate.Root
    if (-not [System.Text.Json.Nodes.JsonNode]::DeepEquals(
            $after.CampaignStatuses, $after.ReignStatuses)) {
        throw 'Candidate status mirrors differ.'
    }

    Assert-SourceStillOriginal $input $ExpectedSHA256 $ExpectedLength $baseline
    if (Test-Path -LiteralPath $backup) {
        throw 'BackupPath appeared during candidate validation.'
    }

    $summary = "campaign=$ExpectedCampaign reign=$ExpectedReign land=$ExpectedLand "
    $summary += "Fire=position:$OriginalPosition->$PassengerPosition/player:$ExpectedPlayer/land:$ExpectedStatusLand "
    $summary += "objects=$($before.ObjectCount) dynamic980=$($before.DynamicFireNetIds) "
    $summary += "nonDynamic980=$($before.NonDynamicFireNetIds) markers=$($before.MarkerMatches) "
    $summary += "paths=$($AllowedPaths -join ',') inputHash=$inputHash "
    $summary += "candidateHash=$candidateHash candidateLength=$candidateLength"

    if (-not $Apply) {
        $resultMessage = "Validated only: $summary"
    }
    elseif ($PSCmdlet.ShouldProcess(
            $input, 'Back up and atomically restore Fire hermit as P1 Passenger')) {
        Assert-SourceStillOriginal $input $ExpectedSHA256 $ExpectedLength $baseline
        if (Test-Path -LiteralPath $backup) { throw 'BackupPath is no longer new.' }
        Copy-Verified $input $backup $ExpectedSHA256 $ExpectedLength

        Assert-SourceStillOriginal $input $ExpectedSHA256 $ExpectedLength $baseline
        [void] (Assert-SourceIdentity $backup $ExpectedSHA256 $ExpectedLength)

        # This is the check immediately adjacent to the only source replacement.
        [void] (Assert-SourceIdentity $backup $ExpectedSHA256 $ExpectedLength)
        Assert-SourceStillOriginal $input $ExpectedSHA256 $ExpectedLength $baseline
        $replaced = $false
        $rollbackValid = $false
        try {
            [System.IO.File]::Replace($candidatePath, $input, $rollbackPath, $false)
            $replaced = $true
            $candidateCreated = $false

            [void] (Assert-SourceIdentity $rollbackPath $ExpectedSHA256 $ExpectedLength)
            $rollbackValid = $true
            [void] (Assert-SourceIdentity $input $candidateHash $candidateLength)
            $final = Read-GzipJson $input
            [void] (Get-RepairContext $final.Root $PassengerPosition $false)
            if (-not [System.Text.Json.Nodes.JsonNode]::DeepEquals(
                    $candidate.Root, $final.Root)) {
                throw 'Final JSON differs from the validated candidate.'
            }
        }
        catch {
            $failure = $_
            if ($replaced) {
                try {
                    Restore-Original $input $rollbackPath $rollbackValid $backup `
                        $ExpectedSHA256 $ExpectedLength
                }
                catch {
                    throw "Repair failed and atomic restore also failed: " +
                        "$($failure.Exception.Message); $($_.Exception.Message)"
                }
                throw "Repair failed; original restored: $($failure.Exception.Message)"
            }
            throw
        }
        Remove-Item -LiteralPath $rollbackPath -Force
        $resultMessage = "Applied: $summary backup='$backup' backupHash=$inputHash"
    }
    else {
        $resultMessage = 'Apply cancelled by ShouldProcess; source unchanged.'
    }
}
finally {
    if ($candidateCreated -and (Test-Path -LiteralPath $candidatePath)) {
        Remove-Item -LiteralPath $candidatePath -Force
    }
}

if (-not $Apply) {
    Assert-GameNotRunning
    [void] (Assert-SourceIdentity $input $ExpectedSHA256 $ExpectedLength)
    if (Test-Path -LiteralPath $backup) { throw 'Dry-run unexpectedly created BackupPath.' }
    if (Test-Path -LiteralPath $candidatePath) { throw 'Dry-run candidate cleanup failed.' }
}
Write-Output $resultMessage
