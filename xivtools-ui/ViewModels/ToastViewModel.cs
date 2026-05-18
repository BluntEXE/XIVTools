using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace XivToolsUI.ViewModels;

// Simple wrapper so ToastOverlay can have an x:DataType for compiled bindings
public class ToastOverlayContext
{
    public System.Collections.ObjectModel.ObservableCollection<ToastEntry> Toasts
        => ToastService.Instance.Toasts;
}

public enum ToastKind { Info, Success, Warning, Error }

public partial class ToastEntry : ObservableObject
{
    public string    Message  { get; init; } = "";
    public ToastKind Kind     { get; init; }
    public string    Icon     => Kind switch {
        ToastKind.Success => "CheckCircle",
        ToastKind.Warning => "AlertCircle",
        ToastKind.Error   => "CloseCircle",
        _                 => "InformationOutline"
    };
    public string    Color    => Kind switch {
        ToastKind.Success => "#10B981",
        ToastKind.Warning => "#F59E0B",
        ToastKind.Error   => "#EF4444",
        _                 => "#0EA5E9"
    };
    public string    BgColor  => Kind switch {
        ToastKind.Success => "#0D2B1A",
        ToastKind.Warning => "#1A1200",
        ToastKind.Error   => "#2B0D0D",
        _                 => "#0D1A2B"
    };
    [ObservableProperty] private bool _isVisible = true;
}

public class ToastService
{
    public static ToastService Instance { get; } = new();

    public ObservableCollection<ToastEntry> Toasts { get; } = new();

    public void Show(string message, ToastKind kind = ToastKind.Info, int durationMs = 4000)
    {
        Dispatcher.UIThread.Post(async () => {
            var toast = new ToastEntry { Message = message, Kind = kind };
            Toasts.Add(toast);
            await Task.Delay(durationMs);
            toast.IsVisible = false;
            await Task.Delay(300); // fade out
            Toasts.Remove(toast);
        });
    }

    public void Success(string msg, int ms = 4000) => Show(msg, ToastKind.Success, ms);
    public void Warning(string msg, int ms = 5000) => Show(msg, ToastKind.Warning, ms);
    public void Error(string msg,   int ms = 6000) => Show(msg, ToastKind.Error,   ms);
    public void Info(string msg,    int ms = 4000) => Show(msg, ToastKind.Info,    ms);
}
