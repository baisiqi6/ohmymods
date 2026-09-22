#!/bin/sh
# Build + run the native arm64 Dobby probe. All outputs stay inside arm64-lab.
set -e
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/native-build/probe-arm64"
DOBBY_DYLIB="$ROOT/native-build/dobby-Darwin-arm64/libdobby.dylib"
CC="$(xcrun --find clang)"
SDK="$(xcrun --sdk macosx --show-sdk-path)"
echo "sdk=$SDK"
mkdir -p "$OUT"

echo "== build target dylib =="
"$CC" -isysroot "$SDK" -arch arm64 -mmacosx-version-min=12.0 -O2 -dynamiclib \
  -install_name "@rpath/libprobe-target.dylib" \
  -o "$OUT/libprobe-target.dylib" "$ROOT/native-probe/target/target.c"

echo "== build probe =="
"$CC" -isysroot "$SDK" -arch arm64 -mmacosx-version-min=12.0 -O2 \
  -o "$OUT/probe-arm64" "$ROOT/native-probe/probe.c"

echo "== arch evidence =="
file "$OUT/probe-arm64" "$OUT/libprobe-target.dylib" "$DOBBY_DYLIB"
lipo -info "$OUT/probe-arm64" "$OUT/libprobe-target.dylib" "$DOBBY_DYLIB"

echo "== run =="
cd "$OUT"
DYLD_PRINT_APIS=0 ./probe-arm64 "$OUT/libprobe-target.dylib" "$DOBBY_DYLIB"
echo "probe_exit=$?"
