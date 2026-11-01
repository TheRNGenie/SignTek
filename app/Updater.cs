using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CasinoSignTool
{
    /// <summary>Checks GitHub (updates/latest.json in the SignTek repo) for a newer version of the app and installs it in place (app, firmware files and all).</summary>
    public static class Updater
    {
        public const string Repo = "TheRNGenie/SignTek";
        const string AssetName = "CasinoSignTool.zip";

        public static Version Current => Assembly.GetExecutingAssembly().GetName().Version;
        public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";
        static string AppDir => AppDomain.CurrentDomain.BaseDirectory;

        public class Release { public Version Version; public string Tag, Notes, ZipUrl; public long Size; }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var wc = new WebClient { Encoding = Encoding.UTF8 };
            wc.Headers[HttpRequestHeader.UserAgent] = "CasinoSignTool/" + CurrentText;
            wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return wc;
        }

        // updates/latest.json in the repo: {"version":"1.2.0","notes":"...","zip":"updates/CasinoSignTool.zip","size":123}
        const string Raw = "https://raw.githubusercontent.com/" + Repo + "/main/";

        /// <summary>Latest published version, or null if there is none. Throws on network errors.</summary>
        public static async Task<Release> GetLatest()
        {
            string json;
            using (var wc = Client())
            {
                try { json = await wc.DownloadStringTaskAsync(Raw + "updates/latest.json?t=" + DateTime.UtcNow.Ticks); }
                catch (WebException ex) when ((ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound) { return null; }
            }
            var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            var tag = d.TryGetValue("version", out var t) ? t as string ?? "" : "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var ver)) return null;
            var zip = d.TryGetValue("zip", out var z) ? z as string : null;
            if (string.IsNullOrEmpty(zip)) return null;
            return new Release
            {
                Version = ver, Tag = tag,
                Notes = d.TryGetValue("notes", out var b) ? b as string ?? "" : "",
                ZipUrl = zip.StartsWith("http") ? zip : Raw + zip,
                Size = d.TryGetValue("size", out var sz) ? Convert.ToInt64(sz) : 0,
            };
        }

        static Version Norm(Version v) => new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        public static bool IsNewer(Release r) => r != null && Norm(r.Version) > Norm(Current);

        /// <summary>Quiet check when the app opens: calls back only if a newer version exists.</summary>
        public static async void CheckInBackground(Action<Release> onNewer)
        {
            try { var r = await GetLatest(); if (IsNewer(r)) onNewer(r); } catch { }
        }

        /// <summary>The "Check for updates" window.</summary>
        public static async Task ShowDialog(IWin32Window owner, Release known = null)
        {
            using (var f = new Form { Text = "Update Casino Sign Tool", ClientSize = new Size(640, 460), MinimumSize = new Size(480, 360), StartPosition = FormStartPosition.CenterParent, Font = new Font("Segoe UI", 9.5f), MinimizeBox = false, MaximizeBox = true, FormBorderStyle = FormBorderStyle.Sizable, AutoScaleMode = AutoScaleMode.Dpi })
            {
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var head = new Label { AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new Size(2000, 0), Margin = new Padding(0, 0, 0, 8), Font = new Font("Segoe UI", 11f, FontStyle.Bold), Text = $"You have version {CurrentText}. Checking for a newer one…" };
                var notes = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window,
                    Text = "How updating works:\r\n\r\n" +
                           "• The app checks for a new version each time it opens. When there is one, a yellow \"Update available\" button appears in the top bar.\r\n" +
                           "• Press Install update. The app downloads the new version, closes, replaces its files (including the firmware files in the firmware folder), and opens again by itself. It takes under a minute.\r\n" +
                           "• Your settings, pin map, pixel art and playlists are kept: they're stored separately in your user folder.\r\n" +
                           "• If the update includes new sign firmware, the app opens on tab 0 afterwards: press Install firmware (USB) or Update over WiFi to put it on the board.\r\n" +
                           "• Needs an internet connection. If it fails, nothing is changed and the old version keeps working.\r\n" +
                           "• This window can be resized: drag its edge or corner." };
                var bar = new ProgressBar { Dock = DockStyle.Fill, Height = 22, Maximum = 1000, Margin = new Padding(0, 8, 0, 8) };
                var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
                var close = new Button { Text = "Close", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(100, 36), Padding = new Padding(10, 4, 10, 4), DialogResult = DialogResult.Cancel };
                var install = new Button { Text = "Install update", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(150, 36), Padding = new Padding(10, 4, 10, 4), Enabled = false, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
                row.Controls.Add(close); row.Controls.Add(install);
                layout.Controls.Add(head, 0, 0); layout.Controls.Add(notes, 0, 1); layout.Controls.Add(bar, 0, 2); layout.Controls.Add(row, 0, 3);
                f.Controls.Add(layout);
                f.CancelButton = close;
                f.Resize += (s, e) => head.MaximumSize = new Size(Math.Max(200, layout.ClientSize.Width - 24), 0);

                Release rel = known;
                f.Shown += async (s, e) =>
                {
                    try { if (rel == null) rel = await GetLatest(); }
                    catch (Exception ex) { head.ForeColor = Color.DarkRed; head.Text = "Couldn't check for updates: " + ex.Message + " Check the internet connection and try again."; return; }
                    if (!IsNewer(rel)) { head.ForeColor = Color.DarkGreen; head.Text = $"You have the latest version ({CurrentText})."; return; }
                    head.Text = $"Version {Norm(rel.Version).ToString(3)} is available (you have {CurrentText}). Download size {rel.Size / 1024 / 1024.0:0.0} MB.";
                    if (!string.IsNullOrWhiteSpace(rel.Notes)) notes.Text = "What's new:\r\n" + rel.Notes.Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n\r\n" + notes.Text;
                    install.Enabled = true;
                    notes.Select(0, 0); install.Focus();
                };
                install.Click += async (s, e) =>
                {
                    install.Enabled = false; close.Enabled = false;
                    try
                    {
                        await Install(rel, p => f.BeginInvoke((Action)(() => { bar.Value = (int)(p * 1000); head.Text = $"Downloading… {p * 100:0}%"; })));
                        head.Text = "Installing. The app will close and reopen by itself.";
                        await Task.Delay(800);
                        Application.Exit();
                    }
                    catch (Exception ex)
                    {
                        head.ForeColor = Color.DarkRed;
                        head.Text = "Update failed, nothing was changed: " + ex.Message;
                        install.Enabled = true; close.Enabled = true; bar.Value = 0;
                    }
                };
                f.ShowDialog(owner);
            }
        }

        static async Task Install(Release rel, Action<double> progress)
        {
            // Can we write next to the exe? (Not if it was put in Program Files.)
            var probe = Path.Combine(AppDir, "update-test.tmp");
            try { File.WriteAllText(probe, "x"); File.Delete(probe); }
            catch { throw new Exception($"Windows won't let the app change its own folder ({AppDir}). Move the CasinoSignTool folder somewhere like Documents or Desktop and try again."); }

            var work = Path.Combine(Path.GetTempPath(), "CasinoSignTool-update");
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);
            var zip = Path.Combine(work, AssetName);
            using (var wc = Client())
            {
                wc.DownloadProgressChanged += (s, e) => progress(e.TotalBytesToReceive > 0 ? (double)e.BytesReceived / e.TotalBytesToReceive : 0);
                await wc.DownloadFileTaskAsync(rel.ZipUrl, zip);
            }
            var unpacked = Path.Combine(work, "files");
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, unpacked));
            // The zip holds one CasinoSignTool folder.
            var src = Path.Combine(unpacked, "CasinoSignTool");
            if (!File.Exists(Path.Combine(src, "CasinoSignTool.exe"))) src = unpacked;
            if (!File.Exists(Path.Combine(src, "CasinoSignTool.exe"))) throw new Exception("the download doesn't contain CasinoSignTool.exe.");

            // Remember the firmware version so the new app can point at tab 0 if it changed.
            try { File.Copy(Path.Combine(AppDir, "firmware", "version.txt"), Path.Combine(AppSettings.Dir, "fw-before-update.txt"), true); } catch { }

            // A small script finishes the job after this app closes (a running exe can't overwrite itself).
            var exe = Path.Combine(AppDir, "CasinoSignTool.exe");
            var dest = AppDir.TrimEnd('\\');
            var cmd = Path.Combine(work, "finish-update.cmd");
            File.WriteAllText(cmd,
                "@echo off\r\n" +
                "title Updating Casino Sign Tool\r\n" +
                "echo Updating Casino Sign Tool, please wait...\r\n" +
                $":wait\r\ntasklist /FI \"PID eq {Process.GetCurrentProcess().Id}\" | find \"{Process.GetCurrentProcess().Id}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                $"robocopy \"{src}\" \"{dest}\" /E /R:5 /W:1 /NFL /NDL /NJH /NJS >nul\r\n" +
                $"start \"\" \"{exe}\"\r\n", Encoding.ASCII);
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cmd}\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = work });
        }

        /// <summary>True once, right after an update that brought new firmware files.</summary>
        public static bool FirmwareChangedByUpdate()
        {
            var before = Path.Combine(AppSettings.Dir, "fw-before-update.txt");
            if (!File.Exists(before)) return false;
            try
            {
                var old = File.ReadAllText(before).Trim();
                File.Delete(before);
                var now = File.ReadAllText(Path.Combine(AppDir, "firmware", "version.txt")).Trim();
                return old != now;
            }
            catch { return false; }
        }
    }
}
