namespace Haven.Core;

/// <summary>Detaches the existing maximum 256 scalar proposals without trusting caller collection metadata.</summary>
public static class DataRecordCreationValues
{
    public sealed class CapacityExceededException() : ArgumentException("Create at most 256 canonical scalar fields.", "values") { }

    public static Dictionary<Guid, DataScalarRecordValue> Capture(IReadOnlyDictionary<Guid, DataScalarRecordValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var captured = new Dictionary<Guid, DataScalarRecordValue>();
        var consumed = 0;
        foreach (var pair in values)
        {
            if (++consumed > 256) throw new CapacityExceededException();
            if (captured.ContainsKey(pair.Key)) throw new ArgumentException("Duplicate canonical scalar field.", nameof(values));
            captured.Add(pair.Key, DataRecordEdits.Capture(pair.Value));
        }
        return captured;
    }
}
