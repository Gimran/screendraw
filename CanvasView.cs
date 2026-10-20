using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenDraw;

enum Tool { Select, Arrow, Line, Rect, Ellipse, Text, Marker, Blur, Crop, Balloon }

sealed record Snap(List<Ann> Items, BitmapSource Image);

/// <summary>
/// Shows the screenshot plus annotations; drawing, selecting and CorelDRAW-style transforms:
/// 1st click selects (8 scale handles), clicking the selected object again switches to rotate mode
/// (corner handles rotate around the pivot marker, which can be dragged anywhere).
/// Also hosts the crop mode (crop rectangle with 8 handles, applied from the editor toolbar).
/// </summary>
sealed class CanvasView : FrameworkElement
{
    enum Mode { None, Draw, Move, Scale, Rotate, Pivot, Endpoint, Crop }

    static readonly Color Accent = Color.FromRgb(0x3A, 0x7B, 0xD5);
    // handle order: TL, T, TR, R, BR, B, BL, L
    static readonly Vector[] Dirs = { new(-1, -1), new(0, -1), new(1, -1), new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0) };

    /// <summary>Empty margin (DIP) around the image where drawing can start / shapes can stick out.</summary>
    public const double PadDip = 28;
    double M => PadDip * S;                         // margin in device pixels

    /// <summary>Top-left of the image in view device pixels: centred, never closer than the margin to the edge.</summary>
    Point Origin => new(Math.Floor(Math.Max(M, (ActualWidth * S - Image.PixelWidth * Zoom) / 2)), Math.Floor(Math.Max(M, (ActualHeight * S - Image.PixelHeight * Zoom) / 2)));
    public Vector OriginDip { get { var o = Origin; return new Vector(o.X / S, o.Y / S); } }

    public BitmapSource Image { get; private set; }
    public double S { get; }                       // device pixels per DIP
    public double Zoom { get; private set; } = 1;  // device pixels per image pixel
    /// <summary>Image pixels per DIP at the current zoom: keeps handles / tolerances a constant size on screen.</summary>
    public double U => S / Zoom;
    public event Action? ZoomChanged;

    public void SetZoom(double z)
    {
        z = Math.Clamp(z, 0.1, 8);
        if (Math.Abs(z - Zoom) < 1e-6) return;
        Zoom = z;
        RenderOptions.SetBitmapScalingMode(this, z >= 2 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        InvalidateMeasure(); InvalidateVisual();
        ZoomChanged?.Invoke();
    }

    /// <summary>Image point -> position inside this element (DIP).</summary>
    public Point ImageToView(Point img) { var o = Origin; return new Point((o.X + img.X * Zoom) / S, (o.Y + img.Y * Zoom) / S); }
    /// <summary>Position inside this element (DIP) -> image point.</summary>
    public Point ViewToImage(Point dip) { var o = Origin; return new Point((dip.X * S - o.X) / Zoom, (dip.Y * S - o.Y) / Zoom); }
    public List<Ann> Items = new();
    public Ann? Selected { get; private set; }
    public Ann? Hidden;                            // text being edited in a TextBox
    public bool RotateMode { get; private set; }
    public bool CropMode { get; private set; }
    public Color CurColor = Colors.Red;
    public double CurW = 4;
    public int CurTextBg;
    public int CurArrowStyle;

    public event Action? Changed;
    public event Action? ImageChanged;
    public event Action<Ann?>? SelectionChanged;
    public event Action<TextAnn?, Point>? TextRequested;
    public event Action<Tool>? ToolChanged;

    Tool _tool = Tool.Arrow;
    Mode mode;
    Ann? cur, orig;
    Point last, startWorld;
    Point? pivot;
    double startAngle;
    Vector grab;
    int handle;
    bool wasSelected, moved;
    Snap? pending;
    Rect crop, cropOrig;
    Point cropStart;
    readonly Stack<Snap> undo = new(), redo = new();

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public CanvasView(BitmapSource image, double scale)
    {
        Image = image;
        S = scale;
        Cursor = Cursors.Cross;
        Focusable = true;
        SnapsToDevicePixels = true;
    }

    public Tool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            Cursor = value is Tool.Select or Tool.Crop ? Cursors.Arrow : Cursors.Cross;
            if (value != Tool.Select) Select(null);
        }
    }

    protected override Size MeasureOverride(Size _) => new(Image.PixelWidth * Zoom / S + 2 * PadDip, Image.PixelHeight * Zoom / S + 2 * PadDip);

    // ---------------------------------------------------------------- handle geometry
    double Pad(Ann a) => 6 * U + a.StrokeHalf;

    Rect DispBox(Ann a) { var r = a.GeomBox(); double p = Pad(a); return new Rect(r.X - p, r.Y - p, r.Width + 2 * p, r.Height + 2 * p); }

    static Point BoxPoint(Rect b, int i) => new(b.X + (Dirs[i].X + 1) / 2 * b.Width, b.Y + (Dirs[i].Y + 1) / 2 * b.Height);

    Point[] WorldHandles(Ann a)
    {
        var box = DispBox(a);
        return Enumerable.Range(0, 8).Select(i => a.ToWorld(BoxPoint(box, i))).ToArray();
    }

    Point PivotPoint => pivot ?? Selected!.Center;

    // ---------------------------------------------------------------- rendering
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));   // hit-testable margin
        dc.PushTransform(new ScaleTransform(1 / S, 1 / S));
        var org = Origin;
        dc.PushTransform(new TranslateTransform(org.X, org.Y));
        dc.PushTransform(new ScaleTransform(Zoom, Zoom));
        dc.DrawImage(Image, new Rect(0, 0, Image.PixelWidth, Image.PixelHeight));
        foreach (var a in Items) if (a != Hidden) a.Draw(dc, Image);
        cur?.Draw(dc, Image);
        if (Selected != null && Selected != Hidden) DrawSelection(dc, Selected);
        if (CropMode) DrawCrop(dc);
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    void DrawCrop(DrawingContext dc)
    {
        var full = new Rect(0, 0, Image.PixelWidth, Image.PixelHeight);
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null,
            new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(crop)));
        var accent = new SolidColorBrush(Accent);
        dc.DrawRectangle(null, new Pen(Brushes.White, U), crop);
        var thirds = new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), U);
        for (int i = 1; i <= 2; i++)
        {
            dc.DrawLine(thirds, new Point(crop.X + crop.Width * i / 3, crop.Y), new Point(crop.X + crop.Width * i / 3, crop.Bottom));
            dc.DrawLine(thirds, new Point(crop.X, crop.Y + crop.Height * i / 3), new Point(crop.Right, crop.Y + crop.Height * i / 3));
        }
        double hs = 5 * U;
        for (int i = 0; i < 8; i++)
        {
            var h = BoxPoint(crop, i);
            dc.DrawEllipse(accent, new Pen(Brushes.White, U), h, hs, hs);
        }
    }

    void DrawSelection(DrawingContext dc, Ann a)
    {
        var accent = new SolidColorBrush(Accent);
        var pen = new Pen(accent, U);
        if (!a.ShowBox)
        {
            foreach (var nd in a.Nodes())
                dc.DrawEllipse(accent, new Pen(Brushes.White, U), a.ToWorld(nd), 5 * U, 5 * U);
            return;
        }
        var hw = WorldHandles(a);
        var outline = new Pen(accent, U) { DashStyle = DashStyles.Dash };
        foreach (var (i, j) in new[] { (0, 2), (2, 4), (4, 6), (6, 0) }) dc.DrawLine(outline, hw[i], hw[j]);

        if (!RotateMode)
        {
            double hs = 4 * U;
            var c = a.Center;
            var cross = new Pen(accent, U);
            dc.DrawLine(cross, new Point(c.X - 4 * U, c.Y - 4 * U), new Point(c.X + 4 * U, c.Y + 4 * U));
            dc.DrawLine(cross, new Point(c.X - 4 * U, c.Y + 4 * U), new Point(c.X + 4 * U, c.Y - 4 * U));
            foreach (var h in hw)
                dc.DrawRectangle(Brushes.White, pen, new Rect(h.X - hs, h.Y - hs, hs * 2, hs * 2));
            foreach (var nd in a.Nodes())
                dc.DrawEllipse(accent, new Pen(Brushes.White, U), a.ToWorld(nd), 5 * U, 5 * U);
        }
        else
        {
            foreach (int i in new[] { 0, 2, 4, 6 })
            {
                var h = hw[i];
                dc.DrawEllipse(Brushes.White, pen, h, 7 * U, 7 * U);
                var arc = new StreamGeometry();
                using (var g = arc.Open())
                {
                    g.BeginFigure(new Point(h.X + 3.5 * U, h.Y - 1.5 * U), false, false);
                    g.ArcTo(new Point(h.X - 1.5 * U, h.Y + 3.5 * U), new Size(3.8 * U, 3.8 * U), 0, true, SweepDirection.Clockwise, true, false);
                }
                arc.Freeze();
                dc.DrawGeometry(null, new Pen(accent, 1.6 * U) { EndLineCap = PenLineCap.Round, StartLineCap = PenLineCap.Round }, arc);
            }
            var p = PivotPoint;
            dc.DrawEllipse(Brushes.White, pen, p, 7 * U, 7 * U);
            dc.DrawLine(pen, new Point(p.X - 10 * U, p.Y), new Point(p.X + 10 * U, p.Y));
            dc.DrawLine(pen, new Point(p.X, p.Y - 10 * U), new Point(p.X, p.Y + 10 * U));
            dc.DrawEllipse(accent, null, p, 2.2 * U, 2.2 * U);
        }
    }

    /// <summary>Final image at native resolution, without selection UI.</summary>
    public BitmapSource Render()
    {
        int w = Image.PixelWidth, h = Image.PixelHeight;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(Image, new Rect(0, 0, w, h));
            foreach (var a in Items) a.Draw(dc, Image);
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    // ---------------------------------------------------------------- history
    Snap Snapshot() => new(Items.Select(a => a.Clone()).ToList(), Image);

    void PushUndo(Snap snap) { undo.Push(snap); redo.Clear(); Changed?.Invoke(); }

    void Restore(Snap s)
    {
        Items = s.Items;
        Select(null);
        if (!ReferenceEquals(Image, s.Image))
        {
            Image = s.Image;
            InvalidateMeasure();
            ImageChanged?.Invoke();
        }
        Changed?.Invoke(); InvalidateVisual();
    }

    public void Undo()
    {
        if (undo.Count == 0) return;
        redo.Push(Snapshot());
        Restore(undo.Pop());
    }

    public void Redo()
    {
        if (redo.Count == 0) return;
        undo.Push(Snapshot());
        Restore(redo.Pop());
    }

    // ---------------------------------------------------------------- selection / edits
    public void Select(Ann? a)
    {
        if (Selected == a) return;
        Selected = a;
        RotateMode = false;
        pivot = null;
        SelectionChanged?.Invoke(a);
        InvalidateVisual();
    }

    public void DeleteSelected()
    {
        if (Selected == null) return;
        PushUndo(Snapshot());
        Items.Remove(Selected);
        Select(null);
        InvalidateVisual();
    }

    Ann? lastStyled; DateTime lastStyleAt;

    public void StyleSelected(Color? color, double? w)
    {
        if (Selected == null) return;
        var now = DateTime.UtcNow;
        if (lastStyled != Selected || (now - lastStyleAt).TotalMilliseconds > 700) PushUndo(Snapshot());
        lastStyled = Selected; lastStyleAt = now;
        if (color is { } c) Selected.Color = c;
        if (w is { } ww) Selected.W = ww;
        InvalidateVisual();
    }

    public void SetArrowStyle(int style)
    {
        CurArrowStyle = style;
        if (Selected is ArrowAnn t && t.Style != style)
        {
            PushUndo(Snapshot());
            t.Style = style;
            InvalidateVisual();
        }
    }

    public void SetTextBg(int bg)
    {
        CurTextBg = bg;
        if (Selected is TextAnn t && t.Bg != bg)
        {
            PushUndo(Snapshot());
            t.Bg = bg;
            InvalidateVisual();
        }
    }

    public void Nudge(Vector v)
    {
        if (Selected == null) return;
        PushUndo(Snapshot());
        Selected.Move(v);
        FitCanvas();
        InvalidateVisual();
    }

    public void CommitText(TextAnn? existing, Point pos, string text)
    {
        text = text.TrimEnd();
        if (existing != null)
        {
            if (text == existing.Text) return;
            PushUndo(Snapshot());
            if (text.Length == 0) { Items.Remove(existing); Select(null); }
            else existing.Text = text;
        }
        else if (text.Length > 0)
        {
            PushUndo(Snapshot());
            var t = new TextAnn { Color = CurColor, W = CurW, A = pos, B = pos, Text = text, Bg = CurTextBg };
            Items.Add(t);
            Select(t);
            FitCanvas();
        }
        InvalidateVisual();
    }

    public void EndTextEdit() { RotateMode = false; }

    // ---------------------------------------------------------------- canvas growth
    /// <summary>If any object sticks out of the image, grow the canvas (white fill) so everything fits.</summary>
    void FitCanvas()
    {
        const double Max = 4000;
        double W = Image.PixelWidth, H = Image.PixelHeight, l = 0, t = 0, r = W, b = H;
        foreach (var a in Items)
        {
            if (!a.ExtendsCanvas) continue;
            var wb = a.WorldBounds();
            l = Math.Min(l, wb.Left); t = Math.Min(t, wb.Top); r = Math.Max(r, wb.Right); b = Math.Max(b, wb.Bottom);
        }
        l = Math.Max(l, -Max); t = Math.Max(t, -Max); r = Math.Min(r, W + Max); b = Math.Min(b, H + Max);
        int il = (int)Math.Floor(l), it = (int)Math.Floor(t), ir = (int)Math.Ceiling(r), ib = (int)Math.Ceiling(b);
        if (il == 0 && it == 0 && ir == Image.PixelWidth && ib == Image.PixelHeight) return;

        int nw = ir - il, nh = ib - it;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, nw, nh));
            dc.DrawImage(Image, new Rect(-il, -it, Image.PixelWidth, Image.PixelHeight));
        }
        var rtb = new RenderTargetBitmap(nw, nh, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        Image = rtb;
        var shift = new Vector(-il, -it);
        foreach (var a in Items) a.Move(shift);
        if (pivot is { } pv) pivot = pv + shift;
        InvalidateMeasure(); InvalidateVisual();
        ImageChanged?.Invoke();
    }

    // ---------------------------------------------------------------- crop
    public void BeginCrop()
    {
        Select(null);
        CropMode = true;
        crop = new Rect(0, 0, Image.PixelWidth, Image.PixelHeight);
        Cursor = Cursors.Arrow;
        InvalidateVisual();
    }

    public void CancelCrop() { CropMode = false; mode = Mode.None; InvalidateVisual(); }

    public void ApplyCrop()
    {
        if (!CropMode) return;
        CropMode = false;
        int x = (int)Math.Round(crop.X), y = (int)Math.Round(crop.Y);
        int w = Math.Min((int)Math.Round(crop.Width), Image.PixelWidth - x), h = Math.Min((int)Math.Round(crop.Height), Image.PixelHeight - y);
        if (w < 2 || h < 2 || (w == Image.PixelWidth && h == Image.PixelHeight)) { InvalidateVisual(); return; }
        PushUndo(Snapshot());
        var cb = new CroppedBitmap(Image, new Int32Rect(x, y, w, h));
        cb.Freeze();
        Image = cb;
        var shift = new Vector(-x, -y);
        foreach (var a in Items) a.Move(shift);
        Select(null);
        InvalidateMeasure(); InvalidateVisual();
        ImageChanged?.Invoke();
    }

    int HitCropHandle(Point p)
    {
        for (int i = 0; i < 8; i++)
            if ((p - BoxPoint(crop, i)).Length <= 9 * U) return i;
        return -1;
    }

    void DragCrop(Point p)
    {
        double W = Image.PixelWidth, H = Image.PixelHeight, min = 8;
        var d = p - cropStart;
        if (handle == -2)   // move
        {
            double nx = Math.Clamp(cropOrig.X + d.X, 0, W - cropOrig.Width), ny = Math.Clamp(cropOrig.Y + d.Y, 0, H - cropOrig.Height);
            crop = new Rect(nx, ny, cropOrig.Width, cropOrig.Height);
            return;
        }
        var dir = Dirs[handle];
        double l = cropOrig.Left, t = cropOrig.Top, r = cropOrig.Right, b = cropOrig.Bottom;
        if (dir.X < 0) l = Math.Clamp(cropOrig.Left + d.X, 0, r - min);
        if (dir.X > 0) r = Math.Clamp(cropOrig.Right + d.X, l + min, W);
        if (dir.Y < 0) t = Math.Clamp(cropOrig.Top + d.Y, 0, b - min);
        if (dir.Y > 0) b = Math.Clamp(cropOrig.Bottom + d.Y, t + min, H);
        crop = new Rect(l, t, r - l, b - t);
    }

    // ---------------------------------------------------------------- hit testing
    Point Pos(MouseEventArgs e) { var p = e.GetPosition(this); var o = Origin; return new Point((p.X * S - o.X) / Zoom, (p.Y * S - o.Y) / Zoom); }

    Ann? HitItem(Point p)
    {
        for (int i = Items.Count - 1; i >= 0; i--)
            if (Items[i].Hit(p, 6 * U)) return Items[i];
        return null;
    }

    /// <summary>Returns (mode, index) of the handle under p, or Mode.None.</summary>
    (Mode, int) HitHandle(Point p)
    {
        var a = Selected;
        if (a == null) return (Mode.None, -1);
        if (!a.ShowBox)
        {
            var ns = a.Nodes();
            for (int i = ns.Length - 1; i >= 0; i--)
                if ((p - a.ToWorld(ns[i])).Length <= 8 * U) return (Mode.Endpoint, i);
            return (Mode.None, -1);
        }
        var hw = WorldHandles(a);
        if (RotateMode)
        {
            if ((p - PivotPoint).Length <= 9 * U) return (Mode.Pivot, 0);
            foreach (int i in new[] { 0, 2, 4, 6 })
                if ((p - hw[i]).Length <= 10 * U) return (Mode.Rotate, i);
            return (Mode.None, -1);
        }
        var nodes = a.Nodes();
        for (int i = nodes.Length - 1; i >= 0; i--)
            if ((p - a.ToWorld(nodes[i])).Length <= 8 * U) return (Mode.Endpoint, i);
        for (int i = 0; i < 8; i++)
            if ((p - hw[i]).Length <= 7 * U) return (Mode.Scale, i);
        return (Mode.None, -1);
    }

    // ---------------------------------------------------------------- mouse
    void BeginEdit() { pending = Snapshot(); moved = false; orig = Selected!.Clone(); }

    void FirstMove()
    {
        if (moved) return;
        moved = true;
        PushUndo(pending!);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var p = Pos(e);

        if (CropMode)
        {
            int ch = HitCropHandle(p);
            if (ch < 0 && !crop.Contains(p)) return;
            handle = ch < 0 ? -2 : ch; cropOrig = crop; cropStart = p; mode = Mode.Crop;
            CaptureMouse();
            return;
        }

        var tool = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? Tool.Select : _tool;

        // handles of the selected object work with any tool
        var (m, idx) = HitHandle(p);
        if (m != Mode.None)
        {
            mode = m; handle = idx; startWorld = p;
            BeginEdit();
            grab = m == Mode.Scale ? WorldHandles(Selected!)[idx] - p : new Vector();
            if (m == Mode.Rotate)
            {
                var pv = PivotPoint;
                startAngle = Math.Atan2(p.Y - pv.Y, p.X - pv.X);
            }
            CaptureMouse(); return;
        }

        // a drawing tool: pressing on the selected object itself moves it; anywhere else starts a new shape
        if (tool != Tool.Select && tool != Tool.Text && Selected != null && Selected.Hit(p, 6 * U))
        {
            wasSelected = true;
            BeginEdit(); mode = Mode.Move; last = p; CaptureMouse();
            return;
        }

        if (tool == Tool.Select)
        {
            var hit = HitItem(p);
            wasSelected = hit != null && hit == Selected;
            Select(hit);
            if (hit == null) return;
            if (e.ClickCount == 2 && hit is TextAnn t) { RotateMode = false; TextRequested?.Invoke(t, t.A); return; }
            BeginEdit(); mode = Mode.Move; last = p; CaptureMouse();
            return;
        }

        if (tool == Tool.Text)
        {
            if (HitItem(p) is TextAnn t) { Select(t); TextRequested?.Invoke(t, t.A); }
            else { Select(null); TextRequested?.Invoke(null, p); }
            return;
        }

        Select(null);
        pending = Snapshot();
        cur = tool switch
        {
            Tool.Arrow => new ArrowAnn { Style = CurArrowStyle },
            Tool.Line => new LineAnn(),
            Tool.Ellipse => new EllipseAnn(),
            Tool.Marker => new MarkerAnn(),
            Tool.Blur => new BlurAnn(),
            Tool.Balloon => new BalloonAnn(),
            _ => new RectAnn(),
        };
        cur.Color = CurColor; cur.W = CurW; cur.A = cur.B = p;
        mode = Mode.Draw;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = Pos(e);
        if (mode == Mode.None) { UpdateCursor(p); return; }
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (mode)
        {
            case Mode.Draw:
                cur!.B = Constrain(cur.A, p, shift, cur is RectAnn or EllipseAnn or BlurAnn);
                break;
            case Mode.Move:
                var d = p - last;
                if (d.Length == 0) return;
                FirstMove();
                Selected!.Move(d); last = p;
                break;
            case Mode.Scale:
                FirstMove(); DoScale(p, shift);
                break;
            case Mode.Endpoint:
                FirstMove(); DoEndpoint(p, shift);
                break;
            case Mode.Rotate:
                FirstMove(); DoRotate(p, shift);
                break;
            case Mode.Pivot:
                pivot = p;
                break;
            case Mode.Crop:
                DragCrop(p);
                break;
        }
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (mode == Mode.None) return;
        ReleaseMouseCapture();
        if (mode == Mode.Draw && cur != null)
        {
            if ((cur.B - cur.A).Length > 3)
            {
                PushUndo(pending!);
                Items.Add(cur);
                Select(cur);   // stays selected for restyling; the drawing tool stays active
            }
            cur = null;
        }
        else if (mode == Mode.Move && !moved && wasSelected && Selected is { CanRotate: true })
        {
            RotateMode = !RotateMode;      // click on the selected object toggles scale <-> rotate
        }
        bool geometryChanged = mode is Mode.Draw or Mode.Move or Mode.Scale or Mode.Rotate or Mode.Endpoint;
        mode = Mode.None;
        if (geometryChanged) FitCanvas();
        InvalidateVisual();
    }

    void UpdateCursor(Point p)
    {
        if (CropMode)
        {
            int ch = HitCropHandle(p);
            Cursor = ch >= 0 ? HandleCursor(ch) : crop.Contains(p) ? Cursors.SizeAll : Cursors.Arrow;
            return;
        }
        bool selectLike = _tool == Tool.Select || Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var (m, i) = HitHandle(p);
        Cursor = m switch
        {
            Mode.Rotate or Mode.Pivot => Cursors.Hand,
            Mode.Endpoint => Cursors.Cross,
            Mode.Scale => HandleCursor(i),
            _ when selectLike => HitItem(p) != null ? Cursors.SizeAll : Cursors.Arrow,
            _ when _tool != Tool.Text && Selected != null && Selected.Hit(p, 6 * U) => Cursors.SizeAll,
            _ => _tool == Tool.Crop ? Cursors.Arrow : Cursors.Cross,
        };
    }

    static Cursor HandleCursor(int i) => (i % 4) switch { 0 => Cursors.SizeNWSE, 1 => Cursors.SizeNS, 2 => Cursors.SizeNESW, _ => Cursors.SizeWE };

    // ---------------------------------------------------------------- transforms
    /// <summary>Translate t so that local point <paramref name="anchor"/> lands on world point <paramref name="worldAnchor"/>.</summary>
    static void Pin(Ann t, Point anchor, Point worldAnchor)
    {
        var now = Ann.RotatePoint(anchor, t.Center, t.Rot);
        t.Move(worldAnchor - now);
    }

    void DoScale(Point p, bool shift)
    {
        var o = orig!;
        var g = o.GeomBox();
        var d = Dirs[handle];
        var anchor = shift ? new Point(g.X + g.Width / 2, g.Y + g.Height / 2)
                           : new Point(g.X + (1 - d.X) / 2 * g.Width, g.Y + (1 - d.Y) / 2 * g.Height);
        var pl = o.ToLocal(p + grab);
        double pad = Pad(o);
        double tx = pl.X - d.X * pad, ty = pl.Y - d.Y * pad;
        double hx = g.X + (d.X + 1) / 2 * g.Width, hy = g.Y + (d.Y + 1) / 2 * g.Height;
        double sx = 1, sy = 1;
        if (d.X != 0 && Math.Abs(hx - anchor.X) > 0.5) sx = Math.Max((tx - anchor.X) / (hx - anchor.X), 0.03);
        if (d.Y != 0 && Math.Abs(hy - anchor.Y) > 0.5) sy = Math.Max((ty - anchor.Y) / (hy - anchor.Y), 0.03);

        var t = o.Clone();
        t.ScaleAbout(anchor, sx, sy);
        Pin(t, anchor, o.ToWorld(anchor));
        if (t is BalloonAnn bt)   // resizing the frame must not drag the pointer tip along
            bt.SetNode(0, bt.ToLocal(o.ToWorld(o.Nodes()[0])));
        Selected!.Assign(t);
    }

    void DoEndpoint(Point p, bool shift)
    {
        var o = orig!;
        var t = o.Clone();
        var pl = o.ToLocal(p);
        if (handle < o.EndpointCount)
        {
            var other = handle == 0 ? o.B : o.A;
            t.SetNode(handle, Constrain(other, pl, shift, false));
            Pin(t, other, o.ToWorld(other));
        }
        else
        {
            t.SetNode(handle, pl);
            Pin(t, o.A, o.ToWorld(o.A));
        }
        Selected!.Assign(t);
    }

    void DoRotate(Point p, bool shift)
    {
        var o = orig!;
        var pv = PivotPoint;
        double delta = (Math.Atan2(p.Y - pv.Y, p.X - pv.X) - startAngle) * 180 / Math.PI;
        double total = o.Rot + delta;
        if (shift) total = Math.Round(total / 15) * 15;
        total = ((total + 180) % 360 + 360) % 360 - 180;
        delta = total - o.Rot;
        var c0 = o.Center;
        var c1 = Ann.RotatePoint(c0, pv, delta);
        var t = o.Clone();
        t.Move(c1 - c0);
        t.Rot = total;
        Selected!.Assign(t);
    }

    static Point Constrain(Point a, Point b, bool shift, bool square)
    {
        if (!shift) return b;
        var d = b - a;
        if (square)
        {
            double s = Math.Max(Math.Abs(d.X), Math.Abs(d.Y));
            return new Point(a.X + Math.CopySign(s, d.X), a.Y + Math.CopySign(s, d.Y));
        }
        double step = Math.PI / 4;
        double ang = Math.Round(Math.Atan2(d.Y, d.X) / step) * step;
        return new Point(a.X + d.Length * Math.Cos(ang), a.Y + d.Length * Math.Sin(ang));
    }
}
