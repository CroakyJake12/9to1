// Tiny RPATH-parity probe for the NativeStrata build/install byte-equality fixture.
// It links only the C++ runtime and carries no non-toolchain runtime path, so under
// CMAKE_BUILD_WITH_INSTALL_RPATH=ON the built and installed binaries are byte-identical.
#include <cstdio>

int main() {
    std::printf("rpath-parity-fixture-ok\n");
    return 0;
}
