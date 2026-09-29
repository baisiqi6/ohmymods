# Samurai hit-output buffer: read-only Cecil check of compiled artifacts (issue-21 r3).
#
# Verifies the compiled candidate DLL uses a resident native array for the IL2CPP NonAlloc output:
#   - SamuraiChoreoMotion/Trip.Colliders and SweepSegment's buffer parameter are
#     Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1<UnityEngine.Collider2D>;
#   - the whole SamuraiChoreoMotion type has no Collider2D[] -> Il2CppReferenceArray<Collider2D>
#     op_Implicit/ctor(T[]) (the old mode: implicit copy, no copyback, read-back all null);
#   - the type contains no UnityEngine.Collider2D[] (field/param/local/newarr), result reads go
#     through the wrapper indexer (get_Item), and Physics2D.OverlapCapsuleNonAlloc receives that
#     same native array type.
# It also reads the real Physics2DModule/Il2CppInterop.Runtime metadata to confirm the signatures
# and the existence of the copying implicit conversion; optional -Installed shows the old DLL as a
# read-only counterexample (not a pass condition).
#
# Read-only: Cecil parses file metadata/IL only; no Unity assembly or native ctor is executed.
# Exit codes: 0 = all required checks pass; 1 = check failure; 2 = infrastructure failure
# (missing file/type/method, or interop assembly hash mismatch vs the recorded baseline).
[CmdletBinding()]
param(
    [string]$Candidate = "C:/Users/ADMIN/projects/ohmymods-wt-samurai-fixed/il2cpp/bin/Debug/KingdomEnhancedMod.dll",
    [string]$InteropDir = "E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/BepInEx/interop",
    [string]$CecilPath = "E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/BepInEx/core/Mono.Cecil.dll",
    [string]$RuntimePath = "",
    [string]$Output = "",
    [string]$Installed = "",
    [string]$ExpectedPhysics2DHash = "E3BEE5BEFC8B195EEAEA59282F9BB0DA616EA5E852877E7333C3D055B80C0939",
    [string]$ExpectedRuntimeHash = "C9C4815846C48F61A570E623B2589792B9541E11D1F185EEABAD56DD7BAF642E"
)
$ErrorActionPreference = 'Stop'

$WRAPPER_TOKEN = 'Il2CppReferenceArray`1<UnityEngine.Collider2D>'
$WRAPPER_FULL = 'Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1<UnityEngine.Collider2D>'
# The indexer is declared on Il2CppArrayBase`1 and inherited; the call site names the base type.
$INDEXER_TOKEN = 'Il2CppArrayBase`1<UnityEngine.Collider2D>'
$MANAGED_ARR = 'UnityEngine.Collider2D[]'

$checks = New-Object System.Collections.ArrayList
$infraReasons = New-Object System.Collections.ArrayList

if ($RuntimePath -eq '') {
    $RuntimePath = Join-Path (Split-Path -Parent $InteropDir) 'core/Il2CppInterop.Runtime.dll'
}

function Add-Check([string]$id, [bool]$pass, [string]$detail) {
    [void]$checks.Add([pscustomobject]@{ id = $id; pass = $pass; detail = $detail })
    $mark = if ($pass) { 'PASS' } else { 'FAIL' }
    Write-Host ("{0} {1} - {2}" -f $mark, $id, $detail)
}
function Add-Infra([string]$reason) {
    [void]$infraReasons.Add($reason)
    Write-Host ("INFRA {0}" -f $reason)
}
function Get-Sha256([string]$path) {
    # Get-FileHash is unavailable in this environment: use .NET directly (read-only file stream).
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($path)
    $bytes = $sha.ComputeHash($stream)
    $stream.Dispose()
    $sha.Dispose()
    return ([BitConverter]::ToString($bytes) -replace '-', '')
}
function Read-Assembly([string]$path) {
    $rp = New-Object Mono.Cecil.ReaderParameters
    $rp.InMemory = $true
    $rp.ReadSymbols = $false
    return [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path, $rp)
}
function Get-AllTypes($module) {
    $stack = New-Object System.Collections.Stack
    foreach ($t in $module.Types) { $stack.Push($t) }
    $all = New-Object System.Collections.ArrayList
    while ($stack.Count -gt 0) {
        $t = $stack.Pop()
        [void]$all.Add($t)
        foreach ($n in $t.NestedTypes) { $stack.Push($n) }
    }
    return $all
}
function Get-MethodInstructions($method) {
    if (-not $method.HasBody) { return @() }
    return $method.Body.Instructions
}

# ---------- input existence ----------
foreach ($p in @($Candidate, $CecilPath, (Join-Path $InteropDir 'UnityEngine.Physics2DModule.dll'), $RuntimePath)) {
    if (-not (Test-Path -Path $p)) { Add-Infra ("missing file: " + $p) }
}
if ($infraReasons.Count -gt 0) {
    $result = [pscustomobject]@{ timestamp = (Get-Date).ToString('o'); pass = $false; infraFailure = $true;
        infraReasons = @($infraReasons); checks = @($checks) }
    if ($Output -ne '') { [IO.File]::WriteAllText($Output, ($result | ConvertTo-Json -Depth 8)) }
    exit 2
}

Add-Type -Path $CecilPath

$candidateHash = Get-Sha256 $Candidate
$physicsPath = Join-Path $InteropDir 'UnityEngine.Physics2DModule.dll'
$runtimePath = $RuntimePath
$physicsHash = Get-Sha256 $physicsPath
$runtimeHash = Get-Sha256 $runtimePath
$interopHashesMatch = (($physicsHash -eq $ExpectedPhysics2DHash) -and ($runtimeHash -eq $ExpectedRuntimeHash))
Add-Check 'interop-hashes-match-review' $interopHashesMatch ("physics2d=" + $physicsHash.Substring(0, 12) + " runtime=" + $runtimeHash.Substring(0, 12))
if (-not $interopHashesMatch) { Add-Infra 'interop assembly hash does not match the recorded review baseline' }

# ---------- candidate DLL ----------
$candidateAsm = Read-Assembly $Candidate

$choreoType = $null
foreach ($t in (Get-AllTypes $candidateAsm.MainModule)) {
    if ($t.FullName -eq 'KingdomEnhancedMod.SamuraiChoreoMotion') { $choreoType = $t; break }
}
if ($choreoType -eq $null) {
    Add-Infra 'KingdomEnhancedMod.SamuraiChoreoMotion not found in candidate'
}

$tripType = $null
if ($choreoType -ne $null) {
    foreach ($n in $choreoType.NestedTypes) { if ($n.Name -eq 'Trip') { $tripType = $n; break } }
}

$fieldOk = $false; $fieldDetail = 'Trip.Colliders not found'
if ($tripType -ne $null) {
    foreach ($f in $tripType.Fields) {
        if ($f.Name -eq 'Colliders') {
            $fieldOk = ($f.FieldType.FullName -eq $WRAPPER_FULL)
            $fieldDetail = 'Trip.Colliders : ' + $f.FieldType.FullName
        }
    }
}
Add-Check 'trip-colliders-native' $fieldOk $fieldDetail

$paramOk = $false; $paramDetail = 'SweepSegment not found'
$indexerReads = 0; $capsuleCalls = 0; $capsuleParamOk = $false
$managedArrayHits = New-Object System.Collections.ArrayList
$conversionHits = New-Object System.Collections.ArrayList
$capacityCtor = 0
$scanTypes = New-Object System.Collections.ArrayList
if ($choreoType -ne $null) {
    $typeStack = New-Object System.Collections.Stack
    $typeStack.Push($choreoType)
    while ($typeStack.Count -gt 0) {
        $st = $typeStack.Pop()
        [void]$scanTypes.Add($st)
        foreach ($n in $st.NestedTypes) { $typeStack.Push($n) }
    }
}
foreach ($st in $scanTypes) {
    foreach ($m in $st.Methods) {
        if ($m.Name -eq 'SweepSegment') {
            foreach ($p in $m.Parameters) {
                if ($p.Name -eq 'buffer') {
                    $paramOk = ($p.ParameterType.FullName -eq $WRAPPER_FULL)
                    $paramDetail = 'SweepSegment buffer : ' + $p.ParameterType.FullName
                }
            }
        }
        foreach ($p in $m.Parameters) {
            if ($p.ParameterType.FullName -eq $MANAGED_ARR) {
                [void]$managedArrayHits.Add($m.Name + ' parameter ' + $p.Name)
            }
        }
        foreach ($ins in (Get-MethodInstructions $m)) {
            $code = $ins.OpCode.Code.ToString()
            if ($code -eq 'Newarr' -and $ins.Operand -ne $null -and $ins.Operand.FullName -eq 'UnityEngine.Collider2D') {
                [void]$managedArrayHits.Add($m.Name + ' IL_' + $ins.Offset + ' newarr Collider2D')
            }
            if ($code -eq 'Call' -or $code -eq 'Callvirt') {
                $mr = $ins.Operand
                if ($mr -is [Mono.Cecil.MethodReference] -and $mr.DeclaringType -ne $null) {
                    $decl = $mr.DeclaringType.FullName
                    if ($decl -ne $null -and ($decl.Contains($WRAPPER_TOKEN) -or $decl.Contains($INDEXER_TOKEN))) {
                        if ($mr.Name -eq 'get_Item') { $indexerReads++ }
                        if ($mr.Name -eq 'op_Implicit') {
                            [void]$conversionHits.Add($m.Name + ' IL_' + $ins.Offset + ' op_Implicit')
                        }
                        if ($mr.Name -eq '.ctor' -and $mr.Parameters.Count -eq 1 -and $mr.Parameters[0].ParameterType.FullName -eq $MANAGED_ARR) {
                            [void]$conversionHits.Add($m.Name + ' IL_' + $ins.Offset + ' ctor(Collider2D[])')
                        }
                    }
                    if ($mr.Name -eq 'OverlapCapsuleNonAlloc' -and $mr.DeclaringType.FullName -eq 'UnityEngine.Physics2D') {
                        $capsuleCalls++
                        if ($mr.Parameters.Count -ge 5 -and
                            $mr.Parameters[4].ParameterType.FullName -eq $WRAPPER_FULL) { $capsuleParamOk = $true }
                    }
                }
            }
            if ($code -eq 'Newobj') {
                $mr = $ins.Operand
                if ($mr -is [Mono.Cecil.MethodReference] -and $mr.DeclaringType -ne $null -and
                    $mr.DeclaringType.FullName.Contains($WRAPPER_TOKEN) -and $mr.Name -eq '.ctor' -and
                    $mr.Parameters.Count -eq 1 -and $mr.Parameters[0].ParameterType.FullName -ne $MANAGED_ARR) {
                    $capacityCtor++
                }
            }
        }
    }
    foreach ($f in $st.Fields) {
        if ($f.FieldType.FullName -eq $MANAGED_ARR) { [void]$managedArrayHits.Add('field ' + $f.Name) }
    }
    foreach ($m in $st.Methods) {
        if ($m.HasBody) {
            foreach ($v in $m.Body.Variables) {
                if ($v.VariableType.FullName -eq $MANAGED_ARR) { [void]$managedArrayHits.Add($m.Name + ' local ' + $v.Index) }
            }
        }
    }
}
Add-Check 'sweep-segment-native-param' $paramOk $paramDetail
Add-Check 'no-managed-collider-array' ($managedArrayHits.Count -eq 0) (($managedArrayHits -join '; ') -replace '^$', 'none')
Add-Check 'no-implicit-array-conversion' ($conversionHits.Count -eq 0) (($conversionHits -join '; ') -replace '^$', 'none')
Add-Check 'native-capacity-construction' ($capacityCtor -ge 1) ('capacity ctor sites=' + $capacityCtor)
Add-Check 'reads-native-indexer' ($indexerReads -ge 1) ('get_Item calls=' + $indexerReads)
Add-Check 'capsule-results-native' ($capsuleCalls -ge 1 -and $capsuleParamOk) ('calls=' + $capsuleCalls + ' nativeResults=' + $capsuleParamOk)

# ---------- real interop metadata ----------
$physicsAsm = Read-Assembly $physicsPath
$physicsType = $null
foreach ($t in (Get-AllTypes $physicsAsm.MainModule)) {
    if ($t.FullName -eq 'UnityEngine.Physics2D') { $physicsType = $t; break }
}
$interopCapsuleOk = $false
if ($physicsType -ne $null) {
    foreach ($m in $physicsType.Methods) {
        if ($m.Name -eq 'OverlapCapsuleNonAlloc' -and $m.Parameters.Count -eq 6 -and
            $m.Parameters[4].ParameterType.FullName -eq $WRAPPER_FULL) { $interopCapsuleOk = $true }
    }
}
Add-Check 'interop-capsule-signature' $interopCapsuleOk 'Physics2D.OverlapCapsuleNonAlloc(..., Il2CppReferenceArray<Collider2D>, int)'

$runtimeAsm = Read-Assembly $runtimePath
$wrapperType = $null
foreach ($t in (Get-AllTypes $runtimeAsm.MainModule)) {
    if ($t.FullName -eq 'Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1') { $wrapperType = $t; break }
}
$implicitExists = $false; $getItemExists = $false
if ($wrapperType -ne $null) {
    foreach ($m in $wrapperType.Methods) {
        if ($m.Name -eq 'op_Implicit' -and $m.Parameters.Count -eq 1 -and $m.Parameters[0].ParameterType.IsArray) { $implicitExists = $true }
        if ($m.Name -eq 'get_Item') { $getItemExists = $true }
    }
}
Add-Check 'interop-implicit-copy-exists' $implicitExists 'Il2CppReferenceArray<T>.op_Implicit(T[]) is a copying conversion (the trap we avoid)'
Add-Check 'interop-indexer-exists' $getItemExists 'Il2CppReferenceArray<T>.get_Item is the native read surface'

# ---------- installed old DLL counterexample (read-only, optional) ----------
$oldInfo = $null
if ($Installed -ne '' -and (Test-Path -Path $Installed)) {
    $oldAsm = Read-Assembly $Installed
    $oldType = $null
    foreach ($t in (Get-AllTypes $oldAsm.MainModule)) {
        if ($t.FullName -eq 'KingdomEnhancedMod.PatchRoles_SamuraiPowerDash') { $oldType = $t; break }
    }
    $oldFieldType = 'not-found'; $oldImplicit = $false; $oldManagedRead = $false
    if ($oldType -ne $null) {
        foreach ($n in $oldType.NestedTypes) {
            if ($n.Name -eq 'ChoreoToken') {
                foreach ($f in $n.Fields) { if ($f.Name -eq 'Colliders') { $oldFieldType = $f.FieldType.FullName } }
            }
        }
        foreach ($m in $oldType.Methods) {
            if ($m.Name -ne 'ChoreoHitScan') { continue }
            foreach ($ins in (Get-MethodInstructions $m)) {
                if ($ins.OpCode.Code.ToString() -eq 'Ldelem_Ref') { $oldManagedRead = $true }
                $mr = $ins.Operand
                if ($mr -is [Mono.Cecil.MethodReference] -and $mr.Name -eq 'op_Implicit' -and $mr.DeclaringType -ne $null -and
                    $mr.DeclaringType.FullName.Contains($WRAPPER_TOKEN)) { $oldImplicit = $true }
            }
        }
    }
    $oldInfo = [pscustomobject]@{ path = $Installed; sha256 = (Get-Sha256 $Installed);
        choreoTokenColliders = $oldFieldType; hitScanImplicitConversion = $oldImplicit; hitScanManagedRead = $oldManagedRead;
        counterexampleDemonstrated = (($oldFieldType -eq $MANAGED_ARR) -and $oldImplicit -and $oldManagedRead) }
}

# ---------- summary ----------
$allPass = $true
foreach ($c in $checks) { if (-not $c.pass) { $allPass = $false } }
$result = [pscustomobject]@{
    timestamp = (Get-Date).ToString('o')
    candidate = $Candidate
    candidateSha256 = $candidateHash
    interopDir = $InteropDir
    physics2dSha256 = $physicsHash
    runtimeSha256 = $runtimeHash
    interopHashesMatchReview = $interopHashesMatch
    checks = @($checks)
    installedCounterexample = $oldInfo
    pass = ($allPass -and $infraReasons.Count -eq 0)
    infraFailure = ($infraReasons.Count -gt 0)
    infraReasons = @($infraReasons)
}
if ($Output -ne '') {
    $dir = Split-Path -Parent $Output
    if ($dir -ne '' -and -not (Test-Path $dir)) { [void](New-Item -ItemType Directory -Path $dir) }
    [IO.File]::WriteAllText($Output, ($result | ConvertTo-Json -Depth 8))
    Write-Host ("report written: " + $Output)
}
if ($infraReasons.Count -gt 0) { exit 2 }
if (-not $allPass) { exit 1 }
exit 0
