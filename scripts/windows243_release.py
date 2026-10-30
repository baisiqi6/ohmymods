#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Windows 2.4.3 / 11.1.0 源码闭包导出与构建工具。

从 release/windows243-11.1.0/source-recipe.json 的不可变 Git commit:path 读取
285 个有 Git 来源的源文件、1 个三 hunk 补丁源、1 个生成源和 38 个资源，
逐项 SHA256 校验后导出为可独立构建的 Release.csproj 源码树。

用法：
  python3 scripts/windows243_release.py verify \
      --recipe release/windows243-11.1.0/source-recipe.json \
      --runtime-root <deps> --interop-root <interop>

  python3 scripts/windows243_release.py export \
      --recipe release/windows243-11.1.0/source-recipe.json \
      --output <new-empty-path> \
      --runtime-root <deps> --interop-root <interop> \
      [--zip <source.zip>] [--build --dotnet <dotnet>]

边界：
  - 只读 Git blob，不读工作区源码，不写游戏/存档/BepInEx。
  - output 必须不存在；任何 hash、数量、路径或引用不匹配都失败。
  - --build 只做 NoDeploy 编译，永不部署。
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path
from xml.sax.saxutils import escape

REPO = Path(__file__).resolve().parents[1]
RECIPE_SCHEMA = 1
SDK_VERSION = "8.0.425"
TARGET_FRAMEWORK = "net6.0"
FORBIDDEN_DEFINES = {"VOYAGE_SAVE_DIAGNOSTICS", "ROLE_TIMING", "ROLE_TIMING_DIAGNOSTICS", "ANDROID"}

SHA_RE = re.compile(r"^[0-9a-f]{64}$")
COMMIT_RE = re.compile(r"^[0-9a-f]{40}$")
SOURCE_PATH_RE = re.compile(r"^source/[A-Za-z0-9._-]+\.cs$")
RESOURCE_PATH_RE = re.compile(r"^assets/[A-Za-z0-9._-]+\.png$")
SOURCE_ORIGIN_RE = re.compile(r"^il2cpp/[A-Za-z0-9._-]+\.cs$")
RESOURCE_ORIGIN_RE = re.compile(r"^il2cpp/Assets/[A-Za-z0-9._-]+\.png$")
REFERENCE_RE = re.compile(r"^[A-Za-z0-9._-]+\.dll$")
INCLUDE_RE = re.compile(r"^[A-Za-z0-9._-]+$")


class ReleaseError(Exception):
    """Recipe/export/build 的可诊断失败。"""


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def load_recipe(path: Path) -> dict:
    try:
        recipe = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError:
        raise ReleaseError(f"recipe not found: {path}")
    except json.JSONDecodeError as exc:
        raise ReleaseError(f"recipe is not valid JSON: {path}: {exc}")
    validate_recipe(recipe)
    return recipe


def _require_sha(value, label: str) -> str:
    if not isinstance(value, str) or not SHA_RE.fullmatch(value):
        raise ReleaseError(f"{label}: expected lowercase sha256, got {value!r}")
    return value


def resolve_commit(recipe: dict, value, label: str) -> str:
    """Accept a provenance key or a full 40-hex commit; return the full commit."""
    if not isinstance(value, str):
        raise ReleaseError(f"{label}: expected Git commit key or hash, got {value!r}")
    commits = recipe["provenance"]["commits"]
    if value in commits:
        return commits[value]
    if COMMIT_RE.fullmatch(value) and value in commits.values():
        return value
    raise ReleaseError(f"{label}: commit {value!r} is not declared in provenance.commits")


def _require_rel(value, regex: re.Pattern, label: str) -> str:
    if not isinstance(value, str) or not regex.fullmatch(value):
        raise ReleaseError(f"{label}: unsafe or unexpected path {value!r}")
    return value


def validate_recipe(recipe: dict) -> None:
    if not isinstance(recipe, dict):
        raise ReleaseError("recipe root must be an object")
    if recipe.get("schemaVersion") != RECIPE_SCHEMA:
        raise ReleaseError(f"unsupported recipe schemaVersion: {recipe.get('schemaVersion')!r}")
    provenance = recipe.get("provenance")
    if not isinstance(provenance, dict) or not isinstance(provenance.get("commits"), dict) or not provenance["commits"]:
        raise ReleaseError("provenance.commits is required")
    for key, commit in provenance["commits"].items():
        if not isinstance(key, str) or not key:
            raise ReleaseError("provenance.commits contains an empty key")
        if not isinstance(commit, str) or not COMMIT_RE.fullmatch(commit):
            raise ReleaseError(f"provenance.commits[{key}]: invalid commit {commit!r}")

    sdk = recipe.get("sdk") or {}
    if sdk.get("version") != SDK_VERSION:
        raise ReleaseError(f"sdk.version must be {SDK_VERSION}, got {sdk.get('version')!r}")
    if sdk.get("targetFramework") != TARGET_FRAMEWORK:
        raise ReleaseError(f"sdk.targetFramework must be {TARGET_FRAMEWORK}, got {sdk.get('targetFramework')!r}")

    properties = recipe.get("properties") or {}
    definitions = properties.get("defineConstants")
    if not isinstance(definitions, list) or not all(isinstance(x, str) and x for x in definitions):
        raise ReleaseError("properties.defineConstants must be a non-empty string list")
    lowered = {x.upper() for x in definitions}
    forbidden = sorted(lowered & FORBIDDEN_DEFINES)
    if forbidden:
        raise ReleaseError(f"properties.defineConstants contains forbidden macro(s): {forbidden}")
    for required in ("IL2CPP", "BIE", "BIE6", "DEBUG", "TRACE", "KTC_GAME_243"):
        if required not in lowered:
            raise ReleaseError(f"properties.defineConstants lacks {required}")
    if properties.get("optimize") is not True:
        raise ReleaseError("properties.optimize must be true (installed Windows B behavior)")
    if str(properties.get("debugType", "")).lower() != "portable":
        raise ReleaseError(
            "properties.debugType must be 'portable': installed B was compiled in DebugPlus mode "
            "(PDB optimization=release-debug-plus); DebugType=None changes business IL")
    if properties.get("debugSymbols") is not True:
        raise ReleaseError("properties.debugSymbols must be true to preserve DebugPlus IL")
    if properties.get("pdbShipped") is not False:
        raise ReleaseError("properties.pdbShipped must be false")
    if properties.get("sanitizeCodeView") is not True:
        raise ReleaseError("properties.sanitizeCodeView must be true")
    if properties.get("noDeploy") is not True:
        raise ReleaseError("properties.noDeploy must be true")
    if properties.get("includeSourceRevisionInInformationalVersion") is not False:
        raise ReleaseError("properties.includeSourceRevisionInInformationalVersion must be false")

    sources = recipe.get("sources")
    resources = recipe.get("resources")
    references = recipe.get("references")
    if not isinstance(sources, list) or not sources:
        raise ReleaseError("sources must be a non-empty list")
    if not isinstance(resources, list) or not resources:
        raise ReleaseError("resources must be a non-empty list")
    if not isinstance(references, list) or not references:
        raise ReleaseError("references must be a non-empty list")

    counts = provenance.get("counts") or {}
    expected = {"sources": len(sources), "resources": len(resources), "references": len(references)}
    for key, value in expected.items():
        if counts.get(key) != value:
            raise ReleaseError(f"provenance.counts.{key}={counts.get(key)!r}, actual {value}")

    seen = set()
    for index, entry in enumerate(sources):
        label = f"sources[{index}]"
        if not isinstance(entry, dict):
            raise ReleaseError(f"{label}: must be an object")
        path = _require_rel(entry.get("path"), SOURCE_PATH_RE, label + ".path")
        if path in seen:
            raise ReleaseError(f"{label}: duplicate source path {path}")
        seen.add(path)
        _require_sha(entry.get("sha256"), label + ".sha256")
        has_origin = "origin" in entry
        has_generated = "generated" in entry
        if has_origin == has_generated:
            raise ReleaseError(f"{label}: exactly one of origin/generated is required")
        if has_origin:
            origin = entry["origin"]
            if not isinstance(origin, dict):
                raise ReleaseError(f"{label}.origin: must be an object")
            resolve_commit(recipe, origin.get("commit"), label + ".origin.commit")
            _require_rel(origin.get("path"), SOURCE_ORIGIN_RE, label + ".origin.path")
            _require_sha(origin.get("sha256"), label + ".origin.sha256")
            edits = entry.get("edits", [])
            if not isinstance(edits, list):
                raise ReleaseError(f"{label}.edits: must be a list")
            for edit_index, edit in enumerate(edits):
                if not isinstance(edit, dict):
                    raise ReleaseError(f"{label}.edits[{edit_index}]: must be an object")
                old = edit.get("old")
                new = edit.get("new")
                if not isinstance(old, str) or not old:
                    raise ReleaseError(f"{label}.edits[{edit_index}].old must be non-empty")
                if not isinstance(new, str):
                    raise ReleaseError(f"{label}.edits[{edit_index}].new must be a string")
        else:
            generated = entry["generated"]
            if not isinstance(generated, dict) or not isinstance(generated.get("content"), str):
                raise ReleaseError(f"{label}.generated.content must be a string")
            if entry.get("edits"):
                raise ReleaseError(f"{label}: generated sources cannot have edits")

    seen = set()
    for index, entry in enumerate(resources):
        label = f"resources[{index}]"
        if not isinstance(entry, dict):
            raise ReleaseError(f"{label}: must be an object")
        path = _require_rel(entry.get("path"), RESOURCE_PATH_RE, label + ".path")
        if path in seen:
            raise ReleaseError(f"{label}: duplicate resource path {path}")
        seen.add(path)
        logical = entry.get("logicalName")
        if not isinstance(logical, str) or not logical or "/" in logical or "\\" in logical:
            raise ReleaseError(f"{label}.logicalName: invalid {logical!r}")
        _require_sha(entry.get("sha256"), label + ".sha256")
        origin = entry.get("origin")
        if not isinstance(origin, dict):
            raise ReleaseError(f"{label}.origin: must be an object")
        resolve_commit(recipe, origin.get("commit"), label + ".origin.commit")
        _require_rel(origin.get("path"), RESOURCE_ORIGIN_RE, label + ".origin.path")
        _require_sha(origin.get("sha256"), label + ".origin.sha256")

    seen = set()
    for index, entry in enumerate(references):
        label = f"references[{index}]"
        if not isinstance(entry, dict):
            raise ReleaseError(f"{label}: must be an object")
        logical = entry.get("logicalName")
        if not isinstance(logical, str) or not REFERENCE_RE.fullmatch(logical):
            raise ReleaseError(f"{label}.logicalName: invalid {logical!r}")
        include = entry.get("include")
        if not isinstance(include, str) or not INCLUDE_RE.fullmatch(include):
            raise ReleaseError(f"{label}.include: invalid {include!r}")
        if logical in seen:
            raise ReleaseError(f"{label}: duplicate reference {logical}")
        seen.add(logical)
        if entry.get("root") not in ("runtime", "interop"):
            raise ReleaseError(f"{label}.root must be 'runtime' or 'interop'")
        aliases = entry.get("aliases")
        if aliases is not None and (not isinstance(aliases, str) or not re.fullmatch(r"[A-Za-z0-9_.,-]+", aliases)):
            raise ReleaseError(f"{label}.aliases: invalid {aliases!r}")
        _require_sha(entry.get("sha256"), label + ".sha256")


def git_blob(repo: Path, commit: str, git_path: str) -> bytes:
    process = subprocess.run(
        ["git", "-C", str(repo), "cat-file", "blob", f"{commit}:{git_path}"],
        capture_output=True,
    )
    if process.returncode != 0:
        detail = process.stderr.decode("utf-8", "replace").strip()
        raise ReleaseError(f"missing Git blob {commit}:{git_path}: {detail}")
    return process.stdout


def apply_edits(data: bytes, edits: list, label: str) -> bytes:
    for index, edit in enumerate(edits):
        old = edit["old"].encode("utf-8")
        new = edit["new"].encode("utf-8")
        count = data.count(old)
        if count != 1:
            raise ReleaseError(f"{label}.edits[{index}]: expected exactly one match, found {count}")
        data = data.replace(old, new, 1)
    return data


def resolve_references(recipe: dict, runtime_root: Path, interop_root: Path) -> dict:
    roots = {"runtime": runtime_root, "interop": interop_root}
    for name, root in roots.items():
        if not root.is_dir():
            raise ReleaseError(f"--{name}-root is not a directory: {root}")
    for entry in recipe["references"]:
        root = roots[entry["root"]]
        path = root / entry["logicalName"]
        if not path.is_file():
            raise ReleaseError(f"missing reference {entry['root']}/{entry['logicalName']} under {root}")
        actual = sha256_file(path)
        if actual != entry["sha256"]:
            raise ReleaseError(
                f"reference sha256 mismatch {entry['root']}/{entry['logicalName']}: "
                f"expected {entry['sha256']}, got {actual}")
    return {
        "runtime": sum(1 for x in recipe["references"] if x["root"] == "runtime"),
        "interop": sum(1 for x in recipe["references"] if x["root"] == "interop"),
    }


def materialize(recipe: dict, repo: Path) -> dict:
    files = {}
    for entry in recipe["sources"]:
        path = entry["path"]
        if "generated" in entry:
            data = entry["generated"]["content"].encode("utf-8")
        else:
            origin = entry["origin"]
            data = git_blob(repo, resolve_commit(recipe, origin["commit"], path + ".origin.commit"), origin["path"])
            origin_sha = sha256_bytes(data)
            if origin_sha != origin["sha256"]:
                raise ReleaseError(
                    f"{path}: Git origin sha256 mismatch at {origin['commit']}:{origin['path']}: "
                    f"expected {origin['sha256']}, got {origin_sha}")
            data = apply_edits(data, entry.get("edits", []), path)
        actual = sha256_bytes(data)
        if actual != entry["sha256"]:
            raise ReleaseError(f"{path}: export sha256 mismatch: expected {entry['sha256']}, got {actual}")
        files[path] = data

    for entry in recipe["resources"]:
        path = entry["path"]
        origin = entry["origin"]
        data = git_blob(repo, resolve_commit(recipe, origin["commit"], path + ".origin.commit"), origin["path"])
        origin_sha = sha256_bytes(data)
        if origin_sha != origin["sha256"]:
            raise ReleaseError(
                f"{path}: resource origin sha256 mismatch at {origin['commit']}:{origin['path']}: "
                f"expected {origin['sha256']}, got {origin_sha}")
        actual = sha256_bytes(data)
        if actual != entry["sha256"]:
            raise ReleaseError(f"{path}: resource export sha256 mismatch: expected {entry['sha256']}, got {actual}")
        files[path] = data
    return files


def _project_xml(recipe: dict) -> str:
    properties = recipe["properties"]
    lines = [
        '<?xml version="1.0" encoding="utf-8"?>',
        '<Project Sdk="Microsoft.NET.Sdk">',
        '  <Import Project="props/ProjectSettings.shared.props" />',
        '  <Import Project="props/EmbeddedAssets.props" />',
        '  <PropertyGroup>',
        f'    <AssemblyName>{escape(properties["assemblyName"])}</AssemblyName>',
        f'    <RootNamespace>{escape(properties["rootNamespace"])}</RootNamespace>',
        f'    <Configuration Condition="\'$(Configuration)\' == \'\'">{escape(properties["configuration"])}</Configuration>',
        f'    <Platform Condition="\'$(Platform)\' == \'\'">{escape(properties["platform"])}</Platform>',
        f'    <Version>{escape(properties["version"])}</Version>',
        f'    <AssemblyVersion>{escape(properties["assemblyVersion"])}</AssemblyVersion>',
        f'    <FileVersion>{escape(properties["fileVersion"])}</FileVersion>',
        f'    <InformationalVersion>{escape(properties["informationalVersion"])}</InformationalVersion>',
        '    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>',
        f'    <Description>{escape(properties["description"])}</Description>',
        f'    <LangVersion>{escape(properties["langVersion"])}</LangVersion>',
        '    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>',
        f'    <Nullable>{escape(properties["nullable"])}</Nullable>',
        f'    <ImplicitUsings>{escape(properties["implicitUsings"])}</ImplicitUsings>',
        '    <EnableDefaultItems>false</EnableDefaultItems>',
        '    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>',
        '    <EnableDefaultEmbeddedResourceItems>false</EnableDefaultEmbeddedResourceItems>',
        '    <Deterministic>true</Deterministic>',
        '    <Optimize>true</Optimize>',
        '    <DebugType>portable</DebugType>',
        '    <DebugSymbols>true</DebugSymbols>',
        '    <NoDeploy>true</NoDeploy>',
        '    <BepInExPluginsPath />',
        '    <UseSharedCompilation>false</UseSharedCompilation>',
        '    <GenerateAspNetCertificate>false</GenerateAspNetCertificate>',
        '    <CheckEolTargetFramework>false</CheckEolTargetFramework>',
        '    <RuntimeRoot Condition="\'$(RuntimeRoot)\' == \'\'"></RuntimeRoot>',
        '    <InteropRoot Condition="\'$(InteropRoot)\' == \'\'"></InteropRoot>',
        '    <PathMap>$([System.IO.Path]::Combine($(MSBuildProjectDirectory),\'source\'))=/_/source,$([System.IO.Path]::Combine($(MSBuildProjectDirectory),\'obj\'))=/_/obj</PathMap>',
        '  </PropertyGroup>',
        '  <Target Name="KtcValidateRoots" BeforeTargets="ResolveAssemblyReferences">',
        '    <Error Condition="\'$(RuntimeRoot)\' == \'\'" Text="RuntimeRoot is required (fixed 31-DLL runtime closure)" />',
        '    <Error Condition="\'$(InteropRoot)\' == \'\'" Text="InteropRoot is required (fixed 131-DLL interop closure)" />',
        '  </Target>',
        '  <ItemGroup>',
    ]
    for entry in recipe["sources"]:
        lines.append(f'    <Compile Include="{escape(entry["path"])}" />')
    lines += ['  </ItemGroup>', '  <ItemGroup>']
    for entry in recipe["references"]:
        root = "RuntimeRoot" if entry["root"] == "runtime" else "InteropRoot"
        lines += [
            f'    <Reference Include="{escape(entry["include"])}">',
            f'      <HintPath>$({root})/{escape(entry["logicalName"])}</HintPath>',
            '      <Private>false</Private>',
        ]
        if entry.get("aliases"):
            lines.append(f'      <Aliases>{escape(entry["aliases"])}</Aliases>')
        lines += [
            '    </Reference>',
        ]
    lines += ['  </ItemGroup>', '</Project>', '']
    return "\n".join(lines)


def _settings_props(recipe: dict) -> str:
    definitions = ";".join(recipe["properties"]["defineConstants"])
    return "\n".join([
        '<?xml version="1.0" encoding="utf-8"?>',
        '<Project>',
        '  <PropertyGroup>',
        f'    <TargetFramework>{escape(recipe["sdk"]["targetFramework"])}</TargetFramework>',
        '    <GameInteropVersion>2.4.3</GameInteropVersion>',
        f'    <DefineConstants>{escape(definitions)}</DefineConstants>',
        '  </PropertyGroup>',
        '</Project>',
        '',
    ])


def _assets_props(recipe: dict) -> str:
    lines = [
        '<?xml version="1.0" encoding="utf-8"?>',
        '<Project>',
        '  <ItemGroup>',
    ]
    for entry in recipe["resources"]:
        lines.append(
            '    <EmbeddedResource Include="$(MSBuildThisFileDirectory)../'
            + escape(entry["path"])
            + '" LogicalName="'
            + escape(entry["logicalName"])
            + '" />')
    lines += ['  </ItemGroup>', '</Project>', '']
    return "\n".join(lines)


def _global_json() -> str:
    return json.dumps(
        {"sdk": {"version": SDK_VERSION, "rollForward": "disable", "allowPrerelease": False}},
        indent=2) + "\n"


def _verify_closure_script() -> str:
    return '''#!/usr/bin/env python3
"""Verify the fixed runtime/interop closure against the pinned recipe shas."""
import argparse
import hashlib
import json
from pathlib import Path


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--recipe", default=str(Path(__file__).with_name("source-recipe.json")))
    parser.add_argument("--runtime-root", required=True, type=Path)
    parser.add_argument("--interop-root", required=True, type=Path)
    args = parser.parse_args()
    recipe = json.loads(Path(args.recipe).read_text(encoding="utf-8"))
    roots = {"runtime": args.runtime_root, "interop": args.interop_root}
    checked = {"runtime": 0, "interop": 0}
    for entry in recipe["references"]:
        root = roots[entry["root"]]
        path = root / entry["logicalName"]
        if not path.is_file():
            raise SystemExit("missing reference %s/%s under %s" % (entry["root"], entry["logicalName"], root))
        actual = sha256(path)
        if actual != entry["sha256"]:
            raise SystemExit("reference sha256 mismatch %s/%s: expected %s, got %s"
                             % (entry["root"], entry["logicalName"], entry["sha256"], actual))
        checked[entry["root"]] += 1
    print("closure ok: runtime=%d interop=%d total=%d"
          % (checked["runtime"], checked["interop"], sum(checked.values())))


if __name__ == "__main__":
    main()
'''


def _sanitize_debug_script() -> str:
    return r'''#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import struct
import sys
from pathlib import Path


def sanitize(path):
    data = bytearray(Path(path).read_bytes())
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    if data[pe:pe + 4] != b"PE\0\0":
        raise SystemExit("invalid PE signature")
    sections = struct.unpack_from("<H", data, pe + 6)[0]
    optional_size = struct.unpack_from("<H", data, pe + 20)[0]
    optional = pe + 24
    magic = struct.unpack_from("<H", data, optional)[0]
    directory = optional + (112 if magic == 0x20B else 96)
    debug_rva, debug_size = struct.unpack_from("<II", data, directory + 6 * 8)
    if not debug_size:
        raise SystemExit("no PE debug directory")
    section_offset = optional + optional_size
    debug_offset = None
    for index in range(sections):
        entry = section_offset + index * 40
        virtual_size, virtual_address, raw_size, raw_pointer = struct.unpack_from(
            "<IIII", data, entry + 8)
        if virtual_address <= debug_rva < virtual_address + max(virtual_size, raw_size):
            debug_offset = debug_rva - virtual_address + raw_pointer
            break
    if debug_offset is None:
        raise SystemExit("debug directory RVA is not mapped")
    removed = 0
    for index in range(debug_size // 28):
        entry = debug_offset + index * 28
        debug_type = struct.unpack_from("<I", data, entry + 12)[0]
        size = struct.unpack_from("<I", data, entry + 16)[0]
        pointer = struct.unpack_from("<I", data, entry + 24)[0]
        if debug_type not in (2, 17, 19):
            continue
        if size and pointer:
            if pointer + size > len(data):
                raise SystemExit("debug payload outside DLL")
            data[pointer:pointer + size] = b"\0" * size
        data[entry:entry + 28] = b"\0" * 28
        removed += 1
    if not removed:
        raise SystemExit("no CodeView/PDB debug entries found")
    Path(path).write_bytes(data)
    print("debug entries removed:", removed)


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("usage: sanitize_debug.py KingdomEnhancedMod.dll")
    sanitize(sys.argv[1])
'''


def _building_markdown(recipe: dict) -> str:
    counts = recipe["provenance"]["counts"]
    return f'''# KingdomEnhancedMod {recipe["release"]} Windows x64 source snapshot

This archive rebuilds `KingdomEnhancedMod.dll` for {recipe["target"]["game"]} from the pinned
source closure. It does **not** contain the game, BepInEx, interop assemblies, saves or configs.

## Contents

- `source/` — {counts["sources"]} C# files ({counts["gitContentSources"]} existing Git blobs,
  {counts["patchedSources"]} patched, {counts["generatedSources"]} generated)
- `assets/` — {counts["resources"]} embedded PNG resources
- `props/` — target framework/defines and embedded-resource items
- `source-recipe.json` — Git commit/path, SHA256, macro and reference closure
- `Release.csproj` — {counts["sources"]} Compile, {counts["resources"]} EmbeddedResource,
  {counts["references"]} Reference items
- `verify_closure.py` — re-checks all reference SHA256 values
- `sanitize_debug.py` — removes CodeView/PDB debug entries after a direct build
- `global.json` — pins .NET SDK {SDK_VERSION}

## Requirements

- .NET SDK {SDK_VERSION} exactly (`global.json` pins it; verify with `dotnet --version`)
- `Microsoft.NETCore.App.Ref` 6.0.36 and `Microsoft.AspNetCore.App.Ref` 6.0.36
  (restored by NuGet or already present in the NuGet cache)
- The fixed closure of {counts["references"]} reference DLLs supplied at build time:
  - runtime root: 31 BepInEx/core tool DLLs
  - interop root: 131 generated IL2CPP binding DLLs

## Verify the closure

```sh
python3 verify_closure.py --recipe source-recipe.json \\
    --runtime-root /path/to/runtime-closure \\
    --interop-root /path/to/interop-closure
```

## Build

```sh
dotnet build Release.csproj -c Debug \\
    -p:RuntimeRoot=/path/to/runtime-closure \\
    -p:InteropRoot=/path/to/interop-closure \\
    -p:NoDeploy=true -p:BepInExPluginsPath=
```

Output: `bin/Debug/net6.0/KingdomEnhancedMod.dll`.

The installed Windows B build was compiled by Roslyn in DebugPlus mode
(`DebugType=portable`, `DebugSymbols=true`, `Optimize=true`; PDB optimization
`release-debug-plus`). Reproducing that mode is required: `DebugType=None` changes business IL.
For a release artifact, do **not** ship the PDB. After a direct `dotnet build`, run:

```sh
python3 sanitize_debug.py bin/Debug/net6.0/KingdomEnhancedMod.dll
```

Then delete every `.pdb` under `bin/` and `obj/` before packaging.

`sanitize_debug.py` zeroes PE debug entries of type 2 (CodeView), 17 (embedded PDB) and
19 (PDB checksum); the deterministic REPRO entry and all IL/metadata remain untouched.
`scripts/windows243_release.py export --build` performs the build, sanitize and PDB removal
automatically. `DEBUG;TRACE;Optimize=true` are preserved, and `VOYAGE_SAVE_DIAGNOSTICS` is
intentionally off. For direct `dotnet` builds, run `verify_closure.py` first.

## Provenance

- base: `{recipe["provenance"]["commits"]["base"]}`
- PR282: `{recipe["provenance"]["commits"]["pr282"]}`
- gems/quick: `{recipe["provenance"]["commits"]["gems"]}`

The only source differences from the installed Windows B tree are the release build-log line in
`KingdomEnhancedPlugin.cs` and the generated `MyPluginInfo.cs` (version {recipe["release"]}).
`ModPanel.cs` is base plus the three exact edits recorded in `source-recipe.json`.
No separate patch file is required. No RoleTiming/U overlay sources are included. Assembly version metadata is {recipe["release"]}.
'''


def write_export(recipe: dict, files: dict, output: Path, recipe_bytes: bytes) -> None:
    created = False
    if output.exists():
        raise ReleaseError(f"output already exists (dirty existing export): {output}")
    output.parent.mkdir(parents=True, exist_ok=True)
    try:
        output.mkdir()
        created = True
        for relative, data in files.items():
            target = output / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        project = output / "Release.csproj"
        project.write_text(_project_xml(recipe), encoding="utf-8")
        props = output / "props"
        props.mkdir()
        (props / "ProjectSettings.shared.props").write_text(_settings_props(recipe), encoding="utf-8")
        (props / "EmbeddedAssets.props").write_text(_assets_props(recipe), encoding="utf-8")
        (output / "global.json").write_text(_global_json(), encoding="utf-8")
        (output / "BUILDING.md").write_text(_building_markdown(recipe), encoding="utf-8")
        (output / "verify_closure.py").write_text(_verify_closure_script(), encoding="utf-8")
        (output / "sanitize_debug.py").write_text(_sanitize_debug_script(), encoding="utf-8")
        (output / "source-recipe.json").write_bytes(recipe_bytes)
    except BaseException:
        if created:
            shutil.rmtree(output, ignore_errors=True)
        raise


def write_source_zip(output: Path, zip_path: Path) -> None:
    if zip_path.exists():
        raise ReleaseError(f"source zip already exists: {zip_path}")
    try:
        zip_path.relative_to(output)
    except ValueError:
        pass
    else:
        raise ReleaseError("source zip must be outside the export directory")
    zip_path.parent.mkdir(parents=True, exist_ok=True)
    entries = []
    for path in sorted(output.rglob("*")):
        if not path.is_file():
            continue
        relative = path.relative_to(output).as_posix()
        if relative.startswith(("bin/", "obj/")) or relative.endswith(".pdb"):
            continue
        entries.append((relative, path.read_bytes()))
    with zipfile.ZipFile(zip_path, "x", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for relative, data in entries:
            info = zipfile.ZipInfo(relative, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            archive.writestr(info, data)


def sanitize_debug_info(dll: Path) -> int:
    """Remove CodeView/embedded-PDB/PDB-checksum debug entries from the built DLL.

    The installed Windows B build used Roslyn DebugPlus (portable PDB, Optimize=true).
    Keeping that mode is required for identical business IL, but the release artifact must
    not ship a PDB or embed a host PDB path. Only PE debug-directory entries of types
    2 (CodeView), 17 (embedded PDB) and 19 (PDB checksum) are zeroed; IL/metadata is untouched.
    """
    import struct

    data = bytearray(dll.read_bytes())
    if len(data) < 0x40:
        raise ReleaseError(f"DLL too small to contain a PE header: {dll}")
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    if data[pe:pe + 4] != b"PE\0\0":
        raise ReleaseError(f"invalid PE signature: {dll}")
    sections = struct.unpack_from("<H", data, pe + 6)[0]
    optional_size = struct.unpack_from("<H", data, pe + 20)[0]
    optional = pe + 24
    magic = struct.unpack_from("<H", data, optional)[0]
    if magic not in (0x10B, 0x20B):
        raise ReleaseError(f"unsupported PE optional-header magic {magic:#x}")
    directory = optional + (112 if magic == 0x20B else 96)
    debug_rva, debug_size = struct.unpack_from("<II", data, directory + 6 * 8)
    if not debug_size:
        raise ReleaseError("built DLL has no PE debug directory; expected a portable PDB CodeView entry")
    debug_offset = None
    section_offset = optional + optional_size
    for index in range(sections):
        entry = section_offset + index * 40
        virtual_size, virtual_address, raw_size, raw_pointer = struct.unpack_from(
            "<IIII", data, entry + 8)
        if virtual_address <= debug_rva < virtual_address + max(virtual_size, raw_size):
            debug_offset = debug_rva - virtual_address + raw_pointer
            break
    if debug_offset is None:
        raise ReleaseError("PE debug directory RVA is not mapped by any section")
    removed = 0
    for index in range(debug_size // 28):
        entry = debug_offset + index * 28
        debug_type, size, pointer = struct.unpack_from(
            "<III", data, entry + 12)[0], struct.unpack_from("<I", data, entry + 16)[0], \
            struct.unpack_from("<I", data, entry + 24)[0]
        if debug_type not in (2, 17, 19):
            continue
        if size and pointer:
            if pointer + size > len(data):
                raise ReleaseError(f"debug payload outside DLL: type={debug_type} size={size} ptr={pointer}")
            data[pointer:pointer + size] = b"\0" * size
        data[entry:entry + 28] = b"\0" * 28
        removed += 1
    if not removed:
        raise ReleaseError("no CodeView/PDB debug entries found; refusing to ship unverified debug info")
    dll.write_bytes(data)
    return removed


def export_pdb_path(output: Path, assembly: str) -> Path:
    """PDB lives inside its own export tree; no process-global or shared path."""
    return output / "obj" / "pdb" / f"{assembly}.pdb"


def dotnet_sdk_version(dotnet: str, cwd: Path) -> str:
    """Run dotnet --version with cwd=export so the source tree's global.json applies."""
    process = subprocess.run([dotnet, "--version"], cwd=cwd, capture_output=True, text=True)
    if process.returncode != 0:
        raise ReleaseError(f"dotnet --version failed in {cwd}: {process.stderr.strip()}")
    return process.stdout.strip()


def build_export(output: Path, dotnet: str, runtime_root: Path, interop_root: Path, assembly: str) -> dict:
    project = output / "Release.csproj"
    if not project.is_file():
        raise ReleaseError(f"missing exported project: {project}")
    dotnet_path = Path(dotnet)
    if not dotnet_path.exists() and shutil.which(dotnet) is None:
        raise ReleaseError(f"dotnet not found: {dotnet}")
    version = dotnet_sdk_version(dotnet, output)
    if version != SDK_VERSION:
        raise ReleaseError(f"dotnet SDK {SDK_VERSION} required by export global.json, found {version!r}")

    # PDB is unique to this export directory. Parallel exports cannot share or delete
    # each other's file. The PDB is deleted and CodeView entries are sanitized afterwards.
    pdb_path = export_pdb_path(output, assembly)
    pdb_path.parent.mkdir(parents=True, exist_ok=True)
    command = [
        dotnet, "build", str(project), "-c", "Debug", "-m:1", "-v:minimal", "-nologo",
        "--disable-build-servers",
        f"-p:RuntimeRoot={runtime_root}",
        f"-p:InteropRoot={interop_root}",
        "-p:NoDeploy=true", "-p:BepInExPluginsPath=",
        "-p:UseSharedCompilation=false", "-p:NuGetAudit=false",
        f"-p:PdbFile={pdb_path}",
    ]
    process = subprocess.run(command, cwd=output, capture_output=True, text=True)
    text = (process.stdout or "") + (process.stderr or "")
    print(text.rstrip())
    if process.returncode != 0:
        raise ReleaseError(f"dotnet build failed with exit {process.returncode}")
    warnings = [int(x) for x in re.findall(r"(\d+) Warning\(s\)", text)]
    errors = [int(x) for x in re.findall(r"(\d+) Error\(s\)", text)]
    if warnings and warnings[-1] != 0:
        raise ReleaseError(f"dotnet build reported {warnings[-1]} warning(s)")
    if errors and errors[-1] != 0:
        raise ReleaseError(f"dotnet build reported {errors[-1]} error(s)")

    dll = output / "bin" / "Debug" / TARGET_FRAMEWORK / f"{assembly}.dll"
    if not dll.is_file():
        raise ReleaseError(f"missing exact build output: {dll}")
    removed = sanitize_debug_info(dll)
    pdb_files = sorted(output.glob("**/*.pdb"))
    for pdb in pdb_files:
        pdb.unlink()
    payload = dll.read_bytes()
    if b"RSDS" in payload or b".pdb" in payload:
        raise ReleaseError("debug sanitize left a PDB reference/path in the DLL")
    return {
        "dll": str(dll),
        "dllSha256": sha256_file(dll),
        "dllSize": dll.stat().st_size,
        "warnings": warnings[-1] if warnings else 0,
        "errors": errors[-1] if errors else 0,
        "codeViewEntriesRemoved": removed,
        "pdbRemoved": len(pdb_files),
        "pdbPath": str(pdb_path),
        "debugType": "portable",
    }


def export_source(
    recipe_path: Path,
    output: Path,
    repo: Path,
    runtime_root: Path,
    interop_root: Path,
    zip_path: Path | None = None,
    build: bool = False,
    dotnet: str = "dotnet",
) -> dict:
    recipe_path = Path(recipe_path)
    output = Path(output)
    repo = Path(repo)
    recipe = load_recipe(recipe_path)
    recipe_bytes = recipe_path.read_bytes()
    if output.exists():
        raise ReleaseError(f"output already exists (dirty existing export): {output}")
    if zip_path is not None and Path(zip_path).exists():
        raise ReleaseError(f"source zip already exists: {zip_path}")
    git_dir = subprocess.run(
        ["git", "-C", str(repo), "rev-parse", "--git-dir"], capture_output=True, text=True)
    if git_dir.returncode != 0:
        raise ReleaseError(f"not a Git repository: {repo}")
    roots = resolve_references(recipe, Path(runtime_root), Path(interop_root))
    files = materialize(recipe, repo)
    write_export(recipe, files, output, recipe_bytes)
    result = {
        "output": str(output),
        "sources": len(recipe["sources"]),
        "resources": len(recipe["resources"]),
        "references": len(recipe["references"]),
        "closure": roots,
    }
    if zip_path is not None:
        write_source_zip(output, Path(zip_path))
        result["zip"] = str(zip_path)
        result["zipSha256"] = sha256_file(Path(zip_path))
    if build:
        result["build"] = build_export(
            output, dotnet, Path(runtime_root), Path(interop_root), recipe["properties"]["assemblyName"])
    return result


def verify_only(recipe_path: Path, repo: Path, runtime_root: Path, interop_root: Path) -> dict:
    recipe = load_recipe(Path(recipe_path))
    roots = resolve_references(recipe, Path(runtime_root), Path(interop_root))
    files = materialize(recipe, Path(repo))
    return {"sources": len(recipe["sources"]), "resources": len(recipe["resources"]),
            "references": len(recipe["references"]), "closure": roots,
            "verifiedSourceBytes": sum(len(files[x["path"]]) for x in recipe["sources"]),
            "verifiedResourceBytes": sum(len(files[x["path"]]) for x in recipe["resources"])}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Windows 2.4.3 / 11.1.0 source closure export")
    sub = parser.add_subparsers(dest="command", required=True)

    verify = sub.add_parser("verify", help="validate Git sources/resources/edits and reference closure")
    verify.add_argument("--recipe", required=True, type=Path)
    verify.add_argument("--repo", type=Path, default=REPO)
    verify.add_argument("--runtime-root", required=True, type=Path)
    verify.add_argument("--interop-root", required=True, type=Path)

    export = sub.add_parser("export", help="export a self-contained source tree")
    export.add_argument("--recipe", required=True, type=Path)
    export.add_argument("--output", required=True, type=Path)
    export.add_argument("--repo", type=Path, default=REPO)
    export.add_argument("--runtime-root", required=True, type=Path)
    export.add_argument("--interop-root", required=True, type=Path)
    export.add_argument("--zip", type=Path)
    export.add_argument("--build", action="store_true")
    export.add_argument("--dotnet", default="dotnet")

    args = parser.parse_args(argv)
    try:
        if args.command == "verify":
            result = verify_only(args.recipe, args.repo, args.runtime_root, args.interop_root)
        else:
            result = export_source(
                args.recipe, args.output, args.repo, args.runtime_root, args.interop_root,
                zip_path=args.zip, build=args.build, dotnet=args.dotnet)
    except ReleaseError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
