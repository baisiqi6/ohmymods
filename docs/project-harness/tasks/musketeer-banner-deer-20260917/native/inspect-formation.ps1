# Read-only native entry inspection. Only task evidence files are written.
$ErrorActionPreference = 'Stop'
$repo = 'C:/Users/ADMIN/projects/ohmymods'
$game = 'E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091'
Add-Type -Path ($game + '/BepInEx/core/Mono.Cecil.dll')
Add-Type -Path ($game + '/BepInEx/core/Iced.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($game + '/BepInEx/interop/Assembly-CSharp.dll')
$rows = [Collections.Generic.List[object]]::new()
try {
    foreach ($name in @('Formation', 'Archer', 'Player')) {
        $type = $assembly.MainModule.Types | Where-Object Name -eq $name
        $instructions = ($type.Methods | Where-Object Name -eq '.cctor').Body.Instructions
        for ($i = 0; $i -lt $instructions.Count - 2; $i++) {
            if ($instructions[$i].OpCode.Name -eq 'ldc.i4' -and
                $instructions[$i + 2].OpCode.Name -eq 'stsfld' -and
                $instructions[$i + 2].Operand.Name -match 'NativeMethodInfoPtr_(RegisterUnit_|Recruit_|TryRecruit_|ActivateFormation_)') {
                $rows.Add([pscustomobject]@{type=[string]$name; method=[string]$instructions[$i + 2].Operand.Name; token=[int]$instructions[$i].Operand})
            }
        }
    }
} finally { $assembly.Dispose() }
$json = ConvertTo-Json -InputObject $rows.ToArray() -Depth 3 -Compress
# Reuse the existing bounded, mmap-only PE/token mapper; only the four requested methods.
$mapper = Join-Path $repo 'docs/project-harness/tasks/combat-reliability-20260917/native/map-native.py'
$code = 'import pathlib,sys; s=pathlib.Path(sys.argv[1]).read_text(); exec(s.replace("else 2048,2048","else 8192,8192"))'
$blocks = $json | python -c $code $mapper | ConvertFrom-Json
$report = [Collections.Generic.List[string]]::new()
$addresses = [Collections.Generic.List[object]]::new()
foreach ($block in $blocks) {
    $addresses.Add([pscustomobject]@{type=$block.type; method=$block.method; token=$block.token; rva=$block.rva; sameAssemblyCSharpSlots=$block.same_slots; spanToNextDistinctEntryBytes=$block.bytes.Count})
    $report.Add($block.type + ' ' + $block.method + ' token=' + $block.token + ' RVA=' + $block.rva + ' sameSlots=' + $block.same_slots + ' spanBytes=' + $block.bytes.Count)
    $decoder = [Iced.Intel.Decoder]::Create(64, [byte[]]$block.bytes, [ulong]$block.va, [Iced.Intel.DecoderOptions]::None)
    while ($decoder.IP -lt ([ulong]$block.va + $block.bytes.Count)) {
        $instruction = $decoder.Decode()
        $report.Add(('{0:X16}: {1}' -f $instruction.IP, $instruction.ToString()))
    }
}
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllLines((Join-Path $PSScriptRoot 'entry-disassembly.txt'), $report, $utf8)
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'addresses.json'), (ConvertTo-Json -InputObject $addresses.ToArray() -Depth 3), $utf8)
$hashes = @('GameAssembly.dll', 'BepInEx/interop/Assembly-CSharp.dll') | ForEach-Object {
    $path = $game + '/' + $_
    [pscustomobject]@{path=$path; sha256=[string](Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'input-hashes.json'), (ConvertTo-Json -InputObject $hashes -Depth 3), $utf8)
$addresses | Format-Table type, rva, sameAssemblyCSharpSlots, spanToNextDistinctEntryBytes
