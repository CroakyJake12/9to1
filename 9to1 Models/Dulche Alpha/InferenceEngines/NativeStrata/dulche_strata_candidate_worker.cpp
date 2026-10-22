// dulche_strata_candidate_worker.cpp
//
// CANDIDATE relay worker for the owner-designated Niko1221/Strata v0.2.2 engine (Thunder).
// Branch: codex/mercury-thunder-upgrade-20261020.
//
// STATUS: SOURCE-ONLY. NOT BUILT / NOT RUN in this checkpoint (no compiler/CUDA/model build was
// executed). This file implements the REAL portable resident-child IPC + lifecycle, the real
// GEN/T/DONE relay, the real bounded response validation, and the lazy verified bundled-helper
// text path. It is NOT a drop-in substitute for the production legacy in-process ABI worker
// (`dulche_strata_worker.cpp`), which remains the known-good rollback. The portable child-process
// RAII component (`dulche_child_process.h`) is compiled and unit-tested standalone on POSIX
// (see work/pc-worker/thunder-benchmark/native-child-test/); the full worker is not built here.
//
// Architecture. The v0.2.2 engine is a standalone CLI. Its main accepts a general `--pack`
// option (pack folder), but the qwen35moe engine dispatch keys off `--model`/`--native` pointing at
// a real Qwen35Moe GGUF; a `--pack` folder alone cannot reach that engine. The candidate therefore
// passes the SINGLE held GGUF file (the SAME protected descriptor) via `--model`, plus `--serve`
// and `--max-context`:
//
//     strata --model <held-gguf> --serve --max-context N ...
//
// which loads weights once, prints a READY line, then serves line-oriented requests on stdin/stdout:
//
//     in:  GEN <max_new> [sampling keys] <id,id,...>
//          STOP
//          QUIT
//     out: READY <max_context> stop
//          INFO ... / PP <pos> <pt> <ms> <tok/s> / RESUME <n> / REUSED <n>
//          T <id>              (one generated token id per line)
//          DONE <generated> <prompt> <prompt_ms> <decode_ms> <finish> <drafts accepted>
//               <drafts offered> <reused> [hits] [lookups] [ram] [file] [file MB]
//               [prompt tokens read] [offloaded] [chain...] chunk=<n> experts_streamed=<n>
//          ERR <message>
//
// This worker hosts the Dulche-owned bounded frame ABI on its own stdin/stdout and relays to the
// engine sub-process through that protocol. It preserves the host-side envelope framing
// (little-endian type+length frames), load/unload custody, bounded streaming/end/error, cancellation
// and shutdown. It deliberately does NOT claim legacy capability parity: text <-> token-id
// conversion is delegated to a bundled, hash-verified helper that reuses the donor tokenizer
// (`tools/strata_tokenizer.py`) and the model's own GGUF chat template. If that bundled helper is
// absent or its verified identity does not match the registered model, ONLY text generation requests
// fail closed with the exact dependency cause; load/probe/shutdown still operate normally.
//
// Host wire (V2 candidate ABI):
//   out hello (type 0): version u32=2, origin text, cuda u32, nccl u32=0, family text
//   in  load  (op 1):  directory text, registration text, context u32,
//                       expected_tokenizer_identity text, count u32, devices[count] u32
//   out loaded (type 4): NativeErrors(0,0)
//   in  text generate (op 2) / structured generate (op 4):
//                        [op4: wire_version u32=1] message_count u32,
//                        messages{role u32, content text, name text, images u32},
//                        max_new u32, temperature f64, top_p f64, top_k u32, seed u64,
//                        stop_count u32, stops[stop_count] text, effort text
//   out token (type 1): byte_len u32 + utf-8 bytes
//   out text terminal (type 2): NativeErrors(0,0), terminalText text, prompt u64, prefill u64,
//                        decode u64, prefillSeconds f64, decodeSeconds f64, reused flag u32(+u64),
//                        kv flag u32(+u32), stopped u32
//   out failure (type 5): NativeErrors(code,count,text...)
//   in  cancel (op 5): stop the in-flight generate (forwarded as ENGINE STOP)
//   in  stop  (op 3): clean shutdown (QUIT), ack (type 3), exit 0
//
// The relay never equates a successful process spawn with a model load: it publishes `loaded` only
// after the engine actually prints READY and has not exited.

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cstdlib>
#include <cerrno>
#include <string>
#include <vector>
#include <memory>
#include <limits>
#include <stdexcept>
#include <bit>
#include <algorithm>
#include <atomic>
#include <thread>
#include <mutex>
#include <optional>
#include <cmath>
#include <filesystem>

#include "dulche_child_process.h"

#if !defined(_WIN32)
#include <poll.h>
#endif

#if defined(STRATA_HAS_CUDA) && STRATA_HAS_CUDA
#include <cuda_runtime.h>
#endif

namespace {

constexpr size_t kLimit = 16U * 1024U * 1024U;
constexpr uint32_t kAbiVersion = 2;  // explicit incompatible transport vs legacy v1 in-process ABI
constexpr uint32_t kOpLoad = 1;
constexpr uint32_t kOpGenerate = 2;
constexpr uint32_t kOpStop = 3;
constexpr uint32_t kOpStructuredGenerate = 4;
constexpr uint32_t kOpCancel = 5;

// ---------------------------------------------------------------------------
// Verified model capability dispatch (Thunder pass 9).
//
// Owner-confirmed models (2026-10-21, independent official metadata):
//   - ornith-ai/Ornith-1.5-122B (rev 7681ef60…; MIT; qwen3_5_moe / text;
//     125,086,497,008 BF16 params; 48 layers/256 experts/8 per-token;
//     ctx 262144; MTP1; vocab 248320; vision token ids present in config)
//   - InternScience/Sage-A1.5 (rev 6bc623ea…; Apache-2.0; qwen3_5_moe;
//     35,951,822,704 BF16 params; 40 layers/256 experts/8 per-token;
//     ctx 262144; MTP1; vocab 248320; vision token ids present in config)
// Both verify to the engine's registered GGUF family `qwen35moe`. The candidate
// relay is TEXT-ONLY: tools/vision/MTP are NEVER advertised from an architecture
// string, card claim, or GGUF tag. A requested registration that is not a
// verified family/identity is refused (manual no-fallback model identity), never
// silently downgraded to a different model. No "Orinth" typo aliases are accepted.
// ---------------------------------------------------------------------------
struct ModelCapability {
    const char* family;           // canonical engine-registered GGUF family
    bool text_only;               // true for the candidate relay (always)
    bool supports_tools;          // false: no structured tool path
    bool supports_vision;         // false: images/video refused
    bool claims_mtp;              // false: MTP not exercised by the relay
};

constexpr ModelCapability kQwen35MoeCapability = {
    "qwen35moe", /*text_only*/ true, /*tools*/ false, /*vision*/ false, /*mtp*/ false
};

struct RegistrationAlias {
    const char* spelling;          // accepted registration spelling
    const ModelCapability* capability;
};

// Manual alias table: each accepted spelling resolves to a VERIFIED family. Exact
// match only (no wildcards, no invented arch/quant aliases). The engine-native
// family spelling, the verified HF `model_type`/`text_config.model_type`
// spellings, and the two owner-confirmed model identities are the ONLY entries.
constexpr RegistrationAlias kRegistrationAliases[] = {
    // Engine-native family spelling.
    { "qwen35moe", &kQwen35MoeCapability },
    // Verified HF model_type / text_config.model_type spellings (both confirmed models).
    { "qwen3_5_moe", &kQwen35MoeCapability },
    { "qwen3_5_moe_text", &kQwen35MoeCapability },
    // Explicit owner-confirmed model identities -> same verified family.
    { "ornith-ai/Ornith-1.5-122B", &kQwen35MoeCapability },
    { "InternScience/Sage-A1.5", &kQwen35MoeCapability },
};

// Resolve a requested registration to a verified model capability. Returns nullptr
// for unknown/unverified spellings (caller refuses, no fabricated fallback).
const ModelCapability* resolve_capability(const std::string& registration) {
    for (const auto& alias : kRegistrationAliases)
        if (registration == alias.spelling) return alias.capability;
    return nullptr;
}

// Accept a registration when it is exactly a verified spelling OR a verified family
// spelling carrying a genuine quant/format suffix (e.g. "qwen35moe-Q4_K_M").
// This preserves genuine compatible fallback without inventing aliases.
const ModelCapability* resolve_capability_tolerant(const std::string& registration) {
    if (const ModelCapability* exact = resolve_capability(registration)) return exact;
    for (const auto& alias : kRegistrationAliases) {
        const std::string_view fam(alias.spelling);
        if (registration.size() > fam.size() && registration.compare(0, fam.size(), fam) == 0) {
            const char next = registration[fam.size()];
            if (next == '-' || next == '_' || next == '/') return alias.capability;
        }
    }
    return nullptr;
}

struct Msg {
    uint32_t role = 0;
    std::string content;
    std::string name;
};

struct GenerateRequest {
    std::vector<Msg> messages;
    uint32_t max_new = 0;
    double temperature = 0.7;
    double top_p = 1.0;
    uint32_t top_k = 0;
    uint64_t seed = 0;
    double min_p = 0.0;
    double penalty_repeat = 0.0;
    double penalty_freq = 0.0;
    double penalty_present = 0.0;
    int32_t penalty_last_n = 0;
    bool structured = false;
    std::string expected_tokenizer_identity;
};

// ------------------------------------------------------- cross-platform binary stdio helpers
void set_binary_stdio() {
#if defined(_WIN32)
    _setmode(_fileno(stdin), _O_BINARY);
    _setmode(_fileno(stdout), _O_BINARY);
    _setmode(_fileno(stderr), _O_BINARY);
#endif
}

// ---------------------------------------------------------------- platform fd primitives
// POSIX is the implemented/tested path. Windows is NOT RUN (no Windows build claimed); these
// wrappers keep the translation unit compiling on Windows and fail closed rather than fake success.
#if defined(_WIN32)
struct pollfd { int fd; short events; short revents; };
typedef unsigned long nfds_t;
constexpr short POLLIN = 0x001;
constexpr short POLLOUT = 0x004;
constexpr short POLLERR = 0x008;
constexpr short POLLHUP = 0x010;
#endif

int os_write(int fd, const void* data, size_t n) {
#if defined(_WIN32)
    return _write(fd, data, static_cast<unsigned>(n));
#else
    return static_cast<int>(::write(fd, data, n));
#endif
}

int os_read(int fd, void* data, size_t n) {
#if defined(_WIN32)
    return _read(fd, data, static_cast<unsigned>(n));
#else
    return static_cast<int>(::read(fd, data, n));
#endif
}

// poll() shim. On POSIX it is the real poll. On Windows the worker is unsupported/not run, so it
// fails closed (-1) rather than pretending the fds are selectable.
int os_poll(struct pollfd* pfd, nfds_t n, int timeout_ms) {
#if defined(_WIN32)
    (void)pfd; (void)n; (void)timeout_ms;
    errno = ENOSYS;
    return -1;
#else
    return ::poll(pfd, n, timeout_ms);
#endif
}

// Resolve an approved/existing interpreter for the bundled tokenizer helper. Uses a deliberate,
// controlled resolution (env override then a constrained PATH search for python3); it never relies
// on the current directory, and returns an absolute resolvable path or a resolved launcher.
// No installs or network are performed.
std::string resolve_python() {
    const char* override_env = std::getenv("STRATA_PYTHON");
    if (override_env && *override_env) {
        std::filesystem::path p(override_env);
        std::error_code ec;
        if (std::filesystem::exists(p, ec) && !ec) return p.string();
        throw std::invalid_argument("STRATA_PYTHON points to a missing interpreter path.");
    }
    const char* path_env = std::getenv("PATH");
    if (path_env && *path_env) {
        std::string path(path_env);
        size_t start = 0;
        while (start <= path.size()) {
            const size_t sep = path.find(':', start);
            const std::string dir = path.substr(
                start, sep == std::string::npos ? std::string::npos : sep - start);
            if (!dir.empty()) {
                std::filesystem::path cand = std::filesystem::path(dir) / "python3";
                std::error_code ec;
                if (std::filesystem::exists(cand, ec) && !ec) return cand.string();
            }
            if (sep == std::string::npos) break;
            start = sep + 1;
        }
    }
    throw std::invalid_argument(
        "candidate text generation dependency: could not resolve a python3 interpreter; set the "
        "STRATA_PYTHON env var to an installed interpreter path or provide python3 on PATH.");
}

// ---------------------------------------------------------------- frame buffer (same envelope)
template<class To, class From>
To bit_pun(const From& src) {
    static_assert(sizeof(To) == sizeof(From), "bit_pun size mismatch");
    To dst;
    std::memcpy(&dst, &src, sizeof(To));
    return dst;
}

struct Buffer {
    std::vector<unsigned char> bytes;
    size_t cursor{};
    template<class T> void number(T value) {
        static_assert(std::is_unsigned_v<T>);
        for (size_t i = 0; i < sizeof(T); ++i) bytes.push_back(static_cast<unsigned char>(value >> (i * 8U)));
    }
    template<class T> T read_number() {
        static_assert(std::is_unsigned_v<T>);
        if (bytes.size() - cursor < sizeof(T)) throw std::invalid_argument("Truncated native command.");
        T value{};
        for (size_t i = 0; i < sizeof(T); ++i) value |= static_cast<T>(bytes[cursor++]) << (i * 8U);
        return value;
    }
    void text(const std::string& value) {
        if (value.size() > kLimit || value.size() > std::numeric_limits<uint32_t>::max())
            throw std::invalid_argument("Oversized native output.");
        number(static_cast<uint32_t>(value.size()));
        bytes.insert(bytes.end(), value.begin(), value.end());
    }
    std::string read_text() {
        const auto size = read_number<uint32_t>();
        if (size > kLimit || size > bytes.size() - cursor) throw std::invalid_argument("Invalid native text length.");
        std::string value(reinterpret_cast<const char*>(bytes.data() + cursor), size);
        cursor += size;
        return value;
    }
    void real(double value) { number(bit_pun<uint64_t>(value)); }
    double read_real() { return bit_pun<double>(read_number<uint64_t>()); }
    void end() const { if (cursor != bytes.size()) throw std::invalid_argument("Unexpected native command suffix."); }
};

void write_frame(FILE* wire, uint32_t type, const Buffer& payload) {
    if (payload.bytes.size() > kLimit) throw std::invalid_argument("Native output frame exceeds limit.");
    Buffer header;
    header.number(type);
    header.number(static_cast<uint32_t>(payload.bytes.size()));
    if (std::fwrite(header.bytes.data(), 1, header.bytes.size(), wire) != header.bytes.size()
        || (!payload.bytes.empty() && std::fwrite(payload.bytes.data(), 1, payload.bytes.size(), wire) != payload.bytes.size())
        || std::fflush(wire) != 0) throw std::runtime_error("Native output pipe failed.");
}

// Read a full host command frame from stdin. Returns false on clean EOF. Blocking (outer loop).
bool read_command(Buffer& result) {
    unsigned char length[4];
    const auto count = std::fread(length, 1, 4, stdin);
    if (count == 0 && std::feof(stdin)) return false;
    if (count != 4) throw std::invalid_argument("Truncated native command length.");
    uint32_t size{};
    for (unsigned i = 0; i < 4; ++i) size |= static_cast<uint32_t>(length[i]) << (i * 8U);
    if (size > kLimit || size < 4) throw std::invalid_argument("Invalid bounded native command length.");
    result.bytes.resize(size);
    result.cursor = 0;
    if (std::fread(result.bytes.data(), 1, size, stdin) != size) throw std::invalid_argument("Truncated native command frame.");
    return true;
}

// ---------------------------------------------------------------- CUDA hardware probe
Buffer hardware_probe() {
    Buffer output;
#if defined(STRATA_HAS_CUDA) && STRATA_HAS_CUDA
    const auto require = [](cudaError_t status, const char* operation) {
        if (status != cudaSuccess) throw std::runtime_error(std::string(operation) + ": " + cudaGetErrorString(status));
    };
    int runtime{}, driver{}, count{};
    require(cudaRuntimeGetVersion(&runtime), "cudaRuntimeGetVersion");
    require(cudaDriverGetVersion(&driver), "cudaDriverGetVersion");
    require(cudaGetDeviceCount(&count), "cudaGetDeviceCount");
    if (runtime <= 0 || driver <= 0 || count < 0 || count > 64) throw std::runtime_error("Invalid bounded actual CUDA inventory.");
    output.number<uint32_t>(0);
    output.number<uint32_t>(0);
    output.number(static_cast<uint32_t>(runtime));
    output.number(static_cast<uint32_t>(driver));
    output.number(static_cast<uint32_t>(count));
    for (int index = 0; index < count; ++index) {
        cudaDeviceProp properties{};
        require(cudaGetDeviceProperties(&properties, index), "cudaGetDeviceProperties");
        require(cudaSetDevice(index), "cudaSetDevice");
        size_t free{}, total{};
        require(cudaMemGetInfo(&free, &total), "cudaMemGetInfo");
        if (properties.major < 0 || properties.minor < 0 || total == 0 || free > total || properties.totalGlobalMem == 0)
            throw std::runtime_error("Invalid actual CUDA device properties/memory.");
        std::string uuid;
        uuid.reserve(32);
        constexpr char hex[] = "0123456789abcdef";
        for (const auto byte : properties.uuid.bytes) {
            const auto value = static_cast<unsigned char>(byte);
            uuid.push_back(hex[value >> 4U]);
            uuid.push_back(hex[value & 15U]);
        }
        output.number(static_cast<uint32_t>(index));
        output.text(std::string(properties.name, ::strnlen(properties.name, sizeof(properties.name))));
        output.text(uuid);
        output.number(static_cast<uint32_t>(properties.major));
        output.number(static_cast<uint32_t>(properties.minor));
        output.number(static_cast<uint64_t>(properties.totalGlobalMem));
        output.number(static_cast<uint64_t>(free));
        output.number(static_cast<uint64_t>(total));
    }
#else
    output.number<uint32_t>(1);
    output.number<uint32_t>(1);
    output.text(std::string("This candidate worker has no actual CUDA build."));
#endif
    return output;
}

// ---------------------------------------------------------------- locate bundled engine/helper
std::string self_dir(int argc, char** argv) {
    std::filesystem::path self;
#if defined(_WIN32)
    {
        std::wstring wbuf(32768, L'\0');
        const DWORD n = GetModuleFileNameW(nullptr, wbuf.data(), static_cast<DWORD>(wbuf.size()));
        if (n > 0) self = std::filesystem::path(wbuf.substr(0, n));
    }
#else
    {
        char link[4096];
        const ssize_t n = ::readlink("/proc/self/exe", link, sizeof link - 1);
        if (n > 0) { link[n] = '\0'; self = std::filesystem::path(link); }
    }
    if (self.empty() && argc > 0) self = std::filesystem::path(argv[0]);
#endif
    if (self.empty()) return std::string();
    return self.parent_path().string();
}

// The host may hand explicit held closure paths (the SAME protected install) through these strict
// environment channels. They are validated to exist and are used only when set; otherwise the
// relative sibling layout is preserved. The host clears its child environment, so only trusted
// values ever populate these variables.
std::string trusted_held_path(const char* name) {
    const char* v = std::getenv(name);
    if (!v || !*v) return std::string();
    std::filesystem::path p(v);
    std::error_code ec;
    if (!std::filesystem::exists(p, ec) || ec)
        throw std::invalid_argument(std::string(name) + " points to a missing held path.");
    return p.string();
}

std::string locate_engine(int argc, char** argv) {
    const std::string held = trusted_held_path("STRATA_ENGINE_PATH");
    if (!held.empty()) return held;
    const auto dir = self_dir(argc, argv);
    if (!dir.empty()) {
        std::filesystem::path base(dir);
        const auto candidate = base / ".." / "engine" / ("strata" + std::string(STRATA_NATIVE_EXT));
        if (std::filesystem::exists(candidate)) return candidate.string();
    }
#ifdef STRATA_CANDIDATE_ENGINE
    return std::string(STRATA_CANDIDATE_ENGINE);
#else
    return std::string();
#endif
}

// The bundled helper runs as <held-root>/strata_tokenizer_helper.py so its sibling imports
// (strata_tokenizer / gguf_reader) resolve through the SAME held directory descriptor — never an
// individual script FD whose `.resolve()` would escape back to an ordinary mutable path. The held
// directory is produced only by the trusted installation source; a legacy/script env fallback and
// the self-dir relative layout are kept for non-host/local runs.
std::string locate_helper(int argc, char** argv) {
    const std::string held_dir = trusted_held_path("STRATA_HELPER_DIR");
    if (!held_dir.empty())
        return (std::filesystem::path(held_dir) / "strata_tokenizer_helper.py").string();
    const std::string held = trusted_held_path("STRATA_HELPER_PATH");
    if (!held.empty()) return held;
    const auto dir = self_dir(argc, argv);
    if (!dir.empty()) {
        std::filesystem::path base(dir);
        const auto candidate = base / "strata_tokenizer_helper.py";
        if (std::filesystem::exists(candidate)) return candidate.string();
    }
    return std::string();
}

// The upstream `strata` main has a general `--pack` option, but its qwen35moe engine dispatch
// (generate.cpp:2380+) keys off `--model`/`--native` pointing at a real Qwen35Moe GGUF; a
// `--pack` folder alone cannot reach that engine. We therefore pass the SINGLE held GGUF file
// (the SAME protected descriptor, never an ordinary mutable path) via `--model`, plus `--serve`
// and `--max-context`. The approved device selection maps to the upstream `--gpu` comma-separated
// argument (generate.cpp:2356 pre-family parser sets CUDA_VISIBLE_DEVICES before CUDA init;
// q36_engine.cu:3753 consumes it). Exactly one engine spawn is issued.
std::vector<std::string> build_engine_args(uint32_t context, const std::vector<uint32_t>& devices) {
    const std::string model = trusted_held_path("STRATA_MODEL_PATH");
    if (model.empty())
        throw std::invalid_argument(
            "candidate engine dependency: STRATA_MODEL_PATH is not set; refusing to launch the "
            "engine without the approved held GGUF model file.");
    // Fail closed on an ambiguous selection rather than silently omitting or defaulting: an empty
    // (no preference) list and a duplicate/out-of-range index are all refused. CUDA device indices are
    // int-sized; a uint32_t beyond the int range can never be a valid CUDA_VISIBLE_DEVICES value.
    if (devices.empty())
        throw std::invalid_argument(
            "candidate load: empty device selection is ambiguous; refusing to launch the engine "
            "without an explicit approved device list.");
    {
        std::vector<uint32_t> sorted = devices;
        std::sort(sorted.begin(), sorted.end());
        for (size_t i = 0; i < sorted.size(); ++i) {
            if (sorted[i] > static_cast<uint32_t>(std::numeric_limits<int>::max()))
                throw std::invalid_argument(
                    "candidate load: device index is outside the valid int range; refusing to launch "
                    "the engine with an out-of-range device index.");
            if (i && sorted[i] == sorted[i - 1])
                throw std::invalid_argument(
                    "candidate load: duplicate device selection is ambiguous; refusing to launch the "
                    "engine with an unresolved device list.");
        }
    }
    std::vector<std::string> args;
    args.push_back("--model");
    args.push_back(model);
    args.push_back("--serve");
    args.push_back("--gpu");
    std::string gpu;
    for (size_t i = 0; i < devices.size(); ++i) {
        if (i) gpu += ",";
        gpu += std::to_string(devices[i]);
    }
    args.push_back(gpu);
    if (context > 0) {
        args.push_back("--max-context");
        args.push_back(std::to_string(context));
    }
    return args;
}

// ---------------------------------------------------------------- raw fd write/read (children)
bool write_all(int fd, const void* data, size_t n) {
    const char* p = static_cast<const char*>(data);
    size_t left = n;
    while (left > 0) {
        const int w = os_write(fd, p, left);
        if (w < 0) {
            if (errno == EINTR) continue;
            return false;
        }
        p += w;
        left -= static_cast<size_t>(w);
    }
    return true;
}

bool read_exact(int fd, void* out, size_t n) {
    char* p = static_cast<char*>(out);
    size_t got = 0;
    while (got < n) {
        const int r = os_read(fd, p + got, n - got);
        if (r < 0) {
            if (errno == EINTR) continue;
            return false;
        }
        if (r == 0) return false;  // EOF
        got += static_cast<size_t>(r);
    }
    return true;
}

void engine_send(dulche::ChildProcess& engine, const std::string& line) {
    if (!write_all(engine.in_fd(), line.data(), line.size()))
        throw std::runtime_error("Engine input pipe failed.");
}

// ---------------------------------------------------------------- bundled tokenizer helper
//
// The helper is a sibling subprocess that reuses the donor tokenizer (tools/strata_tokenizer.py,
// plus sibling gguf_reader.py and the `regex`/`jinja2` runtime modules). Its protocol is
// length-prefixed frames (4-byte LE length + body):
//
//   IDENTITY          -> "IDENT <sha256>"
//   ENC <json>        -> "TON <id,id,...>" | "ERR <msg>"
//   DEC <ids>         -> "BYT <raw bytes>"  | "ERR <msg>"
//   QUIT
bool helper_send(dulche::ChildProcess& h, const std::string& body) {
    if (body.size() > kLimit)
        throw std::invalid_argument("Tokenizer helper request exceeds frame limit.");
    const uint32_t len = static_cast<uint32_t>(body.size());
    unsigned char hdr[4];
    hdr[0] = len & 0xffU; hdr[1] = (len >> 8) & 0xffU;
    hdr[2] = (len >> 16) & 0xffU; hdr[3] = (len >> 24) & 0xffU;
    return write_all(h.in_fd(), hdr, 4) && write_all(h.in_fd(), body.data(), body.size());
}

// Bounded readability wait: returns true when the fd becomes readable (data or EOF) within the
// deadline; returns false on poll error or timeout. This keeps helper reads genuinely deadline-aware
// so a silent/stalled helper cannot stall generation forever.
bool wait_readable(int fd, int timeout_ms) {
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(timeout_ms);
    while (std::chrono::steady_clock::now() < deadline) {
        struct pollfd pfd;
        pfd.fd = fd; pfd.events = POLLIN; pfd.revents = 0;
        const int pr = os_poll(&pfd, 1, 200);
        if (pr < 0) {
            if (errno == EINTR) continue;
            return false;
        }
        if (pr > 0) return true;
    }
    return false;
}

bool helper_recv(dulche::ChildProcess& h, std::string& body) {
    // Real external read deadline; do not block past 30s waiting for the helper.
    if (!wait_readable(h.out_fd(), 30000)) return false;
    unsigned char hdr[4];
    if (!read_exact(h.out_fd(), hdr, 4)) return false;
    const uint32_t len = (uint32_t)hdr[0] | ((uint32_t)hdr[1] << 8)
                       | ((uint32_t)hdr[2] << 16) | ((uint32_t)hdr[3] << 24);
    if (len > kLimit) return false;
    if (!wait_readable(h.out_fd(), 30000)) return false;
    body.assign(len, '\0');
    return read_exact(h.out_fd(), body.data(), len);
}

// ---------------------------------------------------------------- message JSON (ENC)
std::string json_escape(const std::string& s) {
    std::string out;
    out.reserve(s.size() + 8);
    for (char c : s) {
        switch (c) {
            case '"': out += "\\\""; break;
            case '\\': out += "\\\\"; break;
            case '\n': out += "\\n"; break;
            case '\r': out += "\\r"; break;
            case '\t': out += "\\t"; break;
            case '\b': out += "\\b"; break;
            case '\f': out += "\\f"; break;
            default:
                if (static_cast<unsigned char>(c) < 0x20) {
                    char tmp[8];
                    std::snprintf(tmp, sizeof tmp, "\\u%04x", static_cast<unsigned>(c));
                    out += tmp;
                } else {
                    out += c;
                }
        }
    }
    return out;
}

std::string role_name(uint32_t role) {
    switch (role) {
        case 0: return "system";
        case 1: return "user";
        case 2: return "assistant";
        case 3: return "tool";
        default: throw std::invalid_argument("Unknown native chat role.");
    }
}

std::string build_messages_json(const std::vector<Msg>& messages) {
    std::string out = "[";
    bool first = true;
    for (const auto& m : messages) {
        if (!first) out += ",";
        first = false;
        out += "{\"role\":\"" + role_name(m.role) + "\"";
        if (!m.name.empty()) out += ",\"name\":\"" + json_escape(m.name) + "\"";
        out += ",\"content\":\"" + json_escape(m.content) + "\"}";
    }
    out += "]";
    return out;
}

// ---------------------------------------------------------------- DONE terminal parsing
struct DoneMetrics {
    int64_t generated = 0;
    int64_t prompt_tokens = 0;
    double prompt_ms = 0;
    double decode_ms = 0;
    std::string finish;
    int64_t drafts_accepted = 0;
    int64_t drafts_offered = 0;
    int64_t reused = 0;
    int64_t hits = 0;
    int64_t lookups = 0;
    int64_t read_n = 0;
    int64_t offload = 0;
};

static int64_t parse_i64(const std::string& s, const char* what) {
    if (s.empty()) throw std::invalid_argument(std::string("Malformed DONE field: ") + what);
    char* end = nullptr;
    errno = 0;
    long long v = std::strtoll(s.c_str(), &end, 10);
    if (end == s.c_str() || *end != '\0' || errno == ERANGE) throw std::invalid_argument(std::string("Malformed DONE field: ") + what);
    return static_cast<int64_t>(v);
}

static double parse_dbl(const std::string& s, const char* what) {
    if (s.empty()) throw std::invalid_argument(std::string("Malformed DONE field: ") + what);
    char* end = nullptr;
    errno = 0;
    double v = std::strtod(s.c_str(), &end);
    if (end == s.c_str() || *end != '\0' || errno == ERANGE || !std::isfinite(v))
        throw std::invalid_argument(std::string("Malformed DONE field: ") + what);
    return v;
}

static std::vector<std::string> split_space(const std::string& line) {
    std::vector<std::string> tok;
    std::string cur;
    for (char c : line) {
        if (c == ' ' || c == '\n' || c == '\r') { if (!cur.empty()) { tok.push_back(cur); cur.clear(); } }
        else cur.push_back(c);
    }
    if (!cur.empty()) tok.push_back(cur);
    return tok;
}

DoneMetrics parse_done(const std::string& line) {
    if (line.rfind("DONE ", 0) != 0) throw std::invalid_argument("Missing DONE terminal.");
    const auto tok = split_space(line);
    if (tok.size() < 9) throw std::invalid_argument("Malformed DONE terminal.");
    DoneMetrics m;
    m.generated = parse_i64(tok[1], "generated");
    m.prompt_tokens = parse_i64(tok[2], "prompt");
    m.prompt_ms = parse_dbl(tok[3], "prompt_ms");
    m.decode_ms = parse_dbl(tok[4], "decode_ms");
    m.finish = tok[5];
    m.drafts_accepted = parse_i64(tok[6], "drafts_accepted");
    m.drafts_offered = parse_i64(tok[7], "drafts_offered");
    m.reused = parse_i64(tok[8], "reused");
    if (m.generated < 0 || m.prompt_tokens < 0 || m.prompt_ms < 0 || m.decode_ms < 0
        || m.drafts_accepted < 0 || m.drafts_offered < 0 || m.reused < 0)
        throw std::invalid_argument("DONE telemetry must be finite and nonnegative.");
    if (m.finish != "stop" && m.finish != "length" && m.finish != "cancel" && m.finish != "nonfinite")
        throw std::invalid_argument("DONE finish vocabulary is not recognized.");
    if (tok.size() > 9) m.hits = parse_i64(tok[9], "hits");
    if (tok.size() > 10) m.lookups = parse_i64(tok[10], "lookups");
    if (m.hits < 0 || m.lookups < 0) throw std::invalid_argument("DONE cache fields must be nonnegative.");
    if (tok.size() > 14) {
        m.read_n = parse_i64(tok[14], "read_n");
        if (m.read_n < 0) throw std::invalid_argument("DONE read_n must be nonnegative.");
    } else {
        m.read_n = m.prompt_tokens - m.reused;
        if (m.read_n < 0) throw std::invalid_argument("DONE read_n is inconsistent.");
    }
    if (tok.size() > 15) {
        m.offload = parse_i64(tok[15], "offload");
        if (m.offload < 0) throw std::invalid_argument("DONE offload must be nonnegative.");
    }
    for (size_t i = 16; i < tok.size(); ++i) {
        const auto& f = tok[i];
        const auto eq = f.find('=');
        if (eq == std::string::npos || f.size() > 128) throw std::invalid_argument("DONE trailing field is malformed.");
    }
    return m;
}

std::string build_gen_line(int64_t max_new, const std::vector<int64_t>& ids,
                           double temperature, double top_p, uint32_t top_k, uint64_t seed,
                           double min_p, double penalty_repeat, double penalty_freq,
                           double penalty_present, int32_t penalty_last_n) {
    std::string line = "GEN " + std::to_string(max_new);
    if (temperature > 0.0) {
        line += " temperature=" + std::to_string(temperature);
        line += " top_p=" + std::to_string(top_p > 0.0 ? top_p : 1.0);
        if (top_k != 0) line += " top_k=" + std::to_string(top_k);
        if (min_p > 0.0) line += " min_p=" + std::to_string(min_p);
        if (penalty_last_n > 0) line += " penalty_last_n=" + std::to_string(penalty_last_n);
        if (penalty_repeat > 0.0) line += " penalty_repeat=" + std::to_string(penalty_repeat);
        if (penalty_freq > 0.0) line += " penalty_freq=" + std::to_string(penalty_freq);
        if (penalty_present > 0.0) line += " penalty_present=" + std::to_string(penalty_present);
        if (seed != 0) line += " seed=" + std::to_string(seed);
    }
    line += " ";
    for (size_t i = 0; i < ids.size(); ++i) {
        if (i) line += ",";
        line += std::to_string(ids[i]);
    }
    line += "\n";
    return line;
}

// ---------------------------------------------------------------- relay session
struct Session {
    dulche::ChildProcess engine;
    std::optional<dulche::LineReader> engine_reader;
    std::unique_ptr<dulche::ChildProcess> helper;   // bundled tokenizer helper (lazy)
    std::string helper_identity;
    bool loaded = false;
    std::string registration;
    std::string family;                              // resolved verified engine family
    const ModelCapability* capability = nullptr;     // truthfully resolved capability
    std::string pack_dir;
    uint32_t context = 0;
    std::vector<uint32_t> devices;
    std::string expected_tokenizer_identity;
};

// Wait for READY after spawn. Only a live process that prints READY is considered loaded. The
// READY max-context is validated against the requested context.
void wait_ready(dulche::ChildProcess& engine, dulche::LineReader& reader, uint32_t requested_context) {
    std::string line;
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(120);
    while (std::chrono::steady_clock::now() < deadline) {
        if (!engine.alive()) throw std::runtime_error("Engine exited before READY (model load failed).");
        struct pollfd pfd;
        pfd.fd = engine.out_fd(); pfd.events = POLLIN; pfd.revents = 0;
        const int pr = os_poll(&pfd, 1, 200);
        if (pr < 0) {
            if (errno == EINTR) continue;
            break;  // poll error (incl. unsupported Windows) -> bounded fail-closed
        }
        if (pr == 0) continue;  // not readable yet: re-check alive() + deadline
        if (!reader.next(line)) throw std::runtime_error("Engine stdout ended before READY.");
        if (line.rfind("READY ", 0) == 0) {
            const auto tok = split_space(line);
            if (tok.size() >= 2) {
                const int64_t max_ctx = parse_i64(tok[1], "READY max_context");
                if (max_ctx < 0) throw std::runtime_error("Engine READY reports a negative context.");
                if (requested_context > 0 && static_cast<uint64_t>(max_ctx) < requested_context)
                    throw std::runtime_error("Engine READY context is smaller than the requested context.");
            }
            return;
        }
        if (line.rfind("ERR ", 0) == 0) throw std::runtime_error("Engine reported load error: " + line.substr(4));
        // INFO / PP / RESUME telemetry lines are drained harmlessly.
    }
    throw std::runtime_error("Engine did not reach READY within the bound.");
}

// Spawn + verify the bundled helper onto session.helper. Throws with the exact dependency cause
// when the helper (or a required runtime module) is unavailable or its identity mismatches.
void bind_helper(Session& session, int argc, char** argv) {
    if (session.helper) return;
    const std::string helper_path = locate_helper(argc, argv);
    if (helper_path.empty())
        throw std::invalid_argument(
            "candidate text generation dependency: bundled tokenizer helper "
            "(strata_tokenizer_helper.py) could not be located next to the worker; refusing to "
            "fabricate token ids.");
    dulche::SpawnSpec spec;
    // Bind an approved resolved interpreter (env override, then constrained PATH search); the
    // ChildProcess exec-failure handshake confirms the exec actually succeeded. No bare `python3`
    // with execv (execv does not search PATH, so we resolve an absolute path first).
    spec.exe = resolve_python();
    const std::string tokenizer_dir = (std::filesystem::path(session.pack_dir) / "tokenizer").string();
    spec.args.push_back(helper_path);
    spec.args.push_back("--tokenizer-dir");
    spec.args.push_back(tokenizer_dir);
    if (!session.expected_tokenizer_identity.empty()) {
        spec.args.push_back("--expected-identity");
        spec.args.push_back(session.expected_tokenizer_identity);
    }
    auto proc = std::make_unique<dulche::ChildProcess>();
    if (!proc->spawn(spec))
        throw std::invalid_argument(
            "candidate text generation dependency: could not spawn the bundled tokenizer helper "
            "(requires a python3 interpreter with the pinned regex/jinja2 modules); refusing generation "
            "rather than fabricating token ids.");
    std::string resp;
    if (!helper_send(*proc, "IDENTITY") || !helper_recv(*proc, resp) || resp.rfind("IDENT ", 0) != 0)
        throw std::invalid_argument(
            "candidate text generation dependency: bundled tokenizer helper did not answer IDENTITY; "
            "the pinned regex/jinja2 runtime modules are unavailable in this environment.");
    const std::string ident = resp.substr(5);
    if (!session.expected_tokenizer_identity.empty() && ident != session.expected_tokenizer_identity)
        throw std::invalid_argument(
            "candidate text generation dependency: bundled tokenizer identity mismatch (registered "
            + session.expected_tokenizer_identity + " != observed " + ident + ")");
    session.helper = std::move(proc);
    session.helper_identity = ident;
}

// ---------------------------------------------------------------- text generation relay
// Runs one generate request to completion. Emits token frames (type 1) and a terminal frame
// (type 2) on success. On a request-scoped error it emits a failure frame (type 5) for the caller;
// it does not tear down the session for ordinary generation errors.
void run_generation(Session& session, const GenerateRequest& req, int argc, char** argv) {
    bind_helper(session, argc, argv);
    dulche::ChildProcess& helper = *session.helper;

    // ENC messages -> token ids.
    // ENC accepts a tagged object, not a bare messages array. Keep the existing JSON escaping and
    // message ordering, then supply the discriminator required by the bundled helper.
    const std::string json = "{\"messages\":" + build_messages_json(req.messages) + "}";
    if (!helper_send(helper, "ENC " + json)) throw std::runtime_error("Helper input pipe failed.");
    std::string resp;
    if (!helper_recv(helper, resp)) throw std::runtime_error(
        "candidate text generation dependency: bundled tokenizer helper exited; the pinned regex/"
        "jinja2 runtime modules are unavailable in this environment.");
    std::vector<int64_t> ids;
    if (resp.rfind("TON ", 0) == 0) {
        size_t pos = 4;
        while (pos < resp.size()) {
            size_t comma = resp.find(',', pos);
            const std::string part = resp.substr(pos, comma == std::string::npos ? std::string::npos : comma - pos);
            ids.push_back(parse_i64(part, "token id"));
            if (comma == std::string::npos) break;
            pos = comma + 1;
        }
    } else if (resp.rfind("ERR ", 0) == 0) {
        throw std::invalid_argument("candidate text generation dependency: " + resp.substr(4));
    } else {
        throw std::invalid_argument("candidate text generation dependency: malformed TON response.");
    }
    if (ids.empty()) throw std::invalid_argument("candidate text generation dependency: tokenizer produced no ids for the prompt.");

    const std::string line = build_gen_line(req.max_new, ids, req.temperature, req.top_p, req.top_k,
                                             req.seed, req.min_p, req.penalty_repeat, req.penalty_freq,
                                             req.penalty_present, req.penalty_last_n);
    engine_send(session.engine, line);

    bool cancelled = false;
    auto stall_deadline = std::chrono::steady_clock::now() + std::chrono::seconds(120);

    for (;;) {
        std::string line_out;
        bool engine_readable = false;
        bool stdin_readable = false;
        {
            struct pollfd pfd[2];
            pfd[0].fd = 0; pfd[0].events = POLLIN; pfd[0].revents = 0;
            pfd[1].fd = session.engine.out_fd(); pfd[1].events = POLLIN; pfd[1].revents = 0;
            const int pr = os_poll(pfd, 2, 200);
            if (pr < 0) { if (errno == EINTR) continue; throw std::runtime_error("poll failed during generation."); }
            if (pfd[0].revents & (POLLIN | POLLHUP)) stdin_readable = true;
            if (pfd[1].revents & (POLLIN | POLLHUP | POLLERR)) engine_readable = true;
        }

        if (stdin_readable) {
            Buffer cmd;
            if (read_command(cmd)) {
                const uint32_t op = cmd.read_number<uint32_t>();
                if (op == kOpCancel) {
                    if (!cancelled) { engine_send(session.engine, "STOP\n"); cancelled = true; }
                } else if (op == kOpStop) {
                    engine_send(session.engine, "QUIT\n");
                    cancelled = true;
                } else {
                    throw std::invalid_argument("Unexpected native command during generation.");
                }
            }
        }

        if (engine_readable) {
            if (!session.engine_reader->next(line_out)) {
                throw std::runtime_error("Engine stdout ended during generation.");
            }
            stall_deadline = std::chrono::steady_clock::now() + std::chrono::seconds(120);
            if (line_out.rfind("T ", 0) == 0) {
                const int64_t id = parse_i64(line_out.substr(2), "T id");
                if (id < 0) throw std::invalid_argument("Negative token id from engine.");
                std::string dec;
                if (!helper_send(helper, "DEC " + std::to_string(id)) || !helper_recv(helper, dec))
                    throw std::runtime_error("candidate text generation dependency: helper DEC round trip failed.");
                if (dec.rfind("BYT ", 0) == 0) {
                    const std::string raw = dec.substr(4);
                    Buffer tk;
                    tk.number(static_cast<uint32_t>(raw.size()));
                    tk.bytes.insert(tk.bytes.end(), raw.begin(), raw.end());
                    write_frame(stdout, 1, tk);
                } else if (dec.rfind("ERR ", 0) == 0) {
                    throw std::invalid_argument("candidate text generation dependency: " + dec.substr(4));
                }
                continue;
            }
            if (line_out.rfind("DONE ", 0) == 0) {
                const DoneMetrics m = parse_done(line_out);
                Buffer term;
                term.number<uint32_t>(0); term.number<uint32_t>(0);           // NativeErrors: none
                term.text(std::string());
                term.number(static_cast<uint64_t>(m.prompt_tokens));
                term.number(static_cast<uint64_t>(m.read_n));
                term.number(static_cast<uint64_t>(m.generated));
                term.real(m.prompt_ms / 1000.0);
                term.real(m.decode_ms / 1000.0);
                const uint64_t reused = m.reused;
                term.number(static_cast<uint32_t>(reused > 0 ? 1 : 0));
                if (reused > 0) term.number(reused);
                term.number(static_cast<uint32_t>(0));                        // kv: do not claim incremental KV
                term.number(static_cast<uint32_t>(m.finish == "cancel" ? 1 : 0));
                write_frame(stdout, 2, term);
                return;
            }
            if (line_out.rfind("ERR ", 0) == 0) {
                throw std::invalid_argument("Engine reported generation error: " + line_out.substr(4));
            }
            // INFO / PP / RESUME / REUSED / YIELDED telemetry lines are drained harmlessly.
            continue;
        }

        if (std::chrono::steady_clock::now() > stall_deadline)
            throw std::runtime_error("Engine generation stalled without output (bounded).");
    }
}

// Emit a failure frame (type 5) with the standard NativeErrors layout.
void emit_failure(const std::string& message) {
    Buffer failure;
    failure.number<uint32_t>(3); failure.number<uint32_t>(1); failure.text(message);
    write_frame(stdout, 5, failure);
}

}  // namespace

int main(int argc, char** argv) {
    set_binary_stdio();
#if !defined(_WIN32)
    signal(SIGPIPE, SIG_IGN);
#endif
    const bool probe = argc == 2 && std::strcmp(argv[1], "--hardware-probe") == 0;
    if (argc != 1 && !probe) return 64;

    Session session;
    try {
        // Hello: explicit incompatible ABI version 2, candidate origin, candidate-verified family.
        Buffer hello;
        hello.number(kAbiVersion);
        hello.text(std::string("011c9e600a93bbc9fd451c563501b5831c32bbca"));
        hello.number(static_cast<uint32_t>(STRATA_HAS_CUDA));
        hello.number(static_cast<uint32_t>(0));  // NCCL not claimed by the candidate relay
        hello.text(std::string("qwen35moe"));
        write_frame(stdout, 0, hello);

        if (probe) {
            try {
                const auto observed = hardware_probe();
                write_frame(stdout, 6, observed);
                std::fflush(stdout);
                return STRATA_HAS_CUDA ? 0 : 69;
            } catch (const std::exception& error) {
                Buffer failure;
                failure.number<uint32_t>(3); failure.number<uint32_t>(1); failure.text(error.what());
                write_frame(stdout, 6, failure);
                std::fflush(stdout);
                return 69;
            }
        }

        Buffer command;
        while (read_command(command)) {
            const uint32_t operation = command.read_number<uint32_t>();
            if (operation == kOpLoad) {
                const auto directory = command.read_text();
                const auto registration = command.read_text();
                const auto context = command.read_number<uint32_t>();
                const auto expected_identity = command.read_text();
                const auto count = command.read_number<uint32_t>();
                if (count > 64) throw std::invalid_argument("Too many native devices.");
                session.devices.clear();
                for (uint32_t i = 0; i < count; ++i) session.devices.push_back(command.read_number<uint32_t>());
                command.end();
                if (session.loaded) throw std::invalid_argument("Native session is already loaded.");
                if (registration.empty() || directory.empty()) throw std::invalid_argument(
                    "Native load requires a registration and pack directory.");

                // Validate the requested registration/family/format/topology against the protected
                // model identity by truthfully resolving the registration to a verified model
                // capability. Manual no-fallback model identity: an unknown registration is
                // refused (never silently downgraded to a different model). Automatic genuine
                // compatible fallback: verified family/identity spellings resolve to the engine's
                // registered `qwen35moe` family. Tools/vision/MTP are never advertised.
                const ModelCapability* capability = resolve_capability_tolerant(registration);
                if (capability == nullptr)
                    throw std::invalid_argument(
                        "candidate load: registration is not a verified qwen35moe-family model "
                        "(confirmed: Ornith-1.5-122B, Sage-A1.5); refusing to fabricate a load.");
                if (context == 0) throw std::invalid_argument("candidate load: context must be positive.");

                session.registration = registration;
                session.family = capability->family;
                session.capability = capability;
                session.pack_dir = directory;
                session.context = context;
                session.expected_tokenizer_identity = expected_identity;

                // The approved device selection is mapped to the upstream `--gpu` comma-separated argv
                // (not a custom env the upstream never reads). build_engine_args throws on a duplicate/
                // ambiguous selection and omits `--gpu` entirely when no devices are requested.
                dulche::SpawnSpec spec;
                spec.exe = locate_engine(argc, argv);
                if (spec.exe.empty()) throw std::invalid_argument(
                    "candidate load dependency: bundled engine binary could not be located next to the worker "
                    "(expected runtimes/<target>/engine/strata[.exe]); refusing to fabricate a load.");
                spec.args = build_engine_args(context, session.devices);
                if (!session.engine.spawn(spec)) throw std::runtime_error(
                    "candidate load dependency: engine spawn failed (exact argv, no shell).");
                session.engine_reader.emplace(session.engine.out_fd());
                wait_ready(session.engine, *session.engine_reader, context);
                session.loaded = true;
                Buffer loaded;
                loaded.number<uint32_t>(0); loaded.number<uint32_t>(0);  // NativeErrors: none
                write_frame(stdout, 4, loaded);
            } else if (operation == kOpGenerate || operation == kOpStructuredGenerate) {
                GenerateRequest req;
                if (operation == kOpStructuredGenerate) {
                    if (command.read_number<uint32_t>() != 1)
                        throw std::invalid_argument("Unsupported structured tool wire version.");
                    req.structured = true;
                }
                const auto message_count = command.read_number<uint32_t>();
                if (message_count > 4096) throw std::invalid_argument("Too many native messages.");
                for (uint32_t i = 0; i < message_count; ++i) {
                    Msg m;
                    m.role = command.read_number<uint32_t>();
                    m.content = command.read_text();
                    m.name = command.read_text();
                    const auto images = command.read_number<uint32_t>();
                    if (images != 0) throw std::invalid_argument("Image generation is not supported by the candidate text relay.");
                    req.messages.push_back(std::move(m));
                }
                req.max_new = command.read_number<uint32_t>();
                req.temperature = command.read_real();
                req.top_p = command.read_real();
                req.top_k = command.read_number<uint32_t>();
                req.seed = command.read_number<uint64_t>();
                const auto stop_count = command.read_number<uint32_t>();
                if (stop_count > 4096) throw std::invalid_argument("Too many native stops.");
                for (uint32_t i = 0; i < stop_count; ++i) (void)command.read_text();
                const auto effort = command.read_text();
                command.end();

                if (!session.loaded) throw std::invalid_argument("Native generation requires a loaded engine.");
                if (req.max_new == 0 || req.max_new > 1U << 20) throw std::invalid_argument("Bounded native max_new is invalid.");
                if (stop_count > 0) throw std::invalid_argument(
                    "candidate: explicit stop lists are not yet supported by the relay; refusing this option "
                    "rather than silently dropping it.");
                if (!effort.empty() && effort != "medium" && effort != "none")
                    throw std::invalid_argument("candidate: unsupported reasoning effort " + effort + "; only medium/none are mapped.");

                if (req.structured) {
                    // Structured tools remain unavailable until real loaded-model reasoning metadata and a
                    // proven same-model probe exist. Text generation continues to work.
                    emit_failure(
                        "structured tool request requires a proven same-model tool probe, which is not "
                        "available for this candidate registration yet; text generation remains available.");
                    continue;
                }
                req.expected_tokenizer_identity = session.expected_tokenizer_identity;
                try {
                    run_generation(session, req, argc, argv);
                } catch (const std::exception& error) {
                    emit_failure(error.what());
                }
            } else if (operation == kOpStop) {
                command.end();
                if (session.engine.spawned()) {
                    if (session.engine.alive()) engine_send(session.engine, "QUIT\n");
                    if (!session.engine.wait_exit(3000) && session.engine.alive()) session.engine.kill();
                }
                session.engine.shutdown();
                Buffer stopped;
                write_frame(stdout, 3, stopped);
                std::fflush(stdout);
                return 0;
            } else {
                throw std::invalid_argument("Unsupported native operation.");
            }
        }
        session.engine.shutdown();
        std::fflush(stdout);
        return 0;
    } catch (const std::exception& error) {
        try {
            Buffer failure;
            failure.number<uint32_t>(3); failure.number<uint32_t>(1); failure.text(error.what());
            write_frame(stdout, 5, failure);
            std::fflush(stdout);
        } catch (...) {}
        session.engine.shutdown();   // exception-safe RAII teardown (no std::terminate)
        return 72;
    } catch (...) {
        session.engine.shutdown();
        return 73;
    }
}
