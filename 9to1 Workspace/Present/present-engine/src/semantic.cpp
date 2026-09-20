#include "cakeos/present/engine.hpp"

#include <algorithm>
#include <stdexcept>
#include <string>

namespace cakeos::present {

namespace {

const ElementSnapshot& requireSnapshotElement(
    const std::vector<ElementSnapshot>& elements,
    int objectIndex)
{
    const auto match = std::find_if(elements.begin(), elements.end(), [objectIndex](const ElementSnapshot& element) {
        return element.objectIndex == objectIndex;
    });
    if (match == elements.end()) {
        throw std::out_of_range("element reference does not exist in the saved presentation snapshot");
    }
    return *match;
}

} // namespace

void PresentEngine::selectElement(
    std::string_view snapshotPathOrUrl,
    int slideIndex,
    int objectIndex)
{
    if (!supportsElementSnapshots()) {
        throw std::runtime_error(
            "this LibreOfficeKit runtime does not support semantic element snapshots");
    }
    if (snapshotPathOrUrl.empty()) {
        throw std::invalid_argument("snapshot path must not be empty");
    }
    if (objectIndex < 0) {
        throw std::out_of_range("object index must not be negative");
    }

    // The worker already requires a fresh snapshot-scoped reference before
    // calling this method. Do not call extractDocumentStructureRequest here:
    // that office-level extraction loads the saved file again and can replace
    // the active component immediately before MarkObject, suppressing the
    // live view's GRAPHIC_SELECTION callback. Mark the active Impress view
    // directly; HUI correlation remains snapshot-scoped in the worker.
    setCurrentSlide(slideIndex);
    postUnoCommand(
        ".uno:TransformDocumentStructure",
        "{\"DataJson\":{\"type\":\"string\",\"value\":\"{\\\"Transforms\\\":{\\\"SlideCommands\\\":[{\\\"MarkObject\\\":" +
            std::to_string(objectIndex) + "}]}}\"}}",
        true);
}

void PresentEngine::clearElementSelection(int slideIndex, int objectIndex)
{
    if (objectIndex < 0) {
        throw std::out_of_range("object index must not be negative");
    }
    setCurrentSlide(slideIndex);
    postUnoCommand(
        ".uno:TransformDocumentStructure",
        "{\"DataJson\":{\"type\":\"string\",\"value\":\"{\\\"Transforms\\\":{\\\"SlideCommands\\\":[{\\\"UnMarkObject\\\":" +
            std::to_string(objectIndex) + "}]}}\"}}",
        true);
}

bool PresentEngine::replaceElementText(
    std::string_view snapshotPathOrUrl,
    int slideIndex,
    int objectIndex,
    std::string_view text)
{
    if (!supportsElementSnapshots()) {
        throw std::runtime_error(
            "this LibreOfficeKit runtime does not support semantic element snapshots");
    }
    if (objectIndex < 0) {
        throw std::out_of_range("object index must not be negative");
    }
    if (text.find('\n') != std::string_view::npos || text.find('\r') != std::string_view::npos) {
        throw std::invalid_argument(
            "whole-object text replacement currently supports single-line plain text only");
    }

    const auto elements = elementSnapshot(snapshotPathOrUrl, slideIndex);
    const ElementSnapshot& match = requireSnapshotElement(elements, objectIndex);
    if (match.text.size() != 1U) {
        throw std::logic_error(
            "whole-object text replacement currently requires exactly one text value; rich or multi-part text is not supported");
    }

    const std::string beforeText = match.text.front();
    const std::string afterText(text);
    if (beforeText == afterText) {
        return false;
    }

    applyElementText(slideIndex, objectIndex, afterText);
    recordTextMutation(slideIndex, objectIndex, beforeText, afterText);
    return true;
}

void PresentEngine::addSlideAfter(int slideIndex)
{
    const auto before = slides().size();
    setCurrentSlide(slideIndex);
    postUnoCommand(".uno:InsertPage", {}, true);
    if (slides().size() != before + 1U) {
        throw std::runtime_error("LibreOffice did not add the requested slide");
    }
    recordNativeMutation();
}

void PresentEngine::duplicateSlide(int slideIndex)
{
    const auto before = slides().size();
    setCurrentSlide(slideIndex);
    postUnoCommand(".uno:DuplicatePage", {}, true);
    if (slides().size() != before + 1U) {
        throw std::runtime_error("LibreOffice did not duplicate the requested slide");
    }
    recordNativeMutation();
}

void PresentEngine::deleteSlide(int slideIndex)
{
    const auto slideList = slides();
    if (slideList.size() <= 1U) {
        throw std::logic_error("a presentation must keep at least one slide");
    }
    setCurrentSlide(slideIndex);
    postUnoCommand(".uno:DeletePage", {}, true);
    if (slides().size() + 1U != slideList.size()) {
        throw std::runtime_error("LibreOffice did not delete the requested slide");
    }
    recordNativeMutation();
}

} // namespace cakeos::present
