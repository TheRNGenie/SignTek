// Casino Sign screen test for the Freenove 4" ESP32 board (ST7796, 320x480).
// Does nothing but light the built-in screen in three independent ways, so we can
// tell a screen/hardware problem from a problem in the sign firmware.
//   Stage 1: backlight only (GPIO27 blinks 3 times, then stays on)
//   Stage 2: RED screen drawn by hand (bit-banged, no graphics library, no SPI hardware)
//   Stage 3: GREEN, then BLUE, then WHITE with black text, drawn with LovyanGFX (same setup as the sign firmware)
// Every step is also printed on the USB serial port at 115200 baud.
#define LGFX_USE_V1
#include <LovyanGFX.hpp>

#define ST_VERSION "screen-test 1.0"
constexpr int P_SCK = 14, P_MOSI = 13, P_MISO = 12, P_CS = 15, P_DC = 2, P_BL = 27, P_TOUCH_CS = 33;

class LGFX : public lgfx::LGFX_Device {
  lgfx::Panel_ST7796 _panel;
  lgfx::Bus_SPI      _bus;
public:
  LGFX() {
    { auto c = _bus.config();
      c.spi_host = HSPI_HOST; c.spi_mode = 0; c.freq_write = 27000000; c.freq_read = 16000000;
      c.spi_3wire = false; c.use_lock = true;
      c.pin_sclk = P_SCK; c.pin_mosi = P_MOSI; c.pin_miso = P_MISO; c.pin_dc = P_DC;
      c.dma_channel = SPI_DMA_CH_AUTO;
      _bus.config(c); _panel.setBus(&_bus); }
    { auto c = _panel.config();
      c.pin_cs = P_CS; c.pin_rst = -1; c.pin_busy = -1;
      c.panel_width = 320; c.panel_height = 480;
      c.readable = false; c.invert = false; c.rgb_order = false; c.bus_shared = true;
      _panel.config(c); }
    setPanel(&_panel);
  }
};
LGFX* lcd = nullptr;   // created in stage 3 only, so stage 2 has the pins to itself

void say(const char* s) { Serial.print("SCREENTEST: "); Serial.println(s); }

// ---- Stage 2: bit-banged ST7796 -------------------------------------------
static inline void bbByte(uint8_t b) {
  for (int i = 7; i >= 0; --i) {
    digitalWrite(P_MOSI, (b >> i) & 1);
    digitalWrite(P_SCK, HIGH);
    digitalWrite(P_SCK, LOW);
  }
}
void bbCmd(uint8_t c) { digitalWrite(P_DC, LOW); digitalWrite(P_CS, LOW); bbByte(c); digitalWrite(P_CS, HIGH); }
void bbData(uint8_t d) { digitalWrite(P_DC, HIGH); digitalWrite(P_CS, LOW); bbByte(d); digitalWrite(P_CS, HIGH); }

void bitbangRed() {
  pinMode(P_TOUCH_CS, OUTPUT); digitalWrite(P_TOUCH_CS, HIGH);   // keep the touch chip off the bus
  pinMode(P_CS, OUTPUT); pinMode(P_DC, OUTPUT); pinMode(P_SCK, OUTPUT); pinMode(P_MOSI, OUTPUT);
  digitalWrite(P_CS, HIGH); digitalWrite(P_SCK, LOW);
  bbCmd(0x01); delay(150);                 // software reset
  bbCmd(0x11); delay(150);                 // sleep out
  bbCmd(0xF0); bbData(0xC3);               // unlock command set
  bbCmd(0xF0); bbData(0x96);
  bbCmd(0x36); bbData(0x48);               // memory access: same orientation as Freenove's demo
  bbCmd(0x3A); bbData(0x55);               // 16-bit colour
  bbCmd(0xF0); bbData(0x3C);
  bbCmd(0xF0); bbData(0x69);
  bbCmd(0x29); delay(50);                  // display on
  bbCmd(0x2A); bbData(0); bbData(0); bbData(319 >> 8); bbData(319 & 0xFF);
  bbCmd(0x2B); bbData(0); bbData(0); bbData(479 >> 8); bbData(479 & 0xFF);
  bbCmd(0x2C);
  digitalWrite(P_DC, HIGH); digitalWrite(P_CS, LOW);
  for (uint32_t i = 0; i < 320UL * 480UL; ++i) { bbByte(0xF8); bbByte(0x00); }   // pure red
  digitalWrite(P_CS, HIGH);
}

void setup() {
  Serial.begin(115200);
  delay(300);
  Serial.println();
  say(ST_VERSION " starting. This is NOT the sign firmware; reinstall that when done.");

  say("Stage 1: backlight on GPIO27 blinks 3 times, then stays on.");
  pinMode(P_BL, OUTPUT);
  for (int i = 0; i < 3; ++i) { digitalWrite(P_BL, HIGH); delay(400); digitalWrite(P_BL, LOW); delay(400); }
  digitalWrite(P_BL, HIGH);

  say("Stage 2: screen should turn RED (drawn by hand, no library). Takes a few seconds.");
  uint32_t t = millis();
  bitbangRed();
  Serial.printf("SCREENTEST: Stage 2 done in %lu ms.\n", (unsigned long)(millis() - t));
  delay(2000);

  say("Stage 3: LovyanGFX (same as the sign firmware). Screen should go GREEN, BLUE, then WHITE with text.");
  lcd = new LGFX();
  bool ok = lcd->init();
  Serial.printf("SCREENTEST: lcd.init() returned %s\n", ok ? "true" : "false");
  digitalWrite(P_BL, HIGH);
  lcd->setRotation(0);
  lcd->fillScreen(TFT_GREEN); say("now GREEN"); delay(1500);
  lcd->fillScreen(TFT_BLUE);  say("now BLUE");  delay(1500);
  lcd->fillScreen(TFT_WHITE);
  lcd->setTextColor(TFT_BLACK, TFT_WHITE);
  lcd->setTextSize(3);
  lcd->drawString("SCREEN TEST", 40, 60);
  lcd->drawString("WORKS", 100, 110);
  lcd->setTextSize(2);
  lcd->drawString("Tell Claude what", 60, 200);
  lcd->drawString("colours you saw.", 60, 230);
  say("now WHITE with the words SCREEN TEST WORKS. Test finished; it repeats every 15 s.");
}

uint32_t last = 0;
int n = 0;
void loop() {
  if (millis() - last < 15000) return;
  last = millis();
  static const uint16_t cols[] = {TFT_RED, TFT_GREEN, TFT_BLUE, TFT_WHITE};
  static const char* names[] = {"RED", "GREEN", "BLUE", "WHITE"};
  n = (n + 1) % 4;
  lcd->fillRect(0, 300, 320, 180, cols[n]);
  Serial.printf("SCREENTEST: still running, bottom part of the screen is now %s\n", names[n]);
}
