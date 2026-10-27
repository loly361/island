using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Island.Interop;
using Drawing = System.Drawing;
using Shapes = System.Windows.Shapes;

namespace Island;

public enum OverlayMode { Region, Picker }

/// <summary>
/// Full-virtual-screen overlay showing a frozen screenshot. Region mode lets the user drag a rectangle,
/// picker mode reads the colour under the cursor. Results are exposed as properties after ShowDialog().
/// </summary>
public sealed class ScreenOverlayWindow : Window
{
    private readonly Drawing.Bitmap _shot;
    private readonly Drawing.Rectangle _bounds;
    private readonly OverlayMode _mode;
    private readonly Grid _root = new() { Background = Brushes.Transparent };
    private readonly Shapes.Path _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)) };
    private readonly Shapes.Rectangle _frame = new()
    {
        Stroke = Brushes.White,
        StrokeThickness = 1.5,
        Visibility = Visibility.Collapsed,
    };
    private readonly TextBlock _sizeLabel = new()
    {
        Foreground = Brushes.White,
        FontSize = 12,
        Visibility = Visibility.Collapsed,
        Padding = new Thickness(6, 2, 6, 2),
        Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
    };
    private readonly Border _swatch = new()
    {
        CornerRadius = new CornerRadius(8),
        Background = new SolidColorBrush(Color.FromArgb(235, 20, 20, 22)),
        Padding = new Thickness(8),
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
    };
    private readonly Border _swatchColor = new() { Width = 28, Height = 28, CornerRadius = new CornerRadius(6) };
    private readonly TextBlock _swatchText = new() { Foreground = Brushes.White, FontSize = 14, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
    private readonly Canvas _canvas = new();
    private Point? _start;

    public Drawing.Rectangle? Selection { get; private set; }

    public string? PickedHex { get; private set; }

    public ScreenOverlayWindow(Drawing.Bitmap shot, Drawing.Rectangle bounds, OverlayMode mode)
    {
        _shot = shot;
        _bounds = bounds;
        _mode = mode;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
        Width = 200;
        Height = 200;

        var image = new Image { Source = Services.ScreenTools.ToBitmapSource(shot), Stretch = Stretch.Fill };
        _root.Children.Add(image);
        if (mode == OverlayMode.Region)
        {
            _dim.Data = new RectangleGeometry(new Rect(0, 0, 1, 1));
            _canvas.Children.Add(_dim);
            _canvas.Children.Add(_frame);
            _canvas.Children.Add(_sizeLabel);
        }
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(_swatchColor);
            row.Children.Add(new Border { Width = 8 });
            row.Children.Add(_swatchText);
            _swatch.Child = row;
            _canvas.Children.Add(_swatch);
        }

        _root.Children.Add(_canvas);
        Content = _root;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, Native.SWP_SHOWWINDOW);
        };
        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            UpdateDim(null);
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
    }

    private double ScaleX => _shot.Width / Math.Max(1, ActualWidth);

    private double ScaleY => _shot.Height / Math.Max(1, ActualHeight);

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right)
        {
            Close();
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;
        if (_mode == OverlayMode.Picker)
        {
            PickedHex = HexAt(e.GetPosition(this));
            Close();
            return;
        }

        _start = e.GetPosition(this);
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (_mode == OverlayMode.Picker)
        {
            UpdateSwatch(pos);
            return;
        }

        if (_start is null) return;
        var rect = new Rect(_start.Value, pos);
        _frame.Visibility = Visibility.Visible;
        Canvas.SetLeft(_frame, rect.X);
        Canvas.SetTop(_frame, rect.Y);
        _frame.Width = rect.Width;
        _frame.Height = rect.Height;
        UpdateDim(rect);

        _sizeLabel.Visibility = Visibility.Visible;
        _sizeLabel.Text = $"{(int)(rect.Width * ScaleX)} × {(int)(rect.Height * ScaleY)}";
        Canvas.SetLeft(_sizeLabel, rect.X);
        Canvas.SetTop(_sizeLabel, Math.Max(0, rect.Y - 24));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_mode != OverlayMode.Region || _start is null || e.ChangedButton != MouseButton.Left) return;
        ReleaseMouseCapture();
        var rect = new Rect(_start.Value, e.GetPosition(this));
        _start = null;

        var x = (int)Math.Round(rect.X * ScaleX);
        var y = (int)Math.Round(rect.Y * ScaleY);
        var w = (int)Math.Round(rect.Width * ScaleX);
        var h = (int)Math.Round(rect.Height * ScaleY);
        if (w >= 4 && h >= 4) Selection = new Drawing.Rectangle(x, y, w, h);
        Close();
    }

    private void UpdateDim(Rect? hole)
    {
        if (_mode != OverlayMode.Region) return;
        var full = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        _dim.Data = hole is { } h
            ? new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(h))
            : full;
    }

    private string HexAt(Point dip)
    {
        var px = Math.Clamp((int)(dip.X * ScaleX), 0, _shot.Width - 1);
        var py = Math.Clamp((int)(dip.Y * ScaleY), 0, _shot.Height - 1);
        var c = _shot.GetPixel(px, py);
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private void UpdateSwatch(Point pos)
    {
        var hex = HexAt(pos);
        _swatchColor.Background = (Brush)new BrushConverter().ConvertFromString(hex)!;
        _swatchText.Text = hex;
        _swatch.Visibility = Visibility.Visible;
        var x = pos.X + 22;
        var y = pos.Y + 22;
        if (x + 150 > ActualWidth) x = pos.X - 150;
        if (y + 60 > ActualHeight) y = pos.Y - 60;
        Canvas.SetLeft(_swatch, x);
        Canvas.SetTop(_swatch, y);
    }
}
