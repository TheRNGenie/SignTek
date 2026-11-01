// content.ino -- what the sign shows: playlist, widgets, animations, Fight Night,
// startup/shutdown animations, power and the overnight schedule.

// ============================================================================
// Animation storage (LittleFS): /a/<name>.anm
//   "CSA1" + uint16 frames + uint16 0, then per frame: uint16 delay_ms + 192 bytes
// ============================================================================
bool fsOk = false;
void installBuiltinAnims() {
  // Copy the Corvette launch from flash into storage once, so it can be played,
  // replaced or deleted like any uploaded animation.
  if (!fsOk || prefs.getBool("corv_inst", false)) return;
  File f = LittleFS.open("/a/corvette.anm", "w");
  if (!f) return;
  uint8_t h[8] = {'C', 'S', 'A', '1', CORVETTE_FRAME_COUNT & 255, CORVETTE_FRAME_COUNT >> 8, 0, 0};
  f.write(h, 8);
  uint8_t d[2] = {CORVETTE_FRAME_MS & 255, CORVETTE_FRAME_MS >> 8};
  uint8_t buf[FB_BYTES];
  for (int i = 0; i < CORVETTE_FRAME_COUNT; i++) {
    memcpy_P(buf, CORVETTE_FRAMES[i], FB_BYTES);
    f.write(d, 2); f.write(buf, FB_BYTES);
  }
  f.close();
  prefs.putBool("corv_inst", true);
}

void fsBegin() { fsOk = LittleFS.begin(true); if (fsOk && !LittleFS.exists("/a")) LittleFS.mkdir("/a"); installBuiltinAnims(); }
long fsFree() { return fsOk ? (long)(LittleFS.totalBytes() - LittleFS.usedBytes()) : 0; }

bool validName(const String& n) {
  if (n.length() == 0 || n.length() > 24) return false;
  for (size_t i = 0; i < n.length(); i++) { char c = n[i]; if (!(isalnum(c) || c == '_' || c == '-')) return false; }
  return true;
}
String animPath(const String& n) { return "/a/" + n + ".anm"; }
bool animExists(const String& n) { return fsOk && validName(n) && LittleFS.exists(animPath(n)); }

struct AnimReader {
  File f; String name; uint16_t frames = 0;
  bool open(const String& n) {
    if (name == n && f) return true;
    close();
    if (!animExists(n)) return false;
    f = LittleFS.open(animPath(n), "r");
    uint8_t h[8];
    if (!f || f.read(h, 8) != 8 || memcmp(h, "CSA1", 4) != 0) { close(); return false; }
    frames = h[4] | (h[5] << 8); name = n;
    return frames > 0;
  }
  bool frame(int i, uint8_t* buf, uint16_t& delayMs) {
    if (!f || i < 0 || i >= frames) return false;
    f.seek(8 + (size_t)i * (2 + FB_BYTES));
    uint8_t d[2];
    if (f.read(d, 2) != 2 || f.read(buf, FB_BYTES) != FB_BYTES) return false;
    delayMs = max<uint16_t>(20, d[0] | (d[1] << 8));
    return true;
  }
  void close() { if (f) f.close(); name = ""; frames = 0; }
};
AnimReader animR;

// Upload state for ANIM BEGIN / FRAME / END
File animUp; String animUpName; int animUpFrames = 0, animUpNext = 0;

// ============================================================================
// Playlist
// ============================================================================
JsonDocument plDoc;          // {"items":[...]} saved in /playlist.json
JsonDocument ovDoc;          // temporary override list (SHOW), saved in prefs "ov"
bool overrideOn = false;
int itemIdx = 0;
uint32_t itemStart = 0;
RenderState rs;

// Startup / shutdown animation in progress
bool inTransition = false;
String transName;

bool schedLit = true;        // last schedule state
uint32_t lastFrameMs = 0;

const char* DEFAULT_PLAYLIST =
  "{\"items\":[{\"t\":\"anim\",\"name\":\"corvette\",\"d\":0},{\"t\":\"text\",\"text\":\"HELLO FROM THE ESP32\",\"fx\":\"scroll\",\"d\":0},"
  "{\"t\":\"clock\",\"d\":8}]}";

JsonArray activeItems() { return overrideOn ? ovDoc.as<JsonArray>() : plDoc["items"].as<JsonArray>(); }

JsonObject currentItem() {
  JsonArray a = activeItems();
  if (a.isNull() || a.size() == 0) return JsonObject();
  if (itemIdx >= (int)a.size()) itemIdx = 0;
  return a[itemIdx].as<JsonObject>();
}

void startItem(int i) {
  JsonArray a = activeItems();
  itemIdx = (a.isNull() || a.size() == 0) ? 0 : (i % a.size());
  itemStart = millis();
  rs = RenderState();
  rs.t0 = itemStart;
  JsonObject it = currentItem();
  if (!it.isNull() && String((const char*)(it["t"] | "")) == "jackpot") {
    rs.jackpot = conf("jackpot_start").toDouble() + conf("jackpot_rate").toDouble() * (millis() / 1000.0);
  }
  uiDirty = true;
}

void contentNext() { startItem(itemIdx + 1); }

bool loadPlaylist() {
  plDoc.clear();
  if (fsOk && LittleFS.exists("/playlist.json")) {
    File f = LittleFS.open("/playlist.json", "r");
    DeserializationError e = deserializeJson(plDoc, f);
    f.close();
    if (!e && plDoc["items"].is<JsonArray>()) return true;
  }
  deserializeJson(plDoc, DEFAULT_PLAYLIST);
  return false;
}

void setOverride(const String& json) {
  if (json.length() == 0) { overrideOn = false; ovDoc.clear(); prefs.putString("ov", ""); }
  else { overrideOn = true; prefs.putString("ov", json.length() < 3000 ? json : String("")); }
  startItem(0);
}

// SHOW: accepts one item object or an array of items.
String showJson(const String& json) {
  JsonDocument d;
  if (deserializeJson(d, json)) return "bad JSON";
  ovDoc.clear();
  if (d.is<JsonArray>()) ovDoc.set(d);
  else if (d.is<JsonObject>()) { JsonArray a = ovDoc.to<JsonArray>(); a.add(d.as<JsonObject>()); }
  else return "SHOW needs an item {...} or a list [...]";
  String saved; serializeJson(ovDoc, saved);
  setOverride(saved);
  return "";
}

String contentDescribe() {
  if (!signPower) return "off";
  if (inTransition) return "animation " + transName;
  if (!schedLit) return "off (schedule)";
  JsonObject it = currentItem();
  if (it.isNull()) return "nothing";
  String t = it["t"] | "?";
  if (t == "text") t += String(": ") + (const char*)(it["text"] | "");
  if (t == "anim") t += String(": ") + (const char*)(it["name"] | "");
  if (t == "pattern") t += String(": ") + (const char*)(it["p"] | "");
  return (overrideOn ? "" : "playlist ") + t;
}

// ============================================================================
// Power + startup / shutdown animations
// ============================================================================
void startTransition(const String& anim) {
  if (anim.length() && animExists(anim)) { inTransition = true; transName = anim; rs = RenderState(); rs.t0 = millis(); animR.close(); }
  else inTransition = false;
}

void powerSet(bool on) {
  if (on == signPower) return;
  signPower = on;
  prefs.putBool("power", on);
  startTransition(on ? conf("anim_on") : conf("anim_off"));
  if (on) startItem(itemIdx);
  uiDirty = true;
  mqttPublishState();
  sendEvent(String("{\"evt\":\"power\",\"on\":") + (on ? "true" : "false") + "}");
}

bool scheduleLit() {
  if (!confBool("sched_on") || !timeValid()) return true;
  auto mins = [](const String& s) { return s.substring(0, 2).toInt() * 60 + s.substring(3, 5).toInt(); };
  int a = mins(conf("sched_start")), b = mins(conf("sched_end"));
  time_t t = time(nullptr); struct tm lt; localtime_r(&t, &lt);
  int now = lt.tm_hour * 60 + lt.tm_min;
  if (a == b) return true;
  return a < b ? (now >= a && now < b) : (now >= a || now < b);
}

// ============================================================================
// Drawing helpers
// ============================================================================
constexpr uint16_t ON = TFT_WHITE;   // 1-bit sprite keeps bit 0 of the colour: odd = LED on

int textSpeed() { return constrain(conf("text_speed").toInt(), 5, 500); }

// One line of big (16 px) text. Scrolls if asked or if it doesn't fit. Returns true when a scroll pass ends.
bool bigText(const String& s, bool forceScroll, int32_t& x) {
  canvas.setFont(&fonts::Font2);
  canvas.setTextColor(ON);
  canvas.setTextDatum(lgfx::middle_left);
  int w = canvas.textWidth(s);
  if (!forceScroll && w <= SIGN_W) { canvas.drawString(s, (SIGN_W - w) / 2, SIGN_H / 2); return millis() - rs.t0 > 10000; }
  canvas.drawString(s, x, SIGN_H / 2);
  bool pass = false;
  if (millis() - rs.lastStep >= (uint32_t)textSpeed()) {
    rs.lastStep = millis();
    if (--x < -w) { x = SIGN_W; pass = true; }
  }
  return pass;
}

// A row of small (8 px) text at y = 0 or 8. Centered if it fits, else scrolls.
bool smallRow(const String& s, int y, int32_t& x) {
  canvas.setFont(&fonts::Font0);
  canvas.setTextColor(ON);
  canvas.setTextDatum(lgfx::top_left);
  int w = canvas.textWidth(s);
  if (w <= SIGN_W) { canvas.drawString(s, (SIGN_W - w) / 2, y); return millis() - rs.t0 > 10000; }
  canvas.drawString(s, x, y);
  bool pass = false;
  static uint32_t lastStepRow[2] = {0, 0};
  int r = y ? 1 : 0;
  if (millis() - lastStepRow[r] >= (uint32_t)textSpeed()) { lastStepRow[r] = millis(); if (--x < -w) { x = SIGN_W; pass = true; } }
  return pass;
}

String money(double v) {
  bool neg = v < 0; v = fabs(v);
  long whole = (long)(v + 0.5);
  String digits = String(whole), out;
  for (int i = 0; i < (int)digits.length(); i++) { if (i && (digits.length() - i) % 3 == 0) out += ','; out += digits[i]; }
  return String(neg ? "-$" : "$") + out;
}

String two(int v) { return v < 10 ? "0" + String(v) : String(v); }

void drawFrameBytes(const uint8_t* b) {
  for (int y = 0; y < SIGN_H; y++)
    for (int x = 0; x < SIGN_W; x++)
      if (b[y * ROW_BYTES + (x >> 3)] & (0x80 >> (x & 7))) canvas.drawPixel(x, y, ON);
}

// Plays an animation; returns true after each full loop.
bool drawAnim(const String& name) {
  static uint8_t buf[FB_BYTES];
  static uint16_t delayMs = 100;
  if (!animR.open(name)) { smallRow("NO ANIM", 0, rs.x); smallRow(name, 8, rs.x2); return millis() - rs.t0 > 3000; }
  bool loopDone = false;
  if (rs.frameAt == 0 || millis() - rs.frameAt >= delayMs) {
    if (rs.frameAt != 0) { rs.frame++; if (rs.frame >= animR.frames) { rs.frame = 0; loopDone = true; } }
    rs.frameAt = millis();
    if (!animR.frame(rs.frame, buf, delayMs)) memset(buf, 0, FB_BYTES);
  }
  drawFrameBytes(buf);
  return loopDone;
}

// ============================================================================
// Item renderers. Each returns true when the item has "finished one pass"
// (used when the item's duration is 0 = automatic).
// ============================================================================
bool renderText(JsonObject it) {
  String s = it["text"] | "";
  String fx = it["fx"] | "scroll";
  uint32_t el = millis() - rs.t0;
  if (fx == "static") return bigText(s, false, rs.x);
  if (fx == "blink") { if ((el / 500) % 2 == 0) bigText(s, false, rs.x); return el > 6000; }
  if (fx == "wipe") {
    bigText(s, false, rs.x);
    int reveal = el * SIGN_W / 1000;            // reveal over one second
    if (reveal < SIGN_W) canvas.fillRect(reveal, 0, SIGN_W - reveal, SIGN_H, TFT_BLACK);
    return el > 6000;
  }
  return bigText(s, true, rs.x);
}

bool renderClock(JsonObject it) {
  if (!timeValid()) { bigText("--:--", false, rs.x); return millis() - rs.t0 > 5000; }
  time_t t = time(nullptr); struct tm lt; localtime_r(&t, &lt);
  bool h24 = String((const char*)(it["fmt"] | "12")) == "24";
  bool sec = it["sec"] | false;
  int h = lt.tm_hour;
  String ampm = h < 12 ? "AM" : "PM";
  if (!h24) { h %= 12; if (h == 0) h = 12; }
  bool colon = (millis() / 500) % 2 == 0 || sec;
  String tm = String(h) + (colon ? ":" : " ") + two(lt.tm_min) + (sec ? ":" + two(lt.tm_sec) : "");
  if (it["date"] | false) {
    char d[20]; strftime(d, sizeof(d), "%a %d %b", &lt); String ds = d; ds.toUpperCase();
    smallRow(ds, 0, rs.x);
    smallRow(tm + (h24 ? "" : " " + ampm), 8, rs.x2);
  } else {
    bigText(tm + (h24 ? "" : " " + ampm), false, rs.x);
  }
  return millis() - rs.t0 > 8000;
}

String liveGet(bool weather) {
  String s;
  if (xSemaphoreTake(liveMutex, pdMS_TO_TICKS(5)) == pdTRUE) { s = weather ? live.weather : live.ticker; xSemaphoreGive(liveMutex); }
  return s;
}

bool renderWeather(JsonObject) {
  String w = liveGet(true);
  smallRow(conf("weather_label"), 0, rs.x);
  smallRow(w.length() ? w : String("NO DATA YET"), 8, rs.x2);
  return millis() - rs.t0 > 8000;
}

bool renderTicker(JsonObject) {
  String t = liveGet(false);
  return bigText(t.length() ? t : String("NO TICKER DATA YET"), true, rs.x);
}

bool renderCountdown(JsonObject it) {
  String label = it["label"] | "COUNTDOWN";
  long at = it["at"] | 0L;
  smallRow(label, 0, rs.x);
  if (!timeValid()) { smallRow("NO TIME YET", 8, rs.x2); return millis() - rs.t0 > 5000; }
  long left = at - (long)time(nullptr);
  if (left <= 0) { if ((millis() / 400) % 2) smallRow("NOW!", 8, rs.x2); }
  else {
    long d = left / 86400, h = (left / 3600) % 24, m = (left / 60) % 60, s = left % 60;
    smallRow((d ? String(d) + "D " : String("")) + two(h) + ":" + two(m) + ":" + two(s), 8, rs.x2);
  }
  return millis() - rs.t0 > 8000;
}

bool renderJackpot(JsonObject) {
  double rate = conf("jackpot_rate").toDouble();
  rs.jackpot += rate * 0.02 * (0.5 + (esp_random() % 100) / 100.0);   // ~50 fps, jittery like a real meter
  uint32_t el = millis() - rs.t0;
  if (el % 8000 > 6500) { if ((el / 150) % 2) bigText("JACKPOT!", false, rs.x); }
  else bigText(money(rs.jackpot), false, rs.x);
  // chase lights on the top and bottom rows
  int off = (millis() / 80) % 4;
  for (int x = off; x < SIGN_W; x += 4) { canvas.drawPixel(x, 0, ON); canvas.drawPixel(SIGN_W - 1 - x, SIGN_H - 1, ON); }
  return el > 10000;
}

String fmtClock(int s) { if (s < 0) s = 0; return String(s / 60) + ":" + two(s % 60); }

bool renderFight(JsonObject) {
  int left = fight.clockSec;
  if (fight.running) left -= (millis() - fight.updatedMs) / 1000;
  String top;
  if (fight.a.length() == 0) top = "FIGHT NIGHT";
  else if (fight.status == "live") top = "RD " + String(fight.round) + "  " + fmtClock(left);
  else if (fight.status == "between") top = "END OF RD " + String(fight.round);
  else if (fight.status == "final") top = fight.result.length() ? fight.result : String("FINAL");
  else top = "UP NEXT";
  String vs = fight.a.length() ? fight.a + " VS " + fight.b : fight.event;
  if (vs.length() == 0) vs = "WAITING FOR DATA";
  vs.toUpperCase(); top.toUpperCase();
  bool p1 = smallRow(top, 0, rs.x);
  bool p2 = smallRow(vs, 8, rs.x2);
  (void)p1;
  return p2 || millis() - rs.t0 > 15000;
}

bool renderGamble(JsonObject) {
  double net = gamble.won - gamble.bet;
  uint32_t el = millis() - rs.t0;
  String top = (el / 3000) % 2 ? gamble.label : "BET " + money(gamble.bet) + " WON " + money(gamble.won);
  String bot = "NET " + String(net >= 0 ? "+" : "") + money(net);
  top.toUpperCase();
  smallRow(top, 0, rs.x);
  if (net >= 0 || (millis() / 600) % 2) smallRow(bot, 8, rs.x2);
  return el > 10000;
}

uint32_t stepCount = 0;
bool renderPattern(JsonObject it) {
  String p = it["p"] | "corners";
  uint32_t el = millis() - rs.t0;
  if (millis() - rs.lastStep > 150) { rs.lastStep = millis(); stepCount++; }
  if (p == "allon") {
    if (el < 15000) canvas.fillScreen(ON);      // safety: full current only for 15 s
  } else if (p == "walkrow") canvas.drawFastHLine(0, stepCount % SIGN_H, SIGN_W, ON);
  else if (p == "walkcol") canvas.drawFastVLine(stepCount % SIGN_W, 0, SIGN_H, ON);
  else if (p == "corners") {
    canvas.drawPixel(0, 0, ON); canvas.drawPixel(SIGN_W - 1, SIGN_H - 1, ON);
    canvas.drawFastHLine(30, 8, 30, ON);
    canvas.drawLine(60, 8, 54, 2, ON); canvas.drawLine(60, 8, 54, 14, ON);
  }
  return el > 10000;
}

bool renderItem(JsonObject it) {
  String t = it["t"] | "";
  if (t == "text")      return renderText(it);
  if (t == "clock")     return renderClock(it);
  if (t == "weather")   return renderWeather(it);
  if (t == "ticker")    return renderTicker(it);
  if (t == "countdown") return renderCountdown(it);
  if (t == "jackpot")   return renderJackpot(it);
  if (t == "anim")      return drawAnim(it["name"] | "");
  if (t == "fight")     return renderFight(it);
  if (t == "gamble")    return renderGamble(it);
  if (t == "pattern")   return renderPattern(it);
  return millis() - rs.t0 > 5000;   // "off" or unknown: blank
}

// ============================================================================
void contentBegin() {
  loadPlaylist();
  String ov = prefs.getString("ov", "");
  if (ov.length() && !deserializeJson(ovDoc, ov) && ovDoc.is<JsonArray>()) overrideOn = true;
  startItem(0);
  if (signPower) startTransition(conf("anim_on"));
}

void contentLoop() {
  if (runMode != RM_RUN || otaInProgress) return;
  if (millis() - lastFrameMs < 20) return;
  lastFrameMs = millis();

  bool lit = scheduleLit();
  if (lit != schedLit) {
    schedLit = lit;
    if (signPower) startTransition(lit ? conf("anim_on") : conf("anim_off"));
    uiDirty = true;
  }

  canvas.fillScreen(TFT_BLACK);
  if (inTransition) {
    if (drawAnim(transName)) { inTransition = false; animR.close(); startItem(itemIdx); }
  } else if (signPower && schedLit) {
    JsonObject it = currentItem();
    if (!it.isNull()) {
      uint32_t d = it["d"] | 0;
      bool pass = renderItem(it);
      bool multi = activeItems().size() > 1;
      if (multi && ((d > 0 && millis() - itemStart >= d * 1000UL) || (d == 0 && pass))) { animR.close(); contentNext(); }
    }
  }
  pushCanvasToSign();
}

// ============================================================================
// Commands
// ============================================================================
String contentCommand(const String& cmd, String rest) {
  rest.trim();

  if (cmd == "TEXT") {
    if (rest.length() > 200) return err("text too long (max 200)");
    JsonDocument d; JsonObject o = d.to<JsonObject>();
    o["t"] = "text"; o["text"] = rest; o["fx"] = "scroll"; o["d"] = 0;
    String j; serializeJson(d, j); showJson(j);
    return J().str("text", rest).done();
  }

  if (cmd == "SPEED") {
    int v = rest.toInt();
    if (v < 5 || v > 500) return err("SPEED <ms per pixel step 5..500>");
    confSet("text_speed", String(v));
    return J().num("speed", v).done();
  }

  if (cmd == "PATTERN") {
    String p = rest; p.toLowerCase();
    if (p == "text") { setOverride(""); return J().str("pattern", "playlist").done(); }
    if (p == "bitmap") { showJson("{\"t\":\"anim\",\"name\":\"_bitmap\"}"); return J().str("pattern", "bitmap").done(); }
    if (p == "off") { showJson("{\"t\":\"off\"}"); return J().str("pattern", "off").done(); }
    if (p == "allon" || p == "walkrow" || p == "walkcol" || p == "corners") {
      showJson("{\"t\":\"pattern\",\"p\":\"" + p + "\"}");
      return J().str("pattern", p).flag("scanning", scanning).done();
    }
    return err("patterns: allon walkrow walkcol corners text bitmap off");
  }

  if (cmd == "BITMAP") {
    uint8_t b[FB_BYTES];
    if (!parseHexFrame(rest, b)) return err("BITMAP needs exactly 384 hex characters");
    if (!fsOk) return err("storage not available");
    animR.close();
    File f = LittleFS.open(animPath("_bitmap"), "w");
    uint8_t h[8] = {'C', 'S', 'A', '1', 1, 0, 0, 0}; uint8_t d[2] = {0xE8, 0x03};
    f.write(h, 8); f.write(d, 2); f.write(b, FB_BYTES); f.close();
    showJson("{\"t\":\"anim\",\"name\":\"_bitmap\"}");
    return J().str("pattern", "bitmap").done();
  }

  if (cmd == "SHOW") {
    String e = showJson(rest);
    if (e.length()) return err(e);
    return J().str("showing", contentDescribe()).done();
  }

  if (cmd == "PLAY") { setOverride(""); return J().str("showing", contentDescribe()).done(); }
  if (cmd == "NEXT") { contentNext(); return J().str("showing", contentDescribe()).done(); }

  if (cmd == "PLAYLIST") {
    String sub = nextToken(rest); sub.toUpperCase();
    if (sub == "GET" || sub == "") {
      String p; serializeJson(plDoc, p);
      return J().raw("playlist", p).num("index", itemIdx).flag("override", overrideOn).str("showing", contentDescribe()).done();
    }
    if (sub == "SET") {
      JsonDocument d;
      if (deserializeJson(d, rest) || !d["items"].is<JsonArray>()) return err("PLAYLIST SET needs {\"items\":[...]}");
      if (!fsOk) return err("storage not available");
      File f = LittleFS.open("/playlist.json", "w"); serializeJson(d, f); f.close();
      loadPlaylist();
      if (!overrideOn) startItem(0);
      return J().num("items", plDoc["items"].size()).done();
    }
    return err("PLAYLIST GET or PLAYLIST SET <json>");
  }

  if (cmd == "ANIM") {
    String sub = nextToken(rest); sub.toUpperCase();
    if (!fsOk) return err("storage not available");
    if (sub == "BEGIN") {
      String name = nextToken(rest); int n = nextToken(rest).toInt();
      if (!validName(name)) return err("name: 1-24 letters, digits, _ or -");
      if (n < 1 || n > 600) return err("frames must be 1..600");
      long need = 8 + (long)n * (2 + FB_BYTES);
      long have = fsFree() + (animExists(name) ? (long)LittleFS.open(animPath(name), "r").size() : 0);
      if (need > have - 4096) return err("not enough storage: need " + String(need / 1024) + " KB, free " + String(fsFree() / 1024) + " KB");
      if (animR.name == name) animR.close();
      if (animUp) animUp.close();
      animUp = LittleFS.open("/a/_upload.tmp", "w");
      uint8_t h[8] = {'C', 'S', 'A', '1', (uint8_t)(n & 255), (uint8_t)(n >> 8), 0, 0};
      animUp.write(h, 8);
      animUpName = name; animUpFrames = n; animUpNext = 0;
      return J().str("name", name).num("frames", n).done();
    }
    if (sub == "FRAME") {
      int idx = nextToken(rest).toInt(); int dly = nextToken(rest).toInt(); rest.trim();
      if (!animUp) return err("ANIM BEGIN first");
      if (idx != animUpNext) return err("expected frame " + String(animUpNext));
      uint8_t b[FB_BYTES];
      if (!parseHexFrame(rest, b)) return err("frame needs 384 hex characters");
      dly = constrain(dly, 20, 60000);
      uint8_t d[2] = {(uint8_t)(dly & 255), (uint8_t)(dly >> 8)};
      animUp.write(d, 2); animUp.write(b, FB_BYTES);
      animUpNext++;
      return J().num("frame", idx).done();
    }
    if (sub == "END") {
      if (!animUp) return err("ANIM BEGIN first");
      animUp.close();
      if (animUpNext != animUpFrames) { LittleFS.remove("/a/_upload.tmp"); return err("got " + String(animUpNext) + " of " + String(animUpFrames) + " frames, discarded"); }
      if (animR.name == animUpName) animR.close();
      LittleFS.remove(animPath(animUpName));
      LittleFS.rename("/a/_upload.tmp", animPath(animUpName));
      return J().str("saved", animUpName).num("frames", animUpFrames).num("fs_free", fsFree()).done();
    }
    if (sub == "LIST" || sub == "") {
      String a = "[";
      File dir = LittleFS.open("/a");
      bool first = true;
      for (File f = dir.openNextFile(); f; f = dir.openNextFile()) {
        String n = f.name(); int sl = n.lastIndexOf('/'); if (sl >= 0) n = n.substring(sl + 1);
        if (!n.endsWith(".anm")) continue;
        n = n.substring(0, n.length() - 4);
        if (!first) a += ','; first = false;
        a += "{\"name\":\"" + jsonEsc(n) + "\",\"frames\":" + String((f.size() - 8) / (2 + FB_BYTES)) + ",\"bytes\":" + String(f.size()) + "}";
      }
      return J().raw("anims", a + "]").num("fs_free", fsFree()).num("fs_total", LittleFS.totalBytes())
                .str("anim_on", conf("anim_on")).str("anim_off", conf("anim_off")).done();
    }
    if (sub == "DEL") {
      String name = nextToken(rest);
      if (!animExists(name)) return err("no animation called " + name);
      if (animR.name == name) animR.close();
      LittleFS.remove(animPath(name));
      return J().str("deleted", name).num("fs_free", fsFree()).done();
    }
    if (sub == "GET") {
      // ANIM GET <name> <frame>: read one frame back (for the app's editor)
      String name = nextToken(rest); int idx = nextToken(rest).toInt();
      AnimReader r; uint8_t b[FB_BYTES]; uint16_t dly;
      if (!r.open(name) || !r.frame(idx, b, dly)) { r.close(); return err("no such frame"); }
      int frames = r.frames; r.close();
      static const char* hx = "0123456789abcdef";
      String s; for (int i = 0; i < FB_BYTES; i++) { s += hx[b[i] >> 4]; s += hx[b[i] & 15]; }
      return J().str("name", name).num("frame", idx).num("frames", frames).num("delay", dly).str("hex", s).done();
    }
    if (sub == "PLAY") {
      String name = nextToken(rest);
      if (!animExists(name)) return err("no animation called " + name);
      showJson("{\"t\":\"anim\",\"name\":\"" + name + "\"}");
      return J().str("showing", contentDescribe()).done();
    }
    return err("ANIM BEGIN|FRAME|END|LIST|DEL|GET|PLAY");
  }

  if (cmd == "FIGHT") {
    String sub = nextToken(rest); sub.toUpperCase();
    if (sub == "SET") {
      JsonDocument d;
      if (deserializeJson(d, rest)) return err("FIGHT SET needs JSON");
      if (d["a"].is<const char*>()) fight.a = (const char*)d["a"];
      if (d["b"].is<const char*>()) fight.b = (const char*)d["b"];
      if (d["event"].is<const char*>()) fight.event = (const char*)d["event"];
      if (d["status"].is<const char*>()) fight.status = (const char*)d["status"];
      if (d["result"].is<const char*>()) fight.result = (const char*)d["result"];
      if (d["round"].is<int>()) fight.round = d["round"];
      if (d["rounds"].is<int>()) fight.rounds = d["rounds"];
      if (d["clock"].is<int>()) { fight.clockSec = d["clock"]; fight.updatedMs = millis(); }
      if (d["running"].is<bool>()) {
        bool r = d["running"];
        if (!r && fight.running) fight.clockSec -= (millis() - fight.updatedMs) / 1000;   // freeze where it is
        fight.running = r; fight.updatedMs = millis();
      }
      return J().str("a", fight.a).str("b", fight.b).num("round", fight.round).str("status", fight.status).done();
    }
    if (sub == "CLEAR") { fight = FightData(); return J().str("fight", "cleared").done(); }
    int left = fight.clockSec - (fight.running ? (millis() - fight.updatedMs) / 1000 : 0);
    return J().str("a", fight.a).str("b", fight.b).str("event", fight.event).str("status", fight.status)
              .str("result", fight.result).num("round", fight.round).num("clock", left).flag("running", fight.running).done();
  }

  if (cmd == "GAMBLE") {
    String sub = nextToken(rest); sub.toUpperCase();
    if (sub == "SET") { gamble.bet = nextToken(rest).toFloat(); gamble.won = nextToken(rest).toFloat(); rest.trim(); if (rest.length()) gamble.label = rest; }
    else if (sub == "ADD") {
      String what = nextToken(rest); what.toUpperCase(); float v = nextToken(rest).toFloat();
      if (what == "BET") gamble.bet += v; else if (what == "WON") gamble.won += v; else return err("GAMBLE ADD BET|WON <amount>");
    }
    else if (sub == "RESET") { gamble.bet = 0; gamble.won = 0; }
    else if (sub.length() && sub != "GET") return err("GAMBLE SET <bet> <won> [label] | ADD BET|WON <amount> | RESET | GET");
    if (sub.length() && sub != "GET") { prefs.putFloat("g_bet", gamble.bet); prefs.putFloat("g_won", gamble.won); prefs.putString("g_label", gamble.label); }
    return J().dbl("bet", gamble.bet).dbl("won", gamble.won).dbl("net", gamble.won - gamble.bet).str("label", gamble.label).done();
  }

  if (cmd == "LIVE") {
    String sub = nextToken(rest); sub.toUpperCase();
    if (sub == "REFRESH") liveRefreshNow();
    String w, t; uint32_t wa = 0, ta = 0;
    if (xSemaphoreTake(liveMutex, pdMS_TO_TICKS(100)) == pdTRUE) { w = live.weather; t = live.ticker; wa = live.weatherAt; ta = live.tickerAt; xSemaphoreGive(liveMutex); }
    return J().str("weather", w).str("ticker", t)
              .num("weather_age_s", wa ? (millis() - wa) / 1000 : -1).num("ticker_age_s", ta ? (millis() - ta) / 1000 : -1).done();
  }

  return "";
}
