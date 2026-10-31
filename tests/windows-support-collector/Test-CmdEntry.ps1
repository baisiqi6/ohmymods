param([Parameter(Mandatory=$true)][string]$CmdSource)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$root=Join-Path ([IO.Path]::GetTempPath()) ('ohmymods-collector-cmd-'+[Guid]::NewGuid().ToString('N'))
$game=Join-Path $root '懒人包 中文 空格'
$bin=Join-Path $root 'stub-bin'
[IO.Directory]::CreateDirectory($game) | Out-Null
[IO.Directory]::CreateDirectory($bin) | Out-Null
$cmd=Join-Path $game '一键收集排查资料.cmd'
[IO.File]::WriteAllBytes($cmd,[IO.File]::ReadAllBytes($CmdSource))
$code=@'
using System;
using System.IO;
using System.Text;
public static class Entry {
 public static int Main(string[] args) {
  File.WriteAllLines(Environment.GetEnvironmentVariable("OHMYMODS_STUB_ARGS"),args,new UTF8Encoding(false));
  return Int32.Parse(Environment.GetEnvironmentVariable("OHMYMODS_STUB_EXIT"));
 }
}
'@
$stub=Join-Path $bin 'powershell.exe'
Add-Type -TypeDefinition $code -Language CSharp -OutputAssembly $stub -OutputType ConsoleApplication
$oldPath=$env:PATH
$env:PATH=$bin+';'+$oldPath
$env:OHMYMODS_STUB_ARGS=Join-Path $root 'args.txt'
$results=@()
try {
 foreach($expected in @(0,1,2)) {
  $env:OHMYMODS_STUB_EXIT=[string]$expected
  $start=[Diagnostics.ProcessStartInfo]::new()
  $start.FileName=$env:COMSPEC
  $start.Arguments='/d /s /c ""'+$cmd+'""'
  $start.WorkingDirectory=$root
  $start.UseShellExecute=$false
  $start.RedirectStandardInput=$true
  $start.RedirectStandardOutput=$true
  $start.RedirectStandardError=$true
  $p=[Diagnostics.Process]::Start($start)
  $p.StandardInput.Close()
  $o=$p.StandardOutput.ReadToEnd()
  $e=$p.StandardError.ReadToEnd()
  if(-not $p.WaitForExit(10000)){throw 'CMD test timeout'}
  if($p.ExitCode -ne $expected){throw ('exit not preserved: '+$expected+' -> '+$p.ExitCode)}
  $argsRead=[IO.File]::ReadAllLines($env:OHMYMODS_STUB_ARGS)
  $fileIndex=[Array]::IndexOf($argsRead,'-File')
  if($fileIndex -lt 0 -or $argsRead[$fileIndex+1] -cne (Join-Path $game 'OhMyMods-CollectDiagnostics.ps1')){throw 'incorrect script path argument'}
  if([Array]::IndexOf($argsRead,'-NoProfile') -lt 0){throw 'NoProfile missing'}
  $policy=[Array]::IndexOf($argsRead,'-ExecutionPolicy')
  if($policy -lt 0 -or $argsRead[$policy+1] -cne 'Bypass'){throw 'process policy missing'}
  $results+=@{expected=$expected;actual=$p.ExitCode;correctUnicodePath=$true}
 }
} finally {
 $env:PATH=$oldPath
 Remove-Item Env:OHMYMODS_STUB_ARGS -ErrorAction SilentlyContinue
 Remove-Item Env:OHMYMODS_STUB_EXIT -ErrorAction SilentlyContinue
}
[PSCustomObject]@{cases=$results;fixtureRoot=$root;gameProcessStarted=$false;realProfileRead=$false} | ConvertTo-Json -Depth 4 -Compress
