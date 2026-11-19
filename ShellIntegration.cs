using System;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace ScreenDraw;

static class ShellIntegration
{
    [DllImport("shell32.dll")] static extern void SHChangeNotify(uint id, uint flags, IntPtr item1, IntPtr item2);

    public static void Register()
    {
        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
        Register(classes, Environment.ProcessPath!);
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    // Per-user registration; never change extension defaults or UserChoice.
    public static void Register(RegistryKey classes, string exe)
    {
        string command = "\"" + exe + "\" \"%1\"";
        using var app = classes.CreateSubKey(@"Applications\ScreenDraw.exe");
        app.SetValue("FriendlyAppName", "ScreenDraw");
        using (var icon = app.CreateSubKey("DefaultIcon")) icon.SetValue("", "\"" + exe + "\",0");
        using (var open = app.CreateSubKey(@"shell\open\command")) open.SetValue("", command);
        using (var supported = app.CreateSubKey("SupportedTypes"))
            foreach (string ext in ImageInput.Extensions) supported.SetValue(ext, "");
        using var prog = classes.CreateSubKey("ScreenDraw.Image");
        prog.SetValue("", "ScreenDraw image");
        using (var icon = prog.CreateSubKey("DefaultIcon")) icon.SetValue("", "\"" + exe + "\",0");
        using (var open = prog.CreateSubKey(@"shell\open\command")) open.SetValue("", command);
        foreach (string ext in ImageInput.Extensions)
        {
            using var openWith = classes.CreateSubKey(ext + @"\OpenWithProgids");
            openWith.SetValue("ScreenDraw.Image", "");
        }
    }
}
