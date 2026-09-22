#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""OhMyMods Mac ARM64 发行包构建器（开发者侧，Python 3.8+，仅标准库）。

从「实验布局」式的输入根目录 + 操作员固化的 input-lock.json 生成可复现 ZIP：
  OhMyMods-Mac-ARM64/
    launcher.command            (0755)
    README.md
    defaults/BepInEx.cfg
    SHA256SUMS                  (不可变 payload 清单，启动器每启核验)
    game-lock.json              (由 lock.game 派生，启动器运行时核验游戏四指纹)
    package-manifest.json       (payload 逐文件 sha256/size + 生成物单列，防混包对账)
    BepInEx/core/**             (来自输入根)
    BepInEx/plugins/<mod>/**    (仅锁定插件目录)
    dotnet/**                   (CoreCLR 运行时)
    libdoorstop.dylib
    REBUILD.md / VALIDATION.md / tools/** / metadata-sanitization-receipts/**
                                (操作员/其他 Worker 材料，只读取，仅在显式列入清单时打包)
    third-party/**              (来自 --notices-source：许可/源码材料)

硬边界：
  - 只打包 input-lock.json 显式清单内的文件，逐文件校验 SHA256 与 mode；
    清单缺一个、多一个（白名单子树内）、哈希不符、含符号链接、路径穿越、
    绝对路径、含换行、重复条目均拒绝。必备组件缺失即拒绝，无跳过 flag。
  - 拒绝占位符哈希（未固化），强制 GameAssembly 哈希等于契约固定值。
  - 绝不打包 interop / unity-libs / cache / config / 日志 / 游戏本体 / 存档
    （SHA256SUMS 也排除全部运行时可变文件）。
  - game 四指纹：GameAssembly / 可执行文件 / Info.plist /
    Data/il2cpp_data/Metadata/global-metadata.dat（metadata 即指最后一个）。
  - ZIP 条目排序、固定时间戳（2020-01-01T00:00:00）、固定权限，内容一致则字节可复现。
  - 输出已存在时默认拒绝，需 --force。
  - manifest/SHA256SUMS 是防混包对账，不是防恶意替换的签名。

用法：
  python3 build_package.py \
      --input-root  /path/to/lab-like-root        # 含 BepInEx/ dotnet/ libdoorstop.dylib
      --game-app    /path/to/KingdomTwoCrowns.app # 游戏四指纹校验
      --notices-source /path/to/notices           # 第三方材料（内含 third-party/）
      --lock        /path/to/input-lock.json
      --output      /path/to/OhMyMods-Mac-ARM64.zip
  可选: --force                        覆盖已存在输出
        --emit-lock-template OUT.json  扫描输入并生成候选 lock 模板供操作员复核（不构建）

input-lock.json 模式（schema ohmymods-arm64-package-input-lock/2）：
  package: {folder, version, mod_plugin_dir, framework_archive, mod_dll_sha256, arch}
  game:    {assembly_sha256, executable_sha256, metadata_sha256(global-metadata.dat),
            infoplist_sha256(Info.plist), source_tag, game_build, unity_version}
  files:   [{path, sha256, mode, source}]
    source 取值与路径前缀约束：
      input-root      -> libdoorstop.dylib | dotnet/** | BepInEx/core/** | BepInEx/plugins/<mod_plugin_dir>/**
      package-material-> launcher.command(mode 必须 0755) | README.md | defaults/**
                         | REBUILD.md | VALIDATION.md | tools/** | metadata-sanitization-receipts/**
      notices         -> third-party/**
    保留生成路径 game-lock.json / package-manifest.json / SHA256SUMS 不得出现在 files。
"""

import argparse
import hashlib
import json
import os
import struct
import sys
import zipfile

SCHEMA = "ohmymods-arm64-package-input-lock/2"
GAME_LOCK_SCHEMA = "ohmymods-arm64-game-lock/2"
MANIFEST_SCHEMA = "ohmymods-arm64-package-manifest/2"
PACKAGE_FOLDER = "OhMyMods-Mac-ARM64"
GAME_APP_NAME = "KingdomTwoCrowns.app"
GAME_EXE_REL = "Contents/MacOS/KingdomTwoCrowns"
GAME_ASSEMBLY_REL = "Contents/Frameworks/GameAssembly.dylib"
GAME_META_REL = "Contents/Info.plist"
GAME_METADATA_REL = "Contents/Resources/Data/il2cpp_data/Metadata/global-metadata.dat"
# 契约固定的 GameAssembly 指纹（Kingdom Two Crowns 2.4.0 r23485 ARM64）。
BUILTIN_ASSEMBLY_SHA256 = "738fb98871dd6e2136474325ea3f7f4f81f2094873a6bc88f7a942d268660b1a"
FIXED_TIMESTAMP = (2020, 1, 1, 0, 0, 0)
HEX_CHARS = set("0123456789abcdef")
CPU_TYPE_ARM64 = 0x0100000C
BINARY_EXTENSIONS = {".dll", ".dylib", ".so", ".exe", ".bin", ".o", ".a"}
GENERATED_PATHS = {"game-lock.json", "package-manifest.json", "SHA256SUMS"}


def fail(msg):
    print("错误: %s" % msg, file=sys.stderr)
    sys.exit(1)


def info(msg):
    print(msg)


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while True:
            chunk = f.read(1024 * 1024)
            if not chunk:
                break
            h.update(chunk)
    return h.hexdigest()


def is_hex64(value):
    return (
        isinstance(value, str)
        and len(value) == 64
        and all(c in HEX_CHARS for c in value)
    )


def looks_binary(path):
    ext = os.path.splitext(path)[1].lower()
    if ext in BINARY_EXTENSIONS:
        return True
    try:
        with open(path, "rb") as f:
            head = f.read(16)
    except OSError:
        return True
    if b"\x00" in head:
        return True
    if head[:4] in (b"\xcf\xfa\xed\xfe", b"\xca\xfe\xba\xbe", b"\x7fELF", b"MZ"):
        return True
    return False


def macho_has_arm64(path):
    try:
        with open(path, "rb") as f:
            head = f.read(4096)
    except OSError:
        return False
    if len(head) < 8:
        return False
    magic = struct.unpack("<I", head[:4])[0]
    if magic in (0xFEEDFACE, 0xFEEDFACF):
        return struct.unpack("<i", head[4:8])[0] == CPU_TYPE_ARM64
    if struct.unpack(">I", head[:4])[0] == 0xCAFEBABE:
        count = struct.unpack(">I", head[4:8])[0]
        offset = 8
        for _ in range(min(count, 32)):
            if offset + 20 > len(head):
                break
            cpu = struct.unpack(">I", head[offset:offset + 4])[0]
            if cpu == CPU_TYPE_ARM64:
                return True
            offset += 20
    return False


def validate_rel_path(rel):
    if not rel or rel.startswith("/") or rel.startswith("\\") or "\\" in rel:
        return False
    if "\n" in rel or "\r" in rel:
        return False
    parts = rel.split("/")
    for part in parts:
        if part in ("", ".", ".."):
            return False
    return True


def check_no_symlink(root, rel):
    """拒绝 root/rel 解析链上的任何符号链接，返回完整路径。"""
    cur = root
    for part in rel.split("/"):
        cur = os.path.join(cur, part)
        if os.path.islink(cur):
            fail("输入包含符号链接，拒绝打包: %s" % os.path.join(root, rel))
    return os.path.join(root, rel)


def load_and_validate_lock(lock_path):
    try:
        with open(lock_path, "r", encoding="utf-8") as f:
            lock = json.load(f)
    except (OSError, ValueError) as exc:
        fail("无法读取 input-lock.json: %s" % exc)
    if not isinstance(lock, dict) or lock.get("schema") != SCHEMA:
        fail("input-lock.json schema 不符（期望 %s）" % SCHEMA)
    pkg = lock.get("package")
    game = lock.get("game")
    files = lock.get("files")
    if not isinstance(pkg, dict) or not isinstance(game, dict) or not isinstance(files, list):
        fail("input-lock.json 缺少 package/game/files 结构")
    for field in ("folder", "version", "mod_plugin_dir", "framework_archive",
                  "mod_dll_sha256", "arch"):
        value = pkg.get(field)
        if not isinstance(value, str) or not value.strip():
            fail("input-lock.package.%s 缺失或为空（未固化，请操作员填入）" % field)
    if pkg["folder"] != PACKAGE_FOLDER:
        fail("input-lock.package.folder 必须为 %s" % PACKAGE_FOLDER)
    if pkg["arch"] != "arm64":
        fail("input-lock.package.arch 必须为 arm64")
    if not is_hex64(pkg["mod_dll_sha256"]):
        fail("input-lock.package.mod_dll_sha256 不是 64 位十六进制哈希（未固化）")
    for field in ("assembly_sha256", "executable_sha256", "metadata_sha256",
                  "infoplist_sha256"):
        if not is_hex64(game.get(field)):
            fail("input-lock.game.%s 不是 64 位十六进制哈希（未固化，请操作员填入）" % field)
    if game["assembly_sha256"] != BUILTIN_ASSEMBLY_SHA256:
        fail("input-lock.game.assembly_sha256 与契约固定值不符:\n  锁定: %s\n  固定: %s"
             % (game["assembly_sha256"], BUILTIN_ASSEMBLY_SHA256))
    for field in ("source_tag", "game_build", "unity_version"):
        value = game.get(field)
        if not isinstance(value, str) or not value.strip():
            fail("input-lock.game.%s 缺失或为空" % field)
    seen = set()
    for entry in files:
        if not isinstance(entry, dict):
            fail("files 含非对象条目")
        rel = entry.get("path")
        mode = entry.get("mode")
        source = entry.get("source")
        sha = entry.get("sha256")
        if not isinstance(rel, str) or not validate_rel_path(rel):
            fail("files 含非法路径（绝对/穿越/换行/空组件）: %r" % (rel,))
        if rel in seen:
            fail("files 路径重复: %s" % rel)
        seen.add(rel)
        if rel in GENERATED_PATHS:
            fail("files 不得包含构建生成路径: %s" % rel)
        if mode not in ("0644", "0755"):
            fail("files[].mode 仅允许 0644/0755: %s (%r)" % (rel, mode))
        if not is_hex64(sha):
            fail("files[].sha256 不是 64 位十六进制哈希（未固化）: %s" % rel)
        if source not in ("input-root", "package-material", "notices"):
            fail("files[].source 取值非法: %s (%r)" % (rel, source))
    return lock


def category_of(rel, mod_plugin_dir):
    if rel == "libdoorstop.dylib":
        return "input-root"
    if rel.startswith("dotnet/"):
        return "input-root"
    if rel.startswith("BepInEx/core/"):
        return "input-root"
    mod_prefix = "BepInEx/plugins/%s/" % mod_plugin_dir
    if rel.startswith(mod_prefix) or rel == "BepInEx/plugins/%s" % mod_plugin_dir:
        return "input-root"
    if rel in ("launcher.command", "README.md", "REBUILD.md", "VALIDATION.md") \
            or rel.startswith("defaults/") or rel.startswith("tools/") \
            or rel.startswith("metadata-sanitization-receipts/"):
        return "package-material"
    if rel.startswith("third-party/"):
        return "notices"
    return None


def verify_game_app(game_app, game_section):
    if not os.path.isdir(game_app):
        fail("--game-app 不是目录: %s" % game_app)
    if os.path.basename(os.path.normpath(game_app)) != GAME_APP_NAME:
        fail("--game-app 目录名必须精确为 %s" % GAME_APP_NAME)
    exe = os.path.join(game_app, GAME_EXE_REL)
    assembly = os.path.join(game_app, GAME_ASSEMBLY_REL)
    meta = os.path.join(game_app, GAME_META_REL)
    metadata = os.path.join(game_app, GAME_METADATA_REL)
    for path, label in ((exe, "可执行文件"), (assembly, "GameAssembly.dylib"),
                        (meta, "Info.plist"), (metadata, "global-metadata.dat")):
        if not os.path.isfile(path):
            fail("游戏结构不完整，缺少%s: %s" % (label, path))
    if not macho_has_arm64(exe):
        fail("游戏可执行文件不含 arm64 Mach-O 切片: %s" % exe)
    for path, label, key in (
            (assembly, "GameAssembly.dylib", "assembly_sha256"),
            (exe, "可执行文件", "executable_sha256"),
            (metadata, "global-metadata.dat", "metadata_sha256"),
            (meta, "Info.plist", "infoplist_sha256")):
        actual = sha256_file(path)
        expected = game_section[key]
        if actual != expected:
            fail("游戏指纹不符（%s）:\n  路径: %s\n  锁定: %s\n  实际: %s"
                 % (label, path, expected, actual))


SKIP_DIR_NAMES = {"__pycache__"}
SKIP_FILE_EXTS = {".pyc", ".pyo"}


def scan_tree(root, rel_prefix):
    """列出 root/rel_prefix 下全部常规文件（相对 root）；符号链接直接拒绝。

    排除 Python 缓存产物（__pycache__/*.pyc）：它们随本地测试运行出现，
    哈希不稳定，不属于任何清单。
    """
    files = set()
    base = os.path.join(root, rel_prefix) if rel_prefix else root
    if not os.path.isdir(base):
        return files
    for dirpath, dirnames, filenames in os.walk(base):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIR_NAMES]
        for name in dirnames:
            full = os.path.join(dirpath, name)
            if os.path.islink(full):
                fail("输入包含符号链接目录，拒绝打包: %s" % full)
        for name in filenames:
            full = os.path.join(dirpath, name)
            if os.path.islink(full):
                fail("输入包含符号链接文件，拒绝打包: %s" % full)
            if os.path.splitext(name)[1].lower() in SKIP_FILE_EXTS:
                continue
            if not os.path.isfile(full):
                fail("输入含非常规文件，拒绝打包: %s" % full)
            files.add(os.path.relpath(full, root))
    return files


def build(args):
    lock = load_and_validate_lock(args.lock)
    pkg = lock["package"]
    game = lock["game"]
    mod_plugin_dir = pkg["mod_plugin_dir"]

    output = os.path.abspath(args.output)
    if os.path.exists(output) and not args.force:
        fail("输出已存在（默认拒绝覆盖，如确认请加 --force）: %s" % output)

    script_dir = os.path.dirname(os.path.abspath(__file__))
    input_root = os.path.abspath(args.input_root)
    notices_source = os.path.abspath(args.notices_source)
    game_app = os.path.abspath(args.game_app)

    for label, path in (("输入根", input_root), ("notices", notices_source),
                        ("game-app", game_app)):
        if not os.path.isdir(path):
            fail("--%s 不是目录: %s" % (label, path))

    verify_game_app(game_app, game)

    mod_dir_prefix = "BepInEx/plugins/%s/" % mod_plugin_dir
    mod_dll_rel = "%s%s.dll" % (mod_dir_prefix, mod_plugin_dir)

    entries = {}
    payload_meta = {}
    for entry in lock["files"]:
        rel = entry["path"]
        cat = category_of(rel, mod_plugin_dir)
        if cat is None:
            fail("files 路径不属于任何白名单类别，拒绝: %s" % rel)
        if cat != entry["source"]:
            fail("files[].source 与路径类别不符: %s（路径类别 %s，声明 %s）"
                 % (rel, cat, entry["source"]))
        if rel == "launcher.command" and entry["mode"] != "0755":
            fail("launcher.command 的 mode 必须为 0755")
        if cat == "input-root":
            src_root = input_root
        elif cat == "package-material":
            src_root = script_dir
        else:
            src_root = notices_source
        src = check_no_symlink(src_root, rel)
        if not os.path.isfile(src):
            fail("清单文件不存在: %s（根: %s）" % (rel, src_root))
        actual_sha = sha256_file(src)
        if actual_sha != entry["sha256"]:
            fail("哈希不符: %s\n  锁定: %s\n  实际: %s" % (rel, entry["sha256"], actual_sha))
        entries[rel] = (src, entry["mode"])
        payload_meta[rel] = {
            "path": rel,
            "sha256": actual_sha,
            "size": os.path.getsize(src),
            "mode": entry["mode"],
            "source": entry["source"],
        }

    if mod_dll_rel not in entries:
        fail("清单缺少 Mod DLL: %s" % mod_dll_rel)
    if payload_meta[mod_dll_rel]["sha256"] != pkg["mod_dll_sha256"]:
        fail("files 中 Mod DLL 哈希与 package.mod_dll_sha256 不一致: %s" % mod_dll_rel)

    # ---- 白名单子树完整性：多出的文件（尤其二进制）拒绝 ----
    required_trees = ["BepInEx/core", "dotnet", "BepInEx/plugins/%s" % mod_plugin_dir]
    for tree in required_trees:
        present = scan_tree(input_root, tree)
        missing = present - set(entries.keys())
        if missing:
            for rel in sorted(missing):
                full = os.path.join(input_root, rel)
                if looks_binary(full):
                    fail("白名单子树 %s 存在未列入清单的二进制文件，拒绝: %s" % (tree, rel))
            for rel in sorted(missing):
                info("警告: 白名单子树存在未列入清单的文件（不会打包）: %s" % rel)

    # ---- package-material / notices 完整性 ----
    present = scan_tree(script_dir, "defaults")
    missing = present - set(entries.keys())
    if missing:
        fail("包材料目录 defaults/ 存在未列入清单的文件，请先更新清单: %s"
             % ", ".join(sorted(missing)))
    for rel in ("launcher.command", "README.md"):
        if rel not in entries:
            fail("清单缺少必备包材料: %s" % rel)
    third_party_dir = os.path.join(notices_source, "third-party")
    present = scan_tree(notices_source, "third-party") if os.path.isdir(third_party_dir) else set()
    missing = present - set(entries.keys())
    if missing:
        fail("notices 源存在未列入清单的文件，拒绝: %s" % ", ".join(sorted(missing)))

    # ---- 生成物：game-lock（四指纹）、manifest（payload 对账 + 生成物单列） ----
    game_lock = {
        "schema": GAME_LOCK_SCHEMA,
        "assembly_sha256": game["assembly_sha256"],
        "executable_sha256": game["executable_sha256"],
        "metadata_sha256": game["metadata_sha256"],
        "infoplist_sha256": game["infoplist_sha256"],
        "game_build": game["game_build"],
        "unity_version": game["unity_version"],
        "source_tag": game["source_tag"],
    }
    manifest = {
        "schema": MANIFEST_SCHEMA,
        "package": {
            "folder": pkg["folder"],
            "version": pkg["version"],
            "arch": pkg["arch"],
            "framework_archive": pkg["framework_archive"],
            "mod_dll_sha256": pkg["mod_dll_sha256"],
        },
        "game": dict(game),
        "payload": [payload_meta[k] for k in sorted(payload_meta)],
        "generated": sorted(GENERATED_PATHS),
    }

    def json_bytes(obj):
        return json.dumps(obj, ensure_ascii=False, sort_keys=True, indent=2).encode("utf-8") + b"\n"

    game_lock_bytes = json_bytes(game_lock)
    manifest_bytes = json_bytes(manifest)

    # ---- SHA256SUMS：不可变 payload + 两个生成 JSON（排除全部运行时可变文件） ----
    sums_rels = sorted(list(payload_meta.keys()) + ["game-lock.json", "package-manifest.json"])
    sums_lines = []
    for rel in sums_rels:
        if rel == "game-lock.json":
            digest = hashlib.sha256(game_lock_bytes).hexdigest()
        elif rel == "package-manifest.json":
            digest = hashlib.sha256(manifest_bytes).hexdigest()
        else:
            digest = payload_meta[rel]["sha256"]
        sums_lines.append("%s  %s" % (digest, rel))
    sums_bytes = ("\n".join(sums_lines) + "\n").encode("utf-8")

    # ---- 输入根透明度：列出被忽略的顶层项 ----
    ignored = []
    for name in sorted(os.listdir(input_root)):
        if name in ("BepInEx", "dotnet", "libdoorstop.dylib"):
            continue
        ignored.append(name)
    if ignored:
        info("输入根中以下项不在打包范围（已忽略）: %s" % ", ".join(ignored))
    bepinex_extra = []
    if os.path.isdir(os.path.join(input_root, "BepInEx")):
        for name in sorted(os.listdir(os.path.join(input_root, "BepInEx"))):
            if name in ("core", "plugins"):
                continue
            bepinex_extra.append("BepInEx/%s" % name)
    if bepinex_extra:
        info("BepInEx 下以下运行时目录不入包（首启重建/下载）: %s" % ", ".join(bepinex_extra))

    # ---- 写 ZIP（确定性：排序 + 固定时间戳 + 固定权限） ----
    arcnames = {}
    for rel in entries:
        arcnames["%s/%s" % (PACKAGE_FOLDER, rel)] = ("file", entries[rel][0], entries[rel][1])
    arcnames["%s/game-lock.json" % PACKAGE_FOLDER] = ("bytes", game_lock_bytes, "0644")
    arcnames["%s/package-manifest.json" % PACKAGE_FOLDER] = ("bytes", manifest_bytes, "0644")
    arcnames["%s/SHA256SUMS" % PACKAGE_FOLDER] = ("bytes", sums_bytes, "0644")

    tmp_output = output + ".tmp"
    try:
        with zipfile.ZipFile(tmp_output, "w", compression=zipfile.ZIP_DEFLATED) as zf:
            for arcname in sorted(arcnames.keys()):
                kind, payload, mode = arcnames[arcname]
                zi = zipfile.ZipInfo(arcname, date_time=FIXED_TIMESTAMP)
                zi.create_system = 3
                zi.external_attr = (int(mode, 8) | 0o100000) << 16
                zi.compress_type = zipfile.ZIP_DEFLATED
                if kind == "file":
                    with open(payload, "rb") as src_file:
                        zf.writestr(zi, src_file.read())
                else:
                    zf.writestr(zi, payload)
        os.replace(tmp_output, output)
    except Exception:
        if os.path.exists(tmp_output):
            os.remove(tmp_output)
        raise

    info("== 构建完成 ==")
    info("  输出: %s" % output)
    info("  条目: %d（含 game-lock.json / package-manifest.json / SHA256SUMS）" % len(arcnames))
    info("  版本: %s（框架归档 %s，游戏源 tag %s）"
         % (pkg["version"], pkg["framework_archive"], game["source_tag"]))
    info("  ZIP SHA256: %s" % sha256_file(output))
    return 0


def emit_template(args):
    input_root = os.path.abspath(args.input_root)
    notices_source = os.path.abspath(args.notices_source)
    script_dir = os.path.dirname(os.path.abspath(__file__))
    for label, path in (("输入根", input_root), ("notices", notices_source)):
        if not os.path.isdir(path):
            fail("--%s 不是目录: %s" % (label, path))
    mod_plugin_dir = None
    plugins_dir = os.path.join(input_root, "BepInEx", "plugins")
    if os.path.isdir(plugins_dir):
        candidates = [d for d in sorted(os.listdir(plugins_dir))
                      if os.path.isdir(os.path.join(plugins_dir, d))]
        if len(candidates) == 1:
            mod_plugin_dir = candidates[0]
    if mod_plugin_dir is None:
        mod_plugin_dir = "KingdomEnhancedMod"
        info("警告: 无法唯一确定 Mod 插件目录，模板使用默认值 %s（请操作员复核）" % mod_plugin_dir)

    warnings = []
    files = []

    def add_tree(root_dir, prefix, default_source=None):
        present = scan_tree(root_dir, prefix)
        for rel in sorted(present):
            src = os.path.join(root_dir, rel)
            files.append({"path": rel, "sha256": sha256_file(src),
                          "size": os.path.getsize(src),
                          "mode": "0755" if rel == "launcher.command" else "0644",
                          "source": default_source})

    add_tree(input_root, "BepInEx/core")
    add_tree(input_root, "dotnet")
    add_tree(input_root, "BepInEx/plugins/%s" % mod_plugin_dir)
    if os.path.isfile(os.path.join(input_root, "libdoorstop.dylib")):
        check_no_symlink(input_root, "libdoorstop.dylib")
        files.append({"path": "libdoorstop.dylib",
                      "sha256": sha256_file(os.path.join(input_root, "libdoorstop.dylib")),
                      "size": os.path.getsize(os.path.join(input_root, "libdoorstop.dylib")),
                      "mode": "0644", "source": "input-root"})
    else:
        fail("输入根缺少 libdoorstop.dylib")
    add_tree(script_dir, "defaults", default_source="package-material")
    for rel in ("launcher.command", "README.md", "REBUILD.md", "VALIDATION.md"):
        path = os.path.join(script_dir, rel)
        if rel in ("launcher.command", "README.md") or os.path.isfile(path):
            files.append({"path": rel, "sha256": sha256_file(path),
                          "size": os.path.getsize(path),
                          "mode": "0755" if rel == "launcher.command" else "0644",
                          "source": "package-material"})
    for prefix in ("tools", "metadata-sanitization-receipts"):
        if os.path.isdir(os.path.join(script_dir, prefix)):
            add_tree(script_dir, prefix, default_source="package-material")
    notices_tp = os.path.join(notices_source, "third-party")
    if os.path.isdir(notices_tp):
        add_tree(notices_source, "third-party", default_source="notices")

    for entry in files:
        if entry["source"] is not None:
            continue
        entry["source"] = category_of(entry["path"], mod_plugin_dir) or "UNKNOWN"
    unknown = [e["path"] for e in files if e["source"] == "UNKNOWN"]
    if unknown:
        warnings.append("以下条目无法归类（请操作员检查）: %s" % ", ".join(sorted(unknown)))

    game_section = {
        "assembly_sha256": "",
        "executable_sha256": "",
        "metadata_sha256": "",
        "infoplist_sha256": "",
        "source_tag": "1088b9c",
        "game_build": "2.4.0 r23485",
        "unity_version": "6000.0.61f1",
    }
    if args.game_app:
        game_app = os.path.abspath(args.game_app)
        if os.path.isdir(game_app):
            targets = (
                (GAME_ASSEMBLY_REL, "assembly_sha256"),
                (GAME_EXE_REL, "executable_sha256"),
                (GAME_METADATA_REL, "metadata_sha256"),
                (GAME_META_REL, "infoplist_sha256"),
            )
            if all(os.path.isfile(os.path.join(game_app, r)) for r, _ in targets):
                for rel, key in targets:
                    game_section[key] = sha256_file(os.path.join(game_app, rel))
                if game_section["assembly_sha256"] != BUILTIN_ASSEMBLY_SHA256:
                    warnings.append(
                        "实际 GameAssembly 哈希与契约固定值不符（当前输入非正式游戏副本？）: %s"
                        % game_section["assembly_sha256"])
                if not macho_has_arm64(os.path.join(game_app, GAME_EXE_REL)):
                    warnings.append("游戏可执行文件不含 arm64 切片: %s"
                                    % os.path.join(game_app, GAME_EXE_REL))
            else:
                warnings.append("--game-app 结构不完整，游戏哈希未填")
        else:
            warnings.append("--game-app 不是目录，游戏哈希未填")
    else:
        warnings.append("未提供 --game-app，游戏哈希未填")

    mod_dll_rel = "BepInEx/plugins/%s/%s.dll" % (mod_plugin_dir, mod_plugin_dir)
    mod_dll_entry = next((e for e in files if e["path"] == mod_dll_rel), None)
    files = sorted(files, key=lambda e: e["path"])
    template = {
        "schema": SCHEMA,
        "package": {
            "folder": PACKAGE_FOLDER,
            "version": "",
            "mod_plugin_dir": mod_plugin_dir,
            "framework_archive": "9027335",
            "mod_dll_sha256": mod_dll_entry["sha256"] if mod_dll_entry else "",
            "arch": "arm64",
        },
        "game": game_section,
        "files": files,
    }
    if warnings:
        template["warnings"] = warnings
    out = os.path.abspath(args.emit_lock_template)
    if os.path.exists(out):
        fail("模板输出已存在，拒绝覆盖: %s" % out)
    with open(out, "w", encoding="utf-8") as f:
        json.dump(template, f, ensure_ascii=False, sort_keys=True, indent=2)
        f.write("\n")
    info("已生成候选 input-lock 模板（请操作员复核/填写 version 后固化）: %s" % out)
    info("  条目: %d 个文件；警告: %d 条" % (len(files), len(warnings)))
    return 0


def main():
    parser = argparse.ArgumentParser(
        description="OhMyMods Mac ARM64 发行包构建器（确定性 ZIP，仅标准库）")
    parser.add_argument("--input-root", required=True,
                        help="实验布局式输入根（含 BepInEx/ dotnet/ libdoorstop.dylib）")
    parser.add_argument("--game-app",
                        help="KingdomTwoCrowns.app 路径（游戏四指纹校验 / 模板哈希计算）")
    parser.add_argument("--notices-source", required=True,
                        help="第三方许可与源码材料目录（内含 third-party/）")
    parser.add_argument("--lock", help="操作员固化的 input-lock.json")
    parser.add_argument("--output", help="输出 ZIP 路径（默认拒绝覆盖已存在文件）")
    parser.add_argument("--force", action="store_true", help="允许覆盖已存在的输出")
    parser.add_argument("--emit-lock-template",
                        help="只生成候选 input-lock 模板（不构建包）")
    args = parser.parse_args()

    if args.emit_lock_template:
        sys.exit(emit_template(args))

    for flag, attr in (("--lock", "lock"), ("--output", "output"), ("--game-app", "game_app")):
        if not getattr(args, attr):
            parser.error("构建模式需要 %s" % flag)
    sys.exit(build(args))


if __name__ == "__main__":
    main()
