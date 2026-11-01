// netsvc.ino -- WiFi, hotspot, TCP command port, UDP discovery, web page,
// WiFi firmware updates (OTA), MQTT / Home Assistant, time, and the background
// task that fetches weather, stocks and crypto.

#include <WiFiClientSecure.h>
#include <HTTPClient.h>
#include <Update.h>
#include <ArduinoOTA.h>
#include <PubSubClient.h>

WebServer web(80);
WiFiServer cmdServer(CMD_PORT);
WiFiClient cmdClient;
WiFiUDP discoUdp;
WiFiClient mqttNet;
PubSubClient mqtt(mqttNet);
LineReader tcpReader;

bool apActive = false, mdnsStarted = false, servicesStarted = false, otaStarted = false;
uint32_t staStartMs = 0;
String apSsid;
volatile bool liveForce = false;

bool wifiApActive() { return apActive; }
String wifiApName() { return apSsid; }

// ============================================================================
// Time
// ============================================================================
bool timeValid() { return time(nullptr) > 1700000000; }
void timeApplyTz() { configTzTime(conf("tz").c_str(), "pool.ntp.org", "time.nist.gov"); }
void timeSetEpoch(uint32_t t) {
  if (t < 1700000000UL) return;
  struct timeval tv = {(time_t)t, 0};
  settimeofday(&tv, nullptr);
  setenv("TZ", conf("tz").c_str(), 1); tzset();
}

// ============================================================================
// WiFi
// ============================================================================
void startServices() {
  if (servicesStarted) return;
  web.begin(); cmdServer.begin(); cmdServer.setNoDelay(true);
  discoUdp.begin(DISCOVERY_PORT);
  servicesStarted = true;
}

void startAP() {
  if (apActive) return;
  WiFi.mode(conf("ssid").length() ? WIFI_AP_STA : WIFI_AP);
  apSsid = conf("bt_name");                       // same name as Bluetooth: CasinoSign-XXXX
  if (conf("ap_pass").length() >= 8) WiFi.softAP(apSsid.c_str(), conf("ap_pass").c_str());
  else                               WiFi.softAP(apSsid.c_str());
  apActive = true;
  startServices();
  uiDirty = true;
}

void stopAP() { if (apActive) { WiFi.softAPdisconnect(true); apActive = false; uiDirty = true; } }

void wifiBegin() {
  WiFi.persistent(false);
  WiFi.setHostname(conf("host").c_str());
  stopAP();
  if (conf("ssid").length()) {
    WiFi.mode(WIFI_STA);
    WiFi.begin(conf("ssid").c_str(), conf("pass").c_str());
    staStartMs = millis();
    startServices();
  } else if (confBool("ap_fallback")) {
    startAP();
  } else {
    WiFi.mode(WIFI_OFF);
  }
}

void otaBegin() {
  if (otaStarted) return;
  ArduinoOTA.setHostname(conf("host").c_str());
  if (conf("ota_pass").length()) ArduinoOTA.setPassword(conf("ota_pass").c_str());
  ArduinoOTA.onStart([]() { otaInProgress = true; stopScan(); drawUi(); });
  ArduinoOTA.onEnd([]() { otaInProgress = false; });
  ArduinoOTA.onError([](ota_error_t) { otaInProgress = false; if (runMode == RM_RUN) startScan(); uiDirty = true; });
  ArduinoOTA.begin();
  otaStarted = true;
}

void wifiService() {
  static bool wasConnected = false;
  bool c = WiFi.status() == WL_CONNECTED;
  if (c && !wasConnected) {
    if (!mdnsStarted && MDNS.begin(conf("host").c_str())) {
      MDNS.addService("http", "tcp", 80);
      MDNS.addService("casinosign", "tcp", CMD_PORT);
      mdnsStarted = true;
    }
    otaBegin();
    timeApplyTz();
    liveForce = true;
    sendEvent("{\"evt\":\"wifi\",\"connected\":true,\"ip\":\"" + WiFi.localIP().toString() + "\"}");
    uiDirty = true;
  }
  if (!c && wasConnected) { sendEvent("{\"evt\":\"wifi\",\"connected\":false}"); uiDirty = true; }
  wasConnected = c;
  if (!c && !apActive && confBool("ap_fallback") && conf("ssid").length() && millis() - staStartMs > 20000) startAP();
  if (apActive && !otaStarted) otaBegin();
}

// ============================================================================
// UDP discovery: the app broadcasts "CASINOSIGN?" to port 3334, we answer with who we are.
// ============================================================================
void discoveryService() {
  int n = discoUdp.parsePacket();
  if (n <= 0) return;
  char buf[32] = {0};
  discoUdp.read(buf, sizeof(buf) - 1);
  if (strncmp(buf, "CASINOSIGN?", 11) != 0) return;
  String ip = (WiFi.status() == WL_CONNECTED) ? WiFi.localIP().toString() : WiFi.softAPIP().toString();
  String reply = "{\"casinosign\":true,\"fw\":\"" FW_VERSION "\",\"name\":\"" + jsonEsc(conf("bt_name")) +
                 "\",\"host\":\"" + jsonEsc(conf("host")) + "\",\"ip\":\"" + ip + "\",\"port\":" + String(CMD_PORT) +
                 ",\"power\":" + (signPower ? "true" : "false") + "}";
  discoUdp.beginPacket(discoUdp.remoteIP(), discoUdp.remotePort());
  discoUdp.print(reply);
  discoUdp.endPacket();
}

// ============================================================================
// Web page: on/off buttons and text, works from any phone browser
// ============================================================================
String htmlEsc(String s) { s.replace("&", "&amp;"); s.replace("<", "&lt;"); s.replace("'", "&#39;"); s.replace("\"", "&quot;"); return s; }

void handleRoot() {
  if (web.hasArg("power")) powerSet(web.arg("power") == "on");
  if (web.hasArg("msg") && web.arg("msg").length()) contentCommand("TEXT", web.arg("msg"));
  if (web.hasArg("play")) contentCommand("PLAY", "");
  if (web.hasArg("next")) contentNext();
  if (web.hasArg("power") || web.hasArg("msg") || web.hasArg("play") || web.hasArg("next")) {
    web.sendHeader("Location", "/"); web.send(303); return;
  }
  String p = F("<!DOCTYPE html><meta name=viewport content='width=device-width,initial-scale=1'>"
    "<title>Casino sign</title><style>body{font-family:sans-serif;background:#111;color:#eee;max-width:480px;margin:auto;padding:16px}"
    "button,input{font-size:18px;padding:10px;margin:4px 0;border-radius:8px;border:0}button{background:#c33;color:#fff;width:48%}"
    ".on{background:#2a2}.w{width:100%}input{width:100%;box-sizing:border-box}</style><h2>Casino sign</h2>");
  p += "<p>Sign is <b>" + String(signPower ? "ON" : "OFF") + "</b> &middot; showing " + htmlEsc(contentDescribe()) + "</p>";
  p += F("<form><button class=on name=power value=on>Turn on</button> <button name=power value=off>Turn off</button></form>"
         "<form><input name=msg placeholder='Show this text'><button class='on w'>Show text</button></form>"
         "<form><button class=w style='background:#36c' name=play value=1>Back to playlist</button>"
         "<button class=w style='background:#555' name=next value=1>Next item</button></form>"
         "<p style='color:#888'>Everything else: the Casino Sign Tool app.</p>");
  web.send(200, "text/html", p);
}

// POST /update with the firmware .bin as the body or as a multipart file.
// Needs header X-OTA-Pass (or ?pass=) matching the ota_pass setting.
bool otaAuthOk() {
  String want = conf("ota_pass");
  if (!want.length()) return true;
  return web.header("X-OTA-Pass") == want || web.arg("pass") == want;
}

void handleUpdateDone() {
  bool ok = !Update.hasError() && otaAuthOk();
  web.send(ok ? 200 : 500, "application/json", ok ? "{\"ok\":true,\"reboot\":true}" : "{\"ok\":false,\"err\":\"update failed\"}");
  if (ok) { delay(300); ESP.restart(); }
  otaInProgress = false; uiDirty = true;
  if (runMode == RM_RUN) startScan();
}

void handleUpdateUpload() {
  HTTPUpload& up = web.upload();
  if (up.status == UPLOAD_FILE_START) {
    if (!otaAuthOk()) return;
    otaInProgress = true; stopScan(); drawUi();
    Update.begin(UPDATE_SIZE_UNKNOWN);
  } else if (up.status == UPLOAD_FILE_WRITE) {
    if (otaInProgress) Update.write(up.buf, up.currentSize);
  } else if (up.status == UPLOAD_FILE_END) {
    if (otaInProgress) Update.end(true);
  } else if (up.status == UPLOAD_FILE_ABORTED) {
    Update.abort(); otaInProgress = false;
  }
}

// ============================================================================
// MQTT + Home Assistant
// ============================================================================
uint32_t mqttLastTry = 0;
String mqttTopic(const char* t) { return conf("mqtt_prefix") + "/" + t; }

void mqttPublishState() {
  if (!mqtt.connected()) return;
  mqtt.publish(mqttTopic("power/state").c_str(), signPower ? "ON" : "OFF", true);
  mqtt.publish(mqttTopic("showing").c_str(), contentDescribe().c_str(), true);
}

void mqttEvent(const String& json) { if (mqtt.connected()) mqtt.publish(mqttTopic("event").c_str(), json.c_str()); }

void mqttDiscovery() {
  if (!confBool("mqtt_ha")) return;
  String id = conf("host"); id.replace("-", "_");
  String dev = "\"dev\":{\"ids\":[\"" + id + "\"],\"name\":\"Casino sign\",\"mf\":\"DIY\",\"mdl\":\"ESP32 sign driver\",\"sw\":\"" FW_VERSION "\"}";
  String avail = "\"avty_t\":\"" + mqttTopic("status") + "\"";
  String sw = "{\"name\":\"Casino sign\",\"uniq_id\":\"" + id + "_power\",\"cmd_t\":\"" + mqttTopic("power/set") +
              "\",\"stat_t\":\"" + mqttTopic("power/state") + "\"," + avail + "," + dev + "}";
  String tx = "{\"name\":\"Casino sign text\",\"uniq_id\":\"" + id + "_text\",\"cmd_t\":\"" + mqttTopic("text/set") +
              "\",\"max\":200," + avail + "," + dev + "}";
  String nx = "{\"name\":\"Casino sign next item\",\"uniq_id\":\"" + id + "_next\",\"cmd_t\":\"" + mqttTopic("cmd") +
              "\",\"pl_prs\":\"NEXT\"," + avail + "," + dev + "}";
  mqtt.publish(("homeassistant/switch/" + id + "/power/config").c_str(), sw.c_str(), true);
  mqtt.publish(("homeassistant/text/" + id + "/text/config").c_str(), tx.c_str(), true);
  mqtt.publish(("homeassistant/button/" + id + "/next/config").c_str(), nx.c_str(), true);
}

void mqttCallback(char* topic, byte* payload, unsigned int len) {
  String t = topic, msg; msg.reserve(len);
  for (unsigned i = 0; i < len; i++) msg += (char)payload[i];
  if (t == mqttTopic("power/set")) powerSet(msg.equalsIgnoreCase("ON") || msg == "1");
  else if (t == mqttTopic("text/set")) contentCommand("TEXT", msg);
  else if (t == mqttTopic("cmd")) {
    // Any protocol command, e.g. "SHOW {\"t\":\"clock\"}". Reply goes to <prefix>/reply.
    String r = handleCommand(msg);
    mqtt.publish(mqttTopic("reply").c_str(), r.c_str());
  }
  mqttPublishState();
}

void mqttService() {
  if (!confBool("mqtt_on") || conf("mqtt_host").length() == 0 || WiFi.status() != WL_CONNECTED) return;
  if (mqtt.connected()) { mqtt.loop(); return; }
  if (millis() - mqttLastTry < 15000) return;
  mqttLastTry = millis();
  mqtt.setServer(conf("mqtt_host").c_str(), conf("mqtt_port").toInt());
  mqtt.setCallback(mqttCallback);
  mqtt.setBufferSize(1024);
  mqtt.setSocketTimeout(2);
  String id = conf("host") + "-" + String((uint32_t)ESP.getEfuseMac(), HEX);
  bool ok = conf("mqtt_user").length()
    ? mqtt.connect(id.c_str(), conf("mqtt_user").c_str(), conf("mqtt_pass").c_str(), mqttTopic("status").c_str(), 0, true, "offline")
    : mqtt.connect(id.c_str(), mqttTopic("status").c_str(), 0, true, "offline");
  if (!ok) return;
  mqtt.publish(mqttTopic("status").c_str(), "online", true);
  mqtt.subscribe(mqttTopic("power/set").c_str());
  mqtt.subscribe(mqttTopic("text/set").c_str());
  mqtt.subscribe(mqttTopic("cmd").c_str());
  mqttDiscovery();
  mqttPublishState();
}

// ============================================================================
// Background data fetcher (its own task, so slow websites never stall the sign)
// ============================================================================
bool httpGet(const String& url, String& out) {
  WiFiClientSecure c;
  c.setInsecure();                         // public data only; no certificates to keep up to date
  HTTPClient h;
  h.setConnectTimeout(6000);
  h.setTimeout(8000);
  if (!h.begin(c, url)) return false;
  h.addHeader("User-Agent", "Mozilla/5.0 (CasinoSign)");
  int code = h.GET();
  if (code == 200) out = h.getString();
  h.end();
  return code == 200;
}

const char* weatherText(int code) {
  if (code == 0) return "CLEAR";
  if (code <= 2) return "PARTLY CLOUDY";
  if (code == 3) return "CLOUDY";
  if (code == 45 || code == 48) return "FOG";
  if (code >= 51 && code <= 57) return "DRIZZLE";
  if (code >= 61 && code <= 67) return "RAIN";
  if (code >= 71 && code <= 77) return "SNOW";
  if (code >= 80 && code <= 82) return "SHOWERS";
  if (code >= 85 && code <= 86) return "SNOW SHOWERS";
  if (code >= 95) return "STORMS";
  return "";
}

void fetchWeather() {
  bool f = conf("weather_units") != "C";
  String url = "https://api.open-meteo.com/v1/forecast?latitude=" + conf("weather_lat") + "&longitude=" + conf("weather_lon") +
               "&current=temperature_2m,weather_code" + (f ? "&temperature_unit=fahrenheit" : "");
  String body;
  if (!httpGet(url, body)) return;
  JsonDocument d;
  if (deserializeJson(d, body)) return;
  float temp = d["current"]["temperature_2m"] | -999.0f;
  int code = d["current"]["weather_code"] | -1;
  if (temp < -900) return;
  String w = String((int)lroundf(temp)) + (f ? "F " : "C ") + weatherText(code);
  if (xSemaphoreTake(liveMutex, portMAX_DELAY) == pdTRUE) { live.weather = w; live.weatherAt = millis(); xSemaphoreGive(liveMutex); }
}

String pct(double now, double prev) {
  if (prev <= 0) return "";
  double p = (now - prev) / prev * 100.0;
  return String(p >= 0 ? " +" : " ") + String(p, 1) + "%";
}

String fmtPrice(double v) { return v >= 1000 ? money(v).substring(1) : String(v, 2); }

void fetchTickers() {
  String out;
  // Stocks: Yahoo Finance chart endpoint (unofficial, free, no key)
  String list = conf("stocks"); list.trim();
  int start = 0, count = 0;
  while (list.length() && start <= (int)list.length() && count < 6) {
    int comma = list.indexOf(',', start); if (comma < 0) comma = list.length();
    String sym = list.substring(start, comma); sym.trim(); sym.toUpperCase();
    start = comma + 1;
    if (!sym.length()) continue;
    String body;
    if (!httpGet("https://query1.finance.yahoo.com/v8/finance/chart/" + sym + "?range=1d&interval=1d", body)) continue;
    JsonDocument filter; filter["chart"]["result"][0]["meta"]["regularMarketPrice"] = true; filter["chart"]["result"][0]["meta"]["chartPreviousClose"] = true;
    JsonDocument d;
    if (deserializeJson(d, body, DeserializationOption::Filter(filter))) continue;
    double p = d["chart"]["result"][0]["meta"]["regularMarketPrice"] | 0.0;
    double prev = d["chart"]["result"][0]["meta"]["chartPreviousClose"] | 0.0;
    if (p <= 0) continue;
    out += sym + " " + fmtPrice(p) + pct(p, prev) + "   ";
    count++;
  }
  // Crypto: CoinGecko simple price (free, no key)
  String ids = conf("crypto"); ids.trim(); ids.replace(" ", "");
  if (ids.length()) {
    String body;
    if (httpGet("https://api.coingecko.com/api/v3/simple/price?ids=" + ids + "&vs_currencies=usd&include_24hr_change=true", body)) {
      JsonDocument d;
      if (!deserializeJson(d, body)) {
        for (JsonPair kv : d.as<JsonObject>()) {
          String id = kv.key().c_str();
          String sym = id == "bitcoin" ? "BTC" : id == "ethereum" ? "ETH" : id == "dogecoin" ? "DOGE" : id == "solana" ? "SOL" :
                       id == "ripple" ? "XRP" : id == "cardano" ? "ADA" : id == "litecoin" ? "LTC" : id.substring(0, 5);
          sym.toUpperCase();
          double p = kv.value()["usd"] | 0.0, ch = kv.value()["usd_24h_change"] | 0.0;
          if (p <= 0) continue;
          out += sym + " " + fmtPrice(p) + (ch >= 0 ? " +" : " ") + String(ch, 1) + "%   ";
        }
      }
    }
  }
  out.trim();
  if (out.length() && xSemaphoreTake(liveMutex, portMAX_DELAY) == pdTRUE) { live.ticker = out; live.tickerAt = millis(); xSemaphoreGive(liveMutex); }
}

// Only fetch what the playlist actually uses.
bool playlistUses(const char* type) {
  String t = String("\"t\":\"") + type + "\"";
  String p; serializeJson(plDoc, p);
  String o; serializeJson(ovDoc, o);
  return p.indexOf(t) >= 0 || o.indexOf(t) >= 0;
}

void fetchTask(void*) {
  uint32_t lastW = 0, lastT = 0;
  bool firstW = true, firstT = true;
  for (;;) {
    vTaskDelay(pdMS_TO_TICKS(1000));
    if (WiFi.status() != WL_CONNECTED || otaInProgress) continue;
    bool force = liveForce; liveForce = false;
    if (force) { firstW = firstT = true; }
    if ((firstW || millis() - lastW > 15UL * 60 * 1000) && (force || playlistUses("weather"))) { fetchWeather(); lastW = millis(); firstW = false; }
    if ((firstT || millis() - lastT > 5UL * 60 * 1000) && (force || playlistUses("ticker"))) { fetchTickers(); lastT = millis(); firstT = false; }
  }
}

void liveRefreshNow() { liveForce = true; }

// ============================================================================
// Settings that need re-applying when changed (CONF SET)
// ============================================================================
void confApplied(const String& key) {
  if (key == "tz") { setenv("TZ", conf("tz").c_str(), 1); tzset(); if (WiFi.status() == WL_CONNECTED) timeApplyTz(); }
  else if (key == "bright_cap") updateBrightness();
  else if (key.startsWith("mqtt_")) { mqtt.disconnect(); mqttLastTry = 0; }
  else if (key.startsWith("weather_") || key == "stocks" || key == "crypto") liveForce = true;
  else if (key == "bt_on") btApply();
  else if (key == "ap_fallback") { if (!confBool("ap_fallback")) stopAP(); else if (WiFi.status() != WL_CONNECTED) startAP(); }
  uiDirty = true;
}

// ============================================================================
// WIFI / BT commands (kept for the app; they map onto CONF settings)
// ============================================================================
String netCommand(const String& cmd, String rest) {
  String sub = nextToken(rest); sub.toUpperCase(); rest.trim();
  if (cmd == "BT") {
#if !ENABLE_BT
    if (sub == "STATUS" || sub == "") return J().flag("on", false).flag("active", false).str("name", conf("bt_name")).flag("supported", false).done();
    return err("this firmware was built without Bluetooth (it doesn't fit alongside WiFi updates). WiFi and USB do everything Bluetooth did.");
#else
    if (sub == "STATUS" || sub == "") return J().flag("on", confBool("bt_on")).flag("active", btActive).str("name", conf("bt_name")).flag("client", btActive && SerialBT.hasClient()).flag("supported", true).done();
    if (sub == "ON" || sub == "OFF") { confSet("bt_on", sub == "ON" ? "1" : "0"); btApply(); uiDirty = true; return J().flag("on", confBool("bt_on")).flag("active", btActive).done(); }
    if (sub == "NAME") { if (!rest.length() || rest.length() > 30) return err("name must be 1..30 characters"); confSet("bt_name", rest); return J().str("name", rest).str("note", "reboot to apply").done(); }
    return err("BT STATUS|ON|OFF|NAME <name>");
#endif
  }
  if (sub == "STATUS" || sub == "") {
    return J().str("ssid", conf("ssid")).flag("has_pass", conf("pass").length() > 0)
              .flag("connected", WiFi.status() == WL_CONNECTED).str("ip", WiFi.localIP().toString())
              .num("rssi", WiFi.status() == WL_CONNECTED ? WiFi.RSSI() : 0)
              .str("host", conf("host")).flag("ap_fallback", confBool("ap_fallback")).flag("ap_active", apActive)
              .str("ap_ssid", apSsid).str("ap_ip", WiFi.softAPIP().toString())
              .flag("ap_has_pass", conf("ap_pass").length() >= 8).str("mac", WiFi.macAddress())
              .flag("mqtt_on", confBool("mqtt_on")).flag("mqtt_connected", mqtt.connected()).done();
  }
  if (sub == "SSID")   { confSet("ssid", rest); return J().str("ssid", rest).done(); }
  if (sub == "PASS")   { confSet("pass", rest); return J().flag("has_pass", rest.length() > 0).done(); }
  if (sub == "HOST")   { if (!rest.length()) return err("hostname can't be empty"); confSet("host", rest); return J().str("host", rest).str("note", "reboot to apply").done(); }
  if (sub == "AP")     { confSet("ap_fallback", (rest.equalsIgnoreCase("on") || rest == "1") ? "1" : "0"); confApplied("ap_fallback"); return J().flag("ap_fallback", confBool("ap_fallback")).done(); }
  if (sub == "APPASS") { if (rest.length() && rest.length() < 8) return err("hotspot password must be at least 8 characters (or empty for an open hotspot)"); confSet("ap_pass", rest); return J().flag("ap_has_pass", rest.length() >= 8).str("note", "reboot to apply").done(); }
  if (sub == "FORGET") { confSet("ssid", ""); confSet("pass", ""); return J().str("ssid", "").str("note", "reboot to apply").done(); }
  if (sub == "CONNECT") { wifiBegin(); uiDirty = true; return J().str("connecting", conf("ssid")).done(); }
  if (sub == "SCAN") {
    if (WiFi.getMode() == WIFI_OFF || WiFi.getMode() == WIFI_AP) WiFi.mode(apActive ? WIFI_AP_STA : WIFI_STA);
    int n = WiFi.scanNetworks();
    String a = "[";
    for (int i = 0; i < n && i < 30; i++) {
      if (i) a += ',';
      a += "{\"ssid\":\"" + jsonEsc(WiFi.SSID(i)) + "\",\"rssi\":" + String(WiFi.RSSI(i)) +
           ",\"open\":" + (WiFi.encryptionType(i) == WIFI_AUTH_OPEN ? "true" : "false") + "}";
    }
    WiFi.scanDelete();
    return J().raw("networks", a + "]").done();
  }
  return err("WIFI STATUS|SSID|PASS|HOST|AP|APPASS|FORGET|CONNECT|SCAN");
}

void tcpSendEvent(const String& json) { if (cmdClient && cmdClient.connected()) cmdClient.println(json); }

// ============================================================================
void netBegin() {
  setenv("TZ", conf("tz").c_str(), 1); tzset();
  web.on("/", handleRoot);
  web.on("/update", HTTP_POST, handleUpdateDone, handleUpdateUpload);
  const char* hdrs[] = {"X-OTA-Pass"};
  web.collectHeaders(hdrs, 1);
  wifiBegin();
  xTaskCreatePinnedToCore(fetchTask, "fetch", 12288, nullptr, 1, nullptr, 0);
}

void netLoop() {
  if (servicesStarted) {
#if ESP_ARDUINO_VERSION_MAJOR >= 3
    WiFiClient c = cmdServer.accept();
#else
    WiFiClient c = cmdServer.available();
#endif
    if (c) {
      if (cmdClient && cmdClient.connected()) cmdClient.stop();   // newest client wins
      cmdClient = c; cmdClient.setNoDelay(true); tcpReader.buf = "";
      cmdClient.println("{\"evt\":\"hello\",\"fw\":\"" FW_VERSION "\"}");
    }
    if (cmdClient && cmdClient.connected()) tcpReader.poll(cmdClient);
    web.handleClient();
    discoveryService();
  }
  if (otaStarted) ArduinoOTA.handle();
  wifiService();
  mqttService();
}
