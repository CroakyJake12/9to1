using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace Haven.Desktop.Views.Pages.Spaces;

public sealed partial class SpaceDetailsPage
{
    private static CuiDocument LoadDecisionDocument()
    {
        using var stream = typeof(SpaceDetailsPage).Assembly.GetManifestResourceStream("Haven.Desktop.Spaces.SpaceDetailsDecision.cui")
            ?? throw new InvalidOperationException("The authored details decision is missing.");
        using var reader = new StreamReader(stream);
        return new CuiRichParser().Parse(reader.ReadToEnd(), "SpaceDetailsDecision.cui");
    }

    private sealed class Decision
    {
        internal Decision(SpaceDetailsPage page) => Bindings = new(page, this);
        internal CuiSceneHost Host { get; } = new();
        internal DecisionBindings Bindings { get; }
        internal Task<CuiSceneAvailability>? Show;
        internal Task? Close;
        internal TaskCompletionSource<bool> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<Task> Commands { get; } = [];
        internal bool Done;
        internal bool Busy;
        internal ClosePreparation? Lease;
    }

    private sealed class DecisionBindings(SpaceDetailsPage page, Decision decision) : ICuiBindingContext,
        ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Refresh() => PropertyChanged?.Invoke(this, new(null));
        public bool TryGetValue(string path, out object? value)
        {
            lock (page._gate)
            {
                var available = page._live && !decision.Done && !decision.Busy;
                value = path switch
                {
                    "decision.status" => page.State.StatusMessage,
                    "decision.canSave" => available && page.State.CanSave,
                    "decision.canDiscard" => available && page.State.CanDiscard,
                    "decision.canCancel" => available,
                    _ => null
                };
                return value is not null;
            }
        }
        public bool? IsActionAvailable(string command)
        {
            var path = command switch
            {
                "space-details.close.save" => "decision.canSave",
                "space-details.close.discard" => "decision.canDiscard",
                "space-details.close.cancel" => "decision.canCancel",
                _ => null
            };
            return path is not null && TryGetValue(path, out var value) ? value as bool? : null;
        }
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            if (IsActionAvailable(command) != true) throw new InvalidOperationException("This details decision action is unavailable.");
            return new(page.DispatchDecisionAsync(decision, command));
        }
    }
}
