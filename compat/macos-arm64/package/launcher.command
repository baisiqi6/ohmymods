#!/bin/bash
# OhMyMods Mac ARM64 启动器（Kingdom Two Crowns 2.4.0 r23485 / Unity 6000.0.61f1 / BepInEx 6 IL2CPP）
#
# 设计边界（不可放宽）：
#   - 只读游戏与系统；唯一允许的包外副作用是在 .app 所在目录创建相对符号链接
#     KingdomTwoCrowns_Data -> KingdomTwoCrowns.app/Contents/Resources/Data（BepInEx
#     GameDataPath 探测需要，见 README「数据别名」一节）。既有别名只核对正确目标，
#     从不删除或替换未知内容。
#   - 不修改 .app、不 sudo/chmod/chown、不清理缓存。
#   - 下载隔离（com.apple.quarantine）：普通启动与 --check-only 只做只读检测，任一原生库
#     带隔离属性即拒绝（macOS 会拒绝 dyld 加载被隔离的动态库）。清除只走用户显式发起的
#     `--trust-package`：全量只读门 → 精确 TRUST 确认 → 仅对固定名单内的本包原生库逐文件
#     `xattr -d com.apple.quarantine`（不递归、不 sudo、不清其他扩展属性、不重签、不自动
#     启动游戏）。绝不静默清除、绝不清除游戏/配置/缓存/未知文件的属性。
#   - 每次启动、任何写入之前：校验包内 SHA256SUMS（不可变 payload）与游戏四处
#     指纹（GameAssembly / 可执行文件 / global-metadata.dat / Info.plist），对照
#     包内 game-lock.json；缺失或不符即拒绝，无绕过开关。
#   - 包内可写路径（BepInEx、config、cache、interop、unity-libs、日志、实际 cfg）
#     路径链上存在符号链接（含断链）或非目录占位即拒绝；可写子树内的符号链接
#     文件同样拒绝（防 cfg 外部指向被悄悄覆盖）。目录逐级创建，不用 mkdir -p。
#   - 既有 BepInEx.cfg 只做 loader 关键键兼容检查（IL2CPPInteropAssembliesPath /
#     GlobalMetadataPath / ScanMethodRefs）；不兼容则解释并拒绝，绝不改写用户配置。
#     配置只在缺失时播种，永不覆盖。
#   - 锁文件包内保存，持有至游戏退出；残留锁只报告人工恢复路径，绝不自动删除。
#     获锁后立即安装 EXIT/INT/TERM/HUP 清理：有子进程先转发终止并等待，再释放锁。
#   - 预加载日志经 BEPINEX_PRELOADER_LOG 指向包内（默认会写入 .app 的 MacOS 目录）。
#
# 用法：
#   双击本文件；或终端运行：
#     ./launcher.command [--game <KingdomTwoCrowns.app 或可执行文件路径>] [--check-only] [游戏参数...]
#   --check-only 只做只读预检并打印将要执行的命令，不启动游戏、不加锁、不写任何文件。
#
# Bash 3.2 兼容：不用 bash4+ 特性；变量与中文相邻处一律 ${var} 花括号。

set -u

PROGRAM="ohmymods-launcher"
LOCK_NAME=".launcher.lock"
GAME_LOCK_NAME="game-lock.json"
SUMS_NAME="SHA256SUMS"
PRELOADER_LOG_NAME="preloader.log"

GAME_APP_NAME="KingdomTwoCrowns.app"
GAME_EXE_REL="Contents/MacOS/KingdomTwoCrowns"
GAME_ASSEMBLY_REL="Contents/Frameworks/GameAssembly.dylib"
GAME_META_REL="Contents/Info.plist"
GAME_METADATA_REL="Contents/Resources/Data/il2cpp_data/Metadata/global-metadata.dat"
GAME_DATA_DIR_REL="Contents/Resources/Data"
GAME_FRAMEWORKS_REL="Contents/Frameworks"

GAME_DATA_ALIAS="KingdomTwoCrowns_Data"
GAME_DATA_ALIAS_TARGET="KingdomTwoCrowns.app/Contents/Resources/Data"

DOORSTOP_REL="libdoorstop.dylib"
CORE_DLL_REL="BepInEx/core/BepInEx.Unity.IL2CPP.dll"
CORECLR_REL="dotnet/libcoreclr.dylib"

QUARANTINE_ATTR="com.apple.quarantine"

# 需要被 dyld 加载的本包原生库固定名单（与 input-lock.json 的 *.dylib 集合一一对应，
# 测试套件有防漂移断言）。检测与信任只覆盖这些文件；游戏 .app、插件 DLL、配置、
# 缓存、运行时生成物与未知新增文件都不在范围内。
NATIVE_TRUST_RELS="libdoorstop.dylib
BepInEx/core/libdobby.dylib
dotnet/libSystem.Globalization.Native.dylib
dotnet/libSystem.IO.Compression.Native.dylib
dotnet/libSystem.Native.dylib
dotnet/libSystem.Net.Security.Native.dylib
dotnet/libSystem.Security.Cryptography.Native.Apple.dylib
dotnet/libSystem.Security.Cryptography.Native.OpenSsl.dylib
dotnet/libclrjit.dylib
dotnet/libcoreclr.dylib
dotnet/libdbgshim.dylib
dotnet/libhostpolicy.dylib
dotnet/libmscordaccore.dylib
dotnet/libmscordbi.dylib"

TRUST_PACKAGE=0
NATIVE_TOTAL=0
NATIVE_QUARANTINED=""

usage() {
    cat <<'EOF'
OhMyMods Mac ARM64 启动器

用法: launcher.command [--game <路径>] [--check-only] [--trust-package] [--help] [游戏参数...]

选项:
  --game <路径>     显式指定游戏：KingdomTwoCrowns.app 目录或其可执行文件。
                    不指定时，在启动器所在目录及其上级目录精确查找
                    KingdomTwoCrowns.app（两处都有则视为歧义并拒绝）。
  --check-only      只读预检：完成全部校验并打印将要执行的启动命令，
                    不启动游戏、不加锁、不写任何文件。原生库带下载隔离时
                    同样非零退出（只检测，不清除任何扩展属性）。
  --trust-package   一次性、显式、交互式地信任本包：清除本包固定名单内原生库的
                    com.apple.quarantine（下载隔离）属性。需在终端运行并输入
                    精确的 TRUST 确认；与 --check-only、游戏参数互斥（可配 --game）。
                    不递归、不 sudo、不清除其他扩展属性、不启动游戏。
  --help, -h        显示本帮助。

其余参数原样转发给游戏。以下保留参数会被拒绝（防止覆盖加载器
target/runtime/interop/config/程序集指向）：
  --doorstop 及其任意破折/下划线变体（含 =value 形态）
  --unhollowed-path（含 =value 形态）

示例:
  ./launcher.command
  ./launcher.command --game "/Volumes/游戏/KingdomTwoCrowns.app"
  ./launcher.command --check-only
EOF
}

die() { printf '错误: %s\n' "$*" >&2; exit 1; }
info() { printf '%s\n' "$*"; }

# ---------- 基础工具 ----------

resolve_physical() {
    # 将 $1 解析为物理路径（跟踪符号链接，规范化目录部分）；失败返回非 0。
    p="$1"
    i=0
    while [ -L "$p" ]; do
        i=$((i + 1))
        [ "$i" -le 40 ] || return 1
        t=$(readlink "$p") || return 1
        case "$t" in
            /*) p="$t" ;;
            *) p="$(dirname "$p")/$t" ;;
        esac
    done
    d=$(dirname "$p")
    b=$(basename "$p")
    cd "$d" 2>/dev/null || return 1
    printf '%s/%s\n' "$(pwd -P)" "$b"
}

sha256_of() {
    "$SHASUM_BIN" -a 256 "$1" 2>/dev/null | awk '{print $1}'
}

is_hex64() {
    # $1 必须是恰好 64 个字符的纯小写十六进制串。
    v="${1-}"
    [ "${#v}" -eq 64 ] || return 1
    [ -z "$(printf '%s' "$v" | tr -d '0-9a-f')" ]
}

macho_has_arm64() {
    # 依赖系统 file(1) 判定 Mach-O 且含 arm64 切片（universal 亦可）。
    out=$("$FILE_BIN" -b "$1" 2>/dev/null) || return 1
    case "$out" in
        *Mach-O*arm64*) return 0 ;;
        *) return 1 ;;
    esac
}

lock_json_get() {
    # 从扁平 JSON 提取 "key": "value"（生成器输出为规范单层格式）。
    sed -n 's/^[[:space:]]*"'"$1"'"[[:space:]]*:[[:space:]]*"\([^"]*\)"[[:space:]]*,\{0,1\}[[:space:]]*$/\1/p' \
        "$GAME_LOCK_PATH" 2>/dev/null | head -n 1
}

# ---------- 参数解析 ----------

GAME_ARG=""
CHECK_ONLY=0
GAME_ARGS=()

while [ $# -gt 0 ]; do
    case "$1" in
        --game)
            [ $# -ge 2 ] || die "--game 需要一个路径参数"
            [ -z "$GAME_ARG" ] || die "--game 只能指定一次"
            GAME_ARG="$2"
            shift 2
            ;;
        --check-only)
            CHECK_ONLY=1
            shift
            ;;
        --trust-package)
            [ "$TRUST_PACKAGE" -eq 0 ] || die "--trust-package 只能在同一命令行中指定一次。"
            TRUST_PACKAGE=1
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        --doorstop*)
            die "拒绝保留参数（不允许覆盖加载器 target/runtime/interop/config）: $1"
            ;;
        --unhollowed-path|--unhollowed-path=*)
            die "拒绝保留参数 --unhollowed-path（不允许改写 interop 程序集指向）。"
            ;;
        *)
            GAME_ARGS[${#GAME_ARGS[@]}]="$1"
            shift
            ;;
    esac
done

# --trust-package 与 --check-only、与转发的游戏参数互斥（允许 --game 用于定位目标）：
# 该模式只做信任一件事，绝不启动游戏，也不接受任何游戏参数。
if [ "$TRUST_PACKAGE" -eq 1 ]; then
    [ "$CHECK_ONLY" -eq 0 ] || die "--trust-package 与 --check-only 互斥，请只选其一。"
    [ "${#GAME_ARGS[@]}" -eq 0 ] || die "--trust-package 不接受游戏参数（本模式不启动游戏）: ${GAME_ARGS[*]}"
fi

# ---------- 环境、工具、包根 ----------

[ "$(uname -s)" = "Darwin" ] || die "本启动器仅支持 macOS。"
[ "$(uname -m)" = "arm64" ] || die "本启动器仅支持 Apple Silicon（arm64）。当前架构: $(uname -m)"

FILE_BIN=$(command -v file || true)
SHASUM_BIN=$(command -v shasum || true)
ARCH_BIN=$(command -v arch || true)
PS_BIN=$(command -v ps || true)
FIND_BIN=$(command -v find || true)
AWK_BIN=$(command -v awk || true)
XATTR_BIN=/usr/bin/xattr
STAT_BIN=$(command -v stat || true)
[ -n "$FILE_BIN" ]    || die "缺少系统工具 file，无法校验游戏架构。"
[ -n "$SHASUM_BIN" ]  || die "缺少系统工具 shasum，无法校验指纹。"
[ -n "$ARCH_BIN" ]    || die "缺少系统工具 arch，无法以 arm64 启动游戏。"
[ -n "$PS_BIN" ]      || die "缺少系统工具 ps，无法检查游戏是否已在运行。"
[ -n "$FIND_BIN" ]    || die "缺少系统工具 find，无法检查包内可写路径。"
[ -n "$AWK_BIN" ]     || die "缺少系统工具 awk，无法检查既有配置。"
[ -x "$XATTR_BIN" ]   || die "缺少系统工具 /usr/bin/xattr，无法检测/处理下载隔离属性。"
[ -n "$STAT_BIN" ]    || die "缺少系统工具 stat，无法检查原生库链接数。"

a="/$0"; a=${a%/*}; a=${a#/}; a=${a:-.}
PKG=$(cd "$a" 2>/dev/null && pwd -P) || die "无法定位启动器自身目录: $0"
[ -n "$PKG" ] || die "无法定位启动器自身目录: $0"

GAME_LOCK_PATH="$PKG/$GAME_LOCK_NAME"
LOCK_PATH="$PKG/$LOCK_NAME"
SUMS_PATH="$PKG/$SUMS_NAME"

info "== OhMyMods Mac ARM64 启动器 =="
info "包目录: ${PKG}"

# ---------- 包必要文件与 SHA256SUMS（每次启动、任何写入之前） ----------

for rel in "$DOORSTOP_REL" "$CORE_DLL_REL" "$CORECLR_REL" "$GAME_LOCK_NAME" "$SUMS_NAME"; do
    [ -f "$PKG/$rel" ] || die "包不完整，缺少: ${rel}。请重新下载完整包后再试。"
done
[ ! -L "$SUMS_PATH" ] || die "${SUMS_NAME} 是符号链接，拒绝。"
[ ! -L "$GAME_LOCK_PATH" ] || die "${GAME_LOCK_NAME} 是符号链接，拒绝。"

SUMS_OUT=$( ( cd "$PKG" && "$SHASUM_BIN" -a 256 --check "$SUMS_NAME" ) 2>&1 )
if [ $? -ne 0 ]; then
    die "包内文件校验失败（${SUMS_NAME}），包可能损坏或被混入旧文件:
${SUMS_OUT}
请重新下载完整包后再试。"
fi
info "包完整性校验通过（${SUMS_NAME}）。"

# ---------- game-lock.json（四处指纹） ----------

LOCK_ASSEMBLY=$(lock_json_get assembly_sha256)
LOCK_EXECUTABLE=$(lock_json_get executable_sha256)
LOCK_METADATA=$(lock_json_get metadata_sha256)
LOCK_INFOPLIST=$(lock_json_get infoplist_sha256)
is_hex64 "$LOCK_ASSEMBLY"  || die "game-lock.json 缺少有效的 assembly_sha256（64 位十六进制）。拒绝启动。"
is_hex64 "$LOCK_EXECUTABLE" || die "game-lock.json 缺少有效的 executable_sha256（64 位十六进制）。拒绝启动。"
is_hex64 "$LOCK_METADATA"  || die "game-lock.json 缺少有效的 metadata_sha256（64 位十六进制）。拒绝启动。"
is_hex64 "$LOCK_INFOPLIST" || die "game-lock.json 缺少有效的 infoplist_sha256（64 位十六进制）。拒绝启动。"
LOCK_GAME_BUILD=$(lock_json_get game_build)
LOCK_UNITY=$(lock_json_get unity_version)
info "锁定目标: 游戏 ${LOCK_GAME_BUILD:-未知} / Unity ${LOCK_UNITY:-未知}"

# ---------- 游戏定位 ----------

discover_game() {
    local cand_self cand_parent n
    if [ -n "$GAME_ARG" ]; then
        if [ -d "$GAME_ARG" ]; then
            [ "$(basename "$GAME_ARG")" = "$GAME_APP_NAME" ] \
                || die "--game 指定的目录名必须精确为 ${GAME_APP_NAME}（不猜测其他构建）: $GAME_ARG"
            GAME_APP=$(resolve_physical "$GAME_ARG") \
                || die "无法解析 --game 路径: $GAME_ARG"
        elif [ -f "$GAME_ARG" ]; then
            GAME_APP=$(dirname "$(dirname "$(dirname "$GAME_ARG")")")
            [ "$(basename "$GAME_APP")" = "$GAME_APP_NAME" ] \
                || die "--game 指定的可执行文件不在 ${GAME_APP_NAME} 结构内: $GAME_ARG"
            GAME_APP=$(resolve_physical "$GAME_APP") \
                || die "无法解析 --game 路径: $GAME_ARG"
        else
            die "--game 路径不存在: $GAME_ARG"
        fi
        return 0
    fi
    cand_self="$PKG/$GAME_APP_NAME"
    cand_parent="$(dirname "$PKG")/$GAME_APP_NAME"
    n=0
    [ -d "$cand_self" ] && n=$((n + 1))
    [ -d "$cand_parent" ] && n=$((n + 1))
    if [ "$n" -eq 0 ]; then
        die "未找到游戏。已查找:
  ${cand_self}
  ${cand_parent}
请将本包放在 ${GAME_APP_NAME} 旁边（或把游戏放入包内），或用 --game 明确指定。"
    fi
    if [ "$n" -gt 1 ]; then
        die "包内与包上级目录同时存在 ${GAME_APP_NAME}，无法确定目标。请用 --game 明确其一。"
    fi
    if [ -d "$cand_self" ]; then
        GAME_APP=$(resolve_physical "$cand_self") || die "无法解析游戏路径: ${cand_self}"
    else
        GAME_APP=$(resolve_physical "$cand_parent") || die "无法解析游戏路径: ${cand_parent}"
    fi
}

discover_game

GAME_EXE="$GAME_APP/$GAME_EXE_REL"
GAME_ASSEMBLY_PATH="$GAME_APP/$GAME_ASSEMBLY_REL"
GAME_META_PATH="$GAME_APP/$GAME_META_REL"
GAME_METADATA_PATH="$GAME_APP/$GAME_METADATA_REL"
GAME_DATA_PHYS="$GAME_APP/$GAME_DATA_DIR_REL"
GAME_FRAMEWORKS="$GAME_APP/$GAME_FRAMEWORKS_REL"
GAME_ROOT=$(dirname "$GAME_APP")

info "游戏位置: ${GAME_APP}"

for f in "$GAME_EXE" "$GAME_ASSEMBLY_PATH" "$GAME_META_PATH" "$GAME_METADATA_PATH"; do
    [ -f "$f" ] || die "游戏结构不完整，缺少: ${f}"
done
[ -d "$GAME_DATA_PHYS" ] || die "游戏结构不完整，缺少目录: ${GAME_DATA_PHYS}"

# ---------- 架构校验 ----------

if ! macho_has_arm64 "$GAME_EXE"; then
    die "游戏可执行文件不是 ARM64 Mach-O（或不包含 arm64 切片）:
  ${GAME_EXE}
  识别结果: $("$FILE_BIN" -b "$GAME_EXE" 2>/dev/null || echo 无法识别)
本包仅支持 Apple Silicon 原生 ARM64 的 ${GAME_APP_NAME}。"
fi

# ---------- 游戏指纹校验（四文件） ----------

verify_print() { # $1=描述 $2=期望 $3=实际
    printf '指纹不符: %s\n  期望: %s\n  实际: %s\n' "$1" "$2" "$3" >&2
}

ACTUAL_ASSEMBLY=$(sha256_of "$GAME_ASSEMBLY_PATH")
[ -n "$ACTUAL_ASSEMBLY" ] || die "无法读取计算 GameAssembly 指纹: ${GAME_ASSEMBLY_PATH}"
if [ "$ACTUAL_ASSEMBLY" != "$LOCK_ASSEMBLY" ]; then
    verify_print "GameAssembly.dylib（${GAME_ASSEMBLY_PATH}）" "$LOCK_ASSEMBLY" "$ACTUAL_ASSEMBLY"
    die "游戏版本与包锁定的版本不一致。请使用 Kingdom Two Crowns ${LOCK_GAME_BUILD:-指定版本} 的 ARM64 构建。"
fi

ACTUAL_EXE=$(sha256_of "$GAME_EXE")
[ -n "$ACTUAL_EXE" ] || die "无法读取计算可执行文件指纹: ${GAME_EXE}"
if [ "$ACTUAL_EXE" != "$LOCK_EXECUTABLE" ]; then
    verify_print "可执行文件（${GAME_EXE}）" "$LOCK_EXECUTABLE" "$ACTUAL_EXE"
    die "游戏可执行文件与包锁定版本不一致，拒绝启动。"
fi

ACTUAL_METADATA=$(sha256_of "$GAME_METADATA_PATH")
[ -n "$ACTUAL_METADATA" ] || die "无法读取计算 global-metadata.dat 指纹: ${GAME_METADATA_PATH}"
if [ "$ACTUAL_METADATA" != "$LOCK_METADATA" ]; then
    verify_print "global-metadata.dat（${GAME_METADATA_PATH}）" "$LOCK_METADATA" "$ACTUAL_METADATA"
    die "游戏 IL2CPP 元数据与包锁定版本不一致，拒绝启动。"
fi

ACTUAL_INFOPLIST=$(sha256_of "$GAME_META_PATH")
[ -n "$ACTUAL_INFOPLIST" ] || die "无法读取计算 Info.plist 指纹: ${GAME_META_PATH}"
if [ "$ACTUAL_INFOPLIST" != "$LOCK_INFOPLIST" ]; then
    verify_print "Info.plist（${GAME_META_PATH}）" "$LOCK_INFOPLIST" "$ACTUAL_INFOPLIST"
    die "游戏元数据与包锁定版本不一致，拒绝启动。"
fi

info "游戏指纹校验通过（四文件）。"

# ---------- 原生库下载隔离属性（只读检测；先于任何 mkdir/配置/别名写入） ----------
# 下载器会给包内文件打 com.apple.quarantine；macOS 的 dyld 会拒绝加载被隔离的动态库
# （library load disallowed by system policy）。这里只做只读列举；读取失败按 fail-closed
# 处理（绝不能把读取失败当成“没有属性”）。

native_attr_list() { # $1=绝对路径；stdout=属性名列表（空=无属性）；失败=非 0（诊断进 stderr）
    # 注意：本函数总是在命令替换里调用，不能用 die（die 只会退出子 shell）；
    # 读取失败必须由调用者 fail-closed 处理（绝不能把读取失败当成“没有属性”）。
    out=$("$XATTR_BIN" "$1" 2>&1) || {
        printf '%s\n' "$out" >&2
        return 1
    }
    printf '%s' "$out"
}

native_read_fail() { # $1=路径；打印 fail-closed 原因（由调用者在 die 消息里使用）
    printf '无法读取扩展属性（xattr 列举失败）: %s\n按 fail-closed 处理：读取失败不视为“无隔离属性”。请修复权限/文件系统问题后重试。\n' "$1"
}

attr_present() { # $1=属性列表文本 $2=属性名；精确整行匹配（不做模糊/包含判断）
    printf '%s\n' "$1" | "$AWK_BIN" -v want="$2" '$0 == want { found = 1 } END { exit(found ? 0 : 1) }'
}

verify_native_parents() { # $1=相对路径；父目录组件必须是真实目录（不许符号链接）
    cur="$PKG"
    rest=$(dirname "$1")
    while [ -n "$rest" ] && [ "$rest" != "." ]; do
        comp=${rest%%/*}
        if [ "$comp" = "$rest" ]; then rest=""; else rest=${rest#*/}; fi
        [ -n "$comp" ] || continue
        cur="$cur/$comp"
        [ ! -L "$cur" ] || die "原生库父路径是符号链接，拒绝: ${cur}
只信任物理包内的真实文件。请重新解压发布包。"
        [ -d "$cur" ] || die "原生库父路径不是真实目录，拒绝: ${cur}"
    done
}

verify_native_rel() { # $1=相对路径；存在/类型/父路径 fail-closed 检查
    [ ! -L "$PKG/$1" ] || die "原生库是符号链接，拒绝: ${PKG}/$1
只信任物理包内的真实文件。请重新解压发布包。"
    [ -f "$PKG/$1" ] || die "包内缺少原生库: $1（包不完整或已被改动）。请重新下载完整发布包。"
    verify_native_parents "$1"
}

check_native_targets() { # 只读：固定名单逐个检查并收集带隔离属性的文件
    NATIVE_QUARANTINED=""
    NATIVE_TOTAL=0
    for rel in $NATIVE_TRUST_RELS; do
        NATIVE_TOTAL=$((NATIVE_TOTAL + 1))
        verify_native_rel "$rel"
        list=$(native_attr_list "$PKG/$rel") || die "$(native_read_fail "$PKG/$rel")"
        if attr_present "$list" "$QUARANTINE_ATTR"; then
            NATIVE_QUARANTINED="${NATIVE_QUARANTINED}${NATIVE_QUARANTINED:+
}${rel}"
        fi
    done
}

format_rel_list() { # $1=换行分隔的相对路径；输出缩进的绝对路径列表
    while IFS= read -r rel; do
        [ -n "$rel" ] || continue
        printf '  %s\n' "$PKG/$rel"
    done <<EOF
$1
EOF
}

shell_quote() { # $1=字符串；优先单引号直引（空格/中文原样可读），仅在含单引号时退回 %q
    # bash 3.2 的 printf %q 会把非 ASCII 字节转成八进制转义（输出不再是可读文本），
    # 因此对路径优先用单引号；单引号内的任何字符都是字面量，语义等价且可复制。
    case "$1" in
        *"'"*) printf '%q' "$1" ;;
        *) printf "'%s'" "$1" ;;
    esac
}

trust_command_line() { # 供用户复制的一次性命令（含空格/中文路径均已正确引用）
    printf '%s --trust-package\n' "$(shell_quote "$PKG/launcher.command")"
}

sums_hash_for() { # $1=包内相对路径；stdout=已通过校验的 SHA256SUMS 记录的哈希
    "$AWK_BIN" -v rel="$1" '$2 == rel { h = $1 } END { if (h == "") exit 1; print h }' "$SUMS_PATH"
}

assert_native_links() { # $1=相对路径；nlink 必须为 1（xattr 属 inode，硬链接=越界改动包外）
    links=$("$STAT_BIN" -f %l "$PKG/$1" 2>/dev/null) \
        || die "无法读取链接数（stat）: ${PKG}/$1"
    case "$links" in
        ''|*[!0-9]*) die "链接数输出异常（stat）: ${PKG}/$1 → ${links}" ;;
    esac
    if [ "$links" -ne 1 ]; then
        die "原生库有多个硬链接（nlink=${links}），拒绝处理: ${PKG}/$1
扩展属性属于 inode：包内文件被硬链接到包外时，清除属性会越界改动包外内容。
请从发布 ZIP 重新解压出干净副本后重试。"
    fi
}

verify_trust_layout() { # 只读：名单内全部文件存在/非符号链接/父路径真实/nlink==1
    for rel in $NATIVE_TRUST_RELS; do
        verify_native_rel "$rel"
        assert_native_links "$rel"
    done
}

verify_trust_hashes() { # 只读：确认后复核名单内每个文件哈希仍与 SHA256SUMS 一致
    for rel in $NATIVE_TRUST_RELS; do
        expected=$(sums_hash_for "$rel") \
            || die "信任目标不在 ${SUMS_NAME} 清单中，拒绝: ${rel}（请不要使用被改动的包）。"
        actual=$(sha256_of "$PKG/$rel")
        if [ "$actual" != "$expected" ]; then
            printf '信任复核失败: %s\n  期望: %s\n  实际: %s\n' \
                "$PKG/$rel" "$expected" "${actual:-<无法读取>}" >&2
            die "确认后复核发现原生库内容已变化，已中止（未清除任何属性）。请重新解压发布包。"
        fi
    done
}

trust_package_flow() { # 显式一次性信任本包原生库；绝不启动游戏、绝不处理名单外文件
    info ""
    info "== 信任本包原生库（一次性、显式操作） =="
    if [ -z "$NATIVE_QUARANTINED" ]; then
        info "固定名单内 ${NATIVE_TOTAL} 个原生库均无 ${QUARANTINE_ATTR} 属性：无可处理文件，未做任何修改。"
        return 0
    fi
    verify_trust_layout
    info "包目录: ${PKG}"
    info "以下原生库带有下载隔离属性（${QUARANTINE_ATTR}）:"
    format_rel_list "$NATIVE_QUARANTINED"
    info ""
    info "本操作只做一件事：移除上面这些本包原生库的 ${QUARANTINE_ATTR} 属性。"
    info "不会做：递归处理目录、清除其他扩展属性（如 com.apple.provenance）、重签二进制、"
    info "        使用 sudo、修改系统安全设置、触碰游戏 .app/配置/缓存、自动启动游戏。"
    info "风险说明：包内 SHA256 只能核对包内容未被混入旧文件，不能证明来源可信；"
    info "          只有确认本包来自本项目 GitHub Releases（或你本机本人的审核构建）时才继续。"
    info ""
    printf '请输入大写 TRUST 后回车确认（其他输入/直接回车/EOF 均取消）: '
    if ! IFS= read -r answer; then
        info ""
        info "已取消：未做任何修改。"
        return 1
    fi
    if [ "$answer" != "TRUST" ]; then
        info "已取消：未做任何修改。"
        return 1
    fi
    # 确认后复核：占锁 → 复检无游戏 → 复核路径/链接数（再）+哈希 → 逐文件清除并读回
    acquire_lock
    trap cleanup EXIT INT TERM HUP
    if scan_running_game; then
        release_lock
        die "检测到 Kingdom Two Crowns 已在运行（信任流程加锁后复检），请先完全退出游戏:
${SCAN_HITS}"
    fi
    verify_trust_layout
    verify_trust_hashes
    TRUST_CLEANED=0
    TRUST_SKIPPED=0
    TRUST_FAILED=""
    for rel in $NATIVE_TRUST_RELS; do
        if ! list=$(native_attr_list "$PKG/$rel"); then
            info "读取失败，未处理且状态未确认: ${rel}"
            TRUST_FAILED="${TRUST_FAILED}${TRUST_FAILED:+
}${rel}"
            continue
        fi
        if ! attr_present "$list" "$QUARANTINE_ATTR"; then
            TRUST_SKIPPED=$((TRUST_SKIPPED + 1))
            continue
        fi
        assert_native_links "$rel"   # 变更点前再核一次：确认后到此处之间不得被换成硬链接
        del_out=$("$XATTR_BIN" -d "$QUARANTINE_ATTR" "$PKG/$rel" 2>&1)
        del_st=$?
        if [ "$del_st" -ne 0 ]; then
            info "清除失败（xattr -d 退出码 ${del_st}）: ${rel}${del_out:+
${del_out}}"
            TRUST_FAILED="${TRUST_FAILED}${TRUST_FAILED:+
}${rel}"
            continue
        fi
        info "删除命令返回成功: ${rel}"
        if ! list2=$(native_attr_list "$PKG/$rel"); then
            info "删除后读取失败，是否清除尚未确认: ${rel}"
            TRUST_FAILED="${TRUST_FAILED}${TRUST_FAILED:+
}${rel}"
            continue
        fi
        if attr_present "$list2" "$QUARANTINE_ATTR"; then
            info "清除后读回仍带隔离属性（未生效）: ${rel}"
            TRUST_FAILED="${TRUST_FAILED}${TRUST_FAILED:+
}${rel}"
            continue
        fi
        TRUST_CLEANED=$((TRUST_CLEANED + 1))
        info "已清除: ${rel}"
    done
    info ""
    info "已清除隔离属性: ${TRUST_CLEANED} 个；本就无隔离属性被跳过: ${TRUST_SKIPPED} 个。"
    if [ -n "$TRUST_FAILED" ]; then
        info "以下文件未能确认清除（可能仍带隔离属性，或读取失败、状态未知）:"
        format_rel_list "$TRUST_FAILED"
        info "可以重新运行同一条 --trust-package 命令重试：只会处理仍被隔离的文件，已清者跳过。"
        info "若反复失败，请把 \`xattr <上述文件>\` 的完整输出反馈给维护者；"
        info "不要使用 xattr -cr/-dr 递归清除整包，也不要关闭 Gatekeeper。"
        return 1
    fi
    info "全部完成：本包固定名单内 ${NATIVE_TOTAL} 个原生库已无 ${QUARANTINE_ATTR} 属性。"
    info "未改动任何其他扩展属性，未重签二进制、未 sudo、未修改系统安全设置。"
    info "若启动仍被系统拒绝（library load disallowed by system policy），请把 \`xattr\` 的完整输出"
    info "反馈给维护者，以便排查其他系统策略；本启动器不会继续清除其他属性。"
    info "请重新双击 launcher.command 启动游戏（本流程不会自动启动游戏）。"
    return 0
}

check_native_targets

if [ -n "$NATIVE_QUARANTINED" ] && [ "$TRUST_PACKAGE" -eq 0 ]; then
    die "检测到本包原生库带有系统下载隔离属性（${QUARANTINE_ATTR}）。
macOS 会拒绝加载被隔离的动态库（dyld: library load disallowed by system policy），本包无法启动。
已隔离文件（固定名单 ${NATIVE_TOTAL} 个原生库中的以下项）:
$(format_rel_list "$NATIVE_QUARANTINED")
本启动器不会自动清除隔离属性，也不会修改系统安全设置。
若确认本包来自本项目 GitHub Releases（或你本机本人的审核构建），请先完全退出游戏，
然后在「终端」中一次性运行:
  $(trust_command_line)
该命令只移除上面这些本包原生库的 ${QUARANTINE_ATTR} 属性（不递归、不 sudo、不改系统设置），
执行前会再次列出文件并要求输入 TRUST 确认。
注意: 包内 SHA256 只能核对包内容未被混入旧文件，不能证明来源可信。"
fi

# ---------- 可写性检查（含包内可写路径 fail-closed 布局核验） ----------

if [ ! -w "$PKG" ]; then
    die "包目录不可写: ${PKG}
请将完整包移动到当前用户可写的目录后重试（例如「应用程序」或个人文件夹）。"
fi

verify_dir_chain() { # $1=相对 PKG 的路径；已存在组件必须为真实目录，符号链接/断链/占位拒绝
    cur="$PKG"
    rest="$1"
    while [ -n "$rest" ]; do
        comp=${rest%%/*}
        if [ "$comp" = "$rest" ]; then rest=""; else rest=${rest#*/}; fi
        [ -n "$comp" ] || continue
        cur="$cur/$comp"
        if [ -L "$cur" ]; then
            die "包内可写路径 ${cur} 是符号链接（含断链），拒绝写入。"
        fi
        if [ -e "$cur" ]; then
            [ -d "$cur" ] || die "包内可写路径 ${cur} 已存在且不是目录，拒绝写入。"
        else
            return 0
        fi
    done
}

ensure_real_dir() { # $1=相对 PKG 的路径；逐级创建（不穿越既有符号链接）
    cur="$PKG"
    rest="$1"
    while [ -n "$rest" ]; do
        comp=${rest%%/*}
        if [ "$comp" = "$rest" ]; then rest=""; else rest=${rest#*/}; fi
        [ -n "$comp" ] || continue
        cur="$cur/$comp"
        if [ -L "$cur" ]; then
            die "包内路径 ${cur} 是符号链接，拒绝创建/写入。"
        fi
        if [ -e "$cur" ]; then
            [ -d "$cur" ] || die "包内路径 ${cur} 已存在且不是目录，拒绝写入。"
        else
            mkdir "$cur" || die "无法创建目录 ${cur}（权限不足或路径被占用）。"
        fi
    done
}

verify_writable_layout() {
    local rel d hits entry
    for rel in "BepInEx" "BepInEx/config" "BepInEx/cache" "BepInEx/interop" "BepInEx/unity-libs"; do
        verify_dir_chain "$rel"
    done
    # 已存在可写子树内部（含 cfg 等会被写回的文件）不得有符号链接，防外部指向被悄悄覆盖
    for rel in "BepInEx/config" "BepInEx/cache" "BepInEx/interop" "BepInEx/unity-libs"; do
        d="$PKG/$rel"
        [ -d "$d" ] || continue
        hits=$("$FIND_BIN" "$d" \( -type l -o -type s -o -type p \) -print 2>/dev/null)
        [ -z "$hits" ] || die "可写目录 ${rel} 内存在符号链接/非常规文件（防越界写）:
${hits}"
    done
    # BepInEx 根直属项（如 LogOutput.log）同样不得为符号链接
    if [ -d "$PKG/BepInEx" ]; then
        for entry in "$PKG/BepInEx"/*; do
            [ -e "$entry" ] || [ -L "$entry" ] || continue
            [ ! -L "$entry" ] || die "BepInEx 目录内 ${entry} 是符号链接，拒绝。"
        done
    fi
    # 预加载日志目标（包根 preloader.log）不得为符号链接
    [ ! -L "$PKG/$PRELOADER_LOG_NAME" ] || die "${PRELOADER_LOG_NAME} 是符号链接，拒绝。"
}

verify_writable_layout

# ---------- 既有 BepInEx.cfg 兼容检查（只读） ----------
# 语义对齐 BepInEx 实际解析：只看 [IL2CPP] section；同键后值覆盖前值；值两侧 trim；
# 键缺失与显式空值区分。关键键缺失时采用加载器真实默认：
#   IL2CPPInteropAssembliesPath={BepInEx}、GlobalMetadataPath={GameDataPath}/il2cpp_data/Metadata/global-metadata.dat、
#   UpdateInteropAssemblies=true、ScanMethodRefs=true（因此已有 cfg 必须显式 false）。
# 无 cfg 时播种本包默认（含 ScanMethodRefs=false）；有 cfg 时永不改写，仅在不兼容时明确拒绝。

cfg_section_value() { # $1=cfg 路径 $2=section $3=键；输出 "M"（缺失）或 "S:<有效末值>"
    "$AWK_BIN" -v want_sec="$2" -v want_key="$3" '
        {
            if ($0 ~ /^[[:space:]]*#/) next
            line = $0
            gsub(/^[[:space:]]+/, "", line)
            gsub(/[[:space:]]+$/, "", line)
            if (substr(line, 1, 1) == "[" && substr(line, length(line), 1) == "]") {
                # ConfigFile.Reload retains the exact name inside the brackets.
                sec = substr(line, 2, length(line) - 2)
                insec = (sec == want_sec)
                next
            }
            if (!insec) next
            eq = index($0, "=")
            if (eq == 0) next
            key = substr($0, 1, eq - 1)
            val = substr($0, eq + 1)
            gsub(/^[[:space:]]+/, "", key)
            gsub(/[[:space:]]+$/, "", key)
            gsub(/^[[:space:]]+/, "", val)
            gsub(/[[:space:]]+$/, "", val)
            if (key == want_key) { found = 1; value = val }
        }
        END { if (found) print "S:" value; else print "M" }
    ' "$1" 2>/dev/null
}

cfg_bool_norm() { # $1=原始值；输出小写 true/false；其他值返回非 0
    case "$1" in
        [Tt][Rr][Uu][Ee]) printf 'true' ;;
        [Ff][Aa][Ll][Ss][Ee]) printf 'false' ;;
        *) return 1 ;;
    esac
}

check_cfg_key() { # $1=cfg $2=键 $3=兼容值 $4=缺失是否兼容(yes/no) $5=布尔键(yes/no) $6=默认值说明
    local cfg="$1" res val norm norm_out
    res=$(cfg_section_value "$cfg" "IL2CPP" "$2")
    case "$res" in
        M)
            if [ "$4" != "yes" ]; then
                die "现有 BepInEx.cfg 缺少 [IL2CPP] 的 ${2}；BepInEx 对该键的默认值是 ${6}，
与本包要求（${3}）不兼容。为避免静默改写你的配置，请在其中显式设置
${2} = ${3} 后重试（Mod 功能配置不受影响）。"
            fi
            ;;
        S:*)
            val=${res#S:}
            if [ -z "$val" ]; then
                die "现有 BepInEx.cfg 的 [IL2CPP] ${2} 是显式空值，与本包要求（${3}）不兼容。
为避免静默改写你的配置，请手动设置 ${2} = ${3} 后重试。"
            fi
            norm="$val"
            if [ "$5" = "yes" ]; then
                if ! norm_out=$(cfg_bool_norm "$val"); then
                    die "现有 BepInEx.cfg 的 [IL2CPP] ${2}=${val} 不是可识别的布尔值（true/false），拒绝启动。"
                fi
                norm="$norm_out"
            fi
            if [ "$norm" != "$3" ]; then
                die "现有 BepInEx.cfg 的 [IL2CPP] ${2}=${val} 与本包要求的 ${3} 不兼容。
为避免静默改写你的配置，请手动修改该文件后重试（Mod 功能配置不受影响）。"
            fi
            ;;
    esac
}

check_cfg_compatibility() {
    local cfg
    cfg="$PKG/BepInEx/config/BepInEx.cfg"
    [ -f "$cfg" ] || return 0
    check_cfg_key "$cfg" "IL2CPPInteropAssembliesPath" "{BepInEx}" yes no "{BepInEx}"
    check_cfg_key "$cfg" "GlobalMetadataPath" "{GameDataPath}/il2cpp_data/Metadata/global-metadata.dat" yes no "{GameDataPath}/il2cpp_data/Metadata/global-metadata.dat"
    check_cfg_key "$cfg" "UpdateInteropAssemblies" "true" yes yes "true"
    check_cfg_key "$cfg" "ScanMethodRefs" "false" no yes "true"
}

check_cfg_compatibility

# ---------- 数据别名（唯一允许的包外副作用；只核对，不删除未知内容） ----------

alias_state() {
    # 输出: absent | ok | conflict:<原因>
    alias="$GAME_ROOT/$GAME_DATA_ALIAS"
    if [ ! -e "$alias" ] && [ ! -L "$alias" ]; then
        printf 'absent'
        return 0
    fi
    if [ ! -L "$alias" ]; then
        printf 'conflict:已存在同名路径且不是符号链接（不改动）'
        return 0
    fi
    resolved=$(resolve_physical "$alias" 2>/dev/null) || resolved=""
    if [ -z "$resolved" ] || [ ! -d "$resolved" ]; then
        printf 'conflict:符号链接悬空或无法解析（不改动）'
        return 0
    fi
    if [ "$resolved" = "$GAME_DATA_PHYS" ]; then
        printf 'ok'
    else
        printf 'conflict:符号链接指向别处 %s（不改动）' "$resolved"
    fi
}

ensure_data_alias() {
    st=$(alias_state)
    alias="$GAME_ROOT/$GAME_DATA_ALIAS"
    case "$st" in
        absent)
            info "数据别名不存在，将在游戏所在目录创建（BepInEx 定位游戏数据所需，详见 README）:
  ${alias} -> ${GAME_DATA_ALIAS_TARGET}"
            if ! ln -s "$GAME_DATA_ALIAS_TARGET" "$alias" 2>/dev/null; then
                die "无法创建数据别名: ${alias}
游戏所在目录可能不可写。请将游戏（或本包）放到当前用户可写的目录，
或用 --game 明确指定一份位于可写目录的游戏副本。"
            fi
            ;;
        ok)
            ;;
        conflict:*)
            die "数据别名位置被占用: ${st#conflict:}
  路径: ${alias}
为避免破坏未知内容，启动器不会替换它。请手动处理该路径后重试
（若确认无用可自行删除/移动，再重新运行启动器）。"
            ;;
    esac
}

ALIAS_STATE=$(alias_state)
case "$ALIAS_STATE" in
    absent) info "数据别名状态: 缺失（启动时将创建）" ;;
    ok)     info "数据别名状态: 正确（复用既有链接）" ;;
    conflict:*)
        die "数据别名位置被占用: ${ALIAS_STATE#conflict:}
  路径: ${GAME_ROOT}/${GAME_DATA_ALIAS}
为避免破坏未知内容，启动器不会替换它。请手动处理该路径后重试。"
        ;;
esac

# ---------- 运行中游戏检查（ps 失败即拒绝，fail-closed） ----------

scan_running_game() {
    SCAN_RAW=$("$PS_BIN" -axo comm= 2>/dev/null) \
        || die "无法枚举系统进程（ps 失败），为安全起见拒绝启动。"
    [ -n "$SCAN_RAW" ] || die "ps 返回空进程列表，为安全起见拒绝启动。"
    # 精确匹配 comm 基名等于 KingdomTwoCrowns（不依赖 bundle 名：改名 .app 或裸同名
    # 可执行文件同样命中；枚举失败已在上方 fail-closed 拒绝）。
    SCAN_HITS=""
    line=""
    while IFS= read -r line; do
        [ -n "$line" ] || continue
        case "$line" in
            KingdomTwoCrowns|*/KingdomTwoCrowns)
                SCAN_HITS="${SCAN_HITS:+${SCAN_HITS}
}${line}"
                ;;
        esac
    done <<EOF
$SCAN_RAW
EOF
    [ -n "$SCAN_HITS" ]
}

refuse_if_game_running() {
    if scan_running_game; then
        die "检测到 Kingdom Two Crowns 已在运行，请先完全退出游戏后再启动本包:
${SCAN_HITS}"
    fi
}

refuse_if_game_running

# ---------- 锁（获锁即装清理；持有至子进程退出） ----------

analyze_lock() {
    # 输出 none | live:<pid> | foreign:<pid> | stale
    if [ ! -e "$LOCK_PATH" ]; then
        printf 'none'
        return 0
    fi
    lock_pid=$(sed -n 's/^pid=//p' "$LOCK_PATH" 2>/dev/null | head -n 1)
    case "${lock_pid:-}" in ''|*[!0-9]*) printf 'stale'; return 0 ;; esac
    holder_comm=$("$PS_BIN" -p "$lock_pid" -o comm= 2>/dev/null | head -n 1)
    if [ -z "$holder_comm" ]; then
        printf 'stale'
    elif [ "$lock_pid" = "$$" ]; then
        printf 'live:%s' "$lock_pid"
    else
        printf 'foreign:%s:%s' "$lock_pid" "$holder_comm"
    fi
}

LOCK_STATE=$(analyze_lock)
case "$LOCK_STATE" in
    none) : ;;
    live:*)
        die "本包的另一个启动器实例正在运行（PID ${LOCK_STATE#live:}）。
请先等待其退出，或关闭对应的终端窗口后重试。"
        ;;
    foreign:*)
        LOCK_PID=${LOCK_STATE#foreign:}
        LOCK_PID=${LOCK_PID%%:*}
        die "锁文件被一个存活进程持有（可能是 PID 复用或残留）: ${LOCK_PATH}
  PID ${LOCK_PID}（进程: ${LOCK_STATE#foreign:}）
如确认游戏与启动器均已退出，请手动删除该锁文件后重试。启动器不会自动删除它。"
        ;;
    stale)
        die "发现残留锁文件（持有进程已不存在）: ${LOCK_PATH}
如确认游戏与启动器均已完全退出，请手动删除该锁文件后重试。启动器不会自动删除它。"
        ;;
esac

acquire_lock() {
    if ( set -C; printf 'pid=%s\nstarted=%s\nexe=%s\n' "$$" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$GAME_EXE" > "$LOCK_PATH" ) 2>/dev/null; then
        return 0
    fi
    die "无法创建锁文件（可能已有实例正在启动）: ${LOCK_PATH}"
}

release_lock() {
    # 仅当锁内容仍属于本进程时才删除，避免误删他人锁。
    [ -e "$LOCK_PATH" ] || return 0
    lock_pid=$(sed -n 's/^pid=//p' "$LOCK_PATH" 2>/dev/null | head -n 1)
    if [ "$lock_pid" = "$$" ]; then
        rm -f "$LOCK_PATH" 2>/dev/null || printf '警告: 无法删除锁文件: %s\n' "$LOCK_PATH" >&2
    fi
}

# 清理：可重入；有子进程先转发终止并等待结束（超时升级 KILL），再释放本进程持有的锁。
CHILD=""
cleanup() {
    rc=$?
    trap - EXIT INT TERM HUP
    if [ -n "$CHILD" ]; then
        kill -TERM "$CHILD" 2>/dev/null || true
        n=0
        while kill -0 "$CHILD" 2>/dev/null && [ "$n" -lt 300 ]; do
            n=$((n + 1))
            /bin/sleep 0.1
        done
        if kill -0 "$CHILD" 2>/dev/null; then
            kill -KILL "$CHILD" 2>/dev/null || true
        fi
        wait "$CHILD" 2>/dev/null
        CHILD=""
    fi
    release_lock
    exit "$rc"
}

# ---------- 首启提示 ----------

firstboot_hint() {
    if [ ! -d "$PKG/BepInEx/interop" ] || [ -z "$(ls -A "$PKG/BepInEx/interop" 2>/dev/null)" ]; then
        info "提示: 首次启动需要生成 IL2CPP 兼容程序集并联网下载 Unity 基础库
（来源 unity.bepinex.dev），可能耗时数分钟且需要网络。请耐心等待，勿中途强制退出。"
    fi
}

# ---------- 配置播种（仅缺失时；目录逐级受控创建） ----------

seed_configs() {
    [ -d "$PKG/defaults" ] || return 0
    for f in "$PKG/defaults"/*; do
        [ -f "$f" ] || continue
        b=$(basename "$f")
        t="$PKG/BepInEx/config/$b"
        if [ -e "$t" ]; then
            continue
        fi
        ensure_real_dir "BepInEx/config"
        cp "$f" "$t" || die "无法写入默认配置: ${t}"
        info "已生成默认配置: BepInEx/config/${b}（既有文件永不覆盖）"
    done
}

# ---------- 预检输出 / 启动 ----------

print_launch_plan() {
    info ""
    info "== 启动配置 =="
    info "  DOORSTOP_TARGET_ASSEMBLY          = ${PKG}/${CORE_DLL_REL}"
    info "  DOORSTOP_CLR_RUNTIME_CORECLR_PATH = ${PKG}/${CORECLR_REL}"
    info "  DOORSTOP_CLR_CORLIB_DIR           = ${PKG}/dotnet"
    info "  BEPINEX_GAME_ASSEMBLY_PATH        = ${GAME_ASSEMBLY_PATH}"
    info "  BEPINEX_PRELOADER_LOG             = ${PKG}/${PRELOADER_LOG_NAME}"
    info "  DYLD_INSERT_LIBRARIES             = ${PKG}/${DOORSTOP_REL}"
    info "  DYLD_LIBRARY_PATH（前置）         = ${GAME_FRAMEWORKS}:${PKG}:${PKG}/dotnet"
    info "  游戏可执行文件                     = ${GAME_EXE}"
    info "  实际将执行                         = ${ARCH_BIN} -arm64 -e DYLD_INSERT_LIBRARIES=... -e DYLD_LIBRARY_PATH=... ${GAME_EXE}"
}

if [ "$TRUST_PACKAGE" -eq 1 ]; then
    trust_package_flow
    exit $?
fi

if [ "$CHECK_ONLY" -eq 1 ]; then
    print_launch_plan
    info ""
    info "预检完成（--check-only 只读模式）：未启动游戏、未加锁、未写入任何文件。"
    exit 0
fi

firstboot_hint
acquire_lock
trap cleanup EXIT INT TERM HUP

# 锁定后、启动前再次检查运行中游戏（缩短竞态窗口）。
if scan_running_game; then
    release_lock
    die "检测到 Kingdom Two Crowns 已在运行（加锁后复检），请先完全退出游戏:
${SCAN_HITS}"
fi

seed_configs
ensure_data_alias

export DOORSTOP_ENABLED=1
export DOORSTOP_TARGET_ASSEMBLY="$PKG/$CORE_DLL_REL"
export DOORSTOP_CLR_RUNTIME_CORECLR_PATH="$PKG/$CORECLR_REL"
export DOORSTOP_CLR_CORLIB_DIR="$PKG/dotnet"
export BEPINEX_GAME_ASSEMBLY_PATH="$GAME_ASSEMBLY_PATH"
export BEPINEX_PRELOADER_LOG="$PKG/$PRELOADER_LOG_NAME"
export DYLD_LIBRARY_PATH="$GAME_FRAMEWORKS:$PKG:$PKG/dotnet${DYLD_LIBRARY_PATH:+:$DYLD_LIBRARY_PATH}"
export DYLD_INSERT_LIBRARIES="$PKG/$DOORSTOP_REL"
export ARCHPREFERENCE=arm64

printf '正在启动游戏……（游戏退出后本窗口提示将自动结束）\n'

# arch 会剥离 DYLD_* 环境变量，两项都必须经 -e 显式传回（实验室已验证的接法）。
"$ARCH_BIN" -arm64 \
    -e DYLD_INSERT_LIBRARIES="$DYLD_INSERT_LIBRARIES" \
    -e DYLD_LIBRARY_PATH="$DYLD_LIBRARY_PATH" \
    "$GAME_EXE" ${GAME_ARGS[@]+"${GAME_ARGS[@]}"} &
CHILD=$!

EXIT_STATUS=0
while :; do
    if wait "$CHILD"; then
        :
    else
        EXIT_STATUS=$?
    fi
    kill -0 "$CHILD" 2>/dev/null || break
done
CHILD=""

release_lock

if [ "$EXIT_STATUS" -ne 0 ]; then
    printf '游戏进程退出码: %s（详见 BepInEx/LogOutput.log 与游戏 Player.log）\n' "$EXIT_STATUS" >&2
fi
exit "$EXIT_STATUS"
