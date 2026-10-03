#pragma once

#include <cstdlib>
#include <dlfcn.h>
#include <filesystem>
#include <iostream>

// Isolated distributions need their actual liblangtag data root registered before
// LibreOffice initializes locales. Normal system installations require no override.
inline bool configureNativeRuntimeData()
{
    const char* directory = std::getenv("LO_LANGTAG_DATA_PATH");
    if (!directory || !*directory) return true;
    if (!std::filesystem::is_regular_file(std::filesystem::path(directory) / "language-subtag-registry.xml"))
    {
        std::cerr << "FAIL: LO_LANGTAG_DATA_PATH has no language-subtag-registry.xml\n";
        return false;
    }
    // Keep the dependency loaded for the entire process, including LO cleanup.
    static void* library = dlopen("liblangtag.so.1", RTLD_NOW | RTLD_GLOBAL);
    if (!library)
    {
        std::cerr << "FAIL: liblangtag runtime is unavailable: " << dlerror() << '\n';
        return false;
    }
    using SetDataDirectory = void (*)(const char*);
    const auto configure = reinterpret_cast<SetDataDirectory>(dlsym(library, "lt_db_set_datadir"));
    if (!configure)
    {
        std::cerr << "FAIL: liblangtag public data-directory API is unavailable\n";
        return false;
    }
    configure(directory);
    return true;
}
