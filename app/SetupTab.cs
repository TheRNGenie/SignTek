using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CasinoSignTool
{
    /// <summary>Which ESP32 GPIO drives each sign signal, scan type, polarity, and test patterns.</summary>
    public class SetupTab : UserControl
    {
        readonly Ctx _ctx;
        readonly Dictionary<PinRole, ComboBox> _gpio = new Dictionary<PinRole, ComboBox>();
        readonly Dictionary<PinRole, Label> _hdrLabel = new Dictionary<PinRole, Label>();
        RadioButton _rows8, _rows16;
        CheckBox _oeLow, _dataInv, _addrInv, _revCols, _bottomFirst;
        NumericUpDown _refresh;
        TrackBar _bright;
        NumericUpDown _cap;
        Label _brightVal, _warn, _cfgState;
        bool _loading;

        public SetupTab(Ctx ctx)
        {
            _ctx = ctx;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 580));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            root.Controls.Add(left, 0, 0);
            Label H(string t) => new Label { Text = t, AutoSize = true, Font = new Font(Font.FontFamily, 10f, FontStyle.Bold), Margin = new Padding(0, 10, 0, 4) };

            left.Controls.Add(H("ESP32 GPIO for each sign signal"));
            var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 3 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            foreach (var role in Roles.Signals)
            {
                var sw = new Label { Text = "  " + Roles.Label(role), AutoSize = false, Width = 225, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
                sw.Paint += (s, e) => { using var b = new SolidBrush(Roles.Color(role)); e.Graphics.FillRectangle(b, 0, 4, 6, 14); };
                var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
                cb.SelectedIndexChanged += (s, e) => { if (!_loading) { _ctx.Cfg.Gpio[role] = GpioOf(cb); CheckConflicts(); } };
                _gpio[role] = cb;
                var hl = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 6, 3, 0) };
                _hdrLabel[role] = hl;
                grid.Controls.Add(sw); grid.Controls.Add(cb); grid.Controls.Add(hl);
            }
            left.Controls.Add(grid);
            var useMap = new Button { Text = "Use header map: pick GPIOs automatically", AutoSize = true };
            useMap.Click += (s, e) => AutoAssign();
            left.Controls.Add(useMap);
            _warn = new Label { AutoSize = true, MaximumSize = new Size(540, 0), ForeColor = Color.DarkRed };
            left.Controls.Add(_warn);

            left.Controls.Add(H("Scan type"));
            _rows8 = new RadioButton { Text = "1/8 scan: two rows lit at once (rows r and r+8), 3 address lines. 24 MBI5170 chips.", AutoSize = true, MaximumSize = new Size(540, 0) };
            _rows16 = new RadioButton { Text = "1/16 scan: one row lit at a time, 4 address lines. 12 MBI5170 chips.", AutoSize = true, MaximumSize = new Size(540, 0) };
            _rows8.CheckedChanged += (s, e) => { if (!_loading) _ctx.Cfg.ScanRows = _rows8.Checked ? 8 : 16; CheckConflicts(); };
            left.Controls.Add(_rows8); left.Controls.Add(_rows16);

            left.Controls.Add(H("Fix-ups (flip these if a test pattern looks wrong)"));
            CheckBox C(string t, Action<bool> set) { var c = new CheckBox { Text = t, AutoSize = true, MaximumSize = new Size(540, 0) }; c.CheckedChanged += (s, e) => { if (!_loading) set(c.Checked); }; left.Controls.Add(c); return c; }
            _oeLow = C("OE is active-low (normal for MBI5170)", v => _ctx.Cfg.OeActiveLow = v);
            _dataInv = C("Invert data (lit pixels come out dark)", v => _ctx.Cfg.DataInvert = v);
            _addrInv = C("Reverse row order (picture upside down / rows out of order)", v => _ctx.Cfg.AddrInvert = v);
            _revCols = C("Reverse columns (text mirrored)", v => _ctx.Cfg.ReverseCols = v);
            _bottomFirst = C("1/8 scan with one data line: bottom half is shifted first", v => _ctx.Cfg.ChainBottomFirst = v);

            var tune = new FlowLayoutPanel { AutoSize = true };
            tune.Controls.Add(new Label { Text = "Refresh (Hz):", AutoSize = true, Margin = new Padding(0, 6, 2, 0) });
            _refresh = new NumericUpDown { Minimum = 30, Maximum = 400, Value = 120, Width = 60 };
            _refresh.ValueChanged += (s, e) => { if (!_loading) _ctx.Cfg.RefreshHz = (int)_refresh.Value; };
            tune.Controls.Add(_refresh);
            tune.Controls.Add(new Label { Text = "   Brightness:", AutoSize = true, Margin = new Padding(0, 6, 2, 0) });
            _bright = new TrackBar { Minimum = 5, Maximum = 100, TickFrequency = 10, Width = 160, Value = 100 };
            _brightVal = new Label { AutoSize = true, Margin = new Padding(0, 6, 0, 0), Text = "100%" };
            _bright.ValueChanged += (s, e) => { _brightVal.Text = _bright.Value + "%"; if (!_loading) _ctx.Cfg.Brightness = _bright.Value; };
            tune.Controls.Add(_bright); tune.Controls.Add(_brightVal);
            tune.Controls.Add(new Label { Text = "   Safety max:", AutoSize = true, Margin = new Padding(0, 6, 2, 0) });
            _cap = new NumericUpDown { Minimum = 5, Maximum = 100, Value = 100, Width = 55 };
            _cap.ValueChanged += (s, e) => { if (!_loading) _ctx.Cfg.BrightCap = (int)_cap.Value; };
            tune.Controls.Add(_cap);
            tune.Controls.Add(new Label { Text = "%", AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
            left.Controls.Add(tune);
            left.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = Color.DimGray, Text =
                "Safety max caps the brightness no matter what anything else asks for (playlists, Home Assistant, the console). Lower it if the sign's power supply gets hot or the LEDs look too harsh. " +
                "The board also has a scan watchdog: if the display refresh ever stalls, it blanks the LEDs so no row is left stuck on at full power." });

            var btns = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
            Button B(string t, Func<Task> a, bool bold = false) { var b = new Button { Text = t, AutoSize = true }; if (bold) b.Font = new Font(Font, FontStyle.Bold); b.Click += async (s, e) => await a(); btns.Controls.Add(b); return b; }
            B("Read from board", ReadFromBoard);
            B("Apply (try it now)", () => Apply(false), true);
            B("Save to board", () => Apply(true), true);
            B("Reset to defaults", async () => { var r = await _ctx.Try("DEFAULTS"); if (r != null) { _ctx.Cfg.From(r); _ctx.RaiseCfg(); } });
            left.Controls.Add(btns);
            _cfgState = new Label { AutoSize = true, MaximumSize = new Size(540, 0), ForeColor = Color.DimGray };
            left.Controls.Add(_cfgState);

            // ---- Right: test patterns + what to look for ----
            var right = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(10, 0, 0, 0) };
            root.Controls.Add(right, 1, 0);
            right.Controls.Add(new Label { Text = "Test patterns", AutoSize = true, Font = new Font(Font.FontFamily, 10f, FontStyle.Bold), Margin = new Padding(0, 10, 0, 4) });
            right.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(480, 0), Text = "These run the sign with the settings on the left (Apply first). The board switches to Run mode." });
            var pats = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(480, 0) };
            void Pat(string label, string name)
            {
                var b = new Button { Text = label, AutoSize = true };
                b.Click += async (s, e) =>
                {
                    if (_ctx.Mode != "run" && !await _ctx.SetMode("run")) return;
                    var r = await _ctx.Try("PATTERN " + name);
                    if (r != null) _ctx.Status($"Showing {label}.");
                };
                pats.Controls.Add(b);
            }
            Pat("Corners + arrow", "corners"); Pat("Walk row", "walkrow"); Pat("Walk column", "walkcol");
            Pat("All on (brief!)", "allon"); Pat("Scroll text", "text"); Pat("Blank", "off");
            right.Controls.Add(pats);
            right.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(0, 10, 0, 0),
                Text =
                "CORNERS + ARROW should show a dot top-left, a dot bottom-right and an arrow pointing right.\n" +
                "  • Mirrored → tick Reverse columns.\n" +
                "  • Upside down or rows jumbled → tick Reverse row order, or swap the A/B/C GPIOs.\n" +
                "  • Dark dots on a lit sign → tick Invert data.\n" +
                "  • Only the top or bottom half works → check the scan type and DATA 2.\n" +
                "  • Halves swapped (1/8 scan, one data line) → flip \"bottom half shifted first\".\n\n" +
                "WALK ROW / WALK COLUMN: a single line should move smoothly. Jumps mean swapped address lines.\n\n" +
                "ALL ON draws full current; use it only for a few seconds to spot dead LEDs.\n\n" +
                "Flicker → raise Refresh. Ghosting (faint copy of the row above) → lower Brightness a little, which adds blanking time.\n\n" +
                "When it looks right, press Save to board. From then on the board starts in Run mode on power-up.",
            });

            _ctx.CfgChanged += LoadUi;
            _ctx.MapChanged += UpdateHeaderLabels;
            _ctx.StateChanged += () => { FillCombos(); LoadUi(); };
            FillCombos();
            LoadUi();
        }

        static int ParseItem(object item) => item is string s && int.TryParse(s.Split(' ')[0], out int v) ? v : -1;
        static int GpioOf(ComboBox cb) => ParseItem(cb.SelectedItem);

        void FillCombos()
        {
            _loading = true;
            foreach (var kv in _gpio)
            {
                var cb = kv.Value;
                cb.Items.Clear();
                cb.Items.Add("none");
                foreach (var p in _ctx.OutPins) cb.Items.Add(_ctx.StrapPins.Contains(p) ? $"{p} (boot pin)" : p.ToString());
            }
            _loading = false;
        }

        void LoadUi()
        {
            _loading = true;
            var c = _ctx.Cfg;
            foreach (var kv in _gpio)
            {
                int v = c.Gpio[kv.Key];
                var cb = kv.Value;
                cb.SelectedIndex = 0;
                for (int i = 1; i < cb.Items.Count; i++) if (ParseItem(cb.Items[i]) == v) { cb.SelectedIndex = i; break; }
            }
            _rows8.Checked = c.ScanRows == 8; _rows16.Checked = c.ScanRows == 16;
            _oeLow.Checked = c.OeActiveLow; _dataInv.Checked = c.DataInvert; _addrInv.Checked = c.AddrInvert;
            _revCols.Checked = c.ReverseCols; _bottomFirst.Checked = c.ChainBottomFirst;
            _refresh.Value = Math.Max(30, Math.Min(400, c.RefreshHz));
            _bright.Value = Math.Max(5, Math.Min(100, c.Brightness));
            _cap.Value = Math.Max(5, Math.Min(100, c.BrightCap));
            _cfgState.Text = !_ctx.Board.Connected ? "Not connected: you can still plan the wiring; settings go to the board when you Apply."
                           : c.Configured ? "The board has a saved config, so it starts the sign on power-up."
                           : "The board has no saved config yet, so it boots in Diagnostics mode with the sign pins switched off.";
            _loading = false;
            UpdateHeaderLabels();
            CheckConflicts();
        }

        void UpdateHeaderLabels()
        {
            foreach (var role in Roles.Signals)
            {
                var pins = _ctx.Map.WithRole(role);
                _hdrLabel[role].Text = pins.Count == 0 ? "not found on header yet" : "header pin " + string.Join(", ", pins.Select(p => p.Number));
            }
        }

        void CheckConflicts()
        {
            var w = new List<string>();
            var used = _ctx.Cfg.Gpio.Where(kv => kv.Value >= 0).GroupBy(kv => kv.Value).Where(g => g.Count() > 1);
            foreach (var g in used) w.Add($"GPIO {g.Key} is used for {string.Join(" and ", g.Select(x => Roles.Code(x.Key)))}.");
            if (_ctx.Cfg.Gpio[PinRole.DataTop] < 0 || _ctx.Cfg.Gpio[PinRole.Clk] < 0 || _ctx.Cfg.Gpio[PinRole.Lat] < 0) w.Add("DATA, CLK and LAT need a GPIO or the sign can't run.");
            if (_ctx.Cfg.ScanRows == 16 && _ctx.Cfg.Gpio[PinRole.D] < 0) w.Add("1/16 scan uses 4 address lines, but D has no GPIO.");
            _warn.Text = string.Join("\n", w);
        }

        void AutoAssign()
        {
            // Prefer non-strapping pins in this order, keep the classic layout when possible.
            var preferred = new Dictionary<PinRole, int> { [PinRole.DataTop] = 23, [PinRole.Clk] = 18, [PinRole.Lat] = 19, [PinRole.Oe] = 5, [PinRole.A] = 32, [PinRole.B] = 25, [PinRole.C] = 0, [PinRole.D] = 22, [PinRole.DataBot] = 16 };
            var free = _ctx.OutPins.Where(p => !_ctx.StrapPins.Contains(p)).Concat(_ctx.OutPins.Where(p => _ctx.StrapPins.Contains(p) && p != 0)).ToList();
            var taken = new HashSet<int>();
            var result = new Dictionary<PinRole, int>();
            var needed = Roles.Signals.Where(r => _ctx.Map.WithRole(r).Count > 0).ToList();
            if (needed.Count == 0) { _ctx.Status("The header map has no signal pins yet. Map some pins in tab 1 first."); return; }
            foreach (var r in needed) if (preferred.TryGetValue(r, out int p) && _ctx.OutPins.Contains(p) && taken.Add(p)) result[r] = p;
            foreach (var r in needed.Where(r => !result.ContainsKey(r)))
            {
                int? p = free.Where(x => !taken.Contains(x)).Select(x => (int?)x).FirstOrDefault();
                if (p == null) { _ctx.Status($"Ran out of free GPIOs for {Roles.Code(r)}."); continue; }
                taken.Add(p.Value); result[r] = p.Value;
            }
            foreach (var r in Roles.Signals) _ctx.Cfg.Gpio[r] = result.TryGetValue(r, out int g) ? g : -1;
            _ctx.Cfg.ScanRows = _ctx.Map.WithRole(PinRole.D).Count > 0 ? 16 : 8;
            _ctx.RaiseCfg();
            _ctx.Status($"Picked GPIOs for {result.Count} signals and {_ctx.Cfg.ScanRows}-row scan. Press Apply to try them, then check the Wiring guide.");
        }

        async Task ReadFromBoard()
        {
            var r = await _ctx.Try("GET CFG");
            if (r == null) return;
            _ctx.Cfg.From(r); _ctx.RaiseCfg();
            _ctx.Status("Loaded the sign settings from the board.");
        }

        async Task Apply(bool save)
        {
            if (!_ctx.Board.Connected) { _ctx.Status("Connect to the board first."); return; }
            Reply last = null;
            foreach (var cmd in _ctx.Cfg.ToCommands())
            {
                last = await _ctx.Try(cmd);
                if (last == null) return;   // error already shown
            }
            if (save)
            {
                if (await _ctx.Try("SAVE") == null) return;
                _ctx.Cfg.Configured = true;
            }
            if (last != null) _ctx.Cfg.From(last);
            if (save) _ctx.Cfg.Configured = true;
            LoadUi();
            _ctx.Status(save ? "Saved on the board. It will start the sign with these settings on power-up." : "Applied. Run a test pattern to check (settings aren't saved until you press Save).");
        }
    }
}
