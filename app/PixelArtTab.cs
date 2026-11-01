using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CasinoSignTool
{
    public enum Tool { Pen, Eraser, Fill, Line, Rect, RectFill }

    /// <summary>The editable canvas: LED view + mouse drawing.</summary>
    public class PixelCanvas : LedView
    {
        public Tool Tool = Tool.Pen;
        public event Action BeforeEdit;   // push undo
        public event Action Edited;
        Point? _start; Point _last; bool _value; Frame _base;
        public Point Hover { get; set; } = new Point(-1, -1);
        public event Action HoverChanged;

        public PixelCanvas() { Dots = true; Guides = true; }

        Point CellAt(Point p) { float c = Cell; var o = Origin; return new Point((int)Math.Floor((p.X - o.X) / c), (int)Math.Floor((p.Y - o.Y) / c)); }
        static bool In(Point p) => p.X >= 0 && p.Y >= 0 && p.X < Frame.W && p.Y < Frame.H;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            var p = CellAt(e.Location);
            if (!In(p)) return;
            bool erase = e.Button == MouseButtons.Right || Tool == Tool.Eraser;
            _value = !erase;
            BeforeEdit?.Invoke();
            switch (Tool)
            {
                case Tool.Fill: FloodFill(p, _value); Edited?.Invoke(); return;
                case Tool.Pen: case Tool.Eraser: Frame[p.X, p.Y] = _value; _start = p; _last = p; break;
                default: _start = p; _last = p; _base = Frame.Clone(); break;
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = CellAt(e.Location);
            if (p != Hover) { Hover = In(p) ? p : new Point(-1, -1); HoverChanged?.Invoke(); }
            if (_start == null) return;
            p = new Point(Math.Max(0, Math.Min(Frame.W - 1, p.X)), Math.Max(0, Math.Min(Frame.H - 1, p.Y)));
            if (p == _last) return;
            if (Tool == Tool.Pen || Tool == Tool.Eraser) { DrawLine(Frame, _last, p, _value); _last = p; }
            else
            {
                Frame.Px = (bool[])_base.Px.Clone();
                _last = p;
                if (Tool == Tool.Line) DrawLine(Frame, _start.Value, p, _value);
                else DrawRect(Frame, _start.Value, p, _value, Tool == Tool.RectFill);
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_start == null) return;
            _start = null; _base = null;
            Edited?.Invoke();
        }

        protected override void OnMouseLeave(EventArgs e) { Hover = new Point(-1, -1); HoverChanged?.Invoke(); base.OnMouseLeave(e); }

        public static void DrawLine(Frame f, Point a, Point b, bool v)
        {
            int dx = Math.Abs(b.X - a.X), dy = -Math.Abs(b.Y - a.Y), sx = a.X < b.X ? 1 : -1, sy = a.Y < b.Y ? 1 : -1, err = dx + dy;
            int x = a.X, y = a.Y;
            while (true)
            {
                f[x, y] = v;
                if (x == b.X && y == b.Y) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x += sx; }
                if (e2 <= dx) { err += dx; y += sy; }
            }
        }

        public static void DrawRect(Frame f, Point a, Point b, bool v, bool fill)
        {
            int x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X), y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (fill || x == x0 || x == x1 || y == y0 || y == y1) f[x, y] = v;
        }

        void FloodFill(Point p, bool v)
        {
            bool target = Frame[p.X, p.Y];
            if (target == v) return;
            var st = new Stack<Point>(); st.Push(p);
            while (st.Count > 0)
            {
                var q = st.Pop();
                if (!In(q) || Frame[q.X, q.Y] != target) continue;
                Frame[q.X, q.Y] = v;
                st.Push(new Point(q.X + 1, q.Y)); st.Push(new Point(q.X - 1, q.Y));
                st.Push(new Point(q.X, q.Y + 1)); st.Push(new Point(q.X, q.Y - 1));
            }
            Invalidate();
        }
    }

    /// <summary>Image → 1-bit 96x16 conversion.</summary>
    public static class ImageConvert
    {
        public enum Fit { Fit, Fill, Stretch }
        public enum Method { Threshold, FloydSteinberg, Atkinson, Ordered }

        /// <summary>Scale the image to 96x16 and return luminance 0..1 per pixel (transparent = 0).</summary>
        public static float[] Luma(Image src, Fit fit)
        {
            int W = Frame.W, H = Frame.H;
            using var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float sx = (float)W / src.Width, sy = (float)H / src.Height;
                RectangleF dst;
                if (fit == Fit.Stretch) dst = new RectangleF(0, 0, W, H);
                else
                {
                    float s = fit == Fit.Fit ? Math.Min(sx, sy) : Math.Max(sx, sy);
                    float w = src.Width * s, h = src.Height * s;
                    dst = new RectangleF((W - w) / 2, (H - h) / 2, w, h);
                }
                using var ia = new ImageAttributes(); ia.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(src, Rectangle.Round(dst), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
            var l = new float[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    l[y * W + x] = (0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B) / 255f * (c.A / 255f);
                }
            return l;
        }

        static readonly int[,] Bayer = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };

        public static Frame ToFrame(float[] luma, Method m, float threshold, float contrast, bool invert)
        {
            int W = Frame.W, H = Frame.H;
            var v = new float[luma.Length];
            for (int i = 0; i < v.Length; i++)
            {
                float x = (luma[i] - 0.5f) * contrast + 0.5f;
                x = Math.Max(0, Math.Min(1, x));
                v[i] = invert ? 1 - x : x;
            }
            var f = new Frame();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float old = v[i];
                    bool on;
                    if (m == Method.Ordered) on = old > (Bayer[y & 3, x & 3] + 0.5f) / 16f + (threshold - 0.5f);
                    else on = old > threshold;
                    f.Px[i] = on;
                    if (m == Method.Threshold || m == Method.Ordered) continue;
                    float err = old - (on ? 1 : 0);
                    void Add(int dx, int dy, float k) { int xx = x + dx, yy = y + dy; if (xx >= 0 && xx < W && yy < H) v[yy * W + xx] += err * k; }
                    if (m == Method.FloydSteinberg) { Add(1, 0, 7 / 16f); Add(-1, 1, 3 / 16f); Add(0, 1, 5 / 16f); Add(1, 1, 1 / 16f); }
                    else { float k = 1 / 8f; Add(1, 0, k); Add(2, 0, k); Add(-1, 1, k); Add(0, 1, k); Add(1, 1, k); Add(0, 2, k); }
                }
            return f;
        }
    }

    public class ImportDialog : Form
    {
        readonly Image _img;
        readonly LedView _prev = new LedView { Dock = DockStyle.Fill };
        readonly ComboBox _fit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        readonly ComboBox _method = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        readonly TrackBar _thr = new TrackBar { Minimum = 1, Maximum = 99, Value = 50, Width = 220, TickFrequency = 10 };
        readonly TrackBar _con = new TrackBar { Minimum = 5, Maximum = 40, Value = 10, Width = 220, TickFrequency = 5 };
        readonly CheckBox _inv = new CheckBox { Text = "Invert (light pixels become dark)", AutoSize = true };
        public Frame Result;

        public ImportDialog(Image img)
        {
            _img = img;
            Text = "Convert image to sign format (96x16, 1-bit)";
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(960, 470);
            StartPosition = FormStartPosition.CenterParent;
            _fit.Items.AddRange(new object[] { "Fit (keep whole image)", "Fill (crop to fill)", "Stretch" });
            _method.Items.AddRange(new object[] { "Threshold (crisp, best for logos/text)", "Floyd-Steinberg dither (photos)", "Atkinson dither (punchy)", "Ordered dither (pattern)" });
            _fit.SelectedIndex = img.Width / (double)img.Height > 3 ? 2 : 0;
            _method.SelectedIndex = 0;

            var opts = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
            Label L(string t) => new Label { Text = t, AutoSize = true, Margin = new Padding(8, 8, 2, 0) };
            opts.Controls.AddRange(new Control[] { L("Scale:"), _fit, L("Method:"), _method, L("Threshold:"), _thr, L("Contrast:"), _con, _inv });
            var ok = new Button { Text = "Use this", DialogResult = DialogResult.OK, AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            opts.Controls.Add(ok); opts.Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            var orig = new PictureBox { Dock = DockStyle.Top, Height = 110, SizeMode = PictureBoxSizeMode.Zoom, Image = img, BackColor = Color.FromArgb(30, 30, 30) };
            Controls.Add(_prev); Controls.Add(orig); Controls.Add(opts);
            foreach (var c in new Control[] { _fit, _method, _thr, _con, _inv })
            {
                if (c is ComboBox cb) cb.SelectedIndexChanged += (s, e) => Update2();
                if (c is TrackBar tb) tb.ValueChanged += (s, e) => Update2();
                if (c is CheckBox ch) ch.CheckedChanged += (s, e) => Update2();
            }
            Update2();
        }

        /// <summary>Convert another image (e.g. the next GIF frame) with the settings chosen in this dialog.</summary>
        public Frame Convert(Image im) => ImageConvert.ToFrame(ImageConvert.Luma(im, (ImageConvert.Fit)_fit.SelectedIndex), (ImageConvert.Method)_method.SelectedIndex, _thr.Value / 100f, _con.Value / 10f, _inv.Checked);

        void Update2()
        {
            var luma = ImageConvert.Luma(_img, (ImageConvert.Fit)_fit.SelectedIndex);
            Result = ImageConvert.ToFrame(luma, (ImageConvert.Method)_method.SelectedIndex, _thr.Value / 100f, _con.Value / 10f, _inv.Checked);
            _prev.Frame = Result; _prev.Invalidate();
        }
    }

    public class PixelArtTab : UserControl
    {
        readonly Ctx _ctx;
        readonly PixelCanvas _canvas = new PixelCanvas { Dock = DockStyle.Fill };
        readonly List<bool[]> _undo = new List<bool[]>(), _redo = new List<bool[]>();
        readonly Dictionary<Tool, RadioButton> _toolBtns = new Dictionary<Tool, RadioButton>();
        TextBox _stampText, _cName;
        ComboBox _font, _gen;
        NumericUpDown _size, _offX, _offY;
        CheckBox _bold, _center, _clearFirst, _progmem;
        Label _pos;
        readonly Timer _autosave = new Timer { Interval = 1500 };

        // Animation: a list of frames, each with its own delay
        readonly List<Frame> _frames = new List<Frame>();
        readonly List<int> _delays = new List<int>();
        int _cur;
        Label _frameLbl;
        NumericUpDown _delay;
        TextBox _animName;
        ComboBox _boardAnims, _animOn, _animOff;
        ProgressBar _upBar;
        Button _playBtn;
        readonly Timer _playTimer = new Timer();
        bool _loadingFrame;

        public PixelArtTab(Ctx ctx)
        {
            _ctx = ctx;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            // Row 1: tools + edit actions
            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            void T(string label, Tool t, string key)
            {
                var rb = new RadioButton { Text = label, Appearance = Appearance.Button, AutoSize = true, Checked = t == Tool.Pen, TextAlign = ContentAlignment.MiddleCenter };
                rb.CheckedChanged += (s, e) => { if (rb.Checked) _canvas.Tool = t; };
                new ToolTip().SetToolTip(rb, $"{label} ({key})");
                _toolBtns[t] = rb; tools.Controls.Add(rb);
            }
            T("✎ Pen", Tool.Pen, "P"); T("⌫ Eraser", Tool.Eraser, "E"); T("▨ Fill", Tool.Fill, "F");
            T("╱ Line", Tool.Line, "L"); T("▭ Rect", Tool.Rect, "R"); T("■ Filled rect", Tool.RectFill, "Shift+R");
            Button B(FlowLayoutPanel p, string t, Action a, string tip = null) { var b = new Button { Text = t, AutoSize = true }; b.Click += (s, e) => a(); if (tip != null) new ToolTip().SetToolTip(b, tip); p.Controls.Add(b); return b; }
            tools.Controls.Add(new Label { Width = 12 });
            B(tools, "↶ Undo", Undo, "Ctrl+Z"); B(tools, "↷ Redo", Redo, "Ctrl+Y");
            B(tools, "Clear", () => Edit(f => Array.Clear(f.Px, 0, f.Px.Length)));
            B(tools, "Invert", () => Edit(f => { for (int i = 0; i < f.Px.Length; i++) f.Px[i] = !f.Px[i]; }));
            B(tools, "Flip ↔", () => Edit(f => Transform(f, (x, y) => (Frame.W - 1 - x, y))));
            B(tools, "Flip ↕", () => Edit(f => Transform(f, (x, y) => (x, Frame.H - 1 - y))));
            B(tools, "◀", () => Edit(f => Transform(f, (x, y) => ((x + 1) % Frame.W, y))), "Shift left (wraps)");
            B(tools, "▶", () => Edit(f => Transform(f, (x, y) => ((x + Frame.W - 1) % Frame.W, y))), "Shift right (wraps)");
            B(tools, "▲", () => Edit(f => Transform(f, (x, y) => (x, (y + 1) % Frame.H))), "Shift up (wraps)");
            B(tools, "▼", () => Edit(f => Transform(f, (x, y) => (x, (y + Frame.H - 1) % Frame.H))), "Shift down (wraps)");
            var dots = new CheckBox { Text = "LED dots", Checked = true, AutoSize = true, Margin = new Padding(12, 6, 0, 0) };
            dots.CheckedChanged += (s, e) => { _canvas.Dots = dots.Checked; _canvas.Invalidate(); };
            var guides = new CheckBox { Text = "Panel guides", Checked = true, AutoSize = true, Margin = new Padding(6, 6, 0, 0) };
            guides.CheckedChanged += (s, e) => { _canvas.Guides = guides.Checked; _canvas.Invalidate(); };
            tools.Controls.Add(dots); tools.Controls.Add(guides);
            _pos = new Label { AutoSize = true, Margin = new Padding(12, 6, 0, 0), ForeColor = Color.DimGray };
            tools.Controls.Add(_pos);
            root.Controls.Add(tools, 0, 0);

            root.Controls.Add(_canvas, 0, 1);

            // Row 3: text stamp + generators
            var stamp = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            Label L(string t) => new Label { Text = t, AutoSize = true, Margin = new Padding(6, 7, 2, 0) };
            _stampText = new TextBox { Width = 200, Text = "JACKPOT" };
            _font = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
            using (var fonts = new InstalledFontCollection()) foreach (var ff in fonts.Families) _font.Items.Add(ff.Name);
            foreach (var pref in new[] { "Arial", "Segoe UI", "Tahoma", "Verdana" }) if (_font.Items.Contains(pref)) { _font.SelectedItem = pref; break; }
            if (_font.SelectedIndex < 0 && _font.Items.Count > 0) _font.SelectedIndex = 0;
            _size = new NumericUpDown { Minimum = 5, Maximum = 40, Value = 12, Width = 45 };
            _bold = new CheckBox { Text = "Bold", AutoSize = true, Checked = true, Margin = new Padding(4, 6, 0, 0) };
            _center = new CheckBox { Text = "Center", AutoSize = true, Checked = true, Margin = new Padding(4, 6, 0, 0) };
            _clearFirst = new CheckBox { Text = "Clear first", AutoSize = true, Checked = true, Margin = new Padding(4, 6, 0, 0) };
            _offX = new NumericUpDown { Minimum = -96, Maximum = 96, Width = 50 };
            _offY = new NumericUpDown { Minimum = -16, Maximum = 16, Width = 45 };
            stamp.Controls.AddRange(new Control[] { L("Text stamp:"), _stampText, _font, L("size px"), _size, _bold, _center, _clearFirst, L("X"), _offX, L("Y"), _offY });
            B(stamp, "Stamp text", StampText);
            stamp.Controls.Add(L("   Generate:"));
            _gen = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
            _gen.Items.AddRange(new object[] { "Border", "Double border", "Checker", "Stripes", "Stars", "Noise", "Sine wave", "Gradient (dithered)", "Chase lights" });
            _gen.SelectedIndex = 0;
            stamp.Controls.Add(_gen);
            B(stamp, "Apply", Generate);
            root.Controls.Add(stamp, 0, 2);

            // Row 4: files + sign
            var files = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            B(files, "Import image…", ImportImage).Font = new Font(Font, FontStyle.Bold);
            B(files, "Open project…", OpenProject);
            B(files, "Save project…", SaveProject);
            B(files, "Save PNG 1x…", () => SavePng(1));
            B(files, "Save PNG 10x…", () => SavePng(10));
            files.Controls.Add(L("   C name:"));
            _cName = new TextBox { Width = 90, Text = "sign_art" };
            _progmem = new CheckBox { Text = "PROGMEM", Checked = true, AutoSize = true, Margin = new Padding(4, 6, 0, 0) };
            files.Controls.Add(_cName); files.Controls.Add(_progmem);
            B(files, "Copy C array", () => { Clipboard.SetText(CExport()); _ctx.Status("C array copied (1-bit, row-major, MSB first: same as Adafruit GFX drawBitmap and the sign's BITMAP command)."); });
            B(files, "Save .h…", SaveHeader);
            files.Controls.Add(new Label { Width = 16 });
            var send = B(files, "▶ Send to sign", async () => await SendToSign());
            send.Font = new Font(Font, FontStyle.Bold); send.BackColor = Color.FromArgb(255, 220, 200);
            B(files, "Get from sign", async () => await GetFromSign());
            root.Controls.Add(files, 0, 3);

            // Row 5: animation frames + sign storage
            var anim = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, BackColor = Color.FromArgb(235, 240, 250) };
            anim.Controls.Add(new Label { Text = "Animation:", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(4, 7, 4, 0) });
            B(anim, "◀", () => GoFrame(_cur - 1));
            _frameLbl = new Label { AutoSize = true, Margin = new Padding(2, 7, 2, 0) };
            anim.Controls.Add(_frameLbl);
            B(anim, "▶", () => GoFrame(_cur + 1));
            B(anim, "+ Frame", () => { _frames.Insert(_cur + 1, new Frame()); _delays.Insert(_cur + 1, _delays[_cur]); GoFrame(_cur + 1); });
            B(anim, "Duplicate", () => { _frames.Insert(_cur + 1, _frames[_cur].Clone()); _delays.Insert(_cur + 1, _delays[_cur]); GoFrame(_cur + 1); });
            B(anim, "Delete", () => { if (_frames.Count == 1) { Edit(f => Array.Clear(f.Px, 0, f.Px.Length)); return; } _frames.RemoveAt(_cur); _delays.RemoveAt(_cur); GoFrame(Math.Min(_cur, _frames.Count - 1)); });
            anim.Controls.Add(new Label { Text = "ms:", AutoSize = true, Margin = new Padding(6, 7, 2, 0) });
            _delay = new NumericUpDown { Minimum = 20, Maximum = 60000, Value = 100, Increment = 10, Width = 65 };
            _delay.ValueChanged += (s, e) => { if (!_loadingFrame) _delays[_cur] = (int)_delay.Value; };
            anim.Controls.Add(_delay);
            B(anim, "All frames", () => { for (int i = 0; i < _delays.Count; i++) _delays[i] = (int)_delay.Value; _ctx.Status($"Every frame now lasts {_delay.Value} ms."); }, "Use this delay for every frame");
            _playBtn = B(anim, "▶ Preview", TogglePlay);
            anim.Controls.Add(new Label { Text = "  Name:", AutoSize = true, Margin = new Padding(6, 7, 2, 0) });
            _animName = new TextBox { Width = 100, Text = "my_anim" };
            anim.Controls.Add(_animName);
            var up = B(anim, "Upload to sign", async () => await UploadAnim());
            up.Font = new Font(Font, FontStyle.Bold);
            _upBar = new ProgressBar { Width = 80, Height = 16, Margin = new Padding(4, 8, 4, 0), Visible = false };
            anim.Controls.Add(_upBar);
            anim.Controls.Add(new Label { Text = "  On the sign:", AutoSize = true, Margin = new Padding(6, 7, 2, 0) });
            _boardAnims = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
            anim.Controls.Add(_boardAnims);
            B(anim, "Play", async () => { if (_boardAnims.SelectedItem is string n) { if (_ctx.Mode == "diag") await _ctx.SetMode("run"); await _ctx.Try("ANIM PLAY " + n); } });
            B(anim, "Edit", async () => await OpenFromSign());
            B(anim, "Delete", async () =>
            {
                if (!(_boardAnims.SelectedItem is string n)) return;
                if (MessageBox.Show($"Delete \"{n}\" from the sign?", "Delete animation", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                await _ctx.Try("ANIM DEL " + n); await RefreshBoardAnims();
            });
            anim.Controls.Add(new Label { Text = "  Startup:", AutoSize = true, Margin = new Padding(6, 7, 2, 0) });
            _animOn = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
            anim.Controls.Add(_animOn);
            anim.Controls.Add(new Label { Text = "Shutdown:", AutoSize = true, Margin = new Padding(6, 7, 2, 0) });
            _animOff = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
            anim.Controls.Add(_animOff);
            B(anim, "Set", async () =>
            {
                string on = _animOn.SelectedIndex <= 0 ? "" : (string)_animOn.SelectedItem, off = _animOff.SelectedIndex <= 0 ? "" : (string)_animOff.SelectedItem;
                if (await _ctx.Try("CONF SET anim_on " + on) != null && await _ctx.Try("CONF SET anim_off " + off) != null)
                    _ctx.Status($"Startup animation: {(on == "" ? "none" : on)}. Shutdown animation: {(off == "" ? "none" : off)}. They play whenever the sign is switched on/off.");
            }, "Save the startup and shutdown animations");
            root.Controls.Add(anim, 0, 4);
            _frames.Add(_canvas.Frame); _delays.Add(100);
            _playTimer.Tick += (s, e) => { int n = (_cur + 1) % _frames.Count; GoFrame(n, keepUndo: true); _playTimer.Interval = Math.Max(20, _delays[n]); };
            _ctx.StateChanged += async () => { if (_ctx.Board.Connected) await RefreshBoardAnims(); };
            UpdateFrameLabel();

            _canvas.BeforeEdit += PushUndo;
            _canvas.Edited += Changed;
            _canvas.HoverChanged += () => _pos.Text = _canvas.Hover.X >= 0 ? $"x {_canvas.Hover.X}, y {_canvas.Hover.Y}   (right-click erases)" : "right-click erases";
            _autosave.Tick += (s, e) => { _autosave.Stop(); try { Directory.CreateDirectory(AppSettings.Dir); File.WriteAllText(AppSettings.ArtAutosave, ProjectJson()); } catch { } };
            try { if (File.Exists(AppSettings.ArtAutosave)) LoadProjectJson(File.ReadAllText(AppSettings.ArtAutosave)); } catch { }
        }

        // ---------- editing helpers ----------
        void PushUndo()
        {
            _undo.Add((bool[])_canvas.Frame.Px.Clone());
            if (_undo.Count > 100) _undo.RemoveAt(0);
            _redo.Clear();
        }
        void Changed() { _canvas.Invalidate(); _autosave.Stop(); _autosave.Start(); }
        void Edit(Action<Frame> a) { PushUndo(); a(_canvas.Frame); Changed(); }
        void Undo() { if (_undo.Count == 0) return; _redo.Add(_canvas.Frame.Px); _canvas.Frame.Px = _undo[_undo.Count - 1]; _undo.RemoveAt(_undo.Count - 1); Changed(); }
        void Redo() { if (_redo.Count == 0) return; _undo.Add(_canvas.Frame.Px); _canvas.Frame.Px = _redo[_redo.Count - 1]; _redo.RemoveAt(_redo.Count - 1); Changed(); }

        /// <summary>dest(x,y) = src(map(x,y)).</summary>
        static void Transform(Frame f, Func<int, int, (int, int)> map)
        {
            var src = f.Clone();
            for (int y = 0; y < Frame.H; y++) for (int x = 0; x < Frame.W; x++) { var (sx, sy) = map(x, y); f[x, y] = src[sx, sy]; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z)) { Undo(); return true; }
            if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z)) { Redo(); return true; }
            if (!(ActiveControl is TextBox) && !(ActiveControl is NumericUpDown) && !(ActiveControl is ComboBox))
            {
                Tool? t = keyData switch { Keys.P => Tool.Pen, Keys.E => Tool.Eraser, Keys.F => Tool.Fill, Keys.L => Tool.Line, Keys.R => Tool.Rect, Keys.Shift | Keys.R => Tool.RectFill, _ => (Tool?)null };
                if (t != null) { _toolBtns[t.Value].Checked = true; return true; }
                if (keyData == Keys.T) { _stampText.Focus(); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- text stamp ----------
        void StampText()
        {
            if (_font.SelectedItem == null) return;
            using var bmp = new Bitmap(Frame.W, Frame.H, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;   // crisp pixels, no blur
                using var font = new Font((string)_font.SelectedItem, (float)_size.Value, _bold.Checked ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
                var fmt = new StringFormat(StringFormat.GenericTypographic) { LineAlignment = StringAlignment.Center, Alignment = _center.Checked ? StringAlignment.Center : StringAlignment.Near, FormatFlags = StringFormatFlags.NoWrap };
                var rect = new RectangleF((float)_offX.Value, (float)_offY.Value, Frame.W, Frame.H);
                g.DrawString(_stampText.Text, font, Brushes.White, rect, fmt);
            }
            Edit(f =>
            {
                if (_clearFirst.Checked) Array.Clear(f.Px, 0, f.Px.Length);
                for (int y = 0; y < Frame.H; y++) for (int x = 0; x < Frame.W; x++) if (bmp.GetPixel(x, y).R > 127) f[x, y] = true;
            });
        }

        // ---------- generators ----------
        void Generate()
        {
            var rnd = new Random();
            string g = (string)_gen.SelectedItem;
            Edit(f =>
            {
                switch (g)
                {
                    case "Border": PixelCanvas.DrawRect(f, new Point(0, 0), new Point(Frame.W - 1, Frame.H - 1), true, false); break;
                    case "Double border":
                        PixelCanvas.DrawRect(f, new Point(0, 0), new Point(Frame.W - 1, Frame.H - 1), true, false);
                        PixelCanvas.DrawRect(f, new Point(2, 2), new Point(Frame.W - 3, Frame.H - 3), true, false); break;
                    case "Checker": for (int y = 0; y < Frame.H; y++) for (int x = 0; x < Frame.W; x++) f[x, y] = ((x / 2) + (y / 2)) % 2 == 0; break;
                    case "Stripes": for (int y = 0; y < Frame.H; y++) for (int x = 0; x < Frame.W; x++) f[x, y] = ((x + y) / 3) % 2 == 0; break;
                    case "Stars": Array.Clear(f.Px, 0, f.Px.Length); for (int i = 0; i < 40; i++) f[rnd.Next(Frame.W), rnd.Next(Frame.H)] = true; break;
                    case "Noise": for (int i = 0; i < f.Px.Length; i++) f.Px[i] = rnd.NextDouble() < 0.35; break;
                    case "Sine wave":
                        Array.Clear(f.Px, 0, f.Px.Length);
                        for (int x = 0; x < Frame.W; x++)
                        {
                            int y = (int)Math.Round(7.5 + 6.5 * Math.Sin(x / (double)Frame.W * Math.PI * 4));
                            int y2 = (int)Math.Round(7.5 + 6.5 * Math.Sin((x + 1) / (double)Frame.W * Math.PI * 4));
                            PixelCanvas.DrawLine(f, new Point(x, y), new Point(Math.Min(x + 1, Frame.W - 1), y2), true);
                        }
                        break;
                    case "Gradient (dithered)":
                        var luma = new float[Frame.W * Frame.H];
                        for (int y = 0; y < Frame.H; y++) for (int x = 0; x < Frame.W; x++) luma[y * Frame.W + x] = x / (float)(Frame.W - 1);
                        f.Px = ImageConvert.ToFrame(luma, ImageConvert.Method.Ordered, 0.5f, 1f, false).Px; break;
                    case "Chase lights":
                        for (int x = 0; x < Frame.W; x++) { f[x, 0] = x % 3 == 0; f[x, Frame.H - 1] = x % 3 == 0; }
                        for (int y = 0; y < Frame.H; y++) { f[0, y] = y % 3 == 0; f[Frame.W - 1, y] = y % 3 == 0; }
                        break;
                }
            });
        }

        // ---------- import / export ----------
        void ImportImage()
        {
            using var d = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*" };
            if (d.ShowDialog() != DialogResult.OK) return;
            Image src;
            MemoryStream ms;
            try { ms = new MemoryStream(File.ReadAllBytes(d.FileName)); src = Image.FromStream(ms); }
            catch (Exception ex) { MessageBox.Show("Couldn't open that image: " + ex.Message); return; }
            using (ms)
            using (src)
            {
                int count = 1;
                try { count = src.GetFrameCount(FrameDimension.Time); } catch { }
                bool all = count > 1 && MessageBox.Show($"This GIF has {count} frames. Import all of them as an animation? (No = just the first frame)", "Import GIF", MessageBoxButtons.YesNo) == DialogResult.Yes;
                if (count > 600) count = 600;
                using var first = new Bitmap(src);
                using var dlg = new ImportDialog(first);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (!all) { Edit(f => f.Px = (bool[])dlg.Result.Px.Clone()); return; }
                // GIF frame delays are in 1/100 s, 4 bytes per frame.
                int[] delays = Enumerable.Repeat(100, count).ToArray();
                try
                {
                    var prop = src.GetPropertyItem(0x5100);
                    for (int i = 0; i < count && i * 4 + 3 < prop.Value.Length; i++) delays[i] = Math.Max(20, BitConverter.ToInt32(prop.Value, i * 4) * 10);
                }
                catch { }
                _frames.Clear(); _delays.Clear();
                for (int i = 0; i < count; i++)
                {
                    src.SelectActiveFrame(FrameDimension.Time, i);
                    using var bmp = new Bitmap(src);
                    _frames.Add(dlg.Convert(bmp)); _delays.Add(delays[i]);
                }
                _animName.Text = new string(Path.GetFileNameWithoutExtension(d.FileName).Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').Take(24).ToArray());
                GoFrame(0);
                _ctx.Status($"Imported {count} frames. Press Preview to watch it, then Upload to sign.");
            }
        }

        string ProjectJson()
        {
            // "pixels" (first frame) keeps the HTML editor's format; "frames"/"delays" hold the whole animation.
            var px = _frames[0].Px.Select(b => b ? "#ff4628" : "#000000").ToArray();
            return Json.Write(new Dictionary<string, object>
            {
                ["width"] = Frame.W, ["height"] = Frame.H, ["pixels"] = px,
                ["frames"] = _frames.Select(f => f.ToHex()).ToArray(), ["delays"] = _delays.ToArray(), ["name"] = _animName?.Text ?? "",
            });
        }

        void LoadExtraFrames(Reply r)
        {
            var hex = r.D.TryGetValue("frames", out var fo) && fo is object[] fa ? fa.Select(x => System.Convert.ToString(x)).ToList() : null;
            if (hex == null || hex.Count == 0) { _frames.Clear(); _delays.Clear(); _frames.Add(_canvas.Frame); _delays.Add(100); GoFrame(0); return; }
            var delays = r.Ints("delays");
            _frames.Clear(); _delays.Clear();
            for (int i = 0; i < hex.Count; i++) { _frames.Add(Frame.FromHex(hex[i])); _delays.Add(i < delays.Count ? delays[i] : 100); }
            if (r.Str("name").Length > 0 && _animName != null) _animName.Text = r.Str("name");
            GoFrame(0);
        }

        void SetCurrent(Frame f) { if (_frames.Count == 0) { _frames.Add(f); _delays.Add(100); } _frames[_cur] = f; _canvas.Frame = f; _canvas.Invalidate(); }

        void UpdateFrameLabel() { if (_frameLbl != null) _frameLbl.Text = $"frame {_cur + 1} / {_frames.Count}"; }

        void GoFrame(int i, bool keepUndo = false)
        {
            if (_frames.Count == 0) return;
            _cur = (i + _frames.Count) % _frames.Count;
            _canvas.Frame = _frames[_cur];
            _loadingFrame = true; _delay.Value = Math.Max(_delay.Minimum, Math.Min(_delay.Maximum, _delays[_cur])); _loadingFrame = false;
            if (!keepUndo) { _undo.Clear(); _redo.Clear(); }
            UpdateFrameLabel();
            _canvas.Invalidate();
            if (!keepUndo) Changed();
        }

        void TogglePlay()
        {
            if (_playTimer.Enabled) { _playTimer.Stop(); _playBtn.Text = "▶ Preview"; GoFrame(_cur); return; }
            if (_frames.Count < 2) { _ctx.Status("Add more frames to preview an animation."); return; }
            _playTimer.Interval = Math.Max(20, _delays[_cur]); _playTimer.Start(); _playBtn.Text = "■ Stop";
        }

        async Task RefreshBoardAnims()
        {
            var r = await _ctx.Try("ANIM LIST");
            if (r == null) return;
            var names = r.List("anims").Select(a => a.Str("name")).Where(n => !n.StartsWith("_")).OrderBy(n => n).ToList();
            void Fill(ComboBox cb, bool none, string sel)
            {
                cb.Items.Clear();
                if (none) cb.Items.Add("(none)");
                foreach (var n in names) cb.Items.Add(n);
                cb.SelectedItem = sel != null && cb.Items.Contains(sel) ? sel : (cb.Items.Count > 0 ? cb.Items[0] : null);
            }
            Fill(_boardAnims, false, _boardAnims.SelectedItem as string);
            Fill(_animOn, true, r.Str("anim_on")); Fill(_animOff, true, r.Str("anim_off"));
            if (_animOn.SelectedItem == null) _animOn.SelectedIndex = 0;
            if (_animOff.SelectedItem == null) _animOff.SelectedIndex = 0;
            _ctx.Status($"Sign storage: {names.Count} animation(s), {r.Int("fs_free") / 1024} KB free.");
        }

        async Task UploadAnim()
        {
            if (!_ctx.Board.Connected) { _ctx.Status("Connect to the sign first."); return; }
            string name = new string(_animName.Text.Trim().Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
            if (name.Length == 0 || name.Length > 24) { _ctx.Status("Give the animation a name: 1-24 letters, digits, _ or -."); return; }
            _animName.Text = name;
            if (await _ctx.Try($"ANIM BEGIN {name} {_frames.Count}") == null) return;
            _upBar.Visible = true; _upBar.Maximum = _frames.Count; _upBar.Value = 0;
            try
            {
                for (int i = 0; i < _frames.Count; i++)
                {
                    if (await _ctx.Try($"ANIM FRAME {i} {_delays[i]} {_frames[i].ToHex()}") == null) return;
                    _upBar.Value = i + 1;
                }
                var r = await _ctx.Try("ANIM END");
                if (r == null) return;
                _ctx.Status($"Saved \"{name}\" on the sign ({_frames.Count} frames, {r.Int("fs_free") / 1024} KB left). Add it to the playlist, or set it as the startup/shutdown animation.");
                await RefreshBoardAnims();
                _boardAnims.SelectedItem = name;
            }
            finally { _upBar.Visible = false; }
        }

        async Task OpenFromSign()
        {
            if (!(_boardAnims.SelectedItem is string name)) return;
            var first = await _ctx.Try($"ANIM GET {name} 0");
            if (first == null) return;
            int n = first.Int("frames");
            var frames = new List<Frame> { Frame.FromHex(first.Str("hex")) };
            var delays = new List<int> { first.Int("delay", 100) };
            for (int i = 1; i < n; i++)
            {
                var r = await _ctx.Try($"ANIM GET {name} {i}");
                if (r == null) return;
                frames.Add(Frame.FromHex(r.Str("hex"))); delays.Add(r.Int("delay", 100));
            }
            _frames.Clear(); _frames.AddRange(frames); _delays.Clear(); _delays.AddRange(delays);
            _animName.Text = name;
            GoFrame(0);
            _ctx.Status($"Opened \"{name}\" ({n} frames). Edit it, then Upload to sign to save it back.");
        }

        /// <summary>Reads the HTML editor's project format too: any non-black pixel counts as lit.</summary>
        void LoadProjectJson(string json)
        {
            var r = Json.Parse(json);
            int w = r.Int("width"), h = r.Int("height");
            var f = new Frame();
            if (r.D.TryGetValue("pixels", out var o) && o is System.Collections.IEnumerable list)
            {
                int i = 0;
                foreach (var item in list)
                {
                    int x = w > 0 ? i % w : i % Frame.W, y = w > 0 ? i / w : i / Frame.W;
                    string s = Convert.ToString(item)?.Trim().TrimStart('#') ?? "";
                    bool on = s.Length >= 6 && int.TryParse(s.Substring(0, 6), System.Globalization.NumberStyles.HexNumber, null, out int rgb) && rgb != 0;
                    if (!on && item is int n) on = n != 0;
                    f[x, y] = on; i++;
                }
            }
            SetCurrent(f);
            LoadExtraFrames(r);
        }

        void OpenProject()
        {
            using var d = new OpenFileDialog { Filter = "Pixel art project (*.json)|*.json" };
            if (d.ShowDialog() != DialogResult.OK) return;
            try { PushUndo(); LoadProjectJson(File.ReadAllText(d.FileName)); Changed(); }
            catch (Exception ex) { MessageBox.Show("Couldn't read that project: " + ex.Message); }
        }

        void SaveProject()
        {
            using var d = new SaveFileDialog { Filter = "Pixel art project (*.json)|*.json", FileName = "sign-art.json" };
            if (d.ShowDialog() == DialogResult.OK) File.WriteAllText(d.FileName, ProjectJson());
        }

        void SavePng(int scale)
        {
            using var d = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = scale == 1 ? "sign-art.png" : "sign-art-10x.png" };
            if (d.ShowDialog() != DialogResult.OK) return;
            using var bmp = new Bitmap(Frame.W * scale, Frame.H * scale);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                for (int y = 0; y < Frame.H; y++) for (int x = 0; x < Frame.W; x++)
                        if (_canvas.Frame[x, y]) g.FillRectangle(Brushes.White, x * scale, y * scale, scale, scale);
            }
            bmp.Save(d.FileName, ImageFormat.Png);
        }

        string CName() { var n = new string(_cName.Text.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray()); return n.Length == 0 || char.IsDigit(n[0]) ? "sign_art" : n; }

        string CExport()
        {
            string n = CName(), N = n.ToUpperInvariant();
            var b = _canvas.Frame.ToBytes();
            var sb = new StringBuilder();
            sb.AppendLine($"// {n}: {Frame.W}x{Frame.H} 1-bit bitmap, row-major, MSB = leftmost pixel.");
            sb.AppendLine("// Same layout as Adafruit GFX drawBitmap() and the casino sign's BITMAP command.");
            sb.AppendLine("#pragma once");
            sb.AppendLine("#include <stdint.h>");
            if (_progmem.Checked) sb.AppendLine("#ifdef ARDUINO\n#include <pgmspace.h>\n#else\n#define PROGMEM\n#endif");
            sb.AppendLine($"#define {N}_WIDTH {Frame.W}");
            sb.AppendLine($"#define {N}_HEIGHT {Frame.H}");
            sb.AppendLine($"const uint8_t {n}[{b.Length}]{(_progmem.Checked ? " PROGMEM" : "")} = {{");
            for (int y = 0; y < Frame.H; y++)
            {
                sb.Append("  ");
                for (int i = 0; i < Frame.W / 8; i++) sb.Append($"0x{b[y * (Frame.W / 8) + i]:X2}, ");
                sb.AppendLine($"// row {y}");
            }
            sb.AppendLine("};");
            return sb.ToString();
        }

        void SaveHeader()
        {
            using var d = new SaveFileDialog { Filter = "C header (*.h)|*.h", FileName = CName() + ".h" };
            if (d.ShowDialog() == DialogResult.OK) File.WriteAllText(d.FileName, CExport());
        }

        async Task SendToSign()
        {
            if (!_ctx.Board.Connected) { _ctx.Status("Connect to the board first (top bar)."); return; }
            if (_ctx.Mode == "diag" && !await _ctx.SetMode("run")) return;
            var r = await _ctx.Try("BITMAP " + _canvas.Frame.ToHex());
            if (r != null) _ctx.Status("Picture sent to the sign (saved on the board, comes back after a power cut).");
        }

        async Task GetFromSign()
        {
            var r = await _ctx.Try("FB");
            if (r == null) return;
            PushUndo();
            SetCurrent(Frame.FromHex(r.Str("fb")));
            Changed();
        }
    }
}
