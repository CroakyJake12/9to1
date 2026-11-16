#!/usr/bin/env python3
"""One-slot Windows SDK runner with owned-job-only cleanup and source fencing.

This is a Windows counterpart to guarded_lowmem_build.py; it deliberately does
not alter or emulate the Linux runner's receipts. Outputs are written outside
the checkout, beside its parent, with a protected owner/SYSTEM ACL.
"""
from __future__ import annotations

import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import platform
from pathlib import PurePosixPath
import re
import subprocess
import sys
import time
import uuid

if os.name != "nt":  # pragma: no cover - covered by platform guard
    raise SystemExit("REFUSED: guarded_windows_build.py requires Windows")

from ctypes import wintypes

kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
advapi32 = ctypes.WinDLL("advapi32", use_last_error=True)
psapi = ctypes.WinDLL("psapi", use_last_error=True)

DEFAULT_MEMORY_FLOOR_MIB = 4096
DEFAULT_JOB_CAP_MIB = 8192
DEFAULT_DISK_RESERVE_MIB = 4096
DEFAULT_TIMEOUT_SECONDS = 3600
WAIT_OBJECT_0 = 0
WAIT_TIMEOUT = 0x102
WAIT_ABANDONED = 0x80
CREATE_SUSPENDED = 0x00000004
CREATE_NEW_PROCESS_GROUP = 0x00000200
CREATE_UNICODE_ENVIRONMENT = 0x00000400
STARTF_USESTDHANDLES = 0x00000100
JOB_OBJECT_EXTENDED_LIMIT_INFORMATION = 9
JOB_OBJECT_BASIC_ACCOUNTING_INFORMATION = 1
JOB_OBJECT_BASIC_PROCESS_ID_LIST = 3
JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000
JOB_OBJECT_LIMIT_JOB_MEMORY = 0x00000200
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
PROCESS_VM_READ = 0x0010
SYNCHRONIZE = 0x00100000
TH32CS_SNAPPROCESS = 0x00000002
FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400
ERROR_NO_MORE_FILES = 18
SDK_PROCESS_NAMES = {"dotnet.exe", "msbuild.exe", "vbcscompiler.exe", "csc.exe", "vbc.exe"}
PINNED_DONORS = {
    "framework/CUI/vendor/Avalonia/external/XamlX": "009d4815470cf4bf71d1adbb633a5d81dcb2bb52",
    "framework/CUI/vendor/Avalonia/external/Avalonia.DBus": "864a05282841bf04006890f04d11d60d1a046aa9",
}
MAX_FINGERPRINT_BYTES = 2 * 1024 ** 3


class Refusal(RuntimeError):
    pass


class MEMORYSTATUSEX(ctypes.Structure):
    _fields_ = [("dwLength", wintypes.DWORD), ("dwMemoryLoad", wintypes.DWORD),
                ("ullTotalPhys", ctypes.c_ulonglong), ("ullAvailPhys", ctypes.c_ulonglong),
                ("ullTotalPageFile", ctypes.c_ulonglong), ("ullAvailPageFile", ctypes.c_ulonglong),
                ("ullTotalVirtual", ctypes.c_ulonglong), ("ullAvailVirtual", ctypes.c_ulonglong),
                ("ullAvailExtendedVirtual", ctypes.c_ulonglong)]


class SECURITY_ATTRIBUTES(ctypes.Structure):
    _fields_ = [("nLength", wintypes.DWORD), ("lpSecurityDescriptor", wintypes.LPVOID),
                ("bInheritHandle", wintypes.BOOL)]


class STARTUPINFOW(ctypes.Structure):
    _fields_ = [("cb", wintypes.DWORD), ("lpReserved", wintypes.LPWSTR),
                ("lpDesktop", wintypes.LPWSTR), ("lpTitle", wintypes.LPWSTR),
                ("dwX", wintypes.DWORD), ("dwY", wintypes.DWORD),
                ("dwXSize", wintypes.DWORD), ("dwYSize", wintypes.DWORD),
                ("dwXCountChars", wintypes.DWORD), ("dwYCountChars", wintypes.DWORD),
                ("dwFillAttribute", wintypes.DWORD), ("dwFlags", wintypes.DWORD),
                ("wShowWindow", wintypes.WORD), ("cbReserved2", wintypes.WORD),
                ("lpReserved2", ctypes.POINTER(ctypes.c_ubyte)), ("hStdInput", wintypes.HANDLE),
                ("hStdOutput", wintypes.HANDLE), ("hStdError", wintypes.HANDLE)]


class PROCESS_INFORMATION(ctypes.Structure):
    _fields_ = [("hProcess", wintypes.HANDLE), ("hThread", wintypes.HANDLE),
                ("dwProcessId", wintypes.DWORD), ("dwThreadId", wintypes.DWORD)]


class IO_COUNTERS(ctypes.Structure):
    _fields_ = [("ReadOperationCount", ctypes.c_ulonglong), ("WriteOperationCount", ctypes.c_ulonglong),
                ("OtherOperationCount", ctypes.c_ulonglong), ("ReadTransferCount", ctypes.c_ulonglong),
                ("WriteTransferCount", ctypes.c_ulonglong), ("OtherTransferCount", ctypes.c_ulonglong)]


class JOBOBJECT_BASIC_LIMIT_INFORMATION(ctypes.Structure):
    _fields_ = [("PerProcessUserTimeLimit", ctypes.c_longlong), ("PerJobUserTimeLimit", ctypes.c_longlong),
                ("LimitFlags", wintypes.DWORD), ("MinimumWorkingSetSize", ctypes.c_size_t),
                ("MaximumWorkingSetSize", ctypes.c_size_t), ("ActiveProcessLimit", wintypes.DWORD),
                ("Affinity", ctypes.c_size_t), ("PriorityClass", wintypes.DWORD),
                ("SchedulingClass", wintypes.DWORD)]


class JOBOBJECT_EXTENDED_LIMIT_INFORMATION(ctypes.Structure):
    _fields_ = [("BasicLimitInformation", JOBOBJECT_BASIC_LIMIT_INFORMATION), ("IoInfo", IO_COUNTERS),
                ("ProcessMemoryLimit", ctypes.c_size_t), ("JobMemoryLimit", ctypes.c_size_t),
                ("PeakProcessMemoryUsed", ctypes.c_size_t), ("PeakJobMemoryUsed", ctypes.c_size_t)]


class JOBOBJECT_BASIC_ACCOUNTING_INFORMATION(ctypes.Structure):
    _fields_ = [("TotalUserTime", ctypes.c_longlong), ("TotalKernelTime", ctypes.c_longlong),
                ("ThisPeriodTotalUserTime", ctypes.c_longlong), ("ThisPeriodTotalKernelTime", ctypes.c_longlong),
                ("TotalPageFaultCount", wintypes.DWORD), ("TotalProcesses", wintypes.DWORD),
                ("ActiveProcesses", wintypes.DWORD), ("TotalTerminatedProcesses", wintypes.DWORD)]


class PROCESS_MEMORY_COUNTERS_EX(ctypes.Structure):
    _fields_ = [("cb", wintypes.DWORD), ("PageFaultCount", wintypes.DWORD),
                ("PeakWorkingSetSize", ctypes.c_size_t), ("WorkingSetSize", ctypes.c_size_t),
                ("QuotaPeakPagedPoolUsage", ctypes.c_size_t), ("QuotaPagedPoolUsage", ctypes.c_size_t),
                ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t), ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
                ("PagefileUsage", ctypes.c_size_t), ("PeakPagefileUsage", ctypes.c_size_t),
                ("PrivateUsage", ctypes.c_size_t)]


class PROCESSENTRY32W(ctypes.Structure):
    _fields_ = [("dwSize", wintypes.DWORD), ("cntUsage", wintypes.DWORD),
                ("th32ProcessID", wintypes.DWORD), ("th32DefaultHeapID", ctypes.c_size_t),
                ("th32ModuleID", wintypes.DWORD), ("cntThreads", wintypes.DWORD),
                ("th32ParentProcessID", wintypes.DWORD), ("pcPriClassBase", ctypes.c_long),
                ("dwFlags", wintypes.DWORD), ("szExeFile", wintypes.WCHAR * 260)]


kernel32.GlobalMemoryStatusEx.argtypes = [ctypes.POINTER(MEMORYSTATUSEX)]
kernel32.GlobalMemoryStatusEx.restype = wintypes.BOOL
kernel32.GetDiskFreeSpaceExW.argtypes = [wintypes.LPCWSTR, ctypes.POINTER(ctypes.c_ulonglong),
                                         ctypes.POINTER(ctypes.c_ulonglong), ctypes.POINTER(ctypes.c_ulonglong)]
kernel32.GetDiskFreeSpaceExW.restype = wintypes.BOOL
kernel32.CreateDirectoryW.argtypes = [wintypes.LPCWSTR, ctypes.POINTER(SECURITY_ATTRIBUTES)]
kernel32.CreateDirectoryW.restype = wintypes.BOOL
kernel32.GetFileAttributesW.argtypes = [wintypes.LPCWSTR]
kernel32.GetFileAttributesW.restype = wintypes.DWORD
kernel32.LocalFree.argtypes = [wintypes.HLOCAL]
kernel32.LocalFree.restype = wintypes.HLOCAL
kernel32.CreateMutexW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.LPCWSTR]
kernel32.CreateMutexW.restype = wintypes.HANDLE
kernel32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
kernel32.WaitForSingleObject.restype = wintypes.DWORD
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
kernel32.CloseHandle.restype = wintypes.BOOL
kernel32.ReleaseMutex.argtypes = [wintypes.HANDLE]
kernel32.ReleaseMutex.restype = wintypes.BOOL
kernel32.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
kernel32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
kernel32.Process32FirstW.argtypes = [wintypes.HANDLE, ctypes.POINTER(PROCESSENTRY32W)]
kernel32.Process32FirstW.restype = wintypes.BOOL
kernel32.Process32NextW.argtypes = [wintypes.HANDLE, ctypes.POINTER(PROCESSENTRY32W)]
kernel32.Process32NextW.restype = wintypes.BOOL
kernel32.CreateJobObjectW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR]
kernel32.CreateJobObjectW.restype = wintypes.HANDLE
kernel32.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
kernel32.SetInformationJobObject.restype = wintypes.BOOL
kernel32.QueryInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD,
                                                ctypes.POINTER(wintypes.DWORD)]
kernel32.QueryInformationJobObject.restype = wintypes.BOOL
kernel32.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p,
                                 wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
kernel32.CreateFileW.restype = wintypes.HANDLE
kernel32.SetHandleInformation.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.DWORD]
kernel32.SetHandleInformation.restype = wintypes.BOOL
kernel32.GetStdHandle.argtypes = [wintypes.DWORD]
kernel32.GetStdHandle.restype = wintypes.HANDLE
kernel32.CreateProcessW.argtypes = [wintypes.LPCWSTR, wintypes.LPWSTR, ctypes.c_void_p, ctypes.c_void_p,
                                    wintypes.BOOL, wintypes.DWORD, ctypes.c_void_p, wintypes.LPCWSTR,
                                    ctypes.POINTER(STARTUPINFOW), ctypes.POINTER(PROCESS_INFORMATION)]
kernel32.CreateProcessW.restype = wintypes.BOOL
kernel32.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
kernel32.AssignProcessToJobObject.restype = wintypes.BOOL
kernel32.ResumeThread.argtypes = [wintypes.HANDLE]
kernel32.ResumeThread.restype = wintypes.DWORD
kernel32.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
kernel32.GetExitCodeProcess.restype = wintypes.BOOL
kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
kernel32.OpenProcess.restype = wintypes.HANDLE
kernel32.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR,
                                                ctypes.POINTER(wintypes.DWORD)]
kernel32.QueryFullProcessImageNameW.restype = wintypes.BOOL
kernel32.IsProcessInJob.argtypes = [wintypes.HANDLE, wintypes.HANDLE, ctypes.POINTER(wintypes.BOOL)]
kernel32.IsProcessInJob.restype = wintypes.BOOL
psapi.GetProcessMemoryInfo.argtypes = [wintypes.HANDLE, ctypes.POINTER(PROCESS_MEMORY_COUNTERS_EX), wintypes.DWORD]
psapi.GetProcessMemoryInfo.restype = wintypes.BOOL
kernel32.TerminateProcess.argtypes = [wintypes.HANDLE, wintypes.UINT]
kernel32.TerminateProcess.restype = wintypes.BOOL


def _wincheck(ok: int | bool, operation: str) -> None:
    if not ok:
        raise OSError(ctypes.get_last_error(), operation)


def _memory_available() -> int:
    state = MEMORYSTATUSEX()
    state.dwLength = ctypes.sizeof(state)
    _wincheck(kernel32.GlobalMemoryStatusEx(ctypes.byref(state)), "GlobalMemoryStatusEx")
    return int(state.ullAvailPhys)


def _disk_available(path: Path) -> int:
    free = ctypes.c_ulonglong()
    total = ctypes.c_ulonglong()
    total_free = ctypes.c_ulonglong()
    _wincheck(kernel32.GetDiskFreeSpaceExW(str(path), ctypes.byref(free), ctypes.byref(total),
                                            ctypes.byref(total_free)), "GetDiskFreeSpaceExW")
    return int(free.value)


def _git(repo: Path, *args: str, input_bytes: bytes | None = None) -> bytes:
    return subprocess.run(["git", "-C", str(repo), *args], capture_output=True,
                          input=input_bytes, timeout=60, check=True).stdout


def _hash_file(path: Path) -> tuple[int, str]:
    digest = hashlib.sha256()
    size = 0
    with path.open("rb") as stream:
        while block := stream.read(1024 * 1024):
            size += len(block)
            if size > MAX_FINGERPRINT_BYTES:
                raise Refusal("one source file exceeds the bounded fingerprint size")
            digest.update(block)
    return size, digest.hexdigest()


def _safe_source_path(root: Path, relative: str) -> Path:
    posix = PurePosixPath(relative)
    if posix.is_absolute() or ".." in posix.parts:
        raise Refusal("source inventory contains an unsafe relative path")
    candidate = root.joinpath(*posix.parts)
    try:
        resolved = candidate.resolve(strict=True)
        resolved_root = root.resolve(strict=True)
    except OSError:
        raise Refusal("source inventory path is missing") from None
    if not resolved.is_relative_to(resolved_root):
        raise Refusal("source path resolves outside its pinned tree")
    return candidate


def _fingerprint_untracked(root: Path, digest) -> int:
    paths = subprocess.run(["git", "-C", str(root), "ls-files", "--others", "--exclude-standard", "-z"],
                           capture_output=True, timeout=60, check=True).stdout
    total = 0
    for raw in paths.split(b"\0"):
        if not raw:
            continue
        relative = os.fsdecode(raw)
        candidate = _safe_source_path(root, relative)
        if candidate.is_symlink() or not candidate.is_file():
            raise Refusal("untracked source contains a symlink or non-file entry")
        size, content_hash = _hash_file(candidate)
        total += size
        if total > MAX_FINGERPRINT_BYTES:
            raise Refusal("untracked source exceeds the bounded fingerprint size")
        digest.update(relative.encode("utf-8", "surrogatepass") + b"\0" + content_hash.encode() + b"\0")
    return total


def _verify_pinned_donors(repo: Path, digest) -> None:
    manifest_path = repo / "eng/development/donor-inputs.json"
    if manifest_path.is_symlink():
        raise Refusal("pinned donor manifest cannot be a symlink")
    try:
        donors = json.loads(manifest_path.read_text(encoding="utf-8"))["donors"]
    except (OSError, KeyError, json.JSONDecodeError, TypeError):
        raise Refusal("pinned donor manifest is unavailable or invalid") from None
    for relative_root, expected_revision in PINNED_DONORS.items():
        donor = _safe_source_path(repo, relative_root)
        # The manifest uses the donor's logical name rather than its path name.
        donor_name = "xamlx" if relative_root.endswith("/XamlX") else "avalonia-dbus"
        entry = donors.get(donor_name)
        if not isinstance(entry, dict) or entry.get("path") != relative_root or entry.get("revision") != expected_revision:
            raise Refusal("pinned donor manifest differs from the guarded XamlX/DBus revisions")
        revision = _git(donor, "rev-parse", "HEAD").decode("ascii").strip()
        if revision != expected_revision:
            raise Refusal("pinned XamlX/DBus source is absent or at the wrong revision")
        nested_status = _git(donor, "status", "--porcelain=v1", "--untracked-files=all")
        if nested_status:
            raise Refusal("pinned XamlX/DBus source tree is dirty")
        expected_files = entry.get("files")
        if not isinstance(expected_files, dict) or not expected_files:
            raise Refusal("pinned donor file manifest is empty")
        donor_prefix = PurePosixPath(relative_root)
        normalized_files = {}
        for full_path, spec in expected_files.items():
            full_posix = PurePosixPath(full_path)
            if not full_posix.is_relative_to(donor_prefix):
                raise Refusal("pinned donor manifest contains a path outside its source tree")
            normalized_files[full_posix.relative_to(donor_prefix).as_posix()] = spec
        tree_entries = {}
        for record in _git(donor, "ls-tree", "-rz", "HEAD").split(b"\0"):
            if not record:
                continue
            metadata, raw_path = record.split(b"\t", 1)
            mode, kind, oid = metadata.decode("ascii").split()
            if kind == "blob":
                tree_entries[os.fsdecode(raw_path)] = (mode, oid)
        if set(tree_entries) != set(normalized_files):
            raise Refusal("pinned donor tracked-file inventory differs from its source manifest")
        digest.update(relative_root.encode() + b"\0" + expected_revision.encode() + b"\0")
        entries = sorted(normalized_files.items())
        object_ids = [tree_entries[relative][1] for relative, _ in entries]
        blob_stream = _git(donor, "cat-file", "--batch", input_bytes=("\n".join(object_ids) + "\n").encode("ascii"))
        offset = 0
        total_bytes = 0
        for (relative, spec), oid in zip(entries, object_ids):
            candidate = _safe_source_path(donor, relative)
            if not isinstance(spec, dict) or spec.get("kind") != "file" or candidate.is_symlink() or not candidate.is_file():
                raise Refusal("pinned donor contains a missing or unsupported source entry")
            header_end = blob_stream.find(b"\n", offset)
            if header_end < 0:
                raise Refusal("pinned donor object stream is truncated")
            header = blob_stream[offset:header_end].decode("ascii").split()
            if len(header) != 3 or header[0] != oid or header[1] != "blob":
                raise Refusal("pinned donor object stream identity mismatch")
            size = int(header[2])
            start, end = header_end + 1, header_end + 1 + size
            blob = blob_stream[start:end]
            if len(blob) != size or blob_stream[end:end + 1] != b"\n":
                raise Refusal("pinned donor object stream is truncated")
            offset = end + 1
            total_bytes += size
            if total_bytes > MAX_FINGERPRINT_BYTES:
                raise Refusal("pinned donor sources exceed the bounded fingerprint size")
            size, content_hash = len(blob), hashlib.sha256(blob).hexdigest()
            if size != spec.get("bytes") or content_hash != spec.get("sha256"):
                raise Refusal("pinned donor source bytes do not match their recorded manifest")
            digest.update(relative.encode("utf-8", "surrogatepass") + b"\0" + content_hash.encode() + b"\0")


def _repo_snapshot(repo: Path, verify_pins: bool = True) -> tuple[str, str, bool]:
    top = _git(repo, "rev-parse", "--show-toplevel").decode("utf-8", "replace").strip()
    if os.path.normcase(os.path.realpath(top)) != os.path.normcase(os.path.realpath(repo)):
        raise Refusal("repository path is not the Git root")
    head = _git(repo, "rev-parse", "HEAD").decode("ascii").strip()
    if not re.fullmatch(r"[0-9a-f]{40}", head):
        raise Refusal("repository HEAD is not an exact commit")
    status = _git(repo, "status", "--porcelain=v1", "--untracked-files=all", "--ignore-submodules=none")
    dirty = bool(status)
    digest = hashlib.sha256()
    digest.update(head.encode() + b"\0" + status + b"\0")
    digest.update(_git(repo, "diff", "--binary", "--no-ext-diff", "HEAD", "--"))
    _fingerprint_untracked(repo, digest)
    if verify_pins:
        _verify_pinned_donors(repo, digest)
    identity = digest.hexdigest()
    return head, identity, dirty


def _new_private_dir(parent: Path) -> Path:
    if not parent.is_absolute() or not parent.exists():
        raise Refusal("private output parent is unavailable")
    if not parent.is_dir() or kernel32.GetFileAttributesW(str(parent)) & FILE_ATTRIBUTE_REPARSE_POINT:
        raise Refusal("private output parent is not a plain directory")
    advapi32.ConvertStringSecurityDescriptorToSecurityDescriptorW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD,
                                                                                ctypes.POINTER(wintypes.LPVOID),
                                                                                ctypes.POINTER(wintypes.DWORD)]
    advapi32.ConvertStringSecurityDescriptorToSecurityDescriptorW.restype = wintypes.BOOL
    sd = wintypes.LPVOID()
    _wincheck(advapi32.ConvertStringSecurityDescriptorToSecurityDescriptorW(
        "D:P(A;OICI;FA;;;OW)(A;OICI;FA;;;SY)", 1, ctypes.byref(sd), None),
        "create protected output ACL")
    try:
        attrs = SECURITY_ATTRIBUTES(ctypes.sizeof(SECURITY_ATTRIBUTES), sd, False)
        child = parent / (".9to1-guarded-" + uuid.uuid4().hex)
        _wincheck(kernel32.CreateDirectoryW(str(child), ctypes.byref(attrs)), "CreateDirectoryW private output")
        return child
    finally:
        kernel32.LocalFree(sd)


def _acquire_mutex(repo: Path):
    name = "Local\\9to1-SDK-" + hashlib.sha256(os.path.normcase(str(repo)).encode()).hexdigest()[:32]
    handle = kernel32.CreateMutexW(None, False, name)
    _wincheck(handle, "CreateMutexW")
    wait = kernel32.WaitForSingleObject(handle, 0)
    if wait == WAIT_TIMEOUT:
        kernel32.CloseHandle(handle)
        raise Refusal("another guarded SDK task holds this repository lease")
    if wait not in (WAIT_OBJECT_0, WAIT_ABANDONED):
        error = ctypes.get_last_error()
        kernel32.CloseHandle(handle)
        raise OSError(error, "WaitForSingleObject SDK mutex")
    return handle


def _foreign_sdk_processes(job) -> list[str]:
    snapshot = kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    if snapshot == wintypes.HANDLE(-1).value:
        raise OSError(ctypes.get_last_error(), "CreateToolhelp32Snapshot")
    found = []
    try:
        item = PROCESSENTRY32W()
        item.dwSize = ctypes.sizeof(item)
        ctypes.set_last_error(0)
        ok = kernel32.Process32FirstW(snapshot, ctypes.byref(item))
        if not ok and ctypes.get_last_error() != ERROR_NO_MORE_FILES:
            raise OSError(ctypes.get_last_error(), "Process32FirstW")
        while ok:
            name = item.szExeFile.casefold()
            if name in SDK_PROCESS_NAMES:
                pid = int(item.th32ProcessID)
                handle = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, False, pid)
                if not handle:
                    # Toolhelp can return an entry whose process exited before
                    # OpenProcess. That race is not evidence of a foreign build.
                    error = ctypes.get_last_error()
                    if error in (87, 1168):  # INVALID_PARAMETER / NOT_FOUND
                        ctypes.set_last_error(0)
                    else:
                        raise Refusal(f"SDK process PID {pid} could not be safely opened (Win32 {error})")
                else:
                    try:
                        state = kernel32.WaitForSingleObject(handle, 0)
                        if state == WAIT_TIMEOUT:
                            if job is not None:
                                in_job = wintypes.BOOL()
                                if not kernel32.IsProcessInJob(handle, job, ctypes.byref(in_job)):
                                    error = ctypes.get_last_error()
                                    if kernel32.WaitForSingleObject(handle, 0) != WAIT_OBJECT_0:
                                        raise Refusal(f"SDK process PID {pid} job check failed (Win32 {error})")
                                elif in_job.value:
                                    # Job membership on this live process handle
                                    # is stronger than the snapshot name and needs
                                    # no image lookup for our own build children.
                                    pass
                                else:
                                    actual_name, error = _query_process_image_name(handle)
                                    if actual_name is None:
                                        if kernel32.WaitForSingleObject(handle, 0) != WAIT_OBJECT_0:
                                            raise Refusal(f"SDK process PID {pid} image check failed (Win32 {error})")
                                    elif actual_name == name:
                                        final_state = kernel32.WaitForSingleObject(handle, 0)
                                        if final_state == WAIT_TIMEOUT:
                                            found.append(name)
                                        elif final_state != WAIT_OBJECT_0:
                                            raise OSError(ctypes.get_last_error(), "WaitForSingleObject foreign SDK process")
                            else:
                                actual_name, error = _query_process_image_name(handle)
                                if actual_name is None:
                                    if kernel32.WaitForSingleObject(handle, 0) != WAIT_OBJECT_0:
                                        raise Refusal(f"SDK process PID {pid} image check failed (Win32 {error})")
                                elif actual_name == name and kernel32.WaitForSingleObject(handle, 0) == WAIT_TIMEOUT:
                                    found.append(name)
                        elif state != WAIT_OBJECT_0:
                            raise OSError(ctypes.get_last_error(), f"WaitForSingleObject SDK PID {pid}")
                    finally:
                        kernel32.CloseHandle(handle)
            ctypes.set_last_error(0)
            ok = kernel32.Process32NextW(snapshot, ctypes.byref(item))
        if not ok and ctypes.get_last_error() != ERROR_NO_MORE_FILES:
            raise OSError(ctypes.get_last_error(), "Process32NextW")
    finally:
        kernel32.CloseHandle(snapshot)
    return sorted(set(found))


def _query_process_image_name(handle) -> tuple[str | None, int]:
    image = ctypes.create_unicode_buffer(32768)
    image_size = wintypes.DWORD(len(image))
    ctypes.set_last_error(0)
    if not kernel32.QueryFullProcessImageNameW(handle, 0, image, ctypes.byref(image_size)):
        return None, ctypes.get_last_error()
    return Path(image.value).name.casefold(), 0


def _job_pids(job) -> set[int]:
    class PID_LIST(ctypes.Structure):
        _fields_ = [("NumberOfAssignedProcesses", wintypes.DWORD), ("NumberOfProcessIdsInList", wintypes.DWORD),
                    ("ProcessIdList", ctypes.c_size_t * 512)]
    data = PID_LIST()
    returned = wintypes.DWORD()
    _wincheck(kernel32.QueryInformationJobObject(job, JOB_OBJECT_BASIC_PROCESS_ID_LIST,
        ctypes.byref(data), ctypes.sizeof(data), ctypes.byref(returned)), "QueryInformationJobObject PID list")
    if data.NumberOfAssignedProcesses > 512:
        raise Refusal("owned process tree exceeded the bounded PID inspection list")
    return {int(data.ProcessIdList[i]) for i in range(min(data.NumberOfProcessIdsInList, 512))}


def _job_active(job) -> int:
    info = JOBOBJECT_BASIC_ACCOUNTING_INFORMATION()
    returned = wintypes.DWORD()
    _wincheck(kernel32.QueryInformationJobObject(job, JOB_OBJECT_BASIC_ACCOUNTING_INFORMATION,
        ctypes.byref(info), ctypes.sizeof(info), ctypes.byref(returned)), "QueryInformationJobObject accounting")
    return int(info.ActiveProcesses)


def _job_peak_commit(job) -> int:
    info = JOBOBJECT_EXTENDED_LIMIT_INFORMATION()
    returned = wintypes.DWORD()
    _wincheck(kernel32.QueryInformationJobObject(job, JOB_OBJECT_EXTENDED_LIMIT_INFORMATION,
        ctypes.byref(info), ctypes.sizeof(info), ctypes.byref(returned)), "QueryInformationJobObject limits")
    return int(info.PeakJobMemoryUsed)


def _owned_rss(job) -> int:
    total = 0
    for pid in _job_pids(job):
        handle = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, False, pid)
        if not handle:
            continue
        try:
            counters = PROCESS_MEMORY_COUNTERS_EX()
            counters.cb = ctypes.sizeof(counters)
            if psapi.GetProcessMemoryInfo(handle, ctypes.byref(counters), counters.cb):
                total += int(counters.WorkingSetSize)
        finally:
            kernel32.CloseHandle(handle)
    return total


def _dotnet_operation(args: list[str]) -> str | None:
    if not args:
        return None
    candidate = args[0].casefold()
    if candidate in {"build", "test", "publish", "run"}:
        return candidate
    return None


def _prepare_command(command: list[str]) -> tuple[list[str], str | None]:
    is_dotnet = Path(command[0]).name.casefold() == "dotnet.exe"
    operation = _dotnet_operation(command[1:]) if is_dotnet else None
    if is_dotnet and operation is None and command[1:] not in (["--info"], ["--version"], ["--list-sdks"],
                                                                  ["--list-runtimes"]):
        if command[1:2] == ["exec"]:
            operation = "exec"
        else:
            raise Refusal("unsupported dotnet operation; use build/test/publish, or build first then use dotnet exec")
    if operation == "run":
        raise Refusal("use guarded dotnet build followed by guarded dotnet exec; dotnet run cannot accept the required -m:1 MSBuild switch")
    if operation in {"build", "test", "publish"}:
        command = command + ["-m:1", "-p:UseSharedCompilation=false", "-nodeReuse:false"]
    return command, operation


def _environment_block() -> ctypes.Array:
    secret_name = re.compile(r"(?:TOKEN|PASSWORD|SECRET|API_KEY|AUTH|CREDENTIAL|PRIVATE_KEY|ACCESS_KEY)", re.I)
    env = {key: value for key, value in os.environ.items()
           if not secret_name.search(key) and key.casefold() not in {"http_proxy", "https_proxy"}}
    env.update({"DOTNET_gcServer": "0", "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
                "DOTNET_NOLOGO": "1", "MSBUILDDISABLENODEREUSE": "1",
                "DOTNET_CLI_USE_MSBUILD_SERVER": "0", "DOTNET_PARALLEL_COMPILE": "0",
                "UseSharedCompilation": "false", "BuildInParallel": "false"})
    packed = "\0".join(f"{key}={value}" for key, value in sorted(env.items(), key=lambda pair: pair[0].casefold())) + "\0\0"
    return ctypes.create_unicode_buffer(packed)


def run_guarded(*, repo: str | Path, expected_head: str, cwd: str | Path,
                executable: str | Path, allowed_executables: list[str | Path],
                command: list[str], timeout_seconds: int = DEFAULT_TIMEOUT_SECONDS,
                memory_floor_mib: int = DEFAULT_MEMORY_FLOOR_MIB,
                job_cap_mib: int = DEFAULT_JOB_CAP_MIB,
                disk_reserve_mib: int = DEFAULT_DISK_RESERVE_MIB,
                copy_budget_mib: int = 0, test_mode: bool = False,
                poll_seconds: float = 0.25) -> dict:
    started = time.monotonic()
    result = {"runner": "guarded_windows_build.py", "host_os": platform.platform(),
              "owner_host_windows_override": True, "custody": "current-task-windows-guard",
              "status": "BLOCKED", "source_sha": None, "source_identity": None,
              "exit_code": None, "duration_seconds": None, "peak_rss_bytes": 0,
              "peak_job_commit_bytes": 0, "log_path": None}
    job = process = thread = mutex = log_handle = None
    output_dir = None
    try:
        if copy_budget_mib < 0:
            raise Refusal("copy budget must be nonnegative")
        repo = Path(repo).resolve(strict=True)
        cwd = Path(cwd).resolve(strict=True)
        if not re.fullmatch(r"[0-9a-f]{40}", expected_head):
            raise Refusal("expected_head must be exact lowercase 40-hex")
        if not cwd.is_relative_to(repo):
            raise Refusal("working directory must be inside the exact repository")
        result["expected_source_sha"] = expected_head
        result["source_sha"] = _git(repo, "rev-parse", "HEAD").decode("ascii").strip()
        if timeout_seconds <= 0 or timeout_seconds > 14400 or poll_seconds <= 0:
            raise Refusal("timeout must be between 1 second and 4 hours; poll interval must be positive")
        if not test_mode:
            if memory_floor_mib < 2048 or job_cap_mib != DEFAULT_JOB_CAP_MIB or disk_reserve_mib < 2048:
                raise Refusal("production memory floor must be >=2 GiB, job cap is 8 GiB, disk reserve >=2 GiB")
        elif job_cap_mib <= 0 or memory_floor_mib <= 0 or disk_reserve_mib < 0:
            raise Refusal("invalid test-only resource bounds")
        exe = Path(executable).resolve(strict=True)
        allowed = {os.path.normcase(str(Path(p).resolve(strict=True))) for p in allowed_executables}
        if os.path.normcase(str(exe)) not in allowed:
            raise Refusal("executable is not on the exact allowed-path list")
        if not command or os.path.normcase(str(Path(command[0]).resolve(strict=True))) != os.path.normcase(str(exe)):
            raise Refusal("argv[0] must be the exact allowed executable path")
        output_dir = _new_private_dir(repo.parent)
        result["log_path"] = str(output_dir / "console.log")
        result["output_dir"] = str(output_dir)
        head, identity, source_dirty = _repo_snapshot(repo, verify_pins=not test_mode)
        result["source_sha"], result["source_identity"] = head, identity
        result["source_dirty_before"] = source_dirty
        result["memory_floor_mib"] = memory_floor_mib
        result["job_cap_mib"] = job_cap_mib
        result["disk_reserve_mib"] = disk_reserve_mib
        result["copy_budget_mib"] = copy_budget_mib
        if head != expected_head:
            raise Refusal("repository HEAD differs from expected source SHA")
        if _memory_available() < memory_floor_mib * 1024 * 1024:
            raise Refusal("host available-memory floor is not met")
        required_disk = (disk_reserve_mib + copy_budget_mib) * 1024 * 1024
        if _disk_available(cwd) < required_disk:
            raise Refusal("disk reserve plus declared copy budget is not available")
        mutex = _acquire_mutex(repo)
        if _foreign_sdk_processes(None):
            raise Refusal("known foreign dotnet/MSBuild process exists; it was left untouched")
        command, operation = _prepare_command(list(command))
        result["dotnet_operation"] = operation
        result["argument_count"] = len(command) - 1
        result["command_sha256"] = hashlib.sha256("\0".join(command).encode()).hexdigest()

        job = kernel32.CreateJobObjectW(None, None)
        _wincheck(job, "CreateJobObjectW")
        limits = JOBOBJECT_EXTENDED_LIMIT_INFORMATION()
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | JOB_OBJECT_LIMIT_JOB_MEMORY
        limits.JobMemoryLimit = job_cap_mib * 1024 * 1024
        _wincheck(kernel32.SetInformationJobObject(job, JOB_OBJECT_EXTENDED_LIMIT_INFORMATION,
                    ctypes.byref(limits), ctypes.sizeof(limits)), "SetInformationJobObject")
        log_handle = kernel32.CreateFileW(str(output_dir / "console.log"), 0x40000000, 0x00000007,
                                         None, 2, 0x00000080, None)
        if log_handle == wintypes.HANDLE(-1).value:
            raise OSError(ctypes.get_last_error(), "CreateFileW console log")
        _wincheck(kernel32.SetHandleInformation(log_handle, 1, 1), "SetHandleInformation console log")
        startup = STARTUPINFOW()
        startup.cb = ctypes.sizeof(startup)
        startup.dwFlags = STARTF_USESTDHANDLES
        startup.hStdInput = kernel32.GetStdHandle(0xFFFFFFF6)
        startup.hStdOutput = log_handle
        startup.hStdError = log_handle
        info = PROCESS_INFORMATION()
        command_line = ctypes.create_unicode_buffer(subprocess.list2cmdline(command))
        env_block = _environment_block()
        _wincheck(kernel32.CreateProcessW(str(exe), command_line, None, None, True,
                    CREATE_SUSPENDED | CREATE_NEW_PROCESS_GROUP | CREATE_UNICODE_ENVIRONMENT,
                    ctypes.cast(env_block, ctypes.c_void_p), str(cwd), ctypes.byref(startup), ctypes.byref(info)),
                    "CreateProcessW")
        process, thread = info.hProcess, info.hThread
        if not kernel32.AssignProcessToJobObject(job, process):
            error = ctypes.get_last_error()
            kernel32.TerminateProcess(process, 125)  # only the suspended child created by this runner
            kernel32.WaitForSingleObject(process, 5000)
            raise OSError(error, "AssignProcessToJobObject")
        _wincheck(kernel32.ResumeThread(thread) != 0xFFFFFFFF, "ResumeThread")
        result["pid"] = int(info.dwProcessId)
        result["status"] = "RUNNING"

        reason = None
        process_exit = None
        peak_rss = 0
        while True:
            peak_rss = max(peak_rss, _owned_rss(job))
            result["peak_job_commit_bytes"] = max(result["peak_job_commit_bytes"], _job_peak_commit(job))
            if _foreign_sdk_processes(job):
                reason = "foreign dotnet/MSBuild process appeared; left untouched"
                break
            if _memory_available() < memory_floor_mib * 1024 * 1024:
                reason = "host available-memory floor crossed"
                break
            wait = kernel32.WaitForSingleObject(process, 0)
            if wait == WAIT_OBJECT_0:
                code = wintypes.DWORD()
                _wincheck(kernel32.GetExitCodeProcess(process, ctypes.byref(code)), "GetExitCodeProcess")
                process_exit = int(code.value)
                if _job_active(job) == 0:
                    break
            elif wait != WAIT_TIMEOUT:
                raise OSError(ctypes.get_last_error(), "WaitForSingleObject build process")
            if time.monotonic() - started >= timeout_seconds:
                reason = "bounded timeout exceeded"
                break
            time.sleep(poll_seconds)

        result["peak_rss_bytes"] = peak_rss
        result["exit_code"] = process_exit
        if reason:
            result["status"] = "BLOCKED"
            result["reason"] = reason
            kernel32.CloseHandle(job)  # KILL_ON_JOB_CLOSE: only this runner's suspended/assigned process tree.
            job = None
            deadline = time.monotonic() + 5
            while time.monotonic() < deadline and kernel32.WaitForSingleObject(process, 0) == WAIT_TIMEOUT:
                time.sleep(0.05)
            if kernel32.WaitForSingleObject(process, 0) == WAIT_OBJECT_0:
                code = wintypes.DWORD()
                _wincheck(kernel32.GetExitCodeProcess(process, ctypes.byref(code)), "GetExitCodeProcess after job close")
                process_exit = int(code.value)
            else:
                result["cleanup_status"] = "owned process did not signal exit within 5 seconds"
        elif result["peak_job_commit_bytes"] >= job_cap_mib * 1024 * 1024:
            result["status"] = "BLOCKED"
            result["reason"] = "owned job reached its enforced memory cap"
        elif process_exit == 0:
            result["status"] = "OK"
        else:
            result["status"] = "FAILED"

        after_head, after_identity, after_dirty = _repo_snapshot(repo, verify_pins=not test_mode)
        if after_head != head or after_identity != identity:
            result["status"] = "INVALIDATED: repository source changed during execution"
        result["source_sha_after"] = after_head
        result["source_identity_after"] = after_identity
        result["source_dirty_after"] = after_dirty
        result["exit_code"] = process_exit
    except Refusal as exc:
        result["status"] = "BLOCKED"
        result["reason"] = str(exc)
    except Exception as exc:
        result["status"] = "FAILED"
        result["reason"] = f"{type(exc).__name__}: {exc}"
    finally:
        if job:
            kernel32.CloseHandle(job)
        if thread:
            kernel32.CloseHandle(thread)
        if process:
            kernel32.CloseHandle(process)
        if mutex:
            kernel32.ReleaseMutex(mutex)
            kernel32.CloseHandle(mutex)
        if log_handle:
            kernel32.CloseHandle(log_handle)
        result["duration_seconds"] = round(time.monotonic() - started, 3)
        if output_dir:
            try:
                (output_dir / "outcome.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
            except OSError:
                result["status"] = "FAILED"
                result["reason"] = "could not persist the structured outcome in the private output directory"
    return result


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--repo", required=True)
    parser.add_argument("--expected-head", required=True)
    parser.add_argument("--cwd", required=True)
    parser.add_argument("--executable", required=True)
    parser.add_argument("--allowed-executable", action="append", required=True)
    parser.add_argument("--timeout", type=int, default=DEFAULT_TIMEOUT_SECONDS)
    parser.add_argument("--memory-floor-mib", type=int, default=DEFAULT_MEMORY_FLOOR_MIB)
    parser.add_argument("--disk-reserve-mib", type=int, default=DEFAULT_DISK_RESERVE_MIB)
    parser.add_argument("--copy-budget-mib", type=int, default=0)
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args(argv)
    command = args.command[1:] if args.command and args.command[0] == "--" else args.command
    result = run_guarded(repo=args.repo, expected_head=args.expected_head, cwd=args.cwd,
        executable=args.executable, allowed_executables=args.allowed_executable, command=command,
        timeout_seconds=args.timeout, memory_floor_mib=args.memory_floor_mib,
        disk_reserve_mib=args.disk_reserve_mib, copy_budget_mib=args.copy_budget_mib)
    print(json.dumps(result, indent=2))
    return 0 if result["status"] == "OK" else 1


if __name__ == "__main__":
    raise SystemExit(main())
