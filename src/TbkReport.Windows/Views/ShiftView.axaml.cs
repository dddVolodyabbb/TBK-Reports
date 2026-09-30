using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TbkReport.Models;
using TbkReport.ViewModels;
namespace TbkReport.Views;
public partial class ShiftView : UserControl
{
    public ShiftView() { InitializeComponent(); }

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

    private void EditCommentClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShiftViewModel viewModel && sender is Button { DataContext: ReportComment comment })
            viewModel.BeginEditComment(comment);
    }

    private static void DigitsOnlyTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox || string.IsNullOrEmpty(textBox.Text)) return;
        var caret = textBox.CaretIndex;
        var filtered = new string(textBox.Text.Where(char.IsDigit).ToArray());
        if (filtered == textBox.Text) return;
        var removedBeforeCaret = textBox.Text.Take(Math.Min(caret, textBox.Text.Length)).Count(character => !char.IsDigit(character));
        textBox.Text = filtered;
        textBox.CaretIndex = Math.Max(0, caret - removedBeforeCaret);
    }
}
