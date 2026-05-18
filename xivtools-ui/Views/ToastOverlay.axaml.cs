using Avalonia.Controls;
using XivToolsUI.ViewModels;
namespace XivToolsUI.Views;
public partial class ToastOverlay : UserControl {
    public ToastOverlay() { InitializeComponent(); DataContext = new ToastOverlayContext(); }
}
