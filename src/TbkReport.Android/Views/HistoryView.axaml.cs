using Avalonia.Controls;
using Avalonia.Interactivity;
using TbkReport.ViewModels;
using MainViewModel = TbkReport.Mobile.ViewModels.MainViewModel;

namespace TbkReport.Mobile.Views;

public partial class HistoryView : UserControl
{
    public HistoryView() => InitializeComponent();

    private void HistoryMonthClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is Button { Tag: HistoryMonthItem month })
            vm.Report.SelectHistoryMonthCommand.Execute(month);
    }

    private void HistoryDayClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is Button { Tag: HistoryDayItem day })
            vm.Report.SelectHistoryDayCommand.Execute(day);
    }
}
