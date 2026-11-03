#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ohmymods IL2CPP 发布打包脚本。

旧模式（保持不变）：python scripts/package_release.py <ModVersion>   # 如 3.0.0
Windows 2.4.3 完整 Mod 模式：
    python scripts/package_release.py --platform win243-x64 \
        --runtime-root <player-package> --bootstrap-root <bootstrap-readback> \
        --mod-dll <KingdomEnhancedMod.dll> --output-dir <dir> \
        --commit <40-hex> [--source-archive <source.zip>]

旧模式前置：il2cpp 已构建（bin/Debug）；工作区 clean（BUILD-MANIFEST 需要 commit）。
旧模式产物：release/KingdomEnhancedMod_v<ModVersion>_IL2CPP.zip

win243-x64 模式只接受 release/windows243-11.1.0/runtime-lock.json 白名单中的
435 个 runtime/generated 文件、bootstrap 三件套、审定 loader 配置、正式 mod
DLL、启动器与玩家文档；缺件、多件、错 hash、symlink/reparse、路径逃逸或
禁止文件一律失败。game 本体只在 catalog 记录 hash，不进入 zip。
"""
import argparse
import hashlib
import json
import os
import re
import stat
import subprocess
import sys
import tempfile
import zipfile
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath

REPO = Path(__file__).resolve().parents[1]
GAME = Path(r"E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091")
OUT_NAME = "KingdomEnhancedMod_v{}_IL2CPP.zip"

# 允许进入旧包的 BepInEx 子路径（防泄漏：cache/interop/LogOutput 等一律不进）
BEPINEX_ALLOW = {
    "core": None,          # 全部
    "unity-libs": None,    # 全部
}

WIN243_PLATFORM = "win243-x64"
WIN243_VERSION = "11.1.0"
WIN243_RELEASE_REL = Path("release/windows243-11.1.0")
WIN243_OUT_NAME = "KingdomEnhancedMod_v{}_Windows243_x64.zip"
WIN243_GAME_BUILD = "2.4.3r23537 / Unity6000.0.66f2 / x64"
WIN243_RUNTIME_DIRS = ("BepInEx/core", "BepInEx/unity-libs", "dotnet", "BepInEx/interop")
WIN243_EXPECTED_COUNTS = {"loader": 43, "runtime": 259, "generatedBindings": 133}
WIN243_TOTAL_RUNTIME = 435
WIN243_FIXED_KEYS = {
    "UpdateInteropAssemblies": "false",
    "UnityBaseLibrariesSource": "6000.0.66.zip",
    "IL2CPPInteropAssembliesPath": "{BepInEx}",
    "GlobalMetadataPath": "{GameDataPath}/il2cpp_data/Metadata/global-metadata.dat",
}
WIN243_DOCS = ("README.txt", "MOD_V11.1.0版本更新说明.txt")
WIN243_NOTICE = "NOTICE.txt"
WIN243_PS1 = "ohmymods-win243-preview.ps1"
WIN243_CMD = "ohmymods-win243-preview.cmd"
WIN243_EASY_CMD = "启动_OhMyMods_Windows243.cmd"
WIN243_CATALOG = "ohmymods-win243-preview.json"
WIN243_MANIFEST = "BUILD-MANIFEST.txt"
SUPPORT_TOOLS_REL = Path("release/support-tools/windows")
SUPPORT_TOOL_VERSION = "1.0.0"
SUPPORT_TOOL_FILES = (
    "OhMyMods-CollectDiagnostics.ps1", "一键收集排查资料.cmd", "排查资料工具说明.txt",
)
WIN243_CATEGORY_ORDER = (
    "loader", "runtime", "generatedBindings", "bootstrap",
    "loaderConfig", "mod", "docs", "launcher", "supportTools", "manifest", "catalog",
)


class PackageError(Exception):
    pass


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def leak_check(rel: str) -> bool:
    low = rel.lower().replace("\\", "/")
    bad = (".bak", "logoutput", "/cache/", "/interop/", "skidrow",
           "_data/", "kingdomenhancedmod.pdb")
    # 注意：不能泛匹配 .pdb——BepInEx/core 的 Mono.Cecil.Pdb.dll 是核心程序集
    # doorstop 三个引导文件在根级允许，其余命名命中即拒绝
    if low.startswith(("doorstop_config", ".doorstop_version", "winhttp.dll")):
        return False
    return any(b in low for b in bad)


def add_dir(zf: zipfile.ZipFile, base: Path, prefix: str = ""):
    for p in sorted(base.rglob("*")):
        if p.is_file():
            rel = p.relative_to(base).as_posix()
            arc = f"{prefix}{rel}"
            if leak_check(arc):
                print(f"  [LEAK-SKIP] {arc}")
                continue
            zf.write(p, arc)


def legacy_main(version: str):
    """Frozen Windows 2.4.0 / IL2CPP 240 packaging path. Do not broaden."""
    project_version = ET.parse(REPO / "il2cpp/KingdomEnhancedMod.csproj").findtext(".//Version")
    if version != project_version:
        sys.exit(f"ERROR: requested version {version} differs from project {project_version}")
    dll = REPO / "il2cpp/bin/Debug/KingdomEnhancedMod.dll"
    if not dll.is_file():
        print("ERROR: 先构建 il2cpp（dotnet build -c Debug）")
        sys.exit(1)

    dirty = subprocess.run(
        ["git", "status", "--porcelain"], cwd=REPO,
        capture_output=True, text=True).stdout.strip()
    if dirty:
        print("ERROR: 工作区不干净，先 commit（BUILD-MANIFEST 需要 commit hash）：")
        print(dirty)
        sys.exit(1)
    commit = subprocess.run(
        ["git", "rev-parse", "HEAD"], cwd=REPO,
        capture_output=True, text=True).stdout.strip()

    out = REPO / "release" / OUT_NAME.format(version)
    out.parent.mkdir(exist_ok=True)
    dll_sha = sha256(dll)
    manifest = (
        f"ModVersion: {version}\n"
        "GameCompatibility: Kingdom Two Crowns 2.4.0 IL2CPP / BepInEx 6\n"
        f"GitCommit: {commit}\n"
        f"DllSHA256: {dll_sha}\n"
        f"DllSize: {dll.stat().st_size}\n"
        f"GeneratedUtc: {datetime.now(timezone.utc).isoformat()}\n"
    )

    # 玩家文档预检（缺一即失败）：2026-09-23 v9.14.23/24 曾因逐版本说明缺失被
    # if src.is_file() 静默跳过，包内文档缩水到玩家发现。发布文档随本提交入库，此后缺件必须报错。
    rel = REPO / "release"
    # 2026-09-23 起玩家文档用中文文件名（用户要求：玩家能直接看出文档用途）。
    required_docs = [
        "更新日志.txt",
        "功能总览.txt",
        "用户指南.txt",
        "能力与路线图.txt",
        f"MOD_V{version}版本更新说明.txt",
    ]
    for name in required_docs:
        if not (rel / name).is_file():
            raise SystemExit(
                f"package_release: missing player doc release/{name} — "
                "generate/update docs before packaging (fail-hard since 2026-09-23)")
    notes = REPO / "release-notes-il2cpp.md"  # 该文件在仓库根（runbook 口径）
    if not notes.is_file():
        raise SystemExit("package_release: missing release-notes-il2cpp.md (INSTALL.md source)")
    if not (REPO / "VERSIONING.md").is_file():
        raise SystemExit("package_release: missing VERSIONING.md")

    count = 0
    with zipfile.ZipFile(out, "x", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        # 根级引导三件套
        for name in (".doorstop_version", "doorstop_config.ini", "winhttp.dll"):
            src = GAME / name
            if src.is_file():
                zf.write(src, name)
                count += 1
        # dotnet 运行时
        add_dir(zf, GAME / "dotnet", "dotnet/")
        # BepInEx：core + unity-libs 全量，config 仅 .cfg，plugins 仅本 DLL
        add_dir(zf, GAME / "BepInEx/core", "BepInEx/core/")
        add_dir(zf, GAME / "BepInEx/unity-libs", "BepInEx/unity-libs/")
        for cfg in sorted((GAME / "BepInEx/config").glob("*.cfg")):
            if cfg.name != "BepInEx.cfg":
                continue  # Never distribute the tester's personal gameplay settings.
            zf.write(cfg, f"BepInEx/config/{cfg.name}")
            count += 1
        zf.write(dll, "BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll")
        count += 1
        # 玩家文档（预检已在打开 zip 前完成，此处缺件不可能发生）
        for name in required_docs:
            zf.write(rel / name, name)
            count += 1
        zf.write(notes, "安装说明.md")
        count += 1
        zf.write(REPO / "VERSIONING.md", "版本规则.md")
        count += 1
        zf.writestr("BUILD-MANIFEST.txt", manifest)
        count = len(zf.infolist())

    print(f"OK: {out}  items={count}  size={out.stat().st_size}")
    print(manifest)


# ---------------------------------------------------------------------------
# win243-x64 release mode
# ---------------------------------------------------------------------------

def _fail(message: str):
    raise PackageError(message)


def _safe_rel(rel: str, label: str = "path") -> PurePosixPath:
    if not isinstance(rel, str) or not rel:
        _fail(f"{label}: empty or non-string path")
    if "\x00" in rel or "\\" in rel:
        _fail(f"{label}: backslash/NUL not allowed: {rel!r}")
    pp = PurePosixPath(rel)
    if pp.is_absolute() or rel != pp.as_posix():
        _fail(f"{label}: non-canonical or absolute path: {rel!r}")
    if any(part in ("", ".", "..") for part in pp.parts):
        _fail(f"{label}: unsafe path component: {rel!r}")
    if any(":" in part for part in pp.parts):
        _fail(f"{label}: drive/stream separator not allowed: {rel!r}")
    return pp


def _forbidden_arc(rel: str) -> bool:
    low = rel.lower().replace("\\", "/")
    name = low.rsplit("/", 1)[-1]
    if name in {
        "gameassembly.dll", "unityplayer.dll", "kingdomtwocrowns.exe",
        "global-metadata.dat", "kingdomenhancedmod.cfg",
    }:
        return True
    if name.endswith((".pdb", ".log", ".dylib", ".dsym", ".so")):
        return True
    if low.startswith(("bepinex/cache/", "bepinex/dummy/", "bepinex/logoutput/")):
        return True
    if "output_log" in name or "/framework/" in low or "/bundle/" in low:
        return True
    if low.startswith(("modsave/", "saves/")) or "/modsave/" in low or "/saves/" in low:
        return True
    if low.startswith(("kingdomtwocrowns_data/", "contents/", "monobleedingedge/")):
        return True
    if name.endswith(("assembly-csharp.dll", "assembly-csharp-firstpass.dll")):
        return not low.startswith("bepinex/interop/")
    return False


def _check_arc(rel: str, label: str = "archive path"):
    _safe_rel(rel, label)
    if _forbidden_arc(rel):
        _fail(f"{label}: forbidden packaged file: {rel}")


def _regular_file(path: Path, label: str) -> Path:
    path = Path(path)
    try:
        st = os.lstat(path)
    except OSError as exc:
        _fail(f"{label}: cannot stat {path}: {exc}")
    if stat.S_ISLNK(st.st_mode):
        _fail(f"{label}: symlink/reparse point rejected: {path}")
    if not stat.S_ISREG(st.st_mode):
        _fail(f"{label}: not a regular file: {path}")
    return path


def _safe_under(root: Path, rel: str, label: str) -> Path:
    """Resolve rel below root, rejecting symlinked components and escapes."""
    _safe_rel(rel, label)
    root = Path(root)
    if root.is_symlink():
        _fail(f"{label}: symlink/reparse root rejected: {root}")
    if not root.is_dir():
        _fail(f"{label}: root is not a directory: {root}")
    root_real = root.resolve()
    cur = root
    for part in PurePosixPath(rel).parts:
        cur = cur / part
        try:
            st = os.lstat(cur)
        except OSError as exc:
            _fail(f"{label}: missing {rel}: {exc}")
        if stat.S_ISLNK(st.st_mode):
            _fail(f"{label}: symlink/reparse point rejected: {cur}")
    if not stat.S_ISREG(os.lstat(cur).st_mode):
        _fail(f"{label}: not a regular file: {cur}")
    try:
        cur.resolve().relative_to(root_real)
    except ValueError:
        _fail(f"{label}: path escapes root: {rel}")
    return cur


def _hash_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _read_regular(path: Path, label: str, max_size: int) -> bytes:
    path = _regular_file(path, label)
    size = path.stat().st_size
    if size <= 0:
        _fail(f"{label}: empty file: {path}")
    if size > max_size:
        _fail(f"{label}: oversized file ({size} bytes): {path}")
    return path.read_bytes()


def _check_entry(entry, label: str) -> dict:
    if not isinstance(entry, dict):
        _fail(f"{label}: entry is not an object")
    rel = entry.get("path")
    _safe_rel(rel, label)
    size = entry.get("size")
    digest = entry.get("sha256")
    if isinstance(size, bool) or not isinstance(size, int) or size < 0:
        _fail(f"{label}: invalid size for {rel}")
    if not isinstance(digest, str) or not re.fullmatch(r"[0-9a-f]{64}", digest):
        _fail(f"{label}: invalid sha256 for {rel}")
    return {"path": rel, "size": size, "sha256": digest}


def load_runtime_lock(repo: Path) -> dict:
    lock_path = Path(repo) / WIN243_RELEASE_REL / "runtime-lock.json"
    lock_path = _regular_file(lock_path, "runtime-lock")
    try:
        lock = json.loads(lock_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        _fail(f"runtime-lock: cannot parse {lock_path}: {exc}")
    if not isinstance(lock, dict):
        _fail("runtime-lock: top level must be an object")
    if lock.get("schema") != 1:
        _fail("runtime-lock: unsupported schema")
    if lock.get("platform") != WIN243_PLATFORM:
        _fail("runtime-lock: platform must be win243-x64")
    if lock.get("version") != WIN243_VERSION:
        _fail(f"runtime-lock: version must be {WIN243_VERSION}")
    if lock.get("gameBuild") != WIN243_GAME_BUILD:
        _fail("runtime-lock: unexpected game build")
    if lock.get("releaseDirectory") != WIN243_RELEASE_REL.as_posix():
        _fail("runtime-lock: releaseDirectory mismatch")
    if lock.get("expectedCounts") != WIN243_EXPECTED_COUNTS:
        _fail("runtime-lock: expectedCounts mismatch")
    if lock.get("totalRuntimeFiles") != WIN243_TOTAL_RUNTIME:
        _fail("runtime-lock: totalRuntimeFiles must be 435")

    game = lock.get("game")
    if not isinstance(game, list):
        _fail("runtime-lock: game must be a list")
    game_entries = [_check_entry(item, "game") for item in game]
    game_paths = {item["path"] for item in game_entries}
    expected_game = {
        "KingdomTwoCrowns.exe",
        "GameAssembly.dll",
        "KingdomTwoCrowns_Data/il2cpp_data/Metadata/global-metadata.dat",
    }
    if len(game_entries) != 3 or game_paths != expected_game:
        _fail("runtime-lock: game must contain exactly exe/GameAssembly/metadata")
    if len({item["path"].casefold() for item in game_entries}) != 3:
        _fail("runtime-lock: duplicate game paths")

    bootstrap = lock.get("bootstrap")
    if not isinstance(bootstrap, list):
        _fail("runtime-lock: bootstrap must be a list")
    bootstrap_entries = [_check_entry(item, "bootstrap") for item in bootstrap]
    if {item["path"] for item in bootstrap_entries} != {
            ".doorstop_version", "doorstop_config.ini", "winhttp.dll"}:
        _fail("runtime-lock: bootstrap must be exactly doorstop three files")
    if len(bootstrap_entries) != 3:
        _fail("runtime-lock: bootstrap count must be 3")

    runtime = lock.get("runtime")
    if not isinstance(runtime, list):
        _fail("runtime-lock: runtime must be a list")
    runtime_entries = []
    seen = set()
    counts = {key: 0 for key in WIN243_EXPECTED_COUNTS}
    prefixes = {
        "loader": ("BepInEx/core/",),
        "runtime": ("dotnet/", "BepInEx/unity-libs/"),
        "generatedBindings": ("BepInEx/interop/",),
    }
    for item in runtime:
        entry = _check_entry(item, "runtime")
        category = item.get("category")
        if category not in prefixes:
            _fail(f"runtime-lock: bad category for {entry['path']}")
        if not entry["path"].startswith(prefixes[category]):
            _fail(f"runtime-lock: {entry['path']} not under {prefixes[category]}")
        folded = entry["path"].casefold()
        if folded in seen:
            _fail(f"runtime-lock: duplicate path (case-insensitive): {entry['path']}")
        seen.add(folded)
        count_entry = dict(entry)
        count_entry["category"] = category
        runtime_entries.append(count_entry)
        counts[category] += 1
    if len(runtime_entries) != WIN243_TOTAL_RUNTIME:
        _fail(f"runtime-lock: runtime file count is {len(runtime_entries)}, expected 435")
    if counts != WIN243_EXPECTED_COUNTS:
        _fail(f"runtime-lock: category counts {counts}, expected {WIN243_EXPECTED_COUNTS}")
    runtime_entries.sort(key=lambda item: item["path"])

    loader_cfg = lock.get("loaderConfig")
    if not isinstance(loader_cfg, dict):
        _fail("runtime-lock: loaderConfig must be an object")
    if loader_cfg.get("path") != "BepInEx/config/BepInEx.cfg":
        _fail("runtime-lock: loaderConfig path mismatch")
    if loader_cfg.get("fixedKeys") != WIN243_FIXED_KEYS:
        _fail("runtime-lock: loaderConfig fixedKeys differ from the approved four keys")
    loader_cfg_entry = _check_entry(loader_cfg, "loaderConfig")

    mod = lock.get("mod")
    if not isinstance(mod, dict):
        _fail("runtime-lock: mod must be an object")
    if mod.get("path") != "BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll":
        _fail("runtime-lock: mod path mismatch")
    if mod.get("version") != WIN243_VERSION:
        _fail("runtime-lock: mod version mismatch")
    if mod.get("metadataVerification") != "root":
        _fail("runtime-lock: metadata verification must remain Root-owned")

    if lock.get("docs") != list(WIN243_DOCS):
        _fail("runtime-lock: docs must be the Windows-only README and 11.1.0 notes")
    if lock.get("notice") != WIN243_NOTICE:
        _fail("runtime-lock: notice mismatch")
    launcher = lock.get("launcher")
    if not isinstance(launcher, dict):
        _fail("runtime-lock: launcher must be an object")
    if launcher.get("directory") != "launcher":
        _fail("runtime-lock: launcher directory mismatch")
    if launcher.get("files") != [WIN243_PS1, WIN243_CMD, WIN243_EASY_CMD]:
        _fail("runtime-lock: launcher files mismatch")
    if launcher.get("catalog") != WIN243_CATALOG:
        _fail("runtime-lock: launcher catalog mismatch")

    return {
        "path": lock_path,
        "game": game_entries,
        "bootstrap": bootstrap_entries,
        "runtime": runtime_entries,
        "loaderConfig": dict(loader_cfg_entry, fixedKeys=dict(WIN243_FIXED_KEYS)),
        "mod": mod,
        "docs": list(WIN243_DOCS),
        "notice": WIN243_NOTICE,
        "launcher": launcher,
    }


def collect_runtime_files(lock: dict, runtime_root: Path) -> list:
    root = Path(runtime_root)
    if root.is_symlink():
        _fail(f"runtime-root: symlink/reparse root rejected: {root}")
    if not root.is_dir():
        _fail(f"runtime-root is not a directory: {root}")
    expected = {item["path"]: item for item in lock["runtime"]}
    actual = set()
    for dirname in WIN243_RUNTIME_DIRS:
        base = root / dirname
        if base.is_symlink():
            _fail(f"runtime-root: symlink/reparse directory rejected: {dirname}")
        if not base.is_dir():
            _fail(f"runtime-root: missing directory {dirname}")
        for dirpath, dirnames, filenames in os.walk(base, topdown=True, followlinks=False):
            dirnames.sort()
            filenames.sort()
            for name in dirnames:
                child = Path(dirpath) / name
                if child.is_symlink():
                    _fail(f"runtime-root: symlink directory rejected: {child}")
            for name in filenames:
                child = Path(dirpath) / name
                if child.is_symlink():
                    _fail(f"runtime-root: symlink file rejected: {child}")
                if not child.is_file():
                    _fail(f"runtime-root: non-regular file rejected: {child}")
                try:
                    rel = child.relative_to(root).as_posix()
                except ValueError:
                    _fail(f"runtime-root: path escape: {child}")
                actual.add(rel)
    expected_paths = set(expected)
    missing = sorted(expected_paths - actual)
    extra = sorted(actual - expected_paths)
    if missing:
        _fail(f"runtime-root: missing {len(missing)} locked files: {missing[:5]}")
    if extra:
        _fail(f"runtime-root: {len(extra)} unreviewed files: {extra[:5]}")
    for rel in sorted(expected_paths):
        path = _safe_under(root, rel, "runtime-root")
        item = expected[rel]
        if path.stat().st_size != item["size"]:
            _fail(f"runtime-root: size mismatch for {rel}")
        if sha256(path) != item["sha256"]:
            _fail(f"runtime-root: sha256 mismatch for {rel}")
    return [expected[rel] for rel in sorted(expected_paths)]


def verify_bootstrap(lock: dict, bootstrap_root: Path) -> list:
    root = Path(bootstrap_root)
    entries = []
    for item in lock["bootstrap"]:
        path = _safe_under(root, item["path"], "bootstrap-root")
        if path.stat().st_size != item["size"] or sha256(path) != item["sha256"]:
            _fail(f"bootstrap-root: hash/size mismatch for {item['path']}")
        entries.append(item)
    return entries


def parse_fixed_interop_config(text: str) -> dict:
    section = None
    section_seen = False
    keys = {}
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith(("#", ";")):
            continue
        match = re.fullmatch(r"\[([^\]]+)\]", line)
        if match:
            section = match.group(1)
            if section != "IL2CPP":
                _fail(f"loader config: unexpected section [{section}]")
            if section_seen:
                _fail("loader config: duplicate [IL2CPP] section")
            section_seen = True
            continue
        if section is None:
            _fail("loader config: key outside section")
        if "=" not in line:
            _fail(f"loader config: invalid line: {line}")
        key, value = line.split("=", 1)
        key = key.strip()
        value = value.strip()
        if key not in WIN243_FIXED_KEYS:
            _fail(f"loader config: unexpected key {key}")
        if key in keys:
            _fail(f"loader config: duplicate key {key}")
        if value != WIN243_FIXED_KEYS[key]:
            _fail(f"loader config: fixed key changed: {key}")
        keys[key] = value
    if set(keys) != set(WIN243_FIXED_KEYS):
        missing = sorted(set(WIN243_FIXED_KEYS) - set(keys))
        _fail(f"loader config: missing fixed keys: {missing}")
    return keys


def load_loader_config(repo: Path, lock: dict) -> bytes:
    path = Path(repo) / WIN243_RELEASE_REL / "BepInEx.cfg"
    data = _read_regular(path, "loader config", 1 << 20)
    try:
        text = data.decode("utf-8-sig")
    except UnicodeDecodeError as exc:
        _fail(f"loader config: not UTF-8: {exc}")
    keys = parse_fixed_interop_config(text)
    if keys != WIN243_FIXED_KEYS:
        _fail("loader config: fixed keys mismatch")
    expected = lock["loaderConfig"]
    if len(data) != expected["size"] or _hash_bytes(data) != expected["sha256"]:
        _fail("loader config: bytes differ from runtime-lock")
    return data


def load_launcher_files(repo: Path, lock: dict) -> dict:
    root = Path(repo) / WIN243_RELEASE_REL / "launcher"
    result = {}
    for name in lock["launcher"]["files"]:
        path = _safe_under(root, name, "launcher")
        data = path.read_bytes()
        if name == WIN243_PS1:
            if not data.startswith(b"\xef\xbb\xbf"):
                _fail("launcher: PowerShell file must be UTF-8 with BOM")
            try:
                text = data[3:].decode("utf-8")
            except UnicodeDecodeError as exc:
                _fail(f"launcher: PowerShell file is not UTF-8: {exc}")
            required = (
                "param([switch]$CheckOnly)",
                "Get-Process -Name KingdomTwoCrowns",
                "Get-FileHash -LiteralPath $path -Algorithm SHA256",
                "TestFixedInteropConfig",
                "Start-Process -FilePath (Join-Path $root 'KingdomTwoCrowns.exe')",
                "if ($CheckOnly)",
                "ohmymods-win243-preview.json",
            )
            for fragment in required:
                if fragment not in text:
                    _fail(f"launcher: missing required fragment: {fragment}")
            for key, value in WIN243_FIXED_KEYS.items():
                if f"{key}='{value}'" not in text:
                    _fail(f"launcher: fixed key not verified in PowerShell: {key}")
            if "Manual gameplay validation pending" in text:
                _fail("launcher: preview-only player text remains")
            if text.count("Start-Process") != 1:
                _fail("launcher: exactly one Start-Process expected")
        elif name == WIN243_CMD:
            if not data.isascii() or b"\r\n" not in data:
                _fail("launcher: compatibility cmd must be ASCII with CRLF")
            if b"ohmymods-win243-preview.ps1" not in data or b"powershell.exe" not in data:
                _fail("launcher: compatibility cmd no longer calls the PowerShell launcher")
        elif name == WIN243_EASY_CMD:
            if not data.isascii() or b"\r\n" not in data:
                _fail("launcher: readable cmd must be ASCII with CRLF")
            if b"ohmymods-win243-preview.cmd" not in data or b"call " not in data:
                _fail("launcher: readable cmd must call the compatibility cmd")
        else:
            _fail(f"launcher: unexpected file {name}")
        result[name] = data
    if set(result) != set(lock["launcher"]["files"]):
        _fail("launcher: file set mismatch")
    return result


def load_player_docs(repo: Path, lock: dict) -> dict:
    root = Path(repo) / WIN243_RELEASE_REL
    docs = {}
    for name in lock["docs"]:
        data = _read_regular(root / name, f"player doc {name}", 10 << 20)
        try:
            data.decode("utf-8-sig")
        except UnicodeDecodeError as exc:
            _fail(f"player doc {name}: not UTF-8: {exc}")
        docs[name] = data
    notice = _read_regular(root / lock["notice"], "NOTICE", 1 << 20)
    try:
        notice_text = notice.decode("utf-8-sig")
    except UnicodeDecodeError as exc:
        _fail(f"NOTICE: not UTF-8: {exc}")
    for needle in (
        WIN243_VERSION, "2.4.3r23537", "GameAssembly.dll", "global-metadata.dat",
        "Il2CppInterop", "UpdateInteropAssemblies", "不包含", "拥有", "LGPL",
    ):
        if needle not in notice_text:
            _fail(f"NOTICE: missing required statement: {needle}")
    docs[lock["notice"]] = notice
    return docs


def load_support_tools(repo: Path) -> dict:
    """One pinned byte source for both full packages and the small add-on ZIP."""
    root = Path(repo) / SUPPORT_TOOLS_REL
    manifest_path = _safe_under(repo, (SUPPORT_TOOLS_REL / "manifest.json").as_posix(), "support tools")
    try:
        manifest = json.loads(_read_regular(manifest_path, "support manifest", 16384).decode("utf-8-sig"))
    except (ValueError, UnicodeError) as exc:
        _fail(f"support manifest: invalid JSON: {exc}")
    if (not isinstance(manifest, dict) or manifest.get("schema") != 1
            or manifest.get("version") != SUPPORT_TOOL_VERSION):
        _fail("support manifest: unsupported schema/version")
    rows = manifest.get("files")
    if not isinstance(rows, list) or len(rows) != len(SUPPORT_TOOL_FILES):
        _fail("support manifest: incorrect file count")
    if {p.name for p in root.iterdir()} != set(SUPPORT_TOOL_FILES) | {"manifest.json"}:
        _fail("support tools: missing or unexpected source file")
    result = {}
    for row in rows:
        entry = _check_entry(row, "support tools")
        name = entry["path"]
        if name not in SUPPORT_TOOL_FILES or name in result:
            _fail(f"support tools: unexpected or duplicate path: {name}")
        path = _safe_under(repo, (SUPPORT_TOOLS_REL / name).as_posix(), "support tools")
        st = os.lstat(path)
        if getattr(st, "st_file_attributes", 0) & 0x400:
            _fail(f"support tools: reparse point rejected: {name}")
        data = _read_regular(path, "support tools", 1 << 20)
        if len(data) != entry["size"] or _hash_bytes(data) != entry["sha256"]:
            _fail(f"support tools: size/hash mismatch for {name}")
        if name.endswith(".ps1") and not data.startswith(b"\xef\xbb\xbf"):
            _fail("support tools: PowerShell must be UTF-8 BOM for Windows PowerShell 5.1")
        if name.endswith(".cmd") and (not data.isascii() or b"\r\n" not in data):
            _fail("support tools: CMD must be ASCII with CRLF")
        result[name] = data
    return result


def build_support_tools_zip(repo: Path, output_dir: Path) -> dict:
    tools = load_support_tools(repo)  # validate before creating any output
    out_dir = Path(output_dir)
    out = out_dir / f"OhMyMods-Windows-SupportTools-v{SUPPORT_TOOL_VERSION}.zip"
    sidecar = out.with_name(out.name + ".sha256")
    if out.exists() or sidecar.exists():
        _fail("support tools: output already exists")
    out_dir.mkdir(parents=True, exist_ok=True)
    fd, temp_name = tempfile.mkstemp(prefix="support-tools-", suffix=".tmp", dir=out_dir)
    os.close(fd)
    temp = Path(temp_name)
    created_output = False
    try:
        with zipfile.ZipFile(temp, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
            for name, data in sorted(tools.items()):
                info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
                info.compress_type = zipfile.ZIP_DEFLATED
                info.external_attr = 0o100644 << 16
                archive.writestr(info, data, compresslevel=9)
        with zipfile.ZipFile(temp) as archive:
            if set(archive.namelist()) != set(tools) or archive.testzip() is not None:
                _fail("support tools: ZIP readback failed")
            for name, data in tools.items():
                if archive.read(name) != data:
                    _fail(f"support tools: ZIP content mismatch: {name}")
        checksum = sha256(temp)
        # Exclusive creation also rejects a concurrent output of the same name.
        with out.open("xb") as target, temp.open("rb") as source:
            created_output = True
            for chunk in iter(lambda: source.read(1 << 20), b""):
                target.write(chunk)
        with sidecar.open("x", encoding="ascii") as target:
            target.write(f"{checksum}  {out.name}\n")
    except Exception:
        if created_output:
            out.unlink()
        raise
    finally:
        temp.unlink(missing_ok=True)
    return {"output": str(out), "sidecar": str(sidecar), "zipSha256": checksum,
            "entries": len(tools), "version": SUPPORT_TOOL_VERSION}


def validate_mod_dll(path: Path, lock: dict) -> tuple:
    path = _regular_file(path, "mod DLL")
    if path.suffix.lower() != ".dll":
        _fail("mod DLL: .dll required")
    data = path.read_bytes()
    if not data.startswith(b"MZ"):
        _fail("mod DLL: missing PE header")
    probe = WIN243_VERSION.encode("utf-8")
    probe_wide = WIN243_VERSION.encode("utf-16le")
    if probe not in data and probe_wide not in data:
        _fail(f"mod DLL: version string {WIN243_VERSION} not found")
    return data, _hash_bytes(data)


def validate_source_archive(path: Path) -> dict:
    if path is None:
        return None
    path = Path(path)
    _regular_file(path, "source archive")
    if path.suffix.lower() != ".zip":
        _fail("source archive: .zip required")
    if WIN243_VERSION not in path.name or "source" not in path.name.lower():
        _fail("source archive: filename must contain 11.1.0 and 'source'")
    names = []
    try:
        with zipfile.ZipFile(path) as zf:
            bad = zf.testzip()
            if bad is not None:
                _fail(f"source archive: bad CRC at {bad}")
            for info in zf.infolist():
                name = info.filename
                if name.endswith("/"):
                    _check_arc(name[:-1], "source archive")
                    continue
                _check_arc(name, "source archive")
                low = name.lower()
                if low.startswith(("bepinex/", "dotnet/", "kingdomtwocrowns_data/")):
                    _fail(f"source archive: runtime/game tree not allowed: {name}")
                names.append(name)
    except zipfile.BadZipFile as exc:
        _fail(f"source archive: not a valid zip: {exc}")
    if not names:
        _fail("source archive: empty")
    if not any(name.lower().endswith((".cs", ".csproj", ".json", ".props")) for name in names):
        _fail("source archive: no source/build recipe files found")
    return {
        "name": path.name,
        "size": path.stat().st_size,
        "sha256": sha256(path),
        "embedded": False,
    }


def _check_commit(repo: Path, commit: str) -> str:
    if not isinstance(commit, str) or not re.fullmatch(r"[0-9a-fA-F]{40}", commit):
        _fail("commit: full 40-hex SHA required")
    commit = commit.lower()
    status = subprocess.run(
        ["git", "-C", str(repo), "status", "--porcelain=v1", "--untracked-files=normal"],
        capture_output=True, text=True)
    if status.returncode != 0:
        _fail(f"git status failed: {status.stderr.strip()}")
    if status.stdout.strip():
        _fail("git worktree is not clean; commit release inputs before packaging")
    head = subprocess.run(
        ["git", "-C", str(repo), "rev-parse", "HEAD"],
        capture_output=True, text=True)
    if head.returncode != 0:
        _fail(f"git rev-parse failed: {head.stderr.strip()}")
    actual = head.stdout.strip().lower()
    if actual != commit:
        _fail(f"commit mismatch: requested {commit}, HEAD {actual}")
    return actual


def _aggregate(entries: list) -> str:
    h = hashlib.sha256()
    for item in sorted(entries, key=lambda e: e["path"]):
        h.update(item["path"].encode("utf-8"))
        h.update(b"\x00")
        h.update(bytes.fromhex(item["sha256"]))
        h.update(b"\n")
    return h.hexdigest()


def _entry(path: str, size: int, digest: str, category: str) -> dict:
    return {"path": path, "size": size, "sha256": digest, "category": category}


def _write_bytes(zf, arc: str, data: bytes, category: str, entries: list, seen: set) -> dict:
    _check_arc(arc)
    if arc in seen:
        _fail(f"duplicate archive path: {arc}")
    zf.writestr(arc, data)
    info = zf.getinfo(arc)
    item = _entry(arc, len(data), _hash_bytes(data), category)
    if info.file_size != len(data):
        _fail(f"zip write size mismatch: {arc}")
    entries.append(item)
    seen.add(arc)
    return item


def _write_file(zf, arc: str, path: Path, category: str, entries: list, seen: set,
                expected: dict = None) -> dict:
    _check_arc(arc)
    if arc in seen:
        _fail(f"duplicate archive path: {arc}")
    h = hashlib.sha256()
    size = 0
    with open(path, "rb") as src:
        with zf.open(arc, "w") as dst:
            while True:
                chunk = src.read(1 << 20)
                if not chunk:
                    break
                h.update(chunk)
                dst.write(chunk)
                size += len(chunk)
    info = zf.getinfo(arc)
    if info.file_size != size:
        _fail(f"zip write size mismatch: {arc}")
    digest = h.hexdigest()
    if expected is not None:
        if size != expected["size"] or digest != expected["sha256"]:
            _fail(f"input changed while packaging: {arc}")
    item = _entry(arc, size, digest, category)
    entries.append(item)
    seen.add(arc)
    return item


def _manifest_text(lock: dict, commit: str, built: str, source: dict,
                   entries: list, version: str) -> str:
    cfg = next(item for item in entries if item["category"] == "loaderConfig")
    mod = next(item for item in entries if item["category"] == "mod")
    payload_files = len(entries)  # config is replaced by this manifest in payload
    archive_entries = len(entries) + 2  # manifest + catalog
    lines = [
        "OhMyMods Windows 2.4.3 package manifest",
        f"ModVersion: {version}",
        f"Platform: {WIN243_PLATFORM}",
        f"GameBuild: {WIN243_GAME_BUILD}",
        f"GitCommit: {commit}",
        f"BuiltUtc: {built}",
        f"PackageFile: {WIN243_OUT_NAME.format(version)}",
        f"ArchiveEntries: {archive_entries}",
        f"PayloadFiles: {payload_files}",
        f"RuntimeFiles: {WIN243_TOTAL_RUNTIME}",
        "LoaderRuntimeFiles: 302 (loader=43, runtime=259)",
        "GeneratedBindingFiles: 133",
        "BootstrapFiles: 3",
        "ModFiles: 1",
        "DocFiles: 3 (README.txt, MOD_V11.1.0版本更新说明.txt, NOTICE.txt)",
        "LauncherFiles: 3",
        f"SupportToolFiles: {len([e for e in entries if e['category'] == 'supportTools'])}",
        "ManifestFiles: 1",
        "CatalogFiles: 1",
        f"LoaderConfig: {cfg['path']} size={cfg['size']} sha256={cfg['sha256']}",
        f"ModDLL: {mod['path']} size={mod['size']} sha256={mod['sha256']}",
        f"SourceArchive: {source['name'] if source else 'none'}",
        f"SourceArchiveSHA256: {source['sha256'] if source else 'none'}",
        f"SourceArchiveSize: {source['size'] if source else 'none'}",
        "SourceArchiveEmbedded: false",
        "GameFileHashesNotPackaged:",
    ]
    for item in lock["game"]:
        lines.append(f"  {item['path']} size={item['size']} sha256={item['sha256']}")
    lines.append("Categories (aggregate = SHA256 over sorted path NUL digest LF):")
    for category in WIN243_CATEGORY_ORDER:
        if category not in ("manifest", "catalog"):
            subset = [item for item in entries if item["category"] == category]
            lines.append(
                f"  {category}: files={len(subset)} bytes={sum(i['size'] for i in subset)} "
                f"aggregateSha256={_aggregate(subset)}")
    lines.extend([
        "Verification:",
        "  ModVersionString: present (ASCII or UTF-16LE)",
        "  ModMetadataVerification: root",
        "  EmbeddedAssets: inside DLL only; no resource PNGs are packaged separately; root verifies 38 resources",
        "  ILVerification: root",
        "  GameLaunch: not performed by packager",
    ])
    return "\n".join(lines) + "\n"


def _catalog(lock: dict, commit: str, built: str, source: dict, version: str,
             entries: list, archive_entries: int, payload_files: int) -> dict:
    categories = {category: [] for category in WIN243_CATEGORY_ORDER}
    for item in entries:
        categories[item["category"]].append(item["path"])
    categories["catalog"] = [WIN243_CATALOG]
    for paths in categories.values():
        paths.sort()
    counts = {category: len(paths) for category, paths in categories.items()}
    counts["archiveEntries"] = archive_entries
    counts["payloadFiles"] = payload_files
    payload = [
        {"path": item["path"], "size": item["size"], "sha256": item["sha256"]}
        for item in sorted(entries, key=lambda e: e["path"])
        if item["category"] != "loaderConfig"
    ]
    return {
        "schema": 1,
        "kind": "ohmymods-win243-release",
        "version": version,
        "platform": WIN243_PLATFORM,
        "gameBuild": WIN243_GAME_BUILD,
        "releaseCommit": commit,
        "builtUtc": built,
        "sourceArchive": source,
        "game": [dict(item) for item in lock["game"]],
        "payload": payload,
        "categories": categories,
        "counts": counts,
        "startupMutable": ["BepInEx/config/BepInEx.cfg"],
        "startupMutablePolicy": (
            "Install the four pinned IL2CPP keys. Allow loader defaults/formatting "
            "changes on first run; the launcher re-checks the unique four fixed keys."),
        "interopPolicy": (
            "Exact frozen 2.4.3r23537 generated bindings. "
            "UpdateInteropAssemblies=false; do not regenerate or delete BepInEx/interop."),
        "runtimePolicy": (
            "Loader/runtime/generated bindings are exact-hash locked; "
            "game files are checked but never packaged."),
        "modVerification": {
            "versionStringPresent": True,
            "metadataVerification": "root",
            "embeddedAssets": "inside DLL only; root verifies 38 resources",
        },
        "manifest": WIN243_MANIFEST,
        "notice": WIN243_NOTICE,
        "launcher": {
            "powershell": WIN243_PS1,
            "compatibilityCmd": WIN243_CMD,
            "readableCmd": WIN243_EASY_CMD,
            "catalog": WIN243_CATALOG,
        },
    }


def _verify_output(path: Path, lock: dict, entries: list, catalog_name: str,
                   catalog_bytes: bytes) -> None:
    expected = {item["path"] for item in entries}
    expected.add(catalog_name)
    with zipfile.ZipFile(path) as zf:
        infos = zf.infolist()
        names = [info.filename for info in infos]
        if len(names) != len(set(names)):
            _fail("output zip contains duplicate entry names")
        if set(names) != expected:
            missing = sorted(expected - set(names))
            extra = sorted(set(names) - expected)
            _fail(f"output zip entry set mismatch; missing={missing[:5]} extra={extra[:5]}")
        for name in names:
            _check_arc(name, "output zip")
        bad = zf.testzip()
        if bad is not None:
            _fail(f"output zip CRC failure: {bad}")
        catalog = json.loads(zf.read(catalog_name).decode("utf-8"))
        if catalog.get("version") != WIN243_VERSION:
            _fail("output catalog version mismatch")
        if catalog.get("releaseCommit") is None:
            _fail("output catalog missing commit")
        if catalog.get("game") != [dict(item) for item in lock["game"]]:
            _fail("output catalog game hashes changed")
        if len(catalog.get("payload", [])) != len(entries) - 2:
            _fail("output catalog payload count mismatch")
        for item in catalog.get("payload", []):
            if item["path"] not in names:
                _fail(f"catalog payload missing from zip: {item['path']}")
            h = hashlib.sha256()
            size = 0
            with zf.open(item["path"]) as src:
                for chunk in iter(lambda: src.read(1 << 20), b""):
                    h.update(chunk)
                    size += len(chunk)
            if size != item["size"] or h.hexdigest() != item["sha256"]:
                _fail(f"output payload hash mismatch: {item['path']}")
        cfg = zf.read(lock["loaderConfig"]["path"])
        if _hash_bytes(cfg) != lock["loaderConfig"]["sha256"]:
            _fail("output loader config hash mismatch")
        parse_fixed_interop_config(cfg.decode("utf-8-sig"))
        ps1 = zf.read(WIN243_PS1)
        if not ps1.startswith(b"\xef\xbb\xbf"):
            _fail("output launcher lost its UTF-8 BOM")
        if zf.read(catalog_name) != catalog_bytes:
            _fail("output catalog bytes changed")
        for item in lock["game"]:
            if item["path"] in names:
                _fail(f"game file must not be packaged: {item['path']}")


def build_win243_package(repo: Path, lock: dict, runtime_root: Path, bootstrap_root: Path,
                         mod_dll: Path, source_archive: Path, output_dir: Path,
                         commit: str, version: str) -> dict:
    if version != WIN243_VERSION:
        _fail(f"win243-x64 mode requires version {WIN243_VERSION}")
    built = datetime.now(timezone.utc).isoformat(timespec="seconds")
    runtime_entries = collect_runtime_files(lock, runtime_root)
    bootstrap_entries = verify_bootstrap(lock, bootstrap_root)
    cfg_bytes = load_loader_config(repo, lock)
    launcher = load_launcher_files(repo, lock)
    docs = load_player_docs(repo, lock)
    support_tools = load_support_tools(repo)
    mod_bytes, mod_sha = validate_mod_dll(mod_dll, lock)
    source = validate_source_archive(source_archive)

    out_dir = Path(output_dir)
    if out_dir.exists() and not out_dir.is_dir():
        _fail(f"output-dir is not a directory: {out_dir}")
    out_dir.mkdir(parents=True, exist_ok=True)
    out = out_dir / WIN243_OUT_NAME.format(version)
    sidecar = out_dir / (out.name + ".sha256")
    if out.exists() or sidecar.exists():
        _fail(f"output already exists: {out} or {sidecar}")

    entries = []
    seen = set()
    fd, tmp_name = tempfile.mkstemp(prefix=out.name + ".", suffix=".tmp", dir=out_dir)
    os.close(fd)
    tmp = Path(tmp_name)
    try:
        with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
            for item in runtime_entries:
                path = _safe_under(runtime_root, item["path"], "runtime-root")
                _write_file(zf, item["path"], path, item["category"], entries, seen, item)
            for item in bootstrap_entries:
                path = _safe_under(bootstrap_root, item["path"], "bootstrap-root")
                _write_file(zf, item["path"], path, "bootstrap", entries, seen, item)
            _write_bytes(zf, lock["loaderConfig"]["path"], cfg_bytes,
                         "loaderConfig", entries, seen)
            _write_bytes(zf, lock["mod"]["path"], mod_bytes, "mod", entries, seen)
            for name in lock["docs"]:
                _write_bytes(zf, name, docs[name], "docs", entries, seen)
            _write_bytes(zf, lock["notice"], docs[lock["notice"]], "docs", entries, seen)
            for name in lock["launcher"]["files"]:
                _write_bytes(zf, name, launcher[name], "launcher", entries, seen)
            for name, data in sorted(support_tools.items()):
                _write_bytes(zf, name, data, "supportTools", entries, seen)

            manifest = _manifest_text(lock, commit, built, source, entries, version)
            _write_bytes(zf, WIN243_MANIFEST, manifest.encode("utf-8"),
                         "manifest", entries, seen)
            payload_files = len(entries) - 1  # loader config is startup-mutable
            archive_entries = len(entries) + 1  # catalog
            catalog = _catalog(lock, commit, built, source, version, entries,
                               archive_entries, payload_files)
            catalog_bytes = (json.dumps(catalog, ensure_ascii=True, indent=2) + "\n").encode("utf-8")
            _write_bytes(zf, WIN243_CATALOG, catalog_bytes, "catalog", entries, seen)
        _verify_output(tmp, lock, entries, WIN243_CATALOG, catalog_bytes)
        zip_sha = sha256(tmp)
        os.replace(tmp, out)
        try:
            with open(sidecar, "x", encoding="ascii") as handle:
                handle.write(f"{zip_sha}  {out.name}\n")
        except Exception:
            if out.exists():
                out.unlink()
            raise
    except Exception:
        if tmp.exists():
            tmp.unlink()
        raise

    return {
        "output": str(out),
        "sidecar": str(sidecar),
        "zipSha256": zip_sha,
        "entries": len(entries),
        "payloadFiles": payload_files,
        "runtimeFiles": len(runtime_entries),
        "bootstrapFiles": len(bootstrap_entries),
        "generatedBindings": WIN243_EXPECTED_COUNTS["generatedBindings"],
        "modSha256": mod_sha,
        "sourceArchive": source,
        "commit": commit,
        "version": version,
    }


def parse_win243_args(argv: list):
    parser = argparse.ArgumentParser(
        prog="package_release.py",
        description="ohmymods packager: legacy single-version mode or explicit win243-x64 release mode")
    parser.add_argument("--platform", required=True, choices=[WIN243_PLATFORM])
    parser.add_argument("--version", default=WIN243_VERSION)
    parser.add_argument("--runtime-root", required=True, type=Path)
    parser.add_argument("--bootstrap-root", type=Path,
                        help="doorstop three-file readback root (default: runtime-root)")
    parser.add_argument("--mod-dll", required=True, type=Path)
    parser.add_argument("--source-archive", type=Path)
    parser.add_argument("--output-dir", required=True, type=Path)
    parser.add_argument("--commit", required=True)
    return parser.parse_args(argv)


def win243_main(args, repo: Path = None) -> dict:
    repo = Path(repo if repo is not None else REPO)
    if args.platform != WIN243_PLATFORM:
        _fail(f"--platform must be {WIN243_PLATFORM}")
    if args.version != WIN243_VERSION:
        _fail(f"--version must be {WIN243_VERSION} for {WIN243_PLATFORM}")
    lock = load_runtime_lock(repo)
    commit = _check_commit(repo, args.commit)
    bootstrap_root = args.bootstrap_root if args.bootstrap_root is not None else args.runtime_root
    result = build_win243_package(
        repo, lock, args.runtime_root, bootstrap_root, args.mod_dll,
        args.source_archive, args.output_dir, commit, args.version)
    print(f"OK: {result['output']}  entries={result['entries']}  "
          f"runtime={result['runtimeFiles']}  generated={result['generatedBindings']}")
    print(f"ZIP SHA256: {result['zipSha256']}")
    print(f"SIDECAR: {result['sidecar']}")
    return result


def main(argv=None):
    argv = list(sys.argv[1:] if argv is None else argv)
    if not argv:
        print(__doc__)
        return 2
    if len(argv) == 1 and not argv[0].startswith("-"):
        legacy_main(argv[0].strip())
        return 0
    if "--support-tools-only" in argv:
        parser = argparse.ArgumentParser(description="Build the standalone Windows support tools")
        parser.add_argument("--support-tools-only", action="store_true", required=True)
        parser.add_argument("--output-dir", required=True, type=Path)
        parser.add_argument("--commit", required=True)
        args = parser.parse_args(argv)
        try:
            _check_commit(REPO, args.commit)
            result = build_support_tools_zip(REPO, args.output_dir)
        except (PackageError, OSError) as exc:
            print(f"ERROR: {exc}", file=sys.stderr)
            return 1
        print(f"OK: {result['output']}  sha256={result['zipSha256']}")
        return 0
    args = parse_win243_args(argv)
    try:
        win243_main(args)
    except PackageError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
