// NativeStrataDescendantAfterReapFixture.cpp
//
// Regression for Thunder pass-10 Priority 1: the POSIX owned process-group leak after the direct
// child has already exited and been reaped. A descendant (e.g. a backgrounded `sleep`) remains
// alive in the owned process group; `ChildProcess::shutdown()` must retire the still-owned group
// EVEN THOUGH the direct child (group leader) is gone. The header must also never kill a reused
// (recycled) pgid belonging to an unrelated group, nor arbitrary descendants outside the owned
// group.
//
// This mirrors the independent reviewer's production-header challenge (descendant-challenge), but
// is self-contained and asserts the fixed contract.
//
// STATUS: AUTHORED / COMPILE+RUN NOT RUN (no root portable-component compile allocation this
// dispatch). It compiles against the real shared header with:
//
//   g++ -std=c++20 -O1 -pthread -I <native> -o descendant-after-reap-fixture \
//       NativeStrataDescendantAfterReapFixture.cpp
//
// and is intended to be run by a root allocation (one compiler process, short timeout). No native
// engine/model/CUDA symbols are required; this is pure process/IPC plumbing.

#include "dulche_child_process.h"

#include <sys/prctl.h>
#include <sys/wait.h>
#include <poll.h>
#include <unistd.h>
#include <signal.h>
#include <cerrno>
#include <cstdio>
#include <cstdlib>
#include <fstream>
#include <string>
#include <chrono>
#include <thread>

static int g_checks = 0;
static int g_failures = 0;

static void check(bool cond, const char* what) {
    ++g_checks;
    if (!cond) {
        ++g_failures;
        std::fprintf(stderr, "FAIL: %s\n", what);
    }
}

// Live = the pid exists and is not a zombie (Z) or dead (X).
static bool live(pid_t pid) {
    std::ifstream f("/proc/" + std::to_string(pid) + "/stat");
    std::string s;
    std::getline(f, s);
    if (s.empty()) return false;
    const auto x = s.rfind(')');
    return x != std::string::npos && x + 2 < s.size() && s[x + 2] != 'Z' && s[x + 2] != 'X';
}

int main() {
    // Make this test its own reaper so it can reliably reap its verified descendant after the
    // direct child has been reaped by ChildProcess.
    if (prctl(PR_SET_CHILD_SUBREAPER, 1) != 0) {
        std::perror("subreaper");
        return 77;
    }

    dulche::ChildProcess p;
    dulche::SpawnSpec s;
    s.exe = "/bin/sh";
    s.args = {"-c", "sleep 30 & printf '%s\n' \"$!\"; exit 0"};

    if (!p.spawn(s)) { std::puts("FAIL spawn"); return 1; }
    const pid_t root = static_cast<pid_t>(p.os_pid());

    // Read the descendant pid announced by sh on stdout.
    pollfd fd{p.out_fd(), POLLIN, 0};
    if (poll(&fd, 1, 5000) <= 0) { std::puts("FAIL pid announcement"); return 1; }
    char b[80]{};
    const auto n = read(p.out_fd(), b, sizeof(b) - 1);
    if (n <= 0) { std::puts("FAIL pid read"); return 1; }
    const pid_t child = static_cast<pid_t>(std::strtol(b, nullptr, 10));

    check(child > 1, "descendant pid parsed");
    check(getpgid(child) == root, "descendant belongs to the owned process group");
    check(getsid(child) == getsid(0), "descendant shares our session");

    // Reap the DIRECT child (group leader). It has already exited after announcing the pid.
    const bool direct_exited = p.wait_exit(5000);
    check(direct_exited, "direct child reaped by wait_exit");
    check(!live(root), "direct child no longer live after wait_exit");

    const bool descendant_before = live(child);
    check(descendant_before, "descendant alive before shutdown");

    // The regression: shutdown() must retire the STILL-OWNED group after direct reaping.
    p.shutdown();

    auto end = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (live(child) && std::chrono::steady_clock::now() < end)
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    check(!live(child), "descendant retired by shutdown after direct reap");

    // Independent cleanup of only the verified descendant in this test's owned session. This never
    // turns a failure into a pass.
    if (live(child) && getpgid(child) == root && getsid(child) == getsid(0))
        kill(child, SIGKILL);
    int status = 0;
    const auto waited = waitpid(child, &status, 0);
    check(waited == child, "descendant independent reaped");

    std::fprintf(stdout, "checks=%d failures=%d\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
