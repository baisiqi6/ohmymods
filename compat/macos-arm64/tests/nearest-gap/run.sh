#!/bin/sh
set -eu
src=${1:?usage: run.sh /path/to/patched/Dobby/source}
self=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
out=$(mktemp -d)
trap 'rm -rf "$out"' EXIT
xcrun clang++ -std=c++11 -arch arm64 -I "$src/MemoryAllocator" "$self/gaps.cc" -o "$out/gaps"
"$out/gaps"
