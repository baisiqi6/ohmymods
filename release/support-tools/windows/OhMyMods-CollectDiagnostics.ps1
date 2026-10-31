param(
    [string]$GameRoot,
    [string]$UserDataRoot,
    [string]$OutputRoot,
    [switch]$NoOpen,
    [int]$MaxFileBytes = 134217728,
    [int]$MaxTotalBytes = 536870912,
    [int]$MaxFiles = 4096,
    [int]$MaxDepth = 16
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.IO.Compression

$toolName = 'OhMyMods-CollectDiagnostics'
$toolVersion = '1.0.0'
$startedUtc = [DateTime]::UtcNow
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$utf8Bom = New-Object System.Text.UTF8Encoding($true)

function Write-Info([string]$Text) {
    Write-Host $Text
}

function Write-Fail([string]$Text) {
    Write-Host ('ERROR: ' + $Text)
}

function Get-NormalizedFullPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($full)
    while ($full.Length -gt $root.Length -and ($full.EndsWith('\') -or $full.EndsWith('/'))) {
        $full = $full.Substring(0, $full.Length - 1)
    }
    return $full
}

function Test-PathSameOrUnder([string]$Child, [string]$Parent) {
    $childFull = Get-NormalizedFullPath $Child
    $parentFull = Get-NormalizedFullPath $Parent
    if ($childFull.Equals($parentFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }
    $prefix = $parentFull
    if (-not $prefix.EndsWith('\') -and -not $prefix.EndsWith('/')) {
        $prefix = $prefix + '\'
    }
    return $childFull.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
}

function Join-Relative([string]$Base, [string]$Name) {
    if ([string]::IsNullOrEmpty($Base)) {
        return $Name
    }
    return $Base + '/' + $Name
}

function Join-SourcePath([string]$Root, [string]$Relative) {
    $combined = [IO.Path]::Combine($Root, ($Relative -replace '/', '\'))
    $full = Get-NormalizedFullPath $combined
    if (-not (Test-PathSameOrUnder $full $Root)) {
        throw 'path-escape'
    }
    return $full
}

function Test-ReparseOnPath([string]$Root, [string]$Relative) {
    $current = Get-NormalizedFullPath $Root
    foreach ($segment in ($Relative -split '[\\/]')) {
        if ([string]::IsNullOrEmpty($segment)) {
            continue
        }
        if ($segment -eq '.' -or $segment -eq '..') {
            throw 'path-escape'
        }
        $current = [IO.Path]::Combine($current, $segment)
        if (-not (Test-Path -LiteralPath $current)) {
            return $false
        }
        $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            return $true
        }
    }
    return $false
}

function ConvertTo-Hex([byte[]]$Bytes) {
    return (($Bytes | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Get-BytesSha256([byte[]]$Bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($Bytes)
    }
    finally {
        $sha.Dispose()
    }
    return (ConvertTo-Hex $hash)
}

function Add-Error {
    param($State, [string]$Stage, [string]$Area, [string]$RelativePath, [string]$Type, [string]$Code)
    [void]$State.Errors.Add([pscustomobject][ordered]@{
        stage = $Stage
        area = $Area
        relativePath = $RelativePath
        type = $Type
        code = $Code
    })
    $State.Partial = $true
}

function Add-Missing {
    param($State, [string]$Category, [string]$Area, [string]$RelativePath, [bool]$Required, [string]$Code)
    [void]$State.Missing.Add([pscustomobject][ordered]@{
        category = $Category
        area = $Area
        relativePath = $RelativePath
        required = $Required
        code = $Code
    })
    if ($Required) {
        $State.Partial = $true
    }
}

function Add-Skip {
    param($State, [string]$Area, [string]$Category, [string]$SourceRelativePath, [string]$Status, $Info)
    $size = $null
    $before = $null
    if ($null -ne $Info) {
        try {
            if (($Info.Attributes -band [IO.FileAttributes]::Directory) -eq 0) {
                $size = [long]$Info.Length
            }
            $before = $Info.LastWriteTimeUtc.ToString('o')
        }
        catch {
        }
    }
    [void]$State.Entries.Add([pscustomobject][ordered]@{
        area = $Area
        category = $Category
        sourceRelativePath = $SourceRelativePath
        zipPath = $null
        status = $Status
        size = $size
        sha256 = $null
        beforeLastWriteUtc = $before
        afterLastWriteUtc = $null
        changed = $false
    })
    $State.Partial = $true
}

function Test-FileBudget {
    param($State, [string]$Area, [string]$Category, [string]$SourceRelativePath, $Info)
    if ($State.StopEnumeration) {
        Add-Skip $State $Area $Category $SourceRelativePath 'skipped-count' $Info
        return $false
    }
    if ($State.FilesSeen -ge [int]$State.Limits.MaxFiles) {
        Add-Skip $State $Area $Category $SourceRelativePath 'skipped-count' $Info
        $State.StopEnumeration = $true
        return $false
    }
    $State.FilesSeen = $State.FilesSeen + 1
    return $true
}


function Get-FileMetadata {
    param($State, [string]$FullPath, $Info)
    $beforeSize = [long]$Info.Length
    $beforeUtc = $Info.LastWriteTimeUtc.ToString('o')
    $remaining = [long]$State.Limits.MaxTotalBytes - [long]$State.ReadBytes
    if ($beforeSize -gt [long]$State.Limits.MaxFileBytes -or $beforeSize -gt $remaining) {
        throw 'metadata-read-limit'
    }
    $limit = [Math]::Min([long]$State.Limits.MaxFileBytes, $remaining)
    $share = [IO.FileShare]([int][IO.FileShare]::ReadWrite -bor [int][IO.FileShare]::Delete)
    $stream = $null
    $sha = [Security.Cryptography.SHA256]::Create()
    $readBytes = [long]0
    try {
        $stream = New-Object System.IO.FileStream($FullPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, $share, 81920, [IO.FileOptions]::SequentialScan)
        $buffer = New-Object byte[] 81920
        while ($readBytes -lt $limit) {
            $ask = [int][Math]::Min([long]$buffer.Length, $limit - $readBytes)
            $count = $stream.Read($buffer, 0, $ask)
            if ($count -eq 0) { break }
            $readBytes += $count
            $State.ReadBytes += $count
            $State.MetadataBytes += $count
            [void]$sha.TransformBlock($buffer, 0, $count, $buffer, 0)
        }
        if ($stream.Length -gt $readBytes) { throw 'metadata-read-limit' }
        [void]$sha.TransformFinalBlock((New-Object byte[] 0), 0, 0)
        $hash = ConvertTo-Hex $sha.Hash
    }
    finally {
        if ($null -ne $stream) { $stream.Dispose() }
        $sha.Dispose()
    }
    $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($FullPath)
    $version = [string]$versionInfo.FileVersion
    if ([string]::IsNullOrWhiteSpace($version)) { $version = [string]$versionInfo.ProductVersion }
    $afterUtc = $null
    $afterSize = $null
    try {
        $after = Get-Item -LiteralPath $FullPath -Force -ErrorAction Stop
        $afterUtc = $after.LastWriteTimeUtc.ToString('o')
        $afterSize = [long]$after.Length
    } catch { }
    $changed = ($null -eq $afterUtc -or $afterUtc -ne $beforeUtc -or $beforeSize -ne $readBytes -or $afterSize -ne $readBytes)
    if ($changed) { $State.Changed = $true; $State.Partial = $true }
    return [pscustomobject]@{
        Version = ($version -replace '[\r\n]+', ' ').Trim()
        Size = $readBytes; Sha256 = $hash
        BeforeSize = $beforeSize; AfterSize = $afterSize
        BeforeUtc = $beforeUtc; AfterUtc = $afterUtc; Changed = [bool]$changed
    }
}


function Copy-SourceToStaging {
    param($State, [string]$FullPath, $Info)
    $beforeSize = [long]$Info.Length
    $beforeUtc = $Info.LastWriteTimeUtc.ToString('o')
    $stagePath = Join-Path $State.StageDir ('{0:D6}.tmp' -f $State.StageIndex)
    $State.StageIndex++
    $src = $null; $dst = $null; $sha = $null
    $written = [long]0
    $skipStatus = $null; $errorType = $null; $errorCode = $null; $hashHex = $null
    try {
        $share = [IO.FileShare]([int][IO.FileShare]::ReadWrite -bor [int][IO.FileShare]::Delete)
        $src = New-Object System.IO.FileStream($FullPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, $share, 81920, [IO.FileOptions]::SequentialScan)
        $dst = New-Object System.IO.FileStream($stagePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $sha = [Security.Cryptography.SHA256]::Create()
        $buffer = New-Object byte[] 81920
        while ($true) {
            $fileRemaining = [long]$State.Limits.MaxFileBytes - $written
            $totalRemaining = [long]$State.Limits.MaxTotalBytes - [long]$State.ReadBytes
            $ask = [int][Math]::Min([long]$buffer.Length, [Math]::Min($fileRemaining, $totalRemaining))
            if ($ask -le 0) {
                if ($src.Length -gt $written) {
                    $skipStatus = $(if ($totalRemaining -le 0) { 'skipped-total' } else { 'skipped-size' })
                }
                break
            }
            $read = $src.Read($buffer, 0, $ask)
            if ($read -eq 0) { break }
            $State.ReadBytes += $read
            [void]$sha.TransformBlock($buffer, 0, $read, $buffer, 0)
            $dst.Write($buffer, 0, $read)
            $written += $read
        }
        if ($null -eq $skipStatus) {
            [void]$sha.TransformFinalBlock((New-Object byte[] 0), 0, 0)
            $dst.Flush()
            $hashHex = ConvertTo-Hex $sha.Hash
        }
    }
    catch { $errorType = $_.Exception.GetBaseException().GetType().Name; $errorCode = 'file-read-failed' }
    finally {
        if ($null -ne $src) { try { $src.Dispose() } catch { } }
        if ($null -ne $dst) { try { $dst.Dispose() } catch { } }
        if ($null -ne $sha) { try { $sha.Dispose() } catch { } }
    }
    if ($null -ne $skipStatus -or $null -ne $errorCode) {
        if ([IO.File]::Exists($stagePath)) { try { [IO.File]::Delete($stagePath) } catch { } }
        return [pscustomobject]@{
            Outcome = $(if ($null -ne $errorCode) { 'error' } else { 'skipped' })
            Status = $skipStatus; ErrorType = $errorType; ErrorCode = $errorCode; StagePath = $null
        }
    }
    $afterUtc = $null; $afterLength = $null
    try {
        $afterInfo = Get-Item -LiteralPath $FullPath -Force -ErrorAction Stop
        $afterUtc = $afterInfo.LastWriteTimeUtc.ToString('o')
        $afterLength = [long]$afterInfo.Length
    } catch { }
    $changed = ($null -eq $afterUtc -or $afterUtc -ne $beforeUtc -or $beforeSize -ne $written -or $afterLength -ne $written)
    if ($changed) { $State.Changed = $true; $State.Partial = $true }
    return [pscustomobject]@{
        Outcome = 'collected'; Status = $null; ErrorType = $null; ErrorCode = $null
        StagePath = $stagePath; Size = $written; Sha256 = $hashHex
        BeforeSize = $beforeSize; AfterSize = $afterLength
        BeforeUtc = $beforeUtc; AfterUtc = $afterUtc; Changed = [bool]$changed
    }
}

function Add-StageToZip {
    param($State, $Copy, [string]$Area, [string]$Category, [string]$SourceRelativePath, [string]$ZipPath)
    $key = $ZipPath.ToLowerInvariant()
    if ($State.ZipPaths.ContainsKey($key)) {
        throw ('duplicate-zip-path: ' + $ZipPath)
    }
    $entry = $State.Zip.CreateEntry($ZipPath, [System.IO.Compression.CompressionLevel]::Optimal)
    $entryStream = $entry.Open()
    $stageStream = $null
    try {
        $stageStream = New-Object System.IO.FileStream($Copy.StagePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $buffer = New-Object byte[] 81920
        while (($read = $stageStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $entryStream.Write($buffer, 0, $read)
        }
    }
    finally {
        if ($null -ne $stageStream) {
            $stageStream.Dispose()
        }
        $entryStream.Dispose()
    }
    $State.ZipPaths[$key] = $true
    [void]$State.Entries.Add([pscustomobject][ordered]@{
        area = $Area
        category = $Category
        sourceRelativePath = $SourceRelativePath
        zipPath = $ZipPath
        status = 'collected'
        size = [long]$Copy.Size
        beforeSize = $Copy.BeforeSize; afterSize = $Copy.AfterSize
        sha256 = $Copy.Sha256
        beforeLastWriteUtc = $Copy.BeforeUtc
        afterLastWriteUtc = $Copy.AfterUtc
        changed = [bool]$Copy.Changed
    })
    [void]$State.Verify.Add([pscustomobject]@{
        zipPath = $ZipPath
        size = [long]$Copy.Size
        beforeSize = $Copy.BeforeSize; afterSize = $Copy.AfterSize
        sha256 = $Copy.Sha256
    })
    $State.TotalBytes += [long]$Copy.Size
}

function Add-CandidateFile {
    param($State, [string]$Area, [string]$Category, [string]$SourceRelativePath, [string]$ZipPath, $Info)
    if (-not (Test-FileBudget $State $Area $Category $SourceRelativePath $Info)) {
        return
    }
    $attributes = $null
    $length = $null
    try {
        $attributes = $Info.Attributes
        $length = [long]$Info.Length
    }
    catch {
        Add-Error $State 'read' $Area $SourceRelativePath $_.Exception.GetType().Name 'file-metadata-failed'
        return
    }
    if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        Add-Skip $State $Area $Category $SourceRelativePath 'skipped-reparse' $Info
        return
    }
    if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) {
        Add-Error $State 'read' $Area $SourceRelativePath 'DirectoryInfo' 'not-a-file'
        return
    }
    if ($length -gt [long]$State.Limits.MaxFileBytes) {
        Add-Skip $State $Area $Category $SourceRelativePath 'skipped-size' $Info
        return
    }
    if (($State.TotalBytes + $length) -gt [long]$State.Limits.MaxTotalBytes) {
        Add-Skip $State $Area $Category $SourceRelativePath 'skipped-total' $Info
        return
    }
    $copy = Copy-SourceToStaging $State $Info.FullName $Info
    if ($copy.Outcome -eq 'skipped') {
        Add-Skip $State $Area $Category $SourceRelativePath $copy.Status $Info
        return
    }
    if ($copy.Outcome -eq 'error') {
        Add-Error $State 'read' $Area $SourceRelativePath $copy.ErrorType $copy.ErrorCode
        return
    }
    try {
        Add-StageToZip $State $copy $Area $Category $SourceRelativePath $ZipPath
    }
    finally {
        if ([IO.File]::Exists($copy.StagePath)) {
            try { [IO.File]::Delete($copy.StagePath) } catch { }
        }
    }
}

function Add-SourceFile {
    param($State, [string]$AreaRoot, [string]$Area, [string]$Category, [string]$SourceRelativePath, [string]$ZipPath, [bool]$Required)
    $full = $null
    $info = $null
    try {
        if (Test-ReparseOnPath $AreaRoot $SourceRelativePath) {
            Add-Skip $State $Area $Category $SourceRelativePath 'skipped-reparse' $null
            return
        }
        $full = Join-SourcePath $AreaRoot $SourceRelativePath
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
            Add-Missing $State $Category $Area $SourceRelativePath $Required 'missing-file'
            return
        }
        $info = Get-Item -LiteralPath $full -Force -ErrorAction Stop
    }
    catch {
        Add-Error $State 'read' $Area $SourceRelativePath $_.Exception.GetType().Name 'file-open-failed'
        return
    }
    Add-CandidateFile $State $Area $Category $SourceRelativePath $ZipPath $info
}


function Add-TreeDirectory {
    param($State, [string]$DirectoryFull, [string]$AreaRoot, [string]$Area, [string]$Category, [string]$SourceRelativeBase, [string]$ZipBase, [int]$Depth)
    if ($State.StopEnumeration) { return }
    $iterator = $null
    try {
        $directory = New-Object System.IO.DirectoryInfo($DirectoryFull)
        $iterator = $directory.EnumerateFileSystemInfos().GetEnumerator()
        while ($iterator.MoveNext()) {
            if ($State.StopEnumeration) { return }
            if ($State.VisitedEntries -ge (2 * [int]$State.Limits.MaxFiles)) {
                Add-Error $State 'enumerate' $Area $SourceRelativeBase 'EntryLimit' 'entry-limit'
                $State.StopEnumeration = $true
                return
            }
            $State.VisitedEntries++
            $child = $iterator.Current
            $sourceRelative = Join-Relative $SourceRelativeBase $child.Name
            $zipRelative = Join-Relative $ZipBase $child.Name
            try {
                $attributes = $child.Attributes
                if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    Add-Skip $State $Area $Category $sourceRelative 'skipped-reparse' $child
                    continue
                }
                if (-not (Test-PathSameOrUnder $child.FullName $AreaRoot)) {
                    Add-Error $State 'enumerate' $Area $sourceRelative 'PathEscape' 'path-escape'
                    continue
                }
                if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) {
                    if (($Depth + 1) -ge [int]$State.Limits.MaxDepth) {
                        Add-Skip $State $Area $Category $sourceRelative 'skipped-depth' $child
                        continue
                    }
                    Add-TreeDirectory $State $child.FullName $AreaRoot $Area $Category $sourceRelative $zipRelative ($Depth + 1)
                }
                else {
                    if (($Depth + 1) -gt [int]$State.Limits.MaxDepth) {
                        Add-Skip $State $Area $Category $sourceRelative 'skipped-depth' $child
                        continue
                    }
                    $info = Get-Item -LiteralPath $child.FullName -Force -ErrorAction Stop
                    Add-CandidateFile $State $Area $Category $sourceRelative $zipRelative $info
                }
            }
            catch { Add-Error $State 'enumerate' $Area $sourceRelative $_.Exception.GetType().Name 'entry-read-failed' }
        }
    }
    catch { Add-Error $State 'enumerate' $Area $SourceRelativeBase $_.Exception.GetType().Name 'directory-enumeration-failed' }
    finally {
        if ($null -ne $iterator -and $iterator -is [IDisposable]) { $iterator.Dispose() }
    }
}

function Add-SourceTree {
    param($State, [string]$AreaRoot, [string]$Area, [string]$Category, [string]$SourceRelativePath, [string]$ZipPrefix, [bool]$Required)
    $fullRoot = $null
    try {
        if (Test-ReparseOnPath $AreaRoot $SourceRelativePath) {
            Add-Skip $State $Area $Category $SourceRelativePath 'skipped-reparse' $null
            return
        }
        $fullRoot = Join-SourcePath $AreaRoot $SourceRelativePath
        if (-not (Test-Path -LiteralPath $fullRoot)) {
            Add-Missing $State $Category $Area $SourceRelativePath $Required 'missing-directory'
            return
        }
        if (-not (Test-Path -LiteralPath $fullRoot -PathType Container)) {
            Add-Missing $State $Category $Area $SourceRelativePath $Required 'not-a-directory'
            return
        }
    }
    catch {
        Add-Error $State 'read' $Area $SourceRelativePath $_.Exception.GetType().Name 'directory-open-failed'
        return
    }
    Add-TreeDirectory $State $fullRoot $AreaRoot $Area $Category $SourceRelativePath $ZipPrefix 0
}

function New-MetadataRecord {
    param($State, [string]$AreaRoot, [string]$Area, [string]$RelativePath)
    $record = [ordered]@{
        present = $false
        relativePath = $RelativePath
        size = $null
        version = $null
        sha256 = $null
        beforeSize = $null; afterSize = $null
        beforeLastWriteUtc = $null; afterLastWriteUtc = $null
        changed = $false
        code = $null
    }
    try {
        if (Test-ReparseOnPath $AreaRoot $RelativePath) {
            $record.code = 'reparse-skipped'
            Add-Error $State 'metadata' $Area $RelativePath 'ReparsePoint' 'reparse-skipped'
            return [pscustomobject]$record
        }
        $full = Join-SourcePath $AreaRoot $RelativePath
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
            $record.code = 'missing'
            return [pscustomobject]$record
        }
        $info = Get-Item -LiteralPath $full -Force -ErrorAction Stop
        if (($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            $record.code = 'reparse-skipped'
            Add-Error $State 'metadata' $Area $RelativePath 'ReparsePoint' 'reparse-skipped'
            return [pscustomobject]$record
        }
        $record.size = [long]$info.Length
        if ([long]$info.Length -gt [long]$State.Limits.MaxFileBytes) {
            $record.code = 'metadata-size-limit'
            Add-Error $State 'metadata' $Area $RelativePath 'ReadLimit' $record.code
            return [pscustomobject]$record
        }
        if ([long]$info.Length -gt ([long]$State.Limits.MaxTotalBytes - [long]$State.ReadBytes)) {
            $record.code = 'metadata-total-limit'
            Add-Error $State 'metadata' $Area $RelativePath 'ReadLimit' $record.code
            return [pscustomobject]$record
        }
        $metadata = Get-FileMetadata $State $full $info
        $record.present = $true
        $record.size = [long]$metadata.Size
        $record.version = $metadata.Version
        $record.sha256 = $metadata.Sha256
        $record.beforeSize = $metadata.BeforeSize
        $record.afterSize = $metadata.AfterSize
        $record.beforeLastWriteUtc = $metadata.BeforeUtc
        $record.afterLastWriteUtc = $metadata.AfterUtc
        $record.changed = $metadata.Changed
    }
    catch {
        $record.code = $_.Exception.GetType().Name
        Add-Error $State 'metadata' $Area $RelativePath $_.Exception.GetType().Name 'metadata-read-failed'
    }
    return [pscustomobject]$record
}

function Remove-OwnedDirectory {
    param([string]$Path, [string]$AllowedParent)
    if (-not [IO.Directory]::Exists($Path)) {
        return
    }
    try {
        if (-not (Test-PathSameOrUnder $Path $AllowedParent)) {
            return
        }
        $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            return
        }
        [IO.Directory]::Delete($Path, $true)
    }
    catch {
    }
}

function Get-GameRunning {
    param($State)
    try {
        $processes = @(Get-Process -Name KingdomTwoCrowns -ErrorAction SilentlyContinue)
        return ($processes.Count -gt 0)
    }
    catch {
        Add-Error $State 'process' '' '' $_.Exception.GetType().Name 'process-query-failed'
        return $false
    }
}

function Get-RunStatus {
    param($State)
    $live = [bool]($State.GameRunningAtStart -or $State.GameRunningAtEnd -or $State.Changed)
    if ($State.Partial -and $live) {
        return 'partial-live-best-effort'
    }
    if ($State.Partial) {
        return 'partial'
    }
    if ($live) {
        return 'live-best-effort'
    }
    return 'complete'
}

function Get-ReadmeText {
    param([string]$Status, [string]$StartedUtcText, [string]$Version)
    $lines = @(
        'OhMyMods 排查资料包',
        '',
        '这个 ZIP 由 OhMyMods 一键排查资料工具生成。',
        '',
        '包含内容：',
        '- BepInEx 日志（LogOutput.log 及同目录轮转日志）',
        '- Unity Player.log / Player-prev.log',
        '- BepInEx 配置（BepInEx.cfg、KingdomEnhancedMod.cfg）',
        '- BepInEx/config/KingdomEnhancedMod 整个目录（含 ModSave）',
        '- Unity Release 存档目录整个树',
        '- 游戏根目录的构建/发布清单文件（如果存在）',
        '- manifest.json（采集状态、文件 size/SHA256 与限制）',
        '',
        '请把这个 ZIP 私下发送给维护者；本工具不会自动上传，也不要把 ZIP 发到公开渠道。',
        '日志、配置和存档可能包含 Windows 账号名、路径、游戏内名称等个人信息；本工具不承诺匿名化。',
        '建议先完全退出游戏再采集，这样资料更一致；游戏运行中也可以采集，但只是 live-best-effort，文件可能在采集期间变化，不能当作同一保存点的原子快照。',
        '本工具只读取上述固定位置，不修改或删除存档/配置，不联网、不读取凭据、不关闭游戏，也不改变执行策略。',
        '',
        ('工具版本：' + $Version),
        ('采集开始（UTC）：' + $StartedUtcText),
        ('采集状态：' + $Status),
        '详细结果见 ZIP 内的 manifest.json。'
    )
    return (($lines -join "`r`n") + "`r`n")
}

function Add-ZipBytes {
    param($State, [string]$Name, [byte[]]$Bytes)
    $key = $Name.ToLowerInvariant()
    if ($State.ZipPaths.ContainsKey($key)) {
        throw ('duplicate-zip-path: ' + $Name)
    }
    $entry = $State.Zip.CreateEntry($Name, [System.IO.Compression.CompressionLevel]::Optimal)
    $stream = $entry.Open()
    try {
        $stream.Write($Bytes, 0, $Bytes.Length)
    }
    finally {
        $stream.Dispose()
    }
    $State.ZipPaths[$key] = $true
}

function Resolve-RootPath {
    param([string]$Path, [string]$Name, [bool]$MustExist)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw ($Name + ' is empty')
    }
    if (-not [IO.Path]::IsPathRooted($Path)) {
        throw ($Name + ' must be an absolute path')
    }
    if ($Path -match '^[A-Za-z]:$' -or $Path -match '^[A-Za-z]:[^\\/]') {
        throw ($Name + ' must be a fully qualified absolute path')
    }
    $full = Get-NormalizedFullPath $Path
    $ancestor = $full
    while (-not [string]::IsNullOrEmpty($ancestor)) {
        if (Test-Path -LiteralPath $ancestor) {
            $ancestorInfo = Get-Item -LiteralPath $ancestor -Force -ErrorAction Stop
            if (($ancestorInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw ($Name + ' has a reparse point in its path; use the physical directory path')
            }
        }
        $parent = [IO.Path]::GetDirectoryName($ancestor)
        if ($parent -eq $ancestor) { break }
        $ancestor = $parent
    }
    if ([IO.File]::Exists($full)) {
        throw ($Name + ' is a file, not a directory')
    }
    if ($MustExist -and -not [IO.Directory]::Exists($full)) {
        throw ($Name + ' does not exist')
    }
    if ([IO.Directory]::Exists($full)) {
        $item = Get-Item -LiteralPath $full -Force -ErrorAction Stop
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw ($Name + ' is a reparse point')
        }
    }
    return $full
}

$fatal = $null
$success = $false
$exitCode = 1
$runDir = $null
$tempZip = $null
$finalZip = $null
$zipStream = $null
$zip = $null
$state = $null

try {
    $rootParameterNames = @('GameRoot', 'UserDataRoot', 'OutputRoot')
    $specifiedRoots = @()
    foreach ($name in $rootParameterNames) {
        if ($PSBoundParameters.ContainsKey($name)) {
            $specifiedRoots += $name
        }
    }
    if ($specifiedRoots.Count -gt 0 -and $specifiedRoots.Count -lt 3) {
        throw 'GameRoot, UserDataRoot and OutputRoot must be specified together. No default location was read.'
    }

    if ($MaxFileBytes -lt 1 -or $MaxFileBytes -gt 134217728) {
        throw 'MaxFileBytes must be between 1 and 134217728'
    }
    if ($MaxTotalBytes -lt 1 -or $MaxTotalBytes -gt 536870912) {
        throw 'MaxTotalBytes must be between 1 and 536870912'
    }
    if ($MaxFiles -lt 1 -or $MaxFiles -gt 4096) {
        throw 'MaxFiles must be between 1 and 4096'
    }
    if ($MaxDepth -lt 1 -or $MaxDepth -gt 16) {
        throw 'MaxDepth must be between 1 and 16'
    }

    if ($specifiedRoots.Count -eq 0) {
        if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) {
            throw 'cannot determine the tool directory'
        }
        $gameRootInput = $PSScriptRoot
        $profileDir = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
        if ([string]::IsNullOrWhiteSpace($profileDir)) {
            $profileDir = $env:USERPROFILE
        }
        if ([string]::IsNullOrWhiteSpace($profileDir)) {
            throw 'cannot determine the current user profile'
        }
        $userDataRootInput = Join-Path $profileDir 'AppData\LocalLow\noio\KingdomTwoCrowns'
        $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
        if ([string]::IsNullOrWhiteSpace($localAppData)) {
            throw 'cannot determine LocalApplicationData'
        }
        $outputRootInput = Join-Path $localAppData 'OhMyMods\Diagnostics'
    }
    else {
        $gameRootInput = $GameRoot
        $userDataRootInput = $UserDataRoot
        $outputRootInput = $OutputRoot
    }

    $gameRoot = Resolve-RootPath $gameRootInput 'GameRoot' $true
    $userDataRoot = Resolve-RootPath $userDataRootInput 'UserDataRoot' $false
    $outputRoot = Resolve-RootPath $outputRootInput 'OutputRoot' $false

    $rootEntries = @(
        [pscustomobject]@{ Name = 'GameRoot'; Path = $gameRoot },
        [pscustomobject]@{ Name = 'UserDataRoot'; Path = $userDataRoot },
        [pscustomobject]@{ Name = 'OutputRoot'; Path = $outputRoot }
    )
    for ($i = 0; $i -lt $rootEntries.Count; $i++) {
        for ($j = $i + 1; $j -lt $rootEntries.Count; $j++) {
            if ((Test-PathSameOrUnder $rootEntries[$i].Path $rootEntries[$j].Path) -or
                (Test-PathSameOrUnder $rootEntries[$j].Path $rootEntries[$i].Path)) {
                throw ($rootEntries[$i].Name + ' and ' + $rootEntries[$j].Name + ' must not overlap')
            }
        }
    }

    $gameMarker = $null
    if (Test-Path -LiteralPath (Join-Path $gameRoot 'KingdomTwoCrowns.exe') -PathType Leaf) {
        $gameMarker = 'KingdomTwoCrowns.exe'
    }
    elseif (Test-Path -LiteralPath (Join-Path $gameRoot 'BepInEx') -PathType Container) {
        $gameMarker = 'BepInEx'
    }
    else {
        throw 'GameRoot does not contain KingdomTwoCrowns.exe or BepInEx. Put this tool in the game folder (next to KingdomTwoCrowns.exe) and run it again.'
    }

    $state = [pscustomobject]@{
        Partial = $false
        Changed = $false
        Entries = (New-Object System.Collections.ArrayList)
        Missing = (New-Object System.Collections.ArrayList)
        Errors = (New-Object System.Collections.ArrayList)
        Coverage = (New-Object System.Collections.ArrayList)
        Verify = (New-Object System.Collections.ArrayList)
        ZipPaths = @{}
        FilesSeen = 0
        TotalBytes = [long]0
        ReadBytes = [long]0
        MetadataBytes = [long]0
        VisitedEntries = 0
        StopEnumeration = $false
        GameRunningAtStart = $false
        GameRunningAtEnd = $false
        Limits = [pscustomobject]@{
            MaxFileBytes = [long]$MaxFileBytes
            MaxTotalBytes = [long]$MaxTotalBytes
            MaxFiles = [int]$MaxFiles
            MaxDepth = [int]$MaxDepth
        }
        Zip = $null
        StageDir = $null
        StageIndex = 0
    }

    Write-Info ('OhMyMods 排查资料收集工具 ' + $toolVersion)
    Write-Info ('游戏根目录：' + $gameRoot)
    Write-Info ('用户数据目录：' + $userDataRoot)
    Write-Info ('输出目录：' + $outputRoot)
    Write-Info '只读收集；不会修改或删除存档、配置。'

    $state.GameRunningAtStart = Get-GameRunning $state

    try {
        [IO.Directory]::CreateDirectory($outputRoot) | Out-Null
    }
    catch {
        throw ('OutputRoot could not be created: ' + $outputRoot)
    }

    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
    $token = [Guid]::NewGuid().ToString('N').Substring(0, 8)
    for ($attempt = 0; $attempt -lt 32; $attempt++) {
        $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
        $token = [Guid]::NewGuid().ToString('N').Substring(0, 8)
        $candidate = Join-Path $outputRoot ('OhMyMods-Diagnostics-' + $stamp + '-' + $token)
        if (Test-Path -LiteralPath $candidate) {
            continue
        }
        try {
            New-Item -ItemType Directory -Path $candidate -ErrorAction Stop | Out-Null
            $runDir = $candidate
            break
        }
        catch {
            $runDir = $null
        }
    }
    if ($null -eq $runDir) {
        throw 'could not create a unique output directory (OutputRoot may be read-only)'
    }

    $zipBase = 'OhMyMods-Diagnostics-' + $stamp + '-' + $token
    $tempZip = Join-Path $runDir ($zipBase + '.zip.partial')
    $finalZip = Join-Path $runDir ($zipBase + '.zip')
    $state.StageDir = Join-Path $runDir '.work'
    [IO.Directory]::CreateDirectory($state.StageDir) | Out-Null
    $zipStream = New-Object System.IO.FileStream($tempZip, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $zip = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create, $true, [Text.Encoding]::UTF8)
    $state.Zip = $zip

    $items = @(
        [pscustomobject]@{ Kind = 'file'; Area = 'game'; Category = 'bepinex-log'; Source = 'BepInEx/LogOutput.log'; Zip = 'BepInEx/LogOutput.log'; Required = $true },
        [pscustomobject]@{ Kind = 'file'; Area = 'game'; Category = 'bepinex-log'; Source = 'BepInEx/LogOutput.log.1'; Zip = 'BepInEx/LogOutput.log.1'; Required = $false },
        [pscustomobject]@{ Kind = 'file'; Area = 'game'; Category = 'bepinex-log'; Source = 'BepInEx/LogOutput.log.old'; Zip = 'BepInEx/LogOutput.log.old'; Required = $false },
        [pscustomobject]@{ Kind = 'file'; Area = 'game'; Category = 'bepinex-config'; Source = 'BepInEx/config/BepInEx.cfg'; Zip = 'BepInEx/config/BepInEx.cfg'; Required = $false },
        [pscustomobject]@{ Kind = 'file'; Area = 'game'; Category = 'bepinex-config'; Source = 'BepInEx/config/KingdomEnhancedMod.cfg'; Zip = 'BepInEx/config/KingdomEnhancedMod.cfg'; Required = $false },
        [pscustomobject]@{ Kind = 'tree'; Area = 'game'; Category = 'mod-config-tree'; Source = 'BepInEx/config/KingdomEnhancedMod'; Zip = 'BepInEx/config/KingdomEnhancedMod'; Required = $false },
        [pscustomobject]@{ Kind = 'file'; Area = 'userData'; Category = 'player-log'; Source = 'Player.log'; Zip = 'UserData/Player.log'; Required = $true },
        [pscustomobject]@{ Kind = 'file'; Area = 'userData'; Category = 'player-log'; Source = 'Player-prev.log'; Zip = 'UserData/Player-prev.log'; Required = $false },
        [pscustomobject]@{ Kind = 'tree'; Area = 'userData'; Category = 'save-release'; Source = 'Release'; Zip = 'UserData/Release'; Required = $true },
        [pscustomobject]@{ Kind = 'file'; Area = 'game'; Category = 'root-manifest'; Source = 'BUILD-MANIFEST.txt'; Zip = 'BUILD-MANIFEST.txt'; Required = $false },
        [pscustomobject]@{ Kind = 'file'; Area = 'game'; Category = 'root-manifest'; Source = 'RELEASE_MANIFEST.json'; Zip = 'RELEASE_MANIFEST.json'; Required = $false },
        [pscustomobject]@{ Kind = 'file'; Area = 'game'; Category = 'root-manifest'; Source = 'ohmymods-win243-preview.json'; Zip = 'ohmymods-win243-preview.json'; Required = $false }
    )

    foreach ($item in $items) {
        if ($item.Area -eq 'game') {
            $areaRoot = $gameRoot
        }
        else {
            $areaRoot = $userDataRoot
        }
        if ($item.Kind -eq 'file') {
            Add-SourceFile $state $areaRoot $item.Area $item.Category $item.Source $item.Zip ([bool]$item.Required)
        }
        else {
            Add-SourceTree $state $areaRoot $item.Area $item.Category $item.Source $item.Zip ([bool]$item.Required)
        }
    }

    $pluginRelative = 'BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll'
    $pluginPrimary = Join-Path $gameRoot 'BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll'
    $pluginFlat = Join-Path $gameRoot 'BepInEx\plugins\KingdomEnhancedMod.dll'
    if (-not (Test-Path -LiteralPath $pluginPrimary -PathType Leaf) -and
        (Test-Path -LiteralPath $pluginFlat -PathType Leaf)) {
        $pluginRelative = 'BepInEx/plugins/KingdomEnhancedMod.dll'
    }
    $gameExeMetadata = New-MetadataRecord $state $gameRoot 'game' 'KingdomTwoCrowns.exe'
    $pluginMetadata = New-MetadataRecord $state $gameRoot 'game' $pluginRelative

    $bepInExLogCount = @($state.Entries | Where-Object { $_.zipPath -eq 'BepInEx/LogOutput.log' -and $_.status -eq 'collected' }).Count
    $playerLogCount = @($state.Entries | Where-Object { $_.zipPath -eq 'UserData/Player.log' -and $_.status -eq 'collected' }).Count
    $releaseCount = @($state.Entries | Where-Object { $_.category -eq 'save-release' -and $_.status -eq 'collected' }).Count
    $requiredSpecs = @(
        [pscustomobject]@{ Category = 'bepinex-log'; Label = 'BepInEx/LogOutput.log'; FileCount = $bepInExLogCount },
        [pscustomobject]@{ Category = 'player-log'; Label = 'UserData/Player.log'; FileCount = $playerLogCount },
        [pscustomobject]@{ Category = 'save-release'; Label = 'UserData/Release'; FileCount = $releaseCount }
    )
    foreach ($spec in $requiredSpecs) {
        $count = [int]$spec.FileCount
        [void]$state.Coverage.Add([pscustomobject][ordered]@{
            category = $spec.Category
            label = $spec.Label
            collected = ($count -gt 0)
            fileCount = $count
        })
        if ($count -eq 0) {
            $state.Partial = $true
        }
    }

    $state.GameRunningAtEnd = Get-GameRunning $state
    $status = Get-RunStatus $state
    $live = [bool]($state.GameRunningAtStart -or $state.GameRunningAtEnd -or $state.Changed)
    $exitCode = 0
    if ($state.Partial -or $live) {
        $exitCode = 2
    }

    $startedUtcText = $startedUtc.ToString('o')
    $readmeText = Get-ReadmeText $status $startedUtcText $toolVersion
    $readmeBytes = [byte[]]($utf8Bom.GetPreamble() + $utf8Bom.GetBytes($readmeText))
    $readmePath = Join-Path $runDir 'README.txt'
    $readmeWriteFailed = $false
    try {
        [IO.File]::WriteAllText($readmePath, $readmeText, $utf8Bom)
    }
    catch {
        $readmeWriteFailed = $true
        Add-Error $state 'output' 'output' 'README.txt' $_.Exception.GetType().Name 'readme-write-failed'
        try {
            if ([IO.File]::Exists($readmePath)) {
                [IO.File]::Delete($readmePath)
            }
        }
        catch {
        }
    }
    if ($readmeWriteFailed) {
        $status = Get-RunStatus $state
        $live = [bool]($state.GameRunningAtStart -or $state.GameRunningAtEnd -or $state.Changed)
        $exitCode = 0
        if ($state.Partial -or $live) {
            $exitCode = 2
        }
        $readmeText = Get-ReadmeText $status $startedUtcText $toolVersion
        $readmeBytes = [byte[]]($utf8Bom.GetPreamble() + $utf8Bom.GetBytes($readmeText))
    }

    $readmeSha = Get-BytesSha256 $readmeBytes
    Add-ZipBytes $state 'README.txt' $readmeBytes
    [void]$state.Verify.Add([pscustomobject]@{
        zipPath = 'README.txt'
        size = [long]$readmeBytes.Length
        sha256 = $readmeSha
    })

    $finishedUtc = [DateTime]::UtcNow
    $entriesArray = @($state.Entries.ToArray())
    $missingArray = @($state.Missing.ToArray())
    $errorsArray = @($state.Errors.ToArray())
    $coverageArray = @($state.Coverage.ToArray())
    $documentsArray = @(
        [pscustomobject][ordered]@{
            path = 'README.txt'
            size = [long]$readmeBytes.Length
            sha256 = $readmeSha
        }
    )
    $snapshotMode = 'quiescent-best-effort'
    if ($live) {
        $snapshotMode = 'live-best-effort'
    }
    $collectedCount = @($entriesArray | Where-Object { $_.status -eq 'collected' }).Count
    $skippedCount = @($entriesArray | Where-Object { ([string]$_.status).StartsWith('skipped-') }).Count
    $counts = [pscustomobject][ordered]@{
        collected = $collectedCount
        skipped = $skippedCount
        errors = $errorsArray.Count
        missing = $missingArray.Count
        filesSeen = $state.FilesSeen
        bytesCollected = [long]$state.TotalBytes
        bytesRead = [long]$state.ReadBytes
        metadataBytesRead = [long]$state.MetadataBytes
        directoryEntriesVisited = [int]$state.VisitedEntries
    }
    $limitsObject = [pscustomobject][ordered]@{
        maxFileBytes = [long]$state.Limits.MaxFileBytes
        maxTotalBytes = [long]$state.Limits.MaxTotalBytes
        maxFiles = [int]$state.Limits.MaxFiles
        maxDepth = [int]$state.Limits.MaxDepth
        maxDirectoryEntries = 2 * [int]$state.Limits.MaxFiles
    }
    $snapshotObject = [pscustomobject][ordered]@{
        atomic = $false
        mode = $snapshotMode
        note = '采集不是原子快照；游戏或其它程序可能在采集期间写入。'
    }
    $metadataObject = [pscustomobject][ordered]@{
        gameExe = $gameExeMetadata
        pluginDll = $pluginMetadata
    }
    $manifestObject = [pscustomobject][ordered]@{
        tool = $toolName
        toolVersion = $toolVersion
        powershellVersion = $PSVersionTable.PSVersion.ToString()
        startedUtc = $startedUtcText
        finishedUtc = $finishedUtc.ToString('o')
        gameRunningAtStart = [bool]$state.GameRunningAtStart
        gameRunningAtEnd = [bool]$state.GameRunningAtEnd
        gameMarker = $gameMarker
        status = $status
        partial = [bool]$state.Partial
        exitCode = [int]$exitCode
        snapshot = $snapshotObject
        limits = $limitsObject
        counts = $counts
        requiredCoverage = $coverageArray
        entries = $entriesArray
        missing = $missingArray
        errors = $errorsArray
        documents = $documentsArray
        metadata = $metadataObject
    }
    $manifestJson = (ConvertTo-Json -InputObject $manifestObject -Depth 8) + "`r`n"
    $manifestBytes = $utf8NoBom.GetBytes($manifestJson)
    Add-ZipBytes $state 'manifest.json' $manifestBytes
    [void]$state.Verify.Add([pscustomobject]@{
        zipPath = 'manifest.json'
        size = [long]$manifestBytes.Length
        sha256 = (Get-BytesSha256 $manifestBytes)
    })

    if ($null -ne $zip) {
        $zip.Dispose()
        $zip = $null
    }
    if ($null -ne $zipStream) {
        $zipStream.Dispose()
        $zipStream = $null
    }

    $verifyStream = $null
    $verifyArchive = $null
    try {
        $verifyStream = New-Object System.IO.FileStream($tempZip, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $verifyArchive = New-Object System.IO.Compression.ZipArchive($verifyStream, [System.IO.Compression.ZipArchiveMode]::Read, $false)
        if ($verifyArchive.Entries.Count -ne $state.Verify.Count) {
            throw 'ZIP readback found an unlisted or duplicate entry'
        }
        foreach ($expected in @($state.Verify.ToArray())) {
            $entry = $verifyArchive.GetEntry($expected.zipPath)
            if ($null -eq $entry) {
                throw ('ZIP readback missing entry: ' + $expected.zipPath)
            }
            if ([long]$entry.Length -ne [long]$expected.size) {
                throw ('ZIP readback size mismatch: ' + $expected.zipPath)
            }
            $entryStream = $entry.Open()
            $sha = [System.Security.Cryptography.SHA256]::Create()
            try {
                $actualHash = ConvertTo-Hex ($sha.ComputeHash($entryStream))
            }
            finally {
                $entryStream.Dispose()
                $sha.Dispose()
            }
            if (-not $actualHash.Equals([string]$expected.sha256, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw ('ZIP readback hash mismatch: ' + $expected.zipPath)
            }
        }
    }
    finally {
        if ($null -ne $verifyArchive) {
            $verifyArchive.Dispose()
        }
        if ($null -ne $verifyStream) {
            $verifyStream.Dispose()
        }
    }

    if ([IO.File]::Exists($finalZip)) {
        throw 'final ZIP path already exists'
    }
    [IO.File]::Move($tempZip, $finalZip)
    $tempZip = $null
    $success = $true

    if (-not $NoOpen) {
        try {
            Start-Process -FilePath 'explorer.exe' -ArgumentList @(('"' + $runDir + '"')) -ErrorAction Stop | Out-Null
        }
        catch {
            Write-Info ('无法自动打开资源管理器，请手动打开：' + $runDir)
        }
    }

    Write-Info ''
    Write-Info ('排查资料已生成：' + $finalZip)
    Write-Info ('资料项：已收集 ' + $collectedCount + '，缺失 ' + $missingArray.Count + '，跳过 ' + $skippedCount + '，错误 ' + $errorsArray.Count)
    Write-Info ('采集状态：' + $status)
    if ($exitCode -eq 0) {
        Write-Info '主资料收集完成；资料包不作为一致备份恢复。'
    }
    else {
        Write-Info '结果为部分缺失或活动快照；ZIP 仍可发送，manifest.json 中有逐项明细。'
    }
    Write-Info '请把 ZIP 私下发送给维护者；工具不会自动上传。'
}
catch {
    $fatal = $_.Exception.Message
    Write-Fail $fatal
    Write-Info '未生成可用 ZIP；工具没有修改游戏存档或配置。'
}
finally {
    if ($null -ne $zip) { try { $zip.Dispose() } catch { } }
    if ($null -ne $zipStream) { try { $zipStream.Dispose() } catch { } }
    if ($null -ne $tempZip -and [IO.File]::Exists($tempZip)) {
        try { [IO.File]::Delete($tempZip) } catch { }
    }
    if ($null -ne $state -and $null -ne $state.StageDir -and $null -ne $runDir) {
        Remove-OwnedDirectory $state.StageDir $runDir
    }
    if (-not $success -and $null -ne $runDir) {
        Remove-OwnedDirectory $runDir $outputRoot
    }
}

if ($null -ne $fatal) {
    exit 1
}
exit $exitCode
