using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
namespace NineToOne.Admin;
public static class AdminNativeScene
{
    public static CuiNativeScene Create(ICuiBindingContext bindings,ICuiActionDispatcher actions,ICuiSceneReadiness authenticatedHomeReadiness)
    {
        using var stream=typeof(AdminNativeScene).Assembly.GetManifestResourceStream("NineToOne.Admin.UI.Admin.cui")??throw new InvalidDataException("Canonical Admin CUI surface is missing.");
        using var reader=new StreamReader(stream);
        return new("9to1.Admin","9to1 Admin","Admin",new CuiRichParser().Parse(reader.ReadToEnd()),bindings,actions,authenticatedHomeReadiness);
    }
}
