// ============================================================================
// Panel test mode (firmware 3.4.0): drive the RGB-02 panels' 16-pin input
// directly with simple patterns, to confirm the signal format found with the
// logic analyzer. Everything is adjustable from the app (tab "1c. Panel test").
//
// What the recordings showed (Logan's sign, 2026-11-01):
//   pin 16 clock, data changes on the falling edge and is read on the rising
//   edge, 246 clocks per row, 8 rows (A/B/C on pins 3/4/1).
//   pin 6 latch: idles low, short high pulse at the start of each row.
//   pin 5 OE: goes high at the latch, low again about 150 clocks later.
//   pins 9+13 and 2: idle high, low for a while just after the latch.
//   pins 7, 8, 10, 11: pixel data lines.
// The data shifted during one row is shown after the next latch, so while
// row r is lit we shift the bits for row r+1.
// ============================================================================

struct PanelTest {
  volatile bool run = false;
  volatile bool busy = false;
  // GPIOs (defaults match the wiring guide)
  int clk = 18, lat = 19, oe = 21, a = 32, b = 25, c = 0;
  int d[4] = {23, 22, 17, 26};     // panel pins 7, 8, 10, 11
  int t1 = 16, t2 = 4;             // panel pins 9+13, 2
  // pattern
  int pattern = 1;                 // 0 off, 1 all on, 2 one bit, 3 first N bits, 4 every 8th bit, 5 checker, 6 half on
  int param = 0;                   // bit index (2), count (3), offset (4)
  uint8_t mask = 0x1;              // which data lines carry the pattern (bit 0 = pin 7 ... bit 3 = pin 11)
  int onlyRow = -1;                // -1 all rows, 0..7 only this row address
  // timing
  int bits = 246;                  // clocks per row
  int oeOff = 150;                 // clock where OE goes back low
  int tWidth = 30;                 // clocks the timing pins stay low after the latch
  int tMode = 0;                   // 0 pulse low after latch, 1 always low, 2 always high
  bool oeInv = false, latInv = false, dataInv = false;
  int slow = 0;                    // extra delay per half clock (0 = fastest)
  volatile uint32_t frames = 0;
} pt;

static TaskHandle_t ptTask = nullptr;

static inline void ptDelay() { for (volatile int i = 0; i < pt.slow; i++) {} }

static inline bool ptBit(int k, int row) {
  if (pt.onlyRow >= 0 && row != pt.onlyRow) return false;
  switch (pt.pattern) {
    case 1: return true;
    case 2: return k == pt.param;
    case 3: return k < pt.param;
    case 4: return ((k - pt.param) & 7) == 0 && k >= pt.param;
    case 5: return ((k + row) & 1) == 0;
    case 6: return k < pt.bits / 2;
    default: return false;
  }
}

static void ptPins(bool out) {
  int all[] = {pt.clk, pt.lat, pt.oe, pt.a, pt.b, pt.c, pt.d[0], pt.d[1], pt.d[2], pt.d[3], pt.t1, pt.t2};
  for (int p : all) if (p >= 0) { if (out) { pinMode(p, OUTPUT); digitalWrite(p, LOW); } else pinMode(p, INPUT); }
  if (out) {
    pinSet(pt.oe, pt.oeInv);            // LEDs off
    pinSet(pt.lat, pt.latInv);          // latch idle
    pinSet(pt.t1, true); pinSet(pt.t2, true);
  }
}

static void ptFrame() {
  for (int row = 0; row < 8; row++) {
    int next = (row + 1) & 7;
    for (int k = 0; k < pt.bits; k++) {
      if (k == 0) {
        pinSet(pt.oe, pt.oeInv);                        // blank while switching
        pinSet(pt.lat, !pt.latInv);                     // latch pulse
        pinSet(pt.a, row & 1); pinSet(pt.b, (row >> 1) & 1); pinSet(pt.c, (row >> 2) & 1);
        bool tLow = pt.tMode != 2;
        pinSet(pt.t1, !tLow); pinSet(pt.t2, !tLow);
      } else if (k == 2) {
        pinSet(pt.lat, pt.latInv);
        pinSet(pt.oe, !pt.oeInv);                       // LEDs on
      }
      if (k == pt.tWidth && pt.tMode == 0) { pinSet(pt.t1, true); pinSet(pt.t2, true); }
      if (k == pt.oeOff) pinSet(pt.oe, pt.oeInv);
      bool v = ptBit(k, next) ^ pt.dataInv;
      for (int i = 0; i < 4; i++) if (pt.d[i] >= 0) pinSet(pt.d[i], ((pt.mask >> i) & 1) ? v : pt.dataInv);
      ptDelay();
      pinSet(pt.clk, true);                             // panels read on the rising edge
      ptDelay();
      pinSet(pt.clk, false);
    }
  }
  pt.frames++;
}

static void ptLoop(void*) {
  uint32_t n = 0;
  while (true) {
    if (!pt.run) { pt.busy = false; vTaskDelay(20); continue; }
    pt.busy = true;
    ptFrame();
    if (++n % 8 == 0) vTaskDelay(1);                  // let WiFi and the app commands run
  }
}

static bool ptKey(const String& k, int v) {
  if (k == "pattern") pt.pattern = constrain(v, 0, 6);
  else if (k == "param") pt.param = constrain(v, 0, 2000);
  else if (k == "mask") pt.mask = v & 15;
  else if (k == "row") pt.onlyRow = constrain(v, -1, 7);
  else if (k == "bits") pt.bits = constrain(v, 8, 2000);
  else if (k == "oeoff") pt.oeOff = constrain(v, 3, 2000);
  else if (k == "twidth") pt.tWidth = constrain(v, 1, 2000);
  else if (k == "tmode") pt.tMode = constrain(v, 0, 2);
  else if (k == "oeinv") pt.oeInv = v != 0;
  else if (k == "latinv") pt.latInv = v != 0;
  else if (k == "datainv") pt.dataInv = v != 0;
  else if (k == "slow") pt.slow = constrain(v, 0, 10000);
  else return false;
  return true;
}

static bool ptPinKey(const String& k, int v) {
  int* f = nullptr;
  if (k == "clk") f = &pt.clk; else if (k == "lat") f = &pt.lat; else if (k == "oe") f = &pt.oe;
  else if (k == "a") f = &pt.a; else if (k == "b") f = &pt.b; else if (k == "c") f = &pt.c;
  else if (k == "d1") f = &pt.d[0]; else if (k == "d2") f = &pt.d[1]; else if (k == "d3") f = &pt.d[2]; else if (k == "d4") f = &pt.d[3];
  else if (k == "t1") f = &pt.t1; else if (k == "t2") f = &pt.t2;
  if (!f) return false;
  if (v >= 0 && !isOutPin(v)) return false;
  *f = v;
  return true;
}

static String ptJson() {
  J j;
  j.flag("ptest", pt.run).num("pattern", pt.pattern).num("param", pt.param).num("mask", pt.mask).num("row", pt.onlyRow)
   .num("bits", pt.bits).num("oeoff", pt.oeOff).num("twidth", pt.tWidth).num("tmode", pt.tMode)
   .flag("oeinv", pt.oeInv).flag("latinv", pt.latInv).flag("datainv", pt.dataInv).num("slow", pt.slow)
   .num("frames", pt.frames);
  return j.done();
}

static void ptStop() {
  if (!pt.run) return;
  pt.run = false;
  for (int i = 0; i < 50 && pt.busy; i++) delay(2);
  ptPins(false);
}

// PTEST                     status
// PTEST ON [key=val ...]    stop the normal scan and start test patterns
// PTEST SET key=val ...     change settings while running
// PTEST OFF                 stop and release the pins
String panelTestCommand(String rest) {
  String sub = nextToken(rest); sub.toUpperCase();
  if (sub == "OFF") { ptStop(); return ptJson(); }
  if (sub != "ON" && sub != "SET" && sub.length()) return err("PTEST ON|SET|OFF key=value ...");
  bool pinChange = false;
  while (rest.length()) {
    String kv = nextToken(rest);
    int eq = kv.indexOf('=');
    if (eq < 1) return err("expected key=value, got " + kv);
    String k = kv.substring(0, eq); k.toLowerCase();
    int v = kv.substring(eq + 1).toInt();
    if (ptKey(k, v)) continue;
    bool wasRun = pt.run;
    if (wasRun) ptStop();
    if (!ptPinKey(k, v)) { if (wasRun) { ptPins(true); pt.run = true; } return err("unknown key or pin not usable: " + kv); }
    pinChange = true;
    if (wasRun) { ptPins(true); pt.run = true; }
  }
  if (sub == "ON" && !pt.run) {
    stopScan(); probeAllOff(); releaseSignPins();
    runMode = RM_DIAG; uiDirty = true;
    ptPins(true);
    if (!ptTask) xTaskCreatePinnedToCore(ptLoop, "ptest", 4096, nullptr, 1, &ptTask, 1);
    pt.run = true;
  }
  (void)pinChange;
  return ptJson();
}
