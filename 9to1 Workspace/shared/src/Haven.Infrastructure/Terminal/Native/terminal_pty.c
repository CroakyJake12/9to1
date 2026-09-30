/* Prepare all paths and descriptors before fork; no child returns into the CLR. */
#define _GNU_SOURCE
#include <unistd.h>
#include <stdlib.h>
#include <fcntl.h>
#include <errno.h>
#include <signal.h>
#include <pthread.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <sys/ioctl.h>
#if defined(__APPLE__)
#include <util.h>
#endif

__attribute__((visibility("default")))
int haven_terminal_dup(int descriptor)
{
    return fcntl(descriptor, F_DUPFD_CLOEXEC, 3);
}

/* Only call while this owner still owns the unreaped child PID. */
__attribute__((visibility("default")))
int haven_terminal_abort(int pid)
{
    int status;
    int result;
    kill(-pid, SIGKILL);
    kill(pid, SIGKILL); /* The child may not have established its session yet. */
    do { result = waitpid(pid, &status, 0); } while (result < 0 && errno == EINTR);
    return result;
}

__attribute__((visibility("default")))
int haven_terminal_spawn(const char *file, char *const argv[], const char *directory, char *const environment[],
                         unsigned short rows, unsigned short columns, int *master)
{
    struct winsize window = { rows, columns, 0, 0 };
    *master = -1;
#if defined(__linux__)
    char slave_name[128];
    int primary = posix_openpt(O_RDWR | O_NOCTTY | O_CLOEXEC);
    int slave = -1;
    int saved_error;
    if (primary < 0) return -1;
    /* Keep all PTY descriptors away from standard I/O even in a headless host. */
    if (primary < 3) {
        int replacement = haven_terminal_dup(primary);
        saved_error = errno;
        close(primary);
        if (replacement < 0) { errno = saved_error; return -1; }
        primary = replacement;
    }
    if (grantpt(primary) != 0 || unlockpt(primary) != 0) goto failed;
    saved_error = ptsname_r(primary, slave_name, sizeof(slave_name));
    if (saved_error != 0) { errno = saved_error; goto failed; }
    slave = open(slave_name, O_RDWR | O_NOCTTY | O_CLOEXEC);
    if (slave < 0) goto failed;
    if (slave < 3) {
        int replacement = haven_terminal_dup(slave);
        saved_error = errno;
        close(slave);
        slave = replacement;
        if (slave < 0) { errno = saved_error; goto failed; }
    }
    if (ioctl(slave, TIOCSWINSZ, &window) != 0) goto failed;
    /* Block delivery across fork: a pre-exec SIGTERM must never invoke an
       inherited CLR handler in the child. Restore default dispositions first. */
    sigset_t blocked, previous;
    sigfillset(&blocked);
    saved_error = pthread_sigmask(SIG_SETMASK, &blocked, &previous);
    if (saved_error != 0) { errno = saved_error; goto failed; }
    pid_t pid = fork();
    if (pid != 0) {
        saved_error = errno;
        pthread_sigmask(SIG_SETMASK, &previous, 0);
        errno = saved_error;
        if (pid < 0) goto failed;
    }
    if (pid == 0) {
        struct sigaction disposition = { 0 };
        disposition.sa_handler = SIG_DFL;
        sigemptyset(&disposition.sa_mask);
        for (int signal_number = 1; signal_number < NSIG; signal_number++) {
            if (signal_number != SIGKILL && signal_number != SIGSTOP)
                sigaction(signal_number, &disposition, 0);
        }
        if (sigprocmask(SIG_SETMASK, &previous, 0) != 0) _exit(126);
        /* These are native syscall wrappers; no allocation or runtime callbacks. */
        if (setsid() < 0 || ioctl(slave, TIOCSCTTY, 0) < 0 ||
            dup2(slave, STDIN_FILENO) < 0 || dup2(slave, STDOUT_FILENO) < 0 ||
            dup2(slave, STDERR_FILENO) < 0) _exit(126);
        close(slave);
        close(primary);
        if (chdir(directory) != 0) _exit(126);
        execve(file, argv, environment);
        _exit(127);
    }
    close(slave);
    *master = primary;
    return (int)pid;
failed:
    saved_error = errno;
    if (slave >= 0) close(slave);
    close(primary);
    errno = saved_error;
    return -1;
#else
    /* macOS forkpty remains a separate, unverified platform path. Its master
       close-on-exec flag is not atomic with allocation; do not claim Linux's
       cross-app descriptor isolation guarantee for this fallback. */
    sigset_t blocked, previous;
    sigfillset(&blocked);
    int mask_error = pthread_sigmask(SIG_SETMASK, &blocked, &previous);
    if (mask_error != 0) { errno = mask_error; return -1; }
    pid_t pid = forkpty(master, 0, 0, &window);
    if (pid != 0) {
        int saved_error = errno;
        pthread_sigmask(SIG_SETMASK, &previous, 0);
        errno = saved_error;
    }
    if (pid == 0) {
        struct sigaction disposition = { 0 };
        disposition.sa_handler = SIG_DFL;
        sigemptyset(&disposition.sa_mask);
        for (int signal_number = 1; signal_number < NSIG; signal_number++) {
            if (signal_number != SIGKILL && signal_number != SIGSTOP)
                sigaction(signal_number, &disposition, 0);
        }
        if (sigprocmask(SIG_SETMASK, &previous, 0) != 0) _exit(126);
        if (chdir(directory) != 0) _exit(126);
        execve(file, argv, environment);
        _exit(127);
    }
    if (pid > 0 && fcntl(*master, F_SETFD, FD_CLOEXEC) < 0) {
        int saved_error = errno;
        close(*master);
        *master = -1;
        haven_terminal_abort(pid);
        errno = saved_error;
        return -1;
    }
    return (int)pid;
#endif
}
