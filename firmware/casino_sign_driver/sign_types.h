// Types shared by the sketch's .ino files. They live in a header so the Arduino
// IDE's auto-generated function prototypes can see them.
#pragma once
#include <Arduino.h>

constexpr int SIGN_W = 96;
constexpr int SIGN_H = 16;
constexpr int ROW_BYTES = SIGN_W / 8;
constexpr int FB_BYTES = ROW_BYTES * SIGN_H;   // 192

enum RunMode { RM_RUN, RM_DIAG };

// kind: 'H' high, 'L' low, 'P' pulse (LEDC), 'S' slow pulse (software), 0 = free
struct Probe { int pin; char kind; float hz; uint32_t until; bool level; uint32_t lastToggle; int ch; };

struct SniffResult { uint32_t samples, highs, edges, ms; bool level; };

// Runtime sign wiring config (saved in flash)
struct SignConfig {
  uint8_t version;
  uint8_t scanRows;        // 8 or 16
  int8_t  pinDataTop, pinDataBot, pinClk, pinLat, pinOe;
  int8_t  pinAddr[4];      // A, B, C, D
  uint8_t oeActiveLow, dataInvert, addrInvert, reverseCols, chainBottomFirst;
  uint16_t refreshHz;
  uint8_t brightness;      // 1..100 %
  uint8_t configured;      // 0 until the app saves a config; board boots in DIAG mode until then
};

// Live UFC fight data pushed by the app (or typed in by hand)
struct FightData {
  String a, b;             // fighter names
  String event;            // card name
  String status;           // "live", "between", "next", "final"
  String result;           // e.g. "MOICANO KO R2"
  int round = 0, rounds = 3;
  int clockSec = 300;      // seconds left in the round when last updated
  bool running = false;    // count down locally between updates
  uint32_t updatedMs = 0;
};

// Fight Night gambling tally
struct GambleData { float bet = 0, won = 0; String label = "FIGHT NIGHT"; };

// Data fetched from the internet by the background task
struct LiveData {
  String weather;          // "72F CLEAR"
  String ticker;           // "AAPL 344.63 +1.2%   BTC 77,635 +0.9%"
  uint32_t weatherAt = 0, tickerAt = 0;
};

// Per-item render state, reset whenever a new item starts
struct RenderState {
  int32_t x = SIGN_W, x2 = SIGN_W;
  uint32_t t0 = 0, lastStep = 0;
  bool passDone = false;
  int frame = 0; uint32_t frameAt = 0;
  double jackpot = 0;
};
