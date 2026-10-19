using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

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
    public virtual bool HasEndpoints => false;

    public virtual void Assign(Ann o) { Color = o.Color; W = o.W; A = o.A; B = o.B; Rot = o.Rot; }

    public Point Center { get { var r = GeomBox(); return new Point(r.X + r.Width / 2, r.Y + r.Height / 2); } }

    public void Draw(DrawingContext dc)
    {
        if (Math.Abs(Rot) < 1e-9) { DrawLocal(dc); return; }
        var c = Center;
        dc.PushTransform(new RotateTransform(Rot, c.X, c.Y));
        DrawLocal(dc);
        dc.Pop();
    }

    public Point ToWorld(Point local) => RotatePoint(local, Center, Rot);
    public Point ToLocal(Point world) => RotatePoint(world, Center, -Rot);
    public bool Hit(Point world, double tol) => HitLocal(ToLocal(world), tol);
    public void Move(Vector v) { A += v; B += v; }

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
    public override bool HasEndpoints => true;
}

sealed class ArrowAnn : Ann
{
    protected override void DrawLocal(DrawingContext dc)
    {
        var dir = B - A;
        if (dir.Length < 1) return;
        dir.Normalize();
        double head = W * 3.5 + 8;
        dc.DrawLine(MakePen(), A, B - dir * head * 0.8);
        var n = new Vector(-dir.Y, dir.X) * head * 0.42;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(B, true, true);
            c.LineTo(B - dir * head + n, true, false);
            c.LineTo(B - dir * head - n, true, false);
        }
        g.Freeze();
        var brush = new SolidColorBrush(Color);
        dc.DrawGeometry(brush, new Pen(brush, 1) { LineJoin = PenLineJoin.Round }, g);
    }
    public override bool HitLocal(Point p, double tol) => DistToSegment(p, A, B) <= W / 2 + tol;
    public override Rect GeomBox() => new(A, B);
    public override Ann Clone() => CopyTo(new ArrowAnn());
    public override void ScaleAbout(Point anchor, double sx, double sy) => ScaleAB(anchor, sx, sy);
    public override bool HasEndpoints => true;
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
    public static double FontPx(double w) => w * 3 + 12;
    public double Px => Math.Max(FontPx(W) * Scale, 4);

    FormattedText Format() => new(Text.Length == 0 ? " " : Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface("Segoe UI"), Px, new SolidColorBrush(Color), 1.0);

    protected override void DrawLocal(DrawingContext dc) { if (Text.Length > 0) dc.DrawText(Format(), A); }
    public override Rect GeomBox() { var f = Format(); return new Rect(A, new Size(Math.Max(f.Width, 8), Math.Max(f.Height, 8))); }
    public override bool HitLocal(Point p, double tol) { var r = GeomBox(); r.Inflate(tol, tol); return r.Contains(p); }
    public override Ann Clone() => CopyTo(new TextAnn());
    public override void Assign(Ann o)
    {
        base.Assign(o);
        if (o is TextAnn t) { Text = t.Text; Scale = t.Scale; }
    }
    public override void ScaleAbout(Point anchor, double sx, double sy)
    {
        double s = (Math.Abs(sx - 1) > 1e-9 && Math.Abs(sy - 1) > 1e-9) ? (sx + sy) / 2 : (Math.Abs(sx - 1) > 1e-9 ? sx : sy);
        A = ScalePt(A, anchor, s, s);
        Scale *= s;
        B = A;
    }
}
