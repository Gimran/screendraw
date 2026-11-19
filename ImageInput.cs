using System;
using System.IO;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace ScreenDraw;

static class ImageInput
{
    public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico" };

    public static string[] Paths(string[] args)
    {
        var files = new List<string>();
        bool literal = false;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (!literal && arg == "--") { literal = true; continue; }
            if (!literal && (arg == "--test-image" || arg == "--test-rect" || arg == "--quit-after")) { i++; continue; }
            if (!literal && arg.StartsWith("--", StringComparison.Ordinal)) continue;
            files.Add(Path.GetFullPath(arg));
        }
        return files.ToArray();
    }

    public static BitmapSource Load(string path)
    {
        // Decode fully before closing the stream: the original can be moved/deleted afterwards.
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var image = decoder.Frames[0];
        image.Freeze();
        return image;
    }
}
