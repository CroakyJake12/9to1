#define LOK_USE_UNSTABLE_API
#define NOMINMAX
#include <cassert>
#ifdef _MSC_VER
#pragma warning(push)
#pragma warning(disable:4996) // Pinned upstream loader uses standard C strncpy.
#endif
#include <LibreOfficeKit/LibreOfficeKitInit.h>
#include <LibreOfficeKit/LibreOfficeKitEnums.h>
#ifdef _MSC_VER
#pragma warning(pop)
#endif
#include "9to1_write_lok.h"
#include <atomic>
#include <cstdlib>
#include <cstring>
#include <memory>
#include <limits>
#include <stdexcept>
#include <string>
#include <thread>

// No copied vtable layouts: compile against the pinned upstream ABI header.
static constexpr const char* donor_commit = "d3fca73f81dfb19a72a0da41598b7390eaf5845b";
static thread_local std::string last_error;
static std::atomic<bool> runtime_claimed = false;
struct nto_lok_session {
    LibreOfficeKit* office = nullptr;
    LibreOfficeKitDocument* document = nullptr;
    std::thread::id thread = std::this_thread::get_id();
    nto_lok_callback callback = nullptr;
    void* callback_context = nullptr;
    bool in_call = false;
    ~nto_lok_session() {
        if (document) { document->pClass->registerCallback(document, nullptr, nullptr); document->pClass->destroy(document); }
        if (office) office->pClass->destroy(office);
    }
};
static int fail(int code, const char* message) { last_error = message; return code; }
static bool file_url(const char* value) { return value && std::strncmp(value, "file:///", 8) == 0; }
static bool office_member(LibreOfficeKit* office) {
    return office && office->pClass && LIBREOFFICEKIT_HAS(office, getVersionInfo)
        && office->pClass->getVersionInfo && office->pClass->freeError && office->pClass->destroy;
}
static std::string build_id(const std::string& json) {
    const auto key = json.find("\"BuildId\"");
    if (key == std::string::npos) return {};
    const auto colon = json.find(':', key + 9);
    const auto start = json.find('"', colon == std::string::npos ? json.size() : colon + 1);
    if (start == std::string::npos) return {};
    const auto end = json.find('"', start + 1);
    return end == std::string::npos ? std::string{} : json.substr(start + 1, end - start - 1);
}
static void donor_callback(int type, const char* payload, void* context) {
    auto* session = static_cast<nto_lok_session*>(context);
    // The managed/host callback must queue, never call back into LOK or throw.
    if (session->callback) session->callback(type, payload ? payload : "", session->callback_context);
}
template<typename Work> static int invoke(nto_lok_session* session, bool needs_document, Work work) {
    if (!session) return fail(NTO_LOK_INVALID_ARGUMENT, "A live LOK session is required.");
    if (session->thread != std::this_thread::get_id()) return fail(NTO_LOK_WRONG_THREAD, "LOK calls must run on the creating engine thread.");
    if (session->in_call) return fail(NTO_LOK_REENTRANT_CALL, "Queue callback work instead of re-entering LOK.");
    if (needs_document && !session->document) return fail(NTO_LOK_INVALID_ARGUMENT, "Open a donor document first.");
    struct Guard { bool& flag; explicit Guard(bool& value) : flag(value) { flag = true; } ~Guard() { flag = false; } } guard(session->in_call);
    try { last_error.clear(); return work(); }
    catch (const std::exception& error) { return fail(NTO_LOK_DONOR_ERROR, error.what()); }
    catch (...) { return fail(NTO_LOK_DONOR_ERROR, "The donor operation failed."); }
}
static void close_document(nto_lok_session* session) {
    if (!session->document) return;
    session->document->pClass->registerCallback(session->document, nullptr, nullptr);
    session->document->pClass->destroy(session->document);
    session->document = nullptr;
}
unsigned nto_lok_abi_version() { return 1; }
const char* nto_lok_donor_commit() { return donor_commit; }
const char* nto_lok_last_error() { return last_error.c_str(); }
int nto_lok_create(const char* program_directory, const char* profile_file_url,
    nto_lok_callback callback, void* callback_context, nto_lok_session** result) {
    if (!result) return fail(NTO_LOK_INVALID_ARGUMENT, "An output session pointer is required.");
    *result = nullptr;
    if (!program_directory || !*program_directory || !file_url(profile_file_url))
        return fail(NTO_LOK_INVALID_ARGUMENT, "Use an explicit donor program directory and isolated profile file URL.");
    bool expected = false;
    if (!runtime_claimed.compare_exchange_strong(expected, true))
        return fail(NTO_LOK_RUNTIME_UNAVAILABLE, "This adapter owns one process-scoped LOK runtime; use the existing engine session.");
    // Donor restart after destruction is not assumed safe. The owning process
    // starts a fresh engine lifetime when it needs a different runtime/profile.
    try {
        auto session = std::make_unique<nto_lok_session>();
        session->office = lok_init_2(program_directory, profile_file_url);
        if (!session->office) return fail(NTO_LOK_RUNTIME_UNAVAILABLE, "The configured LibreOfficeKit runtime could not initialise.");
        if (!office_member(session->office)) {
            if (session->office->pClass && session->office->pClass->destroy) session->office->pClass->destroy(session->office);
            session->office = nullptr;
            return fail(NTO_LOK_CAPABILITY_UNAVAILABLE, "The donor does not expose the required versioned LOK office ABI.");
        }
        char* raw = session->office->pClass->getVersionInfo(session->office);
        const std::string version = raw ? raw : "";
        if (raw) session->office->pClass->freeError(raw);
        if (build_id(version) != donor_commit) {
            session->office->pClass->destroy(session->office);
            session->office = nullptr;
            return fail(NTO_LOK_VERSION_MISMATCH, "The loaded donor BuildId does not match the pinned source commit.");
        }
        session->callback = callback; session->callback_context = callback_context;
        *result = session.release(); last_error.clear(); return NTO_LOK_OK;
    } catch (const std::exception& error) { return fail(NTO_LOK_DONOR_ERROR, error.what()); }
    catch (...) { return fail(NTO_LOK_DONOR_ERROR, "LibreOfficeKit initialisation failed."); }
}
int nto_lok_destroy(nto_lok_session* session) {
    const int status = invoke(session, false, [&]() -> int { close_document(session); session->office->pClass->destroy(session->office); session->office = nullptr; return NTO_LOK_OK; });
    if (status == NTO_LOK_OK) delete session;
    return status;
}
int nto_lok_open(nto_lok_session* session, const char* url) {
    return invoke(session, false, [&]() -> int {
        if (!file_url(url)) return fail(NTO_LOK_INVALID_ARGUMENT, "Open requires an owner-brokered local file URL.");
        if (session->document) return fail(NTO_LOK_INVALID_ARGUMENT, "Save or explicitly close the current donor document before replacing it.");
        auto* document = session->office->pClass->documentLoad(session->office, url);
        if (!document) return fail(NTO_LOK_DONOR_ERROR, "LibreOfficeKit could not open this document.");
#define NTO_HAS(member) (LIBREOFFICEKIT_DOCUMENT_HAS(document, member) && document->pClass->member)
        const bool supported = NTO_HAS(initializeForRendering) && NTO_HAS(registerCallback) && NTO_HAS(paintTile)
            && NTO_HAS(getDocumentSize) && NTO_HAS(getTileMode) && NTO_HAS(postKeyEvent) && NTO_HAS(postMouseEvent)
            && NTO_HAS(postUnoCommand) && NTO_HAS(getTextSelection) && NTO_HAS(paste) && NTO_HAS(postWindowExtTextInputEvent);
#undef NTO_HAS
        if (!supported) { document->pClass->destroy(document); return fail(NTO_LOK_CAPABILITY_UNAVAILABLE, "The donor lacks a required rendering/input/selection/IME entry point."); }
        session->document = document;
        document->pClass->registerCallback(document, donor_callback, session);
        document->pClass->initializeForRendering(document, nullptr);
        return NTO_LOK_OK;
    });
}
int nto_lok_close_document(nto_lok_session* session) { return invoke(session, false, [&]() -> int { close_document(session); return NTO_LOK_OK; }); }
int nto_lok_document_size(nto_lok_session* session, int64_t* width, int64_t* height, int* mode) {
    return invoke(session, true, [&]() -> int {
        if (!width || !height || !mode) return fail(NTO_LOK_INVALID_ARGUMENT, "Document dimensions and tile mode outputs are required.");
        long w = 0, h = 0; session->document->pClass->getDocumentSize(session->document, &w, &h);
        *width = w; *height = h; *mode = session->document->pClass->getTileMode(session->document); return NTO_LOK_OK;
    });
}
int nto_lok_paint_tile(nto_lok_session* session, unsigned char* pixels, size_t capacity, int pw, int ph, int x, int y, int tw, int th) {
    return invoke(session, true, [&]() -> int {
        if (!pixels || pw <= 0 || ph <= 0 || pw > 4096 || ph > 4096 || tw <= 0 || th <= 0 || x < 0 || y < 0
            || tw > std::numeric_limits<int>::max() - x || th > std::numeric_limits<int>::max() - y
            || capacity < static_cast<size_t>(pw) * static_cast<size_t>(ph) * 4)
            return fail(NTO_LOK_INVALID_ARGUMENT, "Tile bounds and a sufficient 32-bit pixel buffer are required.");
        session->document->pClass->paintTile(session->document, pixels, pw, ph, x, y, tw, th); return NTO_LOK_OK;
    });
}
int nto_lok_key(nto_lok_session* session, int type, int unicode_scalar, int key) {
    return invoke(session, true, [&]() -> int {
        if ((type != LOK_KEYEVENT_KEYINPUT && type != LOK_KEYEVENT_KEYUP) || unicode_scalar < 0 || unicode_scalar > 0x10ffff
            || (unicode_scalar >= 0xd800 && unicode_scalar <= 0xdfff)) return fail(NTO_LOK_INVALID_ARGUMENT, "Use a valid donor key event and Unicode scalar.");
        session->document->pClass->postKeyEvent(session->document, type, unicode_scalar, key); return NTO_LOK_OK;
    });
}
int nto_lok_pointer(nto_lok_session* session, int type, int x, int y, int count, int buttons, int modifiers) {
    return invoke(session, true, [&]() -> int {
        if (type < LOK_MOUSEEVENT_MOUSEBUTTONDOWN || type > LOK_MOUSEEVENT_MOUSEMOVE || count < 0 || count > 3)
            return fail(NTO_LOK_INVALID_ARGUMENT, "Use a valid donor pointer event.");
        session->document->pClass->postMouseEvent(session->document, type, x, y, count, buttons, modifiers); return NTO_LOK_OK;
    });
}
int nto_lok_composition(nto_lok_session* session, int type, const char* text) {
    return invoke(session, true, [&]() -> int {
        if (!text || type < LOK_EXT_TEXTINPUT || type > LOK_EXT_TEXTINPUT_END) return fail(NTO_LOK_INVALID_ARGUMENT, "A valid composition event and UTF-8 text are required.");
        session->document->pClass->postWindowExtTextInputEvent(session->document, 0, type, text); return NTO_LOK_OK;
    });
}
static char* copy_string(const char* text) {
    if (!text) return nullptr;
    const auto size = std::strlen(text) + 1; auto* copy = static_cast<char*>(std::malloc(size));
    if (!copy) throw std::bad_alloc(); std::memcpy(copy, text, size); return copy;
}
int nto_lok_selection(nto_lok_session* session, const char* mime, char** data, char** actual) {
    return invoke(session, true, [&]() -> int {
        if (!mime || !data || !actual) return fail(NTO_LOK_INVALID_ARGUMENT, "Selection MIME and output pointers are required.");
        *data = nullptr; *actual = nullptr;
        char* donor_mime = nullptr;
        char* donor_data = session->document->pClass->getTextSelection(session->document, mime, &donor_mime);
        const auto release = session->office->pClass->freeError;
        std::unique_ptr<char, decltype(release)> owned_data(donor_data, release), owned_mime(donor_mime, release);
        std::unique_ptr<char, decltype(&std::free)> copied_data(copy_string(donor_data), &std::free), copied_mime(copy_string(donor_mime), &std::free);
        *data = copied_data.release(); *actual = copied_mime.release(); return NTO_LOK_OK;
    });
}
int nto_lok_paste(nto_lok_session* session, const char* mime, const char* data, size_t size) {
    return invoke(session, true, [&]() -> int {
        if (!mime || !data || size > 16u * 1024u * 1024u) return fail(NTO_LOK_INVALID_ARGUMENT, "Paste requires typed content within the 16 MiB transport bound.");
        return session->document->pClass->paste(session->document, mime, data, size) ? NTO_LOK_OK : fail(NTO_LOK_DONOR_ERROR, "The donor rejected this clipboard representation.");
    });
}
int nto_lok_command(nto_lok_session* session, const char* command, const char* arguments) {
    return invoke(session, true, [&]() -> int {
        if (!command || std::strncmp(command, ".uno:", 5) != 0) return fail(NTO_LOK_INVALID_ARGUMENT, "Use a mapped donor UNO command.");
        session->document->pClass->postUnoCommand(session->document, command, arguments, true); return NTO_LOK_OK;
    });
}
int nto_lok_save(nto_lok_session* session, const char* url, const char* format, const char* options) {
    return invoke(session, true, [&]() -> int {
        if (!file_url(url)) return fail(NTO_LOK_INVALID_ARGUMENT, "Save requires an owner-brokered local destination file URL.");
        return session->document->pClass->saveAs(session->document, url, format, options) ? NTO_LOK_OK : fail(NTO_LOK_DONOR_ERROR, "The donor could not save this representation.");
    });
}
void nto_lok_free(void* memory) { std::free(memory); }
