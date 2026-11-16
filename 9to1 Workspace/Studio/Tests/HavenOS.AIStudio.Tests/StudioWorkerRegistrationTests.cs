using System.Text;
using System.Text.Json;
using HavenOS.AIStudio;
using Xunit;

namespace HavenOS.AIStudio.Tests;

public sealed class StudioWorkerRegistrationTests
{
    private static Dictionary<string, string> Pins() => new()
    {
        ["PythonPath"] = typeof(StudioWorkerRegistrationTests).Assembly.Location,
        ["PythonSha256"] = new string('a', 64), ["CliArchivePath"] = typeof(StudioWorkerRegistrationTests).Assembly.Location,
        ["CliArchiveSha256"] = new string('b', 64), ["OperatorConfigPath"] = typeof(StudioWorkerRegistrationTests).Assembly.Location,
        ["OperatorConfigSha256"] = new string('c', 64)
    };

    [Fact]
    public void MissingEnvironmentLeavesSelfCheckUnconfigured() => Assert.Null(StudioWorkerRegistration.ReadEnvironment(_ => null));

    [Fact]
    public async Task InvalidRegistrationDoesNotPreventStudioComposition()
    {
        var setup = StudioWorkerRegistration.CreateController(_ => throw new InvalidDataException("malformed deployment fixture"));
        await using var controller = setup.Controller;
        await controller.RefreshAsync();
        Assert.NotNull(setup.Error);
        Assert.False(controller.State.CanLaunch);
        Assert.Contains("Studio projects remain available", setup.Error);
    }

    [Fact]
    public void SixExactPinsParseWithoutClaimingHashesVerified()
    {
        var pins = Pins();
        var result = StudioWorkerRegistration.Parse(JsonSerializer.SerializeToUtf8Bytes(pins));
        Assert.Equal(pins["PythonPath"], result.PythonPath);
        Assert.Equal(pins["OperatorConfigSha256"], result.OperatorConfigSha256);
    }

    [Fact]
    public void UnknownDuplicateAndMissingFieldsAreRejected()
    {
        var pins = Pins();
        pins["JobPath"] = "project input";
        Assert.Throws<InvalidDataException>(() => StudioWorkerRegistration.Parse(JsonSerializer.SerializeToUtf8Bytes(pins)));
        var json = JsonSerializer.Serialize(Pins());
        Assert.Throws<InvalidDataException>(() => StudioWorkerRegistration.Parse(Encoding.UTF8.GetBytes(json[..^1] + ",\"PythonSha256\":\"duplicate\"}")));
        pins = Pins(); pins.Remove("PythonPath");
        Assert.Throws<InvalidDataException>(() => StudioWorkerRegistration.Parse(JsonSerializer.SerializeToUtf8Bytes(pins)));
    }

    [Fact]
    public void RelativeNetworkPathsBadHashesAndOversizedInputAreRejected()
    {
        foreach (var value in new[] { "python.exe", "\\\\server\\share\\python.exe", "//server/share/python.exe" })
        {
            var pins = Pins(); pins["PythonPath"] = value;
            Assert.Throws<InvalidDataException>(() => StudioWorkerRegistration.Parse(JsonSerializer.SerializeToUtf8Bytes(pins)));
        }
        var invalid = Pins(); invalid["PythonSha256"] = new string('A', 64);
        Assert.Throws<InvalidDataException>(() => StudioWorkerRegistration.Parse(JsonSerializer.SerializeToUtf8Bytes(invalid)));
        Assert.Throws<InvalidDataException>(() => StudioWorkerRegistration.Parse(new byte[32769]));
    }
}
