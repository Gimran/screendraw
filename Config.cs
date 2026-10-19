using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Win32;

namespace ScreenDraw;

sealed class Config
{
    public string SaveDir { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
    public string Hotkey { get; set; } = "Ctrl+Shift+`";
    public string Color { get; set; } = "#E5483B";
    public double Width { get; set; } = 4;
    public bool CopyOnSave { get; set; } = true;
    public bool CloseAfter { get; set; } = false;
    public bool Autostart { get; set; } = false;

    static readonly string PathFile = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenDraw", "config.json");

    public static Config Load()
    {
        try
        {
            var cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(PathFile)) ?? new Config();
            if (cfg.Hotkey == "Ctrl+Shift+A") cfg.Hotkey = "Ctrl+Shift+`";   // old default
            return cfg;
        }
        catch { return new Config(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathFile)!);
            File.WriteAllText(PathFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-fatal */ }
    }

    public void ApplyAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (Autostart) key.SetValue("ScreenDraw", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("ScreenDraw", false);
        }
        catch { }
    }
}

static class HotkeyText
{
    public const uint Alt = 1, Ctrl = 2, Shift = 4, Win = 8;

    public static bool TryParse(string s, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        foreach (var part in s.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": mods |= Ctrl; break;
                case "alt": mods |= Alt; break;
                case "shift": mods |= Shift; break;
                case "win": mods |= Win; break;
                case "printscreen": vk = 0x2C; break;
                case "`": vk = 0xC0; break;
                default:
                    if (!Enum.TryParse<Key>(part, true, out var k)) return false;
                    vk = (uint)KeyInterop.VirtualKeyFromKey(k);
                    break;
            }
        }
        return vk != 0;
    }

    public static string Format(ModifierKeys m, Key key)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (m.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key == Key.Snapshot ? "PrintScreen" : key == Key.Oem3 ? "`" : key.ToString());
        return string.Join("+", parts);
    }

    public static bool IsModifier(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None;
}
