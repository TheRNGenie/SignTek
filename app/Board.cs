using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.IO.Ports;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CasinoSignTool
{
    /// <summary>A byte pipe to the board: USB serial, Bluetooth COM port or TCP over WiFi.</summary>
    public abstract class Link : IDisposable
    {
        public event Action<string> LineReceived;
        public event Action<string> Closed;
        public abstract string Description { get; }
        public abstract void WriteLine(string s);
        public abstract void Dispose();

        readonly StringBuilder _buf = new StringBuilder();
        protected void Feed(string chunk)
        {
            foreach (char c in chunk)
            {
                if (c == '\n')
                {
                    var line = _buf.ToString().Trim();
                    _buf.Clear();
                    if (line.Length > 0) LineReceived?.Invoke(line);
                }
                else if (c != '\r' && _buf.Length < 400000) _buf.Append(c);
            }
        }
        protected void RaiseClosed(string why) => Closed?.Invoke(why);
    }

    public class SerialLink : Link
    {
        readonly SerialPort _port;
        public override string Description => _port.PortName;

        public SerialLink(string portName)
        {
            // Keep DTR/RTS low so opening the port doesn't reset the ESP32. The UNO R4 WiFi is the opposite:
            // its USB chip only answers while DTR is on (and DTR doesn't reset it).
            bool arduino = false;
            try { arduino = PortInfo.Scan(out _).Any(p => p.Port == portName && p.Vid == "2341"); } catch { }
            _port = new SerialPort(portName, 115200)
            {
                DtrEnable = arduino, RtsEnable = arduino,
                NewLine = "\n", Encoding = Encoding.UTF8,
                ReadTimeout = 500, WriteTimeout = 2000,
            };
            _port.DataReceived += (s, e) =>
            {
                try { Feed(_port.ReadExisting()); }
                catch (Exception ex) { RaiseClosed(ex.Message); }
            };
            _port.ErrorReceived += (s, e) => { };
            // Windows can keep a port "busy" for a moment after it was last closed (by esptool or an
            // earlier connect attempt). Retry for a few seconds before giving up.
            for (int i = 0; ; i++)
            {
                try { _port.Open(); break; }
                catch (UnauthorizedAccessException) when (i < 8) { Thread.Sleep(500); }
                catch (UnauthorizedAccessException)
                {
                    throw new IOException($"Windows says {portName} is busy (access denied). Close every copy of this app and the Arduino IDE, " +
                        "unplug the board for 5 seconds, plug it back in, then press Connect. If it still says this, restart the PC.");
                }
            }
        }

        public override void WriteLine(string s)
        {
            try { _port.Write(s + "\n"); }
            catch (Exception ex) { RaiseClosed(ex.Message); throw; }
        }

        public override void Dispose() { try { _port.Close(); _port.Dispose(); } catch { } }
    }

    public class TcpLink : Link
    {
        readonly TcpClient _tcp;
        readonly NetworkStream _stream;
        readonly Thread _reader;
        readonly string _host;
        volatile bool _closing;
        public override string Description => _host;

        public TcpLink(string host, int port)
        {
            _host = host;
            _tcp = new TcpClient { NoDelay = true };
            var ar = _tcp.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(5000)) { _tcp.Close(); throw new IOException($"No answer from {host}:{port}"); }
            _tcp.EndConnect(ar);
            _stream = _tcp.GetStream();
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "tcp-read" };
            _reader.Start();
        }

        void ReadLoop()
        {
            var buf = new byte[4096];
            var dec = Encoding.UTF8.GetDecoder();
            var chars = new char[8192];
            try
            {
                while (true)
                {
                    int n = _stream.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    int c = dec.GetChars(buf, 0, n, chars, 0);
                    Feed(new string(chars, 0, c));
                }
                if (!_closing) RaiseClosed("board closed the connection");
            }
            catch (Exception ex) { if (!_closing) RaiseClosed(ex.Message); }
        }

        public override void WriteLine(string s)
        {
            var b = Encoding.UTF8.GetBytes(s + "\n");
            try { _stream.Write(b, 0, b.Length); }
            catch (Exception ex) { RaiseClosed(ex.Message); throw; }
        }

        public override void Dispose() { _closing = true; try { _tcp.Close(); } catch { } }
    }

    /// <summary>
    /// Sends one command at a time and waits for its JSON reply. Lines starting
    /// with {"evt": are events; anything that isn't JSON is boot/debug output.
    /// </summary>
    public class Board : IDisposable
    {
        Link _link;
        readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        TaskCompletionSource<Reply> _pending;
        readonly object _lock = new object();

        public event Action<string, bool> Log;          // text, outgoing?
        public event Action<Reply> EventReceived;
        public event Action<string> Disconnected;

        public bool Connected => _link != null;
        public string Where => _link?.Description ?? "";
        public bool OverWifi => _link is TcpLink;

        public async Task<Reply> ConnectSerial(string port)
        {
            lock (_raw) _raw.Clear();
            Disconnect(null);
            Link link = await Task.Run(() => (Link)new SerialLink(port));
            Attach(link);
            // The board may reset when the port opens; give it a few tries.
            for (int i = 0; i < 6; i++)
            {
                try { return await Send("INFO", 900); }
                catch (TimeoutException) { }
            }
            string raw; lock (_raw) { raw = string.Join(" | ", _raw); _raw.Clear(); }
            Disconnect("no reply");
            if (raw.Length > 0)
                throw new IOException($"The board on {port} is printing text but not answering. It may be crashing at start-up; copy the console text below when asking for help. Last lines: {raw}");
            throw new IOException($"Nothing answered on {port}. Install the firmware (tab 0), press the board's RESET button, and check this is the right COM port.");
        }

        public async Task<Reply> ConnectTcp(string host, int port)
        {
            Link l = await Task.Run(() => (Link)new TcpLink(host, port));
            Attach(l);
            try { return await Send("INFO", 3000); }
            catch (TimeoutException) { Disconnect("no reply"); throw new IOException($"{host} accepted the connection but didn't answer INFO."); }
        }

        void Attach(Link l)
        {
            Disconnect(null);
            _link = l;
            l.LineReceived += OnLine;
            l.Closed += why => Disconnect(why);
        }

        public void Disconnect(string why)
        {
            var l = _link;
            if (l == null) return;
            _link = null;
            l.Dispose();
            lock (_lock) { _pending?.TrySetException(new IOException("disconnected")); _pending = null; }
            if (why != null) Disconnected?.Invoke(why);
        }

        readonly System.Collections.Generic.Queue<string> _raw = new System.Collections.Generic.Queue<string>();

        void OnLine(string line)
        {
            if (!line.StartsWith("{")) { lock (_raw) { _raw.Enqueue(line); while (_raw.Count > 6) _raw.Dequeue(); } }
            bool quiet = line.Contains("\"fb\":") || line.Contains("\"cap\":");     // live preview polling would flood the console
            if (!quiet) Log?.Invoke(line, false);
            if (!line.StartsWith("{")) return;
            Reply r;
            try { r = Json.Parse(line); } catch { return; }
            if (r.Has("evt")) { EventReceived?.Invoke(r); return; }
            if (!r.Has("ok")) return;
            lock (_lock) { _pending?.TrySetResult(r); _pending = null; }
        }

        /// <summary>Send a command and wait for its reply. Throws BoardError if the board says ok:false.</summary>
        public async Task<Reply> Send(string cmd, int timeoutMs = 3000, bool throwOnError = true)
        {
            if (_link == null) throw new IOException("Not connected to the board.");
            await _gate.WaitAsync();
            try
            {
                var tcs = new TaskCompletionSource<Reply>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_lock) _pending = tcs;
                if (cmd != "FB") Log?.Invoke(cmd.Length > 120 ? cmd.Substring(0, 120) + "…" : cmd, true);
                var link = _link ?? throw new IOException("Not connected to the board.");
                link.WriteLine(cmd);
                var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
                if (done != tcs.Task)
                {
                    lock (_lock) if (_pending == tcs) _pending = null;
                    throw new TimeoutException($"No reply to \"{cmd.Split(' ')[0]}\" within {timeoutMs / 1000.0:0.#} s.");
                }
                var r = await tcs.Task;
                if (throwOnError && !r.Ok) throw new BoardError(r.Err);
                return r;
            }
            finally { _gate.Release(); }
        }

        public void Dispose() => Disconnect(null);
    }

    public class BoardError : Exception { public BoardError(string m) : base(m) { } }
}

namespace CasinoSignTool
{
    /// <summary>Finds signs on the local network: UDP broadcast "CASINOSIGN?" to port 3334, signs answer with JSON.</summary>
    public static class Discovery
    {
        public class Found { public string Ip, Name, Host, Fw; public int Port; public bool Power; public override string ToString() => $"{Name} ({Ip})"; }

        static IEnumerable<System.Net.IPAddress> BroadcastAddresses()
        {
            yield return System.Net.IPAddress.Broadcast;
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                    var ip = ua.Address.GetAddressBytes(); var mask = ua.IPv4Mask.GetAddressBytes();
                    if (ip[0] == 127) continue;
                    var b = new byte[4];
                    for (int i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~mask[i]);
                    yield return new System.Net.IPAddress(b);
                }
            }
        }

        public static async Task<List<Found>> Find(int timeoutMs = 1500)
        {
            var found = new Dictionary<string, Found>();
            using (var udp = new UdpClient(0) { EnableBroadcast = true })
            {
                var msg = Encoding.ASCII.GetBytes("CASINOSIGN?");
                foreach (var addr in BroadcastAddresses().Distinct())
                    try { await udp.SendAsync(msg, msg.Length, new System.Net.IPEndPoint(addr, 3334)); } catch { }
                var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (DateTime.UtcNow < until)
                {
                    var recv = udp.ReceiveAsync();
                    var done = await Task.WhenAny(recv, Task.Delay(until - DateTime.UtcNow));
                    if (done != recv) break;
                    try
                    {
                        var r = Json.Parse(Encoding.UTF8.GetString(recv.Result.Buffer));
                        if (!r.Bool("casinosign")) continue;
                        string ip = r.Str("ip"); if (ip == "" || ip == "0.0.0.0") ip = recv.Result.RemoteEndPoint.Address.ToString();
                        found[ip] = new Found { Ip = ip, Name = r.Str("name"), Host = r.Str("host"), Fw = r.Str("fw"), Port = r.Int("port", 3333), Power = r.Bool("power") };
                    }
                    catch { }
                }
            }
            return found.Values.ToList();
        }
    }
}
