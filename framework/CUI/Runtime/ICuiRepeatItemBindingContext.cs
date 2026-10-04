namespace CakeOS.Cui.Runtime;

/// <summary>
/// Optional host bridge for resolving and updating fields on repeated domain items
/// without runtime reflection. Dictionary items and items implementing
/// <see cref="CakeOS.Cui.ICuiBindingContext"/> are handled by the built-in scope.
/// </summary>
public interface ICuiRepeatItemBindingContext
{
    bool TryGetItemValue(object item, string path, out object? value);
    bool TrySetItemValue(object item, string path, object? value);
}
