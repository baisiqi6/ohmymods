// Probe target: real arm64 code inside a real dlopen'ed dylib.
// Functions are noinline/inline-off, exported, and contain enough instructions that
// relocating the prologue is non-trivial (multi-instruction entry + loop + branch).
#include <stdint.h>

__attribute__((noinline, visibility("default"))) int omega_add(int a, int b) {
  int r = a * 3 + b;
  r ^= 0x55;
  r += a << 2;
  if (r & 1)
    r += 7;
  return r;
}

__attribute__((noinline, visibility("default"))) int omega_scale(int x) {
  int acc = 0;
  for (int i = 0; i < 5; i++) {
    acc += x * (i + 2);
    acc ^= (acc >> 3) & 0x3f;
  }
  return acc + 1;
}

__attribute__((noinline, visibility("default"))) const char *omega_tag(void) {
  return "omega-native-arm64";
}
