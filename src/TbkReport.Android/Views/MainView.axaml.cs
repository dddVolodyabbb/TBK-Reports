using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using TbkReport.Mobile.ViewModels;

namespace TbkReport.Mobile.Views;

public partial class MainView : UserControl
{
    private static readonly string[] SwipeSections =
    [
        "Первая смена",
        "Вторая смена",
        "Проблемы",
        "Оборудование",
        "Шапка отчёта",
        "Начальные данные месяца",
        "История отчётов"
    ];

    private bool swipeAnimating;
    private bool swipeClaimed;
    private IPointer? swipePointer;
    private Point swipeStart;
    private double swipeDistance;
    private readonly TranslateTransform sectionTranslate = new();

    public MainView()
    {
        InitializeComponent();
        SectionContent.RenderTransform = sectionTranslate;
        SectionContent.AddHandler(PointerPressedEvent, SectionPointerPressed, RoutingStrategies.Tunnel, true);
        SectionContent.AddHandler(PointerMovedEvent, SectionPointerMoved, RoutingStrategies.Tunnel, true);
        SectionContent.AddHandler(PointerReleasedEvent, SectionPointerReleased, RoutingStrategies.Tunnel, true);
    }

    private void SectionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (swipeAnimating || DataContext is not MainViewModel viewModel || IsSignatureEditorOpen(viewModel)) return;
        if (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(SectionContent).Properties.IsLeftButtonPressed) return;
        swipePointer = e.Pointer;
        swipeStart = e.GetPosition(SectionContent);
        swipeDistance = 0;
        swipeClaimed = false;
    }

    private void SectionPointerMoved(object? sender, PointerEventArgs e)
    {
        if (swipePointer != e.Pointer || swipeAnimating || DataContext is not MainViewModel viewModel) return;
        if (IsSignatureEditorOpen(viewModel))
        {
            ResetSwipeTracking();
            return;
        }

        var position = e.GetPosition(SectionContent);
        var horizontal = position.X - swipeStart.X;
        var vertical = position.Y - swipeStart.Y;
        if (!swipeClaimed)
        {
            if (Math.Abs(vertical) > Math.Abs(horizontal) && Math.Abs(vertical) >= 12)
            {
                ResetSwipeTracking();
                return;
            }
            if (Math.Abs(horizontal) < 20) return;
            swipeClaimed = true;
            e.Pointer.Capture(SectionContent);
            SwipeInputBlocker.IsVisible = true;
            SectionContent.Focus();
        }

        e.Handled = true;
        swipeDistance = horizontal;
        var currentIndex = Array.IndexOf(SwipeSections, viewModel.CurrentSection);
        if (currentIndex < 0)
        {
            ResetSwipeTracking();
            return;
        }

        var movingPastFirst = currentIndex == 0 && swipeDistance > 0;
        var movingPastLast = currentIndex == SwipeSections.Length - 1 && swipeDistance < 0;
        var visualDistance = swipeDistance;
        sectionTranslate.X = movingPastFirst || movingPastLast
            ? Math.Sign(visualDistance) * Math.Min(Math.Abs(visualDistance) * 0.25, 36)
            : Math.Clamp(visualDistance, -SectionWidth, SectionWidth);
    }

    private async void SectionPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (swipePointer != e.Pointer) return;
        var claimed = swipeClaimed;
        var distance = swipeDistance;
        if (!claimed)
        {
            ResetSwipeTracking(releasePointer: false);
            return;
        }
        ResetSwipeTracking(releasePointer: false, resetVisual: false);
        if (e.Pointer.Captured == SectionContent) e.Pointer.Capture(null);
        e.Handled = true;
        await CompleteSectionSwipe(distance);
    }

    private void ResetSwipeTracking(bool releasePointer = true, bool resetVisual = true)
    {
        var pointer = swipePointer;
        swipePointer = null;
        swipeClaimed = false;
        swipeDistance = 0;
        if (releasePointer && pointer?.Captured == SectionContent) pointer.Capture(null);
        if (resetVisual && !swipeAnimating)
        {
            sectionTranslate.X = 0;
            SwipeInputBlocker.IsVisible = false;
        }
    }

    private async Task CompleteSectionSwipe(double distance)
    {
        if (swipeAnimating || DataContext is not MainViewModel viewModel)
        {
            sectionTranslate.X = 0;
            SwipeInputBlocker.IsVisible = false;
            return;
        }
        var currentIndex = Array.IndexOf(SwipeSections, viewModel.CurrentSection);
        if (currentIndex < 0)
        {
            sectionTranslate.X = 0;
            SwipeInputBlocker.IsVisible = false;
            return;
        }
        var direction = distance < 0 ? 1 : -1;
        var nextIndex = currentIndex + direction;
        swipeAnimating = true;
        try
        {
            if (nextIndex < 0 || nextIndex >= SwipeSections.Length)
            {
                var edgeOffset = direction > 0 ? -28d : 28d;
                if (Math.Abs(sectionTranslate.X) < 1)
                    await AnimateSection(0, edgeOffset, 90);
                await AnimateSection(sectionTranslate.X, 0, 150);
                return;
            }

            var exitOffset = Math.Sign(sectionTranslate.X) * SectionWidth;
            if (Math.Abs(exitOffset) < 1)
                exitOffset = direction > 0 ? -SectionWidth : SectionWidth;
            await AnimateSection(sectionTranslate.X, exitOffset, 180);
            viewModel.NavigateCommand.Execute(SwipeSections[nextIndex]);
            sectionTranslate.X = -exitOffset;
            await AnimateSection(-exitOffset, 0, 220);
        }
        finally
        {
            sectionTranslate.X = 0;
            SwipeInputBlocker.IsVisible = false;
            swipeAnimating = false;
        }
    }

    private double SectionWidth => Math.Max(320, SectionContent.Bounds.Width);

    private static bool IsSignatureEditorOpen(MainViewModel viewModel) =>
        viewModel.Report.Header.IsFirstShiftSignatureEditorVisible ||
        viewModel.Report.Header.IsSecondShiftSignatureEditorVisible;

    private async Task AnimateSection(double from, double to, double durationMilliseconds)
    {
        sectionTranslate.X = from;
        var started = DateTime.UtcNow;
        while ((DateTime.UtcNow - started).TotalMilliseconds < durationMilliseconds)
        {
            var progress = (DateTime.UtcNow - started).TotalMilliseconds / durationMilliseconds;
            var eased = 1 - Math.Pow(1 - progress, 3);
            sectionTranslate.X = from + ((to - from) * eased);
            await Task.Delay(16);
        }
        sectionTranslate.X = to;
    }
}
