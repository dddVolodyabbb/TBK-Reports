using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace TbkReport.Mobile.Views;

public partial class ClockTimePicker : UserControl
{
    private static WeakReference<ClockTimePicker>? activeEditor;
    public static readonly StyledProperty<TimeSpan?> SelectedTimeProperty =
        AvaloniaProperty.Register<ClockTimePicker, TimeSpan?>(nameof(SelectedTime), defaultBindingMode: BindingMode.TwoWay);

    private bool synchronizing;
    private bool cancellingEdit;
    private string previousText = "";

    public TimeSpan? SelectedTime
    {
        get => GetValue(SelectedTimeProperty);
        set => SetValue(SelectedTimeProperty, value);
    }

    public ClockTimePicker()
    {
        InitializeComponent();
        TimeTextBox.TextChanged += (_, _) => FormatTypedTime();
        TimeTextBox.GotFocus += (_, _) => activeEditor = new WeakReference<ClockTimePicker>(this);
        TimeTextBox.LostFocus += (_, _) =>
        {
            if (!cancellingEdit) CommitText();
            if (activeEditor?.TryGetTarget(out var editor) == true && ReferenceEquals(editor, this))
                activeEditor = null;
        };
        TimeTextBox.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            CommitText();
            RemoveTextFocus();
            args.Handled = true;
        };
        WheelPicker.SelectedTimeChanged += (_, _) =>
        {
            if (!synchronizing) SelectedTime = WheelPicker.SelectedTime;
        };
        RefreshControls();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedTimeProperty) RefreshControls();
    }

    private void RefreshControls()
    {
        if (TimeTextBox is null || WheelPicker is null || synchronizing) return;
        synchronizing = true;
        var text = SelectedTime is { } time ? $"{time.Hours:00}:{time.Minutes:00}" : "";
        TimeTextBox.Text = text;
        previousText = text;
        WheelPicker.SelectedTime = SelectedTime;
        TimeTextBox.ClearValue(TextBox.BorderBrushProperty);
        synchronizing = false;
    }

    private void FormatTypedTime()
    {
        if (synchronizing) return;
        var raw = TimeTextBox.Text ?? "";
        var formatted = FormatInput(raw, raw.Length >= previousText.Length);
        previousText = formatted;
        if (raw == formatted) return;
        synchronizing = true;
        TimeTextBox.Text = formatted;
        TimeTextBox.CaretIndex = formatted.Length;
        synchronizing = false;
    }

    private static string FormatInput(string raw, bool inserting)
    {
        if (raw.Contains(':'))
        {
            var parts = raw.Split(':', 2);
            var hours = new string(parts[0].Where(char.IsDigit).Take(2).ToArray());
            var minutes = new string(parts[1].Where(char.IsDigit).Take(2).ToArray());
            return hours + ":" + minutes;
        }
        var digits = new string(raw.Where(char.IsDigit).Take(4).ToArray());
        if (digits.Length >= 3) return digits.Insert(2, ":");
        if (digits.Length == 2 && inserting) return digits + ":";
        return digits;
    }

    private void CommitText()
    {
        if (synchronizing) return;
        var text = TimeTextBox.Text?.Trim() ?? "";
        if (text.Length == 0)
        {
            SelectedTime = null;
            TimeTextBox.ClearValue(TextBox.BorderBrushProperty);
            return;
        }
        if (TimeSpan.TryParseExact(text, new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture,
                TimeSpanStyles.None, out var time) && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1))
        {
            SelectedTime = new TimeSpan(time.Hours, time.Minutes, 0);
            RefreshControls();
            return;
        }
        TimeTextBox.BorderBrush = Brushes.Red;
    }

    public static bool TryCancelActiveEdit()
    {
        if (activeEditor is null ||
            !activeEditor.TryGetTarget(out var editor) ||
            editor is null ||
            !editor.TimeTextBox.IsFocused)
        {
            return false;
        }

        editor.CancelTextEdit();
        return true;
    }

    private void CancelTextEdit()
    {
        cancellingEdit = true;
        RefreshControls();
        RemoveTextFocus();
        cancellingEdit = false;
        activeEditor = null;
    }

    private void RemoveTextFocus() => WheelPicker.Focus();
}
