"""Observe the local Windows host and serialize self-checks for one store."""

from contextlib import contextmanager
import ctypes
from ctypes import wintypes
import hashlib
import os

from .protocol import WorkerError


class _Memory(ctypes.Structure):
    _fields_ = [("length", wintypes.DWORD), ("load", wintypes.DWORD)] + [
        (field, ctypes.c_ulonglong) for field in (
            "total_physical", "available_physical", "total_pagefile", "available_pagefile",
            "total_virtual", "available_virtual", "available_extended_virtual",
        )
    ]


class _Process(ctypes.Structure):
    _fields_ = [("size", wintypes.DWORD), ("usage", wintypes.DWORD), ("pid", wintypes.DWORD),
                ("heap", ctypes.c_size_t), ("module", wintypes.DWORD), ("threads", wintypes.DWORD),
                ("parent_pid", wintypes.DWORD), ("priority", wintypes.LONG), ("flags", wintypes.DWORD),
                ("image", ctypes.c_wchar * 260)]


class _ProcessMemory(ctypes.Structure):
    _fields_ = [("size", wintypes.DWORD), ("faults", wintypes.DWORD)] + [
        (field, ctypes.c_size_t) for field in (
            "peak_working", "working", "peak_paged", "paged", "peak_nonpaged",
            "nonpaged", "pagefile", "peak_pagefile", "private",
        )
    ]


class WindowsHost:
    def __init__(self, protected_images: frozenset[str]):
        if os.name != "nt":
            raise WorkerError("unsupported_platform", "The supervised CPU self-check requires Windows.")
        self._protected = protected_images
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        self._kernel = kernel
        for name, result, args in (
            ("CloseHandle", wintypes.BOOL, [wintypes.HANDLE]),
            ("GlobalMemoryStatusEx", wintypes.BOOL, [ctypes.POINTER(_Memory)]),
            ("CreateToolhelp32Snapshot", wintypes.HANDLE, [wintypes.DWORD, wintypes.DWORD]),
            ("Process32FirstW", wintypes.BOOL, [wintypes.HANDLE, ctypes.POINTER(_Process)]),
            ("Process32NextW", wintypes.BOOL, [wintypes.HANDLE, ctypes.POINTER(_Process)]),
            ("OpenProcess", wintypes.HANDLE, [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]),
            ("K32GetProcessMemoryInfo", wintypes.BOOL,
             [wintypes.HANDLE, ctypes.POINTER(_ProcessMemory), wintypes.DWORD]),
            ("CreateMutexW", wintypes.HANDLE, [ctypes.c_void_p, wintypes.BOOL, wintypes.LPCWSTR]),
            ("WaitForSingleObject", wintypes.DWORD, [wintypes.HANDLE, wintypes.DWORD]),
            ("ReleaseMutex", wintypes.BOOL, [wintypes.HANDLE]),
        ):
            function = getattr(kernel, name)
            function.restype = result
            function.argtypes = args

    def free_ram(self) -> int:
        status = _Memory(length=ctypes.sizeof(_Memory))
        if not self._kernel.GlobalMemoryStatusEx(ctypes.byref(status)):
            raise ctypes.WinError(ctypes.get_last_error())
        return status.available_physical

    def protected_active(self) -> bool:
        kernel = self._kernel
        snapshot = kernel.CreateToolhelp32Snapshot(0x2, 0)
        if snapshot == ctypes.c_void_p(-1).value or not snapshot:
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            process = _Process(size=ctypes.sizeof(_Process))
            if not kernel.Process32FirstW(snapshot, ctypes.byref(process)):
                raise ctypes.WinError(ctypes.get_last_error())
            while True:
                if process.image.casefold() in self._protected:
                    return True
                if not kernel.Process32NextW(snapshot, ctypes.byref(process)):
                    error = ctypes.get_last_error()
                    if error == 18:  # ERROR_NO_MORE_FILES
                        return False
                    raise ctypes.WinError(error)
        finally:
            kernel.CloseHandle(snapshot)

    def process_ram(self, owned_pid: int) -> int:
        kernel = self._kernel
        process = kernel.OpenProcess(0x1000, False, owned_pid)  # QUERY_LIMITED_INFORMATION
        if not process:
            error = ctypes.get_last_error()
            if error == 87:  # The owned process already exited between polling and reading.
                return 0
            raise ctypes.WinError(error)
        try:
            status = _ProcessMemory(size=ctypes.sizeof(_ProcessMemory))
            if not kernel.K32GetProcessMemoryInfo(process, ctypes.byref(status), status.size):
                raise ctypes.WinError(ctypes.get_last_error())
            return max(status.working, status.private)
        finally:
            kernel.CloseHandle(process)

    @contextmanager
    def single_job(self, store_path):
        digest = hashlib.sha256(str(store_path.resolve(strict=True)).casefold().encode("utf-8")).hexdigest()
        kernel = self._kernel
        mutex = kernel.CreateMutexW(None, False, "Local\\9to1.Dulche.SelfCheck." + digest)
        if not mutex:
            raise ctypes.WinError(ctypes.get_last_error())
        acquired = False
        try:
            state = kernel.WaitForSingleObject(mutex, 0)
            if state == 258:  # WAIT_TIMEOUT
                raise WorkerError("worker_busy", "Another CPU self-check is already running for this store.")
            if state not in (0, 128):  # WAIT_OBJECT_0 or WAIT_ABANDONED
                raise ctypes.WinError(ctypes.get_last_error())
            acquired = True
            yield
        finally:
            if acquired:
                kernel.ReleaseMutex(mutex)
            kernel.CloseHandle(mutex)
