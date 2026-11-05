using CakeOS.Apps.Boards.Contract;
using Haven.UI.Emoji;

namespace CakeOS.Apps.Boards.App;

/// <summary>
/// The shared icon picker: one popover for boards, sections and pages, in the shape the reference
/// application uses - a tab row, a search field, category-grouped emoji with a skin-tone control and a
/// shuffle, the maintained silhouette family with its colour row, and Remove. It is one implementation
/// rather than a second picker: opening it from a section row, a page row or the page header sets only
/// which entity is the target.
///
/// Two deliberate differences from the reference. The Upload tab is absent, not disabled: an uploaded
/// image as an entity icon needs an attachment route the board does not have yet, and canonical v2 line
/// 9397 requires visible actions to have real implementations rather than dead buttons. And a search with
/// no matches says so - the reference picker leaves a category heading over an empty area and never says
/// anything, which is the one behaviour in that popover that is not worth copying.
/// </summary>
public sealed partial class BoardsViewModel
{
    /// <summary>Opens the same popover for a page, with that page as the target.</summary>
    public void OpenPageIconPicker(string sectionId, string pageId)
    {
        if (!CanEdit || !IsEditMode) return;
        var section = _session.Document.Sections.FirstOrDefault(item => item.Id == sectionId);
        var page = section?.Pages.FirstOrDefault(item => item.Id == pageId);
        if (section is null || page is null) return;
        CancelSectionIconSuggestion();
        _sectionIconPickerSectionId = section.Id;
        _iconPickerPageId = page.Id;
        _generatedSectionIconPreviews = [];
        _sectionIconSearchText = string.Empty;
        Set("SectionIconPickerSearch", string.Empty);
        Set("SectionIconPickerTitle", $"Page appearance · {page.Title}");
        Set("HasGeneratedSectionIconPreview", false);
        Set("GeneratedSectionIconPreviews", Array.Empty<BoardsGeneratedSectionIconPreviewView>());
        Set("IsSectionIconPickerOpen", true);
        RefreshSectionIconPickerChoices();
    }

    private RichBoardSection? IconPickerSection() =>
        _sectionIconPickerSectionId is { } id
            ? _session.Document.Sections.FirstOrDefault(item => item.Id == id)
            : null;

    private RichBoardPage? IconPickerPage() =>
        _iconPickerPageId is { } id
            ? IconPickerSection()?.Pages.FirstOrDefault(item => item.Id == id)
            : null;

    private bool IconPickerTargetsPage => _iconPickerPageId is not null;

    /// <summary>Switches which set of choices the popover is showing. This never writes an icon.</summary>
    private void SelectIconPickerTab(string tabKey)
    {
        if (tabKey is not ("emoji" or "icon" or "suggest")) return;
        if (tabKey == "suggest" && IconPickerTargetsPage) return;
        if (_iconPickerTab == tabKey) return;
        _iconPickerTab = tabKey;
        Set("SectionIconPickerSearch", string.Empty);
        _sectionIconSearchText = string.Empty;
        RefreshSectionIconPickerChoices();
    }

    /// <summary>
    /// Stores an emoji chosen from the grid or the shuffle. The sequence is canonicalised through the
    /// catalogue before it is written, so a pasted or font-supplied spelling is saved as the one Unicode
    /// spells, and something the catalogue does not have is refused rather than stored as an icon.
    /// </summary>
    private async Task ApplyEmojiIconChoiceAsync(string character, bool isRandom, CancellationToken cancellationToken)
    {
        if (!RequireEditMode()) return;
        var entry = EmojiCatalogue.Find(character);
        if (entry is null)
        {
            Set("StatusText", "That is not one of the Unicode emoji Boards can use as an icon.");
            return;
        }
        if (IconPickerTargetsPage)
        {
            var page = IconPickerPage();
            if (page is null) return;
            try
            {
                await _session.SetPageIconEmojiAsync(page.Id, entry.Text, page.IconColor, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidOperationException or ArgumentException)
            {
                Set("StatusText", "The page icon could not be saved. Try again.");
                return;
            }
            Set("StatusText", $"Page icon set to {entry.Name}.");
        }
        else
        {
            var section = IconPickerSection();
            if (section is null) return;
            try
            {
                await _session.SetSectionIconEmojiAsync(section.Id, entry.Text, section.IconColor, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidOperationException or ArgumentException)
            {
                Set("StatusText", "The section icon could not be saved. Try again.");
                return;
            }
            Set("StatusText", $"Section icon set to {entry.Name}.");
        }

        PushDocumentToBindings();
        NavRebuildRequested?.Invoke();
        RequestRebuild();
        // Shuffle keeps the popover open for another roll, exactly as the reference control does; a chosen
        // emoji is a finished decision, so it closes.
        if (isRandom) RefreshSectionIconPickerChoices();
        else CloseSectionIconPicker();
    }

    private async Task ApplyRandomEmojiIconAsync(CancellationToken cancellationToken)
    {
        if (!RequireEditMode()) return;
        var pick = IconPickerEmojiModel.PickRandom(string.IsNullOrEmpty(_iconSkinTone) ? null : _iconSkinTone);
        await ApplyEmojiIconChoiceAsync(pick.Character, isRandom: true, cancellationToken);
    }

    /// <summary>
    /// Sets the skin tone applied to every modifier-capable row. Recent picks are re-rendered through the
    /// same tone, so what the grid shows is what will be stored.
    /// </summary>
    private void ApplyIconSkinTone(string skinToneValue)
    {
        _iconSkinTone = skinToneValue ?? string.Empty;
        RefreshSectionIconPickerChoices();
    }

    private async Task RemovePickerIconAsync(CancellationToken cancellationToken)
    {
        if (!RequireEditMode()) return;
        if (IconPickerTargetsPage)
        {
            var page = IconPickerPage();
            if (page is null) return;
            try
            {
                await _session.ClearPageIconAsync(page.Id, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidOperationException or ArgumentException)
            {
                Set("StatusText", "The page icon could not be removed. Try again.");
                return;
            }
            Set("StatusText", "Page icon removed.");
            RefreshAfterIconWrite();
            return;
        }
        var section = IconPickerSection();
        if (section is null) return;
        if (!await SetSectionIconAsync(section.Id, HavenRichSectionIcons.Default, null, cancellationToken)) return;
        Set("StatusText", "Section icon removed.");
        RefreshAfterIconWrite();
    }

    private async Task ApplyIconColorAsync(string? iconColor, CancellationToken cancellationToken)
    {
        if (!RequireEditMode()) return;
        if (iconColor is not null && !HavenRichSectionIcons.IsSupportedColor(iconColor))
        {
            Set("StatusText", "Choose an available icon color.");
            return;
        }
        if (IconPickerTargetsPage)
        {
            var page = IconPickerPage();
            if (page is null) return;
            try
            {
                if (page.IconKey == HavenRichSectionIcons.Emoji && page.IconEmoji is { Length: > 0 } emoji)
                    await _session.SetPageIconEmojiAsync(page.Id, emoji, iconColor, cancellationToken);
                else
                    await _session.SetPageIconAsync(page.Id, page.IconKey, iconColor, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidOperationException or ArgumentException)
            {
                Set("StatusText", "The page icon color could not be saved. Try again.");
                return;
            }
            Set("StatusText", iconColor is null ? "Page icon follows the theme color." : "Page icon color saved.");
            RefreshAfterIconWrite();
            return;
        }
        var section = IconPickerSection();
        if (section is null) return;
        if (section.IconKey == HavenRichSectionIcons.Emoji && section.IconEmoji is { Length: > 0 } sectionEmoji)
        {
            try
            {
                await _session.SetSectionIconEmojiAsync(section.Id, sectionEmoji, iconColor, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidOperationException or ArgumentException)
            {
                Set("StatusText", "The section icon color could not be saved. Try again.");
                return;
            }
            Set("StatusText", iconColor is null ? "Section icon follows the theme color." : "Section icon color saved.");
            RefreshAfterIconWrite();
            return;
        }
        if (!await SetSectionIconColorAsync(section, iconColor, cancellationToken)) return;
    }

    private void RefreshAfterIconWrite()
    {
        PushDocumentToBindings();
        NavRebuildRequested?.Invoke();
        RequestRebuild();
        RefreshSectionIconPickerChoices();
    }

    /// <summary>
    /// The board's recent icons, read from the projected document. The contract is what records them - in the
    /// real store through <c>HavenRichNotesOps</c>, which re-projects after every write - so the picker's
    /// Recent section and the saved file cannot drift apart.
    /// </summary>
    private IReadOnlyList<string> RecentIconEmoji() => _session.Document.RecentIconEmoji;

    /// <summary>
    /// The icon the target entity currently carries, with whether Remove would do anything. Read through
    /// one place so the popover and the row it opened cannot disagree.
    /// </summary>
    /// <summary>
    /// Publishes the active page's identity icon for the page header, where the reference design shows the
    /// icon to the left of the title. The element itself is always displayed - an icon the owner chose
    /// belongs to the page, not to the editing state - and only its activation is withheld outside Edit mode,
    /// the same rule the tree rows follow.
    /// </summary>
    private void RefreshPageIconState(RichBoardPage page)
    {
        var isEmoji = page.IconKey == HavenRichSectionIcons.Emoji &&
                      HavenRichSectionIcons.IsPlausibleEmojiSequence(page.IconEmoji);
        var emojiName = isEmoji ? EmojiCatalogue.Find(page.IconEmoji)?.Name : null;
        Set("PageIconIsEmoji", isEmoji);
        Set("PageIconIsVector", !isEmoji);
        Set("PageIconGlyph", isEmoji ? page.IconEmoji! : string.Empty);
        Set("PageIconAccessibleName", isEmoji
            ? $"{emojiName ?? "page"} icon for this page"
            : $"{BoardsIcons.SectionIconLabel(page.IconKey)} icon for this page");
        Set("IsPageIconEditable", CanEdit && IsEditMode);
        // The header image is authored as Source="{Binding PageIconSource}" with the catalog form
        // icon:<key>|<colour>. The string form matters: this method runs during the document push, which can
        // happen on a background thread while a save or a reload is in flight, and Boards' icon resolver
        // reads theme brushes that belong to the UI thread. Home already publishes icon: references for the
        // same reason, and the resolver turns them into the tinted silhouette when the surface renders.
        Set("PageIconSource", isEmoji ? null : IconReference(page.IconKey, page.IconColor));
    }

    /// <summary>
    /// The catalog reference for an identity icon, carrying the owner's chosen colour when the entity has one.
    /// Purely textual, so it is safe to publish from any thread and safe to keep in view-model state.
    /// </summary>
    private static string IconReference(string iconKey, string? iconColor) =>
        string.IsNullOrWhiteSpace(iconColor) ? $"icon:{iconKey}" : $"icon:{iconKey}|{iconColor}";

    /// <summary>Opens the shared picker for whichever page is currently displayed.</summary>
    public void OpenActivePageIconPicker()
    {
        if (!CanEdit || !IsEditMode) return;
        OpenPageIconPicker(CurrentSection().Id, CurrentPage().Id);
    }

    private (string Key, string? Emoji, string? Color, bool IsPage, bool HasChosenIcon) CurrentIconSlot()
    {
        if (IconPickerPage() is { } page)
            return (page.IconKey, page.IconEmoji, page.IconColor, true,
                page.IconKey != HavenRichSectionIcons.PageDefault || page.IconColor is not null);
        var section = IconPickerSection() ?? throw new InvalidOperationException("The icon picker has no target.");
        return (section.IconKey, section.IconEmoji, section.IconColor, false,
            section.IconKey != HavenRichSectionIcons.Default || section.GeneratedIcon is not null ||
            section.IconColor is not null);
    }

    /// <summary>
    /// Builds the tab row, the emoji grid for the current query and skin tone, the skin-tone control and the
    /// Remove availability. Called from the existing picker refresh so there is one place that decides what
    /// the popover shows, whichever entity opened it.
    /// </summary>
    private void RefreshIconPickerSharedState()
    {
        if (IconPickerSection() is null) return;
        var slot = CurrentIconSlot();

        Set("IconPickerTargetKind", slot.IsPage ? "page" : "section");
        Set("IsIconPickerEmojiTab", _iconPickerTab == "emoji");
        Set("IsIconPickerIconTab", _iconPickerTab == "icon");
        Set("IsIconPickerSuggestTab", _iconPickerTab == "suggest");
        Set("IconPickerSearchPlaceholder", _iconPickerTab == "emoji"
            ? "Search emoji by name or category"
            : _iconPickerTab == "suggest" ? "Suggested icons appear here" : "Search icons by name");
        Set("EmojiIconNoMatches", IconPickerEmojiModel.HasNoMatches(_sectionIconSearchText));
        _iconPickerTabs = new[]
        {
            new BoardsIconTabView("emoji", "Emoji", _iconPickerTab == "emoji", true),
            new BoardsIconTabView("icon", "Icon", _iconPickerTab == "icon", true),
            new BoardsIconTabView("suggest", "Suggest", _iconPickerTab == "suggest", !slot.IsPage),
        };
        Set("IconPickerTabs", _iconPickerTabs);
        Set("CanUseIconPickerSuggest", !slot.IsPage);

        var built = IconPickerEmojiModel.Sections(_sectionIconSearchText,
                string.IsNullOrEmpty(_iconSkinTone) ? null : _iconSkinTone, RecentIconEmoji())
            .Select(group => new BoardsIconEmojiSectionView(
                group.Heading,
                group.Items.Select(item => new BoardsIconEmojiChoiceView(
                    item.Character, item.Name,
                    string.Equals(item.Character, slot.Emoji, StringComparison.Ordinal))).ToArray()))
            .ToArray();
        _iconPickerEmojiSections = built;
        Set("EmojiIconSections", built);
        Set("HasEmojiIconResults", built.Length > 0);
        Set("EmojiIconNoMatchesMessage", IconPickerEmojiModel.HasNoMatches(_sectionIconSearchText)
            ? $"No emoji matches \u201c{_sectionIconSearchText.Trim()}\u201d."
            : string.Empty);

        var toneLabels = new[] { "Light", "Medium-light", "Medium", "Medium-dark", "Dark" };
        var tones = new List<BoardsIconSkinToneChoiceView>
        {
            new BoardsIconSkinToneChoiceView("none", string.Empty, "No skin tone", "\ud83d\udeb6",
                string.IsNullOrEmpty(_iconSkinTone))
        };
        for (var index = 0; index < EmojiCatalogue.SkinToneModifiers.Count; index++)
        {
            var modifier = EmojiCatalogue.SkinToneModifiers[index];
            tones.Add(new BoardsIconSkinToneChoiceView($"U+{modifier.EnumerateRunes().First().Value:X}", modifier,
                toneLabels[index] + " skin tone", "\ud83e\uddd1" + modifier,
                string.Equals(_iconSkinTone, modifier, StringComparison.Ordinal)));
        }
        _iconPickerSkinTones = tones;
        Set("IconSkinToneChoices", tones);
        Set("SelectedIconSkinTone", _iconSkinTone);

        Set("CanRemoveIcon", slot.HasChosenIcon);
        Set("IconPickerHasCustomColor", slot.Color is not null);
        Set("IconPickerUsesEmoji", slot.Key == HavenRichSectionIcons.Emoji);
        Set("IconPickerEmojiGlyph", slot.Emoji ?? string.Empty);
    }
}
