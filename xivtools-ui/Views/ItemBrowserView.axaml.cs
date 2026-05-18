using Avalonia.Controls;
using Avalonia.Input;
using XivToolsUI.ViewModels;

namespace XivToolsUI.Views;

public partial class ItemBrowserView : UserControl
{
    public ItemBrowserView() { InitializeComponent(); }

    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ItemBrowserViewModel vm)
            vm.SearchByNameCommand.Execute(null);
    }

    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ItemBrowserViewModel vm)
            vm.LookupRootCommand.Execute(null);
    }
}
