namespace NineToOne.Cui.AI;

/// <summary>The single authored shared CUI bar. Hosts adapt the existing state and typed invocation editor.</summary>
public static class FloatingAiBarScene
{
    public static string ReadSource()
    {
        using var stream = typeof(FloatingAiBarScene).Assembly.GetManifestResourceStream("NineToOne.Cui.AI.UI.FloatingAiBar.cui")
            ?? throw new InvalidOperationException("The authored shared AI bar is not packaged.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
