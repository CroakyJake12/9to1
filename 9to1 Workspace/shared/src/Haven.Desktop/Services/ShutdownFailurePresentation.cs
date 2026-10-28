namespace Haven.Desktop.Services;

/// <summary>A bounded view of retained shutdown diagnostics; never a retirement acknowledgement.</summary>
internal sealed record ShutdownFailurePresentation(string Summary, string Details)
{
    internal static ShutdownFailurePresentation From(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        const int maximumNodes = 256, maximumCauses = 8, maximumMessageLength = 240;
        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var messages = new HashSet<string>(StringComparer.Ordinal);
        var details = new List<string>();
        pending.Push(failure);
        while (pending.Count > 0 && visited.Count < maximumNodes && details.Count < maximumCauses)
        {
            var current = pending.Pop();
            if (!visited.Add(current)) continue;
            if (current is AggregateException aggregate && aggregate.InnerExceptions.Count > 0)
            {
                // Do not evaluate AggregateException.Message: it recursively repeats every child.
                for (var i = Math.Min(aggregate.InnerExceptions.Count, maximumNodes - visited.Count) - 1; i >= 0; i--)
                    pending.Push(aggregate.InnerExceptions[i]);
                continue;
            }
            if (current.InnerException is { } inner) { pending.Push(inner); continue; }
            var message = current.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (message.Length > maximumMessageLength) message = message[..maximumMessageLength] + "…";
            if (message.Length == 0) message = current.GetType().Name;
            if (messages.Add(message)) details.Add("• " + message);
        }
        if (pending.Count > 0) details.Add("• Further details are retained in the application log.");
        return new("Some content could not close safely. The window has been kept open.",
            details.Count == 0 ? "See the application log for the retained failure." : string.Join(Environment.NewLine, details));
    }

    internal static void WriteDiagnostic(TextWriter writer, Exception failure)
    {
        // AggregateException.ToString/Message repeat shared child graphs recursively.
        // Write each original node once with its edges, preserving the causal graph.
        var ids = new Dictionary<Exception, int>(ReferenceEqualityComparer.Instance) { [failure] = 1 };
        var pending = new Queue<Exception>();
        pending.Enqueue(failure);
        while (pending.TryDequeue(out var current))
        {
            writer.WriteLine($"[9to1 shutdown cause {ids[current]}] {current.GetType().FullName}");
            if (current is not AggregateException) writer.WriteLine(current.Message);
            if (!string.IsNullOrEmpty(current.StackTrace)) writer.WriteLine(current.StackTrace);
            IEnumerable<Exception> children = current is AggregateException aggregate
                ? aggregate.InnerExceptions : current.InnerException is { } inner ? [inner] : [];
            foreach (var child in children)
            {
                if (!ids.TryGetValue(child, out var id))
                {
                    id = ids.Count + 1;
                    ids.Add(child, id);
                    pending.Enqueue(child);
                }
                writer.WriteLine($"  retained cause -> {id}");
            }
        }
    }
}
