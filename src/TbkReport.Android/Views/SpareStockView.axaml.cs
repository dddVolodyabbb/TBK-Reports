using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TbkReport.ViewModels;

namespace TbkReport.Mobile.Views;
public partial class SpareStockView : UserControl
{
    private readonly KeyboardAwareScrollController keyboardScroll;
    public SpareStockView()
    {
        InitializeComponent();
        keyboardScroll = new KeyboardAwareScrollController(this, FormScrollViewer);
    }
    private void FormGotFocus(object? sender, RoutedEventArgs e) => keyboardScroll.HandleGotFocus(e);
    private void FormLostFocus(object? sender, RoutedEventArgs e) => keyboardScroll.HandleLostFocus();
    private async void EditClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SparesViewModel vm && sender is Button { DataContext: SpareStockRow row })
        {
            // The form is above the list: bring it into view so the user sees what is being edited.
            vm.BeginEditStock(row);
            await Task.Delay(100);
            await keyboardScroll.SmoothScrollTo(StockForm);
        }
    }
    private void RemoveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SparesViewModel vm && sender is Button { DataContext: SpareStockRow row }) vm.RemoveStock(row);
    }
}
