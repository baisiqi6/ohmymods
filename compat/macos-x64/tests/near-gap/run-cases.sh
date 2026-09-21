#!/usr/bin/env bash
# Runs every near-page search case in its own process and reports the summary.
# usage: run-cases.sh <path-to-near-gap-tests> [log-dir]
set -uo pipefail

BIN="${1:?usage: run-cases.sh <path-to-near-gap-tests> [log-dir]}"
LOG_DIR="${2:-$(dirname "$BIN")/logs}"
CASES=(straddle-both one-page-aligned tail-interval leading-free two-holes-lowest window-in-region hole-out-window)

[ -x "$BIN" ] || { printf 'not executable: %s\n' "$BIN" >&2; exit 2; }
mkdir -p "$LOG_DIR"

failed=0
for case in "${CASES[@]}"; do
  if "$BIN" "$case" >"$LOG_DIR/$case.log" 2>&1; then
    printf '%-18s PASS  %s\n' "$case" "$(cat "$LOG_DIR/$case.log")"
  else
    rc=$?
    printf '%-18s FAIL(exit=%s)  %s\n' "$case" "$rc" "$(cat "$LOG_DIR/$case.log")"
    failed=1
  fi
done

if [ "$failed" -eq 0 ]; then
  printf 'NEAR-GAP=PASS cases=%d binary=%s logs=%s\n' "${#CASES[@]}" "$BIN" "$LOG_DIR"
else
  printf 'NEAR-GAP=FAIL binary=%s logs=%s\n' "$BIN" "$LOG_DIR"
fi
exit "$failed"
