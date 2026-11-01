using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace CasinoSignTool
{
    public enum PinRole { Unknown, GND, V5, NC, DataTop, DataBot, Clk, Lat, Oe, A, B, C, D, Other }

    public static class Roles
    {
        public static readonly PinRole[] All = (PinRole[])Enum.GetValues(typeof(PinRole));

        /// <summary>Roles that carry a signal the ESP32 has to drive.</summary>
        public static readonly PinRole[] Signals = { PinRole.DataTop, PinRole.DataBot, PinRole.Clk, PinRole.Lat, PinRole.Oe, PinRole.A, PinRole.B, PinRole.C, PinRole.D };

        public static string Code(PinRole r) => r switch
        {
            PinRole.Unknown => "?", PinRole.GND => "GND", PinRole.V5 => "3V3", PinRole.NC => "NC",
            PinRole.DataTop => "DAT", PinRole.DataBot => "DAT2", PinRole.Clk => "CLK", PinRole.Lat => "LAT",
            PinRole.Oe => "OE", PinRole.A => "A", PinRole.B => "B", PinRole.C => "C", PinRole.D => "D",
            _ => "OTH",
        };

        public static PinRole FromCode(string c)
        {
            if (string.Equals(c, "5V", StringComparison.OrdinalIgnoreCase)) return PinRole.V5;   // maps saved before 1.1.3
            foreach (var r in All) if (string.Equals(Code(r), c, StringComparison.OrdinalIgnoreCase)) return r;
            return PinRole.Unknown;
        }

        public static string Label(PinRole r) => r switch
        {
            PinRole.Unknown => "Unknown (not tested yet)",
            PinRole.GND => "GND (ground)",
            PinRole.V5 => "+3.3V (power; the V-002 runs at 3.3 V)",
            PinRole.NC => "Not connected / does nothing",
            PinRole.DataTop => "DATA (serial data, top half or only line)",
            PinRole.DataBot => "DATA 2 (serial data, bottom half)",
            PinRole.Clk => "CLK (shift clock)",
            PinRole.Lat => "LAT / LE (latch)",
            PinRole.Oe => "OE (output enable / blanking)",
            PinRole.A => "Row address A (fastest)",
            PinRole.B => "Row address B",
            PinRole.C => "Row address C",
            PinRole.D => "Row address D (16-row scan only)",
            _ => "Other (does something, not needed)",
        };

        public static Color Color(PinRole r) => r switch
        {
            PinRole.Unknown => System.Drawing.Color.FromArgb(90, 90, 96),
            PinRole.GND => System.Drawing.Color.FromArgb(30, 30, 30),
            PinRole.V5 => System.Drawing.Color.FromArgb(220, 40, 40),
            PinRole.NC => System.Drawing.Color.FromArgb(160, 160, 160),
            PinRole.DataTop => System.Drawing.Color.FromArgb(40, 170, 80),
            PinRole.DataBot => System.Drawing.Color.FromArgb(120, 210, 90),
            PinRole.Clk => System.Drawing.Color.FromArgb(240, 180, 20),
            PinRole.Lat => System.Drawing.Color.FromArgb(60, 140, 240),
            PinRole.Oe => System.Drawing.Color.FromArgb(170, 90, 230),
            PinRole.A => System.Drawing.Color.FromArgb(0, 190, 190),
            PinRole.B => System.Drawing.Color.FromArgb(0, 160, 200),
            PinRole.C => System.Drawing.Color.FromArgb(0, 130, 210),
            PinRole.D => System.Drawing.Color.FromArgb(0, 100, 220),
            _ => System.Drawing.Color.FromArgb(230, 120, 60),
        };

        /// <summary>The sign-config key on the board for each signal role.</summary>
        public static string CfgKey(PinRole r) => r switch
        {
            PinRole.DataTop => "pin_data_top", PinRole.DataBot => "pin_data_bot", PinRole.Clk => "pin_clk",
            PinRole.Lat => "pin_lat", PinRole.Oe => "pin_oe", PinRole.A => "pin_a", PinRole.B => "pin_b",
            PinRole.C => "pin_c", PinRole.D => "pin_d", _ => null,
        };
    }

    public class HeaderPin
    {
        public int Number { get; set; }
        public string Role { get; set; } = "?";       // role code, see Roles.Code
        public string Note { get; set; } = "";
        public string LastSniff { get; set; } = "";
        public double SniffHz { get; set; }
        public double SniffDuty { get; set; } = -1;

        [System.Web.Script.Serialization.ScriptIgnore]
        public PinRole R { get => Roles.FromCode(Role); set => Role = Roles.Code(value); }
    }

    /// <summary>The 50-pin ribbon header map. Saved as JSON on the PC, and a compact copy on the board.</summary>
    public class HeaderMap
    {
        public int PinCount { get; set; } = 50;
        public bool FlipView { get; set; }           // mirror left/right (socket seen from the other side)
        public string ProbeGpio { get; set; } = "25";
        public string SniffGpio { get; set; } = "39";
        public List<HeaderPin> Pins { get; set; } = new List<HeaderPin>();

        public HeaderMap() { EnsurePins(); }

        public void EnsurePins()
        {
            for (int i = Pins.Count + 1; i <= PinCount; i++) Pins.Add(new HeaderPin { Number = i });
            if (Pins.Count > PinCount) Pins.RemoveRange(PinCount, Pins.Count - PinCount);
        }

        public HeaderPin this[int number] => Pins[number - 1];

        public int MappedCount => Pins.Count(p => p.R != PinRole.Unknown);

        public List<HeaderPin> WithRole(PinRole r) => Pins.Where(p => p.R == r).ToList();

        /// <summary>Compact form stored on the board: "50|1=GND|2=CLK|...". Unknown pins are left out.</summary>
        public string ToCompact()
        {
            var parts = new List<string> { PinCount.ToString() };
            foreach (var p in Pins) if (p.R != PinRole.Unknown) parts.Add($"{p.Number}={p.Role}");
            return string.Join("|", parts);
        }

        public static HeaderMap FromCompact(string s)
        {
            var m = new HeaderMap();
            if (string.IsNullOrWhiteSpace(s)) return m;
            var parts = s.Split('|');
            if (int.TryParse(parts[0], out int n) && n >= 2 && n <= 80) { m.PinCount = n; m.Pins.Clear(); m.EnsurePins(); }
            foreach (var part in parts.Skip(1))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && int.TryParse(kv[0], out int num) && num >= 1 && num <= m.PinCount)
                    m[num].Role = Roles.Code(Roles.FromCode(kv[1]));
            }
            return m;
        }

        public void Save(string path) => File.WriteAllText(path, Json.Write(this));

        public static HeaderMap Load(string path)
        {
            var m = Json.Read<HeaderMap>(File.ReadAllText(path)) ?? new HeaderMap();
            m.Pins = m.Pins.OrderBy(p => p.Number).ToList();
            foreach (var p in m.Pins) p.Role = Roles.Code(Roles.FromCode(p.Role));   // old "5V" becomes "3V3"
            m.EnsurePins();
            return m;
        }

        /// <summary>Problems that would stop the sign working with this map.</summary>
        public List<string> Problems()
        {
            var r = new List<string>();
            if (WithRole(PinRole.GND).Count == 0) r.Add("No GND pin marked yet.");
            foreach (var role in new[] { PinRole.DataTop, PinRole.Clk, PinRole.Lat })
                if (WithRole(role).Count == 0) r.Add($"{Roles.Code(role)} not found yet (required).");
            if (WithRole(PinRole.Oe).Count == 0) r.Add("OE not found yet (strongly recommended, it blanks the LEDs while rows switch).");
            if (WithRole(PinRole.A).Count == 0) r.Add("Row address A not found yet.");
            foreach (var role in Roles.Signals)
                if (WithRole(role).Count > 1) r.Add($"{Roles.Code(role)} is marked on {WithRole(role).Count} pins. Only one is driven, the others will be connected together; check this is right.");
            int unk = Pins.Count(p => p.R == PinRole.Unknown);
            if (unk > 0) r.Add($"{unk} pin(s) still unknown. They will be left unconnected.");
            return r;
        }
    }

    /// <summary>Where each usable GPIO is on the Freenove 4" ESP32 display board (from Freenove's schematic).</summary>
    public static class BoardPins
    {
        public static string Where(int gpio) => gpio switch
        {
            23 => "SPI connector, pin marked MOSI",
            19 => "SPI connector, pin marked MISO",
            18 => "SPI connector, pin marked CLK / SCK",
            5  => "SPI connector, pin marked CS",
            32 => "I2C connector, pin marked SDA",
            25 => "I2C connector, pin marked SCL",
            39 => "IO connector (input only)",
            35 => "input only (solder point)",
            0  => "BOOT button: solder to the button pad that reads 3.3 V (the other pad is GND)",
            22 => "solder point: RGB LED red line (IO22 on the ESP32 module)",
            16 => "solder point: RGB LED green line (IO16 on the ESP32 module)",
            17 => "solder point: RGB LED blue line (IO17 on the ESP32 module)",
            26 => "solder point: speaker amplifier input (IO26 on the ESP32 module)",
            4  => "solder point: speaker amplifier enable (IO4 on the ESP32 module)",
            _  => "",
        };
    }

    /// <summary>App settings kept in %APPDATA%\CasinoSignTool.</summary>
    public class AppSettings
    {
        public string LastPort { get; set; } = "";
        public string LastHost { get; set; } = "casinosign.local";
        public bool AutoConnect { get; set; } = true;
        public bool NoShifter { get; set; } = true;                    // wiring guide: direct 3.3 V drive, no 74HCT245          // find the sign on WiFi when the app opens
        public string OtaPass { get; set; } = "casinosign";
        public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CasinoSignTool");
        static string FilePath => Path.Combine(Dir, "settings.json");
        public static string MapAutosave => Path.Combine(Dir, "header-map.json");
        public static string ArtAutosave => Path.Combine(Dir, "pixel-art.json");

        public static AppSettings Load()
        {
            try { return Json.Read<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings(); }
            catch { return new AppSettings(); }
        }
        public void Save() { try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, Json.Write(this)); } catch { } }
    }

    /// <summary>Local copy of the board's sign config (pin per role, scan settings).</summary>
    public class SignCfg
    {
        public Dictionary<PinRole, int> Gpio = new Dictionary<PinRole, int>
        {
            [PinRole.DataTop] = 23, [PinRole.DataBot] = -1, [PinRole.Clk] = 18, [PinRole.Lat] = 19, [PinRole.Oe] = 5,
            [PinRole.A] = 32, [PinRole.B] = 25, [PinRole.C] = 0, [PinRole.D] = -1,
        };
        public int ScanRows = 8, RefreshHz = 120, Brightness = 100, BrightCap = 100;
        public bool OeActiveLow = true, DataInvert, AddrInvert, ReverseCols, ChainBottomFirst = true, Configured;

        public void From(Reply r)
        {
            foreach (var role in Roles.Signals) if (r.Has(Roles.CfgKey(role))) Gpio[role] = r.Int(Roles.CfgKey(role), -1);
            ScanRows = r.Int("scan_rows", ScanRows); RefreshHz = r.Int("refresh_hz", RefreshHz); Brightness = r.Int("brightness", Brightness); BrightCap = r.Int("bright_cap", BrightCap);
            OeActiveLow = r.Bool("oe_active_low", OeActiveLow); DataInvert = r.Bool("data_invert"); AddrInvert = r.Bool("addr_invert");
            ReverseCols = r.Bool("reverse_cols"); ChainBottomFirst = r.Bool("chain_bottom_first", true); Configured = r.Bool("configured");
        }

        public IEnumerable<string> ToCommands()
        {
            foreach (var role in Roles.Signals) yield return $"SET {Roles.CfgKey(role)} {Gpio[role]}";
            yield return $"SET scan_rows {ScanRows}";
            yield return $"SET refresh_hz {RefreshHz}";
            yield return $"SET bright_cap {BrightCap}";
            yield return $"SET brightness {Brightness}";
            yield return $"SET oe_active_low {(OeActiveLow ? 1 : 0)}";
            yield return $"SET data_invert {(DataInvert ? 1 : 0)}";
            yield return $"SET addr_invert {(AddrInvert ? 1 : 0)}";
            yield return $"SET reverse_cols {(ReverseCols ? 1 : 0)}";
            yield return $"SET chain_bottom_first {(ChainBottomFirst ? 1 : 0)}";
        }
    }
}
