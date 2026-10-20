using System;
using System.IO;
using System.Linq;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace Haven.Desktop.Views.Shell.VoiceVision;

/// <summary>
/// Loads the authored Voice &amp; Vision CUI documents (compact pill and full session surface)
/// and owns the bounded CUI registry they render against. Each Voice &amp; Vision host
/// constructs its own registry, exactly like the Home launcher, so no global image resolver
/// is installed and no loader silently gains a wider capability.
///
/// The only resolver registered is the canonical symbolic icon catalog shared with the
/// browser and Spaces surfaces (<c>icon:&lt;bounded-symbolic-key&gt;</c>). It performs no
/// file, network or arbitrary asset access, and an unrecognised symbolic key keeps the
/// catalog's visible fallback rather than failing the surface.
/// </summary>
internal static class VoiceVisionCuiDocument
{
    public const string PillResourceName = "Haven.Desktop.VoiceVision.Pill.cui";
    public const string SessionResourceName = "Haven.Desktop.VoiceVision.Session.cui";

    public static CuiControlRegistry CreateControlRegistry()
    {
        var registry = new CuiControlRegistry();
        NineToOne.Web.BrowserCanonicalIcons.Register(registry);
        return registry;
    }

    public static CuiDocument ParsePill() => Parse(PillResourceName, "VoiceVisionPill.cui");

    public static CuiDocument ParseSession() => Parse(SessionResourceName, "VoiceVisionSession.cui");

    private static CuiDocument Parse(string resourceName, string sourceName)
    {
        using var stream = typeof(VoiceVisionCuiDocument).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"The Voice &amp; Vision document '{resourceName}' is missing from the desktop assembly.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), sourceName);
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException($"The Voice &amp; Vision document '{sourceName}' is invalid: " + string.Join("; ",
                parser.Diagnostics.Diagnostics.Where(item => item.Severity == CuiDiagnosticSeverity.Error)
                    .Select(item => item.Code + " " + item.Message)));
        return document;
    }
}
