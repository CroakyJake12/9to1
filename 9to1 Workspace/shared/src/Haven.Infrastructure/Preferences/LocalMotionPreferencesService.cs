using System.Text.Json;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>
/// Stores visual-motion preferences independently from model and workspace settings so the
/// shell can honour reduced motion before any page view model has finished initialising.
/// </summary>
public sealed class LocalMotionPreferencesService : IMotionPreferences
{
    private const int MaximumPreferenceBytes = 65_536;
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// Stores lazy current locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Lazy<LocalMotionPreferencesService> LazyCurrent = new(() => new LocalMotionPreferencesService());
    /// <summary>
    /// Stores gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly object _gate = new();
    /// <summary>
    /// Stores path locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _path;
    /// <summary>
    /// Stores preferences locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private MotionPreferences _preferences;
    private long _observationRevision;

    private LocalMotionPreferencesService() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Haven", "ui-preferences.json")) { }

    /// <summary>Trusted host composition supplies an existing preference file; this is not an app-request path.</summary>
    public LocalMotionPreferencesService(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Motion preferences need an absolute host path.", nameof(path));
        _path = Path.GetFullPath(path); _preferences = Load();
    }

    /// <summary>Re-reads the same existing OS-local preference file, allowing separate native processes
    /// to observe changes before advancing playback. Failed reads retain the cache but report unavailable.</summary>
    public async ValueTask<VisualMotionPreferenceSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long observedRevision;
        lock (_gate) observedRevision = _observationRevision;
        MotionPreferences observed;
        try
        {
            await using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                4096, FileOptions.Asynchronous);
            var buffer = new byte[MaximumPreferenceBytes + 1]; var length = 0;
            while (length < buffer.Length)
            {
                var read = await input.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                length += read;
            }
            if (length > MaximumPreferenceBytes) return new(ReduceAnimations, false);
            observed = Deserialize(buffer.AsSpan(0, length));
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { observed = new(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return new(ReduceAnimations, false); }
        bool changed;
        lock (_gate)
        {
            if (_observationRevision != observedRevision) return new(_preferences.ReduceAnimations, true);
            changed = _preferences.ReduceAnimations != observed.ReduceAnimations;
            _preferences = observed;
            if (changed) _observationRevision++;
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
        return new(observed.ReduceAnimations, true);
    }

    /// <summary>
    /// Gets or updates current, the bindable or domain state represented by this property.
    /// </summary>
    public static LocalMotionPreferencesService Current => LazyCurrent.Value;

    public bool ReduceAnimations
    {
        get
        {
            lock (_gate)
                return _preferences.ReduceAnimations;
        }
    }

    internal long ObservationRevision { get { lock (_gate) return _observationRevision; } }

    /// <summary>
    /// Stores changed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Performs the set reduce animations step owned by this component.
    /// </summary>
    public void SetReduceAnimations(bool value)
    {
        lock (_gate)
        {
            if (_preferences.ReduceAnimations == value) return;
            var next = _preferences with { ReduceAnimations = value };
            if (!Save(next)) return;
            _preferences = next;
            _observationRevision++;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Performs the load step owned by this component.
    /// </summary>
    private MotionPreferences Load()
    {
        try
        {
            using var input = new FileStream(_path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[MaximumPreferenceBytes + 1];
            var length = 0;
            while (length < buffer.Length)
            {
                var read = input.Read(buffer.AsSpan(length));
                if (read == 0) break;
                length += read;
            }
            if (length > MaximumPreferenceBytes) return ConservativeMotionPreferences();
            return Deserialize(buffer.AsSpan(0, length));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        { return new MotionPreferences(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // Keep animation reduced until a valid user preference is recovered.
            return ConservativeMotionPreferences();
        }
    }

    private static MotionPreferences Deserialize(ReadOnlySpan<byte> json) =>
        JsonSerializer.Deserialize<MotionPreferences>(json, JsonOptions) ?? throw new JsonException("Missing motion preference record.");

    private static MotionPreferences ConservativeMotionPreferences() => new() { ReduceAnimations = true };

    /// <summary>
    /// Performs the save step owned by this component.
    /// </summary>
    private bool Save(MotionPreferences preferences)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(preferences, JsonOptions);
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine("[9to1 motion preferences] " + exception.Message);
            return false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { System.Diagnostics.Debug.WriteLine("[9to1 motion preferences] temporary cleanup failed: " + exception.Message); }
        }
    }

    /// <summary>
    /// Represents motion preferences and keeps its related state and behavior together.
    /// </summary>
    private sealed record MotionPreferences
    {
        /// <summary>
        /// Gets or updates reduce animations, the bindable or domain state represented by this property.
        /// </summary>
        public bool ReduceAnimations { get; init; }
    }
}
