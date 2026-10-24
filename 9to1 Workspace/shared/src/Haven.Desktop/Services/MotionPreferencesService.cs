/*
 * FILE DOCUMENTATION
 * Where: src/Haven.OldHaven/Services/MotionPreferencesService.cs, in the Desktop services layer, adapting application behavior to Windows and Avalonia concerns.
 * What: This file owns MotionPreferencesService, MotionPreferences. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;

namespace Haven.Desktop.Services;

/// <summary>
/// Stores visual-motion preferences independently from model and workspace settings so the
/// shell can honour reduced motion before any page view model has finished initialising.
/// </summary>
public sealed class MotionPreferencesService
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
    private static readonly Lazy<MotionPreferencesService> LazyCurrent = new(() => new MotionPreferencesService());
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

    private MotionPreferencesService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Haven", "ui-preferences.json"))
    {
    }

    internal MotionPreferencesService(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _preferences = Load();
    }

    /// <summary>
    /// Gets or updates current, the bindable or domain state represented by this property.
    /// </summary>
    public static MotionPreferencesService Current => LazyCurrent.Value;

    public bool ReduceAnimations
    {
        get
        {
            lock (_gate)
                return _preferences.ReduceAnimations;
        }
    }

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
        {
            return new MotionPreferences();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A present but unreadable accessibility preference must not silently
            // become permission to run motion-heavy presentation.
            return ConservativeMotionPreferences();
        }
    }

    private static MotionPreferences Deserialize(ReadOnlySpan<byte> json) =>
        JsonSerializer.Deserialize<MotionPreferences>(json, JsonOptions) ?? ConservativeMotionPreferences();

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
