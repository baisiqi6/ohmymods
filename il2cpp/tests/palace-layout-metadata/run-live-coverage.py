#!/usr/bin/env python3
"""Compile the actual CourtArea livecoverage method against a small host, not a copied algorithm."""
import argparse, pathlib, subprocess, json, hashlib
p=argparse.ArgumentParser();p.add_argument("--output",required=True);p.add_argument("--dotnet",required=True);a=p.parse_args()
out=pathlib.Path(a.output).resolve();out.mkdir(parents=True,exist_ok=True)
tests=pathlib.Path(__file__).resolve().parent;source=tests.parents[1]/"Patch_PalaceCourtArea.cs"
s=source.read_text();start=s.index("    private static bool TryLiveTileCoverage(");end=s.index("    /// <summary>",start+1)
method=s[start:end].replace("private static bool TryLiveTileCoverage","public static bool TryLiveTileCoverage",1)
wrapper="using System; using System.Collections.Generic; using KingdomEnhancedMod;\ninternal static class NewCoverage { private struct CourtSpan { public float Left,Right; } private static bool AnyScatterSet(Tile t)=>false; private static string F(float x)=>x.ToString();\n"+method+"}\n"
(out/"Coverage.Extracted.cs").write_text(wrapper)
files=[out/"Coverage.Extracted.cs",tests/"LiveCoverageStubs.cs.txt",tests/"LiveCoverageProgram.cs.txt",tests.parents[1]/"Patch_PalaceLayoutRecipe.cs",tests.parents[1]/"Patch_PalaceLayoutMetadata.cs"]
project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'+''.join('<Compile Include="'+str(f)+'"/>' for f in files)+'</ItemGroup></Project>'
(out/"Coverage.Tests.csproj").write_text(project)
subprocess.run([a.dotnet,"build",str(out/"Coverage.Tests.csproj")],check=True)
subprocess.run([a.dotnet,str(out/"bin/Debug/net8.0/Coverage.Tests.dll")],check=True)
(out/"extraction.json").write_text(json.dumps({"productionSource":str(source),"sha256":hashlib.sha256(source.read_bytes()).hexdigest(),"method":"TryLiveTileCoverage","hostBoundary":"Tile spans/components are host values; no Unity/native or scatter lifecycle claim"},indent=2)+'\n')
