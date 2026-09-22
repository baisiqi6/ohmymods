// Native arm64 Dobby probe. Independent of any game / external process:
// it hooks only functions inside its own dlopen'ed target dylib.
//
// Checks per function:
//   1. warm call (pre-hook) records ground truth
//   2. DobbyPrepare + DobbyCommit => hooked result differs
//   3. origin trampoline returned by DobbyPrepare still yields the original results
//   4. DobbyDestroy => original results restored
//   5. repeated install/destroy cycles (guards against a single accidental pass)
//   6. two different target functions, plus a simultaneous-hook phase
//
// Exit code 0 == every check passed.

#include <dlfcn.h>
#include <mach-o/dyld.h>
#include <mach-o/loader.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/sysctl.h>

#if !defined(__arm64__)
#error "probe must be compiled for arm64"
#endif

typedef int (*fn_alpha_t)(int, int);
typedef int (*fn_scale_t)(int);

typedef int (*DobbyPrepare_t)(void *address, void *replace_call, void **origin_call);
typedef int (*DobbyCommit_t)(void *address);
typedef int (*DobbyDestroy_t)(void *address);
typedef const char *(*DobbyBuildVersion_t)(void);

static fn_alpha_t tramp_alpha = NULL;
static fn_scale_t tramp_scale = NULL;

static int fails = 0;
static int checks = 0;

#define CHECK(cond, ...)                                                                          \
  do {                                                                                            \
    checks++;                                                                                     \
    if (!(cond)) {                                                                                \
      fails++;                                                                                    \
      printf("FAIL: ");                                                                           \
    } else {                                                                                      \
      printf("ok  : ");                                                                           \
    }                                                                                             \
    printf(__VA_ARGS__);                                                                          \
    printf("\n");                                                                                 \
  } while (0)

// replacement for omega_add: constant, never calls the original
static int hook_alpha(int a, int b) {
  (void)a;
  (void)b;
  return 424242;
}

// replacement for omega_scale: calls the original trampoline (nested hook path)
static int hook_scale(int x) {
  int base = tramp_scale(x);
  return base + 1000;
}

static void *mustsym(void *h, const char *name) {
  void *p = dlsym(h, name);
  if (!p) {
    printf("FAIL: dlsym(%s) -> %s\n", name, dlerror());
    exit(2);
  }
  return p;
}

static int runtime_arch_checks(void) {
  int translated = -1;
  size_t sz = sizeof(translated);
  if (sysctlbyname("sysctl.proc_translated", &translated, &sz, NULL, 0) != 0)
    translated = 0; // key absent == not translated
  CHECK(translated == 0, "runtime: sysctl.proc_translated=%d (0 == native, no Rosetta)",
        translated);

  int is_arm64 = 0;
  sz = sizeof(is_arm64);
  sysctlbyname("hw.optional.arm64", &is_arm64, &sz, NULL, 0);
  CHECK(is_arm64 == 1, "runtime: hw.optional.arm64=%d", is_arm64);
  return translated;
}

static void image_arch_check(const char *path, void *fn) {
  Dl_info info;
  if (!dladdr(fn, &info)) {
    CHECK(0, "dladdr(%s) failed", path);
    return;
  }
  const struct mach_header_64 *mh = (const struct mach_header_64 *)info.dli_fbase;
  CHECK(mh->magic == MH_MAGIC_64 && mh->cputype == CPU_TYPE_ARM64,
        "runtime: loaded image %s cputype=0x%x (CPU_TYPE_ARM64=0x%x), file=%s", path, mh->cputype,
        (unsigned)CPU_TYPE_ARM64, info.dli_fname);
}

int main(int argc, char **argv) {
  const char *target_path = (argc > 1) ? argv[1] : "libprobe-target.dylib";
  const char *dobby_path = (argc > 2) ? argv[2] : "libdobby.dylib";

  printf("== native arm64 Dobby probe ==\n");
  runtime_arch_checks();

  void *ht = dlopen(target_path, RTLD_NOW | RTLD_LOCAL);
  if (!ht) {
    printf("FAIL: dlopen(%s) -> %s\n", target_path, dlerror());
    return 2;
  }
  void *hd = dlopen(dobby_path, RTLD_NOW | RTLD_LOCAL);
  if (!hd) {
    printf("FAIL: dlopen(%s) -> %s\n", dobby_path, dlerror());
    return 2;
  }

  DobbyBuildVersion_t ver = (DobbyBuildVersion_t)mustsym(hd, "DobbyBuildVersion");
  DobbyPrepare_t d_prepare = (DobbyPrepare_t)mustsym(hd, "DobbyPrepare");
  DobbyCommit_t d_commit = (DobbyCommit_t)mustsym(hd, "DobbyCommit");
  DobbyDestroy_t d_destroy = (DobbyDestroy_t)mustsym(hd, "DobbyDestroy");

  fn_alpha_t alpha = (fn_alpha_t)mustsym(ht, "omega_add");
  fn_scale_t scale = (fn_scale_t)mustsym(ht, "omega_scale");

  printf("dobby version: %s\n", ver());
  printf("target  alpha @%p  scale @%p\n", (void *)alpha, (void *)scale);
  image_arch_check("probe(exec)", (void *)&main);
  image_arch_check(target_path, (void *)alpha);

  // ground truth, pre-hook
  const int args_a[3][2] = {{2, 3}, {-5, 7}, {100, -40}};
  const int args_s[3] = {5, 1234, 0};
  int want_a[3], want_s[3];
  for (int i = 0; i < 3; i++) {
    want_a[i] = alpha(args_a[i][0], args_a[i][1]);
    want_s[i] = scale(args_s[i]);
  }
  printf("warm alpha: %d %d %d\n", want_a[0], want_a[1], want_a[2]);
  printf("warm scale: %d %d %d\n", want_s[0], want_s[1], want_s[2]);

  // --- phase 1: repeated install/destroy on omega_add -------------------------
  for (int round = 0; round < 3; round++) {
    void *orig = NULL;
    int rc = d_prepare((void *)alpha, (void *)hook_alpha, &orig);
    CHECK(rc == 0, "alpha round %d: DobbyPrepare rc=%d trampoline=%p", round, rc, orig);
    rc = d_commit((void *)alpha);
    CHECK(rc == 0, "alpha round %d: DobbyCommit rc=%d", round, rc);

    int hooked_ok = 1, tramp_ok = (orig != NULL);
    for (int i = 0; i < 3; i++) {
      if (alpha(args_a[i][0], args_a[i][1]) != 424242)
        hooked_ok = 0;
      if (orig && ((fn_alpha_t)orig)(args_a[i][0], args_a[i][1]) != want_a[i])
        tramp_ok = 0;
    }
    CHECK(hooked_ok, "alpha round %d: hooked call returns replacement 424242", round);
    CHECK(tramp_ok, "alpha round %d: original trampoline returns pre-hook results", round);

    rc = d_destroy((void *)alpha);
    CHECK(rc == 0, "alpha round %d: DobbyDestroy rc=%d", round, rc);
    int restored = 1;
    for (int i = 0; i < 3; i++)
      if (alpha(args_a[i][0], args_a[i][1]) != want_a[i])
        restored = 0;
    CHECK(restored, "alpha round %d: after destroy original behaviour restored", round);
  }

  // --- phase 2: different function, hook calls the original trampoline --------
  for (int round = 0; round < 3; round++) {
    void *orig = NULL;
    int rc = d_prepare((void *)scale, (void *)hook_scale, &orig);
    CHECK(rc == 0, "scale round %d: DobbyPrepare rc=%d trampoline=%p", round, rc, orig);
    tramp_scale = (fn_scale_t)orig;
    rc = d_commit((void *)scale);
    CHECK(rc == 0, "scale round %d: DobbyCommit rc=%d", round, rc);

    int hooked_ok = (tramp_scale != NULL), tramp_in_hook_ok = (tramp_scale != NULL);
    for (int i = 0; i < 3; i++) {
      if (scale(args_s[i]) != want_s[i] + 1000)
        hooked_ok = 0;
    }
    CHECK(hooked_ok, "scale round %d: replacement (calls trampoline) => orig+1000", round);
    if (tramp_scale) {
      for (int i = 0; i < 3; i++)
        if (tramp_scale(args_s[i]) != want_s[i])
          tramp_in_hook_ok = 0;
    }
    CHECK(tramp_in_hook_ok, "scale round %d: trampoline direct call == pre-hook results", round);

    rc = d_destroy((void *)scale);
    CHECK(rc == 0, "scale round %d: DobbyDestroy rc=%d", round, rc);
    tramp_scale = NULL;
    int restored = 1;
    for (int i = 0; i < 3; i++)
      if (scale(args_s[i]) != want_s[i])
        restored = 0;
    CHECK(restored, "scale round %d: after destroy original behaviour restored", round);
  }

  // --- phase 3: both hooked simultaneously (independence) ---------------------
  {
    void *oa = NULL, *os = NULL;
    int rc1 = d_prepare((void *)alpha, (void *)hook_alpha, &oa);
    int rc2 = d_prepare((void *)scale, (void *)hook_scale, &os);
    tramp_scale = (fn_scale_t)os;
    int rcc1 = d_commit((void *)alpha);
    int rcc2 = d_commit((void *)scale);
    CHECK(rc1 == 0 && rc2 == 0 && rcc1 == 0 && rcc2 == 0,
          "both: prepare/commit rc=%d,%d,%d,%d", rc1, rc2, rcc1, rcc2);
    CHECK(alpha(args_a[0][0], args_a[0][1]) == 424242 &&
              scale(args_s[0]) == want_s[0] + 1000 && oa != NULL && os != NULL,
          "both: simultaneous hooks both active");
    d_destroy((void *)alpha);
    d_destroy((void *)scale);
    tramp_scale = NULL;
    int restored = 1;
    for (int i = 0; i < 3; i++) {
      if (alpha(args_a[i][0], args_a[i][1]) != want_a[i])
        restored = 0;
      if (scale(args_s[i]) != want_s[i])
        restored = 0;
    }
    CHECK(restored, "both: after destroying both, both original");
  }

  printf("== %d checks, %d failures ==\n", checks, fails);
  printf("%s\n", fails == 0 ? "RESULT: PASS" : "RESULT: FAIL");
  return fails == 0 ? 0 : 1;
}
