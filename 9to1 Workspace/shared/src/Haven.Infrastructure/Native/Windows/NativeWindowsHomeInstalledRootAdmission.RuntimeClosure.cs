using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using HavenOS.Home.Apps;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Windows;

public sealed partial class NativeWindowsHomeInstalledRootAdmission
{
    // Supported publisher-authenticated standard .NET 10 unbundled apphost shape.
    // Source: dotnet/runtime v10.0.0 src/native/corehost/apphost/bundle_marker.cpp.
    // This is a format marker, never a signer, code-trust or permission anchor.
    private static readonly byte[] DotNetBundleMarker = Convert.FromHexString(
        "8B1202B96A612038727B930214D7A03213F5B9E6EFAE3318EE3B2DCE24B36AAE");

    private SafeFileHandle OpenImmutableRuntimeFile(Invocation work, WindowsOriginalRoot actualRoot, string path) => work.Sources.Invoke(() =>
    {
        var parent = actualRoot.OpenDirectory(Path.GetDirectoryName(path)!); var parentHeld = new Held(parent); work.Resources.Add(parentHeld);
        parentHeld.Identity = WindowsOriginalFileCustody.ReadIdentity(parent);
        parentHeld.Security = NativeWindowsHomePackageActivationOwner.DemandProtectedMachine(parent);
        WindowsOriginalFileCustody.DemandPath(parent, Path.GetDirectoryName(path)!, true);
        // A running child may resolve assemblies/native modules later. The SAME
        // source pins deny WRITE/DELETE for its entire accepted child lifetime.
        var actual = WindowsOriginalFileCustody.OpenRelative(parent, Path.GetFileName(path),
            WindowsOriginalFileCustody.ReadData | WindowsOriginalFileCustody.ReadAttributes | WindowsOriginalFileCustody.ReadControl,
            1U /* FILE_SHARE_READ; no WRITE/DELETE */, WindowsOriginalCreateDisposition.OpenExisting, WindowsOriginalFileKind.File);
        var held = new Held(actual); work.Resources.Add(held);
        held.Identity = WindowsOriginalFileCustody.ReadIdentity(actual);
        held.Security = NativeWindowsHomePackageActivationOwner.DemandProtectedMachine(actual);
        WindowsOriginalFileCustody.DemandPath(actual, path, false); return actual;
    });

    private async Task<bool> HasSupportedOriginalRuntime(Invocation work, WindowsOriginalRoot root,
        HomePackageOriginalInstalledActivationRecord layout, IReadOnlyDictionary<string, SafeFileHandle> files, CancellationToken token)
    {
        var entry = layout.EntrypointRelativePath;
        if (!entry.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        var slash = entry.LastIndexOf('/'); var directory = slash < 0 ? "" : entry[..(slash + 1)];
        var name = entry[(slash + 1)..^4];
        var required = new[] { name + ".dll", name + ".runtimeconfig.json", name + ".deps.json", "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "clrjit.dll", "System.Private.CoreLib.dll" };
        if (required.Any(relative => !files.ContainsKey(directory + relative))) return false;
        // A .dev file can add external probing locations, even when omitted from
        // declared files. Absence is the exact native not-found result in this held
        // protected parent, never File.Exists(false) or an exception-type waiver.
        var devPath = Path.Combine(layout.InstallationRoot, (directory + name + ".runtimeconfig.dev.json").Replace('/', Path.DirectorySeparatorChar));
        var hasDev = work.Sources.Invoke(() =>
        {
            try { _ = OpenFile(work, root, devPath); return true; }
            catch (WindowsOriginalNativeFailure failure) when (failure.OriginalFailureStatus == unchecked((int)0xc0000034)) { return false; }
        });
        if (hasDev) return false;
        var image = await ReadOriginalRuntimeBytes(work, files[entry], 64 * 1024 * 1024, token).ConfigureAwait(false);
        if (!work.Sources.Invoke(() => IsSupportedOriginalAppHost(image, name + ".dll"))) return false;
        var configBytes = await ReadOriginalRuntimeBytes(work, files[directory + name + ".runtimeconfig.json"], 1024 * 1024, token).ConfigureAwait(false);
        var depsBytes = await ReadOriginalRuntimeBytes(work, files[directory + name + ".deps.json"], 16 * 1024 * 1024, token).ConfigureAwait(false);
        var config = work.Sources.Invoke(() => { var value = JsonDocument.Parse(configBytes, new JsonDocumentOptions { MaxDepth = 32 }); work.Resources.Add(new(value)); return value; });
        var deps = work.Sources.Invoke(() => { var value = JsonDocument.Parse(depsBytes, new JsonDocumentOptions { MaxDepth = 64 }); work.Resources.Add(new(value)); return value; });
        var supported = work.Sources.Invoke(() => IsSupportedOriginalRuntimeDocuments(config.RootElement, deps.RootElement,
            directory, files.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)));
        work.Sources.Invoke(() => { DemandNativeCurrent(work); return true; }); return supported;
    }

    private async Task<byte[]> ReadOriginalRuntimeBytes(Invocation work, SafeFileHandle same, int bound, CancellationToken token)
    {
        var identity = work.Sources.Invoke(() => WindowsOriginalFileCustody.ReadIdentity(same));
        if (identity.Size is 0 || identity.Size > (ulong)bound) throw new InvalidDataException("The protected runtime metadata/image exceeds its supported bound.");
        var bytes = new byte[checked((int)identity.Size)]; var offset = 0;
        while (offset < bytes.Length)
        {
            var count = await Read(work, () => RandomAccess.ReadAsync(same, bytes.AsMemory(offset), offset, token).AsTask()).ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException(); offset += count;
        }
        work.Sources.Invoke(() =>
        {
            if (!WindowsOriginalFileCustody.ReadIdentity(same).SameReadVersion(identity))
                throw new UnauthorizedAccessException("The SAME protected runtime metadata changed during its original read.");
            return true;
        }); return bytes;
    }

    // Pure supported-shape checks, used only after actual signed full-cohort/native
    // reads above. Detached bytes cannot issue runtime or controlled-launch proof.
    internal static bool IsSupportedOriginalAppHost(ReadOnlySpan<byte> image, string managedName)
    {
        if (image.Length < 1024 || image[0] != 'M' || image[1] != 'Z' || managedName.Length is < 5 or > 240 ||
            managedName.Any(character => character is '/' or '\\' or ':' || character > 127)) return false;
        var pe = BinaryPrimitives.ReadInt32LittleEndian(image[60..]);
        if (pe < 64 || pe > image.Length - 24 || !image.Slice(pe, 4).SequenceEqual(new byte[] { 80, 69, 0, 0 }) ||
            BinaryPrimitives.ReadUInt16LittleEndian(image[(pe + 4)..]) != 0x8664) return false;
        var marker = image.IndexOf(DotNetBundleMarker);
        if (marker < 8 || image[(marker + DotNetBundleMarker.Length)..].IndexOf(DotNetBundleMarker) >= 0 ||
            BinaryPrimitives.ReadInt64LittleEndian(image[(marker - 8)..]) != 0) return false;
        var search = Encoding.ASCII.GetBytes("19ff3e9c3602ae8e841925bb461a0adb064a1f1903667a5e0d87e8f608f425ac");
        var searchSlot = image.IndexOf(search);
        // Unmodified .NET10 default resolver searches app-local FIRST. The held
        // app-local hostfxr above therefore resolves without environment/global
        // discovery; unsupported alternate search configuration refuses.
        if (searchSlot < 2 || searchSlot + search.Length >= image.Length || image[searchSlot + search.Length] != 0 ||
            image[searchSlot - 2] != 0 || image[searchSlot - 1] != 0 ||
            image[(searchSlot + search.Length)..].IndexOf(search) >= 0) return false;
        var target = Encoding.UTF8.GetBytes(managedName + "\0"); var slot = image.IndexOf(target);
        if (slot < 0 || slot > image.Length - 1024 || image[(slot + target.Length)..].IndexOf(target) >= 0) return false;
        return image.Slice(slot + target.Length, 1024 - target.Length).IndexOfAnyExcept((byte)0) < 0;
    }

    internal static bool IsSupportedOriginalRuntimeDocuments(JsonElement config, JsonElement deps, string directory,
        IReadOnlySet<string> originalFiles)
    {
        if (config.ValueKind != JsonValueKind.Object || !config.TryGetProperty("runtimeOptions", out var options) || options.ValueKind != JsonValueKind.Object ||
            !options.TryGetProperty("tfm", out var tfm) || tfm.GetString() != "net10.0" ||
            !options.TryGetProperty("includedFrameworks", out var included) || included.ValueKind != JsonValueKind.Array || included.GetArrayLength() is < 1 or > 2) return false;
        foreach (var property in options.EnumerateObject())
            if (property.Name is not ("tfm" or "includedFrameworks" or "configProperties")) return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var framework in included.EnumerateArray())
        {
            if (framework.ValueKind != JsonValueKind.Object || !framework.TryGetProperty("name", out var frameworkName) ||
                frameworkName.GetString() is not ("Microsoft.NETCore.App" or "Microsoft.WindowsDesktop.App") ||
                !names.Add(frameworkName.GetString()!) || !framework.TryGetProperty("version", out var version) ||
                !Version.TryParse(version.GetString(), out var parsed) || parsed.Major != 10) return false;
        }
        if (!names.Contains("Microsoft.NETCore.App")) return false;
        if (options.TryGetProperty("configProperties", out var properties))
        {
            if (properties.ValueKind != JsonValueKind.Object) return false;
            foreach (var property in properties.EnumerateObject())
                if (property.Name.Contains("StartupHook", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Probing", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Contains("AdditionalDeps", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Contains("MetadataUpdater", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind != JsonValueKind.False ||
                    property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number or JsonValueKind.String)) return false;
        }
        if (!deps.TryGetProperty("runtimeTarget", out var target) || !target.TryGetProperty("name", out var targetName) ||
            targetName.GetString() is not { } name || name != ".NETCoreApp,Version=v10.0/win-x64" ||
            !deps.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object || !targets.TryGetProperty(name, out var selected) ||
            selected.ValueKind != JsonValueKind.Object) return false;
        var assets = 0;
        foreach (var library in selected.EnumerateObject())
        {
            if (library.Value.ValueKind != JsonValueKind.Object) return false;
            foreach (var group in library.Value.EnumerateObject())
            {
                if (group.Name is "dependencies" or "compile") continue;
                if (group.Name is not ("runtime" or "native" or "resources")) return false;
                if (group.Value.ValueKind != JsonValueKind.Object) return false;
                foreach (var asset in group.Value.EnumerateObject())
                {
                    if (++assets > 4096 || !HomePackageOriginalInstalledActivationRecord.SafeRelative(asset.Name)) return false;
                    // Publish flattens runtime/native package assets; resources keep
                    // their culture subdirectory. The actual complete signed list
                    // must contain that exact app-local published name.
                    var local = group.Name == "resources" ? asset.Name : asset.Name[(asset.Name.LastIndexOf('/') + 1)..];
                    if (!originalFiles.Contains(directory + local)) return false;
                }
            }
        }
        return assets > 0;
    }
}
