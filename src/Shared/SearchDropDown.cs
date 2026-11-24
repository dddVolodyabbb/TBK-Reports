using Avalonia.Controls;
using Avalonia.Threading;

namespace TbkReport.SharedViews;

/// <summary>
/// A search field above a drop-down list: while the user types in the field, the list opens by itself and shows what
/// was found, and the cursor stays in the field so typing can go on. An empty search closes the list.
/// </summary>
/// <remarks>
/// Screen behaviour, so it needs Avalonia and cannot live in Core (which has no UI references). This one file is
/// linked into both applications (Windows and Android project files) instead of keeping a copy in each.
/// </remarks>
internal static class SearchDropDown
{
    public static void Attach(TextBox search, ComboBox list)
    {
        search.TextChanged += (_, _) =>
        {
            // Only typing opens the list; text set by the program (a cleared form) does not.
            if (!search.IsKeyboardFocusWithin) return;
            // After the view model has narrowed the list for the new text.
            Dispatcher.UIThread.Post(() =>
            {
                var open = !string.IsNullOrWhiteSpace(search.Text) && list.ItemCount > 0;
                if (list.IsDropDownOpen != open) list.IsDropDownOpen = open;
                if (!open) return;
                // Opening the list moves the focus into it; give it back so the next letters go to the search
                // (and on Android the on-screen keyboard stays open).
                search.Focus();
                search.CaretIndex = search.Text?.Length ?? 0;
            }, DispatcherPriority.Background);
        };
    }
}
