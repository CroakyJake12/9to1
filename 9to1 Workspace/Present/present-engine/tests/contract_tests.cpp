#include "cakeos/present/engine.hpp"

#include <cassert>
#include <filesystem>
#include <string>

int main()
{
    using cakeos::present::PresentEngine;

    const std::string remote = "file:///tmp/already-url.odp";
    assert(PresentEngine::pathToFileUrl(remote) == remote);

    const auto local = PresentEngine::pathToFileUrl("deck with spaces.odp");
    assert(local.rfind("file://", 0) == 0);
    assert(local.find("deck%20with%20spaces.odp") != std::string::npos);

    const auto nested = PresentEngine::pathToFileUrl("folder/deck.odp");
    assert(nested.rfind("file://", 0) == 0);
    assert(nested.find("folder/deck.odp") != std::string::npos);

    return 0;
}
