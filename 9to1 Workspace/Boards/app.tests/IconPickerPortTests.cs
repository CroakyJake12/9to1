using Avalonia.Automation;
using Avalonia.Controls;
using CakeOS.Apps.Boards.Contract;
using Haven.UI.Emoji;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

/// <summary>
/// The icon picker the owner asked to port from AppFlowy on 4 November 2026, checked as a user flow rather
/// than as a data structure: open it from a page, choose something, see it in the tree, and have the board
/// remember it. The places where the reference application is deliberately not followed are asserted here
/// too, so they stay different: an empty search says so instead of showing a heading over nothing, the
/// Upload tab is absent rather than shown and inert, and the grid lists one row per emoji instead of every
/// skin-tone spelling.
///
/// Every call in this class funnels through <see cref="TestUiThread"/> because the shared UI objects the
/// picker builds are thread-affine in this assembly.
/// </summary>
public sealed class IconPickerPortTests
{
    [Fact]
    public async Task Opening_from_a_page_offers_emoji_and_icons_and_no_suggestion_tab()
    {
        // A unique path, because InMemoryRichBoardSession keeps a process-wide static store keyed by path:
        // opening the shared "default" key would hand this test another test's board, complete with the
        // recent icons it recorded. That coupling is the store's own behaviour, not the product's.
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync($"memory://boards/icon-port-{Guid.NewGuid():N}");
        var vm = new BoardsViewModel(session);
        EnableEdit(vm);
        var (section, page) = FirstPage(session);

        TestUiThread.Run(() => vm.OpenPageIconPicker(section.Id, page.Id));
        Assert.True(vm.Get("IsSectionIconPickerOpen") as bool?);
        Assert.Equal("page", vm.Get("IconPickerTargetKind"));
        var tabs = Tabs(vm);
        Assert.Equal(new[] { "emoji", "icon", "suggest" }, tabs.Select(tab => tab.Key));
        // The reference popover opens on the emoji tab; suggestions are a section-name flow, so a page
        // target must not be offered a control that cannot do anything.
        Assert.True(tabs.Single(tab => tab.Key == "emoji").IsSelected);
        Assert.False(tabs.Single(tab => tab.Key == "suggest").IsEnabled);

        var sections = EmojiSections(vm);
        Assert.NotEmpty(sections);
        // A board with no icon history must not invent a Recent heading.
        Assert.NotEqual(IconPickerEmojiModel.RecentTitle, sections[0].Heading);
        Assert.Contains(sections, group => group.Heading.StartsWith("Smileys & Emotion", StringComparison.Ordinal));
        Assert.All(sections, group => Assert.True(group.Items.Count <= IconPickerEmojiModel.SectionCap,
            $"a category section must stay bounded, saw {group.Items.Count}"));
        // When a category is longer than what is shown, the heading has to say so.
        Assert.Contains(sections, group => group.Heading.Contains("narrow it down", StringComparison.Ordinal));

        // Browsing offers one row per emoji, never two spellings of the same one.
        var waving = sections.SelectMany(group => group.Items).Where(item => item.Name == "waving hand").ToArray();
        Assert.Single(waving);
        Assert.Equal("👋", waving[0].Character);
        Assert.DoesNotContain(sections.SelectMany(group => group.Items), item => item.Character == "👋🏻");

        vm.Dispose();
    }

    [Fact]
    public async Task Choosing_an_emoji_for_a_page_stores_it_shows_it_in_the_tree_and_is_remembered()
    {
        // A unique path, because InMemoryRichBoardSession keeps a process-wide static store keyed by path:
        // opening the shared "default" key would hand this test another test's board, complete with the
        // recent icons it recorded. That coupling is the store's own behaviour, not the product's.
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync($"memory://boards/icon-port-{Guid.NewGuid():N}");
        var vm = new BoardsViewModel(session);
        EnableEdit(vm);
        var (section, page) = FirstPage(session);

        TestUiThread.Run(() => vm.OpenPageIconPicker(section.Id, page.Id));
        Ui(vm, "ApplyEmojiIconChoice", "📚");

        Assert.Equal(HavenRichSectionIcons.Emoji, page.IconKey);
        Assert.Equal("📚", page.IconEmoji);
        Assert.Contains("📚", session.Document.RecentIconEmoji);
        // A hand-picked emoji is a finished decision, so the popover closes as the reference control does.
        Assert.False(vm.Get("IsSectionIconPickerOpen") as bool?);

        TestUiThread.Run(() =>
        {
            var nav = new StackPanel();
            NavBuilder.Rebuild(nav, vm);
            var drawn = TestControlTree.Of<TextBlock>(nav).Single(text => text.Text == "📚");
            Assert.Equal("books icon", AutomationProperties.GetName(drawn));
        });

        // Reopening shows the choice as selected, which is what makes the grid navigable by keyboard, and
        // the history is board data that survived the round trip.
        TestUiThread.Run(() => vm.OpenPageIconPicker(section.Id, page.Id));
        Assert.True(vm.Get("IconPickerUsesEmoji") as bool?);
        Assert.True(vm.Get("CanRemoveIcon") as bool?);
        var sections = EmojiSections(vm);
        Assert.Equal(IconPickerEmojiModel.RecentTitle, sections[0].Heading);
        Assert.Contains(sections[0].Items, item => item.Character == "📚" && item.IsSelected);

        vm.Dispose();
    }

    [Fact]
    public async Task Remove_returns_the_page_to_the_mark_boards_drew_before_the_icon_existed()
    {
        // A unique path, because InMemoryRichBoardSession keeps a process-wide static store keyed by path:
        // opening the shared "default" key would hand this test another test's board, complete with the
        // recent icons it recorded. That coupling is the store's own behaviour, not the product's.
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync($"memory://boards/icon-port-{Guid.NewGuid():N}");
        var vm = new BoardsViewModel(session);
        EnableEdit(vm);
        var (section, page) = FirstPage(session);
        TestUiThread.Run(() => session.SetPageIconEmojiAsync(page.Id, "🔥", "#FF0000").AsTask().GetAwaiter().GetResult());

        TestUiThread.Run(() => vm.OpenPageIconPicker(section.Id, page.Id));
        Assert.True(vm.Get("CanRemoveIcon") as bool?);
        Ui(vm, "RemoveIconPickerIcon");

        Assert.Equal(HavenRichSectionIcons.PageDefault, page.IconKey);
        Assert.Null(page.IconEmoji);
        Assert.Null(page.IconColor);
        Assert.False(vm.Get("CanRemoveIcon") as bool?, "there is nothing left to remove");
        Assert.Contains(page, section.Pages);   // removing an icon is not deleting the page

        vm.Dispose();
    }

    [Fact]
    public async Task An_emoji_search_that_answers_nothing_says_so()
    {
        // A unique path, because InMemoryRichBoardSession keeps a process-wide static store keyed by path:
        // opening the shared "default" key would hand this test another test's board, complete with the
        // recent icons it recorded. That coupling is the store's own behaviour, not the product's.
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync($"memory://boards/icon-port-{Guid.NewGuid():N}");
        var vm = new BoardsViewModel(session);
        EnableEdit(vm);
        var (section, page) = FirstPage(session);
        TestUiThread.Run(() => vm.OpenPageIconPicker(section.Id, page.Id));

        Assert.True(TestUiThread.Run(() => vm.TrySetValue("SectionIconPickerSearch", "open book")));
        Assert.Contains(EmojiSections(vm).SelectMany(group => group.Items), item => item.Name == "open book");
        Assert.False(vm.Get("EmojiIconNoMatches") as bool?);

        // The reference picker's failure state: a heading with nothing under it and no message. Boards
        // reports the miss instead, which is what tells a reader the search actually ran.
        Assert.True(TestUiThread.Run(() => vm.TrySetValue("SectionIconPickerSearch", "scales of justice")));
        Assert.False(vm.Get("HasEmojiIconResults") as bool?);
        Assert.True(vm.Get("EmojiIconNoMatches") as bool?);
        Assert.Contains("scales of justice", Assert.IsType<string>(vm.Get("EmojiIconNoMatchesMessage")));
        Assert.Empty(EmojiSections(vm));

        vm.Dispose();
    }

    [Fact]
    public async Task The_offered_emoji_can_be_searched_for_by_its_own_name_case_insensitively()
    {
        // A unique path, because InMemoryRichBoardSession keeps a process-wide static store keyed by path:
        // opening the shared "default" key would hand this test another test's board, complete with the
        // recent icons it recorded. That coupling is the store's own behaviour, not the product's.
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync($"memory://boards/icon-port-{Guid.NewGuid():N}");
        var vm = new BoardsViewModel(session);
        EnableEdit(vm);
        var (section, page) = FirstPage(session);
        TestUiThread.Run(() => vm.OpenPageIconPicker(section.Id, page.Id));

        Assert.True(TestUiThread.Run(() => vm.TrySetValue("SectionIconPickerSearch", "BALANCE")));
        var scales = EmojiSections(vm).SelectMany(group => group.Items).ToArray();
        Assert.NotEmpty(scales);
        Assert.All(scales, item => Assert.Contains("scale", item.Name, StringComparison.OrdinalIgnoreCase));

        vm.Dispose();
    }

    [Fact]
    public async Task A_skin_tone_changes_what_the_grid_offers_and_what_gets_stored()
    {
        // A unique path, because InMemoryRichBoardSession keeps a process-wide static store keyed by path:
        // opening the shared "default" key would hand this test another test's board, complete with the
        // recent icons it recorded. That coupling is the store's own behaviour, not the product's.
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync($"memory://boards/icon-port-{Guid.NewGuid():N}");
        var vm = new BoardsViewModel(session);
        EnableEdit(vm);
        var (section, page) = FirstPage(session);
        TestUiThread.Run(() => vm.OpenPageIconPicker(section.Id, page.Id));
        Assert.True(TestUiThread.Run(() => vm.TrySetValue("SectionIconPickerSearch", "waving hand")));

        var plain = EmojiSections(vm).SelectMany(group => group.Items).Single(item => item.Name == "waving hand");
        Assert.Equal("👋", plain.Character);

        Ui(vm, "ApplyIconSkinTone", "🏽");
        var toned = EmojiSections(vm).SelectMany(group => group.Items).Single(item => item.Name == "waving hand");
        Assert.Equal("👋🏽", toned.Character);
        Assert.Equal("🏽", vm.Get("SelectedIconSkinTone"));
        // The tone re-renders the same rows; it does not double the grid with unselected spellings.
        Assert.Single(EmojiSections(vm).SelectMany(group => group.Items), item => item.Name == "waving hand");

        Ui(vm, "ApplyEmojiIconChoice", toned.Character);
        // The tone that is displayed is the tone that is stored, and it is a real Unicode sequence.
        Assert.Equal("👋🏽", page.IconEmoji);
        Assert.True(EmojiCatalogue.Contains(page.IconEmoji));

        vm.Dispose();
    }

    [Fact]
    public async Task Shuffle_keeps_the_picker_open_for_another_roll()
    {
        // A unique path, because InMemoryRichBoardSession keeps a process-wide static store keyed by path:
        // opening the shared "default" key would hand this test another test's board, complete with the
        // recent icons it recorded. That coupling is the store's own behaviour, not the product's.
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync($"memory://boards/icon-port-{Guid.NewGuid():N}");
        var vm = new BoardsViewModel(session);
        EnableEdit(vm);
        var (section, page) = FirstPage(session);
        TestUiThread.Run(() => vm.OpenPageIconPicker(section.Id, page.Id));

        Ui(vm, "PickRandomEmojiIcon");
        // The reference shuffle leaves the popover open so the next roll is one key away.
        Assert.True(vm.Get("IsSectionIconPickerOpen") as bool?);
        Assert.Equal(HavenRichSectionIcons.Emoji, page.IconKey);
        Assert.True(EmojiCatalogue.Contains(page.IconEmoji));
        Assert.Contains(page.IconEmoji!, session.Document.RecentIconEmoji);

        vm.Dispose();
    }

    [Fact]
    public async Task View_mode_offers_no_icon_entry_point_at_all()
    {
        // A unique path, because InMemoryRichBoardSession keeps a process-wide static store keyed by path:
        // opening the shared "default" key would hand this test another test's board, complete with the
        // recent icons it recorded. That coupling is the store's own behaviour, not the product's.
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync($"memory://boards/icon-port-{Guid.NewGuid():N}");
        var vm = new BoardsViewModel(session);
        EnableEdit(vm);
        var (section, page) = FirstPage(session);
        TestUiThread.Run(() => vm.OpenPageIconPicker(section.Id, page.Id));
        Ui(vm, "ApplyEmojiIconChoice", "📚");
        Ui(vm, "CloseSectionIconPicker");
        Ui(vm, "ToggleEditView");
        Assert.False(vm.IsEditMode, "the board must be in view mode for this case");

        TestUiThread.Run(() => vm.OpenPageIconPicker(section.Id, page.Id));
        Assert.False(vm.Get("IsSectionIconPickerOpen") as bool?);

        TestUiThread.Run(() =>
        {
            var nav = new StackPanel();
            NavBuilder.Rebuild(nav, vm);
            Assert.DoesNotContain(TestControlTree.Of<Button>(nav),
                button => AutomationProperties.GetName(button) == "Choose page icon");
            // The chosen icon is still displayed in view mode - only the entry point is withheld.
            Assert.Contains(TestControlTree.Of<TextBlock>(nav), text => text.Text == "📚");
        });

        vm.Dispose();
    }

    [Fact]
    public async Task The_emoji_that_the_picker_stores_is_always_something_the_board_can_draw()
    {
        // A unique path, because InMemoryRichBoardSession keeps a process-wide static store keyed by path:
        // opening the shared "default" key would hand this test another test's board, complete with the
        // recent icons it recorded. That coupling is the store's own behaviour, not the product's.
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync($"memory://boards/icon-port-{Guid.NewGuid():N}");
        var vm = new BoardsViewModel(session);
        EnableEdit(vm);
        var (section, page) = FirstPage(session);
        TestUiThread.Run(() => vm.OpenPageIconPicker(section.Id, page.Id));

        // Something that is not in the catalogue is refused rather than stored as an icon.
        Ui(vm, "ApplyEmojiIconChoice", "grade nine");
        Assert.Equal(HavenRichSectionIcons.PageDefault, page.IconKey);
        Assert.Null(page.IconEmoji);
        Ui(vm, "ApplyEmojiIconChoice", "a📚");
        Assert.Equal(HavenRichSectionIcons.PageDefault, page.IconKey);

        vm.Dispose();
    }

    /// <summary>
    /// The Board is the third target for the same popover. Canonical v2 line 9421 lists Icon? among the Board
    /// fields, so this is a specified field with a real entry point in the Board menu, not a preference; and
    /// because a Board icon is optional, the popover must not offer Remove before the owner has chosen one.
    /// </summary>
    [Fact]
    public async Task Opening_from_the_Board_targets_the_Board_and_offers_no_suggestion_tab()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync($"memory://boards/icon-board-{Guid.NewGuid():N}");
        var vm = new BoardsViewModel(session);
        EnableEdit(vm);

        Ui(vm, "OpenBoardIconPicker");
        Assert.True(vm.Get("IsSectionIconPickerOpen") as bool?, "the Board menu opens the shared popover");
        Assert.Equal("board", vm.Get("IconPickerTargetKind"));
        Assert.Contains("Board appearance", vm.Get("SectionIconPickerTitle") as string);
        var tabs = Tabs(vm);
        Assert.False(tabs.Single(tab => tab.Key == "suggest").IsEnabled,
            "suggestions come from a section name today, so a Board target must not be offered that tab");
        Assert.False(vm.Get("CanUseIconPickerSuggest") as bool?);
        Assert.False(vm.Get("CanRemoveIcon") as bool?,
            "a Board with no chosen icon has nothing to remove, so Remove must not sit there doing nothing");
        Assert.NotEmpty(EmojiSections(vm));
        vm.Dispose();
    }

    [Fact]
    public async Task Choosing_an_emoji_for_the_Board_stores_it_remembers_it_and_Remove_leaves_no_icon()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync($"memory://boards/icon-board-{Guid.NewGuid():N}");
        var vm = new BoardsViewModel(session);
        EnableEdit(vm);

        Ui(vm, "OpenBoardIconPicker");
        Ui(vm, "ApplyEmojiIconChoice", "📚");
        Assert.Equal(HavenRichSectionIcons.Emoji, session.Document.IconKey);
        Assert.Equal("📚", session.Document.IconEmoji);
        Assert.True(session.Document.RecentIconEmoji.Contains("📚"),
            "the Board records what its owner used");
        Assert.False(vm.Get("IsSectionIconPickerOpen") as bool?, "a chosen icon is a finished decision");
        Assert.True((vm.Get("StatusText") as string)?.Contains("Board icon set to") == true,
            "the status has to name the entity that changed, not just the icon");

        Ui(vm, "OpenBoardIconPicker");
        Assert.True(vm.Get("CanRemoveIcon") as bool?);
        Ui(vm, "RemoveIconPickerIcon");
        Assert.Null(session.Document.IconKey);
        Assert.Null(session.Document.IconEmoji);
        Assert.Null(session.Document.IconColor);
        Assert.True((vm.Get("StatusText") as string)?.Contains("Board icon removed") == true,
            "removing the Board icon has to say so");
        vm.Dispose();
    }

    [Fact]
    public async Task Tinting_a_Board_with_no_icon_keeps_the_maintained_mark_and_tinting_an_emoji_Board_sets_the_tile()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync($"memory://boards/icon-board-{Guid.NewGuid():N}");
        var vm = new BoardsViewModel(session);
        EnableEdit(vm);

        // A colour needs something to tint, and a Board that has never chosen an icon already draws the
        // maintained mark on its card, so tinting must keep that shape rather than swap it.
        Ui(vm, "OpenBoardIconPicker");
        Ui(vm, "ApplySectionIconColor", "#7026D9");
        Assert.Equal("folder", session.Document.IconKey);
        Assert.Equal("#7026D9", session.Document.IconColor);

        Ui(vm, "OpenBoardIconPicker");
        Ui(vm, "ApplyEmojiIconChoice", "📚");
        Ui(vm, "OpenBoardIconPicker");
        Ui(vm, "ApplySectionIconColor", "#187B72");
        Assert.Equal(HavenRichSectionIcons.Emoji, session.Document.IconKey);
        Assert.Equal("📚", session.Document.IconEmoji);
        // An emoji cannot be recoloured, so the stored colour is the tile behind it.
        Assert.Equal("#187B72", session.Document.IconColor);
        vm.Dispose();
    }

    /// <summary>
    /// The Board's icon is only useful if the landing card reads the saved document rather than a fixed glyph.
    /// These are the presentation members the card binds, checked against the record a card is built from.
    /// </summary>
    [Fact]
    public void A_Board_card_presents_the_icon_the_board_file_carries()
    {
        var marked = new RecentBoardCard("b1", "/tmp/b1.9to1board", "Law", "Criminal Law",
            DateTimeOffset.UtcNow, true, IconKey: HavenRichSectionIcons.Emoji, IconEmoji: "📚");
        Assert.True(marked.IconIsEmoji);
        Assert.False(marked.IconIsVector);
        Assert.Equal("📚", marked.IconGlyph);
        Assert.Equal("books board icon", marked.IconAccessibleName);

        var tinted = new RecentBoardCard("b2", "/tmp/b2.9to1board", "Maths", "Pure",
            DateTimeOffset.UtcNow, true, IconKey: "calendar", IconColor: "#7026D9");
        Assert.True(tinted.IconIsVector);
        // The owner's colour travels in the same catalog reference the page header publishes.
        Assert.Equal("icon:calendar|#7026D9", tinted.IconSource);

        var unmarked = new RecentBoardCard("b3", "/tmp/b3.9to1board", "Old", "Nothing",
            DateTimeOffset.UtcNow, true);
        Assert.True(unmarked.IconIsVector);
        // A Board that never chose an icon keeps the mark its card has always drawn.
        Assert.Equal("icon:folder", unmarked.IconSource);
        Assert.Equal(string.Empty, unmarked.IconGlyph);
        Assert.Equal("Board", unmarked.IconAccessibleName);
    }

    private static (RichBoardSection Section, RichBoardPage Page) FirstPage(InMemoryRichBoardSession session)
    {
        var section = session.Document.Sections[0];
        return (section, section.Pages[0]);
    }

    private static void EnableEdit(BoardsViewModel vm) => TestUiThread.Run(() =>
    {
        if (!vm.IsEditMode) vm.DispatchAsync("ToggleEditView", null).GetAwaiter().GetResult();
        Assert.True(vm.IsEditMode, "the board must be in edit mode to change an icon");
    });

    private static void Ui(BoardsViewModel vm, string command, object? parameter = null) =>
        TestUiThread.Run(() => vm.DispatchAsync(command, parameter).GetAwaiter().GetResult());

    private static IReadOnlyList<BoardsIconTabView> Tabs(BoardsViewModel vm) =>
        Assert.IsAssignableFrom<IReadOnlyList<BoardsIconTabView>>(vm.Get("IconPickerTabs"));

    private static IReadOnlyList<BoardsIconEmojiSectionView> EmojiSections(BoardsViewModel vm) =>
        Assert.IsAssignableFrom<IReadOnlyList<BoardsIconEmojiSectionView>>(vm.Get("EmojiIconSections"));
}
