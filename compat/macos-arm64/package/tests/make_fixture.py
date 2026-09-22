#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""测试夹具生成器（仅测试使用，不接触真实游戏/运行时）。

动作（可组合）：
  --root DIR            生成假游戏 + 包骨架（--layout beside|inside，--arch 控制可执行
                        文件架构：universal|arm64|x86|text）。包内含 game-lock.json
                        （四处指纹）与 SHA256SUMS（覆盖骨架不可变文件）。
  --input-root DIR      生成合成构建输入根：BepInEx/core、dotnet、
                        BepInEx/plugins/KingdomEnhancedMod、libdoorstop.dylib，
                        外加干扰项（BepInEx/interop、unity-libs、cache、config、日志）。
  --notices-dir DIR     生成合成第三方材料目录（third-party/…）。
  --builder-home DIR    生成「构建器家目录」副本：复制真实 build_package.py /
                        launcher.command / README.md / defaults/，并伪造 REBUILD.md、
                        VALIDATION.md、tools/sanitize_codeview.py、
                        metadata-sanitization-receipts/receipt.json（操作员/其他
                        Worker 材料的测试替身）。测试经 builder_driver.py 从家目录
                        import 构建器，monkeypatch 契约常量后运行。
  --emit-lock PATH      生成完整 input-lock.json（schema v2，真实计算哈希）。
                        给了 --builder-home 时包材料哈希取自家目录副本。

输出：单行 JSON facts（各路径与哈希）。
"""

import argparse
import hashlib
import json
import os
import shutil
import struct
import sys

CPU_ARM64 = 0x0100000C
CPU_X86_64 = 0x01000007
GAME_APP = "KingdomTwoCrowns.app"
PKG_DEFAULT = "OhMyMods-Mac-ARM64"

# 需要被 dyld 加载的原生库（与 launcher.command 的固定检测/信任名单一致）。
# 夹具必须提供这些文件，否则每个用例都会在原生库门就失败；一致性由 run_tests.sh
# 的防漂移断言（launcher 名单 == input-lock.json 的 *.dylib 集合）覆盖。
NATIVE_RELS = [
    "libdoorstop.dylib",
    "BepInEx/core/libdobby.dylib",
    "dotnet/libSystem.Globalization.Native.dylib",
    "dotnet/libSystem.IO.Compression.Native.dylib",
    "dotnet/libSystem.Native.dylib",
    "dotnet/libSystem.Net.Security.Native.dylib",
    "dotnet/libSystem.Security.Cryptography.Native.Apple.dylib",
    "dotnet/libSystem.Security.Cryptography.Native.OpenSsl.dylib",
    "dotnet/libclrjit.dylib",
    "dotnet/libcoreclr.dylib",
    "dotnet/libdbgshim.dylib",
    "dotnet/libhostpolicy.dylib",
    "dotnet/libmscordaccore.dylib",
    "dotnet/libmscordbi.dylib",
]


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        h.update(f.read())
    return h.hexdigest()


def mach_header(cpu_type):
    return struct.pack("<7I", 0xFEEDFACF, cpu_type & 0xFFFFFFFF, 0, 2, 0, 0, 0)


def universal_binary():
    arm = mach_header(CPU_ARM64)
    x86 = mach_header(CPU_X86_64)
    header_size = 8 + 2 * 20
    arm_off, x86_off = header_size, header_size + len(arm)
    header = struct.pack(">II", 0xCAFEBABE, 2)
    header += struct.pack(">5I", CPU_ARM64, 0, arm_off, len(arm), 14)
    header += struct.pack(">5I", CPU_X86_64, 3, x86_off, len(x86), 12)
    return header + arm + x86


def write(path, data, mode=None):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(data)
    if mode is not None:
        os.chmod(path, mode)


def exe_bytes(arch):
    if arch == "universal":
        return universal_binary()
    if arch == "arm64":
        return mach_header(CPU_ARM64)
    if arch == "x86":
        return mach_header(CPU_X86_64)
    if arch == "text":
        return b"#!/bin/sh\necho not a mach-o\n"
    raise ValueError(arch)


def deterministic_blob(seed, size=4096):
    out = bytearray()
    block = hashlib.sha256(seed.encode("utf-8")).digest()
    while len(out) < size:
        block = hashlib.sha256(block).digest()
        out += block
    return bytes(out[:size])


PLIST = b"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
 "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
        <key>CFBundleExecutable</key>
        <string>KingdomTwoCrowns</string>
        <key>CFBundleIdentifier</key>
        <string>com.noio.kingdomtwocrowns</string>
</dict>
</plist>
"""


def make_game(root, arch):
    app = os.path.join(root, GAME_APP)
    write(os.path.join(app, "Contents/MacOS/KingdomTwoCrowns"), exe_bytes(arch), 0o755)
    write(os.path.join(app, "Contents/Frameworks/GameAssembly.dylib"),
          deterministic_blob("game-assembly-seed", 8192))
    write(os.path.join(app, "Contents/Info.plist"), PLIST)
    write(os.path.join(app, "Contents/Resources/Data/boot.config"),
          b"player-settings-data\n")
    write(os.path.join(app, "Contents/Resources/Data/il2cpp_data/Metadata/global-metadata.dat"),
          deterministic_blob("metadata-seed", 2048))
    return app


def game_facts(app):
    return {
        "assembly_sha256": sha256_file(os.path.join(app, "Contents/Frameworks/GameAssembly.dylib")),
        "executable_sha256": sha256_file(os.path.join(app, "Contents/MacOS/KingdomTwoCrowns")),
        "metadata_sha256": sha256_file(
            os.path.join(app, "Contents/Resources/Data/il2cpp_data/Metadata/global-metadata.dat")),
        "infoplist_sha256": sha256_file(os.path.join(app, "Contents/Info.plist")),
    }


def make_package_skeleton(parent, pkg_name, game_root):
    pkg = os.path.join(parent, pkg_name)
    os.makedirs(pkg, exist_ok=True)
    source_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    # 复制真实 launcher 与 defaults 进夹具包（测试运行的就是被测脚本本身）
    write(os.path.join(pkg, "launcher.command"),
          open(os.path.join(source_dir, "launcher.command"), "rb").read(), 0o755)
    write(os.path.join(pkg, "BepInEx/core/BepInEx.Unity.IL2CPP.dll"),
          deterministic_blob("stub-core-dll", 512))
    write(os.path.join(pkg, "dotnet/libcoreclr.dylib"), deterministic_blob("stub-coreclr", 512))
    write(os.path.join(pkg, "libdoorstop.dylib"), deterministic_blob("stub-doorstop", 512))
    # 固定名单原生库（launcher 只读检测/信任模式的检查对象；已有桩文件保持不变）
    for rel in NATIVE_RELS:
        full = os.path.join(pkg, rel)
        if not os.path.exists(full):
            write(full, deterministic_blob("native-fixture:" + rel, 512))
    os.makedirs(os.path.join(pkg, "defaults"), exist_ok=True)
    write(os.path.join(pkg, "defaults/BepInEx.cfg"),
          open(os.path.join(source_dir, "defaults/BepInEx.cfg"), "rb").read())
    lock = {"schema": "ohmymods-arm64-game-lock/2"}
    lock.update(game_facts(os.path.join(game_root, GAME_APP)))
    lock.update({
        "game_build": "2.4.0 r23485",
        "unity_version": "6000.0.61f1",
        "source_tag": "1088b9c",
    })
    with open(os.path.join(pkg, "game-lock.json"), "w", encoding="utf-8") as f:
        json.dump(lock, f, ensure_ascii=False, sort_keys=True, indent=2)
        f.write("\n")
    # SHA256SUMS：骨架不可变 payload + game-lock.json（与构建器约定一致，可变目录不列）
    sums = []
    for rel in sorted(set(("BepInEx/core/BepInEx.Unity.IL2CPP.dll", "defaults/BepInEx.cfg",
                           "dotnet/libcoreclr.dylib", "game-lock.json", "launcher.command",
                           "libdoorstop.dylib") + tuple(NATIVE_RELS))):
        sums.append("%s  %s" % (sha256_file(os.path.join(pkg, rel)), rel))
    with open(os.path.join(pkg, "SHA256SUMS"), "w", encoding="utf-8") as f:
        f.write("\n".join(sums) + "\n")
    return pkg


def make_input_root(root):
    os.makedirs(root, exist_ok=True)
    write(os.path.join(root, "BepInEx/core/BepInEx.Unity.IL2CPP.dll"),
          deterministic_blob("ir-core-il2cpp", 1024))
    write(os.path.join(root, "BepInEx/core/BepInEx.Core.dll"),
          deterministic_blob("ir-core-core", 1024))
    write(os.path.join(root, "BepInEx/core/0Harmony.dll"),
          deterministic_blob("ir-core-harmony", 1024))
    write(os.path.join(root, "BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll"),
          deterministic_blob("ir-mod-dll", 4096))
    write(os.path.join(root, "dotnet/libcoreclr.dylib"), deterministic_blob("ir-coreclr", 2048))
    write(os.path.join(root, "dotnet/System.Runtime.dll"), deterministic_blob("ir-sysruntime", 2048))
    write(os.path.join(root, "libdoorstop.dylib"), deterministic_blob("ir-doorstop", 2048))
    # 干扰项：一律不得进入包
    write(os.path.join(root, "BepInEx/interop/generated.dll"), deterministic_blob("ir-interop", 128))
    write(os.path.join(root, "BepInEx/unity-libs/UnityEngine.dll"), deterministic_blob("ir-unitylib", 128))
    write(os.path.join(root, "BepInEx/cache/assembly-cache.bin"), deterministic_blob("ir-cache", 128))
    write(os.path.join(root, "BepInEx/config/BepInEx.cfg"), b"[Caching]\nEnableAssemblyCache = true\n")
    write(os.path.join(root, "BepInEx/LogOutput.log"), b"log line\n")
    os.makedirs(os.path.join(root, "BepInEx/plugins/MacProbe"), exist_ok=True)
    write(os.path.join(root, "run_bepinex.sh"), b"#!/bin/sh\nexit 0\n", 0o755)


def make_notices(root):
    write(os.path.join(root, "third-party/Dobby-LICENSE"), b"dummy license text\n")
    write(os.path.join(root, "third-party/dobby-macos-arm64.patch"), b"diff --git a/x b/x\n")


def make_builder_home(root):
    """复制构建器及其包材料到测试家目录，并伪造操作员/其他 Worker 材料。"""
    source_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    home = root
    os.makedirs(os.path.join(home, "defaults"), exist_ok=True)
    for rel in ("build_package.py", "launcher.command", "README.md"):
        shutil.copyfile(os.path.join(source_dir, rel), os.path.join(home, rel))
        os.chmod(os.path.join(home, rel), 0o755 if rel == "launcher.command" else 0o644)
    shutil.copyfile(os.path.join(source_dir, "defaults/BepInEx.cfg"),
                    os.path.join(home, "defaults/BepInEx.cfg"))
    write(os.path.join(home, "REBUILD.md"), b"# REBUILD (fixture)\n")
    write(os.path.join(home, "VALIDATION.md"), b"# VALIDATION (fixture)\n")
    write(os.path.join(home, "tools/sanitize_codeview.py"),
          b"#!/usr/bin/env python3\n# fixture stand-in\n")
    write(os.path.join(home, "metadata-sanitization-receipts/receipt.json"),
          b'{"fixture": true}\n')
    return home


def category_of(rel, mod_plugin_dir):
    if rel == "libdoorstop.dylib":
        return "input-root"
    if rel.startswith("dotnet/"):
        return "input-root"
    if rel.startswith("BepInEx/core/"):
        return "input-root"
    if rel.startswith("BepInEx/plugins/%s/" % mod_plugin_dir):
        return "input-root"
    if rel in ("launcher.command", "README.md", "REBUILD.md", "VALIDATION.md") \
            or rel.startswith("defaults/") or rel.startswith("tools/") \
            or rel.startswith("metadata-sanitization-receipts/"):
        return "package-material"
    if rel.startswith("third-party/"):
        return "notices"
    return None


def emit_lock(path, input_root, notices_dir, game_root, materials_dir):
    files = []

    def add(root_dir, prefix):
        base = os.path.join(root_dir, prefix) if prefix else root_dir
        for dirpath, _dirnames, filenames in os.walk(base):
            for name in sorted(filenames):
                full = os.path.join(dirpath, name)
                rel = os.path.relpath(full, root_dir)
                files.append({
                    "path": rel,
                    "sha256": sha256_file(full),
                    "size": os.path.getsize(full),
                    "mode": "0755" if rel == "launcher.command" else "0644",
                    "source": category_of(rel, "KingdomEnhancedMod"),
                })

    add(input_root, "BepInEx/core")
    add(input_root, "dotnet")
    add(input_root, "BepInEx/plugins/KingdomEnhancedMod")
    add(input_root, ".")  # libdoorstop.dylib 等根文件（干扰项类别为 None 被过滤）
    add(materials_dir, "defaults")
    for rel in ("launcher.command", "README.md", "REBUILD.md", "VALIDATION.md",
                "tools/sanitize_codeview.py",
                "metadata-sanitization-receipts/receipt.json"):
        full = os.path.join(materials_dir, rel)
        if os.path.isfile(full):
            files.append({"path": rel, "sha256": sha256_file(full),
                          "size": os.path.getsize(full),
                          "mode": "0755" if rel == "launcher.command" else "0644",
                          "source": "package-material"})
    add(notices_dir, "third-party")
    files = [f for f in files if f["source"] is not None]
    files = sorted({f["path"]: f for f in files}.values(), key=lambda e: e["path"])
    game = game_facts(os.path.join(game_root, GAME_APP))
    mod_sha = sha256_file(os.path.join(input_root,
                                       "BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll"))
    lock = {
        "schema": "ohmymods-arm64-package-input-lock/2",
        "package": {
            "folder": PKG_DEFAULT,
            "version": "test-fixture-1",
            "mod_plugin_dir": "KingdomEnhancedMod",
            "framework_archive": "9027335",
            "mod_dll_sha256": mod_sha,
            "arch": "arm64",
        },
        "game": dict(game, source_tag="1088b9c", game_build="2.4.0 r23485",
                     unity_version="6000.0.61f1"),
        "files": files,
    }
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(lock, f, ensure_ascii=False, sort_keys=True, indent=2)
        f.write("\n")
    return lock


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", help="生成假游戏与包骨架的根目录")
    parser.add_argument("--layout", choices=["beside", "inside"], default="beside")
    parser.add_argument("--arch", choices=["universal", "arm64", "x86", "text"], default="universal")
    parser.add_argument("--pkg-name", default=PKG_DEFAULT)
    parser.add_argument("--input-root", help="生成合成构建输入根")
    parser.add_argument("--notices-dir", help="生成合成第三方材料目录")
    parser.add_argument("--builder-home", help="生成构建器家目录副本（含操作员材料替身）")
    parser.add_argument("--emit-lock", help="生成完整 input-lock.json 到该路径")
    args = parser.parse_args()

    facts = {"layout": args.layout, "arch": args.arch}
    game_root = None
    if args.root:
        game_root = args.root
        app = make_game(args.root, args.arch)
        facts["app"] = app
        facts.update(game_facts(app))
        if args.layout == "beside":
            pkg = make_package_skeleton(args.root, args.pkg_name, args.root)
        else:
            inner_root = os.path.join(args.root, args.pkg_name)
            os.makedirs(inner_root, exist_ok=True)
            inner_app = make_game(inner_root, args.arch)
            pkg = make_package_skeleton(args.root, args.pkg_name, inner_root)
            facts["inner_app"] = inner_app
        facts["pkg"] = pkg
    if args.input_root:
        make_input_root(args.input_root)
        facts["input_root"] = os.path.abspath(args.input_root)
    if args.notices_dir:
        make_notices(args.notices_dir)
        facts["notices_dir"] = os.path.abspath(args.notices_dir)
    if args.builder_home:
        home = make_builder_home(args.builder_home)
        facts["builder_home"] = os.path.abspath(home)
    if args.emit_lock:
        if not (args.input_root and args.notices_dir and game_root):
            parser.error("--emit-lock 需要 --input-root --notices-dir --root")
        materials = args.builder_home or os.path.dirname(
            os.path.dirname(os.path.abspath(__file__)))
        lock = emit_lock(args.emit_lock, args.input_root, args.notices_dir, game_root,
                         materials)
        facts["lock_path"] = os.path.abspath(args.emit_lock)
        facts["lock_files"] = len(lock["files"])
    print(json.dumps(facts, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
