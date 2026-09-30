using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TbkReport.Controls;
namespace TbkReport.Views;
public partial class MainWindow : Window
{
    private SignaturePad? activeSignaturePad;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, WindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, WindowKeyUp, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => activeSignaturePad?.FinishKeyboardDrawing();
    }
    private void ExitClick(object? sender, RoutedEventArgs e) => Close();

    private void OpenFirstSignatureClick(object? sender, RoutedEventArgs e) =>
        ActivateSignaturePad(FirstSignaturePad, FirstSignatureEditor);

    private void OpenSecondSignatureClick(object? sender, RoutedEventArgs e) =>
        ActivateSignaturePad(SecondSignaturePad, SecondSignatureEditor);

    private void ActivateSignaturePad(SignaturePad pad, Control editor)
    {
        activeSignaturePad?.FinishKeyboardDrawing();
        activeSignaturePad = pad;
        Dispatcher.UIThread.Post(() =>
        {
            editor.BringIntoView();
            var position = editor.TranslatePoint(default, HeaderScrollViewer);
            if (position is { } point && HeaderScrollViewer.Viewport.Height > 0)
            {
                var centeredOffset = HeaderScrollViewer.Offset.Y + point.Y + editor.Bounds.Height / 2 -
                                     HeaderScrollViewer.Viewport.Height / 2;
                var maximumOffset = Math.Max(0, HeaderScrollViewer.Extent.Height - HeaderScrollViewer.Viewport.Height);
                HeaderScrollViewer.Offset = new Vector(HeaderScrollViewer.Offset.X,
                    Math.Clamp(centeredOffset, 0, maximumOffset));
            }
            pad.Focus();
        }, DispatcherPriority.Background);
    }

    private void CloseSignatureClick(object? sender, RoutedEventArgs e)
    {
        activeSignaturePad?.FinishKeyboardDrawing();
        activeSignaturePad = null;
    }

    private void WindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsShiftKey(e.Key) || activeSignaturePad is not { IsVisible: true }) return;
        activeSignaturePad.BeginKeyboardDrawing();
        e.Handled = true;
    }

    private void WindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (!IsShiftKey(e.Key) || activeSignaturePad is not { IsVisible: true }) return;
        activeSignaturePad.FinishKeyboardDrawing();
        e.Handled = true;
    }

    private static bool IsShiftKey(Key key) => key is Key.LeftShift or Key.RightShift;
}
