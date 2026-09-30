using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TbkReport.Services;

namespace TbkReport.Mobile.Controls;

public sealed class SignaturePad : Control
{
    public static readonly StyledProperty<string?> SignatureProperty = AvaloniaProperty.Register<SignaturePad, string?>(
        nameof(Signature), defaultBindingMode: BindingMode.TwoWay);

    private static readonly Pen InkPen = new(Brushes.Black, 2.6, null, PenLineCap.Round, PenLineJoin.Round, 10);
    private static readonly Pen BorderPen = new(new SolidColorBrush(Color.Parse("#707070")), 1);
    private readonly List<List<SignaturePoint>> strokes = new();
    private List<SignaturePoint>? activeStroke;
    private Bitmap? rasterSignature;
    private bool updatingSignature;
    private bool replaceOnNextStroke;

    static SignaturePad() => SignatureProperty.Changed.AddClassHandler<SignaturePad>((control, _) =>
    {
        if (!control.updatingSignature) control.LoadSignature();
    });

    public SignaturePad()
    {
        MinWidth = 240;
        MinHeight = 180;
        Focusable = true;
    }

    public string? Signature
    {
        get => GetValue(SignatureProperty);
        set => SetValue(SignatureProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var area = new Rect(Bounds.Size);
        context.DrawRectangle(Brushes.White, BorderPen, area, 8, 8);
        if (rasterSignature is not null)
        {
            var source = new Rect(rasterSignature.Size);
            var scale = Math.Min((Bounds.Width - 16) / source.Width, (Bounds.Height - 16) / source.Height);
            var width = source.Width * scale;
            var height = source.Height * scale;
            context.DrawImage(rasterSignature, source,
                new Rect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height));
        }
        foreach (var stroke in strokes) DrawStroke(context, stroke);
        if (activeStroke is not null) DrawStroke(context, activeStroke);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (e.Pointer.Type == PointerType.Mouse && !point.Properties.IsLeftButtonPressed) return;
        Focus();
        PrepareForNewSignature();
        activeStroke = new List<SignaturePoint> { Normalize(e.GetPosition(this)) };
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (activeStroke is null || e.Pointer.Captured != this) return;
        AddPoint(e.GetPosition(this));
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (activeStroke is null) return;
        AddPoint(e.GetPosition(this));
        e.Pointer.Capture(null);
        FinishStroke();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        FinishStroke();
    }

    private void FinishStroke()
    {
        if (activeStroke is null) return;
        strokes.Add(activeStroke);
        activeStroke = null;
        updatingSignature = true;
        try { SetCurrentValue(SignatureProperty, SignatureData.Serialize(strokes)); }
        finally { updatingSignature = false; }
        InvalidateVisual();
    }

    private void AddPoint(Point position)
    {
        if (activeStroke is null) return;
        var point = Normalize(position);
        if (activeStroke.Count == 0 || Distance(activeStroke[^1], point) >= 0.002)
            activeStroke.Add(point);
    }

    private void LoadSignature()
    {
        rasterSignature?.Dispose();
        rasterSignature = null;
        strokes.Clear();
        if (SignatureData.TryGetPng(Signature, out var png))
        {
            using var stream = new MemoryStream(png, writable: false);
            rasterSignature = new Bitmap(stream);
        }
        else strokes.AddRange(SignatureData.Parse(Signature).Select(stroke => stroke.ToList()));
        activeStroke = null;
        replaceOnNextStroke = !string.IsNullOrWhiteSpace(Signature);
        InvalidateVisual();
    }

    private void PrepareForNewSignature()
    {
        if (!replaceOnNextStroke) return;
        rasterSignature?.Dispose();
        rasterSignature = null;
        strokes.Clear();
        replaceOnNextStroke = false;
    }

    private SignaturePoint Normalize(Point point) => new(
        Math.Clamp(point.X / Math.Max(1, Bounds.Width), 0, 1),
        Math.Clamp(point.Y / Math.Max(1, Bounds.Height), 0, 1));

    private void DrawStroke(DrawingContext context, IReadOnlyList<SignaturePoint> stroke)
    {
        if (stroke.Count == 0) return;
        Point Position(SignaturePoint point) => new(point.X * Bounds.Width, point.Y * Bounds.Height);
        if (stroke.Count == 1)
        {
            context.DrawEllipse(Brushes.Black, null, Position(stroke[0]), 1.8, 1.8);
            return;
        }
        for (var index = 1; index < stroke.Count; index++)
            context.DrawLine(InkPen, Position(stroke[index - 1]), Position(stroke[index]));
    }

    private static double Distance(SignaturePoint first, SignaturePoint second)
    {
        var x = second.X - first.X;
        var y = second.Y - first.Y;
        return Math.Sqrt(x * x + y * y);
    }
}
