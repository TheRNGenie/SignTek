using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CasinoSignTool
{
    /// <summary>One page of built-in instructions. Tab = the app tab it belongs to (or null).</summary>
    public class HelpTopic
    {
        public string Id, Title, Tab, Summary, Body;
    }

    /// <summary>
    /// Every instruction for the project lives here, so nothing outside the app is needed.
    /// Markup in Body: "# " heading, "## " sub-heading, "- " bullet, "! " warning line, "> " tip line, blank line = gap.
    /// </summary>
    public static class Help
    {
        public static readonly List<HelpTopic> Topics = new List<HelpTopic>
        {
            new HelpTopic { Id = "overview", Title = "What this project does", Summary = "",
                Body =
@"# What this project does
The casino sign (Neurizon ED0204-24, 96 x 16 full-colour LEDs) has two rows of RGB-02 LED panels. Each row is fed by its own Advanced Row Processor board through a 16-pin grey ribbon connector, and the panels pass the signal along to each other with short 16-pin ribbons. This project replaces the old processor boards with the Freenove 4"" ESP32 touch-screen board, which plugs into the 16-pin input of the first panel of BOTH rows. Every signal is split equally to the two connectors.
The panel signals are 3.3 V or less, the same as the ESP32, so no level shifter is needed: each wire just has a small resistor.

After the one-time setup, everything is wireless: this app finds the sign on your WiFi, and you can change text, playlists, animations and settings, run Fight Night, and even install new firmware without ever plugging in a cable again.

## The big steps (each has its own page in this guide)
- Step 1. Install the firmware on the ESP32 (tab 0). Takes a minute; no other software needed.
- Step 2. Put the ESP32 on your WiFi (WiFi & settings tab). Optional now, but it makes everything else wireless.
- Step 3. Map the 16-pin panel connector (tab 1 and tab 1b): find out which pin does what.
- Step 4. Tell the board how the sign scans (tab 2) and test it.
- Step 5. Wire it up permanently to both panel connectors using the wiring guide (tab 3).
- Step 6. Have fun: Display, Playlist & widgets, Fight Night, Pixel art & animations.

> Follow the pages in order the first time. The ""Where you are"" box on the Start here tab tracks your progress and tells you what to do next.

! The sign runs on mains power. Unplug it from the wall every time you open it, connect or disconnect wires, or touch the boards inside." },

            new HelpTopic { Id = "parts", Title = "Parts and tools you need", Summary = "",
                Body =
@"# Parts and tools you need
## Required
- The Freenove 4"" ESP32 display board and a USB data cable for it (some cables only charge; if Windows doesn't see the board, try another cable).
- Two 16-pin (2 x 8) IDC ribbon cables with plugs, one for each panel row. The short grey ribbons that join the panels are exactly this kind; a spare one or a new 16-pin IDC cable works.
- A small breadboard or perfboard to make the split, and jumper wires (male-to-female are handiest).
- 220 Ω resistors (red-red-brown), two for every signal: one for each panel connector. The wiring guide (tab 3) shows the count.
- Optional: one 10 kΩ resistor (can keep the LEDs dark while the ESP32 starts, once the panel test shows which way round).
- A multimeter with a continuity beeper.

## For pin mapping
- Eight 1 kΩ resistors (brown-black-red) for the logic analyzer channels, and one more for the sniff or probe lead.
- No level shifter (74HCT245) is needed: the panel signals measure 3.3 V or less.

## Nice to have
- Heat-shrink or tape, and labels for the wires." },

            new HelpTopic { Id = "flash", Title = "Step 1. Install the firmware", Tab = "0. Install firmware",
                Summary = "Plug the ESP32 in with USB, pick its port, press Install firmware, then Connect.",
                Body =
@"# Step 1. Install the firmware
A brand-new ESP32 needs no preparation: every ESP32 has a built-in loader, and this app includes everything needed to use it. No Arduino IDE.

## Do this
- Plug the Freenove board into this PC with the USB cable.
- Go to tab 0. Install firmware and press Refresh. Pick the port (it names the USB chip, for example CH340 or CP210x).
- Press Install firmware. The log shows the progress; it takes about a minute.
- When it says Done, press Connect. The board's screen shows DIAGNOSTICS MODE. That's normal: the sign stays dark until it's been mapped and set up.

## If no port shows up
- Try another USB cable (many are charge-only) and another USB socket.
- Windows 10 and 11 usually install the USB driver by themselves within a minute. If the tab says a driver is missing, use the driver link it shows, install, unplug and replug the board, then press Refresh.
- Device Manager > Ports (COM & LPT) lists the board's port when the driver works.

## If installing fails with ""Failed to connect""
- Hold the BOOT button on the board, press and release RESET (or EN), then release BOOT. Press Install firmware again within a few seconds.
- Pick a lower Speed (115200) and try again.
- Close anything else that might be using the port (Arduino IDE, another copy of this app, a serial terminal).

## Later updates go over WiFi
Once the board is on your WiFi and connected in the top bar, use Update over WiFi on the same tab. The board can stay inside the sign. The password is the wireless update password (default casinosign; change it on the WiFi & settings tab). If an update is cut short, the board keeps its old firmware.

> The Erase the whole flash checkbox wipes everything saved on the board (pin map, WiFi, playlists, animations). Only use it for a completely fresh start." },

            new HelpTopic { Id = "wifi", Title = "Step 2. WiFi and wireless control", Tab = "WiFi && settings",
                Summary = "Join your WiFi once over USB; after that the app finds the sign by itself.",
                Body =
@"# Step 2. WiFi and wireless control
## One-time setup (over USB)
- Connect over USB (top bar: pick the COM port, press Connect USB).
- Open the WiFi & settings tab. Press Scan, pick your network, type its password, press Save and connect.
- The ESP32 only supports 2.4 GHz WiFi. If your router has separate 2.4 and 5 GHz names, pick the 2.4 GHz one.
- The status box shows Connected: yes and an IP address. The board's own screen shows the IP too.

## From then on, no cable
- Unplug the USB. Open the app anywhere on the same network and it finds the sign by itself (the Find sign button does the same).
- If finding fails, type the IP from the board's screen into the WiFi box in the top bar and press Connect WiFi. casinosign.local also works on most PCs.
- Everything in the app works over WiFi, including installing firmware (tab 0, Update over WiFi).
- The Sign ON / OFF button in the top bar switches the display on and off, with the startup and shutdown animations.

## Phone control
Open a browser on your phone and go to the sign's IP address (or http://casinosign.local). The page has On / Off, a text box, Play and Next. No app needed.

## If WiFi stops working
If the board can't join your WiFi for 20 seconds, it starts its own hotspot called CasinoSign-XXXX (password casinosign unless you changed it). Join that network from this PC, then connect to 192.168.4.1 in the top bar and fix the WiFi settings. Change the hotspot password on the WiFi & settings tab.

## Home Assistant and MQTT
Optional. Fill in the MQTT box on the WiFi & settings tab with your broker's address (for Home Assistant's Mosquitto add-on, that's the Home Assistant machine's IP, plus the username and password you set for MQTT). The sign then appears in Home Assistant as Casino Sign with a power switch, a text box and a Next button.

## Bluetooth
This firmware leaves Bluetooth out, because Bluetooth and wireless firmware updates don't both fit in the ESP32's memory. WiFi and USB do everything Bluetooth would have." },

            new HelpTopic { Id = "safety", Title = "Before you open the sign: safety", Summary = "",
                Body =
@"# Before you open the sign: safety
! Unplug the sign from the wall before opening it, and every time you connect or move wires. The power supply inside carries mains voltage.
! Never connect anything to a pin you have marked GND or + power except the matching wire. On the panel connector, pin 15 is the only ground pin.
! Never connect a 5 V signal straight to an ESP32 pin. ESP32 pins are 3.3 V only. The panel connector's signal pins measure 3.3 V or less (Logan's sign), so a resistor is all they need; the big capacitors on the panels carry 5 V, so keep wires away from them.

## Grounds
The ESP32 and the sign must share a ground, or nothing works (and signals can do odd things). Join the ESP32's GND to pin 15 of every panel connector you wire to.

## Powering the ESP32
While mapping, power the ESP32 from the PC's USB. For the permanent install the wiring guide (tab 3) shows how to power it from the sign's 5 V supply. Don't connect the PC's USB and the sign's 5 V to the ESP32 at the same time unless the guide says it's safe for your setup.

## The LEDs
The firmware has safety features: a brightness limit (tab 2, Safety max), an automatic 15-second limit on the all-LEDs-on test pattern, and a scan watchdog that blanks the LEDs if the display refresh ever stalls, so a row can't be left stuck on at full power." },

            new HelpTopic { Id = "logic", Title = "Logic analyzer: find CLK, LAT, OE, DATA and rows automatically", Tab = "1b. Logic analyzer",
                Summary = "Record 8 panel pins at once with the ESP32 and let the app work out which is which.",
                Body =
@"# Logic analyzer
The ESP32 can record 8 pins at the same moment, millions of times a second, and the app then works out from the timing which pin is the clock (CLK), the latch (LAT), blanking (OE), pixel data (DATA) and the row lines (A, B, C, D). This is much faster than sniffing one pin at a time, and it can tell a clock from a data line, which sniffing can't.
Needs sign firmware 3.1.0 or newer (tab 0).

## Wiring (8 channels)
Each channel: panel connector pin > 1 kΩ resistor > ESP32 pin. One extra wire joins ESP32 GND (the GND pin of the I2C plug) to panel pin 15. No level shifter: the panel signals stay under 3.3 V.
- Channel 1: GPIO 35, IO plug (the pin your sniff wire is already on).
- Channel 2: GPIO 39, IO plug, the other pin.
- Channel 3: GPIO 25, I2C plug, the pin next to GND.
- Channel 4: GPIO 32, I2C plug, the pin next to 3V3.
- Channels 5 to 8: the SPI plug pins printed IO21(CS), IO18(SCK), IO19(MISO), IO23(MOSI) (the SD card slot must be empty). Channel 5 needs firmware 3.2.1 or newer.
! Go by the labels printed on the board next to each plug, and check the ground wire is on the pin printed GND. Without a ground connection the channels show jumbled signals that change from capture to capture.
Press Show wiring picture in the tab for a drawing of every wire. The clock (panel pin 16) goes on channel 5 for the Sync speeds.

## Using it
- 1. Pick the pins. The tab shows your pin map as the 16-pin socket: black = GND, red = 3V3 (never clip these), grey = dead, white = busy but not identified yet, coloured = identified by the analyzer, ✓ = already recorded. Press Import a map file… to load a map you saved from tab 1 (it becomes the map in tab 1 too). Press Pick the next 8 pins to check to fill the channels automatically, or click pins on the socket picture to put them on channels (click again to take one off). You can also type header pin numbers by hand.
- 2. Power the sign normally so it shows its usual message. Connect the ESP32 by USB.
- 3. Speed Fastest, press Capture. Over USB it takes about 6 seconds to arrive.
- 4. The Result column and the text box show the guess for each channel, with the reason. Press Put results in the pin map to save them in tab 1.
- 5. Press Pick the next 8 pins to check again. Once CLK and LAT are found it keeps them on channels 1 and 2 and puts 6 pins that haven't been recorded yet on the rest. Move the clips to match and capture again. The line under the socket shows how many pins are left.
- 6. For the row lines (A, B, C) and OE, also capture at 100 kHz: many rows fit into one capture, so the slow on/off pattern is clear.
- Turbo speed records roughly 2 to 3 times faster than Fastest, but only on channels 3, 5, 6, 7 and 8 (GPIO 25, 21, 18, 19, 23), and for a shorter time (16,383 samples, about 2 ms). Use it when a pin changes faster than Fastest can follow: Fastest shows it as random single-sample blips. Needs firmware 3.1.1 or newer.

## How it decides
- CLK: the busiest line, ticking in bursts with pauses between.
- DATA: changes during the bursts, in step with the clock.
- LAT: one very short pulse in each pause, after a burst.
- OE: switches twice per row, staying mostly in one state.
- A, B, C, D: change only in the pauses; A once per row, B every 2 rows, C every 4, D every 8.
- Several DATA lines are normal: big signs feed each panel (or top and bottom half) separately.

## Reading the picture
Each coloured line is one channel: up = high (3.3 V), down = low. Scroll the mouse wheel to zoom in on a spot, drag to move. A solid block means the line is changing faster than the screen can show; zoom in to see the individual ticks.

## If it finds nothing
- Nothing moves at all: the sign isn't running, the grounds aren't joined, or the resistors don't reach the right ESP32 pins. Test with the sniff steps in tab 1 first.
- No clock bursts at Fastest: the clock isn't on any channel yet. Clip other busy pins.
- The note says the clock is as fast as the ESP32 can record: the guesses are still useful, but a few clock ticks can be missed." },

            new HelpTopic { Id = "mapper", Title = "Step 3. Map the 16-pin panel connector", Tab = "1. Pin mapper",
                Summary = "Find out what each pin of the panels' 16-pin grey ribbon carries, so the ESP32 can drive both rows directly.",
                Body =
@"# Step 3. Map the 16-pin panel connector
## Why this connector
Inside the sign, an Advanced Row Processor board sits on top of the first panel of each row (two rows, so two processors). Each one feeds its row of RGB panels through a 16-pin connector, and the panels pass the signal on to each other with short grey 16-pin ribbons. The signal enters at the right end of each row and moves left.
Each panel buffers its input with two 74HC244 chips (U2, U3) and feeds it to Neurizon's own programmable chip (labelled RGB02-01-PLD). The output to the next panel comes from U34, U24 and U29. The format is Neurizon's own, so it has to be recorded from a running sign; it can't be looked up.
The plan: the ESP32 replaces both row processors and feeds the 16-pin input of the first panel in each row, with the same signals going to both rows. Leave the row processors in until the map is finished: while they run, they show us the real signals.

## What we know so far (Logan's sign)
- Pin 1 = row C, pin 3 = row A, pin 4 = row B. 1/8 scan: a new row every 32.5 µs.
- Pin 5 = blanking (OE): high about 61% of the time, low once per row.
- Pin 6 = a short pulse once per row: most likely the latch (LAT).
- Pin 16 = a steady clock of about 7.6 MHz that never stops.
- Pin 15 = ground (the only one; confirmed with the beep test).
- Pin 7 = pixel data: about 192 bits per row (96 columns for two rows at once), repeating every 8 rows. With a red-only message it is probably the red data.
- Pins 8, 10, 11 = quiet while the sign shows red only: probably the green and blue data. Still to be confirmed.
- Pins 9, 13 and 2 = one low pulse per row right after the latch, with a length that changes: probably brightness timing for each colour. Pins 9 and 13 always carry the same signal.
- Pin 12 = almost silent, pin 14 = always high. Leave them unconnected.
> A swapped wire caused confusing results for a while: two pairs of analyzer wires were crossed at the ESP32. The pull-one-clip test below found it in one round.

## Step 1. Voltages (sign on)
! Mains power is inside the case. Keep hands and probes away from the power supply and its terminals.
- 1. Sign OFF. Pull off the grey ribbon between the first panel (the one with the row processor on top) and the next panel. Note which side has the red stripe: that is pin 1.
- 2. Find pin 1 on the RIGHT-hand header (the one next to U34, U24, U29; it is the first panel's output). Look for a small triangle or a 1 printed beside it. Odd pins are on one row, even pins on the other.
- 3. Sign ON. Multimeter on DC volts (20 V range). Black probe on the minus leg of one of the big black 1000 µF capacitors: the leg on the striped side.
- 4. In tab 1, click pin 1. Touch the red probe to pin 1, type the reading in the Voltage box and press Enter: the app moves to pin 2. Do all 16.
> Reading meanings: about 0 V = ground or a line sitting low. About 3.3 V = a line sitting high. Anything in between = a busy signal line (a multimeter shows the average of a fast on/off signal).

## Step 2. Ground pins (sign off)
- 1. Sign OFF. Multimeter on the beep (continuity) setting.
- 2. One probe on the capacitor minus leg, the other on each pin that read about 0 V. Beeps = GND: press GND in tab 1.
- 3. Never connect the ESP32 to GND or power pins except with the ground wire.

## Step 3. Record the busy pins (tab 1b, Logic analyzer)
- 1. Wire each analyzer channel with a single 1 kΩ resistor (brown-black-red). Don't use 10 kΩ: the panel's high level is only about 2.5 V, and 10 kΩ rounds off the fast edges so fast lines flicker.
- 2. Join ESP32 GND (the I2C plug pin printed GND) to pin 15.
- 3. Push female jumper wires onto the right-hand header pins (ribbon still off).
- 4. Sign on. Logic analyzer tab: put the pin numbers on the channels, choose a speed and press Capture. Press Put results in the pin map.
- 5. Press Save my captures as one file (to send) and keep it: it holds everything needed to work out the signal format.

## Not sure which clip goes to which channel? Pull one at a time
- 1. Capture once with everything connected.
- 2. Pull one clip off the sign, capture, push it back. Do the next one.
- 3. The channel that goes flat (or changes) is the one that clip is on. If it isn't the channel you expected, two wires are crossed.

## Speeds
- Fast 10 MHz (firmware 3.2.0 or newer): all 8 channels, 10 million times a second, for 3 ms. The board checks itself on every capture. It uses GPIO 16 and 17 (the RGB LED pads) for its own clock, so the small RGB LED flickers; nothing needs to be wired to them.
- Sync to sign clock (firmware 3.3.0): pin 16 (the clock) MUST be on channel 5 (the SPI plug pin printed IO21). The board then takes one sample on every tick of the sign's own clock, so nothing is missed. Capture with rising edge and with falling edge, and send both.
- If the sign can show a single colour (or does briefly when it starts up), capture during it: the busy data pin is that colour.

## Driving the panels later
- The panels' supply is 5 V (across the big capacitors), but every signal pin is 3.3 V or less and the input chip U2 runs at about 2.4 V (its pin 20). So no level shifter is needed.
- Tab 3 (Wiring guide) shows the final wiring: each ESP32 output splits into two 220 Ω resistors, one to each row's panel connector." },

            new HelpTopic { Id = "paneltest", Title = "Panel test: drive panel 2 from the ESP32", Tab = "1c. Panel test",
                Summary = "Send test patterns from the ESP32 into panel 2's input to check the signal format (firmware 3.4.0).",
                Body =
@"# Panel test: drive panel 2 from the ESP32
The recordings showed how the row processor talks to the panels. This test does the same from the ESP32 with simple patterns, so we can check it on the real panels before building the full display driver. Panel 1 keeps showing the row processor's message; panel 2 and the panels after it show the ESP32's pattern.
Needs sign firmware 3.4.0 or newer (tab 0).

## Wiring (sign unplugged)
- Leave the grey ribbon between panel 1 and panel 2 unplugged, and leave both row processors in.
- Wire to the SECOND panel's INPUT header: the LEFT header of the pair (next to U2 and U3), where the other end of that ribbon plugged in. Same layout as the right-hand header: ground (15) and clock (16) at the BOTTOM, pins 1 and 2 at the top. Looking at it, ground (15) is bottom-RIGHT and clock (16) bottom-left, so odd pins are in the right-hand column (checked with the beep test: the bottom-right pin beeps to the big capacitor's minus leg).
! Do NOT wire to the header next to U34, U24 and U29. That is panel 1's output; it is already driven and the two would fight.
- Each signal: ESP32 pin > 220 Ω resistor (red-red-brown) > panel pin. Ground: ESP32 GND (I2C plug) > panel pin 15, no resistor. No 10 kΩ on OE for this test.
- Pin 16 CLK > GPIO 18 (SPI plug IO18). Pin 6 LAT > GPIO 19 (SPI IO19). Pin 5 OE > GPIO 21 (SPI IO21).
- Pin 3 row A > GPIO 32 (I2C IO32). Pin 4 row B > GPIO 25 (I2C IO25). Pin 1 row C: for the first test, 220 Ω to GND (no soldering; only half the rows light). Later > GPIO 0 (BOOT button pad).
- Pin 7 data 1 > GPIO 23 (SPI IO23). Pin 8 data 2 > GPIO 22 (RGB LED pad). Pin 10 data 3 > GPIO 17 (RGB LED pad). Pin 11 data 4 > GPIO 26 (speaker pad).
- Pins 9 and 13 together > GPIO 16 (RGB LED pad). Pin 2 > GPIO 4 (speaker pad). Pins 12 and 14: leave unconnected.
> Pins 16, 6, 5, 3, 4, 1 and 7 are enough to start. The ESP32's plugs give 6 outputs (IO18, 19, 21, 23, 32, 25; IO35 and IO39 can only listen), so everything else needs soldering to board pads. Add those only when a step asks for them.

## Running it
- Plug the sign in, connect the ESP32 (top bar), open tab 1c and press Start test. Start with All on and only the pin 7 data line ticked.
- Every change on the right is sent straight away while the test runs. Stop ends it and lets go of the pins.

## What to look for
- All on: panel 2 onward should light. Tick one data line at a time: the colour you see is the colour that line carries.
- One bit with the bit buttons: a single dot or column moves. Note which column each bit number lands in and which way it moves.
- Only row: one row address at a time, to check A, B and C.
- Checker: a chess-board. Stripes instead mean the bits are out of step.
- Write down (or photograph) what each test shows.

## If nothing lights
- Check ground (pin 15) and that you used panel 2's input header.
- Tick OE inverted. Then try Latch inverted.
- Wire pins 9+13 and 2 and set Timing pins to Always low, then Always high.
- Set Slow down to 50: the ESP32 then sends the clock more slowly, in case the wires are too long for full speed.
- If the board says it doesn't know PTEST, install firmware 3.4.0 or newer in tab 0." },

            new HelpTopic { Id = "uno", Title = "Uno R4: drive panel 2 without soldering", Tab = "1d. Uno R4",
                Summary = "Install the Uno firmware from the app, wire 12 signals through 1 kΩ resistors, send test patterns.",
                Body =
@"# Uno R4: drive panel 2 without soldering
The UNO R4 WiFi (Arduino or Elegoo) has enough header pins for all 12 panel signals, so nothing needs soldering. Tab 1d installs its firmware and sends it test patterns. It will also play back your recording from tab 1b once that is added. The ESP32 isn't used on this tab.

## Install the Uno firmware (once)
- Plug the Uno into the PC with a USB-C data cable.
- Tab 1d: press Refresh, pick the port marked likely the Uno, press Install Uno firmware. It takes about 20 seconds.
- If it fails: press the Uno's RESET button twice quickly (the L light fades in and out), press Refresh, pick the port again and press Install.

## Wiring (sign and Uno unplugged)
- Wire into the end of your ribbon plug as before, and plug it onto panel 2's input header. The grey ribbon stays off panel 1, and the row processors stay in.
- Every signal: Uno pin > 1 kΩ resistor (brown-black-red) > panel pin. Ground: Uno GND (top row, next to 13) > panel pin 15, no resistor.
! Use 1 kΩ, not 220 Ω. The Uno's outputs are 5 V, and the 1 kΩ keeps the panel's inputs safe.
- 2 > pin 16 clock. 3 > pin 6 latch. 4 > pin 5 OE. 5 > pin 7 data 1. 6 > pin 9 timing, plus a link from 9 to 13. 7 > pin 2 timing 2.
- 10 > pin 8 data 2. 11 > pin 4 row B. 12 > pin 1 row C. 13 > pin 3 row A. A4 > pin 10 data 3. A5 > pin 11 data 4.
- Take out the old test resistors. Pins 12 and 14 stay empty.
> The tab has a wiring picture and a photo of the Uno with every socket circled.

## Test
- Plug the Uno into the PC, press Connect, then plug the sign in.
- Press Start test with All on. Panel 2 onward should light.
- Nothing lights? Try one at a time: Clock edge = Falling; OE inverted; Latch inverted; Timing pins Always low, then Always high; Slow down = 20.
- Photograph anything that lights and note which settings were on." },

            new HelpTopic { Id = "setup", Title = "Step 4. Sign setup and first light", Tab = "2. Sign setup",
                Summary = "Pick an ESP32 GPIO for each signal, set the scan type, Apply, check the test patterns, Save.",
                Body =
@"# Step 4. Sign setup and first light
This tab tells the firmware which ESP32 pin drives each sign signal and how the sign scans.

## Do this
- Make sure both row processors are unplugged from their panels (the ESP32 replaces them).
- For each signal (DATA, CLK, LAT, OE, A, B, C, D), pick an ESP32 GPIO. The defaults work on the Freenove board; the tab warns you if a pin clashes with the screen, touch or another signal.
- Wire those GPIOs to both panel connectors (tab 3 shows exactly which wire goes where).
- Scan rows: 8 (1/8 scan, address lines A B C) or 16 (1/16 scan, A B C D).
- Press Apply (try it now). Then press the test pattern buttons on the right of the same tab: Corners + arrow (a dot top-left, a dot bottom-right and an arrow pointing right), Walk row and Walk column (a single line that should move smoothly), All on (brief, to spot dead LEDs) and Scroll text. The right side of the tab also says what to change if a pattern looks wrong.
- When it looks right, press Save to board. From now on the board starts the sign by itself on power-up.

## Fixing the picture
- Nothing lights: check GND (pin 15) is joined on both connectors and the sign's power supply is on. Try toggling ""OE is active-low"".
- Everything inverted (dark where it should be lit): toggle ""Invert data"".
- Rows in the wrong order, doubled or upside down: check the address lines are in the right order (A is the fastest), or toggle ""Reverse row order"". Try the other scan type (8 or 16 rows).
- Picture mirrored left-to-right: toggle ""Reverse columns"".
- Top and bottom halves swapped (1/8 scan with one data line): toggle ""bottom half is shifted first"".
- Flicker: raise Refresh (Hz). Ghosting (a faint copy of the row above): lower Brightness a little; that adds blanking time between rows.

## Brightness and safety
Brightness sets the normal level. Safety max is a hard limit nothing can go above (playlists, Home Assistant, the console). Lower it if the power supply gets warm." },

            new HelpTopic { Id = "wiring", Title = "Step 5. Permanent wiring (both panel rows)", Tab = "3. Wiring guide",
                Summary = "Wire every ESP32 signal to both rows' 16-pin panel connectors, split equally. Print it and follow it.",
                Body =
@"# Step 5. Permanent wiring
The sign has two rows of panels, and each row has its own 16-pin input (where its row processor used to plug in). The ESP32 drives both rows at once: every signal wire splits in two, with one 220 Ω resistor going to each row's connector. The pin numbers are the same on both connectors.

## The picture
The Wiring guide tab draws it: Row 1 (top) connector on the left, the ESP32 in the middle, Row 2 (bottom) connector on the right. Each coloured line is one signal: it leaves the ESP32, splits, and goes through its own 220 Ω resistor to the same pin on both connectors. Press Show breadboard picture for a drawing of the real parts.
- Green rows are confirmed by the recordings. Amber rows (marked ""check first"") are best guesses: wire them last, after the next recordings confirm them.

## Where the pins are on the Freenove board
The sign pins are on the board's SPI plug (IO23, IO19, IO18, IO21) and I2C plug (IO32, IO25), plus solder points: GPIO 0 on the BOOT button pad, the RGB LED pads (IO22, IO16, IO17) and the speaker pads (IO26, IO4) for the extra data and colour lines. The wiring guide shows the location next to each GPIO. Leave the SD card slot empty, because it shares the SPI plug's pins. The screen uses GPIO 2, 12, 13, 14, 15, 27 and 33, so those are never offered for the sign.

## How to use it
- Press Save as printable page and print it, or Copy as text.
- Unplug the sign. Unplug both Advanced Row Processor boards from their panels (keep them, so you can always go back).
- Plug a 16-pin ribbon into the input header of the first panel of each row (where the row processor sat). Red stripe = pin 1.
- On the breadboard, give each signal one row. The ESP32 wire goes into that row, and two 220 Ω resistors go out of it: one to the Row 1 ribbon pin, one to the Row 2 ribbon pin. Tick each checkbox as you go.
- Ground: ESP32 GND to pin 15 of BOTH ribbons.
- A 10 kΩ on the OE GPIO can keep the LEDs dark while the ESP32 starts. Which way it goes (to GND or 3.3 V) depends on the OE polarity, which the panel test (tab 1c) shows. Leave it out until then.
- Leave every pin the guide lists as unused unconnected.

## Before turning it on
- With the power still off, use the multimeter to check no signal wire beeps to GND.
- Plug the sign in. The board starts, its screen shows SIGN ON, and the sign shows the startup animation and then the playlist.

> The two rows get identical signals. If later recordings show each row needs its own picture data, only the data wires change: the guide will then give Row 2 its own data GPIOs." },

            new HelpTopic { Id = "display", Title = "Display: text, patterns, live view", Tab = "Display",
                Summary = "Send a message right now, run test patterns, and watch what the sign is showing.",
                Body =
@"# Display tab
- Scrolling text: type a message and press Show on sign. It shows right away and replaces the playlist until you press Play on the Playlist tab (or Next on the board's screen).
- Speed: how fast text scrolls.
- Brightness: changes it live (it can't go above Safety max).
- Show: Scrolling text, the last picture from Pixel art, and the test patterns used during setup: Corners test (a dot in each corner, checks orientation), Walk row and Walk column (a line moving across, checks rows and columns), and Blank.
- Live preview: a live copy of the 96 x 16 picture, refreshed several times a second, so you can check the content without looking at the sign." },

            new HelpTopic { Id = "playlist", Title = "Playlist, widgets and schedule", Tab = "Playlist && widgets",
                Summary = "Build the loop the sign plays: text, clock, weather, ticker, countdown, jackpot, animations.",
                Body =
@"# Playlist and widgets
The playlist is what the sign shows on its own, top to bottom, then it repeats. It's stored on the board, so it keeps playing with the PC off.

## Editing
- Add: pick a type and press Add. Select an item to edit its settings in the middle column.
- Duration: how many seconds it shows. 0 means automatic (text scrolls once, an animation plays once).
- Up, Down, Duplicate, Remove to arrange items. Show selected now previews one item on the sign.
- Press Save to sign + play to store the playlist on the board. Load from sign brings back what's on the board.

## Item types
- Text: scroll, static, blink or wipe effect.
- Clock: 12 or 24 hour, with seconds or the date. Uses internet time; set the time zone on the right.
- Weather: temperature and conditions for the place set on the right (search a city name to fill in the location).
- Stocks + crypto ticker: the symbols set on the right, like AAPL, TSLA, bitcoin, ethereum. Prices refresh every 5 minutes.
- Countdown: counts down to a date and time with a label (NEW YEAR, FIGHT NIGHT…).
- Jackpot: a casino-style progressive jackpot that keeps climbing, with chasing lights. It's just for show; set the starting amount and rate on the right.
- Animation: any animation stored on the board (Pixel art & animations tab).
- Fight / Bets: the Fight Night screens.

## Overnight schedule
Tick the schedule, set the on and off times (for example 08:00 and 23:30), and press Save settings to sign. The sign plays its shutdown animation at the off time and its startup animation at the on time. The board needs the correct time: it gets it from the internet, and this app also sets it every time it connects.

## Settings on the right
Time zone, weather location and units, ticker symbols and jackpot settings go to the board with Save settings to sign.

> Weather, ticker and clock need the board on WiFi with internet access. If they show dashes, check the WiFi & settings tab." },

            new HelpTopic { Id = "fight", Title = "Fight Night and betting tally", Tab = "Fight Night",
                Summary = "Live UFC round, clock and fighters on the sign, plus tonight's bets vs winnings.",
                Body =
@"# Fight Night
Shows ""FIGHTER vs FIGHTER"", the round and the round clock on the sign during UFC events, and keeps a running tally of tonight's bets.

## Live data
- Tick Live UFC data. The app checks ESPN's public scoreboard every 10 seconds and sends updates to the sign. This PC must stay on with the app open during the event.
- Follow the fight that's on now keeps it on the current bout. Double-click a fight in the list to pin that one instead.
- Press Fight Night mode ON to switch the sign to the fight screen (alternating with the betting tally if that box is ticked). Press it again to go back to the playlist.
- Between updates the sign runs the round clock itself, so it keeps counting smoothly.

## Manual mode (works without internet)
ESPN's feed isn't official and sometimes lags or skips a detail. Untick Live UFC data and use the manual controls: type the fighters, the round and clock, then Start round, Pause, End of round, Next round and Final. Send pushes your changes.

## Betting tally
- Add a row per bet: pick (who or what you bet on), stake in dollars, odds in American format (+150, -200; blank means even money).
- When a fight ends, set the result: won, lost or push. The app works out the payout (stake plus profit) and the totals.
- The sign's Bets screen shows Bet, Won and the net up or down for the night.
- Each night is saved on this PC automatically (one file per date), and the totals are kept on the board.

> This is a personal tally only; the app doesn't place or connect to any bets." },

            new HelpTopic { Id = "art", Title = "Pixel art, animations, GIFs, startup/shutdown", Tab = "Pixel art && animations",
                Summary = "Draw 96 x 16 frames or import images and GIFs, upload animations, choose startup and shutdown animations.",
                Body =
@"# Pixel art and animations
## Drawing
- Left-click draws, right-click erases. The tools across the top: pen, eraser, fill, line, rectangle and filled rectangle. Clear wipes the frame. Text stamps lettering onto the picture.
- ▶ Send to sign shows the current picture on the sign right away.

## Importing images and GIFs
- Import image…: pick a picture. Choose Threshold (crisp, best for logos and text) or one of the dither methods (Floyd-Steinberg, Atkinson, Ordered: dots that fake grey, best for photos), adjust the level and contrast, check the preview, and press Use this.
- GIFs: pick a GIF in Import image… and the app asks whether to import all frames. Yes brings in every frame with its timing as an animation. Small, high-contrast GIFs look best at 96 x 16.

## Animations
- +Frame adds a blank frame, Duplicate copies the current one, Delete removes it. ◀ and ▶ step through frames. ms is how long each frame shows.
- Preview plays it in the app. Give it a name and press Upload to sign. It's stored on the board and can be used in the playlist.
- On the sign lists what's stored: Play, Edit (bring it back into the editor) or Delete.

## Startup and shutdown animations
- Pick an animation for Startup (plays when the sign turns on) and Shutdown (plays before it goes dark), then press Set. (none) turns that animation off.
- The Corvette animation is built in and is the default startup animation. It also plays from Play on this tab, and you can add it to the playlist.
- They play whenever the sign is switched on or off: from the top bar's Sign ON / OFF button, the phone page, Home Assistant, or the overnight schedule.

> The board has room for a few dozen short animations. The On the sign list shows the free space." },

            new HelpTopic { Id = "boardscreen", Title = "The ESP32's own screen", Summary = "",
                Body =
@"# The ESP32's own screen
The Freenove board's 4"" screen shows the status at a glance:
- DIAGNOSTICS MODE (orange): the sign pins are switched off, used while mapping.
- SIGN ON or SIGN OFF: the board is driving the sign (Run mode), with what it's showing now. Tap the screen to skip to the next playlist item.
- UPDATING FIRMWARE: a wireless update is running. Don't unplug the board.
- WiFi network, IP address and name (casinosign.local) to type into this app if it can't find the sign by itself, or the hotspot name if it couldn't join your WiFi.
- The time, once it's been set from the internet or by this app, and the firmware version.

The Diagnostics and Run buttons in this app's top bar switch the mode. The board starts in Run mode once a sign setup has been saved (step 4)." },

            new HelpTopic { Id = "trouble", Title = "Troubleshooting", Summary = "",
                Body =
@"# Troubleshooting
## The app can't find the sign
- Is the PC on the same WiFi network as the sign? Guest networks often block devices from seeing each other.
- Type the IP shown on the board's screen into the top bar and press Connect WiFi.
- If the board's screen shows a hotspot (CasinoSign-XXXX), it couldn't join your WiFi: join the hotspot from this PC, connect to 192.168.4.1, and fix the WiFi settings.
- Windows Firewall may ask whether to allow this app on the network the first time. Allow it on private networks; finding the sign needs it.
- Still nothing: plug in USB and connect that way.

## No COM port / install fails
See Step 1: try another cable, wait for the driver, or hold BOOT while pressing RESET.

## The sign stays dark
- Is the board in RUN mode (top bar)? It starts in Diagnostics until a sign setup is saved.
- Is the sign switched on (top bar Sign ON / OFF)? Is the overnight schedule turning it off?
- Check ESP32 GND reaches pin 15 on both panel connectors.
- Try toggling ""OE is active-low"" in tab 2.
- The board may have stopped the display because of a watchdog trip (see below).

## The picture is scrambled, mirrored or doubled
See Step 4, Fixing the picture.

## ""Scan watchdog"" message
The display refresh stalled and the board blanked the LEDs to protect them, then restarted the refresh. One trip now and then is harmless. If it keeps happening (after 5 trips the board switches to Diagnostics), lower Refresh (Hz) in tab 2 and check the board's power supply.

## Clock, weather or ticker show dashes
The board needs internet: check it's on WiFi (WiFi & settings tab). The clock also needs the right time zone. Stock and crypto symbols must be valid (crypto uses CoinGecko names like bitcoin, ethereum, dogecoin).

## Fight Night shows nothing
ESPN only lists events close to fight day. Check the list on the Fight Night tab; if it's empty or wrong, use manual mode.

## Firmware update over WiFi fails
Check the update password (WiFi & settings tab sets it; tab 0 must match). Keep the PC close to the router. The board keeps its old firmware if an update is cut short, and USB install (tab 0) always works.

## Start completely fresh
Tab 0: tick Erase the whole flash and install again. This wipes the pin map, WiFi, playlists and animations stored on the board. The pin map on this PC is kept." },

            new HelpTopic { Id = "console", Title = "Console commands", Summary = "",
                Body =
@"# Console commands
The box at the bottom of the window shows everything the app and the board say to each other, and lets you type commands yourself. You never need it for normal use, but it's handy for testing. Commands aren't case sensitive. Every command gets a one-line answer.

## Handy ones
- INFO: firmware version, mode, IP, what's showing.
- TEXT HELLO WORLD: show a message now.
- POWER ON / POWER OFF: switch the display (with animations).
- NEXT: skip to the next playlist item. PLAY: go back to the playlist.
- MODE RUN / MODE DIAG: switch modes.
- PATTERN corners (or walkrow, walkcol, allon, off): test patterns. allon switches itself off after 15 seconds.
- SET brightness 50: change brightness.
- ANIM LIST: animations on the board. ANIM PLAY corvette: play one.
- WIFI STATUS / WIFI SCAN: WiFi state and nearby networks.
- CONF GET: all settings. CONF SET key value: change one.
- TIME GET: the board's clock.
- REBOOT: restart the board.

## The same commands work everywhere
Over USB, over WiFi (this app), and over MQTT (send to <prefix>/cmd, the answer comes back on <prefix>/reply)." },
        };

        public static HelpTopic ForTab(string tab) => Topics.FirstOrDefault(t => t.Tab == tab);
        public static HelpTopic Get(string id) => Topics.FirstOrDefault(t => t.Id == id);

        /// <summary>Render a topic's markup into a RichTextBox.</summary>
        public static void Render(RichTextBox box, HelpTopic t)
        {
            box.SuspendLayout();
            box.Clear();
            var baseFont = new Font("Segoe UI", 10f);
            var h1 = new Font("Segoe UI", 15f, FontStyle.Bold);
            var h2 = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            void Add(string text, Font f, Color c, int indent = 0, bool bullet = false)
            {
                box.SelectionStart = box.TextLength; box.SelectionLength = 0;
                box.SelectionFont = f; box.SelectionColor = c;
                box.SelectionIndent = indent; box.SelectionHangingIndent = bullet ? 14 : 0;
                box.SelectionBullet = false;
                box.AppendText((bullet ? "•  " : "") + text + "\n");
            }
            foreach (var raw in t.Body.Replace("\r", "").Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.StartsWith("# ")) Add(line.Substring(2), h1, Color.FromArgb(20, 60, 120));
                else if (line.StartsWith("## ")) { Add("", baseFont, Color.Black); Add(line.Substring(3), h2, Color.FromArgb(30, 30, 30)); }
                else if (line.StartsWith("- ")) Add(line.Substring(2), baseFont, Color.Black, 12, true);
                else if (line.StartsWith("! ")) Add("⚠ " + line.Substring(2), new Font(baseFont, FontStyle.Bold), Color.FromArgb(170, 20, 20));
                else if (line.StartsWith("> ")) Add("Tip: " + line.Substring(2), new Font(baseFont, FontStyle.Italic), Color.FromArgb(0, 100, 60));
                else Add(line, baseFont, Color.Black);
            }
            box.SelectionStart = 0; box.ScrollToCaret();
            box.ResumeLayout();
        }

        static HelpWindow _window;

        /// <summary>Open (or bring forward) the instructions window on a topic.</summary>
        public static void Show(IWin32Window owner, string id)
        {
            if (_window == null || _window.IsDisposed) { _window = new HelpWindow(); _window.Show(owner); }
            _window.Open(id);
            _window.Activate();
        }
    }

    /// <summary>A topic list on the left and the page on the right. Used by the Start here tab and the help window.</summary>
    public class HelpBrowser : UserControl
    {
        readonly ListBox _list;
        readonly RichTextBox _page;
        readonly Button _goTab;
        public Action<string> GoToTab;

        public HelpBrowser()
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            Controls.Add(split);
            _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("Segoe UI", 10f), ItemHeight = 22, DrawMode = DrawMode.OwnerDrawFixed };
            _list.Items.AddRange(Help.Topics.Select(t => (object)t.Title).ToArray());
            _list.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                e.DrawBackground();
                var t = Help.Topics[e.Index];
                bool step = t.Title.StartsWith("Step");
                using (var f = new Font(_list.Font, step ? FontStyle.Bold : FontStyle.Regular))
                    TextRenderer.DrawText(e.Graphics, t.Title, f, new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height),
                        (e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText : SystemColors.ControlText, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            var left = new Panel { Dock = DockStyle.Fill };
            left.Controls.Add(_list);
            left.Controls.Add(new Label { Text = "Instructions", Dock = DockStyle.Top, Height = 26, Font = new Font("Segoe UI", 10f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });
            split.Panel1.Controls.Add(left);

            _page = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, DetectUrls = false, Padding = new Padding(10) };
            var pagePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(14, 8, 8, 8) };
            pagePanel.Controls.Add(_page);
            _goTab = new Button { AutoSize = true, Dock = DockStyle.Bottom, Visible = false, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Height = 34 };
            _goTab.Click += (s, e) => { var t = Current; if (t?.Tab != null) GoToTab?.Invoke(t.Tab); };
            split.Panel2.Controls.Add(pagePanel);
            split.Panel2.Controls.Add(_goTab);
            _list.SelectedIndexChanged += (s, e) => ShowCurrent();
            bool sized = false;
            split.SizeChanged += (s, e) => { if (!sized && split.Width > 700) { sized = true; try { split.SplitterDistance = 290; } catch { } } };
            Load += (s, e) => { if (split.Width > 700 && !sized) { sized = true; try { split.SplitterDistance = 290; } catch { } } if (_list.SelectedIndex < 0) _list.SelectedIndex = 0; };
        }

        HelpTopic Current => _list.SelectedIndex >= 0 ? Help.Topics[_list.SelectedIndex] : null;

        void ShowCurrent()
        {
            var t = Current; if (t == null) return;
            Help.Render(_page, t);
            _goTab.Visible = t.Tab != null && GoToTab != null;
            _goTab.Text = t.Tab != null ? "Open the " + t.Tab.Replace("&&", "&") + " tab →" : "";
        }

        public void Open(string id)
        {
            int i = Help.Topics.FindIndex(t => t.Id == id);
            if (i >= 0) _list.SelectedIndex = i;
            ShowCurrent();
        }
    }

    public class HelpWindow : Form
    {
        readonly HelpBrowser _browser;
        public HelpWindow()
        {
            Text = "Casino Sign Tool: instructions";
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(980, 680);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = true;
            _browser = new HelpBrowser { Dock = DockStyle.Fill };
            Controls.Add(_browser);
        }
        public void Open(string id) => _browser.Open(id);
    }

    /// <summary>The first tab: progress at a glance, what to do next, and every instruction page.</summary>
    public class StartTab : UserControl
    {
        readonly Ctx _ctx;
        readonly Label _where;
        readonly Button _next;
        readonly HelpBrowser _browser;
        string _nextId;
        public Action<string> GoToTab;
        string _fw = "", _ip = "";
        bool _wifiOk, _configured;

        public StartTab(Ctx ctx)
        {
            _ctx = ctx;
            var top = new Panel { Dock = DockStyle.Top, Height = 118, BackColor = Color.FromArgb(255, 249, 225), Padding = new Padding(10, 8, 10, 8) };
            top.Controls.Add(new Label { Text = "Where you are", Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 11f, FontStyle.Bold) });
            _where = new Label { Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5f) };
            _next = new Button { Dock = DockStyle.Right, Width = 260, Font = new Font("Segoe UI", 10f, FontStyle.Bold), BackColor = Color.White };
            _next.Click += (s, e) => { if (_nextId != null) _browser.Open(_nextId); };
            top.Controls.Add(_where);
            top.Controls.Add(_next);
            _where.BringToFront();
            _browser = new HelpBrowser { Dock = DockStyle.Fill };
            Controls.Add(_browser);
            Controls.Add(top);
            _browser.GoToTab = t => GoToTab?.Invoke(t);

            _ctx.StateChanged += async () =>
            {
                if (_ctx.Board.Connected)
                {
                    var r = await _ctx.Try("INFO");
                    if (r != null) { _fw = r.Str("fw"); if (_fw.Length == 0) _fw = r.Str("version"); _ip = r.Str("ip"); }
                    var w = await _ctx.Try("WIFI STATUS");
                    _wifiOk = (w != null && w.Bool("connected")) || _ctx.Board.OverWifi;
                    if (w != null && w.Str("ip").Length > 0) _ip = w.Str("ip");
                }
                Refresh2();
            };
            _ctx.MapChanged += Refresh2;
            _ctx.CfgChanged += () => { _configured = _ctx.Cfg.Configured; Refresh2(); };
            Refresh2();
        }

        void Refresh2()
        {
            if (InvokeRequired) { BeginInvoke((Action)Refresh2); return; }
            bool conn = _ctx.Board.Connected;
            int mapped = _ctx.Map.MappedCount, total = _ctx.Map.PinCount;
            bool mapDone = _ctx.Map.Problems().Count(p => !p.Contains("still unknown")) == 0 && mapped > 0;
            string Mark(bool ok) => ok ? "[✓]" : "[ ]";
            _where.Text =
                $"{Mark(conn)} Board connected     {(conn ? (_ctx.Board.OverWifi ? "over WiFi" : "over USB") + (_fw.Length > 0 ? ", firmware " + _fw : "") : "no (top bar: pick a COM port or press Find sign)")}\n" +
                $"{Mark(_wifiOk)} Board on WiFi       {(_wifiOk ? "yes, " + _ip : conn ? "not yet" : "unknown until connected")}\n" +
                $"{Mark(mapDone)} Pins mapped         {mapped} of {total}{(mapDone ? ", all required signals found" : "")}\n" +
                $"{Mark(_configured)} Sign set up         {(_configured ? "saved on the board" : conn ? "not yet" : "unknown until connected")}";
            string id, label;
            if (!conn) { id = "flash"; label = "Next: install firmware and connect"; }
            else if (!_wifiOk) { id = "wifi"; label = "Next: put the board on WiFi"; }
            else if (!mapDone) { id = "mapper"; label = "Next: map the 16-pin panel connector"; }
            else if (!_configured) { id = "setup"; label = "Next: sign setup"; }
            else { id = "playlist"; label = "All set: build your playlist"; }
            _nextId = id; _next.Text = label + "  →";
        }
    }

    /// <summary>The strip at the top of every tab: one line on what the tab is for, and a button for the full instructions.</summary>
    public static class HelpStrip
    {
        public static Control For(HelpTopic t, IWin32Window owner)
        {
            var p = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.FromArgb(235, 243, 255), Padding = new Padding(6, 3, 6, 3) };
            var btn = new Button { Text = "How to use this tab", Dock = DockStyle.Right, AutoSize = true, BackColor = Color.White, FlatStyle = FlatStyle.System };
            btn.Click += (s, e) => Help.Show(owner, t.Id);
            var lbl = new Label { Text = "ⓘ  " + t.Summary, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            p.Controls.Add(lbl); p.Controls.Add(btn);
            lbl.BringToFront();
            return p;
        }
    }
}
