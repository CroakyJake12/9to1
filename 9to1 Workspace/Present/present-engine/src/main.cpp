#include "cakeos/present/engine.hpp"

#include <cstddef>
#include <cstdint>
#include <fstream>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>
#include <utility>

namespace {

void writePpm(const std::string& path, const cakeos::present::RenderedTile& tile)
{
    std::ofstream output(path, std::ios::binary | std::ios::trunc);
    if (!output) {
        throw std::runtime_error("failed to open output image: " + path);
    }

    output << "P6\n" << tile.pixelWidth << ' ' << tile.pixelHeight << "\n255\n";
    const std::size_t pixels = static_cast<std::size_t>(tile.pixelWidth) *
        static_cast<std::size_t>(tile.pixelHeight);
    for (std::size_t index = 0; index < pixels; ++index) {
        const auto offset = index * 4U;
        const std::uint8_t red = tile.pixelFormat == cakeos::present::PixelFormat::Rgba
            ? tile.pixels[offset]
            : tile.pixels[offset + 2U];
        const std::uint8_t green = tile.pixels[offset + 1U];
        const std::uint8_t blue = tile.pixelFormat == cakeos::present::PixelFormat::Rgba
            ? tile.pixels[offset + 2U]
            : tile.pixels[offset];
        output.put(static_cast<char>(red));
        output.put(static_cast<char>(green));
        output.put(static_cast<char>(blue));
    }
    if (!output) {
        throw std::runtime_error("failed while writing rendered slide image");
    }
}

int parseSlideIndex(const char* value)
{
    std::size_t consumed = 0;
    const std::string text(value);
    const int index = std::stoi(text, &consumed);
    if (consumed != text.size() || index < 0) {
        throw std::invalid_argument("slide index must be a non-negative integer");
    }
    return index;
}

int checkedTwips(long value)
{
    if (value <= 0 || value > std::numeric_limits<int>::max()) {
        throw std::overflow_error("slide extent cannot be represented by the tiled-rendering API");
    }
    return static_cast<int>(value);
}

} // namespace

int main(int argc, char** argv)
{
    if (argc < 3 || argc > 5) {
        std::cerr << "Usage: cakeos-present-engine-smoke <input.odp|pptx> <output.ppm> [slide-index] [libreoffice-program-dir]\n";
        return 2;
    }

    try {
        cakeos::present::EngineOptions options;
        if (argc == 5) {
            options.libreOfficeProgramPath = argv[4];
        }

        cakeos::present::PresentEngine engine(std::move(options));
        engine.open(argv[1]);

        const auto slideList = engine.slides();
        if (slideList.empty()) {
            throw std::runtime_error("presentation has no slides");
        }

        const int slideIndex = argc >= 4 ? parseSlideIndex(argv[3]) : 0;
        if (slideIndex >= static_cast<int>(slideList.size())) {
            throw std::out_of_range("requested slide does not exist");
        }

        const auto extent = engine.slideExtent(slideIndex);
        cakeos::present::TileRequest request;
        request.slideIndex = slideIndex;
        request.pixelWidth = 1280;
        request.pixelHeight = 720;
        request.tileWidthTwips = checkedTwips(extent.widthTwips);
        request.tileHeightTwips = checkedTwips(extent.heightTwips);

        const auto tile = engine.renderTile(request);
        writePpm(argv[2], tile);

        std::cout << "slides=" << slideList.size() << '\n';
        std::cout << "selected_slide=" << slideIndex << '\n';
        std::cout << "slide_name=" << slideList[static_cast<std::size_t>(slideIndex)].name << '\n';
        std::cout << "extent_twips=" << extent.widthTwips << 'x' << extent.heightTwips << '\n';
        std::cout << "pixels=" << tile.pixelWidth << 'x' << tile.pixelHeight << '\n';
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "present-engine smoke failed: " << error.what() << '\n';
        return 1;
    }
}
