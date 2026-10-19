using System;
using System.Collections.Generic;
using System.IO;
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
    readonly Button undoBtn, redoBtn;
    readonly Slider width = new() { Minimum = 1, Maximum = 20, Width = 90, VerticalAlignment = VerticalAlignment.Center, IsSnapToTickEnabled = true, TickFrequency = 1 };
    readonly TextBlock widthLbl = new() { Width = 22, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Brush(Theme.Text) };

    TextBox? tb;
    TextAnn? editing;
    Point editPos;

    public EditorWindow(BitmapSource image, double dpiScale, Config cfg)
    {
        this.cfg = cfg;
        Resources.MergedDictionaries.Add(Theme.Styles);
        Title = "ScreenDraw";
        Background = Theme.Brush(Theme.Bg);
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 900;
        var wa = SystemParameters.WorkArea;
        MaxWidth = wa.Width; MaxHeight = wa.Height;

        view = new CanvasView(image, dpiScale)
        {
            CurColor = (Color)ColorConverter.ConvertFromString(cfg.Color),
            CurW = cfg.Width,
        };
        view.Changed += RefreshUndo;
        view.TextRequested += BeginText;

        var iconToggle = (Style)Resources["IconToggle"];
        var iconButton = (Style)Resources["IconButton"];

        var bar = new WrapPanel { Margin = new Thickness(8, 6, 8, 6) };
        void Add(UIElement el, double right = 4) { if (el is FrameworkElement f) f.Margin = new Thickness(0, 2, right, 2); bar.Children.Add(el); }

        object Label(UIElement icon, string text) => new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { icon, new TextBlock { Text = text, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Brush(Theme.Text) } },
        };

        foreach (var (tool, icon, label, tip) in new (Tool, UIElement, string, string)[] {
            (Tool.Select, Theme.CursorIcon(), "Выбор", "Выбрать и преобразовать (V). Клик по выбранному — режим поворота. Ctrl — временно"),
            (Tool.Arrow, Theme.Icon("arrow"), "Стрелка", "A"),
            (Tool.Line, Theme.Icon("line"), "Линия", "L"),
            (Tool.Rect, Theme.Icon("rect"), "Рамка", "R"),
            (Tool.Ellipse, Theme.Icon("ellipse"), "Овал", "O"),
            (Tool.Text, Theme.Icon("text"), "Текст", "T") })
        {
            var t = tool;
            var b = new ToggleButton { Content = Label(icon, label), ToolTip = tip, Style = iconToggle, Focusable = false };
            b.Click += (_, _) => SetTool(t);
            toolButtons[tool] = b; Add(b, 2);
        }
        Add(new Border { Width = 12 }, 0);

        foreach (var hex in Palette)
        {
            var h = hex;
            var sw = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Cursor = Cursors.Hand,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
            };
            sw.MouseLeftButtonUp += (_, _) => SetColor(h);
            swatches.Add((hex, sw)); Add(sw, 6);
        }
        var more = new Button { Content = new TextBlock { Text = "…", Foreground = Theme.Brush(Theme.Text) }, ToolTip = "Другой цвет", Width = 26, Style = iconButton, Focusable = false };
        more.Click += (_, _) =>
        {
            using var d = new System.Windows.Forms.ColorDialog { FullOpen = true };
            if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                SetColor($"#{d.Color.R:X2}{d.Color.G:X2}{d.Color.B:X2}");
        };
        Add(more, 10);

        foreach (var (w, icon, tip) in new[] { (2.0, "thin", "Тонкая"), (5.0, "mid", "Средняя"), (10.0, "thick", "Толстая") })
        {
            var ww = w;
            var b = new ToggleButton { Content = Theme.Icon(icon), ToolTip = tip, Style = iconToggle, Focusable = false, Padding = new Thickness(6) };
            b.Click += (_, _) => { width.Value = ww; };
            presets.Add((w, b)); Add(b, 2);
        }
        width.Value = cfg.Width; widthLbl.Text = cfg.Width.ToString("0");
        width.ValueChanged += (_, e) =>
        {
            view.CurW = e.NewValue; cfg.Width = e.NewValue; cfg.Save();
            widthLbl.Text = e.NewValue.ToString("0");
            foreach (var (pw, pb) in presets) pb.IsChecked = Math.Abs(pw - e.NewValue) < 0.01;
            view.StyleSelected(null, e.NewValue);
        };
        Add(width, 0); Add(widthLbl, 10);
        foreach (var (pw, pb) in presets) pb.IsChecked = Math.Abs(pw - cfg.Width) < 0.01;

        undoBtn = new Button { Content = Theme.Icon("undo"), ToolTip = "Отменить (Ctrl+Z)", Style = iconButton, Focusable = false };
        redoBtn = new Button { Content = Theme.Icon("redo"), ToolTip = "Повторить (Ctrl+Y)", Style = iconButton, Focusable = false };
        undoBtn.Click += (_, _) => view.Undo(); redoBtn.Click += (_, _) => view.Redo();
        Add(undoBtn, 2); Add(redoBtn, 14);

        foreach (var (text, tip, act) in new (string, string, Action)[] {
            ("Копировать", "Ctrl+C", Copy), ("Сохранить", "Ctrl+S", Save), ("Сохранить как…", "Ctrl+Shift+S", SaveAs) })
        {
            var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(12, 4, 12, 4), Focusable = false };
            b.Click += (_, _) => act();
            Add(b, 4);
        }

        var toolbar = new Border { Background = Theme.Brush(Theme.Surface), BorderBrush = Theme.Brush(Color.FromRgb(0x3A, 0x3A, 0x3A)), BorderThickness = new Thickness(0, 0, 0, 1), Child = bar };

        var stage = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12) };
        stage.Children.Add(view);
        stage.Children.Add(overlay);
        var scroll = new ScrollViewer
        {
            Content = stage, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Theme.Brush(Theme.Stage),
        };

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var statusBar = new Border { Background = Theme.Brush(Theme.Surface), Child = status };
        DockPanel.SetDock(statusBar, Dock.Bottom); root.Children.Add(statusBar);
        root.Children.Add(scroll);
        Content = root;

        SetTool(Tool.Arrow);
        SetColor(cfg.Color, apply: false);
        RefreshUndo();
    }

    public void AddSamples()
    {
        view.Items.Add(new ArrowAnn { Color = Colors.Red, W = 4, A = new Point(30, 30), B = new Point(200, 120) });
        view.Items.Add(new RectAnn { Color = Colors.DodgerBlue, W = 3, A = new Point(60, 150), B = new Point(220, 230), Rot = 20 });
        view.Items.Add(new EllipseAnn { Color = Colors.Orange, W = 3, A = new Point(300, 150), B = new Point(420, 230) });
        view.Items.Add(new TextAnn { Color = Colors.LimeGreen, W = 4, A = new Point(240, 40), B = new Point(240, 40), Text = "Привет", Rot = -10 });
        view.Select(view.Items[1]);
        SetTool(Tool.Select);
    }

    // ---------------------------------------------------------------- toolbar state
    void SetTool(Tool t)
    {
        CommitText();
        view.Tool = t;
        foreach (var (k, b) in toolButtons) b.IsChecked = k == t;
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
            Text = existing?.Text ?? "", AcceptsReturn = true, FontFamily = new FontFamily("Segoe UI"), FontSize = px / s,
            Foreground = new SolidColorBrush(color), Background = new SolidColorBrush(Color.FromArgb(120, 20, 20, 20)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x5B, 0x8D, 0xEF)), BorderThickness = new Thickness(1), Padding = new Thickness(0), MinWidth = 80,
            CaretBrush = Brushes.White,
        };
        Canvas.SetLeft(tb, imgPos.X / s - 1); Canvas.SetTop(tb, imgPos.Y / s - 1);
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
            case Key.Escape: Close(); break;
            case Key.Z when ctrl && shift: view.Redo(); break;
            case Key.Z when ctrl: view.Undo(); break;
            case Key.Y when ctrl: view.Redo(); break;
            case Key.C when ctrl: Copy(); break;
            case Key.S when ctrl && shift: SaveAs(); break;
            case Key.S when ctrl: Save(); break;
            case Key.Delete: case Key.Back: view.DeleteSelected(); break;
            case Key.V when !ctrl: SetTool(Tool.Select); break;
            case Key.A when !ctrl: SetTool(Tool.Arrow); break;
            case Key.L when !ctrl: SetTool(Tool.Line); break;
            case Key.R when !ctrl: SetTool(Tool.Rect); break;
            case Key.O when !ctrl: SetTool(Tool.Ellipse); break;
            case Key.T when !ctrl: SetTool(Tool.Text); break;
            case Key.Left: view.Nudge(new Vector(-view.S, 0)); break;
            case Key.Right: view.Nudge(new Vector(view.S, 0)); break;
            case Key.Up: view.Nudge(new Vector(0, -view.S)); break;
            case Key.Down: view.Nudge(new Vector(0, view.S)); break;
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
        try { ToClipboard(Result()); Finish("Скопировано в буфер обмена"); }
        catch (Exception ex) { status.Text = "Не удалось скопировать: " + ex.Message; }
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

    void WriteFile(string path)
    {
        var bmp = Result();
        BitmapEncoder enc = path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? new JpegBitmapEncoder { QualityLevel = 95 } : new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(path)) enc.Save(fs);
        if (cfg.CopyOnSave) { try { ToClipboard(bmp); } catch { } }
        Finish("Сохранено: " + path);
    }
}
