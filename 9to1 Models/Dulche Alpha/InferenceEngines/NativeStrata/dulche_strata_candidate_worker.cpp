// dulche_strata_candidate_worker.cpp
//
// CANDIDATE (source-only) relay worker for the owner-designated Niko1221/Strata v0.2.1 engine.
//
// STATUS: SOURCE-ONLY. NOT BUILT / NOT RUN in this checkpoint (no compiler/CUDA/model build was
// executed). This file is an explicit CANDIDATE TRANSPORT with a real framing/lifetime contract; it
// is NOT a drop-in substitute for the production legacy in-process ABI worker
// (`dulche_strata_worker.cpp`), which remains the known-good rollback.
//
// Architecture. The v0.2.1 engine has NO library session API (core/session.hpp is low-level GPU
// session state, not a chat runtime; qwen35/engine.hpp exposes only `run(argc,argv)`). It is a
// standalone CLI: `strata generate --serve ...` loads weights once and then serves line-oriented
// requests on stdin/stdout:
//
//     in:  GEN <max_new> [sampling keys] <id,id,...>
//     out: T <id>                     (per generated token)
//          ERR <message>              (request could not run)
//          PP <pos> <pt> <ms> <tok/s> (prompt chunk telemetry)
//          RESUME <n> / REUSED <n>    (cache reuse telemetry)
//          DONE <generated> <prompt> <prompt_ms> <decode_ms> <finish>
//               <drafts_accept> <drafts_offer> <reused> [hits] [lookups] ...
//          STOP                        (cancel the current request)
//          QUIT                        (end the process)
//
// This worker hosts the Dulche-owned bounded frame ABI on its own stdin/stdout and relays to the
// engine sub-process through that protocol. It preserves the original host-side envelope framing
// (little-endian type+length frames), load/unload custody, bounded streaming/end/error, cancellation
// and shutdown. It deliberately does NOT claim legacy capability parity: the engine consumes token
// IDs (no C++ text tokenizer in generate.cpp; tokenisation lives in the donor's Python
// tools/strata_tokenizer.py / a pack tokenizer) and its decode is greedy by default.
//
// FAIL-CLOSED TOKENISER GAP. The host sends chat TEXT; the engine needs token IDs. Until a verified
// tokenizer provider (donor Python tokenizer, or a future C++ BPE) is bound and validated in a real
// build, any text generation request is refused with a native error. The transport (spawn, framing,
// GEN/T/DONE parsing, cancellation, shutdown, telemetry translation) is implemented here, but the
// text->token boundary is a REQUIRED BLOCKER with an explicit callsite (`tokenize_messages`).

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
#include <memory>
#include <limits>
#include <stdexcept>
#include <bit>
#include <algorithm>

#if defined(_WIN32)
#include <windows.h>
#include <process.h>
#else
#include <unistd.h>
#include <sys/wait.h>
#include <fcntl.h>
#endif

#if STRATA_HAS_CUDA
#include <cuda_runtime.h>
#endif

namespace {

constexpr size_t kLimit = 16U * 1024U * 1024U;
constexpr uint32_t kAbiVersion = 2;  // explicit incompatible transport vs legacy v1 in-process ABI

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
#if STRATA_HAS_CUDA
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

// ---------------------------------------------------------------- engine sub-process (serve)
#if defined(_WIN32)
// Windows: CreateProcess spawn. (The owner's Windows RTX 5080 is the target; the actual build and
// run are NOT executed in this checkpoint.)
struct EngineProcess {
    PROCESS_INFORMATION pi{};
    HANDLE in_write = nullptr, out_read = nullptr;
    bool alive() const { return pi.hProcess && WaitForSingleObject(pi.hProcess, 0) == WAIT_TIMEOUT; }
    void kill() { if (pi.hProcess) TerminateProcess(pi.hProcess, 1); }
    void close() {
        if (pi.hProcess) { CloseHandle(pi.hProcess); pi.hProcess = nullptr; }
        if (pi.hThread) { CloseHandle(pi.hThread); pi.hThread = nullptr; }
        if (in_write) { CloseHandle(in_write); in_write = nullptr; }
        if (out_read) { CloseHandle(out_read); out_read = nullptr; }
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

// The engine command line is assembled from the verified upstream build; this is what the relay
// spawns. The actual argv is filled by `spawn_engine` from the load registration/config.
struct EngineSpawn {
    std::string exe;      // path to the `strata` engine binary
    std::vector<std::string> args;
};

// ---------------------------------------------------------------- tokenizer boundary (blocker)
// The host sends chat text; the v0.2.1 engine consumes token IDs (no C++ tokenizer in generate.cpp).
// A verified tokenizer provider (donor Python tools/strata_tokenizer.py or a future C++ BPE) is not
// bound in this source checkpoint, so this function always fails closed with an explicit reason.
std::vector<int64_t> tokenize_messages(const std::vector<std::string>& /*messages*/) {
    throw std::invalid_argument(
        "candidate transport blocker: text->tokenid tokenizer not bound/verified (donor Python tokenizer "
        "or future C++ BPE); refusing generation rather than fabricating token ids.");
}

// Encode a generation request as a GEN line. `ids` are the token ids; sampling keys are passed
// through when nonzero (temperature/top_p/top_k/seed).
std::string build_gen_line(int64_t max_new, const std::vector<int64_t>& ids,
                           double temperature, double top_p, uint32_t top_k, uint64_t seed) {
    std::string line = "GEN " + std::to_string(max_new);
    if (temperature > 0.0) {
        line += " temperature=" + std::to_string(temperature);
        line += " top_p=" + std::to_string(top_p > 0.0 ? top_p : 1.0);
        if (top_k != 0) line += " top_k=" + std::to_string(top_k);
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

// Parse the engine's DONE line into telemetry. Returns metrics; throws on malformed finish.
struct DoneMetrics {
    int64_t generated = 0;
    int64_t prompt_tokens = 0;
    double prompt_ms = 0;
    double decode_ms = 0;
    std::string finish;
    int64_t drafts_accepted = 0;
    int64_t drafts_offered = 0;
    int64_t reused = 0;
    int64_t read_n = 0;
};
DoneMetrics parse_done(const std::string& line) {
    // DONE <generated> <prompt> <prompt_ms> <decode_ms> <finish> <a> <o> <reused> [..] .. key=value
    if (line.rfind("DONE ", 0) != 0) throw std::invalid_argument("Missing DONE terminal.");
    std::vector<std::string> tok;
    std::string cur;
    for (char c : line) {
        if (c == ' ' || c == '\n') { if (!cur.empty()) { tok.push_back(cur); cur.clear(); } }
        else cur.push_back(c);
    }
    if (!cur.empty()) tok.push_back(cur);
    if (tok.size() < 9) throw std::invalid_argument("Malformed DONE terminal.");
    DoneMetrics m;
    try {
        m.generated = std::stoll(tok[1]);
        m.prompt_tokens = std::stoll(tok[2]);
        m.prompt_ms = std::stod(tok[3]);
        m.decode_ms = std::stod(tok[4]);
        m.finish = tok[5];
        m.drafts_accepted = std::stoll(tok[6]);
        m.drafts_offered = std::stoll(tok[7]);
        m.reused = std::stoll(tok[8]);
    } catch (...) { throw std::invalid_argument("Malformed DONE terminal numbers."); }
    // optional trailing key=value (chunk=..., experts_streamed=...); read_n is the 8th positional
    // after hits/lookups only when present; we keep only the stable leading fields here.
    return m;
}

// ---------------------------------------------------------------- relay core
struct CandidateSession {
    EngineProcess engine;
    bool loaded = false;
    std::string registration;
    std::string engine_exe;
    std::vector<std::string> engine_args;
};

}  // namespace

int main(int argc, char** argv) {
    const bool probe = argc == 2 && std::strcmp(argv[1], "--hardware-probe") == 0;
    if (argc != 1 && !probe) return 64;

    FILE* wire = ::fdopen(::dup(STDOUT_FILENO), "wb");
    if (wire == nullptr || ::dup2(STDERR_FILENO, STDOUT_FILENO) < 0) return 70;

    CandidateSession session;
    try {
        // Hello: explicit incompatible ABI version 2, candidate origin, and candidate-verified
        // registration family (not the legacy Safetensors/centralized registry). The managed legacy
        // provider does not claim this transport yet; integration is BLOCKED.
        Buffer hello;
        hello.number(kAbiVersion);
        hello.text(std::string("90767252f420e25223c739d604145ad15911d48b"));
        hello.number(static_cast<uint32_t>(STRATA_HAS_CUDA));
        hello.number(static_cast<uint32_t>(0));  // NCCL not claimed by the candidate relay
        hello.text(std::string("qwen35moe"));    // candidate family (registration enumeration pending real build)
        write_frame(wire, 0, hello);

        if (probe) {
            try {
                const auto observed = hardware_probe();
                write_frame(wire, 6, observed);
                std::fclose(wire);
                return STRATA_HAS_CUDA ? 0 : 69;
            } catch (const std::exception& error) {
                Buffer failure;
                failure.number<uint32_t>(3); failure.number<uint32_t>(1); failure.text(error.what());
                write_frame(wire, 6, failure);
                std::fclose(wire);
                return 69;
            }
        }

        Buffer command;
        while (read_command(command)) {
            const auto operation = command.read_number<uint32_t>();
            if (operation == 1) {
                // load: directory (checkpoint), registration, context, devices
                const auto directory = command.read_text();
                const auto registration = command.read_text();
                const auto context = command.read_number<uint32_t>();
                const auto count = command.read_number<uint32_t>();
                if (count > 64) throw std::invalid_argument("Too many native devices.");
                for (uint32_t i = 0; i < count; ++i) {
                    const auto id = command.read_number<uint32_t>();
                    (void)id;
                }
                command.end();
                // The candidate engine is spawned as `strata generate --serve` with the model file and
                // context. In this source checkpoint the exact argv is NOT validated against a real
                // build; the spawn is recorded and the request FAILS CLOSED until a build slot
                // validates the true engine CLI and binds the tokenizer provider.
                throw std::invalid_argument(
                    "candidate load blocker: engine spawn argv and tokenizer provider not validated in a "
                    "real build (source-only checkpoint); refusing to fabricate a load.");
            } else if (operation == 2 || operation == 4) {
                // generate (text) or structured-tools generate (op 4). Both require a loaded engine and
                // a bound tokenizer; both fail closed here.
                if (operation == 4) {
                    if (command.read_number<uint32_t>() != 1)
                        throw std::invalid_argument("Unsupported structured tool wire version.");
                }
                throw std::invalid_argument(
                    "candidate generate blocker: no loaded/bound engine + tokenizer (source-only); "
                    "refusing generation rather than fabricating results.");
            } else if (operation == 3) {
                command.end();
                if (session.engine.alive()) session.engine.kill();
                session.engine.close();
                Buffer stopped;
                write_frame(wire, 3, stopped);
                std::fclose(wire);
                return 0;
            } else {
                throw std::invalid_argument("Unsupported native operation.");
            }
        }
        if (session.engine.alive()) session.engine.kill();
        session.engine.close();
        std::fclose(wire);
        return 0;
    } catch (const std::exception& error) {
        try {
            Buffer failure;
            failure.number<uint32_t>(3); failure.number<uint32_t>(1); failure.text(error.what());
            write_frame(wire, 5, failure);
        } catch (...) {}
        if (session.engine.alive()) session.engine.kill();
        session.engine.close();
        std::fclose(wire);
        return 72;
    } catch (...) {
        if (session.engine.alive()) session.engine.kill();
        session.engine.close();
        std::fclose(wire);
        return 73;
    }
}
