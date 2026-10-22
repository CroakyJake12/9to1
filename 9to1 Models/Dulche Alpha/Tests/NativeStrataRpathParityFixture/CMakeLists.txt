cmake_minimum_required(VERSION 3.24)
project(rpath_parity_fixture LANGUAGES CXX)

# Mirror the production NativeStrata overlay's parity contract: build with the INSTALL RPATH so the
# inspected BUILD artifact is byte-identical to the INSTALLED artifact (no install-time RPATH
# rewrite). The install RPATH is left empty, matching the engine/worker/K-quant DSO closure where
# the optional DSO is loaded from the engine's own folder by explicit path.
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_BUILD_WITH_INSTALL_RPATH ON)

add_executable(rpath_probe main.cpp)
install(TARGETS rpath_probe RUNTIME DESTINATION bin)
