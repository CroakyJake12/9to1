using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

public sealed class WriteSelectionFormattingTests
{
    [Fact]
    public void Selection_formatting_only_changes_highlighted_text()
    {
        var document = NotesDocument.Create();
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "Alpha beta gamma";
        block.Runs = [new NotesTextRun { Text = block.PlainText }];
        var editor = new WriteDocumentEditor(document);

        editor.SelectBlock(block.Id, 10, 6, 10);
        editor.ToggleSelectionCharacter(WriteCharacterFormat.Bold);

        Assert.Equal(3, block.Runs.Count);
        Assert.Equal("Alpha ", block.Runs[0].Text);
        Assert.False(block.Runs[0].Bold);
        Assert.Equal("beta", block.Runs[1].Text);
        Assert.True(block.Runs[1].Bold);
        Assert.Equal(" gamma", block.Runs[2].Text);
        Assert.False(block.Runs[2].Bold);
    }

    [Fact]
    public void Selection_formatting_spans_existing_runs_without_touching_outer_text()
    {
        var document = NotesDocument.Create();
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "One Two Three";
        block.Runs =
        [
            new NotesTextRun { Text = "One ", Italic = true },
            new NotesTextRun { Text = "Two ", Bold = true },
            new NotesTextRun { Text = "Three", Underline = true }
        ];
        var editor = new WriteDocumentEditor(document);

        editor.SelectBlock(block.Id, 9, 2, 9);
        editor.SetFontFamily("Aptos");

        Assert.Equal("On", block.Runs[0].Text);
        Assert.NotEqual("Aptos", block.Runs[0].FontFamily);
        Assert.Equal("e ", block.Runs[1].Text);
        Assert.Equal("Aptos", block.Runs[1].FontFamily);
        Assert.Equal("Two ", block.Runs[2].Text);
        Assert.Equal("Aptos", block.Runs[2].FontFamily);
        Assert.Equal("T", block.Runs[3].Text);
        Assert.Equal("Aptos", block.Runs[3].FontFamily);
        Assert.Equal("hree", block.Runs[4].Text);
        Assert.NotEqual("Aptos", block.Runs[4].FontFamily);
    }

    [Fact]
    public void Comment_targets_selected_text_range()
    {
        var document = NotesDocument.Create();
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "Alpha beta";
        block.Runs = [new NotesTextRun { Text = block.PlainText }];
        var editor = new WriteDocumentEditor(document);

        editor.SelectBlock(block.Id, 10, 6, 10);
        editor.AddComment("Review");

        var comment = Assert.Single(document.Comments);
        Assert.Equal(6, comment.StartOffset);
        Assert.Equal(10, comment.EndOffset);
        Assert.Equal("Review", comment.Text);
    }

    [Theory]
    [InlineData(WriteCharacterFormat.Bold, false)]
    [InlineData(WriteCharacterFormat.Bold, true)]
    [InlineData(WriteCharacterFormat.Italic, false)]
    [InlineData(WriteCharacterFormat.Italic, true)]
    [InlineData(WriteCharacterFormat.Underline, false)]
    [InlineData(WriteCharacterFormat.Underline, true)]
    [InlineData(WriteCharacterFormat.StrikeThrough, false)]
    [InlineData(WriteCharacterFormat.StrikeThrough, true)]
    public void Mixed_range_enables_every_selected_character_without_changing_neighbours(
        WriteCharacterFormat format, bool backwards)
    {
        var (document, block, editor) = MixedDocument(format);
        var before = Sections(document);
        var expected = Characters(block);
        var originalIds = block.Runs.Select(run => run.Id).ToArray();
        var first = System.Text.Json.JsonSerializer.Serialize(block.Runs[0]);
        var last = System.Text.Json.JsonSerializer.Serialize(block.Runs[^1]);
        var caret = backwards ? 4 : 9;
        editor.SelectBlock(block.Id, caret, backwards ? 9 : 4, backwards ? 4 : 9);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.ToggleSelectionCharacter(format);

        AssertCharacters(expected, block, format, 4, 9, true);
        Assert.Equal("AAOne Two ZZ", block.PlainText);
        Assert.Equal("e Two", editor.SelectedText);
        Assert.Equal(caret, editor.CaretIndex);
        Assert.Equal(4, editor.SelectionStart);
        Assert.Equal(9, editor.SelectionEnd);
        Assert.Equal(1, changes);
        Assert.Equal(first, System.Text.Json.JsonSerializer.Serialize(block.Runs[0]));
        Assert.Equal(last, System.Text.Json.JsonSerializer.Serialize(block.Runs[^1]));
        Assert.All(originalIds, id => Assert.Single(block.Runs, run => run.Id == id));
        Assert.Equal(block.Runs.Count, block.Runs.Select(run => run.Id).Distinct().Count());
        var after = Sections(document);
        Assert.True(editor.Undo());
        Assert.Equal(before, Sections(document));
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Equal(after, Sections(document));
    }

    [Theory]
    [InlineData(WriteCharacterFormat.Bold)]
    [InlineData(WriteCharacterFormat.Italic)]
    [InlineData(WriteCharacterFormat.Underline)]
    [InlineData(WriteCharacterFormat.StrikeThrough)]
    public void Same_range_toggles_off_after_splitting_and_history_restores_exact_runs(WriteCharacterFormat format)
    {
        var document = NotesDocument.Create();
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "Alpha beta gamma";
        block.Runs = [new NotesTextRun { Text = block.PlainText, Link = "https://example.test/revision", Language = "en-GB" }];
        var editor = new WriteDocumentEditor(document);
        var before = Sections(document);
        var expected = Characters(block);
        editor.SelectBlock(block.Id, 10, 6, 10);

        editor.ToggleSelectionCharacter(format);
        AssertCharacters(expected, block, format, 6, 10, true);
        Assert.True(Marked(editor.ActiveRun!, format));
        var enabled = Sections(document);
        editor.ToggleSelectionCharacter(format);
        AssertCharacters(expected, block, format, 6, 10, false);
        Assert.False(Marked(editor.ActiveRun!, format));
        Assert.Equal("beta", editor.SelectedText);
        var disabled = Sections(document);

        Assert.True(editor.Undo());
        Assert.Equal(enabled, Sections(document));
        Assert.True(editor.Undo());
        Assert.Equal(before, Sections(document));
        Assert.True(editor.Redo());
        Assert.Equal(enabled, Sections(document));
        Assert.True(editor.Redo());
        Assert.Equal(disabled, Sections(document));
    }

    [Theory]
    [InlineData(WriteCharacterFormat.Bold)]
    [InlineData(WriteCharacterFormat.Italic)]
    [InlineData(WriteCharacterFormat.Underline)]
    [InlineData(WriteCharacterFormat.StrikeThrough)]
    public void Document_selection_takes_priority_over_a_previous_block_range(WriteCharacterFormat format)
    {
        var (document, block, editor) = MixedDocument(format);
        var second = NotesBlock.CreateParagraph();
        second.PlainText = "Tail";
        second.Runs = [new NotesTextRun { Text = second.PlainText }];
        second.Order = block.Order + 1;
        document.Sections[0].Pages[0].Blocks.Add(second);
        var firstExpected = Characters(block);
        var secondExpected = Characters(second);
        var before = Sections(document);
        editor.SelectBlock(block.Id, 9, 4, 9);
        editor.SetDocumentCaret(block.Id, 1);
        editor.SetDocumentCaret(second.Id, 3, extendSelection: true);
        var anchor = editor.DocumentAnchor;
        var caret = editor.DocumentCaret;
        var selectedText = editor.SelectedDocumentText;

        editor.ToggleSelectionCharacter(format);

        AssertCharacters(firstExpected, block, format, 1, block.PlainText.Length, true);
        AssertCharacters(secondExpected, second, format, 0, 3, true);
        Assert.Equal(anchor, editor.DocumentAnchor);
        Assert.Equal(caret, editor.DocumentCaret);
        Assert.Equal(selectedText, editor.SelectedDocumentText);
        Assert.True(editor.Undo());
        Assert.Equal(before, Sections(document));
        Assert.False(editor.CanUndo);
    }

    [Theory]
    [InlineData(WriteCharacterFormat.Bold)]
    [InlineData(WriteCharacterFormat.Italic)]
    [InlineData(WriteCharacterFormat.Underline)]
    [InlineData(WriteCharacterFormat.StrikeThrough)]
    public async Task Selected_marks_and_stable_ids_survive_owner_save_and_fresh_repository_reopen(WriteCharacterFormat format)
    {
        using var paths = new RangeTestPaths();
        await using var diagnostics = new ProductionDiagnostics(paths);
        var repository = new NotesRepository(paths, new NotesDocumentValidator(), diagnostics);
        var (document, block, editor) = MixedDocument(format);
        await repository.SaveAsync(document, "Before selected formatting", CancellationToken.None);
        var before = Sections(document);
        var expected = Characters(block);
        editor.SelectBlock(block.Id, 9, 4, 9);
        editor.ToggleSelectionCharacter(format);
        AssertCharacters(expected, block, format, 4, 9, true);
        var formatted = Sections(document);
        Assert.True(editor.Undo());
        Assert.Equal(before, Sections(document));
        Assert.True(editor.Redo());
        Assert.Equal(formatted, Sections(document));
        await repository.SaveAsync(document, "Selected formatting", CancellationToken.None);

        var freshRepository = new NotesRepository(paths, new NotesDocumentValidator(), diagnostics);
        var reopened = await freshRepository.LoadAsync(document.Id, CancellationToken.None);
        Assert.NotNull(reopened);
        Assert.Equal(document.Id, reopened!.Id);
        Assert.Equal(document.Version, reopened.Version);
        Assert.Equal(Sections(document), Sections(reopened));
        var reopenedBlock = reopened.Sections[0].Pages[0].Blocks.Single(item => item.Id == block.Id);
        AssertCharacters(expected, reopenedBlock, format, 4, 9, true);
        var versions = await freshRepository.GetVersionsAsync(document.Id, CancellationToken.None);
        Assert.Equal(2, versions.Count);
        var latest = versions.Single(version => version.Version == document.Version);
        var historical = await freshRepository.LoadVersionAsync(document.Id, latest.VersionId, CancellationToken.None);
        Assert.NotNull(historical);
        Assert.Equal(Sections(document), Sections(historical!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task History_identity_uses_the_committed_timestamp_when_current_file_time_changes(bool previousHistoryMissing)
    {
        using var paths = new RangeTestPaths();
        await using var diagnostics = new ProductionDiagnostics(paths);
        var repository = new NotesRepository(paths, new NotesDocumentValidator(), diagnostics);
        var document = NotesDocument.Create("Before metadata-only change");
        var first = await repository.SaveAsync(document, "First revision", CancellationToken.None);
        var original = Assert.Single(await repository.GetVersionsAsync(document.Id, CancellationToken.None));
        var firstBytes = await File.ReadAllBytesAsync(first.CurrentPath);
        Assert.Equal(firstBytes, await File.ReadAllBytesAsync(first.VersionPath));
        var metaPath = Path.Combine(Path.GetDirectoryName(first.VersionPath)!, original.VersionId + ".meta.json");
        var metaBytes = await File.ReadAllBytesAsync(metaPath);
        if (previousHistoryMissing)
        {
            File.Delete(first.VersionPath);
            File.Delete(metaPath);
        }
        // Deterministically separate the filesystem timestamp from the saved revision;
        // elapsed scheduler or disk timing must not decide whether a duplicate appears.
        File.SetLastWriteTimeUtc(first.CurrentPath, first.SavedAt.UtcDateTime.AddDays(1));
        Assert.NotEqual(first.SavedAt.UtcDateTime.Date, File.GetLastWriteTimeUtc(first.CurrentPath).Date);
        document.Title = "After metadata-only change";
        var second = await repository.SaveAsync(document, "Second revision", CancellationToken.None);

        var fresh = new NotesRepository(paths, new NotesDocumentValidator(), diagnostics);
        var versions = await fresh.GetVersionsAsync(document.Id, CancellationToken.None);
        Assert.Equal(2, versions.Count);
        var retained = Assert.Single(versions, item => item.Version == first.Version);
        Assert.Equal(original.VersionId, retained.VersionId);
        Assert.Equal(original.CreatedAt, retained.CreatedAt);
        Assert.Equal(original.SizeBytes, retained.SizeBytes);
        Assert.Equal(original.Sha256, retained.Sha256);
        Assert.Equal(firstBytes, await File.ReadAllBytesAsync(first.VersionPath));
        if (!previousHistoryMissing) Assert.Equal(metaBytes, await File.ReadAllBytesAsync(metaPath));
        var oldDocument = await fresh.LoadVersionAsync(document.Id, retained.VersionId, CancellationToken.None);
        Assert.NotNull(oldDocument);
        Assert.Equal(document.Id, oldDocument!.Id);
        Assert.Equal(first.Version, oldDocument.Version);
        Assert.Equal("Before metadata-only change", oldDocument.Title);
        var current = await fresh.LoadAsync(document.Id, CancellationToken.None);
        Assert.NotNull(current);
        Assert.Equal(document.Id, current!.Id);
        Assert.Equal(second.Version, current.Version);
        Assert.Equal(document.Title, current.Title);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conflicting_previous_history_is_preserved_and_blocks_the_next_publication(bool corruptMetadata)
    {
        using var paths = new RangeTestPaths();
        await using var diagnostics = new ProductionDiagnostics(paths);
        var repository = new NotesRepository(paths, new NotesDocumentValidator(), diagnostics);
        var document = NotesDocument.Create("Known-good current revision");
        var first = await repository.SaveAsync(document, "First revision", CancellationToken.None);
        var version = Assert.Single(await repository.GetVersionsAsync(document.Id, CancellationToken.None));
        var currentBytes = await File.ReadAllBytesAsync(first.CurrentPath);
        var metaPath = Path.Combine(Path.GetDirectoryName(first.VersionPath)!, version.VersionId + ".meta.json");
        var conflictPath = corruptMetadata ? metaPath : first.VersionPath;
        if (corruptMetadata)
        {
            var metadata = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(metaPath))!;
            metadata["sha256"] = new string('0', 64);
            await File.WriteAllTextAsync(metaPath, metadata.ToJsonString());
        }
        else await File.WriteAllTextAsync(first.VersionPath, "{ conflicting historical bytes }");
        var conflictingBytes = await File.ReadAllBytesAsync(conflictPath);
        File.SetLastWriteTimeUtc(first.CurrentPath, first.SavedAt.UtcDateTime.AddDays(1));
        document.Title = "Unpublished edit";

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.SaveAsync(document, "Must refuse", CancellationToken.None));

        Assert.Equal(first.Version, document.Version);
        Assert.Equal(currentBytes, await File.ReadAllBytesAsync(first.CurrentPath));
        Assert.Equal(conflictingBytes, await File.ReadAllBytesAsync(conflictPath));
        var fresh = new NotesRepository(paths, new NotesDocumentValidator(), diagnostics);
        var current = await fresh.LoadAsync(document.Id, CancellationToken.None);
        Assert.NotNull(current);
        Assert.Equal(document.Id, current!.Id);
        Assert.Equal(first.Version, current.Version);
        Assert.Equal("Known-good current revision", current.Title);
    }

    private static (NotesDocument Document, NotesBlock Block, WriteDocumentEditor Editor) MixedDocument(WriteCharacterFormat format)
    {
        var document = NotesDocument.Create("Mixed revision notes");
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "AAOne Two ZZ";
        block.Runs = new[] { "AA", "One ", "Two ", "ZZ" }.Select((text, index) => new NotesTextRun
        {
            Text = text, FontFamily = index % 2 == 0 ? "Aptos" : "Montserrat", FontSize = 12 + index,
            Foreground = index % 2 == 0 ? "#FF112233" : "#FF445566", Background = "#FFEEDDCC",
            Link = "https://example.test/revision/" + index, Language = index % 2 == 0 ? "en-GB" : "fr-FR"
        }).ToList();
        var other = (WriteCharacterFormat)(((int)format + 1) % 4);
        SetMark(block.Runs[0], other, true);
        SetMark(block.Runs[1], other, true);
        SetMark(block.Runs[2], format, true);
        SetMark(block.Runs[3], other, true);
        return (document, block, new WriteDocumentEditor(document));
    }

    private static string Sections(NotesDocument document) => System.Text.Json.JsonSerializer.Serialize(document.Sections);
    private sealed record Character(char Text, string Font, double Size, bool Bold, bool Italic, bool Underline,
        bool Strike, string Foreground, string Background, string? Link, string? Language);
    private static Character[] Characters(NotesBlock block) => block.Runs.SelectMany(run => run.Text.Select(text =>
        new Character(text, run.FontFamily, run.FontSize, run.Bold, run.Italic, run.Underline,
            run.StrikeThrough, run.Foreground, run.Background, run.Link, run.Language))).ToArray();
    private static void AssertCharacters(Character[] before, NotesBlock block, WriteCharacterFormat format, int start, int end, bool value)
    {
        var expected = before.Select((item, index) => index < start || index >= end ? item : format switch
        {
            WriteCharacterFormat.Bold => item with { Bold = value },
            WriteCharacterFormat.Italic => item with { Italic = value },
            WriteCharacterFormat.Underline => item with { Underline = value },
            WriteCharacterFormat.StrikeThrough => item with { Strike = value },
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        }).ToArray();
        Assert.Equal(expected, Characters(block));
    }
    private static bool Marked(NotesTextRun run, WriteCharacterFormat format) => format switch
    {
        WriteCharacterFormat.Bold => run.Bold, WriteCharacterFormat.Italic => run.Italic,
        WriteCharacterFormat.Underline => run.Underline, WriteCharacterFormat.StrikeThrough => run.StrikeThrough,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
    private static void SetMark(NotesTextRun run, WriteCharacterFormat format, bool value)
    {
        switch (format)
        {
            case WriteCharacterFormat.Bold: run.Bold = value; break;
            case WriteCharacterFormat.Italic: run.Italic = value; break;
            case WriteCharacterFormat.Underline: run.Underline = value; break;
            case WriteCharacterFormat.StrikeThrough: run.StrikeThrough = value; break;
        }
    }
    private sealed class RangeTestPaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "write-range-tests-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "missing.json");
        public RangeTestPaths() { Directory.CreateDirectory(DataDirectory); Directory.CreateDirectory(LogsDirectory); }
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, recursive: true); }
    }
}
