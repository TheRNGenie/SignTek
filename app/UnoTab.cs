using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CasinoSignTool
{
    /// <summary>
    /// Drive panel 2 from an Arduino UNO R4 WiFi instead of the ESP32 (no soldering).
    /// Installs the bundled uno_panel firmware with bossac and talks to it over its own COM port.
    /// </summary>
    public class UnoTab : UserControl
    {
        ComboBox _ports, _pattern, _row, _tmode, _edge;
        NumericUpDown _param, _slow, _oeoff, _twidth;
        CheckBox[] _mask = new CheckBox[4];
        CheckBox _oeinv, _latinv, _datainv;
        Button _flash, _connect;
        Label _state, _status;
        TextBox _out;
        SerialPort _sp;
        bool _busy;

        static string AppDir => AppDomain.CurrentDomain.BaseDirectory;
        static string BossacPath => Environment.GetEnvironmentVariable("CST_BOSSAC") ?? Path.Combine(AppDir, "bossac", "bossac.exe");
        static string UnoFw => Path.Combine(AppDir, "firmware", "uno_panel.bin");

        static readonly (string uno, string pin, string sig)[] Wires =
        {
            ("GND (top row, next to 13)", "15", "Ground: plain wire, NO resistor"),
            ("2", "16", "Clock"),
            ("3", "6", "Latch"),
            ("4", "5", "OE (blanking)"),
            ("5", "7", "Data 1"),
            ("6", "9 and 13", "Timing 1 (one wire to pin 9, plus a short link from 9 to 13)"),
            ("7", "2", "Timing 2"),
            ("10", "8", "Data 2"),
            ("11", "4", "Row B"),
            ("12", "1", "Row C"),
            ("13", "3", "Row A"),
            ("A4", "10", "Data 3"),
            ("A5", "11", "Data 4"),
            ("8, 9, 0, 1, A0-A3", "-", "Not used"),
        };

        public UnoTab(Ctx ctx)
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            Controls.Add(root);

            // ---------------- left: what this is + wiring ----------------
            var left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            root.Controls.Add(left, 0, 0);
            Label H(string t) => new Label { Text = t, AutoSize = true, Font = new Font(Font.FontFamily, 10f, FontStyle.Bold), Margin = new Padding(0, 8, 0, 3) };
            Label P(string t) => new Label { Text = t, AutoSize = true, MaximumSize = new Size(600, 0), Margin = new Padding(0, 1, 0, 3) };

            left.Controls.Add(H("Drive panel 2 from the UNO R4 WiFi (no soldering)"));
            left.Controls.Add(P(
                "The UNO R4 WiFi (Arduino or Elegoo) has enough header pins for all 12 panel signals, so nothing needs soldering. " +
                "This tab installs the Uno's firmware, plays back the sign's own signals (rebuilt from your recordings) and sends test patterns. " +
                "The ESP32 is not used on this tab, so you can leave it unplugged."));

            left.Controls.Add(H("Step 1: install the Uno firmware (once)"));
            left.Controls.Add(P(
                "1. Plug the Uno into this PC with a USB-C data cable. Nothing else needs to be connected to it yet.\n" +
                "2. On the right, press Refresh and pick the Uno's port. It is marked \"likely the Uno\".\n" +
                "3. Press Install Uno firmware. When it asks, double-press the Uno's RESET button (two quick presses): the L light next to pin 13 slowly fades in and out. Wait until it says Done.\n" +
                "If it fails: press the Uno's RESET button twice quickly, so the L light (next to pin 13) slowly fades in and out, then press Install within 5 seconds."));

            left.Controls.Add(H("Step 2: wiring (sign unplugged, Uno unplugged)"));
            left.Controls.Add(P(
                "Wire into the END of your ribbon plug, the same way as before, and plug it onto panel 2's input header. The grey ribbon stays off panel 1, and the row processors stay in.\n" +
                "Every signal goes Uno pin → one 1 kΩ resistor (brown-black-red) → panel pin. Ground goes straight from Uno GND to panel pin 15 with no resistor.\n" +
                "Use 1 kΩ, NOT 220 Ω: the Uno's outputs are 5 V and the 1 kΩ keeps the panel's inputs safe.\n" +
                "Take OUT all the old test resistors (pins 9, 13, 2, 12, 14 to GND or 3V3). Pins 12 and 14 stay empty."));

            var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single, Margin = new Padding(0, 4, 0, 4) };
            void Cell(string t, int col, int row, bool bold = false) =>
                grid.Controls.Add(new Label { Text = t, AutoSize = true, MaximumSize = new Size(330, 0), Margin = new Padding(4, 3, 8, 3), Font = bold ? new Font(Font, FontStyle.Bold) : Font }, col, row);
            Cell("Uno pin (printed on the board)", 0, 0, true); Cell("Panel pin", 1, 0, true); Cell("Signal", 2, 0, true);
            for (int i = 0; i < Wires.Length; i++) { Cell(Wires[i].uno, 0, i + 1, true); Cell(Wires[i].pin, 1, i + 1); Cell(Wires[i].sig, 2, i + 1); }
            left.Controls.Add(grid);

            var pics = new FlowLayoutPanel { AutoSize = true };
            pics.Controls.Add(PicButton("Show wiring picture", "uno-wiring.png", "Uno R4 wiring to panel 2"));
            pics.Controls.Add(PicButton("Show the pins on the Uno photo", "uno-photo-pins.png", "Uno R4 pins"));
            left.Controls.Add(pics);

            left.Controls.Add(H("Step 3: test"));
            left.Controls.Add(P(
                "4. Plug the Uno into the PC, press Connect on the right, then plug the sign in.\n" +
                "5. Press Play the recording first: panel 2 (and the panels after it) should show part of the sign's message. Then try Start test with All on.\n" +
                "6. Nothing lights? Try these one at a time, a few seconds each: Clock edge = falling; OE inverted; Latch inverted; Timing pins = always low, then always high; Slow down = 20.\n" +
                "Take a phone photo of anything that lights and send it over, together with which settings were on."));

            // ---------------- right: install, connect, controls ----------------
            var right = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            root.Controls.Add(right, 1, 0);

            right.Controls.Add(new Label { Text = "Uno port", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 6, 0, 2) });
            var prow = new FlowLayoutPanel { AutoSize = true };
            _ports = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
            var refresh = new Button { Text = "Refresh", AutoSize = true };
            refresh.Click += (s, e) => RefreshPorts();
            prow.Controls.Add(_ports); prow.Controls.Add(refresh);
            right.Controls.Add(prow);

            var brow = new FlowLayoutPanel { AutoSize = true };
            _flash = new Button { Text = "Install Uno firmware", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            _flash.Click += async (s, e) => await Flash();
            _connect = new Button { Text = "Connect", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            _connect.Click += async (s, e) => await ToggleConnect();
            brow.Controls.Add(_flash); brow.Controls.Add(_connect);
            right.Controls.Add(brow);
            _state = new Label { AutoSize = true, MaximumSize = new Size(470, 0), Text = "Not connected.", Margin = new Padding(3, 2, 0, 6) };
            right.Controls.Add(_state);

            right.Controls.Add(new Label { Text = "Test pattern", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 8, 0, 2) });
            var run = new FlowLayoutPanel { AutoSize = true };
            var start = new Button { Text = "▶ Start test", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            start.Click += async (s, e) => await Cmd("PTEST ON " + AllKeys());
            var stop = new Button { Text = "■ Stop", AutoSize = true, ForeColor = Color.DarkRed };
            stop.Click += async (s, e) => await Cmd("STOP");
            var st = new Button { Text = "Refresh status", AutoSize = true };
            st.Click += async (s, e) => await Cmd("STATUS");
            run.Controls.Add(start); run.Controls.Add(stop); run.Controls.Add(st);
            right.Controls.Add(run);
            var playRow = new FlowLayoutPanel { AutoSize = true };
            var play = new Button { Text = "▶ Play the recording", AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = Color.DarkGreen };
            play.Click += async (s, e) => await PlayRecording();
            playRow.Controls.Add(play);
            playRow.Controls.Add(new Label { Text = "Sends the sign's own signals, rebuilt from your recordings, to panel 2.", AutoSize = true, MaximumSize = new Size(300, 0), Margin = new Padding(4, 4, 0, 0) });
            right.Controls.Add(playRow);
            _status = new Label { AutoSize = true, MaximumSize = new Size(470, 0), Text = "", Margin = new Padding(3, 2, 0, 6) };
            right.Controls.Add(_status);

            var form = new TableLayoutPanel { AutoSize = true, ColumnCount = 2 };
            void Row(string label, Control c) { form.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 7, 6, 0) }); form.Controls.Add(c); }
            _pattern = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            _pattern.Items.AddRange(new object[] { "Off (all dark)", "All on", "One bit", "First N bits", "Every 8th bit", "Checker", "First half" });
            _pattern.SelectedIndex = 1;
            _pattern.SelectedIndexChanged += async (s, e) => await Set("pattern", _pattern.SelectedIndex);
            Row("Pattern:", _pattern);
            _param = Num(0, 2000, 0); _param.ValueChanged += async (s, e) => await Set("param", (int)_param.Value);
            Row("Bit number / count:", _param);
            var maskRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
            string[] mp = { "pin 7", "pin 8", "pin 10", "pin 11" };
            for (int i = 0; i < 4; i++)
            {
                _mask[i] = new CheckBox { Text = mp[i], AutoSize = true, Checked = i == 0 };
                _mask[i].CheckedChanged += async (s, e) => await Set("mask", Mask());
                maskRow.Controls.Add(_mask[i]);
            }
            Row("Data lines:", maskRow);
            _row = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
            _row.Items.Add("All rows"); for (int i = 0; i < 8; i++) _row.Items.Add($"Only row {i}");
            _row.SelectedIndex = 0;
            _row.SelectedIndexChanged += async (s, e) => await Set("row", _row.SelectedIndex - 1);
            Row("Rows:", _row);
            _edge = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            _edge.Items.AddRange(new object[] { "Rising (normal)", "Falling" });
            _edge.SelectedIndex = 0;
            _edge.SelectedIndexChanged += async (s, e) => await Set("edge", _edge.SelectedIndex);
            Row("Clock edge:", _edge);
            _tmode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            _tmode.Items.AddRange(new object[] { "Pulse low after latch", "Always low", "Always high" });
            _tmode.SelectedIndex = 0;
            _tmode.SelectedIndexChanged += async (s, e) => await Set("tmode", _tmode.SelectedIndex);
            Row("Timing pins (9+13, 2):", _tmode);
            _oeoff = Num(3, 2000, 150); _oeoff.ValueChanged += async (s, e) => await Set("oeoff", (int)_oeoff.Value);
            Row("OE off at clock:", _oeoff);
            _twidth = Num(1, 2000, 30); _twidth.ValueChanged += async (s, e) => await Set("twidth", (int)_twidth.Value);
            Row("Timing pins low for (clocks):", _twidth);
            _slow = Num(0, 100000, 0); _slow.ValueChanged += async (s, e) => await Set("slow", (int)_slow.Value);
            Row("Slow down (0 = fastest):", _slow);
            var inv = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
            _oeinv = new CheckBox { Text = "OE inverted", AutoSize = true };
            _latinv = new CheckBox { Text = "Latch inverted", AutoSize = true };
            _datainv = new CheckBox { Text = "Data inverted", AutoSize = true };
            _oeinv.CheckedChanged += async (s, e) => await Set("oeinv", _oeinv.Checked ? 1 : 0);
            _latinv.CheckedChanged += async (s, e) => await Set("latinv", _latinv.Checked ? 1 : 0);
            _datainv.CheckedChanged += async (s, e) => await Set("datainv", _datainv.Checked ? 1 : 0);
            inv.Controls.Add(_oeinv); inv.Controls.Add(_latinv); inv.Controls.Add(_datainv);
            Row("Flip:", inv);
            right.Controls.Add(form);

            right.Controls.Add(new Label { Text = "Log", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 8, 0, 2) });
            _out = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = 470, Height = 170, Font = new Font(FontFamily.GenericMonospace, 8.5f) };
            right.Controls.Add(_out);

            RefreshPorts();
        }

        Button PicButton(string text, string res, string title)
        {
            var b = new Button { Text = text, AutoSize = true };
            b.Click += (s, e) =>
            {
                using var st = typeof(UnoTab).Assembly.GetManifestResourceStream(res);
                if (st == null) return;
                Image img; using (var tmp = Image.FromStream(st)) img = new Bitmap(tmp);
                var frm = new Form { Text = title, ClientSize = new Size(Math.Min(img.Width, 1300), Math.Min(img.Height, 840)), StartPosition = FormStartPosition.CenterParent };
                frm.Controls.Add(new PictureBox { Dock = DockStyle.Fill, Image = img, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White });
                frm.FormClosed += (s2, e2) => img.Dispose();
                frm.Show(FindForm());
            };
            return b;
        }

        bool ctxHoldsPort(string port) { try { using var t = new SerialPort(port); t.Open(); t.Close(); return false; } catch { return true; } }

        static NumericUpDown Num(int min, int max, int val) => new NumericUpDown { Minimum = min, Maximum = max, Value = val, Width = 80 };
        int Mask() { int m = 0; for (int i = 0; i < 4; i++) if (_mask[i].Checked) m |= 1 << i; return m; }

        string AllKeys() =>
            $"pattern={_pattern.SelectedIndex} param={(int)_param.Value} mask={Mask()} row={_row.SelectedIndex - 1} edge={_edge.SelectedIndex} " +
            $"tmode={_tmode.SelectedIndex} oeoff={(int)_oeoff.Value} twidth={(int)_twidth.Value} slow={(int)_slow.Value} " +
            $"oeinv={(_oeinv.Checked ? 1 : 0)} latinv={(_latinv.Checked ? 1 : 0)} datainv={(_datainv.Checked ? 1 : 0)}";

        void Log(string s)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => Log(s))); return; }
            _out.AppendText(s + Environment.NewLine);
        }

        void RefreshPorts()
        {
            var ports = PortInfo.Scan(out _);
            // Arduino's USB id is 2341; put those first.
            var sorted = ports.OrderByDescending(p => p.Vid == "2341").ToList();
            _ports.Items.Clear();
            foreach (var p in sorted) _ports.Items.Add(new UnoPort(p));
            if (_ports.Items.Count > 0) _ports.SelectedIndex = 0;
        }

        class UnoPort
        {
            public PortInfo P;
            public UnoPort(PortInfo p) { P = p; }
            public override string ToString() =>
                P.Vid == "2341" ? $"{P.Port}  {P.Name.Replace($"({P.Port})", "").Trim()}  ← likely the Uno" : P.ToString().Replace("← likely the ESP32", "(ESP32?)");
        }

        string SelectedPort => (_ports.SelectedItem as UnoPort)?.P.Port;

        // ---------------- install ----------------
        async Task Flash()
        {
            if (_busy) return;
            var port = SelectedPort;
            if (port == null) { MessageBox.Show(this, "Plug the Uno in and press Refresh first.", "No port"); return; }
            if (!File.Exists(BossacPath) || !File.Exists(UnoFw))
            { MessageBox.Show(this, "The Uno installer files are missing next to the app (bossac\\bossac.exe and firmware\\uno_panel.bin). Re-download the app with the Update button.", "Missing files"); return; }
            Disconnect();
            _busy = true; _flash.Enabled = false;
            _state.ForeColor = SystemColors.ControlText; _state.Text = "Installing… don't unplug the Uno.";
            try
            {
                foreach (var pi in PortInfo.Scan(out _)) Log($"  port {pi.Port}: {pi.Name} (USB id {(pi.Vid.Length > 0 ? pi.Vid : "?")})");
                if (ctxHoldsPort(port)) Log("  Note: the top bar may be connected to this port; press Disconnect at the top first if the install fails.");
                string args() => $"-d --port={port} -U -e -w \"{UnoFw}\" -R";
                // Route 1: the Uno may already be in install mode (RESET pressed twice: the L light fades in and out).
                // Keep trying for about 20 seconds so the RESET double-press can happen at any time.
                _state.ForeColor = Color.DarkOrange;
                _state.Text = "Now double-press the Uno's RESET button (two quick presses). The L light next to pin 13 should slowly fade in and out.";
                int code = -1;
                var until = DateTime.Now.AddSeconds(20);
                int n = 0;
                while (DateTime.Now < until)
                {
                    Log($"Try {++n}: waiting for the Uno's install mode (double-press RESET)…");
                    code = await RunBossac(args());
                    if (code == 0) break;
                    await Task.Delay(300);
                }
                _state.ForeColor = SystemColors.ControlText; _state.Text = "Installing… don't unplug the Uno.";
                if (code != 0)
                {
                    // Route 2: ask it to restart into install mode, the way the Arduino IDE does (1200 baud, DTR drop).
                    Log($"Try 2: asking the Uno on {port} to restart into install mode…");
                    await Task.Run(() =>
                    {
                        try
                        {
                            using var sp = new SerialPort(port, 1200) { DtrEnable = true, RtsEnable = true };
                            sp.Open(); System.Threading.Thread.Sleep(50);
                            sp.DtrEnable = false; sp.RtsEnable = false; System.Threading.Thread.Sleep(50);
                            sp.Close();
                        }
                        catch (Exception ex) { Log("  (restart request: " + ex.Message + ")"); }
                        System.Threading.Thread.Sleep(2500);
                    });
                    var again = PortInfo.Scan(out _);
                    if (!again.Any(p => p.Port == port))
                    {
                        var alt = again.FirstOrDefault(p => p.Vid == "2341");
                        if (alt != null) { Log($"The Uno moved to {alt.Port}."); port = alt.Port; }
                    }
                    code = await RunBossac(args());
                    if (code != 0) { await Task.Delay(2000); Log("Try 3…"); code = await RunBossac(args()); }
                }
                if (code == 0)
                {
                    _state.ForeColor = Color.DarkGreen;
                    _state.Text = "Done. The Uno firmware is installed. Press Connect.";
                    Log("Done.");
                }
                else
                {
                    _state.ForeColor = Color.DarkRed;
                    _state.Text = "Install failed. Do this: press the Uno's RESET button twice quickly, so the L light (next to pin 13) slowly fades in and out. Then press Install within 5 seconds.";
                }
            }
            finally { _busy = false; _flash.Enabled = true; RefreshPorts(); }
        }

        // bossac keeps whatever line settings the port had last. The Uno's USB chip only answers the PC while
        // DTR is on, so switch DTR/RTS on (and flow control off) before every bossac run.
        static void PrepPort(string port)
        {
            try
            {
                using var sp = new SerialPort(port, 115200) { DtrEnable = true, RtsEnable = true, Handshake = Handshake.None };
                sp.Open(); System.Threading.Thread.Sleep(30); sp.Close();
            }
            catch { }
        }

        Task<int> RunBossac(string args)
        {
            var m = System.Text.RegularExpressions.Regex.Match(args, @"--port=(\S+)");
            if (m.Success) PrepPort(m.Groups[1].Value);
            Log("bossac " + args);
            var tcs = new TaskCompletionSource<int>();
            var psi = new ProcessStartInfo(BossacPath, args)
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            try
            {
                var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                p.OutputDataReceived += (s, e) => { if (e.Data != null && !e.Data.StartsWith("[")) Log(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) Log(e.Data); };
                p.Exited += (s, e) => { p.WaitForExit(); tcs.TrySetResult(p.ExitCode); };
                p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
            }
            catch (Exception ex) { Log("Could not start bossac: " + ex.Message); tcs.TrySetResult(-1); }
            return tcs.Task;
        }

        // ---------------- talk to the Uno ----------------
        async Task ToggleConnect()
        {
            if (_sp != null) { Disconnect(); return; }
            var port = SelectedPort;
            if (port == null) { MessageBox.Show(this, "Plug the Uno in and press Refresh first.", "No port"); return; }
            try
            {
                _sp = new SerialPort(port, 115200) { NewLine = "\n", ReadTimeout = 2500, WriteTimeout = 2000, DtrEnable = true };
                _sp.Open();
                await Task.Delay(1500);   // the Uno may restart when the port opens
                _sp.DiscardInBuffer();
                var r = await Ask("HELLO");
                if (r == null || !r.StartsWith("OK uno_panel"))
                {
                    _state.ForeColor = Color.DarkRed;
                    _state.Text = "Connected to " + port + " but the Uno firmware didn't answer. Press Install Uno firmware first.";
                    Disconnect(keepText: true);
                    return;
                }
                _connect.Text = "Disconnect";
                _state.ForeColor = Color.DarkGreen;
                _state.Text = "Connected: " + r.Substring(3);
                await Cmd("STATUS");
            }
            catch (Exception ex)
            {
                _state.ForeColor = Color.DarkRed;
                _state.Text = "Couldn't open " + port + ": " + ex.Message + " (close the Arduino IDE or anything else using it).";
                Disconnect(keepText: true);
            }
        }

        void Disconnect(bool keepText = false)
        {
            try { _sp?.Close(); } catch { }
            _sp = null;
            _connect.Text = "Connect";
            if (!keepText) { _state.ForeColor = SystemColors.ControlText; _state.Text = "Not connected."; }
        }

        readonly System.Threading.SemaphoreSlim _lock = new System.Threading.SemaphoreSlim(1, 1);

        async Task<string> Ask(string cmd)
        {
            if (_sp == null) return null;
            await _lock.WaitAsync();
            try
            {
                return await Task.Run(() =>
                {
                    try { _sp.WriteLine(cmd); return _sp.ReadLine().Trim(); }
                    catch (Exception ex) { return "ERR " + ex.Message; }
                });
            }
            finally { _lock.Release(); }
        }

        async Task Cmd(string cmd)
        {
            if (_sp == null) { _status.ForeColor = Color.DarkRed; _status.Text = "Press Connect first."; return; }
            var r = await Ask(cmd);
            Log("> " + cmd); Log("< " + r);
            if (r == null) return;
            if (r.StartsWith("ERR")) { _status.ForeColor = Color.DarkRed; _status.Text = "⚠ " + r.Substring(3).Trim(); return; }
            string Get(string k) { var t = r.Split(' ').FirstOrDefault(x => x.StartsWith(k + "=")); return t?.Substring(k.Length + 1) ?? ""; }
            var mode = Get("mode");
            _status.ForeColor = mode == "stop" ? SystemColors.ControlText : Color.DarkGreen;
            _status.Text = mode == "test" ? $"Running test pattern: {Get("frames")} frames sent so far."
                         : mode == "replay" ? $"Playing the recording: {Get("frames")} frames sent so far."
                         : "Stopped (all outputs off).";
        }

        static string ReplayPath => Path.Combine(AppDir, "firmware", "uno-replay.txt");

        async Task PlayRecording()
        {
            if (_sp == null) { _status.ForeColor = Color.DarkRed; _status.Text = "Press Connect first."; return; }
            if (!File.Exists(ReplayPath)) { _status.ForeColor = Color.DarkRed; _status.Text = "firmware\\uno-replay.txt is missing: press Updates."; return; }
            var words = File.ReadAllText(ReplayPath).Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            _status.ForeColor = SystemColors.ControlText; _status.Text = $"Sending the recording ({words.Length} clock steps)…";
            var r = await Ask("LOAD " + words.Length);
            Log("> LOAD " + words.Length); Log("< " + r);
            if (r == null || !r.StartsWith("OK")) { _status.ForeColor = Color.DarkRed; _status.Text = "⚠ " + r; return; }
            for (int i = 0; i < words.Length; i += 32)
            {
                var line = "W " + i + " " + string.Join(" ", words.Skip(i).Take(32));
                r = await Ask(line);
                if (r == null || !r.StartsWith("OK")) { Log("< " + r); _status.ForeColor = Color.DarkRed; _status.Text = "⚠ sending failed: " + r; return; }
            }
            await Cmd($"PLAY slow={(int)_slow.Value} edge={_edge.SelectedIndex}");
        }

        async Task Set(string key, int val)
        {
            if (_sp == null) return;
            await Cmd($"PTEST SET {key}={val}");
        }

        protected override void Dispose(bool disposing) { if (disposing) Disconnect(true); base.Dispose(disposing); }
    }
}
