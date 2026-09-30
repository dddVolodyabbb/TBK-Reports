using Avalonia.Controls;
using Avalonia.Interactivity;
using TbkReport.Services;
using TbkReport.ViewModels;

namespace TbkReport.Views;
public partial class EquipmentView : UserControl
{
    public EquipmentView() => InitializeComponent();
    private void EditClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EquipmentViewModel vm && sender is Button { DataContext: EquipmentData item }) vm.BeginEdit(item);
    }
    private void RemoveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EquipmentViewModel vm && sender is Button { DataContext: EquipmentData item }) vm.Remove(item);
    }
}
