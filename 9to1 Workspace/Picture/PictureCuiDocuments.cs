using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace HavenOS.Images;

internal static class PictureCuiDocuments
{
    internal static CuiDocument Load(string name)
    {
        using var source = typeof(PictureCuiDocuments).Assembly.GetManifestResourceStream($"HavenOS.Images.UI.{name}.cui")
            ?? throw new InvalidDataException($"The authored Picture scene {name} is missing.");
        using var reader = new StreamReader(source);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), name + ".cui");
        if (parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException(string.Join(Environment.NewLine, parser.Diagnostics.Diagnostics));
        return document;
    }
}

internal sealed class PictureRequiredHome : ICuiSceneReadiness
{
    public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable,
            "HomeStartupAttachmentUnavailable",
            "Open Picture from Home to connect its shared Files and editing services. If Home is not installed, install Home before continuing. Your existing images and Picture documents are preserved."));
    }
}
