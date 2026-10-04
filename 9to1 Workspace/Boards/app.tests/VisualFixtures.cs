// Visual fixture builders: representative Maths / Law / CS boards used by
// headless screenshot capture. Built through the production contract API
// so fixtures exercise the real persistence path.

using System.Text;
using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.App.Tests;

internal static class VisualFixtures
{
    public static async Task<string> BuildMathsAsync(string directory)
    {
        var path = Path.Combine(directory, "A-Level Maths.9to1board");
        using var store = new JsonFileHavenBoardStore(directory);
        await using var session = await RichBoardSession.CreateNewAsync(store, "A-Level Maths");
        await session.SaveAsAsync(path);
        await session.MutateAsync(rich =>
        {
            var formula = HavenRichNotesOps.CreateCustomStyle(rich, "Key Formula");
            HavenRichNotesOps.UpdateStyle(rich, formula.Id, s =>
            {
                s.Bold = true;
                s.FontSize = 16;
                s.Background = "#FFFFF3C4";
            });
            rich.Sections.Clear();
            var pure = new HavenRichSection { Title = "Pure Mathematics" };
            var stats = new HavenRichSection { Title = "Statistics" };
            var mech = new HavenRichSection { Title = "Mechanics" };
            rich.Sections.Add(pure);
            rich.Sections.Add(stats);
            rich.Sections.Add(mech);
            foreach (var section in rich.Sections)
            {
                section.Pages.Clear();
                section.Pages.Add(new HavenRichPage { Title = section.Title == "Pure Mathematics" ? "Trigonometry" : "Overview" });
            }
            var page = pure.Pages[0];
            page.Blocks.Clear();
            var h1 = new HavenRichBlock { Kind = HavenRichBlockKind.Heading, PlainText = "Trigonometry", StyleId = "heading-1" };
            var intro = new HavenRichBlock
            {
                Kind = HavenRichBlockKind.Paragraph,
                PlainText = "Angles, identities and the graphs of the circular functions."
            };
            var formulaBlock = new HavenRichBlock
            {
                Kind = HavenRichBlockKind.Paragraph,
                StyleId = formula.Id,
                PlainText = "sin²θ + cos²θ = 1",
                Runs =
                [
                    new HavenRichTextRun { Text = "sin" },
                    new HavenRichTextRun { Text = "2", Baseline = HavenRichBaseline.Superscript },
                    new HavenRichTextRun { Text = "θ + cos" },
                    new HavenRichTextRun { Text = "2", Baseline = HavenRichBaseline.Superscript },
                    new HavenRichTextRun { Text = "θ = 1" },
                ],
            };
            var water = new HavenRichBlock
            {
                Kind = HavenRichBlockKind.Paragraph,
                PlainText = "Remember H₂O breaks down as hydrogen and oxygen.",
                Runs =
                [
                    new HavenRichTextRun { Text = "Remember H" },
                    new HavenRichTextRun { Text = "2", Baseline = HavenRichBaseline.Subscript },
                    new HavenRichTextRun { Text = "O breaks down as hydrogen and oxygen." },
                ],
            };
            var checks = new HavenRichBlock
            {
                Kind = HavenRichBlockKind.Checklist,
                Items =
                [
                    new HavenRichListItem { Text = "Learn double-angle identities", Checked = true },
                    new HavenRichListItem { Text = "Finish differentiation notes", Checked = true },
                    new HavenRichListItem { Text = "Complete practice questions", Checked = false },
                ],
            };
            var table = new HavenRichBlock { Kind = HavenRichBlockKind.Table, Table = HavenRichTable.Create(3, 3) };
            table.Table.Rows[0].Cells[0].Text = "Angle";
            table.Table.Rows[0].Cells[1].Text = "sin";
            table.Table.Rows[0].Cells[2].Text = "cos";
            table.Table.Rows[1].Cells[0].Text = "30°";
            table.Table.Rows[1].Cells[1].Text = "½";
            table.Table.Rows[1].Cells[2].Text = "√3/2";
            table.Table.Rows[2].Cells[0].Text = "45°";
            table.Table.Rows[2].Cells[1].Text = "√2/2";
            table.Table.Rows[2].Cells[2].Text = "√2/2";
            table.Table.BorderColor = "#FF4A6FA5";
            table.Table.BorderWidth = 1;
            table.Table.BorderStyle = HavenRichBorderStyle.Solid;
            table.Table.CornerRadius = 6;
            table.Table.AlternatingRows = true;
            var graph = new HavenRichBlock
            {
                Kind = HavenRichBlockKind.Graph,
                Graph = new HavenRichGraph
                {
                    Expressions =
                    [
                        new HavenRichGraphExpression { Text = "y = sin(x)", Color = "#FF1A73E8" },
                        new HavenRichGraphExpression { Text = "y = x^2", Color = "#FFE8710A" },
                    ],
                    Viewport = new HavenRichGraphViewport { XMin = -6.28, XMax = 6.28, YMin = -2, YMax = 4 },
                },
            };
            var image = new HavenRichBlock
            {
                Kind = HavenRichBlockKind.Image,
                Image = new HavenRichImage
                {
                    DisplayName = "unit-circle.bmp",
                    MediaType = "image/bmp",
                    DataBase64 = Convert.ToBase64String(SampleBmp(96, 64)),
                    SizeBytes = 96 * 64 * 3,
                    Width = 320,
                    AltText = "Unit circle diagram",
                },
            };
            var divider = new HavenRichBlock { Kind = HavenRichBlockKind.Divider, Divider = new HavenRichDivider() };
            var attach = new HavenRichBlock
            {
                Kind = HavenRichBlockKind.Paragraph,
                PlainText = "See the attached formula sheet.",
                Attachment = new HavenRichAttachmentRef
                {
                    DisplayName = "formulae.pdf",
                    MediaType = "application/pdf",
                    DataBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("%PDF revision notes")),
                    SizeBytes = 18,
                },
            };
            page.Blocks.Add(h1);
            page.Blocks.Add(intro);
            page.Blocks.Add(formulaBlock);
            page.Blocks.Add(water);
            page.Blocks.Add(checks);
            page.Blocks.Add(table);
            page.Blocks.Add(graph);
            page.Blocks.Add(image);
            page.Blocks.Add(divider);
            page.Blocks.Add(attach);
            for (var i = 0; i < page.Blocks.Count; i++)
                page.Blocks[i].Order = i;
            page.Ink.Add(new HavenRichInkStroke
            {
                Tool = HavenRichInkTool.Pen,
                Color = "#FF111111",
                Width = 2.5,
                Points = [new() { X = 20, Y = 10, Pressure = 0.7 }, new() { X = 120, Y = 60, Pressure = 0.5 }, new() { X = 220, Y = 30, Pressure = 0.6 }],
            });
            page.Canvas.Add(new HavenRichCanvasObject { Kind = "Text", Text = "CAST diagram", X = 40, Y = 320, Width = 220, Height = 120 });
        });
        await session.SaveAsync();
        return path;
    }

    public static async Task<string> BuildLawAsync(string directory)
    {
        var path = Path.Combine(directory, "A-Level Law.9to1board");
        using var store = new JsonFileHavenBoardStore(directory);
        await using var session = await RichBoardSession.CreateNewAsync(store, "A-Level Law");
        await session.SaveAsAsync(path);
        await session.MutateAsync(rich =>
        {
            var @case = HavenRichNotesOps.CreateCustomStyle(rich, "Case");
            HavenRichNotesOps.UpdateStyle(rich, @case.Id, s => { s.Bold = true; s.Foreground = "#FF1B4F72"; });
            var statute = HavenRichNotesOps.CreateCustomStyle(rich, "Statute");
            HavenRichNotesOps.UpdateStyle(rich, statute.Id, s => { s.Italic = true; s.Background = "#FFFEF9E7"; });
            var evaluation = HavenRichNotesOps.CreateCustomStyle(rich, "Evaluation");
            HavenRichNotesOps.UpdateStyle(rich, evaluation.Id, s => { s.Underline = true; });
            rich.Sections.Clear();
            var crime = new HavenRichSection { Title = "Criminal Law" };
            var tort = new HavenRichSection { Title = "Tort" };
            rich.Sections.Add(crime);
            rich.Sections.Add(tort);
            foreach (var section in rich.Sections)
            {
                section.Pages.Clear();
                section.Pages.Add(new HavenRichPage { Title = section.Title == "Criminal Law" ? "Murder" : "Negligence" });
            }
            var page = crime.Pages[0];
            page.Blocks.Clear();
            page.Blocks.Add(new HavenRichBlock { Kind = HavenRichBlockKind.Heading, PlainText = "Murder: actus reus", StyleId = "heading-1" });
            page.Blocks.Add(new HavenRichBlock
            {
                Kind = HavenRichBlockKind.Paragraph,
                PlainText = "The unlawful killing of a reasonable person in being under the Queen's peace requires proof of causation in both fact and law, examined here across the leading authorities with attention to thin-skull and novus actus interveniens problems.",
            });
            page.Blocks.Add(new HavenRichBlock { Kind = HavenRichBlockKind.Paragraph, PlainText = "R v Pagett (1983) — shield case on causation", StyleId = @case.Id });
            page.Blocks.Add(new HavenRichBlock { Kind = HavenRichBlockKind.Paragraph, PlainText = "Homicide Act 1957, s.1 — abolition of constructive malice", StyleId = statute.Id });
            page.Blocks.Add(new HavenRichBlock { Kind = HavenRichBlockKind.Paragraph, PlainText = "Reform would favour a ladder of offences, but consensus on wording remains elusive.", StyleId = evaluation.Id });
            page.Blocks.Add(new HavenRichBlock
            {
                Kind = HavenRichBlockKind.BulletList,
                Items =
                [
                    new HavenRichListItem { Text = "Factual causation: but-for test" },
                    new HavenRichListItem { Text = "Legal causation: operative and substantial", Level = 1 },
                    new HavenRichListItem { Text = "Breaks in the chain", Level = 1 },
                ],
            });
            var table = new HavenRichBlock { Kind = HavenRichBlockKind.Table, Table = HavenRichTable.Create(3, 2) };
            table.Table.Rows[0].Cells[0].Text = "Case";
            table.Table.Rows[0].Cells[1].Text = "Principle";
            table.Table.Rows[1].Cells[0].Text = "R v White";
            table.Table.Rows[1].Cells[1].Text = "But for";
            table.Table.Rows[2].Cells[0].Text = "R v Pagett";
            table.Table.Rows[2].Cells[1].Text = "Shield";
            page.Blocks.Add(table);
            for (var i = 0; i < page.Blocks.Count; i++)
                page.Blocks[i].Order = i;
        });
        await session.SaveAsync();
        return path;
    }

    public static async Task<string> BuildCsAsync(string directory)
    {
        var path = Path.Combine(directory, "A-Level Computer Science.9to1board");
        using var store = new JsonFileHavenBoardStore(directory);
        await using var session = await RichBoardSession.CreateNewAsync(store, "A-Level Computer Science");
        await session.SaveAsAsync(path);
        await session.MutateAsync(rich =>
        {
            rich.Sections.Clear();
            var section = new HavenRichSection { Title = "Algorithms" };
            rich.Sections.Add(section);
            section.Pages.Clear();
            section.Pages.Add(new HavenRichPage { Title = "Sorting" });
            var page = section.Pages[0];
            page.Blocks.Clear();
            page.Blocks.Add(new HavenRichBlock { Kind = HavenRichBlockKind.Heading, PlainText = "Merge sort", StyleId = "heading-1" });
            page.Blocks.Add(new HavenRichBlock
            {
                Kind = HavenRichBlockKind.Paragraph,
                PlainText = "def merge_sort(xs):\n    if len(xs) <= 1:\n        return xs",
                StyleId = "code",
            });
            var table = new HavenRichBlock { Kind = HavenRichBlockKind.Table, Table = HavenRichTable.Create(3, 3) };
            table.Table.Rows[0].Cells[0].Text = "Algorithm";
            table.Table.Rows[0].Cells[1].Text = "Best";
            table.Table.Rows[0].Cells[2].Text = "Worst";
            table.Table.Rows[1].Cells[0].Text = "Merge sort";
            table.Table.Rows[1].Cells[1].Text = "n log n";
            table.Table.Rows[1].Cells[2].Text = "n log n";
            table.Table.Rows[2].Cells[0].Text = "Quicksort";
            table.Table.Rows[2].Cells[1].Text = "n log n";
            table.Table.Rows[2].Cells[2].Text = "n²";
            page.Blocks.Add(table);
            page.Blocks.Add(new HavenRichBlock
            {
                Kind = HavenRichBlockKind.Image,
                Image = new HavenRichImage
                {
                    DisplayName = "recursion.bmp",
                    MediaType = "image/bmp",
                    DataBase64 = Convert.ToBase64String(SampleBmp(96, 64)),
                    SizeBytes = 96 * 64 * 3,
                    Width = 280,
                    AltText = "Recursion tree",
                },
            });
            page.Blocks.Add(new HavenRichBlock
            {
                Kind = HavenRichBlockKind.Paragraph,
                PlainText = "Lecture handout attached.",
                Attachment = new HavenRichAttachmentRef
                {
                    DisplayName = "sorting.pdf",
                    MediaType = "application/pdf",
                    DataBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("%PDF sorting")),
                    SizeBytes = 13,
                },
            });
            for (var i = 0; i < page.Blocks.Count; i++)
                page.Blocks[i].Order = i;
            page.Ink.Add(new HavenRichInkStroke
            {
                Points = [new() { X = 30, Y = 20, Pressure = 0.6 }, new() { X = 90, Y = 80, Pressure = 0.6 }],
            });
            page.Canvas.Add(new HavenRichCanvasObject { Kind = "Text", Text = "partition diagram", X = 60, Y = 300, Width = 240, Height = 140 });
        });
        await session.SaveAsync();
        return path;
    }

    /// <summary>Deterministic 24-bit BMP (white bg, grey axes, blue diagonal) — no asset files needed.</summary>
    internal static byte[] SampleBmp(int width, int height)
    {
        var rowBytes = width * 3;
        var padding = (4 - (rowBytes % 4)) % 4;
        var stride = rowBytes + padding;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = y * stride + x * 3;
                byte r = 255, g = 255, b = 255;
                if (x == width / 2 || y == height / 2) { r = 200; g = 200; b = 200; }
                var diag = (height - 1) - (x * (height - 1) / Math.Max(1, width - 1));
                if (Math.Abs(y - diag) <= 1) { r = 26; g = 115; b = 232; }
                pixels[offset] = b;
                pixels[offset + 1] = g;
                pixels[offset + 2] = r;
            }
        }
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0x4D42);
        writer.Write(54 + pixels.Length);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((ushort)1);
        writer.Write((ushort)24);
        writer.Write(0);
        writer.Write(pixels.Length);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);
        writer.Write(pixels);
        return stream.ToArray();
    }
}
