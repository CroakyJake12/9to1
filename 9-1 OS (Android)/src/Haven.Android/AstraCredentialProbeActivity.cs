#if ASTRA_ANDROID_CREDENTIAL_PROBE
using Android.App;
using Android.OS;

namespace Haven.Android;

/// <summary>Explicit isolated-validation activity. Normal production builds do not contain this component.</summary>
[Activity(Name = "com.cakemods.haven.AstraCredentialProbeActivity", Exported = true, NoHistory = true,
    Theme = "@style/Theme.AppCompat.Light.NoActionBar")]
public sealed class AstraCredentialProbeActivity : Activity
{
    private const string Tag = "ASTRA_CREDENTIAL_PROBE";
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationToken _token;
    private string _phase = "invalid", _run = "invalid", _head = "invalid";
    private int _positive;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _token = _lifetime.Token;
        _ = RunAsync(); // Every probe failure is observed below and produces a failing completion.
    }
    protected override void OnDestroy()
    {
        _lifetime.Cancel(); _lifetime.Dispose(); base.OnDestroy();
    }
    private async Task RunAsync()
    {
        try
        {
            var phase = Intent?.GetStringExtra("astra_phase");
            var run = Intent?.GetStringExtra("astra_run");
            var head = Intent?.GetStringExtra("astra_head");
            if (phase is not ("write-read" or "read-only") || run is null || run.Length is < 1 or > 20 ||
                run.Any(c => c is < '0' or > '9') || head is null || head.Length != 40 || head.Any(c => !char.IsAsciiHexDigit(c)))
                throw new ProbeFailure("InvalidBoundProbeArguments");
            _phase = phase; _run = run; _head = head.ToLowerInvariant();
            // Reserved fictional fixture only; no real credential name/value is accepted from an Intent.
            var key = "astra.validation.credentials." + _run + "." + _head;
            var expected = "astra-fictional-credential:" + _run + ":" + _head;
            if (_phase == "write-read")
            {
                var writer = new AndroidEncryptedPreferenceStore();
                if (await writer.GetAsync(key, _token) is not null)
                    throw new ProbeFailure("FixtureAlreadyExists");
                await writer.SetAsync(key, expected, _token);
            }
            // Two new production-store objects, not a cached plaintext or synthetic crypto port.
            for (var i = 0; i < 2; i++)
            {
                _token.ThrowIfCancellationRequested();
                var reader = new AndroidEncryptedPreferenceStore();
                var actual = await reader.GetAsync(key, _token);
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                    throw new ProbeFailure("FixtureReadbackMismatch");
                _positive++;
            }
            _token.ThrowIfCancellationRequested();
            global::Android.Util.Log.Info(Tag, Marker("PASS", "none"));
        }
        catch (Exception exception)
        {
            var code = exception switch
            {
                AndroidCredentialRecoveryRequiredException recovery => recovery.Code,
                ProbeFailure failure => failure.Code,
                System.OperationCanceledException => "Cancelled",
                _ => "ProbeOperationFailed"
            };
            // Report only fixed status/code/count/binding. Never plaintext, ciphertext, IV or key material.
            global::Android.Util.Log.Error(Tag, Marker("FAIL", code));
        }
        finally
        {
            try { RunOnUiThread(() => { if (!IsFinishing && !IsDestroyed) Finish(); }); }
            catch { global::Android.Util.Log.Error(Tag, Marker("FAIL", "ProbeActivityCloseFailed")); }
        }
    }
    private string Marker(string result, string code) =>
        $"phase={_phase}|run={_run}|head={_head}|uid={(global::Android.OS.Process.MyUid())}|positive={_positive}|result={result}|code={code}|complete=true";
    private sealed class ProbeFailure(string code) : IOException("The reserved validation fixture failed.")
    { public string Code { get; } = code; }
}
#endif
