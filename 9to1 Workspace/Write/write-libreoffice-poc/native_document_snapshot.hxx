#pragma once

#include <cstdlib>
#include <filesystem>
#include <stdexcept>
#include <string>
#include <vector>

// The disposable LOK process owns its profile and native working copy. Never
// give LOK the shared source path: process-bound teardown leaves its lockfile,
// which would otherwise prevent later helpers from editing that same source.
inline std::filesystem::path createNativeDocumentSnapshot(
    const std::filesystem::path& source, const std::filesystem::path& profile)
{
    const auto pattern = (profile / "writer-document-XXXXXX").string();
    std::vector<char> buffer(pattern.begin(), pattern.end());
    buffer.push_back('\0');
    const auto* directory = ::mkdtemp(buffer.data());
    if (!directory) throw std::runtime_error("Could not create private Writer document directory");
    const auto snapshot = std::filesystem::path(directory) / ("source" + source.extension().string());
    if (!std::filesystem::copy_file(source, snapshot))
        throw std::runtime_error("Could not snapshot the authorised Writer source");
    return snapshot;
}
