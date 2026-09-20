#include "cakeos/present/engine.hpp"

#include <LibreOfficeKit/LibreOfficeKitEnums.h>

#include <array>
#include <charconv>
#include <chrono>
#include <cctype>
#include <iostream>
#include <mutex>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>

namespace {

std::optional<long> parseLong(std::string_view value)
{
    while (!value.empty() && std::isspace(static_cast<unsigned char>(value.front())) != 0) {
        value.remove_prefix(1U);
    }
    while (!value.empty() && std::isspace(static_cast<unsigned char>(value.back())) != 0) {
        value.remove_suffix(1U);
    }
    if (value.empty()) {
        return std::nullopt;
    }

    long parsed = 0;
    const auto result = std::from_chars(value.data(), value.data() + value.size(), parsed);
    if (result.ec != std::errc{} || result.ptr != value.data() + value.size()) {
        return std::nullopt;
    }
    return parsed;
}

std::optional<std::array<long, 4>> parseSelectionRect(std::string_view payload)
{
    if (payload.empty() || payload == "EMPTY") {
        return std::nullopt;
    }

    std::array<long, 4> values{};
    std::size_t offset = 0U;
    for (std::size_t index = 0U; index < values.size(); ++index) {
        const auto comma = payload.find(',', offset);
        if (comma == std::string_view::npos) {
            return std::nullopt;
        }
        const auto parsed = parseLong(payload.substr(offset, comma - offset));
        if (!parsed.has_value()) {
            return std::nullopt;
        }
        values[index] = *parsed;
        offset = comma + 1U;
    }
    return values;
}

} // namespace

int main(int argc, char** argv)
{
    if (argc != 2) {
        std::cerr << "Usage: cakeos-present-geometry-probe INPUT.odp\n";
        return 2;
    }

    try {
        cakeos::present::PresentEngine engine;
        std::mutex selectionMutex;
        std::string graphicSelection;
        engine.setEventCallback([&](const cakeos::present::EngineEvent& event) {
            if (event.upstreamType != LOK_CALLBACK_GRAPHIC_SELECTION ||
                event.payload.empty() || event.payload == "EMPTY") {
                return;
            }
            std::scoped_lock lock(selectionMutex);
            graphicSelection = event.payload;
        });

        engine.open(argv[1]);
        engine.setCurrentSlide(0);
        engine.postUnoCommand(
            ".uno:TransformDocumentStructure",
            R"json({"DataJson":{"type":"string","value":"{\"Transforms\":{\"SlideCommands\":[{\"MarkObject\":0}]}}"}})json",
            true);

        std::optional<std::array<long, 4>> rect;
        for (int attempt = 0; attempt < 50 && !rect.has_value(); ++attempt) {
            {
                std::scoped_lock lock(selectionMutex);
                rect = parseSelectionRect(graphicSelection);
            }
            if (!rect.has_value()) {
                std::this_thread::sleep_for(std::chrono::milliseconds(20));
            }
        }
        if (!rect.has_value()) {
            throw std::runtime_error(
                "marking snapshot object 0 did not produce a graphic-selection geometry callback");
        }
        if ((*rect)[2] <= 0 || (*rect)[3] <= 0) {
            throw std::runtime_error("graphic-selection callback returned non-positive object bounds");
        }

        engine.postUnoCommand(
            ".uno:TransformDocumentStructure",
            R"json({"DataJson":{"type":"string","value":"{\"Transforms\":{\"SlideCommands\":[{\"UnMarkObject\":0}]}}"}})json",
            true);

        std::cout << "object_geometry=passed\n";
        std::cout << "object_ref=slide:0/object:0\n";
        std::cout << "object_rect_twips="
                  << (*rect)[0] << ',' << (*rect)[1] << ','
                  << (*rect)[2] << ',' << (*rect)[3] << '\n';
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "present geometry probe failed: " << error.what() << '\n';
        return 1;
    }
}
