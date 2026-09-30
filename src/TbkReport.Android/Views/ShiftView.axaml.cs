using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TbkReport.Models;
using TbkReport.ViewModels;
namespace TbkReport.Mobile.Views;
public partial class ShiftView : UserControl
{
    private readonly KeyboardAwareScrollController keyboardScroll;

    public ShiftView()
    {
        InitializeComponent();
        keyboardScroll = new KeyboardAwareScrollController(this, ReportScrollViewer);
    }

    private void RemoveCommentClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShiftViewModel viewModel && sender is Button { DataContext: ReportComment comment })
            viewModel.RemoveComment(comment);
    }

    private void CommentTextBoxGotFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShiftViewModel viewModel)
            viewModel.EnsureNewCommentTime();
    }

    private async void EditCommentClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShiftViewModel viewModel && sender is Button { DataContext: ReportComment comment })
        {
            viewModel.BeginEditComment(comment);
            await Task.Delay(100);
            await keyboardScroll.SmoothScrollTo(CommentTextBox);
        }
    }

    private void FormGotFocus(object? sender, RoutedEventArgs e) => keyboardScroll.HandleGotFocus(e);

    private void FormLostFocus(object? sender, RoutedEventArgs e) => keyboardScroll.HandleLostFocus();
}
