#include <dlfcn.h>
#include <stdio.h>
#include <string.h>

typedef void (*target_fn)(unsigned char *);
typedef int (*prepare_fn)(void *, void *, void **);
typedef int (*operation_fn)(void *);
static target_fn original;
static int hits;
static void replacement(unsigned char *state) { ++hits; original(state); }
static int value(const unsigned char *state) {
    int result;
    memcpy(&result, state + 36, sizeof(result));
    return result;
}
int main(int argc, char **argv) {
    setvbuf(stdout, NULL, _IONBF, 0);
    if (argc != 3) { fprintf(stderr, "usage: branch-probe DOBBY TARGET\n"); return 2; }
    void *dobby = dlopen(argv[1], RTLD_NOW);
    void *target_lib = dlopen(argv[2], RTLD_NOW);
    if (!dobby || !target_lib) { fprintf(stderr, "dlopen: %s\n", dlerror()); return 2; }
    target_fn target = (target_fn)dlsym(target_lib, "branch_target");
    prepare_fn prepare = (prepare_fn)dlsym(dobby, "DobbyPrepare");
    operation_fn commit = (operation_fn)dlsym(dobby, "DobbyCommit");
    operation_fn destroy = (operation_fn)dlsym(dobby, "DobbyDestroy");
    if (!target || !prepare || !commit || !destroy) return 2;
    unsigned char baseline[64] = {0}, taken[64] = {0}, not_taken[64] = {0}, restored[64] = {0};
    target(baseline);
    if (value(baseline) != 1) { puts("baseline FAIL"); return 3; }
    if (prepare((void *)target, (void *)replacement, (void **)&original) != 0 || !original) return 4;
    if (commit((void *)target) != 0) return 5;
    target(taken);
    not_taken[33] = 1;
    target(not_taken);
    if (destroy((void *)target) != 0) { puts("restore FAIL"); return 6; }
    target(restored);
    int ok = value(taken) == 1 && value(not_taken) == 0 && value(restored) == 1 && hits == 2;
    printf("taken=%d nottaken=%d restored=%d hits=%d RESULT=%s\n", value(taken), value(not_taken),
           value(restored), hits, ok ? "PASS" : "FAIL");
    return ok ? 0 : 1;
}
