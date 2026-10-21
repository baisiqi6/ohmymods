#!/usr/bin/env python3
"""Issue #275 regression checks for the IL2CPP Release build entrance.

Default checks require no game/interop references:
  * Release maps to net6.0 + IL2CPP/BIE/BIE6 for both GameInteropVersion values.
  * Release and Debug evaluate the same Compile, EmbeddedResource LogicalName and
    Reference (name/HintPath/Private/Aliases) sets; counts are printed and saved
    in a per-run receipt rather than hard-coded.
  * Release and Debug each generate exactly one MyPluginInfo.cs with the same content.
  * KTC_GAME_243 follows GameInteropVersion; late or contradictory values fail.
  * NoDeploy=true suppresses the Debug CopyOutput target.

Pass --dll-240/--dll-243 to verify final PE metadata (TFM, BasePlugin, typed
save refs, Debuggable flags, MyPluginInfo GUID, resources). The DLLs must be
built with the actual game/interop closure; this script never downloads them.
"""

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "il2cpp" / "KingdomEnhancedMod.csproj"
SHARED_PROPS = ROOT / "il2cpp" / "ProjectSettings.shared.props"
OUT_ROOT = ROOT / ".local" / "issue275-build"

EXPECTED_GENERATED = 1
REQUIRED_DEFINES = {"IL2CPP", "BIE", "BIE6"}

SAVE_REFS = {
    "2.4.0": {"_Save_d__89", "__TryDeleteCampaign_d__91", "__TryDeleteChallenge_d__94"},
    "2.4.3": {"_Save_d__92", "__TryDeleteCampaign_d__94", "__TryDeleteChallenge_d__97"},
}

PROBE_SOURCE = r"""
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

static class Probe
{
    static string TypeName(MetadataReader md, EntityHandle h)
    {
        switch (h.Kind)
        {
            case HandleKind.TypeReference:
                var tr = md.GetTypeReference((TypeReferenceHandle)h);
                return md.GetString(tr.Namespace) + "." + md.GetString(tr.Name);
            case HandleKind.TypeDefinition:
                var td = md.GetTypeDefinition((TypeDefinitionHandle)h);
                return md.GetString(td.Namespace) + "." + md.GetString(td.Name);
            case HandleKind.MemberReference:
                var mr = md.GetMemberReference((MemberReferenceHandle)h);
                return TypeName(md, mr.Parent);
            default:
                return h.Kind.ToString();
        }
    }

    static string ScopeName(MetadataReader md, EntityHandle h)
    {
        if (h.Kind != HandleKind.TypeReference) return "";
        var scope = md.GetTypeReference((TypeReferenceHandle)h).ResolutionScope;
        if (scope.Kind == HandleKind.AssemblyReference)
            return md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);
        return scope.Kind.ToString();
    }

    static string ReadSerString(byte[] blob)
    {
        int i = 2;
        if (i >= blob.Length || blob[i] == 0xFF) return "";
        int len = blob[i++];
        if ((len & 0x80) != 0)
        {
            if ((len & 0xC0) == 0x80) len = ((len & 0x3F) << 8) | blob[i++];
            else if ((len & 0xE0) == 0xC0)
            {
                len = ((len & 0x1F) << 24) | (blob[i] << 16) | (blob[i + 1] << 8) | blob[i + 2];
                i += 3;
            }
            else return "";
        }
        return Encoding.UTF8.GetString(blob, i, len);
    }

    static void Main(string[] args)
    {
        foreach (var path in args)
        {
            using var fs = File.OpenRead(path);
            using var pe = new PEReader(fs);
            var md = pe.GetMetadataReader();
            var asm = md.GetAssemblyDefinition();
            Console.WriteLine("FILE\t" + path);
            Console.WriteLine("MVID\t" + md.GetGuid(md.GetModuleDefinition().Mvid));
            Console.WriteLine("ASSEMBLY\t" + md.GetString(asm.Name) + " " + asm.Version);
            foreach (var ah in asm.GetCustomAttributes())
            {
                var ca = md.GetCustomAttribute(ah);
                string tn = TypeName(md, ca.Constructor);
                var blob = md.GetBlobBytes(ca.Value);
                if (tn == "System.Runtime.Versioning.TargetFrameworkAttribute")
                    Console.WriteLine("TFM\t" + ReadSerString(blob));
                if (tn == "System.Diagnostics.DebuggableAttribute" && blob.Length >= 6)
                    Console.WriteLine("DEBUGGABLE_FLAGS\t0x" + BitConverter.ToInt32(blob, 2).ToString("x"));
            }
            foreach (var th in md.TypeDefinitions)
            {
                var t = md.GetTypeDefinition(th);
                string name = md.GetString(t.Name);
                if (name == "KingdomEnhancedPlugin")
                    Console.WriteLine("PLUGIN_BASE\t" + TypeName(md, t.BaseType) + " @ " + ScopeName(md, t.BaseType));
                if (name == "MyPluginInfo")
                {
                    Console.WriteLine("MYPLUGININFO\tTrue");
                    foreach (var fh in t.GetFields())
                    {
                        var f = md.GetFieldDefinition(fh);
                        string fn = md.GetString(f.Name);
                        if (fn != "PLUGIN_GUID" && fn != "PLUGIN_NAME" && fn != "PLUGIN_VERSION") continue;
                        var ch = f.GetDefaultValue();
                        if (ch.IsNil) continue;
                        var c = md.GetConstant(ch);
                        Console.WriteLine(fn + "\t" + Encoding.Unicode.GetString(md.GetBlobBytes(c.Value)));
                    }
                }
            }
            var saves = new HashSet<string>();
            foreach (var trh in md.TypeReferences)
            {
                var tr = md.GetTypeReference(trh);
                string n = md.GetString(tr.Name);
                if (n.Contains("_Save_d__") || n.Contains("__TryDeleteCampaign_") || n.Contains("__TryDeleteChallenge_"))
                    saves.Add(n);
            }
            Console.WriteLine("SAVE_REFS\t" + string.Join(",", saves.OrderBy(x => x)));
            Console.WriteLine("RESOURCES\t" + md.ManifestResources.Count);
        }
    }
}
"""

PROBE_PROJECT = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <BepInExPluginsPath />
    <NoDeploy>true</NoDeploy>
    <UseSharedCompilation>false</UseSharedCompilation>
    <BaseOutputPath>$(MSBuildThisFileDirectory)out/</BaseOutputPath>
    <BaseIntermediateOutputPath>$(MSBuildThisFileDirectory)obj/</BaseIntermediateOutputPath>
    <MSBuildProjectExtensionsPath>$(MSBuildThisFileDirectory)obj/</MSBuildProjectExtensionsPath>
  </PropertyGroup>
</Project>
"""


class Checker:
    def __init__(self):
        self.failures = []

    def check(self, condition, message):
        if condition:
            print("PASS  " + message)
        else:
            self.failures.append(message)
            print("FAIL  " + message)

    def finish(self):
        if self.failures:
            print("\nFAILED: %d check(s)" % len(self.failures))
            for f in self.failures:
                print("  - " + f)
            return 1
        print("\nALL CHECKS PASSED")
        return 0


def find_dotnet():
    dotnet = os.environ.get("DOTNET") or shutil.which("dotnet")
    if not dotnet:
        raise SystemExit("dotnet not found; set DOTNET=/path/to/dotnet")
    return dotnet


def run(cmd, env):
    proc = subprocess.run(
        [str(c) for c in cmd],
        cwd=str(ROOT),
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )
    return proc.returncode, proc.stdout


def last_lines(text, count=8):
    lines = [line for line in text.splitlines() if line.strip()]
    return "\n".join(lines[-count:])


def dotnet_env(run_dir):
    env = os.environ.copy()
    env["DOTNET_CLI_HOME"] = str(run_dir / "dotnet-home")
    env.setdefault("DOTNET_CLI_TELEMETRY_OPTOUT", "1")
    env.setdefault("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1")
    env.setdefault("DOTNET_NOLOGO", "1")
    return env


def ensure_restored(dotnet, version, obj_dir, env, run_dir):
    cmd = [
        dotnet,
        "restore",
        PROJECT,
        "--nologo",
        "--ignore-failed-sources",
        "-p:Configuration=Release",
        "-p:GameInteropVersion=" + version,
        "-p:BaseIntermediateOutputPath=%s/" % obj_dir,
        "-p:MSBuildProjectExtensionsPath=%s/" % obj_dir,
        "-p:BaseOutputPath=%s/" % (run_dir / ("out-" + version)),
    ]
    feed = os.environ.get("KTC_NUGET_FEED")
    if feed:
        cmd += ["-p:RestoreSources=" + feed, "-p:RestoreAdditionalProjectSources="]
    rc, out = run(cmd, env)
    if rc:
        raise SystemExit("restore failed for %s:\n%s" % (version, last_lines(out, 20)))


def evaluate(dotnet, version, config, obj_dir, env):
    cmd = [
        dotnet,
        "msbuild",
        PROJECT,
        "-nologo",
        "-getProperty:Configuration,TargetFramework,DefineConstants,Optimize,DebugType,AssemblyName,Version",
        "-getItem:Compile",
        "-getItem:EmbeddedResource",
        "-getItem:Reference",
        "-p:Configuration=" + config,
        "-p:GameInteropVersion=" + version,
        "-p:BaseIntermediateOutputPath=%s/" % obj_dir,
        "-p:MSBuildProjectExtensionsPath=%s/" % obj_dir,
    ]
    rc, out = run(cmd, env)
    if rc:
        raise SystemExit("evaluation failed for %s/%s:\n%s" % (version, config, last_lines(out, 20)))
    start = out.find("{")
    if start < 0:
        raise SystemExit("no JSON from MSBuild evaluation:\n" + last_lines(out, 20))
    return json.loads(out[start:])


def define_tokens(value):
    return {x.strip() for x in re.split(r"[;,]", value or "") if x.strip()}


def reference_closure(items):
    result = {}
    for item in items:
        result[item["Identity"]] = {
            "HintPath": item.get("HintPath", ""),
            "Private": item.get("Private", "").lower(),
            "Aliases": item.get("Aliases", ""),
        }
    return result


def resource_names(items):
    return [item.get("LogicalName") or item["Identity"] for item in items]


def check_evaluation(checker, version, release, debug):
    props = release["Properties"]
    defines = define_tokens(props["DefineConstants"])
    items = release["Items"]
    compile_items = items.get("Compile", [])
    resources = items.get("EmbeddedResource", [])
    references = reference_closure(items.get("Reference", []))
    debug_items = debug["Items"]
    debug_compile = debug_items.get("Compile", [])
    debug_resources = debug_items.get("EmbeddedResource", [])
    debug_references = reference_closure(debug_items.get("Reference", []))

    checker.check(props["Configuration"] == "Release", "%s: Configuration is Release" % version)
    checker.check(props["TargetFramework"] == "net6.0", "%s: Release TargetFramework is net6.0" % version)
    checker.check(str(props["Optimize"]).lower() == "true", "%s: Release Optimize=true" % version)
    checker.check(REQUIRED_DEFINES <= defines, "%s: Release defines IL2CPP/BIE/BIE6" % version)
    checker.check(("KTC_GAME_243" in defines) == (version == "2.4.3"),
                  "%s: KTC_GAME_243 presence follows GameInteropVersion" % version)
    checker.check(props["AssemblyName"] == "KingdomEnhancedMod", "%s: AssemblyName unchanged" % version)

    compile_ids = [item["Identity"] for item in compile_items]
    debug_compile_ids = [item["Identity"] for item in debug_compile]
    checker.check(len(compile_ids) == len(set(compile_ids)), "%s: Compile identities are unique" % version)
    checker.check(set(compile_ids) == set(debug_compile_ids),
                  "%s: Release and Debug compile the same inputs" % version)
    checker.check(len(compile_ids) > 0, "%s: product Compile inputs discovered" % version)

    logical_names = resource_names(resources)
    debug_logical_names = resource_names(debug_resources)
    checker.check(len(logical_names) == len(set(logical_names)),
                  "%s: EmbeddedResource LogicalNames are unique" % version)
    checker.check(sorted(logical_names) == sorted(debug_logical_names),
                  "%s: Release and Debug EmbeddedResource LogicalNames match" % version)
    checker.check(len(logical_names) > 0, "%s: EmbeddedResource inputs discovered" % version)

    checker.check(references == debug_references,
                  "%s: Release and Debug Reference name/HintPath/Private/Aliases match" % version)
    checker.check(len(references) > 0, "%s: Reference closure discovered" % version)

    product_root = (ROOT / "il2cpp").resolve()
    source_ok = True
    for item in compile_items:
        full = Path(item["FullPath"]).resolve()
        if product_root not in full.parents or "obj" in full.parts or "bin" in full.parts:
            source_ok = False
            break
    checker.check(source_ok, "%s: Compile inputs stay in il2cpp and exclude obj/bin" % version)

    required_refs = {"0Harmony", "BepInEx.Core", "BepInEx.Unity.IL2CPP",
                     "Il2CppInterop.Runtime", "Il2CppInterop.Common",
                     "MonoMod.RuntimeDetour",
                     "Assembly-CSharp", "UnityEngine", "UnityEngine.AudioModule",
                     "UnityEngine.UIModule", "UnityEngine.UI"}
    checker.check(required_refs <= set(references),
                  "%s: direct IL2CPP/Unity references present" % version)

    print("INFO  %s Release: Compile(product)=%d, GeneratedMyPluginInfo=%d, Compile(total)=%d, "
          "EmbeddedResource=%d, Reference=%d"
          % (version, len(compile_items), EXPECTED_GENERATED, len(compile_items) + EXPECTED_GENERATED,
             len(resources), len(references)))
    return {
        "compile_product": len(compile_items),
        "generated": EXPECTED_GENERATED,
        "compile_total": len(compile_items) + EXPECTED_GENERATED,
        "resources": len(resources),
        "references": len(references),
    }


def check_generated_plugin_info(checker, dotnet, version, obj_dir, config, env, expected_version):
    cmd = [
        dotnet,
        "msbuild",
        PROJECT,
        "-nologo",
        "-t:AddGeneratedFile",
        "-p:Configuration=" + config,
        "-p:GameInteropVersion=" + version,
        "-p:BaseIntermediateOutputPath=%s/" % obj_dir,
        "-p:MSBuildProjectExtensionsPath=%s/" % obj_dir,
    ]
    rc, out = run(cmd, env)
    checker.check(rc == 0, "%s/%s: MyPluginInfo generation target succeeds" % (version, config))
    generated_dir = obj_dir / config
    found = list(generated_dir.rglob("MyPluginInfo.cs")) if generated_dir.exists() else []
    checker.check(len(found) == EXPECTED_GENERATED,
                  "%s/%s: exactly %d generated MyPluginInfo.cs" % (version, config, EXPECTED_GENERATED))
    generated = generated_dir / "MyPluginInfo.cs"
    checker.check(generated.is_file(),
                  "%s/%s: one generated MyPluginInfo.cs at %s" % (version, config, generated))
    if not generated.is_file():
        if rc:
            print(last_lines(out, 10))
        return None
    text = generated.read_text(encoding="utf-8")
    checker.check('PLUGIN_GUID = "KingdomEnhancedMod"' in text,
                  "%s/%s: generated PLUGIN_GUID is KingdomEnhancedMod" % (version, config))
    checker.check('PLUGIN_VERSION = "%s"' % expected_version in text,
                  "%s/%s: generated PLUGIN_VERSION is %s" % (version, config, expected_version))
    if rc:
        print(last_lines(out, 10))
    return text


def validate(dotnet, defines=None, version=None, project=None, env=None, config="Release", optimize=None):
    cmd = [
        dotnet,
        "msbuild",
        str(project or PROJECT),
        "-nologo",
        "-t:KTCValidateGameInteropVersion",
        "-p:Configuration=" + config,
    ]
    if version is not None:
        cmd.append("-p:GameInteropVersion=" + version)
    if defines is not None:
        cmd.append("-p:DefineConstants=" + defines.replace(";", "%3B"))
    if optimize is not None:
        cmd.append("-p:Optimize=" + optimize)
    return run(cmd, env)


def check_validation(checker, dotnet, run_dir, env):
    rc, out = validate(dotnet, version="2.4.0", env=env)
    checker.check(rc == 0, "validation accepts 2.4.0 with default macros")
    rc, out = validate(dotnet, version="2.4.3", env=env)
    checker.check(rc == 0, "validation accepts 2.4.3 with KTC_GAME_243")

    rc, out = validate(dotnet, version="2.4.5", env=env)
    checker.check(rc != 0 and "Unsupported GameInteropVersion" in out,
                  "validation rejects unknown GameInteropVersion")
    rc, out = validate(dotnet, version="2.4.3", defines="IL2CPP;BIE;BIE6", env=env)
    checker.check(rc != 0 and "requires the KTC_GAME_243" in out,
                  "validation rejects 2.4.3 without KTC_GAME_243")
    rc, out = validate(dotnet, version="2.4.0", defines="IL2CPP;BIE;BIE6;KTC_GAME_243", env=env)
    checker.check(rc != 0 and "must not define KTC_GAME_243" in out,
                  "validation rejects 2.4.0 with KTC_GAME_243")

    for missing, defines in (
        ("IL2CPP", "BIE;BIE6"),
        ("BIE", "IL2CPP;BIE6"),
        ("BIE6", "IL2CPP;BIE"),
    ):
        rc, out = validate(dotnet, version="2.4.0", defines=defines, env=env)
        checker.check(rc != 0 and "Release must" in out,
                      "validation rejects Release missing %s" % missing)

    rc, out = validate(dotnet, version="2.4.0", optimize="false", env=env)
    checker.check(rc != 0 and "must set Optimize=true" in out,
                  "validation rejects Release with Optimize=false")

    late = run_dir / "late243.csproj"
    late.write_text(
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
        "  <Import Project=\"%s\" />\n"
        "  <PropertyGroup>\n"
        "    <TargetFramework>net6.0</TargetFramework>\n"
        "    <Configuration>Release</Configuration>\n"
        "    <Platform>AnyCPU</Platform>\n"
        "    <GameInteropVersion>2.4.3</GameInteropVersion>\n"
        "  </PropertyGroup>\n"
        "</Project>\n" % SHARED_PROPS,
        encoding="utf-8",
    )
    rc, out = validate(dotnet, project=late, env=env)
    checker.check(rc != 0 and "requires the KTC_GAME_243" in out,
                  "validation rejects GameInteropVersion assigned after the import")


def check_no_deploy(checker, dotnet, run_dir, env):
    src = run_dir / "copy-src"
    dst = run_dir / "copy-dst"
    dst_release = run_dir / "copy-dst-release"
    for d in (src, dst, dst_release):
        d.mkdir(parents=True, exist_ok=True)
    (src / "KingdomEnhancedMod.dll").write_bytes(b"dll")
    (src / "KingdomEnhancedMod.pdb").write_bytes(b"pdb")

    base = [
        dotnet, "msbuild", PROJECT, "-nologo", "-t:CopyOutput",
        "-p:OutputPath=%s/" % src,
    ]
    rc, out = run(base + ["-p:Configuration=Debug", "-p:BepInExPluginsPath=%s/" % dst, "-p:NoDeploy=true"], env)
    deployed = list(dst.rglob("*"))
    checker.check(rc == 0 and not any(p.is_file() for p in deployed),
                  "NoDeploy=true suppresses Debug CopyOutput")
    rc, out = run(base + ["-p:Configuration=Debug", "-p:BepInExPluginsPath=%s/" % dst], env)
    checker.check(rc == 0 and (dst / "KingdomEnhancedMod" / "KingdomEnhancedMod.dll").is_file(),
                  "Debug CopyOutput still copies when NoDeploy is unset")
    rc, out = run(base + ["-p:Configuration=Release", "-p:BepInExPluginsPath=%s/" % dst_release], env)
    checker.check(rc == 0 and not any(p.is_file() for p in dst_release.rglob("*")),
                  "Release never invokes CopyOutput")


def build_probe(dotnet, run_dir, env):
    probe_dir = run_dir / "pe-probe"
    probe_dir.mkdir(parents=True, exist_ok=True)
    source = probe_dir / "Program.cs"
    project = probe_dir / "PeProbe.csproj"
    if not source.exists() or source.read_text(encoding="utf-8") != PROBE_SOURCE:
        source.write_text(PROBE_SOURCE, encoding="utf-8")
    if not project.exists() or project.read_text(encoding="utf-8") != PROBE_PROJECT:
        project.write_text(PROBE_PROJECT, encoding="utf-8")
    cmd = [
        dotnet, "build", project, "-c", "Release", "--nologo",
        "-p:NuGetAudit=false", "-p:UseSharedCompilation=false",
    ]
    rc, out = run(cmd, env)
    if rc:
        raise SystemExit("PE probe build failed:\n" + last_lines(out, 20))
    return probe_dir / "out" / "Release" / "net8.0" / "PeProbe.dll"


def parse_probe(output):
    blocks = {}
    current = None
    for line in output.splitlines():
        key, sep, value = line.partition("\t")
        if not sep:
            continue
        if key == "FILE":
            current = value
            blocks[current] = {}
        elif current is not None:
            blocks[current][key] = value
    return blocks


def check_pe(checker, dotnet, run_dir, env, dll_240, dll_243, expected_resources):
    targets = []
    if dll_240:
        targets.append(("2.4.0", Path(dll_240)))
    if dll_243:
        targets.append(("2.4.3", Path(dll_243)))
    if not targets:
        print("SKIP  final PE checks (pass --dll-240/--dll-243 with built DLLs)")
        return
    probe = build_probe(dotnet, run_dir, env)
    rc, out = run([dotnet, probe] + [str(path) for _, path in targets], env)
    if rc:
        raise SystemExit("PE probe failed:\n" + last_lines(out, 20))
    blocks = parse_probe(out)
    for version, path in targets:
        key = str(path)
        block = blocks.get(key)
        checker.check(block is not None, "%s: PE probe found %s" % (version, path.name))
        if not block:
            continue
        checker.check(block.get("TFM") == ".NETCoreApp,Version=v6.0",
                      "%s: PE TargetFramework is net6.0" % version)
        base = block.get("PLUGIN_BASE", "")
        checker.check(base.startswith("BepInEx.Unity.IL2CPP.BasePlugin"),
                      "%s: plugin derives from IL2CPP BasePlugin" % version)
        flags_text = block.get("DEBUGGABLE_FLAGS")
        checker.check(flags_text is not None, "%s: Debuggable attribute present" % version)
        flags = int(flags_text or "0", 16)
        checker.check(flags & 0x100 == 0,
                      "%s: Debuggable has no DisableOptimizations (0x%x)" % (version, flags))
        checker.check(block.get("MYPLUGININFO") == "True", "%s: MyPluginInfo type present" % version)
        checker.check(block.get("PLUGIN_GUID") == "KingdomEnhancedMod",
                      "%s: plugin GUID unchanged" % version)
        expected_count = expected_resources.get(version)
        checker.check(expected_count is not None,
                      "%s: evaluated resource count available" % version)
        checker.check(block.get("RESOURCES") == str(expected_count),
                      "%s: PE carries %s resources (evaluated)" % (version, expected_count))
        actual = set(filter(None, block.get("SAVE_REFS", "").split(",")))
        expected = SAVE_REFS[version]
        checker.check(actual == expected,
                      "%s: typed save refs are %s (got %s)" % (version, sorted(expected), sorted(actual)))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dll-240", help="built 2.4.0 Release DLL to inspect")
    parser.add_argument("--dll-243", help="built 2.4.3 Release DLL to inspect")
    args = parser.parse_args()

    dotnet = find_dotnet()
    run_dir = OUT_ROOT / ("script-%d" % int(time.time()))
    run_dir.mkdir(parents=True, exist_ok=True)
    env = dotnet_env(run_dir)
    checker = Checker()
    print("dotnet: " + dotnet)
    print("run dir: " + str(run_dir))

    expected_version = None
    counts = {}
    for version in ("2.4.0", "2.4.3"):
        obj_dir = run_dir / ("obj-" + version)
        obj_dir.mkdir(parents=True, exist_ok=True)
        ensure_restored(dotnet, version, obj_dir, env, run_dir)
        release = evaluate(dotnet, version, "Release", obj_dir, env)
        debug = evaluate(dotnet, version, "Debug", obj_dir, env)
        if expected_version is None:
            expected_version = release["Properties"]["Version"]
        counts[version] = check_evaluation(checker, version, release, debug)
        release_info = check_generated_plugin_info(checker, dotnet, version, obj_dir, "Release", env, expected_version)
        debug_info = check_generated_plugin_info(checker, dotnet, version, obj_dir, "Debug", env, expected_version)
        if release_info is not None and debug_info is not None:
            checker.check(release_info == debug_info,
                          "%s: Release and Debug generate identical MyPluginInfo.cs" % version)

    check_validation(checker, dotnet, run_dir, env)
    check_no_deploy(checker, dotnet, run_dir, env)
    check_pe(checker, dotnet, run_dir, env, args.dll_240, args.dll_243,
             {version: count["resources"] for version, count in counts.items()})
    receipt = {
        "run_dir": str(run_dir),
        "versions": counts,
        "dlls": {"2.4.0": args.dll_240, "2.4.3": args.dll_243},
    }
    (run_dir / "receipt.json").write_text(json.dumps(receipt, indent=2, ensure_ascii=False) + "\n",
                                          encoding="utf-8")
    print("receipt: " + str(run_dir / "receipt.json"))
    return checker.finish()


if __name__ == "__main__":
    sys.exit(main())
