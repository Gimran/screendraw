using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenDraw;

/// <summary>
/// Annotation. Geometry (A, B, text origin) lives in "local" image-pixel coordinates;
/// Rot (degrees) rotates the drawn result about the centre of the local geometry box.
/// </summary>
abstract class Ann
{
    public Color Color = Colors.Red;
    public double W = 4;
    public Point A, B;
    public double Rot;

    protected abstract void DrawLocal(DrawingContext dc);
    public abstract bool HitLocal(Point p, double tol);
    public abstract Rect GeomBox();
    public abstract Ann Clone();
    public abstract void ScaleAbout(Point anchor, double sx, double sy);
    /// <summary>Draggable nodes in local coordinates (line/arrow ends, curve mid point).</summary>
    public virtual Point[] Nodes() => Array.Empty<Point>();
    public virtual void SetNode(int i, Point local) { }

    public virtual void Assign(Ann o) { Color = o.Color; W = o.W; A = o.A; B = o.B; Rot = o.Rot; }

    public Point Center { get { var r = GeomBox(); return new Point(r.X + r.Width / 2, r.Y + r.Height / 2); } }

    public virtual double StrokeHalf => W / 2;
    public virtual bool CanRotate => true;
    /// <summary>Show the 8-handle bounding box when selected (arrows only use their end nodes).</summary>
    public virtual bool ShowBox => true;
    /// <summary>Nodes with index below this are line-like end points (the opposite end stays pinned while dragging).</summary>
    public virtual int EndpointCount => 2;
    /// <summary>Box used to decide whether the canvas must grow (may be larger than the geometry box).</summary>
    protected virtual Rect ReachBox() => GeomBox();
    /// <summary>Whether this object may grow the canvas when it sticks out of the image.</summary>
    public virtual bool ExtendsCanvas => true;
    protected virtual double ExtraReach => 2;

    /// <summary>Bounds of the drawn object in image coordinates (after rotation, including stroke).</summary>
    public Rect WorldBounds()
    {
        var r = ReachBox();
        var pts = new[] { r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft };
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in pts)
        {
            var w = ToWorld(p);
            minX = Math.Min(minX, w.X); minY = Math.Min(minY, w.Y); maxX = Math.Max(maxX, w.X); maxY = Math.Max(maxY, w.Y);
        }
        double m = StrokeHalf + ExtraReach;
        return new Rect(minX - m, minY - m, maxX - minX + 2 * m, maxY - minY + 2 * m);
    }
    protected virtual void DrawLocal(DrawingContext dc, BitmapSource src) => DrawLocal(dc);

    public void Draw(DrawingContext dc, BitmapSource src)
    {
        if (Math.Abs(Rot) < 1e-9) { DrawLocal(dc, src); return; }
        var c = Center;
        dc.PushTransform(new RotateTransform(Rot, c.X, c.Y));
        DrawLocal(dc, src);
        dc.Pop();
    }

    public Point ToWorld(Point local) => RotatePoint(local, Center, Rot);
    public Point ToLocal(Point world) => RotatePoint(world, Center, -Rot);
    public bool Hit(Point world, double tol) => HitLocal(ToLocal(world), tol);
    public virtual void Move(Vector v) { A += v; B += v; }

    public static Point RotatePoint(Point p, Point c, double deg)
    {
        double r = deg * Math.PI / 180, cs = Math.Cos(r), sn = Math.Sin(r);
        var d = p - c;
        return new Point(c.X + d.X * cs - d.Y * sn, c.Y + d.X * sn + d.Y * cs);
    }

    protected T CopyTo<T>(T o) where T : Ann { o.Assign(this); return o; }
    protected Pen MakePen() => new(new SolidColorBrush(Color), W)
    { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

    protected static Point ScalePt(Point p, Point anchor, double sx, double sy) =>
        new(anchor.X + (p.X - anchor.X) * sx, anchor.Y + (p.Y - anchor.Y) * sy);

    protected static double DistToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        double len2 = ab.LengthSquared;
        double t = len2 == 0 ? 0 : Math.Clamp(((p - a) * ab) / len2, 0, 1);
        return (p - (a + ab * t)).Length;
    }

    protected void ScaleAB(Point anchor, double sx, double sy) { A = ScalePt(A, anchor, sx, sy); B = ScalePt(B, anchor, sx, sy); }
}

sealed class LineAnn : Ann
{
    protected override void DrawLocal(DrawingContext dc) => dc.DrawLine(MakePen(), A, B);
    public override bool HitLocal(Point p, double tol) => DistToSegment(p, A, B) <= W / 2 + tol;
    public override Rect GeomBox() => new(A, B);
    public override Ann Clone() => CopyTo(new LineAnn());
    public override void ScaleAbout(Point anchor, double sx, double sy) => ScaleAB(anchor, sx, sy);
    public override Point[] Nodes() => new[] { A, B };
    public override void SetNode(int i, Point p) { if (i == 0) A = p; else B = p; }
}

sealed class ArrowAnn : Ann
{
    public int Style;                 // 0 plain, 1 tapered, 2 dotted (curved)
    public override bool ShowBox => false;
    public override bool CanRotate => false;
    protected override double ExtraReach => W * 2 + 6;
    public double MidU = 0.5, MidV;   // curve mid point in chord-relative coordinates

    Point Mid()
    {
        var ab = B - A; double len = ab.Length;
        if (len < 1e-6) return A;
        var n = new Vector(-ab.Y, ab.X) / len;
        return A + ab * MidU + n * (MidV * len);
    }

    void SetMid(Point m)
    {
        var ab = B - A; double len2 = ab.LengthSquared;
        if (len2 < 1e-6) return;
        double len = Math.Sqrt(len2);
        var n = new Vector(-ab.Y, ab.X) / len;
        var d = m - A;
        MidU = (d * ab) / len2;
        MidV = (d * n) / len;
    }

    Point Ctrl() { var m = Mid(); return new Point(2 * m.X - (A.X + B.X) / 2, 2 * m.Y - (A.Y + B.Y) / 2); }

    Point[] Curve(int n)
    {
        var c = Style == 2 ? Ctrl() : new Point((A.X + B.X) / 2, (A.Y + B.Y) / 2);
        var pts = new Point[n + 1];
        for (int i = 0; i <= n; i++)
        {
            double t = (double)i / n, u = 1 - t;
            pts[i] = new Point(u * u * A.X + 2 * u * t * c.X + t * t * B.X, u * u * A.Y + 2 * u * t * c.Y + t * t * B.Y);
        }
        return pts;
    }

    static void Head(DrawingContext dc, Color color, Point tip, Vector dir, double len, double halfW)
    {
        var n = new Vector(-dir.Y, dir.X) * halfW;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(tip, true, true);
            c.LineTo(tip - dir * len + n, true, false);
            c.LineTo(tip - dir * len - n, true, false);
        }
        g.Freeze();
        var brush = new SolidColorBrush(color);
        dc.DrawGeometry(brush, new Pen(brush, 1) { LineJoin = PenLineJoin.Round }, g);
    }

    protected override void DrawLocal(DrawingContext dc)
    {
        var ab = B - A;
        if (ab.Length < 1) return;
        var brush = new SolidColorBrush(Color);
        if (Style == 1)          // tapered: thin tail growing into a big head
        {
            var dir = ab; dir.Normalize();
            var n = new Vector(-dir.Y, dir.X);
            double head = W * 4 + 10, neck = W * 0.9 + 1, tail = Math.Max(W * 0.12, 0.5);
            double hl = Math.Min(head, ab.Length * 0.8);
            var baseP = B - dir * hl * 0.85;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(A + n * tail, true, true);
                c.LineTo(baseP + n * neck, true, false);
                c.LineTo(baseP - n * neck, true, false);
                c.LineTo(A - n * tail, true, false);
            }
            g.Freeze();
            dc.DrawGeometry(brush, new Pen(brush, 1) { LineJoin = PenLineJoin.Round }, g);
            Head(dc, Color, B, dir, hl, hl * 0.5);
        }
        else if (Style == 2)     // dotted curve
        {
            var pts = Curve(80);
            double total = 0; var cum = new double[pts.Length];
            for (int i = 1; i < pts.Length; i++) { total += (pts[i] - pts[i - 1]).Length; cum[i] = total; }
            double head = W * 3.5 + 8, r = Math.Max(W * 0.85, 1.5), step = r * 3.4;
            var tip = B - Ctrl(); if (tip.Length < 1e-6) tip = ab; tip.Normalize();
            int k = 1;
            for (double s = 0; s <= total - head * 0.9; s += step)
            {
                while (k < pts.Length - 1 && cum[k] < s) k++;
                double seg = cum[k] - cum[k - 1], f = seg < 1e-9 ? 0 : (s - cum[k - 1]) / seg;
                dc.DrawEllipse(brush, null, pts[k - 1] + (pts[k] - pts[k - 1]) * f, r, r);
            }
            Head(dc, Color, B, tip, head, head * 0.42);
        }
        else                     // plain
        {
            var dir = ab; dir.Normalize();
            double head = W * 3.5 + 8;
            dc.DrawLine(MakePen(), A, B - dir * head * 0.8);
            Head(dc, Color, B, dir, head, head * 0.42);
        }
    }

    public override bool HitLocal(Point p, double tol)
    {
        var pts = Curve(Style == 2 ? 40 : 1);
        double w = Style == 0 ? W / 2 : Math.Max(W, 6);
        for (int i = 1; i < pts.Length; i++)
            if (DistToSegment(p, pts[i - 1], pts[i]) <= w + tol) return true;
        return false;
    }

    public override Rect GeomBox() { var r = new Rect(A, B); if (Style == 2) r.Union(Mid()); return r; }
    public override Ann Clone() => CopyTo(new ArrowAnn());
    public override void Assign(Ann o)
    {
        base.Assign(o);
        if (o is ArrowAnn t) { Style = t.Style; MidU = t.MidU; MidV = t.MidV; }
    }
    public override void ScaleAbout(Point anchor, double sx, double sy)
    {
        var m = ScalePt(Mid(), anchor, sx, sy);
        ScaleAB(anchor, sx, sy);
        if (Style == 2) SetMid(m);
    }
    public override Point[] Nodes() => Style == 2 ? new[] { A, B, Mid() } : new[] { A, B };
    public override void SetNode(int i, Point p) { if (i == 0) A = p; else if (i == 1) B = p; else SetMid(p); }
}

sealed class RectAnn : Ann
{
    protected override void DrawLocal(DrawingContext dc) => dc.DrawRectangle(null, MakePen(), new Rect(A, B));
    public override bool HitLocal(Point p, double tol)
    {
        var r = new Rect(A, B);
        var outer = r; outer.Inflate(W / 2 + tol, W / 2 + tol);
        if (!outer.Contains(p)) return false;
        double m = W / 2 + tol;
        if (r.Width <= 2 * m || r.Height <= 2 * m) return true;
        var inner = r; inner.Inflate(-m, -m);
        return !inner.Contains(p);
    }
    public override Rect GeomBox() => new(A, B);
    public override Ann Clone() => CopyTo(new RectAnn());
    public override void ScaleAbout(Point anchor, double sx, double sy) => ScaleAB(anchor, sx, sy);
}

sealed class EllipseAnn : Ann
{
    protected override void DrawLocal(DrawingContext dc)
    {
        var r = new Rect(A, B);
        dc.DrawEllipse(null, MakePen(), new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
    }
    public override bool HitLocal(Point p, double tol)
    {
        var r = new Rect(A, B);
        double rx = Math.Max(r.Width / 2, 1), ry = Math.Max(r.Height / 2, 1);
        double nx = (p.X - (r.X + r.Width / 2)) / rx, ny = (p.Y - (r.Y + r.Height / 2)) / ry;
        return Math.Abs(Math.Sqrt(nx * nx + ny * ny) - 1) * Math.Min(rx, ry) <= W / 2 + tol;
    }
    public override Rect GeomBox() => new(A, B);
    public override Ann Clone() => CopyTo(new EllipseAnn());
    public override void ScaleAbout(Point anchor, double sx, double sy) => ScaleAB(anchor, sx, sy);
}

sealed class TextAnn : Ann
{
    public string Text = "";
    public double Scale = 1;
    public int Bg;                      // 0 none, 1 light, 2 dark
    double Pad => Bg == 0 ? 0 : Px * 0.2;
    public override double StrokeHalf => 0;
    public static double FontPx(double w) => w * 3 + 12;
    public double Px => Math.Max(FontPx(W) * Scale, 4);

    FormattedText Format() => new(Text.Length == 0 ? " " : Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface("Segoe UI"), Px, new SolidColorBrush(Color), 1.0);

    protected override void DrawLocal(DrawingContext dc)
    {
        if (Text.Length == 0) return;
        if (Bg != 0)
        {
            var fill = Bg == 1 ? Color.FromArgb(235, 250, 250, 250) : Color.FromArgb(215, 24, 24, 24);
            dc.DrawRoundedRectangle(new SolidColorBrush(fill), null, GeomBox(), Pad * 0.6, Pad * 0.6);
        }
        dc.DrawText(Format(), A);
    }
    public override Rect GeomBox()
    {
        var f = Format();
        double p = Pad;
        return new Rect(A.X - p, A.Y - p, Math.Max(f.Width, 8) + 2 * p, Math.Max(f.Height, 8) + 2 * p);
    }
    public override bool HitLocal(Point p, double tol) { var r = GeomBox(); r.Inflate(tol, tol); return r.Contains(p); }
    public override Ann Clone() => CopyTo(new TextAnn());
    public override void Assign(Ann o)
    {
        base.Assign(o);
        if (o is TextAnn t) { Text = t.Text; Scale = t.Scale; Bg = t.Bg; }
    }
    public override void ScaleAbout(Point anchor, double sx, double sy)
    {
        double s = (Math.Abs(sx - 1) > 1e-9 && Math.Abs(sy - 1) > 1e-9) ? (sx + sy) / 2 : (Math.Abs(sx - 1) > 1e-9 ? sx : sy);
        A = ScalePt(A, anchor, s, s);
        Scale *= s;
        B = A;
    }
}

/// <summary>Semi-transparent highlighter stroke.</summary>
sealed class MarkerAnn : Ann
{
    double Thick => W * 3 + 10;
    public override double StrokeHalf => Thick / 2;
    protected override void DrawLocal(DrawingContext dc)
    {
        var c = Color;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(120, c.R, c.G, c.B)), Thick) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
        dc.DrawLine(pen, A, B);
    }
    public override bool HitLocal(Point p, double tol) => DistToSegment(p, A, B) <= Thick / 2 + tol;
    public override Rect GeomBox() => new(A, B);
    public override Ann Clone() => CopyTo(new MarkerAnn());
    public override void ScaleAbout(Point anchor, double sx, double sy) => ScaleAB(anchor, sx, sy);
    public override Point[] Nodes() => new[] { A, B };
    public override void SetNode(int i, Point p) { if (i == 0) A = p; else B = p; }
}

/// <summary>Pixelates the underlying image region (W controls block size).</summary>
sealed class BlurAnn : Ann
{
    public override double StrokeHalf => 0;
    public override bool CanRotate => false;
    public override bool ExtendsCanvas => false;
    protected override void DrawLocal(DrawingContext dc) { }
    protected override void DrawLocal(DrawingContext dc, BitmapSource src)
    {
        var r = new Rect(A, B);
        int x = (int)Math.Max(0, Math.Floor(r.X)), y = (int)Math.Max(0, Math.Floor(r.Y));
        int w = (int)Math.Min(src.PixelWidth, Math.Ceiling(r.Right)) - x, h = (int)Math.Min(src.PixelHeight, Math.Ceiling(r.Bottom)) - y;
        if (w < 2 || h < 2) return;
        double k = Math.Max(3, 4 + W * 2), f = 1.0 / k;
        var crop = new CroppedBitmap(src, new Int32Rect(x, y, w, h));
        var small = new TransformedBitmap(crop, new ScaleTransform(f, f));
        var g = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(g, BitmapScalingMode.NearestNeighbor);
        using (var gc = g.Open()) gc.DrawImage(small, new Rect(x, y, w, h));
        dc.DrawDrawing(g);
    }
    public override bool HitLocal(Point p, double tol) { var r = new Rect(A, B); r.Inflate(tol, tol); return r.Contains(p); }
    public override Rect GeomBox() => new(A, B);
    public override Ann Clone() => CopyTo(new BlurAnn());
    public override void ScaleAbout(Point anchor, double sx, double sy) => ScaleAB(anchor, sx, sy);
}

/// <summary>Speech balloon: rounded frame (A..B) with a tail pointing at a draggable tip node.</summary>
sealed class BalloonAnn : Ann
{
    public Point Tip;
    public bool AutoTip = true;
    public override int EndpointCount => 0;

    Rect Body() => new(A, B);

    Point TipPt()
    {
        if (!AutoTip) return Tip;
        var r = Body();
        return new Point(r.X + r.Width * 0.3, r.Bottom + Math.Max(18, r.Height * 0.35));
    }

    /// <summary>Which side the tail leaves from (0 top, 1 right, 2 bottom, 3 left) and the base span; false if no tail.</summary>
    bool Tail(out int side, out double c, out double half, out Point tip, out double r)
    {
        var b = Body(); tip = TipPt();
        side = 0; c = 0; half = 0; r = Math.Min(Math.Min(b.Width, b.Height) * 0.22, 22);
        if (b.Width < 4 || b.Height < 4 || b.Contains(tip)) return false;
        double dx = (tip.X - (b.X + b.Width / 2)) / (b.Width / 2), dy = (tip.Y - (b.Y + b.Height / 2)) / (b.Height / 2);
        bool horiz = Math.Abs(dx) > Math.Abs(dy);
        side = horiz ? (dx > 0 ? 1 : 3) : (dy > 0 ? 2 : 0);
        double len = horiz ? b.Height : b.Width;
        half = Math.Max(Math.Min(len * 0.17, 18), 3);
        double lo = (horiz ? b.Y : b.X) + r + half, hi = (horiz ? b.Bottom : b.Right) - r - half;
        double want = horiz ? tip.Y : tip.X;
        c = hi < lo ? (lo + hi) / 2 : Math.Clamp(want, lo, hi);
        return true;
    }

    Geometry Build()
    {
        var b = Body();
        bool hasTail = Tail(out int side, out double c, out double hw, out var tip, out double r);
        double L = b.Left, T = b.Top, R = b.Right, B = b.Bottom;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            void Arc(Point p) => ctx.ArcTo(p, new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            ctx.BeginFigure(new Point(L + r, T), false, true);
            if (hasTail && side == 0) { ctx.LineTo(new Point(c - hw, T), true, true); ctx.LineTo(tip, true, true); ctx.LineTo(new Point(c + hw, T), true, true); }
            ctx.LineTo(new Point(R - r, T), true, true); Arc(new Point(R, T + r));
            if (hasTail && side == 1) { ctx.LineTo(new Point(R, c - hw), true, true); ctx.LineTo(tip, true, true); ctx.LineTo(new Point(R, c + hw), true, true); }
            ctx.LineTo(new Point(R, B - r), true, true); Arc(new Point(R - r, B));
            if (hasTail && side == 2) { ctx.LineTo(new Point(c + hw, B), true, true); ctx.LineTo(tip, true, true); ctx.LineTo(new Point(c - hw, B), true, true); }
            ctx.LineTo(new Point(L + r, B), true, true); Arc(new Point(L, B - r));
            if (hasTail && side == 3) { ctx.LineTo(new Point(L, c + hw), true, true); ctx.LineTo(tip, true, true); ctx.LineTo(new Point(L, c - hw), true, true); }
            ctx.LineTo(new Point(L, T + r), true, true); Arc(new Point(L + r, T));
        }
        g.Freeze();
        return g;
    }

    protected override void DrawLocal(DrawingContext dc)
    {
        if (Body().Width < 2 || Body().Height < 2) return;
        dc.DrawGeometry(null, MakePen(), Build());
    }

    public override bool HitLocal(Point p, double tol)
    {
        var rect = Body();
        var outer = rect; outer.Inflate(W / 2 + tol, W / 2 + tol);
        double m = W / 2 + tol;
        if (outer.Contains(p))
        {
            if (rect.Width <= 2 * m || rect.Height <= 2 * m) return true;
            var inner = rect; inner.Inflate(-m, -m);
            if (!inner.Contains(p)) return true;
        }
        if (Tail(out int side, out double c, out double hw, out var tip, out _))
        {
            Point b1, b2;
            switch (side)
            {
                case 0: b1 = new Point(c - hw, rect.Top); b2 = new Point(c + hw, rect.Top); break;
                case 1: b1 = new Point(rect.Right, c - hw); b2 = new Point(rect.Right, c + hw); break;
                case 2: b1 = new Point(c - hw, rect.Bottom); b2 = new Point(c + hw, rect.Bottom); break;
                default: b1 = new Point(rect.Left, c - hw); b2 = new Point(rect.Left, c + hw); break;
            }
            if (DistToSegment(p, tip, b1) <= W / 2 + tol || DistToSegment(p, tip, b2) <= W / 2 + tol) return true;
        }
        return false;
    }

    public override Rect GeomBox() => Body();
    protected override Rect ReachBox() { var r = Body(); r.Union(TipPt()); return r; }
    public override Ann Clone() => CopyTo(new BalloonAnn());
    public override void Assign(Ann o)
    {
        base.Assign(o);
        if (o is BalloonAnn t) { Tip = t.Tip; AutoTip = t.AutoTip; }
    }
    public override void ScaleAbout(Point anchor, double sx, double sy)
    {
        var tip = TipPt();
        ScaleAB(anchor, sx, sy);
        if (!AutoTip) Tip = ScalePt(tip, anchor, sx, sy);
    }
    public override Point[] Nodes() => new[] { TipPt() };
    public override void SetNode(int i, Point p) { Tip = p; AutoTip = false; }
    public override void Move(Vector v) { A += v; B += v; Tip += v; }
}
