#include "cakeos/present/engine.hpp"

#include <array>
#include <iostream>
#include <stdexcept>
#include <string>
#include <string_view>

namespace {

template <std::size_t N>
void requireOrder(
    const cakeos::present::PresentEngine& engine,
    const std::array<std::string_view, N>& expected,
    const char* stage)
{
    const auto slides = engine.slides();
    if (slides.size() != expected.size()) {
        throw std::runtime_error(
            std::string(stage) + ": expected " + std::to_string(expected.size()) +
            " slides, got " + std::to_string(slides.size()));
    }

    for (std::size_t index = 0; index < expected.size(); ++index) {
        if (slides[index].name != expected[index]) {
            throw std::runtime_error(
                std::string(stage) + ": expected slide " + std::to_string(index) +
                " to be '" + std::string(expected[index]) + "', got '" + slides[index].name + "'");
        }
    }
}

constexpr std::array InitialOrder{
    std::string_view{"Alpha"},
    std::string_view{"Beta"},
    std::string_view{"Gamma"}
};

constexpr std::array DownwardOrder{
    std::string_view{"Beta"},
    std::string_view{"Gamma"},
    std::string_view{"Alpha"}
};

} // namespace

int main(int argc, char** argv)
{
    if (argc != 3) {
        std::cerr << "Usage: cakeos-present-engine-reorder-smoke <input.odp> <output.odp>\n";
        return 2;
    }

    try {
        cakeos::present::PresentEngine engine;
        engine.open(argv[1]);
        requireOrder(engine, InitialOrder, "initial order");

        engine.moveSlide(0, 2);
        requireOrder(engine, DownwardOrder, "move first slide to end");
        if (engine.currentSlide() != 2) {
            throw std::runtime_error("downward-moved slide did not become current at its destination");
        }

        engine.undo();
        requireOrder(engine, InitialOrder, "undo downward reorder");

        engine.redo();
        requireOrder(engine, DownwardOrder, "redo downward reorder");

        engine.moveSlide(2, 0);
        requireOrder(engine, InitialOrder, "move last slide to beginning");
        if (engine.currentSlide() != 0) {
            throw std::runtime_error("upward-moved slide did not become current at its destination");
        }

        engine.undo();
        requireOrder(engine, DownwardOrder, "undo upward reorder");

        engine.redo();
        requireOrder(engine, InitialOrder, "redo upward reorder");

        // Finish in a non-original ordering so save/reopen proves persistence,
        // rather than accidentally passing with an unchanged document.
        engine.moveSlide(0, 2);
        requireOrder(engine, DownwardOrder, "final downward reorder");

        engine.saveAs(argv[2], "odp");
        engine.close();
        engine.open(argv[2]);
        requireOrder(engine, DownwardOrder, "saved reorder reopen");

        std::cout << "slide_reorder=passed\n";
        std::cout << "slide_reorder_directions=down,up\n";
        std::cout << "slide_order=Beta,Gamma,Alpha\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "present-engine reorder smoke failed: " << error.what() << '\n';
        return 1;
    }
}
