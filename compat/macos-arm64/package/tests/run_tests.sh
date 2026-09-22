#!/bin/bash
# OhMyMods Mac ARM64 package 测试（合成夹具，不接触真实游戏/运行时/存档）。
#
# 运行: bash tests/run_tests.sh          （在 compat/macos-arm64/package 下）
#
# 机制：
#  - make_fixture.py 生成假游戏 + 包骨架（含被测 launcher.command 副本、game-lock
#    四指纹、SHA256SUMS）；每个夹具类测试把 CUR_LAUNCHER 指向自己夹具包的副本。
#  - 启动命令不设生产测试通道：需要观察“真实 arch 调用行”时，用 PATH 前置的
#    mock arch(1)（测试自有脚本）捕获 argv/env；长运行用例经 mock 休眠模拟。
#  - 构建器经 tests/builder_driver.py import 运行，契约常量 monkeypatch 为夹具
#    哈希；生产 build_package.py / launcher.command 无任何绕过通道（有静态断言）。
#
# 覆盖：SHA256SUMS 每启校验（缺失/篡改 payload/篡改 game-lock）、游戏发现（缺失/
# 歧义/两种布局/空格路径/--game 两形态）、四指纹（含 global-metadata/Info.plist）、
# 架构（x86/text）、保留参数（--doorstop*/--unhollowed-path*）、cfg 兼容检查、锁
# （存活/残留/正常释放/TERM 清理等待子进程）、可写路径 fail-closed（symlink/断链/
# 非目录占位/cfg 外指不覆盖）、别名（创建/复用/冲突/悬空且不破坏）、配置哨兵保全、
# .app 零改动、参数与双 -e DYLD 转发、BEPINEX_PRELOADER_LOG 包内、运行中游戏检测、
# 原生库下载隔离（固定名单防漂移、普通/只读模式拒绝且零写入、提示命令含空格可直接
# 执行、--trust-package 参数重复/互斥、取消/EOF 零写入、只清名单内被隔离项且严格
# argv、其他属性与字节保持、部分失败如实报告与可重入、列举失败 fail-closed、删除
# 无效时读回判失败、文件/父路径符号链接与硬链接拒绝、名单外文件不碰）、
# 构建器（确定性/内容/权限/SHA256SUMS/manifest/排除项/operator 材料/各类拒绝/契约
# 常量强制/模板）。
#
# 测试不等于实机启动验收（Operator 门）。Bash 3.2 兼容；${var} 花括号紧邻中文。

set -u -o pipefail

SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd)
PKG_DIR=$(dirname "$SCRIPT_DIR")
LAUNCHER="$PKG_DIR/launcher.command"
BUILDER="$PKG_DIR/build_package.py"
DRIVER="$SCRIPT_DIR/builder_driver.py"
FIXTURE="$SCRIPT_DIR/make_fixture.py"
PY=python3

PASS=0
FAIL=0
FAILED_NAMES=""
WORK_ROOT=""
FAKE_GAME_PID=""
CUR_LAUNCHER="$LAUNCHER"

cleanup() {
    if [ -n "$FAKE_GAME_PID" ]; then
        kill "$FAKE_GAME_PID" 2>/dev/null || true
    fi
    [ -n "$WORK_ROOT" ] && rm -rf "$WORK_ROOT"
}
trap cleanup EXIT

ok()  { PASS=$((PASS + 1)); printf 'PASS %s\n' "$1"; }
bad() { FAIL=$((FAIL + 1)); FAILED_NAMES="$FAILED_NAMES|$1"; printf 'FAIL %s\n' "$1" >&2; }

assert_fail() { # 名称 命令...（期望非零退出）
    local name="$1"; shift
    if "$@" >/dev/null 2>&1; then bad "$name"; else ok "$name"; fi
}
out_contains() { # 名称 输出 子串
    case "$2" in
        *"$3"*) ok "$1" ;;
        *) bad "$1（输出缺少: $3）" ;;
    esac
}
out_lacks() { # 名称 输出 子串
    case "$2" in
        *"$3"*) bad "$1（不应出现: $3）" ;;
        *) ok "$1" ;;
    esac
}

jget() { # stdin=JSON, $1=键
    "$PY" -c 'import json,sys
d=json.load(sys.stdin)
v=d.get(sys.argv[1],"")
print(v if isinstance(v,(str,int)) else json.dumps(v,ensure_ascii=False))' "$1"
}

lock_edit() { # $1=lock 文件 $2=作用于 d 的 python 片段
    "$PY" - "$1" "$2" <<'PYEOF'
import json, sys
path, expr = sys.argv[1], sys.argv[2]
with open(path, "r", encoding="utf-8") as f:
    d = json.load(f)
exec(expr)
with open(path, "w", encoding="utf-8") as f:
    json.dump(d, f, ensure_ascii=False, sort_keys=True, indent=2)
    f.write("\n")
PYEOF
}

new_fixture() { # $1=目录 $2...=make_fixture 额外参数；stdout=facts JSON
    local dir="$1"
    shift
    "$PY" "$FIXTURE" --root "$dir" "$@"
}

phys() { # 物理路径（启动器内部一律 pwd -P 解析，断言须用同一路径形态）
    cd "$1" && pwd -P
}

run_launcher() { # 使用 CUR_LAUNCHER；回显 "status\noutput"
    local st out
    if [ "$PS_MOCK_ENABLED" -eq 1 ]; then
        out=$(PATH="$(ps_mock_setup):$PATH" "$CUR_LAUNCHER" "$@" 2>&1)
    else
        out=$("$CUR_LAUNCHER" "$@" 2>&1)
    fi
    st=$?
    printf '%s\n%s\n' "$st" "$out"
}

run_launcher_input() { # $1=stdin 文本；其余=启动器参数；使用真实 xattr，回显 "status\noutput"
    local input="$1" st out
    shift
    out=$(printf '%s' "$input" | PATH="$(ps_mock_setup):$PATH" "$CUR_LAUNCHER" "$@" 2>&1)
    st=$?
    printf '%s\n%s\n' "$st" "$out"
}

# ---------------- 测试隔离：ps 替身（过滤宿主真实游戏进程） ----------------
# 「运行中游戏」门是 fail-closed：宿主机上只要有真实的 KingdomTwoCrowns 进程（例如
# 操作员的 lab 构建在跑），不测这扇门的用例都会在门处提前退出。因此这些用例注入 ps
# 替身：只在枚举全部进程（-axo comm=）时过滤同名进程，其它查询（-p … -o comm=）
# 原样转发真实 ps。该门本身由 test_running_game_scan 用真实进程覆盖（那里关掉替身）。

PS_MOCK_DIR=""
PS_MOCK_ENABLED=1

ps_mock_setup() { # 幂等；stdout=替身目录
    if [ -z "$PS_MOCK_DIR" ]; then
        PS_MOCK_DIR="$WORK_ROOT/ps-mock"
        mkdir -p "$PS_MOCK_DIR"
        cat > "$PS_MOCK_DIR/ps" <<EOF
#!/bin/sh
real_ps="$(command -v ps)"
case " \$* " in
    " -axo comm= "*) "\$real_ps" -axo comm= | grep -v 'KingdomTwoCrowns\$' || true ;;
    *) exec "\$real_ps" "\$@" ;;
esac
EOF
        chmod +x "$PS_MOCK_DIR/ps"
    fi
    printf '%s' "$PS_MOCK_DIR"
}

# ---------------- 测试替身：xattr(1) ----------------
# 严格记录每次调用的 argv；真实模拟列举/删除语义。受控失败与“删除返回 0 但未生效”
# 都可用环境变量注入——生产脚本不读取任何 OHMYMODS_* 变量（套件内有静态断言），
# 这些开关只存在于替身内部。

make_mock_xattr() { # $1=bin 目录 $2=调用日志 $3=状态目录（quarantined 列表）
    mkdir -p "$1" "$3"
    : > "$3/quarantined"
    cat > "$1/xattr" <<'MOCKEOF'
#!/bin/sh
# xattr <file>            列举属性名（每行一个；无属性=空输出、退出 0）
# xattr -d <attr> <file>  删除属性（不存在则退出 1）
# 注入（子串匹配文件路径；"all"=全部）：OHMYMODS_MOCK_XATTR_FAIL_LIST /
# OHMYMODS_MOCK_XATTR_FAIL_DELETE；OHMYMODS_MOCK_XATTR_NOOP_DELETE=删除返回 0 但不生效。
log="${OHMYMODS_MOCK_XATTR_LOG:?}"
state="${OHMYMODS_MOCK_XATTR_STATE:?}"
qfile="$state/quarantined"
line=""
for a in "$@"; do line="$line|$a"; done
printf 'CALL%s\n' "$line" >> "$log"
matches() { # $1=注入值 $2=文件
    case "$1" in
        "") return 1 ;;
        all) return 0 ;;
        *"$2"*) return 0 ;;
        *) return 1 ;;
    esac
}
if [ "$1" = "-d" ]; then
    attr="$2"; file="$3"
    [ -n "$attr" ] && [ -n "$file" ] || { echo "xattr: usage" >&2; exit 2; }
    if matches "${OHMYMODS_MOCK_XATTR_FAIL_DELETE:-}" "$file"; then
        echo "xattr: [Errno 1] mock 注入的删除失败: $file" >&2
        exit 1
    fi
    if ! grep -qxF "$file" "$qfile" 2>/dev/null; then
        echo "xattr: No such xattr: $attr" >&2
        exit 1
    fi
    if ! matches "${OHMYMODS_MOCK_XATTR_NOOP_DELETE:-}" "$file"; then
        grep -vxF "$file" "$qfile" > "$qfile.tmp" || :
        mv "$qfile.tmp" "$qfile"
    fi
    exit 0
fi
if [ $# -eq 1 ] && [ -f "$1" ]; then
    if matches "${OHMYMODS_MOCK_XATTR_FAIL_LIST_AFTER_DELETE:-}" "$1" && grep -q '^CALL|-d|' "$log"; then
        echo "xattr: mock read failure after deletion: $1" >&2
        exit 1
    fi
    if matches "${OHMYMODS_MOCK_XATTR_FAIL_LIST:-}" "$1"; then
        echo "xattr: [Errno 13] mock 注入的读取失败: $1" >&2
        exit 1
    fi
    if grep -qxF "$1" "$qfile" 2>/dev/null; then
        printf 'com.apple.quarantine\n'
    fi
    exit 0
fi
echo "xattr: unknown arguments" >&2
exit 2
MOCKEOF
    chmod +x "$1/xattr"
}

make_mock_arch() { # $1=bin 目录 $2=输出文件
    mkdir -p "$1"
    cat > "$1/arch" <<EOF
#!/bin/sh
# 测试替身：模拟系统 arch(1)。记录 argv 与加载器相关 env；可选休眠模拟长运行游戏。
out="\${OHMYMODS_MOCK_ARCH_OUT:?}"
{
    printf 'ARGV:\n'
    printf '%s\n' "\$@"
    printf 'ENV:\n'
    env | grep -E '^(DOORSTOP_|BEPINEX_|DYLD_|ARCHPREFERENCE)' | sort
    printf 'MARKERS:\n'
} > "\$out"
if [ -n "\${OHMYMODS_MOCK_ARCH_SECONDS:-}" ]; then
    /bin/sleep "\$OHMYMODS_MOCK_ARCH_SECONDS"
    printf 'END\n' >> "\$out"
fi
exit 0
EOF
    chmod +x "$1/arch"
}

builder_env() { # $1=构建器路径(通常家目录副本) $2=assembly sha 或 '-'；其后为参数
    local builder="$1" sha="$2"
    shift 2
    "$PY" "$DRIVER" "$builder" "$sha" "$@"
}

setup_builder_fixture() { # $1=工作目录；设置 BH/B_INPUT/B_NOTICES/B_LOCK/B_GAME/B_FILES/B_SHA
    local w="$1" f
    f=$("$PY" "$FIXTURE" --root "$w/game-root" --input-root "$w/input-root" \
        --notices-dir "$w/notices" --builder-home "$w/builder-home" \
        --emit-lock "$w/input-lock.json")
    BH=$(printf '%s' "$f" | jget builder_home)
    B_INPUT=$(printf '%s' "$f" | jget input_root)
    B_NOTICES=$(printf '%s' "$f" | jget notices_dir)
    B_LOCK=$(printf '%s' "$f" | jget lock_path)
    B_GAME="$w/game-root/KingdomTwoCrowns.app"
    B_FILES=$(printf '%s' "$f" | jget lock_files)
    B_SHA=$(printf '%s' "$f" | jget assembly_sha256)
}

# ---------------- 生产代码无测试通道（静态断言） ----------------

test_no_test_bypass_in_production() {
    if grep -n -E 'TEST_SPAWN|allow_test_overrides|OHMYMODS_TEST' "$LAUNCHER" >/dev/null; then
        bad "no-seam/launcher（发现测试通道残留）"
    else
        ok "no-seam/launcher"
    fi
    if grep -n -E 'OHMYMODS_TEST|ALLOW_NONCONTRACT' "$BUILDER" >/dev/null; then
        bad "no-seam/builder（发现测试通道残留）"
    else
        ok "no-seam/builder"
    fi
    if grep -q 'trap cleanup EXIT INT TERM HUP' "$LAUNCHER"; then
        ok "no-seam/signal-list-has-INT"
    else
        bad "no-seam/signal-list-has-INT（信号列表缺 INT）"
    fi
    if grep -q 'OHMYMODS_' "$LAUNCHER"; then
        bad "no-seam/launcher-env（启动器引用了测试/信任环境变量）"
    else
        ok "no-seam/launcher-env"
    fi
    if [ "$(grep -c -e '--yes' -e '--assume-yes' -e '--force-trust' -e '--trust-all' "$LAUNCHER")" = "0" ]; then
        ok "no-seam/no-trust-bypass-flag"
    else
        bad "no-seam/no-trust-bypass-flag（存在自动确认/绕过开关）"
    fi
}

# ---------------- 启动器：发现与参数 ----------------

test_help() {
    local r
    r=$(run_launcher --help)
    out_contains "help/exit0" "${r%%$'\n'*}" "0"
    out_contains "help/usage" "$r" "--check-only"
    out_contains "help/unhollowed" "$r" "unhollowed"
}

test_missing_game() {
    local w f pkg r
    w="$WORK_ROOT/t02"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    rm -rf "$w/KingdomTwoCrowns.app"
    r=$(run_launcher --check-only)
    out_contains "missing/fail" "${r%%$'\n'*}" "1"
    out_contains "missing/msg" "$r" "未找到游戏"
}

test_beside_layout_checkonly() {
    local w f pkg r
    w="$WORK_ROOT/t03"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    r=$(run_launcher --check-only)
    out_contains "beside/exit0" "${r%%$'\n'*}" "0"
    out_contains "beside/sums" "$r" "包完整性校验通过"
    out_contains "beside/fingerprint" "$r" "游戏指纹校验通过"
    out_contains "beside/readonly" "$r" "未写入任何文件"
    if [ -e "$w/KingdomTwoCrowns_Data" ] || [ -L "$w/KingdomTwoCrowns_Data" ]; then
        bad "beside/no-alias-in-checkonly"
    else
        ok "beside/no-alias-in-checkonly"
    fi
}

test_inside_layout_checkonly() {
    local w f pkg r
    w="$WORK_ROOT/t04"; f=$(new_fixture "$w" --layout inside)
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    rm -rf "$w/KingdomTwoCrowns.app"   # inside 布局只保留包内游戏副本
    pkg=$(phys "$pkg")
    r=$(run_launcher --check-only)
    out_contains "inside/exit0" "${r%%$'\n'*}" "0"
    out_contains "inside/game-inside" "$r" "$pkg/KingdomTwoCrowns.app"
}

test_ambiguous_game() {
    local w f pkg app r
    w="$WORK_ROOT/t05"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    app=$(printf '%s' "$f" | jget app)
    CUR_LAUNCHER="$pkg/launcher.command"
    cp -R "$app" "$pkg/KingdomTwoCrowns.app"
    r=$(run_launcher --check-only)
    out_contains "ambiguous/fail" "${r%%$'\n'*}" "1"
    out_contains "ambiguous/msg" "$r" "同时存在"
}

test_spaces_in_paths() {
    local w f pkg app r
    w="$WORK_ROOT/My Games (测试)/t06"
    f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    app=$(printf '%s' "$f" | jget app)
    CUR_LAUNCHER="$pkg/launcher.command"
    app=$(phys "$app")
    r=$(run_launcher --check-only)
    out_contains "spaces/exit0" "${r%%$'\n'*}" "0"
    out_contains "spaces/path" "$r" "$app"
    r=$(run_launcher --game "$app" --check-only)
    out_contains "spaces/--game" "${r%%$'\n'*}" "0"
}

test_game_exe_variant() {
    local w f pkg app r
    w="$WORK_ROOT/t07"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    app=$(printf '%s' "$f" | jget app)
    CUR_LAUNCHER="$pkg/launcher.command"
    r=$(run_launcher --game "$app/Contents/MacOS/KingdomTwoCrowns" --check-only)
    out_contains "exe-variant/exit0" "${r%%$'\n'*}" "0"
    r=$(run_launcher --game "$w/不存在" --check-only)
    out_contains "exe-variant/missing" "${r%%$'\n'*}" "1"
    r=$(run_launcher --game "$app" --game "$app" --check-only)
    out_contains "exe-variant/dup" "${r%%$'\n'*}" "1"
    r=$(run_launcher --game "$w" --check-only)
    out_contains "exe-variant/wrong-name" "${r%%$'\n'*}" "1"
}

test_reserved_flags() {
    local w f pkg r
    w="$WORK_ROOT/t08"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    r=$(run_launcher --doorstop-target-assembly /tmp/evil.dll --check-only)
    out_contains "reserved/long" "${r%%$'\n'*}" "1"
    out_contains "reserved/msg" "$r" "拒绝保留参数"
    r=$(run_launcher --doorstop-enable --check-only)
    out_contains "reserved/exact" "${r%%$'\n'*}" "1"
    r=$(run_launcher --doorstop_target_assembly /tmp/x --check-only)
    out_contains "reserved/underscore" "${r%%$'\n'*}" "1"
    r=$(run_launcher --doorstop-enabled=1 --check-only)
    out_contains "reserved/eq-value" "${r%%$'\n'*}" "1"
    r=$(run_launcher --unhollowed-path /tmp/interop --check-only)
    out_contains "reserved/unhollowed" "${r%%$'\n'*}" "1"
    r=$(run_launcher --unhollowed-path=/tmp/interop --check-only)
    out_contains "reserved/unhollowed-eq" "${r%%$'\n'*}" "1"
}

# ---------------- 启动器：SHA256SUMS / 指纹 / 架构 ----------------

test_sums_verification() {
    local w f pkg r
    w="$WORK_ROOT/t09"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    printf 'junk' >> "$pkg/BepInEx/core/BepInEx.Unity.IL2CPP.dll"
    r=$(run_launcher --check-only)
    out_contains "sums/tamper-fail" "${r%%$'\n'*}" "1"
    out_contains "sums/tamper-msg" "$r" "校验失败"

    w="$WORK_ROOT/t09b"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    rm "$pkg/SHA256SUMS"
    r=$(run_launcher --check-only)
    out_contains "sums/missing-fail" "${r%%$'\n'*}" "1"
    out_contains "sums/missing-msg" "$r" "包不完整"

    w="$WORK_ROOT/t09c"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    lock_edit "$pkg/game-lock.json" 'd["executable_sha256"]="0"*64'
    r=$(run_launcher --check-only)
    out_contains "sums/gamelock-tamper-fail" "${r%%$'\n'*}" "1"
    out_contains "sums/gamelock-tamper-msg" "$r" "校验失败"

    w="$WORK_ROOT/t09d"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    printf '\n; x' >> "$pkg/defaults/BepInEx.cfg"
    r=$(run_launcher --check-only)
    out_contains "sums/defaults-tamper-fail" "${r%%$'\n'*}" "1"
}

test_assembly_hash_mismatch() {
    local w f pkg app r
    w="$WORK_ROOT/t10"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    app=$(printf '%s' "$f" | jget app)
    CUR_LAUNCHER="$pkg/launcher.command"
    # 篡改游戏侧（包内 SUMS 不覆盖游戏文件）→ 四指纹路径报错
    printf 'X' >> "$app/Contents/Frameworks/GameAssembly.dylib"
    r=$(run_launcher --check-only)
    out_contains "asm-hash/fail" "${r%%$'\n'*}" "1"
    out_contains "asm-hash/msg" "$r" "游戏版本与包锁定"
}

test_game_fingerprints() {
    local w f pkg app r
    w="$WORK_ROOT/t10b"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    app=$(printf '%s' "$f" | jget app)
    CUR_LAUNCHER="$pkg/launcher.command"
    printf 'X' >> "$app/Contents/Info.plist"
    r=$(run_launcher --check-only)
    out_contains "plist-hash/fail" "${r%%$'\n'*}" "1"
    out_contains "plist-hash/msg" "$r" "Info.plist"

    w="$WORK_ROOT/t10c"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    app=$(printf '%s' "$f" | jget app)
    CUR_LAUNCHER="$pkg/launcher.command"
    printf 'X' >> "$app/Contents/Resources/Data/il2cpp_data/Metadata/global-metadata.dat"
    r=$(run_launcher --check-only)
    out_contains "metadata-hash/fail" "${r%%$'\n'*}" "1"
    out_contains "metadata-hash/msg" "$r" "global-metadata"
}

test_wrong_arch() {
    local w f pkg r
    w="$WORK_ROOT/t11"; f=$(new_fixture "$w" --arch x86)
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    r=$(run_launcher --check-only)
    out_contains "arch-x86/fail" "${r%%$'\n'*}" "1"
    out_contains "arch-x86/msg" "$r" "ARM64 Mach-O"
    w="$WORK_ROOT/t12"; f=$(new_fixture "$w" --arch text)
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    r=$(run_launcher --check-only)
    out_contains "arch-text/fail" "${r%%$'\n'*}" "1"
}

test_gamelock_problems() {
    local w f pkg r
    w="$WORK_ROOT/t13"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    rm "$pkg/game-lock.json" "$pkg/SHA256SUMS"   # 一并移除 SUMS 才能到达 lock 缺失分支
    r=$(run_launcher --check-only)
    out_contains "gamelock-missing/fail" "${r%%$'\n'*}" "1"
    out_contains "gamelock-missing/msg" "$r" "包不完整"

    w="$WORK_ROOT/t14"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    lock_edit "$pkg/game-lock.json" 'd["assembly_sha256"]="zz"'
    "$PY" - "$pkg" <<'PYEOF'   # 重写 SUMS 中 game-lock 哈希，保证到达解析分支
import hashlib, os, sys
pkg = sys.argv[1]
path = os.path.join(pkg, "game-lock.json")
lines = open(os.path.join(pkg, "SHA256SUMS"), encoding="utf-8").read().splitlines()
out = []
for line in lines:
    digest, rel = line.split("  ", 1)
    if rel == "game-lock.json":
        digest = hashlib.sha256(open(path, "rb").read()).hexdigest()
    out.append("%s  %s" % (digest, rel))
open(os.path.join(pkg, "SHA256SUMS"), "w", encoding="utf-8").write("\n".join(out) + "\n")
PYEOF
    r=$(run_launcher --check-only)
    out_contains "gamelock-invalid/fail" "${r%%$'\n'*}" "1"
    out_contains "gamelock-invalid/msg" "$r" "缺少有效的 assembly_sha256"

    w="$WORK_ROOT/t14b"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    lock_edit "$pkg/game-lock.json" 'd["executable_sha256"]="0"*64'
    "$PY" - "$pkg" <<'PYEOF'
import hashlib, os, sys
pkg = sys.argv[1]
path = os.path.join(pkg, "game-lock.json")
lines = open(os.path.join(pkg, "SHA256SUMS"), encoding="utf-8").read().splitlines()
out = []
for line in lines:
    digest, rel = line.split("  ", 1)
    if rel == "game-lock.json":
        digest = hashlib.sha256(open(path, "rb").read()).hexdigest()
    out.append("%s  %s" % (digest, rel))
open(os.path.join(pkg, "SHA256SUMS"), "w", encoding="utf-8").write("\n".join(out) + "\n")
PYEOF
    r=$(run_launcher --check-only)
    out_contains "exe-hash-mismatch/fail" "${r%%$'\n'*}" "1"
    out_contains "exe-hash-mismatch/msg" "$r" "可执行文件"
}

# ---------------- 启动器：cfg 兼容检查 ----------------

cfg_case() { # $1=目录 $2=cfg 内容（printf %b 转义）；stdout 同 run_launcher
    local f pkg cfg
    f=$(new_fixture "$1")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    cfg="$pkg/BepInEx/config/BepInEx.cfg"
    mkdir -p "$pkg/BepInEx/config"
    printf '%b' "$2" > "$cfg"
    run_launcher --check-only
}

test_cfg_semantics() {
    # 语义回归：只认 [IL2CPP] section；同键末值覆盖；显式空值拒绝；缺失按真实默认。
    local r
    # [Other] 伪安全值 + [IL2CPP] 外部值 → 拒（防骗过预检）
    r=$(cfg_case "$WORK_ROOT/t17d" '[Other]\nIL2CPPInteropAssembliesPath = {BepInEx}\nScanMethodRefs = false\n[IL2CPP]\nIL2CPPInteropAssembliesPath = /tmp/evil\nScanMethodRefs = false\n')
    out_contains "cfg-sem/other-decoy-fail" "${r%%$'\n'*}" "1"
    out_contains "cfg-sem/other-decoy-msg" "$r" "IL2CPPInteropAssembliesPath"
    # 带尾注的行不是 BepInEx section header；仍在 IL2CPP，外部末值必须被拒。
    r=$(cfg_case "$WORK_ROOT/t17-header-note" '[IL2CPP]\nScanMethodRefs = false\nIL2CPPInteropAssembliesPath = {BepInEx}\n[Other] # note\nIL2CPPInteropAssembliesPath = /tmp/evil\n')
    out_contains "cfg-sem/header-note-fail" "${r%%$'\n'*}" "1"
    out_contains "cfg-sem/header-note-msg" "$r" "IL2CPPInteropAssembliesPath"
    # 方括号内部空白属于 section 名；不能被当作 IL2CPP 的 false 设置。
    r=$(cfg_case "$WORK_ROOT/t17-header-space" '[ IL2CPP ]\nScanMethodRefs = false\n')
    out_contains "cfg-sem/header-inner-space-fail" "${r%%$'\n'*}" "1"
    # 同 section 末值覆盖为外部值 → 拒
    r=$(cfg_case "$WORK_ROOT/t17e" '[IL2CPP]\nIL2CPPInteropAssembliesPath = {BepInEx}\nIL2CPPInteropAssembliesPath = /tmp/evil\nScanMethodRefs = false\n')
    out_contains "cfg-sem/last-wins-evil-fail" "${r%%$'\n'*}" "1"
    # 末值覆盖回安全值 → 过（真实末值语义）
    r=$(cfg_case "$WORK_ROOT/t17f" '[IL2CPP]\nIL2CPPInteropAssembliesPath = /tmp/evil\nIL2CPPInteropAssembliesPath = {BepInEx}\nScanMethodRefs = false\n')
    out_contains "cfg-sem/last-wins-safe-ok" "${r%%$'\n'*}" "0"
    # 显式空值（bool 键）→ 拒
    r=$(cfg_case "$WORK_ROOT/t17g" '[IL2CPP]\nScanMethodRefs =\n')
    out_contains "cfg-sem/empty-fail" "${r%%$'\n'*}" "1"
    out_contains "cfg-sem/empty-msg" "$r" "显式空值"
    # 显式空值（路径键）→ 拒
    r=$(cfg_case "$WORK_ROOT/t17h" '[IL2CPP]\nScanMethodRefs = false\nGlobalMetadataPath =\n')
    out_contains "cfg-sem/empty-path-fail" "${r%%$'\n'*}" "1"
    # 已有 cfg 缺 ScanMethodRefs（真实默认 true）→ 拒
    r=$(cfg_case "$WORK_ROOT/t17i" '[IL2CPP]\nIL2CPPInteropAssembliesPath = {BepInEx}\n')
    out_contains "cfg-sem/missing-scan-fail" "${r%%$'\n'*}" "1"
    out_contains "cfg-sem/missing-scan-msg" "$r" "缺少"
    # UpdateInteropAssemblies=false → 拒
    r=$(cfg_case "$WORK_ROOT/t17j" '[IL2CPP]\nScanMethodRefs = false\nUpdateInteropAssemblies = false\n')
    out_contains "cfg-sem/update-interop-fail" "${r%%$'\n'*}" "1"
    out_contains "cfg-sem/update-interop-msg" "$r" "UpdateInteropAssemblies"
    # bool 大小写兼容（FALSE）→ 过
    r=$(cfg_case "$WORK_ROOT/t17k" '[IL2CPP]\nScanMethodRefs = FALSE\n')
    out_contains "cfg-sem/bool-case-ok" "${r%%$'\n'*}" "0"
    # 非布尔值 → 拒
    r=$(cfg_case "$WORK_ROOT/t17l" '[IL2CPP]\nScanMethodRefs = yes\n')
    out_contains "cfg-sem/bool-invalid-fail" "${r%%$'\n'*}" "1"
    out_contains "cfg-sem/bool-invalid-msg" "$r" "布尔值"
}

test_cfg_compatibility() {
    local w f pkg cfg r
    w="$WORK_ROOT/t17"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    cfg="$pkg/BepInEx/config/BepInEx.cfg"
    mkdir -p "$pkg/BepInEx/config"
    printf '[IL2CPP]\nScanMethodRefs = true\n' > "$cfg"
    r=$(run_launcher --check-only)
    out_contains "cfg-compat/scan-fail" "${r%%$'\n'*}" "1"
    out_contains "cfg-compat/scan-msg" "$r" "ScanMethodRefs"

    w="$WORK_ROOT/t17b"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    cfg="$pkg/BepInEx/config/BepInEx.cfg"
    mkdir -p "$pkg/BepInEx/config"
    printf '[IL2CPP]\nIL2CPPInteropAssembliesPath = /tmp/evil\n' > "$cfg"
    r=$(run_launcher --check-only)
    out_contains "cfg-compat/interop-fail" "${r%%$'\n'*}" "1"
    out_contains "cfg-compat/interop-msg" "$r" "IL2CPPInteropAssembliesPath"

    w="$WORK_ROOT/t17c"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    cfg="$pkg/BepInEx/config/BepInEx.cfg"
    mkdir -p "$pkg/BepInEx/config"
    printf '[IL2CPP]\nScanMethodRefs = false\nIL2CPPInteropAssembliesPath = {BepInEx}\nGlobalMetadataPath = {GameDataPath}/il2cpp_data/Metadata/global-metadata.dat\n[MyMod]\nKeepMe = 1\n' > "$cfg"
    r=$(run_launcher --check-only)
    out_contains "cfg-compat/ok" "${r%%$'\n'*}" "0"
}

# ---------------- 启动器：锁 / 运行中游戏 / 清理 ----------------

test_stale_lock_not_deleted() {
    local w f pkg r
    w="$WORK_ROOT/t15"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    printf 'pid=999999\nstarted=2020-01-01T00:00:00Z\nexe=/x\n' > "$pkg/.launcher.lock"
    r=$(run_launcher --check-only)
    out_contains "stale/fail" "${r%%$'\n'*}" "1"
    out_contains "stale/msg" "$r" "残留锁文件"
    if [ -e "$pkg/.launcher.lock" ]; then
        ok "stale/kept"
    else
        bad "stale/kept（锁被自动删除了）"
    fi
}

test_live_lock() {
    local w f pkg r holder
    w="$WORK_ROOT/t16"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    /bin/sleep 300 &
    holder=$!
    printf 'pid=%s\nstarted=2020-01-01T00:00:00Z\nexe=/x\n' "$holder" > "$pkg/.launcher.lock"
    r=$(run_launcher --check-only)
    out_contains "live/fail" "${r%%$'\n'*}" "1"
    out_contains "live/msg" "$r" "存活进程"
    kill "$holder" 2>/dev/null || true
    wait "$holder" 2>/dev/null || true
}

start_fake_game() { # $1=可执行文件路径；启动并等待 comm 出现，设置 FAKE_GAME_PID
    mkdir -p "$(dirname "$1")"
    cp /bin/sleep "$1"
    chmod +x "$1"
    codesign -f -s - "$1" >/dev/null 2>&1 || true
    "$1" 300 &
    FAKE_GAME_PID=$!
    local i=0
    while [ $i -lt 50 ]; do
        if ps -axo comm= | grep -q 'KingdomTwoCrowns$'; then
            return 0
        fi
        i=$((i + 1))
        sleep 0.1
    done
    return 1
}

stop_fake_game() {
    kill "$FAKE_GAME_PID" 2>/dev/null || true
    wait "$FAKE_GAME_PID" 2>/dev/null || true
    FAKE_GAME_PID=""
}

test_running_game_scan() {
    local w f pkg r saved_ps_mock
    w="$WORK_ROOT/t33"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    saved_ps_mock="$PS_MOCK_ENABLED"
    PS_MOCK_ENABLED=0   # 本用例用真实进程列表覆盖该门本身

    # 场景 1：标准 bundle 路径
    start_fake_game "$WORK_ROOT/fakegame/KingdomTwoCrowns.app/Contents/MacOS/KingdomTwoCrowns"
    r=$(run_launcher --check-only)
    out_contains "running/fail" "${r%%$'\n'*}" "1"
    out_contains "running/msg" "$r" "已在运行"
    stop_fake_game

    # 场景 2：改名 bundle（基名仍为 KingdomTwoCrowns）
    start_fake_game "$WORK_ROOT/fakegame/Renamed.app/Contents/MacOS/KingdomTwoCrowns"
    r=$(run_launcher --check-only)
    out_contains "running-renamed/fail" "${r%%$'\n'*}" "1"
    out_contains "running-renamed/msg" "$r" "已在运行"
    stop_fake_game

    # 场景 3：裸同名可执行文件（不在 .app 内）
    start_fake_game "$WORK_ROOT/fakegame/bin/KingdomTwoCrowns"
    r=$(run_launcher --check-only)
    out_contains "running-bare/fail" "${r%%$'\n'*}" "1"
    out_contains "running-bare/msg" "$r" "已在运行"
    stop_fake_game
    PS_MOCK_ENABLED="$saved_ps_mock"
}

# ---------------- 启动器：mock-arch 完整启动（无生产测试通道） ----------------

test_mock_launch_happy_path() {
    local w f pkg app r st tgt mock_out mock_bin
    w="$WORK_ROOT/happy path (空格)"
    f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    app=$(printf '%s' "$f" | jget app)
    CUR_LAUNCHER="$pkg/launcher.command"
    pkg=$(phys "$pkg")
    app=$(phys "$app")
    mock_out="$w/mock-arch-out.txt"
    mock_bin="$w/mockbin"
    make_mock_arch "$mock_bin" "$mock_out"
    local before_as before_ex before_pl before_md app_files_before app_files_after
    before_as=$(shasum -a 256 "$app/Contents/Frameworks/GameAssembly.dylib" | awk '{print $1}')
    before_ex=$(shasum -a 256 "$app/Contents/MacOS/KingdomTwoCrowns" | awk '{print $1}')
    before_pl=$(shasum -a 256 "$app/Contents/Info.plist" | awk '{print $1}')
    before_md=$(shasum -a 256 "$app/Contents/Resources/Data/il2cpp_data/Metadata/global-metadata.dat" | awk '{print $1}')
    app_files_before=$(find "$app" -type f | wc -l | tr -d ' ')

    OHMYMODS_MOCK_ARCH_OUT="$mock_out" PATH="$(ps_mock_setup):$mock_bin:$PATH" \
        "$CUR_LAUNCHER" -width 1280 "my arg with space" > "$w/out.txt" 2>&1
    st=$?
    if [ "$st" -eq 0 ]; then ok "happy/exit0"; else bad "happy/exit0（退出码 ${st}）"; fi

    if [ -L "$w/KingdomTwoCrowns_Data" ]; then
        tgt=$(readlink "$w/KingdomTwoCrowns_Data")
        if [ "$tgt" = "KingdomTwoCrowns.app/Contents/Resources/Data" ]; then
            ok "happy/alias-created"
        else
            bad "happy/alias-created（指向 ${tgt}）"
        fi
    else
        bad "happy/alias-created（别名不存在）"
    fi
    app_files_after=$(find "$app" -type f | wc -l | tr -d ' ')
    [ "$app_files_before" = "$app_files_after" ] && ok "happy/app-filecount-unchanged" \
        || bad "happy/app-filecount-unchanged（${app_files_before}->${app_files_after}）"
    [ "$(shasum -a 256 "$app/Contents/Frameworks/GameAssembly.dylib" | awk '{print $1}')" = "$before_as" ] \
        && ok "happy/app-untouched-assembly" || bad "happy/app-untouched-assembly"
    [ "$(shasum -a 256 "$app/Contents/MacOS/KingdomTwoCrowns" | awk '{print $1}')" = "$before_ex" ] \
        && ok "happy/app-untouched-exe" || bad "happy/app-untouched-exe"
    [ "$(shasum -a 256 "$app/Contents/Info.plist" | awk '{print $1}')" = "$before_pl" ] \
        && ok "happy/app-untouched-plist" || bad "happy/app-untouched-plist"
    [ "$(shasum -a 256 "$app/Contents/Resources/Data/il2cpp_data/Metadata/global-metadata.dat" | awk '{print $1}')" = "$before_md" ] \
        && ok "happy/app-untouched-metadata" || bad "happy/app-untouched-metadata"
    if [ -e "$pkg/.launcher.lock" ]; then bad "happy/lock-released"; else ok "happy/lock-released"; fi
    if cmp -s "$PKG_DIR/defaults/BepInEx.cfg" "$pkg/BepInEx/config/BepInEx.cfg"; then
        ok "happy/cfg-seeded"
    else
        bad "happy/cfg-seeded"
    fi
    # arch 替身收到的 argv 与 env（真实 arch 调用行产出）
    grep -qx -- '-arm64' "$mock_out" && ok "happy/arch-arm64" || bad "happy/arch-arm64"
    grep -qx -- '-e' "$mock_out" && ok "happy/arch-e" || bad "happy/arch-e"
    grep -qx "DYLD_INSERT_LIBRARIES=$pkg/libdoorstop.dylib" "$mock_out" \
        && ok "happy/arch-e-insert" || bad "happy/arch-e-insert"
    grep -qx "DYLD_LIBRARY_PATH=$app/Contents/Frameworks:$pkg:$pkg/dotnet" "$mock_out" \
        && ok "happy/arch-e-libpath" || bad "happy/arch-e-libpath"
    grep -qx "$app/Contents/MacOS/KingdomTwoCrowns" "$mock_out" && ok "happy/arch-exe" || bad "happy/arch-exe"
    grep -qx -- '-width' "$mock_out" && ok "happy/arg1" || bad "happy/arg1"
    grep -qx -- '1280' "$mock_out" && ok "happy/arg2" || bad "happy/arg2"
    grep -qx -- 'my arg with space' "$mock_out" && ok "happy/arg3" || bad "happy/arg3"
    grep -qx "DOORSTOP_ENABLED=1" "$mock_out" && ok "happy/env-doorstop" || bad "happy/env-doorstop"
    grep -qx "ARCHPREFERENCE=arm64" "$mock_out" && ok "happy/env-arch" || bad "happy/env-arch"
    grep -qx "BEPINEX_GAME_ASSEMBLY_PATH=$app/Contents/Frameworks/GameAssembly.dylib" "$mock_out" \
        && ok "happy/env-assembly" || bad "happy/env-assembly"
    grep -qx "BEPINEX_PRELOADER_LOG=$pkg/preloader.log" "$mock_out" \
        && ok "happy/env-preloader-log" || bad "happy/env-preloader-log"
    grep -qx "DOORSTOP_TARGET_ASSEMBLY=$pkg/BepInEx/core/BepInEx.Unity.IL2CPP.dll" "$mock_out" \
        && ok "happy/env-target" || bad "happy/env-target"
    if grep -q '^DYLD_' <(sed -n '/^ENV:/,/^MARKERS:/p' "$mock_out" | grep -v '^DYLD_INSERT\|^DYLD_LIBRARY\|^ENV:\|^MARKERS:'); then
        bad "happy/no-extra-dyld"
    else
        ok "happy/no-extra-dyld"
    fi

    OHMYMODS_MOCK_ARCH_OUT="$mock_out" PATH="$(ps_mock_setup):$mock_bin:$PATH" "$CUR_LAUNCHER" >/dev/null 2>&1
    [ $? -eq 0 ] && ok "happy/second-run" || bad "happy/second-run"
}

test_term_cleanup_waits_child() {
    # TERM 清理：转发终止子进程（mock arch 替身）、等待退出后释放锁；子进程未写 END。
    local w f pkg mock_out mock_bin lpid i st
    w="$WORK_ROOT/t35"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    mock_out="$w/mock-arch-out.txt"
    mock_bin="$w/mockbin"
    make_mock_arch "$mock_bin" "$mock_out"
    OHMYMODS_MOCK_ARCH_OUT="$mock_out" OHMYMODS_MOCK_ARCH_SECONDS=30 \
        PATH="$(ps_mock_setup):$mock_bin:$PATH" "$CUR_LAUNCHER" > "$w/out.txt" 2>&1 &
    lpid=$!
    # 等待「锁已持有 且 mock arch 已执行」（launcher 先获锁后 spawn，
    # 只等锁会在子进程尚未建立时触发清理路径，测不到等待子进程的语义）。
    i=0
    while [ $i -lt 200 ] && { [ ! -e "$pkg/.launcher.lock" ] || [ ! -s "$mock_out" ]; }; do
        i=$((i + 1))
        sleep 0.1
    done
    [ -e "$pkg/.launcher.lock" ] && ok "term/lock-held" || bad "term/lock-held"
    [ -s "$mock_out" ] && ok "term/child-started" || bad "term/child-started"
    kill -TERM "$lpid" 2>/dev/null
    st=0
    wait "$lpid" 2>/dev/null || st=$?
    if [ "$st" -ne 0 ]; then ok "term/nonzero-exit"; else bad "term/nonzero-exit"; fi
    if [ -e "$pkg/.launcher.lock" ]; then bad "term/lock-released"; else ok "term/lock-released"; fi
    if grep -q '^END$' "$mock_out"; then bad "term/child-terminated"; else ok "term/child-terminated"; fi
    sleep 0.5
    if ps -axo comm= | grep -q '/mockbin/arch'; then
        bad "term/no-orphan-mock"
    else
        ok "term/no-orphan-mock"
    fi
}

test_cfg_sentinel_preserved() {
    local w f pkg cfg m0 i0
    w="$WORK_ROOT/t18"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    cfg="$pkg/BepInEx/config/BepInEx.cfg"
    mkdir -p "$pkg/BepInEx/config"
    printf '[Caching]\nEnableAssemblyCache = false ; sentinel\n[IL2CPP]\nScanMethodRefs = false\n' > "$cfg"
    touch -t 202001020304 "$cfg"
    m0=$(stat -f %m "$cfg")
    i0=$(stat -f %i "$cfg")
    local mock_out="$w/mock-arch-out.txt" mock_bin="$w/mockbin"
    make_mock_arch "$mock_bin" "$mock_out"
    OHMYMODS_MOCK_ARCH_OUT="$mock_out" PATH="$(ps_mock_setup):$mock_bin:$PATH" "$CUR_LAUNCHER" >/dev/null 2>&1
    [ "$(cat "$cfg")" = '[Caching]
EnableAssemblyCache = false ; sentinel
[IL2CPP]
ScanMethodRefs = false' ] && ok "cfg/content-preserved" || bad "cfg/content-preserved"
    [ "$(stat -f %m "$cfg")" = "$m0" ] && ok "cfg/mtime-preserved" || bad "cfg/mtime-preserved"
    [ "$(stat -f %i "$cfg")" = "$i0" ] && ok "cfg/inode-preserved" || bad "cfg/inode-preserved"
}

# ---------------- 启动器：可写路径 fail-closed ----------------

test_writable_layout_failclosed() {
    local w f pkg r ext
    w="$WORK_ROOT/t19d"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    mkdir -p "$w/external-config"
    ln -s "$w/external-config" "$pkg/BepInEx/config"
    r=$(run_launcher --check-only)
    out_contains "layout/config-symlink-fail" "${r%%$'\n'*}" "1"
    out_contains "layout/config-symlink-msg" "$r" "符号链接"

    w="$WORK_ROOT/t19e"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    mkdir -p "$pkg/BepInEx/config" "$w/external"
    printf 'precious' > "$w/external/BepInEx.cfg"
    ln -s "$w/external/BepInEx.cfg" "$pkg/BepInEx/config/BepInEx.cfg"
    r=$(run_launcher --check-only)
    out_contains "layout/cfg-symlink-fail" "${r%%$'\n'*}" "1"
    [ "$(cat "$w/external/BepInEx.cfg")" = "precious" ] \
        && ok "layout/cfg-external-untouched" || bad "layout/cfg-external-untouched"

    w="$WORK_ROOT/t19f"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    printf 'file' > "$pkg/BepInEx/config"   # config 层被文件占位（core 保持完好）
    r=$(run_launcher --check-only)
    out_contains "layout/bepinex-file-fail" "${r%%$'\n'*}" "1"
    out_contains "layout/bepinex-file-msg" "$r" "不是目录"

    w="$WORK_ROOT/t19g"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    ln -s "$w/gone-config" "$pkg/BepInEx/config"   # 断链占位
    r=$(run_launcher --check-only)
    out_contains "layout/bepinex-dangling-fail" "${r%%$'\n'*}" "1"
    out_contains "layout/bepinex-dangling-msg" "$r" "符号链接"

    w="$WORK_ROOT/t19h"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    mkdir -p "$pkg/BepInEx/cache" "$w/external"
    ln -s "$w/external/dll" "$pkg/BepInEx/cache/evil.dll"
    r=$(run_launcher --check-only)
    out_contains "layout/cache-symlink-fail" "${r%%$'\n'*}" "1"
}

# ---------------- 启动器：别名 ----------------

test_alias_reuse_and_conflicts() {
    local w f pkg r i0 m0 mock_bin mock_out
    w="$WORK_ROOT/t19"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    ln -s "KingdomTwoCrowns.app/Contents/Resources/Data" "$w/KingdomTwoCrowns_Data"
    i0=$(stat -f %i "$w/KingdomTwoCrowns_Data")
    m0=$(stat -f %m "$w/KingdomTwoCrowns_Data")
    mock_out="$w/mock-arch-out.txt"; mock_bin="$w/mockbin"
    make_mock_arch "$mock_bin" "$mock_out"
    OHMYMODS_MOCK_ARCH_OUT="$mock_out" PATH="$(ps_mock_setup):$mock_bin:$PATH" "$CUR_LAUNCHER" >/dev/null 2>&1
    [ "$(stat -f %i "$w/KingdomTwoCrowns_Data")" = "$i0" ] && ok "alias-reuse/inode" || bad "alias-reuse/inode"
    [ "$(stat -f %m "$w/KingdomTwoCrowns_Data")" = "$m0" ] && ok "alias-reuse/mtime" || bad "alias-reuse/mtime"

    w="$WORK_ROOT/t19b"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    mkdir "$w/KingdomTwoCrowns_Data"
    printf 'keep me' > "$w/KingdomTwoCrowns_Data/important.txt"
    r=$(run_launcher --check-only)
    out_contains "alias-conflict-dir/fail" "${r%%$'\n'*}" "1"
    out_contains "alias-conflict-dir/msg" "$r" "数据别名位置被占用"
    [ "$(cat "$w/KingdomTwoCrowns_Data/important.txt")" = "keep me" ] \
        && ok "alias-conflict-dir/untouched" || bad "alias-conflict-dir/untouched"

    w="$WORK_ROOT/t20"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    ln -s "Gone.app/Contents/Resources/Data" "$w/KingdomTwoCrowns_Data"
    r=$(run_launcher --check-only)
    out_contains "alias-dangling/fail" "${r%%$'\n'*}" "1"
    out_contains "alias-dangling/msg" "$r" "悬空或无法解析"
}

test_pre_spawn_failure_releases_lock() {
    # 获锁后、spawn 前失败（配置播种写入被拒）：退出非零且不残留锁。
    local w f pkg st
    w="$WORK_ROOT/t38"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    mkdir -p "$pkg/BepInEx/config"
    chmod 555 "$pkg/BepInEx/config"
    PATH="$(ps_mock_setup):$PATH" "$CUR_LAUNCHER" > "$w/out.txt" 2>&1
    st=$?
    chmod 755 "$pkg/BepInEx/config"
    if [ "$st" -ne 0 ]; then ok "pre-spawn/nonzero"; else bad "pre-spawn/nonzero（意外成功）"; fi
    if [ -e "$pkg/.launcher.lock" ]; then bad "pre-spawn/lock-released"; else ok "pre-spawn/lock-released"; fi
    out_contains "pre-spawn/msg" "$(cat "$w/out.txt")" "无法写入默认配置"
}

test_real_spawn_failure_releases_lock() {
    # 正式语义、无替身：stub 可执行文件无法被内核执行 → 清理路径仍释放锁并创建别名。
    local w f pkg st
    w="$WORK_ROOT/t36"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    PATH="$(ps_mock_setup):$PATH" "$CUR_LAUNCHER" > "$w/out.txt" 2>&1
    st=$?
    if [ "$st" -ne 0 ]; then ok "spawn-fail/nonzero"; else bad "spawn-fail/nonzero（意外成功）"; fi
    if [ -e "$pkg/.launcher.lock" ]; then bad "spawn-fail/lock-released"; else ok "spawn-fail/lock-released"; fi
    [ -L "$w/KingdomTwoCrowns_Data" ] && ok "spawn-fail/alias-created" || bad "spawn-fail/alias-created"
}

test_unwritable_package() {
    local w f pkg r
    w="$WORK_ROOT/t34"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    chmod 555 "$pkg"
    r=$(run_launcher --check-only)
    chmod 755 "$pkg"
    out_contains "unwritable/fail" "${r%%$'\n'*}" "1"
    out_contains "unwritable/msg" "$r" "不可写"
}

# ---------------- 启动器：原生库下载隔离（quarantine）与显式信任 ----------------

quarantine_set() { # $1...=夹具文件；打真实下载隔离属性（只用于合成夹具）
    local f
    for f in "$@"; do
        xattr -w com.apple.quarantine "0081;decafbad;Safari;" "$f" || return 1
    done
}

quarantine_has() { xattr "$1" 2>/dev/null | grep -qxF com.apple.quarantine; }

attrs_without_quarantine() { xattr "$1" 2>/dev/null | grep -vxF com.apple.quarantine | sort; }

mock_xattr_prepare() { # $1=工作目录；设置 XMOCK_BIN/XMOCK_LOG/XMOCK_STATE
    XMOCK_BIN="$1/mockxattr"
    XMOCK_LOG="$1/xattr-calls.log"
    XMOCK_STATE="$1/xattr-state"
    make_mock_xattr "$XMOCK_BIN" "$XMOCK_LOG" "$XMOCK_STATE"
}

mock_xattr_quarantine() { # $1...=文件；写进替身状态（不碰真实属性）
    local f
    for f in "$@"; do printf '%s\n' "$f" >> "$XMOCK_STATE/quarantined"; done
}

xmock_run() { # $1=stdin 文本；其余=启动器参数；PATH 前置替身 xattr；回显 "status\noutput"
    local input="$1" st out
    shift
    # 只在合成夹具的额外脚本副本替换固定依赖，生产源码没有环境/CLI注入入口。
    local mock_launcher="${CUR_LAUNCHER}.mock.command"
    "$PY" - "$CUR_LAUNCHER" "$mock_launcher" "$XMOCK_BIN/xattr" <<'PYMOCK'
from pathlib import Path
import sys, shlex
src, dst, tool = sys.argv[1:]
s = Path(src).read_text()
assert s.count("XATTR_BIN=/usr/bin/xattr") == 1
Path(dst).write_text(s.replace("XATTR_BIN=/usr/bin/xattr", "XATTR_BIN=" + shlex.quote(tool)))
Path(dst).chmod(0o755)
PYMOCK
    out=$(printf '%s' "$input" | OHMYMODS_MOCK_XATTR_LOG="$XMOCK_LOG" \
        OHMYMODS_MOCK_XATTR_STATE="$XMOCK_STATE" \
        PATH="$(ps_mock_setup):$XMOCK_BIN:$PATH" "$mock_launcher" "$@" 2>&1)
    st=$?
    printf '%s\n%s\n' "$st" "$out"
}

xmock_deletes() { grep '^CALL|-d|' "$XMOCK_LOG" 2>/dev/null; }
xmock_lists() { grep '^CALL|/' "$XMOCK_LOG" 2>/dev/null; }

test_trust_native_list_drift() { # 名单防漂移：launcher 固定名单 == input-lock 的 *.dylib 集合
    local w f pkg list lock_list missing rel
    w="$WORK_ROOT/t40"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    cat > "$w/drift.py" <<'PYEOF'
import json, os, re, sys
mode = sys.argv[1]
if mode == "launcher":
    text = open(sys.argv[2], encoding="utf-8").read()
    m = re.search(r'^NATIVE_TRUST_RELS="(.*?)"$', text, re.M | re.S)
    if not m:
        sys.exit(1)
    print("\n".join(l for l in m.group(1).splitlines() if l))
elif mode == "lock":
    lock = json.load(open(sys.argv[2], encoding="utf-8"))
    print("\n".join(sorted(e["path"] for e in lock["files"] if e["path"].endswith(".dylib"))))
else:
    lock = json.load(open(sys.argv[2], encoding="utf-8"))
    print("|".join(e["path"] for e in lock["files"]
                   if e["path"].endswith(".dylib") and e.get("source") != "input-root"))
PYEOF
    list=$("$PY" "$w/drift.py" launcher "$LAUNCHER")
    lock_list=$("$PY" "$w/drift.py" lock "$PKG_DIR/input-lock.json")
    [ "$(printf '%s\n' "$list" | grep -c .)" -eq 14 ] && ok "drift/launcher-count-14" \
        || bad "drift/launcher-count-14（$(printf '%s\n' "$list" | grep -c .)）"
    if [ "$(printf '%s\n' "$list" | sort)" = "$lock_list" ]; then
        ok "drift/launcher-equals-input-lock-native-set"
    else
        bad "drift/launcher-equals-input-lock-native-set"
    fi
    [ "$("$PY" "$w/drift.py" source "$PKG_DIR/input-lock.json")" = "" ] \
        && ok "drift/lock-natives-input-root" || bad "drift/lock-natives-input-root"
    missing=""
    while IFS= read -r rel; do
        [ -n "$rel" ] || continue
        [ -f "$pkg/$rel" ] || missing="${missing} ${rel}"
    done <<EOF
$list
EOF
    [ -z "$missing" ] && ok "drift/fixture-covers-list" || bad "drift/fixture-covers-list（缺:${missing}）"
}

test_quarantine_blocks_normal_and_checkonly() {
    local w f pkg mock_bin mock_out st r
    w="$WORK_ROOT/t41"; f=$(new_fixture "$w")
    pkg=$(phys "$(printf '%s' "$f" | jget pkg)")
    CUR_LAUNCHER="$pkg/launcher.command"
    quarantine_set "$pkg/dotnet/libcoreclr.dylib" "$pkg/libdoorstop.dylib"
    mock_out="$w/mock-arch-out.txt"; mock_bin="$w/mockbin"
    make_mock_arch "$mock_bin" "$mock_out"

    r=$(run_launcher --check-only)
    out_contains "quarantine/checkonly-nonzero" "${r%%$'\n'*}" "1"
    out_contains "quarantine/checkonly-msg" "$r" "--trust-package"
    out_contains "quarantine/checkonly-file" "$r" "$pkg/dotnet/libcoreclr.dylib"
    quarantine_has "$pkg/dotnet/libcoreclr.dylib" && quarantine_has "$pkg/libdoorstop.dylib" \
        && ok "quarantine/checkonly-kept-attrs" || bad "quarantine/checkonly-kept-attrs"

    # 普通启动：在任何写之前拒绝（无 arch 调用、无 config/cache、无别名、无锁）
    PATH="$(ps_mock_setup):$mock_bin:$PATH" OHMYMODS_MOCK_ARCH_OUT="$mock_out" \
        "$CUR_LAUNCHER" > "$w/out.txt" 2>&1
    st=$?
    [ "$st" -ne 0 ] && ok "quarantine/launch-nonzero" || bad "quarantine/launch-nonzero（意外成功）"
    out_contains "quarantine/launch-msg" "$(cat "$w/out.txt")" "下载隔离属性"
    [ -s "$mock_out" ] && bad "quarantine/no-arch-call" || ok "quarantine/no-arch-call"
    [ -e "$pkg/BepInEx/config" ] && bad "quarantine/no-config-seed" || ok "quarantine/no-config-seed"
    [ -e "$pkg/BepInEx/cache" ] && bad "quarantine/no-cache" || ok "quarantine/no-cache"
    [ -e "$w/KingdomTwoCrowns_Data" ] || [ -L "$w/KingdomTwoCrowns_Data" ] \
        && bad "quarantine/no-alias" || ok "quarantine/no-alias"
    [ -e "$pkg/.launcher.lock" ] && bad "quarantine/no-lock" || ok "quarantine/no-lock"
    quarantine_has "$pkg/dotnet/libcoreclr.dylib" && ok "quarantine/launch-kept-attrs" \
        || bad "quarantine/launch-kept-attrs"

    # 环境变量不能当信任开关（无 env 绕过）
    PATH="$(ps_mock_setup):$mock_bin:$PATH" OHMYMODS_MOCK_ARCH_OUT="$mock_out" \
        TRUST_PACKAGE=1 OHMYMODS_TRUST=1 "$CUR_LAUNCHER" > "$w/out2.txt" 2>&1
    st=$?
    [ "$st" -ne 0 ] && ok "quarantine/env-no-bypass" || bad "quarantine/env-no-bypass"
    [ -s "$mock_out" ] && bad "quarantine/env-no-spawn" || ok "quarantine/env-no-spawn"
}

test_quarantine_suggested_command_quoting() {
    local w f pkg r cmd st
    w="$WORK_ROOT/t42 with space/子目录"; f=$(new_fixture "$w")
    pkg=$(phys "$(printf '%s' "$f" | jget pkg)")
    CUR_LAUNCHER="$pkg/launcher.command"
    quarantine_set "$pkg/dotnet/libclrjit.dylib"
    r=$(run_launcher --check-only)
    cmd=$(printf '%s\n' "$r" | LC_ALL=C sed -n 's/^  \(.*--trust-package\)$/\1/p' | head -n 1)
    if [ -z "$cmd" ]; then
        bad "quoting/cmd-printed"
        bad "quoting/cmd-shape"
        bad "quoting/cmd-runs"
        bad "quoting/cmd-cleaned"
        return
    fi
    ok "quoting/cmd-printed"
    case "$cmd" in
        "'$pkg/launcher.command' --trust-package") ok "quoting/cmd-shape" ;;
        *) bad "quoting/cmd-shape（实际命令: $cmd）" ;;
    esac
    # 打印出来的命令必须真的可执行（含空格路径按 shell 引号处理后一次跑通）
    printf 'TRUST\n' | env PATH="$(ps_mock_setup):$PATH" bash -c "$cmd" >/dev/null 2>&1
    st=$?
    [ "$st" -eq 0 ] && ok "quoting/cmd-runs" || bad "quoting/cmd-runs（退出码 ${st}）"
    quarantine_has "$pkg/dotnet/libclrjit.dylib" && bad "quoting/cmd-cleaned" \
        || ok "quoting/cmd-cleaned"
}

test_trust_args_rules() {
    local w f pkg app r
    w="$WORK_ROOT/t43"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    app=$(printf '%s' "$f" | jget app)
    CUR_LAUNCHER="$pkg/launcher.command"
    r=$(run_launcher --trust-package --trust-package)
    out_contains "trust-args/dup-nonzero" "${r%%$'\n'*}" "1"
    out_contains "trust-args/dup-msg" "$r" "只能在同一命令行中指定一次"
    r=$(run_launcher --trust-package --check-only)
    out_contains "trust-args/mutex-checkonly" "${r%%$'\n'*}" "1"
    out_contains "trust-args/mutex-checkonly-msg" "$r" "互斥"
    r=$(run_launcher --check-only --trust-package)
    out_contains "trust-args/mutex-reversed" "${r%%$'\n'*}" "1"
    r=$(run_launcher --trust-package -width 800)
    out_contains "trust-args/game-args-nonzero" "${r%%$'\n'*}" "1"
    out_contains "trust-args/game-args-msg" "$r" "不接受游戏参数"
    r=$(run_launcher --trust-package --yes)
    out_contains "trust-args/no-yes-flag" "${r%%$'\n'*}" "1"

    # --game 允许：走完整预检后报告“无可处理文件”（替身 xattr 观察调用）
    mock_xattr_prepare "$w"
    r=$(xmock_run "" --game "$app" --trust-package)
    out_contains "trust-args/game-allowed" "${r%%$'\n'*}" "0"
    out_contains "trust-args/game-allowed-noop" "$r" "无可处理文件"
    [ -z "$(xmock_deletes)" ] && ok "trust-args/game-allowed-no-delete" \
        || bad "trust-args/game-allowed-no-delete"
}

test_trust_cancel_and_eof_no_writes() {
    local w f pkg scenario input r
    for scenario in eof empty wrong lower; do
        case "$scenario" in
            eof) input="" ;;
            empty) input="
" ;;
            wrong) input="yes
" ;;
            lower) input="trust
" ;;
        esac
        w="$WORK_ROOT/t44-$scenario"; f=$(new_fixture "$w")
        pkg=$(phys "$(printf '%s' "$f" | jget pkg)")
        CUR_LAUNCHER="$pkg/launcher.command"
        mock_xattr_prepare "$w"
        mock_xattr_quarantine "$pkg/dotnet/libcoreclr.dylib"
        r=$(xmock_run "$input" --trust-package)
        out_contains "trust-cancel/$scenario-nonzero" "${r%%$'\n'*}" "1"
        out_contains "trust-cancel/$scenario-msg" "$r" "已取消"
        [ -z "$(xmock_deletes)" ] && ok "trust-cancel/$scenario-no-delete" \
            || bad "trust-cancel/$scenario-no-delete"
        grep -qxF "$pkg/dotnet/libcoreclr.dylib" "$XMOCK_STATE/quarantined" \
            && ok "trust-cancel/$scenario-state-kept" || bad "trust-cancel/$scenario-state-kept"
        [ -e "$pkg/.launcher.lock" ] && bad "trust-cancel/$scenario-no-lock" \
            || ok "trust-cancel/$scenario-no-lock"
        [ -e "$w/KingdomTwoCrowns_Data" ] || [ -L "$w/KingdomTwoCrowns_Data" ] \
            && bad "trust-cancel/$scenario-no-alias" || ok "trust-cancel/$scenario-no-alias"
        [ -e "$pkg/BepInEx/config" ] && bad "trust-cancel/$scenario-no-cfg" \
            || ok "trust-cancel/$scenario-no-cfg"
    done
}

test_trust_cleans_only_listed_targets() {
    local w f pkg app r got want
    w="$WORK_ROOT/t45"; f=$(new_fixture "$w")
    pkg=$(phys "$(printf '%s' "$f" | jget pkg)")
    app=$(phys "$(printf '%s' "$f" | jget app)")
    CUR_LAUNCHER="$pkg/launcher.command"
    mock_xattr_prepare "$w"
    mock_xattr_quarantine "$pkg/dotnet/libcoreclr.dylib" "$pkg/BepInEx/core/libdobby.dylib"
    # 名单外（游戏文件/插件/配置/未知新增 dylib）即使“带隔离”也不得被列举或清除
    mock_xattr_quarantine "$app/Contents/Frameworks/GameAssembly.dylib" \
        "$pkg/BepInEx/core/BepInEx.Unity.IL2CPP.dll" "$pkg/defaults/BepInEx.cfg" \
        "$pkg/unknown-extra.dylib"
    r=$(xmock_run "TRUST
" --trust-package)
    out_contains "trust-only/exit0" "${r%%$'\n'*}" "0"
    out_contains "trust-only/summary" "$r" "已清除隔离属性: 2 个"
    got=$(xmock_deletes | sed 's/^CALL|-d|com.apple.quarantine|//' | sort)
    want=$(printf '%s\n' "$pkg/BepInEx/core/libdobby.dylib" "$pkg/dotnet/libcoreclr.dylib" | sort)
    [ "$got" = "$want" ] && ok "trust-only/exact-targets" || bad "trust-only/exact-targets"
    # 严格 argv：只允许 -d（无 -r/-c/-w/-x 等），且属性名精确为 com.apple.quarantine
    [ "$(grep -E '^CALL\|-' "$XMOCK_LOG" | grep -vc '^CALL|-d|com.apple.quarantine|')" = "0" ] \
        && ok "trust-only/strict-argv" || bad "trust-only/strict-argv"
    # 只列举固定名单（14 个），名单外路径一次都不碰
    got=$(xmock_lists | sed 's/^CALL|//' | sort -u)
    want=$(printf '%s\n' "$pkg/libdoorstop.dylib" "$pkg/BepInEx/core/libdobby.dylib" \
        "$pkg/dotnet/libSystem.Globalization.Native.dylib" \
        "$pkg/dotnet/libSystem.IO.Compression.Native.dylib" \
        "$pkg/dotnet/libSystem.Native.dylib" \
        "$pkg/dotnet/libSystem.Net.Security.Native.dylib" \
        "$pkg/dotnet/libSystem.Security.Cryptography.Native.Apple.dylib" \
        "$pkg/dotnet/libSystem.Security.Cryptography.Native.OpenSsl.dylib" \
        "$pkg/dotnet/libclrjit.dylib" "$pkg/dotnet/libcoreclr.dylib" \
        "$pkg/dotnet/libdbgshim.dylib" "$pkg/dotnet/libhostpolicy.dylib" \
        "$pkg/dotnet/libmscordaccore.dylib" "$pkg/dotnet/libmscordbi.dylib" | sort)
    [ "$got" = "$want" ] && ok "trust-only/only-listed-paths-probed" \
        || bad "trust-only/only-listed-paths-probed"
    for rel in "unknown-extra.dylib" "BepInEx/core/BepInEx.Unity.IL2CPP.dll" "defaults/BepInEx.cfg"; do
        grep -qxF "$pkg/$rel" "$XMOCK_STATE/quarantined" && ok "trust-only/unknown-kept-$(basename "$rel")" \
            || bad "trust-only/unknown-kept-$(basename "$rel")"
    done
    grep -qxF "$app/Contents/Frameworks/GameAssembly.dylib" "$XMOCK_STATE/quarantined" \
        && ok "trust-only/game-kept" || bad "trust-only/game-kept"
    [ -e "$pkg/.launcher.lock" ] && bad "trust-only/lock-released" || ok "trust-only/lock-released"
    [ -e "$w/KingdomTwoCrowns_Data" ] || [ -L "$w/KingdomTwoCrowns_Data" ] \
        && bad "trust-only/no-alias" || ok "trust-only/no-alias"
}

test_trust_real_xattr_semantics() {
    local w f pkg app r before_attrs after_attrs rel
    w="$WORK_ROOT/t46"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    app=$(printf '%s' "$f" | jget app)
    CUR_LAUNCHER="$pkg/launcher.command"
    quarantine_set "$pkg/dotnet/libcoreclr.dylib" "$pkg/BepInEx/core/libdobby.dylib"
    printf 'unknown' > "$pkg/unknown-extra.dylib"
    quarantine_set "$app/Contents/Frameworks/GameAssembly.dylib" "$pkg/unknown-extra.dylib"
    xattr -w user.ohmymods.sentinel keep "$pkg/dotnet/libcoreclr.dylib"
    cp "$pkg/dotnet/libcoreclr.dylib" "$w/before.bin"
    before_attrs=$(attrs_without_quarantine "$pkg/dotnet/libcoreclr.dylib")

    r=$(run_launcher_input "TRUST
" --trust-package)
    out_contains "trust-real/exit0" "${r%%$'\n'*}" "0"
    quarantine_has "$pkg/dotnet/libcoreclr.dylib" && bad "trust-real/cleaned-coreclr" \
        || ok "trust-real/cleaned-coreclr"
    quarantine_has "$pkg/BepInEx/core/libdobby.dylib" && bad "trust-real/cleaned-dobby" \
        || ok "trust-real/cleaned-dobby"
    after_attrs=$(attrs_without_quarantine "$pkg/dotnet/libcoreclr.dylib")
    [ "$before_attrs" = "$after_attrs" ] && ok "trust-real/other-attrs-preserved" \
        || bad "trust-real/other-attrs-preserved（${before_attrs} → ${after_attrs}）"
    attrs_without_quarantine "$pkg/dotnet/libcoreclr.dylib" | grep -qxF "user.ohmymods.sentinel" \
        && ok "trust-real/custom-attr-kept" || bad "trust-real/custom-attr-kept"
    cmp -s "$pkg/dotnet/libcoreclr.dylib" "$w/before.bin" && ok "trust-real/bytes-unchanged" \
        || bad "trust-real/bytes-unchanged"
    # 名单外文件与游戏文件绝不被清除
    quarantine_has "$app/Contents/Frameworks/GameAssembly.dylib" && ok "trust-real/game-kept" \
        || bad "trust-real/game-kept"
    quarantine_has "$pkg/unknown-extra.dylib" && ok "trust-real/unknown-kept" \
        || bad "trust-real/unknown-kept"
    for rel in libdoorstop.dylib dotnet/libclrjit.dylib dotnet/libmscordbi.dylib; do
        quarantine_has "$pkg/$rel" && bad "trust-real/other-native-touched" \
            || ok "trust-real/other-native-untouched"
    done
    [ -e "$pkg/.launcher.lock" ] && bad "trust-real/lock-released" || ok "trust-real/lock-released"

    # 重入：已全部清洁时无事可做、exit 0、不调用任何删除
    mock_xattr_prepare "$w"
    r=$(xmock_run "" --trust-package)
    out_contains "trust-real/rerun-noop" "${r%%$'\n'*}" "0"
    out_contains "trust-real/rerun-msg" "$r" "无可处理文件"
    [ -z "$(xmock_deletes)" ] && ok "trust-real/rerun-no-delete" || bad "trust-real/rerun-no-delete"
}

test_trust_partial_failure_and_rerun() {
    local w f pkg r got
    w="$WORK_ROOT/t47"; f=$(new_fixture "$w")
    pkg=$(phys "$(printf '%s' "$f" | jget pkg)")
    CUR_LAUNCHER="$pkg/launcher.command"
    quarantine_set "$pkg/dotnet/libcoreclr.dylib" "$pkg/dotnet/libclrjit.dylib"
    chmod 444 "$pkg/dotnet/libclrjit.dylib"   # 真实失败注入：无写权限 → xattr -d 失败
    r=$(run_launcher_input "TRUST
" --trust-package)
    chmod 644 "$pkg/dotnet/libclrjit.dylib"
    out_contains "trust-partial/nonzero" "${r%%$'\n'*}" "1"
    out_contains "trust-partial/report" "$r" "清除失败"
    quarantine_has "$pkg/dotnet/libcoreclr.dylib" && bad "trust-partial/clean-one" \
        || ok "trust-partial/clean-one"
    quarantine_has "$pkg/dotnet/libclrjit.dylib" && ok "trust-partial/still-quarantined" \
        || bad "trust-partial/still-quarantined"
    out_contains "trust-partial/retry-hint" "$r" "--trust-package 命令重试"
    [ -e "$pkg/.launcher.lock" ] && bad "trust-partial/lock-released" \
        || ok "trust-partial/lock-released"

    # 重入：只处理仍被隔离者（替身状态只剩 clrjit；coreclr 已清洁不得再被删除）
    mock_xattr_prepare "$w"
    mock_xattr_quarantine "$pkg/dotnet/libclrjit.dylib"
    r=$(xmock_run "TRUST
" --trust-package)
    out_contains "trust-partial/rerun-exit0" "${r%%$'\n'*}" "0"
    got=$(xmock_deletes | sed 's/^CALL|-d|com.apple.quarantine|//')
    [ "$got" = "$pkg/dotnet/libclrjit.dylib" ] && ok "trust-partial/rerun-only-remaining" \
        || bad "trust-partial/rerun-only-remaining"
}

test_trust_read_error_failclosed() {
    local w f pkg r
    w="$WORK_ROOT/t48"; f=$(new_fixture "$w")
    pkg=$(phys "$(printf '%s' "$f" | jget pkg)")
    CUR_LAUNCHER="$pkg/launcher.command"
    mock_xattr_prepare "$w"
    mock_xattr_quarantine "$pkg/dotnet/libcoreclr.dylib"
    # 列举失败：读取失败 ≠ 无属性；普通启动/check-only/--trust-package 全部拒绝且不删除
    r=$(OHMYMODS_MOCK_XATTR_FAIL_LIST=all xmock_run "" --check-only)
    out_contains "trust-readerr/checkonly-nonzero" "${r%%$'\n'*}" "1"
    out_contains "trust-readerr/checkonly-msg" "$r" "无法读取扩展属性"
    r=$(OHMYMODS_MOCK_XATTR_FAIL_LIST=all xmock_run "" --trust-package)
    out_contains "trust-readerr/trust-nonzero" "${r%%$'\n'*}" "1"
    out_contains "trust-readerr/trust-msg" "$r" "无法读取扩展属性"
    [ -z "$(xmock_deletes)" ] && ok "trust-readerr/no-delete" || bad "trust-readerr/no-delete"
    grep -qxF "$pkg/dotnet/libcoreclr.dylib" "$XMOCK_STATE/quarantined" \
        && ok "trust-readerr/state-kept" || bad "trust-readerr/state-kept"
    # 删除返回 0 但属性仍在：读回必须发现并如实报告（不得标记成功）
    r=$(OHMYMODS_MOCK_XATTR_NOOP_DELETE=all xmock_run "TRUST
" --trust-package)
    out_contains "trust-readerr/noop-nonzero" "${r%%$'\n'*}" "1"
    out_contains "trust-readerr/noop-readback" "$r" "清除后读回仍带隔离属性"
}

test_trust_review_regressions() {
    local w f pkg r input
    w="$WORK_ROOT/review-trust"; f=$(new_fixture "$w")
    pkg=$(phys "$(printf '%s' "$f" | jget pkg)")
    CUR_LAUNCHER="$pkg/launcher.command"
    mock_xattr_prepare "$w"
    mock_xattr_quarantine "$pkg/libdoorstop.dylib" "$pkg/BepInEx/core/libdobby.dylib"
    for input in $' TRUST\n' $'TRUST \n' $'TRUST\r\n'; do
        r=$(xmock_run "$input" --trust-package)
        [ "${r%%$'\n'*}" = 1 ] && ok "trust-exact/nonexact-rejected" || bad "trust-exact/nonexact-rejected"
        [ -z "$(xmock_deletes)" ] && ok "trust-exact/no-deletion" || bad "trust-exact/no-deletion"
        [ ! -e "$pkg/.launcher.lock" ] && ok "trust-exact/no-lock" || bad "trust-exact/no-lock"
    done
    r=$(OHMYMODS_MOCK_XATTR_FAIL_LIST_AFTER_DELETE=all xmock_run $'TRUST\n' --trust-package)
    [ "${r%%$'\n'*}" = 1 ] && ok "trust-readback/nonzero" || bad "trust-readback/nonzero"
    out_contains "trust-readback/deletion-receipt" "$r" "删除命令返回成功: libdoorstop.dylib"
    out_contains "trust-readback/unverified" "$r" "删除后读取失败，是否清除尚未确认"
    out_contains "trust-readback/later-unprocessed" "$r" "读取失败，未处理且状态未确认"
    out_lacks "trust-readback/no-success" "$r" "全部完成"
    out_contains "trust-readback/verified-zero" "$r" "已清除隔离属性: 0 个"
    [ ! -e "$pkg/.launcher.lock" ] && ok "trust-readback/lock-released" || bad "trust-readback/lock-released"
    [ "$(xmock_deletes | wc -l | tr -d ' ')" = 1 ] && ok "trust-readback/only-first-delete" || bad "trust-readback/only-first-delete"
    grep -qxF "$pkg/BepInEx/core/libdobby.dylib" "$XMOCK_STATE/quarantined" && ok "trust-readback/remaining-kept" || bad "trust-readback/remaining-kept"
    # 真实生产启动器即便PATH中有xattr替身，也必须使用/usr/bin/xattr。
    r=$(OHMYMODS_MOCK_XATTR_FAIL_LIST=all OHMYMODS_MOCK_XATTR_LOG="$XMOCK_LOG" OHMYMODS_MOCK_XATTR_STATE="$XMOCK_STATE" PATH="$(ps_mock_setup):$XMOCK_BIN:$PATH" "$CUR_LAUNCHER" --check-only 2>&1)
    [ "$?" = 0 ] && ok "trust-xattr/production-fixed-tool" || bad "trust-xattr/production-fixed-tool"
}

test_trust_symlink_and_hardlink_refusals() {
    local w f pkg app r st
    w="$WORK_ROOT/t49"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    # (a) 文件符号链接：SHA 清单会跟随链接通过，必须由原生库检查拒绝
    mv "$pkg/dotnet/libcoreclr.dylib" "$w/coreclr-real.dylib"
    ln -s "$w/coreclr-real.dylib" "$pkg/dotnet/libcoreclr.dylib"
    r=$(run_launcher --check-only)
    out_contains "trust-link/file-nonzero" "${r%%$'\n'*}" "1"
    out_contains "trust-link/file-msg" "$r" "符号链接"

    w="$WORK_ROOT/t49b"; f=$(new_fixture "$w")
    pkg=$(printf '%s' "$f" | jget pkg)
    CUR_LAUNCHER="$pkg/launcher.command"
    mkdir -p "$w/external-native"
    cp "$pkg/dotnet/"*.dylib "$w/external-native/"
    rm -rf "$pkg/dotnet"
    ln -s "$w/external-native" "$pkg/dotnet"
    r=$(run_launcher --check-only)
    out_contains "trust-link/parent-nonzero" "${r%%$'\n'*}" "1"
    out_contains "trust-link/parent-msg" "$r" "父路径是符号链接"

    # (b) 硬链接：xattr 属 inode，清属性会越界改动包外；trust 必须拒绝且不删除
    w="$WORK_ROOT/t49c"; f=$(new_fixture "$w")
    pkg=$(phys "$(printf '%s' "$f" | jget pkg)")
    CUR_LAUNCHER="$pkg/launcher.command"
    mock_xattr_prepare "$w"
    mock_xattr_quarantine "$pkg/dotnet/libcoreclr.dylib" "$pkg/libdoorstop.dylib"
    ln "$pkg/dotnet/libcoreclr.dylib" "$w/hardlink-outside.dylib"
    ln "$pkg/libdoorstop.dylib" "$w/hardlink-outside-2.dylib"
    r=$(xmock_run "TRUST
" --trust-package)
    out_contains "trust-link/hardlink-nonzero" "${r%%$'\n'*}" "1"
    out_contains "trust-link/hardlink-msg" "$r" "硬链接"
    [ -z "$(xmock_deletes)" ] && ok "trust-link/hardlink-no-delete" || bad "trust-link/hardlink-no-delete"
    grep -qxF "$pkg/dotnet/libcoreclr.dylib" "$XMOCK_STATE/quarantined" \
        && ok "trust-link/hardlink-state-kept" || bad "trust-link/hardlink-state-kept"
    [ -e "$pkg/.launcher.lock" ] && bad "trust-link/hardlink-no-lock" \
        || ok "trust-link/hardlink-no-lock"
    rm -f "$w/hardlink-outside.dylib" "$w/hardlink-outside-2.dylib"

    # (c) 硬链接在确认前就被拒绝：EOF 输入也必须失败在硬链接门（不消耗确认）
    w="$WORK_ROOT/t49d"; f=$(new_fixture "$w")
    pkg=$(phys "$(printf '%s' "$f" | jget pkg)")
    CUR_LAUNCHER="$pkg/launcher.command"
    mock_xattr_prepare "$w"
    mock_xattr_quarantine "$pkg/dotnet/libcoreclr.dylib"
    ln "$pkg/dotnet/libcoreclr.dylib" "$w/hardlink-2.dylib"
    r=$(xmock_run "" --trust-package)
    out_contains "trust-link/hardlink-eof-nonzero" "${r%%$'\n'*}" "1"
    out_contains "trust-link/hardlink-eof-msg" "$r" "硬链接"
    rm -f "$w/hardlink-2.dylib"
}


# ---------------- 构建器（import 驱动 + 家目录副本） ----------------

test_builder_zip_content_and_determinism() {
    local w="$WORK_ROOT/t21"
    setup_builder_fixture "$w"
    [ "$B_FILES" = "16" ] && ok "zip/inventory-count" || bad "zip/inventory-count（${B_FILES}）"
    builder_env "$BH/build_package.py" "$B_SHA" --input-root "$B_INPUT" \
        --notices-source "$B_NOTICES" --game-app "$B_GAME" --lock "$B_LOCK" \
        --output "$w/a.zip" >/dev/null 2>&1 \
        && ok "zip/build-ok" || bad "zip/build-ok"
    builder_env "$BH/build_package.py" "$B_SHA" --input-root "$B_INPUT" \
        --notices-source "$B_NOTICES" --game-app "$B_GAME" --lock "$B_LOCK" \
        --output "$w/b.zip" >/dev/null 2>&1 \
        && ok "zip/build2-ok" || bad "zip/build2-ok"
    cmp -s "$w/a.zip" "$w/b.zip" && ok "zip/deterministic" || bad "zip/deterministic"

    if "$PY" - "$w/a.zip" "$B_GAME" <<'PYEOF'
import hashlib, json, sys, zipfile
zf = zipfile.ZipFile(sys.argv[1])
names = zf.namelist()
root = "OhMyMods-Mac-ARM64/"
assert names == sorted(names), "条目未排序"
assert all(n.startswith(root) for n in names), "存在根目录外条目"
for zi in zf.infolist():
    assert zi.date_time == (2020, 1, 1, 0, 0, 0), "时间戳未固定: " + zi.filename
    mode = (zi.external_attr >> 16) & 0o777
    expect = 0o755 if zi.filename == root + "launcher.command" else 0o644
    assert mode == expect, "权限错误 %s: %o" % (zi.filename, mode)
banned = ("config", "cache", "interop", "unity-libs", "LogOutput", "run_bepinex", "KingdomTwoCrowns.app")
for n in names:
    for b in banned:
        assert b not in n, "禁止条目 %s 含 %s" % (n, b)
need = [root + "game-lock.json", root + "package-manifest.json", root + "SHA256SUMS",
        root + "launcher.command", root + "README.md", root + "defaults/BepInEx.cfg",
        root + "third-party/Dobby-LICENSE", root + "REBUILD.md", root + "VALIDATION.md",
        root + "tools/sanitize_codeview.py",
        root + "metadata-sanitization-receipts/receipt.json",
        root + "BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll",
        root + "libdoorstop.dylib", root + "dotnet/libcoreclr.dylib"]
for n in need:
    assert n in names, "缺少 " + n
gl = json.loads(zf.read(root + "game-lock.json").decode("utf-8"))
assert "allow_test_overrides" not in gl, "game-lock 泄漏测试通道"
assert set(gl) == {"schema", "assembly_sha256", "executable_sha256", "metadata_sha256",
                   "infoplist_sha256", "game_build", "unity_version", "source_tag"}
game = sys.argv[2]
def sha_file(p):
    return hashlib.sha256(open(p, "rb").read()).hexdigest()
assert gl["assembly_sha256"] == sha_file(game + "/Contents/Frameworks/GameAssembly.dylib")
assert gl["executable_sha256"] == sha_file(game + "/Contents/MacOS/KingdomTwoCrowns")
assert gl["metadata_sha256"] == sha_file(game + "/Contents/Resources/Data/il2cpp_data/Metadata/global-metadata.dat")
assert gl["infoplist_sha256"] == sha_file(game + "/Contents/Info.plist")
pm = json.loads(zf.read(root + "package-manifest.json").decode("utf-8"))
assert pm["package"]["framework_archive"] == "9027335"
assert pm["game"]["source_tag"] == "1088b9c"
assert pm["generated"] == ["SHA256SUMS", "game-lock.json", "package-manifest.json"]
assert len(pm["payload"]) == 16, "payload 条数 %d" % len(pm["payload"])
for e in pm["payload"]:
    assert set(e) == {"path", "sha256", "size", "mode", "source"}, "payload 字段: %r" % e
    data = zf.read(root + e["path"])
    assert len(data) == e["size"], "size 不符: " + e["path"]
    assert hashlib.sha256(data).hexdigest() == e["sha256"], "manifest sha 不符: " + e["path"]
# SHA256SUMS：逐行对 zip 内实际内容核验，并覆盖两个生成 JSON
sums = zf.read(root + "SHA256SUMS").decode("utf-8").splitlines()
listed = []
for line in sums:
    digest, rel = line.split("  ", 1)
    listed.append(rel)
    assert hashlib.sha256(zf.read(root + rel)).hexdigest() == digest, "SUMS 不符: " + rel
assert "game-lock.json" in listed and "package-manifest.json" in listed
assert listed == sorted(listed)
assert not any(l.startswith(("BepInEx/config", "BepInEx/cache", "BepInEx/interop",
                             "BepInEx/unity-libs")) for l in listed), "SUMS 含可变文件"
print("zip-ok")
PYEOF
    then ok "zip/content-modes-sums-manifest"; else bad "zip/content-modes-sums-manifest"; fi
}

test_builder_rejections() {
    local w="$WORK_ROOT/t22"
    setup_builder_fixture "$w"

    printf 'tamper' >> "$B_INPUT/BepInEx/core/0Harmony.dll"
    assert_fail "builder/hash-mismatch" builder_env "$BH/build_package.py" "$B_SHA" \
        --input-root "$B_INPUT" --notices-source "$B_NOTICES" --game-app "$B_GAME" \
        --lock "$B_LOCK" --output "$w/x1.zip"
    setup_builder_fixture "$WORK_ROOT/t22b"

    rm "$B_INPUT/dotnet/System.Runtime.dll"
    ln -s "$B_INPUT/BepInEx/core/0Harmony.dll" "$B_INPUT/dotnet/System.Runtime.dll"
    assert_fail "builder/symlink-input" builder_env "$BH/build_package.py" "$B_SHA" \
        --input-root "$B_INPUT" --notices-source "$B_NOTICES" --game-app "$B_GAME" \
        --lock "$B_LOCK" --output "$w/x2.zip"
    setup_builder_fixture "$WORK_ROOT/t22c"

    lock_edit "$B_LOCK" 'd["files"].append({"path":"../evil.txt","sha256":"0"*64,"mode":"0644","source":"notices"})'
    assert_fail "builder/traversal" builder_env "$BH/build_package.py" "$B_SHA" \
        --input-root "$B_INPUT" --notices-source "$B_NOTICES" --game-app "$B_GAME" \
        --lock "$B_LOCK" --output "$w/x3.zip"
    setup_builder_fixture "$WORK_ROOT/t22d"

    lock_edit "$B_LOCK" 'd["files"].append({"path":"evil\n.txt","sha256":"0"*64,"mode":"0644","source":"notices"})'
    assert_fail "builder/newline-path" builder_env "$BH/build_package.py" "$B_SHA" \
        --input-root "$B_INPUT" --notices-source "$B_NOTICES" --game-app "$B_GAME" \
        --lock "$B_LOCK" --output "$w/x4.zip"
    setup_builder_fixture "$WORK_ROOT/t22e"

    lock_edit "$B_LOCK" 'd["files"].append(dict(d["files"][0]))'
    assert_fail "builder/duplicate-path" builder_env "$BH/build_package.py" "$B_SHA" \
        --input-root "$B_INPUT" --notices-source "$B_NOTICES" --game-app "$B_GAME" \
        --lock "$B_LOCK" --output "$w/x5.zip"
    setup_builder_fixture "$WORK_ROOT/t22f"

    printf '\000\001binary' > "$B_INPUT/BepInEx/core/Sneaky.dll"
    assert_fail "builder/extra-binary" builder_env "$BH/build_package.py" "$B_SHA" \
        --input-root "$B_INPUT" --notices-source "$B_NOTICES" --game-app "$B_GAME" \
        --lock "$B_LOCK" --output "$w/x6.zip"
    setup_builder_fixture "$WORK_ROOT/t22g"

    rm "$B_INPUT/dotnet/System.Runtime.dll"
    assert_fail "builder/missing-file" builder_env "$BH/build_package.py" "$B_SHA" \
        --input-root "$B_INPUT" --notices-source "$B_NOTICES" --game-app "$B_GAME" \
        --lock "$B_LOCK" --output "$w/x7.zip"
    setup_builder_fixture "$WORK_ROOT/t22h"

    lock_edit "$B_LOCK" 'd["files"][0]["sha256"]="<OPERATOR填入>"'
    assert_fail "builder/placeholder" builder_env "$BH/build_package.py" "$B_SHA" \
        --input-root "$B_INPUT" --notices-source "$B_NOTICES" --game-app "$B_GAME" \
        --lock "$B_LOCK" --output "$w/x8.zip"
    setup_builder_fixture "$WORK_ROOT/t22i"

    printf 'X' >> "$B_GAME/Contents/Info.plist"
    assert_fail "builder/infoplist-mismatch" builder_env "$BH/build_package.py" "$B_SHA" \
        --input-root "$B_INPUT" --notices-source "$B_NOTICES" --game-app "$B_GAME" \
        --lock "$B_LOCK" --output "$w/x9.zip"
    setup_builder_fixture "$WORK_ROOT/t22j"

    printf 'X' >> "$B_GAME/Contents/Resources/Data/il2cpp_data/Metadata/global-metadata.dat"
    assert_fail "builder/metadata-mismatch" builder_env "$BH/build_package.py" "$B_SHA" \
        --input-root "$B_INPUT" --notices-source "$B_NOTICES" --game-app "$B_GAME" \
        --lock "$B_LOCK" --output "$w/x10.zip"
    setup_builder_fixture "$WORK_ROOT/t22k"

    printf 'extra' > "$B_NOTICES/third-party/EXTRA.txt"
    assert_fail "builder/notices-extra" builder_env "$BH/build_package.py" "$B_SHA" \
        --input-root "$B_INPUT" --notices-source "$B_NOTICES" --game-app "$B_GAME" \
        --lock "$B_LOCK" --output "$w/x11.zip"
    setup_builder_fixture "$WORK_ROOT/t22l"

    # 契约常量强制：不 monkeypatch（sha 传 '-'）时夹具哈希被拒
    assert_fail "builder/contract-constant" builder_env "$BH/build_package.py" "-" \
        --input-root "$B_INPUT" --notices-source "$B_NOTICES" --game-app "$B_GAME" \
        --lock "$B_LOCK" --output "$w/x12.zip"

    builder_env "$BH/build_package.py" "$B_SHA" --input-root "$B_INPUT" \
        --notices-source "$B_NOTICES" --game-app "$B_GAME" --lock "$B_LOCK" \
        --output "$w/exists.zip" >/dev/null 2>&1
    assert_fail "builder/output-exists" builder_env "$BH/build_package.py" "$B_SHA" \
        --input-root "$B_INPUT" --notices-source "$B_NOTICES" --game-app "$B_GAME" \
        --lock "$B_LOCK" --output "$w/exists.zip"
    setup_builder_fixture "$WORK_ROOT/t22m"

    lock_edit "$B_LOCK" 'd["files"].append({"path":"game-lock.json","sha256":"0"*64,"mode":"0644","source":"package-material"})'
    assert_fail "builder/reserved-generated" builder_env "$BH/build_package.py" "$B_SHA" \
        --input-root "$B_INPUT" --notices-source "$B_NOTICES" --game-app "$B_GAME" \
        --lock "$B_LOCK" --output "$w/x13.zip"
    setup_builder_fixture "$WORK_ROOT/t22n"

    lock_edit "$B_LOCK" 'd["files"]=[{"path":"REBUILD-missing.md","sha256":f["sha256"],"mode":"0644","source":"package-material"} if f["path"]=="REBUILD.md" else f for f in d["files"]]'
    assert_fail "builder/inventory-refers-missing" builder_env "$BH/build_package.py" "$B_SHA" \
        --input-root "$B_INPUT" --notices-source "$B_NOTICES" --game-app "$B_GAME" \
        --lock "$B_LOCK" --output "$w/x14.zip"
}

test_builder_emit_template() {
    local w="$WORK_ROOT/t31" n warn
    "$PY" "$FIXTURE" --root "$w/game-root" --input-root "$w/input-root" \
        --notices-dir "$w/notices" >/dev/null
    "$PY" "$BUILDER" --input-root "$w/input-root" --notices-source "$w/notices" \
        --game-app "$w/game-root/KingdomTwoCrowns.app" \
        --emit-lock-template "$w/candidate.json" >/dev/null 2>&1 \
        && ok "template/emitted" || bad "template/emitted"
    n=$("$PY" -c 'import json,sys;print(len(json.load(open(sys.argv[1]))["files"]))' "$w/candidate.json")
    paths="$("$PY" -c 'import json,sys;print(" ".join(f["path"] for f in json.load(open(sys.argv[1]))["files"]))' "$w/candidate.json")"
    case "$paths" in
        *REBUILD.md*|*tools/sanitize_codeview.py*) ok "template/operator-materials" ;;
        *) bad "template/operator-materials（未包含 REBUILD.md/tools 材料）" ;;
    esac
    case "$paths" in
        *__pycache__*|*.pyc*) bad "template/no-pyc（模板包含缓存产物）" ;;
        *) ok "template/no-pyc" ;;
    esac
    [ "$(printf '%s' "$paths" | wc -w | tr -d ' ')" -ge 12 ] \
        && ok "template/count" || bad "template/count（过少）"
    warn=$("$PY" -c 'import json,sys;print("|".join(json.load(open(sys.argv[1])).get("warnings",[])))' "$w/candidate.json")
    out_contains "template/warn-assembly" "$warn" "契约固定值不符"
    out_contains "template/warn-metadata" \
        "$("$PY" -c 'import json;print(json.dumps(json.load(open("'"$w"'/candidate.json"))["game"],ensure_ascii=False,sort_keys=True))')" \
        "metadata_sha256"
}

# ---------------- 运行 ----------------

WORK_ROOT=$(mktemp -d "${TMPDIR:-/tmp}/ohmymods-pkg-tests.XXXXXX")

if [ $# -gt 0 ]; then
    ALL_TESTS="$*"
else
    ALL_TESTS=$(declare -F | awk '{print $3}' | grep '^test_' | sort)
fi

for t in $ALL_TESTS; do
    "$t"
done
printf '\n== 结果: PASS=%d FAIL=%d ==\n' "$PASS" "$FAIL"
if [ -n "$FAILED_NAMES" ]; then
    printf '失败用例:%s\n' "$FAILED_NAMES"
    exit 1
fi
exit 0
