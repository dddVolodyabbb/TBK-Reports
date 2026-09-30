using Avalonia.Controls;
using Avalonia.Interactivity;
using TbkReport.ViewModels;

namespace TbkReport.Mobile.Views;
public partial class ProblemsView : UserControl
{
    private readonly KeyboardAwareScrollController keyboardScroll;
    public ProblemsView()
    {
        InitializeComponent();
        keyboardScroll = new KeyboardAwareScrollController(this, FormScrollViewer);
    }
    private void FormGotFocus(object? sender, RoutedEventArgs e) => keyboardScroll.HandleGotFocus(e);
    private void FormLostFocus(object? sender, RoutedEventArgs e) => keyboardScroll.HandleLostFocus();
    private void EditClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProblemsViewModel vm && sender is Button { DataContext: ProblemItem item }) vm.BeginEdit(item);
    }
    private void RemoveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProblemsViewModel vm && sender is Button { DataContext: ProblemItem item }) vm.Remove(item);
    }
}
