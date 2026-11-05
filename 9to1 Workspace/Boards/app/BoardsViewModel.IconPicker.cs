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
    /// <summary>The mark a Board's card draws when its owner has chosen no icon, and the shape a colour choice
    /// tints in that case. Kept in one place because the landing card and the picker have to agree.</summary>
    private const string BoardMaintainedIconKey = "folder";

    /// <summary>
    /// A picker confirmation is queued here and published after the refresh, because the refresh pushes the
    /// document bindings and that push sets StatusText from the session's own status. Setting the message
    /// before it - which is how this read before - meant "Section icon set to books." was written and then
    /// overwritten inside the same action, so the owner never saw which icon had been stored.
    /// </summary>
    private string? _pendingIconAnnouncement;

    private void AnnounceIconChange(string message) => _pendingIconAnnouncement = message;

    private void PublishPendingIconAnnouncement()
    {
        if (_pendingIconAnnouncement is null) return;
        Set("StatusText", _pendingIconAnnouncement);
        _pendingIconAnnouncement = null;
    }

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
        _iconPickerBoardTarget = false;
        _generatedSectionIconPreviews = [];
        _sectionIconSearchText = string.Empty;
        Set("SectionIconPickerSearch", string.Empty);
        Set("SectionIconPickerTitle", $"Page appearance · {page.Title}");
        Set("HasGeneratedSectionIconPreview", false);
        Set("GeneratedSectionIconPreviews", Array.Empty<BoardsGeneratedSectionIconPreviewView>());
        Set("IsSectionIconPickerOpen", true);
        RefreshSectionIconPickerChoices();
    }

    /// <summary>
    /// Opens the same popover with the Board as its target. The reference application gives a workspace the
    /// same icon treatment as its pages, and canonical v2 line 9421 lists Icon? among the Board fields, so a
    /// Board icon is a specified field rather than a preference. This is not a second picker: only the target
    /// differs.
    /// </summary>
    public void OpenBoardIconPicker()
    {
        if (!CanSetBoardIcon) return;
        CancelSectionIconSuggestion();
        _sectionIconPickerSectionId = null;
        _iconPickerPageId = null;
        _iconPickerBoardTarget = true;
        _generatedSectionIconPreviews = [];
        _sectionIconSearchText = string.Empty;
        Set("SectionIconPickerSearch", string.Empty);
        Set("SectionIconPickerTitle", $"Board appearance · {_session.Document.Title}");
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

    private bool IconPickerTargetsBoard => _iconPickerBoardTarget;

    /// <summary>
    /// The Suggest tab asks the configured image model for a mark from an entity's name. It is offered where
    /// that flow exists today - for a section title - and withheld elsewhere, so a tab never promises something
    /// the application cannot do for the thing that opened it.
    /// </summary>
    private bool IconPickerSuggestAvailable => !IconPickerTargetsPage && !IconPickerTargetsBoard;

    /// <summary>Switches which set of choices the popover is showing. This never writes an icon.</summary>
    private void SelectIconPickerTab(string tabKey)
    {
        if (tabKey is not ("emoji" or "icon" or "suggest")) return;
        if (tabKey == "suggest" && !IconPickerSuggestAvailable) return;
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
        if (IconPickerTargetsBoard)
        {
            try
            {
                await _session.SetBoardIconAsync(HavenRichSectionIcons.Emoji, entry.Text,
                    _session.Document.IconColor, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidOperationException or ArgumentException)
            {
                Set("StatusText", "The Board icon could not be saved. Try again.");
                return;
            }
            AnnounceIconChange($"Board icon set to {entry.Name}.");
        }
        else if (IconPickerTargetsPage)
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
            AnnounceIconChange($"Page icon set to {entry.Name}.");
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
            AnnounceIconChange($"Section icon set to {entry.Name}.");
        }

        PushDocumentToBindings();
        NavRebuildRequested?.Invoke();
        RequestRebuild();
        PublishPendingIconAnnouncement();
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
        if (IconPickerTargetsBoard)
        {
            try
            {
                await _session.SetBoardIconAsync(null, null, null, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidOperationException or ArgumentException)
            {
                Set("StatusText", "The Board icon could not be removed. Try again.");
                return;
            }
            AnnounceIconChange("Board icon removed.");
            RefreshAfterIconWrite();
            return;
        }
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
            AnnounceIconChange("Page icon removed.");
            RefreshAfterIconWrite();
            return;
        }
        var section = IconPickerSection();
        if (section is null) return;
        if (!await SetSectionIconAsync(section.Id, HavenRichSectionIcons.Default, null, cancellationToken)) return;
        AnnounceIconChange("Section icon removed.");
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
        if (IconPickerTargetsBoard)
        {
            var document = _session.Document;
            try
            {
                if (document.IconKey == HavenRichSectionIcons.Emoji && document.IconEmoji is { Length: > 0 } boardEmoji)
                    await _session.SetBoardIconAsync(HavenRichSectionIcons.Emoji, boardEmoji, iconColor, cancellationToken);
                else
                    // A colour needs something to tint. A Board with no chosen icon already draws the maintained
                    // mark on its card, so tinting keeps that same mark rather than swapping shapes.
                    await _session.SetBoardIconAsync(document.IconKey ?? BoardMaintainedIconKey, null, iconColor,
                        cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidOperationException or ArgumentException)
            {
                Set("StatusText", "The Board icon color could not be saved. Try again.");
                return;
            }
            AnnounceIconChange(iconColor is null ? "Board icon follows the theme color." : "Board icon color saved.");
            RefreshAfterIconWrite();
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
            AnnounceIconChange(iconColor is null ? "Page icon follows the theme color." : "Page icon color saved.");
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
            AnnounceIconChange(iconColor is null ? "Section icon follows the theme color." : "Section icon color saved.");
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
        PublishPendingIconAnnouncement();
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

    private (string Key, string? Emoji, string? Color, string Kind, bool HasChosenIcon) CurrentIconSlot()
    {
        if (IconPickerTargetsBoard)
        {
            var document = _session.Document;
            // A Board icon is optional in the contract: null means no icon has been chosen, and the card draws
            // the maintained mark. Remove is therefore offered only when something is actually set.
            return (document.IconKey ?? string.Empty, document.IconEmoji, document.IconColor, "board",
                document.IconKey is not null || document.IconColor is not null);
        }
        if (IconPickerPage() is { } page)
            return (page.IconKey, page.IconEmoji, page.IconColor, "page",
                page.IconKey != HavenRichSectionIcons.PageDefault || page.IconColor is not null);
        var section = IconPickerSection() ?? throw new InvalidOperationException("The icon picker has no target.");
        return (section.IconKey, section.IconEmoji, section.IconColor, "section",
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
        if (!IconPickerTargetsBoard && IconPickerSection() is null) return;
        var slot = CurrentIconSlot();

        Set("IconPickerTargetKind", slot.Kind);
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
            new BoardsIconTabView("suggest", "Suggest", _iconPickerTab == "suggest", IconPickerSuggestAvailable),
        };
        Set("IconPickerTabs", _iconPickerTabs);
        Set("CanUseIconPickerSuggest", IconPickerSuggestAvailable);

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
