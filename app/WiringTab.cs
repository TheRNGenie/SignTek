using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace CasinoSignTool
{
    /// <summary>One signal path: header pin → 74HCT245 channel → ESP32 GPIO.</summary>
    public class WireRun
    {
        public PinRole Role;
        public List<int> HeaderPins = new List<int>();
        public int Gpio;
        public int Chip;      // which 74HCT245 (0, 1)
        public int Channel;   // 0..7
        public int APin => 2 + Channel;    // A1..A8 = pins 2..9
        public int BPin => 18 - Channel;   // B1..B8 = pins 18..11
        public string ChipName => Chip == 0 ? "U1" : "U2";
    }

    public class WiringPlan
    {
        public List<WireRun> Runs = new List<WireRun>();
        public List<int> GndPins = new List<int>(), V5Pins = new List<int>(), LeaveOpen = new List<int>(), UnknownPins = new List<int>();
        public int Chips => Runs.Count == 0 ? 1 : (Runs.Count + 7) / 8;
        public List<string> Warnings = new List<string>();
        public bool Direct;   // no 74HCT245: ESP32 3.3 V outputs wired straight to the header through resistors

        public static WiringPlan Build(HeaderMap map, SignCfg cfg, bool direct = false)
        {
            var p = new WiringPlan { Direct = direct };
            int i = 0;
            foreach (var role in Roles.Signals)
            {
                int gpio = cfg.Gpio[role];
                var pins = map.WithRole(role).Select(x => x.Number).ToList();
                if (gpio < 0 && pins.Count == 0) continue;
                if (role == PinRole.D && cfg.ScanRows == 8 && pins.Count == 0) continue;
                if (gpio < 0) { p.Warnings.Add($"{Roles.Code(role)} is on header pin {string.Join(", ", pins)} but has no ESP32 GPIO. Set one in 2. Sign setup."); continue; }
                if (pins.Count == 0) p.Warnings.Add($"{Roles.Code(role)} has GPIO {gpio} but hasn't been found on the header yet.");
                p.Runs.Add(new WireRun { Role = role, HeaderPins = pins, Gpio = gpio, Chip = i / 8, Channel = i % 8 });
                i++;
            }
            foreach (var req in new[] { PinRole.DataTop, PinRole.Clk, PinRole.Lat })
                if (!p.Runs.Any(r => r.Role == req)) p.Warnings.Add($"{Roles.Code(req)} isn't mapped yet (required). Find it in tab 1.");
            p.GndPins = map.WithRole(PinRole.GND).Select(x => x.Number).ToList();
            p.V5Pins = map.WithRole(PinRole.V5).Select(x => x.Number).ToList();
            p.LeaveOpen = map.Pins.Where(x => x.R == PinRole.NC || x.R == PinRole.Other).Select(x => x.Number).ToList();
            p.UnknownPins = map.Pins.Where(x => x.R == PinRole.Unknown).Select(x => x.Number).ToList();
            if (p.GndPins.Count == 0) p.Warnings.Add("No GND pin on the header is marked. Find at least one before wiring: without a shared ground nothing works.");
            if (p.UnknownPins.Count > 0) p.Warnings.Add($"{p.UnknownPins.Count} header pins are still unknown. They're left unconnected below.");
            return p;
        }

        static string Pins(List<int> l) => l.Count == 0 ? "?" : string.Join(", ", l);

        /// <summary>Ordered build steps, grouped by section.</summary>
        public List<(string section, string text)> Steps(SignCfg cfg) => Direct ? DirectSteps(cfg) : ShifterSteps(cfg);

        /// <summary>Wiring without the 74HCT245: 3.3 V GPIOs straight into the sign's 5 V logic inputs.</summary>
        List<(string section, string text)> DirectSteps(SignCfg cfg)
        {
            var s = new List<(string, string)>();
            void A(string sec, string t) => s.Add((sec, t));
            const string S0 = "Before you start (no level shifter)", S1 = "Ground first", S2 = "Signals (ESP32 → resistor → header)", S3 = "Leave these alone", S4 = "Power the ESP32", S5 = "Bring it up";

            A(S0, "Direct wiring, no 74HCT245. The V-002 controller measures 3.37 V on its pins, so the panels already work with 3.3 V signals, the same as the ESP32. If you ever see flicker, random dots or a shifted picture, lower Refresh (Hz) in tab 2 and keep the wires short.");
            A(S0, "Unplug the sign from the wall. Its power supply has mains on the input side.");
            A(S0, "Unplug the 50-pin ribbon from the CPU-002 board and keep the CPU-002 somewhere safe.");
            A(S0, $"Parts: a 50-pin (2x25) IDC male header or breakout to plug the ribbon into, {Runs.Count} resistors of 100 to 330 Ω (220 Ω is ideal), one 10 kΩ resistor, and short jumper wires.");
            A(S0, "Only wire the pins listed below. They are all inputs on the sign side, so the ESP32 only ever sends signals into the sign and nothing at 5 V comes back.");
            A(S0, "Leave the Freenove board's SD card slot empty: its SPI connector shares pins with the card slot.");

            A(S1, "ESP32 board GND → common ground. Connect this before any signal wire.");
            A(S1, GndPins.Count > 0
                ? $"Header GND pins {Pins(GndPins)} → the same common ground. Connect every one of them."
                : "Header GND: none marked yet! Find the ground pins (tab 1, multimeter step) before going on.");
            A(S1, V5Pins.Count > 0
                ? $"Header power pins {Pins(V5Pins)}: leave them unconnected. The ESP32 gets its power from USB."
                : "Don't connect the sign's power pins or its 5 V supply to any ESP32 signal pin.");

            foreach (var r in Runs)
            {
                string where = BoardPins.Where(r.Gpio);
                A(S2, $"{Roles.Code(r.Role)}: ESP32 GPIO {r.Gpio}{(where.Length > 0 ? " [" + where + "]" : "")} → 220 Ω resistor → header pin {Pins(r.HeaderPins)}.");
            }
            var oe = Runs.FirstOrDefault(r => r.Role == PinRole.Oe);
            if (oe != null) A(S2, $"10 kΩ pull-up from ESP32 GPIO {oe.Gpio} to 3.3V. It keeps the LEDs dark while the ESP32 boots.");
            A(S2, "Keep every signal wire as short as you can (under 15 cm). Run a ground wire alongside the CLK wire if you can; it cuts glitches.");

            if (LeaveOpen.Count > 0) A(S3, $"Header pins {Pins(LeaveOpen)} (NC / Other): leave unconnected.");
            if (UnknownPins.Count > 0) A(S3, $"Header pins {Pins(UnknownPins)} are still unknown: leave unconnected. An unknown pin could be a 5 V output.");

            A(S4, "Power the Freenove board from your PC's USB while testing. No extra power parts are needed.");
            A(S4, "Later, for the permanent install, feed it through its USB-C socket from the sign's 5V supply.");

            A(S5, "Double-check: no 5V wire touches an ESP32 pin, and all grounds are joined.");
            A(S5, "Plug in the sign and the USB cable. Connect with this app, open 2. Sign setup, press Apply, then run Corners + arrow.");
            A(S5, "If the picture flickers, has random dots or looks shifted, lower Refresh (Hz) in tab 2 and keep the wires shorter. If it's still wrong, add the 74HCT245.");
            A(S5, "When the patterns look right, press Save to board.");
            return s;
        }

        List<(string section, string text)> ShifterSteps(SignCfg cfg)
        {
            var s = new List<(string, string)>();
            string chips = Chips == 1 ? "one 74HCT245 (U1)" : $"{Chips} 74HCT245 chips (U1, U2)";
            string vccSrc = V5Pins.Count > 0 ? $"header pin {V5Pins[0]} (5V)" : "the sign's 5V supply terminal";

            void A(string sec, string t) => s.Add((sec, t));
            const string S0 = "Before you start", S1 = "Power and ground", S2 = "Signals (ESP32 → 74HCT245 → header)", S3 = "Leave these alone", S4 = "Power the ESP32", S5 = "Bring it up";

            A(S0, "Unplug the sign from the wall. Its power supply has mains on the input side.");
            A(S0, "Unplug the 50-pin ribbon from the CPU-002 board. The CPU-002 is retired from here on; keep it somewhere safe in case you want to go back.");
            A(S0, $"Parts: {chips}, a 50-pin (2x25) IDC male header or breakout to plug the ribbon into, {Runs.Count} resistors of 33 to 100 Ω, one 10 kΩ resistor, {Chips} 100 nF capacitors, perfboard and hook-up wire.");
            A(S0, "Leave the Freenove board's SD card slot empty: its SPI connector, used for the sign signals, shares pins with the card slot.");
            A(S0, "Solder the 2x25 header onto the perfboard with the 74HCT245 next to it, and the ESP32 wires short (under 15 cm). The ribbon from the header to the panels stays the original one.");

            for (int c = 0; c < Chips; c++)
            {
                string u = c == 0 ? "U1" : "U2";
                A(S1, $"{u} pin 20 (VCC) → {vccSrc}. Put a 100 nF capacitor from {u} pin 20 to {u} pin 10, right at the chip.");
                A(S1, $"{u} pin 10 (GND) → common ground.");
                A(S1, $"{u} pin 1 (DIR) → 5V, so data flows A → B (ESP32 side to sign side).");
                A(S1, $"{u} pin 19 (/OE) → GND, so the chip is always enabled.");
            }
            A(S1, GndPins.Count > 0
                ? $"Header GND pins {Pins(GndPins)} → common ground. Connect every one of them; the sign's return current is shared across them."
                : "Header GND: none marked yet! Find the ground pins (tab 1, multimeter step) before going on.");
            A(S1, "ESP32 board GND → common ground. Do this before connecting any signal wire.");
            if (V5Pins.Count > 0) A(S1, $"Header 5V pins {Pins(V5Pins)}: only use one of them to power the 74HCT245. Never connect 5V to an ESP32 GPIO.");

            foreach (var r in Runs)
            {
                string strap = cfg != null && new[] { 0, 2, 5, 15 }.Contains(r.Gpio) ? " (boot strapping pin: fine here because the 74HCT245 input doesn't pull it)" : "";
                string where = BoardPins.Where(r.Gpio);
                A(S2, $"{Roles.Code(r.Role)}: ESP32 GPIO {r.Gpio}{(where.Length > 0 ? " [" + where + "]" : "")}{strap} → {r.ChipName} pin {r.APin} (A{r.Channel + 1}).   {r.ChipName} pin {r.BPin} (B{r.Channel + 1}) → 47 Ω resistor → header pin {Pins(r.HeaderPins)}.");
            }
            var oe = Runs.FirstOrDefault(r => r.Role == PinRole.Oe);
            if (oe != null)
                A(S2, $"10 kΩ pull-up from ESP32 GPIO {oe.Gpio} to 3.3V. It keeps OE inactive (LEDs dark) while the ESP32 boots and its pins float.");
            for (int c = 0; c < Chips; c++)
            {
                int used = Runs.Count(r => r.Chip == c);
                if (used < 8)
                {
                    string u = c == 0 ? "U1" : "U2";
                    var spare = Enumerable.Range(used, 8 - used).Select(ch => (2 + ch).ToString());
                    A(S2, $"Unused {u} inputs, pins {string.Join(", ", spare)}: tie to GND so they don't float. Leave the matching B outputs unconnected.");
                }
            }

            if (LeaveOpen.Count > 0) A(S3, $"Header pins {Pins(LeaveOpen)} (NC / Other): leave unconnected.");
            if (UnknownPins.Count > 0) A(S3, $"Header pins {Pins(UnknownPins)} are still unknown: leave unconnected for now.");
            A(S3, "Nothing from the header goes straight to the ESP32. Every signal passes through the 74HCT245.");

            A(S4, "While testing, power the Freenove board from your PC's USB.");
            A(S4, "For the permanent install, feed the Freenove board from the sign's 5V supply (its 5V/VIN pin or a USB cable from a 5V-to-USB lead). Use USB or the sign's 5V, not both at once, unless the board's schematic shows a diode on USB 5V.");
            A(S4, "Mount the ESP32 board where you can still see the screen and reach the USB port, away from the mains side of the power supply.");

            A(S5, "Double-check: no 5V wire touches an ESP32 pin; all grounds are joined; the 74HCT245 is the right way round (pin 1 notch).");
            A(S5, "Plug in the sign and the USB cable. Connect with this app, open 2. Sign setup, press Apply, then run Corners + arrow.");
            A(S5, "Fix orientation with the check-boxes until all patterns look right, then press Save to board.");
            A(S5, "Set WiFi in the WiFi & settings tab. From then on everything works wirelessly: the app finds the sign with Find sign, and any phone can switch it on or off and change the text at http://casinosign.local.");
            return s;
        }
    }

    public class WiringTab : UserControl
    {
        readonly Ctx _ctx;
        readonly WiringDiagram _diagram = new WiringDiagram();
        readonly FlowLayoutPanel _steps = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(4) };
        readonly HashSet<string> _done = new HashSet<string>();
        Label _warn;
        CheckBox _direct;

        public WiringTab(Ctx ctx)
        {
            _ctx = ctx;
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
            Controls.Add(split);

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            top.Controls.Add(new Label { Text = "Final wiring for the permanent install, built from your header map (tab 1) and GPIO choices (tab 2). It updates as you map pins.", AutoSize = true, Margin = new Padding(3, 8, 10, 0) });
            var exp = new Button { Text = "Save as printable page…", AutoSize = true };
            exp.Click += (s, e) => Export();
            var copy = new Button { Text = "Copy as text", AutoSize = true };
            copy.Click += (s, e) => { Clipboard.SetText(AsText()); _ctx.Status("Wiring steps copied."); };
            top.Controls.Add(exp); top.Controls.Add(copy);
            _direct = new CheckBox { Text = "No level shifter (I don't have a 74HCT245): wire the ESP32 straight to the sign", AutoSize = true, Checked = _ctx.Settings?.NoShifter ?? false, Margin = new Padding(12, 7, 0, 0), Font = new Font(Font, FontStyle.Bold) };
            _direct.CheckedChanged += (s, e) => { if (_ctx.Settings != null) { _ctx.Settings.NoShifter = _direct.Checked; _ctx.Settings.Save(); } Rebuild(); };
            top.Controls.Add(_direct);
            Controls.Add(top);

            _diagram.Dock = DockStyle.Fill;
            split.Panel1.Controls.Add(_diagram);
            _warn = new Label { Dock = DockStyle.Top, AutoSize = true, ForeColor = Color.DarkRed, Padding = new Padding(4), MaximumSize = new Size(600, 0) };
            split.Panel2.Controls.Add(_steps);
            split.Panel2.Controls.Add(_warn);
            Load += (s, e) => { try { split.SplitterDistance = split.Width / 2; } catch { } };

            _ctx.MapChanged += Rebuild;
            _ctx.CfgChanged += Rebuild;
            VisibleChanged += (s, e) => { if (Visible) Rebuild(); };
            int lastW = 0;
            _steps.Resize += (s, e) => { if (Math.Abs(_steps.Width - lastW) > 40) { lastW = _steps.Width; Rebuild(); } };
            Rebuild();
        }

        WiringPlan _plan;
        void Rebuild()
        {
            _plan = WiringPlan.Build(_ctx.Map, _ctx.Cfg, _direct?.Checked ?? false);
            _diagram.Plan = _plan;
            _diagram.Invalidate();
            _warn.Text = _plan.Warnings.Count > 0 ? "⚠ " + string.Join("\n⚠ ", _plan.Warnings) : "";
            _warn.MaximumSize = new Size(Math.Max(300, _steps.Width - 20), 0);

            _steps.SuspendLayout();
            _steps.Controls.Clear();
            int w = Math.Max(300, _steps.ClientSize.Width - 30);
            string lastSec = null;
            foreach (var (sec, text) in _plan.Steps(_ctx.Cfg))
            {
                if (sec != lastSec)
                {
                    _steps.Controls.Add(new Label { Text = sec, AutoSize = true, Font = new Font(Font.FontFamily, 10f, FontStyle.Bold), Margin = new Padding(0, 10, 0, 2) });
                    lastSec = sec;
                }
                // Checkbox + wrapping label (a CheckBox's own text doesn't wrap reliably).
                var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(6, 1, 0, 1) };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                var cb = new CheckBox { AutoSize = true, Checked = _done.Contains(text), Margin = new Padding(0, 2, 0, 0) };
                var lbl = new Label { Text = text, AutoSize = true, MaximumSize = new Size(w - 30, 0), Margin = new Padding(0, 3, 0, 3) };
                lbl.Click += (s, e) => cb.Checked = !cb.Checked;
                cb.CheckedChanged += (s, e) => { if (cb.Checked) _done.Add(text); else _done.Remove(text); lbl.ForeColor = cb.Checked ? Color.Gray : SystemColors.ControlText; };
                if (cb.Checked) lbl.ForeColor = Color.Gray;
                row.Controls.Add(cb, 0, 0); row.Controls.Add(lbl, 1, 0);
                _steps.Controls.Add(row);
            }
            _steps.ResumeLayout();
        }

        string AsText()
        {
            var sb = new StringBuilder("Casino sign wiring (ESP32 replaces CPU-002)\r\n");
            string last = null;
            foreach (var (sec, text) in _plan.Steps(_ctx.Cfg))
            {
                if (sec != last) { sb.Append("\r\n").Append(sec.ToUpperInvariant()).Append("\r\n"); last = sec; }
                sb.Append("[ ] ").Append(text).Append("\r\n");
            }
            foreach (var w in _plan.Warnings) sb.Append("\r\nWARNING: ").Append(w);
            return sb.ToString();
        }

        void Export()
        {
            using var d = new SaveFileDialog { Filter = "Web page (*.html)|*.html", FileName = "sign-wiring.html" };
            if (d.ShowDialog() != DialogResult.OK) return;
            using var bmp = new Bitmap(1400, Math.Max(700, 120 + 70 * Math.Max(_plan.Runs.Count + 2, 4)));
            using (var g = Graphics.FromImage(bmp)) _diagram.Draw(g, new Rectangle(0, 0, bmp.Width, bmp.Height), Color.White);
            string png;
            using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); png = Convert.ToBase64String(ms.ToArray()); }
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'><title>Casino sign wiring</title><style>body{font-family:Segoe UI,Arial,sans-serif;max-width:1000px;margin:20px auto;line-height:1.45}h2{margin-top:28px}li{margin:4px 0}img{width:100%;border:1px solid #ccc}.w{color:#a00}</style></head><body>");
            sb.Append("<h1>Casino sign wiring: ESP32 replaces the CPU-002</h1>");
            sb.Append($"<p>Generated {DateTime.Now:yyyy-MM-dd HH:mm} by Casino Sign Tool from the header map and GPIO settings.</p>");
            foreach (var w in _plan.Warnings) sb.Append($"<p class='w'>⚠ {WebUtility.HtmlEncode(w)}</p>");
            sb.Append($"<img src='data:image/png;base64,{png}'>");
            string last = null;
            foreach (var (sec, text) in _plan.Steps(_ctx.Cfg))
            {
                if (sec != last) { if (last != null) sb.Append("</ul>"); sb.Append($"<h2>{WebUtility.HtmlEncode(sec)}</h2><ul style='list-style:none;padding-left:0'>"); last = sec; }
                sb.Append($"<li>☐ {WebUtility.HtmlEncode(text)}</li>");
            }
            sb.Append("</ul>");
            sb.Append("<h2>74HCT245 pinout</h2><p>Pin 1 DIR, 2-9 A1-A8 (inputs from ESP32), 10 GND, 11-18 B8-B1 (outputs to sign), 19 /OE, 20 VCC. The notch marks the pin 1 end.</p>");
            sb.Append("</body></html>");
            File.WriteAllText(d.FileName, sb.ToString(), Encoding.UTF8);
            _ctx.Status("Saved " + d.FileName);
            try { System.Diagnostics.Process.Start(d.FileName); } catch { }
        }
    }

    /// <summary>Block diagram: header pins on the left, 74HCT245 in the middle, ESP32 GPIOs on the right.</summary>
    public class WiringDiagram : Control
    {
        public WiringPlan Plan;
        public WiringDiagram() { DoubleBuffered = true; ResizeRedraw = true; BackColor = Color.White; }

        protected override void OnPaint(PaintEventArgs e) => Draw(e.Graphics, ClientRectangle, BackColor);

        public void Draw(Graphics g, Rectangle area, Color bg)
        {
            g.Clear(bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            if (Plan == null) return;
            var runs = Plan.Runs;
            int rows = Math.Max(runs.Count + 2, 4);
            float top = area.Top + 50, rowH = Math.Min(44f, (area.Height - 90f) / rows);
            float W = area.Width;
            float xHdr = area.Left + 20, wHdr = W * 0.17f;
            float xChip = area.Left + W * 0.38f, wChip = W * 0.20f;
            float xEsp = area.Left + W * 0.78f, wEsp = W * 0.19f;
            using var f = new Font("Segoe UI", Math.Max(7f, Math.Min(10f, rowH / 3.6f)));
            using var fb = new Font("Segoe UI", Math.Max(7.5f, Math.Min(11f, rowH / 3.2f)), FontStyle.Bold);
            var cen = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            var left = new StringFormat { LineAlignment = StringAlignment.Center };
            var right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

            g.DrawString("50-pin header (to panels)", fb, Brushes.Black, xHdr, area.Top + 14);
            if (Plan.Direct) { DrawDirect(g, area, bg); return; }
            g.DrawString(Plan.Chips > 1 ? "74HCT245 U1/U2 (5V)" : "74HCT245 U1 (5V)", fb, Brushes.Black, xChip, area.Top + 14);
            g.DrawString("ESP32 board", fb, Brushes.Black, xEsp, area.Top + 14);

            float bodyH = rows * rowH;
            using (var pen = new Pen(Color.FromArgb(60, 60, 60), 2))
            {
                g.DrawRectangle(pen, xHdr, top, wHdr, bodyH);
                g.DrawRectangle(pen, xChip, top, wChip, bodyH);
                g.DrawRectangle(pen, xEsp, top, wEsp, bodyH);
            }

            for (int i = 0; i < runs.Count; i++)
            {
                var r = runs[i];
                float y = top + rowH * (i + 0.5f);
                var col = Roles.Color(r.Role);
                using var pen = new Pen(col, 3f);
                string hp = r.HeaderPins.Count == 0 ? "pin ?" : "pin " + string.Join("/", r.HeaderPins);
                g.DrawString($"{hp}  {Roles.Code(r.Role)}", f, Brushes.Black, new RectangleF(xHdr + 6, y - rowH / 2, wHdr - 8, rowH), left);
                // header → resistor → B pin
                float rx = xHdr + wHdr + (xChip - xHdr - wHdr) * 0.45f;
                g.DrawLine(pen, xHdr + wHdr, y, rx - 14, y);
                g.DrawRectangle(Pens.Black, rx - 14, y - 5, 28, 10);
                g.DrawString("47Ω", f, Brushes.DimGray, rx - 14, y - rowH * 0.48f);
                g.DrawLine(pen, rx + 14, y, xChip, y);
                g.DrawString($"B{r.Channel + 1} ({r.ChipName}.{r.BPin})", f, Brushes.Black, new RectangleF(xChip + 4, y - rowH / 2, wChip / 2, rowH), left);
                g.DrawString($"({r.ChipName}.{r.APin}) A{r.Channel + 1}", f, Brushes.Black, new RectangleF(xChip + wChip / 2, y - rowH / 2, wChip / 2 - 4, rowH), right);
                g.DrawLine(pen, xChip + wChip, y, xEsp, y);
                g.DrawString($"GPIO {r.Gpio}", fb, Brushes.Black, new RectangleF(xEsp + 6, y - rowH / 2, wEsp - 8, rowH), left);
                if (r.Role == PinRole.Oe)
                    g.DrawString("+10k to 3.3V", f, Brushes.DimGray, xChip + wChip + 6, y - rowH * 0.48f);
            }

            // Ground and power rows.
            float yG = top + rowH * (runs.Count + 0.5f), yV = top + rowH * (runs.Count + 1.5f);
            using (var gp = new Pen(Color.Black, 3f) { DashStyle = DashStyle.Solid })
            {
                string gnd = Plan.GndPins.Count == 0 ? "GND: not found!" : "GND pins " + string.Join(",", Plan.GndPins);
                g.DrawString(gnd, f, Plan.GndPins.Count == 0 ? Brushes.Red : Brushes.Black, new RectangleF(xHdr + 6, yG - rowH / 2, wHdr - 8, rowH), left);
                g.DrawLine(gp, xHdr + wHdr, yG, xEsp, yG);
                using (var bgb = new SolidBrush(bg)) g.FillRectangle(bgb, xChip + 2, yG - 9, wChip - 4, 18);
                g.DrawString("GND: pins 10, 19", f, Brushes.Black, new RectangleF(xChip, yG - rowH / 2, wChip, rowH), cen);
                g.DrawString("GND", fb, Brushes.Black, new RectangleF(xEsp + 6, yG - rowH / 2, wEsp, rowH), left);
            }
            using (var vp = new Pen(Color.Red, 3f))
            {
                string v = Plan.V5Pins.Count == 0 ? "5V: from PSU" : "5V pin " + Plan.V5Pins[0];
                g.DrawString(v, f, Brushes.DarkRed, new RectangleF(xHdr + 6, yV - rowH / 2, wHdr - 8, rowH), left);
                g.DrawLine(vp, xHdr + wHdr, yV, xChip, yV);
                g.DrawString("VCC 20, DIR 1", f, Brushes.DarkRed, new RectangleF(xChip, yV - rowH / 2, wChip, rowH), cen);
                g.DrawString("not to ESP32 pins", f, Brushes.DarkRed, new RectangleF(xEsp + 6, yV - rowH / 2, wEsp, rowH), left);
            }
            if (runs.Count == 0)
                g.DrawString("No signals yet: map the header (tab 1) and pick GPIOs (tab 2).", fb, Brushes.Gray, new RectangleF(area.Left, top, area.Width, rowH * 2), cen);
        }

        /// <summary>No level shifter: header ← 220 Ω ← ESP32 GPIO, plus grounds, and 5 V left off.</summary>
        void DrawDirect(Graphics g, Rectangle area, Color bg)
        {
            var runs = Plan.Runs;
            int rows = Math.Max(runs.Count + 2, 4);
            float top = area.Top + 50, rowH = Math.Min(44f, (area.Height - 120f) / rows);
            float W = area.Width;
            float xHdr = area.Left + 20, wHdr = W * 0.22f;
            float xEsp = area.Left + W * 0.70f, wEsp = W * 0.28f;
            using var f = new Font("Segoe UI", Math.Max(7f, Math.Min(10f, rowH / 3.6f)));
            using var fb = new Font("Segoe UI", Math.Max(7.5f, Math.Min(11f, rowH / 3.2f)), FontStyle.Bold);
            var cen = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            var left = new StringFormat { LineAlignment = StringAlignment.Center };

            g.DrawString("50-pin header (to panels)", fb, Brushes.Black, xHdr, area.Top + 14);
            g.DrawString("ESP32 board (3.3 V)", fb, Brushes.Black, xEsp, area.Top + 14);
            g.DrawString("No level shifter: test setup", fb, Brushes.DarkOrange, new RectangleF(xHdr + wHdr, area.Top + 30, xEsp - xHdr - wHdr, 20), cen);
            float bodyH = rows * rowH;
            using (var pen = new Pen(Color.FromArgb(60, 60, 60), 2))
            {
                g.DrawRectangle(pen, xHdr, top, wHdr, bodyH);
                g.DrawRectangle(pen, xEsp, top, wEsp, bodyH);
            }
            for (int i = 0; i < runs.Count; i++)
            {
                var r = runs[i];
                float y = top + rowH * (i + 0.5f);
                using var pen = new Pen(Roles.Color(r.Role), 3f);
                string hp = r.HeaderPins.Count == 0 ? "pin ?" : "pin " + string.Join("/", r.HeaderPins);
                g.DrawString($"{hp}  {Roles.Code(r.Role)}", f, Brushes.Black, new RectangleF(xHdr + 6, y - rowH / 2, wHdr - 8, rowH), left);
                float rx = (xHdr + wHdr + xEsp) / 2;
                g.DrawLine(pen, xHdr + wHdr, y, rx - 16, y);
                g.DrawRectangle(Pens.Black, rx - 16, y - 5, 32, 10);
                g.DrawString("220Ω", f, Brushes.DimGray, rx - 16, y - rowH * 0.48f);
                g.DrawLine(pen, rx + 16, y, xEsp, y);
                if (r.Role == PinRole.Oe) g.DrawString("+10k to 3.3V", f, Brushes.DimGray, rx + 22, y - rowH * 0.48f);
                string where = BoardPins.Where(r.Gpio);
                g.DrawString($"GPIO {r.Gpio}" + (where.Length > 0 ? "  " + where.Split(',')[0].Split(':')[0] : ""), f, Brushes.Black, new RectangleF(xEsp + 6, y - rowH / 2, wEsp - 8, rowH), left);
            }
            float yG = top + rowH * (runs.Count + 0.5f), yV = top + rowH * (runs.Count + 1.5f);
            using (var gp = new Pen(Color.Black, 3f))
            {
                string gnd = Plan.GndPins.Count == 0 ? "GND: not found!" : "GND pins " + string.Join(",", Plan.GndPins);
                g.DrawString(gnd, f, Plan.GndPins.Count == 0 ? Brushes.Red : Brushes.Black, new RectangleF(xHdr + 6, yG - rowH / 2, wHdr - 8, rowH), left);
                g.DrawLine(gp, xHdr + wHdr, yG, xEsp, yG);
                g.DrawString("GND", fb, Brushes.Black, new RectangleF(xEsp + 6, yG - rowH / 2, wEsp, rowH), left);
            }
            string v = Plan.V5Pins.Count == 0 ? "3.3V power" : "3.3V power pin " + string.Join(",", Plan.V5Pins);
            g.DrawString(v, f, Brushes.DarkRed, new RectangleF(xHdr + 6, yV - rowH / 2, wHdr - 8, rowH), left);
            using (var vp = new Pen(Color.Red, 3f)) g.DrawLine(vp, xHdr + wHdr, yV, xHdr + wHdr + 30, yV);
            g.DrawString("✕ leave unconnected: never to an ESP32 pin", f, Brushes.DarkRed, xHdr + wHdr + 36, yV - 8);
            g.DrawString("The V-002 runs at 3.3 V, so the ESP32 drives the panels directly. Leave the header's power pins unconnected.", f, Brushes.DarkGreen,
                new RectangleF(area.Left + 10, top + bodyH + 8, area.Width - 20, 40), cen);
            if (runs.Count == 0)
                g.DrawString("No signals yet: map the header (tab 1) and pick GPIOs (tab 2).", fb, Brushes.Gray, new RectangleF(area.Left, top, area.Width, rowH * 2), cen);
        }
    }
}
