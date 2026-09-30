using Avalonia.Controls;
using Avalonia.Interactivity;
using TbkReport.ViewModels;

namespace TbkReport.Views;

public partial class HistoryView : UserControl
{
    public HistoryView() => InitializeComponent();

    private void HistoryMonthClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is Button { Tag: HistoryMonthItem month })
            viewModel.SelectHistoryMonthCommand.Execute(month);
    }

    private void HistoryDayClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is Button { Tag: HistoryDayItem day })
            viewModel.SelectHistoryDayCommand.Execute(day);
    }
}
