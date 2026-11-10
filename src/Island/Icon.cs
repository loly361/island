using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace Island;

/// <summary>
/// Draws one icon of <see cref="IconSet"/>. The colour comes from the inherited Foreground, like a font glyph,
/// so button styles and triggers recolour it. The size is <see cref="Size"/> or, when unset, the inherited FontSize.
/// </summary>
public sealed class Icon : FrameworkElement
{
    /// <summary>Thinnest stroke in device pixels, so small icons stay crisp instead of fading out.</summary>
    private const double MinStrokePixels = 1.0;

    /// <summary>Grid units left outside the element box on each side; icon artwork stays inside 1–23.</summary>
    private const double LiveAreaInset = 1;

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(Icon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnKindChanged));

    public static readonly DependencyProperty FilledProperty = DependencyProperty.Register(
        nameof(Filled), typeof(bool), typeof(Icon),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnKindChanged));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WeightProperty = DependencyProperty.Register(
        nameof(Weight), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(IconSet.DefaultWeight, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Icon), new FrameworkPropertyMetadata(SystemColors.ControlTextBrush,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(
        typeof(Icon), new FrameworkPropertyMetadata(SystemFonts.MessageFontSize,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private string? _resolved;

    /// <summary>Icon name from <see cref="IconSet"/>, e.g. "magnifyingglass". An old font glyph also works.</summary>
    public string? Kind
    {
        get => (string?)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Use the ".fill" variant of the icon when there is one.</summary>
    public bool Filled
    {
        get => (bool)GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    /// <summary>Edge of the icon square in DIPs; NaN means the inherited FontSize.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Stroke weight on the 24-unit grid.</summary>
    public double Weight
    {
        get => (double)GetValue(WeightProperty);
        set => SetValue(WeightProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    private double EffectiveSize => double.IsNaN(Size) || Size <= 0 ? FontSize : Size;

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var icon = (Icon)d;
        var name = IconSet.Resolve(icon.Kind);
        if (name is not null && icon.Filled && !name.EndsWith(".fill", StringComparison.OrdinalIgnoreCase))
            name = IconSet.Resolve(name + ".fill") ?? name;
        icon._resolved = name;
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
    {
        var size = EffectiveSize;
        return new System.Windows.Size(size, size);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_resolved is null || Foreground is not { } brush) return;
        var size = EffectiveSize;
        if (size <= 0) return;

        // The element box holds the grid's live area (one unit of margin trimmed on each side), so an icon of
        // Size 16 is optically as large as a 16 px font glyph.
        var scale = size / (IconSet.GridSize - 2 * LiveAreaInset);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var weight = Math.Max(Weight, MinStrokePixels / pixelsPerDip / scale);
        weight = Math.Round(weight * 20) / 20;
        var geometry = IconSet.Outline(_resolved, weight);
        if (geometry is null) return;

        var dx = (RenderSize.Width - size) / 2 - LiveAreaInset * scale;
        var dy = (RenderSize.Height - size) / 2 - LiveAreaInset * scale;
        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, dx, dy));
        dc.DrawGeometry(brush, null, geometry);
        dc.Pop();
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        // The whole square is hit-testable, like a glyph cell, not just the thin strokes.
        var p = hitTestParameters.HitPoint;
        return p.X >= 0 && p.Y >= 0 && p.X <= RenderSize.Width && p.Y <= RenderSize.Height
            ? new PointHitTestResult(this, p)
            : null;
    }
}

/// <summary>Shows string content of icon buttons as an <see cref="Icon"/>; any other content keeps its default look.</summary>
public sealed class IconContentSelector : DataTemplateSelector
{
    private static readonly DataTemplate IconTemplate = Build();

    private static DataTemplate Build()
    {
        var factory = new FrameworkElementFactory(typeof(Icon));
        factory.SetBinding(Icon.KindProperty, new Binding());
        var template = new DataTemplate { VisualTree = factory };
        template.Seal();
        return template;
    }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is string text && IconSet.Resolve(text) is not null ? IconTemplate : null;
}
