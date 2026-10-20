using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScreenDraw;

sealed class SettingsWindow : Window
{
    readonly TextBox dir = new() { MinWidth = 280 };
    readonly TextBox hotkey = new() { IsReadOnly = true, IsReadOnlyCaretVisible = false };
    readonly CheckBox copy = new() { Content = "Копировать в буфер при сохранении" };
    readonly CheckBox close = new() { Content = "Закрывать редактор после сохранения/копирования" };
    readonly CheckBox auto = new() { Content = "Запускать вместе с Windows" };

    public SettingsWindow(Config cfg)
    {
        Title = "ScreenDraw — настройки";
        Icon = Theme.AppIcon;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        dir.Text = cfg.SaveDir;
        hotkey.Text = cfg.Hotkey;
        hotkey.ToolTip = "Нажмите нужное сочетание клавиш";
        hotkey.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (HotkeyText.IsModifier(key)) return;
            hotkey.Text = HotkeyText.Format(Keyboard.Modifiers, key);
        };
        copy.IsChecked = cfg.CopyOnSave;
        close.IsChecked = cfg.CloseAfter;
        auto.IsChecked = cfg.Autostart;

        var browse = new Button { Content = "Обзор…", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            var d = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = dir.Text };
            if (d.ShowDialog(this) == true) dir.Text = d.FolderName;
        };
        var ok = new Button { Content = "OK", IsDefault = true, Width = 80, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => { DialogResult = true; };

        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 10) };
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse); row.Children.Add(dir);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = "Папка сохранения" });
        root.Children.Add(row);
        root.Children.Add(new TextBlock { Text = "Горячая клавиша" });
        hotkey.Margin = new Thickness(0, 2, 0, 10);
        root.Children.Add(hotkey);
        foreach (var c in new[] { copy, close, auto }) { c.Margin = new Thickness(0, 3, 0, 3); root.Children.Add(c); }
        root.Children.Add(ok);
        Content = root;
    }

    public void ApplyTo(Config cfg)
    {
        if (!string.IsNullOrWhiteSpace(dir.Text)) cfg.SaveDir = dir.Text.Trim();
        cfg.Hotkey = hotkey.Text;
        cfg.CopyOnSave = copy.IsChecked == true;
        cfg.CloseAfter = close.IsChecked == true;
        cfg.Autostart = auto.IsChecked == true;
    }
}
