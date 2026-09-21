cmake_minimum_required(VERSION 3.5)
project(near-gap CXX)

# Standalone test project for the macOS near-page search in
# source/MemoryAllocator/NearMemoryArena.cc. It only needs a prebuilt libdobby.a:
#
#   cmake -S tests/near-gap -B <build-dir> \
#         -DCMAKE_OSX_ARCHITECTURES=x86_64 -DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
#         -DDOBBY_SOURCE_DIR=<repo> -DDOBBY_LIBRARY=<repo>/.test-gap/libbuild/libdobby.a
#   cmake --build <build-dir>
#   tests/near-gap/run-cases.sh <build-dir>/near-gap-tests
#
# The same project builds against the pre-fix library as a negative control.

set(CMAKE_CXX_STANDARD 11)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(DOBBY_SOURCE_DIR "" CACHE PATH "Dobby source tree")
set(DOBBY_LIBRARY "" CACHE FILEPATH "libdobby.a to test")

if(DOBBY_SOURCE_DIR STREQUAL "" OR DOBBY_LIBRARY STREQUAL "")
  message(FATAL_ERROR "DOBBY_SOURCE_DIR and DOBBY_LIBRARY are required")
endif()
if(NOT EXISTS "${DOBBY_LIBRARY}")
  message(FATAL_ERROR "DOBBY_LIBRARY not found: ${DOBBY_LIBRARY}")
endif()

include_directories(
  ${DOBBY_SOURCE_DIR}
  ${DOBBY_SOURCE_DIR}/source
  ${DOBBY_SOURCE_DIR}/source/include
  ${DOBBY_SOURCE_DIR}/source/UserMode
  ${DOBBY_SOURCE_DIR}/source/UserMode/include
  ${DOBBY_SOURCE_DIR}/external
  ${DOBBY_SOURCE_DIR}/external/logging
  ${DOBBY_SOURCE_DIR}/external/xnucxx
)

add_executable(near-gap-tests near-gap-tests.cc)
target_link_libraries(near-gap-tests "${DOBBY_LIBRARY}")
