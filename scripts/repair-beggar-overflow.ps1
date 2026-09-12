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

$ExpectedCampaign = 1
$ExpectedLand = 7
$ExpectedSerializedSaveDataVersion = 16
$ExpectedCampaignCount = 2
$ExpectedInitialObjectCount = 2194
$ExpectedBeggarCount = 158
$ExpectedGroupCounts = @(136, 22)
$CampX = @(-120.0, 70.0)
$ExpectedBeggarCampPrefab = 'Prefabs/Buildings and Interactive/greece/Beggar Camp_greece'
$CampPositionTolerance = 0.001
$KeepPerCamp = 5
$ExpectedRemoved = 148

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
    $actualLength = (Get-Item -LiteralPath $Path).Length
    if ($actualLength -ne $Length) {
        throw "Source length=$actualLength, expected $Length."
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
            try {
                $text = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }
        }
        finally {
            $gzip.Dispose()
        }
    }
    finally {
        $file.Dispose()
    }

    $root = [System.Text.Json.Nodes.JsonNode]::Parse($text)
    if ($root -isnot [System.Text.Json.Nodes.JsonObject]) {
        throw "Save root is not a JSON object."
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

function Get-RequiredDouble {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Object,
        [Parameter(Mandatory = $true)][string] $Name
    )
    try { (Get-RequiredNode $Object $Name).GetValue[double]() }
    catch { throw "Property '$Name' is not numeric." }
}

function Get-RequiredString {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Object,
        [Parameter(Mandatory = $true)][string] $Name
    )
    try { (Get-RequiredNode $Object $Name).GetValue[string]() }
    catch { throw "Property '$Name' is not a string." }
}

function Assert-RequiredFalse {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Object,
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Context
    )
    try { $value = (Get-RequiredNode $Object $Name).GetValue[bool]() }
    catch { throw "$Context property '$Name' is not Boolean." }
    if ($value) { throw "$Context property '$Name' must be false." }
}

function Get-CurrentIsland {
    param([Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Root)

    $currentCampaign = Get-RequiredInt $Root '_currentCampaign'
    if ($currentCampaign -ne $ExpectedCampaign) {
        throw "Refusing save: currentCampaign=$currentCampaign, expected $ExpectedCampaign."
    }

    $campaigns = Get-RequiredArray $Root 'campaigns'
    if ($campaigns.Count -le $ExpectedCampaign -or
        $campaigns[$ExpectedCampaign] -isnot [System.Text.Json.Nodes.JsonObject]) {
        throw "Campaign index $ExpectedCampaign is missing."
    }
    $campaign = [System.Text.Json.Nodes.JsonObject] $campaigns[$ExpectedCampaign]
    $currentLand = Get-RequiredInt $campaign 'currentLand'
    if ($currentLand -ne $ExpectedLand) {
        throw "Refusing save: currentLand=$currentLand, expected $ExpectedLand."
    }

    $islands = Get-RequiredArray $campaign '_islands'
    $matches = [System.Collections.Generic.List[object]]::new()
    for ($islandIndex = 0; $islandIndex -lt $islands.Count; $islandIndex++) {
        if ($islands[$islandIndex] -isnot [System.Text.Json.Nodes.JsonObject]) { continue }
        $candidate = [System.Text.Json.Nodes.JsonObject] $islands[$islandIndex]
        if ((Get-RequiredInt $candidate 'land') -eq $ExpectedLand) {
            $matches.Add([pscustomobject]@{ Index = $islandIndex; Island = $candidate })
        }
    }
    if ($matches.Count -ne 1) {
        throw "Expected exactly one island with land=$ExpectedLand; found $($matches.Count)."
    }
    $script:ResolvedIslandIndex = $matches[0].Index
    $island = [System.Text.Json.Nodes.JsonObject] $matches[0].Island
    Write-Output -NoEnumerate $island
}

function Assert-SaveFingerprint {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Root,
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Island,
        [Parameter(Mandatory = $true)][int] $ExpectedObjectCount
    )

    $version = Get-RequiredInt $Root 'serializedSaveDataVersion'
    if ($version -ne $ExpectedSerializedSaveDataVersion) {
        throw "serializedSaveDataVersion=$version, expected $ExpectedSerializedSaveDataVersion."
    }
    $campaigns = Get-RequiredArray $Root 'campaigns'
    if ($campaigns.Count -ne $ExpectedCampaignCount) {
        throw "campaigns.Count=$($campaigns.Count), expected $ExpectedCampaignCount."
    }

    $objects = Get-RequiredArray $Island 'objects'
    if ($objects.Count -ne $ExpectedObjectCount) {
        throw "Land $ExpectedLand objects.Count=$($objects.Count), expected $ExpectedObjectCount."
    }

    $campPositions = [System.Collections.Generic.List[double]]::new()
    foreach ($objectNode in $objects) {
        if ($objectNode -isnot [System.Text.Json.Nodes.JsonObject]) { continue }
        $object = [System.Text.Json.Nodes.JsonObject] $objectNode
        if ((Get-RequiredString $object 'prefabPath') -ne $ExpectedBeggarCampPrefab) {
            continue
        }
        $position = Get-RequiredObject $object 'localPosition'
        $x = Get-RequiredDouble $position 'x'
        if ([double]::IsNaN($x) -or [double]::IsInfinity($x)) {
            throw 'Greek BeggarCamp has a non-finite x position.'
        }
        $campPositions.Add($x)
    }
    if ($campPositions.Count -ne $CampX.Count) {
        throw (
            "Greek BeggarCamp count=$($campPositions.Count), expected exactly " +
            "$($CampX.Count) for prefab '$ExpectedBeggarCampPrefab'.")
    }
    $actual = @($campPositions | Sort-Object)
    $expected = @($CampX | Sort-Object)
    for ($i = 0; $i -lt $expected.Count; $i++) {
        if ([Math]::Abs($actual[$i] - $expected[$i]) -gt $CampPositionTolerance) {
            throw (
                "Greek BeggarCamp[$i] x=$($actual[$i]), expected $($expected[$i]) " +
                "+/- $CampPositionTolerance.")
        }
    }
}

function Get-ComponentByType {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Object,
        [Parameter(Mandatory = $true)][string] $Type
    )

    $components = Get-RequiredArray $Object 'componentData2'
    $matches = [System.Collections.Generic.List[System.Text.Json.Nodes.JsonObject]]::new()
    foreach ($componentNode in $components) {
        if ($componentNode -isnot [System.Text.Json.Nodes.JsonObject]) { continue }
        $component = [System.Text.Json.Nodes.JsonObject] $componentNode
        if ((Get-RequiredString $component 'type') -eq $Type) {
            $matches.Add($component)
        }
    }
    $matches
}

function Parse-ComponentData {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Component,
        [Parameter(Mandatory = $true)][string] $Context
    )
    $data = Get-RequiredString $Component 'data'
    try { $parsed = [System.Text.Json.Nodes.JsonNode]::Parse($data) }
    catch { throw "$Context contains invalid nested JSON." }
    if ($parsed -isnot [System.Text.Json.Nodes.JsonObject]) {
        throw "$Context nested data is not an object."
    }
    Write-Output -NoEnumerate $parsed
}

function Get-ValidatedBeggars {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Island,
        [Parameter(Mandatory = $true)][int] $ExpectedCount,
        [Parameter(Mandatory = $true)][int[]] $ExpectedGroups
    )

    $objects = Get-RequiredArray $Island 'objects'
    $beggars = [System.Collections.Generic.List[object]]::new()
    $allObjectIds = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)

    for ($index = 0; $index -lt $objects.Count; $index++) {
        if ($objects[$index] -isnot [System.Text.Json.Nodes.JsonObject]) {
            throw "objects[$index] is not an object."
        }
        $object = [System.Text.Json.Nodes.JsonObject] $objects[$index]
        $uniqueId = Get-RequiredString $object 'uniqueID'
        if ([string]::IsNullOrEmpty($uniqueId) -or -not $allObjectIds.Add($uniqueId)) {
            throw "Current-island object uniqueID is empty or duplicated: '$uniqueId'."
        }

        $beggarComponents = @(Get-ComponentByType $object 'BeggarData')
        if ($beggarComponents.Count -eq 0) { continue }
        if ($beggarComponents.Count -ne 1) {
            throw "Object '$uniqueId' has $($beggarComponents.Count) BeggarData components."
        }
        if ((Get-RequiredString $object 'prefabPath') -ne 'Prefabs/Characters/Beggar') {
            throw "BeggarData object '$uniqueId' has an unexpected prefabPath."
        }

        $characterComponents = @(Get-ComponentByType $object 'CharacterData')
        if ($characterComponents.Count -ne 1) {
            throw "Beggar '$uniqueId' must have exactly one CharacterData component."
        }

        $beggarData = Parse-ComponentData $beggarComponents[0] "Beggar '$uniqueId' BeggarData"
        Assert-RequiredFalse $beggarData 'settler' "Beggar '$uniqueId'"
        Assert-RequiredFalse $beggarData 'despawnOnLoad' "Beggar '$uniqueId'"
        $baker = Get-RequiredObject $beggarData 'baker'
        if ((Get-RequiredString $baker 'linkedObjectID') -ne '') {
            throw "Beggar '$uniqueId' has a non-empty baker link."
        }

        $characterData = Parse-ComponentData $characterComponents[0] "Beggar '$uniqueId' CharacterData"
        Assert-RequiredFalse $characterData 'inert' "Beggar '$uniqueId'"
        Assert-RequiredFalse $characterData 'isGrabbed' "Beggar '$uniqueId'"

        $position = Get-RequiredObject $object 'localPosition'
        $x = Get-RequiredDouble $position 'x'
        if ([double]::IsNaN($x) -or [double]::IsInfinity($x)) {
            throw "Beggar '$uniqueId' has invalid x=$x."
        }
        $netId = Get-RequiredInt $object 'netID'
        $leftDistance = [Math]::Abs($x - $CampX[0])
        $rightDistance = [Math]::Abs($x - $CampX[1])
        $campIndex = if ($leftDistance -le $rightDistance) { 0 } else { 1 }
        $distance = if ($campIndex -eq 0) { $leftDistance } else { $rightDistance }

        $beggars.Add([pscustomobject]@{
            Index = $index
            Node = $object
            UniqueID = $uniqueId
            NetID = $netId
            X = $x
            CampIndex = $campIndex
            Distance = $distance
        })
    }

    if ($beggars.Count -ne $ExpectedCount) {
        throw "BeggarData count=$($beggars.Count), expected $ExpectedCount."
    }
    $counts = @(
        @($beggars | Where-Object CampIndex -EQ 0).Count,
        @($beggars | Where-Object CampIndex -EQ 1).Count)
    if ($counts[0] -ne $ExpectedGroups[0] -or $counts[1] -ne $ExpectedGroups[1]) {
        throw "Beggar grouping=$($counts[0])/$($counts[1]), expected $($ExpectedGroups[0])/$($ExpectedGroups[1])."
    }

    [pscustomobject]@{
        Objects = $objects
        Beggars = $beggars
        GroupCounts = $counts
        AllObjectIds = $allObjectIds
    }
}

function Get-IslandObjectCounts {
    param([Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Root)

    $counts = [System.Collections.Generic.Dictionary[string, int]]::new(
        [System.StringComparer]::Ordinal)
    $campaigns = Get-RequiredArray $Root 'campaigns'
    for ($campaignIndex = 0; $campaignIndex -lt $campaigns.Count; $campaignIndex++) {
        if ($campaigns[$campaignIndex] -isnot [System.Text.Json.Nodes.JsonObject]) { continue }
        $campaign = [System.Text.Json.Nodes.JsonObject] $campaigns[$campaignIndex]
        $islands = Get-RequiredArray $campaign '_islands'
        for ($islandIndex = 0; $islandIndex -lt $islands.Count; $islandIndex++) {
            if ($islands[$islandIndex] -isnot [System.Text.Json.Nodes.JsonObject]) { continue }
            $island = [System.Text.Json.Nodes.JsonObject] $islands[$islandIndex]
            $objects = Get-RequiredArray $island 'objects'
            $counts["$campaignIndex/$islandIndex"] = $objects.Count
        }
    }
    [pscustomobject]@{ Map = $counts }
}

function Assert-IslandObjectCounts {
    param(
        [Parameter(Mandatory = $true)] $Before,
        [Parameter(Mandatory = $true)] $After,
        [Parameter(Mandatory = $true)][int] $TargetIslandIndex
    )
    if ($null -eq $Before -or $null -eq $After -or
        $null -eq $Before.PSObject.Properties['Map'] -or
        $null -eq $After.PSObject.Properties['Map']) {
        throw 'Island count result is missing its Map wrapper.'
    }
    $beforeMap = $Before.Map
    $afterMap = $After.Map
    if ($beforeMap -isnot [System.Collections.Generic.Dictionary[string, int]] -or
        $afterMap -isnot [System.Collections.Generic.Dictionary[string, int]]) {
        throw 'Island count wrapper does not contain the expected dictionary type.'
    }
    if ($beforeMap.Count -ne $afterMap.Count) { throw 'Island count map changed.' }
    foreach ($entry in $beforeMap.GetEnumerator()) {
        if (-not $afterMap.ContainsKey($entry.Key)) {
            throw "Island '$($entry.Key)' disappeared."
        }
        $expected = if ($entry.Key -eq "$ExpectedCampaign/$TargetIslandIndex") {
            $entry.Value - $ExpectedRemoved
        }
        else { $entry.Value }
        if ($afterMap[$entry.Key] -ne $expected) {
            throw "Island '$($entry.Key)' object count=$($afterMap[$entry.Key]), expected $expected."
        }
    }
}

function Assert-OnlyExpectedObjectRemoval {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $BaselineRoot,
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $CandidateRoot,
        [Parameter(Mandatory = $true)] $RemovedIds,
        [Parameter(Mandatory = $true)][int] $TargetIslandIndex
    )

    $baselineCampaigns = Get-RequiredArray $BaselineRoot 'campaigns'
    $candidateCampaigns = Get-RequiredArray $CandidateRoot 'campaigns'
    $baselineCampaign = [System.Text.Json.Nodes.JsonObject] $baselineCampaigns[$ExpectedCampaign]
    $candidateCampaign = [System.Text.Json.Nodes.JsonObject] $candidateCampaigns[$ExpectedCampaign]
    $baselineIslands = Get-RequiredArray $baselineCampaign '_islands'
    $candidateIslands = Get-RequiredArray $candidateCampaign '_islands'
    $baselineIsland = [System.Text.Json.Nodes.JsonObject] $baselineIslands[$TargetIslandIndex]
    $candidateIsland = [System.Text.Json.Nodes.JsonObject] $candidateIslands[$TargetIslandIndex]
    if ((Get-RequiredInt $baselineIsland 'land') -ne $ExpectedLand -or
        (Get-RequiredInt $candidateIsland 'land') -ne $ExpectedLand) {
        throw 'Target island slot changed during candidate generation.'
    }

    $baselineObjects = Get-RequiredArray $baselineIsland 'objects'
    $candidateObjects = Get-RequiredArray $candidateIsland 'objects'
    $candidateIndex = 0
    foreach ($baselineNode in $baselineObjects) {
        if ($baselineNode -isnot [System.Text.Json.Nodes.JsonObject]) {
            throw 'Baseline target island contains a non-object entry.'
        }
        $baselineObject = [System.Text.Json.Nodes.JsonObject] $baselineNode
        $uniqueId = Get-RequiredString $baselineObject 'uniqueID'
        if ($RemovedIds.Contains($uniqueId)) { continue }
        if ($candidateIndex -ge $candidateObjects.Count) {
            throw 'Candidate target island ended before all survivors were checked.'
        }
        if (-not [System.Text.Json.Nodes.JsonNode]::DeepEquals(
                $baselineObject,
                $candidateObjects[$candidateIndex])) {
            throw "Surviving object content/order changed at uniqueID '$uniqueId'."
        }
        $candidateIndex++
    }
    if ($candidateIndex -ne $candidateObjects.Count) {
        throw 'Candidate target island contains unexpected additional objects.'
    }

    $baselineWithoutObjects = [System.Text.Json.Nodes.JsonObject] $BaselineRoot.DeepClone()
    $candidateWithoutObjects = [System.Text.Json.Nodes.JsonObject] $CandidateRoot.DeepClone()
    $baselineCampaignsClone = Get-RequiredArray $baselineWithoutObjects 'campaigns'
    $candidateCampaignsClone = Get-RequiredArray $candidateWithoutObjects 'campaigns'
    $baselineCampaignClone = [System.Text.Json.Nodes.JsonObject] (
        $baselineCampaignsClone[$ExpectedCampaign])
    $candidateCampaignClone = [System.Text.Json.Nodes.JsonObject] (
        $candidateCampaignsClone[$ExpectedCampaign])
    $baselineIslandsClone = Get-RequiredArray $baselineCampaignClone '_islands'
    $candidateIslandsClone = Get-RequiredArray $candidateCampaignClone '_islands'
    $baselineIslandClone = [System.Text.Json.Nodes.JsonObject] (
        $baselineIslandsClone[$TargetIslandIndex])
    $candidateIslandClone = [System.Text.Json.Nodes.JsonObject] (
        $candidateIslandsClone[$TargetIslandIndex])
    $baselineIslandClone['objects'] = [System.Text.Json.Nodes.JsonArray]::new()
    $candidateIslandClone['objects'] = [System.Text.Json.Nodes.JsonArray]::new()
    if (-not [System.Text.Json.Nodes.JsonNode]::DeepEquals(
            $baselineWithoutObjects,
            $candidateWithoutObjects)) {
        throw 'JSON outside the target island object array changed.'
    }
}

function Get-OrdinalOccurrenceCount {
    param(
        [Parameter(Mandatory = $true)][string] $Text,
        [Parameter(Mandatory = $true)][string] $Needle
    )
    $count = 0
    $offset = 0
    while ($offset -lt $Text.Length) {
        $found = $Text.IndexOf($Needle, $offset, [System.StringComparison]::Ordinal)
        if ($found -lt 0) { break }
        $count++
        $offset = $found + $Needle.Length
    }
    $count
}

function Assert-SameStringSet {
    param(
        [Parameter(Mandatory = $true)] $Expected,
        [Parameter(Mandatory = $true)] $Actual,
        [Parameter(Mandatory = $true)][string] $Label
    )
    if ($Expected.Count -ne $Actual.Count) {
        throw "$Label count changed from $($Expected.Count) to $($Actual.Count)."
    }
    foreach ($value in $Expected) {
        if (-not $Actual.Contains($value)) { throw "$Label lost '$value'." }
    }
}

function Write-GzipJson {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.Nodes.JsonObject] $Root,
        [Parameter(Mandatory = $true)][string] $Path
    )
    $options = [System.Text.Json.JsonSerializerOptions]::new()
    $options.WriteIndented = $false
    $json = $Root.ToJsonString($options)
    $bytes = [System.Text.UTF8Encoding]::new($false, $true).GetBytes($json)

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

function Copy-VerifiedBackup {
    param(
        [Parameter(Mandatory = $true)][string] $Source,
        [Parameter(Mandatory = $true)][string] $Destination,
        [Parameter(Mandatory = $true)][string] $ExpectedHash,
        [Parameter(Mandatory = $true)][long] $ExpectedFileLength
    )
    $sourceStream = $null
    $destinationStream = $null
    try {
        $sourceStream = [System.IO.File]::OpenRead($Source)
        $destinationStream = [System.IO.File]::Open(
            $Destination,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        $sourceStream.CopyTo($destinationStream)
        $destinationStream.Flush($true)
    }
    catch {
        if ($destinationStream) { $destinationStream.Dispose(); $destinationStream = $null }
        if ($sourceStream) { $sourceStream.Dispose(); $sourceStream = $null }
        if (Test-Path -LiteralPath $Destination -PathType Leaf) {
            Remove-Item -LiteralPath $Destination -Force
        }
        throw
    }
    finally {
        if ($destinationStream) { $destinationStream.Dispose() }
        if ($sourceStream) { $sourceStream.Dispose() }
    }

    $backupLength = (Get-Item -LiteralPath $Destination).Length
    $backupHash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
    if ($backupLength -ne $ExpectedFileLength -or $backupHash -ne $ExpectedHash) {
        Remove-Item -LiteralPath $Destination -Force
        throw (
            "Backup verification failed: length=$backupLength hash=$backupHash, " +
            "expected length=$ExpectedFileLength hash=$ExpectedHash.")
    }
    $backupHash
}

function Restore-OriginalSave {
    param(
        [Parameter(Mandatory = $true)][string] $Input,
        [Parameter(Mandatory = $true)][string] $Rollback,
        [Parameter(Mandatory = $true)][bool] $RollbackVerified,
        [Parameter(Mandatory = $true)][string] $VerifiedBackup,
        [Parameter(Mandatory = $true)][string] $OriginalHash,
        [Parameter(Mandatory = $true)][long] $OriginalLength
    )

    $directory = [System.IO.Path]::GetDirectoryName($Input)
    $failedReplacementPath = [System.IO.Path]::Combine(
        $directory,
        ([System.IO.Path]::GetFileName($Input) + '.beggar-repair.failed.' +
            [Guid]::NewGuid().ToString('N') + '.tmp'))

    if ($RollbackVerified) {
        try {
            [System.IO.File]::Replace($Rollback, $Input, $failedReplacementPath, $false)
            [void] (Assert-SourceIdentity $Input $OriginalHash $OriginalLength)
            if (Test-Path -LiteralPath $failedReplacementPath -PathType Leaf) {
                Remove-Item -LiteralPath $failedReplacementPath -Force
            }
            return
        }
        catch {
            # The separately verified operator backup remains the final recovery path.
        }
    }

    [void] (Assert-SourceIdentity $VerifiedBackup $OriginalHash $OriginalLength)
    $restoreSourcePath = [System.IO.Path]::Combine(
        $directory,
        ([System.IO.Path]::GetFileName($Input) + '.beggar-repair.restore.' +
            [Guid]::NewGuid().ToString('N') + '.tmp'))
    [void] (Copy-VerifiedBackup `
        $VerifiedBackup `
        $restoreSourcePath `
        $OriginalHash `
        $OriginalLength)

    $failedBackupPath = [System.IO.Path]::Combine(
        $directory,
        ([System.IO.Path]::GetFileName($Input) + '.beggar-repair.failed-backup.' +
            [Guid]::NewGuid().ToString('N') + '.tmp'))
    [System.IO.File]::Replace($restoreSourcePath, $Input, $failedBackupPath, $false)
    [void] (Assert-SourceIdentity $Input $OriginalHash $OriginalLength)
    if (Test-Path -LiteralPath $failedBackupPath -PathType Leaf) {
        Remove-Item -LiteralPath $failedBackupPath -Force
    }
}

$resolvedInput = (Resolve-Path -LiteralPath $InputPath).ProviderPath
$resolvedBackup = [System.IO.Path]::GetFullPath($BackupPath)
if ([string]::Equals($resolvedInput, $resolvedBackup, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'BackupPath must not equal InputPath.'
}
if (Test-Path -LiteralPath $resolvedBackup) {
    throw "BackupPath already exists: $resolvedBackup"
}
$backupDirectory = [System.IO.Path]::GetDirectoryName($resolvedBackup)
if ([string]::IsNullOrEmpty($backupDirectory) -or
    -not (Test-Path -LiteralPath $backupDirectory -PathType Container)) {
    throw "BackupPath parent directory does not exist: $backupDirectory"
}

$inputDirectory = [System.IO.Path]::GetDirectoryName($resolvedInput)
$candidateDirectory = if ($Apply) {
    # Apply requires a same-volume source for atomic File.Replace.
    $inputDirectory
}
else {
    # Validation-only mode never needs to write beside the live save.
    [System.IO.Path]::GetTempPath()
}
$temporaryPath = [System.IO.Path]::Combine(
    $candidateDirectory,
    ([System.IO.Path]::GetFileName($resolvedInput) + '.beggar-repair.' +
        [Guid]::NewGuid().ToString('N') + '.tmp'))

$rollbackPath = [System.IO.Path]::Combine(
    $inputDirectory,
    ([System.IO.Path]::GetFileName($resolvedInput) + '.beggar-repair.rollback.' +
        [Guid]::NewGuid().ToString('N') + '.tmp'))

Assert-GameNotRunning
$inputHash = Assert-SourceIdentity `
    $resolvedInput `
    $ExpectedSHA256 `
    $ExpectedLength
$temporaryCreated = $false
try {
    $source = Read-GzipJson $resolvedInput
    $baselineRoot = [System.Text.Json.Nodes.JsonObject] $source.Root.DeepClone()
    $sourceIsland = Get-CurrentIsland $source.Root
    $sourceIslandIndex = $script:ResolvedIslandIndex
    Assert-SaveFingerprint $source.Root $sourceIsland $ExpectedInitialObjectCount
    $before = Get-ValidatedBeggars $sourceIsland $ExpectedBeggarCount $ExpectedGroupCounts
    $beforeIslandCounts = Get-IslandObjectCounts $source.Root

    $keepIds = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    for ($campIndex = 0; $campIndex -lt $CampX.Count; $campIndex++) {
        $ordered = @($before.Beggars |
            Where-Object CampIndex -EQ $campIndex |
            Sort-Object Distance, NetID, UniqueID)
        for ($i = 0; $i -lt $KeepPerCamp; $i++) {
            [void] $keepIds.Add($ordered[$i].UniqueID)
        }
    }

    $remove = @($before.Beggars | Where-Object { -not $keepIds.Contains($_.UniqueID) })
    if ($remove.Count -ne $ExpectedRemoved) {
        throw "Removal count=$($remove.Count), expected $ExpectedRemoved."
    }
    $removeIds = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    foreach ($beggar in $remove) {
        if (-not $removeIds.Add($beggar.UniqueID)) {
            throw "Duplicate removal uniqueID '$($beggar.UniqueID)'."
        }
        $jsonStringLiteral = [System.Text.Json.JsonSerializer]::Serialize(
            [object] [string] $beggar.UniqueID,
            [string],
            [System.Text.Json.JsonSerializerOptions]::new())
        $occurrences = Get-OrdinalOccurrenceCount $source.Text $jsonStringLiteral
        if ($occurrences -ne 1) {
            throw "Removal uniqueID '$($beggar.UniqueID)' occurs $occurrences times in original JSON."
        }
    }

    $nonTargetIdsBefore = [System.Collections.Generic.HashSet[string]]::new(
        $before.AllObjectIds,
        [System.StringComparer]::Ordinal)
    $nonTargetIdsBefore.ExceptWith($removeIds)

    foreach ($beggar in ($remove | Sort-Object Index -Descending)) {
        $before.Objects.RemoveAt($beggar.Index)
    }

    Write-GzipJson $source.Root $temporaryPath
    $temporaryCreated = $true
    $repairedHash = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash

    # Re-open the compressed candidate. Nothing reaches the live save until every invariant
    # below succeeds.
    $candidate = Read-GzipJson $temporaryPath
    $candidateIsland = Get-CurrentIsland $candidate.Root
    if ($script:ResolvedIslandIndex -ne $sourceIslandIndex) {
        throw "Target island index changed from $sourceIslandIndex to $script:ResolvedIslandIndex."
    }
    Assert-SaveFingerprint `
        $candidate.Root `
        $candidateIsland `
        ($ExpectedInitialObjectCount - $ExpectedRemoved)
    $after = Get-ValidatedBeggars $candidateIsland 10 @(5, 5)
    $afterIslandCounts = Get-IslandObjectCounts $candidate.Root
    Assert-IslandObjectCounts $beforeIslandCounts $afterIslandCounts $sourceIslandIndex

    $afterBeggarIds = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    foreach ($beggar in $after.Beggars) { [void] $afterBeggarIds.Add($beggar.UniqueID) }
    Assert-SameStringSet $keepIds $afterBeggarIds 'Retained Beggar uniqueID set'
    foreach ($removedId in $removeIds) {
        if ($after.AllObjectIds.Contains($removedId)) {
            throw "Removed uniqueID '$removedId' remains in candidate."
        }
    }

    $nonTargetIdsAfter = [System.Collections.Generic.HashSet[string]]::new(
        $after.AllObjectIds,
        [System.StringComparer]::Ordinal)
    Assert-SameStringSet $nonTargetIdsBefore $nonTargetIdsAfter 'Surviving object uniqueID set'
    Assert-OnlyExpectedObjectRemoval `
        $baselineRoot `
        $candidate.Root `
        $removeIds `
        $sourceIslandIndex

    if (-not $Apply) {
        Write-Output "Validated only: before=158 removed=148 after=10 groups=5/5 inputHash=$inputHash candidateHash=$repairedHash"
        return
    }

    if (-not $PSCmdlet.ShouldProcess(
            $resolvedInput,
            "Back up and atomically replace save after removing $ExpectedRemoved Beggar objects")) {
        Write-Output "Apply requested but declined: original save unchanged; inputHash=$inputHash candidateHash=$repairedHash"
        return
    }

    # Close the race between validation and replacement. If the game or any other
    # writer touched the save, abort before creating a backup or replacing anything.
    Assert-GameNotRunning
    [void] (Assert-SourceIdentity `
        $resolvedInput `
        $ExpectedSHA256 `
        $ExpectedLength)
    $backupHash = Copy-VerifiedBackup `
        $resolvedInput `
        $resolvedBackup `
        $inputHash `
        $ExpectedLength

    # The backup copy closes its source handle before File.Replace. Recheck at the
    # last possible point so a concurrent writer cannot silently replace a newer save.
    Assert-GameNotRunning
    [void] (Assert-SourceIdentity `
        $resolvedInput `
        $ExpectedSHA256 `
        $ExpectedLength)

    $replacementCompleted = $false
    $rollbackVerified = $false
    try {
        [System.IO.File]::Replace(
            $temporaryPath,
            $resolvedInput,
            $rollbackPath,
            $false)
        $replacementCompleted = $true
        $temporaryCreated = $false

        # The rollback is not trusted merely because File.Replace created it.
        # Validate it before it can participate in recovery.
        [void] (Assert-SourceIdentity `
            $rollbackPath `
            $ExpectedSHA256 `
            $ExpectedLength)
        $rollbackVerified = $true

        $finalHash = (Get-FileHash -LiteralPath $resolvedInput -Algorithm SHA256).Hash
        if ($finalHash -ne $repairedHash) {
            throw "Final save hash mismatch: got $finalHash, expected $repairedHash."
        }

        # Final gzip/JSON re-read detects truncation and repeats all semantic checks.
        $final = Read-GzipJson $resolvedInput
        $finalIsland = Get-CurrentIsland $final.Root
        if ($script:ResolvedIslandIndex -ne $sourceIslandIndex) {
            throw 'Final target island identity changed.'
        }
        Assert-SaveFingerprint `
            $final.Root `
            $finalIsland `
            ($ExpectedInitialObjectCount - $ExpectedRemoved)
        $finalBeggars = Get-ValidatedBeggars $finalIsland 10 @(5, 5)
        if (-not [System.Text.Json.Nodes.JsonNode]::DeepEquals(
                $candidate.Root,
                $final.Root)) {
            throw 'Final save content differs from the fully validated candidate.'
        }
    }
    catch {
        $replacementError = $_
        if ($replacementCompleted) {
            try {
                Restore-OriginalSave `
                    $resolvedInput `
                    $rollbackPath `
                    $rollbackVerified `
                    $resolvedBackup `
                    $ExpectedSHA256 `
                    $ExpectedLength
            }
            catch {
                throw (
                    "Post-replacement validation failed and atomic rollback also failed. " +
                    "Repair error: $($replacementError.Exception.Message) Rollback error: " +
                    "$($_.Exception.Message) Rollback file, if present: '$rollbackPath'.")
            }
            $restoreSource = if ($rollbackVerified) {
                'the verified rollback'
            }
            else {
                'the verified backup'
            }
            throw (
                "Post-replacement validation failed; the original save was atomically " +
                "restored from $restoreSource" +
                ". Repair error: $($replacementError.Exception.Message)")
        }
        throw
    }

    if (Test-Path -LiteralPath $rollbackPath -PathType Leaf) {
        Remove-Item -LiteralPath $rollbackPath -Force
    }

    Write-Output (
        "before=158 removed=148 after=$($finalBeggars.Beggars.Count) groups=5/5 " +
        "inputHash=$inputHash backupHash=$backupHash outputHash=$finalHash backup='$resolvedBackup'")
}
finally {
    if ($temporaryCreated -and (Test-Path -LiteralPath $temporaryPath -PathType Leaf)) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}
