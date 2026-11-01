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
    /// <summary>One signal: an ESP32 GPIO that splits to the same pin on both rows' panel connectors.</summary>
    public class WireRun
    {
        public PinRole Role;
        public string Name;
        public int Pin;       // panel connector pin (same on both rows)
        public int Pin2;      // second pin carrying the same signal, or 0
        public string PinText => Pin2 > 0 ? $"{Pin}+{Pin2}" : Pin.ToString();
        public int Gpio;
        public bool Sure;     // confirmed by the recordings
    }

    /// <summary>Final wiring: the ESP32 feeds the 16-pin input of the first panel in BOTH rows, every signal split equally.</summary>
    public class WiringPlan
    {
        public List<WireRun> Runs = new List<WireRun>();
        public List<string> Warnings = new List<string>();
        public int Resistors => Runs.Sum(r => r.Pin2 > 0 ? 4 : 2);

        public static WiringPlan Build(HeaderMap map, SignCfg cfg)
        {
            var p = new WiringPlan();
            foreach (var e in PanelPinout.Signals)
            {
                int gpio = cfg.Gpio.TryGetValue(e.Role, out int g) ? g : -1;
                if (gpio < 0) gpio = e.ExtraGpio;
                if (e.Role == PinRole.Other) gpio = e.ExtraGpio;
                if (gpio < 0) { p.Warnings.Add($"{e.Name} (panel pin {e.Pin}) has no ESP32 GPIO. Set one in 2. Sign setup."); continue; }
                p.Runs.Add(new WireRun { Role = e.Role, Name = e.Name, Pin = e.Pin, Pin2 = e.Pin2, Gpio = gpio, Sure = e.Sure });
            }
            foreach (var dup in p.Runs.GroupBy(r => r.Gpio).Where(x => x.Count() > 1))
                p.Warnings.Add($"GPIO {dup.Key} is used for {string.Join(" and ", dup.Select(r => r.Name))}. Give each signal its own GPIO in 2. Sign setup.");
            int unsure = p.Runs.Count(r => !r.Sure);
            if (unsure > 0) p.Warnings.Add($"{unsure} lines are marked \"check first\": they are best guesses from the recordings so far. Wire them last.");
            return p;
        }

        static string Where(int gpio) { var w = BoardPins.Where(gpio); return w.Length > 0 ? " [" + w + "]" : ""; }

        public List<(string section, string text)> Steps()
        {
            var s = new List<(string, string)>();
            void A(string sec, string t) => s.Add((sec, t));
            const string S0 = "Before you start", S1 = "Ground first", S2 = "Signals (ESP32 → split → 220 Ω → both rows)", S3 = "Leave these alone", S4 = "Power the ESP32", S5 = "Bring it up";

            A(S0, "Unplug the sign from the wall. Its power supply has mains on the input side.");
            A(S0, "Unplug both Advanced Row Processor boards from the first panel of each row. Keep them somewhere safe in case you want to go back.");
            A(S0, $"Parts: two 16-pin (2 x 8) IDC ribbon cables, a small breadboard, {Resistors} resistors of 220 Ω (red-red-brown), one 10 kΩ resistor and jumper wires.");
            A(S0, "Plug one ribbon into the input header of the first panel in Row 1 (top), where its row processor was, and the other into the same header in Row 2 (bottom). The red stripe marks pin 1.");
            A(S0, "No level shifter: the panel signals are 3.3 V or less, the same as the ESP32.");
            A(S0, "Leave the Freenove board's SD card slot empty: its SPI plug shares pins with the card slot.");

            A(S1, $"ESP32 GND (the I2C plug pin printed GND) → pin {PanelPinout.Gnd} of the Row 1 ribbon AND pin {PanelPinout.Gnd} of the Row 2 ribbon. Connect this before any signal wire.");

            foreach (var r in Runs)
                A(S2, r.Pin2 > 0
                    ? $"{r.Name}{(r.Sure ? "" : " (check first)")}: ESP32 GPIO {r.Gpio}{Where(r.Gpio)} → one breadboard row → four 220 Ω resistors → Row 1 pins {r.Pin} and {r.Pin2}, Row 2 pins {r.Pin} and {r.Pin2} (these two pins always carry the same signal)."
                    : $"{r.Name}{(r.Sure ? "" : " (check first)")}: ESP32 GPIO {r.Gpio}{Where(r.Gpio)} → one breadboard row → 220 Ω → Row 1 pin {r.Pin}, and a second 220 Ω → Row 2 pin {r.Pin}.");
            var oe = Runs.FirstOrDefault(r => r.Role == PinRole.Oe);
            if (oe != null) A(S2, $"10 kΩ from ESP32 GPIO {oe.Gpio} to 3.3V. It keeps the LEDs dark while the ESP32 starts.");
            A(S2, "Keep the two branches of each signal about the same length, and every wire as short as you can (under 15 cm).");

            A(S3, $"Pins {string.Join(", ", PanelPinout.LeaveOpen)} on both ribbons: leave unconnected.");

            A(S4, "Power the Freenove board from your PC's USB while testing. No extra power parts are needed.");
            A(S4, "Later, for the permanent install, feed it through its USB-C socket from the sign's 5V supply.");

            A(S5, "Double-check: no wire goes to the sign's 5V, and pin 15 of both ribbons is joined to ESP32 GND.");
            A(S5, "Plug in the sign and the USB cable. Connect with this app, open 2. Sign setup, press Apply, then run Corners + arrow. Both rows should show their part of the picture.");
            A(S5, "When the patterns look right, press Save to board.");
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

        public WiringTab(Ctx ctx)
        {
            _ctx = ctx;
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
            Controls.Add(split);

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            top.Controls.Add(new Label { Text = "Final wiring: the ESP32 feeds the 16-pin input of the first panel in BOTH rows. Every signal splits in two, one 220 Ω resistor to each row.", AutoSize = true, Margin = new Padding(3, 8, 10, 0) });
            var pic = new Button { Text = "Show breadboard picture", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            pic.Click += (s, e) => ShowPicture();
            var exp = new Button { Text = "Save as printable page…", AutoSize = true };
            exp.Click += (s, e) => Export();
            var copy = new Button { Text = "Copy as text", AutoSize = true };
            copy.Click += (s, e) => { Clipboard.SetText(AsText()); _ctx.Status("Wiring steps copied."); };
            top.Controls.Add(pic); top.Controls.Add(exp); top.Controls.Add(copy);
            Controls.Add(top);

            _diagram.Dock = DockStyle.Fill;
            split.Panel1.Controls.Add(_diagram);
            _warn = new Label { Dock = DockStyle.Top, AutoSize = true, ForeColor = Color.DarkRed, Padding = new Padding(4), MaximumSize = new Size(600, 0) };
            split.Panel2.Controls.Add(_steps);
            split.Panel2.Controls.Add(_warn);
            Load += (s, e) => { try { split.SplitterDistance = split.Width * 3 / 5; } catch { } };

            _ctx.MapChanged += Rebuild;
            _ctx.CfgChanged += Rebuild;
            VisibleChanged += (s, e) => { if (Visible) Rebuild(); };
            int lastW = 0;
            _steps.Resize += (s, e) => { if (Math.Abs(_steps.Width - lastW) > 40) { lastW = _steps.Width; Rebuild(); } };
            Rebuild();
        }

        void ShowPicture()
        {
            using var st = typeof(WiringTab).Assembly.GetManifestResourceStream("panel-drive-wiring.png");
            if (st == null) return;
            Image img; using (var tmp = Image.FromStream(st)) img = new Bitmap(tmp);
            var frm = new Form { Text = "Final wiring: ESP32 to both panel rows", ClientSize = new Size(Math.Min(img.Width, 1300), Math.Min(img.Height, 840)), StartPosition = FormStartPosition.CenterParent };
            frm.Controls.Add(new PictureBox { Dock = DockStyle.Fill, Image = img, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White });
            frm.FormClosed += (s, e) => img.Dispose();
            frm.Show(FindForm());
        }

        WiringPlan _plan;
        void Rebuild()
        {
            _plan = WiringPlan.Build(_ctx.Map, _ctx.Cfg);
            _diagram.Plan = _plan;
            _diagram.Invalidate();
            _warn.Text = _plan.Warnings.Count > 0 ? "⚠ " + string.Join("\n⚠ ", _plan.Warnings) : "";
            _warn.MaximumSize = new Size(Math.Max(300, _steps.Width - 20), 0);

            _steps.SuspendLayout();
            _steps.Controls.Clear();
            int w = Math.Max(300, _steps.ClientSize.Width - 30);
            string lastSec = null;
            foreach (var (sec, text) in _plan.Steps())
            {
                if (sec != lastSec)
                {
                    _steps.Controls.Add(new Label { Text = sec, AutoSize = true, Font = new Font(Font.FontFamily, 10f, FontStyle.Bold), Margin = new Padding(0, 10, 0, 2) });
                    lastSec = sec;
                }
                var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(6, 1, 0, 1) };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                var cb = new CheckBox { AutoSize = true, Checked = _done.Contains(text), Margin = new Padding(0, 2, 0, 0) };
                var lbl = new Label { Text = text, AutoSize = true, MaximumSize = new Size(w - 30, 0), Margin = new Padding(0, 3, 0, 3) };
                if (text.Contains("(check first)")) lbl.ForeColor = Color.DarkOrange;
                lbl.Click += (s, e) => cb.Checked = !cb.Checked;
                var baseCol = lbl.ForeColor;
                cb.CheckedChanged += (s, e) => { if (cb.Checked) _done.Add(text); else _done.Remove(text); lbl.ForeColor = cb.Checked ? Color.Gray : baseCol; };
                if (cb.Checked) lbl.ForeColor = Color.Gray;
                row.Controls.Add(cb, 0, 0); row.Controls.Add(lbl, 1, 0);
                _steps.Controls.Add(row);
            }
            _steps.ResumeLayout();
        }

        string AsText()
        {
            var sb = new StringBuilder("Casino sign wiring: ESP32 to both panel rows\r\n");
            string last = null;
            foreach (var (sec, text) in _plan.Steps())
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
            using var bmp = new Bitmap(1400, Math.Max(700, 160 + 56 * (_plan.Runs.Count + 2)));
            using (var g = Graphics.FromImage(bmp)) _diagram.Draw(g, new Rectangle(0, 0, bmp.Width, bmp.Height), Color.White);
            string png;
            using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); png = Convert.ToBase64String(ms.ToArray()); }
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'><title>Casino sign wiring</title><style>body{font-family:Segoe UI,Arial,sans-serif;max-width:1000px;margin:20px auto;line-height:1.45}h2{margin-top:28px}li{margin:4px 0}img{width:100%;border:1px solid #ccc}.w{color:#a00}</style></head><body>");
            sb.Append("<h1>Casino sign wiring: ESP32 to both panel rows</h1>");
            sb.Append($"<p>Generated {DateTime.Now:yyyy-MM-dd HH:mm} by Casino Sign Tool.</p>");
            foreach (var w in _plan.Warnings) sb.Append($"<p class='w'>⚠ {WebUtility.HtmlEncode(w)}</p>");
            sb.Append($"<img src='data:image/png;base64,{png}'>");
            string last = null;
            foreach (var (sec, text) in _plan.Steps())
            {
                if (sec != last) { if (last != null) sb.Append("</ul>"); sb.Append($"<h2>{WebUtility.HtmlEncode(sec)}</h2><ul style='list-style:none;padding-left:0'>"); last = sec; }
                sb.Append($"<li>☐ {WebUtility.HtmlEncode(text)}</li>");
            }
            sb.Append("</ul></body></html>");
            File.WriteAllText(d.FileName, sb.ToString(), Encoding.UTF8);
            _ctx.Status("Saved " + d.FileName);
            try { System.Diagnostics.Process.Start(d.FileName); } catch { }
        }
    }

    /// <summary>Row 1 connector on the left, ESP32 in the middle, Row 2 connector on the right. Each signal splits both ways through its own 220 Ω.</summary>
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
            int rows = runs.Count + 2;
            float top = area.Top + 70, rowH = Math.Min(46f, (area.Height - 130f) / rows);
            float W = area.Width;
            float wCon = W * 0.17f, wEsp = W * 0.22f;
            float xL = area.Left + 12, xR = area.Right - 12 - wCon;
            float xEsp = area.Left + (W - wEsp) / 2;
            using var f = new Font("Segoe UI", Math.Max(7f, Math.Min(10f, rowH / 3.6f)));
            using var fb = new Font("Segoe UI", Math.Max(7.5f, Math.Min(11f, rowH / 3.2f)), FontStyle.Bold);
            var cen = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            var left = new StringFormat { LineAlignment = StringAlignment.Center };
            var right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

            g.DrawString("Row 1 (top) panel input\n16-pin ribbon", fb, Brushes.Black, new RectangleF(xL, area.Top + 10, wCon + 40, 50));
            g.DrawString("Row 2 (bottom) panel input\n16-pin ribbon", fb, Brushes.Black, new RectangleF(xR - 40, area.Top + 10, wCon + 40, 50), new StringFormat { Alignment = StringAlignment.Far });
            g.DrawString("ESP32 board (3.3 V)", fb, Brushes.Black, new RectangleF(xEsp - 20, area.Top + 10, wEsp + 40, 24), new StringFormat { Alignment = StringAlignment.Center });
            g.DrawString("each signal splits: one 220 Ω to each row", f, Brushes.DimGray, new RectangleF(xEsp - 60, area.Top + 34, wEsp + 120, 24), new StringFormat { Alignment = StringAlignment.Center });

            float bodyH = rows * rowH;
            using (var pen = new Pen(Color.FromArgb(60, 60, 60), 2))
            {
                g.DrawRectangle(pen, xL, top, wCon, bodyH);
                g.DrawRectangle(pen, xR, top, wCon, bodyH);
                g.DrawRectangle(pen, xEsp, top, wEsp, bodyH);
            }

            void Res(float cx, float y)
            {
                g.FillRectangle(Brushes.White, cx - 16, y - 6, 32, 12);
                g.DrawRectangle(Pens.Black, cx - 16, y - 6, 32, 12);
            }

            for (int i = 0; i < runs.Count; i++)
            {
                var r = runs[i];
                float y = top + rowH * (i + 0.5f);
                var col = Roles.Color(r.Role == PinRole.Other ? PinRole.DataBot : r.Role);
                using var pen = new Pen(col, 3f);
                if (!r.Sure) pen.DashStyle = DashStyle.Dash;
                string tag = r.Sure ? "" : "  (check first)";
                var txtBrush = r.Sure ? Brushes.Black : Brushes.DarkOrange;
                g.DrawString($"pin {r.PinText}  {r.Name}", f, txtBrush, new RectangleF(xL + 6, y - rowH / 2, wCon - 8, rowH), left);
                g.DrawString($"{r.Name}  pin {r.PinText}", f, txtBrush, new RectangleF(xR + 2, y - rowH / 2, wCon - 8, rowH), right);
                float rl = (xL + wCon + xEsp) / 2, rr = (xEsp + wEsp + xR) / 2;
                g.DrawLine(pen, xL + wCon, y, xEsp, y);
                g.DrawLine(pen, xEsp + wEsp, y, xR, y);
                Res(rl, y); Res(rr, y);
                g.DrawString("220Ω", f, Brushes.DimGray, rl - 16, y - rowH * 0.5f);
                g.DrawString("220Ω", f, Brushes.DimGray, rr - 16, y - rowH * 0.5f);
                // split dots at the ESP32 edges show the one GPIO feeding both sides
                g.FillEllipse(new SolidBrush(col), xEsp - 5, y - 5, 10, 10);
                g.FillEllipse(new SolidBrush(col), xEsp + wEsp - 5, y - 5, 10, 10);
                g.DrawString($"GPIO {r.Gpio}{tag}", r.Sure ? fb : f, txtBrush, new RectangleF(xEsp + 4, y - rowH / 2, wEsp - 8, rowH), cen);
                if (r.Role == PinRole.Oe) g.DrawString("+10k to 3.3V", f, Brushes.DimGray, xEsp + wEsp - 80, y + rowH * 0.12f);
            }

            float yG = top + rowH * (runs.Count + 0.5f), yO = top + rowH * (runs.Count + 1.5f);
            using (var gp = new Pen(Color.Black, 3f))
            {
                g.DrawString($"pin {PanelPinout.Gnd}  GND", fb, Brushes.Black, new RectangleF(xL + 6, yG - rowH / 2, wCon - 8, rowH), left);
                g.DrawString($"GND  pin {PanelPinout.Gnd}", fb, Brushes.Black, new RectangleF(xR + 2, yG - rowH / 2, wCon - 8, rowH), right);
                g.DrawLine(gp, xL + wCon, yG, xEsp, yG);
                g.DrawLine(gp, xEsp + wEsp, yG, xR, yG);
                g.DrawString("GND (I2C plug)", fb, Brushes.Black, new RectangleF(xEsp, yG - rowH / 2, wEsp, rowH), cen);
            }
            string open = string.Join(", ", PanelPinout.LeaveOpen);
            g.DrawString($"pins {open}: unconnected", f, Brushes.Gray, new RectangleF(xL + 6, yO - rowH / 2, wCon - 8, rowH), left);
            g.DrawString($"pins {open}: unconnected", f, Brushes.Gray, new RectangleF(xR + 2, yO - rowH / 2, wCon - 8, rowH), right);
            g.DrawString("no wire to the sign's 5 V", f, Brushes.DarkRed, new RectangleF(xEsp, yO - rowH / 2, wEsp, rowH), cen);

            g.DrawString("Same pin numbers on both rows. Solid lines are confirmed; dashed amber lines are best guesses, wire them last.", f, Brushes.DarkGreen,
                new RectangleF(area.Left + 10, top + bodyH + 8, area.Width - 20, 40), cen);
        }
    }
}
