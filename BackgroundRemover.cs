using System;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenDraw;

/// <summary>
/// "Colour to alpha": makes the key colour (and everything within the tolerance of it) transparent and
/// un-blends anti-aliased edge pixels so no halo of the old background is left around text and lines.
/// </summary>
static class BackgroundRemover
{
    /// <param name="tol">0..1 — share of "difference from the key colour" that is still treated as background.</param>
    public static BitmapSource Apply(BitmapSource src, Color key, double tol)
    {
        var fc = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);   // straight (non-premultiplied) alpha
        int w = fc.PixelWidth, h = fc.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        fc.CopyPixels(px, stride, 0);
        double kr = key.R, kg = key.G, kb = key.B;
        tol = Math.Clamp(tol, 0, 0.95);

        Parallel.For(0, h, y =>
        {
            int o = y * stride;
            for (int x = 0; x < w; x++, o += 4)
            {
                double b = px[o], g = px[o + 1], r = px[o + 2], srcA = px[o + 3] / 255.0;
                double a = Math.Max(Chan(r, kr), Math.Max(Chan(g, kg), Chan(b, kb)));
                if (a <= tol)
                {
                    px[o] = px[o + 1] = px[o + 2] = px[o + 3] = 0;
                    continue;
                }
                double na = (a - tol) / (1 - tol);
                if (a < 1)   // un-blend: pixel = key + a * (fg - key)
                {
                    r = kr + (r - kr) / a; g = kg + (g - kg) / a; b = kb + (b - kb) / a;
                }
                px[o] = Clamp(b); px[o + 1] = Clamp(g); px[o + 2] = Clamp(r);
                px[o + 3] = (byte)Math.Clamp(na * srcA * 255 + 0.5, 0, 255);
            }
        });

        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, stride);
        result.Freeze();
        return result;
    }

    static byte Clamp(double v) => (byte)Math.Clamp(v + 0.5, 0, 255);

    /// <summary>How far channel value p is from key value k, as a share of the room available in that direction.</summary>
    static double Chan(double p, double k)
    {
        if (p > k) return k >= 255 ? 0 : (p - k) / (255 - k);
        if (p < k) return k <= 0 ? 0 : (k - p) / k;
        return 0;
    }
}
