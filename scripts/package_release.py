#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ohmymods IL2CPP 发布打包脚本（从 runbook/NEXT-UP 附录模板重建，v3.0.0 起入库）。

用法：python scripts/package_release.py <ModVersion>   # 如 3.0.0
前置：il2cpp 已构建（bin/Debug）；工作区 clean（BUILD-MANIFEST 需要 commit）。
产物：release/KingdomEnhancedMod_v<ModVersion>_IL2CPP.zip
"""
import hashlib
import subprocess
import sys
import zipfile
from datetime import datetime, timezone
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
GAME = Path(r"E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091")
OUT_NAME = "KingdomEnhancedMod_v{}_IL2CPP.zip"

# 允许进入包的 BepInEx 子路径（防泄漏：cache/interop/LogOutput 等一律不进）
BEPINEX_ALLOW = {
    "core": None,          # 全部
    "unity-libs": None,    # 全部
}


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def leak_check(rel: str) -> bool:
    low = rel.lower().replace("\\", "/")
    bad = (".bak", "logoutput", "/cache/", "/interop/", "skidrow", ".pdb",
           "_data/", "doorstop_config", ".doorstop_version")
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


def main():
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(2)
    version = sys.argv[1].strip()
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
        f"GitCommit: {commit}\n"
        f"DllSHA256: {dll_sha}\n"
        f"DllSize: {dll.stat().st_size}\n"
        f"GeneratedUtc: {datetime.now(timezone.utc).isoformat()}\n"
    )

    count = 0
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
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
            zf.write(cfg, f"BepInEx/config/{cfg.name}")
            count += 1
        zf.write(dll, "BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll")
        count += 1
        # 玩家文档
        rel = REPO / "release"
        for name in ("MOD_UPDATE_AND_FIX_LOG_ZH.txt", "MOD_USER_GUIDE_ZH.txt",
                     "MOD_CAPABILITIES_AND_ROADMAP_ZH.txt"):
            src = rel / name
            if src.is_file():
                zf.write(src, name)
                count += 1
        notes = rel / "release-notes-il2cpp.md"
        if notes.is_file():
            zf.write(notes, "INSTALL.md")
            count += 1
        zf.writestr("BUILD-MANIFEST.txt", manifest)
        count += 1

    print(f"OK: {out}  items={count}  size={out.stat().st_size}")
    print(manifest)


if __name__ == "__main__":
    main()
