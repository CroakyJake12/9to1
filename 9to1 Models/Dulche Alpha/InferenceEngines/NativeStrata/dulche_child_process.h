// dulche_child_process.h
//
// Portable, exception-safe child-process ownership for the candidate Strata relay worker
// (Thunder). It fixes the pass-3 fatal retirement defect: draining stderr in a raw
// `std::thread` with no joining destructor caused `std::terminate` whenever an exception unwound
// past the drain's scope while its thread was still joinable (e.g. a generate failure after the
// engine load had started). Every resource in this header is owned by an RAII object whose
// destructor performs a deterministic, bounded teardown and never throws:
//
//   * StderrDrain  - owns the child stderr read end + reader thread; stop -> close fd -> join.
//   * LineReader    - reads the child stdout fd; used by the relay; does not own the fd lifecycle
//                     beyond what ChildProcess owns (it reads the value, ChildProcess closes it).
//   * ChildProcess  - owns pid + the three pipe fds; spawn/kill/wait_exit/shutdown with exact
//                     owned-child reap and full fd cleanup. Its destructor runs shutdown().
//
// The worker includes this header and holds a ChildProcess per child (engine + tokenizer helper).
// POSIX is implemented and unit-tested (a bounded standalone compile/run); Windows uses the same
// interface with CreateProcess/TerminateProcess and `_open_osfhandle` fd ownership, but is not
// compiled in this source-only checkpoint. No CUDA/engine symbols are required to use this header;
// it is pure process/IPC plumbing.

#ifndef DULCHE_CHILD_PROCESS_H
#define DULCHE_CHILD_PROCESS_H

#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <mutex>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

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

namespace dulche {

constexpr size_t kStderrCap = 64U * 1024U;

// ------------------------------------------------------------------ raw fd read
inline int read_raw(int fd, char* buf, size_t count) {
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

// Close and clear an owned fd. Never throws.
inline void close_fd(int& fd) {
    if (fd < 0) return;
#if defined(_WIN32)
    _close(fd);
#else
    ::close(fd);
#endif
    fd = -1;
}

// ------------------------------------------------------------------ StderrDrain
class StderrDrain {
public:
    StderrDrain() = default;
    ~StderrDrain() { stop_and_join(); }
    StderrDrain(const StderrDrain&) = delete;
    StderrDrain& operator=(const StderrDrain&) = delete;

    // Takes ownership of `fd` (the child stderr read end) and starts the reader thread.
    void start(int fd) {
        _fd = fd;
        _stop.store(false, std::memory_order_relaxed);
        _thread = std::thread([this, fd] {
            char buf[4096];
            for (;;) {
                const int n = read_raw(fd, buf, sizeof buf);
                if (n <= 0) break;
                if (_stop.load(std::memory_order_relaxed)) break;
                std::lock_guard<std::mutex> lk(_mu);
                if (_captured.size() + static_cast<size_t>(n) <= kStderrCap)
                    _captured.append(buf, static_cast<size_t>(n));
            }
        });
    }

    // Idempotent, bounded, never throws: request stop, close the owned fd to unblock a pending
    // read, then join the thread.
    void stop_and_join() {
        _stop.store(true, std::memory_order_relaxed);
        close_fd(_fd);
        if (_thread.joinable()) _thread.join();
    }

    std::string captured() const {
        std::lock_guard<std::mutex> lk(_mu);
        return _captured;
    }

private:
    std::thread _thread;
    std::atomic<bool> _stop{false};
    int _fd = -1;               // owned reading end
    mutable std::mutex _mu;
    std::string _captured;
};

// ------------------------------------------------------------------ LineReader (stdout)
class LineReader {
public:
    explicit LineReader(int fd) : _fd(fd) {}
    // Returns true with a full line (without '\n'/'r'), false on EOF when no partial data is
    // buffered. Throws std::invalid_argument on an unbounded line. Never blocks after the
    // owning ChildProcess closes the fd (read returns EOF -> false).
    bool next(std::string& out) {
        out.clear();
        for (;;) {
            const auto nl = _buf.find('\n');
            if (nl != std::string::npos) {
                out.assign(_buf, 0, nl);
                _buf.erase(0, nl + 1);
                if (!out.empty() && out.back() == '\r') out.pop_back();
                if (out.size() > kLineLimit) throw std::invalid_argument("Child output line exceeds limit.");
                return true;
            }
            if (_buf.size() > kLineLimit) throw std::invalid_argument("Child output line exceeds limit.");
            char chunk[4096];
            const int n = read_raw(_fd, chunk, sizeof chunk);
            if (n <= 0) {
                if (_buf.empty()) return false;
                out.swap(_buf);
                if (!out.empty() && out.back() == '\r') out.pop_back();
                _buf.clear();
                return true;
            }
            _buf.append(chunk, static_cast<size_t>(n));
        }
    }

private:
    static constexpr size_t kLineLimit = 16U * 1024U * 1024U;
    std::string _buf;
    int _fd;
};

// ------------------------------------------------------------------ ChildProcess
struct SpawnSpec {
    std::string exe;
    std::vector<std::string> args;
};

class ChildProcess {
public:
    ChildProcess() = default;
    ~ChildProcess() { shutdown(); }
    ChildProcess(const ChildProcess&) = delete;
    ChildProcess& operator=(const ChildProcess&) = delete;

    // Returns false on spawn failure (no process started, pipe error). On success the child is
    // owned: pid alive, three pipe fds open, stderr drain started.
    bool spawn(const SpawnSpec& spec) {
        if (_spawned) return false;
#if defined(_WIN32)
        vector _spawn_windows(spec);
#else
        if (!_spawn_posix(spec)) return false;
#endif
        return true;
    }

    bool spawned() const { return _spawned; }
    bool reaped() const { return _reaped; }
    bool alive() const {
        if (!_spawned || _reaped) return false;
#if defined(_WIN32)
        return _hProcess != nullptr && WaitForSingleObject(_hProcess, 0) == WAIT_TIMEOUT;
#else
        return ::kill(_pid, 0) == 0;
#endif
    }

    void kill() {
        if (!_spawned || _reaped) return;
#if defined(_WIN32)
        if (_hProcess) TerminateProcess(_hProcess, 1);
#else
        ::kill(_pid, SIGKILL);
#endif
    }

    // Bounded wait for exit (polls). Returns true once reaped/closed. Never blocks forever.
    bool wait_exit(int timeout_ms) {
        if (!_spawned || _reaped) return _reaped;
        auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(timeout_ms);
        for (;;) {
            if (_try_reap()) return true;
            if (std::chrono::steady_clock::now() >= deadline) return false;
            std::this_thread::sleep_for(std::chrono::milliseconds(5));
        }
    }

    // Deterministic full teardown: stop/close stderr, signal the child, close pipes, reap.
    // Idempotent and never throws. Safe to call from a destructor during exception unwinding.
    void shutdown() {
        if (!_spawned) return;
        _stderr.stop_and_join();       // closes stderr read end, joins reader thread
        close_fd(_out_fd);
        close_fd(_in_fd);
        if (!_reaped) kill();
        _try_reap();
        _spawned = false;
    }

    int in_fd() const { return _in_fd; }
    int out_fd() const { return _out_fd; }
    std::string stderr_text() const { return _stderr.captured(); }

    // Test-only observation (POSIX): the owned child pid, so a standalone test can verify the child
    // is killed and reaped after destruction. Not part of the worker-facing API.
#ifndef _WIN32
    long os_pid() const { return _pid; }
#endif

private:
    bool _try_reap() {
        if (_reaped || !_spawned) return _reaped;
#if defined(_WIN32)
        if (_hProcess && WaitForSingleObject(_hProcess, 0) == WAIT_OBJECT_0) {
            CloseHandle(_hProcess); _hProcess = nullptr; _reaped = true;
        }
        return _reaped;
#else
        int status = 0;
        const pid_t r = ::waitpid(_pid, &status, WNOHANG);
        if (r == _pid) { _reaped = true; return true; }
        if (r < 0 && errno != EINTR) { _reaped = true; return true; }  // ECHILD: already reaped
        return false;
#endif
    }

    bool _spawn_posix(const SpawnSpec& spec) {
        int in_pipe[2], out_pipe[2], err_pipe[2];
        if (::pipe(in_pipe) != 0 || ::pipe(out_pipe) != 0 || ::pipe(err_pipe) != 0) return false;
        const pid_t pid = ::fork();
        if (pid < 0) {
            ::close(in_pipe[0]); ::close(in_pipe[1]);
            ::close(out_pipe[0]); ::close(out_pipe[1]);
            ::close(err_pipe[0]); ::close(err_pipe[1]);
            return false;
        }
        if (pid == 0) {
            ::dup2(in_pipe[0], STDIN_FILENO);
            ::dup2(out_pipe[1], STDOUT_FILENO);
            ::dup2(err_pipe[1], STDERR_FILENO);
            ::close(in_pipe[0]); ::close(in_pipe[1]);
            ::close(out_pipe[0]); ::close(out_pipe[1]);
            ::close(err_pipe[0]); ::close(err_pipe[1]);
            std::vector<char*> argv;
            argv.reserve(spec.args.size() + 2);
            argv.push_back(const_cast<char*>(spec.exe.c_str()));
            for (const auto& arg : spec.args) argv.push_back(const_cast<char*>(arg.c_str()));
            argv.push_back(nullptr);
            ::execv(spec.exe.c_str(), argv.data());
            _exit(127);
        }
        ::close(in_pipe[0]);
        ::close(out_pipe[1]);
        ::close(err_pipe[1]);
        _pid = pid;
        _in_fd = in_pipe[1];
        _out_fd = out_pipe[0];
        _err_fd = err_pipe[0];
        _spawned = true;
        _reaped = false;
        _stderr.start(_err_fd);
        return true;
    }

#if defined(_WIN32)
    bool _spawn_windows(const SpawnSpec& spec) {
        std::string cmdline = WideQuote(spec.exe);
        for (const auto& arg : spec.args) cmdline += " " + WideQuote(arg);
        SECURITY_ATTRIBUTES sa{};
        sa.nLength = sizeof(sa); sa.bInheritHandle = TRUE;
        HANDLE in_read = nullptr, in_write = nullptr, out_read = nullptr, out_write = nullptr;
        HANDLE err_read = nullptr, err_write = nullptr;
        auto cleanup_pipes = [&]() {
            if (in_read) CloseHandle(in_read); if (in_write) CloseHandle(in_write);
            if (out_read) CloseHandle(out_read); if (out_write) CloseHandle(out_write);
            if (err_read) CloseHandle(err_read); if (err_write) CloseHandle(err_write);
        };
        if (!CreatePipe(&in_read, &in_write, &sa, 0)
            || !CreatePipe(&out_read, &out_write, &sa, 0)
            || !CreatePipe(&err_read, &err_write, &sa, 0)) { cleanup_pipes(); return false; }
        SetHandleInformation(in_write, HANDLE_FLAG_INHERIT, 0);
        SetHandleInformation(out_read, HANDLE_FLAG_INHERIT, 0);
        SetHandleInformation(err_read, HANDLE_FLAG_INHERIT, 0);
        STARTUPINFOW si{};
        si.cb = sizeof(si);
        si.dwFlags = STARTF_USESTDHANDLES;
        si.hStdInput = in_read; si.hStdOutput = out_write; si.hStdError = err_write;
        std::wstring wcmdline = ToWide(cmdline);
        PROCESS_INFORMATION pi{};
        const BOOL ok = CreateProcessW(nullptr, wcmdline.data(), nullptr, nullptr, TRUE,
                                       CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi);
        CloseHandle(in_read); CloseHandle(out_write); CloseHandle(err_write);
        if (!ok) { cleanup_pipes(); return false; }
        CloseHandle(pi.hThread);
        _hProcess = pi.hProcess;
        _in_fd = _open_osfhandle(reinterpret_cast<intptr_t>(in_write), _O_WRONLY | _O_BINARY);
        _out_fd = _open_osfhandle(reinterpret_cast<intptr_t>(out_read), _O_RDONLY | _O_BINARY);
        _err_fd = _open_osfhandle(reinterpret_cast<intptr_t>(err_read), _O_RDONLY | _O_BINARY);
        _spawned = true; _reaped = false;
        _stderr.start(_err_fd);
        return true;
    }
    static std::wstring ToWide(const std::string& s) {
        std::wstring w; w.resize(::MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, nullptr, 0) - 1);
        ::MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, w.data(), static_cast<int>(w.size()));
        return w;
    }
    static std::string WideQuote(const std::string& s) {
        // CommandLineToArgvW-compatible quoting (spaces, quotes, trailing backslashes).
        std::string q = "\"";
        size_t bs = 0;
        for (char c : s) {
            if (c == '\\') { ++bs; continue; }
            if (c == '"') { q.append(bs * 2 + 1, '\\'); q.push_back('"'); bs = 0; continue; }
            q.append(bs, '\\'); bs = 0; q.push_back(c);
        }
        q.append(bs * 2, '\\');
        q.push_back('"');
        return q;
    }
#endif

    int _in_fd = -1, _out_fd = -1, _err_fd = -1;
    StderrDrain _stderr;
#if defined(_WIN32)
    HANDLE _hProcess = nullptr;
#else
    pid_t _pid = -1;
#endif
    bool _spawned = false;
    bool _reaped = false;
};

}  // namespace dulche

#endif  // DULCHE_CHILD_PROCESS_H
