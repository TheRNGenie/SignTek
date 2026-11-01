using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CasinoSignTool
{
    public class NetworkTab : UserControl
    {
        readonly Ctx _ctx;
        ComboBox _ssid;
        TextBox _pass, _host, _apPass, _btName, _mqttHost, _mqttPort, _mqttUser, _mqttPass, _mqttPrefix, _otaPass;
        CheckBox _showPass, _apOn, _btOn, _mqttOn, _mqttHa, _autoConn;
        Label _wifiState, _btState;
        FlowLayoutPanel _btBox;

        public NetworkTab(Ctx ctx)
        {
            _ctx = ctx;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            Controls.Add(root);

            var w = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            var b = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(10, 0, 0, 0) };
            root.Controls.Add(w, 0, 0); root.Controls.Add(b, 1, 0);
            Label H(string t) => new Label { Text = t, AutoSize = true, Font = new Font(Font.FontFamily, 10f, FontStyle.Bold), Margin = new Padding(0, 10, 0, 4) };
            Label P(string t, int mw = 500) => new Label { Text = t, AutoSize = true, MaximumSize = new Size(mw, 0), ForeColor = Color.DimGray, Margin = new Padding(0, 2, 0, 4) };
            FlowLayoutPanel Row(params Control[] cs) { var r = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) }; r.Controls.AddRange(cs); return r; }
            Label L(string t, int width = 110) => new Label { Text = t, Width = width, Margin = new Padding(0, 6, 0, 0) };
            Button Btn(string t, Func<Task> a, bool bold = false) { var x = new Button { Text = t, AutoSize = true }; if (bold) x.Font = new Font(Font, FontStyle.Bold); x.Click += async (s, e) => await a(); return x; }

            // ---- WiFi ----
            w.Controls.Add(H("WiFi"));
            _wifiState = new Label { AutoSize = true, MaximumSize = new Size(520, 0), Font = new Font("Consolas", 9f) };
            w.Controls.Add(_wifiState);
            w.Controls.Add(Row(Btn("Refresh status", RefreshAll)));

            w.Controls.Add(H("Join a network"));
            _ssid = new ComboBox { Width = 260 };
            w.Controls.Add(Row(L("Network name:"), _ssid, Btn("Scan", Scan)));
            _pass = new TextBox { Width = 260, UseSystemPasswordChar = true };
            _showPass = new CheckBox { Text = "show", AutoSize = true, Margin = new Padding(6, 4, 0, 0) };
            _showPass.CheckedChanged += (s, e) => _pass.UseSystemPasswordChar = !_showPass.Checked;
            w.Controls.Add(Row(L("Password:"), _pass, _showPass));
            w.Controls.Add(Row(L(""), Btn("Save and connect", Join, true), Btn("Forget network", async () => { await _ctx.Try("WIFI FORGET"); await RefreshAll(); })));
            w.Controls.Add(P("If you're connected over WiFi, the link drops while the board switches networks. Reconnect using the IP shown on the board's screen. The ESP32 only does 2.4 GHz WiFi."));

            w.Controls.Add(H("Name and hotspot"));
            _host = new TextBox { Width = 180 };
            w.Controls.Add(Row(L("Hostname:"), _host, new Label { Text = ".local", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }));
            _apOn = new CheckBox { Text = "Start its own hotspot when it can't join WiFi (so you can always reach it)", AutoSize = true, MaximumSize = new Size(520, 0) };
            w.Controls.Add(_apOn);
            _apPass = new TextBox { Width = 180, UseSystemPasswordChar = true };
            w.Controls.Add(Row(L("Hotspot password:"), _apPass, new Label { Text = "(8+ characters; blank = keep current)", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(4, 6, 0, 0) }));
            w.Controls.Add(Row(L(""), Btn("Apply", ApplyNameAp, true)));
            w.Controls.Add(P("The hotspot is called CasinoSign-XXXX and the default password is casinosign. Change it. On the hotspot, the board is at 192.168.4.1."));

            // ---- Home Assistant / MQTT ----
            b.Controls.Add(H("Home Assistant / MQTT (optional)"));
            b.Controls.Add(P("Lets Home Assistant, Node-RED or any MQTT app turn the sign on/off and change its text. Skip this if you don't use them.", 400));
            _mqttOn = new CheckBox { Text = "Connect to an MQTT broker", AutoSize = true };
            b.Controls.Add(_mqttOn);
            _mqttHost = new TextBox { Width = 170 }; _mqttPort = new TextBox { Width = 55, Text = "1883" };
            b.Controls.Add(Row(L("Broker address:", 100), _mqttHost, new Label { Text = "port", AutoSize = true, Margin = new Padding(4, 6, 0, 0) }, _mqttPort));
            _mqttUser = new TextBox { Width = 170 };
            b.Controls.Add(Row(L("Username:", 100), _mqttUser));
            _mqttPass = new TextBox { Width = 170, UseSystemPasswordChar = true };
            b.Controls.Add(Row(L("Password:", 100), _mqttPass, new Label { Text = "(blank = keep)", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(4, 6, 0, 0) }));
            _mqttPrefix = new TextBox { Width = 170 };
            b.Controls.Add(Row(L("Topic prefix:", 100), _mqttPrefix));
            _mqttHa = new CheckBox { Text = "Show up in Home Assistant automatically", AutoSize = true };
            b.Controls.Add(_mqttHa);
            b.Controls.Add(Row(Btn("Save MQTT settings", SaveMqtt, true)));
            b.Controls.Add(P("In Home Assistant: Settings > Devices && services > MQTT. The sign appears as \"Casino Sign\" with a power switch, a text box and a Next button. " +
                "Topics if you set things up by hand: <prefix>/power/set (ON/OFF), <prefix>/text/set (any text), <prefix>/cmd (any console command, answer on <prefix>/reply).", 400));

            // ---- Wireless update password ----
            b.Controls.Add(H("Wireless update password"));
            _otaPass = new TextBox { Width = 170, UseSystemPasswordChar = true };
            b.Controls.Add(Row(L("New password:", 100), _otaPass, Btn("Set", SaveOta, true)));
            b.Controls.Add(P("Needed to install firmware over WiFi (tab 0). The default is casinosign. If you change it, this app remembers the new one.", 400));

            // ---- Bluetooth (only on firmware built with it) ----
            _btBox = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0), Visible = false };
            _btBox.Controls.Add(H("Bluetooth"));
            _btState = new Label { AutoSize = true, MaximumSize = new Size(400, 0), Font = new Font("Consolas", 9f) };
            _btBox.Controls.Add(_btState);
            _btOn = new CheckBox { Text = "Bluetooth on", AutoSize = true };
            _btBox.Controls.Add(_btOn);
            _btName = new TextBox { Width = 200 };
            _btBox.Controls.Add(Row(L("Name:", 60), _btName));
            _btBox.Controls.Add(Row(Btn("Apply", ApplyBt, true)));
            b.Controls.Add(_btBox);

            // ---- App + board ----
            b.Controls.Add(H("This app"));
            _autoConn = new CheckBox { Text = "Find and connect to the sign automatically when the app opens", AutoSize = true, MaximumSize = new Size(400, 0), Checked = _ctx.Settings.AutoConnect };
            _autoConn.CheckedChanged += (s, e) => { _ctx.Settings.AutoConnect = _autoConn.Checked; _ctx.Settings.Save(); };
            b.Controls.Add(_autoConn);
            b.Controls.Add(H("Board"));
            b.Controls.Add(Row(Btn("Reboot board", async () =>
            {
                if (MessageBox.Show("Restart the ESP32 now? Name and hotspot changes take effect after a restart.", "Reboot", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                try { await _ctx.Board.Send("REBOOT", 1500, false); } catch { }
                _ctx.Board.Disconnect("board is rebooting, reconnect in a few seconds");
            })));

            _ctx.StateChanged += async () => { if (_ctx.Board.Connected) await RefreshAll(); else { _wifiState.Text = "Not connected."; _btState.Text = ""; } };
            _wifiState.Text = "Not connected.";
        }

        async Task RefreshAll()
        {
            var r = await _ctx.Try("WIFI STATUS");
            if (r != null)
            {
                _wifiState.Text =
                    $"Saved network: {(r.Str("ssid").Length > 0 ? r.Str("ssid") : "(none)")}\n" +
                    $"Connected:     {(r.Bool("connected") ? $"yes, IP {r.Str("ip")}, signal {r.Int("rssi")} dBm" : "no")}\n" +
                    $"Hostname:      {r.Str("host")}.local\n" +
                    $"Hotspot:       {(r.Bool("ap_active") ? $"ON ({r.Str("ap_ssid")}, {r.Str("ap_ip")})" : "off")}{(r.Bool("ap_fallback") ? ", starts if WiFi fails" : "")}\n" +
                    $"MQTT:          {(r.Bool("mqtt_on") ? (r.Bool("mqtt_connected") ? "connected" : "on, not connected (check broker address)") : "off")}\n" +
                    $"MAC:           {r.Str("mac")}";
                if (_ssid.Text.Length == 0) _ssid.Text = r.Str("ssid");
                _host.Text = r.Str("host");
                _apOn.Checked = r.Bool("ap_fallback");
            }
            var c = await _ctx.Try("CONF GET");
            if (c != null)
            {
                _mqttOn.Checked = c.Bool("mqtt_on"); _mqttHost.Text = c.Str("mqtt_host"); _mqttPort.Text = c.Str("mqtt_port");
                _mqttUser.Text = c.Str("mqtt_user"); _mqttPrefix.Text = c.Str("mqtt_prefix"); _mqttHa.Checked = c.Bool("mqtt_ha");
            }
            var bt = await _ctx.Try("BT STATUS");
            _btBox.Visible = bt != null && bt.Bool("supported", true);
            if (bt != null)
            {
                _btState.Text = $"State:  {(bt.Bool("active") ? "on" : "off")}{(bt.Bool("client") ? ", a device is connected" : "")}\nName:   {bt.Str("name")}";
                _btOn.Checked = bt.Bool("on");
                _btName.Text = bt.Str("name");
            }
        }

        async Task SaveMqtt()
        {
            if (_mqttOn.Checked && _mqttHost.Text.Trim().Length == 0) { _ctx.Status("Enter the broker address (for Home Assistant, its IP address)."); return; }
            if (!int.TryParse(_mqttPort.Text.Trim(), out int port) || port < 1 || port > 65535) { _ctx.Status("Port must be a number (usually 1883)."); return; }
            var cmds = new System.Collections.Generic.List<string> {
                "CONF SET mqtt_host " + _mqttHost.Text.Trim(), "CONF SET mqtt_port " + port, "CONF SET mqtt_user " + _mqttUser.Text.Trim(),
                "CONF SET mqtt_prefix " + (_mqttPrefix.Text.Trim().Length > 0 ? _mqttPrefix.Text.Trim() : "casinosign"),
                "CONF SET mqtt_ha " + (_mqttHa.Checked ? 1 : 0) };
            if (_mqttPass.Text.Length > 0) cmds.Add("CONF SET mqtt_pass " + _mqttPass.Text);
            cmds.Add("CONF SET mqtt_on " + (_mqttOn.Checked ? 1 : 0));
            foreach (var cmd in cmds) if (await _ctx.Try(cmd) == null) return;
            _mqttPass.Text = "";
            _ctx.Status("MQTT settings saved. The sign connects within a few seconds (see the MQTT line on the left).");
            await Task.Delay(4000);
            if (_ctx.Board.Connected) await RefreshAll();
        }

        async Task SaveOta()
        {
            if (_otaPass.Text.Length < 4) { _ctx.Status("Use at least 4 characters for the update password."); return; }
            if (await _ctx.Try("CONF SET ota_pass " + _otaPass.Text) == null) return;
            _ctx.Settings.OtaPass = _otaPass.Text; _ctx.Settings.Save();
            _otaPass.Text = "";
            _ctx.Status("Update password changed. The app will use the new one for WiFi updates.");
        }

        async Task Scan()
        {
            _ctx.Status("Scanning for WiFi networks (takes a few seconds)…");
            var r = await _ctx.Try("WIFI SCAN", 15000);
            if (r == null) return;
            var nets = r.List("networks").OrderByDescending(n => n.Int("rssi")).Select(n => n.Str("ssid")).Where(s => s.Length > 0).Distinct().ToArray();
            _ssid.Items.Clear(); _ssid.Items.AddRange(nets);
            if (nets.Length > 0) { _ssid.DroppedDown = true; }
            _ctx.Status($"Found {nets.Length} networks.");
        }

        async Task Join()
        {
            if (_ssid.Text.Trim().Length == 0) { _ctx.Status("Type or pick a network name first."); return; }
            if (await _ctx.Try("WIFI SSID " + _ssid.Text.Trim()) == null) return;
            if (await _ctx.Try("WIFI PASS " + _pass.Text) == null) return;
            bool wifiLink = _ctx.Board.OverWifi;
            try { await _ctx.Board.Send("WIFI CONNECT", 3000, false); } catch { }
            if (wifiLink) { _ctx.Status("Board is switching networks. Reconnect using its new IP (on its screen)."); return; }
            _ctx.Status("Connecting… watch for the IP in the status bar.");
            await Task.Delay(6000);
            if (_ctx.Board.Connected) await RefreshAll();
        }

        async Task ApplyNameAp()
        {
            if (_host.Text.Trim().Length > 0 && await _ctx.Try("WIFI HOST " + _host.Text.Trim()) == null) return;
            if (await _ctx.Try("WIFI AP " + (_apOn.Checked ? "on" : "off")) == null) return;
            if (_apPass.Text.Length > 0)
            {
                if (_apPass.Text.Length < 8) { _ctx.Status("Hotspot password needs at least 8 characters."); return; }
                if (await _ctx.Try("WIFI APPASS " + _apPass.Text) == null) return;
            }
            _ctx.Status("Saved. Hostname and hotspot password changes take effect after a reboot.");
            await RefreshAll();
        }

        async Task ApplyBt()
        {
            if (_btName.Text.Trim().Length > 0 && await _ctx.Try("BT NAME " + _btName.Text.Trim()) == null) return;
            bool overBt = !_ctx.Board.OverWifi && !_btOn.Checked;
            if (overBt && MessageBox.Show("If you're connected over Bluetooth, turning it off drops this connection. Continue?", "Bluetooth", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            if (await _ctx.Try("BT " + (_btOn.Checked ? "ON" : "OFF")) == null) return;
            _ctx.Status("Bluetooth settings saved. A new name shows up after a reboot.");
            await RefreshAll();
        }
    }
}
