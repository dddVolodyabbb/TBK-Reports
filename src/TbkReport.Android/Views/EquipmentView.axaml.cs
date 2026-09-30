using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TbkReport.Services;
using TbkReport.ViewModels;

namespace TbkReport.Mobile.Views;
public partial class EquipmentView : UserControl
{
    private readonly KeyboardAwareScrollController keyboardScroll;
    public EquipmentView()
    {
        InitializeComponent();
        keyboardScroll = new KeyboardAwareScrollController(this, FormScrollViewer);
    }
    private void FormGotFocus(object? sender, RoutedEventArgs e) => keyboardScroll.HandleGotFocus(e);
    private void FormLostFocus(object? sender, RoutedEventArgs e) => keyboardScroll.HandleLostFocus();
    private async void EditClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EquipmentViewModel vm && sender is Button { DataContext: EquipmentData item })
        {
            vm.BeginEdit(item);
            await Task.Delay(100);
            await keyboardScroll.SmoothScrollTo(EquipmentNameTextBox);
        }
    }
    private void RemoveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EquipmentViewModel vm && sender is Button { DataContext: EquipmentData item }) vm.Remove(item);
    }
}
