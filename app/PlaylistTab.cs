using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CasinoSignTool
{
    /// <summary>Playlist editor, overnight schedule, and settings for the live widgets.</summary>
    public class PlaylistTab : UserControl
    {
        readonly Ctx _ctx;
        readonly ListBox _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("Segoe UI", 10f) };
        readonly List<Dictionary<string, object>> _items = new List<Dictionary<string, object>>();
        readonly FlowLayoutPanel _editor = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        List<string> _anims = new List<string> { "corvette" };
        bool _loading;

        // settings controls
        CheckBox _schedOn; DateTimePicker _schedStart, _schedEnd;
        ComboBox _tz, _units; TextBox _wLabel, _wLat, _wLon, _stocks, _crypto, _jStart, _jRate, _city;
        Label _liveState;

        public static readonly (string name, string posix)[] Zones =
        {
            ("Pacific (Los Angeles, Las Vegas)", "PST8PDT,M3.2.0,M11.1.0"), ("Mountain (Denver)", "MST7MDT,M3.2.0,M11.1.0"),
            ("Arizona (Phoenix)", "MST7"), ("Central (Chicago)", "CST6CDT,M3.2.0,M11.1.0"), ("Eastern (New York)", "EST5EDT,M3.2.0,M11.1.0"),
            ("Alaska", "AKST9AKDT,M3.2.0,M11.1.0"), ("Hawaii", "HST10"), ("UK (London)", "GMT0BST,M3.5.0/1,M10.5.0"),
            ("Central Europe (Paris, Berlin)", "CET-1CEST,M3.5.0,M10.5.0/3"), ("Eastern Europe (Athens)", "EET-2EEST,M3.5.0/3,M10.5.0/4"),
            ("Australia East (Sydney)", "AEST-10AEDT,M10.1.0,M4.1.0/3"), ("UTC", "UTC0"),
        };

        static readonly (string type, string label)[] Types =
        {
            ("text", "Text"), ("clock", "Clock"), ("weather", "Weather"), ("ticker", "Stocks + crypto ticker"),
            ("countdown", "Countdown"), ("jackpot", "Jackpot counter"), ("anim", "Animation / picture"),
            ("fight", "Fight Night (live UFC)"), ("gamble", "Fight Night gambling tally"), ("pattern", "Test pattern"), ("off", "Blank"),
        };

        public PlaylistTab(Ctx ctx)
        {
            _ctx = ctx;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            Controls.Add(root);
            Label H(string t) => new Label { Text = t, AutoSize = true, Font = new Font(Font.FontFamily, 10f, FontStyle.Bold), Margin = new Padding(0, 8, 0, 4) };

            // ---- Column 1: the list ----
            var c1 = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4 };
            c1.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            c1.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            c1.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            c1.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            c1.Controls.Add(H("Playlist (plays top to bottom, then repeats)"), 0, 0);
            c1.Controls.Add(_list, 0, 1);
            var edit = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            var add = new Button { Text = "+ Add ▾", AutoSize = true };
            var menu = new ContextMenuStrip();
            foreach (var (type, label) in Types) { var t = type; menu.Items.Add(label, null, (s, e) => AddItem(t)); }
            add.Click += (s, e) => menu.Show(add, new Point(0, add.Height));
            edit.Controls.Add(add);
            Button B(FlowLayoutPanel p, string t, Action a) { var b = new Button { Text = t, AutoSize = true }; b.Click += (s, e) => a(); p.Controls.Add(b); return b; }
            B(edit, "▲", () => Move(-1)); B(edit, "▼", () => Move(1));
            B(edit, "Duplicate", () => { if (Sel < 0) return; _items.Insert(Sel + 1, new Dictionary<string, object>(_items[Sel])); RefreshList(Sel + 1); });
            B(edit, "Remove", () => { if (Sel < 0) return; int i = Sel; _items.RemoveAt(i); RefreshList(Math.Min(i, _items.Count - 1)); });
            c1.Controls.Add(edit, 0, 2);
            var board = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            B(board, "Load from sign", async () => await LoadFromBoard());
            var save = B(board, "Save to sign + play", async () => await SaveToBoard());
            save.Font = new Font(Font, FontStyle.Bold);
            B(board, "Show selected now", async () => { if (Sel >= 0) await _ctx.Try("SHOW " + Json.Write(_items[Sel])); });
            B(board, "Skip to next", async () => await _ctx.Try("NEXT"));
            c1.Controls.Add(board, 0, 3);
            root.Controls.Add(c1, 0, 0);
            _list.SelectedIndexChanged += (s, e) => BuildEditor();

            // ---- Column 2: item editor ----
            var c2 = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
            c2.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            c2.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            c2.Controls.Add(H("Selected item"), 0, 0);
            c2.Controls.Add(_editor, 0, 1);
            root.Controls.Add(c2, 1, 0);

            // ---- Column 3: schedule + widget settings ----
            var c3 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            root.Controls.Add(c3, 2, 0);
            FlowLayoutPanel Row(params Control[] cs) { var r = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) }; r.Controls.AddRange(cs); return r; }
            Label L(string t, int w = 95) => new Label { Text = t, Width = w, Margin = new Padding(0, 6, 0, 0) };

            c3.Controls.Add(H("Overnight schedule"));
            _schedOn = new CheckBox { Text = "Only light the sign between these times", AutoSize = true };
            _schedStart = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 70 };
            _schedEnd = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 70 };
            c3.Controls.Add(_schedOn);
            c3.Controls.Add(Row(L("On at:", 50), _schedStart, L("  Off at:", 55), _schedEnd));
            c3.Controls.Add(new Label { Text = "The startup and shutdown animations play at these times too.", AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(380, 0) });

            c3.Controls.Add(H("Time zone (clock, countdown, schedule)"));
            _tz = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
            foreach (var z in Zones) _tz.Items.Add(z.name);
            _tz.SelectedIndex = GuessZone();
            c3.Controls.Add(_tz);

            c3.Controls.Add(H("Weather"));
            _city = new TextBox { Width = 170 };
            var look = new Button { Text = "Look up city", AutoSize = true };
            look.Click += async (s, e) => await LookupCity();
            c3.Controls.Add(Row(L("City:"), _city, look));
            _wLabel = new TextBox { Width = 170 }; c3.Controls.Add(Row(L("Label on sign:"), _wLabel));
            _wLat = new TextBox { Width = 80 }; _wLon = new TextBox { Width = 80 };
            c3.Controls.Add(Row(L("Lat / lon:"), _wLat, _wLon));
            _units = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 }; _units.Items.AddRange(new object[] { "F", "C" }); _units.SelectedIndex = 0;
            c3.Controls.Add(Row(L("Units:"), _units));

            c3.Controls.Add(H("Ticker"));
            _stocks = new TextBox { Width = 230 }; c3.Controls.Add(Row(L("Stocks:"), _stocks));
            _crypto = new TextBox { Width = 230 }; c3.Controls.Add(Row(L("Crypto:"), _crypto));
            c3.Controls.Add(new Label { Text = "Stocks: symbols like AAPL,TSLA,SPY. Crypto: CoinGecko ids like bitcoin,ethereum,dogecoin.", AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(380, 0) });

            c3.Controls.Add(H("Jackpot counter"));
            _jStart = new TextBox { Width = 90 }; _jRate = new TextBox { Width = 60 };
            c3.Controls.Add(Row(L("Starts at $:"), _jStart, L("  $/second:", 70), _jRate));

            var srow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
            var apply = new Button { Text = "Save settings to sign", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            apply.Click += async (s, e) => await SaveSettings();
            var refresh = new Button { Text = "Refresh data now", AutoSize = true };
            refresh.Click += async (s, e) => { await _ctx.Try("LIVE REFRESH"); await Task.Delay(4000); await ShowLive(); };
            srow.Controls.Add(apply); srow.Controls.Add(refresh);
            c3.Controls.Add(srow);
            _liveState = new Label { AutoSize = true, MaximumSize = new Size(380, 0), Font = new Font("Consolas", 8.5f), ForeColor = Color.DimGray };
            c3.Controls.Add(_liveState);

            _ctx.StateChanged += async () => { if (_ctx.Board.Connected) await LoadFromBoard(); };
            _items.Add(new Dictionary<string, object> { ["t"] = "anim", ["name"] = "corvette", ["d"] = 0 });
            _items.Add(new Dictionary<string, object> { ["t"] = "text", ["text"] = "HELLO FROM THE ESP32", ["fx"] = "scroll", ["d"] = 0 });
            _items.Add(new Dictionary<string, object> { ["t"] = "clock", ["d"] = 8 });
            RefreshList(0);
        }

        int Sel => _list.SelectedIndex;

        static int GuessZone()
        {
            string id = TimeZoneInfo.Local.Id;
            if (id.StartsWith("Pacific")) return 0; if (id.StartsWith("US Mountain")) return 2; if (id.StartsWith("Mountain")) return 1;
            if (id.StartsWith("Central Standard") || id.StartsWith("Central America")) return 3; if (id.StartsWith("Eastern") || id.StartsWith("US Eastern")) return 4;
            if (id.StartsWith("Alaska")) return 5; if (id.StartsWith("Hawaii")) return 6; if (id.StartsWith("GMT")) return 7;
            if (id.StartsWith("W. Europe") || id.StartsWith("Romance") || id.StartsWith("Central Europe")) return 8;
            if (id.StartsWith("GTB") || id.StartsWith("E. Europe") || id.StartsWith("FLE")) return 9; if (id.StartsWith("AUS Eastern")) return 10;
            return 0;
        }

        static string Str(Dictionary<string, object> d, string k, string def = "") => d.TryGetValue(k, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : def;
        static int Int(Dictionary<string, object> d, string k, int def = 0) { try { return d.TryGetValue(k, out var v) && v != null ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : def; } catch { return def; } }
        static bool Bool(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) && v is bool b && b;

        string Describe(Dictionary<string, object> it)
        {
            string t = Str(it, "t"); int d = Int(it, "d");
            string label = Types.FirstOrDefault(x => x.type == t).label ?? t;
            string extra = t switch
            {
                "text" => $": \"{Str(it, "text")}\" ({Str(it, "fx", "scroll")})",
                "anim" => ": " + Str(it, "name"),
                "countdown" => ": " + Str(it, "label"),
                "clock" => Str(it, "fmt", "12") == "24" ? " (24 h)" : "",
                "pattern" => ": " + Str(it, "p", "corners"),
                _ => "",
            };
            return $"{label}{extra}   [{(d == 0 ? "auto" : d + " s")}]";
        }

        void RefreshList(int select)
        {
            _loading = true;
            _list.Items.Clear();
            foreach (var it in _items) _list.Items.Add(Describe(it));
            _loading = false;
            if (_items.Count > 0) _list.SelectedIndex = Math.Max(0, Math.Min(select, _items.Count - 1));
            else BuildEditor();
        }

        void AddItem(string type)
        {
            var it = new Dictionary<string, object> { ["t"] = type, ["d"] = type == "text" || type == "ticker" || type == "anim" ? 0 : 10 };
            if (type == "text") { it["text"] = "NEW MESSAGE"; it["fx"] = "scroll"; }
            if (type == "clock") it["fmt"] = "12";
            if (type == "countdown") { it["label"] = "NEW YEAR"; it["at"] = new DateTimeOffset(new DateTime(DateTime.Now.Year + 1, 1, 1)).ToUnixTimeSeconds(); }
            if (type == "anim") it["name"] = _anims.FirstOrDefault() ?? "corvette";
            if (type == "pattern") it["p"] = "corners";
            if (type == "fight") it["d"] = 20;
            _items.Insert(Sel + 1, it);
            RefreshList(Sel + 1);
        }

        void Move(int dir)
        {
            int i = Sel, j = i + dir;
            if (i < 0 || j < 0 || j >= _items.Count) return;
            (_items[i], _items[j]) = (_items[j], _items[i]);
            RefreshList(j);
        }

        void Changed() { if (_loading || Sel < 0) return; int i = Sel; _loading = true; _list.Items[i] = Describe(_items[i]); _loading = false; }

        void BuildEditor()
        {
            if (_loading) return;
            _editor.SuspendLayout();
            _editor.Controls.Clear();
            if (Sel < 0) { _editor.ResumeLayout(); return; }
            var it = _items[Sel];
            string t = Str(it, "t");
            Label L(string s) => new Label { Text = s, AutoSize = true, Margin = new Padding(0, 8, 0, 2) };
            Label Note(string s) => new Label { Text = s, AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = Color.DimGray, Margin = new Padding(0, 6, 0, 2) };

            _editor.Controls.Add(new Label { Text = Types.FirstOrDefault(x => x.type == t).label ?? t, AutoSize = true, Font = new Font(Font.FontFamily, 12f, FontStyle.Bold) });
            _editor.Controls.Add(L("Show for (seconds, 0 = automatic):"));
            var dur = new NumericUpDown { Minimum = 0, Maximum = 3600, Value = Int(it, "d"), Width = 70 };
            dur.ValueChanged += (s, e) => { it["d"] = (int)dur.Value; Changed(); };
            _editor.Controls.Add(dur);
            _editor.Controls.Add(Note("Automatic: text and tickers scroll through once, animations play once, everything else shows for about 10 s."));

            switch (t)
            {
                case "text":
                    _editor.Controls.Add(L("Text:"));
                    var tb = new TextBox { Width = 320, Text = Str(it, "text"), MaxLength = 200 };
                    tb.TextChanged += (s, e) => { it["text"] = tb.Text; Changed(); };
                    _editor.Controls.Add(tb);
                    _editor.Controls.Add(L("Effect:"));
                    var fx = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
                    fx.Items.AddRange(new object[] { "scroll", "static", "blink", "wipe" });
                    fx.SelectedItem = Str(it, "fx", "scroll");
                    fx.SelectedIndexChanged += (s, e) => { it["fx"] = fx.SelectedItem; Changed(); };
                    _editor.Controls.Add(fx);
                    _editor.Controls.Add(Note("static centres the text (it scrolls anyway if it's too long), blink flashes it, wipe reveals it left to right."));
                    break;
                case "clock":
                    var h24 = new CheckBox { Text = "24-hour", AutoSize = true, Checked = Str(it, "fmt") == "24" };
                    h24.CheckedChanged += (s, e) => { it["fmt"] = h24.Checked ? "24" : "12"; Changed(); };
                    var sec = new CheckBox { Text = "Show seconds", AutoSize = true, Checked = Bool(it, "sec") };
                    sec.CheckedChanged += (s, e) => { it["sec"] = sec.Checked; Changed(); };
                    var date = new CheckBox { Text = "Show the date above the time (small text)", AutoSize = true, Checked = Bool(it, "date") };
                    date.CheckedChanged += (s, e) => { it["date"] = date.Checked; Changed(); };
                    _editor.Controls.AddRange(new Control[] { h24, sec, date });
                    _editor.Controls.Add(Note("Time comes from the internet (and from this PC whenever the app connects). Set the time zone on the right."));
                    break;
                case "countdown":
                    _editor.Controls.Add(L("Label:"));
                    var lb = new TextBox { Width = 200, Text = Str(it, "label"), MaxLength = 30 };
                    lb.TextChanged += (s, e) => { it["label"] = lb.Text; Changed(); };
                    _editor.Controls.Add(lb);
                    _editor.Controls.Add(L("Counts down to:"));
                    long at = it.TryGetValue("at", out var a) ? Convert.ToInt64(a) : 0;
                    var dt = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm", Width = 170,
                        Value = at > 0 ? DateTimeOffset.FromUnixTimeSeconds(at).LocalDateTime : DateTime.Now.AddDays(7) };
                    dt.ValueChanged += (s, e) => { it["at"] = new DateTimeOffset(dt.Value).ToUnixTimeSeconds(); Changed(); };
                    _editor.Controls.Add(dt);
                    break;
                case "anim":
                    _editor.Controls.Add(L("Animation stored on the sign:"));
                    var an = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 200 };
                    foreach (var n in _anims) an.Items.Add(n);
                    an.Text = Str(it, "name");
                    an.TextChanged += (s, e) => { it["name"] = an.Text; Changed(); };
                    _editor.Controls.Add(an);
                    _editor.Controls.Add(Note("Make animations and pictures in the Pixel art tab (draw, or import a GIF/image) and upload them with a name."));
                    break;
                case "pattern":
                    var pt = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
                    pt.Items.AddRange(new object[] { "corners", "walkrow", "walkcol", "allon" });
                    pt.SelectedItem = Str(it, "p", "corners");
                    pt.SelectedIndexChanged += (s, e) => { it["p"] = pt.SelectedItem; Changed(); };
                    _editor.Controls.Add(pt);
                    _editor.Controls.Add(Note("ALL ON switches itself off after 15 s to protect the LEDs."));
                    break;
                case "weather": _editor.Controls.Add(Note("Shows the label and current temperature + conditions. Set the city on the right. Updates every 15 minutes (Open-Meteo, free).")); break;
                case "ticker": _editor.Controls.Add(Note("Scrolls prices with today's change. Set symbols on the right. Updates every 5 minutes (Yahoo Finance and CoinGecko, free, no account).")); break;
                case "jackpot": _editor.Controls.Add(Note("A casino-style progressive jackpot that keeps climbing, with chase lights and a JACKPOT! flash. Set the start and speed on the right.")); break;
                case "fight": _editor.Controls.Add(Note("Round, fight clock and FIGHTER VS FIGHTER. Fed live by the Fight Night tab while the app runs, or typed in by hand there.")); break;
                case "gamble": _editor.Controls.Add(Note("Total bet, total won and the net for the night. Update the amounts in the Fight Night tab.")); break;
            }
            _editor.ResumeLayout();
        }

        async Task LoadFromBoard()
        {
            var al = await _ctx.Try("ANIM LIST");
            if (al != null) _anims = al.List("anims").Select(a => a.Str("name")).Where(n => !n.StartsWith("_")).ToList();
            var r = await _ctx.Try("PLAYLIST GET");
            if (r != null && r.D.TryGetValue("playlist", out var p) && p is Dictionary<string, object> pd && pd.TryGetValue("items", out var items) && items is object[] arr)
            {
                _items.Clear();
                foreach (var o in arr) if (o is Dictionary<string, object> d) _items.Add(d);
                RefreshList(0);
            }
            var c = await _ctx.Try("CONF GET");
            if (c != null)
            {
                _schedOn.Checked = c.Str("sched_on") == "1";
                _schedStart.Value = DateTime.Today.Add(TimeSpan.TryParse(c.Str("sched_start", "08:00"), out var a) ? a : TimeSpan.FromHours(8));
                _schedEnd.Value = DateTime.Today.Add(TimeSpan.TryParse(c.Str("sched_end", "23:30"), out var b) ? b : TimeSpan.FromHours(23.5));
                int zi = Array.FindIndex(Zones, z => z.posix == c.Str("tz")); if (zi >= 0) _tz.SelectedIndex = zi;
                _wLabel.Text = c.Str("weather_label"); _wLat.Text = c.Str("weather_lat"); _wLon.Text = c.Str("weather_lon");
                _units.SelectedItem = c.Str("weather_units", "F") == "C" ? "C" : "F";
                _stocks.Text = c.Str("stocks"); _crypto.Text = c.Str("crypto");
                _jStart.Text = c.Str("jackpot_start"); _jRate.Text = c.Str("jackpot_rate");
            }
            await ShowLive();
        }

        async Task SaveToBoard()
        {
            var json = Json.Write(new Dictionary<string, object> { ["items"] = _items });
            if (json.Length > 7000) { _ctx.Status("Playlist is too long for one save (keep it under ~40 items)."); return; }
            if (await _ctx.Try("PLAYLIST SET " + json) == null) return;
            if (_ctx.Mode == "diag") await _ctx.SetMode("run");
            await _ctx.Try("PLAY");
            _ctx.Status($"Playlist saved on the sign ({_items.Count} items) and playing.");
        }

        async Task SaveSettings()
        {
            var kv = new List<(string, string)>
            {
                ("sched_on", _schedOn.Checked ? "1" : "0"), ("sched_start", _schedStart.Value.ToString("HH:mm")), ("sched_end", _schedEnd.Value.ToString("HH:mm")),
                ("tz", Zones[Math.Max(0, _tz.SelectedIndex)].posix),
                ("weather_label", _wLabel.Text.Trim().ToUpperInvariant()), ("weather_lat", _wLat.Text.Trim()), ("weather_lon", _wLon.Text.Trim()),
                ("weather_units", (string)_units.SelectedItem), ("stocks", _stocks.Text.Replace(" ", "")), ("crypto", _crypto.Text.Replace(" ", "").ToLowerInvariant()),
                ("jackpot_start", _jStart.Text.Trim()), ("jackpot_rate", _jRate.Text.Trim()),
            };
            foreach (var (k, v) in kv) if (await _ctx.Try($"CONF SET {k} {v}") == null) return;
            _ctx.Status("Settings saved on the sign. Weather and prices refresh within a few seconds.");
            await Task.Delay(5000);
            await ShowLive();
        }

        async Task ShowLive()
        {
            var r = await _ctx.Try("LIVE GET");
            if (r == null) return;
            _liveState.Text = $"Weather: {(r.Str("weather").Length > 0 ? r.Str("weather") : "(none yet)")}\nTicker:  {(r.Str("ticker").Length > 0 ? r.Str("ticker") : "(none yet)")}\n" +
                              "(Data is only fetched for widgets that are in the playlist.)";
        }

        async Task LookupCity()
        {
            string q = _city.Text.Trim();
            if (q.Length == 0) return;
            try
            {
                using var wc = new WebClient { Encoding = System.Text.Encoding.UTF8 };
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                string body = await wc.DownloadStringTaskAsync("https://geocoding-api.open-meteo.com/v1/search?count=1&name=" + Uri.EscapeDataString(q));
                var res = Json.Parse(body).List("results");
                if (res.Count == 0) { _ctx.Status("City not found."); return; }
                var c = res[0];
                _wLat.Text = c.Dbl("latitude").ToString("0.00", CultureInfo.InvariantCulture);
                _wLon.Text = c.Dbl("longitude").ToString("0.00", CultureInfo.InvariantCulture);
                _wLabel.Text = c.Str("name").ToUpperInvariant();
                _ctx.Status($"Found {c.Str("name")}, {c.Str("admin1")}, {c.Str("country")}. Press Save settings to sign.");
            }
            catch (Exception ex) { _ctx.Status("City lookup failed: " + ex.Message); }
        }
    }
}
