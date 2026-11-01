using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CasinoSignTool
{
    /// <summary>A COM port with the friendly name Windows gives it.</summary>
    public class PortInfo
    {
        public string Port, Name = "", Vid = "";
        public bool LooksLikeEsp32 => Vid == "1A86" || Vid == "10C4" || Vid == "0403" || Vid == "303A";
        public override string ToString() => Name.Length > 0 ? $"{Port}  {Name.Replace($"({Port})", "").Trim()}{(LooksLikeEsp32 ? "  ← likely the ESP32" : "")}" : Port;


        // Separate method so a missing System.Management only fails this call, not the whole tab.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void AddWmiNames(List<PortInfo> ports, List<string> driverProblems)
        {
            using var q = new ManagementObjectSearcher("SELECT Name, PNPDeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity");
            foreach (ManagementObject o in q.Get())
            {
                string name = o["Name"] as string ?? "", id = o["PNPDeviceID"] as string ?? "";
                uint errCode = o["ConfigManagerErrorCode"] is uint u ? u : 0;
                var vid = Regex.Match(id, @"VID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
                var com = Regex.Match(name, @"\((COM\d+)\)");
                if (com.Success)
                {
                    var p = ports.FirstOrDefault(x => x.Port == com.Groups[1].Value);
                    if (p == null) ports.Add(p = new PortInfo { Port = com.Groups[1].Value });
                    p.Name = name; p.Vid = vid.Success ? vid.Groups[1].Value.ToUpperInvariant() : "";
                }
                // A USB-serial chip plugged in but without a working driver (yellow "!" in Device Manager).
                bool usbSerialChip = vid.Success && new[] { "1A86", "10C4", "0403" }.Contains(vid.Groups[1].Value.ToUpperInvariant());
                if (usbSerialChip && errCode != 0) driverProblems.Add(name.Length > 0 ? name : id);
            }
        }

        public static List<PortInfo> Scan(out List<string> driverProblems)
        {
            driverProblems = new List<string>();
            var ports = SerialPort.GetPortNames().Distinct().Select(p => new PortInfo { Port = p }).ToList();
            try { AddWmiNames(ports, driverProblems); }
            catch { /* WMI not available: plain port names are still fine */ }
            return ports.OrderByDescending(p => p.LooksLikeEsp32).ThenBy(p => p.Port.Length).ThenBy(p => p.Port).ToList();
        }
    }

    /// <summary>Installs the sign firmware on the ESP32 with Espressif's esptool (bundled), no Arduino IDE needed.</summary>
    public class FlashTab : UserControl
    {
        readonly Ctx _ctx;
        ComboBox _ports;
        ComboBox _baud;
        CheckBox _eraseAll;
        Button _flash, _connect;
        ProgressBar _bar;
        Label _state, _drivers, _fwInfo;
        TextBox _out, _otaPass;
        Button _wifiBtn;
        Process _proc;

        static string AppDir => AppDomain.CurrentDomain.BaseDirectory;
        static string FwDir => Path.Combine(AppDir, "firmware");
        static string EsptoolPath => Environment.GetEnvironmentVariable("CST_ESPTOOL") ?? Path.Combine(AppDir, "esptool", "esptool.exe");

        // Same offsets the Arduino IDE uses for "ESP32 Dev Module". The NVS area (0x9000, saved settings) is left alone.
        static readonly (string addr, string file)[] Images =
        {
            ("0x1000", "bootloader.bin"), ("0x8000", "partitions.bin"), ("0xe000", "boot_app0.bin"), ("0x10000", "casino_sign_driver.bin"),
        };
        // The screen test replaces only the app part; settings (NVS) are left alone.
        static readonly (string addr, string file)[] TestImages =
        {
            ("0x1000", "bootloader.bin"), ("0x8000", "partitions.bin"), ("0xe000", "boot_app0.bin"), ("0x10000", "screen_test.bin"),
        };
        bool _testRun;
        Button _testBtn, _watchBtn;

        public FlashTab(Ctx ctx)
        {
            _ctx = ctx;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 520));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            root.Controls.Add(left, 0, 0);
            Label H(string t) => new Label { Text = t, AutoSize = true, Font = new Font(Font.FontFamily, 10f, FontStyle.Bold), Margin = new Padding(0, 10, 0, 4) };
            Label P(string t) => new Label { Text = t, AutoSize = true, MaximumSize = new Size(490, 0), Margin = new Padding(0, 2, 0, 4) };

            left.Controls.Add(H("Install the sign firmware on the ESP32"));
            left.Controls.Add(P(
                "No Arduino IDE needed, and a brand-new board needs no preparation: every ESP32 has a built-in loader.\n\n" +
                "1. Plug the Freenove board into this PC with a USB cable. It must be a data cable; some charge-only cables don't work.\n" +
                "2. Pick its port below (press Refresh after plugging in).\n" +
                "3. Press Install firmware and wait about a minute.\n" +
                "4. Press Connect. The other tabs then work."));

            var prow = new FlowLayoutPanel { AutoSize = true };
            _ports = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
            var refresh = new Button { Text = "Refresh", AutoSize = true };
            refresh.Click += (s, e) => RefreshPorts();
            prow.Controls.Add(_ports); prow.Controls.Add(refresh);
            left.Controls.Add(prow);
            _drivers = new Label { AutoSize = true, MaximumSize = new Size(490, 0), ForeColor = Color.DarkRed };
            left.Controls.Add(_drivers);

            var orow = new FlowLayoutPanel { AutoSize = true };
            orow.Controls.Add(new Label { Text = "Speed:", AutoSize = true, Margin = new Padding(0, 7, 2, 0) });
            _baud = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
            _baud.Items.AddRange(new object[] { "460800 (normal)", "921600 (fast)", "115200 (slow, most reliable)" });
            _baud.SelectedIndex = 0;
            orow.Controls.Add(_baud);
            left.Controls.Add(orow);
            _eraseAll = new CheckBox { Text = "Erase the whole flash first (wipes saved pin map, WiFi and text; use for a clean start)", AutoSize = true, MaximumSize = new Size(490, 0) };
            left.Controls.Add(_eraseAll);

            var brow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            _flash = new Button { Text = "Install firmware", AutoSize = true, Font = new Font(Font.FontFamily, 11f, FontStyle.Bold), Padding = new Padding(10, 4, 10, 4) };
            _flash.Click += async (s, e) => await Flash(false);
            _connect = new Button { Text = "Connect", AutoSize = true, Font = new Font(Font.FontFamily, 11f), Padding = new Padding(10, 4, 10, 4), Enabled = false };
            _connect.Click += async (s, e) => { if (_ports.SelectedItem is PortInfo p && _ctx.ConnectPort != null) await _ctx.ConnectPort(p.Port); };
            brow.Controls.Add(_flash); brow.Controls.Add(_connect);
            left.Controls.Add(brow);
            _bar = new ProgressBar { Width = 490, Height = 22, Maximum = 1000 };
            left.Controls.Add(_bar);
            _state = new Label { AutoSize = true, MaximumSize = new Size(490, 0), Font = new Font(Font, FontStyle.Bold) };
            left.Controls.Add(_state);

            left.Controls.Add(H("Built-in screen stays dark? Run the screen test"));
            left.Controls.Add(P(
                "The screen test is a tiny separate firmware that does nothing except light the board's own screen. It tells us whether the screen itself works.\n\n" +
                "1. Pick the port above and press Install screen test (about 20 seconds).\n" +
                "2. Press Watch board output. The board restarts and, for 30 seconds, everything it prints appears in the black box on the right.\n" +
                "3. Watch the screen. In order you should see: the backlight blink 3 times (a grey glow), the screen fill RED from top to bottom (a few seconds), then GREEN, BLUE, and finally WHITE with the words SCREEN TEST WORKS.\n" +
                "4. Note which of those you saw (for example \"blinks and red, but no green\"). Copy the black box text too (click in it, Ctrl+A, Ctrl+C).\n" +
                "5. When finished, press Install firmware above to put the sign firmware back. Your saved settings are kept.\n\n" +
                "What it means: nothing at all, not even the blink = the screen's power or backlight (check the USB cable and port, try another). Blink but no red = the screen's connection or chip. Red but no green = a software problem in the graphics part, which I can fix. Everything shows = the screen is fine and the problem is in the sign firmware's start-up."));
            var trow = new FlowLayoutPanel { AutoSize = true };
            _testBtn = new Button { Text = "Install screen test", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            _testBtn.Click += async (s, e) => await Flash(true);
            _watchBtn = new Button { Text = "Watch board output (30 s)", AutoSize = true };
            _watchBtn.Click += async (s, e) => await WatchOutput();
            trow.Controls.Add(_testBtn); trow.Controls.Add(_watchBtn);
            left.Controls.Add(trow);
            left.Controls.Add(P("Watch board output works with either firmware: it restarts the board and shows its start-up messages, which helps whenever something doesn't work."));

            left.Controls.Add(H("Update over WiFi (after the first install)"));
            left.Controls.Add(P("Once the sign is on your WiFi and connected in the top bar, new firmware goes over the air: no USB, the board can stay inside the sign."));
            var wrow = new FlowLayoutPanel { AutoSize = true };
            wrow.Controls.Add(new Label { Text = "Update password:", AutoSize = true, Margin = new Padding(0, 7, 2, 0) });
            _otaPass = new TextBox { Width = 140, UseSystemPasswordChar = true, Text = _ctx.Settings?.OtaPass ?? "casinosign" };
            wrow.Controls.Add(_otaPass);
            _wifiBtn = new Button { Text = "Update over WiFi", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            _wifiBtn.Click += async (s, e) => await UpdateOverWifi();
            wrow.Controls.Add(_wifiBtn);
            left.Controls.Add(wrow);
            left.Controls.Add(P("The password is the board's ota_pass setting (default casinosign). Change it in the WiFi & settings tab."));

            left.Controls.Add(H("If it can't connect"));
            left.Controls.Add(P(
                "Put the board in download mode by hand: hold the BOOT button, press and release RST (sometimes labelled EN), then let go of BOOT. Press Install firmware again.\n\n" +
                "No port shows up at all? The board's USB chip needs a driver:\n" +
                "  • CH340 / CH343 (most Freenove boards): install the WCH driver.\n" +
                "  • CP2102 / CP210x: install the Silicon Labs driver.\n" +
                "Then unplug and replug the board, and press Refresh."));
            var links = new FlowLayoutPanel { AutoSize = true };
            LinkLabel Link(string t, string url) { var l = new LinkLabel { Text = t, AutoSize = true, Margin = new Padding(0, 0, 14, 0) }; l.LinkClicked += (s, e) => { try { Process.Start(url); } catch { } }; links.Controls.Add(l); return l; }
            Link("CH340 driver (wch-ic.com)", "https://www.wch-ic.com/downloads/CH341SER_EXE.html");
            Link("CP210x driver (silabs.com)", "https://www.silabs.com/developers/usb-to-uart-bridge-vcp-drivers");
            Link("Device Manager", "devmgmt.msc");
            left.Controls.Add(links);
            _fwInfo = new Label { AutoSize = true, MaximumSize = new Size(490, 0), ForeColor = Color.DimGray, Margin = new Padding(0, 12, 0, 0) };
            left.Controls.Add(_fwInfo);

            _out = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 8.5f), BackColor = Color.FromArgb(20, 20, 24), ForeColor = Color.Gainsboro };
            root.Controls.Add(_out, 1, 0);

            ShowFirmwareInfo();
            RefreshPorts();
            VisibleChanged += (s, e) => { if (Visible && _proc == null) RefreshPorts(); };
        }

        void ShowFirmwareInfo()
        {
            var missing = Images.Where(i => !File.Exists(Path.Combine(FwDir, i.file))).Select(i => i.file).ToList();
            if (!File.Exists(EsptoolPath)) missing.Add("esptool\\esptool.exe");
            if (missing.Count > 0) { _fwInfo.ForeColor = Color.DarkRed; _fwInfo.Text = "Missing next to the app: " + string.Join(", ", missing) + ". Keep the firmware and esptool folders from the zip beside CasinoSignTool.exe."; return; }
            string ver = "";
            try { ver = File.ReadAllText(Path.Combine(FwDir, "version.txt")).Trim(); } catch { }
            var app = new FileInfo(Path.Combine(FwDir, "casino_sign_driver.bin"));
            _fwInfo.Text = $"Firmware: {(ver.Length > 0 ? ver : "casino_sign_driver")}, {app.Length / 1024} KB, built {app.LastWriteTime:yyyy-MM-dd}. Flashed with Espressif esptool (GPLv2, source: github.com/espressif/esptool).";
        }

        void RefreshPorts()
        {
            var prev = (_ports.SelectedItem as PortInfo)?.Port;
            var list = PortInfo.Scan(out var problems);
            _ports.Items.Clear();
            foreach (var p in list) _ports.Items.Add(p);
            var pick = list.FirstOrDefault(p => p.Port == prev) ?? list.FirstOrDefault();
            if (pick != null) _ports.SelectedItem = pick;
            _drivers.Text = problems.Count > 0
                ? "A USB-serial chip is plugged in but its driver isn't working: " + string.Join(", ", problems) + ". Install the driver (links below)."
                : list.Count == 0 ? "No COM ports found. Plug the board in, check the cable is a data cable, and install the driver if needed (links below)." : "";
        }

        int Baud => _baud.SelectedIndex == 1 ? 921600 : _baud.SelectedIndex == 2 ? 115200 : 460800;

        async Task Flash(bool screenTest)
        {
            if (_proc != null) return;
            _testRun = screenTest;
            var Images = screenTest ? TestImages : FlashTab.Images;
            if (!(_ports.SelectedItem is PortInfo port)) { _state.Text = "Pick the board's COM port first."; return; }
            ShowFirmwareInfo();
            var missing = Images.Where(i => !File.Exists(Path.Combine(FwDir, i.file))).ToList();
            if (missing.Count > 0 || !File.Exists(EsptoolPath)) { _state.Text = "Firmware files are missing (see the note below)."; return; }

            // The port can only be open in one program at a time.
            if (_ctx.Board.Connected && !_ctx.Board.OverWifi) _ctx.Board.Disconnect("flashing firmware");

            var sizes = Images.Select(i => new FileInfo(Path.Combine(FwDir, i.file)).Length).ToArray();
            long total = sizes.Sum();
            var args = new StringBuilder($"--chip esp32 --port {port.Port} --baud {Baud} --before default_reset --after hard_reset write_flash");
            if (_eraseAll.Checked) args.Append(" --erase-all");
            args.Append(" -z --flash_mode keep --flash_freq keep --flash_size keep");
            foreach (var (addr, file) in Images) args.Append($" {addr} \"{Path.Combine(FwDir, file)}\"");

            _out.Clear();
            Append($"> esptool {args}\r\n");
            _bar.Style = ProgressBarStyle.Marquee; _bar.Value = 0;
            _state.ForeColor = SystemColors.ControlText;
            _state.Text = "Connecting to the ESP32…";
            _flash.Enabled = false; _connect.Enabled = false; _testBtn.Enabled = false;

            int fileIdx = 0, lastPct = -1;
            var all = new StringBuilder();
            var sw = Stopwatch.StartNew();
            var psi = new ProcessStartInfo(EsptoolPath, args.ToString())
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = AppDir,
            };
            psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
            var done = new TaskCompletionSource<int>();
            try
            {
                _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                void OnLine(string line)
                {
                    if (line == null) return;
                    BeginInvoke((Action)(() =>
                    {
                        all.AppendLine(line);
                        Append(line + "\r\n");
                        if (line.StartsWith("Connecting")) _state.Text = "Connecting to the ESP32…";
                        else if (line.StartsWith("Chip is")) _state.Text = line.Trim();
                        else if (line.StartsWith("Erasing flash (this may take")) _state.Text = "Erasing the whole flash (up to 30 s)…";
                        var m = Regex.Match(line, @"Writing at 0x[0-9a-f]+\.\.\. \((\d+) %\)", RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            int pct = int.Parse(m.Groups[1].Value);
                            lastPct = pct;
                            long before = sizes.Take(fileIdx).Sum();
                            double overall = (before + sizes[fileIdx] * pct / 100.0) / total;
                            _bar.Style = ProgressBarStyle.Continuous;
                            _bar.Value = Math.Max(0, Math.Min(1000, (int)(overall * 1000)));
                            _state.Text = $"Writing firmware… {overall * 100:0}%";
                        }
                        if (line.StartsWith("Wrote ")) { if (fileIdx < sizes.Length - 1) fileIdx++; lastPct = -1; }
                    }));
                }
                _proc.OutputDataReceived += (s, e) => OnLine(e.Data);
                _proc.ErrorDataReceived += (s, e) => OnLine(e.Data);
                _proc.Exited += (s, e) => done.TrySetResult(_proc.ExitCode);
                _proc.Start();
                _proc.BeginOutputReadLine();
                _proc.BeginErrorReadLine();
                // If esptool's output is buffered, show time so it doesn't look frozen.
                var tick = new Timer { Interval = 1000 };
                tick.Tick += (s, e) => { if (_bar.Style == ProgressBarStyle.Marquee && sw.Elapsed.TotalSeconds > 4 && _state.Text.StartsWith("Connecting")) _state.Text = $"Working… {sw.Elapsed.TotalSeconds:0} s"; };
                tick.Start();
                int code = await done.Task;
                tick.Stop();
                await Task.Delay(200);   // let the last output lines arrive
                Finish(code, all.ToString());
            }
            catch (Exception ex)
            {
                _state.ForeColor = Color.DarkRed;
                _state.Text = "Couldn't start esptool: " + ex.Message;
                _bar.Style = ProgressBarStyle.Continuous; _bar.Value = 0;
            }
            finally
            {
                _proc?.Dispose(); _proc = null;
                _flash.Enabled = true; _testBtn.Enabled = true;
            }
        }

        async Task WatchOutput()
        {
            if (_proc != null) return;
            if (!(_ports.SelectedItem is PortInfo port)) { _state.Text = "Pick the board's COM port first."; return; }
            if (_ctx.Board.Connected && !_ctx.Board.OverWifi) _ctx.Board.Disconnect("watching board output");
            _watchBtn.Enabled = false; _flash.Enabled = false; _testBtn.Enabled = false;
            _out.Clear();
            Append($"--- Board output on {port.Port} (115200 baud), restarting the board ---\r\n");
            _state.ForeColor = SystemColors.ControlText;
            _state.Text = "Watching the board for 30 seconds…";
            var sp = new System.IO.Ports.SerialPort(port.Port, 115200) { ReadTimeout = 200, Encoding = Encoding.ASCII };
            try
            {
                Exception openErr = null;
                for (int i = 0; i < 8; i++)
                {
                    try { await Task.Run(() => sp.Open()); openErr = null; break; }
                    catch (Exception ex) { openErr = ex; await Task.Delay(500); }
                }
                if (openErr != null) throw openErr;
                // Same reset the Arduino IDE does: EN low through RTS, IO0 left high.
                sp.DtrEnable = false; sp.RtsEnable = true; await Task.Delay(120); sp.RtsEnable = false;
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < 30)
                {
                    string chunk = await Task.Run(() => { try { return sp.ReadExisting(); } catch { return ""; } });
                    if (chunk.Length > 0)
                    {
                        var clean = new string(chunk.Where(c => c == '\n' || c == '\r' || c == '\t' || (c >= ' ' && c < 127)).ToArray());
                        Append(clean.Replace("\r\n", "\n").Replace("\n", "\r\n"));
                    }
                    else await Task.Delay(100);
                    _state.Text = $"Watching the board… {30 - sw.Elapsed.TotalSeconds:0} s left";
                }
                Append("\r\n--- End. To copy: click in this box, Ctrl+A, Ctrl+C ---\r\n");
                _state.Text = "Finished watching. The text is in the black box on the right.";
            }
            catch (Exception ex)
            {
                _state.ForeColor = Color.DarkRed;
                _state.Text = $"Couldn't open {port.Port}: {ex.Message} Close any other program using it (Arduino IDE serial monitor), unplug and replug the board, then try again.";
            }
            finally
            {
                try { if (sp.IsOpen) sp.Close(); } catch { }
                sp.Dispose();
                _watchBtn.Enabled = true; _flash.Enabled = true; _testBtn.Enabled = true;
            }
        }

        void Finish(int code, string output)
        {
            _bar.Style = ProgressBarStyle.Continuous;
            if (code == 0)
            {
                _bar.Value = 1000;
                _state.ForeColor = Color.DarkGreen;
                if (_testRun)
                {
                    _state.Text = "Screen test installed and running. Press Watch board output, then watch the screen (see the steps above). Press Install firmware afterwards to put the sign firmware back.";
                    _ctx.Status("Screen test installed.");
                    return;
                }
                _state.Text = "Done! The board restarted with the sign firmware. Its screen shows DIAGNOSTICS MODE. Press Connect.";
                _connect.Enabled = true;
                _ctx.Status("Firmware installed.");
                return;
            }
            _bar.Value = 0;
            _state.ForeColor = Color.DarkRed;
            _state.Text = Explain(output) + $"  (esptool exit code {code})";
        }

        /// <summary>Turn esptool's error into something actionable.</summary>
        static string Explain(string o)
        {
            if (Regex.IsMatch(o, "Failed to connect|No serial data received|Wrong boot mode|Timed out waiting for packet header", RegexOptions.IgnoreCase))
                return "Couldn't get the ESP32 into download mode. Hold BOOT, press and release RST/EN, release BOOT, then press Install firmware again.";
            if (Regex.IsMatch(o, "could not open port|PermissionError|Access is denied|being used by another", RegexOptions.IgnoreCase))
                return "The COM port is busy. Close anything else using it (Arduino Serial Monitor, PuTTY, another copy of this app) and try again.";
            if (Regex.IsMatch(o, "FileNotFoundError|The system cannot find the file|does not exist", RegexOptions.IgnoreCase))
                return "That COM port isn't there any more. Replug the board, press Refresh and pick the port again.";
            var chip = Regex.Match(o, @"This chip is (ESP32-\w+)", RegexOptions.IgnoreCase);
            if (chip.Success || Regex.IsMatch(o, "Wrong --chip argument", RegexOptions.IgnoreCase))
                return $"This board has a different chip ({(chip.Success ? chip.Groups[1].Value : "not a plain ESP32")}). The sign firmware is built for the original ESP32; it needs rebuilding for this chip.";
            if (Regex.IsMatch(o, "Detected overwhelming|corrupt|checksum|MD5 of file does not match", RegexOptions.IgnoreCase))
                return "The data got garbled on the way. Choose the 115200 (slow) speed, try a shorter or better USB cable, and try again.";
            return "Flashing failed. The log on the right has the details. Try the BOOT button trick or the slow speed.";
        }

        async Task UpdateOverWifi()
        {
            if (!_ctx.Board.Connected || !_ctx.Board.OverWifi) { _state.ForeColor = Color.DarkRed; _state.Text = "Connect to the sign over WiFi first (top bar: Find sign)."; return; }
            var bin = Path.Combine(FwDir, "casino_sign_driver.bin");
            if (!File.Exists(bin)) { _state.Text = "firmware\\casino_sign_driver.bin is missing next to the app."; return; }
            string host = _ctx.Board.Where;
            if (_ctx.Settings != null) { _ctx.Settings.OtaPass = _otaPass.Text; _ctx.Settings.Save(); }
            var data = File.ReadAllBytes(bin);
            _out.Clear(); Append($"> POST http://{host}/update  ({data.Length / 1024} KB)\r\n");
            _flash.Enabled = _wifiBtn.Enabled = false;
            _bar.Style = ProgressBarStyle.Continuous; _bar.Value = 0;
            _state.ForeColor = SystemColors.ControlText; _state.Text = "Sending firmware over WiFi…";
            try
            {
                string pass = _otaPass.Text;
                var progress = new Progress<double>(p => { _bar.Value = (int)(p * 1000); _state.Text = $"Sending firmware over WiFi… {p * 100:0}%"; });
                string reply = await Task.Run(() => PostFirmware(host, pass, data, progress));
                Append(reply + "\r\n");
                if (reply.Contains("\"ok\":true"))
                {
                    _bar.Value = 1000; _state.ForeColor = Color.DarkGreen;
                    _state.Text = "Updated. The board is restarting with the new firmware; reconnect with Find sign in about 10 seconds.";
                    _ctx.Board.Disconnect("board is restarting after the update");
                }
                else { _state.ForeColor = Color.DarkRed; _state.Text = "The board rejected the update. Check the update password."; }
            }
            catch (System.Net.WebException ex) when (ex.Response is System.Net.HttpWebResponse hr)
            {
                _state.ForeColor = Color.DarkRed;
                _state.Text = hr.StatusCode == System.Net.HttpStatusCode.InternalServerError ? "The board rejected the update. Check the update password." : "Update failed: " + ex.Message;
            }
            catch (Exception ex)
            {
                _state.ForeColor = Color.DarkRed;
                _state.Text = "Update failed: " + ex.Message + ". The board keeps its old firmware if an update is cut short.";
            }
            finally { _flash.Enabled = _wifiBtn.Enabled = true; }
        }

        static string PostFirmware(string host, string pass, byte[] data, IProgress<double> progress)
        {
            string boundary = "----casinosign" + DateTime.Now.Ticks.ToString("x");
            var head = Encoding.ASCII.GetBytes($"--{boundary}\r\nContent-Disposition: form-data; name=\"firmware\"; filename=\"firmware.bin\"\r\nContent-Type: application/octet-stream\r\n\r\n");
            var tail = Encoding.ASCII.GetBytes($"\r\n--{boundary}--\r\n");
            var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create($"http://{host}/update");
            req.Method = "POST";
            req.ContentType = "multipart/form-data; boundary=" + boundary;
            req.Headers["X-OTA-Pass"] = pass;
            req.ContentLength = head.Length + data.Length + tail.Length;
            req.Timeout = 120000; req.ReadWriteTimeout = 120000;
            req.AllowWriteStreamBuffering = false;
            using (var s = req.GetRequestStream())
            {
                s.Write(head, 0, head.Length);
                for (int off = 0; off < data.Length; off += 4096)
                {
                    int n = Math.Min(4096, data.Length - off);
                    s.Write(data, off, n);
                    progress.Report((double)(off + n) / data.Length);
                }
                s.Write(tail, 0, tail.Length);
            }
            using var resp = req.GetResponse();
            using var rd = new StreamReader(resp.GetResponseStream());
            return rd.ReadToEnd();
        }

        void Append(string s)
        {
            if (_out.TextLength > 300000) _out.Clear();
            _out.AppendText(s);
        }
    }
}
