// casino_sign_driver.ino
// Drives the Neurizon ED0204-24 16x96 LED matrix (MBI5170 column drivers +
// row decoder/P-MOSFETs) straight from the Freenove 4" ESP32 board, with the
// CPU-002 unplugged. Signals go through a 74HCT245 powered at 5V.
//
// v3: playlists + overnight schedule, clock / weather / stocks / crypto /
// countdown / jackpot widgets, text effects, stored animations (GIF import in
// the app), programmable startup and shutdown animations, sign on/off,
// Fight Night (live UFC data pushed by the app) and Fight Night gambling tally,
// MQTT + Home Assistant, WiFi firmware updates, brightness cap, scan watchdog.
//
// Everything is controlled with text commands (one per line, JSON replies) on:
//   - USB serial, 115200 baud
//   - TCP port 3333 over WiFi (the app finds the board with a UDP broadcast on port 3334)
//   - Bluetooth Classic serial (only if ENABLE_BT is 1, see below)
//   - MQTT topic <prefix>/cmd
// See PROTOCOL.md next to this file for the command list.
//
// Board: "ESP32 Dev Module" (Arduino-ESP32 core 2.x or 3.x)
// Partition scheme: "Minimal SPIFFS (1.9MB APP with OTA/190KB SPIFFS)"  <-- needed for WiFi updates + animation storage
// Libraries: LovyanGFX, ArduinoJson (7.x), PubSubClient.
//
// DRAFT: compiles, not tested on hardware yet.

#define LGFX_USE_V1
#include <LovyanGFX.hpp>
#include <WiFi.h>
#include <WiFiUdp.h>
#include <WebServer.h>
#include <ESPmDNS.h>
#include <Preferences.h>
#include <LittleFS.h>
#include <ArduinoJson.h>
#include <time.h>
#include "esp_timer.h"
#include "soc/gpio_reg.h"
#include "soc/i2s_struct.h"
#include "soc/gpio_sig_map.h"
#include "soc/io_mux_reg.h"
#include "soc/gpio_periph.h"
#include "esp_rom_gpio.h"
#include "esp32/rom/lldesc.h"
#include "esp_private/periph_ctrl.h"
#include "esp_heap_caps.h"
#include "sign_types.h"
#include "corvette_anim.h"     // built-in Corvette launch animation (installed to storage on first boot)

// Bluetooth Classic serial. Off by default: with it the firmware no longer fits
// the OTA partition layout (WiFi does everything Bluetooth did). Set to 1 and pick
// the "Huge APP" partition scheme if you want Bluetooth instead of WiFi updates.
#ifndef ENABLE_BT
#define ENABLE_BT 0
#endif
#if ENABLE_BT
#include "BluetoothSerial.h"
BluetoothSerial SerialBT;
#endif

#define FW_VERSION "3.2.0"

constexpr uint16_t CMD_PORT = 3333;
constexpr uint16_t DISCOVERY_PORT = 3334;

// GPIOs the app may use. Everything else is taken by flash/PSRAM (6-11, 16, 17),
// USB serial (1, 3), or the Freenove LCD/touch (12, 13, 14, 18, 19, 21, 22).
// 0, 2, 5 and 15 are strapping pins: usable, but they can stop the board booting
// if the sign pulls them the wrong way at power-up. The app warns about them.
// Outputs the sign may use on the Freenove 4" board. 23/19/18/5 = SPI connector, 32/25 = I2C connector,
// 0 = BOOT button pad, 22/16/17 = RGB LED pads, 26 = speaker input, 4 = speaker amp enable (solder points).
const int OUT_PINS[]  = {23, 19, 18, 5, 32, 25, 0, 22, 16, 17, 26, 4};
const int IN_ONLY[]   = {39, 35};   // 39 is on the IO connector (good for sniffing); 34 = battery, 36 = touch IRQ
const int STRAP_PINS[] = {0, 2, 5, 15};

// ============================================================================
// Runtime sign config (saved in flash)
// ============================================================================
constexpr uint8_t CFG_VERSION = 3;   // 3: Freenove 4" pin defaults (older saved configs used the screen's pins)
SignConfig cfg;

void cfgDefaults() {
  cfg = {};
  cfg.version = CFG_VERSION;
  cfg.scanRows = 8;
  cfg.pinDataTop = 23; cfg.pinDataBot = -1; cfg.pinClk = 18; cfg.pinLat = 19; cfg.pinOe = 5;
  cfg.pinAddr[0] = 32; cfg.pinAddr[1] = 25; cfg.pinAddr[2] = 0; cfg.pinAddr[3] = -1;
  cfg.oeActiveLow = 1; cfg.dataInvert = 0; cfg.addrInvert = 0; cfg.reverseCols = 0; cfg.chainBottomFirst = 1;
  cfg.refreshHz = 120;
  cfg.brightness = 100;
  cfg.configured = 0;
}

// ============================================================================
// General settings: a fixed list of string keys with defaults ("CONF" command)
// ============================================================================
struct ConfKey { const char* key; const char* def; bool secret; };
const ConfKey CONF_KEYS[] = {
  {"ssid", "", false}, {"pass", "", true}, {"host", "casinosign", false},
  {"ap_fallback", "1", false}, {"ap_pass", "casinosign", true},
  {"bt_on", "1", false}, {"bt_name", "", false},
  {"tz", "PST8PDT,M3.2.0,M11.1.0", false},          // POSIX time zone string
  {"weather_lat", "36.17", false}, {"weather_lon", "-115.14", false},
  {"weather_units", "F", false}, {"weather_label", "LAS VEGAS", false},
  {"stocks", "AAPL,TSLA", false}, {"crypto", "bitcoin,ethereum", false},
  {"jackpot_start", "100000", false}, {"jackpot_rate", "7.77", false},   // dollars per second
  {"bright_cap", "100", false},                     // brightness can never go above this
  {"anim_on", "corvette", false}, {"anim_off", "", false},  // startup / shutdown animation names ("" = none)
  {"mqtt_on", "0", false}, {"mqtt_host", "", false}, {"mqtt_port", "1883", false},
  {"mqtt_user", "", false}, {"mqtt_pass", "", true}, {"mqtt_prefix", "casinosign", false}, {"mqtt_ha", "1", false},
  {"ota_pass", "casinosign", true},
  {"text_speed", "30", false},                      // ms per scroll step
  {"sched_on", "0", false}, {"sched_start", "08:00", false}, {"sched_end", "23:30", false},
};
constexpr int CONF_N = sizeof(CONF_KEYS) / sizeof(ConfKey);
String confVal[CONF_N];

int confIndex(const String& k) { for (int i = 0; i < CONF_N; i++) if (k == CONF_KEYS[i].key) return i; return -1; }
const String& conf(const char* k) { static String empty; int i = confIndex(k); return i < 0 ? empty : confVal[i]; }
bool confBool(const char* k) { const String& v = conf(k); return v == "1" || v.equalsIgnoreCase("true") || v.equalsIgnoreCase("on"); }

Preferences prefs;

// ============================================================================
// Shared state (used across the .ino files)
// ============================================================================
RunMode runMode = RM_DIAG;
bool signPower = true;          // false = sign blanked by POWER OFF / schedule
FightData fight;
GambleData gamble;
LiveData live;
SemaphoreHandle_t liveMutex;
bool uiDirty = true;
bool otaInProgress = false;

// ============================================================================
// Freenove 4" display
// ============================================================================
// Pins from Freenove's own files for the 4" board (FNK0103 family, ESP32-32E, same as LCDWiki E32R40T):
// LCD on HSPI: SCK 14, MOSI 13, MISO 12, CS 15, DC 2, reset = board reset, backlight 27 (high = on).
// Touch is a resistive XPT2046 on the same SPI bus: CS 33, IRQ 36.
// Free for the sign: SPI connector 23/19/18/5 (shared with the SD slot), I2C connector 32/25, BOOT pad 0.
class LGFX : public lgfx::LGFX_Device {
  lgfx::Panel_ST7796  _panel;
  lgfx::Bus_SPI       _bus;
  lgfx::Touch_XPT2046 _touch;
public:
  LGFX() {
    { auto c = _bus.config();
      c.spi_host = HSPI_HOST; c.spi_mode = 0; c.freq_write = 27000000; c.freq_read = 16000000;
      c.spi_3wire = false; c.use_lock = true;
      c.pin_sclk = 14; c.pin_mosi = 13; c.pin_miso = 12; c.pin_dc = 2;
      c.dma_channel = SPI_DMA_CH_AUTO;
      _bus.config(c); _panel.setBus(&_bus); }
    { auto c = _panel.config();
      c.pin_cs = 15; c.pin_rst = -1; c.pin_busy = -1;
      c.panel_width = 320; c.panel_height = 480;
      c.offset_x = 0; c.offset_y = 0; c.offset_rotation = 0;
      c.readable = false; c.invert = false; c.rgb_order = false;
      c.bus_shared = true;   // the touch chip shares the SPI bus
      _panel.config(c); }
    { auto c = _touch.config();
      c.x_min = 300; c.x_max = 3460; c.y_min = 350; c.y_max = 3300;   // Freenove's calibration for this panel
      c.pin_int = 36; c.bus_shared = true; c.offset_rotation = 0;
      c.spi_host = HSPI_HOST; c.freq = 2500000;
      c.pin_sclk = 14; c.pin_mosi = 13; c.pin_miso = 12; c.pin_cs = 33;
      _touch.config(c); _panel.setTouch(&_touch); }
    setPanel(&_panel);
  }
};
constexpr int LCD_BL_PIN = 27;
LGFX lcd;
LGFX_Sprite canvas;   // 1-bit 96x16 drawing surface for the sign

// ============================================================================
// Fast GPIO
// ============================================================================
static inline void pinSet(int p, bool v) {
  if (p < 0) return;
  if (p < 32) REG_WRITE(v ? GPIO_OUT_W1TS_REG : GPIO_OUT_W1TC_REG, 1UL << p);
  else        REG_WRITE(v ? GPIO_OUT1_W1TS_REG : GPIO_OUT1_W1TC_REG, 1UL << (p - 32));
}
static inline bool pinGet(int p) {
  if (p < 32) return (REG_READ(GPIO_IN_REG) >> p) & 1;
  return (REG_READ(GPIO_IN1_REG) >> (p - 32)) & 1;
}
static inline void oeEnable(bool on) { pinSet(cfg.pinOe, cfg.oeActiveLow ? !on : on); }

// ============================================================================
// Framebuffer: 1 bit per pixel, double-buffered
// ============================================================================
static uint8_t fb[2][SIGN_H][ROW_BYTES];
static volatile int frontBuf = 0;

static inline bool fbGet(int buf, int x, int y) {
  return fb[buf][y][x >> 3] & (0x80 >> (x & 7));
}

void pushCanvasToSign() {
  int back = frontBuf ^ 1;
  for (int y = 0; y < SIGN_H; y++)
    for (int x = 0; x < SIGN_W; x++) {
      uint8_t m = 0x80 >> (x & 7);
      if (canvas.readPixelValue(x, y)) fb[back][y][x >> 3] |= m;
      else                             fb[back][y][x >> 3] &= ~m;
    }
  frontBuf = back;
}

// ============================================================================
// Scan driver
// ============================================================================
esp_timer_handle_t scanTimer = nullptr, blankTimer = nullptr;
volatile bool scanning = false;
volatile uint32_t scanCount = 0;      // watched by the scan watchdog in loop()
volatile uint8_t effectiveBrightness = 100;   // min(brightness, brightness cap)
uint32_t scanPeriodUs = 1000;

static void shiftRow(int buf, int y, int pinData) {
  for (int i = 0; i < SIGN_W; i++) {
    int x = cfg.reverseCols ? i : (SIGN_W - 1 - i);  // last bit shifted lands in column 0
    pinSet(pinData, fbGet(buf, x, y) ^ cfg.dataInvert);
    pinSet(cfg.pinClk, 1);
    pinSet(cfg.pinClk, 0);
  }
}

static void blankCallback(void*) { oeEnable(false); }

static void scanCallback(void*) {
  static int addr = 0;
  const int buf = frontBuf;
  const int rows = cfg.scanRows;
  if (addr >= rows) addr = 0;

  if (rows == SIGN_H) {
    shiftRow(buf, addr, cfg.pinDataTop);
  } else if (cfg.pinDataBot >= 0) {
    for (int i = 0; i < SIGN_W; i++) {
      int x = cfg.reverseCols ? i : (SIGN_W - 1 - i);
      pinSet(cfg.pinDataTop, fbGet(buf, x, addr) ^ cfg.dataInvert);
      pinSet(cfg.pinDataBot, fbGet(buf, x, addr + rows) ^ cfg.dataInvert);
      pinSet(cfg.pinClk, 1);
      pinSet(cfg.pinClk, 0);
    }
  } else {
    if (cfg.chainBottomFirst) { shiftRow(buf, addr + rows, cfg.pinDataTop); shiftRow(buf, addr, cfg.pinDataTop); }
    else                      { shiftRow(buf, addr, cfg.pinDataTop); shiftRow(buf, addr + rows, cfg.pinDataTop); }
  }

  // Blank, switch row, latch, unblank.
  oeEnable(false);
  int a = cfg.addrInvert ? (rows - 1 - addr) : addr;
  for (int b = 0; b < 4; b++) pinSet(cfg.pinAddr[b], (a >> b) & 1);
  pinSet(cfg.pinLat, 1);
  pinSet(cfg.pinLat, 0);
  oeEnable(true);
  // Brightness: blank again part-way through the row period.
  scanCount++;
  uint8_t br = effectiveBrightness;
  if (br < 100 && blankTimer) {
    uint32_t on = scanPeriodUs * br / 100;
    if (on < 20) on = 20;
    esp_timer_start_once(blankTimer, on);
  }

  addr = (addr + 1) % rows;
}

int8_t* signPins[] = {&cfg.pinDataTop, &cfg.pinDataBot, &cfg.pinClk, &cfg.pinLat, &cfg.pinOe,
                      &cfg.pinAddr[0], &cfg.pinAddr[1], &cfg.pinAddr[2], &cfg.pinAddr[3]};

void releaseSignPins() {
  for (auto p : signPins) if (*p >= 0) pinMode(*p, INPUT);
}

void stopScan() {
  if (scanTimer) esp_timer_stop(scanTimer);
  if (blankTimer) esp_timer_stop(blankTimer);
  delay(2);                  // let a running callback finish
  if (scanning) oeEnable(false);
  scanning = false;
}

bool startScan() {
  stopScan();
  if (cfg.pinClk < 0 || cfg.pinLat < 0 || cfg.pinDataTop < 0) return false;
  if (cfg.pinOe >= 0) { pinMode(cfg.pinOe, OUTPUT); oeEnable(false); }
  for (auto p : signPins) if (*p >= 0 && p != &cfg.pinOe) { pinMode(*p, OUTPUT); digitalWrite(*p, LOW); }
  if (!scanTimer) {
    esp_timer_create_args_t args = {};
    args.callback = scanCallback; args.name = "signscan";
    esp_timer_create(&args, &scanTimer);
    esp_timer_create_args_t bargs = {};
    bargs.callback = blankCallback; bargs.name = "signblank";
    esp_timer_create(&bargs, &blankTimer);
  }
  uint32_t hz = constrain(cfg.refreshHz, 30, 400);
  scanPeriodUs = 1000000UL / (hz * cfg.scanRows);
  esp_timer_start_periodic(scanTimer, scanPeriodUs);
  scanning = true;
  return true;
}

// ============================================================================
// Diagnostics: probe outputs and signal sniffing
// ============================================================================

Probe probes[8];
uint32_t probeTimeoutMs = 10000;

bool inList(int p, const int* list, size_t n) { for (size_t i = 0; i < n; i++) if (list[i] == p) return true; return false; }
bool isOutPin(int p) { return inList(p, OUT_PINS, sizeof(OUT_PINS) / sizeof(int)); }
bool isInPin(int p)  { return isOutPin(p) || inList(p, IN_ONLY, sizeof(IN_ONLY) / sizeof(int)); }

void probeRelease(Probe& pr) {
  if (!pr.kind) return;
  if (pr.kind == 'P') {
#if ESP_ARDUINO_VERSION_MAJOR >= 3
    ledcDetach(pr.pin);
#else
    ledcDetachPin(pr.pin);
#endif
  }
  pinMode(pr.pin, INPUT);
  pr.kind = 0;
}

Probe* probeFind(int pin, bool alloc) {
  for (auto& p : probes) if (p.kind && p.pin == pin) return &p;
  if (!alloc) return nullptr;
  for (int i = 0; i < 8; i++) if (!probes[i].kind) { probes[i].ch = i; return &probes[i]; }
  return nullptr;
}

void probeAllOff() { for (auto& p : probes) probeRelease(p); }

String probeStart(int pin, char kind, float hz, uint32_t ms) {
  if (runMode != RM_DIAG) return "switch to DIAG mode first (MODE DIAG)";
  if (!isOutPin(pin)) return "GPIO not allowed as output";
  Probe* pr = probeFind(pin, false);
  if (pr) probeRelease(*pr);
  pr = probeFind(pin, true);
  if (!pr) return "too many probes active (max 8)";
  pr->pin = pin; pr->hz = hz; pr->level = false; pr->lastToggle = millis();
  pr->until = millis() + (ms ? ms : probeTimeoutMs);
  if (kind == 'P' && hz < 50) kind = 'S';
  pr->kind = kind;
  if (kind == 'H' || kind == 'L' || kind == 'S') {
    pinMode(pin, OUTPUT);
    digitalWrite(pin, kind == 'H');
  } else {
#if ESP_ARDUINO_VERSION_MAJOR >= 3
    ledcAttach(pin, (uint32_t)hz, 8);
    ledcWrite(pin, 128);
#else
    ledcSetup(pr->ch, hz, 8);
    ledcAttachPin(pin, pr->ch);
    ledcWrite(pr->ch, 128);
#endif
  }
  return "";
}

void sendEvent(const String& json);

void probeService() {
  uint32_t now = millis();
  for (auto& p : probes) {
    if (!p.kind) continue;
    if ((int32_t)(now - p.until) >= 0) {
      int pin = p.pin;
      probeRelease(p);
      sendEvent(String("{\"evt\":\"probe_off\",\"gpio\":") + pin + ",\"why\":\"timeout\"}");
      continue;
    }
    if (p.kind == 'S') {
      uint32_t half = (uint32_t)(500.0f / max(p.hz, 0.1f));
      if (now - p.lastToggle >= half) { p.lastToggle = now; p.level = !p.level; digitalWrite(p.pin, p.level); }
    }
  }
}


SniffResult sniff(int pin, uint32_t ms) {
  pinMode(pin, INPUT);
  delayMicroseconds(50);
  SniffResult r = {};
  bool last = pinGet(pin);
  uint32_t t0 = micros(), dur = ms * 1000;
  while (micros() - t0 < dur) {
    for (int k = 0; k < 64; k++) {      // unrolled-ish inner loop for speed
      bool v = pinGet(pin);
      r.samples++; r.highs += v;
      if (v != last) { r.edges++; last = v; }
    }
  }
  r.ms = ms; r.level = last;
  return r;
}

// ============================================================================
// Logic analyzer: samples 8 GPIOs at once into RAM, then streams them as base64.
// Channels (bit 0..7): the free pins on the Freenove board's plugs.
// ============================================================================
extern Stream* replyTo;
const int LA_PINS[8] = {35, 39, 25, 32, 5, 18, 19, 23};
static const char B64[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

static inline uint8_t laRead() {
  uint32_t a = REG_READ(GPIO_IN_REG), b = REG_READ(GPIO_IN1_REG);
  return ((b >> 3) & 1) | (((b >> 7) & 1) << 1) | (((a >> 25) & 1) << 2) | ((b & 1) << 3) |
         (((a >> 5) & 1) << 4) | (((a >> 18) & 1) << 5) | (((a >> 19) & 1) << 6) | (((a >> 23) & 1) << 7);
}

// ---- Fast capture: the I2S peripheral in "camera" mode samples all 8 channels in hardware ----
// The sample clock comes from LEDC on GPIO 16 and is looped back into I2S as its pixel clock.
// A self-test clock at exactly 1/6 of the sample rate runs on GPIO 17 and is recorded on I2S data bit 8,
// so the firmware can check the result and work out the order of the two samples in each 32-bit word.
// GPIO 16 and 17 are the RGB LED pads on the Freenove board: the LED flickers during a fast capture.
static const int FAST_CLK_PIN = 16, FAST_TEST_PIN = 17;
static String fastInfo;   // extra JSON for the reply

static void fastDetach() {
  ledcDetach(FAST_CLK_PIN); ledcDetach(FAST_TEST_PIN);
  pinMode(FAST_CLK_PIN, OUTPUT); digitalWrite(FAST_CLK_PIN, HIGH);    // RGB LED is active low: HIGH = off
  pinMode(FAST_TEST_PIN, OUTPUT); digitalWrite(FAST_TEST_PIN, HIGH);
}

// Fills buf with n channel bytes. Returns "" on success or an error message.
static String laFastCapture(uint8_t* buf, uint32_t& n, uint32_t hz, double& actual) {
  uint32_t bytes = n * 2;                              // 2 samples per 32-bit word (16 bits each)
  const uint32_t CH = 4092;                            // bytes per DMA descriptor (max 4095, word aligned)
  uint32_t nd = (bytes + CH - 1) / CH;
  uint8_t* dma = (uint8_t*)heap_caps_malloc(bytes, MALLOC_CAP_DMA | MALLOC_CAP_8BIT);
  lldesc_t* d = (lldesc_t*)heap_caps_calloc(nd, sizeof(lldesc_t), MALLOC_CAP_DMA);
  if (!dma || !d) { free(dma); free(d); return "not enough memory for a fast capture"; }
  for (uint32_t i = 0; i < nd; i++) {
    uint32_t sz = min(CH, bytes - i * CH);
    d[i].size = sz; d[i].length = sz; d[i].buf = dma + i * CH; d[i].owner = 1; d[i].sosf = 1; d[i].eof = 1; d[i].offset = 0;
    d[i].qe.stqe_next = (i + 1 < nd) ? &d[i + 1] : NULL;
  }

  // Clocks: sample clock on GPIO 16, self-test at hz/6 on GPIO 17. Both come from the same 80 MHz APB clock.
  if (!ledcAttach(FAST_CLK_PIN, hz, 1) || !ledcAttach(FAST_TEST_PIN, hz / 6, 1)) { fastDetach(); free(dma); free(d); return "could not start the sample clock (LEDC)"; }
  ledcWrite(FAST_CLK_PIN, 1); ledcWrite(FAST_TEST_PIN, 1);
  PIN_INPUT_ENABLE(GPIO_PIN_MUX_REG[FAST_CLK_PIN]);
  PIN_INPUT_ENABLE(GPIO_PIN_MUX_REG[FAST_TEST_PIN]);
  for (int i = 0; i < 8; i++) { PIN_INPUT_ENABLE(GPIO_PIN_MUX_REG[LA_PINS[i]]); esp_rom_gpio_connect_in_signal(LA_PINS[i], I2S0I_DATA_IN0_IDX + i, false); }
  esp_rom_gpio_connect_in_signal(FAST_TEST_PIN, I2S0I_DATA_IN8_IDX, false);
  for (int i = 9; i < 16; i++) esp_rom_gpio_connect_in_signal(0x30, I2S0I_DATA_IN0_IDX + i, false);   // constant low
  esp_rom_gpio_connect_in_signal(0x38, I2S0I_V_SYNC_IDX, false);      // constant high
  esp_rom_gpio_connect_in_signal(0x38, I2S0I_H_SYNC_IDX, false);
  esp_rom_gpio_connect_in_signal(0x38, I2S0I_H_ENABLE_IDX, false);
  esp_rom_gpio_connect_in_signal(FAST_CLK_PIN, I2S0I_WS_IN_IDX, false);

  periph_module_enable(PERIPH_I2S0_MODULE);
  I2S0.conf.rx_reset = 1; I2S0.conf.rx_reset = 0;
  I2S0.conf.rx_fifo_reset = 1; I2S0.conf.rx_fifo_reset = 0;
  I2S0.lc_conf.in_rst = 1; I2S0.lc_conf.in_rst = 0;
  I2S0.lc_conf.ahbm_fifo_rst = 1; I2S0.lc_conf.ahbm_fifo_rst = 0;
  I2S0.lc_conf.ahbm_rst = 1; I2S0.lc_conf.ahbm_rst = 0;
  I2S0.conf.rx_slave_mod = 1;
  I2S0.conf.rx_right_first = 0; I2S0.conf.rx_msb_right = 0; I2S0.conf.rx_msb_shift = 0;
  I2S0.conf.rx_mono = 0; I2S0.conf.rx_short_sync = 0;
  I2S0.conf2.lcd_en = 1; I2S0.conf2.camera_en = 1;
  I2S0.clkm_conf.clka_en = 0; I2S0.clkm_conf.clkm_div_a = 1; I2S0.clkm_conf.clkm_div_b = 0; I2S0.clkm_conf.clkm_div_num = 2;
  I2S0.fifo_conf.dscr_en = 1;
  I2S0.fifo_conf.rx_fifo_mod = 1;                      // 16-bit samples, two per word
  I2S0.fifo_conf.rx_fifo_mod_force_en = 1;
  I2S0.conf_chan.rx_chan_mod = 1;
  I2S0.sample_rate_conf.rx_bits_mod = 0;
  I2S0.timing.val = 0;
  I2S0.timing.rx_dsync_sw = 1;
  I2S0.rx_eof_num = bytes / 4;
  I2S0.in_link.addr = (uint32_t)&d[0];
  I2S0.int_clr.val = 0xFFFFFFFF;

  // Real samples always have bits 9-15 clear (those inputs are tied low), so pre-fill with 0xFF
  // and afterwards count how much of the buffer the DMA actually wrote.
  memset(dma, 0xFF, bytes);
  uint32_t t0 = micros();
  I2S0.in_link.start = 1;
  I2S0.conf.rx_start = 1;
  uint32_t waitUs = (uint32_t)((double)n * 1e6 / hz) * 2 + 3000;   // the capture itself takes n / hz (about 1.5 ms)
  volatile uint16_t* last = (volatile uint16_t*)(dma + bytes - 2);
  while (micros() - t0 < waitUs) { if ((*last & 0xFE00) == 0 && I2S0.int_raw.in_suc_eof) break; }
  uint32_t us = micros() - t0;
  I2S0.conf.rx_start = 0; I2S0.in_link.stop = 1;
  periph_module_disable(PERIPH_I2S0_MODULE);
  fastDetach();
  for (int i = 0; i < 8; i++) pinMode(LA_PINS[i], INPUT);   // back to plain GPIO inputs
  uint16_t* hw0 = (uint16_t*)dma;
  uint32_t filled = n;
  while (filled > 0 && (hw0[filled - 1] & 0xFE00)) filled--;
  if (filled < 600) {
    char h[96]; uint32_t* w = (uint32_t*)dma; snprintf(h, sizeof h, "%08lx %08lx %08lx %08lx", (unsigned long)w[0], (unsigned long)w[1], (unsigned long)w[2], (unsigned long)w[3]);
    free(dma); free(d);
    return String("fast capture got no data from I2S (") + filled + " samples). First words: " + h;
  }
  filled &= ~1u;
  n = filled - filled % 6;

  // Each word holds two 16-bit samples. Use the self-test bit (bit 8) to find their order:
  // a clock at exactly 1/6 of the sample rate must give runs of 3 samples.
  uint16_t* hw = (uint16_t*)dma;
  uint32_t badA = 0, badB = 0, runA = 0, runB = 0, edgesA = 0;
  int lastA = -1, lastB = -1;
  uint32_t check = min<uint32_t>(n, 6000);
  for (uint32_t i = 0; i < check; i++) {
    int a = (hw[i] >> 8) & 1;                  // order A: low half first
    int b = (hw[i ^ 1] >> 8) & 1;              // order B: high half first
    if (a == lastA) runA++; else { if (lastA >= 0 && i > 8 && runA != 3) badA++; if (lastA >= 0) edgesA++; runA = 1; lastA = a; }
    if (b == lastB) runB++; else { if (lastB >= 0 && i > 8 && runB != 3) badB++; runB = 1; lastB = b; }
  }
  bool swap = badB < badA;
  uint32_t bad = swap ? badB : badA;
  for (uint32_t i = 0; i < n; i++) buf[i] = hw[swap ? (i ^ 1) : i] & 0xFF;
  char inf[160];
  snprintf(inf, sizeof inf, ",\"fast\":true,\"filled\":%lu,", (unsigned long)filled);
  fastInfo = inf;
  snprintf(inf, sizeof inf, "\"selftest_bad_runs\":%lu,\"selftest_checked\":%lu,\"selftest_edges\":%lu,\"swapped\":%s,\"took_us\":%lu",
           (unsigned long)bad, (unsigned long)check, (unsigned long)edgesA, swap ? "true" : "false", (unsigned long)us);
  fastInfo += inf;
  actual = hz;
  if (edgesA < 10) fastInfo += ",\"selftest\":\"no clock seen on bit 8\"";
  else if (bad > check / 60) fastInfo += ",\"selftest\":\"irregular\"";
  else fastInfo += ",\"selftest\":\"ok\"";
  free(dma); free(d);
  return "";
}

// Streams {"ok":true,"cap":"<base64>" and returns the rest of the JSON line.
String laCapture(uint32_t n, uint32_t rateHz, int trigCh, int trigEdge) {
  for (int i = 0; i < 8; i++) if (probeFind(LA_PINS[i], false)) return err(String("GPIO ") + LA_PINS[i] + " is a probe output, turn it off first");
  for (int i = 0; i < 8; i++) pinMode(LA_PINS[i], INPUT);
  uint32_t fastHz = rateHz == 2 ? 20000000 : rateHz == 3 ? 10000000 : 0;   // rate 2 / 3 = fast I2S capture at 20 / 10 MHz
  bool turbo = rateHz == 1;                          // rate 1 = turbo: raw reads of GPIO 0-31 only (channels 3, 5-8)
  if (turbo) { rateHz = 0; n = min<uint32_t>(n, 16383); }
  n = constrain(n, 1024, 65536); n -= n % 3;        // multiple of 3 keeps base64 simple
  fastInfo = "";
  if (fastHz) {
    if (probeFind(FAST_CLK_PIN, false) || probeFind(FAST_TEST_PIN, false)) return err("GPIO 16/17 are probe outputs, turn them off first");
    n = min<uint32_t>(n, 30000); n -= n % 6;
    uint8_t* fb = nullptr;
    while (n >= 3000 && !(fb = (uint8_t*)malloc(n))) n /= 2;
    if (!fb) return err("not enough memory for a capture");
    n -= n % 6;
    double act = 0;
    String e = laFastCapture(fb, n, fastHz, act);
    if (e.length() && n > 6000) { n = 6000; e = laFastCapture(fb, n, fastHz, act); }   // retry smaller if DMA memory was short
    if (e.length()) { free(fb); return err(e); }
    Stream* o = replyTo;
    o->print("{\"ok\":true,\"cap\":\"");
    char chunk[64]; int k = 0;
    for (uint32_t i = 0; i < n; i += 3) {
      uint32_t v = (fb[i] << 16) | (fb[i + 1] << 8) | fb[i + 2];
      chunk[k++] = B64[(v >> 18) & 63]; chunk[k++] = B64[(v >> 12) & 63]; chunk[k++] = B64[(v >> 6) & 63]; chunk[k++] = B64[v & 63];
      if (k >= 60) { o->write((const uint8_t*)chunk, k); k = 0; }
    }
    if (k) o->write((const uint8_t*)chunk, k);
    free(fb);
    return "\",\"n\":" + String(n) + ",\"rate\":" + String(act, 0) + ",\"triggered\":false,\"turbo\":false" + fastInfo + ",\"pins\":" + intList(LA_PINS, 8) + "}";
  }
  uint8_t* buf = (uint8_t*)malloc(turbo ? n * 4 : n);
  if (!buf) { n = 24576; buf = (uint8_t*)malloc(n); }
  if (!buf) return err("not enough memory for a capture");
  uint32_t cpu = ESP.getCpuFreqMHz() * 1000000UL;
  if (rateHz && rateHz < 10000) rateHz = 10000;
  uint32_t period = rateHz ? cpu / rateHz : 0;       // CPU cycles per sample, 0 = as fast as possible
  bool triggered = false;
  // Interrupts off gives even sample timing, but only for short captures (the interrupt watchdog trips at ~300 ms).
  bool noInt = (double)n * (period ? period : 40) / cpu < 0.12;
  if (noInt) portDISABLE_INTERRUPTS();
  uint32_t t0 = ESP.getCycleCount();
  if (trigCh >= 0 && trigCh < 8) {                   // wait up to ~0.1 s for the trigger edge
    uint8_t m = 1 << trigCh; bool last = laRead() & m;
    uint32_t limit = cpu / 10;
    while (ESP.getCycleCount() - t0 < limit) {
      bool v = laRead() & m;
      if (v != last && (trigEdge == 0 || (trigEdge > 0) == v)) { triggered = true; break; }
      last = v;
    }
  }
  uint32_t start = ESP.getCycleCount(), next = start;
  if (turbo) {
    volatile uint32_t* w = (volatile uint32_t*)buf;
    uint32_t i = 0;
    for (; i + 8 <= n; i += 8) {
      w[i] = REG_READ(GPIO_IN_REG); w[i + 1] = REG_READ(GPIO_IN_REG); w[i + 2] = REG_READ(GPIO_IN_REG); w[i + 3] = REG_READ(GPIO_IN_REG);
      w[i + 4] = REG_READ(GPIO_IN_REG); w[i + 5] = REG_READ(GPIO_IN_REG); w[i + 6] = REG_READ(GPIO_IN_REG); w[i + 7] = REG_READ(GPIO_IN_REG);
    }
    for (; i < n; i++) w[i] = REG_READ(GPIO_IN_REG);
  }
  else if (period == 0) { for (uint32_t i = 0; i < n; i++) buf[i] = laRead(); }
  else for (uint32_t i = 0; i < n; i++) { while ((int32_t)(ESP.getCycleCount() - next) < 0) {} buf[i] = laRead(); next += period; }
  uint32_t cycles = ESP.getCycleCount() - start;
  if (noInt) portENABLE_INTERRUPTS();
  double actual = cycles ? (double)n * cpu / cycles : 0;
  if (turbo) {   // squeeze each 32-bit read into the usual channel byte (in place: byte i never passes word i)
    uint32_t* w = (uint32_t*)buf;
    for (uint32_t i = 0; i < n; i++) {
      uint32_t a = w[i];
      buf[i] = (((a >> 25) & 1) << 2) | (((a >> 5) & 1) << 4) | (((a >> 18) & 1) << 5) | (((a >> 19) & 1) << 6) | (((a >> 23) & 1) << 7);
    }
  }

  Stream* o = replyTo;
  o->print("{\"ok\":true,\"cap\":\"");
  char chunk[64]; int k = 0;
  for (uint32_t i = 0; i < n; i += 3) {
    uint32_t v = (buf[i] << 16) | (buf[i + 1] << 8) | buf[i + 2];
    chunk[k++] = B64[(v >> 18) & 63]; chunk[k++] = B64[(v >> 12) & 63]; chunk[k++] = B64[(v >> 6) & 63]; chunk[k++] = B64[v & 63];
    if (k >= 60) { o->write((const uint8_t*)chunk, k); k = 0; }
  }
  if (k) o->write((const uint8_t*)chunk, k);
  free(buf);
  String tail = "\",\"n\":" + String(n) + ",\"rate\":" + String(actual, 0) + ",\"triggered\":" + (triggered ? "true" : "false") + ",\"turbo\":" + (turbo ? "true" : "false") +
                ",\"pins\":" + intList(LA_PINS, 8) + "}";
  return tail;
}

// ============================================================================
// Settings load/save
// ============================================================================
void loadSettings() {
  cfgDefaults();
  if (prefs.getBytesLength("cfg") == sizeof(SignConfig)) {
    SignConfig tmp;
    prefs.getBytes("cfg", &tmp, sizeof(tmp));
    if (tmp.version == CFG_VERSION) cfg = tmp;
  }
  uint64_t mac = ESP.getEfuseMac();
  char suffix[8]; snprintf(suffix, sizeof(suffix), "%04X", (uint16_t)(mac >> 32));
  for (int i = 0; i < CONF_N; i++) {
    String k = String("c_") + CONF_KEYS[i].key;   // NVS keys are max 15 chars
    if (k.length() > 15) k = k.substring(0, 15);
    confVal[i] = prefs.getString(k.c_str(), CONF_KEYS[i].def);
  }
  if (conf("bt_name").length() == 0) confVal[confIndex("bt_name")] = String("CasinoSign-") + suffix;
  signPower = prefs.getBool("power", true);
  gamble.bet = prefs.getFloat("g_bet", 0); gamble.won = prefs.getFloat("g_won", 0);
  gamble.label = prefs.getString("g_label", "FIGHT NIGHT");
}

void saveSignConfig() { prefs.putBytes("cfg", &cfg, sizeof(cfg)); }

bool confSet(const String& key, const String& val) {
  int i = confIndex(key);
  if (i < 0) return false;
  confVal[i] = val;
  String k = String("c_") + CONF_KEYS[i].key;
  if (k.length() > 15) k = k.substring(0, 15);
  prefs.putString(k.c_str(), val);
  return true;
}

void updateBrightness() {
  int cap = constrain(conf("bright_cap").toInt(), 1, 100);
  effectiveBrightness = min<int>(cfg.brightness, cap);
}

// ============================================================================
// Bluetooth
// ============================================================================
bool btActive = false;
void btApply() {
#if ENABLE_BT
  if (btActive) { SerialBT.end(); btActive = false; }
  if (confBool("bt_on")) btActive = SerialBT.begin(conf("bt_name"));
#endif
}

// ============================================================================
// LCD status screen
// ============================================================================
void drawUi() {
  lcd.fillScreen(TFT_BLACK);
  lcd.setTextColor(TFT_WHITE); lcd.setTextSize(2);
  lcd.drawString("Casino Sign Driver", 10, 10);
  lcd.drawFastHLine(0, 40, 320, TFT_DARKGREY);
  if (otaInProgress) {
    lcd.setTextColor(TFT_YELLOW); lcd.drawString("UPDATING FIRMWARE", 10, 60);
    lcd.setTextSize(1); lcd.drawString("Don't unplug the board.", 10, 90);
    uiDirty = false; return;
  }
  if (runMode == RM_DIAG) {
    lcd.setTextColor(TFT_ORANGE);
    lcd.drawString("DIAGNOSTICS MODE", 10, 60);
    lcd.setTextSize(1); lcd.setTextColor(TFT_LIGHTGREY);
    lcd.drawString("Sign scan stopped, pins released.", 10, 90);
    lcd.drawString("Use the Windows app to probe pins.", 10, 105);
  } else {
    lcd.setTextColor(signPower ? TFT_CYAN : TFT_DARKGREY);
    lcd.drawString(signPower ? "SIGN ON" : "SIGN OFF", 10, 60);
    lcd.setTextSize(1); lcd.setTextColor(TFT_LIGHTGREY);
    lcd.drawString("Showing: " + contentDescribe(), 10, 90);
    lcd.drawString("Tap to skip to the next item", 10, 105);
  }
  int y = 140;
  lcd.setTextColor(TFT_GREENYELLOW);
  if (WiFi.status() == WL_CONNECTED) {
    lcd.drawString("WiFi: " + conf("ssid"), 10, y); y += 15;
    lcd.drawString("IP: " + WiFi.localIP().toString() + "  (" + conf("host") + ".local)", 10, y); y += 15;
  } else if (conf("ssid").length()) { lcd.drawString("WiFi: connecting to " + conf("ssid"), 10, y); y += 15; }
  if (wifiApActive()) { lcd.drawString("Hotspot: " + wifiApName() + "  IP " + WiFi.softAPIP().toString(), 10, y); y += 15; }
#if ENABLE_BT
  lcd.drawString(btActive ? "Bluetooth: " + conf("bt_name") : String("Bluetooth: off"), 10, y); y += 15;
#endif
  char tb[32];
  if (timeValid()) { time_t t = time(nullptr); struct tm lt; localtime_r(&t, &lt); strftime(tb, sizeof(tb), "%a %d %b %H:%M", &lt); lcd.drawString(String("Time: ") + tb, 10, y); y += 15; }
  lcd.setTextColor(TFT_DARKGREY);
  lcd.drawString("fw " FW_VERSION "  app port " + String(CMD_PORT), 10, y + 10);
  uiDirty = false;
}

// ============================================================================
// JSON helper for replies (one JSON object per line)
// ============================================================================
String jsonEsc(const String& s) {
  String o; o.reserve(s.length() + 4);
  for (size_t i = 0; i < s.length(); i++) {
    char c = s[i];
    if (c == '"' || c == '\\') { o += '\\'; o += c; }
    else if (c == '\n') o += "\\n";
    else if ((uint8_t)c < 0x20) { char b[8]; snprintf(b, sizeof(b), "\\u%04x", c); o += b; }
    else o += c;
  }
  return o;
}

struct J {
  String s = "{\"ok\":true";
  J& str(const char* k, const String& v) { s += ",\""; s += k; s += "\":\""; s += jsonEsc(v); s += '"'; return *this; }
  J& num(const char* k, long v)          { s += ",\""; s += k; s += "\":"; s += v; return *this; }
  J& dbl(const char* k, double v)        { s += ",\""; s += k; s += "\":"; s += String(v, 2); return *this; }
  J& flag(const char* k, bool v)         { s += ",\""; s += k; s += "\":"; s += v ? "true" : "false"; return *this; }
  J& raw(const char* k, const String& v) { s += ",\""; s += k; s += "\":"; s += v; return *this; }
  String done() { return s + "}"; }
};

String err(const String& m) { return "{\"ok\":false,\"err\":\"" + jsonEsc(m) + "\"}"; }
String intList(const int* a, size_t n) { String s = "["; for (size_t i = 0; i < n; i++) { if (i) s += ','; s += a[i]; } return s + "]"; }

// ============================================================================
// Command handling
// ============================================================================
Stream* replyTo = &Serial;

void sendEvent(const String& json) {
  Serial.println(json);
  tcpSendEvent(json);
#if ENABLE_BT
  if (btActive && SerialBT.hasClient()) SerialBT.println(json);
#endif
  mqttEvent(json);
}

String cfgJson() {
  J j;
  j.num("scan_rows", cfg.scanRows).num("refresh_hz", cfg.refreshHz).num("brightness", cfg.brightness)
   .num("pin_data_top", cfg.pinDataTop).num("pin_data_bot", cfg.pinDataBot)
   .num("pin_clk", cfg.pinClk).num("pin_lat", cfg.pinLat).num("pin_oe", cfg.pinOe)
   .num("pin_a", cfg.pinAddr[0]).num("pin_b", cfg.pinAddr[1]).num("pin_c", cfg.pinAddr[2]).num("pin_d", cfg.pinAddr[3])
   .flag("oe_active_low", cfg.oeActiveLow).flag("data_invert", cfg.dataInvert).flag("addr_invert", cfg.addrInvert)
   .flag("reverse_cols", cfg.reverseCols).flag("chain_bottom_first", cfg.chainBottomFirst)
   .flag("configured", cfg.configured).num("bright_cap", conf("bright_cap").toInt());
  return j.done();
}

String setKey(String key, const String& val) {
  key.toLowerCase();
  long v = val.toInt();
  bool b = (val == "1" || val.equalsIgnoreCase("true") || val.equalsIgnoreCase("on"));
  int8_t* pinField = nullptr;
  if      (key == "pin_data_top") pinField = &cfg.pinDataTop;
  else if (key == "pin_data_bot") pinField = &cfg.pinDataBot;
  else if (key == "pin_clk")      pinField = &cfg.pinClk;
  else if (key == "pin_lat")      pinField = &cfg.pinLat;
  else if (key == "pin_oe")       pinField = &cfg.pinOe;
  else if (key == "pin_a")        pinField = &cfg.pinAddr[0];
  else if (key == "pin_b")        pinField = &cfg.pinAddr[1];
  else if (key == "pin_c")        pinField = &cfg.pinAddr[2];
  else if (key == "pin_d")        pinField = &cfg.pinAddr[3];
  if (pinField) {
    if (v != -1 && !isOutPin(v)) return "GPIO " + val + " can't be used as a sign output";
    *pinField = v; return "";
  }
  if (key == "scan_rows")  { if (v != 8 && v != 16) return "scan_rows must be 8 or 16"; cfg.scanRows = v; return ""; }
  if (key == "refresh_hz") { if (v < 30 || v > 400) return "refresh_hz must be 30..400"; cfg.refreshHz = v; return ""; }
  if (key == "brightness") { if (v < 1 || v > 100) return "brightness must be 1..100"; cfg.brightness = v; updateBrightness(); return ""; }
  if (key == "bright_cap") { if (v < 1 || v > 100) return "bright_cap must be 1..100"; confSet("bright_cap", String(v)); updateBrightness(); return ""; }
  if (key == "oe_active_low")      { cfg.oeActiveLow = b; return ""; }
  if (key == "data_invert")        { cfg.dataInvert = b; return ""; }
  if (key == "addr_invert")        { cfg.addrInvert = b; return ""; }
  if (key == "reverse_cols")       { cfg.reverseCols = b; return ""; }
  if (key == "chain_bottom_first") { cfg.chainBottomFirst = b; return ""; }
  return "unknown key " + key;
}

String nextToken(String& rest) {
  rest.trim();
  int sp = rest.indexOf(' ');
  String t = sp < 0 ? rest : rest.substring(0, sp);
  rest = sp < 0 ? "" : rest.substring(sp + 1);
  return t;
}

int hexNib(char c) {
  if (c >= '0' && c <= '9') return c - '0';
  c |= 0x20;
  if (c >= 'a' && c <= 'f') return c - 'a' + 10;
  return -1;
}

bool parseHexFrame(const String& hex, uint8_t* out) {
  if (hex.length() != FB_BYTES * 2) return false;
  for (int i = 0; i < FB_BYTES; i++) {
    int h = hexNib(hex[i * 2]), l = hexNib(hex[i * 2 + 1]);
    if (h < 0 || l < 0) return false;
    out[i] = (h << 4) | l;
  }
  return true;
}

void setRunMode(RunMode m) {
  if (m == RM_DIAG) { stopScan(); probeAllOff(); releaseSignPins(); runMode = RM_DIAG; }
  else { probeAllOff(); runMode = RM_RUN; startScan(); }
  uiDirty = true;
}

String confJson() {
  J j;
  for (int i = 0; i < CONF_N; i++) {
    if (CONF_KEYS[i].secret) j.flag((String("has_") + CONF_KEYS[i].key).c_str(), confVal[i].length() > 0);
    else j.str(CONF_KEYS[i].key, confVal[i]);
  }
  return j.done();
}

String handleCommand(String line) {
  String rest = line;
  String cmd = nextToken(rest);
  cmd.toUpperCase();

  if (cmd == "PING") return J().str("pong", FW_VERSION).done();

  if (cmd == "INFO") {
    J j;
    j.str("fw", FW_VERSION).str("chip", ESP.getChipModel()).num("heap", ESP.getFreeHeap())
     .num("uptime_s", millis() / 1000).str("mode", runMode == RM_DIAG ? "diag" : "run")
     .flag("scanning", scanning).flag("power", signPower).str("showing", contentDescribe())
     .raw("out_pins", intList(OUT_PINS, sizeof(OUT_PINS) / sizeof(int)))
     .raw("in_only_pins", intList(IN_ONLY, sizeof(IN_ONLY) / sizeof(int)))
     .raw("strap_pins", intList(STRAP_PINS, sizeof(STRAP_PINS) / sizeof(int)))
     .num("sign_w", SIGN_W).num("sign_h", SIGN_H).num("cmd_port", CMD_PORT)
     .num("fs_free", fsFree()).num("sketch_free", ESP.getFreeSketchSpace()).flag("time_valid", timeValid())
     .str("ip", WiFi.localIP().toString()).str("host", conf("host"));
#if ENABLE_BT
    j.flag("bt_supported", true);
#else
    j.flag("bt_supported", false);
#endif
    return j.done();
  }

  if (cmd == "GET") {
    String what = nextToken(rest); what.toUpperCase();
    if (what == "CFG" || what == "") return cfgJson();
    if (what == "HDR") return J().str("hdr", prefs.getString("hdr", "")).done();
    return err("GET CFG or GET HDR");
  }

  if (cmd == "SET") {
    String key = nextToken(rest); rest.trim();
    String e = setKey(key, rest);
    if (e.length()) return err(e);
    if (runMode == RM_RUN && !key.startsWith("bright")) startScan();
    return cfgJson();
  }

  if (cmd == "SAVE")  { cfg.configured = 1; saveSignConfig(); return J().str("saved", "cfg").done(); }
  if (cmd == "DEFAULTS") { bool wasRun = runMode == RM_RUN; stopScan(); releaseSignPins(); cfgDefaults(); updateBrightness(); if (wasRun) startScan(); return cfgJson(); }

  if (cmd == "CONF") {
    String sub = nextToken(rest); sub.toUpperCase();
    if (sub == "GET" || sub == "") return confJson();
    if (sub == "SET") {
      String key = nextToken(rest); key.toLowerCase(); rest.trim();
      if (key == "ap_pass" && rest.length() && rest.length() < 8) return err("hotspot password must be at least 8 characters");
      if (!confSet(key, rest)) return err("unknown setting " + key);
      confApplied(key);
      return J().str("key", key).str("note", (key == "host" || key == "ap_pass" || key == "bt_name") ? "reboot to apply" : "").done();
    }
    return err("CONF GET or CONF SET <key> <value>");
  }

  if (cmd == "MODE") {
    String m = nextToken(rest); m.toUpperCase();
    if (m == "DIAG") setRunMode(RM_DIAG);
    else if (m == "RUN") { setRunMode(RM_RUN); if (!scanning) return err("scan not started: set pin_clk, pin_lat and pin_data_top first"); }
    else if (m.length()) return err("MODE RUN or MODE DIAG");
    return J().str("mode", runMode == RM_DIAG ? "diag" : "run").flag("scanning", scanning).done();
  }

  if (cmd == "POWER") {
    String m = nextToken(rest); m.toUpperCase();
    if (m == "ON") powerSet(true);
    else if (m == "OFF") powerSet(false);
    else if (m == "TOGGLE") powerSet(!signPower);
    else if (m.length() && m != "STATUS") return err("POWER ON|OFF|TOGGLE|STATUS");
    return J().flag("power", signPower).done();
  }

  if (cmd == "PROBE") {
    int pin = nextToken(rest).toInt();
    String what = nextToken(rest); what.toUpperCase();
    if (what == "OFF") { Probe* p = probeFind(pin, false); if (p) probeRelease(*p); return J().num("gpio", pin).str("probe", "off").done(); }
    char kind = what == "HIGH" ? 'H' : what == "LOW" ? 'L' : what == "PULSE" ? 'P' : 0;
    if (!kind) return err("PROBE <gpio> HIGH|LOW|PULSE [hz] [ms] or OFF");
    float hz = 1000; uint32_t ms = 0;
    String t = nextToken(rest); if (t.length()) hz = t.toFloat();
    t = nextToken(rest); if (t.length()) ms = t.toInt();
    if (kind == 'P' && (hz < 0.2f || hz > 10000000.0f)) return err("pulse hz must be 0.2 .. 10000000");
    if (ms > 120000) ms = 120000;
    String e = probeStart(pin, kind, hz, ms);
    if (e.length()) return err(e);
    return J().num("gpio", pin).str("probe", what).dbl("hz", hz).num("ms", ms ? ms : probeTimeoutMs).done();
  }

  if (cmd == "ALLOFF") { probeAllOff(); return J().str("probe", "all off").done(); }

  if (cmd == "SNIFF") {
    if (runMode != RM_DIAG) return err("switch to DIAG mode first (MODE DIAG)");
    int pin = nextToken(rest).toInt();
    if (!isInPin(pin)) return err("GPIO not allowed as input");
    if (probeFind(pin, false)) return err("that GPIO is currently a probe output, turn it off first");
    String t = nextToken(rest);
    uint32_t ms = t.length() ? t.toInt() : 100;
    ms = constrain(ms, 5, 500);
    SniffResult r = sniff(pin, ms);
    double sps = r.samples * 1000.0 / r.ms;
    double hz = r.edges * 500.0 / r.ms;
    return J().num("gpio", pin).num("ms", r.ms).num("samples", r.samples).num("edges", r.edges)
              .dbl("high_pct", r.samples ? 100.0 * r.highs / r.samples : 0).dbl("hz", hz)
              .dbl("sample_rate", sps).flag("level", r.level).flag("saturated", hz > sps / 8).done();
  }

  if (cmd == "CAPTURE") {   // CAPTURE <samples> <rate_hz, 0 = max> <trigger channel -1..7> <edge: 1 rise, -1 fall, 0 any>
    if (runMode != RM_DIAG) return err("switch to DIAG mode first (MODE DIAG)");
    long n = rest.length() ? nextToken(rest).toInt() : 49152;
    long rate = rest.length() ? nextToken(rest).toInt() : 0;
    int trig = rest.length() ? nextToken(rest).toInt() : -1;
    int edge = rest.length() ? nextToken(rest).toInt() : 1;
    return laCapture(n, rate, trig, edge);
  }

  if (cmd == "FB") {
    static const char* hx = "0123456789abcdef";
    String s; s.reserve(FB_BYTES * 2);
    int b = frontBuf;
    for (int y = 0; y < SIGN_H; y++) for (int x = 0; x < ROW_BYTES; x++) { s += hx[fb[b][y][x] >> 4]; s += hx[fb[b][y][x] & 15]; }
    return J().str("fb", s).str("pattern", contentDescribe()).flag("scanning", scanning).flag("power", signPower).done();
  }

  if (cmd == "HDR") {
    String sub = nextToken(rest); sub.toUpperCase(); rest.trim();
    if (sub == "SET") { if (rest.length() > 3500) return err("too long"); prefs.putString("hdr", rest); return J().num("len", rest.length()).done(); }
    if (sub == "GET") return J().str("hdr", prefs.getString("hdr", "")).done();
    return err("HDR SET <data> or HDR GET");
  }

  if (cmd == "TIME") {
    String sub = nextToken(rest); sub.toUpperCase();
    if (sub == "SET") { timeSetEpoch(strtoul(nextToken(rest).c_str(), nullptr, 10)); }
    else if (sub.length() && sub != "GET") return err("TIME GET or TIME SET <unix seconds>");
    time_t t = time(nullptr); struct tm lt; localtime_r(&t, &lt); char b[32]; strftime(b, sizeof(b), "%Y-%m-%d %H:%M:%S", &lt);
    return J().flag("valid", timeValid()).num("epoch", (long)t).str("local", b).str("tz", conf("tz")).done();
  }

  if (cmd == "WIFI" || cmd == "BT") return netCommand(cmd, rest);

  if (cmd == "REBOOT") { replyTo->println(J().str("reboot", "now").done()); replyTo->flush(); delay(200); ESP.restart(); }

  // Content commands live in content.ino
  String r = contentCommand(cmd, rest);
  if (r.length()) return r;

  return err("unknown command " + cmd + " (try INFO)");
}

// ============================================================================
// Line readers for each transport
// ============================================================================
struct LineReader {
  String buf;
  void poll(Stream& s) {
    while (s.available()) {
      char c = s.read();
      if (c == '\n') {
        String line = buf; buf = ""; line.trim();
        if (line.length()) { replyTo = &s; s.println(handleCommand(line)); }
      } else if (c != '\r') {
        if (buf.length() < 12000) buf += c;
      }
    }
  }
};
LineReader usbReader, btReader;

// ============================================================================
// Scan watchdog: if the scan timer stops firing while the sign should be lit,
// one row could stay on and overheat. Blank the sign and restart the scan.
// ============================================================================
void scanWatchdog() {
  static uint32_t lastCount = 0, lastCheck = 0, trips = 0;
  if (millis() - lastCheck < 250) return;
  lastCheck = millis();
  if (!scanning) { lastCount = scanCount; return; }
  if (scanCount == lastCount) {
    stopScan();
    if (cfg.pinOe >= 0) { oeEnable(false); pinMode(cfg.pinOe, INPUT); }   // pull-up keeps it blanked
    trips++;
    sendEvent(String("{\"evt\":\"watchdog\",\"trips\":") + trips + "}");
    if (trips < 5) startScan();
    else { runMode = RM_DIAG; uiDirty = true; }   // something is badly wrong: stay dark
  }
  lastCount = scanCount;
}

// ============================================================================
#define BOOTLOG(msg) do { Serial.print("BOOT: "); Serial.println(msg); Serial.flush(); } while (0)

void setup() {
  Serial.begin(115200);
  delay(200);
  Serial.println();
  BOOTLOG("casino_sign_driver " FW_VERSION " starting");

  // Screen first, exactly like the screen test, before anything else can get in the way.
  pinMode(LCD_BL_PIN, OUTPUT); digitalWrite(LCD_BL_PIN, HIGH);   // backlight on (plain GPIO, nothing else may use it)
  bool lcdOk = lcd.init();
  digitalWrite(LCD_BL_PIN, HIGH);
  lcd.setRotation(0);
  lcd.fillScreen(TFT_BLACK); lcd.setTextColor(TFT_WHITE); lcd.setTextSize(2); lcd.drawString("Casino Sign: starting...", 10, 10);
  BOOTLOG(lcdOk ? "screen ready" : "screen init FAILED");

  lcd.setTextSize(1); lcd.setTextColor(TFT_LIGHTGREY);
  int y = 40;
  auto step = [&](const char* what) { BOOTLOG(what); lcd.drawString(what, 10, y); y += 12; };

  step("loading settings");
  prefs.begin("sign", false);
  loadSettings();
  updateBrightness();
  liveMutex = xSemaphoreCreateMutex();
  step("mounting storage (first start can take 20 s)");
  fsBegin();

  // Boot in DIAG mode until the app has saved a pin map, so unknown wiring is never driven.
  step(cfg.configured ? "starting sign scan" : "diagnostics mode, releasing sign pins");
  if (cfg.configured) { runMode = RM_RUN; startScan(); }
  else { runMode = RM_DIAG; releaseSignPins(); }

  canvas.setColorDepth(1);
  canvas.createSprite(SIGN_W, SIGN_H);

  step("loading playlist");
  contentBegin();          // loads the playlist, plays the startup animation
  step("starting WiFi and network");
#ifndef QEMU_TEST
  netBegin();              // WiFi, web page, TCP port, discovery, OTA, MQTT, time, data fetcher
#endif
  btApply();
  BOOTLOG("ready");
  drawUi();
  Serial.println("{\"evt\":\"boot\",\"fw\":\"" FW_VERSION "\"}");
}

void loop() {
  usbReader.poll(Serial);
#if ENABLE_BT
  if (btActive) btReader.poll(SerialBT);
#endif
  netLoop();
  probeService();
  scanWatchdog();

  // Touch: in RUN mode, tap skips to the next item.
  static bool wasTouched = false;
  int32_t tx, ty;
  bool touched = lcd.getTouch(&tx, &ty);
  if (touched && !wasTouched && runMode == RM_RUN) { contentNext(); uiDirty = true; }
  wasTouched = touched;

  static uint32_t lastUi = 0;
  if (uiDirty || millis() - lastUi > 10000) { lastUi = millis(); drawUi(); }

  contentLoop();
}
