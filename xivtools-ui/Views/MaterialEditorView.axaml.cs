using Avalonia.Controls;
using Avalonia.Input;
using XivToolsUI.ViewModels;

namespace XivToolsUI.Views;

public partial class MaterialEditorView : UserControl
{
    public MaterialEditorView() { InitializeComponent(); }

    private void OnRowClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: ColorsetRowViewModel row })
            row.IsExpanded = !row.IsExpanded;
    }
}
