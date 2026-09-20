#include "cakeos/present/engine.hpp"

#include <LibreOfficeKit/LibreOfficeKit.hxx>
#include <nlohmann/json.hpp>

#include <filesystem>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>

#ifndef CAKEOS_PRESENT_HAS_STRUCTURE_REQUEST
#define CAKEOS_PRESENT_HAS_STRUCTURE_REQUEST 0
#endif

#if CAKEOS_PRESENT_HAS_STRUCTURE_REQUEST
namespace {

using Json = nlohmann::json;

bool containsString(const Json& value, const std::string& expected)
{
    if (value.is_string()) {
        return value.get<std::string>() == expected;
    }
    if (value.is_array()) {
        for (const auto& child : value) {
            if (containsString(child, expected)) {
                return true;
            }
        }
    } else if (value.is_object()) {
        for (const auto& [key, child] : value.items()) {
            (void)key;
            if (containsString(child, expected)) {
                return true;
            }
        }
    }
    return false;
}

const Json* findObjectMemberRecursive(const Json& value, const std::string& member)
{
    if (value.is_object()) {
        const auto direct = value.find(member);
        if (direct != value.end()) {
            return &(*direct);
        }
        for (const auto& [key, child] : value.items()) {
            (void)key;
            if (const Json* found = findObjectMemberRecursive(child, member); found != nullptr) {
                return found;
            }
        }
    } else if (value.is_array()) {
        for (const auto& child : value) {
            if (const Json* found = findObjectMemberRecursive(child, member); found != nullptr) {
                return found;
            }
        }
    }
    return nullptr;
}

std::string rootShape(const Json& value)
{
    if (value.is_object()) {
        std::string keys;
        for (const auto& [key, child] : value.items()) {
            (void)child;
            if (!keys.empty()) {
                keys += ',';
            }
            keys += key;
        }
        return "object:" + keys;
    }
    if (value.is_array()) {
        return "array:" + std::to_string(value.size());
    }
    return value.type_name();
}

} // namespace
#endif

int main(int argc, char** argv)
{
    if (argc != 2) {
        std::cerr << "Usage: cakeos-present-structure-probe <input.odp>\n";
        return 2;
    }

#if !CAKEOS_PRESENT_HAS_STRUCTURE_REQUEST
    (void)argv;
    std::cout << "structure_request=unavailable\n";
    return 0;
#else
    try {
        const auto profile = std::filesystem::temp_directory_path() / "cakeos-present-structure-probe-profile";
        std::error_code ignored;
        std::filesystem::remove_all(profile, ignored);
        std::filesystem::create_directories(profile);
        const std::string profileUrl = cakeos::present::PresentEngine::pathToFileUrl(profile.string());

        std::unique_ptr<lok::Office> office(lok::lok_cpp_init(
            "/usr/lib/libreoffice/program",
            profileUrl.c_str()));
        if (!office) {
            throw std::runtime_error("failed to initialise LibreOfficeKit");
        }

        char* raw = office->extractDocumentStructureRequest(argv[1], "slides");
        if (raw == nullptr || *raw == '\0') {
            if (raw != nullptr) {
                office->freeError(raw);
            }
            throw std::runtime_error("dedicated structure request returned no data");
        }
        const std::string structureText(raw);
        office->freeError(raw);

        const Json structure = Json::parse(structureText);
        const Json* slides = findObjectMemberRecursive(structure, "Slides");
        if (slides == nullptr || !slides->is_object()) {
            throw std::runtime_error(
                "document structure has no Slides object; root=" + rootShape(structure));
        }
        if (!slides->contains("Slide 0")) {
            throw std::runtime_error("document structure has no Slide 0");
        }
        const Json& slide0 = slides->at("Slide 0");
        if (!slide0.contains("Objects") || !slide0.at("Objects").is_object()) {
            throw std::runtime_error("Slide 0 has no semantic Objects inventory");
        }
        const auto objectCount = slide0.at("Objects").size();
        if (objectCount == 0U) {
            throw std::runtime_error("Slide 0 semantic Objects inventory is empty");
        }
        if (!containsString(slide0.at("Objects"), "Alpha")) {
            throw std::runtime_error("semantic object inventory does not expose the Alpha title text");
        }

        std::cout << "structure_request=dedicated-office-api\n";
        std::cout << "structure_inventory=passed\n";
        std::cout << "structure_slide0_objects=" << objectCount << '\n';
        std::cout << "structure_text=Alpha\n";

        office.reset();
        std::filesystem::remove_all(profile, ignored);
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "present structure probe failed: " << error.what() << '\n';
        return 1;
    }
#endif
}
