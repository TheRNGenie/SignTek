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
            "SPI plug, 1st pin. Pulled high by the SD slot: a pin that isn't driven reads HIGH here", "SPI plug, 2nd pin", "SPI plug, 3rd pin", "SPI plug, 4th pin. Pulled high by the SD slot: a pin that isn't driven reads HIGH here",
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
                "2. Choose the pins: press Pick the next 8 pins to check (below), click pins on the socket picture, or type the header pin numbers (0 = not used).\n" +
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
            var pic = new Button { Text = "Show wiring picture (both boards)", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            pic.Click += (s, e) => ShowPicture();
            left.Controls.Add(pic);

            left.Controls.Add(H("Your pin map: click pins to check next"));
            left.Controls.Add(P("Black = GND, red = 3V3 power (never clip these), grey = dead. Coloured = already identified. White = busy, not identified yet (guesses from single-pin sniffing are ignored here). A ✓ means the analyzer has recorded that pin. Click a pin to put it on the next free channel; click it again to take it off."));
            _picker = new PinPicker { Width = 445, Height = 96, Map = _ctx.Map, Channels = _hdr, ChannelColors = TraceColors };
            _picker.PinClicked += OnPinClicked;
            left.Controls.Add(_picker);
            var mrow = new FlowLayoutPanel { AutoSize = true };
            var imp = new Button { Text = "Import a map file…", AutoSize = true };
            imp.Click += (s, e) => ImportMap();
            var next = new Button { Text = "Pick the next 8 pins to check", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            next.Click += (s, e) => PickNext();
            var clr = new Button { Text = "Clear channels", AutoSize = true };
            clr.Click += (s, e) => { foreach (var h in _hdr) h.Value = 0; };
            mrow.Controls.Add(imp); mrow.Controls.Add(next); mrow.Controls.Add(clr);
            left.Controls.Add(mrow);
            _progress = new Label { AutoSize = true, MaximumSize = new Size(445, 0), ForeColor = Color.DimGray };
            left.Controls.Add(_progress);
            foreach (var h in _hdr) h.ValueChanged += (s, e) => _picker.Invalidate();
            _ctx.MapChanged += () => { _picker.Map = _ctx.Map; _picker.Invalidate(); UpdateProgress(); };
            UpdateProgress();

            var orow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            orow.Controls.Add(new Label { Text = "Speed:", AutoSize = true, Margin = new Padding(0, 7, 2, 0) });
            _rate = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
            _rate.Items.AddRange(new object[] { "Fastest (finds CLK and DATA)", "1 MHz", "100 kHz (finds rows A, B, C and OE)", "20 kHz (slowest, longest)", "Turbo: 2-3x faster, channels 3, 5, 6, 7, 8 only", "Fast 10 MHz: all 8 channels, exact timing (firmware 3.2.0)" });
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
            var srow = new FlowLayoutPanel { AutoSize = true };
            var saveAll = new Button { Text = "Save my captures as one file (to send)", AutoSize = true };
            saveAll.Click += (s, e) => SaveCaptures();
            var clearRes = new Button { Text = "Clear analyzer results", AutoSize = true };
            clearRes.Click += (s, e) => ClearResults();
            srow.Controls.Add(saveAll); srow.Controls.Add(clearRes);
            left.Controls.Add(brow);
            left.Controls.Add(srow);
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

        void ShowPicture()
        {
            using var st = typeof(LogicTab).Assembly.GetManifestResourceStream("logic-wiring.png");
            if (st == null) return;
            var bmp = new Bitmap(st);
            var f = new Form { Text = "Logic analyzer wiring: both boards", ClientSize = new Size(Math.Min(1400, bmp.Width), Math.Min(900, bmp.Height)), StartPosition = FormStartPosition.CenterParent };
            f.Controls.Add(new PictureBox { Image = bmp, Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom });
            f.FormClosed += (s, e) => bmp.Dispose();
            f.Show(FindForm());
        }

        PinPicker _picker;
        Label _progress;

        /// <summary>A multimeter reading between "low" and "high" means the pin switches fast (the meter shows the average).</summary>
        static bool VoltsBusy(HeaderPin p) => double.TryParse((p.Volts ?? "").Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) && v >= 0.15 && v <= 3.2;
        static bool Skip(HeaderPin p) => p.R == PinRole.GND || p.R == PinRole.V5;
        /// <summary>Signal roles found by this analyzer. Roles guessed by single-pin sniffing aren't trusted here.</summary>
        public static bool Confirmed(HeaderPin p) => Roles.Signals.Contains(p.R) && (p.Note ?? "").StartsWith("Logic analyzer");

        void UpdateProgress()
        {
            var cand = _ctx.Map.Pins.Where(p => !Skip(p) && p.R != PinRole.NC).ToList();
            int done = cand.Count(p => p.LaChecked);
            var known = _ctx.Map.Pins.Where(Confirmed).Select(p => $"{p.Number}={Roles.Code(p.R)}").ToList();
            var dup = new[] { PinRole.Clk, PinRole.Lat }.Where(r => _ctx.Map.Pins.Count(p => p.R == r && Confirmed(p)) > 1).Select(Roles.Code).ToList();
            _progress.Text = $"{done} of {cand.Count} pins worth checking have been recorded." + (known.Count > 0 ? " Identified so far: " + string.Join(", ", known) + "." : "") +
                (dup.Count > 0 ? $" ⚠ Results conflict: more than one {string.Join(" and ", dup)}. A sign has only one of each, so some captures didn't include the real clock. Clear analyzer results and start again with Fastest speed." : "");
            _progress.ForeColor = dup.Count > 0 ? Color.DarkRed : Color.DimGray;
        }

        void OnPinClicked(int pin)
        {
            var hp = _ctx.Map[pin];
            _state.ForeColor = SystemColors.ControlText;
            for (int i = 0; i < 8; i++) if (_hdr[i].Value == pin) { _hdr[i].Value = 0; _state.Text = $"Pin {pin} taken off channel {i + 1}."; return; }
            if (Skip(hp)) { _state.ForeColor = Color.DarkRed; _state.Text = $"Pin {pin} is marked {Roles.Code(hp.R)} in your map. Don't clip a channel onto it."; return; }
            for (int i = 0; i < 8; i++) if (_hdr[i].Value == 0) { _hdr[i].Value = pin; _state.Text = $"Pin {pin} → channel {i + 1} (GPIO {Gpios[i]}, {Where[i]})."; return; }
            _state.Text = "All 8 channels are in use. Click a pin that's already on a channel to free it, or press Clear channels.";
        }

        void ImportMap()
        {
            using var d = new OpenFileDialog { Filter = "Header map (*.json)|*.json|All files|*.*", Title = "Import a pin map saved from tab 1" };
            if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
            try
            {
                var m = HeaderMap.Load(d.FileName);
                _ctx.Map = m;
                _ctx.RaiseMap();
                _state.ForeColor = Color.DarkGreen;
                _state.Text = $"Imported {System.IO.Path.GetFileName(d.FileName)}: {m.Pins.Count(p => p.R == PinRole.GND)} GND, {m.Pins.Count(p => p.R == PinRole.V5)} 3V3, {m.Pins.Count(p => p.SniffHz > 0)} busy pins. It's now the map in tab 1 too.";
            }
            catch (Exception ex) { _state.ForeColor = Color.DarkRed; _state.Text = "Couldn't read that file: " + ex.Message; }
        }

        /// <summary>Keeps CLK and LAT (once found) on channels 1 and 2, fills the rest with pins not recorded yet: busy ones first.</summary>
        void PickNext()
        {
            var map = _ctx.Map;
            var keep = new List<int>();
            var clk = map.Pins.FirstOrDefault(p => p.R == PinRole.Clk && Confirmed(p)); var lat = map.Pins.FirstOrDefault(p => p.R == PinRole.Lat && Confirmed(p));
            if (clk != null) keep.Add(clk.Number);
            if (lat != null) keep.Add(lat.Number);
            var todo = map.Pins.Where(p => !Skip(p) && p.R != PinRole.NC && !p.LaChecked && !keep.Contains(p.Number))
                              .OrderByDescending(p => p.SniffHz > 0 || VoltsBusy(p)).ThenBy(p => p.Number).Select(p => p.Number).ToList();
            var pick = keep.Concat(todo).Take(8).ToList();
            // Channels 5 and 8 (GPIO 5, 23) are pulled high by the SD slot, so they get pins last.
            int[] slots = { 0, 1, 2, 3, 5, 6, 4, 7 };
            for (int i = 0; i < 8; i++) _hdr[i].Value = 0;
            for (int i = 0; i < pick.Count; i++) _hdr[slots[i]].Value = pick[i];
            _state.ForeColor = SystemColors.ControlText;
            if (todo.Count == 0) _state.Text = "Every pin worth checking has been recorded. " + (keep.Count > 0 ? "CLK/LAT kept on channels 1-2." : "");
            else _state.Text = $"Next to clip: {string.Join(", ", pick.Select((p, i) => $"ch{i + 1}=pin {p}"))}." + (keep.Count > 0 ? " CLK/LAT stay on channels 1-2 so the new pins can be compared against them." : "") + $" {Math.Max(0, todo.Count - (8 - keep.Count))} more pins after this round.";
        }

        static string CapDir => System.IO.Path.Combine(AppSettings.Dir, "captures");

        /// <summary>Every capture is kept on the PC so it can be looked at again or sent for a second opinion.</summary>
        void AutoSave(string b64, string extra = "")
        {
            try
            {
                System.IO.Directory.CreateDirectory(CapDir);
                var hdr = string.Join(",", _hdr.Select(h => ((int)h.Value).ToString()));
                var gp = string.Join(",", Gpios);
                var json = $"{{{extra}\"map\":\"{(_ctx.Map.IsPanel ? "panel16" : "v002")}\",\"time\":\"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",\"rate\":{_sampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"speed\":\"{_rate.SelectedItem}\",\"gpios\":[{gp}],\"header_pins\":[{hdr}],\"data\":\"{b64}\"}}";
                System.IO.File.WriteAllText(System.IO.Path.Combine(CapDir, $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.json"), json);
            }
            catch { }
        }

        void SaveCaptures()
        {
            if (!System.IO.Directory.Exists(CapDir) || System.IO.Directory.GetFiles(CapDir, "*.json").Length == 0) { _state.ForeColor = Color.DarkRed; _state.Text = "No captures saved yet. Press Capture first."; return; }
            using var d = new SaveFileDialog { Filter = "Zip file (*.zip)|*.zip", FileName = $"sign-captures-{DateTime.Now:yyyyMMdd-HHmm}.zip" };
            if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
            try
            {
                if (System.IO.File.Exists(d.FileName)) System.IO.File.Delete(d.FileName);
                var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sign-captures-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(tmp);
                foreach (var fn in System.IO.Directory.GetFiles(CapDir, "*.json")) System.IO.File.Copy(fn, System.IO.Path.Combine(tmp, System.IO.Path.GetFileName(fn)));
                try { _ctx.Map.Save(System.IO.Path.Combine(tmp, "sign-header-map.json")); } catch { }
                System.IO.Compression.ZipFile.CreateFromDirectory(tmp, d.FileName);
                System.IO.Directory.Delete(tmp, true);
                _state.ForeColor = Color.DarkGreen;
                _state.Text = $"Saved {System.IO.Directory.GetFiles(CapDir, "*.json").Length} captures and your pin map in {System.IO.Path.GetFileName(d.FileName)}.";
            }
            catch (Exception ex) { _state.ForeColor = Color.DarkRed; _state.Text = "Couldn't save: " + ex.Message; }
        }

        void ClearResults()
        {
            if (MessageBox.Show(FindForm(), "Forget every result the logic analyzer put in the pin map (and the ✓ marks)? GND, 3V3 and NC pins are kept. Saved captures are kept too.", "Clear analyzer results", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            foreach (var p in _ctx.Map.Pins)
            {
                if (Confirmed(p) || (Roles.Signals.Contains(p.R) && p.LaChecked)) { p.R = PinRole.Unknown; p.Note = ""; }
                p.LaChecked = false;
            }
            _ctx.RaiseMap();
            _state.ForeColor = SystemColors.ControlText; _state.Text = "Analyzer results cleared.";
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

        int RateHz => _rate.SelectedIndex switch { 1 => 1000000, 2 => 100000, 3 => 20000, 4 => 1, 5 => 3, _ => 0 };
        bool Fast => _rate.SelectedIndex == 5;
        bool Turbo => _rate.SelectedIndex == 4;
        static readonly int[] TurboChannels = { 2, 4, 5, 6, 7 };   // 0-based: GPIO 25, 5, 18, 19, 23

        async Task Capture()
        {
            if (!_ctx.Board.Connected) { _state.ForeColor = Color.DarkRed; _state.Text = "Connect to the board first (top bar)."; return; }
            if (_ctx.Mode != "diag" && !await _ctx.SetMode("diag")) { _state.ForeColor = Color.DarkRed; _state.Text = "Couldn't switch the board to Diagnostics mode."; return; }
            _capture.Enabled = false;
            _state.ForeColor = SystemColors.ControlText;
            int n = Turbo ? 16383 : Fast ? 30000 : 49152;
            int trig = _trig.SelectedIndex - 1;
            double secs = RateHz <= 3 ? 0.02 : (double)n / RateHz;
            int timeout = (int)(secs * 1000) + (_ctx.Board.OverWifi ? 6000 : 12000);
            _state.Text = _ctx.Board.OverWifi ? "Capturing…" : "Capturing… then sending it over USB (about 6 seconds)";
            try
            {
                var r = await _ctx.Board.Send($"CAPTURE {n} {RateHz} {trig} 1", timeout);
                _data = Convert.FromBase64String(r.Str("cap"));
                _sampleRate = r.Dbl("rate");
                bool trigOk = trig < 0 || r.Bool("triggered");
                if (Turbo && !r.Bool("turbo"))
                {
                    _state.ForeColor = Color.DarkRed;
                    _state.Text = "The board's firmware is too old for Turbo (it recorded very slowly instead). Install the newest firmware in tab 0 (3.1.1 or newer), press Connect, then capture again.";
                    return;
                }
                if (Fast && !r.Bool("fast"))
                {
                    _state.ForeColor = Color.DarkRed;
                    _state.Text = "The board's firmware is too old for Fast mode. Install the newest firmware in tab 0 (3.2.0 or newer), press Connect, then capture again.";
                    return;
                }
                string fastNote = "";
                if (Fast && r.Str("selftest") != "ok")
                    fastNote = $" ⚠ Fast mode self-test: {r.Str("selftest")} ({r.Str("selftest_bad_runs")} bad of {r.Str("selftest_checked")}). The recording may be wrong: send the captures file so it can be checked.";
                _last = Analysis.Run(_data, _sampleRate);
                AutoSave(r.Str("cap"), Fast ? $"\"fast_selftest\":\"{r.Str("selftest")}\",\"fast_bad_runs\":\"{r.Str("selftest_bad_runs")}\",\"fast_edges\":\"{r.Str("selftest_edges")}\",\"fast_swapped\":\"{r.Str("swapped")}\",\"fast_filled\":\"{r.Str("filled")}\"," : "");
                for (int i = 0; i < 8; i++) if (_hdr[i].Value > 0) _ctx.Map[(int)_hdr[i].Value].LaChecked = true;
                _ctx.RaiseMap();
                if (Turbo)
                    for (int i = 0; i < 8; i++)
                        if (!TurboChannels.Contains(i)) { var ci = _last.Ch[i]; ci.Code = "?"; ci.Guess = "not recorded (Turbo only records channels 3, 5, 6, 7, 8)"; ci.Why = ""; }
                _wave.SetData(_data, _sampleRate, Labels());
                ShowResults();
                _state.ForeColor = Color.DarkGreen;
                _state.Text = $"Captured {_data.Length:N0} samples at {Fmt.Hz(_sampleRate)} ({Fmt.Time(_data.Length / Math.Max(1, _sampleRate))})." +
                              (trigOk ? "" : " The start signal never came, so it recorded right away.") + fastNote;
                if (fastNote.Length > 0) _state.ForeColor = Color.DarkOrange;
            }
            catch (Exception ex)
            {
                _state.ForeColor = Color.DarkRed;
                _state.Text = "Capture failed: " + ex.Message + (ex.Message.Contains("CAPTURE") || ex.Message.Contains("unknown") ? " Install the newest firmware (tab 0)." : "");
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

    /// <summary>The 50-pin header as 2 rows of 25 (odd pins on top), coloured by role, with channel assignments.</summary>
    public class PinPicker : Control
    {
        public HeaderMap Map;
        public NumericUpDown[] Channels;
        public Color[] ChannelColors;
        public event Action<int> PinClicked;
        public PinPicker() { DoubleBuffered = true; ResizeRedraw = true; Cursor = Cursors.Hand; }

        int Cols => ((Map?.Pins.Count ?? 50) + 1) / 2;
        RectangleF Cell(int pin)
        {
            float cw = (Width - 24f) / Cols, ch = 30f;
            int col = (pin - 1) / 2, row = (pin - 1) % 2;
            return new RectangleF(20 + col * cw, 14 + row * (ch + 4), cw - 2, ch);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (Map == null) return;
            for (int p = 1; p <= Map.Pins.Count; p++) if (Cell(p).Contains(e.Location)) { PinClicked?.Invoke(p); return; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Map == null) return;
            using var f = new Font("Segoe UI", 6.5f); using var fb = new Font("Segoe UI", 7f, FontStyle.Bold);
            var cen = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("odd", f, Brushes.Gray, 0, 22); g.DrawString("even", f, Brushes.Gray, 0, 56);
            for (int p = 1; p <= Map.Pins.Count; p++)
            {
                var hp = Map[p]; var r = Cell(p);
                bool guess = Roles.Signals.Contains(hp.R) && !LogicTab.Confirmed(hp);   // sniff guess: show as busy, not identified
                Color back = hp.R == PinRole.Unknown || guess ? (hp.SniffHz > 0 || guess ? Color.White : Color.FromArgb(225, 225, 225)) : Roles.Color(hp.R);
                if (hp.R == PinRole.NC) back = Color.FromArgb(185, 185, 185);
                using (var b = new SolidBrush(back)) g.FillRectangle(b, r);
                g.DrawRectangle(Pens.Gray, r.X, r.Y, r.Width, r.Height);
                bool dark = back.GetBrightness() < 0.45f;
                string code = hp.R == PinRole.Unknown || guess ? (hp.SniffHz > 0 || guess ? "busy" : "") : Roles.Code(hp.R);
                g.DrawString(p.ToString(), fb, dark ? Brushes.White : Brushes.Black, new RectangleF(r.X, r.Y + 1, r.Width, r.Height / 2), cen);
                g.DrawString(code, f, dark ? Brushes.White : Brushes.DimGray, new RectangleF(r.X - 3, r.Y + r.Height / 2, r.Width + 6, r.Height / 2), cen);
                if (hp.LaChecked) g.DrawString("✓", fb, Brushes.Green, r.Right - 8, r.Y - 3);
                int ch = Array.FindIndex(Channels, c => c.Value == p);
                if (ch >= 0)
                {
                    using var pen = new Pen(ChannelColors[ch], 3f);
                    g.DrawRectangle(pen, r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2);
                    var tag = (p % 2 == 1) ? new RectangleF(r.X, r.Y - 13, r.Width, 12) : new RectangleF(r.X, r.Bottom + 1, r.Width, 12);
                    using var tb = new SolidBrush(ChannelColors[ch]);
                    g.DrawString((ch + 1).ToString(), fb, tb, tag, cen);
                }
            }
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
            // A real shift clock ticks many times per burst (at least once per column chip, usually dozens of times).
            bool bursty = gaps.Count >= 2 && ce.Count / (double)(gaps.Count + 1) >= 16;
            if (!bursty)
            {
                foreach (var c in active)
                {
                    var ci = a.Ch[c]; ci.Code = "?";
                    ci.Guess = "active, but can't be identified from this capture";
                    ci.Why = "no channel in this capture looks like a clock, so there's nothing to compare against.";
                }
                a.Summary = "No shift clock in this capture: none of the 8 channels ticks in bursts. Nothing was labelled, because without the clock the guesses would be wrong. " +
                            "Use Fastest speed and clip other busy pins until a capture shows a clock (try the pins between the first two GND pairs, e.g. 6, 7, 8, 9, 12, 13, 14, 15).";
                return a;
            }
            a.ClockCh = clk;

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
