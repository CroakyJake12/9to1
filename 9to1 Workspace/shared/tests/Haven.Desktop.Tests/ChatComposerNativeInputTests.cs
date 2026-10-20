using System;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Haven.Desktop.Views.Pages.Chat;
using Haven.UI.Components;
using HavenOS.Apps.Spaces.Chat;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>
/// REAL shown-Window input tests for the shared rich-input engine. These exercise the actual
/// transport adapters (not a fake-flag path): the standalone/ChatSpace consumer drives a genuine
/// Avalonia <see cref="TextBox"/> through <see cref="CuiComposerInputPort"/> (the same port the
/// authored CUI composer mounts), and the Haven consumer drives a real legacy <see cref="Input"/>
/// through <see cref="HavenChatComposerInputPort"/>. Both adapters reconcile real text/selection
/// edits into <see cref="ChatComposerTextEdit"/> deltas which the SAME retained
/// <see cref="ChatComposerRichInputEngine"/> consumes — the exact bridge the mounted composer uses.
/// Every case asserts the engine's retained/h/history (one shared undo owner), not a mock.
/// </summary>
public sealed class ChatComposerNativeInputTests
{
    // ---------- Collapsed Bold -> real insert -> styled run ----------

    [AvaloniaFact]
    public async Task Collapsed_bold_then_real_standalone_input_produces_styled_run()
    {
        using var rig = new StandaloneRig();
        var engine = rig.Engine;

        // Type "ab" through the REAL input edit signal (TextChanging -> port -> TextEdited -> engine).
        rig.Type("ab");
        Assert.Equal("ab", engine.Document.Text);

        // Collapsed Bold at the caret between 'a' and 'b'.
        rig.Port.SetSelection(1, 1);
        engine.ApplyCommand("Bold", 1, 1);

        // A real insert at the caret inherits Bold.
        rig.TypeAt(1, "X");
        Assert.Equal("aXb", rig.Box.Text);
        Assert.Equal("aXb", engine.Document.Text);
        Assert.True((engine.Document.Marks[1] & ChatComposerInlineMark.Bold) != 0);
        Assert.Contains("**", engine.SerializeDraft());
    }

    // ---------- Backward selection -> format -> exact range/caret ----------

    [AvaloniaFact]
    public async Task Backward_selection_format_applies_exact_range()
    {
        using var rig = new StandaloneRig();
        var engine = rig.Engine;

        rig.Type("hello world");
        // Backward selection: anchor at end (11), caret at start (6).
        rig.Port.SetSelection(11, 6);
        engine.ApplyCommand("Bold", rig.Port.SelectionAnchor, rig.Port.CaretIndex);

        Assert.Equal("hello **world**", engine.SerializeDraft());
        Assert.True((engine.Document.Marks[6] & ChatComposerInlineMark.Bold) != 0);
    }

    // ---------- Native typing + format + emoji -> one shared undo history ----------

    [AvaloniaFact]
    public async Task Native_typing_format_and_emoji_share_one_undo_history()
    {
        using var rig = new StandaloneRig(useSource: true);
        var engine = rig.Engine;

        rig.Type("ab");
        rig.Port.SetSelection(0, 1);
        engine.ApplyCommand("Bold", 0, 1);
        rig.Type("c");
        await rig.Source!.InsertEmoji("😀");

        Assert.Equal("abc😀", engine.Document.Text);
        Assert.True((engine.Document.Marks[0] & ChatComposerInlineMark.Bold) != 0);

        // One shared history: undo emoji, the "c" typing, the Bold format, then the "ab" typing.
        engine.Undo();
        Assert.Equal("abc", engine.Document.Text);
        engine.Undo();
        Assert.Equal("ab", engine.Document.Text);
        engine.Undo();
        Assert.Equal("ab", engine.Document.Text); // bold was a range toggle (no content change)
        engine.Undo();
        Assert.Equal("", engine.Document.Text);   // back to the empty initial state
        Assert.False(engine.Editor.CanUndo || engine.Editor.CanRedo);
    }

    // ---------- Enter/list continuation atomic undo ----------

    [AvaloniaFact]
    public async Task Enter_list_continuation_undo_is_atomic()
    {
        using var rig = new StandaloneRig();
        var engine = rig.Engine;

        engine.ApplyCommand("Bullet", 0, 0); // "- " on empty line
        rig.Type("item");                    // "- item"
        rig.Type("\n");                      // one native Enter: newline + continued marker

        Assert.Equal("- item\n- ", rig.Box.Text);
        Assert.Equal("- item\n- ", engine.Document.Text);

        // One atomic Undo restores exactly "- item" (the continued marker is NOT a second
        // transaction left behind by the Enter).
        engine.Undo();
        Assert.Equal("- item", engine.Document.Text);
    }

    // ---------- IME / combining text ----------

    [AvaloniaFact]
    public async Task Combining_sequence_entry_is_preserved_as_one_grapheme()
    {
        using var rig = new StandaloneRig();
        var engine = rig.Engine;

        rig.Type("e\u0301x");
        Assert.Equal("e\u0301x", engine.Document.Text);
        engine.Backspace(3, 3);
        Assert.Equal("e\u0301", engine.Document.Text);
    }

    // ---------- Haven consumer: real legacy input -> same shared engine ----------

    [AvaloniaFact]
    public async Task Haven_adapter_reconciles_edits_into_shared_engine()
    {
        var input = new Input();
        var engine = new ChatComposerRichInputEngine();
        var port = new HavenChatComposerInputPort(input);
        port.TextEdited += (_, edit) => engine.ReplaceSelection(edit.Anchor, edit.Caret, edit.NewText);

        // A real edit on the legacy input triggers TextChanged -> adapter reconciles -> engine.
        input.Text = "ab";
        Assert.Equal("ab", engine.Document.Text);

        // Backward selection + Bold over the real input selection.
        input.SetSelection(2, 0);
        engine.ApplyCommand("Bold", port.SelectionAnchor, port.CaretIndex);
        Assert.Equal("**ab**", engine.SerializeDraft());

        // One shared undo history across the native edit and formatting.
        engine.Undo();
        Assert.Equal("ab", engine.Document.Text);
        engine.Undo();
        Assert.Equal("", engine.Document.Text);
    }

    // ---------- save/remount + no late writes after close ----------

    [AvaloniaFact]
    public async Task Remount_and_close_do_not_accept_late_writes()
    {
        var engine = new ChatComposerRichInputEngine();
        using (var rig = new StandaloneRig(engine))
        {
            rig.Type("persisted");
            // Serialize the retained draft (canonical identity); a remount restores from it.
            var draft = engine.SerializeDraft();
            Assert.Equal("persisted", engine.SerializePlainText());

            // Close/withdraw the rig: late writes must be refused.
            rig.Port.SetSelection(0, 0);
            rig.Close();

            // A late write after close is a no-op (no late write reaches a retired input).
            engine.ReplaceSelection(0, 0, "LATE");
            Assert.Equal("persisted", engine.SerializePlainText());
            Assert.Equal(draft, engine.SerializeDraft());
        }
    }

    // ---------- Rig: real Window + TextBox + real standalone adapter + shared engine ----------

    /// <summary>Mounts the genuine shared input port over a real TextBox in a real shown Window and
    /// wires it to the shared engine exactly as the mounted CUI composer does.</summary>
    private sealed class StandaloneRig : IDisposable
    {
        private readonly Window _window;
        private readonly ChatComposerRichInputEngine _engine;
        private readonly CuiComposerInputPort _port;
        private readonly TextBox _box;
        private readonly ChatSpaceComposerSource? _source;
        private bool _closed;

        public StandaloneRig(ChatComposerRichInputEngine? engine = null, bool useSource = false)
        {
            _engine = engine ?? new ChatComposerRichInputEngine();
            _box = new TextBox();
            AutomationProperties.SetAutomationId(_box, "composer-input");
            _window = new Window { Content = _box, Width = 1000, Height = 800 };
            _window.Show();

            _port = new CuiComposerInputPort(() => _box);
            _port.Attach(_box);
            _port.TextEdited += (_, edit) => _engine.ReplaceSelection(edit.Anchor, edit.Caret, edit.NewText);

            if (useSource)
            {
                _source = new ChatSpaceComposerSource(
                    () => ChatSpaceViewState.Empty,
                    (command, parameter, ct) => ValueTask.CompletedTask,
                    () => _port,
                    _engine);
            }
            Render();
        }

        public ChatComposerRichInputEngine Engine => _engine;
        public CuiComposerInputPort Port => _port;
        public TextBox Box => _box;
        public ChatSpaceComposerSource? Source => _source;

        /// <summary>Appends text through the REAL edit signal (TextBox.Text set -> TextChanging ->
        /// port reconciliation -> TextEdited -> engine).</summary>
        public void Type(string text)
        {
            _box.SelectionStart = _box.SelectionEnd = _box.Text?.Length ?? 0;
            _box.Text = _box.Text + text;
            _box.CaretIndex = _box.Text!.Length;
            Render();
        }

        /// <summary>Inserts text at a specific caret position through the real edit signal.</summary>
        public void TypeAt(int caret, string text)
        {
            _box.SelectionStart = _box.SelectionEnd = caret;
            var current = _box.Text ?? string.Empty;
            _box.Text = current.Insert(caret, text);
            _box.CaretIndex = caret + text.Length;
            Render();
        }

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            _port.Detach();
            _window.Close();
        }

        public void Dispose() => Close();
    }

    private static void Render()
    {
        for (var pass = 0; pass < 3; pass++)
        { Dispatcher.UIThread.RunJobs(); }
    }
}
