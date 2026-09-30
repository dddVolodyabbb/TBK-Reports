using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TbkReport.Mobile.Views;

internal sealed class KeyboardAwareScrollController
{
    private const double MinimumKeyboardSpace = 340;
    private const double KeyboardGap = 32;
    private readonly UserControl owner;
    private readonly ScrollViewer scrollViewer;
    private readonly Control? content;
    private readonly Thickness contentMargin;
    private double keyboardScrollSpace = MinimumKeyboardSpace;
    private bool hasKeyboardSpace;

    public KeyboardAwareScrollController(UserControl owner, ScrollViewer scrollViewer)
    {
        this.owner = owner;
        this.scrollViewer = scrollViewer;
        content = scrollViewer.Content as Control;
        contentMargin = content?.Margin ?? default;
        owner.Loaded += (_, _) => MainActivity.KeyboardVisibilityChanged += KeyboardVisibilityChanged;
        owner.Unloaded += (_, _) => MainActivity.KeyboardVisibilityChanged -= KeyboardVisibilityChanged;
    }

    public async void HandleGotFocus(RoutedEventArgs e)
    {
        if (e.Source is not TextBox textBox) return;
        ApplyKeyboardSpace();
        await Task.Delay(350);
        if (textBox.IsFocused)
            await SmoothScrollTo(textBox, true);
    }

    public async void HandleLostFocus()
    {
        await Task.Delay(250);
        if (TopLevel.GetTopLevel(owner)?.FocusManager?.GetFocusedElement() is TextBox) return;
        RemoveKeyboardSpace();
    }

    public async Task SmoothScrollTo(Control control, bool keepAboveKeyboard = false)
    {
        var point = control.TranslatePoint(default, scrollViewer);
        if (point is null) return;

        var start = scrollViewer.Offset.Y;
        double target;
        if (keepAboveKeyboard)
        {
            const double gap = 24;
            var visibleBottom = Math.Max(80, scrollViewer.Viewport.Height - keyboardScrollSpace - gap);
            var controlBottom = point.Value.Y + control.Bounds.Height;
            if (controlBottom > visibleBottom)
                target = start + controlBottom - visibleBottom;
            else if (point.Value.Y < gap)
                target = start + point.Value.Y - gap;
            else
                return;
        }
        else
        {
            target = start + point.Value.Y - 24;
        }

        target = Math.Clamp(target, 0, Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height));
        if (Math.Abs(target - start) < 1) return;

        var stopwatch = Stopwatch.StartNew();
        const double duration = 260;
        while (stopwatch.Elapsed.TotalMilliseconds < duration)
        {
            var progress = stopwatch.Elapsed.TotalMilliseconds / duration;
            var eased = 1 - Math.Pow(1 - progress, 3);
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, start + ((target - start) * eased));
            await Task.Delay(16);
        }
        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, target);
    }

    private async void KeyboardVisibilityChanged(bool isVisible, double keyboardHeight)
    {
        if (!isVisible)
        {
            if (!hasKeyboardSpace) return;
            RemoveKeyboardSpace();
            scrollViewer.Focus();
            return;
        }

        if (!owner.IsVisible || TopLevel.GetTopLevel(owner)?.FocusManager?.GetFocusedElement() is not TextBox textBox)
            return;
        keyboardScrollSpace = Math.Max(MinimumKeyboardSpace, keyboardHeight + KeyboardGap);
        ApplyKeyboardSpace();
        await Task.Delay(80);
        if (textBox.IsFocused) await SmoothScrollTo(textBox, true);
    }

    private void ApplyKeyboardSpace()
    {
        hasKeyboardSpace = true;
        if (content is not null)
            content.Margin = new Thickness(contentMargin.Left, contentMargin.Top, contentMargin.Right,
                contentMargin.Bottom + keyboardScrollSpace);
        else
            scrollViewer.Padding = new Thickness(0, 0, 0, keyboardScrollSpace);
    }

    private void RemoveKeyboardSpace()
    {
        hasKeyboardSpace = false;
        if (content is not null) content.Margin = contentMargin;
        else scrollViewer.Padding = default;
    }
}
