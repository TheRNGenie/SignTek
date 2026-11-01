using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CasinoSignTool
{
    /// <summary>Drawing of the 2x25 ribbon header. Odd pins on the top row, even on the bottom (standard IDC numbering).</summary>
    public class HeaderView : Control
    {
        public HeaderMap Map;
        public int Selected = 1;
        public int Active = -1;              // pin currently being probed/sniffed (pulses)
        public event Action<int> PinClicked;
        int _hover = -1;
        readonly Timer _blink = new Timer { Interval = 250 };
        bool _blinkOn;
        readonly ToolTip _tip = new ToolTip();

        public HeaderView()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.FromArgb(24, 26, 30);
            _blink.Tick += (s, e) => { _blinkOn = !_blinkOn; if (Active > 0) Invalidate(); };
            _blink.Start();
        }

        int Cols => (Map.PinCount + 1) / 2;

        RectangleF Body()
        {
            float w = Width - 40, h = Math.Min(Height - 70, w / Cols * 2.6f);
            return new RectangleF(20, (Height - h) / 2 + 10, w, h);
        }

        RectangleF PinRect(int n)
        {
            var b = Body();
            int col = (n - 1) / 2;
            int row = (n - 1) % 2;            // 0 = top (odd), 1 = bottom (even)
            if (Map.FlipView) col = Cols - 1 - col;
            float cw = b.Width / Cols, rh = b.Height / 2;
            float d = Math.Min(cw, rh) * 0.62f;
            float cx = b.Left + cw * (col + 0.5f), cy = b.Top + rh * (row + 0.5f);
            return new RectangleF(cx - d / 2, cy - d / 2, d, d);
        }

        int HitTest(Point p)
        {
            for (int n = 1; n <= Map.PinCount; n++)
            {
                var r = PinRect(n); r.Inflate(r.Width * 0.3f, r.Height * 0.3f);
                if (r.Contains(p)) return n;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != _hover)
            {
                _hover = h; Invalidate();
                if (h > 0)
                {
                    var p = Map[h];
                    string tip = $"Pin {h}: {Roles.Label(p.R)}" + (p.Note.Length > 0 ? "\n" + p.Note : "") + (p.LastSniff.Length > 0 ? "\nLast sniff: " + p.LastSniff : "");
                    _tip.SetToolTip(this, tip);
                }
                else _tip.SetToolTip(this, "");
            }
            Cursor = h > 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int h = HitTest(e.Location);
            if (h > 0) PinClicked?.Invoke(h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (Map == null) return;
            var b = Body();

            // Connector shroud with the polarity key in the middle of the top edge.
            var shroud = RectangleF.Inflate(b, 10, 10);
            using (var br = new SolidBrush(Color.FromArgb(48, 50, 56))) g.FillRectangle(br, shroud);
            using (var pen = new Pen(Color.FromArgb(110, 110, 120), 2)) g.DrawRectangle(pen, shroud.X, shroud.Y, shroud.Width, shroud.Height);
            var key = new RectangleF(shroud.Left + shroud.Width / 2 - 30, shroud.Top - 2, 60, 9);
            using (var br = new SolidBrush(BackColor)) g.FillRectangle(br, key);

            float cw = b.Width / Cols;
            using var numFont = new Font("Segoe UI", Math.Max(6.5f, Math.Min(9f, cw / 4.2f)));
            using var roleFont = new Font("Segoe UI", Math.Max(6f, Math.Min(8.5f, cw / 4.6f)), FontStyle.Bold);
            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            for (int n = 1; n <= Map.PinCount; n++)
            {
                var r = PinRect(n);
                var pin = Map[n];
                var col = Roles.Color(pin.R);
                bool active = n == Active;
                if (active && _blinkOn) col = Color.White;
                using (var br = new SolidBrush(col)) g.FillEllipse(br, r);
                var border = n == Selected ? Color.Yellow : n == _hover ? Color.White : Color.FromArgb(140, 140, 150);
                using (var pen = new Pen(border, n == Selected ? 3f : 1.2f)) g.DrawEllipse(pen, r);
                if (pin.R == PinRole.Unknown)
                    using (var pen = new Pen(Color.FromArgb(70, 70, 76), 1)) { g.DrawLine(pen, r.Left + 4, r.Top + 4, r.Right - 4, r.Bottom - 4); }
                if (n == 1)
                {
                    // Pin 1 marker: triangle next to it.
                    float tx = Map.FlipView ? r.Right + 6 : r.Left - 14, ty = r.Top - 6;
                    var tri = Map.FlipView
                        ? new[] { new PointF(tx + 8, ty), new PointF(tx, ty + 6), new PointF(tx + 8, ty + 12) }
                        : new[] { new PointF(tx, ty), new PointF(tx + 8, ty + 6), new PointF(tx, ty + 12) };
                    g.FillPolygon(Brushes.Orange, tri);
                }
                // Number outside the connector, role code inside the pin.
                bool top = (n - 1) % 2 == 0;
                var numRect = new RectangleF(r.Left - 10, top ? b.Top - 34 : b.Bottom + 14, r.Width + 20, 18);
                g.DrawString(n.ToString(), numFont, n == Selected ? Brushes.Yellow : Brushes.Gainsboro, numRect, center);
                if (pin.R != PinRole.Unknown)
                {
                    var txt = col.GetBrightness() > 0.55f || active ? Brushes.Black : Brushes.White;
                    g.DrawString(Roles.Code(pin.R), roleFont, txt, r, center);
                }
            }

            string caption = Map.FlipView ? "View: mirrored (looking at the other side of the connector)" : "View: looking into the connector, key notch up, pin 1 marked ▶";
            g.DrawString(caption, numFont, Brushes.Gray, 8, 4);
        }
    }

    public class MapperTab : UserControl
    {
        readonly Ctx _ctx;
        readonly HeaderView _view = new HeaderView();
        Label _pinTitle, _progress, _sniffOut, _probeState, _problems;
        ComboBox _role, _probeGpio, _sniffGpio;
        TextBox _note;
        NumericUpDown _timeout;
        Button _useSuggestion;
        PinRole _suggested = PinRole.Unknown;
        bool _loading;
        int _probing = -1;   // GPIO currently driven
        readonly Timer _probeTimer = new Timer { Interval = 500 };
        DateTime _probeUntil;

        public MapperTab(Ctx ctx)
        {
            _ctx = ctx;
            _view.Map = ctx.Map;
            _view.Dock = DockStyle.Fill;
            _view.PinClicked += SelectPin;
            _view.KeyDown += OnKey;
            _view.PreviewKeyDown += (s, e) => { if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) e.IsInputKey = true; };

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
            Controls.Add(root);

            // Left: steps on top, connector drawing, legend + file buttons at the bottom.
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(left, 0, 0);

            var steps = new Label
            {
                AutoSize = true, MaximumSize = new Size(760, 0), Padding = new Padding(4),
                Text = "How to map the header (click a pin, then):\n" +
                       "  Step 1, power OFF, multimeter: beep each pin to the chassis (G) and the power rail (V, 3.3 V on the V-002), then to the driver chip legs: MBI5170 pin 2 = DATA, 3 = CLK, 4 = LAT, 13 = OE (? Help has the full steps).\n" +
                       "  Step 2, safest, CPU-002 still plugged in and running: clip the sniff lead onto a pin and press Sniff. 3.3 V controller (V-002 measures 3.37 V): lead = one 10k to IO39, nothing to ground. 5 V controller: 10k/20k divider (Show breadboard picture). Test the lead first on a pin at full voltage: it must read about 100% high.\n" +
                       "  Step 3, only if needed, CPU-002 UNPLUGGED: touch the probe lead (through a 1k resistor) to a pin and blink it. Watch the sign to see what reacts.\n" +
                       "Arrow keys move between pins. The map saves itself; use \"Send map to board\" to keep a copy on the ESP32.",
            };
            left.Controls.Add(steps, 0, 0);
            left.Controls.Add(_view, 0, 1);

            var legend = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(2) };
            foreach (var r in Roles.All)
            {
                var sw = new Label { Width = 14, Height = 14, BackColor = Roles.Color(r), Margin = new Padding(6, 4, 2, 0) };
                legend.Controls.Add(sw);
                legend.Controls.Add(new Label { Text = Roles.Code(r), AutoSize = true, Margin = new Padding(0, 3, 4, 0) });
            }
            left.Controls.Add(legend, 0, 2);

            var files = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            Button B(string t, EventHandler h) { var b = new Button { Text = t, AutoSize = true }; b.Click += h; files.Controls.Add(b); return b; }
            B("Save map…", (s, e) => SaveMap());
            B("Open map…", (s, e) => OpenMap());
            B("Send map to board", async (s, e) => { var r = await _ctx.Try("HDR SET " + _ctx.Map.ToCompact()); if (r != null) _ctx.Status("Header map saved on the board."); });
            B("Load map from board", async (s, e) => await LoadFromBoard());
            B("Flip view", (s, e) => { _ctx.Map.FlipView = !_ctx.Map.FlipView; _ctx.RaiseMap(); _view.Invalidate(); });
            B("Clear all…", (s, e) =>
            {
                if (MessageBox.Show("Forget every pin role in this map?", "Clear map", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                foreach (var p in _ctx.Map.Pins) { p.Role = "?"; p.Note = ""; p.LastSniff = ""; p.SniffDuty = -1; p.SniffHz = 0; }
                _ctx.RaiseMap(); SelectPin(1);
            });
            _progress = new Label { AutoSize = true, Margin = new Padding(12, 8, 0, 0), Font = new Font(Font, FontStyle.Bold) };
            files.Controls.Add(_progress);
            left.Controls.Add(files, 0, 3);

            root.Controls.Add(BuildSide(), 1, 0);

            _probeTimer.Tick += (s, e) => UpdateProbeState();
            _ctx.MapChanged += () => { _view.Map = _ctx.Map; _view.Invalidate(); UpdateProgress(); };
            _ctx.StateChanged += () => { FillGpioLists(); UpdateProbeState(); };
            FillGpioLists();
            SelectPin(1);
        }

        Control BuildSide()
        {
            var side = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(6, 0, 0, 0) };
            int W = 320;
            Label H(string t) => new Label { Text = t, AutoSize = true, Font = new Font(Font.FontFamily, 10f, FontStyle.Bold), Margin = new Padding(0, 10, 0, 2) };
            Label P(string t) => new Label { Text = t, AutoSize = true, MaximumSize = new Size(W, 0), ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 4) };

            _pinTitle = new Label { AutoSize = true, Font = new Font(Font.FontFamily, 15f, FontStyle.Bold) };
            side.Controls.Add(_pinTitle);

            var nav = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
            Button N(string t, Action a) { var b = new Button { Text = t, AutoSize = true }; b.Click += (s, e) => a(); nav.Controls.Add(b); return b; }
            N("◀ Prev", () => SelectPin(_view.Selected > 1 ? _view.Selected - 1 : _ctx.Map.PinCount));
            N("Next ▶", () => SelectPin(_view.Selected < _ctx.Map.PinCount ? _view.Selected + 1 : 1));
            N("Next unknown", NextUnknown);
            side.Controls.Add(nav);

            side.Controls.Add(H("What this pin is"));
            _role = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = W };
            foreach (var r in Roles.All) _role.Items.Add(Roles.Label(r));
            _role.SelectedIndexChanged += (s, e) => { if (_loading) return; SetRole((PinRole)_role.SelectedIndex, false); };
            side.Controls.Add(_role);
            var quick = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
            foreach (var r in new[] { PinRole.GND, PinRole.V5, PinRole.NC, PinRole.Clk, PinRole.DataTop, PinRole.Lat, PinRole.Oe, PinRole.A, PinRole.B, PinRole.C, PinRole.D })
            {
                var b = new Button { Text = Roles.Code(r), Width = 44, Height = 26, BackColor = Roles.Color(r), ForeColor = Roles.Color(r).GetBrightness() > 0.55f ? Color.Black : Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(1) };
                var role = r;
                b.Click += (s, e) => SetRole(role, false);
                quick.Controls.Add(b);
            }
            quick.MaximumSize = new Size(W, 0);
            side.Controls.Add(quick);
            side.Controls.Add(new Label { Text = "Notes:", AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
            _note = new TextBox { Width = W };
            _note.TextChanged += (s, e) => { if (_loading) return; _ctx.Map[_view.Selected].Note = _note.Text; _ctx.RaiseMap(); };
            side.Controls.Add(_note);

            // ---- Sniff (passive) ----
            side.Controls.Add(H("Sniff the pin (CPU-002 running)"));
            side.Controls.Add(P("3.3 V controller (the V-002 measures 3.37 V): header pin → one 10k → ESP32 GPIO 39 (IO connector), nothing to ground. 5 V controller: add a 20k from GPIO 39 to GND. Always join the grounds."));
            var pic33 = new Button { Text = "Show whole setup (3.3 V, V-002)", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            pic33.Click += (s, e) => ShowPicture("sniff-setup-3v3.png", "Sniff setup for a 3.3 V controller: both boards");
            side.Controls.Add(pic33);
            var pic = new Button { Text = "Show 5 V version (divider)", AutoSize = true };
            pic.Click += (s, e) => ShowPicture("sniff-lead.png", "Sniff lead for a 5 V controller: breadboard wiring");
            side.Controls.Add(pic);
            var back = new Button { Text = "Show where it plugs into the ESP32", AutoSize = true };
            back.Click += (s, e) => ShowPicture("esp32-back.png", "Back of the ESP32 board: listening-lead wires");
            side.Controls.Add(back);
            var srow = new FlowLayoutPanel { AutoSize = true };
            srow.Controls.Add(new Label { Text = "Sniff GPIO:", AutoSize = true, Margin = new Padding(0, 7, 2, 0) });
            _sniffGpio = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };
            _sniffGpio.SelectedIndexChanged += (s, e) => { if (_sniffGpio.SelectedItem != null) _ctx.Map.SniffGpio = _sniffGpio.SelectedItem.ToString(); };
            srow.Controls.Add(_sniffGpio);
            var sniffBtn = new Button { Text = "Sniff this pin", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            sniffBtn.Click += async (s, e) => await Sniff();
            srow.Controls.Add(sniffBtn);
            side.Controls.Add(srow);
            _sniffOut = new Label { AutoSize = true, MaximumSize = new Size(W, 0), Font = new Font("Consolas", 8.5f), Margin = new Padding(0, 2, 0, 2) };
            side.Controls.Add(_sniffOut);
            _useSuggestion = new Button { Text = "Use suggestion", AutoSize = true, Visible = false };
            _useSuggestion.Click += (s, e) => SetRole(_suggested, true);
            side.Controls.Add(_useSuggestion);

            // ---- Probe (active) ----
            side.Controls.Add(H("Probe the pin (CPU-002 unplugged)"));
            side.Controls.Add(P("Wire: ESP32 probe GPIO → 1k resistor → test lead. No 74HCT245 needed: the V-002 and its panels run at 3.3 V like the ESP32. Never probe pins marked GND or 3V3. Outputs switch off by themselves after the timeout."));
            var prow = new FlowLayoutPanel { AutoSize = true };
            prow.Controls.Add(new Label { Text = "Probe GPIO:", AutoSize = true, Margin = new Padding(0, 7, 2, 0) });
            _probeGpio = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };
            _probeGpio.SelectedIndexChanged += (s, e) => { if (_probeGpio.SelectedItem != null) _ctx.Map.ProbeGpio = _probeGpio.SelectedItem.ToString(); };
            prow.Controls.Add(_probeGpio);
            prow.Controls.Add(new Label { Text = "auto-off (s):", AutoSize = true, Margin = new Padding(6, 7, 2, 0) });
            _timeout = new NumericUpDown { Minimum = 1, Maximum = 120, Value = 10, Width = 50 };
            prow.Controls.Add(_timeout);
            side.Controls.Add(prow);
            var pbtns = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(W, 0) };
            Button PB(string t, string mode, double hz) { var b = new Button { Text = t, AutoSize = true }; b.Click += async (s, e) => await Probe(mode, hz); pbtns.Controls.Add(b); return b; }
            PB("Blink 2 Hz", "PULSE", 2);
            PB("Pulse 1 kHz", "PULSE", 1000);
            PB("Pulse 100 kHz", "PULSE", 100000);
            PB("Hold HIGH", "HIGH", 0);
            PB("Hold LOW", "LOW", 0);
            var stop = PB("■ Stop", "OFF", 0);
            stop.ForeColor = Color.DarkRed;
            side.Controls.Add(pbtns);
            _probeState = new Label { AutoSize = true, MaximumSize = new Size(W, 0), ForeColor = Color.DarkOrange, Font = new Font(Font, FontStyle.Bold) };
            side.Controls.Add(_probeState);
            side.Controls.Add(P("What to look for: CLK or DATA pulses make random dots or smears appear; LAT makes the pattern change in steps; OE makes the whole sign flash; an address line moves which row is lit."));

            side.Controls.Add(H("Still missing"));
            _problems = new Label { AutoSize = true, MaximumSize = new Size(W, 0) };
            side.Controls.Add(_problems);
            return side;
        }

        void FillGpioLists()
        {
            void Fill(ComboBox cb, IEnumerable<int> pins, string want)
            {
                cb.Items.Clear();
                foreach (var p in pins) cb.Items.Add(p.ToString());
                cb.SelectedItem = cb.Items.Contains(want) ? want : (cb.Items.Count > 0 ? cb.Items[0] : null);
            }
            Fill(_probeGpio, _ctx.OutPins.Where(p => !_ctx.StrapPins.Contains(p)).Concat(_ctx.OutPins.Where(p => _ctx.StrapPins.Contains(p))), _ctx.Map.ProbeGpio);
            Fill(_sniffGpio, _ctx.InPins.Where(p => p >= 34).Concat(_ctx.InPins.Where(p => p < 34)).Distinct(), _ctx.Map.SniffGpio);
        }

        void SelectPin(int n)
        {
            _loading = true;
            _view.Selected = n;
            var p = _ctx.Map[n];
            _pinTitle.Text = $"Pin {n}  ({((n % 2) == 1 ? "top" : "bottom")} row)";
            _role.SelectedIndex = (int)p.R;
            _note.Text = p.Note;
            _sniffOut.Text = p.LastSniff.Length > 0 ? "Last sniff: " + p.LastSniff : "";
            _useSuggestion.Visible = false;
            if (_probing >= 0) _view.Active = n;
            _loading = false;
            _view.Invalidate();
            UpdateProgress();
        }

        void SetRole(PinRole r, bool advance)
        {
            var p = _ctx.Map[_view.Selected];
            p.R = r;
            _loading = true; _role.SelectedIndex = (int)r; _loading = false;
            _ctx.RaiseMap();
            _ctx.Status($"Pin {p.Number} = {Roles.Label(r)}");
            if (advance) NextUnknown();
        }

        void NextUnknown()
        {
            int n = _view.Selected;
            for (int i = 1; i <= _ctx.Map.PinCount; i++)
            {
                int k = (n - 1 + i) % _ctx.Map.PinCount + 1;
                if (_ctx.Map[k].R == PinRole.Unknown) { SelectPin(k); return; }
            }
            _ctx.Status("Every pin has a role now. Check the Wiring guide tab.");
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Left: SelectPin(_view.Selected > 1 ? _view.Selected - 1 : _ctx.Map.PinCount); break;
                case Keys.Right: SelectPin(_view.Selected < _ctx.Map.PinCount ? _view.Selected + 1 : 1); break;
                case Keys.G: SetRole(PinRole.GND, true); break;
                case Keys.V: SetRole(PinRole.V5, true); break;
                case Keys.N: SetRole(PinRole.NC, true); break;
                case Keys.U: SetRole(PinRole.Unknown, false); break;
                case Keys.Space: _ = Sniff(); break;
                default: return;
            }
            e.Handled = true;
        }

        void UpdateProgress()
        {
            _progress.Text = $"{_ctx.Map.MappedCount} / {_ctx.Map.PinCount} pins mapped";
            var probs = _ctx.Map.Problems();
            _problems.Text = probs.Count == 0 ? "Nothing. The map is complete. Go to 2. Sign setup → \"Use header map\"." : "• " + string.Join("\n• ", probs);
        }

        async Task<bool> EnsureDiag()
        {
            if (!_ctx.Board.Connected) { _ctx.Status("Connect to the board first (top bar)."); return false; }
            if (_ctx.Mode == "diag") return true;
            if (MessageBox.Show("Probing needs Diagnostics mode. This stops the sign scan and releases all sign pins. Switch now?", "Diagnostics mode", MessageBoxButtons.OKCancel) != DialogResult.OK) return false;
            return await _ctx.SetMode("diag");
        }

        async Task Sniff()
        {
            if (!await EnsureDiag()) return;
            int gpio = int.Parse(_sniffGpio.SelectedItem?.ToString() ?? "39");
            int pinNo = _view.Selected;
            _view.Active = pinNo;
            _sniffOut.Text = "Sniffing…";
            var r = await _ctx.Try($"SNIFF {gpio} 200", 3000);
            _view.Active = _probing >= 0 ? _view.Selected : -1;
            if (r == null) { _sniffOut.Text = ""; return; }
            double hz = r.Dbl("hz"), duty = r.Dbl("high_pct");
            bool sat = r.Bool("saturated");
            var p = _ctx.Map[pinNo];
            string freq = sat ? $"> {Fmt(r.Dbl("sample_rate") / 8)}" : Fmt(hz);
            p.LastSniff = $"{freq}, high {duty:0}% of the time, {r.Int("edges")} edges";
            p.SniffHz = sat ? 1e9 : hz; p.SniffDuty = duty;
            _ctx.RaiseMap();
            (PinRole role, string why) = Classify(p, sat);
            _suggested = role;
            _sniffOut.Text = $"Frequency: {freq}\nHigh:      {duty:0.0}% of the time\nEdges:     {r.Int("edges")} in {r.Int("ms")} ms\n\nLooks like: {why}";
            _useSuggestion.Text = $"Mark pin {pinNo} as {Roles.Code(role)} and go to next";
            _useSuggestion.Visible = role != PinRole.Unknown;
        }

        static string Fmt(double hz) => hz >= 1e6 ? $"{hz / 1e6:0.##} MHz" : hz >= 1e3 ? $"{hz / 1e3:0.##} kHz" : $"{hz:0.#} Hz";

        /// <summary>Guess a role from activity. Address lines are ranked against other sniffed pins.</summary>
        (PinRole, string) Classify(HeaderPin p, bool saturated)
        {
            double hz = p.SniffHz, duty = p.SniffDuty;
            if (saturated || hz > 300_000) return (PinRole.Clk, "CLK. It is the fastest signal on the bus.");
            if (hz < 0.5)
            {
                if (duty > 50) return (PinRole.Unknown, "steady HIGH. Could be a 3.3V power pin (check with the meter), or an idle line that's held high.");
                return (PinRole.Unknown, "steady LOW. Could be GND, a line that isn't used, or nothing connected. Check with the meter.");
            }
            if (duty >= 35 && duty <= 65 && hz < 20_000)
            {
                // Square wave: a row address line. Rank by frequency among the other square-wave pins.
                var squares = _ctx.Map.Pins.Where(x => x.SniffDuty >= 35 && x.SniffDuty <= 65 && x.SniffHz > 0.5 && x.SniffHz < 20_000)
                                           .OrderByDescending(x => x.SniffHz).ToList();
                int rank = squares.FindIndex(x => x.Number == p.Number);
                // Each address line runs at half the speed of the one before it.
                double fastest = squares.Count > 0 ? squares[0].SniffHz : hz;
                int bit = (int)Math.Round(Math.Log(fastest / hz, 2));
                bit = Math.Max(0, Math.Min(3, bit));
                var role = new[] { PinRole.A, PinRole.B, PinRole.C, PinRole.D }[bit];
                return (role, $"a row address line (even square wave). Sniffed address-like pins so far: {squares.Count}. " +
                              $"This one is {(bit == 0 ? "the fastest, so A" : $"{Math.Pow(2, bit):0}x slower than the fastest, so {Roles.Code(role)}")}. Sniff all of them for a reliable ranking.");
            }
            if (duty < 6) return (PinRole.Lat, "LAT. Very short high pulses, about once per row.");
            if (duty > 94) return (PinRole.Oe, "OE (or an active-low latch). Mostly high with short low pulses. Try blinking it with the CPU-002 unplugged to be sure.");
            if (duty < 35 && hz < 50_000) return (PinRole.Oe, "OE. Short blanking pulses around each row change.");
            if (hz > 20_000) return (PinRole.DataTop, "DATA. It changes fast and irregularly. If you find two of these, one is DATA 2 (bottom half).");
            return (PinRole.Other, "active, but doesn't match a known pattern. Mark it Other, or sniff again.");
        }

        async Task Probe(string mode, double hz)
        {
            if (mode != "OFF" && !await EnsureDiag()) return;
            int gpio = int.Parse(_probeGpio.SelectedItem?.ToString() ?? "4");
            var pin = _ctx.Map[_view.Selected];
            if (mode != "OFF" && (pin.R == PinRole.GND || pin.R == PinRole.V5))
            {
                if (MessageBox.Show($"Pin {pin.Number} is marked {Roles.Code(pin.R)}. Driving a power pin can damage the ESP32 or the level shifter. Probe it anyway?", "Careful", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            if (mode != "OFF" && _ctx.StrapPins.Contains(gpio)) _ctx.Status($"Note: GPIO {gpio} is a boot strapping pin; fine for probing, but avoid it for the final wiring if you can.");
            if (mode == "OFF")
            {
                await _ctx.Try("ALLOFF");
                _probing = -1; _view.Active = -1; _view.Invalidate(); UpdateProbeState();
                return;
            }
            int ms = (int)_timeout.Value * 1000;
            string cmd = mode == "PULSE" ? $"PROBE {gpio} PULSE {hz} {ms}" : $"PROBE {gpio} {mode} 0 {ms}";
            var r = await _ctx.Try(cmd);
            if (r == null) return;
            _probing = gpio;
            _probeUntil = DateTime.Now.AddMilliseconds(ms);
            _view.Active = _view.Selected;
            _probeTimer.Start();
            UpdateProbeState(mode == "PULSE" ? $"pulsing {Fmt(hz)}" : $"held {mode}");
        }

        string _probeDesc = "";
        void UpdateProbeState(string desc = null)
        {
            if (desc != null) _probeDesc = desc;
            if (_probing < 0 || !_ctx.Board.Connected || DateTime.Now > _probeUntil)
            {
                if (_probing >= 0) { _probing = -1; _view.Active = -1; _view.Invalidate(); }
                _probeTimer.Stop();
                _probeState.Text = "";
                return;
            }
            _probeState.Text = $"● GPIO {_probing} {_probeDesc}, stops in {(int)(_probeUntil - DateTime.Now).TotalSeconds + 1} s";
        }

        void SaveMap()
        {
            using var d = new SaveFileDialog { Filter = "Header map (*.json)|*.json", FileName = "sign-header-map.json" };
            if (d.ShowDialog() == DialogResult.OK) { _ctx.Map.Save(d.FileName); _ctx.Status("Saved " + d.FileName); }
        }

        void OpenMap()
        {
            using var d = new OpenFileDialog { Filter = "Header map (*.json)|*.json" };
            if (d.ShowDialog() != DialogResult.OK) return;
            try { _ctx.Map = HeaderMap.Load(d.FileName); _ctx.RaiseMap(); SelectPin(1); }
            catch (Exception ex) { MessageBox.Show("Couldn't read that file: " + ex.Message); }
        }

        async Task LoadFromBoard()
        {
            var r = await _ctx.Try("HDR GET");
            if (r == null) return;
            if (r.Str("hdr").Length == 0) { _ctx.Status("The board has no header map saved yet."); return; }
            if (MessageBox.Show("Replace the map on screen with the one stored on the board? (Notes and sniff results aren't stored on the board.)", "Load map", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            var m = HeaderMap.FromCompact(r.Str("hdr"));
            m.ProbeGpio = _ctx.Map.ProbeGpio; m.SniffGpio = _ctx.Map.SniffGpio; m.FlipView = _ctx.Map.FlipView;
            _ctx.Map = m; _ctx.RaiseMap(); SelectPin(1);
        }
        void ShowPicture(string res, string title)
        {
            using var st = typeof(MapperTab).Assembly.GetManifestResourceStream(res);
            if (st == null) return;
            Image img; using (var tmp = Image.FromStream(st)) img = new Bitmap(tmp);
            var frm = new Form { Text = title, ClientSize = new Size(Math.Min(img.Width, 1300), Math.Min(img.Height, 840)), StartPosition = FormStartPosition.CenterParent };
            frm.Controls.Add(new PictureBox { Dock = DockStyle.Fill, Image = img, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White });
            frm.Show(FindForm());
        }
    }
}
