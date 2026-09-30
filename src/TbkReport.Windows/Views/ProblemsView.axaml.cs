using Avalonia.Controls;
using Avalonia.Interactivity;
using TbkReport.ViewModels;

namespace TbkReport.Views;
public partial class ProblemsView : UserControl
{
    public ProblemsView() => InitializeComponent();
    private void EditClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProblemsViewModel vm && sender is Button { DataContext: ProblemItem item }) vm.BeginEdit(item);
    }
    private void RemoveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProblemsViewModel vm && sender is Button { DataContext: ProblemItem item }) vm.Remove(item);
    }
}
