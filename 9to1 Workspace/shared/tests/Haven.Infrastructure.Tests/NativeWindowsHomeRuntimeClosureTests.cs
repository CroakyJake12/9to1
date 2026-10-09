using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Haven.Infrastructure.Native.Windows;
using Xunit;

namespace Haven.Infrastructure.Tests;

// Format controls only, never fabricated publisher/enrollment/native/runtime or
// launch qualification. The producer separately reads these exact pinned files.
public sealed class NativeWindowsHomeRuntimeClosureTests
{
    [Fact]
    public void Standard_unbundled_apphost_shape_refuses_extraction_external_search_and_different_managed_entry()
    {
        var bytes = AppHost("ActualHome.dll");
        Assert.True(NativeWindowsHomeInstalledRootAdmission.IsSupportedOriginalAppHost(bytes, "ActualHome.dll"));
        var bundle = bytes.ToArray(); BinaryPrimitives.WriteInt64LittleEndian(bundle.AsSpan(120), 2048);
        Assert.False(NativeWindowsHomeInstalledRootAdmission.IsSupportedOriginalAppHost(bundle, "ActualHome.dll"));
        var external = bytes.ToArray(); external[254] = 8; // Global-only resolver configuration.
        Assert.False(NativeWindowsHomeInstalledRootAdmission.IsSupportedOriginalAppHost(external, "ActualHome.dll"));
        Assert.False(NativeWindowsHomeInstalledRootAdmission.IsSupportedOriginalAppHost(bytes, "Foreign.dll"));
    }

    [Fact]
    public void Framework_reference_probing_and_missing_actual_declared_asset_refuse_supported_documents()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ActualHome.dll", "System.Private.CoreLib.dll", "coreclr.dll" };
        using var actual = JsonDocument.Parse("""{"runtimeOptions":{"tfm":"net10.0","includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"}],"configProperties":{"System.Reflection.Metadata.MetadataUpdater.IsSupported":false}}}""");
        using var deps = JsonDocument.Parse("""{"runtimeTarget":{"name":".NETCoreApp,Version=v10.0/win-x64"},"targets":{".NETCoreApp,Version=v10.0/win-x64":{"Original/1":{"runtime":{"ActualHome.dll":{},"System.Private.CoreLib.dll":{}},"native":{"coreclr.dll":{}}}}}}""");
        Assert.True(NativeWindowsHomeInstalledRootAdmission.IsSupportedOriginalRuntimeDocuments(actual.RootElement, deps.RootElement, "", files));
        Assert.True(files.Remove("coreclr.dll"));
        Assert.False(NativeWindowsHomeInstalledRootAdmission.IsSupportedOriginalRuntimeDocuments(actual.RootElement, deps.RootElement, "", files));
        using var framework = JsonDocument.Parse("""{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}""");
        using var probing = JsonDocument.Parse("""{"runtimeOptions":{"tfm":"net10.0","includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"}],"additionalProbingPaths":["outside"]}}""");
        Assert.False(NativeWindowsHomeInstalledRootAdmission.IsSupportedOriginalRuntimeDocuments(framework.RootElement, deps.RootElement, "", files));
        Assert.False(NativeWindowsHomeInstalledRootAdmission.IsSupportedOriginalRuntimeDocuments(probing.RootElement, deps.RootElement, "", files));
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("C:/outside.dll")]
    [InlineData("runtimeTargets")]
    public void Runtime_manifest_cannot_promote_escaping_or_alternate_unpinned_assets(string unsupported)
    {
        using var config = JsonDocument.Parse("""{"runtimeOptions":{"tfm":"net10.0","includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"}]}}""");
        var group = unsupported == "runtimeTargets" ? unsupported : "runtime";
        var asset = unsupported == "runtimeTargets" ? "ActualHome.dll" : unsupported;
        var source = new { runtimeTarget = new { name = ".NETCoreApp,Version=v10.0/win-x64" }, targets = new Dictionary<string, object> {
            [".NETCoreApp,Version=v10.0/win-x64"] = new Dictionary<string, object> { ["Original/1"] = new Dictionary<string, object> {
                [group] = new Dictionary<string, object> { [asset] = new { } } } } } };
        using var deps = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(source));
        Assert.False(NativeWindowsHomeInstalledRootAdmission.IsSupportedOriginalRuntimeDocuments(config.RootElement, deps.RootElement, "",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ActualHome.dll", "outside.dll" }));
    }

    private static byte[] AppHost(string managed)
    {
        var bytes = new byte[2048]; bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(60), 64);
        new byte[] { 80, 69, 0, 0 }.CopyTo(bytes, 64); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(68), 0x8664);
        Convert.FromHexString("8B1202B96A612038727B930214D7A03213F5B9E6EFAE3318EE3B2DCE24B36AAE").CopyTo(bytes, 128);
        Encoding.ASCII.GetBytes("19ff3e9c3602ae8e841925bb461a0adb064a1f1903667a5e0d87e8f608f425ac").CopyTo(bytes, 256);
        Encoding.UTF8.GetBytes(managed + "\0").CopyTo(bytes, 512); return bytes;
    }
}
