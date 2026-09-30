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

namespace TbkReport.Controls;

public sealed class SignaturePad : Control
{
    public static readonly StyledProperty<string?> SignatureProperty = AvaloniaProperty.Register<SignaturePad, string?>(
        nameof(Signature), defaultBindingMode: BindingMode.TwoWay);

    private static readonly Pen InkPen = new(Brushes.Black, 2.2, null, PenLineCap.Round, PenLineJoin.Round, 10);
    private static readonly Pen BorderPen = new(new SolidColorBrush(Color.Parse("#707070")), 1);
    private static readonly IBrush KeyboardDrawingBackground = new SolidColorBrush(Color.Parse("#FFFFF0F0"));
    private readonly List<List<SignaturePoint>> strokes = new();
    private List<SignaturePoint>? activeStroke;
    private bool isKeyboardDrawingEnabled;
    private SignaturePoint? lastPointerPosition;
    private IPointer? lastPointer;
    private Bitmap? rasterSignature;
    private bool updatingSignature;
    private bool replaceOnNextStroke;

    static SignaturePad()
    {
        SignatureProperty.Changed.AddClassHandler<SignaturePad>((control, _) =>
        {
            if (!control.updatingSignature) control.LoadSignature();
        });
    }

    public SignaturePad()
    {
        MinWidth = 220;
        MinHeight = 110;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public string? Signature
    {
        get => GetValue(SignatureProperty);
        set => SetValue(SignatureProperty, value);
    }

    public bool IsKeyboardDrawingEnabled => isKeyboardDrawingEnabled;

    public void BeginKeyboardDrawing()
    {
        if (isKeyboardDrawingEnabled) return;
        isKeyboardDrawingEnabled = true;
        if (lastPointerPosition is { } position)
        {
            PrepareForNewSignature();
            activeStroke = new List<SignaturePoint> { position };
        }
        lastPointer?.Capture(this);
        Focus();
        InvalidateVisual();
    }

    public void FinishKeyboardDrawing()
    {
        if (!isKeyboardDrawingEnabled && activeStroke is null) return;
        isKeyboardDrawingEnabled = false;
        if (lastPointer?.Captured == this) lastPointer.Capture(null);
        CompleteActiveStroke();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var area = new Rect(Bounds.Size);
        context.DrawRectangle(isKeyboardDrawingEnabled ? KeyboardDrawingBackground : Brushes.White, BorderPen, area, 6, 6);
        if (rasterSignature is not null)
        {
            var source = new Rect(rasterSignature.Size);
            var scale = Math.Min((Bounds.Width - 12) / source.Width, (Bounds.Height - 12) / source.Height);
            var width = source.Width * scale;
            var height = source.Height * scale;
            var destination = new Rect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
            context.DrawImage(rasterSignature, source, destination);
        }
        foreach (var stroke in strokes) DrawStroke(context, stroke);
        if (activeStroke is not null) DrawStroke(context, activeStroke);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (isKeyboardDrawingEnabled)
        {
            e.Handled = true;
            return;
        }
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
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
        lastPointer = e.Pointer;
        lastPointerPosition = Normalize(e.GetPosition(this));
        if (isKeyboardDrawingEnabled)
        {
            if (rasterSignature is not null) PrepareForNewSignature();
            activeStroke ??= new List<SignaturePoint>();
            AddPoint(e.GetPosition(this));
            e.Handled = true;
            InvalidateVisual();
            return;
        }
        if (activeStroke is null || e.Pointer.Captured != this) return;
        AddPoint(e.GetPosition(this));
        e.Handled = true;
        InvalidateVisual();
    }

    private void AddPoint(Point position)
    {
        if (activeStroke is null) return;
        var point = Normalize(position);
        if (activeStroke.Count == 0 || Distance(activeStroke[^1], point) >= 0.002)
            activeStroke.Add(point);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (isKeyboardDrawingEnabled)
        {
            e.Handled = true;
            return;
        }
        if (activeStroke is null) return;
        activeStroke.Add(Normalize(e.GetPosition(this)));
        e.Pointer.Capture(null);
        CompleteActiveStroke();
        e.Handled = true;
        InvalidateVisual();
    }

    private void CompleteActiveStroke()
    {
        if (activeStroke is not { Count: > 0 })
        {
            activeStroke = null;
            return;
        }

        strokes.Add(activeStroke);
        activeStroke = null;
        updatingSignature = true;
        try { SetCurrentValue(SignatureProperty, SignatureData.Serialize(strokes)); }
        finally { updatingSignature = false; }
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
        isKeyboardDrawingEnabled = false;
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
            var position = Position(stroke[0]);
            context.DrawEllipse(Brushes.Black, null, position, 1.5, 1.5);
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
