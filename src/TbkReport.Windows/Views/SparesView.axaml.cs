using Avalonia.Controls;
using Avalonia.Interactivity;
using TbkReport.Services;
using TbkReport.ViewModels;

namespace TbkReport.Views;
public partial class SparesView : UserControl
{
    public SparesView()
    {
        InitializeComponent();
        SearchDropDown.Attach(StockChoiceSearchBox, StockChoiceList);
    }
    private void EditPartClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SparesViewModel vm && sender is Button { DataContext: SparePart part }) vm.BeginEditPart(part);
    }
    private void RemovePartClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SparesViewModel vm && sender is Button { DataContext: SparePart part }) vm.RemovePart(part);
    }
    private void EditStockClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SparesViewModel vm && sender is Button { DataContext: SpareStockRow row }) vm.BeginEditStock(row);
    }
    private void RemoveStockClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SparesViewModel vm && sender is Button { DataContext: SpareStockRow row }) vm.RemoveStock(row);
    }
}
