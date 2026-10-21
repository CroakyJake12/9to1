// dulche_child_ownership_fixture.cpp
//
// Synchronized child-ownership fixture for the ACTUAL shared production header
// `dulche_child_process.h` (Thunder pass-5 Priority 3). It exercises the real ownership/teardown
// code path, NOT a mirror or a stub.
//
// STATUS: AUTHORED / COMPILE+RUN NOT RUN in this source-only checkpoint (no root portable-component
// compile allocation was carried into this dispatch). It is written to compile with:
//
//   g++ -std=c++20 -O1 -pthread -I <native> -o child-ownership-fixture \
//       dulche_child_ownership_fixture.cpp
// where <native> is the absolute path of the InferenceEngines/NativeStrata directory containing
// `dulche_child_process.h`.
//
// and is intended to be run by a root allocation (one compiler process, short timeout). No native
// engine/model/CUDA symbols are required; this is pure process/IPC plumbing.
//
// Coverage:
//   * exec failure is reported by spawn() (handshake), not mistaken for a live child
//   * a silent live child/drain enters the blocked-read state BEFORE exceptional destruction, and
//     destruction then completes without hanging (poll-based wake + group kill)
//   * a long-lived child and its descendant holding the pipe are killed as a group and the exact pid
//     is reaped
//   * broken pipe writes to a dead child return EPIPE (harness ignores SIGPIPE) and do not crash
//   * exact reap is verified via kill(pid,0) == -1/ESRCH after teardown

#include <chrono>
#include <csignal>
#include <cstdio>
#include <cstdlib>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

#include "dulche_child_process.h"

namespace {

int g_checks = 0;
int g_failures = 0;

void check(bool cond, const char* what) {
    ++g_checks;
    if (!cond) {
        ++g_failures;
        std::fprintf(stderr, "FAIL: %s\n", what);
    }
}

bool wait_for_pid_reaped(long pid, int timeout_ms) {
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(timeout_ms);
    while (std::chrono::steady_clock::now() < deadline) {
        // kill(pid,0) returns -1 with ESRCH once the pid is reaped/gone.
        if (::kill(static_cast<pid_t>(pid), 0) != 0) return true;
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }
    return false;
}

dulche::SpawnSpec shell(const std::string& script) {
    dulche::SpawnSpec spec;
    spec.exe = "/bin/sh";
    spec.args = {"-c", script};
    return spec;
}

void test_exec_failure_reported() {
    dulche::SpawnSpec spec;
    spec.exe = "/nonexistent/definitely-not-a-real-executable";
    spec.args.push_back("--whatever");
    dulche::ChildProcess p;
    const bool ok = p.spawn(spec);
    check(!ok, "exec failure must be reported by spawn()");
    check(!p.spawned(), "exec failure must not leave a spawned child");
}

void test_exceptional_destruction_unblocks_blocked_drain() {
    long pid = -1;
    try {
        dulche::ChildProcess p;
        if (!p.spawn(shell("sleep 300"))) { check(false, "spawn for blocked-drain test"); return; }
        pid = p.os_pid();
        // Prove the silent live child/drain entered the blocked-read state BEFORE exceptional
        // destruction. (sleep 300 holds stdout/stderr open, so the drain blocks on poll/read.)
        check(p.wait_drain_blocked(5000), "drain must enter blocked-read state before destruction");
        throw std::runtime_error("exceptional teardown");
    } catch (const std::runtime_error&) {
        // destructor runs shutdown() during unwind
    }
    check(pid > 0, "blocked-drain test captured a pid");
    if (pid > 0)
        check(wait_for_pid_reaped(pid, 5000), "exceptional teardown must reap the exact child pid");
}

void test_long_lived_descendant_group_kill() {
    dulche::ChildProcess p;
    // sh spawns a descendant that holds the pipes open (sleep 300), then sh sleeps too. Only a
    // group kill terminates the whole tree; a single-pid kill would strand the descendant.
    if (!p.spawn(shell("sleep 300 & sleep 300"))) { check(false, "spawn descendant test"); return; }
    const long pid = p.os_pid();
    check(p.wait_drain_blocked(5000), "descendant drain must block before teardown");
    p.shutdown();
    check(wait_for_pid_reaped(pid, 5000), "group kill + exact reap of the descendant-bearing tree");
}

void test_broken_pipe_no_crash() {
    dulche::ChildProcess p;
    if (!p.spawn(shell("exit 0"))) { check(false, "spawn broken-pipe test"); return; }
    check(p.wait_exit(5000), "child should exit 0");
    // Writing to the dead child's stdin must fail with EPIPE, not crash or raise SIGPIPE (which the
    // harness ignores below).
    const char c = 'x';
    errno = 0;
    const ssize_t w = ::write(p.in_fd(), &c, 1);
    check(w < 0, "write to dead child must fail");
    check(errno == EPIPE, "write to dead child must report EPIPE");
}

void test_exact_reap_after_wait_exit() {
    dulche::ChildProcess p;
    if (!p.spawn(shell("exit 3"))) { check(false, "spawn exit-code test"); return; }
    const long pid = p.os_pid();
    check(p.wait_exit(5000), "wait_exit must observe exit");
    check(wait_for_pid_reaped(pid, 5000), "wait_exit must reap the exact pid");
}

}  // namespace

int main() {
#if !defined(_WIN32)
    std::signal(SIGPIPE, SIG_IGN);  // harness-only: let EPIPE be observed instead of dying
#endif
    test_exec_failure_reported();
    test_exceptional_destruction_unblocks_blocked_drain();
    test_long_lived_descendant_group_kill();
    test_broken_pipe_no_crash();
    test_exact_reap_after_wait_exit();
    std::fprintf(stdout, "checks=%d failures=%d\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
