using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CasinoSignTool
{
    /// <summary>Shared state every tab can use.</summary>
    public class Ctx
    {
        public Board Board = new Board();
        public HeaderMap Map = new HeaderMap();
        public SignCfg Cfg = new SignCfg();
        public Reply Info;                      // last INFO reply
        public List<int> OutPins = new List<int> { 23, 19, 18, 21, 32, 25, 0, 22, 16, 17, 26, 4 };
        public List<int> InPins = new List<int> { 39, 35, 23, 19, 18, 21, 32, 25, 0, 22, 16, 17, 26, 4 };
        public List<int> StrapPins = new List<int> { 0 };
        public string Mode = "";                // "run" / "diag"
        public bool Power = true;               // sign on/off (POWER command)

        public event Action StateChanged;       // connection or mode changed
        public event Action MapChanged;
        public event Action CfgChanged;
        public Action<string> Status;           // show a line in the status bar
        public Func<string, Task> ConnectPort;
        public AppSettings Settings;  // connect over a given COM port (used after flashing)
        public Control Ui;

        public void RaiseState() => Ui.BeginInvoke((Action)(() => StateChanged?.Invoke()));
        public void RaiseMap() { MapChanged?.Invoke(); try { System.IO.Directory.CreateDirectory(AppSettings.Dir); Map.Save(AppSettings.MapPath(Map.Kind)); } catch { } }
        public void RaiseCfg() => CfgChanged?.Invoke();

        /// <summary>Load the saved map of the 16-pin panel connector (the only connector used now).</summary>
        public HeaderMap LoadMap(string kind)
        {
            try
            {
                kind = "panel20";
                var path = AppSettings.MapPath(kind);
                if (System.IO.File.Exists(path))
                {
                    var m = HeaderMap.Load(path);
                    if (kind == "panel20") { m.Kind = "panel20"; if (m.PinCount != 16) { m.PinCount = 16; m.EnsurePins(); } }
                    return m;
                }
            }
            catch { }
            return HeaderMap.NewPanel();
        }

        /// <summary>Switch the pin mapper and logic analyzer to the other connector. Each one keeps its own saved map.</summary>
        public void SwitchMap(string kind)
        {
            if (Map.Kind == kind) return;
            try { System.IO.Directory.CreateDirectory(AppSettings.Dir); Map.Save(AppSettings.MapPath(Map.Kind)); } catch { }
            Map = LoadMap(kind);
            if (Settings != null) { Settings.ActiveMap = kind; Settings.Save(); }
            RaiseMap();
        }

        /// <summary>Run a board command, show errors in the status bar instead of throwing.</summary>
        public async Task<Reply> Try(string cmd, int timeoutMs = 3000)
        {
            if (!Board.Connected) { Status("Connect to the board first (top bar)."); return null; }
            try { return await Board.Send(cmd, timeoutMs); }
            catch (Exception ex) { Status("⚠ " + ex.Message); return null; }
        }

        public async Task<bool> SetMode(string mode)
        {
            var r = await Try("MODE " + mode.ToUpperInvariant());
            if (r == null) return false;
            Mode = r.Str("mode");
            RaiseState();
            return Mode == mode;
        }
    }

    public class MainForm : Form
    {
        readonly Ctx _ctx = new Ctx();
        readonly AppSettings _settings = AppSettings.Load();

        ComboBox _ports;
        TextBox _host;
        Button _btnUsb, _btnWifi, _btnDisc, _btnDiag, _btnRun, _btnFind, _btnPower;
        Label _connLabel, _modeLabel;
        ToolStripStatusLabel _status;
        TextBox _log, _cmdBox;
        TabControl _tabs;
        SplitContainer _split;

        public MainForm()
        {
            Text = "Casino Sign Tool";
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(1240, 820);
            MinimumSize = new Size(980, 680);
            StartPosition = FormStartPosition.CenterScreen;
            _ctx.Ui = this;
            _ctx.Settings = _settings;
            _ctx.Status = s => BeginInvoke((Action)(() => _status.Text = s));

            _ctx.Map = _ctx.LoadMap("panel20");

            BuildTopBar();
            BuildBody();

            _ctx.Board.Log += (line, outgoing) => BeginInvoke((Action)(() => AppendLog(line, outgoing)));
            _ctx.Board.Disconnected += why => BeginInvoke((Action)(() => { _status.Text = "Disconnected: " + why; UpdateConn(); }));
            _ctx.Board.EventReceived += r => BeginInvoke((Action)(() => OnBoardEvent(r)));
            _ctx.StateChanged += UpdateConn;
            _ctx.ConnectPort = async port =>
            {
                RefreshPorts();
                if (!_ports.Items.Contains(port)) _ports.Items.Add(port);
                _ports.SelectedItem = port;
                await Task.Delay(1500);   // board is still booting after the flash
                await ConnectSerial();
                if (_ctx.Board.Connected) _tabs.SelectedIndex = 1;
            };

            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            RefreshPorts();
            UpdateConn();
            FormClosing += (s, e) => { _settings.Save(); _ctx.Board.Dispose(); };
            Text = "Casino Sign Tool " + Updater.CurrentText;
            Shown += (s, e) =>
            {
                if (Updater.FirmwareChangedByUpdate())
                {
                    GoToTab("0. Install firmware");
                    _status.Text = "Updated to " + Updater.CurrentText + ". This update includes new sign firmware: press Install firmware (USB) or Update over WiFi to put it on the board.";
                }
                Updater.CheckInBackground(r => BeginInvoke((Action)(() =>
                {
                    _newer = r;
                    _btnUpdate.Text = "Update available";
                    _btnUpdate.BackColor = Color.Gold; _btnUpdate.ForeColor = Color.Black;
                    _status.Text = $"Version {r.Version.ToString(3)} of the app is available: press Update available in the top bar.";
                })));
            };
        }

        void BuildTopBar()
        {
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 6, 6, 2),
                WrapContents = false, BackColor = Color.FromArgb(32, 36, 44), ForeColor = Color.White,
            };
            Label L(string t) => new Label { Text = t, AutoSize = true, Margin = new Padding(8, 7, 2, 0) };

            _ports = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(2, 3, 2, 0) };
            var refresh = new Button { Text = "↻", Width = 30, Height = 26, FlatStyle = FlatStyle.Flat };
            refresh.Click += (s, e) => RefreshPorts();
            _btnUsb = new Button { Text = "Connect", AutoSize = true, Height = 26, FlatStyle = FlatStyle.Flat };
            _btnUsb.Click += async (s, e) => await ConnectSerial();

            _host = new TextBox { Width = 130, Text = _settings.LastHost, Margin = new Padding(2, 4, 2, 0) };
            _btnWifi = new Button { Text = "Connect", AutoSize = true, Height = 26, FlatStyle = FlatStyle.Flat };
            _btnWifi.Click += async (s, e) => await ConnectWifi();
            _btnFind = new Button { Text = "Find sign", AutoSize = true, Height = 26, FlatStyle = FlatStyle.Flat };
            _btnFind.Click += async (s, e) => await FindAndConnect(true);
            _btnPower = new Button { Text = "Sign ON", AutoSize = true, Height = 26, FlatStyle = FlatStyle.Flat, Font = new Font(Font, FontStyle.Bold) };
            _btnPower.Click += async (s, e) =>
            {
                var r = await _ctx.Try("POWER " + (_ctx.Power ? "OFF" : "ON"));
                if (r != null) { _ctx.Power = r.Bool("power"); UpdateConn(); _status.Text = _ctx.Power ? "Sign switched on (startup animation plays)." : "Sign switched off (shutdown animation plays)."; }
            };
            _btnDisc = new Button { Text = "Disconnect", AutoSize = true, Height = 26, FlatStyle = FlatStyle.Flat };
            _btnDisc.Click += (s, e) => { _ctx.Board.Disconnect("by you"); UpdateConn(); };

            _connLabel = new Label { AutoSize = true, Margin = new Padding(14, 7, 2, 0), ForeColor = Color.Silver };
            _modeLabel = new Label { AutoSize = true, Margin = new Padding(10, 4, 2, 0), Padding = new Padding(6, 3, 6, 3), Font = new Font(Font, FontStyle.Bold) };
            _btnDiag = new Button { Text = "Diagnostics", AutoSize = true, Height = 26, FlatStyle = FlatStyle.Flat };
            _btnDiag.Click += async (s, e) => await _ctx.SetMode("diag");
            _btnRun = new Button { Text = "Run sign", AutoSize = true, Height = 26, FlatStyle = FlatStyle.Flat };
            _btnRun.Click += async (s, e) => { if (await _ctx.SetMode("run")) _status.Text = "Sign scanning with the current pin settings."; };

            var help = new Button { Text = "? Help", AutoSize = true, Height = 26, FlatStyle = FlatStyle.Flat, Font = new Font(Font, FontStyle.Bold) };
            help.Click += (s, e) => { var t = _tabs?.SelectedTab != null ? Help.ForTab(_tabs.SelectedTab.Text) : null; Help.Show(this, t?.Id ?? "overview"); };
            _btnHelp = help;
            _btnUpdate = new Button { Text = "Updates", AutoSize = true, Height = 26, FlatStyle = FlatStyle.Flat };
            _btnUpdate.Click += async (s, e) => await Updater.ShowDialog(this, _newer);
            foreach (var b in new[] { help, refresh, _btnUsb, _btnWifi, _btnFind, _btnDisc, _btnDiag, _btnRun, _btnPower }) { b.FlatAppearance.BorderColor = Color.Gray; b.Margin = new Padding(2, 2, 2, 0); }

            bar.Controls.AddRange(new Control[] { L("USB:"), _ports, refresh, _btnUsb, L("  WiFi:"), _host, _btnWifi, _btnFind, _btnDisc, _connLabel, _btnPower, _modeLabel, _btnDiag, _btnRun, _btnHelp, _btnUpdate });
            Controls.Add(bar);

            var strip = new StatusStrip();
            _status = new ToolStripStatusLabel("Not connected. Plug the board in with USB and pick its COM port, or enter its WiFi address.") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            strip.Items.Add(_status);
            Controls.Add(strip);
        }

        Button _btnHelp, _btnUpdate;
        Updater.Release _newer;

        void BuildBody()
        {
            _split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2 };
            Controls.Add(_split);
            _split.BringToFront();

            _tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
            var start = new StartTab(_ctx) { GoToTab = GoToTab };
            AddTab("Start here", start);
            AddTab("0. Install firmware", new FlashTab(_ctx));
            AddTab("1. Pin mapper", new MapperTab(_ctx));
            AddTab("1b. Logic analyzer", new LogicTab(_ctx));
            AddTab("1c. Panel test", new PanelTestTab(_ctx));
            AddTab("2. Sign setup", new SetupTab(_ctx));
            AddTab("3. Wiring guide", new WiringTab(_ctx));
            AddTab("Display", new DisplayTab(_ctx));
            AddTab("Playlist && widgets", new PlaylistTab(_ctx));
            AddTab("Fight Night", new FightTab(_ctx));
            AddTab("Pixel art && animations", new PixelArtTab(_ctx));
            AddTab("WiFi && settings", new NetworkTab(_ctx));
            _split.Panel1.Controls.Add(_tabs);

            // Console: raw traffic + manual command line.
            _log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 8.5f), BackColor = Color.FromArgb(20, 20, 24), ForeColor = Color.Gainsboro, WordWrap = false };
            var cmdRow = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 30, ColumnCount = 3 };
            cmdRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cmdRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            cmdRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _cmdBox = new TextBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 9f) };
            var send = new Button { Text = "Send", AutoSize = true };
            send.Click += async (s, e) => await SendManual();
            _cmdBox.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SendManual(); } };
            cmdRow.Controls.Add(new Label { Text = "Console command:", AutoSize = true, Margin = new Padding(3, 7, 3, 0) }, 0, 0);
            cmdRow.Controls.Add(_cmdBox, 1, 0);
            cmdRow.Controls.Add(send, 2, 0);
            _split.Panel2.Controls.Add(_log);
            _split.Panel2.Controls.Add(cmdRow);
            Load += (s, e) => { try { _split.SplitterDistance = Math.Max(300, _split.Height - 150); } catch { } };
            Shown += async (s, e) => { if (_settings.AutoConnect && Environment.GetEnvironmentVariable("CST_NO_AUTOCONNECT") == null) await FindAndConnect(false); };
        }

        void AddTab(string title, Control c)
        {
            var p = new TabPage(title) { Padding = new Padding(6), UseVisualStyleBackColor = true };
            c.Dock = DockStyle.Fill;
            p.Controls.Add(c);
            var topic = Help.ForTab(title);
            if (topic != null) p.Controls.Add(HelpStrip.For(topic, this));
            _tabs.TabPages.Add(p);
        }

        void GoToTab(string title)
        {
            foreach (TabPage tp in _tabs.TabPages) if (tp.Text == title) { _tabs.SelectedTab = tp; return; }
        }

        void RefreshPorts()
        {
            var names = SerialPort.GetPortNames().Distinct().OrderBy(n => n.Length).ThenBy(n => n).ToArray();
            _ports.Items.Clear();
            _ports.Items.AddRange(names);
            if (names.Contains(_settings.LastPort)) _ports.SelectedItem = _settings.LastPort;
            else if (names.Length > 0) _ports.SelectedIndex = names.Length - 1;
        }

        async Task ConnectSerial()
        {
            if (_ports.SelectedItem == null) { _status.Text = "No COM port found. Plug the board in (USB) or pair it over Bluetooth, then press ↻."; return; }
            string port = (string)_ports.SelectedItem;
            _status.Text = $"Connecting to {port}…";
            SetBusy(true);
            try
            {
                var info = await _ctx.Board.ConnectSerial(port);
                _settings.LastPort = port; _settings.Save();
                await AfterConnect(info);
            }
            catch (Exception ex) { _status.Text = "⚠ " + ex.Message; }
            finally { SetBusy(false); UpdateConn(); }
        }

        async Task ConnectWifi()
        {
            string host = _host.Text.Trim();
            int port = 3333;
            if (host.Contains(":")) { var p = host.Split(':'); host = p[0]; int.TryParse(p[1], out port); }
            if (host.Length == 0) return;
            _status.Text = $"Connecting to {host}…";
            SetBusy(true);
            try
            {
                var info = await _ctx.Board.ConnectTcp(host, port);
                _settings.LastHost = _host.Text.Trim(); _settings.Save();
                await AfterConnect(info);
            }
            catch (Exception ex)
            {
                _status.Text = "⚠ " + ex.Message + (host.EndsWith(".local") ? "  (If .local names don't work on this PC, use the IP shown on the board's screen.)" : "");
            }
            finally { SetBusy(false); UpdateConn(); }
        }

        async Task AfterConnect(Reply info)
        {
            _ctx.Info = info;
            var outs = info.Ints("out_pins"); if (outs.Count > 0) _ctx.OutPins = outs;
            var ins = info.Ints("in_only_pins"); if (outs.Count > 0) _ctx.InPins = outs.Concat(ins).ToList();
            var straps = info.Ints("strap_pins"); if (straps.Count > 0) _ctx.StrapPins = straps;
            _ctx.Mode = info.Str("mode");
            _ctx.Power = info.Bool("power", true);
            // Give the board the PC's clock in case it has no internet for NTP.
            await _ctx.Try("TIME SET " + DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var cfg = await _ctx.Try("GET CFG");
            if (cfg != null) { _ctx.Cfg.From(cfg); _ctx.RaiseCfg(); }
            _status.Text = $"Connected to {_ctx.Board.Where}: firmware {info.Str("fw")}, {info.Str("chip")}, mode {info.Str("mode").ToUpperInvariant()}.";
            _ctx.RaiseState();
        }

        /// <summary>Look for signs on the network (UDP broadcast) and connect to the first one.</summary>
        async Task FindAndConnect(bool userAsked)
        {
            if (_ctx.Board.Connected) { if (userAsked) _status.Text = "Already connected. Disconnect first to switch signs."; return; }
            _status.Text = "Looking for the sign on your network…";
            SetBusy(true);
            List<Discovery.Found> found;
            try { found = await Discovery.Find(); }
            catch (Exception ex) { _status.Text = "⚠ Network search failed: " + ex.Message; SetBusy(false); return; }
            SetBusy(false);
            if (found.Count == 0)
            {
                _status.Text = userAsked ? "No sign answered. Is it powered, and on the same WiFi as this PC? (First time: set up WiFi over USB in the WiFi & settings tab.)" : "Not connected. Press Find sign, or plug in USB.";
                return;
            }
            var pick = found.FirstOrDefault(f => f.Ip == _settings.LastHost || f.Host + ".local" == _settings.LastHost) ?? found[0];
            if (found.Count > 1 && userAsked) _status.Text = $"Found {found.Count} signs; connecting to {pick}.";
            _host.Text = pick.Ip;
            await ConnectWifi();
        }

        void SetBusy(bool b) { _btnUsb.Enabled = _btnWifi.Enabled = _btnFind.Enabled = !b; Cursor = b ? Cursors.WaitCursor : Cursors.Default; }

        void UpdateConn()
        {
            bool c = _ctx.Board.Connected;
            _connLabel.Text = c ? $"● {(_ctx.Board.OverWifi ? "WiFi" : "Serial")} {_ctx.Board.Where}" : "○ not connected";
            _connLabel.ForeColor = c ? Color.LightGreen : Color.Silver;
            _btnDisc.Enabled = _btnDiag.Enabled = _btnRun.Enabled = _btnPower.Enabled = c;
            _btnPower.Text = _ctx.Power ? "Sign ON" : "Sign OFF";
            _btnPower.BackColor = c ? (_ctx.Power ? Color.SeaGreen : Color.DimGray) : Color.Transparent;
            if (!c) { _modeLabel.Text = ""; _modeLabel.BackColor = Color.Transparent; return; }
            bool diag = _ctx.Mode == "diag";
            _modeLabel.Text = diag ? "DIAGNOSTICS" : "RUNNING";
            _modeLabel.BackColor = diag ? Color.DarkOrange : Color.SeaGreen;
        }

        void OnBoardEvent(Reply r)
        {
            switch (r.Str("evt"))
            {
                case "probe_off": _status.Text = $"Probe on GPIO {r.Int("gpio")} switched itself off (safety timeout)."; break;
                case "wifi": _status.Text = r.Bool("connected") ? $"Board joined WiFi, IP {r.Str("ip")}" : "Board lost WiFi."; break;
                case "boot": _status.Text = "Board restarted."; _ctx.Mode = ""; break;
                case "power": _ctx.Power = r.Bool("on"); UpdateConn(); break;
                case "watchdog": _status.Text = $"⚠ Scan watchdog tripped ({r.Int("trips")}x): the sign was blanked for safety and the scan restarted."; break;
            }
        }

        void AppendLog(string line, bool outgoing)
        {
            if (_log.TextLength > 200000) _log.Text = _log.Text.Substring(100000);
            _log.AppendText((outgoing ? "→ " : "← ") + line + Environment.NewLine);
        }

        async Task SendManual()
        {
            var cmd = _cmdBox.Text.Trim();
            if (cmd.Length == 0) return;
            _cmdBox.Clear();
            var r = await _ctx.Try(cmd, cmd.StartsWith("WIFI SCAN", StringComparison.OrdinalIgnoreCase) ? 12000 : 3000);
            if (r != null && cmd.StartsWith("MODE", StringComparison.OrdinalIgnoreCase)) { _ctx.Mode = r.Str("mode"); UpdateConn(); }
        }
    }
}
