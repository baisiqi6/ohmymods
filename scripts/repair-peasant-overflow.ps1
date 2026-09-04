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

$ExpectedVersion = 16
$ApprovedInputSHA256 = '2C681C5C2CA01E6BBCBB5F05BDEA32FC63A0D86EA563F68325D12C08D088F87A'
$ApprovedInputLength = 748730
$ExpectedCampaign = 1
$ExpectedCampaignCount = 2
$ExpectedLand = 7
$ExpectedObjectsBefore = 2046
$ExpectedObjectsAfter = 1696
$ExpectedWorkerCount = 14
$ExpectedPeasantsBefore = 733
$ExpectedPeasantsAfter = 383
$ExpectedGreekBefore = 638
$ExpectedGreekAfter = 288
$ExpectedNorse = 95
$ExpectedBeggars = 10
$ExpectedCandidates = 383
$KeepCandidates = 33
$ExpectedRemoved = 350
$ExpectedCreateOrderMin = 20264
$ExpectedCreateOrderMax = 21335
$GreekPeasantPrefab = 'Prefabs/Characters/Peasant'
$NorsePeasantPrefab = 'Prefabs/Characters/norselands/Peasant_norselands'
$BeggarPrefab = 'Prefabs/Characters/Beggar'
$ExpectedComponents = [ordered]@{
    'CharacterData' = 'Character'
    'DamageableData' = 'Damageable'
    'GenderSelectorSaveData' = 'GenderAnimatorSelector'
    'PetrifiableSaveData' = 'Petrifiable'
    'WalletData' = 'Wallet'
}
$ExpectedCurrencies = @(
    'Candle', 'Coins', 'Crown', 'Egg', 'Gems', 'Merchandise', 'Shades', 'Skulls')
$CampX = @(-120.0, 70.0)

function Assert-GameNotRunning {
    if (Get-Process -Name 'KingdomTwoCrowns' -ErrorAction SilentlyContinue) {
        throw 'KingdomTwoCrowns.exe is running.'
    }
}

function Assert-SourceIdentity {
    param([string] $Path, [string] $Hash, [long] $Length)
    $actualLength = (Get-Item -LiteralPath $Path).Length
    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actualLength -ne $Length -or $actualHash -ne $Hash.ToUpperInvariant()) {
        throw "Source identity changed: length=$actualLength hash=$actualHash."
    }
    $actualHash
}

function Read-GzipJson {
    param([string] $Path)
    $file = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $gzip = [IO.Compression.GZipStream]::new($file, [IO.Compression.CompressionMode]::Decompress)
        try {
            $reader = [IO.StreamReader]::new($gzip, [Text.UTF8Encoding]::new($false, $true))
            try { $text = $reader.ReadToEnd() }
            finally { $reader.Dispose() }
        }
        finally { $gzip.Dispose() }
    }
    finally { $file.Dispose() }
    $root = [Text.Json.Nodes.JsonNode]::Parse($text)
    if ($root -isnot [Text.Json.Nodes.JsonObject]) { throw 'Save root is not a JSON object.' }
    [pscustomobject]@{ Text = $text; Root = $root }
}

function Get-Node {
    param([Text.Json.Nodes.JsonObject] $Object, [string] $Name)
    if (-not $Object.ContainsKey($Name) -or $null -eq $Object[$Name]) {
        throw "Missing property '$Name'."
    }
    Write-Output -NoEnumerate $Object[$Name]
}

function Get-Object {
    param([Text.Json.Nodes.JsonObject] $Object, [string] $Name)
    $node = Get-Node $Object $Name
    if ($node -isnot [Text.Json.Nodes.JsonObject]) { throw "'$Name' is not an object." }
    Write-Output -NoEnumerate $node
}

function Get-Array {
    param([Text.Json.Nodes.JsonObject] $Object, [string] $Name)
    $node = Get-Node $Object $Name
    if ($node -isnot [Text.Json.Nodes.JsonArray]) { throw "'$Name' is not an array." }
    Write-Output -NoEnumerate $node
}

function Get-Int {
    param([Text.Json.Nodes.JsonObject] $Object, [string] $Name)
    try { (Get-Node $Object $Name).GetValue[int]() }
    catch { throw "'$Name' is not Int32." }
}

function Get-String {
    param([Text.Json.Nodes.JsonObject] $Object, [string] $Name)
    try { (Get-Node $Object $Name).GetValue[string]() }
    catch { throw "'$Name' is not a string." }
}

function Get-Bool {
    param([Text.Json.Nodes.JsonObject] $Object, [string] $Name)
    try { (Get-Node $Object $Name).GetValue[bool]() }
    catch { throw "'$Name' is not Boolean." }
}

function Get-Double {
    param([Text.Json.Nodes.JsonObject] $Object, [string] $Name)
    try { (Get-Node $Object $Name).GetValue[double]() }
    catch { throw "'$Name' is not numeric." }
}

function Parse-Data {
    param([Text.Json.Nodes.JsonObject] $Component)
    $node = [Text.Json.Nodes.JsonNode]::Parse((Get-String $Component 'data'))
    if ($node -isnot [Text.Json.Nodes.JsonObject]) { throw 'Component data is not an object.' }
    Write-Output -NoEnumerate $node
}

function Get-Components {
    param([Text.Json.Nodes.JsonObject] $Object)
    $result = [Collections.Generic.Dictionary[string, Text.Json.Nodes.JsonObject]]::new(
        [StringComparer]::Ordinal)
    foreach ($node in (Get-Array $Object 'componentData2')) {
        if ($node -isnot [Text.Json.Nodes.JsonObject]) { throw 'componentData2 contains non-object.' }
        $component = [Text.Json.Nodes.JsonObject] $node
        $type = Get-String $component 'type'
        if (-not $result.TryAdd($type, $component)) { throw "Duplicate component '$type'." }
    }
    [pscustomobject]@{ Map = $result }
}

function Get-CurrentIsland {
    param([Text.Json.Nodes.JsonObject] $Root)
    if ((Get-Int $Root 'serializedSaveDataVersion') -ne $ExpectedVersion) { throw 'Save version mismatch.' }
    if ((Get-Int $Root '_currentCampaign') -ne $ExpectedCampaign) { throw 'Current campaign mismatch.' }
    $campaigns = Get-Array $Root 'campaigns'
    if ($campaigns.Count -ne $ExpectedCampaignCount) { throw 'Campaign count mismatch.' }
    $campaign = [Text.Json.Nodes.JsonObject] $campaigns[$ExpectedCampaign]
    if ((Get-Int $campaign 'currentLand') -ne $ExpectedLand) { throw 'Current land mismatch.' }
    $matches = [Collections.Generic.List[object]]::new()
    $islands = Get-Array $campaign '_islands'
    for ($i = 0; $i -lt $islands.Count; $i++) {
        if ($islands[$i] -isnot [Text.Json.Nodes.JsonObject]) { continue }
        $island = [Text.Json.Nodes.JsonObject] $islands[$i]
        if ((Get-Int $island 'land') -eq $ExpectedLand) {
            $matches.Add([pscustomobject]@{ Index = $i; Island = $island })
        }
    }
    if ($matches.Count -ne 1) { throw "Expected one land $ExpectedLand; found $($matches.Count)." }
    [pscustomobject]@{ Index = $matches[0].Index; Island = $matches[0].Island }
}

function Test-AllCurrencyZero {
    param([Text.Json.Nodes.JsonObject] $Wallet)
    if ((Get-Int $Wallet 'coins') -ne 0 -or (Get-Int $Wallet 'gems') -ne 0) { return $false }
    if (-not (Get-Bool $Wallet 'usesCurrencySystem')) { return $false }
    $currency = Get-Object $Wallet 'currency'
    if ($currency.Count -ne $ExpectedCurrencies.Count) { return $false }
    foreach ($name in $ExpectedCurrencies) {
        if (-not $currency.ContainsKey($name)) { return $false }
    }
    foreach ($entry in $currency) {
        try { $value = $entry.Value.GetValue[int]() }
        catch { return $false }
        if ($value -ne 0) { return $false }
    }
    return $true
}

function Test-StandardGreekPeasant {
    param([Text.Json.Nodes.JsonObject] $Object)
    if ((Get-String $Object 'prefabPath') -ne $GreekPeasantPrefab) { return $false }
    if ((Get-String (Get-Object $Object 'parentObject') 'linkedObjectID') -ne '') { return $false }
    if ((Get-String $Object 'hierarchyPath') -ne 'Level/GameLayer/' -or
        (Get-Int $Object 'mode') -ne 0 -or
        (Get-Int $Object 'linkOrder') -ne 0 -or
        (Get-Int $Object 'decayHint') -ne 0 -or
        (Get-Int $Object 'decayResistanceDays') -ne -1 -or
        (Get-String $Object 'decayedVersionPrefabPath') -ne '' -or
        (Get-Int $Object 'crpcType') -ne 1 -or
        (Get-Int $Object 'netID') -le 0 -or
        (Get-Int $Object 'createOrder') -le 0) {
        return $false
    }
    $components = (Get-Components $Object).Map
    if ($components.Count -ne $ExpectedComponents.Count) { return $false }
    foreach ($entry in $ExpectedComponents.GetEnumerator()) {
        if (-not $components.ContainsKey($entry.Key) -or
            (Get-String $components[$entry.Key] 'name') -ne $entry.Value) {
            return $false
        }
    }
    $character = Parse-Data $components['CharacterData']
    if ((Get-Bool $character 'isGrabbed') -or (Get-Bool $character 'inert')) { return $false }
    $petrifiable = Parse-Data $components['PetrifiableSaveData']
    $remainingDuration = Get-Double $petrifiable 'RemainingDuration'
    if ((Get-Bool $petrifiable 'IsPetrified') -or
        (Get-Int $petrifiable 'RemainingHP') -ne 0 -or
        [double]::IsNaN($remainingDuration) -or [double]::IsInfinity($remainingDuration)) {
        return $false
    }
    $damageable = Parse-Data $components['DamageableData']
    if ((Get-Int $damageable 'hitPoints') -ne 0 -or (Get-Bool $damageable 'invulnerable')) {
        return $false
    }
    $gender = Parse-Data $components['GenderSelectorSaveData']
    [void] (Get-Bool $gender 'IsFemale')
    return (Test-AllCurrencyZero (Parse-Data $components['WalletData']))
}

function Get-Audit {
    param(
        [Text.Json.Nodes.JsonObject] $Root,
        [Text.Json.Nodes.JsonObject] $Island,
        [string] $RawText,
        [int] $ExpectedObjectCount,
        [int] $ExpectedGreek,
        [int] $ExpectedPeasants,
        [int] $ExpectedCandidateCount,
        [bool] $RequireReferenceAudit
    )
    $objects = Get-Array $Island 'objects'
    if ($objects.Count -ne $ExpectedObjectCount) { throw "objects=$($objects.Count), expected $ExpectedObjectCount." }
    $allIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $networkIdentityCounts = [Collections.Generic.Dictionary[string, int]]::new(
        [StringComparer]::Ordinal)
    $candidates = [Collections.Generic.List[object]]::new()
    $workerCount = 0
    $greekCount = 0
    $norseCount = 0
    $beggars = [Collections.Generic.List[double]]::new()
    for ($i = 0; $i -lt $objects.Count; $i++) {
        if ($objects[$i] -isnot [Text.Json.Nodes.JsonObject]) { throw "objects[$i] is not an object." }
        $object = [Text.Json.Nodes.JsonObject] $objects[$i]
        $id = Get-String $object 'uniqueID'
        if ([string]::IsNullOrEmpty($id) -or -not $allIds.Add($id)) { throw "Invalid uniqueID '$id'." }
        $netId = Get-Int $object 'netID'
        $crpcType = Get-Int $object 'crpcType'
        $networkKey = "$crpcType/$netId"
        if ($networkIdentityCounts.ContainsKey($networkKey)) { $networkIdentityCounts[$networkKey]++ }
        else { $networkIdentityCounts[$networkKey] = 1 }
        $prefab = Get-String $object 'prefabPath'
        if ($prefab -eq $GreekPeasantPrefab) { $greekCount++ }
        if ($prefab -eq $NorsePeasantPrefab) { $norseCount++ }
        $components = (Get-Components $object).Map
        if ($components.ContainsKey('WorkerData')) { $workerCount++ }
        if ($prefab -eq $BeggarPrefab -and $components.ContainsKey('BeggarData')) {
            $position = Get-Object $object 'localPosition'
            $beggars.Add((Get-Double $position 'x'))
        }
        if (Test-StandardGreekPeasant $object) {
            $candidates.Add([pscustomobject]@{
                Index = $i
                Node = $object
                UniqueID = $id
                NetID = $netId
                CrpcType = $crpcType
                CreateOrder = Get-Int $object 'createOrder'
                X = Get-Double (Get-Object $object 'localPosition') 'x'
            })
        }
    }
    $peasantCount = $greekCount + $norseCount
    if ($workerCount -ne $ExpectedWorkerCount -or $greekCount -ne $ExpectedGreek -or
        $norseCount -ne $ExpectedNorse -or $peasantCount -ne $ExpectedPeasants -or
        $beggars.Count -ne $ExpectedBeggars -or $candidates.Count -ne $ExpectedCandidateCount) {
        throw "Count mismatch: Worker=$workerCount Peasant=$peasantCount Greek=$greekCount Norse=$norseCount Beggar=$($beggars.Count) candidates=$($candidates.Count)."
    }
    $candidateNetIds = [Collections.Generic.HashSet[int]]::new()
    foreach ($candidate in $candidates) {
        $networkKey = "$($candidate.CrpcType)/$($candidate.NetID)"
        if ($candidate.NetID -le 0 -or -not $candidateNetIds.Add($candidate.NetID) -or
            $networkIdentityCounts[$networkKey] -ne 1) {
            throw "Candidate '$($candidate.UniqueID)' has unsafe netID=$($candidate.NetID)."
        }
    }
    $beggarGroups = @(0, 0)
    foreach ($x in $beggars) {
        if ([Math]::Abs($x - $CampX[0]) -le [Math]::Abs($x - $CampX[1])) { $beggarGroups[0]++ }
        else { $beggarGroups[1]++ }
    }
    if ($beggarGroups[0] -ne 5 -or $beggarGroups[1] -ne 5) { throw 'Beggar grouping is not 5/5.' }

    if ($RequireReferenceAudit -and $candidates.Count -gt 0) {
        $pattern = [string]::Join('|', @(
            $candidates | Sort-Object { $_.UniqueID.Length } -Descending |
                ForEach-Object { [regex]::Escape($_.UniqueID) }))
        $occurrences = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
        foreach ($candidate in $candidates) { $occurrences[$candidate.UniqueID] = 0 }
        foreach ($match in [regex]::Matches($RawText, $pattern)) { $occurrences[$match.Value]++ }
        foreach ($entry in $occurrences.GetEnumerator()) {
            if ($entry.Value -ne 1) { throw "Candidate '$($entry.Key)' occurs $($entry.Value) times." }
        }
    }
    [pscustomobject]@{
        Objects = $objects
        Candidates = $candidates
        AllIds = $allIds
        Worker = $workerCount
        Peasant = $peasantCount
        Greek = $greekCount
        Norse = $norseCount
        Beggar = $beggars.Count
    }
}

function Write-GzipJson {
    param([Text.Json.Nodes.JsonObject] $Root, [string] $Path)
    $bytes = [Text.UTF8Encoding]::new($false, $true).GetBytes(
        $Root.ToJsonString([Text.Json.JsonSerializerOptions]::new()))
    $file = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $gzip = [IO.Compression.GZipStream]::new($file, [IO.Compression.CompressionLevel]::Optimal, $true)
        try { $gzip.Write($bytes, 0, $bytes.Length) }
        finally { $gzip.Dispose() }
        $file.Flush($true)
    }
    finally { $file.Dispose() }
}

function Copy-Verified {
    param([string] $Source, [string] $Destination, [string] $Hash, [long] $Length)
    $sourceStream = $null
    $destinationStream = $null
    try {
        $sourceStream = [IO.File]::OpenRead($Source)
        $destinationStream = [IO.File]::Open(
            $Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $sourceStream.CopyTo($destinationStream)
        $destinationStream.Flush($true)
    }
    catch {
        if ($destinationStream) { $destinationStream.Dispose(); $destinationStream = $null }
        if ($sourceStream) { $sourceStream.Dispose(); $sourceStream = $null }
        if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Force }
        throw
    }
    finally {
        if ($destinationStream) { $destinationStream.Dispose() }
        if ($sourceStream) { $sourceStream.Dispose() }
    }
    try { [void] (Assert-SourceIdentity $Destination $Hash $Length) }
    catch {
        Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue
        throw
    }
}

function Restore-Original {
    param([string] $Input, [string] $Rollback, [bool] $RollbackValid,
        [string] $Backup, [string] $Hash, [long] $Length)
    $directory = [IO.Path]::GetDirectoryName($Input)
    if ($RollbackValid) {
        try {
            $failed = [IO.Path]::Combine($directory, ([IO.Path]::GetFileName($Input) + '.peasant.failed.' + [guid]::NewGuid().ToString('N') + '.tmp'))
            [IO.File]::Replace($Rollback, $Input, $failed, $false)
            [void] (Assert-SourceIdentity $Input $Hash $Length)
            Remove-Item -LiteralPath $failed -Force -ErrorAction SilentlyContinue
            return
        }
        catch { }
    }
    [void] (Assert-SourceIdentity $Backup $Hash $Length)
    $restore = [IO.Path]::Combine($directory, ([IO.Path]::GetFileName($Input) + '.peasant.restore.' + [guid]::NewGuid().ToString('N') + '.tmp'))
    Copy-Verified $Backup $restore $Hash $Length
    $failed = [IO.Path]::Combine($directory, ([IO.Path]::GetFileName($Input) + '.peasant.failed-backup.' + [guid]::NewGuid().ToString('N') + '.tmp'))
    [IO.File]::Replace($restore, $Input, $failed, $false)
    [void] (Assert-SourceIdentity $Input $Hash $Length)
    Remove-Item -LiteralPath $failed -Force -ErrorAction SilentlyContinue
}

function Assert-FilteredClone {
    param([Text.Json.Nodes.JsonObject] $BeforeRoot, [Text.Json.Nodes.JsonObject] $AfterRoot,
        [int] $IslandIndex, $RemovedIds)
    $beforeIsland = [Text.Json.Nodes.JsonObject] (Get-Array ([Text.Json.Nodes.JsonObject] (Get-Array $BeforeRoot 'campaigns')[$ExpectedCampaign]) '_islands')[$IslandIndex]
    $afterIsland = [Text.Json.Nodes.JsonObject] (Get-Array ([Text.Json.Nodes.JsonObject] (Get-Array $AfterRoot 'campaigns')[$ExpectedCampaign]) '_islands')[$IslandIndex]
    $beforeObjects = Get-Array $beforeIsland 'objects'
    $afterObjects = Get-Array $afterIsland 'objects'
    $afterIndex = 0
    foreach ($node in $beforeObjects) {
        $object = [Text.Json.Nodes.JsonObject] $node
        if ($RemovedIds.Contains((Get-String $object 'uniqueID'))) { continue }
        if ($afterIndex -ge $afterObjects.Count -or
            -not [Text.Json.Nodes.JsonNode]::DeepEquals($object, $afterObjects[$afterIndex])) {
            throw "Survivor order/content changed at index $afterIndex."
        }
        $afterIndex++
    }
    if ($afterIndex -ne $afterObjects.Count) { throw 'Candidate contains unexpected objects.' }
    $beforeClone = [Text.Json.Nodes.JsonObject] $BeforeRoot.DeepClone()
    $afterClone = [Text.Json.Nodes.JsonObject] $AfterRoot.DeepClone()
    $beforeCloneIsland = [Text.Json.Nodes.JsonObject] (Get-Array ([Text.Json.Nodes.JsonObject] (Get-Array $beforeClone 'campaigns')[$ExpectedCampaign]) '_islands')[$IslandIndex]
    $afterCloneIsland = [Text.Json.Nodes.JsonObject] (Get-Array ([Text.Json.Nodes.JsonObject] (Get-Array $afterClone 'campaigns')[$ExpectedCampaign]) '_islands')[$IslandIndex]
    $beforeCloneIsland['objects'] = [Text.Json.Nodes.JsonArray]::new()
    $afterCloneIsland['objects'] = [Text.Json.Nodes.JsonArray]::new()
    if (-not [Text.Json.Nodes.JsonNode]::DeepEquals($beforeClone, $afterClone)) {
        throw 'JSON outside target objects changed.'
    }
}

$input = (Resolve-Path -LiteralPath $InputPath).ProviderPath
$backup = [IO.Path]::GetFullPath($BackupPath)
if ($ExpectedSHA256.ToUpperInvariant() -ne $ApprovedInputSHA256 -or
    $ExpectedLength -ne $ApprovedInputLength) {
    throw 'ExpectedSHA256/ExpectedLength do not match this one-time repair input.'
}
if ([string]::Equals($input, $backup, [StringComparison]::OrdinalIgnoreCase)) { throw 'Backup equals input.' }
if (Test-Path -LiteralPath $backup) { throw "Backup already exists: $backup" }
if (-not (Test-Path -LiteralPath ([IO.Path]::GetDirectoryName($backup)) -PathType Container)) { throw 'Backup parent missing.' }
$inputDirectory = [IO.Path]::GetDirectoryName($input)
$candidateDirectory = if ($Apply) { $inputDirectory } else { [IO.Path]::GetTempPath() }
$temporary = [IO.Path]::Combine($candidateDirectory, ([IO.Path]::GetFileName($input) + '.peasant-repair.' + [guid]::NewGuid().ToString('N') + '.tmp'))
$rollback = [IO.Path]::Combine($inputDirectory, ([IO.Path]::GetFileName($input) + '.peasant-rollback.' + [guid]::NewGuid().ToString('N') + '.tmp'))

Assert-GameNotRunning
$inputHash = Assert-SourceIdentity $input $ExpectedSHA256 $ExpectedLength
$temporaryCreated = $false
try {
    $source = Read-GzipJson $input
    $baseline = [Text.Json.Nodes.JsonObject] $source.Root.DeepClone()
    $location = Get-CurrentIsland $source.Root
    $before = Get-Audit $source.Root $location.Island $source.Text $ExpectedObjectsBefore $ExpectedGreekBefore $ExpectedPeasantsBefore $ExpectedCandidates $true
    $createOrders = [Collections.Generic.HashSet[int]]::new()
    foreach ($candidate in $before.Candidates) {
        if (-not $createOrders.Add($candidate.CreateOrder)) { throw 'Candidate createOrder is not unique.' }
    }
    $orders = @($before.Candidates | ForEach-Object CreateOrder)
    if (($orders | Measure-Object -Minimum).Minimum -ne $ExpectedCreateOrderMin -or
        ($orders | Measure-Object -Maximum).Maximum -ne $ExpectedCreateOrderMax) {
        throw 'Candidate createOrder range mismatch.'
    }
    $before.Candidates.Sort([Comparison[object]] {
        param($a, $b)
        $comparison = $a.CreateOrder.CompareTo($b.CreateOrder)
        if ($comparison -ne 0) { return $comparison }
        return [StringComparer]::Ordinal.Compare($a.UniqueID, $b.UniqueID)
    })
    $keepIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for ($i = 0; $i -lt $KeepCandidates; $i++) { [void] $keepIds.Add($before.Candidates[$i].UniqueID) }
    $remove = @($before.Candidates | Select-Object -Skip $KeepCandidates)
    if ($remove.Count -ne $ExpectedRemoved) { throw 'Removal count mismatch.' }
    $kept = @($before.Candidates | Select-Object -First $KeepCandidates)
    $keptLeft = @($kept | Where-Object X -LT 0).Count
    $keptRight = @($kept | Where-Object X -GT 0).Count
    $removedLeft = @($remove | Where-Object X -LT 0).Count
    $removedRight = @($remove | Where-Object X -GT 0).Count
    if ($keptLeft -ne 17 -or $keptRight -ne 16 -or
        $removedLeft -ne 182 -or $removedRight -ne 168) {
        throw "Side distribution mismatch: kept=$keptLeft/$keptRight removed=$removedLeft/$removedRight."
    }
    $removeIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($candidate in $remove) { [void] $removeIds.Add($candidate.UniqueID) }
    foreach ($candidate in ($remove | Sort-Object Index -Descending)) { $before.Objects.RemoveAt($candidate.Index) }

    Write-GzipJson $source.Root $temporary
    $temporaryCreated = $true
    $candidateHash = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash
    $candidate = Read-GzipJson $temporary
    $candidateLocation = Get-CurrentIsland $candidate.Root
    if ($candidateLocation.Index -ne $location.Index) { throw 'Island index changed.' }
    $after = Get-Audit $candidate.Root $candidateLocation.Island $candidate.Text $ExpectedObjectsAfter $ExpectedGreekAfter $ExpectedPeasantsAfter $KeepCandidates $true
    Assert-FilteredClone $baseline $candidate.Root $location.Index $removeIds
    foreach ($id in $keepIds) { if (-not $after.AllIds.Contains($id)) { throw "Kept ID '$id' missing." } }
    foreach ($id in $removeIds) { if ($after.AllIds.Contains($id)) { throw "Removed ID '$id' remains." } }

    Assert-GameNotRunning
    [void] (Assert-SourceIdentity $input $ExpectedSHA256 $ExpectedLength)
    $summary = "Worker=14 Peasant=733->383 Greek=638->288 Norse=95 removed=350 Beggar=10 groups=5/5 inputHash=$inputHash candidateHash=$candidateHash"
    if (-not $Apply) { Write-Output "Validated only: $summary"; return }
    if (-not $PSCmdlet.ShouldProcess($input, 'Back up and atomically apply Peasant repair')) { return }

    Assert-GameNotRunning
    [void] (Assert-SourceIdentity $input $ExpectedSHA256 $ExpectedLength)
    Copy-Verified $input $backup $ExpectedSHA256 $ExpectedLength
    Assert-GameNotRunning
    [void] (Assert-SourceIdentity $input $ExpectedSHA256 $ExpectedLength)
    $replaced = $false
    $rollbackValid = $false
    try {
        [IO.File]::Replace($temporary, $input, $rollback, $false)
        $replaced = $true
        $temporaryCreated = $false
        [void] (Assert-SourceIdentity $rollback $ExpectedSHA256 $ExpectedLength)
        $rollbackValid = $true
        if ((Get-FileHash -LiteralPath $input -Algorithm SHA256).Hash -ne $candidateHash) { throw 'Output hash mismatch.' }
        $final = Read-GzipJson $input
        if (-not [Text.Json.Nodes.JsonNode]::DeepEquals($candidate.Root, $final.Root)) { throw 'Final JSON differs from candidate.' }
    }
    catch {
        $failure = $_
        if ($replaced) {
            try { Restore-Original $input $rollback $rollbackValid $backup $ExpectedSHA256 $ExpectedLength }
            catch { throw "Repair failed and restore failed: $($failure.Exception.Message); $($_.Exception.Message)" }
            throw "Repair failed; original restored: $($failure.Exception.Message)"
        }
        throw
    }
    Remove-Item -LiteralPath $rollback -Force
    Write-Output "Applied: $summary backupHash=$inputHash backup='$backup'"
}
finally {
    if ($temporaryCreated -and (Test-Path -LiteralPath $temporary)) { Remove-Item -LiteralPath $temporary -Force }
}
