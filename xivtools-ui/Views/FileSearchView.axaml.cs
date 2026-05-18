using Avalonia.Controls;
using Avalonia.Input;
using XivToolsUI.ViewModels;
namespace XivToolsUI.Views;
public partial class FileSearchView : UserControl {
    public FileSearchView() { InitializeComponent(); }
    private void OnKeyDown(object? s, KeyEventArgs e) {
        if (e.Key == Key.Enter && DataContext is FileSearchViewModel vm)
            vm.SearchCommand.Execute(null);
    }
}
