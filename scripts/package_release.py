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
import xml.etree.ElementTree as ET
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


def main():
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(2)
    version = sys.argv[1].strip()
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


if __name__ == "__main__":
    main()
