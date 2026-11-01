# Casino sign command protocol (firmware v3)

The board accepts one text command per line and answers each one with a single line of JSON.

- **USB serial:** 115200 baud.
- **WiFi:** TCP port 3333 (`casinosign.local`, or the IP shown on the board's screen). The newest client wins.
- **MQTT:** publish a command to `<prefix>/cmd`; the answer comes back on `<prefix>/reply` (see the MQTT section).
- **Discovery:** broadcast the UDP text `CASINOSIGN?` to port 3334; every sign answers with a JSON line (`ip`, `port`, `name`, `host`, `fw`, `power`).
- Bluetooth is not in the standard v3 build (it doesn't fit next to the two firmware slots needed for WiFi updates). Build with `-DENABLE_BT=1` and a non-OTA partition scheme to bring it back.

Replies are `{"ok":true,...}` or `{"ok":false,"err":"..."}`. The board can also send event lines on its own, such as `{"evt":"probe_off",...}`, `{"evt":"wifi",...}` or `{"evt":"boot",...}`, `{"evt":"power",...}` or `{"evt":"watchdog",...}`. Anything that isn't JSON is boot or debug output and can be ignored.

## General
| Command | What it does |
|---|---|
| `PING` | Liveness check |
| `INFO` | Firmware version, chip, mode, power, what's showing, free storage, IP, host, whether the clock is set, and the GPIOs you're allowed to use (`out_pins`, `in_only_pins`, `strap_pins`) |
| `POWER ON\|OFF\|TOGGLE\|STATUS` | Switch the display on/off with the startup/shutdown animations (saved) |
| `TIME GET` / `TIME SET <unix seconds>` | Board clock. The app sets it on connect; NTP sets it when there's internet |
| `MODE RUN` / `MODE DIAG` | RUN scans the sign. DIAG stops the scan and releases every sign pin, so you can use the probe tools |
| `REBOOT` | Restart the board |

## Sign config (saved with `SAVE`)
| Command | What it does |
|---|---|
| `GET CFG` | Current config |
| `SET <key> <value>` | Keys: `pin_data_top pin_data_bot pin_clk pin_lat pin_oe pin_a pin_b pin_c pin_d` (GPIO or -1), `scan_rows` (8/16), `refresh_hz` (30-400), `brightness` (1-100), `bright_cap` (1-100, hard limit, saved straight away), `oe_active_low data_invert addr_invert reverse_cols chain_bottom_first` (0/1). In RUN mode the change applies straight away |
| `SAVE` | Store in flash. The board then boots in RUN mode. Until the first SAVE it boots in DIAG mode with the sign pins switched off |
| `DEFAULTS` | Go back to the built-in defaults (doesn't save) |

## Diagnostics (DIAG mode only)
| Command | What it does |
|---|---|
| `PROBE <gpio> PULSE <hz> [ms]` | Square wave on a GPIO. Below 50 Hz it's done in software, so it's slow enough to see. It switches itself off after `ms` (default 10 s, max 120 s) |
| `PROBE <gpio> HIGH\|LOW 0 [ms]` | Hold a level, with the same timeout |
| `PROBE <gpio> OFF` / `ALLOFF` | Release the probe outputs |
| `SNIFF <gpio> [ms]` | Read a pin as an input for 5-500 ms. Returns `hz`, `high_pct`, `edges`, `level`, `sample_rate` and `saturated` (too fast to measure, which is typical for CLK) |
| `HDR SET <text>` / `HDR GET` | Store or fetch the app's header map on the board (`50\|1=GND\|2=CLK\|...`) |

## Content
| Command | What it does |
|---|---|
| `TEXT <message>` | Show a message now (overrides the playlist until `PLAY`) |
| `SPEED <ms>` | Time per scroll step, 5-500 ms |
| `PATTERN allon\|walkrow\|walkcol\|corners\|text\|bitmap\|off` | Test patterns. `allon` switches itself off after 15 s |
| `BITMAP <384 hex>` | 96x16 1-bit picture: rows top to bottom, 12 bytes per row, MSB = leftmost pixel (stored as the `_bitmap` animation). Same layout as Adafruit GFX `drawBitmap` |
| `FB` | What's being sent to the sign right now, as 384 hex characters |
| `SHOW <item or [items]>` | Show one item (or a list) now, as an override |
| `PLAY` | Drop any override and go back to the playlist |
| `NEXT` | Skip to the next item |
| `PLAYLIST GET` / `PLAYLIST SET {"items":[...]}` | The stored playlist |

### Playlist items
Every item has `t` (type) and `d` (seconds; 0 = automatic: text scrolls once, an animation plays once).

| `t` | Fields |
|---|---|
| `text` | `text`, `fx` = `scroll`\|`static`\|`blink`\|`wipe` |
| `clock` | `fmt` = `12`\|`24`, `sec` (0/1), `date` (0/1) |
| `weather` | uses the `weather_*` settings |
| `ticker` | uses `stocks` and `crypto` |
| `countdown` | `label`, `at` (unix seconds) |
| `jackpot` | uses `jackpot_start`, `jackpot_rate` |
| `anim` | `name` of a stored animation |
| `fight` / `gamble` | the Fight Night screens |
| `pattern` | `p` = a pattern name |
| `off` | blank |

## Animations (stored in LittleFS, about 120 KB)
| Command | What it does |
|---|---|
| `ANIM BEGIN <name> <frames>` | Start an upload (name: 1-24 letters, digits, `_`, `-`) |
| `ANIM FRAME <index> <delay ms> <384 hex>` | Send frames in order |
| `ANIM END` | Finish and store |
| `ANIM LIST` | Stored animations, free space, `anim_on`, `anim_off` |
| `ANIM GET <name> <index>` | Read one frame back |
| `ANIM PLAY <name>` / `ANIM DEL <name>` | Play now / delete |

The `corvette` animation is built into the firmware and installed on first boot. `anim_on` / `anim_off` (CONF) pick the startup and shutdown animations.

## Fight Night
| Command | What it does |
|---|---|
| `FIGHT SET {json}` | Any of `a`, `b`, `event`, `status` (`pre`/`live`/`break`/`final`/...), `result`, `round`, `rounds`, `clock` (seconds left), `running` (bool). The board counts the clock down itself between updates |
| `FIGHT GET` / `FIGHT CLEAR` | |
| `GAMBLE SET <bet> <won> [label]` | Tonight's totals (saved) |
| `GAMBLE ADD BET\|WON <amount>` / `GAMBLE RESET` / `GAMBLE GET` | |

## Live data
| Command | What it does |
|---|---|
| `LIVE GET` | Latest weather and ticker text and their age |
| `LIVE REFRESH` | Fetch now (otherwise weather every 15 min and tickers every 5 min, only when the playlist uses them) |

## General settings (CONF, saved straight away)
`CONF GET` lists them (passwords show only as `has_<key>`). `CONF SET <key> <value>` changes one.

| Keys | Meaning |
|---|---|
| `ssid pass host ap_fallback ap_pass` | WiFi, hostname, fallback hotspot |
| `tz` | POSIX time zone (default `PST8PDT,M3.2.0,M11.1.0`) |
| `weather_lat weather_lon weather_units weather_label` | Weather (Open-Meteo) |
| `stocks crypto` | Comma lists (Yahoo symbols, CoinGecko ids) |
| `jackpot_start jackpot_rate` | Jackpot widget |
| `bright_cap anim_on anim_off text_speed` | Display |
| `sched_on sched_start sched_end` | Overnight schedule (`HH:MM`) |
| `mqtt_on mqtt_host mqtt_port mqtt_user mqtt_pass mqtt_prefix mqtt_ha` | MQTT / Home Assistant |
| `ota_pass` | Password for WiFi firmware updates |

## WiFi (saved straight away)
| Command | What it does |
|---|---|
| `WIFI STATUS` | SSID, connected, IP, RSSI, hostname, hotspot state, MAC |
| `WIFI SSID <name>` / `WIFI PASS <password>` then `WIFI CONNECT` | Join a network |
| `WIFI SCAN` | List nearby networks (takes a few seconds) |
| `WIFI FORGET` | Clear the saved network |
| `WIFI HOST <name>` | mDNS name (takes effect after a reboot) |
| `WIFI AP on\|off` | Start a fallback hotspot if WiFi doesn't connect within 20 s (or straight away if no network is set) |
| `WIFI APPASS <8+ chars>` | Hotspot password (default `casinosign`; takes effect after a reboot) |
| `WIFI STATUS` also reports `mqtt_on` and `mqtt_connected` | |
| `BT STATUS` | Reports `supported:false` on the standard build |

## MQTT topics (prefix default `casinosign`)
| Topic | Direction | Payload |
|---|---|---|
| `<prefix>/power/set` | to sign | `ON` / `OFF` |
| `<prefix>/text/set` | to sign | any text |
| `<prefix>/cmd` | to sign | any command above |
| `<prefix>/reply` | from sign | the JSON answer |
| `<prefix>/status` | from sign | `online` / `offline` (retained) |
| `<prefix>/power/state`, `<prefix>/showing`, `<prefix>/event` | from sign | state updates |

With `mqtt_ha` = 1 the sign publishes Home Assistant discovery for a power switch, a text entity and a Next button.

## WiFi firmware updates
- From the app: `POST http://<sign>/update` (multipart, field `firmware`) with header `X-OTA-Pass: <ota_pass>`.
- From the Arduino IDE: the sign shows up as a network port (ArduinoOTA, same password).
- The board's web page at `http://<sign>/` has power, text, play and next buttons for phones.

## CAPTURE (logic analyzer, firmware 3.1.0+)
`CAPTURE <samples> <rate_hz> <trigger_ch> <edge>` (DIAG mode only). Samples 1024-65536 (rounded down to a multiple of 3), rate 0 = as fast as possible, otherwise at least 10000. Trigger channel -1 = start right away, 0-7 = wait up to 0.1 s for an edge (edge 1 rising, -1 falling, 0 any).
Reply: `{"ok":true,"cap":"<base64, one byte per sample>","n":N,"rate":<measured samples/s>,"triggered":bool,"pins":[35,39,25,32,21,18,19,23]}`. Bit k of each byte = level of pins[k].
Special rates: `1` = Turbo (raw GPIO_IN reads, ~13 MSa/s, channels 3 and 5-8 only, max 16383 samples, reply has `"turbo":true`).
`2` / `3` = Fast I2S capture at 20 / 10 MSa/s on all 8 channels (firmware 3.2.0+, max 30000 samples, rounded to a multiple of 6). Uses I2S0 in camera mode; the sample clock is LEDC on GPIO 16 looped back as the I2S pixel clock, and a self-test clock at rate/6 on GPIO 17 is recorded on I2S bit 8. Reply adds `"fast":true`, `"filled"`, `"selftest"` (`ok` / `irregular` / `no clock seen on bit 8`), `"selftest_bad_runs"`, `"selftest_checked"`, `"selftest_edges"`, `"swapped"` (sample order inside each 32-bit DMA word), `"took_us"`. Error `fast capture got no data from I2S` includes the first DMA words.
`4` / `5` = Sync capture (firmware 3.3.0+): no internal clock; the sign clock on channel 5 (GPIO 21) is the I2S pixel clock, one sample per rising (4) or falling (5) edge, up to 30000 samples. Reply adds `"sync":true`, `"clock_gpio"`, `"edge"`, `"filled"`, `"took_us"`, `"clock_hz"` (measured), `"rate"` = clock_hz, and `"note":"clock stopped early"` if fewer than 90% of samples arrived.
