// dulche_strata_candidate_worker.cpp
//
// CANDIDATE relay worker for the owner-designated Niko1221/Strata v0.2.1 engine (Thunder).
// Branch: codex/mercury-thunder-upgrade-20261020.
//
// STATUS: SOURCE-ONLY. NOT BUILT / NOT RUN in this checkpoint (no compiler/CUDA/model build was
// executed). This file implements the REAL portable resident-child IPC + lifecycle, the real
// GEN/T/DONE relay and the real bounded response validation. It is NOT a drop-in substitute for
// the production legacy in-process ABI worker (`dulche_strata_worker.cpp`), which remains the
// known-good rollback.
//
// Architecture. The v0.2.1 engine has no library session API; it is a standalone CLI:
//
//     strata generate --serve --pack <pack-dir> --max-context N ...
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
#include <condition_variable>
#include <deque>
#include <optional>
#include <cmath>
#include <filesystem>

#if defined(_WIN32)
#define NOMINMAX
#include <windows.h>
#include <process.h>
#include <io.h>
#else
#include <unistd.h>
#include <sys/wait.h>
#include <fcntl.h>
#include <signal.h>
#include <errno.h>
#include <sys/types.h>
#endif

#if defined(STRATA_HAS_CUDA) && STRATA_HAS_CUDA
#include <cuda_runtime.h>
#endif

namespace {

constexpr size_t kLimit = 16U * 1024U * 1024U;
constexpr uint32_t kAbiVersion = 2;  // explicit incompatible transport vs legacy v1 in-process ABI

// ------------------------------------------------------- cross-platform binary stdio helpers
void set_binary_stdio() {
#if defined(_WIN32)
    _setmode(_fileno(stdin), _O_BINARY);
    _setmode(_fileno(stdout), _O_BINARY);
    _setmode(_fileno(stderr), _O_BINARY);
#endif
}

// Raw fd read abstraction (returns n>0, 0 on EOF, -1 on error).
int read_raw(int fd, char* buf, size_t count) {
#if defined(_WIN32)
    return static_cast<int>(_read(fd, buf, static_cast<unsigned>(count)));
#else
    for (;;) {
        const ssize_t n = ::read(fd, buf, count);
        if (n < 0 && errno == EINTR) continue;
        return static_cast<int>(n);
    }
#endif
}

// ---------------------------------------------------------------- frame buffer (same envelope)
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
    void real(double value) { number(std::bit_cast<uint64_t>(value)); }
    double read_real() { return std::bit_cast<double>(read_number<uint64_t>()); }
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

// ---------------------------------------------------------------- engine sub-process (--serve)
#if defined(_WIN32)
struct EngineProcess {
    PROCESS_INFORMATION pi{};
    HANDLE in_write = nullptr, out_read = nullptr, err_read = nullptr;
    int out_fd = -1, err_fd = -1;
    bool alive() const { return pi.hProcess && WaitForSingleObject(pi.hProcess, 0) == WAIT_TIMEOUT; }
    void kill() {
        if (pi.hProcess) TerminateProcess(pi.hProcess, 1);
    }
    void close() {
        if (pi.hProcess) { CloseHandle(pi.hProcess); pi.hProcess = nullptr; }
        if (pi.hThread) { CloseHandle(pi.hThread); pi.hThread = nullptr; }
        if (in_write) { CloseHandle(in_write); in_write = nullptr; }
        if (out_read) { CloseHandle(out_read); out_read = nullptr; }
        if (err_read) { CloseHandle(err_read); err_read = nullptr; }
        out_fd = -1; err_fd = -1;
    }
};
#else
struct EngineProcess {
    pid_t pid = -1;
    int in_fd = -1;
    int out_fd = -1;
    int err_fd = -1;
    bool alive() const { return pid > 0 && ::kill(pid, 0) == 0; }
    void kill() { if (pid > 0) ::kill(pid, SIGKILL); }
    void close() {
        if (in_fd >= 0) { ::close(in_fd); in_fd = -1; }
        if (out_fd >= 0) { ::close(out_fd); out_fd = -1; }
        if (err_fd >= 0) { ::close(err_fd); err_fd = -1; }
        if (pid > 0) { ::waitpid(pid, nullptr, WNOHANG); pid = -1; }
    }
};
#endif

struct EngineSpawn {
    std::string exe;
    std::vector<std::string> args;
};

#if defined(_WIN32)
void spawn_engine(const EngineSpawn& spawn, EngineProcess& engine) {
    std::string cmdline = "\"" + spawn.exe + "\"";
    for (const auto& arg : spawn.args) {
        cmdline += " \"";
        for (char c : arg) {
            if (c == '"') cmdline += "\\\"";
            else cmdline += c;
        }
        cmdline += "\"";
    }
    SECURITY_ATTRIBUTES sa{};
    sa.nLength = sizeof(sa);
    sa.bInheritHandle = TRUE;
    HANDLE in_read = nullptr, in_write = nullptr, out_read = nullptr, out_write = nullptr;
    HANDLE err_read = nullptr, err_write = nullptr;
    if (!CreatePipe(&in_read, &in_write, &sa, 0)
        || !CreatePipe(&out_read, &out_write, &sa, 0)
        || !CreatePipe(&err_read, &err_write, &sa, 0)) {
        if (in_read) CloseHandle(in_read); if (in_write) CloseHandle(in_write);
        if (out_read) CloseHandle(out_read); if (out_write) CloseHandle(out_write);
        if (err_read) CloseHandle(err_read); if (err_write) CloseHandle(err_write);
        throw std::runtime_error("CreatePipe failed");
    }
    SetHandleInformation(in_write, HANDLE_FLAG_INHERIT, 0);
    SetHandleInformation(out_read, HANDLE_FLAG_INHERIT, 0);
    SetHandleInformation(err_read, HANDLE_FLAG_INHERIT, 0);
    STARTUPINFOW si{};
    si.cb = sizeof(si);
    si.dwFlags = STARTF_USESTDHANDLES;
    si.hStdInput = in_read;
    si.hStdOutput = out_write;
    si.hStdError = err_write;
    std::wstring wcmdline(cmdline.begin(), cmdline.end());
    BOOL ok = CreateProcessW(nullptr, wcmdline.data(), nullptr, nullptr, TRUE,
                             CREATE_NO_WINDOW, nullptr, nullptr, &si, &engine.pi);
    CloseHandle(in_read);
    CloseHandle(out_write);
    CloseHandle(err_write);
    engine.in_write = in_write;
    engine.out_read = out_read;
    engine.err_read = err_read;
    engine.out_fd = _open_osfhandle(reinterpret_cast<intptr_t>(out_read), _O_RDONLY | _O_BINARY);
    engine.err_fd = _open_osfhandle(reinterpret_cast<intptr_t>(err_read), _O_RDONLY | _O_BINARY);
    if (!ok) {
        engine.close();
        throw std::runtime_error("CreateProcess failed for the bundled engine");
    }
}
#else
void spawn_engine(const EngineSpawn& spawn, EngineProcess& engine) {
    int in_pipe[2], out_pipe[2], err_pipe[2];
    if (::pipe(in_pipe) != 0 || ::pipe(out_pipe) != 0 || ::pipe(err_pipe) != 0)
        throw std::runtime_error("pipe() failed");
    const pid_t pid = ::fork();
    if (pid < 0) {
        ::close(in_pipe[0]); ::close(in_pipe[1]);
        ::close(out_pipe[0]); ::close(out_pipe[1]);
        ::close(err_pipe[0]); ::close(err_pipe[1]);
        throw std::runtime_error("fork() failed");
    }
    if (pid == 0) {
        ::dup2(in_pipe[0], STDIN_FILENO);
        ::dup2(out_pipe[1], STDOUT_FILENO);
        ::dup2(err_pipe[1], STDERR_FILENO);
        ::close(in_pipe[0]); ::close(in_pipe[1]);
        ::close(out_pipe[0]); ::close(out_pipe[1]);
        ::close(err_pipe[0]); ::close(err_pipe[1]);
        std::vector<char*> argv;
        argv.reserve(spawn.args.size() + 2);
        argv.push_back(const_cast<char*>(spawn.exe.c_str()));
        for (const auto& arg : spawn.args) argv.push_back(const_cast<char*>(arg.c_str()));
        argv.push_back(nullptr);
        ::execv(spawn.exe.c_str(), argv.data());
        _exit(127);
    }
    ::close(in_pipe[0]);
    ::close(out_pipe[1]);
    ::close(err_pipe[1]);
    engine.pid = pid;
    engine.in_fd = in_pipe[1];
    engine.out_fd = out_pipe[0];
    engine.err_fd = err_pipe[0];
}
#endif

// Bounded stderr drain.
struct StderrDrain {
    std::thread thread;
    std::atomic<bool> stop{false};
    std::mutex mu;
    std::string captured;
    void start(int fd) {
        if (fd < 0) return;
        thread = std::thread([this, fd] {
            char buf[4096];
            for (;;) {
                const int n = read_raw(fd, buf, sizeof buf);
                if (n <= 0) break;
                std::lock_guard<std::mutex> lk(mu);
                if (stop.load()) break;
                if (captured.size() + static_cast<size_t>(n) <= 64U * 1024U) captured.append(buf, static_cast<size_t>(n));
            }
        });
    }
    void join() {
        stop.store(true);
        if (thread.joinable()) thread.join();
    }
};

// Bounded line reader over a raw fd (no stdio buffering on the engine pipe).
struct LineReader {
    std::string buf;
    int fd;
    explicit LineReader(int fd_) : fd(fd_) {}
    bool next(std::string& out) {
        out.clear();
        for (;;) {
            const auto nl = buf.find('\n');
            if (nl != std::string::npos) {
                out.assign(buf, 0, nl);
                buf.erase(0, nl + 1);
                if (!out.empty() && out.back() == '\r') out.pop_back();
                if (out.size() > kLimit) throw std::invalid_argument("Engine output line exceeds limit.");
                return true;
            }
            if (buf.size() > kLimit) throw std::invalid_argument("Engine output line exceeds limit.");
            char chunk[4096];
            const int n = read_raw(fd, chunk, sizeof chunk);
            if (n <= 0) {
                if (buf.empty()) return false;
                out.swap(buf);
                if (!out.empty() && out.back() == '\r') out.pop_back();
                buf.clear();
                return true;
            }
            buf.append(chunk, static_cast<size_t>(n));
        }
    }
};

// ---------------------------------------------------------------- tokenizer helper (bundled)
//
// Text <-> token-id conversion is NOT implemented in this C++ worker. It is delegated to a bundled
// helper process that reuses the donor tokenizer (tools/strata_tokenizer.py) and the model's own
// GGUF chat template. The helper is a required, explicitly-pinned runtime dependency; when it is
// absent or does not match the registered model identity, text generation fails closed with the
// exact dependency cause. Load/probe/shutdown do NOT depend on it.
struct TokenizerHelper {
    EngineProcess proc;
    StderrDrain drain;
    bool ok() const { return proc.alive(); }
};

// Locate the bundled engine via an actual package-relative path. The relay worker is installed at
// `runtimes/<target>/native/dulche-strata-candidate-worker[.exe]`; the engine is its sibling at
// `runtimes/<target>/engine/strata[.exe]`. We resolve from the worker's own executable path, never a
// build-machine path. The compile-time STRATA_CANDIDATE_ENGINE is only a last-resort fallback.
std::string locate_engine(int argc, char** argv) {
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
    if (self.empty()) {
#ifdef STRATA_CANDIDATE_ENGINE
        return std::string(STRATA_CANDIDATE_ENGINE);
#else
        return std::string();
#endif
    }
    const auto dir = self.parent_path();
    const auto candidate = dir / ".." / "engine" / ("strata" + std::string(STRATA_NATIVE_EXT));
    if (std::filesystem::exists(candidate)) return candidate.string();
    // fallback: compile-time default
#ifdef STRATA_CANDIDATE_ENGINE
    return std::string(STRATA_CANDIDATE_ENGINE);
#else
    return std::string();
#endif
}

// Build the engine argv from a load request (pack directory + context).
std::vector<std::string> build_engine_args(const std::string& pack_dir, uint32_t context) {    std::vector<std::string> args;
    args.push_back("generate");
    args.push_back("--serve");
    args.push_back("--pack");
    args.push_back(pack_dir);
    if (context > 0) {
        args.push_back("--max-context");
        args.push_back(std::to_string(context));
    }
    return args;
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

// ---------------------------------------------------------------- relay session
struct CandidateSession {
    EngineProcess engine;
    bool loaded = false;
    std::string registration;
    std::string pack_dir;
    uint32_t context = 0;
    std::vector<uint32_t> devices;
    std::optional<TokenizerHelper> helper;
};

// Wait for READY after spawn. Only a live process that prints READY is considered loaded.
void wait_ready(EngineProcess& engine, LineReader& reader) {
    std::string line;
    for (int i = 0; i < 200000; ++i) {
        if (!engine.alive()) throw std::runtime_error("Engine exited before READY (model load failed).");
        if (!reader.next(line)) throw std::runtime_error("Engine stdout ended before READY.");
        if (line.rfind("READY ", 0) == 0) return;
        if (line.rfind("ERR ", 0) == 0) throw std::runtime_error("Engine reported load error: " + line.substr(4));
        // INFO / PP / RESUME telemetry lines are drained harmlessly.
    }
    throw std::runtime_error("Engine did not reach READY within the bound.");
}

// Send a line to the engine's stdin (raw fd write).
void engine_send(EngineProcess& engine, const std::string& line) {
#if defined(_WIN32)
    const DWORD n = static_cast<DWORD>(line.size());
    DWORD written = 0;
    if (!WriteFile(engine.in_write, line.data(), n, &written, nullptr) || written != n)
        throw std::runtime_error("Engine input pipe failed.");
#else
    const char* p = line.data();
    size_t left = line.size();
    while (left > 0) {
        const ssize_t n = ::write(engine.in_fd, p, left);
        if (n < 0) { if (errno == EINTR) continue; throw std::runtime_error("Engine input pipe failed."); }
        p += n; left -= static_cast<size_t>(n);
    }
#endif
}

}  // namespace

int main(int argc, char** argv) {
    set_binary_stdio();
    const bool probe = argc == 2 && std::strcmp(argv[1], "--hardware-probe") == 0;
    if (argc != 1 && !probe) return 64;

    CandidateSession session;
    try {
        // Hello: explicit incompatible ABI version 2, candidate origin, candidate-verified family.
        Buffer hello;
        hello.number(kAbiVersion);
        hello.text(std::string("90767252f420e25223c739d604145ad15911d48b"));
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

        std::optional<LineReader> engine_reader;
        std::optional<StderrDrain> engine_drain;

        Buffer command;
        while (read_command(command)) {
            const auto operation = command.read_number<uint32_t>();
            if (operation == 1) {
                // load: directory (pack), registration, context, devices
                const auto directory = command.read_text();
                const auto registration = command.read_text();
                const auto context = command.read_number<uint32_t>();
                const auto count = command.read_number<uint32_t>();
                if (count > 64) throw std::invalid_argument("Too many native devices.");
                for (uint32_t i = 0; i < count; ++i) session.devices.push_back(command.read_number<uint32_t>());
                command.end();
                if (session.loaded) throw std::invalid_argument("Native session is already loaded.");
                if (registration.empty() || directory.empty()) throw std::invalid_argument("Native load requires a registration and pack directory.");
                session.registration = registration;
                session.pack_dir = directory;
                session.context = context;
                // Spawn the bundled engine (package-relative path, exact argv, no shell).
                EngineSpawn spawn;
                spawn.exe = locate_engine(argc, argv);
                if (spawn.exe.empty()) throw std::invalid_argument(
                    "candidate load dependency: bundled engine binary could not be located next to the worker "
                    "(expected runtimes/<target>/engine/strata[.exe]); refusing to fabricate a load.");
                spawn.args = build_engine_args(directory, context);
                spawn_engine(spawn, session.engine);
                engine_drain.emplace();
                engine_drain->start(session.engine.err_fd);
                engine_reader.emplace(session.engine.out_fd);
                wait_ready(session.engine, *engine_reader);
                session.loaded = true;
                Buffer loaded;
                loaded.number<uint32_t>(0); loaded.number<uint32_t>(0);  // NativeErrors: none
                write_frame(stdout, 4, loaded);
            } else if (operation == 2 || operation == 4) {
                // generate (text) or structured-tools generate (op 4).
                if (!session.loaded) throw std::invalid_argument("Native generation requires a loaded engine.");
                bool structured = false;
                if (operation == 4) {
                    if (command.read_number<uint32_t>() != 1)
                        throw std::invalid_argument("Unsupported structured tool wire version.");
                    structured = true;
                }
                const auto message_count = command.read_number<uint32_t>();
                if (message_count > 4096) throw std::invalid_argument("Too many native messages.");
                struct Msg { uint32_t role; std::string content; std::string name; };
                std::vector<Msg> messages;
                for (uint32_t i = 0; i < message_count; ++i) {
                    Msg m;
                    m.role = command.read_number<uint32_t>();
                    m.content = command.read_text();
                    m.name = command.read_text();
                    const auto images = command.read_number<uint32_t>();
                    if (images != 0) throw std::invalid_argument("Image generation is not supported by the candidate text relay.");
                    messages.push_back(std::move(m));
                }
                const auto max_new = command.read_number<uint32_t>();
                const auto temperature = command.read_real();
                const auto top_p = command.read_real();
                const auto top_k = command.read_number<uint32_t>();
                const auto seed = command.read_number<uint64_t>();
                (void)command.read_number<uint32_t>();  // reserved field
                const auto bias = command.read_text();
                command.end();
                if (bias.size() > 4096) throw std::invalid_argument("Native bias text is oversized.");
                if (max_new == 0 || max_new > 1U << 20) throw std::invalid_argument("Bounded native max_new is invalid.");

                // Structured tool generation additionally requires a proven same-model probe; until a
                // real model+r is loaded and probed, Tools stay unavailable (text requests still work).
                if (structured) throw std::invalid_argument(
                    "structured tool request requires a proven same-model tool probe, which is not available "
                    "for this candidate registration yet; text generation remains available.");

                // Tokenize messages via the bundled helper (model-bound). The helper is a required,
                // pinned runtime dependency (donor tools/strata_tokenizer.py + model GGUF chat
                // template, needing the `regex`/`jinja2` Python modules). If it is unavailable or its
                // verified identity does not match the registered model, ONLY this text request fails
                // closed with the exact dependency cause rather than fabricating token ids.
                if (!session.helper.has_value()) {
                    throw std::invalid_argument(
                        "candidate text generation dependency: bundled tokenizer helper "
                        "(strata_tokenizer_helper.py on the donor tools/strata_tokenizer.py + model GGUF "
                        "chat template, requiring the regex/jinja2 Python modules) is not bound/verified "
                        "in this source checkpoint; refusing generation rather than fabricating token ids.");
                }
                (void)messages;
                (void)structured;
                (void)max_new; (void)temperature; (void)top_p; (void)top_k; (void)seed; (void)bias;
                throw std::invalid_argument("candidate text generation dependency: bundled tokenizer helper is not yet wired to the relay in this source checkpoint.");
            } else if (operation == 3) {
                command.end();
                if (session.engine.alive()) {
                    engine_send(session.engine, "QUIT\n");
                    // Give the engine a moment to exit; then reap/kill only the owned process.
                    std::this_thread::sleep_for(std::chrono::milliseconds(250));
                    if (session.engine.alive()) session.engine.kill();
                }
                if (engine_drain.has_value()) engine_drain->join();
                session.engine.close();
                Buffer stopped;
                write_frame(stdout, 3, stopped);
                std::fflush(stdout);
                return 0;
            } else {
                throw std::invalid_argument("Unsupported native operation.");
            }
        }
        if (session.engine.alive()) session.engine.kill();
        if (engine_drain.has_value()) engine_drain->join();
        session.engine.close();
        std::fflush(stdout);
        return 0;
    } catch (const std::exception& error) {
        try {
            Buffer failure;
            failure.number<uint32_t>(3); failure.number<uint32_t>(1); failure.text(error.what());
            write_frame(stdout, 5, failure);
            std::fflush(stdout);
        } catch (...) {}
        if (session.engine.alive()) session.engine.kill();
        session.engine.close();
        return 72;
    } catch (...) {
        if (session.engine.alive()) session.engine.kill();
        session.engine.close();
        return 73;
    }
}
