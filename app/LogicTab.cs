using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CasinoSignTool
{
    /// <summary>
    /// Turns the ESP32 into an 8-channel logic analyzer (firmware CAPTURE command), shows the
    /// waveforms and works out which header pin is CLK, LAT, OE, DATA and the row lines.
    /// </summary>
    public class LogicTab : UserControl
    {
        public static readonly int[] Gpios = { 35, 39, 25, 32, 5, 18, 19, 23 };
        public static readonly string[] Where =
        {
            "IO plug (the pin your sniff wire is on)", "IO plug (other pin)", "I2C plug, pin next to GND", "I2C plug, pin next to 3V3",
            "SPI plug, 1st pin", "SPI plug, 2nd pin", "SPI plug, 3rd pin", "SPI plug, 4th pin",
        };
        static readonly Color[] TraceColors =
        {
            Color.FromArgb(230, 170, 0), Color.FromArgb(60, 140, 240), Color.FromArgb(220, 60, 60), Color.FromArgb(40, 170, 80),
            Color.FromArgb(170, 90, 230), Color.FromArgb(0, 170, 170), Color.FromArgb(230, 110, 30), Color.FromArgb(120, 120, 120),
        };

        readonly Ctx _ctx;
        readonly NumericUpDown[] _hdr = new NumericUpDown[8];
        readonly Label[] _chResult = new Label[8];
        ComboBox _rate, _trig;
        Button _capture, _apply;
        Label _state;
        TextBox _report;
        WavePanel _wave;
        byte[] _data;
        double _sampleRate;
        Analysis _last;

        public LogicTab(Ctx ctx)
        {
            _ctx = ctx;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 470));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            root.Controls.Add(left, 0, 0);
            Label H(string t) => new Label { Text = t, AutoSize = true, Font = new Font(Font.FontFamily, 10f, FontStyle.Bold), Margin = new Padding(0, 8, 0, 3) };
            Label P(string t) => new Label { Text = t, AutoSize = true, MaximumSize = new Size(445, 0), Margin = new Padding(0, 1, 0, 3) };

            left.Controls.Add(H("Use the ESP32 as an 8-channel logic analyzer"));
            left.Controls.Add(P(
                "It records 8 controller pins at the same moment and works out which one is CLK, LAT, OE, DATA and the row lines A, B, C.\n\n" +
                "1. Wire each channel: controller pin → 10 kΩ resistor → the ESP32 pin listed below. Plus one wire from ESP32 GND (I2C plug) to a controller GND pin. (3.3 V controllers like the V-002 only; a 5 V controller needs the 10k/20k divider on every channel.)\n" +
                "2. Type which header pin each channel is clipped to (0 = not used).\n" +
                "3. Power the sign normally (V-002 running). Connect the ESP32 by USB, press Capture. It takes about 6 seconds.\n" +
                "4. Read the results, then press Put results in the pin map.\n\n" +
                "Tip: first clip 8 of the busy pins from your sniff map. Once CLK and LAT are found, keep those two on channels 1 and 2 and move the other 6 clips to new pins each time, so every capture can be compared against the clock."));

            var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, Margin = new Padding(0, 4, 0, 4) };
            grid.Controls.Add(new Label { Text = "Ch", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
            grid.Controls.Add(new Label { Text = "ESP32 pin (where)", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 1, 0);
            grid.Controls.Add(new Label { Text = "Header pin", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 2, 0);
            grid.Controls.Add(new Label { Text = "Result", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 3, 0);
            for (int i = 0; i < 8; i++)
            {
                grid.Controls.Add(new Label { Text = (i + 1).ToString(), AutoSize = true, ForeColor = TraceColors[i], Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 6, 3, 0) }, 0, i + 1);
                grid.Controls.Add(new Label { Text = $"GPIO {Gpios[i]} ({Where[i]})", AutoSize = true, MaximumSize = new Size(210, 0), Margin = new Padding(3, 6, 3, 0) }, 1, i + 1);
                _hdr[i] = new NumericUpDown { Minimum = 0, Maximum = 50, Width = 52 };
                grid.Controls.Add(_hdr[i], 2, i + 1);
                _chResult[i] = new Label { AutoSize = true, Margin = new Padding(3, 6, 3, 0), Font = new Font(Font, FontStyle.Bold) };
                grid.Controls.Add(_chResult[i], 3, i + 1);
            }
            left.Controls.Add(grid);
            var fill = new Button { Text = "Fill header pins with busy pins from my sniff map", AutoSize = true };
            fill.Click += (s, e) => FillFromMap();
            left.Controls.Add(fill);

            var orow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            orow.Controls.Add(new Label { Text = "Speed:", AutoSize = true, Margin = new Padding(0, 7, 2, 0) });
            _rate = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
            _rate.Items.AddRange(new object[] { "Fastest (finds CLK and DATA)", "1 MHz", "100 kHz (finds rows A, B, C and OE)", "20 kHz (slowest, longest)" });
            _rate.SelectedIndex = 0;
            orow.Controls.Add(_rate);
            left.Controls.Add(orow);
            var trow = new FlowLayoutPanel { AutoSize = true };
            trow.Controls.Add(new Label { Text = "Start when:", AutoSize = true, Margin = new Padding(0, 7, 2, 0) });
            _trig = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
            _trig.Items.Add("right away");
            for (int i = 0; i < 8; i++) _trig.Items.Add($"channel {i + 1} goes high");
            _trig.SelectedIndex = 0;
            trow.Controls.Add(_trig);
            left.Controls.Add(trow);

            var brow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            _capture = new Button { Text = "Capture", AutoSize = true, Font = new Font(Font.FontFamily, 11f, FontStyle.Bold), Padding = new Padding(10, 3, 10, 3) };
            _capture.Click += async (s, e) => await Capture();
            _apply = new Button { Text = "Put results in the pin map", AutoSize = true, Enabled = false };
            _apply.Click += (s, e) => ApplyToMap();
            brow.Controls.Add(_capture); brow.Controls.Add(_apply);
            left.Controls.Add(brow);
            _state = new Label { AutoSize = true, MaximumSize = new Size(445, 0), Font = new Font(Font, FontStyle.Bold) };
            left.Controls.Add(_state);
            left.Controls.Add(P("Reading the picture: scroll the mouse wheel to zoom, drag to move. Fastest speed covers a few milliseconds (one or two rows of the sign); 100 kHz covers about half a second (many full frames)."));

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            _wave = new WavePanel { Dock = DockStyle.Fill, Colors = TraceColors };
            right.Controls.Add(_wave, 0, 0);
            _report = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BackColor = Color.White };
            _report.Text = "Results appear here after a capture.";
            right.Controls.Add(_report, 0, 1);
            root.Controls.Add(right, 1, 0);
        }

        void FillFromMap()
        {
            var busy = _ctx.Map.Pins.Where(p => p.SniffHz > 0 && p.R != PinRole.GND && p.R != PinRole.V5).Select(p => p.Number).ToList();
            var used = new HashSet<int>(_hdr.Select(h => (int)h.Value).Where(v => v > 0));
            var queue = new Queue<int>(busy.Where(b => !used.Contains(b)));
            for (int i = 0; i < 8; i++) if (_hdr[i].Value == 0 && queue.Count > 0) _hdr[i].Value = queue.Dequeue();
            _state.ForeColor = SystemColors.ControlText;
            _state.Text = busy.Count == 0 ? "Your map has no sniffed busy pins yet. Type the header pins by hand." : $"Filled empty channels from {busy.Count} busy pins in your map.";
        }

        int RateHz => _rate.SelectedIndex switch { 1 => 1000000, 2 => 100000, 3 => 20000, _ => 0 };

        async Task Capture()
        {
            if (!_ctx.Board.Connected) { _state.ForeColor = Color.DarkRed; _state.Text = "Connect to the board first (top bar)."; return; }
            if (_ctx.Mode != "diag" && !await _ctx.SetMode("diag")) { _state.ForeColor = Color.DarkRed; _state.Text = "Couldn't switch the board to Diagnostics mode."; return; }
            _capture.Enabled = false;
            _state.ForeColor = SystemColors.ControlText;
            int n = 49152;
            int trig = _trig.SelectedIndex - 1;
            double secs = RateHz == 0 ? 0.02 : (double)n / RateHz;
            int timeout = (int)(secs * 1000) + (_ctx.Board.OverWifi ? 6000 : 12000);
            _state.Text = _ctx.Board.OverWifi ? "Capturing…" : "Capturing… then sending it over USB (about 6 seconds)";
            try
            {
                var r = await _ctx.Board.Send($"CAPTURE {n} {RateHz} {trig} 1", timeout);
                _data = Convert.FromBase64String(r.Str("cap"));
                _sampleRate = r.Dbl("rate");
                bool trigOk = trig < 0 || r.Bool("triggered");
                _last = Analysis.Run(_data, _sampleRate);
                _wave.SetData(_data, _sampleRate, Labels());
                ShowResults();
                _state.ForeColor = Color.DarkGreen;
                _state.Text = $"Captured {_data.Length:N0} samples at {Fmt.Hz(_sampleRate)} ({Fmt.Time(_data.Length / Math.Max(1, _sampleRate))})." +
                              (trigOk ? "" : " The start signal never came, so it recorded right away.");
            }
            catch (Exception ex)
            {
                _state.ForeColor = Color.DarkRed;
                _state.Text = "Capture failed: " + ex.Message + (ex.Message.Contains("CAPTURE") || ex.Message.Contains("unknown") ? " Install firmware 3.1.0 or newer (tab 0)." : "");
            }
            finally { _capture.Enabled = true; }
        }

        string[] Labels() => Enumerable.Range(0, 8).Select(i => $"{i + 1}: GPIO {Gpios[i]}" + (_hdr[i].Value > 0 ? $" = pin {_hdr[i].Value}" : "")).ToArray();

        void ShowResults()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Sample rate {Fmt.Hz(_sampleRate)}, {Fmt.Time(_data.Length / _sampleRate)} recorded.");
            if (_sampleRate > 0 && _last.ClockCh >= 0 && _last.Ch[_last.ClockCh].MinWidth <= 1)
                sb.AppendLine("Note: the clock is about as fast as the ESP32 can record, so some clock ticks may be missed. The CLK/DATA/LAT picks are still useful.");
            sb.AppendLine();
            for (int i = 0; i < 8; i++)
            {
                var c = _last.Ch[i];
                string pin = _hdr[i].Value > 0 ? $"header pin {_hdr[i].Value}" : "(header pin not set)";
                sb.AppendLine($"Ch {i + 1} {pin}: {c.Guess}");
                sb.AppendLine($"      {c.Edges} changes, high {c.Duty:0.#}% of the time, {c.Why}");
                _chResult[i].Text = c.Code;
                _chResult[i].ForeColor = c.Code == "?" || c.Code == "" ? Color.DimGray : Roles.Color(Roles.FromCode(c.Code));
            }
            sb.AppendLine();
            sb.AppendLine(_last.Summary);
            _report.Text = sb.ToString().Replace("\n", "\r\n").Replace("\r\r", "\r");
            _apply.Enabled = Enumerable.Range(0, 8).Any(i => _hdr[i].Value > 0 && Roles.FromCode(_last.Ch[i].Code) != PinRole.Unknown);
        }

        void ApplyToMap()
        {
            if (_last == null) return;
            int changed = 0;
            var notes = new List<string>();
            for (int i = 0; i < 8; i++)
            {
                int pin = (int)_hdr[i].Value;
                if (pin < 1 || pin > _ctx.Map.Pins.Count) continue;
                var role = Roles.FromCode(_last.Ch[i].Code);
                if (role == PinRole.Unknown) continue;
                var hp = _ctx.Map[pin];
                hp.R = role;
                hp.Note = "Logic analyzer: " + _last.Ch[i].Guess;
                changed++;
                notes.Add($"{pin}={Roles.Code(role)}");
            }
            _ctx.RaiseMap();
            _state.ForeColor = Color.DarkGreen;
            _state.Text = changed == 0 ? "Nothing to put in the map (set the header pins first)." : $"Pin map updated: {string.Join(", ", notes)}. Check it in tab 1.";
        }
    }

    static class Fmt
    {
        public static string Hz(double hz) => hz >= 1e6 ? $"{hz / 1e6:0.##} MHz" : hz >= 1e3 ? $"{hz / 1e3:0.##} kHz" : $"{hz:0} Hz";
        public static string Time(double s) => s >= 1 ? $"{s:0.##} s" : s >= 1e-3 ? $"{s * 1e3:0.##} ms" : $"{s * 1e6:0.#} µs";
    }

    /// <summary>Works out what each captured channel is from its timing.</summary>
    public class Analysis
    {
        public class ChanInfo
        {
            public int Edges; public double Duty; public int MinWidth; public double MedHigh, MedLow;
            public List<int> EdgeIdx = new List<int>();
            public string Code = "?", Guess = "", Why = "";
        }
        public ChanInfo[] Ch = new ChanInfo[8];
        public int ClockCh = -1, LatchCh = -1;
        public string Summary = "";

        static double Median(List<int> v) { if (v.Count == 0) return 0; var s = v.OrderBy(x => x).ToList(); return s[s.Count / 2]; }

        public static Analysis Run(byte[] d, double rate)
        {
            var a = new Analysis();
            int n = d.Length;
            for (int c = 0; c < 8; c++)
            {
                var ci = new ChanInfo(); a.Ch[c] = ci;
                int m = 1 << c, highs = 0, runStart = 0;
                bool last = (d[0] & m) != 0;
                var hi = new List<int>(); var lo = new List<int>();
                for (int i = 0; i < n; i++)
                {
                    bool v = (d[i] & m) != 0;
                    if (v) highs++;
                    if (v != last)
                    {
                        if (ci.EdgeIdx.Count > 0) (last ? hi : lo).Add(i - runStart);   // skip the first (cut-off) run
                        ci.EdgeIdx.Add(i); runStart = i; last = v;
                    }
                }
                ci.Edges = ci.EdgeIdx.Count;
                ci.Duty = 100.0 * highs / n;
                ci.MedHigh = Median(hi); ci.MedLow = Median(lo);
                ci.MinWidth = hi.Concat(lo).DefaultIfEmpty(0).Min();
            }

            var active = Enumerable.Range(0, 8).Where(c => a.Ch[c].Edges >= 4).ToList();
            foreach (var c in Enumerable.Range(0, 8).Except(active))
            {
                var ci = a.Ch[c];
                ci.Code = ci.Edges == 0 ? (ci.Duty > 50 ? "?" : "?") : "?";
                ci.Guess = ci.Edges == 0 ? (ci.Duty > 50 ? "steady HIGH (power, or an idle line)" : "steady LOW (ground, unused, or idle)") : "barely changes in this capture";
                ci.Why = "try a slower speed to see slow signals.";
            }
            if (active.Count == 0) { a.Summary = "Nothing moved. Check the controller is running, the grounds are joined, and each channel's 10 kΩ goes to the right ESP32 pin."; return a; }

            // CLK: the busiest channel whose changes come in bursts with gaps between them.
            int clk = active.OrderByDescending(c => a.Ch[c].Edges).First();
            var ce = a.Ch[clk].EdgeIdx;
            var gaps = new List<(int s, int e)>();
            double medStep = 0;
            if (ce.Count > 8)
            {
                var steps = new List<int>(); for (int i = 1; i < ce.Count; i++) steps.Add(ce[i] - ce[i - 1]);
                medStep = Median(steps);
                double gapMin = Math.Max(8, medStep * 6);
                for (int i = 1; i < ce.Count; i++) if (ce[i] - ce[i - 1] >= gapMin) gaps.Add((ce[i - 1], ce[i]));
            }
            bool bursty = gaps.Count >= 2;
            if (bursty) { a.ClockCh = clk; }

            bool InGap(int idx, int slack) => gaps.Any(g => idx >= g.s - slack && idx <= g.e + slack);

            // LAT: short pulses, one per clock gap.
            int lat = -1; double bestScore = 0;
            foreach (var c in active.Where(c => c != a.ClockCh))
            {
                var ci = a.Ch[c];
                double shortW = Math.Min(ci.MedHigh, ci.MedLow), longW = Math.Max(ci.MedHigh, ci.MedLow);
                if (shortW <= 0 || shortW > longW * 0.15) continue;
                int pulses = ci.Edges / 2;
                double inGap = bursty ? ci.EdgeIdx.Count(e => InGap(e, 2)) / (double)ci.Edges : 0.5;
                double perGap = bursty ? pulses / (double)gaps.Count : 1;
                double score = inGap * (perGap > 0.5 && perGap < 2 ? 1 : 0.3) * (1 - shortW / Math.Max(1, longW));
                if (score > bestScore) { bestScore = score; lat = c; }
            }
            if (lat >= 0) a.LatchCh = lat;

            // Row-change count = number of LAT pulses (or clock gaps).
            int rows = lat >= 0 ? a.Ch[lat].Edges / 2 : gaps.Count;
            var addr = new List<(int c, double ratio)>();
            foreach (var c in active)
            {
                var ci = a.Ch[c];
                if (c == a.ClockCh) { ci.Code = "CLK"; ci.Guess = "CLK (shift clock)"; ci.Why = $"busiest line, comes in {gaps.Count} bursts."; continue; }
                if (c == a.LatchCh) { ci.Code = "LAT"; ci.Guess = "LAT (latch)"; ci.Why = "one short pulse after each clock burst."; continue; }
                double inGap = bursty ? ci.EdgeIdx.Count(e => InGap(e, 2)) / (double)ci.Edges : 0;
                if (bursty && inGap < 0.5) { ci.Code = "DAT"; ci.Guess = "DATA (changes while the clock runs)"; ci.Why = "changes in step with the clock bursts."; continue; }
                double ratio = rows > 0 ? ci.Edges / (double)rows : 0;
                if (ratio > 1.6 && ratio < 2.6 && Math.Abs(ci.Duty - 50) > 10) { ci.Code = "OE"; ci.Guess = "OE (blanking, switches off around each latch)"; ci.Why = "two changes per row, mostly one state."; continue; }
                addr.Add((c, ratio));
                ci.Why = $"changes {ratio:0.##}x per row.";
            }
            // Address lines: A changes once per row, B every 2 rows, C every 4, D every 8.
            foreach (var (c, ratio) in addr)
            {
                var ci = a.Ch[c];
                string code = ratio > 0.7 && ratio < 1.4 ? "A" : ratio > 0.35 && ratio <= 0.7 ? "B" : ratio > 0.17 && ratio <= 0.35 ? "C" : ratio > 0.08 && ratio <= 0.17 ? "D" : "?";
                if (code == "?") { ci.Guess = "active, but doesn't fit a known pattern in this capture"; ci.Why += " Try another speed."; }
                else { ci.Code = code; ci.Guess = $"Row address {code}"; }
            }

            var sb = new StringBuilder();
            if (!bursty) sb.AppendLine("No clear clock bursts in this capture. If you're on Fastest, the clock may not be on any channel yet: clip other busy pins. If you're on a slow speed, use Fastest to find CLK and DATA.");
            else sb.AppendLine($"Clock bursts: {gaps.Count}, clock about {Fmt.Hz(rate / Math.Max(1, medStep * 2))} inside a burst.");
            if (lat < 0) sb.AppendLine("No latch found yet: clip more busy pins (the latch is usually a line with very short pulses).");
            if (rows > 0 && addr.Count > 0) sb.AppendLine("Row lines are clearest at 100 kHz, where many rows fit in one capture.");
            sb.AppendLine("These are best guesses from timing. If the sign later shows the picture shifted, scrambled or in the wrong rows, swap the guessed pins in tab 2.");
            a.Summary = sb.ToString();
            return a;
        }
    }

    /// <summary>Draws 8 digital traces with zoom (mouse wheel) and pan (drag).</summary>
    public class WavePanel : Panel
    {
        public Color[] Colors;
        byte[] _d; double _rate; string[] _labels = new string[8];
        double _start, _span;    // in samples
        int _dragX = -1; double _dragStart;

        public WavePanel() { DoubleBuffered = true; BackColor = Color.FromArgb(18, 18, 22); ResizeRedraw = true; }

        public void SetData(byte[] d, double rate, string[] labels)
        {
            _d = d; _rate = rate; _labels = labels; _start = 0; _span = d.Length; Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (_d == null) return;
            int lw = 150; double w = Math.Max(1, Width - lw - 10);
            double at = _start + (e.X - lw) / w * _span;
            double f = e.Delta > 0 ? 0.6 : 1 / 0.6;
            _span = Math.Max(20, Math.Min(_d.Length, _span * f));
            _start = Math.Max(0, Math.Min(_d.Length - _span, at - (e.X - lw) / w * _span));
            Invalidate();
        }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); _dragX = e.X; _dragStart = _start; }
        protected override void OnMouseUp(MouseEventArgs e) { _dragX = -1; }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragX < 0 || _d == null) return;
            double w = Math.Max(1, Width - 160);
            _start = Math.Max(0, Math.Min(_d.Length - _span, _dragStart - (e.X - _dragX) / w * _span));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using var font = new Font("Segoe UI", 8.5f);
            if (_d == null)
            {
                g.DrawString("Press Capture to record 8 pins.", font, Brushes.Gray, 20, 20);
                return;
            }
            int lw = 150, top = 8, bottom = 26;
            double w = Math.Max(1, Width - lw - 10);
            float rowH = (Height - top - bottom) / 8f;
            int s0 = (int)_start, s1 = (int)Math.Min(_d.Length - 1, _start + _span);
            double perPx = _span / w;
            for (int c = 0; c < 8; c++)
            {
                float yHi = top + c * rowH + rowH * 0.2f, yLo = top + c * rowH + rowH * 0.8f;
                using (var b = new SolidBrush(Colors[c])) g.DrawString(_labels[c] ?? "", font, b, 4, (yHi + yLo) / 2 - 7);
                using var pen = new Pen(Colors[c], 1.5f);
                int m = 1 << c;
                float prevY = (_d[s0] & m) != 0 ? yHi : yLo;
                for (int px = 0; px < w; px++)
                {
                    int a = s0 + (int)(px * perPx), bnd = Math.Min(s1, s0 + (int)((px + 1) * perPx));
                    bool anyHi = false, anyLo = false;
                    for (int i = a; i <= Math.Max(a, bnd); i++) { if ((_d[i] & m) != 0) anyHi = true; else anyLo = true; if (anyHi && anyLo) break; }
                    float x = lw + px;
                    if (anyHi && anyLo) { g.DrawLine(pen, x, yHi, x, yLo); prevY = (_d[Math.Max(a, bnd)] & m) != 0 ? yHi : yLo; }
                    else { float y = anyHi ? yHi : yLo; if (y != prevY) g.DrawLine(pen, x, prevY, x, y); g.DrawLine(pen, x, y, x + 1, y); prevY = y; }
                }
            }
            // time axis
            using var axis = new Pen(Color.FromArgb(70, 70, 80));
            g.DrawLine(axis, lw, Height - bottom + 4, Width - 10, Height - bottom + 4);
            for (int k = 0; k <= 4; k++)
            {
                float x = (float)(lw + w * k / 4);
                double t = (_start + _span * k / 4) / Math.Max(1, _rate);
                g.DrawLine(axis, x, Height - bottom + 1, x, Height - bottom + 7);
                g.DrawString(Fmt.Time(t), font, Brushes.Gray, x - (k == 4 ? 50 : 0), Height - bottom + 8);
            }
        }
    }
}
