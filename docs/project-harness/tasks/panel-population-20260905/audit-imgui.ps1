param([string]$ModDll)
$ErrorActionPreference = 'Stop'
$gameRoot = 'E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091'
$depsRoot = 'E:\mod-dev\KingdomMod\deps\KTC-ModDevLibs\BIE6_IL2CPP'
Add-Type -Path "$depsRoot\core\Mono.Cecil.dll"
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory("$gameRoot\BepInEx\interop")
$resolver.AddSearchDirectory("$gameRoot\BepInEx\core")
$resolver.AddSearchDirectory("$depsRoot\core")
$parameters = [Mono.Cecil.ReaderParameters]::new()
$parameters.AssemblyResolver = $resolver
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($ModDll, $parameters)
$visited = [System.Collections.Generic.HashSet[string]]::new()
$failures = [System.Collections.Generic.List[string]]::new()
function Visit-UnityMethod($method, [string]$route, [int]$depth) {
    if (-not $visited.Add($method.FullName)) { return }
    if ($depth -gt 100) { $failures.Add("Depth limit: $route"); return }
    if (-not $method.HasBody) { return }
    foreach ($instruction in $method.Body.Instructions) {
        if ($instruction.OpCode.Name -eq 'ldstr' -and $instruction.Operand -like '*unstripping failed*') {
            $failures.Add("STUB: $route")
        }
        if ($instruction.Operand -is [Mono.Cecil.MethodReference] -and $instruction.Operand.DeclaringType.Namespace -like 'UnityEngine*') {
            $reference = $instruction.Operand
            try { $resolved = $reference.Resolve() }
            catch { $failures.Add("UNRESOLVED: $($reference.FullName): $($_.Exception.Message)"); continue }
            if ($null -eq $resolved) { $failures.Add("UNRESOLVED: $($reference.FullName)"); continue }
            Visit-UnityMethod $resolved "$route -> $($reference.FullName)" ($depth + 1)
        }
    }
}
try {
    foreach ($type in $assembly.MainModule.Types | Where-Object Name -in @('ModPanel', 'CalendarHud', 'ImGuiCompat')) {
        foreach ($method in $type.Methods) { Visit-UnityMethod $method $method.FullName 0 }
    }
    "Audited $($visited.Count) managed methods reachable from panel/HUD Unity calls against target interop."
    $failures
    if ($failures.Count -gt 0) { exit 1 }
    'PASS: no reachable Method unstripping failed stubs; runtime rendering still requires in-game verification.'
}
finally { $assembly.Dispose(); $resolver.Dispose() }
