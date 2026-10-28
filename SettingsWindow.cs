using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScreenDraw;

sealed class SettingsWindow : Window
{
    readonly TextBox dir = new() { MinWidth = 280 };
    readonly TextBox hotkey = new() { IsReadOnly = true, IsReadOnlyCaretVisible = false };
    readonly CheckBox copy = new() { Content = Loc.T("Копировать в буфер при сохранении") };
    readonly CheckBox close = new() { Content = Loc.T("Закрывать редактор после сохранения/копирования") };
    bool accepted;
    readonly ComboBox lang = new() { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
    readonly CheckBox auto = new() { Content = Loc.T("Запускать вместе с Windows") };

    public SettingsWindow(Config cfg)
    {
        Title = Loc.T("ScreenDraw — настройки");
        Icon = Theme.AppIcon;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        dir.Text = cfg.SaveDir;
        hotkey.Text = cfg.Hotkey;
        hotkey.ToolTip = Loc.T("Нажмите нужное сочетание клавиш");
        hotkey.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (HotkeyText.IsModifier(key)) return;
            hotkey.Text = HotkeyText.Format(Keyboard.Modifiers, key);
        };
        lang.Items.Add(new ComboBoxItem { Content = Loc.T("Автоматически"), Tag = "auto" });
        lang.Items.Add(new ComboBoxItem { Content = Loc.T("Русский"), Tag = "ru" });
        lang.Items.Add(new ComboBoxItem { Content = "English", Tag = "en" });
        foreach (ComboBoxItem it in lang.Items) { if ((string)it.Tag == cfg.Language) lang.SelectedItem = it; }
        if (lang.SelectedItem == null) lang.SelectedIndex = 0;
        copy.IsChecked = cfg.CopyOnSave;
        close.IsChecked = cfg.CloseAfter;
        auto.IsChecked = cfg.Autostart;

        var browse = new Button { Content = Loc.T("Обзор…"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            var d = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = dir.Text };
            if (d.ShowDialog(this) == true) dir.Text = d.FolderName;
        };
        var ok = new Button { Content = "OK", IsDefault = true, Width = 80, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => { if (!accepted) { accepted = true; DialogResult = true; } };   // a double click must not set the result twice

        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 10) };
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse); row.Children.Add(dir);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = Loc.T("Папка сохранения") });
        root.Children.Add(row);
        root.Children.Add(new TextBlock { Text = Loc.T("Горячая клавиша") });
        hotkey.Margin = new Thickness(0, 2, 0, 10);
        root.Children.Add(hotkey);
        root.Children.Add(new TextBlock { Text = Loc.T("Язык интерфейса"), Margin = new Thickness(0, 4, 0, 0) });
        lang.Margin = new Thickness(0, 2, 0, 10);
        lang.ToolTip = Loc.T("Язык изменится после перезапуска приложения (он произойдёт автоматически)");
        root.Children.Add(lang);
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
        if (lang.SelectedItem is ComboBoxItem li) cfg.Language = (string)li.Tag;
        cfg.Autostart = auto.IsChecked == true;
    }
}
