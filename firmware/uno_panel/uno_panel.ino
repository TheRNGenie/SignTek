// ============================================================================
// uno_panel 2.0.0: drive the casino sign's RGB-02 panel input from an
// Arduino UNO R4 WiFi (or Elegoo UNO R4 WiFi). Flashed and controlled from the
// Casino Sign Tool app, tab "1d. Uno R4".
//
// Every panel signal that changes with the clock is on port 1 of the RA4M1, so
// one register write updates them all at once. Rows B and C (which only change
// between rows) are on port 4.
//
//   panel pin  signal       Uno pin   RA4M1
//   16         clock        D2        P104
//   6          latch        D3        P105
//   5          OE           D4        P106
//   7          data 1       D5        P107
//   9 + 13     timing 1     D6        P111
//   2          timing 2     D7        P112
//   8          data 2       D10       P103
//   10         data 3       A4        P101
//   11         data 4       A5        P100
//   3          row A        D13       P102
//   4          row B        D11       P411
//   1          row C        D12       P410
//   15         GND          GND
// Each signal wire goes through a 1 kOhm resistor (the Uno's outputs are 5 V).
//
// The output plays a list of "clock steps" from RAM over and over. Each step
// sets every line, then pulses the clock. The list is either built here from a
// test pattern (PTEST) or sent by the app from a recording (LOAD/W/PLAY).
//
// Commands: one per line over USB serial (115200) or WiFi (TCP port 3333).
// Every reply is one JSON line, the same style as the ESP32 sign firmware, so
// the app's top bar, WiFi tab and "Find sign" work with the Uno too.
//   PING, INFO, TIME SET <s>, GET CFG, MODE RUN|DIAG
//   PTEST [ON|SET|OFF] key=val ...   test pattern
//   LOAD n / W i hhhh ... / PLAY     play a recording
//   STOP                             outputs off
//   WIFI STATUS|SCAN|SSID <s>|PASS <p>|CONNECT|FORGET|HOST <name>
// Discovery: answers the UDP text CASINOSIGN? on port 3334.
// Logical word bits: 0 latch, 1 OE, 2 row A, 3 row B, 4 row C, 5 data 1 (pin 7),
// 6 data 2 (pin 8), 7 data 3 (pin 10), 8 data 4 (pin 11), 9 timing 1 (pins 9+13),
// 10 timing 2 (pin 2). Higher bits are ignored.
// ============================================================================
#include <Arduino.h>
#include <WiFiS3.h>
#include <WiFiUdp.h>
#include <EEPROM.h>

#define FW_VERSION "uno-2.0.0"
#define MAXSTEPS 2400

// port 1 bit numbers
static const uint16_t B_CLK = 1u << 4, B_LAT = 1u << 5, B_OE = 1u << 6, B_D1 = 1u << 7,
                      B_T1 = 1u << 11, B_T2 = 1u << 12, B_D2 = 1u << 3, B_D3 = 1u << 1,
                      B_D4 = 1u << 0, B_A = 1u << 2;
static const uint16_t M1 = B_CLK | B_LAT | B_OE | B_D1 | B_T1 | B_T2 | B_D2 | B_D3 | B_D4 | B_A;
// port 4
static const uint16_t B_B = 1u << 11, B_C = 1u << 10;
static const uint16_t M4 = B_B | B_C;

static uint16_t p1[MAXSTEPS];
static uint8_t rw[MAXSTEPS];      // bit 0 row B, bit 1 row C
static volatile int nSteps = 0;

enum { M_STOP, M_TEST, M_REPLAY };
static int mode = M_STOP;
static int loaded = 0, loadN = 0;
static uint32_t frames = 0;

// test pattern settings (same meaning as the ESP32's PTEST)
static int pattern = 1, param = 0, mask = 1, onlyRow = -1;
static int bits = 246, oeOff = 150, tWidth = 30, tMode = 0;
static bool oeInv = false, latInv = false, dataInv = false;
static int slow = 0;
static int edge = 0;              // 0: panel reads on the rising clock edge, 1: falling

static uint16_t toPort1(uint16_t w) {
  uint16_t v = 0;
  if (w & (1 << 0)) v |= B_LAT;
  if (w & (1 << 1)) v |= B_OE;
  if (w & (1 << 2)) v |= B_A;
  if (w & (1 << 5)) v |= B_D1;
  if (w & (1 << 6)) v |= B_D2;
  if (w & (1 << 7)) v |= B_D3;
  if (w & (1 << 8)) v |= B_D4;
  if (w & (1 << 9)) v |= B_T1;
  if (w & (1 << 10)) v |= B_T2;
  if (edge) v |= B_CLK;           // falling-edge mode: data is set while the clock is high
  return v;
}
static uint8_t toRows(uint16_t w) { return ((w >> 3) & 1) | (((w >> 4) & 1) << 1); }

static void setStep(int i, uint16_t w) { p1[i] = toPort1(w); rw[i] = toRows(w); }

static inline void writePort1(uint16_t v) { R_PORT1->PCNTR3 = ((uint32_t)(~v & M1) << 16) | (v & M1); }
static inline void writeRows(uint8_t r) {
  uint16_t v = ((r & 1) ? B_B : 0) | ((r & 2) ? B_C : 0);
  R_PORT4->PCNTR3 = ((uint32_t)(~v & M4) << 16) | v;
}

static void allIdle() {
  uint16_t v = 0;
  if (!oeInv) v &= ~B_OE; else v |= B_OE;  // LEDs off (OE high = on in our guess, see PTEST)
  writePort1(v);
  writeRows(0);
}

static bool patBit(int k, int row) {
  if (onlyRow >= 0 && row != onlyRow) return false;
  switch (pattern) {
    case 1: return true;
    case 2: return k == param;
    case 3: return k < param;
    case 4: return ((k - param) & 7) == 0 && k >= param;
    case 5: return ((k + row) & 1) == 0;
    case 6: return k < bits / 2;
    default: return false;
  }
}

// Same frame as the ESP32 panel test: latch pulse + row change at clock 0,
// OE on from clock 2 to oeOff, timing pins low for tWidth clocks, and the
// data for the NEXT row shifted during this row.
static void buildTest() {
  int n = 0;
  bool lat = false, oe = false, t = true;
  int row = 0;
  for (int r = 0; r < 8 && n < MAXSTEPS; r++) {
    int next = (r + 1) & 7;
    for (int k = 0; k < bits && n < MAXSTEPS; k++) {
      if (k == 0) { oe = false; lat = true; row = r; t = (tMode == 2); }
      else if (k == 2) { lat = false; oe = true; }
      if (k == tWidth && tMode == 0) t = true;
      if (k == oeOff) oe = false;
      bool v = patBit(k, next) ^ dataInv;
      uint16_t w = 0;
      if (lat ^ latInv) w |= 1 << 0;
      if (oe ^ oeInv) w |= 1 << 1;
      w |= (row & 1) << 2; w |= ((row >> 1) & 1) << 3; w |= ((row >> 2) & 1) << 4;
      for (int i = 0; i < 4; i++) {
        bool d = ((mask >> i) & 1) ? v : dataInv;
        if (d) w |= 1 << (5 + i);
      }
      if (t) w |= (1 << 9) | (1 << 10);
      setStep(n++, w);
    }
  }
  nSteps = n;
}

static void playOnce() {
  int n = nSteps;
  uint8_t lastR = 0xFF;
  const uint32_t second = edge ? ((uint32_t)B_CLK << 16) : B_CLK;   // falling: clear CLK, rising: set CLK
  for (int i = 0; i < n; i++) {
    uint8_t r = rw[i];
    if (r != lastR) { writeRows(r); lastR = r; }
    writePort1(p1[i]);
    for (volatile int d = 0; d < slow; d++) {}
    R_PORT1->PCNTR3 = second;
    for (volatile int d = 0; d < slow; d++) {}
  }
  frames++;
}

// ---------------------------------------------------------------- JSON helpers
static String jstr(const String& v) {
  String o = "\"";
  for (unsigned i = 0; i < v.length(); i++) { char c = v[i]; if (c == '"' || c == '\\') o += '\\'; if (c >= 32) o += c; }
  return o + "\"";
}
struct J {
  String s = "{\"ok\":true";
  J& num(const char* k, long v) { s += ",\""; s += k; s += "\":"; s += String(v); return *this; }
  J& flag(const char* k, bool v) { s += ",\""; s += k; s += "\":"; s += v ? "true" : "false"; return *this; }
  J& str(const char* k, const String& v) { s += ",\""; s += k; s += "\":"; s += jstr(v); return *this; }
  J& raw(const char* k, const String& v) { s += ",\""; s += k; s += "\":"; s += v; return *this; }
  String done() { return s + "}"; }
};
static String err(const String& m) { return "{\"ok\":false,\"err\":" + jstr(m) + "}"; }

// ---------------------------------------------------------------- WiFi settings (EEPROM)
struct Net { uint32_t magic; char ssid[33]; char pass[65]; char host[25]; };
static Net net;
static const uint32_t NET_MAGIC = 0x554E4F32;
static WiFiServer server(3333);
static WiFiClient client;
static WiFiUDP udp;
static bool netUp = false;
static String tcpLine;
static uint32_t lastNet = 0;

static void netLoad() {
  EEPROM.get(0, net);
  if (net.magic != NET_MAGIC) { memset(&net, 0, sizeof(net)); net.magic = NET_MAGIC; strcpy(net.host, "casinosign-uno"); }
  net.ssid[32] = 0; net.pass[64] = 0; net.host[24] = 0;
}
static void netSave() { EEPROM.put(0, net); }

static String ipStr() { IPAddress ip = WiFi.localIP(); return String(ip[0]) + "." + ip[1] + "." + ip[2] + "." + ip[3]; }
static String macStr() {
  uint8_t m[6]; WiFi.macAddress(m); char b[20];
  snprintf(b, sizeof b, "%02X:%02X:%02X:%02X:%02X:%02X", m[5], m[4], m[3], m[2], m[1], m[0]);
  return String(b);
}

static void wifiStart() {
  netUp = false;
  if (!net.ssid[0]) return;
  allIdle();
  WiFi.setHostname(net.host);
  for (int i = 0; i < 1 && WiFi.status() != WL_CONNECTED; i++) {
    if (net.pass[0]) WiFi.begin(net.ssid, net.pass); else WiFi.begin(net.ssid);
  }
  if (WiFi.status() == WL_CONNECTED) {
    for (int i = 0; i < 20 && ipStr() == "0.0.0.0"; i++) delay(100);
    server.begin();
    udp.begin(3334);
    netUp = true;
    Serial.println("{\"evt\":\"wifi\",\"connected\":true,\"ip\":\"" + ipStr() + "\"}");
  } else {
    Serial.println("{\"evt\":\"wifi\",\"connected\":false}");
  }
}

static String wifiStatus() {
  bool c = WiFi.status() == WL_CONNECTED;
  J j; j.str("ssid", net.ssid).flag("connected", c).str("ip", c ? ipStr() : "").num("rssi", c ? WiFi.RSSI() : 0)
       .str("host", net.host).flag("ap_active", false).flag("ap_fallback", false).flag("mqtt_on", false).flag("mqtt_connected", false)
       .str("mac", macStr());
  return j.done();
}

static String wifiScan() {
  allIdle();
  int n = WiFi.scanNetworks();
  String a = "[";
  for (int i = 0; i < n && i < 20; i++) {
    if (i) a += ",";
    a += "{\"ssid\":" + jstr(WiFi.SSID(i)) + ",\"rssi\":" + String(WiFi.RSSI(i)) + "}";
  }
  a += "]";
  J j; j.raw("networks", a); return j.done();
}

// ---------------------------------------------------------------- commands
static String status() {
  J j;
  j.str("board", "uno").flag("ptest", mode == M_TEST).flag("replay", mode == M_REPLAY)
   .str("run", mode == M_TEST ? "test" : mode == M_REPLAY ? "replay" : "stop")
   .num("steps", nSteps).num("frames", frames).num("loaded", loaded).num("load_n", loadN)
   .num("pattern", pattern).num("param", param).num("mask", mask).num("row", onlyRow)
   .num("bits", bits).num("oeoff", oeOff).num("twidth", tWidth).num("tmode", tMode)
   .flag("oeinv", oeInv).flag("latinv", latInv).flag("datainv", dataInv).num("slow", slow).num("edge", edge);
  return j.done();
}

static String info() {
  bool c = WiFi.status() == WL_CONNECTED;
  J j;
  j.str("fw", FW_VERSION).str("board", "uno").str("chip", "UNO R4 WiFi").str("mode", "diag").flag("power", true)
   .str("ip", c ? ipStr() : "").str("host", net.host).flag("wifi", c)
   .str("pins", "clk=D2 lat=D3 oe=D4 d1=D5 t1=D6 t2=D7 d2=D10 d3=A4 d4=A5 a=D13 b=D11 c=D12")
   .num("max_steps", MAXSTEPS).raw("out_pins", "[]").raw("in_only_pins", "[]").raw("strap_pins", "[]")
   .str("run", mode == M_TEST ? "test" : mode == M_REPLAY ? "replay" : "stop");
  return j.done();
}

static bool setKey(const String& k, int v) {
  if (k == "pattern") pattern = constrain(v, 0, 6);
  else if (k == "param") param = constrain(v, 0, 2000);
  else if (k == "mask") mask = v & 15;
  else if (k == "row") onlyRow = constrain(v, -1, 7);
  else if (k == "bits") bits = constrain(v, 8, MAXSTEPS / 8);
  else if (k == "oeoff") oeOff = constrain(v, 3, 2000);
  else if (k == "twidth") tWidth = constrain(v, 1, 2000);
  else if (k == "tmode") tMode = constrain(v, 0, 2);
  else if (k == "oeinv") oeInv = v != 0;
  else if (k == "latinv") latInv = v != 0;
  else if (k == "datainv") dataInv = v != 0;
  else if (k == "slow") slow = constrain(v, 0, 100000);
  else if (k == "edge") edge = v ? 1 : 0;
  else return false;
  return true;
}

static String nextTok(String& s) {
  s.trim();
  int sp = s.indexOf(' ');
  String t = sp < 0 ? s : s.substring(0, sp);
  s = sp < 0 ? "" : s.substring(sp + 1);
  return t;
}

static int hexVal(const String& t) { return (int)strtol(t.c_str(), nullptr, 16); }

static bool wantWifiConnect = false;

static String handle(String cmd) {
  String c = nextTok(cmd); c.toUpperCase();
  if (c == "PING") { J j; return j.flag("pong", true).done(); }
  if (c == "INFO" || c == "HELLO") return info();
  if (c == "STATUS" || c.length() == 0) return status();
  if (c == "TIME" || c == "MODE" || c == "SAVE") { J j; return j.done(); }
  if (c == "GET") { J j; return j.flag("configured", false).str("board", "uno").done(); }
  if (c == "STOP") { mode = M_STOP; allIdle(); return status(); }
  if (c == "REBOOT") { Serial.println("{\"ok\":true}"); delay(100); NVIC_SystemReset(); }
  if (c == "WIFI") {
    String sub = nextTok(cmd); sub.toUpperCase();
    if (sub == "STATUS" || sub.length() == 0) return wifiStatus();
    if (sub == "SCAN") return wifiScan();
    if (sub == "SSID") { cmd.trim(); cmd.toCharArray(net.ssid, sizeof net.ssid); netSave(); J j; return j.str("ssid", net.ssid).done(); }
    if (sub == "PASS") { cmd.toCharArray(net.pass, sizeof net.pass); netSave(); J j; return j.done(); }
    if (sub == "HOST") { cmd.trim(); if (cmd.length()) { cmd.toCharArray(net.host, sizeof net.host); netSave(); } J j; return j.str("host", net.host).done(); }
    if (sub == "FORGET") { net.ssid[0] = 0; net.pass[0] = 0; netSave(); WiFi.disconnect(); netUp = false; J j; return j.done(); }
    if (sub == "CONNECT") { wantWifiConnect = true; J j; return j.flag("connecting", true).str("ssid", net.ssid).done(); }
    if (sub == "AP") { J j; return j.flag("ap_fallback", false).str("note", "the Uno has no hotspot mode").done(); }
    return err("WIFI STATUS|SCAN|SSID|PASS|CONNECT|FORGET|HOST");
  }
  if (c == "PTEST") {
    String sub = nextTok(cmd); sub.toUpperCase();
    if (sub == "OFF") { mode = M_STOP; allIdle(); return status(); }
    if (sub != "ON" && sub != "SET" && sub.length()) return err("PTEST ON|SET|OFF key=value ...");
    while (cmd.length()) {
      String kv = nextTok(cmd);
      int eq = kv.indexOf('=');
      if (eq < 1) return err("expected key=value, got " + kv);
      String k = kv.substring(0, eq); k.toLowerCase();
      if (!setKey(k, kv.substring(eq + 1).toInt())) return err("unknown key " + k);
    }
    if (sub == "ON") mode = M_TEST;
    if (mode == M_TEST) buildTest();
    return status();
  }
  if (c == "LOAD") {
    int n = nextTok(cmd).toInt();
    if (n < 1 || n > MAXSTEPS) return err("steps must be 1.." + String(MAXSTEPS));
    mode = M_STOP; allIdle();
    loadN = n; loaded = 0;
    J j; return j.num("load", n).done();
  }
  if (c == "W") {
    if (loadN == 0) return err("LOAD first");
    int i = nextTok(cmd).toInt();
    int got = 0;
    while (cmd.length()) {
      String t = nextTok(cmd);
      if (!t.length()) break;
      if (i >= loadN) return err("past the end");
      p1[i] = (uint16_t)hexVal(t); i++; got++;   // raw words; PLAY converts them
    }
    if (i > loaded) loaded = i;
    J j; return j.num("got", got).done();
  }
  if (c == "PLAY") {
    if (loadN == 0 || loaded < loadN) return err("recording not fully loaded (" + String(loaded) + "/" + String(loadN) + ")");
    while (cmd.length()) {
      String kv = nextTok(cmd); int eq = kv.indexOf('=');
      if (eq > 0) { String k = kv.substring(0, eq); k.toLowerCase(); setKey(k, kv.substring(eq + 1).toInt()); }
    }
    for (int i = 0; i < loadN; i++) { uint16_t w = p1[i]; setStep(i, w); }
    nSteps = loadN; loadN = 0;
    mode = M_REPLAY;
    return status();
  }
  return err("unknown command " + c + " (this is the Uno panel firmware: it only drives panel 2 for testing)");
}

// ---------------------------------------------------------------- main
static String line;

static void pollNet() {
  if (!netUp) return;
  // discovery
  int n = udp.parsePacket();
  if (n > 0) {
    char b[32]; int r = udp.read(b, sizeof b - 1); b[r > 0 ? r : 0] = 0;
    if (strncmp(b, "CASINOSIGN?", 11) == 0) {
      J j; String rep = j.str("ip", ipStr()).num("port", 3333).str("name", "Uno panel driver").str("host", net.host).str("fw", FW_VERSION).flag("power", true).done();
      udp.beginPacket(udp.remoteIP(), udp.remotePort()); udp.print(rep); udp.endPacket();
    }
  }
  // TCP: the newest client wins
  WiFiClient nc = server.available();
  if (nc && nc != client) { if (client) client.stop(); client = nc; tcpLine = ""; }
  if (client && client.connected()) {
    while (client.available()) {
      char ch = (char)client.read();
      if (ch == '\r') continue;
      if (ch == '\n') { String r = handle(tcpLine); tcpLine = ""; client.println(r); }
      else if (tcpLine.length() < 400) tcpLine += ch;
    }
  }
}

void setup() {
  const int pins[] = {2, 3, 4, 5, 6, 7, 10, 13, A4, A5, 11, 12};
  for (int p : pins) { pinMode(p, OUTPUT); digitalWrite(p, LOW); }
  allIdle();
  Serial.begin(115200);
  netLoad();
  buildTest();
  mode = M_STOP;
  Serial.println("{\"evt\":\"boot\",\"fw\":\"" FW_VERSION "\",\"board\":\"uno\"}");
  wantWifiConnect = net.ssid[0] != 0;   // joined a few seconds after start, so USB answers straight away
}

void loop() {
  while (Serial.available()) {
    char ch = (char)Serial.read();
    if (ch == '\r') continue;
    if (ch == '\n') { String r = handle(line); line = ""; Serial.println(r); }
    else if (line.length() < 400) line += ch;
  }
  if (wantWifiConnect && millis() > 4000) { wantWifiConnect = false; WiFi.disconnect(); wifiStart(); }
  // The WiFi chip is slow to ask, so only check it every 25 ms (the panel keeps its last row meanwhile).
  if (netUp && millis() - lastNet > 25) { lastNet = millis(); pollNet(); }
  if (mode != M_STOP && nSteps > 0) playOnce();
}
