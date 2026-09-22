#!/usr/bin/env bash
# compat/macos-x64/verify.sh
# 编译并运行归档的四个 x86_64 探针（固定架构，任何失败以非零退出）：
#   branch-probe    回归短条件跳转（Jcc rel8 扩宽 rel32）搬移位移
#   multi-probe     8 函数 × 25 轮安装/卸载，验证原函数与恢复
#   pressure-probe  512 函数同时 Hook × 3 轮，验证近地址分配不重叠
#
# 只读 artifacts/ 之外的输入，只写 <脚本目录>/artifacts/verify/。
# 锁定 x86_64：既要求被测 libdobby.dylib 与全部探针产物为 x86_64，也要求本机可执行
# x86_64（Apple Silicon 需 Rosetta 2；Intel Mac 原生可用）。
set -euo pipefail

usage() {
  cat <<'USAGE'
用法：
  verify.sh [--dobby PATH] [--timeout SECONDS]

参数：
  --dobby PATH       被测 libdobby.dylib，默认 artifacts/libdobby.dylib（须为 x86_64）。
  --timeout SECONDS  单个探针超时秒数，默认 300。超时按失败处理（需系统 perl，用于 alarm）。
  -h, --help         显示本帮助。

退出码：0 = 四个探针全部 PASS；非 0 = 任一编译、架构校验、超时或探针断言失败。

说明：
  - 日志与探针二进制写入 artifacts/verify/，可直接作为验收证据；
  - 探针目标库（branch/multi/pressure target）由 tests/ 下的 .c/.s 现场编译为 x86_64 dylib；
  - 本脚本不修改任何源码目录，不写游戏目录，不联网。
USAGE
}

die() { printf '错误: %s\n' "$*" >&2; exit 1; }

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ARTIFACTS_DIR="$SCRIPT_DIR/artifacts"
WORK_DIR="$ARTIFACTS_DIR/verify"
TESTS_DIR="$SCRIPT_DIR/tests"
DOBBY_LIB="$ARTIFACTS_DIR/libdobby.dylib"
TIMEOUT="300"

while [ $# -gt 0 ]; do
  case "$1" in
    --dobby)   [ $# -ge 2 ] || die "--dobby 缺少取值";   DOBBY_LIB="$2"; shift 2 ;;
    --timeout) [ $# -ge 2 ] || die "--timeout 缺少取值"; TIMEOUT="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) die "未知参数: $1（用 --help 查看用法；未知输入一律拒绝）" ;;
  esac
done
case "$TIMEOUT" in ''|*[!0-9]*) die "--timeout 必须是正整数秒" ;; esac
[ "$TIMEOUT" -ge 1 ] || die "--timeout 必须 >= 1"

CLANG="$(command -v clang || true)"
[ -n "$CLANG" ] || die "需要 clang（Xcode Command Line Tools）"
[ -f "$DOBBY_LIB" ] || die "找不到被测 Dobby 库: ${DOBBY_LIB}（先运行 build.sh）"
[ -d "$TESTS_DIR" ] || die "缺少测试源码目录: $TESTS_DIR"
for src in branch-probe.c branch-target.s multi-probe.c multi-target.c pressure-probe.c pressure-target.s mixed-page-probe.c; do
  [ -f "$TESTS_DIR/$src" ] || die "缺少测试源码: tests/$src"
done

# ---- 固定 x86_64 ----
DOBBY_ARCHS="$(/usr/bin/lipo -archs "$DOBBY_LIB")"
[ "$DOBBY_ARCHS" = "x86_64" ] || die "被测库架构应为 x86_64，实际: $DOBBY_ARCHS ($DOBBY_LIB)"
for sym in DobbyPrepare DobbyCommit DobbyDestroy; do
  /usr/bin/nm -gU "$DOBBY_LIB" | grep -q " _${sym}\$" || die "被测库缺少导出符号: $sym"
done
if ! /usr/bin/arch -x86_64 /usr/bin/true >/dev/null 2>&1; then
  die "本机无法执行 x86_64 可执行文件；Apple Silicon 请安装 Rosetta 2"
fi

# ---- 输出边界：只写 artifacts/verify，且拒绝覆盖未知内容 ----
[ ! -L "$ARTIFACTS_DIR" ] || die "拒绝操作符号链接: $ARTIFACTS_DIR"
if [ -e "$ARTIFACTS_DIR" ] && [ ! -d "$ARTIFACTS_DIR" ]; then
  die "artifacts 路径已存在但不是目录，拒绝覆盖: $ARTIFACTS_DIR"
fi
[ ! -L "$WORK_DIR" ] || die "拒绝操作符号链接: $WORK_DIR"
if [ -e "$WORK_DIR" ] && [ ! -d "$WORK_DIR" ]; then
  die "artifacts/verify 已存在但不是目录，拒绝覆盖: $WORK_DIR"
fi
if [ -d "$WORK_DIR" ] && [ ! -f "$WORK_DIR/.ohmymods-verify" ] && [ -n "$(ls -A "$WORK_DIR" 2>/dev/null || true)" ]; then
  die "artifacts/verify 非空且缺少本脚本标记，拒绝覆盖: $WORK_DIR"
fi
rm -rf "$WORK_DIR"
mkdir -p "$WORK_DIR"
printf 'verify.sh run at %s\ndobby=%s sha256=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$DOBBY_LIB" "$(shasum -a 256 "$DOBBY_LIB" | awk '{print $1}')" > "$WORK_DIR/.ohmymods-verify"
cat "$WORK_DIR/.ohmymods-verify"

# ---- 编译（固定 -arch x86_64）----
printf '== 编译探针 ==\n'
"$CLANG" -arch x86_64 -Wall -o "$WORK_DIR/branch-probe" "$TESTS_DIR/branch-probe.c"
"$CLANG" -arch x86_64 -dynamiclib -o "$WORK_DIR/libbranch-target.dylib" "$TESTS_DIR/branch-target.s"
"$CLANG" -arch x86_64 -Wall -o "$WORK_DIR/multi-probe" "$TESTS_DIR/multi-probe.c"
"$CLANG" -arch x86_64 -dynamiclib -o "$WORK_DIR/libmulti-target.dylib" "$TESTS_DIR/multi-target.c"
"$CLANG" -arch x86_64 -Wall -o "$WORK_DIR/pressure-probe" "$TESTS_DIR/pressure-probe.c"
"$CLANG" -arch x86_64 -dynamiclib -o "$WORK_DIR/libpressure-target.dylib" "$TESTS_DIR/pressure-target.s"

"$CLANG" -arch x86_64 -Wall -o "$WORK_DIR/mixed-page-probe" "$TESTS_DIR/mixed-page-probe.c"

for bin in mixed-page-probe branch-probe multi-probe pressure-probe libbranch-target.dylib libmulti-target.dylib libpressure-target.dylib; do
  archs="$(/usr/bin/lipo -archs "$WORK_DIR/$bin")"
  [ "$archs" = "x86_64" ] || die "$bin 架构应为 x86_64，实际: $archs"
  printf '  %-24s x86_64 ok\n' "$bin"
done

# ---- 运行（perl alarm 提供超时；无 perl 时直接运行）----
PERL_BIN="$(command -v perl || true)"
[ -n "$PERL_BIN" ] || printf '提示: 未找到 perl，本次运行不设超时\n' >&2

FAILED=0
run_probe() {
  local name="$1" probe="$2" target="$3" expect="$4" rc=0
  local log="$WORK_DIR/$name.log"
  local args=("$DOBBY_LIB")
  if [ -n "$target" ]; then args+=("$WORK_DIR/$target"); fi
  printf '== 运行 %s ==\n' "$name"
  if [ -n "$PERL_BIN" ]; then
    "$PERL_BIN" -e 'alarm shift; exec @ARGV' "$TIMEOUT" \
      "$WORK_DIR/$probe" "${args[@]}" >"$log" 2>&1 || rc=$?
  else
    "$WORK_DIR/$probe" "${args[@]}" >"$log" 2>&1 || rc=$?
  fi
  cat "$log"
  if [ "$rc" -eq 142 ]; then
    printf '结果: %s FAIL（超时 %ss 被杀）\n' "$name" "$TIMEOUT"
    FAILED=1
    return 0
  fi
  if [ "$rc" -ne 0 ] || ! grep -q "$expect" "$log"; then
    printf '结果: %s FAIL（exit=%s，缺少预期输出 "%s"）\n' "$name" "$rc" "$expect"
    FAILED=1
    return 0
  fi
  printf '结果: %s PASS（exit=0）\n' "$name"
}

run_probe branch-probe   branch-probe   libbranch-target.dylib   "RESULT=PASS"
run_probe multi-probe    multi-probe    libmulti-target.dylib    "RESULT=PASS"
run_probe pressure-probe pressure-probe libpressure-target.dylib "RESULT=PASS"

run_probe mixed-page-probe mixed-page-probe "" "MIXED-PAGE=PASS"

printf '== 汇总 ==\n'
if [ "$FAILED" -ne 0 ]; then
  printf 'VERIFY=FAIL（日志见 %s）\n' "$WORK_DIR"
  exit 1
fi
printf 'VERIFY=PASS dobby=%s\n' "$DOBBY_LIB"
printf '日志: %s/{branch-probe,multi-probe,pressure-probe,mixed-page-probe}.log\n' "$WORK_DIR"
