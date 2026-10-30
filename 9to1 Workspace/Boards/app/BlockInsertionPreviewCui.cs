using System.ComponentModel;
using Avalonia.Controls;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;

namespace CakeOS.Apps.Boards.App;

internal static class BlockInsertionPreviewCui
{
    internal static Control Build(BoardsViewModel vm, string rowId)
    {
        var context = new PreviewContext(vm, rowId);
        var loader = BoardsCuiDocuments.CreateLoader();
        loader.SetBindingContext(context);
        var (root, diagnostics) = loader.TryLoad(BoardsCuiDocuments.Load(BoardsCuiDocuments.InsertionPreviewSourceName));
        if (root is null)
        {
            loader.Dispose();
            throw new InvalidOperationException("Insertion preview CUI: " + string.Join("; ", diagnostics.Select(d => d.Message)));
        }
        loader.WireBindings(root);
        root.AttachedToVisualTree += (_, _) =>
        {
            vm.InsertionPreviewChanged += context.Refresh;
            context.Refresh();
        };
        root.DetachedFromVisualTree += (_, _) =>
        {
            vm.InsertionPreviewChanged -= context.Refresh;
            loader.Dispose();
        };
        return root;
    }

    private sealed class PreviewContext(BoardsViewModel vm, string rowId) : ICuiBindingContext, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        public bool TryGetValue(string path, out object? value)
        {
            var preview = vm.InsertionPreview;
            var placement = preview is not null && preview.Owner.Row.Id == rowId &&
                vm.IsInsertionPreviewCurrent(preview.Owner) ? preview.Placement : (BlockInsertionPlacement?)null;
            value = path switch
            {
                "Above" => placement == BlockInsertionPlacement.Above,
                "Below" => placement == BlockInsertionPlacement.Below,
                "Replace" => placement == BlockInsertionPlacement.Replace,
                _ => null,
            };
            return value is not null;
        }
    }
}
