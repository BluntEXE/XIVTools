using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using XivToolsUI.ViewModels;

namespace XivToolsUI.Views;

public partial class ModelViewerView : UserControl
{
    private ModelViewerViewModel? _vm;
    private Avalonia.Point _lastPos;
    private bool _leftDown, _rightDown;

    public ModelViewerView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
    }

    private void Attach()
    {
        if (_vm != null) _vm.ModelLoaded -= OnModelLoaded;
        _vm = DataContext as ModelViewerViewModel;
        if (_vm != null) _vm.ModelLoaded += OnModelLoaded;
    }

    private void OnModelLoaded(System.Collections.Generic.List<GlMeshData> meshes)
    {
        Dispatcher.UIThread.Post(() => {
            GlControl.PendingMeshes = meshes;
            GlControl.RequestNextFrameRendering();
        });
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        ResetBtn.Click += (_, _) => GlControl.ResetCamera();

        // Wire input to the Viewport grid so events aren't swallowed by GL surface
        Viewport.PointerPressed  += OnViewportPressed;
        Viewport.PointerReleased += OnViewportReleased;
        Viewport.PointerMoved    += OnViewportMoved;
        Viewport.PointerWheelChanged += OnViewportWheel;
    }

    private void OnViewportPressed(object? s, PointerPressedEventArgs e)
    {
        _lastPos   = e.GetPosition(Viewport);
        var props  = e.GetCurrentPoint(Viewport).Properties;
        _leftDown  = props.IsLeftButtonPressed;
        _rightDown = props.IsRightButtonPressed;
        e.Pointer.Capture(Viewport);
        e.Handled = true;
    }

    private void OnViewportReleased(object? s, PointerReleasedEventArgs e)
    {
        _leftDown = _rightDown = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnViewportMoved(object? s, PointerEventArgs e)
    {
        var pos = e.GetPosition(Viewport);
        var dx  = (float)(pos.X - _lastPos.X);
        var dy  = (float)(pos.Y - _lastPos.Y);
        _lastPos = pos;

        if (_leftDown)       GlControl.Orbit(dx, dy);
        else if (_rightDown) GlControl.Pan(dx, dy);

        e.Handled = true;
    }

    private void OnViewportWheel(object? s, PointerWheelEventArgs e)
    {
        GlControl.Zoom(-(float)e.Delta.Y);   // negative so scroll-up = zoom in
        e.Handled = true;
    }
}
