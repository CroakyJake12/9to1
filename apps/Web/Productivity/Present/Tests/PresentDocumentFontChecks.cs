using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;

internal static class PresentDocumentFontChecks
{
    internal static int Run(IAppPaths paths)
    {
        var count = 0;
        void Check(bool pass, string message)
        { if (!pass) throw new InvalidOperationException(message); count++; Console.WriteLine("ASSERT " + message); }
        var fontHashes = new Dictionary<string, string>
        {
            ["Regular"] = "b4ff23ba370cc95a3c349336b73f9c28514a1371210f89832efc85c4b1ea7131",
            ["Italic"] = "718a0663864d37a4868220a19b9668a5fe10a46197f6df367b4c2c30c04c026c",
            ["Bold"] = "0f62ab34ad5d079a0a28fac01bcf7c7a724a4db4d6cb99cab9cabff382fbb80f",
            ["BoldItalic"] = "380764b6898d7b73ceae6384b2958b196d2a0428962ef3adf138d27947228666"
        };
        foreach (var (style, hash) in fontHashes)
        {
            using var bytes = AssetLoader.Open(new Uri($"avares://Haven/Assets/Fonts/Carlito/Carlito-{style}.ttf"));
            Check(Convert.ToHexStringLower(SHA256.HashData(bytes)) == hash,
                $"actual packaged Carlito {style} bytes match the pinned LibreOffice OFL font archive");
        }
        using (var license = new StreamReader(AssetLoader.Open(new Uri("avares://Haven/Assets/Fonts/Carlito/LICENSE"))))
            Check(license.ReadToEnd().Contains("SIL OPEN FONT LICENSE Version 1.1", StringComparison.Ordinal), "packaged document font includes its original redistribution license");

        var receipts = new List<object>();
        foreach (var (weight, italic) in new[] { (400, false), (400, true), (700, false), (700, true) })
        {
            const string text = "Saved title object";
            var run = new HavenParagraphRun(text, "Calibri", 44 * 96d / 72, weight, italic, new HavenSolidBrush(255, 0, 0, 0));
            var paragraph = HavenSceneControl.ShapeParagraph([run], 2000);
            using var shaped = ActualFrame(paragraph);
            var faces = shaped.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>()
                .Select(item => item.GlyphRun.GlyphTypeface).ToArray();
            var expected = FontManager.Current.SystemFonts.Any(font => font.Name.Equals("Calibri", StringComparison.OrdinalIgnoreCase)) ? "Calibri" : "Carlito";
            Check(faces.Length > 0 && faces.All(face => face.FamilyName == expected),
                $"actual authored Calibri paragraph resolves to {expected}, weight {weight}, italic {italic}");
            Check(paragraph.Runs.Single().FontFamily == "Calibri" && paragraph.Runs.Single().FontSize == run.FontSize,
                "font substitution leaves the authored family and size unchanged");
            if (expected == "Carlito")
            {
                using var reference = new TextLayout(text, new Typeface(new FontFamily("avares://Haven/Assets/Fonts/Carlito#Carlito"),
                    italic ? FontStyle.Italic : FontStyle.Normal, (FontWeight)weight), run.FontSize, Brushes.Black);
                Check(Math.Abs(paragraph.Size.Width - reference.WidthIncludingTrailingWhitespace) < .001,
                    "document measurement uses the same actual Carlito metrics as rendering");
            }
            var end = paragraph.CaretRect(text.Length);
            Check(paragraph.HitTest(new HavenPoint(end.X, end.Y + end.Height / 2)) == text.Length
                && Math.Abs(end.X - paragraph.Size.Width) < .001,
                "document end caret and pointer hit testing agree with the shaped font width");
            receipts.Add(new { authored = run.FontFamily, weight, italic, faces = faces.Select(face => new { face.FamilyName, style = face.Style.ToString(), weight = face.Weight.ToString() }), width = paragraph.Size.Width });
        }

        var uiFont = typeof(HavenSceneControl).Assembly.GetType("Haven.Desktop.HavenUI.Tokens.HavenUiFont")!;
        var setUser = uiFont.GetMethod("SetUserFamily", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previousUser = uiFont.GetProperty("UserFamily", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        try
        {
            setUser.Invoke(null, ["Liberation Sans"]);
            var paragraph = HavenSceneControl.ShapeParagraph([new("Authored Montserrat", "Montserrat", 24, 500, false, new HavenSolidBrush(255, 0, 0, 0))], 900);
            using var shaped = ActualFrame(paragraph);
            var documentFaces = shaped.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>()
                .Select(run => run.GlyphRun.GlyphTypeface).ToArray();
            // The unmodified static medium font's legacy family name is "Montserrat Medium";
            // compare to the actual bundled face instead of confusing family naming with fallback.
            var bundledMedium = new Typeface(new FontFamily("avares://Haven/Assets/Fonts/MontserratStatic#Montserrat"), weight: FontWeight.Medium).GlyphTypeface;
            Check(documentFaces.Length > 0 && documentFaces.All(face => face.FamilyName == bundledMedium.FamilyName
                && face.Weight == bundledMedium.Weight && face.FamilyName.StartsWith("Montserrat", StringComparison.Ordinal)),
                "interface font preference cannot rewrite authored Montserrat document typography");
        }
        finally { setUser.Invoke(null, [previousUser]); }
        Check(FontManager.Current.DefaultFontFamily.ToString() == "avares://CakeOS.Cui.Runtime/Assets/Fonts/MontserratStatic#Montserrat",
            "CUI retains its original bundled Montserrat default");
        File.WriteAllText(Path.Combine(paths.DataDirectory, "document-font-faces.json"), JsonSerializer.Serialize(receipts, new JsonSerializerOptions { WriteIndented = true }));
        return count;
    }

    private static TextLayout ActualFrame(HavenParagraphLayout paragraph) => (TextLayout)paragraph.GetType()
        .GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes)!.Invoke(paragraph, null)!;
}
