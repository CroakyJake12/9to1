#pragma once

#include <cstdint>
#include <functional>
#include <memory>
#include <string>
#include <string_view>
#include <vector>

namespace cakeos::present {

struct EngineOptions {
    std::string libreOfficeProgramPath{ "/usr/lib/libreoffice/program" };
    std::string userProfileUrl;
};

struct SlideInfo {
    int index{};
    std::string name;
    std::string hash;
};

struct SlideExtent {
    long widthTwips{};
    long heightTwips{};
};

// A semantic object discovered from a saved presentation snapshot. objectIndex
// is only meaningful for that exact snapshot and must not be treated as a
// persistent document identity by HUI/GenUI.
struct ElementSnapshot {
    int slideIndex{};
    int objectIndex{};
    std::vector<std::string> text;
};

enum class PixelFormat {
    Rgba,
    Bgra
};

struct TileRequest {
    int slideIndex{};
    int pixelWidth{};
    int pixelHeight{};
    int tileXTwips{};
    int tileYTwips{};
    int tileWidthTwips{};
    int tileHeightTwips{};
};

struct RenderedTile {
    int pixelWidth{};
    int pixelHeight{};
    PixelFormat pixelFormat{PixelFormat::Bgra};
    std::vector<std::uint8_t> pixels;
};

struct EngineEvent {
    int upstreamType{};
    std::string payload;
};

enum class KeyEventType {
    Input,
    Up
};

enum class MouseEventType {
    ButtonDown,
    ButtonUp,
    Move
};

using EventCallback = std::function<void(const EngineEvent&)>;

class PresentEngine final {
public:
    explicit PresentEngine(EngineOptions options = {});
    ~PresentEngine();

    PresentEngine(const PresentEngine&) = delete;
    PresentEngine& operator=(const PresentEngine&) = delete;
    PresentEngine(PresentEngine&&) noexcept;
    PresentEngine& operator=(PresentEngine&&) noexcept;

    void open(std::string_view documentPathOrUrl);
    void close() noexcept;
    [[nodiscard]] bool isOpen() const noexcept;

    [[nodiscard]] std::vector<SlideInfo> slides() const;
    [[nodiscard]] int currentSlide() const;
    void setCurrentSlide(int slideIndex);
    [[nodiscard]] SlideExtent slideExtent(int slideIndex) const;

    // Snapshot-only semantic discovery. This reads the saved file supplied by
    // the caller; it does not inspect unsaved in-memory edits. Callers must
    // invalidate any returned objectIndex values after document mutation.
    [[nodiscard]] bool supportsElementSnapshots() const noexcept;
    [[nodiscard]] std::vector<ElementSnapshot> elementSnapshot(
        std::string_view documentPathOrUrl,
        int slideIndex) const;

    // Resolve a snapshot-scoped semantic object into the live Impress view.
    // LibreOfficeKit reports the resulting selection geometry through the
    // normal engine event callback. The object index remains snapshot-only.
    void selectElement(
        std::string_view snapshotPathOrUrl,
        int slideIndex,
        int objectIndex);
    void clearElementSelection(int slideIndex, int objectIndex);

    // Whole-object single-line plain-text replacement. The saved snapshot is
    // used to verify that objectIndex still identifies exactly one text value
    // before the live document is mutated. Rich/multi-part/multiline text is
    // deliberately not flattened by this first semantic write operation.
    // Returns false when the requested text already matches the snapshot.
    bool replaceElementText(
        std::string_view snapshotPathOrUrl,
        int slideIndex,
        int objectIndex,
        std::string_view text);

    // CakeOS-owned semantic slide operations. These deliberately avoid
    // exposing LibreOffice command names or selection mechanics to HUI/GenUI.
    void addSlideAfter(int slideIndex);
    void duplicateSlide(int slideIndex);
    void deleteSlide(int slideIndex);
    void moveSlide(int fromIndex, int toIndex);
    void undo();
    void redo();

    [[nodiscard]] RenderedTile renderTile(const TileRequest& request);

    void postKeyEvent(KeyEventType type, int charCode, int keyCode);
    void postMouseEvent(MouseEventType type, int xTwips, int yTwips, int clickCount, int buttons, int modifiers);
    void postUnoCommand(std::string_view command, std::string_view jsonArguments = {}, bool notifyWhenFinished = false);

    void saveAs(std::string_view destinationPathOrUrl, std::string_view format = {}, std::string_view filterOptions = {});
    void setEventCallback(EventCallback callback);

    [[nodiscard]] static std::string pathToFileUrl(std::string_view pathOrUrl);

private:
    void applySlideMove(int fromIndex, int toIndex);
    void applyElementText(int slideIndex, int objectIndex, std::string_view text);
    void recordNativeMutation();
    void recordTextMutation(
        int slideIndex,
        int objectIndex,
        std::string beforeText,
        std::string afterText);

    struct Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace cakeos::present
