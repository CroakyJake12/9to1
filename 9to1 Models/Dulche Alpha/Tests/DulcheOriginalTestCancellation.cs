// The selected v3 project supplies the genuine original test-host cancellation token.
// The sibling v2 project has no TestContext API; preserve its former default token there.
internal static class DulcheOriginalTestCancellation
{
    public static System.Threading.CancellationToken Current
    {
        get
        {
#if ASTRA_DULCHE_XUNIT_V3
            return Xunit.TestContext.Current.CancellationToken;
#else
            return System.Threading.CancellationToken.None;
#endif
        }
    }
}
