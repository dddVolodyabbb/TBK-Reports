using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
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

    // Gesture thresholds in device-independent pixels.
    private const double ClaimDistance = 20;          // horizontal travel before a swipe takes the pointer
    private const double VerticalCancelDistance = 12; // vertical travel that hands the gesture to scrolling
    private const double DirectionRatio = 2;          // a swipe must be at least twice as horizontal as vertical
    private const double CommitFraction = 0.25;       // share of the section width that switches sections
    private const double FlingVelocity = 0.6;         // px/ms; a quick flick switches sections earlier
    private const double FlingMinimumDistance = 48;
    private const ulong VelocityWindowMilliseconds = 100;

    private bool swipeAnimating;
    private bool swipeClaimed;
    private IPointer? swipePointer;
    private Point swipeStart;
    private double swipeDistance;
    // Recent (timestamp, x) samples. Velocity is measured over a time window rather than
    // between two neighbouring events, because Android may deliver moves sparsely or in bursts.
    private readonly List<(ulong Time, double X)> swipeSamples = new();
    private readonly TranslateTransform sectionTranslate = new();

    public MainView()
    {
        InitializeComponent();
        SectionContent.RenderTransform = sectionTranslate;
        SectionContent.AddHandler(PointerPressedEvent, SectionPointerPressed, RoutingStrategies.Tunnel, true);
        SectionContent.AddHandler(PointerMovedEvent, SectionPointerMoved, RoutingStrategies.Tunnel, true);
        SectionContent.AddHandler(PointerReleasedEvent, SectionPointerReleased, RoutingStrategies.Tunnel, true);
        SectionContent.PointerCaptureLost += SectionPointerCaptureLost;
    }

    private void SectionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (swipeAnimating || DataContext is not MainViewModel viewModel || IsSignatureEditorOpen(viewModel)) return;
        // A second finger must not take over a swipe that is already moving the section.
        if (swipeClaimed && swipePointer is not null && swipePointer != e.Pointer) return;
        if (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(SectionContent).Properties.IsLeftButtonPressed) return;
        // Dragging inside the field being edited moves the caret or selects text instead of switching sections.
        if ((e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) is { IsKeyboardFocusWithin: true })
        {
            ResetSwipeTracking();
            return;
        }
        swipePointer = e.Pointer;
        swipeStart = e.GetPosition(this);
        swipeDistance = 0;
        swipeSamples.Clear();
        AddSwipeSample(e.Timestamp, swipeStart.X);
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

        // Positions are taken relative to this view: SectionContent itself moves with the swipe,
        // and measuring against it would subtract the section's own offset from the finger travel.
        var position = e.GetPosition(this);
        var horizontal = position.X - swipeStart.X;
        var vertical = position.Y - swipeStart.Y;
        AddSwipeSample(e.Timestamp, position.X);
        if (!swipeClaimed)
        {
            if (IsVerticalGesture(horizontal, vertical))
            {
                ResetSwipeTracking();
                return;
            }
            if (!IsHorizontalSwipe(horizontal, vertical)) return;
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
        var position = e.GetPosition(this);
        var horizontal = position.X - swipeStart.X;
        AddSwipeSample(e.Timestamp, position.X);
        var velocity = SwipeVelocity();
        var claimed = swipeClaimed;
        // A very quick flick may reach release without a single intermediate move.
        if (!claimed && !swipeAnimating && IsHorizontalSwipe(horizontal, position.Y - swipeStart.Y))
        {
            claimed = true;
            SwipeInputBlocker.IsVisible = true;
        }
        var distance = claimed ? horizontal : 0;        if (!claimed)
        {
            ResetSwipeTracking(releasePointer: false);
            return;
        }
        // Tracking is cleared before releasing capture, so the resulting capture-lost event is ignored.
        ResetSwipeTracking(releasePointer: false, resetVisual: false);
        if (e.Pointer.Captured == SectionContent) e.Pointer.Capture(null);
        e.Handled = true;
        if (ShouldCommitSwipe(distance, velocity)) await CompleteSectionSwipe(distance);
        else await CancelSectionSwipe();
    }

    // The system took the pointer (incoming call, notification shade, Back gesture):
    // return the section to its place instead of leaving it shifted and blocked.
    private async void SectionPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (swipePointer != e.Pointer) return;
        var claimed = swipeClaimed;
        ResetSwipeTracking(releasePointer: false, resetVisual: !claimed);
        if (claimed) await CancelSectionSwipe();
    }

    // Diagonal movement belongs to vertical scrolling unless it is clearly horizontal.
    private static bool IsVerticalGesture(double horizontal, double vertical) =>
        Math.Abs(vertical) >= VerticalCancelDistance && Math.Abs(horizontal) < Math.Abs(vertical) * DirectionRatio;

    private static bool IsHorizontalSwipe(double horizontal, double vertical) =>
        Math.Abs(horizontal) >= ClaimDistance && Math.Abs(horizontal) >= Math.Abs(vertical) * DirectionRatio;

    private void AddSwipeSample(ulong time, double x)
    {
        swipeSamples.Add((time, x));
        while (swipeSamples.Count > 2 && time - swipeSamples[1].Time > VelocityWindowMilliseconds)
            swipeSamples.RemoveAt(0);
    }

    private double SwipeVelocity()
    {
        if (swipeSamples.Count < 2) return 0;
        var first = swipeSamples[0];
        var last = swipeSamples[^1];
        return last.Time > first.Time ? (last.X - first.X) / (last.Time - first.Time) : 0;
    }

    private bool ShouldCommitSwipe(double distance, double velocity) =>
        Math.Abs(distance) >= SectionWidth * CommitFraction ||
        (Math.Abs(distance) >= FlingMinimumDistance && Math.Abs(velocity) >= FlingVelocity &&
         Math.Sign(velocity) == Math.Sign(distance));

    private async Task CancelSectionSwipe()
    {
        if (swipeAnimating) return;
        swipeAnimating = true;
        try
        {
            if (Math.Abs(sectionTranslate.X) >= 1) await AnimateSection(sectionTranslate.X, 0, 160);
        }
        finally
        {
            sectionTranslate.X = 0;
            SwipeInputBlocker.IsVisible = false;
            swipeAnimating = false;
        }
    }

    private void ResetSwipeTracking(bool releasePointer = true, bool resetVisual = true)
    {
        var pointer = swipePointer;
        swipePointer = null;
        swipeClaimed = false;
        swipeDistance = 0;
        swipeSamples.Clear();
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
