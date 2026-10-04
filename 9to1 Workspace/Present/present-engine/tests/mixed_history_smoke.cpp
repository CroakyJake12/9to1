#include "cakeos/present/engine.hpp"

#include <iostream>
#include <stdexcept>
#include <string>

namespace {

void requireSlides(const cakeos::present::PresentEngine& engine, std::size_t count, const char* stage)
{
    const auto slides = engine.slides();
    if (slides.size() != count) {
        throw std::runtime_error(
            std::string(stage) + ": expected " + std::to_string(count) +
            " slides, got " + std::to_string(slides.size()));
    }
}

void requireFirstSlide(const cakeos::present::PresentEngine& engine, const std::string& name, const char* stage)
{
    const auto slides = engine.slides();
    if (slides.empty() || slides.front().name != name) {
        throw std::runtime_error(std::string(stage) + ": unexpected first slide");
    }
}

} // namespace

int main(int argc, char** argv)
{
    if (argc != 2) {
        std::cerr << "Usage: cakeos-present-engine-mixed-history-smoke <input.odp>\n";
        return 2;
    }

    try {
        cakeos::present::PresentEngine engine;
        engine.open(argv[1]);
        requireSlides(engine, 3U, "initial");
        requireFirstSlide(engine, "Alpha", "initial");

        // Native mutation followed by a CakeOS-journalled structural move.
        engine.addSlideAfter(2);
        requireSlides(engine, 4U, "after add");
        engine.moveSlide(0, 2);
        requireFirstSlide(engine, "Beta", "after move");

        engine.undo();
        requireFirstSlide(engine, "Alpha", "undo move");
        requireSlides(engine, 4U, "undo move count");

        engine.undo();
        requireSlides(engine, 3U, "undo native add");
        requireFirstSlide(engine, "Alpha", "undo native add order");

        engine.redo();
        requireSlides(engine, 4U, "redo native add");
        engine.redo();
        requireFirstSlide(engine, "Beta", "redo move");

        // Undo the move, then branch history with a different move. Redo must
        // not resurrect the abandoned branch.
        engine.undo();
        requireFirstSlide(engine, "Alpha", "undo before branch");
        engine.moveSlide(2, 0);
        requireFirstSlide(engine, "Gamma", "branched move");
        engine.redo();
        requireFirstSlide(engine, "Gamma", "redo after branch truncation");

        std::cout << "mixed_history=passed\n";
        std::cout << "mixed_history_redo_branch=truncated\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "present-engine mixed history smoke failed: " << error.what() << '\n';
        return 1;
    }
}
