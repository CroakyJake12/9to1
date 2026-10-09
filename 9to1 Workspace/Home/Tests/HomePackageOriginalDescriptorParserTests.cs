using System.Text.Json;
using HavenOS.Home.Apps;
using Xunit;

namespace HavenOS.Home.Tests;

// Parser controls only: these shaped bytes deliberately contain no real signature.
// The parser does not issue publisher enrollment, installation or execution authority.
public sealed class HomePackageOriginalDescriptorParserTests
{
    [Fact]
    public void Same_parser_detaches_source_bytes_and_bounded_collections_without_minting_trust()
    {
        var descriptor = new HomePackageArtifactDescriptor(1, "fixture.package", "fixture", "1", "stable", "app",
            "windows", "win-x64", [], ["home.core"], new string('A', 64), new string('B', 64), 1);
        var payload = JsonSerializer.SerializeToUtf8Bytes(descriptor, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var envelope = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, issuerKeyId = "fixture.untrusted",
            payload = Convert.ToBase64String(payload), signature = Convert.ToBase64String(new byte[384]) });
        var expectedEnvelope = envelope.ToArray(); var expectedPayload = payload.ToArray();
        var parsed = HomePackageOriginalArtifactDescriptorParser.Parse(envelope, payload, "fixture.catalogue",
            new("fixture.package", HomePackageAction.Install, "fixture.shape", "1", "stable", "fixture.catalogue"));
        Array.Fill(envelope, (byte)0); Array.Fill(payload, (byte)0);
        Assert.Equal(expectedEnvelope, parsed.SignedDescriptor); Assert.Equal(expectedPayload, parsed.Payload);
        Assert.Equal("fixture.package", parsed.Descriptor.PackageId);
        Assert.False(parsed.Descriptor.RequiredServiceIds is string[]);
        Assert.Equal("home.core", Assert.Single(parsed.Descriptor.RequiredServiceIds));
    }
    [Fact]
    public void Enveloped_payload_substitution_and_wrong_catalogue_revision_are_refused_before_selection()
    {
        var descriptor = new HomePackageArtifactDescriptor(1, "fixture.package", "fixture", "1", "stable", "app",
            "windows", "win-x64", [], ["home.core"], new string('A', 64), new string('B', 64), 1);
        var payload = JsonSerializer.SerializeToUtf8Bytes(descriptor, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var envelope = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, issuerKeyId = "fixture.untrusted",
            payload = Convert.ToBase64String(payload), signature = Convert.ToBase64String(new byte[384]) });
        var changed = JsonSerializer.SerializeToUtf8Bytes(descriptor with { Version = "2" }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Throws<InvalidDataException>(() => HomePackageOriginalArtifactDescriptorParser.Parse(envelope, changed, "fixture.catalogue",
            new("fixture.package", HomePackageAction.Install, "fixture.shape", "2", "stable", "fixture.catalogue")));
        Assert.Throws<InvalidDataException>(() => HomePackageOriginalArtifactDescriptorParser.Parse(envelope, payload, "changed.catalogue",
            new("fixture.package", HomePackageAction.Install, "fixture.shape", "1", "stable", "fixture.catalogue")));
    }
}
