"""The Windows process ownership primitive used by the experiment supervisor."""

from __future__ import annotations

import ctypes
from ctypes import wintypes
import os
import threading

from .artifacts import CustodyError


class _BasicLimits(ctypes.Structure):
    _fields_ = [
        ("PerProcessUserTimeLimit", ctypes.c_longlong), ("PerJobUserTimeLimit", ctypes.c_longlong),
        ("LimitFlags", wintypes.DWORD), ("MinimumWorkingSetSize", ctypes.c_size_t),
        ("MaximumWorkingSetSize", ctypes.c_size_t), ("ActiveProcessLimit", wintypes.DWORD),
        ("Affinity", ctypes.c_size_t), ("PriorityClass", wintypes.DWORD), ("SchedulingClass", wintypes.DWORD),
    ]


class _IoCounters(ctypes.Structure):
    _fields_ = [(name, ctypes.c_ulonglong) for name in (
        "ReadOperationCount", "WriteOperationCount", "OtherOperationCount",
        "ReadTransferCount", "WriteTransferCount", "OtherTransferCount",
    )]


class _ExtendedLimits(ctypes.Structure):
    _fields_ = [
        ("BasicLimitInformation", _BasicLimits), ("IoInfo", _IoCounters),
        ("ProcessMemoryLimit", ctypes.c_size_t), ("JobMemoryLimit", ctypes.c_size_t),
        ("PeakProcessMemoryUsed", ctypes.c_size_t), ("PeakJobMemoryUsed", ctypes.c_size_t),
    ]


class _CpuRate(ctypes.Structure):
    _fields_ = [("flags", wintypes.DWORD), ("rate", wintypes.DWORD)]


class WindowsJob:
    def __init__(self, ram_bytes: int, *, cpu_rate_hundredths: int | None = None):
        if os.name != "nt":
            raise CustodyError("The current experiment launcher requires Windows Job Object ownership.")
        if cpu_rate_hundredths is not None and (
            type(cpu_rate_hundredths) is not int or not 1 <= cpu_rate_hundredths <= 10_000
        ):
            raise CustodyError("The Windows Job CPU rate cap is out of bounds.")
        self._lock = threading.Lock()
        self._kernel = kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.CreateJobObjectW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR]
        kernel.CreateJobObjectW.restype = wintypes.HANDLE
        kernel.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
        kernel.SetInformationJobObject.restype = wintypes.BOOL
        kernel.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
        kernel.AssignProcessToJobObject.restype = wintypes.BOOL
        kernel.TerminateJobObject.argtypes = [wintypes.HANDLE, wintypes.UINT]
        kernel.TerminateJobObject.restype = wintypes.BOOL
        kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel.CloseHandle.restype = wintypes.BOOL
        self._handle = kernel.CreateJobObjectW(None, None)
        if not self._handle:
            raise ctypes.WinError(ctypes.get_last_error())
        limits = _ExtendedLimits()
        limits.BasicLimitInformation.LimitFlags = 0x00002000 | 0x00000200  # kill on close, job memory
        limits.JobMemoryLimit = ram_bytes
        if not kernel.SetInformationJobObject(self._handle, 9, ctypes.byref(limits), ctypes.sizeof(limits)):
            error = ctypes.WinError(ctypes.get_last_error())
            self.close()
            raise error
        if cpu_rate_hundredths is not None:
            cpu = _CpuRate(flags=0x1 | 0x4, rate=cpu_rate_hundredths)  # Enabled hard cap.
            if not kernel.SetInformationJobObject(self._handle, 15, ctypes.byref(cpu), ctypes.sizeof(cpu)):
                error = ctypes.WinError(ctypes.get_last_error())
                self.close()
                raise error

    def assign(self, process) -> None:
        # The verified bootstrap waits for a byte on stdin. Use the exact
        # process handle Popen owns, never a re-opened, potentially reused PID.
        native = getattr(process, "_handle", None)
        if native is None:
            raise CustodyError("The registered worker has no native process handle.")
        with self._lock:
            if not self._handle or not self._kernel.AssignProcessToJobObject(self._handle, int(native)):
                raise ctypes.WinError(ctypes.get_last_error())

    def terminate(self) -> None:
        with self._lock:
            if self._handle and not self._kernel.TerminateJobObject(self._handle, 125):
                raise ctypes.WinError(ctypes.get_last_error())

    def close(self) -> None:
        with self._lock:
            if self._handle:
                handle, self._handle = self._handle, None
                if not self._kernel.CloseHandle(handle):
                    raise ctypes.WinError(ctypes.get_last_error())

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, trace):
        self.close()
