using System.Collections.Generic;
using System.Globalization;

namespace ScreenDraw;

/// <summary>
/// UI localisation. Russian is the source language: Loc.T("русский текст") returns the English text
/// when English is active and the string itself otherwise (so a missing translation just stays Russian).
/// </summary>
static class Loc
{
    public static bool English { get; private set; }

    /// <param name="setting">"auto" (follow Windows UI language), "ru" or "en".</param>
    public static void Init(string setting)
    {
        English = setting switch
        {
            "en" => true,
            "ru" => false,
            _ => !CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", System.StringComparison.OrdinalIgnoreCase),
        };
    }

    public static string T(string ru) => English && En.TryGetValue(ru, out var en) ? en : ru;

    public static string F(string ru, params object[] args) => string.Format(T(ru), args);

    static readonly Dictionary<string, string> En = new()
    {
        // settings window
        ["Копировать в буфер при сохранении"] = "Copy to clipboard when saving",
        ["Закрывать редактор после сохранения/копирования"] = "Close the editor after saving/copying",
        ["Запускать вместе с Windows"] = "Start with Windows",
        ["ScreenDraw — настройки"] = "ScreenDraw — Settings",
        ["Нажмите нужное сочетание клавиш"] = "Press the key combination",
        ["Обзор…"] = "Browse…",
        ["Папка сохранения"] = "Save folder",
        ["Горячая клавиша"] = "Hotkey",
        ["Язык интерфейса"] = "Interface language",
        ["Автоматически"] = "Automatic",
        ["Язык изменится после перезапуска приложения (он произойдёт автоматически)"] = "The language changes after the app restarts (it restarts automatically)",
        // tray / balloons
        ["Сделать скриншот"] = "Take screenshot",
        ["Настройки…"] = "Settings…",
        ["Выход"] = "Exit",
        ["Не удалось занять {0} — сочетание занято другой программой (код {1}). Выберите другое в настройках."] =
            "Could not register {0} — it is used by another program (code {1}). Choose another one in Settings.",
        ["Запущено. Скриншот: {0}"] = "Running. Screenshot: {0}",
        ["Ошибка: "] = "Error: ",
        ["Буфер обмена занят другой программой"] = "The clipboard is in use by another program",
        // tools
        ["Выбор"] = "Select",
        ["Выбрать и преобразовать (V). Клик по выбранному — режим поворота. Ctrl — временно"] =
            "Select and transform (V). Click a selected object again to rotate. Hold Ctrl for a temporary select",
        ["Стрелка"] = "Arrow",
        ["Линия"] = "Line",
        ["Рамка"] = "Frame",
        ["Овал"] = "Oval",
        ["Выноска"] = "Callout",
        ["Выноска с указателем (K). Узел — куда указывает"] = "Callout with a pointer (K). The node sets where it points",
        ["Текст"] = "Text",
        ["Маркер"] = "Marker",
        ["Выделитель (M)"] = "Highlighter (M)",
        ["Размыть"] = "Blur",
        ["Пикселизация области (B)"] = "Pixelate an area (B)",
        ["Обрезать"] = "Crop",
        ["Обрезка (C). Enter — применить, Esc — отмена"] = "Crop (C). Enter applies, Esc cancels",
        ["Прозрачный фон: выбранный цвет станет прозрачным при сохранении в PNG и копировании"] =
            "Transparent background: the chosen colour becomes transparent when saving to PNG and copying",
        // options
        ["Обычная стрелка"] = "Plain arrow",
        ["Сужающаяся стрелка"] = "Tapered arrow",
        ["Пунктирная изогнутая стрелка (средний узел — изгиб)"] = "Dotted curved arrow (the middle node bends it)",
        ["Фон текста"] = "Text background",
        ["Светлый фон"] = "Light background",
        ["Тёмный фон"] = "Dark background",
        ["Без фона"] = "No background",
        ["Другой цвет"] = "More colours",
        ["Тонкая"] = "Thin",
        ["Средняя"] = "Medium",
        ["Толстая"] = "Thick",
        ["Отменить (Ctrl+Z)"] = "Undo (Ctrl+Z)",
        ["Повторить (Ctrl+Y)"] = "Redo (Ctrl+Y)",
        // background removal row
        ["Удалить цвет:"] = "Remove colour:",
        ["Цвет фона (клик — выбрать из палитры)"] = "Background colour (click to pick from the palette)",
        ["Пипетка: кликните по цвету фона на картинке"] = "Eyedropper: click the background colour in the image",
        ["Белый"] = "White",
        ["Вернуть белый цвет"] = "Reset to white",
        ["Допуск"] = "Tolerance",
        // crop bar, bottom bar, zoom
        ["Применить"] = "Apply",
        ["Отмена"] = "Cancel",
        ["Копировать"] = "Copy",
        ["Копировать путь"] = "Copy path",
        ["Ctrl+Shift+C — путь к файлу в папке сохранения (файл создаётся, если ещё не сохранён)"] =
            "Ctrl+Shift+C — copy the file path in the save folder (the file is created if it is not saved yet)",
        ["Сохранить"] = "Save",
        ["Сохранить как…"] = "Save as…",
        ["Сбросить масштаб (Ctrl+0)"] = "Reset zoom (Ctrl+0)",
        ["Уменьшить (Ctrl+колесо)"] = "Zoom out (Ctrl+wheel)",
        ["Увеличить (Ctrl+колесо)"] = "Zoom in (Ctrl+wheel)",
        // status messages
        ["В буфере нет изображения"] = "No image on the clipboard",
        ["Не удалось вставить: "] = "Could not paste: ",
        ["Это не изображение"] = "Not an image file",
        ["Изображение вставлено — тяните за рамку, Delete — удалить"] = "Image pasted — drag it by its frame, Delete removes it",
        ["Не удалось сохранить: "] = "Could not save: ",
        ["Буфер обмена: "] = "Clipboard: ",
        ["Скопировано и сохранено: "] = "Copied and saved: ",
        ["Сохранено: "] = "Saved: ",
        ["Путь скопирован: "] = "Path copied: ",
        ["Не удалось скопировать путь: "] = "Could not copy the path: ",
    };
}
