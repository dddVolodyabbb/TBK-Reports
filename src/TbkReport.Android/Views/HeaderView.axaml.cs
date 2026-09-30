using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace TbkReport.Mobile.Views;

public partial class HeaderView : UserControl
{
    private readonly KeyboardAwareScrollController keyboardScroll;
    public HeaderView()
    {
        InitializeComponent();
        keyboardScroll = new KeyboardAwareScrollController(this, FormScrollViewer);
    }
    private void FormGotFocus(object? sender, RoutedEventArgs e) => keyboardScroll.HandleGotFocus(e);
    private void FormLostFocus(object? sender, RoutedEventArgs e) => keyboardScroll.HandleLostFocus();
    private void OpenFirstSignatureClick(object? sender, RoutedEventArgs e) => ShowSignatureEditor(FirstSignatureEditor);
    private void OpenSecondSignatureClick(object? sender, RoutedEventArgs e) => ShowSignatureEditor(SecondSignatureEditor);
    private static void ShowSignatureEditor(Control editor) =>
        Dispatcher.UIThread.Post(editor.BringIntoView, DispatcherPriority.Background);
}
