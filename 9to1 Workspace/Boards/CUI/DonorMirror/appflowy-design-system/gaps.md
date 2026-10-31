# appflowy-design-system Stage A mirror gaps - constructs CUI cannot express yet

Produced by eng/cui-mirror/mirror.py. Each row is a CUI framework extension request, not a
reason to keep donor markup. Nothing here is rendered or accepted evidence.

- donor UI constructs translated: 129
- donor nodes dropped: 0
- gap reports: 362

| CUI construct needed / rejected | occurrences | donor constructs seen |
|---|---|---|
| `n/a` | 97 | no widget constructor found |
| `custom donor widget` | 58 | AFBaseButton, AFMenuItem, AFPopover, Actions, AnimatedSize, CallbackShortcuts |
| `computed size` | 32 | AnimatedContainer.height=height, AnimatedContainer.width=width, Container.height=expandHeight ? size.height : null, Container.height=thickness, Container.height=widget.axis == Axis.horizontal
         , Container.height=widget.height |
| `insets expression` | 27 | AnimatedContainer.margin, Container.margin, Container.padding, FlowyTooltip.padding, Padding.padding, SingleChildScrollView.padding |
| `property not modelled` | 20 | GestureDetector.action, InkWell.action, MouseRegion.action, TextField.action, TextFormField.action |
| `theme colour token` | 17 | AFThemeExtension.of(context).borderColor, BoxDecoration(
                  border: Border.all(
       , BoxDecoration(
                  borderRadius: BorderRadius., BoxDecoration(
              boxShadow: theme.shadow.medium,, BoxDecoration(
        border: style.border,
        color: , BoxDecoration(
        color: backgroundColor ?? theme.surfa |
| `distributor alignment` | 14 | Column.crossAxisAlignment, Row.crossAxisAlignment, Row.mainAxisAlignment |
| `gesture handling (tap/drag/long-press)` | 11 | GestureDetector |
| `hover pointer state` | 7 | MouseRegion |
| `flex share sizing` | 7 | Expanded, Flexible |
| `fixed horizontal spacer` | 7 | HSpace |
| `scroller` | 6 | SingleChildScrollView |
| `donor tooltip wrapper (FlowyTooltip)` | 6 | FlowyTooltip |
| `border spec` | 5 | Container.decoration.border, DecoratedBox.decoration.border |
| `text overflow mode` | 5 | FlowyText.overflow, Text.overflow |
| `layered stack with positioned children` | 4 | Stack |
| `donor icon set` | 4 | Icons.arrow_drop_down, Icons.cancel, Icons.check, Icons.close |
| `donor button wrapper (FlowyButton)` | 4 | FlowyButton |
| `donor icon asset (FlowySvg)` | 4 | FlowySvg |
| `donor text wrapper (FlowyText)` | 4 | FlowyText |
| `transient anchored popover` | 2 | Popover |
| `fixed vertical spacer` | 2 | VSpace |
| `donor icon button wrapper (FlowyIconButton)` | 2 | FlowyIconButton |
| `inline handler block` | 2 | MouseRegion.onEnter, MouseRegion.onExit |
| `validated form field` | 2 | TextFormField |
| `tooltip surface` | 2 | Tooltip |
| `computed subtree` | 1 | Builder |
| `donor popover wrapper (AppFlowyPopover)` | 1 | AppFlowyPopover |
| `ripple/press visual state` | 1 | InkWell |
| `inline text run` | 1 | RichText |
| `animated property transition` | 1 | AnimatedContainer |
| `live variable / binding expression` | 1 | ValueListenableBuilder |
| `sliver-based scrolling` | 1 | CustomScrollView |
| `transient banner` | 1 | SnackBar |
| `donor text field wrapper (FlowyTextField)` | 1 | FlowyTextField |
| `intrinsic sizing` | 1 | IntrinsicHeight |
| `fixed zero-size spacer` | 1 | SizedBox.shrink |
