using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenDraw;

/// <summary>Dimmed full-screen overlay on one monitor; drag to pick a region.</summary>
sealed class SelectionWindow : Window
{
    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    static readonly IntPtr HWND_TOPMOST = new(-1);

    readonly SelView view;
    readonly System.Drawing.Rectangle bounds;

    public event Action<BitmapSource, double>? Selected;
    public event Action? Cancelled;

    public SelectionWindow(System.Drawing.Rectangle bounds, BitmapSource shot)
    {
        this.bounds = bounds;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = bounds.X; Top = bounds.Y; Width = bounds.Width; Height = bounds.Height;
        Cursor = Cursors.Cross;

        view = new SelView(shot);
        view.Picked += rect => Selected?.Invoke(rect, VisualTreeHelper.GetDpi(this).DpiScaleX);
        view.Cancelled += () => Cancelled?.Invoke();
        Content = view;

        SourceInitialized += (_, _) => Place();
        Loaded += (_, _) => Place();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Cancelled?.Invoke(); };
    }

    void Place()
    {
        var h = new WindowInteropHelper(this).Handle;
        SetWindowPos(h, HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0);
    }

    sealed class SelView : FrameworkElement
    {
        readonly BitmapSource shot;
        Point? start;
        Rect sel = Rect.Empty;
        public event Action<BitmapSource>? Picked;
        public event Action? Cancelled;

        public SelView(BitmapSource shot) { this.shot = shot; }

        protected override void OnRender(DrawingContext dc)
        {
            var full = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawImage(shot, full);
            var dim = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
            if (sel.IsEmpty)
            {
                dc.DrawRectangle(dim, null, full);
                return;
            }
            dc.DrawGeometry(dim, null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(sel)));
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)), 1), sel);

            double k = shot.PixelWidth / ActualWidth;
            var ft = new FormattedText($"{(int)Math.Round(sel.Width * k)} × {(int)Math.Round(sel.Height * k)}",
                CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double ly = sel.Top - 22 < 0 ? sel.Top + 2 : sel.Top - 22;
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), null, new Rect(sel.Left, ly, ft.Width + 10, 20));
            dc.DrawText(ft, new Point(sel.Left + 5, ly + 2));
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            start = e.GetPosition(this);
            sel = new Rect(start.Value, start.Value);
            CaptureMouse();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (start == null) return;
            sel = new Rect(start.Value, e.GetPosition(this));
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (start == null) return;
            ReleaseMouseCapture();
            start = null;
            if (sel.Width < 4 || sel.Height < 4) { sel = Rect.Empty; InvalidateVisual(); return; }
            double kx = shot.PixelWidth / ActualWidth, ky = shot.PixelHeight / ActualHeight;
            int x = Math.Max(0, (int)Math.Round(sel.X * kx)), y = Math.Max(0, (int)Math.Round(sel.Y * ky));
            int w = Math.Min(shot.PixelWidth - x, (int)Math.Round(sel.Width * kx));
            int h = Math.Min(shot.PixelHeight - y, (int)Math.Round(sel.Height * ky));
            if (w < 1 || h < 1) return;
            var cb = new CroppedBitmap(shot, new Int32Rect(x, y, w, h));
            cb.Freeze();
            Picked?.Invoke(cb);
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e) => Cancelled?.Invoke();
    }
}
