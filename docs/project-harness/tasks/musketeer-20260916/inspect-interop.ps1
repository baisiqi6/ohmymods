$ErrorActionPreference='Stop'
Add-Type -Path 'E:/mod-dev/KingdomMod/deps/KTC-ModDevLibs/BIE6_IL2CPP/core/Mono.Cecil.dll'
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/BepInEx/interop/Assembly-CSharp.dll')
$wanted=@('Archer','Character','Peasant','DroppableTool','Droppable','Arrow','ArrowAttack','Damageable','DamageSource','Enemy','EnemyFlyer','ToolShop','Shop','PayableComponent','Persistent','IslandSaveData','Holder','Pool','PoolManager','Bow','TargetScanner','AttackBase')
$lines=[Collections.Generic.List[string]]::new()
foreach($type in $assembly.MainModule.Types){
 if($wanted -notcontains $type.Name){continue}
 $lines.Add('TYPE '+$type.FullName+' : '+$type.BaseType.FullName)
 foreach($p in $type.Properties){$lines.Add('  PROP '+$p.PropertyType.FullName+' '+$p.Name)}
 foreach($f in $type.Fields){if($f.Name -notlike 'Native*'){$lines.Add('  FIELD '+$f.FieldType.FullName+' '+$f.Name+' = '+$f.Constant)}}
 foreach($m in $type.Methods){if($m.Name -notmatch '^(get_|set_|\.cctor)'){$lines.Add('  METHOD '+$m.FullName+' PARAMS '+[string]::Join(', ',@($m.Parameters|ForEach-Object {$_.Name})) )}}
 foreach($nested in $type.NestedTypes){if($nested.Name -notmatch '^__c|^_.*d__'){
  $lines.Add('  NESTED '+$nested.FullName)
  foreach($p in $nested.Properties){$lines.Add('    PROP '+$p.PropertyType.FullName+' '+$p.Name)}
  foreach($f in $nested.Fields){if($f.Name -notlike 'Native*'){$lines.Add('    FIELD '+$f.FieldType.FullName+' '+$f.Name+' = '+$f.Constant)}}
  foreach($m in $nested.Methods){if($m.Name -notmatch '^(get_|set_|\.cctor)'){$lines.Add('    METHOD '+$m.FullName+' PARAMS '+[string]::Join(', ',@($m.Parameters|ForEach-Object {$_.Name})) )}}
 }}
}
$assembly.Dispose()
$lines | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'actual-interop.txt') -Encoding UTF8
'Read-only actual 2.4 interop metadata exported.'
