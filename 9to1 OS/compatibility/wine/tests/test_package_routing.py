from __future__ import annotations

import io
import json
import os
import struct
import tempfile
import unittest
import uuid
import zipfile
from dataclasses import replace
from pathlib import Path
from unittest.mock import patch

from compatibility.wine.haven_compat.broker import CompatibilityBroker, CompatibilityError
from compatibility.wine.haven_compat.daemon import RequestError, dispatch_request
from compatibility.wine.haven_compat.manifest import AppManifest
from compatibility.wine.haven_compat.packages import PackageError, inspect_package
from compatibility.wine.haven_compat.registry import AppRegistry
from compatibility.wine.haven_compat.routing import (AdministratorPolicySource, BackendPreferences,
    CompatibilityBackend, CompatibilityPolicy, RoutingError, route_package)


def pe_image(machine=0x8664, dll=False):
    data = bytearray(1024)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 60, 128)
    data[128:132] = b"PE\0\0"
    struct.pack_into("<HHIIIHH", data, 132, machine, 1, 0, 0, 0, 240, 0x2002 if dll else 2)
    struct.pack_into("<H", data, 152, 0x10b if machine == 0x14c else 0x20b)
    return data


def binary_manifest(utf8=True):
    strings = ["manifest", "package", "com.example.calendar"]
    encoded = []
    for text in strings:
        raw = text.encode("utf-8" if utf8 else "utf-16-le")
        encoded.append(bytes([len(text), len(raw)]) + raw + b"\0" if utf8 else struct.pack("<H", len(text)) + raw + b"\0\0")
    offsets = []
    total = 0
    for raw in encoded:
        offsets.append(total)
        total += len(raw)
    start = 28 + 4 * len(strings)
    pool = struct.pack("<HHIIIIII", 1, 28, start + total, len(strings), 0, 0x100 if utf8 else 0, start, 0)
    pool += b"".join(struct.pack("<I", value) for value in offsets) + b"".join(encoded)
    element = struct.pack("<HHIII", 0x102, 16, 56, 1, 0xffffffff)
    element += struct.pack("<IIHHHHHH", 0xffffffff, 0, 20, 20, 1, 0, 0, 0)
    element += struct.pack("<IIIHBBI", 0xffffffff, 1, 2, 8, 0, 3, 2)
    return struct.pack("<HHI", 3, 8, 8 + len(pool) + len(element)) + pool + element


class PackageRoutingTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)
        self.backends = (
            CompatibilityBackend("wine", "Wine", ("windows-exe",), ("x86_64", "x86"), True, 100),
            CompatibilityBackend("winboat", "Windows VM", ("windows-exe",), ("x86_64",), True, 90),
            CompatibilityBackend("android", "Android", ("android-apk",), ("arm64-v8a",), True, 100))

    def tearDown(self):
        self.directory.cleanup()

    def write(self, name, data):
        path = self.root / name
        path.write_bytes(data)
        return path

    def apk(self, manifest, name="test.apk", abi="arm64-v8a"):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            archive.writestr("AndroidManifest.xml", manifest)
            if abi:
                archive.writestr(f"lib/{abi}/libapp.so", b"test")
        return self.write(name, stream.getvalue())

    def test_executable_format_comes_from_signature_and_survives_rename(self):
        first = self.write("application.txt", pe_image())
        package = inspect_package(first)
        self.assertEqual("windows-exe", package.format)
        self.assertEqual(("x86_64",), package.architectures)
        self.assertEqual("package-content-sha256", package.identity_source)
        second = first.with_name("renamed.apk")
        first.rename(second)
        self.assertEqual(package.identity, inspect_package(second).identity)

    def test_extension_does_not_make_arbitrary_content_executable(self):
        with self.assertRaises(PackageError):
            inspect_package(self.write("application.exe", b"ordinary text"))

    def test_dll_and_malformed_pe_headers_are_rejected(self):
        with self.assertRaises(PackageError):
            inspect_package(self.write("library.exe", pe_image(dll=True)))
        raw = pe_image()
        struct.pack_into("<I", raw, 60, 0xfffffffe)
        with self.assertRaises(PackageError):
            inspect_package(self.write("truncated.exe", raw))

    def test_compound_office_document_is_not_msi(self):
        raw = bytearray(1024)
        raw[:8] = bytes.fromhex("d0cf11e0a1b11ae1")
        struct.pack_into("<HHHH", raw, 26, 3, 0xfffe, 9, 6)
        struct.pack_into("<I", raw, 48, 0)
        raw[512 + 66] = 5
        with self.assertRaises(PackageError):
            inspect_package(self.write("document.msi", raw))
        raw[512 + 80:512 + 96] = uuid.UUID("000c1084-0000-0000-c000-000000000046").bytes_le
        package = inspect_package(self.write("installer.dat", raw))
        self.assertEqual("windows-msi", package.format)
        self.assertEqual((), package.architectures)
        backend = replace(self.backends[0], formats=("windows-msi",))
        self.assertEqual("no-eligible-backend", route_package(package, (backend,), CompatibilityPolicy())["status"])

    def test_apk_package_identity_is_stable_across_versions_and_names(self):
        one = inspect_package(self.apk(b'<manifest package="com.example.calendar"/>'))
        two = inspect_package(self.apk(b'<manifest package="com.example.calendar" version="2"/>', "update.zip"))
        self.assertEqual("android:com.example.calendar", one.identity)
        self.assertEqual(one.identity, two.identity)
        self.assertNotEqual(one.sha256, two.sha256)
        self.assertEqual(("arm64-v8a",), one.architectures)

    def test_real_binary_android_manifest_string_encodings(self):
        for utf8 in (True, False):
            with self.subTest(utf8=utf8):
                self.assertEqual("android:com.example.calendar", inspect_package(self.apk(binary_manifest(utf8))).identity)

    def test_invalid_android_manifest_and_plain_zip_are_rejected(self):
        for raw in (b"not xml", b'<manifest package="../bad"/>', b'<resources package="com.test.app"/>',
                    b'<!DOCTYPE manifest><manifest package="com.test.app"/>', binary_manifest()[:-1]):
            with self.subTest(raw=raw[:16]):
                with self.assertRaises(PackageError):
                    inspect_package(self.apk(raw))

    def test_unverified_packages_never_select_execution_backend(self):
        package = inspect_package(self.write("sample.exe", pe_image()))
        route = route_package(package, self.backends, CompatibilityPolicy())
        self.assertEqual("requires-package-trust", route["status"])
        self.assertIsNone(route["selectedBackend"])
        self.assertEqual(["wine", "winboat"], route["eligibleBackends"])

    def test_policy_filters_before_priority_or_defaults(self):
        package = replace(inspect_package(self.write("sample.exe", pe_image())), trust="verified")
        route = route_package(package, self.backends, CompatibilityPolicy(allowed_backends=frozenset({"winboat"})))
        self.assertEqual("winboat", route["selectedBackend"])
        denied = route_package(package, self.backends, CompatibilityPolicy(enabled=False), "wine")
        self.assertEqual("policy-denied", denied["status"])
        self.assertEqual([], denied["eligibleBackends"])

    def test_unavailable_preferred_framework_does_not_silently_switch(self):
        package = replace(inspect_package(self.write("sample.exe", pe_image())), trust="verified")
        backends = tuple(replace(item, available=False) if item.id == "wine" else item for item in self.backends)
        route = route_package(package, backends, CompatibilityPolicy(), "wine")
        self.assertEqual("preferred-backend-unavailable", route["status"])
        self.assertIsNone(route["selectedBackend"])
        self.assertEqual(["winboat"], route["eligibleBackends"])

    def test_incompatible_apk_abi_is_explicit(self):
        package = inspect_package(self.apk(b'<manifest package="com.test.app"/>', abi="x86"))
        route = route_package(package, self.backends, CompatibilityPolicy())
        self.assertEqual("no-eligible-backend", route["status"])
        self.assertTrue(any("architecture/ABI" in entry["reason"] for entry in route["excludedBackends"]))

    def test_preferences_reopen_and_reset_by_identity(self):
        preferences = BackendPreferences(self.root / "preferences")
        preferences.set("android:com.test.app", "android")
        reopened = BackendPreferences(preferences.root)
        self.assertEqual("android", reopened.get("android:com.test.app"))
        self.assertEqual(0o700, preferences.root.stat().st_mode & 0o777)
        self.assertEqual(0o600, next(preferences.root.glob("*.json")).stat().st_mode & 0o777)
        reopened.set("android:com.test.app", None)
        self.assertIsNone(preferences.get("android:com.test.app"))

    def test_preferences_reject_identity_tampering_and_symlink(self):
        preferences = BackendPreferences(self.root / "preferences")
        preferences.set("android:com.test.app", "android")
        path = next(preferences.root.glob("*.json"))
        path.write_text(json.dumps({"schemaVersion": 1, "identity": "other", "backend": "android"}))
        with self.assertRaises(RoutingError):
            preferences.get("android:com.test.app")
        path.unlink()
        path.symlink_to(self.write("external", b"{}"))
        with self.assertRaises(RoutingError):
            preferences.get("android:com.test.app")

    def test_administrator_policy_is_reread_and_fail_closed(self):
        if os.geteuid() != 0:
            self.skipTest("requires root-owned temporary policy fixture")
        path = self.write("policy.json", b'{"schemaVersion":1,"enabled":false}')
        path.chmod(0o600)
        source = AdministratorPolicySource(path)
        self.assertFalse(source.read().enabled)
        path.write_text('{"schemaVersion":1,"enabled":true}')
        self.assertTrue(source.read().enabled)
        path.chmod(0o666)
        with self.assertRaises(RoutingError):
            source.read()

    def test_daemon_validates_package_request_shape(self):
        broker = CompatibilityBroker(registry=AppRegistry(self.root / "registry"))
        for request in ({"method": "inspectPackage", "params": {"path": "relative.apk"}},
                        {"method": "inspectPackage", "params": {"path": "/tmp/app.apk", "execute": True}},
                        {"method": "setPackageBackend", "params": {"path": "/tmp/app.apk", "backend": 42}}):
            with self.assertRaises(RequestError):
                dispatch_request(broker, request)

    def test_broker_inspection_and_association_use_the_same_route(self):
        path = self.write("app.exe", pe_image())
        broker = CompatibilityBroker(registry=AppRegistry(self.root / "registry"), policy_source=AdministratorPolicySource(self.root / "absent"))
        with patch.object(broker, "compatibility_backends", return_value=self.backends):
            route = dispatch_request(broker, {"method": "inspectPackage", "params": {"path": str(path)}})
            self.assertEqual("requires-package-trust", route["status"])
            route = dispatch_request(broker, {"method": "setPackageBackend", "params": {"path": str(path), "backend": "winboat"}})
            renamed = path.with_name("renamed.bin")
            path.rename(renamed)
            self.assertEqual("winboat", broker.inspect_foreign_package(str(renamed))["preferredBackend"])
            self.assertIsNone(broker.set_package_backend(str(renamed), None)["preferredBackend"])

    def test_broker_policy_blocks_legacy_registration_and_launch_plan(self):
        source = AdministratorPolicySource(self.root / "absent")
        broker = CompatibilityBroker(registry=AppRegistry(self.root / "registry"), policy_source=source)
        manifest = AppManifest("approved.app", "wine", "wine-11", "C:\\App.exe")
        for policy in (CompatibilityPolicy(enabled=False), CompatibilityPolicy(allowed_identities=frozenset({"approved.app"}))):
            with patch.object(source, "read", return_value=policy):
                with self.assertRaises(CompatibilityError):
                    broker.register_app(manifest)
                with self.assertRaises(CompatibilityError):
                    broker.plan(manifest)


if __name__ == "__main__":
    unittest.main()
