using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using XivToolsUI.ViewModels;
using XivToolsUI.Views;

namespace XivToolsUI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Load Material Icons styles programmatically to avoid XAML precompile issues
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://XivToolsUI/"))
        {
            Source = new Uri("avares://Material.Icons.Avalonia/MaterialIconStyles.axaml")
        });
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://XivToolsUI/"))
        {
            Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml")
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}