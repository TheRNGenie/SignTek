using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CasinoSignTool
{
    /// <summary>Drive panel 2's input straight from the ESP32 with test patterns (firmware 3.4.0, PTEST command).</summary>
    public class PanelTestTab : UserControl
    {
        readonly Ctx _ctx;
        ComboBox _pattern, _row, _tmode;
        NumericUpDown _param, _bits, _oeoff, _twidth, _slow;
        CheckBox[] _mask = new CheckBox[4];
        CheckBox _oeinv, _latinv, _datainv;
        Label _paramLbl, _status;
        bool _running, _loading;

        static readonly (string pin, string sig, string gpio)[] Wires =
        {
            ("16", "CLK (clock)", "GPIO 18: SPI plug, IO18"),
            ("6", "LAT (latch)", "GPIO 19: SPI plug, IO19"),
            ("5", "OE (blanking)", "GPIO 21: SPI plug, IO21"),
            ("3", "Row A", "GPIO 32: I2C plug, IO32"),
            ("4", "Row B", "GPIO 25: I2C plug, IO25"),
            ("1", "Row C", "First test: 220 Ω to GND (no solder). Later: GPIO 0, BOOT button pad"),
            ("7", "Data 1", "GPIO 23: SPI plug, IO23"),
            ("8", "Data 2", "GPIO 22: RGB LED pad IO22"),
            ("10", "Data 3", "GPIO 17: RGB LED pad IO17"),
            ("11", "Data 4", "GPIO 26: speaker pad IO26"),
            ("9 + 13", "Colour timing 1 (both pins)", "GPIO 16: RGB LED pad IO16"),
            ("2", "Colour timing 2", "GPIO 4: speaker pad IO4"),
            ("15", "GND", "ESP32 GND (I2C plug, pin printed GND)"),
            ("12, 14", "leave unconnected", ""),
        };

        public PanelTestTab(Ctx ctx)
        {
            _ctx = ctx;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            Controls.Add(root);

            // ---- Left: wiring + steps ----
            var left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            root.Controls.Add(left, 0, 0);
            Label H(string t) => new Label { Text = t, AutoSize = true, Font = new Font(Font.FontFamily, 10f, FontStyle.Bold), Margin = new Padding(0, 8, 0, 3) };
            Label P(string t) => new Label { Text = t, AutoSize = true, MaximumSize = new Size(600, 0), Margin = new Padding(0, 1, 0, 3) };

            left.Controls.Add(H("Drive panel 2 straight from the ESP32"));
            left.Controls.Add(P(
                "This sends simple test patterns into the panels so we can check the signal format. Panel 1 keeps showing the row processor's message; panel 2 and the panels after it show the ESP32's pattern. Needs sign firmware 3.4.0 or newer (tab 0)."));
            left.Controls.Add(H("Wiring (sign unplugged)"));
            left.Controls.Add(P(
                "1. Leave the grey ribbon between panel 1 and panel 2 unplugged, and leave the row processors in.\n" +
                "2. Wire to the SECOND panel's INPUT header: the LEFT header of the pair (next to U2 and U3), where the other end of that ribbon plugged in. Same layout as the right-hand header: ground (15) and clock (16) at the BOTTOM, pins 1 and 2 at the top. Looking at it, ground (15) is bottom-RIGHT and clock (16) bottom-left, so odd pins are in the right-hand column (checked with the beep test: the bottom-right pin beeps to the big capacitor's minus leg). Do NOT use the header next to U34, U24 and U29: that is panel 1's output, it is already driven and the two would fight.\n" +
                "3. Each signal: ESP32 pin → one 220 Ω resistor (red-red-brown) → panel pin. Ground: ESP32 GND → panel pin 15, no resistor.\n" +
                "4. No 10 kΩ on OE for this test.\n" +
                "5. The first 7 rows below (pins 16, 6, 5, 3, 4, 1, 7) are enough to start. The ESP32's plugs give 6 outputs (IO18, 19, 21, 23, 32, 25; IO35 and IO39 can only listen), so for the first test wire pin 1 (row C) to GND through a 220 Ω: only half the rows light, which is enough. The other rows need soldering to board pads; add them only when needed."));

            var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single, Margin = new Padding(0, 4, 0, 4) };
            void Cell(string t, int col, int row, bool bold = false) =>
                grid.Controls.Add(new Label { Text = t, AutoSize = true, Margin = new Padding(4, 3, 8, 3), Font = bold ? new Font(Font, FontStyle.Bold) : Font }, col, row);
            Cell("Panel pin", 0, 0, true); Cell("Signal", 1, 0, true); Cell("ESP32 (where)", 2, 0, true);
            for (int i = 0; i < Wires.Length; i++) { Cell(Wires[i].pin, 0, i + 1, i < 7); Cell(Wires[i].sig, 1, i + 1); Cell(Wires[i].gpio, 2, i + 1); }
            left.Controls.Add(grid);
            var picBtn = new Button { Text = "Show test wiring picture", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            picBtn.Click += (s, e) =>
            {
                using var st = typeof(PanelTestTab).Assembly.GetManifestResourceStream("panel-test-wiring.png");
                if (st == null) return;
                Image img; using (var tmp = Image.FromStream(st)) img = new Bitmap(tmp);
                var frm = new Form { Text = "Panel test wiring", ClientSize = new Size(Math.Min(img.Width, 1300), Math.Min(img.Height, 840)), StartPosition = FormStartPosition.CenterParent };
                frm.Controls.Add(new PictureBox { Dock = DockStyle.Fill, Image = img, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White });
                frm.FormClosed += (s2, e2) => img.Dispose();
                frm.Show(FindForm());
            };
            left.Controls.Add(picBtn);

            left.Controls.Add(H("Then"));
            left.Controls.Add(P("6. Plug the sign in, connect the ESP32 (USB or WiFi, top bar) and press Start test on the right. Start with All on."));

            left.Controls.Add(H("What to look for"));
            left.Controls.Add(P(
                "- All on: panel 2 and the panels after it should light. The colour tells you which colour the ticked data line is: tick one data line at a time.\n" +
                "- Nothing lights? Try these one at a time: tick OE inverted; then Latch inverted; then Timing pins = always low, then always high (needs the pins 9+13 and 2 wires); then set Slow down to 50.\n" +
                "- One bit + the bit buttons: a single dot or column moves. Note which column each bit number lands in and which way it moves.\n" +
                "- Only row: lights one row address at a time, which checks A, B and C.\n" +
                "- Checker: a chess-board pattern. If it looks like stripes instead, the bits are out of step.\n" +
                "Note down what you see for each test (a phone photo is perfect) so it can be built into the real display driver."));

            // ---- Right: controls ----
            var right = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            root.Controls.Add(right, 1, 0);

            var runRow = new FlowLayoutPanel { AutoSize = true };
            var start = new Button { Text = "▶ Start test", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            start.Click += async (s, e) => await Start();
            var stop = new Button { Text = "■ Stop", AutoSize = true, ForeColor = Color.DarkRed };
            stop.Click += async (s, e) => { var r = await _ctx.Try("PTEST OFF"); if (r != null) { _running = false; Show(r); } };
            var refresh = new Button { Text = "Refresh status", AutoSize = true };
            refresh.Click += async (s, e) => Show(await _ctx.Try("PTEST"));
            runRow.Controls.Add(start); runRow.Controls.Add(stop); runRow.Controls.Add(refresh);
            right.Controls.Add(runRow);
            _status = new Label { AutoSize = true, MaximumSize = new Size(480, 0), Text = "Not running.", Margin = new Padding(3, 2, 0, 6) };
            right.Controls.Add(_status);

            TableLayoutPanel form = new TableLayoutPanel { AutoSize = true, ColumnCount = 2 };
            void Row(string label, Control c) { form.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 7, 6, 0) }); form.Controls.Add(c); }

            _pattern = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
            _pattern.Items.AddRange(new object[] { "Off (all dark)", "All on", "One bit", "First N bits", "Every 8th bit", "Checker", "First half" });
            _pattern.SelectedIndex = 1;
            _pattern.SelectedIndexChanged += async (s, e) => { UpdateParamLabel(); await Send("pattern", _pattern.SelectedIndex); };
            Row("Pattern:", _pattern);

            _paramLbl = new Label { Text = "Bit number:", AutoSize = true, Margin = new Padding(0, 7, 6, 0) };
            _param = Num(0, 2000, 0);
            _param.ValueChanged += async (s, e) => await Send("param", (int)_param.Value);
            form.Controls.Add(_paramLbl); form.Controls.Add(_param);

            var walk = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
            void W(string t, int d) { var b = new Button { Text = t, AutoSize = true }; b.Click += async (s, e) => await Walk(d); walk.Controls.Add(b); }
            W("-8", -8); W("◀ bit -1", -1); W("bit +1 ▶", 1); W("+8", 8);
            Row("Walk:", walk);

            var maskRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
            string[] mp = { "pin 7", "pin 8", "pin 10", "pin 11" };
            for (int i = 0; i < 4; i++)
            {
                _mask[i] = new CheckBox { Text = mp[i], AutoSize = true, Checked = i == 0 };
                _mask[i].CheckedChanged += async (s, e) => await Send("mask", Mask());
                maskRow.Controls.Add(_mask[i]);
            }
            Row("Data lines:", maskRow);

            _row = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
            _row.Items.Add("All rows");
            for (int i = 0; i < 8; i++) _row.Items.Add($"Only row {i}");
            _row.SelectedIndex = 0;
            _row.SelectedIndexChanged += async (s, e) => await Send("row", _row.SelectedIndex - 1);
            Row("Rows:", _row);
            right.Controls.Add(form);

            right.Controls.Add(new Label { Text = "Timing (leave as is unless nothing lights)", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 10, 0, 2) });
            var form2 = new TableLayoutPanel { AutoSize = true, ColumnCount = 2 };
            void Row2(string label, Control c) { form2.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 7, 6, 0) }); form2.Controls.Add(c); }
            _bits = Num(8, 2000, 246); _bits.ValueChanged += async (s, e) => await Send("bits", (int)_bits.Value);
            Row2("Clocks per row:", _bits);
            _oeoff = Num(3, 2000, 150); _oeoff.ValueChanged += async (s, e) => await Send("oeoff", (int)_oeoff.Value);
            Row2("OE off at clock:", _oeoff);
            _twidth = Num(1, 2000, 30); _twidth.ValueChanged += async (s, e) => await Send("twidth", (int)_twidth.Value);
            Row2("Timing pins low for (clocks):", _twidth);
            _tmode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            _tmode.Items.AddRange(new object[] { "Pulse low after latch", "Always low", "Always high" });
            _tmode.SelectedIndex = 0;
            _tmode.SelectedIndexChanged += async (s, e) => await Send("tmode", _tmode.SelectedIndex);
            Row2("Timing pins (9+13, 2):", _tmode);
            _slow = Num(0, 10000, 0); _slow.ValueChanged += async (s, e) => await Send("slow", (int)_slow.Value);
            Row2("Slow down (0 = fastest):", _slow);
            var inv = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
            _oeinv = new CheckBox { Text = "OE inverted", AutoSize = true };
            _latinv = new CheckBox { Text = "Latch inverted", AutoSize = true };
            _datainv = new CheckBox { Text = "Data inverted", AutoSize = true };
            _oeinv.CheckedChanged += async (s, e) => await Send("oeinv", _oeinv.Checked ? 1 : 0);
            _latinv.CheckedChanged += async (s, e) => await Send("latinv", _latinv.Checked ? 1 : 0);
            _datainv.CheckedChanged += async (s, e) => await Send("datainv", _datainv.Checked ? 1 : 0);
            inv.Controls.Add(_oeinv); inv.Controls.Add(_latinv); inv.Controls.Add(_datainv);
            Row2("Flip:", inv);
            right.Controls.Add(form2);
            right.Controls.Add(new Label { Text = "Stop the test before going back to the normal display (MODE RUN or Run sign in the top bar).", AutoSize = true, MaximumSize = new Size(460, 0), ForeColor = Color.DimGray, Margin = new Padding(0, 10, 0, 0) });
            UpdateParamLabel();
        }

        static NumericUpDown Num(int min, int max, int val) => new NumericUpDown { Minimum = min, Maximum = max, Value = val, Width = 80 };

        int Mask() { int m = 0; for (int i = 0; i < 4; i++) if (_mask[i].Checked) m |= 1 << i; return m; }

        void UpdateParamLabel()
        {
            _paramLbl.Text = _pattern.SelectedIndex switch { 2 => "Bit number:", 3 => "How many bits:", 4 => "Start at bit:", _ => "Number (not used):" };
        }

        string AllKeys() =>
            $"pattern={_pattern.SelectedIndex} param={(int)_param.Value} mask={Mask()} row={_row.SelectedIndex - 1} bits={(int)_bits.Value} " +
            $"oeoff={(int)_oeoff.Value} twidth={(int)_twidth.Value} tmode={_tmode.SelectedIndex} oeinv={(_oeinv.Checked ? 1 : 0)} " +
            $"latinv={(_latinv.Checked ? 1 : 0)} datainv={(_datainv.Checked ? 1 : 0)} slow={(int)_slow.Value}";

        async Task Start()
        {
            var r = await _ctx.Try("PTEST ON " + AllKeys());
            if (r == null) return;
            if (!r.Ok && r.Has("err"))
            {
                _status.ForeColor = Color.DarkRed;
                _status.Text = r.Err.Contains("unknown") ? "The board doesn't know PTEST: install firmware 3.4.0 or newer in tab 0." : "⚠ " + r.Err;
                return;
            }
            _running = true; Show(r);
        }

        async Task Send(string key, int val)
        {
            if (_loading || !_running) return;
            Show(await _ctx.Try($"PTEST SET {key}={val}"));
        }

        async Task Walk(int d)
        {
            _loading = true;
            _pattern.SelectedIndex = 2;
            _param.Value = Math.Max(_param.Minimum, Math.Min(_param.Maximum, _param.Value + d));
            _loading = false;
            UpdateParamLabel();
            if (_running) Show(await _ctx.Try($"PTEST SET pattern=2 param={(int)_param.Value}"));
        }

        void Show(Reply r)
        {
            if (r == null) return;
            if (r.Has("err")) { _status.ForeColor = Color.DarkRed; _status.Text = "⚠ " + r.Err; return; }
            _running = r.Bool("ptest");
            _status.ForeColor = _running ? Color.DarkGreen : SystemColors.ControlText;
            _status.Text = _running
                ? $"Running: pattern {r.Int("pattern")}, bit {r.Int("param")}, {r.Int("frames")} frames sent so far."
                : "Not running.";
        }
    }
}
