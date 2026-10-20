using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;


namespace ScreenDraw;

static class Theme
{
    public static readonly Color Bg = Color.FromRgb(0x20, 0x20, 0x20);
    public static readonly Color Surface = Color.FromRgb(0x2B, 0x2B, 0x2B);
    public static readonly Color Stage = Color.FromRgb(0x18, 0x18, 0x18);
    public static readonly Color Text = Color.FromRgb(0xE6, 0xE6, 0xE6);
    public static SolidColorBrush Brush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    const string Xaml = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Style x:Key="IconToggle" TargetType="ToggleButton">
    <Setter Property="Padding" Value="8,4"/>
    <Setter Property="Foreground" Value="#E6E6E6"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ToggleButton">
      <Border x:Name="bd" CornerRadius="4" Background="Transparent" BorderThickness="1" BorderBrush="Transparent" Padding="{TemplateBinding Padding}">
        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
      <ControlTemplate.Triggers>
        <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="bd" Property="Background" Value="#38383F"/></Trigger>
        <Trigger Property="IsChecked" Value="True"><Setter TargetName="bd" Property="Background" Value="#46464F"/><Setter TargetName="bd" Property="BorderBrush" Value="#5B8DEF"/></Trigger>
        <Trigger Property="IsEnabled" Value="False"><Setter TargetName="bd" Property="Opacity" Value="0.35"/></Trigger>
      </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key="IconButton" TargetType="Button">
    <Setter Property="Padding" Value="8,4"/>
    <Setter Property="Foreground" Value="#E6E6E6"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
      <Border x:Name="bd" CornerRadius="4" Background="Transparent" BorderThickness="1" BorderBrush="Transparent" Padding="{TemplateBinding Padding}">
        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
      <ControlTemplate.Triggers>
        <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="bd" Property="Background" Value="#38383F"/></Trigger>
        <Trigger Property="IsPressed" Value="True"><Setter TargetName="bd" Property="Background" Value="#46464F"/></Trigger>
        <Trigger Property="IsEnabled" Value="False"><Setter TargetName="bd" Property="Opacity" Value="0.35"/></Trigger>
      </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
  </Style>
</ResourceDictionary>
""";

    static ResourceDictionary? dict;
    public static ResourceDictionary Styles => dict ??= (ResourceDictionary)XamlReader.Parse(Xaml);

    public static Image Icon(string name)
    {
        var bi = new BitmapImage(new Uri($"pack://application:,,,/Assets/{name}.png"));
        bi.Freeze();
        var im = new Image { Source = bi, Width = bi.PixelWidth / 3.0, Height = bi.PixelHeight / 3.0, Stretch = Stretch.Fill, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
        return im;
    }

    static BitmapFrame? appIcon;
    public static BitmapFrame AppIcon => appIcon ??= BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.png"));

    public static FrameworkElement BgIcon(int v)
    {
        var g = new Grid { Width = 20, Height = 20, IsHitTestVisible = false };
        var fill = v == 1 ? Color.FromRgb(0xF2, 0xF2, 0xF2) : v == 2 ? Color.FromRgb(0x2A, 0x2A, 0x2A) : Colors.Transparent;
        g.Children.Add(new Border { Background = new SolidColorBrush(fill), BorderBrush = Brush(Color.FromRgb(0x88, 0x88, 0x88)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2) });
        if (v == 0)
            g.Children.Add(new System.Windows.Shapes.Line { X1 = 3, Y1 = 17, X2 = 17, Y2 = 3, Stroke = Brush(Color.FromRgb(0xE5, 0x48, 0x3B)), StrokeThickness = 2 });
        else
            g.Children.Add(new TextBlock { Text = "A", FontWeight = FontWeights.Bold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = v == 1 ? Brush(Color.FromRgb(0xE5, 0x48, 0x3B)) : Brushes.White });
        return g;
    }

    public static FrameworkElement BalloonIcon() => new System.Windows.Shapes.Path
    {
        Data = Geometry.Parse("M 4,3 L 17,3 Q 19,3 19,5 L 19,12 Q 19,14 17,14 L 11,14 L 6,19 L 7,14 L 4,14 Q 2,14 2,12 L 2,5 Q 2,3 4,3 Z"),
        Stroke = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)), StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round,
        Width = 18, Height = 18, Stretch = Stretch.Uniform, IsHitTestVisible = false,
    };

    public static FrameworkElement CursorIcon() => new System.Windows.Shapes.Path
    {
        Data = Geometry.Parse("M 2,1 L 2,17 L 6.2,13.2 L 9,19.5 L 11.6,18.4 L 8.9,12.2 L 14.5,12 Z"),
        Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 0.5, Width = 16, Height = 20, Stretch = Stretch.Uniform,
        IsHitTestVisible = false,
    };
}

sealed class EditorWindow : Window
{
    static readonly string[] Palette = { "#222222", "#FFFFFF", "#3A7BD5", "#F5D142", "#E5483B", "#7BC043", "#9B59B6", "#8E8E93" };

    readonly Config cfg;
    readonly CanvasView view;
    readonly Canvas overlay = new();
    readonly TextBlock status = new() { Margin = new Thickness(10, 5, 10, 5), Foreground = Theme.Brush(Color.FromRgb(0xAA, 0xAA, 0xAA)) };
    readonly Dictionary<Tool, ToggleButton> toolButtons = new();
    readonly List<(string hex, Border b)> swatches = new();
    readonly List<(double w, ToggleButton b)> presets = new();
    readonly List<(int v, ToggleButton b)> bgButtons = new();
    readonly Button undoBtn, redoBtn;
    readonly Slider width = new() { Minimum = 1, Maximum = 20, Width = 90, VerticalAlignment = VerticalAlignment.Center, IsSnapToTickEnabled = true, TickFrequency = 1 };
    readonly TextBlock widthLbl = new() { Width = 22, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Brush(Theme.Text) };

    TextBox? tb;
    TextAnn? editing;
    Point editPos;

    readonly List<(int v, ToggleButton b)> arrowButtons = new();
    FrameworkElement arrowGroup = null!, textGroup = null!;
    Border cropBar = null!;
    ScrollViewer scroll = null!;
    readonly Slider zoomSlider = new() { Minimum = 10, Maximum = 400, Value = 100, Width = 150, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
    readonly Button zoomLbl = new() { Content = "100%", Width = 52, Focusable = false, ToolTip = "Сбросить масштаб (Ctrl+0)", Margin = new Thickness(8, 4, 2, 4) };
    bool syncingZoom;

    public EditorWindow(BitmapSource image, double dpiScale, Config cfg)
    {
        this.cfg = cfg;
        Resources.MergedDictionaries.Add(Theme.Styles);
        Title = "ScreenDraw";
        Icon = Theme.AppIcon;
        Background = Theme.Brush(Theme.Bg);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SizeToContent = SizeToContent.WidthAndHeight;
        var wa = SystemParameters.WorkArea;
        MaxWidth = wa.Width; MaxHeight = wa.Height;

        view = new CanvasView(image, dpiScale)
        {
            CurColor = (Color)ColorConverter.ConvertFromString(cfg.Color),
            CurW = cfg.Width,
            CurTextBg = cfg.TextBg,
            CurArrowStyle = cfg.ArrowStyle,
        };
        view.Changed += RefreshUndo;
        view.TextRequested += BeginText;
        view.ImageChanged += FitWindow;
        ContentRendered += OnFirstRender;
        view.ToolChanged += t => { SetToolButtons(t); UpdateContext(); };
        view.SelectionChanged += a =>
        {
            if (a is TextAnn ta) { view.CurTextBg = ta.Bg; RefreshBg(ta.Bg); }
            if (a is ArrowAnn aa) { view.CurArrowStyle = aa.Style; RefreshArrow(aa.Style); }
            UpdateContext();
        };

        var iconToggle = (Style)Resources["IconToggle"];
        var iconButton = (Style)Resources["IconButton"];
        var dim = Theme.Brush(Color.FromRgb(0xAA, 0xAA, 0xAA));

        WrapPanel Row() => new() { Margin = new Thickness(8, 3, 8, 3) };
        var row1 = Row();
        var row2 = Row();
        void Add(Panel row, UIElement el, double right = 4) { if (el is FrameworkElement f) f.Margin = new Thickness(0, 2, right, 2); row.Children.Add(el); }

        object Label(UIElement icon, string text) => new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { icon, new TextBlock { Text = text, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Brush(Theme.Text) } },
        };

        // ---- row 1: primitives
        foreach (var (tool, icon, label, tip) in new (Tool, UIElement, string, string)[] {
            (Tool.Select, Theme.CursorIcon(), "Выбор", "Выбрать и преобразовать (V). Клик по выбранному — режим поворота. Ctrl — временно"),
            (Tool.Arrow, Theme.Icon("arrow"), "Стрелка", "A"),
            (Tool.Line, Theme.Icon("line"), "Линия", "L"),
            (Tool.Rect, Theme.Icon("rect"), "Рамка", "R"),
            (Tool.Ellipse, Theme.Icon("ellipse"), "Овал", "O"),
            (Tool.Balloon, Theme.BalloonIcon(), "Выноска", "Выноска с указателем (K). Узел — куда указывает"),
            (Tool.Text, Theme.Icon("text"), "Текст", "T"),
            (Tool.Marker, Theme.Icon("marker"), "Маркер", "Выделитель (M)"),
            (Tool.Blur, Theme.Icon("blur"), "Размыть", "Пикселизация области (B)"),
            (Tool.Crop, Theme.Icon("crop"), "Обрезать", "Обрезка (C). Enter — применить, Esc — отмена") })
        {
            var t = tool;
            var b = new ToggleButton { Content = Label(icon, label), ToolTip = tip, Style = iconToggle, Focusable = false };
            b.Click += (_, _) => SetTool(t);
            toolButtons[tool] = b; Add(row1, b, 2);
        }
        var pasteBtn = new Button
        {
            Content = new TextBlock { Text = "Вставить", Foreground = Theme.Brush(Theme.Text) }, Style = iconButton, Focusable = false,
            ToolTip = "Вставить изображение из буфера (Ctrl+V) — или перетащите файл в окно",
        };
        pasteBtn.Click += (_, _) => Paste();
        Add(row1, pasteBtn, 2);
        AllowDrop = true;
        Drop += OnDrop;

        // ---- row 2: options (arrow style / text background, colour, thickness, undo)
        var ag = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var (v, icon, tip) in new[] { (0, "arrow_normal", "Обычная стрелка"), (1, "arrow_taper", "Сужающаяся стрелка"), (2, "arrow_dots", "Пунктирная изогнутая стрелка (средний узел — изгиб)") })
        {
            var vv = v;
            var b = new ToggleButton { Content = Theme.Icon(icon), ToolTip = tip, Style = iconToggle, Focusable = false, Padding = new Thickness(6, 5, 6, 5) };
            b.Click += (_, _) => { view.SetArrowStyle(vv); cfg.ArrowStyle = vv; cfg.Save(); RefreshArrow(vv); };
            arrowButtons.Add((v, b)); ag.Children.Add(b);
        }
        arrowGroup = ag; row2.Children.Add(ag);

        var tg = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
        tg.Children.Add(new TextBlock { Text = "Фон текста", Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = dim });
        foreach (var (v, tip) in new[] { (1, "Светлый фон"), (2, "Тёмный фон"), (0, "Без фона") })
        {
            var vv = v;
            var b = new ToggleButton { Content = Theme.BgIcon(v), ToolTip = tip, Style = iconToggle, Focusable = false, Padding = new Thickness(5) };
            b.Click += (_, _) => { view.SetTextBg(vv); cfg.TextBg = vv; cfg.Save(); RefreshBg(vv); };
            bgButtons.Add((v, b)); tg.Children.Add(b);
        }
        textGroup = tg; row2.Children.Add(tg);

        foreach (var hex in Palette)
        {
            var h = hex;
            var sw = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Cursor = Cursors.Hand,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
            };
            sw.MouseLeftButtonUp += (_, _) => SetColor(h);
            swatches.Add((hex, sw)); Add(row2, sw, 6);
        }
        var more = new Button { Content = new TextBlock { Text = "…", Foreground = Theme.Brush(Theme.Text) }, ToolTip = "Другой цвет", Width = 26, Style = iconButton, Focusable = false };
        more.Click += (_, _) =>
        {
            using var d = new System.Windows.Forms.ColorDialog { FullOpen = true };
            if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                SetColor($"#{d.Color.R:X2}{d.Color.G:X2}{d.Color.B:X2}");
        };
        Add(row2, more, 10);

        foreach (var (w, icon, tip) in new[] { (2.0, "thin", "Тонкая"), (5.0, "mid", "Средняя"), (10.0, "thick", "Толстая") })
        {
            var ww = w;
            var b = new ToggleButton { Content = Theme.Icon(icon), ToolTip = tip, Style = iconToggle, Focusable = false, Padding = new Thickness(6) };
            b.Click += (_, _) => { width.Value = ww; };
            presets.Add((w, b)); Add(row2, b, 2);
        }
        width.Value = cfg.Width; widthLbl.Text = cfg.Width.ToString("0");
        width.ValueChanged += (_, e) =>
        {
            view.CurW = e.NewValue; cfg.Width = e.NewValue; cfg.Save();
            widthLbl.Text = e.NewValue.ToString("0");
            foreach (var (pw, pb) in presets) pb.IsChecked = Math.Abs(pw - e.NewValue) < 0.01;
            view.StyleSelected(null, e.NewValue);
        };
        Add(row2, width, 0); Add(row2, widthLbl, 10);
        foreach (var (pw, pb) in presets) pb.IsChecked = Math.Abs(pw - cfg.Width) < 0.01;

        undoBtn = new Button { Content = Theme.Icon("undo"), ToolTip = "Отменить (Ctrl+Z)", Style = iconButton, Focusable = false };
        redoBtn = new Button { Content = Theme.Icon("redo"), ToolTip = "Повторить (Ctrl+Y)", Style = iconButton, Focusable = false };
        undoBtn.Click += (_, _) => view.Undo(); redoBtn.Click += (_, _) => view.Redo();
        Add(row2, undoBtn, 2); Add(row2, redoBtn, 0);

        var line = Theme.Brush(Color.FromRgb(0x3A, 0x3A, 0x3A));
        var toolbar = new StackPanel { Children = { row1, row2 } };
        var toolbarBox = new Border { Background = Theme.Brush(Theme.Surface), BorderBrush = line, BorderThickness = new Thickness(0, 0, 0, 1), Child = toolbar };

        // ---- crop bar (visible only in crop mode)
        var apply = new Button { Content = "Применить", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0), Focusable = false, Background = Theme.Brush(Color.FromRgb(0xF5, 0xD1, 0x42)), Foreground = Brushes.Black };
        var cancel = new Button { Content = "Отмена", Padding = new Thickness(12, 4, 12, 4), Focusable = false };
        apply.Click += (_, _) => ApplyCrop(); cancel.Click += (_, _) => CancelCrop();
        cropBar = new Border
        {
            Background = Theme.Brush(Color.FromRgb(0x33, 0x33, 0x33)), BorderBrush = Theme.Brush(Color.FromRgb(0x3A, 0x3A, 0x3A)), BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 5, 8, 5), Visibility = Visibility.Collapsed,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { apply, cancel } },
        };

        // ---- bottom bar: status + actions
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var (text, tip, act) in new (string, string, Action)[] {
            ("Копировать", "Ctrl+C", Copy), ("Сохранить", "Ctrl+S", Save), ("Сохранить как…", "Ctrl+Shift+S", SaveAs) })
        {
            var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(6, 6, 0, 6), Focusable = false };
            b.Click += (_, _) => act();
            actions.Children.Add(b);
        }
        var bottom = new Grid { Background = Theme.Brush(Theme.Surface) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.VerticalAlignment = VerticalAlignment.Center; status.TextTrimming = TextTrimming.CharacterEllipsis;

        var zoomMinus = new Button { Content = "−", Width = 26, Focusable = false, ToolTip = "Уменьшить (Ctrl+колесо)", Style = iconButton, Padding = new Thickness(0, 2, 0, 2) };
        var zoomPlus = new Button { Content = "+", Width = 26, Focusable = false, ToolTip = "Увеличить (Ctrl+колесо)", Style = iconButton, Padding = new Thickness(0, 2, 0, 2) };
        zoomMinus.Foreground = zoomPlus.Foreground = Theme.Brush(Theme.Text);
        zoomMinus.Click += (_, _) => ApplyZoom(view.Zoom - 0.1);
        zoomPlus.Click += (_, _) => ApplyZoom(view.Zoom + 0.1);
        zoomLbl.Click += (_, _) => ApplyZoom(1);
        zoomSlider.ValueChanged += (_, ev) => { if (!syncingZoom) ApplyZoom(ev.NewValue / 100); };
        view.ZoomChanged += () =>
        {
            syncingZoom = true;
            zoomSlider.Value = view.Zoom * 100;
            zoomLbl.Content = $"{Math.Round(view.Zoom * 100)}%";
            syncingZoom = false;
        };
        var zoomGroup = new StackPanel { Orientation = Orientation.Horizontal, Children = { zoomLbl, zoomMinus, zoomSlider, zoomPlus } };
        Grid.SetColumn(status, 1); Grid.SetColumn(actions, 2);
        bottom.Children.Add(zoomGroup); bottom.Children.Add(status); bottom.Children.Add(actions);
        var bottomBox = new Border { BorderBrush = line, BorderThickness = new Thickness(0, 1, 0, 0), Child = bottom };

        var stage = new Grid();
        stage.Children.Add(view);
        stage.Children.Add(overlay);
        scroll = new ScrollViewer
        {
            Content = stage, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Theme.Brush(Theme.Stage),
        };

        var root = new DockPanel();
        var top = new StackPanel { Children = { toolbarBox, cropBar } };
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        DockPanel.SetDock(bottomBox, Dock.Bottom); root.Children.Add(bottomBox);
        root.Children.Add(scroll);
        Content = root;

        // window is at least as wide as the widest toolbar row, otherwise it hugs the captured area
        // the arrow-style and text-background groups are never shown together, so measure each separately
        double need = 0;
        foreach (var (ag2, tg2) in new[] { (Visibility.Visible, Visibility.Collapsed), (Visibility.Collapsed, Visibility.Visible) })
        {
            arrowGroup.Visibility = ag2; textGroup.Visibility = tg2;
            foreach (var r in new[] { row1, row2 })
            {
                r.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                need = Math.Max(need, r.DesiredSize.Width);
            }
        }
        MinWidth = need + 24;
        bottom.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        MinWidth = Math.Max(MinWidth, bottom.DesiredSize.Width + 24);

        SetTool(Tool.Arrow);
        SetColor(cfg.Color, apply: false);
        RefreshBg(cfg.TextBg); RefreshArrow(cfg.ArrowStyle);
        RefreshUndo();
    }

    /// <summary>Set the zoom keeping the image point under <paramref name="anchorDip"/> (view coordinates) fixed; default = viewport centre.</summary>
    void ApplyZoom(double z, Point? anchorDip = null)
    {
        z = Math.Clamp(z, 0.1, 8);
        if (Math.Abs(z - view.Zoom) < 1e-6) return;
        var a = anchorDip ?? scroll.TranslatePoint(new Point(scroll.ViewportWidth / 2, scroll.ViewportHeight / 2), view);
        var img = view.ViewToImage(a);
        view.SetZoom(z);
        view.UpdateLayout();
        var na = view.ImageToView(img);
        scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + (na.X - a.X));
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset + (na.Y - a.Y));
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            ApplyZoom(view.Zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), Mouse.GetPosition(view));
            return;
        }
        base.OnPreviewMouseWheel(e);
    }

    // ---------------------------------------------------------------- pasted / dropped images
    static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".ico" };

    static BitmapSource? LoadFile(string path)
    {
        if (!ImageExt.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant())) return null;
        try
        {
            var b = new BitmapImage();
            b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.UriSource = new Uri(path); b.EndInit(); b.Freeze();
            return b;
        }
        catch { return null; }
    }

    void Paste()
    {
        try
        {
            BitmapSource? bmp = null;
            var data = Clipboard.GetDataObject();
            if (data?.GetDataPresent("PNG") == true && data.GetData("PNG") is Stream ps)
            {
                ps.Position = 0;
                var f = new PngBitmapDecoder(ps, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                f.Freeze(); bmp = f;
            }
            else if (Clipboard.ContainsImage()) { bmp = Clipboard.GetImage(); bmp?.Freeze(); }
            else if (Clipboard.ContainsFileDropList())
                foreach (var f in Clipboard.GetFileDropList()) { bmp = f == null ? null : LoadFile(f); if (bmp != null) break; }
            if (bmp == null) { status.Text = "В буфере нет изображения"; return; }
            PlaceImage(bmp, null);
        }
        catch (Exception ex) { status.Text = "Не удалось вставить: " + ex.Message; }
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        try
        {
            BitmapSource? bmp = null;
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
                foreach (var f in files) { bmp = LoadFile(f); if (bmp != null) break; }
            if (bmp == null) { status.Text = "Это не изображение"; return; }
            PlaceImage(bmp, e.GetPosition(view));
        }
        catch (Exception ex) { status.Text = "Не удалось вставить: " + ex.Message; }
    }

    /// <summary>Add the bitmap as a selectable object (at a view point, or the middle of the visible area).</summary>
    void PlaceImage(BitmapSource bmp, Point? viewPoint)
    {
        CommitText();
        SetTool(Tool.Select);
        var c = viewPoint ?? scroll.TranslatePoint(new Point(scroll.ViewportWidth / 2, scroll.ViewportHeight / 2), view);
        view.AddImage(bmp, view.ViewToImage(c));
        status.Text = "Изображение вставлено — тяните за рамку, Delete — удалить";
    }

    void SetToolButtons(Tool t) { foreach (var (k, b) in toolButtons) b.IsChecked = k == t; }

    void UpdateContext()
    {
        arrowGroup.Visibility = view.Tool == Tool.Arrow || view.Selected is ArrowAnn ? Visibility.Visible : Visibility.Collapsed;
        textGroup.Visibility = view.Tool == Tool.Text || view.Selected is TextAnn ? Visibility.Visible : Visibility.Collapsed;
    }

    void RefreshArrow(int v) { foreach (var (av, ab) in arrowButtons) ab.IsChecked = av == v; }

    public void SaveTest(string path) => WriteFile(path);
    public void TestZoom(double z) => ApplyZoom(z);

    public void AddSamples()
    {
        view.Items.Add(new ArrowAnn { Color = Colors.Red, W = 4, A = new Point(30, 30), B = new Point(200, 120) });
        view.Items.Add(new RectAnn { Color = Colors.DodgerBlue, W = 3, A = new Point(60, 150), B = new Point(220, 230), Rot = 20 });
        view.Items.Add(new EllipseAnn { Color = Colors.Orange, W = 3, A = new Point(300, 150), B = new Point(420, 230) });
        view.Items.Add(new TextAnn { Color = Colors.LimeGreen, W = 4, A = new Point(240, 40), B = new Point(240, 40), Text = "Привет", Rot = -10, Bg = 1 });
        view.Items.Add(new ArrowAnn { Color = Colors.Red, W = 4, Style = 1, A = new Point(40, 260), B = new Point(260, 200) });
        view.Items.Add(new ArrowAnn { Color = Colors.Red, W = 4, Style = 2, A = new Point(300, 280), B = new Point(470, 240), MidU = 0.5, MidV = -0.3 });
        view.Items.Add(new MarkerAnn { Color = Colors.Gold, W = 4, A = new Point(20, 80), B = new Point(220, 80) });
        view.Items.Add(new BlurAnn { W = 4, A = new Point(250, 20), B = new Point(380, 70) });
        view.Items.Add(new BalloonAnn { Color = Colors.Orange, W = 3, A = new Point(40, 100), B = new Point(200, 160) });
        var px = new byte[60 * 40 * 4];
        for (int i = 0; i < 60 * 40; i++) { px[i * 4] = (byte)(i % 60 * 4); px[i * 4 + 1] = (byte)(i / 60 * 6); px[i * 4 + 2] = 200; px[i * 4 + 3] = 255; }
        var sample = BitmapSource.Create(60, 40, 96, 96, PixelFormats.Bgra32, null, px, 60 * 4); sample.Freeze();
        view.Items.Add(new ImageAnn { Src = sample, A = new Point(300, 10), B = new Point(420, 90), Rot = 12 });
        view.Select(view.Items[^1]);
        SetTool(Tool.Select);
    }

    // ---------------------------------------------------------------- toolbar state
    void SetTool(Tool t)
    {
        CommitText();
        if (view.CropMode && t != Tool.Crop) view.CancelCrop();
        view.Tool = t;
        if (t == Tool.Crop) view.BeginCrop();
        cropBar.Visibility = view.CropMode ? Visibility.Visible : Visibility.Collapsed;
        SetToolButtons(t);
        UpdateContext();
    }

    void ApplyCrop()
    {
        view.ApplyCrop();
        SetTool(Tool.Select);
        FitWindow();
    }

    void CancelCrop() => SetTool(Tool.Select);

    void FitWindow()
    {
        // re-fit the window to the new image size once, then freeze it again (zoom must not resize the window)
        SizeToContent = SizeToContent.Manual;
        Width = double.NaN; Height = double.NaN;
        SizeToContent = SizeToContent.WidthAndHeight;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, FreezeSize);
    }

    void OnFirstRender(object? s, EventArgs e)
    {
        ContentRendered -= OnFirstRender;
        FreezeSize();   // the initial size fits the capture; after that only an explicit re-fit may change it
    }

    void FreezeSize()
    {
        double w = ActualWidth, h = ActualHeight;
        SizeToContent = SizeToContent.Manual;
        Width = w; Height = h;
    }

    void SetColor(string hex, bool apply = true)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        view.CurColor = c;
        cfg.Color = hex; cfg.Save();
        foreach (var (h, b) in swatches)
        {
            bool on = string.Equals(h, hex, StringComparison.OrdinalIgnoreCase);
            b.BorderBrush = on ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            b.BorderThickness = new Thickness(on ? 3 : 1);
        }
        if (apply) view.StyleSelected(c, null);
    }

    void RefreshBg(int v) { foreach (var (bv, bb) in bgButtons) bb.IsChecked = bv == v; }

    void RefreshUndo() { undoBtn.IsEnabled = view.CanUndo; redoBtn.IsEnabled = view.CanRedo; }

    // ---------------------------------------------------------------- inline text editing
    void BeginText(TextAnn? existing, Point imgPos)
    {
        CommitText();
        view.EndTextEdit();
        editing = existing; editPos = imgPos;
        double s = view.S;
        double px = existing?.Px ?? TextAnn.FontPx(view.CurW);
        var color = existing?.Color ?? view.CurColor;
        tb = new TextBox
        {
            Text = existing?.Text ?? "", AcceptsReturn = true, FontFamily = new FontFamily("Segoe UI"), FontSize = px * view.Zoom / s,
            Foreground = new SolidColorBrush(color), Background = new SolidColorBrush(((existing?.Bg ?? view.CurTextBg) == 1) ? Color.FromArgb(235, 250, 250, 250) : Color.FromArgb(150, 20, 20, 20)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x5B, 0x8D, 0xEF)), BorderThickness = new Thickness(1), Padding = new Thickness(0), MinWidth = 80,
            CaretBrush = Brushes.White,
        };
        var od = view.OriginDip; Canvas.SetLeft(tb, imgPos.X * view.Zoom / s + od.X - 1); Canvas.SetTop(tb, imgPos.Y * view.Zoom / s + od.Y - 1);
        view.Hidden = existing;
        overlay.Children.Add(tb);
        view.InvalidateVisual();
        tb.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { e.Handled = true; CommitText(); }
            else if (e.Key == Key.Escape) { e.Handled = true; CommitText(cancel: true); }
        };
        tb.LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(() => CommitText());
        tb.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
    }

    void CommitText(bool cancel = false)
    {
        if (tb == null) return;
        var box = tb; tb = null;
        var text = box.Text;
        overlay.Children.Remove(box);
        view.Hidden = null;
        if (!cancel) view.CommitText(editing, editPos, text);
        view.InvalidateVisual();
        view.Focus();
    }

    // ---------------------------------------------------------------- keys
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (Keyboard.FocusedElement is TextBox) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Escape: if (view.CropMode) CancelCrop(); else Close(); break;
            case Key.Enter when view.CropMode: ApplyCrop(); break;
            case Key.Z when ctrl && shift: view.Redo(); break;
            case Key.Z when ctrl: view.Undo(); break;
            case Key.Y when ctrl: view.Redo(); break;
            case Key.C when ctrl: Copy(); break;
            case Key.S when ctrl && shift: SaveAs(); break;
            case Key.S when ctrl: Save(); break;
            case Key.D0 or Key.NumPad0 when ctrl: ApplyZoom(1); break;
            case Key.OemPlus or Key.Add when ctrl: ApplyZoom(view.Zoom + 0.1); break;
            case Key.OemMinus or Key.Subtract when ctrl: ApplyZoom(view.Zoom - 0.1); break;
            case Key.Delete: case Key.Back: view.DeleteSelected(); break;
            case Key.V when ctrl: Paste(); break;
            case Key.V when !ctrl: SetTool(Tool.Select); break;
            case Key.A when !ctrl: SetTool(Tool.Arrow); break;
            case Key.L when !ctrl: SetTool(Tool.Line); break;
            case Key.R when !ctrl: SetTool(Tool.Rect); break;
            case Key.O when !ctrl: SetTool(Tool.Ellipse); break;
            case Key.K when !ctrl: SetTool(Tool.Balloon); break;
            case Key.T when !ctrl: SetTool(Tool.Text); break;
            case Key.M when !ctrl: SetTool(Tool.Marker); break;
            case Key.B when !ctrl: SetTool(Tool.Blur); break;
            case Key.C when !ctrl: SetTool(Tool.Crop); break;
            case Key.Left: view.Nudge(new Vector(-view.U, 0)); break;
            case Key.Right: view.Nudge(new Vector(view.U, 0)); break;
            case Key.Up: view.Nudge(new Vector(0, -view.U)); break;
            case Key.Down: view.Nudge(new Vector(0, view.U)); break;
            default: e.Handled = false; break;
        }
    }

    // ---------------------------------------------------------------- output
    void Finish(string msg)
    {
        status.Text = msg;
        if (cfg.CloseAfter) Close();
    }

    static string NewName() => DateTime.Now.ToString("'screenshot_'yyyy-MM-dd_HH-mm-ss'.png'");

    BitmapSource Result() { CommitText(); return view.Render(); }

    static void ToClipboard(BitmapSource bmp)
    {
        var data = new DataObject();
        data.SetImage(bmp);
        var ms = new MemoryStream();
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp)); enc.Save(ms);
        data.SetData("PNG", ms);
        Clipboard.SetDataObject(data, true);
    }

    void Copy()
    {
        try
        {
            Directory.CreateDirectory(cfg.SaveDir);
            WriteFile(System.IO.Path.Combine(cfg.SaveDir, NewName()), forceCopy: true);   // always saved to the folder too
        }
        catch (Exception ex) { status.Text = "Не удалось сохранить: " + ex.Message; }
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(cfg.SaveDir);
            WriteFile(System.IO.Path.Combine(cfg.SaveDir, NewName()));
        }
        catch (Exception ex) { status.Text = "Не удалось сохранить: " + ex.Message; }
    }

    void SaveAs()
    {
        var d = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg", FileName = NewName(),
            InitialDirectory = Directory.Exists(cfg.SaveDir) ? cfg.SaveDir : null,
        };
        if (d.ShowDialog(this) == true)
        {
            try { WriteFile(d.FileName); }
            catch (Exception ex) { status.Text = "Не удалось сохранить: " + ex.Message; }
        }
    }

    void WriteFile(string path, bool forceCopy = false)
    {
        var bmp = Result();
        BitmapEncoder enc = path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? new JpegBitmapEncoder { QualityLevel = 95 } : new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(path)) enc.Save(fs);
        if (forceCopy || cfg.CopyOnSave) { try { ToClipboard(bmp); } catch { } }
        Finish((forceCopy ? "Скопировано и сохранено: " : "Сохранено: ") + path);
    }
}
