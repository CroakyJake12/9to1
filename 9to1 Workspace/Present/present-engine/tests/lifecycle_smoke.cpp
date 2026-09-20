#include "cakeos/present/engine.hpp"

#include <iostream>
#include <stdexcept>
#include <string>

namespace {

void requireSlideCount(const cakeos::present::PresentEngine& engine, std::size_t expected, const char* stage)
{
    const auto actual = engine.slides().size();
    if (actual != expected) {
        throw std::runtime_error(
            std::string(stage) + ": expected " + std::to_string(expected) +
            " slides, got " + std::to_string(actual));
    }
}

} // namespace

int main(int argc, char** argv)
{
    if (argc != 3 && argc != 4) {
        std::cerr << "Usage: cakeos-present-engine-lifecycle-smoke <input.odp> <output.odp> [output.pptx]\n";
        return 2;
    }

    try {
        cakeos::present::PresentEngine engine;
        engine.open(argv[1]);
        requireSlideCount(engine, 1U, "initial load");

        engine.addSlideAfter(0);
        requireSlideCount(engine, 2U, "add slide");

        engine.undo();
        requireSlideCount(engine, 1U, "undo add slide");

        engine.redo();
        requireSlideCount(engine, 2U, "redo add slide");

        engine.duplicateSlide(0);
        requireSlideCount(engine, 3U, "duplicate slide");

        engine.deleteSlide(2);
        requireSlideCount(engine, 2U, "delete slide");

        engine.saveAs(argv[2], "odp");
        engine.close();

        engine.open(argv[2]);
        requireSlideCount(engine, 2U, "saved ODP reopen");

        if (argc == 4) {
            engine.saveAs(argv[3], "pptx");
            engine.close();
            engine.open(argv[3]);
            requireSlideCount(engine, 2U, "saved PPTX reopen");
            std::cout << "pptx_roundtrip=passed\n";
        }

        std::cout << "semantic_lifecycle=passed\n";
        std::cout << "saved_slides=2\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "present-engine lifecycle smoke failed: " << error.what() << '\n';
        return 1;
    }
}
