using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CasinoSignTool
{
    /// <summary>The sign's 1-bit 96x16 picture. Same layout as the firmware: rows top to bottom, 12 bytes per row, MSB = leftmost pixel.</summary>
    public class Frame
    {
        public const int W = 96, H = 16;
        public bool[] Px = new bool[W * H];
        public bool this[int x, int y] { get => x >= 0 && y >= 0 && x < W && y < H && Px[y * W + x]; set { if (x >= 0 && y >= 0 && x < W && y < H) Px[y * W + x] = value; } }
        public Frame Clone() => new Frame { Px = (bool[])Px.Clone() };

        public byte[] ToBytes()
        {
            var b = new byte[W * H / 8];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (this[x, y]) b[y * (W / 8) + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            return b;
        }
        public string ToHex() { var sb = new System.Text.StringBuilder(); foreach (var x in ToBytes()) sb.Append(x.ToString("x2")); return sb.ToString(); }
        public static Frame FromHex(string hex)
        {
            var f = new Frame();
            if (hex == null || hex.Length != W * H / 4) return f;
            for (int i = 0; i < W * H / 8; i++)
            {
                byte v = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                int y = i / (W / 8), x0 = (i % (W / 8)) * 8;
                for (int k = 0; k < 8; k++) f[x0 + k, y] = (v & (0x80 >> k)) != 0;
            }
            return f;
        }
    }

    /// <summary>Draws a Frame as LED dots.</summary>
    public class LedView : Control
    {
        public Frame Frame = new Frame();
        public Color On = Color.FromArgb(255, 70, 40), Off = Color.FromArgb(48, 18, 14);
        public bool Dots = true, Guides;
        public LedView() { DoubleBuffered = true; ResizeRedraw = true; BackColor = Color.Black; }

        public float Cell => Math.Max(1f, Math.Min((Width - 8f) / Frame.W, (Height - 8f) / Frame.H));
        public PointF Origin { get { float c = Cell; return new PointF((Width - c * Frame.W) / 2, (Height - c * Frame.H) / 2); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float c = Cell; var o = Origin;
            g.SmoothingMode = Dots && c >= 4 ? SmoothingMode.AntiAlias : SmoothingMode.None;
            using var on = new SolidBrush(On); using var off = new SolidBrush(Off);
            for (int y = 0; y < Frame.H; y++)
                for (int x = 0; x < Frame.W; x++)
                {
                    var b = Frame[x, y] ? on : off;
                    if (Dots && c >= 4) g.FillEllipse(b, o.X + x * c + c * 0.1f, o.Y + y * c + c * 0.1f, c * 0.8f, c * 0.8f);
                    else g.FillRectangle(b, o.X + x * c, o.Y + y * c, c - (c >= 4 ? 1 : 0), c - (c >= 4 ? 1 : 0));
                }
            if (Guides)
                using (var p = new Pen(Color.FromArgb(90, 80, 160, 255)) { DashStyle = DashStyle.Dash })
                {
                    for (int x = 16; x < Frame.W; x += 16) g.DrawLine(p, o.X + x * c, o.Y, o.X + x * c, o.Y + Frame.H * c);
                    g.DrawLine(p, o.X, o.Y + 8 * c, o.X + Frame.W * c, o.Y + 8 * c);
                }
        }
    }

    public class DisplayTab : UserControl
    {
        readonly Ctx _ctx;
        TextBox _text;
        TrackBar _speed, _bright;
        CheckBox _live;
        readonly LedView _preview = new LedView { Height = 120, Dock = DockStyle.Fill };
        readonly Timer _poll = new Timer { Interval = 250 };
        Label _previewInfo;
        bool _polling;

        public DisplayTab(Ctx ctx)
        {
            _ctx = ctx;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var top = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            root.Controls.Add(top, 0, 0);
            Label H(string t) => new Label { Text = t, AutoSize = true, Font = new Font(Font.FontFamily, 10f, FontStyle.Bold), Margin = new Padding(0, 10, 0, 4) };

            top.Controls.Add(H("Scrolling text"));
            var trow = new FlowLayoutPanel { AutoSize = true };
            _text = new TextBox { Width = 560, Font = new Font(Font.FontFamily, 12f), MaxLength = 200, Text = "WELCOME" };
            var send = new Button { Text = "Show on sign", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            send.Click += async (s, e) => await SendText();
            _text.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SendText(); } };
            trow.Controls.Add(_text); trow.Controls.Add(send);
            top.Controls.Add(trow);

            var sliders = new FlowLayoutPanel { AutoSize = true };
            sliders.Controls.Add(new Label { Text = "Scroll speed:", AutoSize = true, Margin = new Padding(0, 8, 2, 0) });
            _speed = new TrackBar { Minimum = 1, Maximum = 20, Value = 10, Width = 200, TickFrequency = 2 };
            _speed.MouseUp += async (s, e) => await _ctx.Try("SPEED " + SpeedMs());
            _speed.KeyUp += async (s, e) => await _ctx.Try("SPEED " + SpeedMs());
            sliders.Controls.Add(new Label { Text = "slow", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
            sliders.Controls.Add(_speed);
            sliders.Controls.Add(new Label { Text = "fast", AutoSize = true, Margin = new Padding(0, 8, 20, 0) });
            sliders.Controls.Add(new Label { Text = "Brightness:", AutoSize = true, Margin = new Padding(0, 8, 2, 0) });
            _bright = new TrackBar { Minimum = 5, Maximum = 100, Value = 100, Width = 200, TickFrequency = 10 };
            _bright.MouseUp += async (s, e) => await SetBright();
            _bright.KeyUp += async (s, e) => await SetBright();
            sliders.Controls.Add(_bright);
            top.Controls.Add(sliders);

            top.Controls.Add(H("Show"));
            var pats = new FlowLayoutPanel { AutoSize = true };
            void Pat(string label, string name)
            {
                var b = new Button { Text = label, AutoSize = true };
                b.Click += async (s, e) =>
                {
                    if (_ctx.Mode == "diag" && !await _ctx.SetMode("run")) return;
                    await _ctx.Try("PATTERN " + name);
                };
                pats.Controls.Add(b);
            }
            Pat("Scrolling text", "text"); Pat("Last bitmap from pixel art", "bitmap"); Pat("Corners test", "corners");
            Pat("Walk row", "walkrow"); Pat("Walk column", "walkcol"); Pat("Blank", "off");
            top.Controls.Add(pats);

            top.Controls.Add(H("What the board is sending to the sign"));
            var prow = new FlowLayoutPanel { AutoSize = true };
            _live = new CheckBox { Text = "Live preview (reads the board's framebuffer 4x a second)", AutoSize = true };
            _live.CheckedChanged += (s, e) => { if (_live.Checked) _poll.Start(); else _poll.Stop(); };
            _previewInfo = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(10, 4, 0, 0) };
            prow.Controls.Add(_live); prow.Controls.Add(_previewInfo);
            top.Controls.Add(prow);

            root.Controls.Add(_preview, 0, 1);

            _poll.Tick += async (s, e) => await Poll();
            _ctx.CfgChanged += () => { _bright.Value = Math.Max(5, Math.Min(100, _ctx.Cfg.Brightness)); };
            VisibleChanged += (s, e) => { if (!Visible) { _live.Checked = false; } };
        }

        int SpeedMs() => (int)Math.Round(5 + (20 - _speed.Value) * 9.5);   // 5..195 ms per pixel

        async Task SetBright()
        {
            var r = await _ctx.Try("SET brightness " + _bright.Value);
            if (r != null) { _ctx.Cfg.Brightness = _bright.Value; _ctx.Status($"Brightness {_bright.Value}% (press Save in Sign setup to keep it after a reboot)."); }
        }

        async Task SendText()
        {
            if (_ctx.Mode == "diag" && !await _ctx.SetMode("run")) return;
            var r = await _ctx.Try("TEXT " + _text.Text.Replace("\r", " ").Replace("\n", " "));
            if (r != null) { await _ctx.Try("SPEED " + SpeedMs()); _ctx.Status("Text sent. It's saved on the board, so it comes back after a power cut."); }
        }

        async Task Poll()
        {
            if (_polling || !_ctx.Board.Connected) return;
            _polling = true;
            try
            {
                var r = await _ctx.Board.Send("FB", 2000, false);
                if (r.Ok)
                {
                    _preview.Frame = Frame.FromHex(r.Str("fb"));
                    _preview.Invalidate();
                    _previewInfo.Text = $"pattern: {r.Str("pattern")}   scanning: {(r.Bool("scanning") ? "yes" : "no (Diagnostics mode or pins not set)")}";
                }
            }
            catch { }
            finally { _polling = false; }
        }
    }
}
