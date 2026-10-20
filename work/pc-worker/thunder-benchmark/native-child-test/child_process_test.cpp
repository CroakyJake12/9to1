// child_process_test.cpp
//
// Bounded standalone test for the portable, exception-safe child-process RAII header
// `dulche_child_process.h` (POSIX). It is the independent "portable child/error-cleanup"
// component evidence requested by the pass-4 coordinator allocation. It does NOT touch the engine,
// CUDA, .NET, models, or hardware. One compiler process, a worktree-local output directory, a
// short timeout.
//
// Coverage:
//   1. RAII retirement: after spawning a long-running child, an exception unwinds and the
//      ChildProcess destructor kills + reaps the child WITHOUT std::terminate.
//   2. stderr drain captures the child's stderr.
//   3. stdin->stdout echo through the owned pipes (`cat`).
//   4. wait_exit reaps an already-exited child, bounded.
//   5. shutdown() is idempotent and leaves no live child.

#include "dulche_child_process.h"
#include <cassert>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <stdexcept>
#include <string>
#include <thread>

using namespace dulche;

static int g_unwind_caught = 0;

// ---- Test 1: fatal-retirement fix ----------------------------------------
// Spawn a long-running child, then throw from inside a try AFTER spawn. In pass-3 this destroyed a
// joinable std::thread during unwind -> std::terminate. Now the ChildProcess (and its
// StderrDrain) must clean up deterministically as the exception unwinds through the child's scope,
// and the child must be killed + reaped (its pid released).
static void test_raii_retirement() {
    g_unwind_caught = 0;
    long pid = -1;
    bool caught = false;
    try {
        ChildProcess child;
        SpawnSpec spec;
        spec.exe = "/bin/sh";
        spec.args = {"-c", "sleep 30"};
        assert(child.spawn(spec));
        pid = child.os_pid();
        assert(pid > 0);
        assert(child.alive());
        throw std::runtime_error("boom");
    } catch (const std::exception&) {
        caught = true;   // reached only if ~ChildProcess (during unwind) did not std::terminate
    }
    assert(caught);
    // The child is destroyed during unwinding; verify it was killed and reaped, its pid released.
    const bool gone = (::kill(static_cast<pid_t>(pid), 0) != 0);
    assert(gone);
    std::printf("raii_retirement: ok\n");
}

// ---- Test 2: stderr drain ------------------------------------------------
static void test_stderr_drain() {
    {
        ChildProcess child;
        SpawnSpec spec;
        spec.exe = "/bin/sh";
        spec.args = {"-c", "echo line1; echo line2 >&2"};
        assert(child.spawn(spec));
        assert(child.wait_exit(3000));
        // After wait_exit the child is reaped; drain may still have buffered content.
        child.shutdown();
        const std::string& text = child.stderr_text();
        assert(text.find("line2") != std::string::npos);
        std::printf("stderr_drain: ok (got %zu bytes)\n", text.size());
    }
    std::printf("stderr_drain: destructor path ok\n");
}

// ---- Test 3: stdin->stdout echo ------------------------------------------
static void test_echo() {
    ChildProcess child;
    SpawnSpec spec;
    spec.exe = "/bin/cat";
    assert(child.spawn(spec));
    const std::string msg = "hello-from-worker\n";
    const char* p = msg.data();
    size_t left = msg.size();
    while (left > 0) {
        const ssize_t n = ::write(child.in_fd(), p, left);
        if (n < 0 && errno == EINTR) continue;
        assert(n > 0);
        p += n; left -= static_cast<size_t>(n);
    }
    ::close(child.in_fd());  // EOF to cat -> it exits
    LineReader reader(child.out_fd());
    std::string line;
    bool got = false;
    while (reader.next(line)) {
        if (line == "hello-from-worker") got = true;
    }
    assert(got);
    assert(child.wait_exit(3000));
    child.shutdown();
    std::printf("echo: ok\n");
}

// ---- Test 4: wait_exit reaps already-exited child -------------------------
static void test_wait_exit() {
    ChildProcess child;
    SpawnSpec spec;
    spec.exe = "/bin/sh";
    spec.args = {"-c", "exit 7"};
    assert(child.spawn(spec));
    assert(child.wait_exit(3000));
    assert(child.reaped());
    assert(!child.alive());
    child.shutdown();  // idempotent
    child.shutdown();
    std::printf("wait_exit: ok\n");
}

int main() {
    signal(SIGPIPE, SIG_IGN);
    test_raii_retirement();
    test_stderr_drain();
    test_echo();
    test_wait_exit();
    std::printf("ALL CHILD PROCESS TESTS PASSED\n");
    return 0;
}
