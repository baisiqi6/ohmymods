// Near-page search tests for the macOS NearMemoryArena path.
//
// Every case builds a real Mach VM layout and lets
// NearMemoryArena::AllocateCodeChunk resolve a page inside a window of that
// layout. The layouts are the ones the search has to survive:
//   straddle-both     both surrounding mappings cross a window bound
//   one-page-aligned  the hole is exactly one page and page aligned
//   tail-interval     the hole merges with the free space after the last mapping
//   leading-free      the free space sits before the first mapping
//   two-holes-lowest  two holes in the window, the lowest one wins
//   window-in-region  the window holds no free page, it lies inside a mapping
//   hole-out-window   the only hole lies outside the window
//
// One case per process: NearMemoryArena keeps its pages in static state, so a
// case must not inherit pages of another one.
//
// usage: near-gap-tests <case>

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#include <mach/mach.h>
#include <mach/mach_vm.h>

#include "MemoryAllocator/NearMemoryArena.h"

static int checks = 0;
static int failures = 0;

static void check(bool ok, const char *what) {
  checks++;
  if (!ok) {
    failures++;
    printf("  FAIL %s\n", what);
  }
}

static size_t page_size() {
  return (size_t)sysconf(_SC_PAGESIZE);
}

struct Block {
  mach_vm_address_t base;
  size_t pages;
};

static Block allocate_block(size_t pages) {
  Block block = {0, pages};
  kern_return_t kr = mach_vm_allocate(mach_task_self(), &block.base, pages * page_size(), VM_FLAGS_ANYWHERE);
  if (kr != KERN_SUCCESS) {
    printf("  FAIL mach_vm_allocate(%zu pages) kr=%d\n", pages, (int)kr);
    exit(2);
  }
  return block;
}

static void release_block(Block block) {
  mach_vm_deallocate(mach_task_self(), block.base, block.pages * page_size());
}

// The freed pages are not mapped any more, they must stay untouched.
static void free_page(Block block, size_t index) {
  kern_return_t kr = mach_vm_deallocate(mach_task_self(), block.base + index * page_size(), page_size());
  if (kr != KERN_SUCCESS) {
    printf("  FAIL mach_vm_deallocate(page %zu) kr=%d\n", index, (int)kr);
    exit(2);
  }
}

static bool skipped(size_t index, const size_t *skip, size_t skip_count) {
  for (size_t i = 0; i < skip_count; ++i) {
    if (skip[i] == index)
      return true;
  }
  return false;
}

static unsigned long long pattern(size_t page_index, size_t offset) {
  return 0xC0FFEE00C0FFEE00ull ^ ((unsigned long long)page_index << 32) ^ (unsigned long long)offset;
}

// A pattern that identifies its page, written over every mapped page of the
// block. If the search overwrote a mapping instead of reserving free space, the
// patterns no longer read back.
static void fill_pattern(Block block, const size_t *skip, size_t skip_count) {
  size_t p = page_size();
  for (size_t i = 0; i < block.pages; ++i) {
    if (skipped(i, skip, skip_count))
      continue;
    unsigned char *page = (unsigned char *)(block.base + i * p);
    for (size_t off = 0; off < p; off += sizeof(unsigned long long)) {
      unsigned long long value = pattern(i, off);
      memcpy(page + off, &value, sizeof(value));
    }
  }
}

static void verify_pattern(Block block, const size_t *skip, size_t skip_count) {
  size_t p = page_size();
  int corrupted = 0;
  for (size_t i = 0; i < block.pages; ++i) {
    if (skipped(i, skip, skip_count))
      continue;
    unsigned char *page = (unsigned char *)(block.base + i * p);
    for (size_t off = 0; off < p; off += sizeof(unsigned long long)) {
      unsigned long long value = 0;
      memcpy(&value, page + off, sizeof(value));
      if (value != pattern(i, off))
        corrupted++;
    }
  }
  check(corrupted == 0, "surrounding mappings keep their contents");
}

// The page handed to the caller must be a fresh, standalone read+execute page.
static void check_page_ownership(mach_vm_address_t addr) {
  mach_vm_address_t region_addr = addr;
  mach_vm_size_t region_size = 0;
  vm_region_basic_info_data_64_t info;
  mach_msg_type_number_t count = VM_REGION_BASIC_INFO_COUNT_64;
  mach_port_t object = MACH_PORT_NULL;
  kern_return_t kr = mach_vm_region(mach_task_self(), &region_addr, &region_size, VM_REGION_BASIC_INFO_64,
                                    (vm_region_info_t)&info, &count, &object);
  check(kr == KERN_SUCCESS, "allocated page is mapped");
  if (kr != KERN_SUCCESS)
    return;
  check(region_addr == addr, "allocated page starts its own region");
  check(region_size == page_size(), "allocated page is one page long");
  check((info.protection & VM_PROT_READ) != 0 && (info.protection & VM_PROT_EXECUTE) != 0,
        "allocated page is readable and executable");
  check(((unsigned char *)addr)[0] == 0, "allocated page is zero filled");
}

static MemoryChunk *search(addr_t pos, size_t range) {
  return NearMemoryArena::AllocateCodeChunk(pos, range, 32);
}

static int finish(const char *name) {
  printf("%s checks=%d failures=%d RESULT=%s\n", name, checks, failures, failures == 0 ? "PASS" : "FAIL");
  return failures == 0 ? 0 : 1;
}

// 8 pages, the 5th freed. The window covers the hole while both mappings cross
// a window bound: the one below starts before the window, the one above ends
// after it.
static int case_straddle_both() {
  size_t p = page_size();
  Block block = allocate_block(8);
  const size_t skip[1] = {5};
  free_page(block, 5);
  fill_pattern(block, skip, 1);

  MemoryChunk *chunk = search((addr_t)(block.base + 4 * p + 256), (size_t)(3 * p));
  check(chunk != NULL, "allocation found the free page");
  if (chunk) {
    check(chunk->address == (void *)(block.base + 5 * p), "allocation is the only free page");
    check(chunk->length == 32, "chunk length is the requested size");
    check_page_ownership((mach_vm_address_t)chunk->address);
  }
  verify_pattern(block, skip, 1);
  release_block(block);
  return finish("straddle-both");
}

// 4 pages, the 3rd freed. The window is exactly that one page and the hole
// starts on a page boundary. Add one byte because allocator distance is strictly less than range.
static int case_one_page_aligned() {
  size_t p = page_size();
  Block block = allocate_block(4);
  const size_t skip[1] = {2};
  free_page(block, 2);
  fill_pattern(block, skip, 1);

  MemoryChunk *chunk = search((addr_t)(block.base + 2 * p + p / 2), (size_t)(p / 2 + 1));
  check(chunk != NULL, "allocation found the page aligned hole");
  if (chunk) {
    check(chunk->address == (void *)(block.base + 2 * p), "allocation is the page aligned hole");
    check_page_ownership((mach_vm_address_t)chunk->address);
  }
  verify_pattern(block, skip, 1);
  release_block(block);
  return finish("one-page-aligned");
}

// 8 pages, the last freed: the hole merges with the free space after the
// mapping, so the search has to take free space trailing the last region.
static int case_tail_interval() {
  size_t p = page_size();
  Block block = allocate_block(8);
  const size_t skip[1] = {7};
  free_page(block, 7);
  fill_pattern(block, skip, 1);

  MemoryChunk *chunk = search((addr_t)(block.base + 7 * p), (size_t)p);
  check(chunk != NULL, "allocation found free space after the last mapping");
  if (chunk) {
    check(chunk->address == (void *)(block.base + 7 * p), "allocation is the trailing free page");
    check_page_ownership((mach_vm_address_t)chunk->address);
  }
  verify_pattern(block, skip, 1);
  release_block(block);
  return finish("tail-interval");
}

// 8 pages, the first freed: the free space sits before the first mapping.
static int case_leading_free() {
  size_t p = page_size();
  Block block = allocate_block(8);
  const size_t skip[1] = {0};
  free_page(block, 0);
  fill_pattern(block, skip, 1);

  MemoryChunk *chunk = search((addr_t)(block.base + p / 2), (size_t)(p / 2 + 1));
  check(chunk != NULL, "allocation found free space before the first mapping");
  if (chunk) {
    check(chunk->address == (void *)block.base, "allocation is the leading free page");
    check_page_ownership((mach_vm_address_t)chunk->address);
  }
  verify_pattern(block, skip, 1);
  release_block(block);
  return finish("leading-free");
}

// 8 pages, pages 2 and 5 freed, both inside the window. The search reports the
// lowest free page of the window.
static int case_two_holes_lowest() {
  size_t p = page_size();
  Block block = allocate_block(8);
  const size_t skip[2] = {2, 5};
  free_page(block, 2);
  free_page(block, 5);
  fill_pattern(block, skip, 2);

  MemoryChunk *chunk = search((addr_t)(block.base + 3 * p), (size_t)(2 * p));
  check(chunk != NULL, "allocation found a hole");
  if (chunk) {
    check(chunk->address == (void *)(block.base + 2 * p), "allocation is the lowest hole of the window");
    check_page_ownership((mach_vm_address_t)chunk->address);
  }
  verify_pattern(block, skip, 2);
  release_block(block);
  return finish("two-holes-lowest");
}

// No hole at all: the window lies inside one mapping, so there is nothing to
// hand out and nothing may be overwritten.
static int case_window_in_region() {
  size_t p = page_size();
  Block block = allocate_block(4);
  fill_pattern(block, NULL, 0);

  MemoryChunk *chunk = search((addr_t)(block.base + 2 * p), (size_t)(p / 2));
  check(chunk == NULL, "no free page in a fully mapped window");
  check(search((addr_t)(block.base + 2 * p), 0) == NULL, "zero range is rejected");
  verify_pattern(block, NULL, 0);
  release_block(block);
  return finish("window-in-region");
}

// The only hole of the layout lies outside the window, so it must not be used.
static int case_hole_out_of_window() {
  size_t p = page_size();
  Block block = allocate_block(8);
  const size_t skip[1] = {5};
  free_page(block, 5);
  fill_pattern(block, skip, 1);

  MemoryChunk *chunk = search((addr_t)(block.base + p), (size_t)p);
  check(chunk == NULL, "hole outside the window is not used");
  verify_pattern(block, skip, 1);
  release_block(block);
  return finish("hole-out-window");
}

int main(int argc, char **argv) {
  if (argc != 2) {
    printf("usage: %s <straddle-both|one-page-aligned|tail-interval|leading-free|two-holes-lowest|window-in-region|"
           "hole-out-window>\n",
           argv[0]);
    return 2;
  }
  const char *name = argv[1];
  if (strcmp(name, "straddle-both") == 0)
    return case_straddle_both();
  if (strcmp(name, "one-page-aligned") == 0)
    return case_one_page_aligned();
  if (strcmp(name, "tail-interval") == 0)
    return case_tail_interval();
  if (strcmp(name, "leading-free") == 0)
    return case_leading_free();
  if (strcmp(name, "two-holes-lowest") == 0)
    return case_two_holes_lowest();
  if (strcmp(name, "window-in-region") == 0)
    return case_window_in_region();
  if (strcmp(name, "hole-out-window") == 0)
    return case_hole_out_of_window();
  printf("unknown case: %s\n", name);
  return 2;
}
