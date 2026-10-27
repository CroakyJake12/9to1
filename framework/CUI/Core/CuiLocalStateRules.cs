using System.Globalization;

namespace CakeOS.Cui;

/// <summary>Compiler/compiled-admission parity with the existing bounded runtime state types.</summary>
public static class CuiLocalStateRules
{
    public static bool IsInteger(string type) => string.Equals(type, "int", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "integer", StringComparison.OrdinalIgnoreCase);
    public static bool IsBoolean(string type) => string.Equals(type, "bool", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "boolean", StringComparison.OrdinalIgnoreCase);
    public static bool IsSettable(string type) => IsBoolean(type) || IsInteger(type) || string.Equals(type, "string", StringComparison.OrdinalIgnoreCase);
    public static bool AcceptsLiteral(string type, string value) => IsBoolean(type) ? bool.TryParse(value, out _) :
        IsInteger(type) ? int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) : string.Equals(type, "string", StringComparison.OrdinalIgnoreCase);
    public static bool AcceptsConditionPath(IReadOnlyDictionary<string, string> variables, string path)
    {
        var root = path.Split('.')[0];
        // Undeclared names are ordinary host bindings, like conditions on If. The runtime
        // resolves their current value and refuses any present non-boolean value.
        if (!variables.TryGetValue(root, out var type)) return true;
        return IsBoolean(type) ? path == root :
            string.Equals(type, "commandstate", StringComparison.OrdinalIgnoreCase) && path.Length > root.Length + 1;
    }
}
