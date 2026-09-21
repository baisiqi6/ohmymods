// Pre-reservation tests for the macOS near-code arena.
//
// Every case builds a real Mach block with one contiguous hole and lets
// NearMemoryArena::AllocateCodeChunk resolve a page inside it. The arena takes
// the first free page of the hole and must claim the pages behind it in the
// same batch; a foreign mapping that arrives afterwards (CLR, another runtime)
// then finds only the pages that were left. Every chunk that needs a page of
// its own has to come from the reserved run: the old arena holds one single
// page, so it fails on the second chunk once the hole is taken.
//
//   reserve-successors  hole of 20 pages, the window covers all of it: the
//                       batch stops at its 16 page limit, 16 pages are
//                       reserved, the foreign client gets the 4 trailing
//                       pages, 16 chunks are handed out and the 17th fails.
//   window-limit        the same hole with a window that ends after 4 pages:
//                       only 4 pages may be reserved, the foreign client gets
//                       16, 4 chunks are handed out and the 5th fails.
//
// Nothing is simulated: mappings are mach_vm_allocate, every decision comes
// from the real allocator, and the contents of the surrounding mappings and of
// the foreign pages are checked after the fact.
//
// One case per process: the arena keeps its pages in static state.
//
// usage: near-reserve-tests <reserve-successors|window-limit>

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
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

static void free_pages(Block block, size_t first, size_t count) {
  kern_return_t kr = mach_vm_deallocate(mach_task_self(), block.base + first * page_size(), count * page_size());
  if (kr != KERN_SUCCESS) {
    printf("  FAIL mach_vm_deallocate(%zu pages at %zu) kr=%d\n", count, first, (int)kr);
    exit(2);
  }
}

static bool page_is_mapped(mach_vm_address_t addr) {
  mach_vm_address_t region_addr = addr;
  mach_vm_size_t region_size = 0;
  vm_region_basic_info_data_64_t info;
  mach_msg_type_number_t count = VM_REGION_BASIC_INFO_COUNT_64;
  mach_port_t object = MACH_PORT_NULL;
  kern_return_t kr = mach_vm_region(mach_task_self(), &region_addr, &region_size, VM_REGION_BASIC_INFO_64,
                                    (vm_region_info_t)&info, &count, &object);
  if (MACH_PORT_VALID(object)) mach_port_deallocate(mach_task_self(), object);
  return kr == KERN_SUCCESS && region_addr <= addr && addr < region_addr + region_size;
}

static unsigned long long pattern(size_t page_index, size_t offset) {
  return 0xC0FFEE00C0FFEE00ull ^ ((unsigned long long)page_index << 32) ^ (unsigned long long)offset;
}

// Identifies its page, so a handed out page or an overwritten mapping is
// visible afterwards.
static void fill_pattern(mach_vm_address_t addr, size_t page_index) {
  size_t p = page_size();
  for (size_t off = 0; off < p; off += sizeof(unsigned long long)) {
    unsigned long long value = pattern(page_index, off);
    memcpy((unsigned char *)addr + off, &value, sizeof(value));
  }
}

static void verify_pattern(mach_vm_address_t addr, size_t page_index, const char *what) {
  size_t p = page_size();
  int corrupted = 0;
  for (size_t off = 0; off < p; off += sizeof(unsigned long long)) {
    unsigned long long value = 0;
    memcpy(&value, (unsigned char *)addr + off, sizeof(value));
    if (value != pattern(page_index, off))
      corrupted++;
  }
  check(corrupted == 0, what);
}

// A page of the reserved run is a fresh, standalone read+execute page.
static void check_page_ownership(mach_vm_address_t addr, const char *what) {
  mach_vm_address_t region_addr = addr;
  mach_vm_size_t region_size = 0;
  vm_region_basic_info_data_64_t info;
  mach_msg_type_number_t count = VM_REGION_BASIC_INFO_COUNT_64;
  mach_port_t object = MACH_PORT_NULL;
  kern_return_t kr = mach_vm_region(mach_task_self(), &region_addr, &region_size, VM_REGION_BASIC_INFO_64,
                                    (vm_region_info_t)&info, &count, &object);
  if (MACH_PORT_VALID(object)) mach_port_deallocate(mach_task_self(), object);
  bool own_region = kr == KERN_SUCCESS && region_addr == addr && region_size == page_size();
  check(own_region, what);
  if (own_region)
    check((info.protection & VM_PROT_READ) != 0 && (info.protection & VM_PROT_EXECUTE) != 0, what);
}

// What a foreign client does with the space the arena did not take: map every
// page of the hole that is still free at its exact address, one page at a
// time, and write a pattern into it.
static size_t foreign_fill_hole(Block block, size_t first, size_t count) {
  size_t p = page_size();
  size_t taken = 0;
  for (size_t i = first; i < first + count; ++i) {
    mach_vm_address_t addr = block.base + i * p;
    if (page_is_mapped(addr))
      continue;
    mach_vm_address_t requested = addr;
    if (mach_vm_allocate(mach_task_self(), &requested, p, VM_FLAGS_FIXED) != KERN_SUCCESS || requested != addr)
      continue;
    if (mprotect((void *)addr, p, PROT_READ | PROT_WRITE) != 0) {
      printf("  FAIL mprotect(foreign page %zu)\n", i);
      exit(2);
    }
    fill_pattern(addr, i);
    taken++;
  }
  return taken;
}

// 4000 bytes: a chunk of that size leaves less than a page behind, so every
// call has to resolve a page of its own.
static MemoryChunk *search(addr_t pos, size_t range) {
  return NearMemoryArena::AllocateCodeChunk(pos, range, 4000);
}

// 28 pages, the 20 pages 4..23 freed. The mappings that stay hold a pattern.
static Block build_layout() {
  size_t p = page_size();
  Block block = allocate_block(28);
  free_pages(block, 4, 20);
  for (size_t i = 0; i < 4; ++i)
    fill_pattern(block.base + i * p, i);
  for (size_t i = 24; i < 28; ++i)
    fill_pattern(block.base + i * p, i);
  return block;
}

static void verify_layout(Block block) {
  size_t p = page_size();
  for (size_t i = 0; i < 4; ++i)
    verify_pattern(block.base + i * p, i, "mapping before the hole keeps its contents");
  for (size_t i = 24; i < 28; ++i)
    verify_pattern(block.base + i * p, i, "mapping after the hole keeps its contents");
}

// Chunks must walk the reserved pages in order, start on a page boundary and
// never touch the foreign pages.
static size_t check_chunks(Block block, size_t first_chunk_page, size_t expected_chunks, size_t position, size_t range,
                           size_t foreign_first, size_t foreign_count) {
  size_t p = page_size();
  for (size_t i = 0; i < expected_chunks; ++i) {
    MemoryChunk *chunk = search((addr_t)position, range);
    check(chunk != NULL, "chunk inside the reserved run is handed out");
    if (!chunk)
      return i;
    mach_vm_address_t addr = (mach_vm_address_t)chunk->address;
    check(chunk->length == 4000, "chunk length is the requested size");
    check(addr == block.base + (first_chunk_page + i) * p, "chunk is the expected reserved page");
    check_page_ownership(addr, "chunk page is a read+execute region of its own");
  }
  // One more call once the run and the hole are exhausted.
  check(search((addr_t)position, range) == NULL, "no page is handed out once the reservation is used up");
  for (size_t i = 0; i < foreign_count; ++i)
    verify_pattern(block.base + (foreign_first + i) * p, foreign_first + i, "foreign page keeps its contents");
  return expected_chunks;
}

// Hole of 20 pages, window covers all of it: the 16 page limit of the batch
// decides how much is reserved.
static int case_reserve_successors() {
  size_t p = page_size();
  Block block = build_layout();
  size_t position = block.base + 14 * p;
  size_t range = 10 * p + 1; // window: [base+4p-1, base+24p]

  MemoryChunk *first = search((addr_t)position, range);
  check(first != NULL, "first allocation found the hole");
  if (first) {
    check((mach_vm_address_t)first->address == block.base + 4 * p, "first allocation is the first page of the hole");
    check_page_ownership((mach_vm_address_t)first->address, "first page is a read+execute region of its own");
  }
  verify_layout(block);

  size_t taken = foreign_fill_hole(block, 4, 20);
  check(taken == 4, "a 16 page batch leaves the 4 trailing pages to the foreign client");
  check(page_is_mapped(block.base + 20 * p), "the foreign client mapped the page after the reserved run");

  check_chunks(block, 5, 15, position, range, 20, 4);
  verify_layout(block);
  release_block(block);
  return 0;
}

// Same hole, a window that ends after 4 pages: the window bound decides, not
// the 16 page limit.
static int case_window_limit() {
  size_t p = page_size();
  Block block = build_layout();
  size_t position = block.base + 6 * p;
  size_t range = 2 * p + 1; // window: [base+4p-1, base+8p]

  MemoryChunk *first = search((addr_t)position, range);
  check(first != NULL, "first allocation found the hole");
  if (first)
    check((mach_vm_address_t)first->address == block.base + 4 * p, "first allocation is the first page of the hole");
  verify_layout(block);

  size_t taken = foreign_fill_hole(block, 4, 20);
  check(taken == 16, "the reservation stops at the window end, the other 16 pages stay free");
  check(page_is_mapped(block.base + 8 * p), "the foreign client mapped the page past the window end");

  check_chunks(block, 5, 3, position, range, 8, 16);
  verify_layout(block);
  release_block(block);
  return 0;
}

static int finish(const char *name) {
  printf("%s checks=%d failures=%d RESULT=%s\n", name, checks, failures, failures == 0 ? "PASS" : "FAIL");
  return failures == 0 ? 0 : 1;
}

int main(int argc, char **argv) {
  if (argc != 2) {
    printf("usage: %s <reserve-successors|window-limit>\n", argv[0]);
    return 2;
  }
  if (strcmp(argv[1], "reserve-successors") == 0) {
    case_reserve_successors();
    return finish("reserve-successors");
  }
  if (strcmp(argv[1], "window-limit") == 0) {
    case_window_limit();
    return finish("window-limit");
  }
  printf("unknown case: %s\n", argv[1]);
  return 2;
}
