using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TbkReport.Mobile.Views;

public partial class MonthlyOpeningView : UserControl
{
    private readonly KeyboardAwareScrollController keyboardScroll;
    public MonthlyOpeningView()
    {
        InitializeComponent();
        keyboardScroll = new KeyboardAwareScrollController(this, FormScrollViewer);
    }
    private void FormGotFocus(object? sender, RoutedEventArgs e) => keyboardScroll.HandleGotFocus(e);
    private void FormLostFocus(object? sender, RoutedEventArgs e) => keyboardScroll.HandleLostFocus();
}
