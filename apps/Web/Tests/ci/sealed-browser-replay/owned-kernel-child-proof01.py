"""Nonreaping Linux own-child wait proof; never signals or selects another owner."""
import os
import signal

LINUX_WALL = 1073741824
WAIT_FLAGS = os.WEXITED | os.WNOHANG | os.WNOWAIT | LINUX_WALL
FLAG_NAMES = ['WEXITED', 'WNOHANG', 'WNOWAIT', 'LINUX___WALL']

def sigchld_disposition():
    current = signal.getsignal(signal.SIGCHLD)
    return 'DEFAULT' if current == signal.SIG_DFL else 'IGNORED' if current == signal.SIG_IGN else 'CUSTOM'

def kernel_own_child_receipt():
    result = {'empty': False, 'flags': FLAG_NAMES, 'flagsValue': WAIT_FLAGS,
              'flagsAccepted': False, 'sigchldDisposition': sigchld_disposition(),
              'nonreaping': True}
    if result['sigchldDisposition'] != 'DEFAULT':
        return dict(result, status='UNSAFE_SIGCHLD_DISPOSITION')
    try:
        event = os.waitid(os.P_ALL, 0, WAIT_FLAGS)
    except ChildProcessError:
        return dict(result, empty=True, flagsAccepted=True, status='ECHILD')
    except OSError as error:
        return dict(result, status='UNSUPPORTED_OR_UNAVAILABLE', errorType=type(error).__name__)
    if event is None:
        # WNOHANG returns no event for a LIVE waitable child. Not absence.
        return dict(result, flagsAccepted=True, status='OWN_WAITABLE_CHILD_NO_EXIT_EVENT')
    return dict(result, flagsAccepted=True, status='OWN_EXIT_EVENT_UNREAPED', ownChildPid=event.si_pid)
