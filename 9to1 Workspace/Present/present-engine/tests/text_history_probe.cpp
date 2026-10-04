#include "cakeos/present/engine.hpp"

#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <string>
#include <string_view>

namespace {

bool snapshotContains(
    const cakeos::present::PresentEngine& engine,
    const std::filesystem::path& path,
    std::string_view expected)
{
    for (const auto& element : engine.elementSnapshot(path.string(), 0)) {
        for (const auto& text : element.text) {
            if (text == expected) {
                return true;
            }
        }
    }
    return false;
}

} // namespace

int main(int argc, char** argv)
{
    if (argc != 4) {
        std::cerr << "Usage: cakeos-present-text-history-probe INPUT.odp UPDATED.odp UNDO.odp\n";
        return 2;
    }

    try {
        const std::filesystem::path source = argv[1];
        const std::filesystem::path updated = argv[2];
        const std::filesystem::path undone = argv[3];
        const std::filesystem::path redone = undone.parent_path() /
            (undone.stem().string() + "-redo.odp");
        std::error_code ignored;
        std::filesystem::remove(updated, ignored);
        std::filesystem::remove(undone, ignored);
        std::filesystem::remove(redone, ignored);

        cakeos::present::PresentEngine engine;
        if (!engine.supportsElementSnapshots()) {
            std::cout << "text_transform=unavailable\n";
            return 0;
        }

        engine.open(source.string());
        engine.setCurrentSlide(0);
        engine.postUnoCommand(
            ".uno:TransformDocumentStructure",
            R"json({"DataJson":{"type":"string","value":"{\"Transforms\":{\"SlideCommands\":[{\"SetText.0\":\"CakeOS Typed\"}]}}"}})json",
            true);
        engine.saveAs(updated.string(), "odp");
        if (!snapshotContains(engine, updated, "CakeOS Typed")) {
            throw std::runtime_error("SetText.0 did not persist CakeOS Typed");
        }
        std::cout << "text_transform=passed\n";

        engine.undo();
        engine.saveAs(undone.string(), "odp");
        const bool nativeUndo = snapshotContains(engine, undone, "Alpha");
        std::cout << "native_text_undo=" << (nativeUndo ? "supported" : "unsupported") << '\n';

        engine.redo();
        engine.saveAs(redone.string(), "odp");
        const bool nativeRedo = snapshotContains(engine, redone, "CakeOS Typed");
        std::cout << "native_text_redo=" << (nativeRedo ? "supported" : "unsupported") << '\n';
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "present text history probe failed: " << error.what() << '\n';
        return 1;
    }
}
