using System.Text.Json;
using System.Text.Json.Nodes;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.Tests;

/// <summary>
/// Icon identity for boards, sections and pages, ported from the AppFlowy model the owner showed on
/// 4 November 2026. Canonical v2 lists <c>Icon?</c> on BoardPage (line 9423) and Board (line 9421), and
/// UX27-BOARD-012 requires "an actual page/section icon" in each tree row - pages had no such field at
/// all, so every page drew the same glyph. These tests pin the data model, the rejection of nonsense, and
/// the two properties the owner's boards depend on: an icon change never changes stable identity, and a
/// board written before the field existed still opens unchanged.
/// </summary>
public sealed class HavenRichIconPortTests
{
    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "boards-icon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task Chosen_page_icon_survives_save_reopen_without_touching_any_identity()
    {
        var root = TempDirectory();
        try
        {
            var boardPath = Path.Combine(root, "Law.9to1board");
            string pageId;
            string sectionId;
            Guid documentId;

            await using (var session = await RichBoardSession.CreateNewAsync(store: new JsonFileHavenBoardStore(root), title: "Law"))
            {
                await session.SaveAsAsync(boardPath);
                documentId = session.Document.DocumentId;
                sectionId = session.Rich.Sections[0].Id;
                pageId = session.Rich.Sections[0].Pages[0].Id;

                await session.MutateAsync(rich =>
                {
                    HavenRichNotesOps.SetPageIconEmoji(rich, pageId, "📚", "#22aa55");
                    HavenRichNotesOps.SetSectionIconEmoji(rich, sectionId, "⚖️", null);
                    HavenRichNotesOps.SetBoardIcon(rich, "folder", null, "#101010");
                });
                await session.SaveAsync();
            }

            await using (var reopened = await RichBoardSession.OpenAtPathAsync(new JsonFileHavenBoardStore(root), boardPath))
            {
                var page = reopened.Rich.Sections[0].Pages[0];
                var section = reopened.Rich.Sections[0];
                Assert.Equal(pageId, page.Id);
                Assert.Equal(sectionId, section.Id);
                Assert.Equal(documentId, reopened.Document.DocumentId);
                Assert.Equal(HavenRichSectionIcons.Emoji, page.IconKey);
                Assert.Equal("📚", page.IconEmoji);
                Assert.Equal("#22AA55", page.IconColor);
                Assert.Equal("⚖️", section.IconEmoji);
                Assert.Equal("folder", reopened.Rich.IconKey);
                Assert.Equal("#101010", reopened.Rich.IconColor);
            }

            // Renaming must not disturb the icon, and changing the icon must not disturb identity -
            // the pair of guarantees canonical line 9417 asks for.
            await using (var editing = await RichBoardSession.OpenAtPathAsync(new JsonFileHavenBoardStore(root), boardPath))
            {
                await editing.MutateAsync(rich =>
                {
                    HavenRichNotesOps.RenamePage(rich, pageId, "Statutory interpretation");
                    HavenRichNotesOps.SetPageIcon(rich, pageId, "study", null);
                });
                await editing.SaveAsync();
            }

            await using (var after = await RichBoardSession.OpenAtPathAsync(new JsonFileHavenBoardStore(root), boardPath))
            {
                var page = after.Rich.Sections[0].Pages[0];
                Assert.Equal(pageId, page.Id);
                Assert.Equal("Statutory interpretation", page.Title);
                Assert.Equal("study", page.IconKey);
                Assert.Null(page.IconEmoji);
                Assert.Null(page.IconColor);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Remove_returns_a_page_to_the_mark_boards_drew_before_the_icon_field_existed()
    {
        var root = TempDirectory();
        try
        {
            var boardPath = Path.Combine(root, "Notes.9to1board");
            await using (var session = await RichBoardSession.CreateNewAsync(new JsonFileHavenBoardStore(root), "Notes"))
            {
                await session.SaveAsAsync(boardPath);
                var pageId = session.Rich.Sections[0].Pages[0].Id;
                Assert.Equal(HavenRichSectionIcons.PageDefault, session.Rich.Sections[0].Pages[0].IconKey);

                await session.MutateAsync(rich =>
                {
                    HavenRichNotesOps.SetPageIconEmoji(rich, pageId, "🔥", "#FF0000");
                    HavenRichNotesOps.ClearPageIcon(rich, pageId);
                });
                await session.SaveAsync();
            }

            await using (var reopened = await RichBoardSession.OpenAtPathAsync(new JsonFileHavenBoardStore(root), boardPath))
            {
                var page = reopened.Rich.Sections[0].Pages[0];
                Assert.Equal(HavenRichSectionIcons.PageDefault, page.IconKey);
                Assert.Null(page.IconEmoji);
                Assert.Null(page.IconColor);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_board_saved_without_the_icon_fields_opens_with_todays_defaults()
    {
        // Proves the additive claim instead of asserting it: strip every icon property from a real saved
        // document, exactly as a file written before this change would look, and open it.
        var root = TempDirectory();
        try
        {
            var boardPath = Path.Combine(root, "Legacy.9to1board");
            await using (var session = await RichBoardSession.CreateNewAsync(new JsonFileHavenBoardStore(root), "Legacy"))
            {
                await session.SaveAsAsync(boardPath);
                await session.MutateAsync(rich => HavenRichNotesOps.SetPageIconEmoji(rich,
                    rich.Sections[0].Pages[0].Id, "📚", "#22AA55"));
                await session.SaveAsync();
            }

            var json = JsonNode.Parse(await File.ReadAllTextAsync(boardPath))!.AsObject();
            StripIconProperties(json);
            var legacyPath = Path.Combine(root, "Legacy-stripped.9to1board");
            await File.WriteAllTextAsync(legacyPath, json.ToJsonString());

            await using var reopened = await RichBoardSession.OpenAtPathAsync(new JsonFileHavenBoardStore(root), legacyPath);
            var page = reopened.Rich.Sections[0].Pages[0];
            Assert.Equal(HavenRichSectionIcons.PageDefault, page.IconKey);
            Assert.Null(page.IconEmoji);
            Assert.Null(reopened.Rich.IconKey);
            Assert.Equal("Legacy", reopened.Rich.Title);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_page_that_had_a_suggestion_cannot_keep_generated_geometry_under_an_emoji()
    {
        var notes = HavenRichNotes.Create("Study");
        var section = notes.Sections[0];
        HavenRichNotesOps.SetSectionGeneratedIcon(notes, section.Id,
            new HavenRichSectionGeneratedIcon { Shapes = [new HavenRichSectionIconCircle(12, 12, 8)] },
            "#112233");
        Assert.Equal(HavenRichSectionIcons.Generated, section.IconKey);

        HavenRichNotesOps.SetSectionIconEmoji(notes, section.Id, "⚖️", null);
        Assert.Equal(HavenRichSectionIcons.Emoji, section.IconKey);
        Assert.Null(section.GeneratedIcon);
        HavenRichNotesValidator.Validate(notes);

        // And the other direction: switching back to a silhouette must drop the emoji as well.
        HavenRichNotesOps.SetSectionIcon(notes, section.Id, "study", null);
        Assert.Null(section.IconEmoji);
        HavenRichNotesValidator.Validate(notes);
    }

    [Fact]
    public void Recent_icons_are_board_data_most_recent_first_and_bounded()
    {
        var notes = HavenRichNotes.Create("Recents");
        var sectionId = notes.Sections[0].Id;
        var pageId = notes.Sections[0].Pages[0].Id;

        HavenRichNotesOps.SetPageIconEmoji(notes, pageId, "📚", null);
        HavenRichNotesOps.SetSectionIconEmoji(notes, sectionId, "⚖️", null);
        HavenRichNotesOps.SetPageIconEmoji(notes, pageId, "📚", "#22AA55");      // same pick, new colour
        Assert.Equal(new[] { "📚", "⚖️" }, notes.RecentIconEmoji);          // newest first, no duplicate

        for (var index = 0; index < 20; index++)
            HavenRichNotesOps.SetPageIconEmoji(notes, pageId, char.ConvertFromUtf32(0x1F600 + index), null);
        Assert.Equal(HavenRichNotes.RecentIconEmojiLimit, notes.RecentIconEmoji.Count);
        Assert.Equal(notes.RecentIconEmoji.Distinct().Count(), notes.RecentIconEmoji.Count);
        HavenRichNotesValidator.Validate(notes);

        // A silhouette choice records nothing, and removing an icon does not forget the sequence.
        HavenRichNotesOps.SetPageIcon(notes, pageId, "study", null);
        HavenRichNotesOps.ClearPageIcon(notes, pageId);
        Assert.NotEmpty(notes.RecentIconEmoji);
        HavenRichNotesValidator.Validate(notes);

        var tampered = HavenRichNotes.Create("Bad recents");
        tampered.RecentIconEmoji.Add("not an emoji");
        Assert.Throws<InvalidOperationException>(() => HavenRichNotesValidator.Validate(tampered));
    }

    [Fact]
    public void An_icon_slot_never_holds_text_or_a_mismatched_form()
    {
        var notes = HavenRichNotes.Create("Guard");
        var sectionId = notes.Sections[0].Id;
        var pageId = notes.Sections[0].Pages[0].Id;

        Assert.Throws<ArgumentException>(() =>
            HavenRichNotesOps.SetPageIconEmoji(notes, pageId, "not an icon", null));
        Assert.Throws<ArgumentException>(() =>
            HavenRichNotesOps.SetSectionIconEmoji(notes, sectionId, "", null));
        Assert.Throws<ArgumentException>(() =>
            HavenRichNotesOps.SetPageIcon(notes, pageId, "spaceship", null));
        Assert.Throws<ArgumentException>(() =>
            HavenRichNotesOps.SetBoardIcon(notes, "emoji", "this is prose", "#FFFFFF"));

        // A hand-edited file must fail the same way, not only the ops that could produce it.
        var tampered = HavenRichNotes.Create("Tampered");
        tampered.Sections[0].Pages[0].IconEmoji = "grade nine revision";
        var error = Assert.Throws<InvalidOperationException>(() => HavenRichNotesValidator.Validate(tampered));
        Assert.Contains("icon key", error.Message, StringComparison.Ordinal);

        var unknownKey = HavenRichNotes.Create("Unknown");
        unknownKey.Sections[0].Pages[0].IconKey = "spaceship";
        Assert.Throws<InvalidOperationException>(() => HavenRichNotesValidator.Validate(unknownKey));

        var boardWithEmojiUnderSilhouette = HavenRichNotes.Create("Mismatch");
        boardWithEmojiUnderSilhouette.IconKey = "folder";
        boardWithEmojiUnderSilhouette.IconEmoji = "📚";
        Assert.Throws<InvalidOperationException>(() => HavenRichNotesValidator.Validate(boardWithEmojiUnderSilhouette));
    }

    [Theory]
    [InlineData("📚", true)]                        // single pictograph
    [InlineData("⚖️", true)]                        // scales with its emoji presentation selector
    [InlineData("👋🏽", true)]                        // skin-tone modifier
    [InlineData("👨‍👩‍👧", true)]                     // ZWJ family
    [InlineData("🇬🇧", true)]                        // regional-indicator flag
    [InlineData("🏴󠁧󠁢󠁥󠁮󠁧󠁿", true)]           // subdivision flag built from tag characters
    [InlineData("1⃣", true)]                        // keycap
    [InlineData("hello", false)]
    [InlineData("", false)]
    [InlineData("a", false)]
    [InlineData("📚📚", false)]                     // juxtaposition is not one sequence
    [InlineData("⚖hello", false)]
    public void The_icon_slot_grammar_accepts_sequences_and_refuses_prose(string value, bool expected) =>
        Assert.Equal(expected, HavenRichSectionIcons.IsPlausibleEmojiSequence(value));

    private static void StripIconProperties(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var name in new[] { "iconKey", "iconColor", "iconEmoji", "IconKey", "IconColor", "IconEmoji" })
                    obj.Remove(name);
                foreach (var child in obj) StripIconProperties(child.Value);
                break;
            case JsonArray array:
                foreach (var child in array) StripIconProperties(child);
                break;
        }
    }
}
