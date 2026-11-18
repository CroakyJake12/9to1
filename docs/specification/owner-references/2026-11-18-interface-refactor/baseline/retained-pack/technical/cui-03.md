# CUI, themes and shared interaction system — Basic markup and Container types; Comments, whitespace and escaping (part 03)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0304 · v2 lines 1319–1726

V3 editorial amendment: V3-OWNER-005. Original wording is preserved in the replacement ledger.

Core nonvisual/declarative constructs:
* Theme, Style, Property, Variable, Prefab, ActionEvent/&, If, Else, Repeat, Import, Keyframe/@ and Anchor. Anchor is the nonvisual named placement declaration used by LoadAt and related placement APIs; it is not an eleventh visual primitive or a donor layout control.
Built-in language/framework keywords are case-insensitive. User-defined IDs, Groups, C# variables, custom Definition names, Prefab IDs and other user symbols are case-sensitive unless the owning external language/package explicitly defines different rules.
Basic markup and Container types
CUI uses XML-like nesting but is its own strict language. Most behavioural variation is expressed through Type and properties rather than different primitive element names.
<Page>
    <Container Type="Grid">
        <Text>Hello</Text>
    </Container>
</Page>
Container MUST support at least Grid, Vertical Stack, Horizontal Stack and Absolute as built-in Types. Additional mature layout Types MAY be registered without changing the core element vocabulary.
<Container Type="Vertical Stack" Horizontal-Scrolling="True" Vertical-Scrolling="True">
Attribute-assignment shorthand uses a colon after an identifier. The following are equivalent when unambiguous:
<Container Type="Grid">
<Container Type:Grid>
For unquoted shorthand values, underscore MAY represent a space. Therefore Type:Vertical_Stack resolves to Type="Vertical Stack" when that is the only valid symbol. Quoted values are literal and underscore substitution never occurs inside quotes.
If both an underscored name and the corresponding spaced name exist in the active resolution scope, shorthand is ambiguous and MUST fail compilation rather than choosing one.
Comments, whitespace and escaping
Comments use <# and #> and MAY span multiple lines. Comments do not nest and do not create runtime nodes.
<# This is a CUI comment #>
Formatting whitespace outside text-bearing content is ignored. Text content uses the Text element's whitespace policy; the default collapses source indentation/newline whitespace that exists only for readable markup, while Whitespace:Preserve retains exact authored whitespace.
Quoted values are literal except for explicitly documented escape sequences. The compiler MUST provide an escaping mechanism for CUI-significant punctuation in free text/shorthand. Formatter output MUST be deterministic.
(Definition), custom elements and custom Types
(Definition) is a declaration modifier. A Definition creates reusable language/component definitions and is not rendered merely because its declaration is present.
<Paragraph (Definition)>
    ...
</Paragraph>
The declaration above creates a reusable Paragraph element that can later be instantiated as <Paragraph>...</Paragraph>.
<Text (Definition) Type="ABCDEFG">
    ...
</Text>
A Type Definition extends the relevant base element's registered behaviour/style contract. Built-in element and Type names cannot be silently shadowed by user definitions; an intentional framework-supported override must be explicit.
Definitions SHOULD be authored using ordinary CUI elements/properties so authors do not have to learn a separate component language.
Themes and inherited theme structure
Theme is an inherited visual-design scope. Everything within a Theme uses that Theme until a descendant Theme overrides it; after the descendant closes, inheritance returns to the parent Theme.
Current built-in Theme names:
* Professional.
* Glow.
* Playful.
* Frosted Glass.
* Bubble.
* Metallic.
* Retro.
* Western.
Cinematic is retained only as explicit legacy compatibility. Create Theme supports compatible custom definitions.
Themes are complete CUI-owned visual systems, not colour-only palettes. Each built-in Theme MUST provide coherent effective typography, colours, surfaces, spacing, control shapes/templates, corner treatment, elevation, focus/hover/pressed/selected/disabled/validation states and motion defaults for supported CUI elements and Types, using explicit CUI inheritance/reuse where appropriate. Themes MUST NOT silently change a control's semantic meaning, required user workflow or user-authored artifact content.
<Theme Name="Glow">
    <Container Type:Vertical_Stack>
        ...
    </Container>
</Theme>
CUI-owned theme implementation and renderer isolation
CUI-THEME-001 — One presentation authority. CUI owns the effective styles, resources, control templates and state visuals of first-party application surfaces on every supported renderer. Do not install Avalonia FluentTheme, SimpleTheme or another donor/application theme as the global/default visual owner underneath CUI. Removing a theme import alone is not a fix: supply the complete CUI-owned templates, states and resources required for functioning controls. Retained donor-private rendering mechanisms must remain encapsulated and unable to override CUI styling or leak default native chrome into CUI surfaces. System-owned OS dialogs and user-authored document/content styles are separate explicitly identified boundaries, not excuses for unthemed first-party app controls.
CUI-THEME-002 — Deterministic startup and fallback. Resolve Home's effective user_preference before presenting the normal shell, using the last valid authorised cache when offline. Missing/invalid custom themes fall back explicitly to CUI Professional with a clear diagnostic; never silently fall back to Fluent, Simple or unrelated native styling. New windows, menus, popups, tooltips, dialogs, nested Prefabs and specialised Object chrome use the same effective CUI resources. Reactive theme changes preserve focus/selection, canonical data and ongoing work; they cannot merely swap an accent colour.
CUI-THEME-003 — Inspectable precedence. The CUI Style and property precedence section below is the sole cascade. Donor style priorities, resource load order, native selectors or framework theme variants are not additional precedence layers. Resolve each effective typed property through CUI and expose its winning value/source in the CUI theme/property inspector. General control defaults belong in the active CUI theme; hidden hard-coded donor defaults must not defeat it. Renderer adapters apply resolved values and return input/state changes through CUI rather than independently restyling controls. Equal-specificity declarations resolve deterministically by CUI scope/source order, not incidental renderer timing. Animation and state presentation remain governed by their declaring CUI property/Style contract; accessibility policies remain enforced.
CUI-THEME-004 — Real conformance evidence. Test all eight current built-in Themes, Cinematic compatibility and a custom theme, nested overrides and return to inheritance, user_preference across at least two independently running apps, restart/offline fallback, supported native/web surfaces, keyboard focus, validation, disabled/selected/hover/pressed states, scale/contrast and reduced motion. Include menus/dialogs/popups and specialised Object chrome, not only Buttons. Demonstrate that competing donor-default resource/selector values cannot win the CUI cascade, and inspect actual screenshots plus computed semantic/property state. Preserve user-authored content identically through theme changes. Missing theme/template resources or a renderer-specific default-theme leak is a conformance failure, not accepted visual variation.
Home user-preference theme binding
CUI MUST accept the reserved dynamic theme-name input `user_preference` in any valid Theme scope, for example `<Theme Name="user_preference"> ... </Theme>`. `user_preference` is a resolver/reference, NOT an additional built-in theme or a literal visual style. Its resolved value is the currently effective Home-managed theme selection for the active user/profile, including supported installed custom themes. Resolution MUST use the canonical Home Core settings/theme service; app-specific hard-coded theme copies, duplicated preference stores and silent overrides are prohibited.
Unless a particular nested region explicitly chooses another supported Theme, first-party 9to1 app shells and shared CUI components MUST inherit the user's effective preferred theme consistently (including Picture, Wave and Motion). An explicit descendant Theme override remains scoped and retains the documented parent-theme inheritance when it ends; app-specific temporary/preview overrides MUST be visible and reversible, must not overwrite the user's Home preference, and must not silently disable user-selected accessibility settings. Dynamic binding is reactive: valid Home changes update affected active windows/surfaces and future launches coherently, with theme resource and inherited properties re-resolved rather than merely changing accent colours.
CUI/theme acceptance MUST exercise switching Home's preferred theme among Glow, Professional and a supported custom theme, applying `Name="user_preference"` in at least two independent running apps, observing correct live/restored styling across restart and signed-in supported surfaces, then testing offline cached preference, missing/invalid custom theme fallback, descendant explicit override and return to parent preference, plus reduced-motion/contrast/accessibility inheritance. Theme updates MUST NOT change user content or canonical artifact state.
Users can define custom Theme types in .cui-theme files.
<Theme (Definition) Name="Rainbow" Inherits="Professional">
    <Button (Definition)>
        <# Theme-specific Button properties #>
    </Button>
</Theme>
Within a Theme definition, an element Definition without Type defines that Theme's default presentation for that element. A Definition with Type defines the Theme presentation for that element Type. Custom Themes SHOULD inherit another Theme when only deltas are needed.
Default typography
Montserrat is the default CUI/9to1 interface font wherever technically available and legally redistributable. First-party 9to1 applications and the 9to1-OS shell SHOULD inherit the global CUI typography system rather than independently selecting unrelated UI fonts.
CUI MUST define a robust fallback font stack for environments where Montserrat is unavailable and must support specialised font roles such as monospace/code text. Users may override the system/interface font through the supported customisation/theme framework without requiring application-specific hacks.
Prefabs and reusable shared CUI
A Prefab packages reusable shared CUI structure/code in a .cui-prefab file. Prefabs are intended for components that should be implemented once and reused across apps, such as the shared Dulche AI bar.
<Prefab (Definition) Type="static" ID="AIBar">
    ...
</Prefab>
Prefab Type MUST be static or dynamic.
* Static Prefab — all live instances share one internal mutable Prefab state. If an internal property/state changes, every live instance reflects the same shared state. Host placement, host parent, host size and host Active state remain per instance.
* Dynamic Prefab — each instance receives independent runtime state while sharing the same compiled implementation/defaults.
Static Prefab state is runtime-shared, not automatically persistent across process restart. Persistence occurs only when the owning Prefab/app explicitly stores that state.
Prefabs may contain normal elements, Definitions, nested Prefabs, variables, Styles, actions, Themes and specialised Objects. Cyclic Prefab/Definition dependencies MUST be rejected.
A Prefab exposes caller-settable inputs, actions/events and optional CUI-content/Children inputs explicitly. It MUST NOT implicitly capture arbitrary caller state.
Prefab inputs and instantiation
Prefab inputs are declared using Variable Definitions or another equivalent typed public-definition form implemented by the compiler.
<Variable (Definition) Type="string"::Title>
<Variable (Definition) Type="string"::Subtitle>
A Prefab instance may provide declared inputs in markup or runtime load arguments. Static Prefabs MUST reject conflicting per-instance mutable input semantics that would violate their shared-state contract; content requiring independent mutable state uses a dynamic Prefab.
Variables and variable-reference syntax
Ordinary application variables remain ordinary implementation-language values. CUI reactivity is attached to the CUI reference by default; application authors do not have to wrap every value in a special Live<T> type merely to update UI.
string Name = "Jacob";
<Text>Hello <:Name:></Text>
<:Name:> is the compact live variable-reference form. Live is assumed unless static is explicitly requested.
<:Name:live>
<:Name:static>
Typed/conversion shorthand prefixes the target type:
<int:Age:>
<int:Age:static>
The long conversion forms are equivalent:
<Variable.ToInt>Age</Variable>
<Variable Type="int">Age</Variable>
Variable.ToInt and Variable Type="int" MUST resolve to the same typed conversion operation. Equivalent ToString, ToFloat, ToBool and other registered conversion forms MAY exist. Conversion failure MUST return/produce a defined typed validation/conversion error; it MUST NOT silently substitute an unrelated default such as zero.
Live versus static references
A live CUI reference subscribes to the referenced CUI-visible value and automatically reevaluates the dependent UI when that value changes. A static CUI reference evaluates on load/explicit refresh and does not update merely because the source changes.
Static subexpressions retain their last evaluated value even when another live reference in the same element causes an unrelated update. Element.Refresh() explicitly reevaluates the element's static references; refreshing a Container/Page recursively refreshes the applicable active subtree.
CUI build tooling MUST instrument or generate change-notification plumbing for CUI-visible persistent fields/properties/observable collections referenced live. Per-frame polling is prohibited. Short-lived method locals cannot become magically persistent observable state after their scope ends.
Live dependency relationships
new Live(...) is reserved for relationships between application values/objects, not for basic CUI binding.
Graph = new Live(x, y);
The declaration above means Graph depends on x and y. A change to either dependency invalidates/recomputes Graph through Graph's defined derived getter, refresh/update operation or explicitly supplied recomputation function. Live MUST NOT invent a new scalar value when no recomputation semantics exist.
Dependency cycles MUST be rejected at compile/build time when statically discoverable and surfaced as typed dependency errors otherwise.
ID/Name and Group addressing
ID and Name are aliases for the same unique identity mechanism. An element may own zero or one ID/Name and any number of Groups. Multiple elements may belong to the same Group.
<Text ID="txt" Group="Labels,Important">Hello</Text>
<Text Name="txt">Hello</Text>
ID shorthand is :: and Group shorthand is :::.
<Text::txt:::Labels,Important>Hello</Text>
Within a scope, IDs are unique. Groups intentionally contain multiple members. An ID and Group MUST NOT use the same identifier in one scope because direct runtime addressing would be ambiguous.
Generated/runtime handles allow direct element mutation:
txt.Text = "Hello";
Labels.Color = "Green";
A direct Group operation broadcasts to every current compatible member in scope. The runtime MUST validate the entire target set before a persistent group mutation; incompatible required targets cause the operation to fail rather than silently producing partial state.
ID and Group scope
Page creates a top-level CUI addressing scope. Each dynamic Prefab/custom Definition instance creates a child scope for its internal IDs/Groups. A static Prefab has one shared internal state scope. Bare IDs/Groups resolve only in the current scope; cross-scope access must explicitly name the containing Page/Prefab instance.
Groups are dynamic: active or inactive members remain members while the corresponding object exists. Persistent active Styles targeting a Group apply to newly loaded compatible members while that Style remains active.
Property declarations and shorthand
Properties are strongly typed registered CUI properties. The long Property form and compact $ form are semantically equivalent.
<Property="Color" Color="Green">...</Property>
<$Color:Green>...</$Color>
<$Color=Green>...</$Color>
The short closing form </$> closes the most recently opened property region when nesting is unambiguous.
<$Color:Green>Hello</$>
A property region is a typed property scope, not an ordinary visual element. It can have an ID or Groups using the same shorthand system.
<$Color:Green::pinkText>Hello</$>
<$Color:Green:::greenText>Hello</$>
Multiple property declarations may contribute different property types to the same Group. If a target Group exposes exactly one property type, a property mutation may omit the property name. If multiple property types are available, the property name is mandatory.
Property-target mutation directives
CUI markup may mutate an existing property target/Group declaratively.
<:::$greenText.Color="Pink">
<:::$greenText="Pink">
The second form is valid only when greenText resolves to exactly one property type. For a multi-property Group such as Color plus Background, the explicit property is required.
<:::$fancyText.Color="Blue">
<:::$fancyText.Background="Black">
Outside a Style/event rule, a mutation directive executes when its containing declaration becomes active/loaded and executes again on later reactivation. It is a persistent stored-property mutation, not an automatically reversible Style overlay.
Text and Children runtime content
Text and Children are distinct runtime surfaces.
* Text is literal textual content. Assigning markup-looking characters to Text displays them as text.
* Children is structured CUI child content. Assigning to Children parses the value as a CUI child fragment under the host's allowed component/action capabilities.
Assignment means Replace by default; Replace() is the explicit equivalent and Add() appends.
txt.Text = "Hello";
txt.Text.Add(" world");
txt.Children = "<$Color:Green>Hello</$>";
txt.Children.Add("<Text>Another item</Text>");
A failed dynamic Children parse/validation MUST leave the previous valid child tree intact and return a typed error.
Active, Hidden and Interactive
Active, Hidden and Interactive are separate properties and MUST NOT be collapsed into one Avalonia visibility/enabled flag.
* Active=True means the element/declaration participates in its normal active lifecycle/tree. Active=False deactivates/unloads it from normal layout/render/input participation while retaining enough object/state information for later reactivation.
* Hidden=True suppresses visual rendering only. A hidden active object still occupies layout, retains state and can receive interaction/events when Interactive=True.
* Interactive=False disables user-originated interaction/focus targeting while leaving the active object visible/layout-participating unless another property changes that.
All objects default to Active=True, Hidden=False and Interactive=True unless a Definition/Type/Theme explicitly defines another default.
<Text::Txt Active="False">Hello</Text>
Txt.Active(true);
Active(false) is an effective participation gate and MUST NOT overwrite stored Hidden, Interactive or other properties. Reactivating restores those stored properties exactly. Property assignments made while inactive update stored state and become effective when reactivated.
Parent lifecycle and independent activation
Normal descendants inherit Active state from the nearest active parent. Effective Active is Self.Active AND parent EffectiveActive.
A leading ! or ActiveIndependently=True breaks only the parent-to-child Active dependency.
<!Style::PersistentStyle>
<Text ActiveIndependently="True">...</Text>
An independently active child/declaration is not forced inactive when its parent deactivates and must be deactivated separately. ! does not break Theme inheritance, symbol scope, Prefab ownership, variables, Groups, styling inheritance or event targeting.
For independently active visual descendants whose logical parent deactivates, the runtime preserves the required logical/positioning reference state without causing the inactive parent to re-enter ordinary sibling layout.
Lifecycle events
Initial activation generates Loaded. A False-to-True Active transition generates Loaded again. A True-to-False transition generates Deactivated. Event processing is typed and follows the ActionEvent rules below.
Stateful/non-once events are temporary overlays for the event lifetime; Once-prefixed variants commit their property/action result as persistent state rather than reverting with event end. Once means committed/persistent, not 'execute only once ever'.
Layout property model
CUI's property catalogue MUST provide mature visual/layout coverage comparable in scope to broadly applicable CSS and Avalonia properties while retaining CUI's typed semantics. Layout is property-driven.
Required layout/property families include:
* Width, Height, MinWidth, MaxWidth, MinHeight and MaxHeight.
* Margin, Padding, CellMargin and CellGap.
* HorizontalAlignment, VerticalAlignment, wrapping/overflow and scrolling properties.
* Grid Rows, Columns, RowSizes, ColumnSizes, Row, Column, RowSpan and ColumnSpan.
* Absolute X/Y positioning and associated coordinate modes.
* Typography, colour, background, borders, corner radii, opacity, clipping, transforms, shadows/effects and other applicable mature property families.
Numeric layout units MUST be typed. Bare fixed dimensions use CUI logical/DPI-aware pixels unless the property explicitly defines another unit. Auto, percentages and fractional/fill sizing are supported where meaningful.
ColumnSizes:1fr_2fr_200
RowSizes:Auto_1fr
Grid and stack behaviour
Container Type:Grid performs row/column layout. Container Type:Vertical_Stack and Horizontal_Stack perform ordered stack layout. CellGap controls spacing between grid/stack cells/items; CellMargin controls the per-cell/item margin contribution.
Scrolling is orthogonal to layout Type and can be enabled independently.
<Container Type:Vertical_Stack Horizontal-Scrolling:True Vertical-Scrolling:True>
Invalid layout-property combinations MUST fail at compile time when statically knowable or return a typed runtime layout error when dynamically introduced.
Absolute positioning and coordinate system
Container Type:Absolute enables free child positioning. Bare X and Y use CUI's proportional coordinate system by default and MAY be decimal values.
The centre of the positioning surface is (0,0). The normal horizontal range is -1000 at the left edge to +1000 at the right edge. The normal vertical range is -1000 at the bottom edge to +1000 at the top edge. Values outside the normal range are legal and place the reference point beyond the surface, which is useful for transitions/off-screen UI.
<Container Type:Absolute>
    <Text X:-500.5 Y:250.25>Hello</Text>
</Container>
An axis may alternatively use X-px/Y-px for CUI logical pixels or X-%/Y-% for percentage positioning. Percentage 0 is left/bottom, 50 is centre and 100 is right/top. Coordinates in every mode MAY be decimals.
An element may define only one authoritative positioning mode per axis at a time. Supplying X and X-px together, for example, is an error.
Coordinate recalibration and inherited axes
X-Inherited and Y-Inherited independently recalibrate descendant coordinate resolution to the declaring Container's bounds.
* X-Inherited=True makes that Container's left/right bounds the descendant -1000/+1000 X range and the corresponding percentage/pixel reference surface.
* Y-Inherited=True makes that Container's bottom/top bounds the descendant -1000/+1000 Y range and the corresponding percentage/pixel reference surface.
The nearest applicable inherited axis scope wins. X and Y may therefore resolve against different ancestor Containers. Outside an Absolute/free-positioning context, free X/Y placement is invalid unless the owning specialised Type explicitly declares support.
Layering and rendering depth
Layer is both a container element and an inheritable signed-integer property.
<Layer Layer="1">...</Layer>
<Layer:2>...</Layer>
<Text Layer="5">Hello</Text>
Higher Layer values render/hit-test above lower values; negative and positive values are valid. An element-level Layer overrides the parent layer and becomes the inherited layer for its normal descendants unless they override it again. Equal layers use normal tree/source order, with the later item above the earlier item where they overlap.
Layer controls depth only; it does not by itself imply modal behaviour or layout.
Modal behaviour
Modal is a separate property. Modal=True traps applicable focus/input inside the modal subtree and blocks lower-layer interaction in the same render surface until dismissal. Nonmodal high-layer popups may sit above content without blocking unrelated lower-layer interaction outside their bounds.
Anchor and runtime placement
Anchor is an optional named placement point, not a prerequisite for loading a Prefab or dynamically created UI.
<Anchor::bob>
Runtime placement MUST support at least:
* LoadAt(anchor) — place at an Anchor.
* LoadAt(x,y) — fixed proportional coordinates in the selected/current Absolute coordinate scope.
* LoadAtPixels(x,y) and LoadAtPercent(x,y).
* LoadUnder(element), LoadAbove(element), LoadLeftOf(element) and LoadRightOf(element).
* LoadIn(grid,row,column, optional spans/alignment).
* LoadInto(container).
Relative placement operations such as LoadUnder, LoadAbove, LoadAt(anchor) and LoadIn(grid,...) remain live placement relationships and track target movement/resizing. Raw coordinate placement is fixed in the chosen coordinate scope unless the caller changes it.
When multiple Prefabs/UI instances are loaded at the same Anchor without another placement rule, they flow downward from that Anchor in load order. Raw repeated coordinate loads overlap unless another rule is supplied.
Style and property precedence
Effective presentation MUST resolve deterministically in the following order from lowest to highest precedence:
* CUI built-in property default.
* Inherited Theme.
* element Type/(Definition) default.
* explicit instance property.
* scoped <$Property> region.
* active Style/event overlay.
A direct runtime property assignment changes the stored explicit property value at its normal level; it is not an invisible new top-level precedence layer. If a temporary event Style currently overrides that property, the new stored value becomes visible when the temporary overlay ends. This complete CUI cascade MUST be enforced before renderer application; Avalonia/native themes, bindings, selectors and local-value priorities MUST NOT override or reinterpret it. Test both stored mutation during an active overlay and restoration after the overlay ends.
Style rules and ActionEvent
Action events use the long form <ActionEvent Type="..."> or & shorthand.
<&Hover>
<&Clicked>
<&PointerEnter>
<&PointerExit>
Inside a Style, an ActionEvent begins a rule and following property/action declarations belong to that event until the next ActionEvent or the end of the Style. A property declaration before any event is an unconditional Style property while that Style is active.
<Style>
    <&Hover{::button}>
    <$Color:Pink{::label}>
    <$Background:Black{::label}>
</Style>
Event target syntax
Curly braces declare an explicit target. {::foo} targets ID foo; {:::foo} targets Group foo. TargetID and TargetGroup are the verbose equivalent attributes.
<&Hover{::greenText}>
<&Hover TargetID="greenText">
<$Color:Pink{:::labels}>
<$Color:Pink TargetGroup="labels">
When an event/property declaration is owned by an element/Style and no explicit target is provided, it targets the owning element/context according to that construct's standard rule.
Temporary versus Once events
Stateful events such as Hover apply their associated Style overlay only while the state is active; ending Hover automatically reveals the previously effective values. OnceHover commits the resulting mutation so it remains after Hover ends.
The same temporary-versus-committed distinction applies to corresponding event families such as Clicked/OnceClick, Loaded/OnceLoaded and Deactivated/OnceDeactivated where meaningful. Once-prefixed events may execute on later qualifying occurrences; 'Once' denotes committed effect semantics, not a one-time lifetime subscription.
Event source, propagation and cancellation
Events target the actual originating element by default and do not automatically bubble through every parent. Bubble=True enables supported upward propagation.
Event context MUST expose at least Event.Source, Event.Target, Event.Type and applicable Event.Value. For a Group trigger, Event.Source identifies the particular Group member that generated the event, enabling a single Group rule to affect only that member when desired.
Event.Cancel() cancels further compatible propagation/action when the event family permits cancellation.
Typed actions and asynchronous work
ActionEvent MAY invoke a named typed application/domain action in addition to Style/property changes. UI actions MUST converge on the same domain operations used by the app's API/Automation surface where an equivalent operation exists; CUI MUST NOT become a parallel private business-logic layer.
<&Clicked{::SaveButton} Action:SaveDocument>
Long-running/async actions MUST NOT block the UI thread and must expose typed running/completed/failed/cancelled state/events usable by CUI.
Input and two-way value flow
Displaying a variable is one-way by default. Writable Input properties bind two-way by default when the source is writable and the conversion is valid.
<Input Type:Text Value=<:Name:>>
<Input Type:Checkbox Checked=<:Enabled:>>
<Input Type:Slider Value=<:Volume:>>
WriteBack=False disables automatic source update. A :static reference is snapshot-only and has no automatic write-back.
When typed conversion fails during input, the control retains the invalid entered representation and exposes validation error state; the source variable is not overwritten until conversion succeeds.
Conditional UI
CUI MUST provide structural conditional composition.
<If Condition=<:LoggedIn:>>
    ...
</If>
<Else>
    ...
</Else>
Condition is live by default. When the condition changes, the applicable branch is instantiated and the other branch is removed from the structural tree. A static condition reevaluates only on explicit containing-scope refresh.
Structural removal is stronger than Active inheritance: an ActiveIndependently child cannot remain alive after the entire conditional branch that owns it ceases to exist.
Repeated UI
Repeat creates one generated UI instance per source item and MUST preserve item identity across live collection additions/removals/reordering.
<Repeat Source=<:Users:> As:user Key=<:user.ID:>>
    <Text><:user.Name:></Text>
</Repeat>
Key may be omitted only when each item exposes an unambiguous stable ID. A live Repeat without stable identity MUST fail rather than recreate items with ambiguous state transfer. The iteration variable exists only inside its Repeat instance.
Property catalogue and transform support
The CUI property registry MUST include mature CSS/Avalonia-like visual properties and transforms rather than limiting authors to basic colours and dimensions.
At minimum, applicable registered properties include:
* Rotate, Scale, ScaleX, ScaleY, Skew, SkewX, SkewY, Translate, TranslateX and TranslateY.
* Opacity, Background, Foreground/Color, Border, BorderColor, BorderWidth, CornerRadius, Shadow, Clip and Effect.
* Typography, text alignment/wrapping, layout sizing/alignment, scrolling, cursor/input/focus and accessibility properties.
* Animation and animation-control properties.
Properties remain strongly typed. Unknown/incompatible property names or values are compiler errors when statically knowable.
Effect versus Background backdrop processing
Effect and Background modifier chains have different targets.
* Effect processes the element's own rendered appearance. Effect:Blur(1), for example, blurs that element.
* Background modifier chains process only the live rendered appearance of content behind the element as seen through the element's background region. They MUST NOT mutate, rotate, recolour, blur, move, restyle or otherwise change the actual underlying elements, their layout, hitboxes, state, events, Layer or accessibility tree.
Backdrop processing is live, not a captured fake snapshot. If content behind the background changes because of user interaction, variable updates, animation, video, scrolling, layout or any other render change, the appearance seen through the Background MUST update automatically without calling Refresh(). The compositor MAY cache safely but must invalidate affected backdrop dependencies correctly.
A fully transparent background exposes the processed backdrop directly; a semi-transparent colour/material composites over the processed backdrop; a fully opaque result may make the backdrop invisible and may be optimised away when observationally equivalent.
Blur, Refract and chained backdrop modifiers
Gaussian blur is built in.
Effect:Blur(1)
Background:Blur(1).Red
Blur uses a ratio by default: Blur(1) is equivalent to Blur(100%), Blur(0.5) to Blur(50%). The renderer maps the public ratio to the platform blur implementation deterministically.
Refract is a built-in live backdrop modifier that refracts/distorts content behind the Background, enabling liquid-glass-like materials.
Background:Refract(1).Blur(1).Color(Green).Red
Backdrop modifiers are evaluated left-to-right. Visual properties with a defined backdrop-rendering interpretation may participate as modifiers. For example:
Background:Refract(1).Blur(1).Color(Green).Rotate(30deg).Red
In the example above, only the sampled live backdrop appearance inside the Background is refracted, blurred, colour-treated and rotated; the underlying elements themselves remain unchanged. A property with no meaningful/registered backdrop interpretation MUST produce a typed compiler/runtime error rather than being silently ignored.
Gradients
Gradient is a first-class property value/modifier.
Gradient(Linear),Red(10%),Blue(20%),Green(70%)
Gradient(Linear),Red,Blue,Green
Gradient(Linear).Red, Blue, Green
Explicit stop percentages define stop positions. When stops omit positions, they are distributed evenly. Radial and other registered gradient forms are supported through the same Gradient(Type) model. Stop positions MUST be deterministic and validated; duplicate positions MAY be used for hard transitions where supported.
Animation and Keyframes
Animation is the canonical property used to apply a named animation.
<Button Animation:ShinyButton>
Named animations are declared using @ shorthand or Keyframe long form.
<@ShinyButton>
<Keyframe Name="ShinyButton">
Keyframe positions may be percentages or absolute time in milliseconds.
<$10%>
<$1000>
<$1000> means 1000 milliseconds / one second. Explicit ms/s duration syntax is also supported. Percentage keyframes require a declared/inferable Animation duration; absolute-time keyframes may infer total duration from the greatest absolute keyframe when no longer Duration is declared.
Keyframe bodies contain ordinary typed property declarations. Animatable/interpolatable property types interpolate between frames; non-interpolatable properties switch at the relevant frame boundary.
Animation control properties MUST cover at least Duration, Delay, Easing, Repeat/iteration, direction/playback and reduced-motion behaviour. The exact property registry is versioned with CUI.
Assigning/changing Animation while an element is active starts/restarts it according to its playback properties. An animation applied by a temporary Style follows that Style's lifetime unless its action explicitly commits/persists animation state.
Theme, Definition and property interaction
Theme defaults, Type/Definition defaults, explicit properties, property regions, Styles, animations and runtime changes MUST all resolve through the same registered CUI property system. There MUST NOT be separate incompatible colour/layout/transform implementations for each syntax.
Theme changes at runtime MUST invalidate only affected presentation state and preserve canonical app/domain state.
Runtime loading and element methods
Named/ID-addressable elements expose generated typed runtime handles. Runtime APIs MUST provide property mutation plus lifecycle/content operations including Active(...), Refresh(), Text/Children Replace/Add and placement/load functions where supported.
Runtime calls and markup directives MUST converge on the same underlying CUI property/lifecycle/action operations so behaviour does not depend on how a mutation was initiated.
Imports and project file discovery
The CUI toolchain automatically indexes supported local .cui, .cui-theme and .cui-prefab files within the declared project/source roots. External/shared packages use Import.
<Import Source:"package/path">
Ambiguous symbols from multiple imports MUST fail until disambiguated, including by an explicit alias such as As:SharedUI. Circular Definition/Prefab import dependencies are rejected.
Accessibility, focus and keyboard semantics
CUI MUST make accessible behaviour the default. Interactive built-ins provide appropriate semantic roles, focusability and keyboard behaviour unless explicitly overridden for a valid reason.
Framework accessibility/focus properties include:
* AccessibleName and AccessibleDescription.
* Role.
* TabIndex and Focusable.
* Shortcut and focus-state presentation.
Focus order follows semantic/tree/layout order unless explicitly overridden. Inactive elements cannot receive user focus. Hidden-but-interactive controls are technically representable under CUI's Hidden semantics, but first-party accessibility lint MUST reject invisible focusable/interactive constructs unless an explicit justified accessibility contract is supplied.
Reduced motion, high contrast, scaling/zoom and non-colour-only state communication MUST be framework-level capabilities rather than app-specific reinventions.
Localisation and directionality
CUI MUST support resource-backed/localisable user-visible strings, locale-aware number/date formatting and right-to-left directionality. Direction/layout mirroring is framework-managed where applicable. Literal developer/debug strings MAY remain literal; shipping product copy SHOULD use the localisation/resource system.
Security and dynamic CUI
First-party compiled .cui, Plugin CUI and Generative UI are different trust classes.
* Compiled first-party CUI may reference its declared application contracts.
* Plugin/external CUI surfaces execute inside their declared/authorised host capability environment and do not gain action permissions merely by rendering controls.
* Generative UI is structured data rendered through approved CUI components/actions and MUST NOT become arbitrary trusted .cui/C# execution.
Dynamic Children strings are parsed as CUI only when the caller intentionally uses Children. User/external text should normally use Text or an explicit AddText-equivalent. Dynamic markup MUST be constrained to registered/authorised elements, properties, variables and actions of the current host.
Error boundaries and failure behaviour
Static syntax/type/property/binding errors are build/compile errors with file, line and column diagnostics and, where safe, suggested corrections.
Runtime failures from dynamic CUI, plugin content, specialised Object hosts, data or async actions MUST be isolated at the nearest declared/default error boundary where feasible so one failed component does not unnecessarily destroy the entire window/application.
An invalid dynamic CUI update must preserve the last valid tree/content. Unknown incompatible compiled CUI/runtime versions fail with a structured compatibility error.
Hot reload and developer tooling
CUI tooling MUST include compiler/build integration, formatter, linter, hot reload, component/Prefab preview, semantic tree inspector, accessibility inspector, binding/variable dependency inspection, Theme/resource inspection and rendering/performance diagnostics.
Hot reload preserves the current valid runtime tree when edited CUI is invalid. Once valid again, compatible nodes are patched while preserving state by stable IDs where unambiguous. Definition/Theme/Prefab changes update compatible live instances without arbitrarily resetting unrelated runtime state.
Testing and semantic selectors
CUI MUST expose a semantic test/accessibility tree so automated tests can locate elements by ID, role, accessible name and other stable semantic selectors rather than raw screen coordinates when semantic targeting exists.
The test harness MUST be able to render a CUI Page/Prefab at declared dimensions, Theme, scale, accessibility/reduced-motion settings and platform capability profile; interact through semantic input; inspect resulting state; and capture deterministic visual evidence where applicable.
Performance and rendering
CUI MUST use Avalonia's mature low-level rendering/composition foundations while adding CUI-level invalidation, dependency and component semantics without introducing unnecessary full-tree work.
* Live variable changes invalidate only dependent expressions/properties/subtrees.
* Virtualisable lists/grids/trees MUST use framework virtualisation rather than constructing every off-screen item by default.
* Backdrop Blur/Refract/Gradient processing MAY use compositor caching, but cache invalidation MUST remain live and correct when sampled content changes.
* Animation/input loops MUST avoid blocking application/domain work on the UI thread.
* Caches must be bounded and revision/dependency invalidated.
Performance acceptance requires measured evidence on the declared supported-hardware/platform baseline. 'Looks fast' is not sufficient.
Platform and Home runtime integration
Native CUI applications depend on the compatible Home-managed CUI/shared runtime under the global Home runtime-dependency contract. The CUI compiler/toolchain belongs to the development/build environment; the runtime version/capability requirement is recorded in app/package metadata.
Web CUI MAY use a browser-specific implementation/runtime, but it MUST preserve the same public CUI semantics, component contracts, state identities and supported action/property behaviour except for explicitly declared platform capability differences.
Platform differences such as unsupported backdrop effects, specialised renderer capabilities or input hardware MUST be capability-negotiated explicitly. The runtime MUST NOT silently reinterpret unsupported CUI into materially different behaviour.
Language/runtime versioning
Compiled CUI output MUST record at least CUI language version, CUI runtime ABI/component-registry version and required capability set. Breaking language/runtime changes require migration tooling or explicit compatibility handling.
Custom distributed Themes/Prefabs/packages SHOULD carry their own version/dependency metadata. Missing/incompatible package versions surface as structured build/runtime dependency errors.
CUI compiler pipeline
The canonical pipeline is parse -> shorthand normalization -> symbol/import resolution -> Definition/Prefab/Theme expansion metadata -> typed property/variable/action validation -> accessibility/static lint -> dependency graph generation -> compiled CUI representation/source maps -> runtime binding.
The compiler MUST NOT silently accept unknown attributes/tags/types/events/properties. Typographical mistakes such as Type:Vertcal_Stack SHOULD produce an unknown-symbol error with an unambiguous suggestion such as Vertical Stack when available.
Acceptance criteria
CUI is implementation-complete only when real compiled/runtime behaviour proves the agreed language and framework semantics, including CUI-CONF-001–004 and CUI-THEME-001–004. Source presence, parsing a toy file or rendering a static Button is insufficient. Acceptance MUST include compiler-negative tests for forbidden donor tags/namespaces/attached layout syntax, unknown tags/properties, ambiguous shorthand and production .axaml/.hui input; compiler-positive tests for all canonical elements, Types and legitimate Definitions; runtime property-cascade/theme-isolation tests; and inventory-based integration checks against real first-party applications. A canonical parser beside an unchanged legacy shipping host does not pass.
Release validation MUST demonstrate at minimum:
* A real app surface authored only in .cui with .axaml/.hui rejected as app markup while using the pinned Avalonia fork underneath.
* Fixed core elements, Container Grid/Vertical Stack/Horizontal Stack/Absolute Types, scrolling and mature layout properties.
* Colon attribute shorthand, underscore-to-space shorthand, comments, strict ambiguity detection and explicit quoted disambiguation.
* (Definition) custom elements and Types plus built-in/custom inherited Themes including Professional, Glow, Playful, Frosted Glass, Bubble, Metallic, Retro and Western, with Cinematic legacy compatibility.
* Static and dynamic Prefabs loaded in multiple hosts, proving shared versus per-instance internal state.
* Live/default and static variable references, typed conversion forms and Live(x,y) dependency relationships without per-frame polling.
* ID/Name aliases, :: ID shorthand, ::: Group shorthand, scoped Prefab IDs/Groups and deterministic cross-scope access.
* Property long form, $ shorthand, property IDs/Groups, inferred single-property Group mutation and mandatory explicit property selection for multi-property Groups.
* Text versus Children semantics, Replace/Add and rollback on invalid dynamic child markup.
* Active, Hidden, Interactive and !/ActiveIndependently lifecycle semantics including restoration of stored state.
* Layer tag/property with positive/negative values, element override, same-layer ordering and separate Modal behaviour.
* Absolute decimal ratio coordinates, x/y pixel and percent modes, X-Inherited/Y-Inherited recalibration and nested independent axis scopes.
* Prefab/UI placement at Anchor, raw coordinates, pixels, percentages, above/below/left/right of elements and Grid positions, including live relative tracking.
* Style rules with Hover/OnceHover, Clicked/OnceClick, Loaded/OnceLoaded and Deactivated/OnceDeactivated plus TargetID/TargetGroup and {::}/{:::} shorthand.
* Event.Source for Group triggers, opt-in bubbling/cancellation and typed async action invocation.
* Conditional If/Else and identity-preserving Repeat over live collections.
* Two-way Input write-back with conversion/validation failure handling.
* Rotate/Scale/Skew/Translate and representative CSS/Avalonia-like visual/layout properties through the common typed registry.
* Element Effect:Blur and live Background:Blur/Refract modifier chains proving that underlying elements remain semantically/visually unchanged outside the backdrop view.
* A backdrop over changing/animated/video content updates live without Refresh().
* Linear and Radial gradients with explicit and evenly distributed stops.
* Named Animation: application using <@Name>/<Keyframe Name=...>, percentage and millisecond keyframes, interpolation, easing/repeat and reduced-motion handling.
* Hot reload preserving the last valid UI and stable state across compatible updates.
* Keyboard-only operation, screen-reader semantics, high-contrast/reduced-motion/scaling and accessibility lint.
* Semantic UI automation/test selection without coordinate clicking when IDs/roles are available.
* Native and web/platform capability negotiation plus language/runtime version mismatch handling.
Any implementation that recreates CUI as an AXAML preprocessor, uses polling for ordinary live variables, treats Hidden as unloaded, treats Active as destructive property reset, mutates underlying elements through Background backdrop modifiers, snapshots Blur/Refract instead of keeping them live, silently guesses ambiguous shorthand, partially applies incompatible Group mutations, or exposes unreviewed donor/property capability gaps MUST fail release conformance.
