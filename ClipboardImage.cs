using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenDraw;

/// <summary>
/// Puts an image with transparency on the clipboard in the formats applications actually read:
///   "PNG"   — real alpha (browsers, Office, Telegram, ...),
///   CF_DIBV5 — 32-bit with alpha; fully transparent pixels carry WHITE rgb, so a reader that ignores alpha sees white, not black,
///   CF_DIB   — 24-bit, composited over white (what older / simple readers get; Windows derives CF_BITMAP from it).
/// WPF's Clipboard.SetImage only writes an alpha-less DIB, which turns transparent pixels black.
/// </summary>
static class ClipboardImage
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr h);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint format, IntPtr mem);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr h);

    const uint CF_DIB = 8, CF_DIBV5 = 17, GMEM_MOVEABLE = 2;

    public static void SetWithAlpha(BitmapSource bmp)
    {
        var straight = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);   // straight (un-premultiplied) alpha
        int w = straight.PixelWidth, h = straight.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        straight.CopyPixels(px, stride, 0);

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        var png = ms.ToArray();

        var v5 = BuildDibV5(px, w, h);
        var dib = BuildDib24(px, w, h);
        uint pngFmt = RegisterClipboardFormat("PNG");

        for (int attempt = 0; ; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero)) break;
            if (attempt >= 20) throw new InvalidOperationException("Буфер обмена занят другой программой");
            Thread.Sleep(30);
        }
        try
        {
            EmptyClipboard();
            Put(pngFmt, png);
            Put(CF_DIBV5, v5);
            Put(CF_DIB, dib);
        }
        finally { CloseClipboard(); }
    }

    static void Put(uint format, byte[] data)
    {
        var mem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)data.Length);
        if (mem == IntPtr.Zero) throw new OutOfMemoryException();
        var p = GlobalLock(mem);
        Marshal.Copy(data, 0, p, data.Length);
        GlobalUnlock(mem);
        if (SetClipboardData(format, mem) == IntPtr.Zero) GlobalFree(mem);   // on success the system owns the memory
    }

    static byte[] BuildDibV5(byte[] px, int w, int h)
    {
        int stride = w * 4;
        var buf = new byte[124 + stride * h];
        using (var bw = new BinaryWriter(new MemoryStream(buf)))
        {
            bw.Write(124);                 // bV5Size
            bw.Write(w); bw.Write(h);      // positive height = bottom-up rows
            bw.Write((short)1); bw.Write((short)32);
            bw.Write(3);                   // BI_BITFIELDS
            bw.Write(stride * h);          // image size
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);   // pels/meter, clrUsed, clrImportant
            bw.Write(0x00FF0000u); bw.Write(0x0000FF00u); bw.Write(0x000000FFu); bw.Write(0xFF000000u);   // R, G, B, A masks
            bw.Write(0x73524742);          // LCS_sRGB
            bw.Write(new byte[36]);        // endpoints
            bw.Write(0); bw.Write(0); bw.Write(0);   // gamma
            bw.Write(4);                   // LCS_GM_IMAGES
            bw.Write(0); bw.Write(0); bw.Write(0);   // profile data / size / reserved
        }
        for (int y = 0; y < h; y++)
        {
            int src = (h - 1 - y) * stride, dst = 124 + y * stride;
            for (int x = 0; x < w; x++, src += 4, dst += 4)
            {
                byte a = px[src + 3];
                if (a == 0) { buf[dst] = buf[dst + 1] = buf[dst + 2] = 255; buf[dst + 3] = 0; }   // invisible, but white for readers that drop alpha
                else { buf[dst] = px[src]; buf[dst + 1] = px[src + 1]; buf[dst + 2] = px[src + 2]; buf[dst + 3] = a; }
            }
        }
        return buf;
    }

    static byte[] BuildDib24(byte[] px, int w, int h)
    {
        int stride = w * 4, row = (w * 3 + 3) & ~3;
        var buf = new byte[40 + row * h];
        using (var bw = new BinaryWriter(new MemoryStream(buf)))
        {
            bw.Write(40); bw.Write(w); bw.Write(h);
            bw.Write((short)1); bw.Write((short)24);
            bw.Write(0); bw.Write(row * h);
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
        }
        for (int y = 0; y < h; y++)
        {
            int src = (h - 1 - y) * stride, dst = 40 + y * row;
            for (int x = 0; x < w; x++, src += 4, dst += 3)
            {
                int a = px[src + 3];
                for (int c = 0; c < 3; c++)   // composite over white
                    buf[dst + c] = (byte)((px[src + c] * a + 255 * (255 - a) + 127) / 255);
            }
        }
        return buf;
    }
}
