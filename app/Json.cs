using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace CasinoSignTool
{
    /// <summary>One JSON reply or event line from the board.</summary>
    public class Reply
    {
        public readonly Dictionary<string, object> D;
        public Reply(Dictionary<string, object> d) { D = d ?? new Dictionary<string, object>(); }

        public bool Ok => Bool("ok");
        public string Err => Str("err");
        public bool Has(string k) => D.ContainsKey(k);
        public string Str(string k, string def = "") => D.TryGetValue(k, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : def;
        public int Int(string k, int def = 0) { try { return D.TryGetValue(k, out var v) && v != null ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : def; } catch { return def; } }
        public double Dbl(string k, double def = 0) { try { return D.TryGetValue(k, out var v) && v != null ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : def; } catch { return def; } }
        public bool Bool(string k, bool def = false)
        {
            if (!D.TryGetValue(k, out var v) || v == null) return def;
            if (v is bool b) return b;
            if (v is string s) { s = s.Trim().ToLowerInvariant(); return s == "1" || s == "true" || s == "on" || s == "yes"; }
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture) != 0; } catch { return def; }
        }
        public List<int> Ints(string k)
        {
            var r = new List<int>();
            if (D.TryGetValue(k, out var v) && v is IEnumerable e && !(v is string))
                foreach (var x in e) r.Add(Convert.ToInt32(x, CultureInfo.InvariantCulture));
            return r;
        }
        public List<Reply> List(string k)
        {
            var r = new List<Reply>();
            if (D.TryGetValue(k, out var v) && v is IEnumerable e && !(v is string))
                foreach (var x in e) r.Add(new Reply(x as Dictionary<string, object>));
            return r;
        }
    }

    public static class Json
    {
        static readonly JavaScriptSerializer S = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        public static Reply Parse(string line) => new Reply(S.DeserializeObject(line) as Dictionary<string, object>);
        public static string Write(object o) => S.Serialize(o);
        public static T Read<T>(string s) => S.Deserialize<T>(s);
    }
}
