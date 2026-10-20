# Lane 2 → lane 1/root integration patch: `CuiActionDefinition` four-argument binary compatibility

`Core/CuiDocument.cs` is owned by lane 1/root. Lane 2 does not edit it. This is the **exact
minimal integration patch** lane 1/root must apply so precompiled binaries that referenced the old
four-parameter CLR constructor and the old four-output `Deconstruct` keep working.

## Why a source-only claim is insufficient

The current record has a single five-parameter primary constructor with an optional fifth
`LocalTarget`:

```csharp
public sealed record CuiActionDefinition(
    string Name, string Command, CuiValue? Parameter, CuiSourceSpan Span, string? LocalTarget = null);
```

An optional fifth parameter lets *new source* call four-argument form, but it does **not** emit the
four-parameter CLR constructor old binaries bind to, and it does not regenerate the four-output
`Deconstruct` that the old four-parameter record produced. This is a binary-compatibility gap, not a
source-compatibility one.

## Exact patch (apply to `framework/CUI/Core/CuiDocument.cs`)

Replace the current record declaration with:

```csharp
public sealed record CuiActionDefinition(
    string Name,
    string Command,
    CuiValue? Parameter,
    CuiSourceSpan Span,
    string? LocalTarget = null)
{
    // Binary compatibility: old precompiled consumers referenced the four-parameter CLR constructor.
    public CuiActionDefinition(string name, string command, CuiValue? parameter, CuiSourceSpan span)
        : this(name, command, parameter, span, LocalTarget: null)
    {
    }

    // Binary compatibility: the old four-parameter record generated a four-output Deconstruct; keep
    // that overload available alongside the five-output one generated from the primary constructor.
    public void Deconstruct(out string name, out string command, out CuiValue? parameter, out CuiSourceSpan span) =>
        (name, command, parameter, span) = (Name, Command, Parameter, Span);
}
```

## What this preserves

- The five-parameter primary constructor (used by current code and `with` expressions) is unchanged.
- A real four-parameter CLR constructor is added, forwarding to the five-parameter primary.
- The four-output `Deconstruct` overload is restored for old consumers.
- No production metadata, ABI, or capability logic is touched.

## How lane 2 verifies it (NOT RUN until .NET slot)

`framework/CUI/Compiler.Tests/CuiLocalStateAbiOutputTests.cs`:

- `Four_argument_action_definition_clr_constructor_and_deconstruct_are_preserved` asserts via
  reflection that exactly these members exist with the exact parameter types:
  - ctor `(string, string, CuiValue?, CuiSourceSpan)`
  - `Deconstruct(out string, out string, out CuiValue?, out CuiSourceSpan)`
- `Four_argument_action_definition_constructor_form_is_preserved` keeps the source-form checks.

These tests intentionally fail until this patch is applied (they document the required contract). Once
lane 1 applies the patch, they pass. An authentic old-reference binary fixture would be a child
project compiled against the old four-parameter Core and then loaded against the patched Core; that is
a separate integration artifact to run under the allocated slot.

## Ownership

- File: `framework/CUI/Core/CuiDocument.cs` (lane 1/root owns; lane 2 supplies this patch only).
- Test coverage: lane 2 (`framework/CUI/Compiler.Tests/CuiLocalStateAbiOutputTests.cs`).
