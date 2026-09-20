cmake_minimum_required(VERSION 3.20)
project(cakeos_present_engine LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

find_path(LIBREOFFICEKIT_INCLUDE_DIR
    NAMES LibreOfficeKit/LibreOfficeKit.hxx
    PATH_SUFFIXES libreoffice
)

if(NOT LIBREOFFICEKIT_INCLUDE_DIR)
    message(FATAL_ERROR "LibreOfficeKit headers not found. Install libreofficekit-dev.")
endif()

find_package(nlohmann_json 3.2.0 REQUIRED)

include(CheckCXXSourceCompiles)
set(CMAKE_REQUIRED_INCLUDES ${LIBREOFFICEKIT_INCLUDE_DIR})
set(CMAKE_REQUIRED_DEFINITIONS -DLOK_USE_UNSTABLE_API)
check_cxx_source_compiles([[
#include <LibreOfficeKit/LibreOfficeKit.hxx>
int main()
{
    auto method = &lok::Office::extractDocumentStructureRequest;
    (void)method;
    return 0;
}
]] CAKEOS_PRESENT_HAS_STRUCTURE_REQUEST)
unset(CMAKE_REQUIRED_INCLUDES)
unset(CMAKE_REQUIRED_DEFINITIONS)

function(cakeos_enable_strict_warnings target)
    target_compile_options(${target} PRIVATE
        -Wall
        -Wextra
        -Wpedantic
        -Werror
    )
endfunction()

add_library(cakeos-present-engine STATIC
    src/engine.cpp
    src/semantic.cpp
)

target_include_directories(cakeos-present-engine
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include
    PRIVATE
        ${LIBREOFFICEKIT_INCLUDE_DIR}
)

target_compile_definitions(cakeos-present-engine PRIVATE
    LOK_USE_UNSTABLE_API
    CAKEOS_PRESENT_HAS_STRUCTURE_REQUEST=$<BOOL:${CAKEOS_PRESENT_HAS_STRUCTURE_REQUEST}>
)
target_link_libraries(cakeos-present-engine PRIVATE
    ${CMAKE_DL_LIBS}
    nlohmann_json::nlohmann_json
)
cakeos_enable_strict_warnings(cakeos-present-engine)

add_executable(cakeos-present-worker
    src/worker.cpp
)
target_include_directories(cakeos-present-worker PRIVATE ${LIBREOFFICEKIT_INCLUDE_DIR})
target_compile_definitions(cakeos-present-worker PRIVATE LOK_USE_UNSTABLE_API)
target_link_libraries(cakeos-present-worker PRIVATE
    cakeos-present-engine
    nlohmann_json::nlohmann_json
)
cakeos_enable_strict_warnings(cakeos-present-worker)

add_executable(cakeos-present-engine-smoke
    src/main.cpp
)
target_include_directories(cakeos-present-engine-smoke PRIVATE ${LIBREOFFICEKIT_INCLUDE_DIR})
target_link_libraries(cakeos-present-engine-smoke PRIVATE cakeos-present-engine)
cakeos_enable_strict_warnings(cakeos-present-engine-smoke)

add_executable(cakeos-present-engine-lifecycle-smoke
    tests/lifecycle_smoke.cpp
)
target_link_libraries(cakeos-present-engine-lifecycle-smoke PRIVATE cakeos-present-engine)
cakeos_enable_strict_warnings(cakeos-present-engine-lifecycle-smoke)

add_executable(cakeos-present-engine-reorder-smoke
    tests/reorder_smoke.cpp
)
target_link_libraries(cakeos-present-engine-reorder-smoke PRIVATE cakeos-present-engine)
cakeos_enable_strict_warnings(cakeos-present-engine-reorder-smoke)

add_executable(cakeos-present-engine-mixed-history-smoke
    tests/mixed_history_smoke.cpp
)
target_link_libraries(cakeos-present-engine-mixed-history-smoke PRIVATE cakeos-present-engine)
cakeos_enable_strict_warnings(cakeos-present-engine-mixed-history-smoke)

add_executable(cakeos-present-structure-probe
    tests/structure_probe.cpp
)
target_include_directories(cakeos-present-structure-probe PRIVATE ${LIBREOFFICEKIT_INCLUDE_DIR})
target_compile_definitions(cakeos-present-structure-probe PRIVATE
    LOK_USE_UNSTABLE_API
    CAKEOS_PRESENT_HAS_STRUCTURE_REQUEST=$<BOOL:${CAKEOS_PRESENT_HAS_STRUCTURE_REQUEST}>
)
target_link_libraries(cakeos-present-structure-probe PRIVATE
    cakeos-present-engine
    nlohmann_json::nlohmann_json
)
cakeos_enable_strict_warnings(cakeos-present-structure-probe)

add_executable(cakeos-present-text-history-probe
    tests/text_history_probe.cpp
)
target_link_libraries(cakeos-present-text-history-probe PRIVATE cakeos-present-engine)
cakeos_enable_strict_warnings(cakeos-present-text-history-probe)

add_executable(cakeos-present-geometry-probe
    tests/geometry_probe.cpp
)
target_include_directories(cakeos-present-geometry-probe PRIVATE ${LIBREOFFICEKIT_INCLUDE_DIR})
target_compile_definitions(cakeos-present-geometry-probe PRIVATE LOK_USE_UNSTABLE_API)
target_link_libraries(cakeos-present-geometry-probe PRIVATE cakeos-present-engine)
cakeos_enable_strict_warnings(cakeos-present-geometry-probe)

enable_testing()
add_executable(cakeos-present-engine-contract-tests
    tests/contract_tests.cpp
)
target_link_libraries(cakeos-present-engine-contract-tests PRIVATE cakeos-present-engine)
cakeos_enable_strict_warnings(cakeos-present-engine-contract-tests)
add_test(NAME present-engine-contract COMMAND cakeos-present-engine-contract-tests)
