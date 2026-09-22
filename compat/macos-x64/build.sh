#!/usr/bin/env bash
# compat/macos-x64/build.sh
# 可重复重建归档中的 macOS x86_64 兼容层，共两类产物：
#   1) Dobby：核对 versions.json 固定的 commit 与 clean 状态后，导出源码快照到
#      artifacts/dobby-src，应用 patches/dobby-macos-x64.patch，构建 libdobby.dylib；
#   2) 三个 C# 工具（cpp2il-patcher / interop-patcher / mac-shim）：引用由参数传入的
#      BepInEx core 目录，中间产物与输出全部落在 artifacts/tools 下。
#
# 边界（不可放宽）：
#   - 只写 <脚本目录>/artifacts；不写游戏目录、不写 Dobby 源目录、不联网、不部署；
#   - 所有输入路径由参数显式传入，路径含空格安全（全程引号）；
#   - artifacts 目录若已存在但缺少本脚本写入的 build-info.json（即未知输入），拒绝覆盖。
#
# 退出码：0 成功；非 0 表示校验、构建或产物检查失败（失败时不会保留 completed 标记）。
set -euo pipefail

usage() {
  cat <<'USAGE'
用法：
  build.sh --dobby-src DIR --bepinex-core DIR --dotnet PATH --cmake PATH [--jobs N]

参数：
  --dobby-src DIR     Dobby 源码检出目录。必须是 git 工作区、HEAD 等于 versions.json
                      中的固定 commit，且工作区完全干净（含未跟踪文件）。脚本只读取该
                      目录，源码快照与补丁应用都在 artifacts 副本内完成。
  --bepinex-core DIR BepInEx core 目录（编译 C# 工具时的只读引用），需包含
                      Mono.Cecil.dll 与 Iced.dll。
  --dotnet PATH      dotnet 可执行文件路径（.NET SDK 8）。
  --cmake PATH       cmake 可执行文件路径（需支持 -S/-B/--build）。
  --jobs N           并行编译任务数，默认取 hw.ncpu。
  -h, --help         显示本帮助。

产物（均在 artifacts/ 下）：
  libdobby.dylib                已应用补丁、x86_64 的 Dobby 动态库
  dobby-src/                    固定 commit 源码快照 + 补丁后的构建副本（中间产物）
  dobby-build/                  CMake 构建目录（仅 target dobby）
  tools/<name>/bin|obj/         三个 C# 工具的构建输出与中间产物
  build-info.json               本次构建的输入锁定信息与产物哈希（也是覆盖保护标记）

示例：
  ./build.sh \
    --dobby-src /tmp/dobby-888d971 \
    --bepinex-core "/Applications/ohmymods/BepInEx/core" \
    --dotnet /path/to/sdk/dotnet \
    --cmake /path/to/build-tools/bin/cmake
USAGE
}

die() { printf '错误: %s\n' "$*" >&2; exit 1; }

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ARTIFACTS_DIR="$SCRIPT_DIR/artifacts"
VERSIONS_FILE="$SCRIPT_DIR/versions.json"
PATCH_FILE="$SCRIPT_DIR/patches/dobby-macos-x64.patch"
BUILD_INFO="$ARTIFACTS_DIR/build-info.json"

DOBBY_SRC=""
BEPINEX_CORE=""
DOTNET=""
CMAKE=""
JOBS=""

while [ $# -gt 0 ]; do
  case "$1" in
    --dobby-src)   [ $# -ge 2 ] || die "--dobby-src 缺少取值";   DOBBY_SRC="$2"; shift 2 ;;
    --bepinex-core) [ $# -ge 2 ] || die "--bepinex-core 缺少取值"; BEPINEX_CORE="$2"; shift 2 ;;
    --dotnet)      [ $# -ge 2 ] || die "--dotnet 缺少取值";      DOTNET="$2"; shift 2 ;;
    --cmake)       [ $# -ge 2 ] || die "--cmake 缺少取值";       CMAKE="$2"; shift 2 ;;
    --jobs)        [ $# -ge 2 ] || die "--jobs 缺少取值";        JOBS="$2"; shift 2 ;;
    -h|--help)     usage; exit 0 ;;
    *) die "未知参数: $1（用 --help 查看用法；未知输入一律拒绝）" ;;
  esac
done

[ -n "$DOBBY_SRC" ]    || die "缺少必填参数 --dobby-src（--help 查看用法）"
[ -n "$BEPINEX_CORE" ] || die "缺少必填参数 --bepinex-core（--help 查看用法）"
[ -n "$DOTNET" ]       || die "缺少必填参数 --dotnet（--help 查看用法）"
[ -n "$CMAKE" ]        || die "缺少必填参数 --cmake（--help 查看用法）"

if [ -z "$JOBS" ]; then
  JOBS="$(sysctl -n hw.ncpu 2>/dev/null || printf '4')"
fi
case "$JOBS" in ''|*[!0-9]*) die "--jobs 必须是正整数，收到: $JOBS" ;; esac
[ "$JOBS" -ge 1 ] || die "--jobs 必须 >= 1"

# ---- 输出边界：artifacts 固定为脚本同级目录，无法被参数改写，因此不可能写入游戏目录 ----
[ "$ARTIFACTS_DIR" = "$SCRIPT_DIR/artifacts" ] || die "内部错误: artifacts 路径异常"
[ ! -L "$ARTIFACTS_DIR" ] || die "拒绝操作符号链接: $ARTIFACTS_DIR"
if [ -e "$ARTIFACTS_DIR" ] && [ ! -d "$ARTIFACTS_DIR" ]; then
  die "artifacts 路径已存在但不是目录，拒绝覆盖: $ARTIFACTS_DIR"
fi

# ---- 工具与输入校验 ----
[ -f "$VERSIONS_FILE" ] || die "缺少 $VERSIONS_FILE"
[ -f "$PATCH_FILE" ]    || die "缺少 $PATCH_FILE"

PYTHON_BIN="$(command -v python3 || true)"
[ -n "$PYTHON_BIN" ] || die "需要 python3 解析 versions.json"

VERSIONS_TSV="$("$PYTHON_BIN" - "$VERSIONS_FILE" <<'PY'
import json, sys
doc = json.load(open(sys.argv[1], encoding="utf-8"))
print("\t".join([
    doc["dobby"]["commit"],
    doc["dobby"]["repository"],
    doc.get("bepinex", ""),
    doc.get("unity", ""),
    doc.get("game", ""),
    doc.get("official_mod", ""),
]))
PY
)"
IFS=$'\t' read -r PINNED_COMMIT PINNED_REPO BEPINEX_VER UNITY_VER GAME_VER OFFICIAL_MOD <<<"$VERSIONS_TSV"
[ -n "$PINNED_COMMIT" ] || die "versions.json 中 dobby.commit 为空"

# Dobby 源：git 工作区、HEAD 命中固定 commit、完全干净
[ -d "$DOBBY_SRC" ] || die "--dobby-src 不是目录: $DOBBY_SRC"
command -v git >/dev/null 2>&1 || die "需要 git 校验 Dobby 源"
git -C "$DOBBY_SRC" rev-parse --is-inside-work-tree >/dev/null 2>&1 \
  || die "--dobby-src 不是 git 工作区: $DOBBY_SRC"
DOBBY_SRC="$(cd "$DOBBY_SRC" && pwd -P)"
DOBBY_HEAD="$(git -C "$DOBBY_SRC" rev-parse HEAD)"
[ "$DOBBY_HEAD" = "$PINNED_COMMIT" ] || die "Dobby HEAD=$DOBBY_HEAD 与 versions.json 固定 commit=$PINNED_COMMIT 不一致"
DOBBY_TREE="$(git -C "$DOBBY_SRC" rev-parse 'HEAD^{tree}')"
DOBBY_DIRTY="$(git -C "$DOBBY_SRC" status --porcelain)"
[ -z "$DOBBY_DIRTY" ] || die "Dobby 源不干净，拒绝构建（先清理为干净检出）:
$DOBBY_DIRTY"
DOBBY_ORIGIN="$(git -C "$DOBBY_SRC" remote get-url origin 2>/dev/null || true)"
if [ -n "$DOBBY_ORIGIN" ] && [ "$DOBBY_ORIGIN" != "$PINNED_REPO" ]; then
  printf '提示: Dobby origin=%s 与 versions.json repository=%s 不同（仅提示，不阻断）\n' "$DOBBY_ORIGIN" "$PINNED_REPO" >&2
fi

# BepInEx core：只读引用
[ -d "$BEPINEX_CORE" ] || die "--bepinex-core 不是目录: $BEPINEX_CORE"
BEPINEX_CORE="$(cd "$BEPINEX_CORE" && pwd -P)"
for ref in Mono.Cecil.dll Iced.dll; do
  [ -f "$BEPINEX_CORE/$ref" ] || die "BepInEx core 缺少引用: $BEPINEX_CORE/$ref"
done

# dotnet / cmake
[ -f "$DOTNET" ] || die "--dotnet 不是文件: $DOTNET"
[ -x "$DOTNET" ] || die "--dotnet 不可执行: $DOTNET"
DOTNET="$(cd "$(dirname "$DOTNET")" && pwd -P)/$(basename "$DOTNET")"
[ -f "$CMAKE" ] || die "--cmake 不是文件: $CMAKE"
[ -x "$CMAKE" ] || die "--cmake 不可执行: $CMAKE"
CMAKE="$(cd "$(dirname "$CMAKE")" && pwd -P)/$(basename "$CMAKE")"

DOTNET_VERSION="$(DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 "$DOTNET" --version 2>/dev/null | head -n 1)"
[ -n "$DOTNET_VERSION" ] || die "dotnet --version 执行失败: $DOTNET"
CMAKE_VERSION="$("$CMAKE" --version 2>/dev/null | head -n 1)"
[ -n "$CMAKE_VERSION" ] || die "cmake --version 执行失败: $CMAKE"

# ---- 覆盖保护：artifacts 已存在但不是本脚本产物（缺 build-info.json）→ 拒绝 ----
if [ -d "$ARTIFACTS_DIR" ] && [ ! -f "$BUILD_INFO" ]; then
  if [ -n "$(ls -A "$ARTIFACTS_DIR" 2>/dev/null || true)" ]; then
    die "artifacts 目录非空且缺少本脚本标记 build-info.json（未知输入），拒绝覆盖: $ARTIFACTS_DIR"
  fi
fi

TIMESTAMP="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
PATCH_SHA="$(shasum -a 256 "$PATCH_FILE" | awk '{print $1}')"
TOOLS_SUMMARY=""

write_build_info() {
  "$PYTHON_BIN" - "$BUILD_INFO" \
    "status=$1" \
    "timestamp_utc=$TIMESTAMP" \
    "script=compat/macos-x64/build.sh" \
    "artifacts_dir=$ARTIFACTS_DIR" \
    "dobby_src=$DOBBY_SRC" \
    "dobby_repo=$PINNED_REPO" \
    "dobby_commit=$PINNED_COMMIT" \
    "dobby_tree=$DOBBY_TREE" \
    "dobby_source_clean=yes" \
    "patch_file=patches/dobby-macos-x64.patch" \
    "patch_sha256=$PATCH_SHA" \
    "libdobby_sha256=${LIB_SHA:-}" \
    "libdobby_archs=${LIB_ARCHS:-}" \
    "libdobby_build_version=${DOBBY_BUILD_VERSION:-}" \
    "cmake=$CMAKE_VERSION" \
    "dotnet=$DOTNET_VERSION" \
    "bepinex_core=$BEPINEX_CORE" \
    "pinned_bepinex=$BEPINEX_VER" \
    "pinned_unity=$UNITY_VER" \
    "pinned_game=$GAME_VER" \
    "official_mod=$OFFICIAL_MOD" \
    "jobs=$JOBS" \
    "tools=${TOOLS_SUMMARY:-}" \
    <<'PY'
import json, sys
out = sys.argv[1]
doc = {}
for item in sys.argv[2:]:
    key, _, value = item.partition("=")
    doc[key] = value
with open(out, "w", encoding="utf-8") as fh:
    json.dump(doc, fh, ensure_ascii=False, indent=2, sort_keys=True)
    fh.write("\n")
PY
}

# 标记本次运行为“本脚本所有”，失败重跑时允许替换自己此前留下的半成品
mkdir -p "$ARTIFACTS_DIR"
write_build_info running

printf '== 输入确认 ==\n'
printf '  Dobby 源      : %s @ %s (tree %s, clean)\n' "$DOBBY_SRC" "$PINNED_COMMIT" "$DOBBY_TREE"
printf '  Dobby 仓库    : %s\n' "$PINNED_REPO"
printf '  BepInEx core  : %s\n' "$BEPINEX_CORE"
printf '  dotnet        : %s (%s)\n' "$DOTNET" "$DOTNET_VERSION"
printf '  cmake         : %s (%s)\n' "$CMAKE" "$CMAKE_VERSION"
printf '  并行任务      : %s\n' "$JOBS"
printf '  固定版本      : game %s / unity %s / bepinex %s / official_mod %s\n' "$GAME_VER" "$UNITY_VER" "$BEPINEX_VER" "$OFFICIAL_MOD"

# ---- 1. 导出固定 commit 源码快照到 artifacts（不读取/不修改源目录工作区）----
for rel in dobby-src dobby-build tools verify libdobby.dylib; do
  [ ! -L "$ARTIFACTS_DIR/$rel" ] || die "拒绝操作符号链接: artifacts/$rel"
  rm -rf "$ARTIFACTS_DIR/$rel"
done
SRC_COPY="$ARTIFACTS_DIR/dobby-src"
mkdir -p "$SRC_COPY"
printf '== 导出源码快照 ==\n'
git -C "$DOBBY_SRC" archive --format=tar HEAD | tar -xf - -C "$SRC_COPY"
[ -f "$SRC_COPY/CMakeLists.txt" ] || die "Dobby 源码快照导出失败（缺 CMakeLists.txt）"

# ---- 2. 在快照上应用归档补丁 ----
printf '== 应用补丁 %s ==\n' "$(basename "$PATCH_FILE")"
if ! patch -p1 --batch --forward --directory "$SRC_COPY" -i "$PATCH_FILE"; then
  die "Dobby 补丁应用失败（上下文不匹配；补丁与固定 commit 不再对应？）"
fi
REJECTS="$(find "$SRC_COPY" -name '*.rej' -print -quit)"
[ -z "$REJECTS" ] || die "补丁产生 .rej: $REJECTS"

check_anchor() {
  grep -q "$2" "$SRC_COPY/$1" || die "补丁校验失败：$1 缺少预期内容（$2）"
}
check_anchor source/InstructionRelocation/x64/X64InstructionRelocation.cc "curr_orig_ip + insn.length + orig_offset"
check_anchor source/MemoryAllocator/NearMemoryArena.cc "borrow unreserved code caves"
check_anchor source/UserMode/ExecMemory/code-patch-tool-darwin.cc "sysctl.proc_translated"
check_anchor source/UserMode/UnifiedInterface/platform-posix.cc "VM_FLAGS_FIXED"

# ---- 3. 构建 libdobby.dylib（仅 target dobby；ObjcRuntimeHook 在新 SDK 下无法编译且本归档不需要）----
BUILD_DIR="$ARTIFACTS_DIR/dobby-build"
printf '== CMake 配置（x86_64 Release）==\n'
# CMAKE_SYSTEM_NAME/CMAKE_SYSTEM_PROCESSOR 必须在首次配置时显式固定：EarlyGlobals.cmake 在
# project() 之前读取它们，否则 Apple Silicon 主机上会识别为 arm64/Darwin 走 iOS 分支而报错。
"$CMAKE" -S "$SRC_COPY" -B "$BUILD_DIR" \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_OSX_ARCHITECTURES=x86_64 \
  -DCMAKE_SYSTEM_NAME=Darwin \
  -DCMAKE_SYSTEM_PROCESSOR=x86_64
printf '== 构建 target dobby ==\n'
"$CMAKE" --build "$BUILD_DIR" --target dobby --parallel "$JOBS"

DOBBY_LIB="$BUILD_DIR/libdobby.dylib"
[ -f "$DOBBY_LIB" ] || die "构建结束但找不到 $DOBBY_LIB"
LIB_ARCHS="$(/usr/bin/lipo -archs "$DOBBY_LIB")"
[ "$LIB_ARCHS" = "x86_64" ] || die "libdobby.dylib 架构应为 x86_64，实际: $LIB_ARCHS"
for sym in DobbyPrepare DobbyCommit DobbyDestroy DobbyHook DobbyBuildVersion; do
  if ! /usr/bin/nm -gU "$DOBBY_LIB" | grep -q " _${sym}\$"; then
    die "libdobby.dylib 缺少导出符号: $sym"
  fi
done
DOBBY_BUILD_VERSION="$(LC_ALL=C strings "$DOBBY_LIB" | grep -m1 '^Dobby-[0-9]\{8\}' || true)"
cp -p "$DOBBY_LIB" "$ARTIFACTS_DIR/libdobby.dylib"
LIB_SHA="$(shasum -a 256 "$ARTIFACTS_DIR/libdobby.dylib" | awk '{print $1}')"
printf '产物 artifacts/libdobby.dylib arch=%s sha256=%s\n' "$LIB_ARCHS" "$LIB_SHA"
if [ -n "$DOBBY_BUILD_VERSION" ]; then
  printf '  DobbyBuildVersion=%s（快照无 .git，版本串不含 commit 后缀；commit 以 build-info.json 为准）\n' "$DOBBY_BUILD_VERSION"
fi

# ---- 4. 构建三个 C# 工具（BepInExCoreDir 由参数传入；bin/obj 全部落在 artifacts）----
printf '== 构建 C# 工具 ==\n'
build_tool() {
  local proj_rel="$1" out_dll="$2" tfm="$3"
  local name out_root
  name="$(basename "$(dirname "$proj_rel")")"
  out_root="$ARTIFACTS_DIR/tools/$name"
  DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
  "$DOTNET" build "$SCRIPT_DIR/$proj_rel" -c Release --nologo \
    -p:BepInExCoreDir="$BEPINEX_CORE" \
    -p:BaseIntermediateOutputPath="$out_root/obj/" \
    -p:BaseOutputPath="$out_root/bin/"
  local dll="$out_root/bin/Release/$tfm/$out_dll"
  [ -f "$dll" ] || die "工具构建缺少输出: $dll"
  local sha
  sha="$(shasum -a 256 "$dll" | awk '{print $1}')"
  TOOLS_SUMMARY="${TOOLS_SUMMARY}${TOOLS_SUMMARY:+,}$name=$sha"
  printf '  %s -> %s sha256=%s\n' "$name" "${dll#"$ARTIFACTS_DIR"/}" "$sha"
}

build_tool tools/cpp2il-patcher/patcher.csproj patcher.dll net8.0
build_tool tools/interop-patcher/patch.csproj patch.dll net8.0
build_tool tools/mac-shim/MacShim.csproj OhMyMods.MacCompatibility.dll net6.0

# ---- 5. 收尾校验：源目录未被本次构建改写 ----
DOBBY_DIRTY_AFTER="$(git -C "$DOBBY_SRC" status --porcelain)"
[ -z "$DOBBY_DIRTY_AFTER" ] || die "构建过程改动了 Dobby 源目录，违反只读约束:
$DOBBY_DIRTY_AFTER"
[ "$(git -C "$DOBBY_SRC" rev-parse HEAD)" = "$PINNED_COMMIT" ] || die "Dobby 源 HEAD 在构建期间发生变化"

write_build_info completed
printf '== 完成 ==\n'
printf '  artifacts/libdobby.dylib        sha256=%s arch=%s\n' "$LIB_SHA" "$LIB_ARCHS"
printf '  artifacts/tools/*/bin/Release   %s\n' "$TOOLS_SUMMARY"
printf '  artifacts/build-info.json       输入锁定与产物哈希\n'
printf '下一步验证: ./verify.sh --dobby "%s"\n' "$ARTIFACTS_DIR/libdobby.dylib"
