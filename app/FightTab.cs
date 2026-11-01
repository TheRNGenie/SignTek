using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CasinoSignTool
{
    /// <summary>
    /// Fight Night: follows tonight's UFC card from ESPN's public scoreboard feed (no account or key)
    /// and pushes round / clock / names to the sign. Manual entry works without internet.
    /// Also a betting tracker that keeps the sign's BET / WON / NET up to date.
    /// </summary>
    public class FightTab : UserControl
    {
        const string EspnUrl = "https://site.api.espn.com/apis/site/v2/sports/mma/ufc/scoreboard";
        readonly Ctx _ctx;
        readonly Timer _poll = new Timer { Interval = 10000 };
        CheckBox _live, _follow, _withGamble;
        ListView _card;
        Label _feedState, _preview, _totals;
        TextBox _a, _b, _clock, _result;
        NumericUpDown _round, _rounds;
        ComboBox _status;
        DataGridView _bets;
        bool _fetching, _manualDirty;
        string _pinnedId;                    // fight picked by hand in the list
        int _lastPeriod = -1; DateTime _periodStart = DateTime.Now; string _lastId;

        class Fight { public string Id, A, B, AFull, BFull, State, Detail, Winner, Weight; public int Period, Rounds, Clock; public bool EndOfRound; }
        List<Fight> _fights = new List<Fight>();

        public class Bet { public string Pick { get; set; } = ""; public decimal Stake { get; set; } public string Odds { get; set; } = ""; public string Result { get; set; } = "pending"; }
        readonly BindingSourceList _betList = new BindingSourceList();
        public class BindingSourceList : System.ComponentModel.BindingList<Bet> { }

        static string BetFile => Path.Combine(AppSettings.Dir, $"fightnight-{DateTime.Now:yyyy-MM-dd}.json");

        public FightTab(Ctx ctx)
        {
            _ctx = ctx;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            Controls.Add(root);
            Label H(string t) => new Label { Text = t, AutoSize = true, Font = new Font(Font.FontFamily, 10f, FontStyle.Bold), Margin = new Padding(0, 8, 0, 4) };
            Label L(string t, int w = 80) => new Label { Text = t, Width = w, Margin = new Padding(0, 6, 0, 0) };
            FlowLayoutPanel Row(params Control[] cs) { var r = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) }; r.Controls.AddRange(cs); return r; }
            Button Btn(string t, Func<Task> a, bool bold = false) { var b = new Button { Text = t, AutoSize = true }; if (bold) b.Font = new Font(Font, FontStyle.Bold); b.Click += async (s, e) => await a(); return b; }

            // ---------------- Left: live card + manual ----------------
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5 };
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(left, 0, 0);

            var mode = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            mode.Controls.Add(Btn("▶ Fight Night mode ON", FightModeOn, true));
            mode.Controls.Add(Btn("Back to playlist", async () => { await _ctx.Try("PLAY"); _ctx.Status("Back to the normal playlist."); }));
            _withGamble = new CheckBox { Text = "include the betting tally", AutoSize = true, Checked = true, Margin = new Padding(8, 6, 0, 0) };
            mode.Controls.Add(_withGamble);
            left.Controls.Add(mode, 0, 0);

            var feed = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            _live = new CheckBox { Text = "Live UFC data (ESPN, every 10 s)", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 6, 8, 0) };
            _live.CheckedChanged += async (s, e) => { if (_live.Checked) { _poll.Start(); await Fetch(); } else _poll.Stop(); };
            _follow = new CheckBox { Text = "follow the fight that's on now", AutoSize = true, Checked = true, Margin = new Padding(0, 6, 8, 0) };
            _follow.CheckedChanged += (s, e) => { if (_follow.Checked) _pinnedId = null; };
            feed.Controls.Add(_live); feed.Controls.Add(_follow);
            feed.Controls.Add(Btn("Refresh", Fetch));
            _feedState = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(8, 6, 0, 0) };
            feed.Controls.Add(_feedState);
            left.Controls.Add(feed, 0, 1);

            _card = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
            _card.Columns.Add("Fight", 260); _card.Columns.Add("Class", 110); _card.Columns.Add("Status", 200);
            _card.ItemActivate += async (s, e) => { if (_card.SelectedItems.Count > 0) { _pinnedId = (string)_card.SelectedItems[0].Tag; _follow.Checked = false; await PushLive(); } };
            left.Controls.Add(_card, 0, 2);
            left.Controls.Add(new Label { Text = "Double-click a fight to show that one instead of following the live fight.", AutoSize = true, ForeColor = Color.DimGray }, 0, 3);

            var man = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            man.Controls.Add(H("Manual entry (no internet needed; turns live data off)"));
            _a = new TextBox { Width = 140 }; _b = new TextBox { Width = 140 };
            man.Controls.Add(Row(L("Fighters:"), _a, new Label { Text = "vs", AutoSize = true, Margin = new Padding(4, 6, 4, 0) }, _b));
            _round = new NumericUpDown { Minimum = 1, Maximum = 5, Value = 1, Width = 45 };
            _rounds = new NumericUpDown { Minimum = 1, Maximum = 5, Value = 3, Width = 45 };
            _clock = new TextBox { Width = 50, Text = "5:00" };
            man.Controls.Add(Row(L("Round:"), _round, new Label { Text = "of", AutoSize = true, Margin = new Padding(4, 6, 4, 0) }, _rounds, L("   Clock:", 55), _clock));
            _status = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
            _status.Items.AddRange(new object[] { "next", "live", "between", "final" }); _status.SelectedIndex = 0;
            _result = new TextBox { Width = 170 };
            man.Controls.Add(Row(L("Status:"), _status, L("  Result:", 55), _result));
            foreach (Control c in new Control[] { _a, _b, _clock, _result }) c.TextChanged += (s, e) => _manualDirty = true;
            var mb = new FlowLayoutPanel { AutoSize = true };
            mb.Controls.Add(Btn("Send", () => SendManual(null), true));
            mb.Controls.Add(Btn("Start round", async () => { _status.SelectedItem = "live"; await SendManual(true); }));
            mb.Controls.Add(Btn("Pause clock", () => SendManual(false)));
            mb.Controls.Add(Btn("End of round", async () => { _status.SelectedItem = "between"; await SendManual(false); }));
            mb.Controls.Add(Btn("Next round", async () => { if (_round.Value < _round.Maximum) _round.Value++; _clock.Text = "5:00"; _status.SelectedItem = "live"; await SendManual(true); }));
            mb.Controls.Add(Btn("Final", async () => { _status.SelectedItem = "final"; await SendManual(false); }));
            man.Controls.Add(mb);
            left.Controls.Add(man, 0, 4);

            // ---------------- Right: preview + betting ----------------
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, Padding = new Padding(8, 0, 0, 0) };
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(right, 1, 0);
            right.Controls.Add(H("On the sign"), 0, 0);
            _preview = new Label { AutoSize = true, Font = new Font("Consolas", 11f, FontStyle.Bold), BackColor = Color.Black, ForeColor = Color.FromArgb(255, 80, 40), Padding = new Padding(8), MinimumSize = new Size(300, 0), Text = "FIGHT NIGHT\nWAITING FOR DATA" };
            right.Controls.Add(_preview, 0, 1);
            right.Controls.Add(H("Tonight's bets"), 0, 2);
            _bets = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = true, RowHeadersVisible = false, BackgroundColor = SystemColors.Window, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
            _bets.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Pick / fight", DataPropertyName = "Pick", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            _bets.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Stake $", DataPropertyName = "Stake", Width = 70 });
            _bets.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Odds", DataPropertyName = "Odds", Width = 60, ToolTipText = "American odds like +150 or -200 (blank = even money)" });
            var res = new DataGridViewComboBoxColumn { HeaderText = "Result", DataPropertyName = "Result", Width = 85, FlatStyle = FlatStyle.Flat };
            res.Items.AddRange("pending", "won", "lost", "push");
            _bets.Columns.Add(res);
            _bets.DataSource = _betList;
            _bets.DataError += (s, e) => { e.ThrowException = false; };
            _bets.CellEndEdit += async (s, e) => await BetsChanged();
            _bets.UserDeletedRow += async (s, e) => await BetsChanged();
            _bets.CurrentCellDirtyStateChanged += (s, e) => { if (_bets.IsCurrentCellDirty && _bets.CurrentCell is DataGridViewComboBoxCell) _bets.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _bets.CellValueChanged += async (s, e) => { if (e.ColumnIndex == 3) await BetsChanged(); };
            right.Controls.Add(_bets, 0, 3);
            _totals = new Label { AutoSize = true, Font = new Font(Font.FontFamily, 11f, FontStyle.Bold), Margin = new Padding(0, 6, 0, 0) };
            right.Controls.Add(_totals, 0, 4);
            var gb = new FlowLayoutPanel { AutoSize = true };
            gb.Controls.Add(Btn("Send to sign", PushGamble, true));
            gb.Controls.Add(Btn("Show tally now", async () => { if (_ctx.Mode == "diag") await _ctx.SetMode("run"); await PushGamble(); await _ctx.Try("SHOW {\"t\":\"gamble\"}"); }));
            gb.Controls.Add(Btn("New night (clear)", async () =>
            {
                if (MessageBox.Show("Clear tonight's bets?", "Fight Night", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                _betList.Clear(); await BetsChanged();
            }));
            gb.Controls.Add(new Label { Text = "Winnings = what winning bets paid back (stake + profit). Net = winnings − total bet.", AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(420, 0) });
            right.Controls.Add(gb, 0, 5);

            _poll.Tick += async (s, e) => await Fetch();
            LoadBets();
            UpdateTotals();
        }

        // ---------------- live feed ----------------
        async Task Fetch()
        {
            if (_fetching) return;
            _fetching = true;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using var wc = new WebClient { Encoding = System.Text.Encoding.UTF8 };
                wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 CasinoSignTool";
                string body = await wc.DownloadStringTaskAsync(EspnUrl);
                _fights = Parse(Json.Parse(body));
                ShowCard();
                _feedState.Text = $"updated {DateTime.Now:HH:mm:ss}";
                if (_live.Checked) await PushLive();
            }
            catch (Exception ex) { _feedState.Text = "feed error: " + ex.Message; }
            finally { _fetching = false; }
        }

        /// <summary>The sign's font is plain ASCII: turn "Szabová" into "Szabova".</summary>
        static string Plain(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char ch in s.Normalize(System.Text.NormalizationForm.FormD))
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                sb.Append(ch < 128 ? ch : ch == 'ø' ? 'o' : ch == 'Ø' ? 'O' : ch == 'ł' ? 'l' : ch == 'ß' ? 's' : '?');
            }
            return sb.ToString();
        }

        static List<Fight> Parse(Reply root)
        {
            var list = new List<Fight>();
            foreach (var ev in root.List("events"))
            {
                if (!(ev.D.TryGetValue("competitions", out var comps) && comps is object[] arr)) continue;
                foreach (var co in arr.OfType<Dictionary<string, object>>().Select(d => new Reply(d)))
                {
                    var f = new Fight { Id = co.Str("id") };
                    var cs = co.List("competitors");
                    // ESPN lists competitors with "order"; keep that order.
                    cs = cs.OrderBy(c => c.Int("order", 1)).ToList();
                    if (cs.Count < 2) continue;
                    string Name(Reply c, bool full)
                    {
                        var ath = c.D.TryGetValue("athlete", out var a) ? new Reply(a as Dictionary<string, object>) : new Reply(null);
                        string dn = ath.Str("displayName", ath.Str("shortName", "?"));
                        if (full) return dn;
                        string last = ath.Str("lastName"); if (last.Length == 0) last = dn.Split(' ').Last();
                        return Plain(last).ToUpperInvariant();
                    }
                    f.A = Name(cs[0], false); f.B = Name(cs[1], false); f.AFull = Name(cs[0], true); f.BFull = Name(cs[1], true);
                    if (cs[0].Bool("winner")) f.Winner = f.A; else if (cs[1].Bool("winner")) f.Winner = f.B;
                    var st = co.D.TryGetValue("status", out var so) ? new Reply(so as Dictionary<string, object>) : new Reply(null);
                    var type = st.D.TryGetValue("type", out var to) ? new Reply(to as Dictionary<string, object>) : new Reply(null);
                    f.State = type.Str("state");                        // pre / in / post
                    f.Detail = type.Str("shortDetail", type.Str("detail"));
                    f.EndOfRound = type.Str("name").Contains("END_PERIOD") || type.Str("name").Contains("HALFTIME");
                    f.Period = st.Int("period");
                    f.Clock = ParseClock(st.Str("displayClock"));
                    var fmt = co.D.TryGetValue("format", out var fo) ? new Reply(fo as Dictionary<string, object>) : new Reply(null);
                    var reg = fmt.D.TryGetValue("regulation", out var ro) ? new Reply(ro as Dictionary<string, object>) : new Reply(null);
                    f.Rounds = reg.Int("periods", 3);
                    var ty = co.D.TryGetValue("type", out var cto) ? new Reply(cto as Dictionary<string, object>) : new Reply(null);
                    f.Weight = ty.Str("abbreviation");
                    list.Add(f);
                }
            }
            return list;
        }

        static int ParseClock(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return -1;
            var p = s.Split(':');
            if (p.Length == 2 && int.TryParse(p[0], out int m) && int.TryParse(p[1].Split('.')[0], out int sec)) return m * 60 + sec;
            return -1;
        }

        void ShowCard()
        {
            _card.BeginUpdate();
            _card.Items.Clear();
            foreach (var f in _fights)
            {
                var it = new ListViewItem(new[] { $"{f.AFull} vs {f.BFull}", f.Weight, f.State == "in" ? $"LIVE R{f.Period} {f.Detail}" : f.Detail }) { Tag = f.Id };
                if (f.State == "in") it.BackColor = Color.FromArgb(255, 230, 210);
                if (f.Id == Current()?.Id) it.Font = new Font(_card.Font, FontStyle.Bold);
                _card.Items.Add(it);
            }
            _card.EndUpdate();
        }

        /// <summary>The fight to show: picked by hand, else the live one, else the next one, else the last result.</summary>
        Fight Current()
        {
            if (_pinnedId != null) { var p = _fights.FirstOrDefault(f => f.Id == _pinnedId); if (p != null) return p; }
            // ESPN lists the main event first; the card runs bottom-up.
            var live = _fights.FirstOrDefault(f => f.State == "in");
            if (live != null) return live;
            var next = _fights.LastOrDefault(f => f.State == "pre");
            var lastDone = _fights.FirstOrDefault(f => f.State == "post");
            if (lastDone != null && next != null && _fights.IndexOf(lastDone) > _fights.IndexOf(next)) return next;
            return next ?? lastDone ?? _fights.FirstOrDefault();
        }

        async Task PushLive()
        {
            var f = Current();
            if (f == null) return;
            string status = f.State == "in" ? (f.EndOfRound ? "between" : "live") : f.State == "post" ? "final" : "next";
            int clock = f.Clock;
            // Some feeds don't fill in the clock: estimate it from when the round started.
            if (f.Id != _lastId || f.Period != _lastPeriod) { _periodStart = DateTime.Now; _lastPeriod = f.Period; _lastId = f.Id; }
            if (status == "live" && clock <= 0) clock = Math.Max(0, 300 - (int)(DateTime.Now - _periodStart).TotalSeconds);
            string result = status == "final" ? (f.Winner != null ? $"{f.Winner} WINS" : "FINAL") + (f.Detail.Length > 0 ? " " + f.Detail.Replace("Final", "").Trim(' ', '-') : "") : "";
            var o = new Dictionary<string, object>
            {
                ["a"] = f.A, ["b"] = f.B, ["status"] = status, ["round"] = Math.Max(1, f.Period), ["rounds"] = f.Rounds,
                ["clock"] = Math.Max(0, clock), ["running"] = status == "live", ["result"] = result.ToUpperInvariant(), ["event"] = "UFC",
            };
            UpdatePreview(o);
            if (_ctx.Board.Connected) await _ctx.Try("FIGHT SET " + Json.Write(o));
        }

        async Task SendManual(bool? running)
        {
            _live.Checked = false;
            int clock = ParseClock(_clock.Text); if (clock < 0) clock = 300;
            var o = new Dictionary<string, object>
            {
                ["a"] = _a.Text.Trim().ToUpperInvariant(), ["b"] = _b.Text.Trim().ToUpperInvariant(), ["status"] = (string)_status.SelectedItem,
                ["round"] = (int)_round.Value, ["rounds"] = (int)_rounds.Value, ["clock"] = clock, ["result"] = _result.Text.Trim().ToUpperInvariant(), ["event"] = "FIGHT NIGHT",
            };
            if (running.HasValue) o["running"] = running.Value;
            UpdatePreview(o);
            _manualDirty = false;
            var r = await _ctx.Try("FIGHT SET " + Json.Write(o));
            if (r != null) _ctx.Status("Fight data sent to the sign.");
        }

        void UpdatePreview(Dictionary<string, object> o)
        {
            string st = (string)o["status"]; int rd = Convert.ToInt32(o["round"]); int ck = Convert.ToInt32(o["clock"]);
            string top = st == "live" ? $"RD {rd}  {ck / 60}:{ck % 60:00}" : st == "between" ? $"END OF RD {rd}" : st == "final" ? (string)o["result"] : "UP NEXT";
            _preview.Text = $"{top}\n{o["a"]} VS {o["b"]}";
        }

        async Task FightModeOn()
        {
            if (!_ctx.Board.Connected) { _ctx.Status("Connect to the sign first (top bar)."); return; }
            if (_ctx.Mode == "diag" && !await _ctx.SetMode("run")) return;
            if (_withGamble.Checked) await PushGamble();
            string show = _withGamble.Checked ? "[{\"t\":\"fight\",\"d\":20},{\"t\":\"gamble\",\"d\":8}]" : "{\"t\":\"fight\",\"d\":0}";
            if (await _ctx.Try("SHOW " + show) != null) _ctx.Status("Fight Night mode is on. Turn on live data, or use manual entry.");
            if (!_live.Checked && !_manualDirty) _live.Checked = true;
        }

        // ---------------- betting ----------------
        static decimal Payout(Bet b)
        {
            if (b.Result == "push") return b.Stake;
            if (b.Result != "won") return 0;
            string o = (b.Odds ?? "").Trim().Replace("+", "");
            if (!decimal.TryParse(o, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal odds) || odds == 0) return b.Stake * 2;   // even money
            decimal profit = odds > 0 ? b.Stake * odds / 100m : b.Stake * 100m / -odds;
            return Math.Round(b.Stake + profit, 2);
        }

        (decimal bet, decimal won) Totals() => (_betList.Sum(b => b.Stake), _betList.Sum(Payout));

        void UpdateTotals()
        {
            var (bet, won) = Totals();
            decimal net = won - bet;
            _totals.Text = $"Bet ${bet:N0}   Won ${won:N0}   Net {(net >= 0 ? "+" : "−")}${Math.Abs(net):N0}";
            _totals.ForeColor = net >= 0 ? Color.DarkGreen : Color.DarkRed;
        }

        async Task BetsChanged()
        {
            UpdateTotals();
            try { Directory.CreateDirectory(AppSettings.Dir); File.WriteAllText(BetFile, Json.Write(_betList.ToList())); } catch { }
            await PushGamble(quiet: true);
        }

        Task PushGamble() => PushGamble(false);
        async Task PushGamble(bool quiet)
        {
            if (!_ctx.Board.Connected) { if (!quiet) _ctx.Status("Connect to the sign first."); return; }
            var (bet, won) = Totals();
            string cmd = string.Format(CultureInfo.InvariantCulture, "GAMBLE SET {0:0.##} {1:0.##} FIGHT NIGHT", bet, won);
            var r = quiet ? await _ctx.Board.Send(cmd, 2000, false).ContinueWith(t => t.IsFaulted ? null : t.Result) : await _ctx.Try(cmd);
            if (r != null && !quiet) _ctx.Status("Betting tally sent to the sign.");
        }

        void LoadBets()
        {
            try
            {
                if (!File.Exists(BetFile)) return;
                foreach (var b in Json.Read<List<Bet>>(File.ReadAllText(BetFile)) ?? new List<Bet>()) _betList.Add(b);
            }
            catch { }
        }
    }
}
