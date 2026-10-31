# appflowy Stage A mirror records - CUI framework gaps plus donor plumbing

Produced by eng/cui-mirror/mirror.py. Each row is a CUI framework extension request, not a
reason to keep donor markup. Nothing here is rendered or accepted evidence.

- donor UI constructs translated: 2855
- donor nodes dropped: 0
- gap reports: 9898
- CUI framework gaps: 9898
- donor plumbing recorded (not a CUI gap): 0

## CUI constructs still needed

| CUI construct needed / rejected | occurrences | donor constructs seen |
|---|---|---|

| CUI construct needed / rejected | occurrences | donor constructs seen |
|---|---|---|
| `custom donor widget` | 1365 | AFBaseButton, AFCallbackShortcuts, AFDropDownMenu, AFFilledButton.disabled, AFFilledButton.primary, AFFocusManager |
| `donor icon asset (FlowySvg)` | 754 | FlowySvg |
| `n/a` | 702 | no widget constructor found |
| `theme colour token` | 579 | AFThemeExtension.of(context)
                               , AFThemeExtension.of(context).borderColor, AFThemeExtension.of(context).greyHover, AFThemeExtension.of(context).lightIconColor, AFThemeExtension.of(context).onBackground, AFThemeExtension.of(context).secondaryTextColor |
| `fixed vertical spacer` | 579 | VSpace |
| `fixed horizontal spacer` | 510 | HSpace |
| `flex share sizing` | 426 | Expanded, Flexible |
| `donor text wrapper (FlowyText)` | 424 | FlowyText |
| `computed size` | 405 | AnimatedContainer.height=kPageStyleLayoutHeight, Container.height=192 + errorHeight, Container.height=250 + (showingRecent ? 32 : 0), Container.height=DesktopAIChatSizes.avatarSize, Container.height=DesktopAIPromptSizes.actionBarSendButton, Container.height=GridSize.footerHeight |
| `distributor alignment` | 385 | Column.crossAxisAlignment, Column.mainAxisAlignment, Row.crossAxisAlignment, Row.mainAxisAlignment |
| `property not modelled` | 379 | FlowyText.background, FlowyTextField.action, GestureDetector.action, InkWell.action, MouseRegion.action, Slider.action |
| `live variable / binding expression` | 377 | BlocProvider, Consumer, Provider, ValueListenableBuilder |
| `fixed zero-size spacer` | 365 | SizedBox.shrink |
| `conditional rebuild on state` | 332 | BlocBuilder, StreamBuilder |
| `insets expression` | 261 | AppFlowyPopover.margin, Card.margin, Container.margin, Container.padding, FlowyButton.margin, IconButton.padding |
| `gesture handling (tap/drag/long-press)` | 229 | GestureDetector |
| `inline text run` | 209 | RichText, TextSpan |
| `donor button wrapper (FlowyButton)` | 201 | FlowyButton |
| `hover pointer state` | 152 | MouseRegion |
| `text overflow mode` | 139 | FlowyText.overflow, RichText.overflow, Text.overflow |
| `donor popover wrapper (AppFlowyPopover)` | 136 | AppFlowyPopover |
| `donor tooltip wrapper (FlowyTooltip)` | 131 | FlowyTooltip |
| `donor icon button wrapper (FlowyIconButton)` | 112 | FlowyIconButton |
| `layered stack with positioned children` | 105 | Stack |
| `scroller` | 80 | SingleChildScrollView |
| `absolute placement` | 75 | Positioned |
| `inline handler block` | 65 | FlowyButton.onTap, FlowyIconButton.onPressed, FlowyTextField.onChanged, GestureDetector.onLongPress, GestureDetector.onTap, IconButton.onPressed |
| `border spec` | 57 | Container.decoration.border, DecoratedBox.decoration.border, FlowyButton.decoration.border, FlowyIconButton.decoration.border |
| `application frame with body/appBar layers` | 46 | Scaffold |
| `donor text field wrapper (FlowyTextField)` | 42 | FlowyTextField |
| `donor icon set` | 39 | Icons.adaptive, Icons.add, Icons.arrow_back_rounded, Icons.arrow_drop_down, Icons.arrow_drop_down_rounded, Icons.arrow_drop_up |
| `intrinsic sizing` | 30 | IntrinsicHeight |
| `inline text template` | 29 | FlowyText, Text, TextSpan |
| `ripple/press visual state` | 28 | InkWell |
| `computed subtree` | 23 | Builder |
| `item collection (list/tree/table)` | 19 | ListView, SliverList |
| `tab strip` | 19 | Tab, TabBar |
| `dialog with typed responses` | 14 | AlertDialog, Dialog |
| `whitespace-only text run` | 12 | TextSpan |
| `validated form field` | 10 | TextFormField |
| `sliver-based scrolling` | 9 | CustomScrollView |
| `derived live value projection` | 8 | Selector |
| `animated property transition` | 6 | AnimatedContainer, AnimatedOpacity |
| `header bar` | 6 | AppBar |
| `conditional visibility` | 5 | Visibility |
| `elevated card surface` | 4 | Card |
| `menu button with typed responses` | 3 | PopupMenuButton |
| `tooltip surface` | 3 | Tooltip |
| `sliver layout` | 2 | SliverPadding |
| `focus/keyboard action state` | 2 | FocusableActionDetector |
| `drag-reorderable item collection` | 2 | ReorderableListView |
| `transient banner` | 2 | SnackBar |
| `anchored menu with typed items` | 1 | MenuAnchor |
