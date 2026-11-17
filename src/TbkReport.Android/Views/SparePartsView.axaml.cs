using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TbkReport.Services;
using TbkReport.ViewModels;

namespace TbkReport.Mobile.Views;
public partial class SparePartsView : UserControl
{
    private readonly KeyboardAwareScrollController keyboardScroll;
    public SparePartsView()
    {
        InitializeComponent();
        keyboardScroll = new KeyboardAwareScrollController(this, FormScrollViewer);
    }
    private void FormGotFocus(object? sender, RoutedEventArgs e) => keyboardScroll.HandleGotFocus(e);
    private void FormLostFocus(object? sender, RoutedEventArgs e) => keyboardScroll.HandleLostFocus();
    private async void EditClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SparesViewModel vm && sender is Button { DataContext: SparePart part })
        {
            // The form is above the list: bring it into view so the user sees what is being edited.
            vm.BeginEditPart(part);
            await Task.Delay(100);
            await keyboardScroll.SmoothScrollTo(PartForm);
        }
    }
    private void RemoveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SparesViewModel vm && sender is Button { DataContext: SparePart part }) vm.RemovePart(part);
    }
}
