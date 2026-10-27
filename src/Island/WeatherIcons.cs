using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Island.Services;

namespace Island;

/// <summary>
/// Vector weather icons. WPF cannot draw colour emoji, so each icon is built from simple shapes on a 24×24 canvas.
/// </summary>
public static class WeatherIcons
{
    private static readonly Brush Sun = Frozen(0xFF, 0xC8, 0x3D);
    private static readonly Brush SunDisc = Radial(0xFF, 0xE9, 0x8A, 0xFF, 0xB2, 0x1F);
    private static readonly Brush Moon = Vertical(0xFA, 0xFA, 0xFF, 0xBC, 0xC0, 0xDC);
    private static readonly Brush Cloud = Vertical(0xFF, 0xFF, 0xFF, 0xBE, 0xCB, 0xE2);
    private static readonly Brush Rain = Frozen(0x5A, 0xB0, 0xFF);
    private static readonly Brush Snow = Frozen(0xFF, 0xFF, 0xFF);
    private static readonly Brush Bolt = Vertical(0xFF, 0xE6, 0x6D, 0xFF, 0xA6, 0x00);
    private static readonly Brush Fog = Frozen(0xC4, 0xCC, 0xD8);

    private static readonly Geometry CloudShape =
        Geometry.Parse("M7.2,19 A4.2,4.2 0 0 1 6.6,10.6 A5.8,5.8 0 0 1 17.6,9.4 A4.8,4.8 0 0 1 17.4,19 Z");

    private static readonly Geometry MoonShape =
        Geometry.Parse("M19.5,14.8 A8.2,8.2 0 1 1 9.2,4.5 A6.6,6.6 0 0 0 19.5,14.8 Z");

    public static FrameworkElement Create(WxKind kind, double size)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        switch (kind)
        {
            case WxKind.Clear:
                AddSun(canvas, 12, 12, 1);
                break;
            case WxKind.ClearNight:
                AddShape(canvas, MoonShape, Moon, 0, 0, 1);
                break;
            case WxKind.PartlyCloudy:
                AddSun(canvas, 8.5, 8.5, 0.72);
                AddShape(canvas, CloudShape, Cloud, 2.6, 2.4, 0.86);
                break;
            case WxKind.PartlyCloudyNight:
                AddShape(canvas, MoonShape, Moon, -3.2, -3.4, 0.6);
                AddShape(canvas, CloudShape, Cloud, 2.6, 2.4, 0.86);
                break;
            case WxKind.Cloudy:
                AddShape(canvas, CloudShape, Cloud, 0, 1, 1);
                break;
            case WxKind.Fog:
                AddLines(canvas, Fog, 1.8, "M3,8 L21,8", "M5,12 L19,12", "M3,16 L21,16", "M6,20 L18,20");
                break;
            case WxKind.Drizzle:
                AddShape(canvas, CloudShape, Cloud, 0, -3, 1);
                AddLines(canvas, Rain, 1.6, "M8.5,19.4 L8,21", "M12.5,19.4 L12,21", "M16.5,19.4 L16,21");
                break;
            case WxKind.Rain:
                AddShape(canvas, CloudShape, Cloud, 0, -3, 1);
                AddLines(canvas, Rain, 1.9, "M8.5,18.6 L7.3,22", "M12.8,18.6 L11.6,22", "M17,18.6 L15.8,22");
                break;
            case WxKind.Snow:
                AddShape(canvas, CloudShape, Cloud, 0, -3, 1);
                AddLines(canvas, Snow, 2.4, "M8,19.6 L8,19.7", "M12,22 L12,22.1", "M16,19.6 L16,19.7", "M12,17.6 L12,17.7");
                break;
            case WxKind.Thunder:
                AddShape(canvas, CloudShape, Cloud, 0, -3, 1);
                canvas.Children.Add(new Polygon
                {
                    Fill = Bolt,
                    Points = new PointCollection
                    {
                        new(12.8, 13), new(9, 19.4), new(11.8, 19.4), new(10.6, 23.2), new(15.6, 16.4), new(12.8, 16.4), new(14.4, 13),
                    },
                });
                break;
        }

        return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform };
    }

    private static void AddSun(Canvas canvas, double cx, double cy, double scale)
    {
        var radius = 4.6 * scale;
        var disc = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = SunDisc };
        Canvas.SetLeft(disc, cx - radius);
        Canvas.SetTop(disc, cy - radius);
        canvas.Children.Add(disc);

        var inner = 7.2 * scale;
        var outer = 10.2 * scale;
        for (var i = 0; i < 8; i++)
        {
            var angle = i * Math.PI / 4;
            canvas.Children.Add(new Line
            {
                X1 = cx + inner * Math.Cos(angle),
                Y1 = cy + inner * Math.Sin(angle),
                X2 = cx + outer * Math.Cos(angle),
                Y2 = cy + outer * Math.Sin(angle),
                Stroke = Sun,
                StrokeThickness = 1.8 * Math.Max(scale, 0.8),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
        }
    }

    private static void AddShape(Canvas canvas, Geometry geometry, Brush fill, double dx, double dy, double scale)
    {
        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(scale, scale));
        transform.Children.Add(new TranslateTransform(dx, dy));
        canvas.Children.Add(new Path { Data = geometry, Fill = fill, RenderTransform = transform });
    }

    private static void AddLines(Canvas canvas, Brush stroke, double thickness, params string[] data)
    {
        foreach (var d in data)
        {
            canvas.Children.Add(new Path
            {
                Data = Geometry.Parse(d),
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
        }
    }

    private static Brush Vertical(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
    {
        var brush = new LinearGradientBrush(Color.FromRgb(r1, g1, b1), Color.FromRgb(r2, g2, b2), 90);
        brush.Freeze();
        return brush;
    }

    private static Brush Radial(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
    {
        var brush = new RadialGradientBrush(Color.FromRgb(r1, g1, b1), Color.FromRgb(r2, g2, b2))
        {
            GradientOrigin = new Point(0.38, 0.32),
        };
        brush.Freeze();
        return brush;
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
