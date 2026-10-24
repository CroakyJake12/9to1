#include "cakeos/present/engine.hpp"

#include <filesystem>
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

        bool invalidSlideRejected = false;
        try {
            engine.deleteSlide(1);
        } catch (const std::out_of_range&) {
            invalidSlideRejected = true;
        }
        if (!invalidSlideRejected) {
            throw std::runtime_error("delete accepted or misclassified an out-of-range slide index");
        }
        bool negativeSlideRejected = false;
        try {
            engine.deleteSlide(-1);
        } catch (const std::out_of_range&) {
            negativeSlideRejected = true;
        }
        if (!negativeSlideRejected) {
            throw std::runtime_error("delete accepted a negative slide index");
        }
        bool finalSlideProtected = false;
        try {
            engine.deleteSlide(0);
        } catch (const std::out_of_range&) {
            throw std::runtime_error("delete misclassified a valid final slide index");
        } catch (const std::logic_error&) {
            finalSlideProtected = true;
        }
        if (!finalSlideProtected) {
            throw std::runtime_error("delete removed the presentation's final slide");
        }

        engine.addSlideAfter(0);
        requireSlideCount(engine, 2U, "add slide");

        bool missingOpenRejected = false;
        const std::string missingPath = std::string(argv[1]) + ".luna-missing";
        if (std::filesystem::exists(missingPath)) {
            throw std::runtime_error("failed-open fixture path unexpectedly exists");
        }
        try {
            engine.open(missingPath);
        } catch (const std::exception&) {
            missingOpenRejected = true;
        }
        if (!missingOpenRejected || !engine.isOpen()) {
            throw std::runtime_error("failed open did not preserve the active presentation");
        }
        requireSlideCount(engine, 2U, "failed open preserves active presentation");

        engine.undo();
        requireSlideCount(engine, 1U, "undo add slide");

        engine.redo();
        requireSlideCount(engine, 2U, "redo add slide");

        engine.duplicateSlide(0);
        requireSlideCount(engine, 3U, "duplicate slide");

        engine.deleteSlide(2);
        requireSlideCount(engine, 2U, "delete slide");

        engine.saveAs(argv[2], "odp");
        if (engine.supportsElementSnapshots()) {
            const auto savedSnapshot = engine.elementSnapshot(argv[2], 0);
            if (!savedSnapshot.empty()) {
                const auto& element = savedSnapshot.front();
                engine.selectElement(argv[2], element.slideIndex, element.objectIndex);
                engine.clearElementSelection(element.slideIndex, element.objectIndex);
                std::cout << "save_as_snapshot_matches_active_document=passed\n";
            }
        }
        engine.close();

        if (engine.supportsElementSnapshots()) {
            bool closedDocumentRejected = false;
            try {
                engine.selectElement(argv[2], 0, 0);
            } catch (const std::logic_error&) {
                closedDocumentRejected = true;
            }
            if (!closedDocumentRejected) {
                throw std::runtime_error("semantic selection remained available after close");
            }
        }

        engine.open(argv[2]);
        requireSlideCount(engine, 2U, "saved ODP reopen");

        if (engine.supportsElementSnapshots()) {
            const auto reopenedSnapshot = engine.elementSnapshot(argv[2], 0);
            if (!reopenedSnapshot.empty()) {
                const auto urlSnapshot = cakeos::present::PresentEngine::pathToFileUrl(argv[2]);
                const auto& element = reopenedSnapshot.front();
                engine.selectElement(urlSnapshot, element.slideIndex, element.objectIndex);
                engine.clearElementSelection(element.slideIndex, element.objectIndex);
                std::cout << "same_document_url_snapshot_accepted=passed\n";
            }

            bool staleSnapshotRejected = false;
            try {
                (void)engine.replaceElementText(argv[1], 0, 0, "must not cross documents");
            } catch (const std::invalid_argument&) {
                staleSnapshotRejected = true;
            }
            if (!staleSnapshotRejected) {
                throw std::runtime_error(
                    "semantic mutation accepted a snapshot from a different presentation");
            }
            bool staleSelectionRejected = false;
            try {
                engine.selectElement(argv[1], 0, 0);
            } catch (const std::invalid_argument&) {
                staleSelectionRejected = true;
            }
            if (!staleSelectionRejected) {
                throw std::runtime_error(
                    "semantic selection accepted a snapshot from a different presentation");
            }
            std::cout << "cross_document_snapshot_rejected=passed\n";
        }

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
