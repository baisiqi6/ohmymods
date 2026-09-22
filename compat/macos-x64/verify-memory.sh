#!/usr/bin/env bash
# Build and run the real Mach layout regressions against the archived patch.
set -euo pipefail
[ "$#" -eq 1 ] || { echo 'usage: verify-memory.sh /absolute/path/to/cmake' >&2; exit 2; }
cmake_bin="$1"
root="$(cd "$(dirname "$0")" && pwd)"
source_dir="$root/artifacts/dobby-src"
out="$root/artifacts/memory-tests"
[ -f "$root/artifacts/build-info.json" ] && [ -d "$source_dir" ] || { echo 'Run build.sh first' >&2; exit 2; }
[ -x "$cmake_bin" ] || exit 2
[ ! -L "$out" ] || exit 2
mkdir -p "$out"
"$cmake_bin" -S "$source_dir" -B "$out/static" -DCMAKE_SYSTEM_NAME=Darwin -DCMAKE_SYSTEM_PROCESSOR=x86_64 -DCMAKE_OSX_ARCHITECTURES=x86_64 -DCMAKE_BUILD_TYPE=Release -DDOBBY_GENERATE_SHARED=OFF
"$cmake_bin" --build "$out/static" --target dobby --parallel 6
[ "$(lipo -archs "$out/static/libdobby.a")" = x86_64 ]
for fixture in near-gap near-reserve; do
  "$cmake_bin" -S "$root/tests/$fixture" -B "$out/$fixture" -DCMAKE_OSX_ARCHITECTURES=x86_64 -DCMAKE_POLICY_VERSION_MINIMUM=3.5 -DDOBBY_SOURCE_DIR="$source_dir" -DDOBBY_LIBRARY="$out/static/libdobby.a"
  "$cmake_bin" --build "$out/$fixture" --parallel 6
  bash "$root/tests/$fixture/run-cases.sh" "$out/$fixture/$fixture-tests" "$out/$fixture/logs"
done
echo 'MEMORY-REGRESSIONS=PASS'
