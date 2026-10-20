# Dulche maintained fork overlay.
#
# Two explicit profiles share this single overlay/backend (never a second engine directory):
#   * legacy (default): exact ro99/strata 015b075079c51a7aec670ee24924f920f5e7bb2b with the
#     in-process C ABI worker (dulche-strata-worker).
#   * candidate: owner-designated Niko1221/Strata v0.2.1 (engine 0.1.46) with the candidate relay
#     worker (dulche-strata-candidate-worker) that drives the upstream `strata --serve` engine as a
#     dedicated sub-process. It is selected explicitly (DULCHE_STRATA_PROFILE=candidate).
#
# The candidate profile is SOURCE-ready but is NOT built by this checkpoint: the upstream CUDA engine
# requires a real build slot, CUDA 12.8+ (sm_120/RTX 50), and a verified local llama.cpp checkout
# (STRATA_GGML_DIR). Building it is NOT RUN until the coordinator allocates the heavy-build slot.

cmake_minimum_required(VERSION 3.24)
project(dulche_strata VERSION 1.1.0 LANGUAGES C CXX)

set(DULCHE_STRATA_PROFILE "legacy" CACHE STRING "Choose the Strata donor profile: legacy (default) or candidate.")
set_property(CACHE DULCHE_STRATA_PROFILE PROPERTY STRINGS legacy candidate)
if(NOT DULCHE_STRATA_PROFILE MATCHES "^(legacy|candidate)$")
  message(FATAL_ERROR "Unknown DULCHE_STRATA_PROFILE '${DULCHE_STRATA_PROFILE}' (legacy|candidate).")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(NOT DEFINED DULCHE_STRATA_UPSTREAM OR NOT EXISTS "${DULCHE_STRATA_UPSTREAM}/CMakeLists.txt")
  message(FATAL_ERROR "The verified bundled Strata source checkout is required; no separately installed app is used.")
endif()

# Host-named install dir: linux-x64 or windows-x64.
if(WIN32)
  set(_strata_target_dir "windows-x64")
  set(_strata_native_ext ".exe")
else()
  set(_strata_target_dir "linux-x64")
  set(_strata_native_ext "")
endif()

set(BUILD_TESTING OFF CACHE BOOL "" FORCE)

if(DULCHE_STRATA_PROFILE STREQUAL "legacy")
  # ------------------------------------------------------------------ legacy profile
  if(NOT CMAKE_SYSTEM_NAME STREQUAL "Linux" OR NOT CMAKE_SYSTEM_PROCESSOR MATCHES "^(x86_64|amd64|AMD64)$")
    message(FATAL_ERROR "legacy target is Linux x86_64 only; another target cannot be labeled linux-x64.")
  endif()
  add_subdirectory("${DULCHE_STRATA_UPSTREAM}" upstream EXCLUDE_FROM_ALL)
  add_executable(dulche-strata-worker dulche_strata.cpp dulche_strata_worker.cpp)
  target_link_libraries(dulche-strata-worker PRIVATE strata_core)
  get_target_property(DULCHE_DEVICE_DEFINITIONS strata_device INTERFACE_COMPILE_DEFINITIONS)
  if("STRATA_HAS_CUDA=1" IN_LIST DULCHE_DEVICE_DEFINITIONS)
    find_package(CUDAToolkit REQUIRED)
    target_link_libraries(dulche-strata-worker PRIVATE CUDA::cudart)
  endif()
  target_compile_options(dulche-strata-worker PRIVATE -Wall -Wextra -Wpedantic -Wconversion -Wshadow)
  install(TARGETS dulche-strata-worker RUNTIME DESTINATION runtimes/${_strata_target_dir}/native)
  install(FILES "${DULCHE_STRATA_UPSTREAM}/LICENSE" DESTINATION licenses/strata)
  install(FILES "${DULCHE_STRATA_UPSTREAM}/vendor/marlin/LICENSE"
                 "${DULCHE_STRATA_UPSTREAM}/vendor/marlin/README.md"
          DESTINATION licenses/strata/marlin)
else()
  # ------------------------------------------------------------------ candidate profile (v0.2.1)
  # Upstream engine targets (strata_core / strata_engine / strata_prefill / strata_spec /
  # strata_kernels_cpu) live inside the GPU branch: STRATA_ENABLE_CUDA must be ON. The native IQ
  # expert path (STRATA_NATIVE_EXPERTS=ON) needs ggml from a verified llama.cpp checkout. We never
  # disable required quantisations to avoid dependency custody; we require STRATA_GGML_DIR (or the
  # pinned FetchContent commit) and record it.
  set(STRATA_ENABLE_CUDA ON CACHE BOOL "" FORCE)
  set(STRATA_NATIVE_EXPERTS ON CACHE BOOL "" FORCE)
  set(STRATA_BUILD_TESTS OFF CACHE BOOL "" FORCE)
  set(STRATA_BUILD_CONVERSATION_TESTS OFF CACHE BOOL "" FORCE)
  set(STRATA_WERROR OFF CACHE BOOL "" FORCE)
  # Exclude upstream GUI/chat/agent/terminal application bundles: they are separate Node/Tauri apps
  # under apps/ and are NOT part of the core CMake build; only the `strata` engine target is built.
  add_subdirectory("${DULCHE_STRATA_UPSTREAM}" upstream EXCLUDE_FROM_ALL)

  # The engine server binary (the `strata` CLI, used in --serve mode). Built as part of ALL so a
  # normal `cmake --build` produces both the engine and the relay worker, then installed as a sibling.
  add_custom_target(dulche-thunder-engine ALL DEPENDS strata)
  install(TARGETS strata RUNTIME DESTINATION runtimes/${_strata_target_dir}/engine)

  # Candidate relay worker: hosts the Dulche frame ABI on stdin/stdout and drives the upstream
  # `strata --serve` engine as a dedicated sub-process. It does not link the engine; it does link
  # cudart when the candidate CUDA build is enabled so the hardware probe is real.
  add_executable(dulche-strata-candidate-worker dulche_strata_candidate_worker.cpp)
  target_compile_definitions(dulche-strata-candidate-worker PRIVATE
    STRATA_HAS_CUDA=1
    STRATA_NATIVE_EXT="${_strata_native_ext}"
    DULCHE_STRATA_UPSTREAM_PATH="${DULCHE_STRATA_UPSTREAM}")
  find_package(CUDAToolkit REQUIRED)
  target_link_libraries(dulche-strata-candidate-worker PRIVATE CUDA::cudart)
  if(NOT WIN32)
    target_compile_options(dulche-strata-candidate-worker PRIVATE -Wall -Wextra -Wpedantic -Wconversion -Wshadow)
  endif()
  install(TARGETS dulche-strata-candidate-worker RUNTIME DESTINATION runtimes/${_strata_target_dir}/native)
  # Bundled tokenizer/chat-template helper plus the donor runtime closure it imports, all installed
  # next to the worker. The helper (strata_tokenizer_helper.py) imports the donor
  # `strata_tokenizer.py` (which itself imports the sibling `gguf_reader.py` and the pinned `regex`
  # runtime module) and the `jinja2` runtime module for chat-template rendering. These files are a
  # REQUIRED, hash-verified runtime closure of the candidate text path; installing only the helper
  # (as in pass-3) is a real defect. `strata-runtime-dependencies.json` pins the exact
  # regex/jinja2 versions and is installed alongside so the installed closure is auditable.
  install(FILES strata_tokenizer_helper.py
                "${DULCHE_STRATA_UPSTREAM}/tools/strata_tokenizer.py"
                "${DULCHE_STRATA_UPSTREAM}/tools/gguf_reader.py"
                strata-runtime-dependencies.json
        DESTINATION runtimes/${_strata_target_dir}/native)
  # Candidate upstream license is MIT (© 2026 Niko1221 and Strata contributors). The original legacy
  # Apache-2.0 notice is preserved separately in the maintained fork/rollback and is not deleted.
  install(FILES "${DULCHE_STRATA_UPSTREAM}/LICENSE" DESTINATION licenses/strata)
  install(FILES "${DULCHE_STRATA_UPSTREAM}/third_party/ggml/LICENSE"
          DESTINATION licenses/strata/ggml)
endif()

# A build receipt (selected lock, profile, toolchain, target, binary digests) is written by
# build-package.py after the build/install; it is never manufactured by this file.
