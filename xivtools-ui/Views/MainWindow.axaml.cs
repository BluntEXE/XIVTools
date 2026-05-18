using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using XivToolsUI.ViewModels;

namespace XivToolsUI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent,     OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = e.DataTransfer.Formats.Any(f => f.Kind == DataFormatKind.Universal &&
                                                        f.Identifier == "File");
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Collect all dropped file paths
        var paths = e.DataTransfer.Items
            .Select(item => {
                try { return (item.TryGetRaw(DataFormat.File) as IStorageItem)?.Path.LocalPath; }
                catch { return null; }
            })
            .Where(p => p != null)
            .Select(p => p!)
            .ToList();

        var modpack = paths.FirstOrDefault(p =>
            p.EndsWith(".ttmp2", System.StringComparison.OrdinalIgnoreCase) ||
            p.EndsWith(".ttmp",  System.StringComparison.OrdinalIgnoreCase) ||
            p.EndsWith(".pmp",   System.StringComparison.OrdinalIgnoreCase));

        if (modpack == null) return;

        vm.Modpack.InputPath  = modpack;
        vm.Modpack.OutputPath = Path.ChangeExtension(modpack, null) + "_DT.pmp";
        vm.Modpack.OperationLog = $"Dropped: {Path.GetFileName(modpack)}\nReady to upgrade or convert.";
        vm.NavigateToModpackCommand.Execute(null);
        vm.SetStatus($"Dropped {Path.GetFileName(modpack)} — click Upgrade to Dawntrail or Convert Format");
        e.Handled = true;
    }
}
