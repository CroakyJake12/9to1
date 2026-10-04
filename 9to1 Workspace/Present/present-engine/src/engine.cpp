#include "cakeos/present/engine.hpp"

#include <LibreOfficeKit/LibreOfficeKit.hxx>
#include <LibreOfficeKit/LibreOfficeKitEnums.h>
#include <nlohmann/json.hpp>

#include <algorithm>
#include <charconv>
#include <chrono>
#include <cmath>
#include <cctype>
#include <filesystem>
#include <limits>
#include <optional>
#include <stdexcept>
#include <system_error>
#include <utility>

#if defined(__linux__)
#include <unistd.h>
#endif

#ifndef CAKEOS_PRESENT_HAS_STRUCTURE_REQUEST
#define CAKEOS_PRESENT_HAS_STRUCTURE_REQUEST 0
#endif

namespace cakeos::present {
namespace {

std::string requireNonEmpty(std::string_view value, const char* name)
{
    if (value.empty()) {
        throw std::invalid_argument(std::string(name) + " must not be empty");
    }
    return std::string(value);
}

std::string percentEncodePath(std::string_view value)
{
    static constexpr char Hex[] = "0123456789ABCDEF";
    std::string encoded;
    encoded.reserve(value.size());

    for (const unsigned char ch : value) {
        const bool safe =
            (ch >= 'a' && ch <= 'z') ||
            (ch >= 'A' && ch <= 'Z') ||
            (ch >= '0' && ch <= '9') ||
            ch == '-' || ch == '_' || ch == '.' || ch == '~' || ch == '/';
        if (safe) {
            encoded.push_back(static_cast<char>(ch));
        } else {
            encoded.push_back('%');
            encoded.push_back(Hex[(ch >> 4U) & 0x0FU]);
            encoded.push_back(Hex[ch & 0x0FU]);
        }
    }
    return encoded;
}

std::string makePrivateProfileUrl()
{
    const auto stamp = std::chrono::steady_clock::now().time_since_epoch().count();
#if defined(__linux__)
    const auto pid = static_cast<long long>(::getpid());
#else
    const auto pid = 0LL;
#endif
    auto path = std::filesystem::temp_directory_path() /
        ("cakeos-present-" + std::to_string(pid) + "-" + std::to_string(stamp));
    std::filesystem::create_directories(path);
    return "file://" + percentEncodePath(path.generic_string());
}

long parsePositiveLongField(std::string_view json, std::string_view field)
{
    const std::string key = "\"" + std::string(field) + "\"";
    const auto keyPosition = json.find(key);
    if (keyPosition == std::string_view::npos) {
        throw std::runtime_error("LibreOfficeKit part metadata is missing " + std::string(field));
    }

    auto valuePosition = json.find(':', keyPosition + key.size());
    if (valuePosition == std::string_view::npos) {
        throw std::runtime_error("LibreOfficeKit returned malformed part metadata");
    }
    ++valuePosition;
    while (valuePosition < json.size() &&
           std::isspace(static_cast<unsigned char>(json[valuePosition])) != 0) {
        ++valuePosition;
    }

    long value = 0;
    const char* begin = json.data() + valuePosition;
    const char* end = json.data() + json.size();
    const auto result = std::from_chars(begin, end, value);
    if (result.ec != std::errc{} || result.ptr == begin || value <= 0) {
        throw std::runtime_error("LibreOfficeKit returned an invalid " + std::string(field));
    }
    return value;
}

long mm100ToTwips(long value)
{
    if (value <= 0) {
        throw std::runtime_error("LibreOfficeKit returned an invalid page dimension");
    }

    // Impress stores page dimensions in hundredths of a millimetre. Core's
    // getPartSize() converts (dimension + 1) to TWIPs; use the same ratio here.
    const long double twips =
        (static_cast<long double>(value) + 1.0L) * 72.0L / 127.0L;
    if (twips > static_cast<long double>(std::numeric_limits<long>::max())) {
        throw std::overflow_error("presentation page dimension is too large");
    }
    return static_cast<long>(std::llround(twips));
}

std::string jsonStringLiteral(std::string_view value)
{
    std::string encoded;
    encoded.reserve(value.size() + 2U);
    encoded.push_back('"');
    for (const unsigned char ch : value) {
        switch (ch) {
        case '"':
            encoded += "\\\"";
            break;
        case '\\':
            encoded += "\\\\";
            break;
        case '\b':
            encoded += "\\b";
            break;
        case '\f':
            encoded += "\\f";
            break;
        case '\n':
            encoded += "\\n";
            break;
        case '\r':
            encoded += "\\r";
            break;
        case '\t':
            encoded += "\\t";
            break;
        default:
            if (ch < 0x20U) {
                static constexpr char Hex[] = "0123456789ABCDEF";
                encoded += "\\u00";
                encoded.push_back(Hex[(ch >> 4U) & 0x0FU]);
                encoded.push_back(Hex[ch & 0x0FU]);
            } else {
                encoded.push_back(static_cast<char>(ch));
            }
        }
    }
    encoded.push_back('"');
    return encoded;
}

std::string makeDocumentTransformArguments(std::string_view commandName, int value)
{
    const std::string transform =
        "{\"Transforms\":{\"SlideCommands\":[{" + jsonStringLiteral(commandName) + ":" +
        std::to_string(value) + "}]}}";
    return "{\"DataJson\":{\"type\":\"string\",\"value\":" +
        jsonStringLiteral(transform) + "}}";
}

std::string makeDocumentTransformArguments(
    std::string_view commandName,
    std::string_view value)
{
    const std::string transform =
        "{\"Transforms\":{\"SlideCommands\":[{" + jsonStringLiteral(commandName) + ":" +
        jsonStringLiteral(value) + "}]}}";
    return "{\"DataJson\":{\"type\":\"string\",\"value\":" +
        jsonStringLiteral(transform) + "}}";
}

int toLokKeyType(KeyEventType type)
{
    switch (type) {
    case KeyEventType::Input:
        return LOK_KEYEVENT_KEYINPUT;
    case KeyEventType::Up:
        return LOK_KEYEVENT_KEYUP;
    }
    throw std::invalid_argument("unsupported key event type");
}

int toLokMouseType(MouseEventType type)
{
    switch (type) {
    case MouseEventType::ButtonDown:
        return LOK_MOUSEEVENT_MOUSEBUTTONDOWN;
    case MouseEventType::ButtonUp:
        return LOK_MOUSEEVENT_MOUSEBUTTONUP;
    case MouseEventType::Move:
        return LOK_MOUSEEVENT_MOUSEMOVE;
    }
    throw std::invalid_argument("unsupported mouse event type");
}

PixelFormat toPixelFormat(int tileMode)
{
    switch (tileMode) {
    case LOK_TILEMODE_RGBA:
        return PixelFormat::Rgba;
    case LOK_TILEMODE_BGRA:
        return PixelFormat::Bgra;
    default:
        throw std::runtime_error("LibreOfficeKit returned an unsupported tile format");
    }
}

#if CAKEOS_PRESENT_HAS_STRUCTURE_REQUEST
using Json = nlohmann::json;

const Json* findObjectMemberRecursive(const Json& value, std::string_view member)
{
    if (value.is_object()) {
        const auto direct = value.find(std::string(member));
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

void collectStringLeaves(const Json& value, std::vector<std::string>& output)
{
    if (value.is_string()) {
        output.push_back(value.get<std::string>());
        return;
    }
    if (value.is_array()) {
        for (const auto& child : value) {
            collectStringLeaves(child, output);
        }
        return;
    }
    if (value.is_object()) {
        for (const auto& [key, child] : value.items()) {
            (void)key;
            collectStringLeaves(child, output);
        }
    }
}

std::optional<int> parseObjectIndex(std::string_view key)
{
    constexpr std::string_view prefix{"Objects "};
    if (key.rfind(prefix, 0) != 0) {
        return std::nullopt;
    }
    const std::string_view digits = key.substr(prefix.size());
    int value = -1;
    const auto parsed = std::from_chars(digits.data(), digits.data() + digits.size(), value);
    if (parsed.ec != std::errc{} || parsed.ptr != digits.data() + digits.size() || value < 0) {
        return std::nullopt;
    }
    return value;
}
#endif

} // namespace

struct PresentEngine::Impl {
    enum class HistoryKind {
        Native,
        Move,
        Text
    };

    struct HistoryEntry {
        HistoryKind kind{HistoryKind::Native};
        int fromIndex{};
        int toIndex{};
        int slideIndex{};
        int objectIndex{};
        std::string beforeText;
        std::string afterText;
    };

    explicit Impl(EngineOptions value)
        : options(std::move(value))
    {
        if (options.libreOfficeProgramPath.empty()) {
            throw std::invalid_argument("libreOfficeProgramPath must not be empty");
        }
        if (options.userProfileUrl.empty()) {
            options.userProfileUrl = makePrivateProfileUrl();
            ownsProfile = true;
        }

        office.reset(lok::lok_cpp_init(
            options.libreOfficeProgramPath.c_str(),
            options.userProfileUrl.c_str()));
        if (!office) {
            throw std::runtime_error(
                "failed to initialise LibreOfficeKit from " + options.libreOfficeProgramPath);
        }
    }

    ~Impl()
    {
        document.reset();
        office.reset();
        if (ownsProfile) {
            std::error_code ignored;
            constexpr std::string_view prefix{"file://"};
            if (options.userProfileUrl.rfind(prefix, 0) == 0 &&
                options.userProfileUrl.find('%') == std::string::npos) {
                std::filesystem::remove_all(options.userProfileUrl.substr(prefix.size()), ignored);
            }
        }
    }

    static void callbackThunk(int type, const char* payload, void* data) noexcept
    {
        auto* self = static_cast<Impl*>(data);
        if (self == nullptr || !self->eventCallback) {
            return;
        }
        try {
            self->eventCallback(EngineEvent{
                type,
                payload == nullptr ? std::string{} : std::string(payload)
            });
        } catch (...) {
            // Never allow CakeOS callback exceptions to unwind through the
            // LibreOfficeKit C callback boundary.
        }
    }

    [[nodiscard]] std::runtime_error lastError(std::string_view prefix) const
    {
        std::string message(prefix);
        if (!office) {
            return std::runtime_error(message);
        }
        char* raw = office->getError();
        if (raw != nullptr) {
            if (*raw != '\0') {
                message += ": ";
                message += raw;
            }
            office->freeError(raw);
        }
        return std::runtime_error(message);
    }

    void requireDocument() const
    {
        if (!document) {
            throw std::logic_error("no presentation is open");
        }
    }

    void requireSlideIndex(int slideIndex) const
    {
        requireDocument();
        const int count = document->getParts();
        if (slideIndex < 0 || slideIndex >= count) {
            throw std::out_of_range("slide index is outside the presentation");
        }
    }

    std::string takeString(char* raw) const
    {
        if (raw == nullptr) {
            return {};
        }
        std::string value(raw);
        office->freeError(raw);
        return value;
    }

    std::string partInfo(int slideIndex) const
    {
        requireSlideIndex(slideIndex);
        LibreOfficeKitDocument* rawDocument = document->get();
        if (!LIBREOFFICEKIT_DOCUMENT_HAS(rawDocument, getPartInfo) ||
            rawDocument->pClass->getPartInfo == nullptr) {
            throw std::runtime_error("LibreOfficeKit runtime does not expose slide metadata");
        }
        return takeString(rawDocument->pClass->getPartInfo(rawDocument, slideIndex));
    }

    void clearHistory()
    {
        history.clear();
        historyCursor = 0U;
        replayingHistory = false;
    }

    void appendHistory(HistoryEntry entry)
    {
        if (replayingHistory) {
            return;
        }
        if (historyCursor < history.size()) {
            history.erase(history.begin() + static_cast<std::ptrdiff_t>(historyCursor), history.end());
        }
        history.push_back(std::move(entry));
        historyCursor = history.size();
    }

    EngineOptions options;
    bool ownsProfile{false};
    std::unique_ptr<lok::Office> office;
    std::unique_ptr<lok::Document> document;
    EventCallback eventCallback;
    std::vector<HistoryEntry> history;
    std::size_t historyCursor{0U};
    bool replayingHistory{false};
};

PresentEngine::PresentEngine(EngineOptions options)
    : impl_(std::make_unique<Impl>(std::move(options)))
{
}

PresentEngine::~PresentEngine() = default;
PresentEngine::PresentEngine(PresentEngine&&) noexcept = default;
PresentEngine& PresentEngine::operator=(PresentEngine&&) noexcept = default;

void PresentEngine::open(std::string_view documentPathOrUrl)
{
    const auto url = pathToFileUrl(requireNonEmpty(documentPathOrUrl, "documentPathOrUrl"));
    auto next = std::unique_ptr<lok::Document>(impl_->office->documentLoad(url.c_str()));
    if (!next) {
        throw impl_->lastError("failed to open presentation");
    }
    if (next->getDocumentType() != LOK_DOCTYPE_PRESENTATION) {
        throw std::runtime_error("the selected document is not a presentation");
    }

    next->initializeForRendering();
    next->setPartMode(LOK_PARTMODE_SLIDES);
    next->registerCallback(&Impl::callbackThunk, impl_.get());
    impl_->document = std::move(next);
    impl_->clearHistory();
}

void PresentEngine::close() noexcept
{
    impl_->document.reset();
    impl_->clearHistory();
}

bool PresentEngine::isOpen() const noexcept
{
    return static_cast<bool>(impl_->document);
}

std::vector<SlideInfo> PresentEngine::slides() const
{
    impl_->requireDocument();
    const int count = impl_->document->getParts();
    std::vector<SlideInfo> result;
    result.reserve(static_cast<std::size_t>(count));
    for (int index = 0; index < count; ++index) {
        result.push_back(SlideInfo{
            index,
            impl_->takeString(impl_->document->getPartName(index)),
            impl_->takeString(impl_->document->getPartHash(index))
        });
    }
    return result;
}

int PresentEngine::currentSlide() const
{
    impl_->requireDocument();
    return impl_->document->getPart();
}

void PresentEngine::setCurrentSlide(int slideIndex)
{
    impl_->requireSlideIndex(slideIndex);
    impl_->document->setPart(slideIndex);
}

SlideExtent PresentEngine::slideExtent(int slideIndex) const
{
    const std::string info = impl_->partInfo(slideIndex);
    if (info.empty()) {
        throw std::runtime_error("LibreOfficeKit returned no metadata for the requested slide");
    }

    const long widthMm100 = parsePositiveLongField(info, "width");
    const long heightMm100 = parsePositiveLongField(info, "height");
    return SlideExtent{mm100ToTwips(widthMm100), mm100ToTwips(heightMm100)};
}

bool PresentEngine::supportsElementSnapshots() const noexcept
{
#if CAKEOS_PRESENT_HAS_STRUCTURE_REQUEST
    return true;
#else
    return false;
#endif
}

std::vector<ElementSnapshot> PresentEngine::elementSnapshot(
    std::string_view documentPathOrUrl,
    int slideIndex) const
{
#if !CAKEOS_PRESENT_HAS_STRUCTURE_REQUEST
    (void)documentPathOrUrl;
    (void)slideIndex;
    throw std::runtime_error(
        "this LibreOfficeKit runtime does not support semantic element snapshots");
#else
    if (slideIndex < 0) {
        throw std::out_of_range("slide index is outside the presentation snapshot");
    }

    const std::string path = requireNonEmpty(documentPathOrUrl, "documentPathOrUrl");
    char* raw = impl_->office->extractDocumentStructureRequest(path.c_str(), "slides");
    if (raw == nullptr || *raw == '\0') {
        if (raw != nullptr) {
            impl_->office->freeError(raw);
        }
        throw std::runtime_error("LibreOfficeKit returned no presentation structure snapshot");
    }

    std::string structureText(raw);
    impl_->office->freeError(raw);

    const Json structure = Json::parse(structureText);
    const Json* slides = findObjectMemberRecursive(structure, "Slides");
    if (slides == nullptr || !slides->is_object()) {
        throw std::runtime_error("LibreOfficeKit structure snapshot has no Slides object");
    }

    const std::string slideKey = "Slide " + std::to_string(slideIndex);
    const auto slideIt = slides->find(slideKey);
    if (slideIt == slides->end() || !slideIt->is_object()) {
        throw std::out_of_range("slide index is outside the presentation snapshot");
    }

    const auto objectsIt = slideIt->find("Objects");
    if (objectsIt == slideIt->end() || !objectsIt->is_object()) {
        return {};
    }

    std::vector<ElementSnapshot> result;
    result.reserve(objectsIt->size());
    for (const auto& [key, object] : objectsIt->items()) {
        const auto objectIndex = parseObjectIndex(key);
        if (!objectIndex.has_value() || !object.is_object()) {
            continue;
        }

        ElementSnapshot snapshot;
        snapshot.slideIndex = slideIndex;
        snapshot.objectIndex = *objectIndex;
        const auto textsIt = object.find("Texts");
        if (textsIt != object.end()) {
            collectStringLeaves(*textsIt, snapshot.text);
        }
        result.push_back(std::move(snapshot));
    }

    std::sort(result.begin(), result.end(), [](const ElementSnapshot& left, const ElementSnapshot& right) {
        return left.objectIndex < right.objectIndex;
    });
    return result;
#endif
}

void PresentEngine::applySlideMove(int fromIndex, int toIndex)
{
    impl_->requireSlideIndex(fromIndex);
    impl_->requireSlideIndex(toIndex);
    if (fromIndex == toIndex) {
        return;
    }

    const auto before = slides();
    const SlideInfo moved = before[static_cast<std::size_t>(fromIndex)];
    const std::string transformCommand = "MoveSlide." + std::to_string(fromIndex);
    const std::string arguments = makeDocumentTransformArguments(transformCommand, toIndex);
    postUnoCommand(".uno:TransformDocumentStructure", arguments, true);

    const auto after = slides();
    if (after.size() != before.size()) {
        throw std::runtime_error("LibreOffice document transform changed the slide count while reordering");
    }

    const auto& destination = after[static_cast<std::size_t>(toIndex)];
    const bool hashMatches =
        !moved.hash.empty() && !destination.hash.empty() && moved.hash == destination.hash;
    const bool nameMatches = moved.name == destination.name;
    if (!hashMatches && !nameMatches) {
        throw std::runtime_error("LibreOffice document transform did not move the requested slide");
    }

    setCurrentSlide(toIndex);
}

void PresentEngine::applyElementText(
    int slideIndex,
    int objectIndex,
    std::string_view text)
{
    impl_->requireSlideIndex(slideIndex);
    if (objectIndex < 0) {
        throw std::out_of_range("object index must not be negative");
    }

    setCurrentSlide(slideIndex);
    const std::string transformCommand = "SetText." + std::to_string(objectIndex);
    const std::string arguments = makeDocumentTransformArguments(transformCommand, text);
    postUnoCommand(".uno:TransformDocumentStructure", arguments, true);
}

void PresentEngine::moveSlide(int fromIndex, int toIndex)
{
    if (fromIndex == toIndex) {
        impl_->requireSlideIndex(fromIndex);
        return;
    }
    applySlideMove(fromIndex, toIndex);
    Impl::HistoryEntry entry;
    entry.kind = Impl::HistoryKind::Move;
    entry.fromIndex = fromIndex;
    entry.toIndex = toIndex;
    impl_->appendHistory(std::move(entry));
}

void PresentEngine::recordNativeMutation()
{
    Impl::HistoryEntry entry;
    entry.kind = Impl::HistoryKind::Native;
    impl_->appendHistory(std::move(entry));
}

void PresentEngine::recordTextMutation(
    int slideIndex,
    int objectIndex,
    std::string beforeText,
    std::string afterText)
{
    Impl::HistoryEntry entry;
    entry.kind = Impl::HistoryKind::Text;
    entry.slideIndex = slideIndex;
    entry.objectIndex = objectIndex;
    entry.beforeText = std::move(beforeText);
    entry.afterText = std::move(afterText);
    impl_->appendHistory(std::move(entry));
}

void PresentEngine::undo()
{
    impl_->requireDocument();
    if (impl_->historyCursor == 0U) {
        postUnoCommand(".uno:Undo", {}, true);
        return;
    }

    const auto entry = impl_->history[impl_->historyCursor - 1U];
    impl_->replayingHistory = true;
    try {
        if (entry.kind == Impl::HistoryKind::Move) {
            applySlideMove(entry.toIndex, entry.fromIndex);
        } else if (entry.kind == Impl::HistoryKind::Text) {
            applyElementText(entry.slideIndex, entry.objectIndex, entry.beforeText);
        } else {
            postUnoCommand(".uno:Undo", {}, true);
        }
        --impl_->historyCursor;
        impl_->replayingHistory = false;
    } catch (...) {
        impl_->replayingHistory = false;
        throw;
    }
}

void PresentEngine::redo()
{
    impl_->requireDocument();
    if (impl_->historyCursor >= impl_->history.size()) {
        postUnoCommand(".uno:Redo", {}, true);
        return;
    }

    const auto entry = impl_->history[impl_->historyCursor];
    impl_->replayingHistory = true;
    try {
        if (entry.kind == Impl::HistoryKind::Move) {
            applySlideMove(entry.fromIndex, entry.toIndex);
        } else if (entry.kind == Impl::HistoryKind::Text) {
            applyElementText(entry.slideIndex, entry.objectIndex, entry.afterText);
        } else {
            postUnoCommand(".uno:Redo", {}, true);
        }
        ++impl_->historyCursor;
        impl_->replayingHistory = false;
    } catch (...) {
        impl_->replayingHistory = false;
        throw;
    }
}

RenderedTile PresentEngine::renderTile(const TileRequest& request)
{
    impl_->requireSlideIndex(request.slideIndex);
    if (request.pixelWidth <= 0 || request.pixelHeight <= 0 ||
        request.tileWidthTwips <= 0 || request.tileHeightTwips <= 0) {
        throw std::invalid_argument("tile dimensions must be positive");
    }

    const auto width = static_cast<std::size_t>(request.pixelWidth);
    const auto height = static_cast<std::size_t>(request.pixelHeight);
    if (height > std::numeric_limits<std::size_t>::max() / 4U ||
        width > std::numeric_limits<std::size_t>::max() / (height * 4U)) {
        throw std::overflow_error("requested tile buffer is too large");
    }

    setCurrentSlide(request.slideIndex);
    RenderedTile tile;
    tile.pixelWidth = request.pixelWidth;
    tile.pixelHeight = request.pixelHeight;
    tile.pixelFormat = toPixelFormat(impl_->document->getTileMode());
    tile.pixels.resize(width * height * 4U);

    impl_->document->paintTile(
        tile.pixels.data(),
        request.pixelWidth,
        request.pixelHeight,
        request.tileXTwips,
        request.tileYTwips,
        request.tileWidthTwips,
        request.tileHeightTwips);
    return tile;
}

void PresentEngine::postKeyEvent(KeyEventType type, int charCode, int keyCode)
{
    impl_->requireDocument();
    impl_->document->postKeyEvent(toLokKeyType(type), charCode, keyCode);
}

void PresentEngine::postMouseEvent(
    MouseEventType type,
    int xTwips,
    int yTwips,
    int clickCount,
    int buttons,
    int modifiers)
{
    impl_->requireDocument();
    if (clickCount < 0) {
        throw std::invalid_argument("clickCount must not be negative");
    }
    impl_->document->postMouseEvent(
        toLokMouseType(type), xTwips, yTwips, clickCount, buttons, modifiers);
}

void PresentEngine::postUnoCommand(
    std::string_view command,
    std::string_view jsonArguments,
    bool notifyWhenFinished)
{
    impl_->requireDocument();
    const auto commandValue = requireNonEmpty(command, "command");
    const std::string arguments(jsonArguments);
    impl_->document->postUnoCommand(
        commandValue.c_str(),
        arguments.empty() ? nullptr : arguments.c_str(),
        notifyWhenFinished);
}

void PresentEngine::saveAs(
    std::string_view destinationPathOrUrl,
    std::string_view format,
    std::string_view filterOptions)
{
    impl_->requireDocument();
    const auto url = pathToFileUrl(requireNonEmpty(destinationPathOrUrl, "destinationPathOrUrl"));
    const std::string formatValue(format);
    const std::string optionsValue(filterOptions);
    if (!impl_->document->saveAs(
            url.c_str(),
            formatValue.empty() ? nullptr : formatValue.c_str(),
            optionsValue.empty() ? nullptr : optionsValue.c_str())) {
        throw impl_->lastError("failed to save presentation");
    }
}

void PresentEngine::setEventCallback(EventCallback callback)
{
    impl_->eventCallback = std::move(callback);
}

std::string PresentEngine::pathToFileUrl(std::string_view pathOrUrl)
{
    const std::string value(pathOrUrl);
    if (value.find("://") != std::string::npos) {
        return value;
    }
    const auto absolute = std::filesystem::absolute(std::filesystem::path(value)).lexically_normal();
    return "file://" + percentEncodePath(absolute.generic_string());
}

} // namespace cakeos::present
