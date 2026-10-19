using System;
using System.Collections.Generic;
using GdiFormat = System.Drawing.Imaging.PixelFormat;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace ScreenDraw;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "ScreenDraw.SingleInstance", out bool first);
        if (!first) return;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.Dark };
        var ctl = new Controller(app);
        if (args.Contains("--now")) app.Dispatcher.BeginInvoke(ctl.Capture);
        int ti = Array.IndexOf(args, "--test-rect");   // debug: skip selection, grab x,y,w,h and add sample marks
        if (ti >= 0) { var n = args[ti + 1].Split(',').Select(int.Parse).ToArray(); app.Dispatcher.BeginInvoke(() => ctl.TestRect(n[0], n[1], n[2], n[3])); }
        app.Run();
        ctl.Dispose();
    }
}

sealed class Controller : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    const int HotkeyId = 0x5D01, WM_HOTKEY = 0x0312;
    const uint MOD_NOREPEAT = 0x4000;

    readonly Application app;
    readonly Config cfg = Config.Load();
    readonly Forms.NotifyIcon tray;
    readonly HwndSource msgWindow;
    readonly List<SelectionWindow> overlays = new();
    bool hotkeyOk;

    public Controller(Application app)
    {
        this.app = app;
        msgWindow = new HwndSource(new HwndSourceParameters("ScreenDrawHotkey") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        msgWindow.AddHook(WndProc);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Сделать скриншот", null, (_, _) => Capture());
        menu.Items.Add("Настройки…", null, (_, _) => ShowSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => app.Shutdown());
        tray = new Forms.NotifyIcon { Icon = MakeIcon(), ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Capture(); };

        ApplyHotkey(announce: true);
    }

    static Drawing.Icon MakeIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var p1 = new Drawing.Pen(Drawing.Color.FromArgb(0x3A, 0x7B, 0xD5), 3);
            using var p2 = new Drawing.Pen(Drawing.Color.FromArgb(0xE5, 0x48, 0x3B), 3);
            g.DrawRectangle(p1, 4, 7, 24, 18);
            g.DrawLine(p2, 9, 21, 23, 11);
        }
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            app.Dispatcher.BeginInvoke(Capture);
        }
        return IntPtr.Zero;
    }

    void ApplyHotkey(bool announce = false)
    {
        UnregisterHotKey(msgWindow.Handle, HotkeyId);
        hotkeyOk = HotkeyText.TryParse(cfg.Hotkey, out var mods, out var vk)
                   && RegisterHotKey(msgWindow.Handle, HotkeyId, mods | MOD_NOREPEAT, vk);
        int err = hotkeyOk ? 0 : Marshal.GetLastWin32Error();
        tray.Text = $"ScreenDraw ({cfg.Hotkey})";
        if (!hotkeyOk)
            tray.ShowBalloonTip(5000, "ScreenDraw", $"Не удалось занять {cfg.Hotkey} — сочетание занято другой программой (код {err}). Выберите другое в настройках.", Forms.ToolTipIcon.Warning);
        else if (announce)
            tray.ShowBalloonTip(2000, "ScreenDraw", $"Запущено. Скриншот: {cfg.Hotkey}", Forms.ToolTipIcon.Info);
    }

    void ShowSettings()
    {
        var w = new SettingsWindow(cfg);
        if (w.ShowDialog() == true)
        {
            w.ApplyTo(cfg);
            cfg.Save();
            cfg.ApplyAutostart();
            ApplyHotkey();
        }
    }

    // ---------------------------------------------------------------- capture
    static BitmapSource Grab(Drawing.Rectangle r)
    {
        using var bmp = new Drawing.Bitmap(r.Width, r.Height, GdiFormat.Format32bppRgb);
        using (var g = Drawing.Graphics.FromImage(bmp))
            g.CopyFromScreen(r.X, r.Y, 0, 0, r.Size, Drawing.CopyPixelOperation.SourceCopy);
        var data = bmp.LockBits(new Drawing.Rectangle(0, 0, r.Width, r.Height), ImageLockMode.ReadOnly, GdiFormat.Format32bppRgb);
        try
        {
            var src = BitmapSource.Create(r.Width, r.Height, 96, 96, PixelFormats.Bgr32, null, data.Scan0, data.Stride * r.Height, data.Stride);
            src.Freeze();
            return src;
        }
        finally { bmp.UnlockBits(data); }
    }

    public void Capture()
    {
        try { CaptureCore(); }
        catch (Exception ex)
        {
            CloseOverlays();
            tray.ShowBalloonTip(5000, "ScreenDraw", "Ошибка: " + ex.Message, Forms.ToolTipIcon.Error);
        }
    }

    void CaptureCore()
    {
        if (overlays.Count > 0) return;
        var screens = Forms.Screen.AllScreens;
        var shots = screens.Select(s => Grab(s.Bounds)).ToList();
        var cursor = Forms.Cursor.Position;
        SelectionWindow? active = null;
        for (int i = 0; i < screens.Length; i++)
        {
            var w = new SelectionWindow(screens[i].Bounds, shots[i]);
            w.Selected += OnSelected;
            w.Cancelled += CloseOverlays;
            overlays.Add(w);
            if (screens[i].Bounds.Contains(cursor)) active = w;
        }
        foreach (var w in overlays) w.Show();
        (active ?? overlays[0]).Activate();
    }

    public void TestRect(int x, int y, int w, int h)
    {
        var ed = new EditorWindow(Grab(new Drawing.Rectangle(x, y, w, h)), 1, cfg);
        ed.Show();
        ed.AddSamples();
    }

    void CloseOverlays()
    {
        var list = overlays.ToList();
        overlays.Clear();
        foreach (var w in list) w.Close();
    }

    void OnSelected(BitmapSource crop, double dpiScale)
    {
        CloseOverlays();
        var ed = new EditorWindow(crop, dpiScale, cfg);
        ed.Show();
        ed.Activate();
    }

    public void Dispose()
    {
        UnregisterHotKey(msgWindow.Handle, HotkeyId);
        msgWindow.Dispose();
        tray.Visible = false;
        tray.Dispose();
    }
}
