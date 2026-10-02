namespace Haven.Browser;

public sealed partial class BrowserSessionService
{
    /// <summary>Derives another revocable selection of the SAME actual original host,
    /// not the ambient replacement. Provenance only; no execution permission.</summary>
    public IOriginalBrowserSessionSelection ForkOriginalSession(IOriginalBrowserSessionSelection selected)
    {
        if (selected is not OriginalSelection original || !IsOriginalCurrent(original))
            throw new UnauthorizedAccessException("Actual current original browser selection required.");
        var fork = new OriginalSelection(this, original.Host, original.Generation, original.Document);
        if (!IsOriginalCurrent(original) || !IsOriginalCurrent(fork))
        { fork.Dispose(); throw new UnauthorizedAccessException("Original browser changed during selection fork."); }
        return fork;
    }
}
