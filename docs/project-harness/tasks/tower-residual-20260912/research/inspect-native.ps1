$ErrorActionPreference='Stop'
$expected='C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/hud-pixel-style-20260912/research'
if([IO.Path]::GetFullPath($PSScriptRoot) -ne [IO.Path]::GetFullPath($expected)){throw 'Wrong output directory'}
$g='E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091'
Add-Type -Path ($g+'/BepInEx/core/Mono.Cecil.dll')
Add-Type -Path ($g+'/BepInEx/core/Iced.dll')
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($g+'/BepInEx/interop/Assembly-CSharp.dll')
$rows=[Collections.Generic.List[object]]::new()
foreach($name in @('GuardSlot','Archer','Kingdom')){
 $t=$a.MainModule.Types|Where-Object {$_.Name -eq $name}
 $ins=($t.Methods|Where-Object {$_.Name -eq '.cctor'}).Body.Instructions
 for($i=0;$i -lt $ins.Count-2;$i++){
  if($ins[$i].OpCode.Name -eq 'ldc.i4' -and $ins[$i+2].OpCode.Name -eq 'stsfld' -and $ins[$i+2].Operand.Name -match 'NativeMethodInfoPtr_(get_GuardSlot_|get_inGuardSlot_|set_inGuardSlot_|ExitGuardSlot_|SetGuardSlot_|EnterGuardSlot_|HasGuardSlot_|AssignArcher_|RemoveArcher_|ExitArcher_|IsValidGuardSlot_|AddGuardSlot_|RemoveGuardSlot_|DistributeTowerArchers_)'){
   $rows.Add([pscustomobject]@{type=[string]$name;method=[string]$ins[$i+2].Operand.Name;token=[int]$ins[$i].Operand})
  }
 }
}
$a.Dispose()
$inputJson=ConvertTo-Json -InputObject $rows.ToArray() -Depth 3 -Compress
$blocks=$inputJson | python (Join-Path $PSScriptRoot 'map-native.py') | ConvertFrom-Json
$report=[Collections.Generic.List[string]]::new()
foreach($b in $blocks){
 $report.Add($b.type+' '+$b.method+' token='+$b.token+' RVA='+$b.rva+' sameSlots='+$b.same_slots)
 $decoder=[Iced.Intel.Decoder]::Create(64,[byte[]]$b.bytes,[ulong]$b.va,[Iced.Intel.DecoderOptions]::None)
 for($j=0;$j -lt 350 -and $decoder.IP -lt ([ulong]$b.va+$b.bytes.Count);$j++){
  $instruction=$decoder.Decode()
  $report.Add(('{0:X16}: {1}' -f $instruction.IP,$instruction.ToString()))
 }
}
[IO.File]::WriteAllLines((Join-Path $PSScriptRoot 'native-disassembly.txt'),$report,[Text.UTF8Encoding]::new($false))
$simple=@($blocks|ForEach-Object{[pscustomobject]@{type=$_.type;method=$_.method;token=$_.token;rva=$_.rva;same_slots=$_.same_slots}})
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'native-addresses.json'),(ConvertTo-Json -InputObject $simple -Depth 3),[Text.UTF8Encoding]::new($false))
$hashes=@('GameAssembly.dll','BepInEx/interop/Assembly-CSharp.dll')|ForEach-Object {
 $path=$g+'/'+$_
 [pscustomobject]@{path=$path;sha256=[string](Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'input-hashes.json'),(ConvertTo-Json -InputObject @($hashes) -Depth 3),[Text.UTF8Encoding]::new($false))
$simple|Format-Table type,rva,same_slots,method
