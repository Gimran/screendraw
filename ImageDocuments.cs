using System;
using System.IO;
using System.Windows;

namespace ScreenDraw;

static class ImageDocuments
{
    public static void Choose(Config cfg, Window? owner = null)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("Открыть файл…"), Multiselect = true, CheckFileExists = true,
            Filter = Loc.T("Изображения") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ico|" + Loc.T("Все файлы") + "|*.*"
        };
        bool? accepted = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        if (accepted == true) Open(dialog.FileNames, cfg, owner);
    }

    public static void Open(string[] paths, Config cfg, Window? owner = null)
    {
        foreach (string path in paths)
        {
            try
            {
                var image = ImageInput.Load(path);
                var editor = new EditorWindow(image, 1, cfg, openedFile: true) { Title = "ScreenDraw — " + Path.GetFileName(path) };
                editor.Show();
                editor.Activate();
            }
            catch (Exception ex)
            {
                string message = Loc.T("Не удалось открыть изображение: ") + path + "\n\n" + ex.Message;
                if (owner == null) MessageBox.Show(message, "ScreenDraw", MessageBoxButton.OK, MessageBoxImage.Warning);
                else MessageBox.Show(owner, message, "ScreenDraw", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
