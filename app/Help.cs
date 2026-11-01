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
The casino sign (Neurizon ED0204-24, 96 x 16 LEDs) was driven by its own controller board, the CPU-002. This project replaces the CPU-002 with the Freenove 4"" ESP32 touch-screen board. The ESP32 talks to the sign's LED panels over the same 50-pin ribbon cable, through a small 74HCT245 chip that turns the ESP32's 3.3 V signals into the 5 V the sign expects.

After the one-time setup, everything is wireless: this app finds the sign on your WiFi, and you can change text, playlists, animations and settings, run Fight Night, and even install new firmware without ever plugging in a cable again.

## The big steps (each has its own page in this guide)
- Step 1. Install the firmware on the ESP32 (tab 0). Takes a minute; no other software needed.
- Step 2. Put the ESP32 on your WiFi (WiFi & settings tab). Optional now, but it makes everything else wireless.
- Step 3. Map the 50-pin connector (tab 1): find out which pin does what.
- Step 4. Tell the board how the sign scans (tab 2) and test it.
- Step 5. Wire it up permanently using the generated wiring guide (tab 3).
- Step 6. Have fun: Display, Playlist & widgets, Fight Night, Pixel art & animations.

> Follow the pages in order the first time. The ""Where you are"" box on the Start here tab tracks your progress and tells you what to do next.

! The sign runs on mains power. Unplug it from the wall every time you open it, connect or disconnect wires, or touch the boards inside." },

            new HelpTopic { Id = "parts", Title = "Parts and tools you need", Summary = "",
                Body =
@"# Parts and tools you need
## Required
- The Freenove 4"" ESP32 display board and a USB data cable for it (some cables only charge; if Windows doesn't see the board, try another cable).
- A 74HCT245 chip (it must be HCT, not HC or LS: HCT reads 3.3 V as a valid high while running at 5 V). One chip handles 8 signals; the wiring guide tells you if you need a second one. Don't have one yet? The wiring guide has a ""No level shifter"" option for a test setup without it (it may or may not work reliably).
- A small breadboard or perfboard, and jumper wires (male-to-female are handiest for the ribbon connector).
- A 50-pin IDC connector or the sign's original ribbon cable, so you can plug into the panels without cutting anything.
- A multimeter with a continuity beeper (to find ground and 5 V pins).

## For pin mapping
- Resistors: one 10 kΩ and one 20 kΩ (or two 10 kΩ in series) for the safe ""sniff"" lead, and one 1 kΩ for the ""probe"" lead.
- Optional but very helpful: a cheap USB logic analyzer (8 channel, about $10). It records all the signals at once while the original CPU-002 runs the sign, which can map the bus in minutes.

## Nice to have
- A 100 nF capacitor across the 74HCT245's 5 V and GND pins (pins 20 and 10), as close to the chip as possible.
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
! Never connect anything to a pin you have marked GND or 5V except the matching GND or 5V wire.
! Never connect a 5 V signal straight to an ESP32 pin. ESP32 pins are 3.3 V only. For a 5 V controller use the 10k/20k divider for sniffing and the 74HCT245 for driving. If the controller's pins measure 3.3 V (Logan's V-002 does), sniff through a single 10k and drive with the No level shifter wiring.

## Grounds
The ESP32 and the sign must share a ground, or nothing works (and signals can do odd things). While mapping, connect the ESP32's GND to a sign ground pin you found with the multimeter.

## Powering the ESP32
While mapping, power the ESP32 from the PC's USB. For the permanent install the wiring guide (tab 3) shows how to power it from the sign's 5 V supply. Don't connect the PC's USB and the sign's 5 V to the ESP32 at the same time unless the guide says it's safe for your setup.

## The LEDs
The firmware has safety features: a brightness limit (tab 2, Safety max), an automatic 15-second limit on the all-LEDs-on test pattern, and a scan watchdog that blanks the LEDs if the display refresh ever stalls, so a row can't be left stuck on at full power." },

            new HelpTopic { Id = "mapper", Title = "Step 3. Map the 50-pin connector", Tab = "1. Pin mapper",
                Summary = "Click a pin, test it (multimeter, sniff, or probe), and mark what it is.",
                Body =
@"# Step 3. Map the 50-pin connector
The ribbon cable between the CPU-002 and the LED panels has 50 pins. You need to find which ones carry the signals the sign needs. Most pins are ground or unused; usually only 6 to 10 matter.

## What you're looking for
- GND (ground) and 5V (power): found with the multimeter.
- DATA: the pixel data (sometimes two: top half and bottom half).
- CLK: the clock that shifts data along the row.
- LAT (or LE): the latch that copies the shifted data to the LEDs.
- OE: output enable, blanks the LEDs while rows switch.
- A, B, C (and maybe D): the row address lines that pick which row is lit.

## Using the pin graphic
- Click a pin to select it; arrow keys move between pins.
- Keys: G = GND, V = 5V, N = not connected, or pick a role from the list on the right.
- If the picture is mirrored compared to your connector, tick the flip option. Pin 1 is usually marked with a triangle or a red stripe on the cable.
- The map saves by itself on this PC. Press Send map to board to keep a copy on the ESP32 too.

## Method 1: multimeter (power OFF, finds almost everything)
Set the meter to continuity (beep). With the sign unplugged you can trace every ribbon pin to the chips on the LED boards. No power, no risk.
- GND and 5V: touch one lead to the sign's metal chassis or a power-supply ground and the other to each pin. Beep = GND. Then do the same against the 5 V terminal of the sign's power supply. Beep = 5V.
- No beeps for ground? The metal case often isn't connected to the electronics. Use one of these instead: the power supply's low-voltage terminal marked V−, −, COM or GND (never the mains side marked L / N / AC); pin 1 of an MBI5170 on an LED board; or the striped (minus) leg of a big round capacitor on an LED board. On 50-pin cables many pins are ground, often every other pin. Check the meter first: touching its two leads together should beep.
- Driver chips: find the MBI5170 (16 legs) nearest the ribbon connector. Pin 1 is next to the dot or notch; pins count down one side and back up the other. Beep each ribbon pin against its legs: pin 2 = DATA, pin 3 = CLK, pin 4 = LAT, pin 13 = OE. Pin 1 should beep to GND and pin 16 to 5V, which proves you're counting right.
- CLK, LAT and OE go to every driver chip. DATA only goes to the first chip in a chain (each next chip gets it from the previous chip's pin 14). If a chip on the other half of the sign has its pin 2 on a different ribbon pin, mark that one DATA 2.
- Row lines: beep the ribbon pins against the inputs of the HZ81731G row-select chip. If it uses the usual layout for that kind of chip (check first: pin 8 beeps to GND, pin 16 to 5V), pins 1, 2, 3 are A, B, C. A second row-select chip or a fourth line means D.
- No beep to any chip? There's probably a buffer chip in between: beep the ribbon pin to the buffer, then follow the buffer's matching output pin onward.
- Mark each pin in the app as you find it. Pins that beep to nothing at all are usually not connected (N).

## Method 2: sniff (CPU-002 still connected and running, to confirm or fill gaps)
In plain words: the old controller keeps running the sign. You touch one ribbon pin at a time with a wire that goes to the ESP32, and the ESP32 listens to what that pin is doing. The app guesses what the pin is from the pattern. You only listen; nothing is sent into the sign.
First check your controller's voltage: multimeter on DC volts (20V range), controller powered, black lead on ground (a metal screw or the power cable's black wire), red lead on each ribbon pin. Note the highest reading.
- Highest about 3.3 V (Logan's V-002 reads 3.37 V): the board is 3.3 V, the same as the ESP32. The lead is just ONE 10 kΩ resistor between the probe tip and IO39, with NO resistor to ground. (With the 20 kΩ to ground a 3.3 V signal drops to about 2.2 V, too low for the ESP32 to see, and every pin reads 0 Hz.)
- Highest about 5 V: use the full lead below, 10 kΩ plus 20 kΩ (or two 10 kΩ) to ground, which turns 5 V into about 3.3 V.
- Anything above 5.5 V: stop, don't connect the ESP32 to that pin.
You need: one 10 kΩ resistor (plus a 20 kΩ, or two more 10 kΩ, only for a 5 V board), jumper wires, and a small breadboard if you have one.
- 1. Build the listening lead (sign unplugged). For a 3.3 V board: one 10 kΩ, one end wired to IO39, the other end is the probe tip, done (skip the rest of this step except the ground). For a 5 V board: Put one end of the 10 kΩ and one end of the 20 kΩ into the same breadboard row (the middle point). Wire the middle point to ESP32 GPIO 39 (the pin labelled 39 on the board's IO connector). Wire the free end of the 20 kΩ to ESP32 GND. Put a jumper wire on the free end of the 10 kΩ: that's your probe tip. Tab 1 has Show whole setup (3.3 V, V-002), a drawing of both boards and the breadboard, and Show 5 V version for the divider, and Show where it plugs into the ESP32 for the back of the board. The IO connector has no ground pin: take GND from the GND pin of the I2C connector.
- 2. Share the ground: wire ESP32 GND to a ribbon pin you found to be GND with the multimeter (or to the power supply's V− / COM terminal). Without this the readings mean nothing.
- 3. In the app: connect the ESP32 by USB, go to tab 1, set Sniff GPIO to 39.
- 4. Power the sign the normal way with the CPU-002 still plugged into the panels by its ribbon, so it shows its usual display. Without the ribbon the CPU-002 may get no power and every pin reads 0 Hz. Keep hands away from the power supply.
- 4b. Test the lead first. 3.3 V board: touch the probe tip to the controller pin that reads about 3.3 V on the multimeter and press Sniff; it should say about 100% high. 5 V board: touch a 5 V pin (the ESP32 board's own Serial plug on its bottom edge, first pin, is 5 V; pins are 5V, GND, TX0, RX0); about 100% high. Then touch a GND pin: about 0% high. If the high test says 0%: check the lead goes to IO39, the ground from step 2 is connected, and (3.3 V board) that there's no resistor from IO39 to ground.
- 5. Touch the probe tip to a pin. The easiest spot is the solder joints on the back of the board where the 50-pin socket is soldered. Click the same pin number on the picture and press Sniff this pin (or Space). If the suggestion looks right, press Use suggestion.
- 6. Pick the next pin and repeat. Skip pins already marked GND or 5V.
What the readings mean:
- Very fast and steady: CLK.
- A short blip regularly: LAT.
- A slow on/off pattern: an address line. A is the fastest, B half as fast, C half again.
- A wide on/off signal: OE. Busy and messy: DATA.
- Never changes: not used, or a power pin.

## Method 3: probe (only if needed, CPU-002 UNPLUGGED)
- Build the probe lead: ESP32 probe GPIO (default 25, the SCL pin on the board's I2C connector) > 74HCT245 input, its output > 1 kΩ > test lead. The 74HCT245 needs 5 V on pin 20, GND on pin 10, pin 1 (DIR) to 5 V, pin 19 (OE) to GND.
- Unplug the CPU-002 from the ribbon. Touch the test lead to a pin, press High, Low or Pulse, and watch the sign. The output switches off by itself after the timeout.
- What you'll see: CLK or DATA pulses make random dots or smears; LAT makes the pattern change in steps; OE makes everything flash; an address line moves which row is lit.
! Never probe a pin marked GND or 5V.

## A logic analyzer is the fastest way
If you have one, connect its ground to a sign GND, clip channels onto 8 unknown pins at a time while the CPU-002 runs, and record. In the recording CLK is the fastest regular signal, LAT pulses once after each burst of CLK, OE toggles around LAT, A/B/C count in binary, and DATA changes in step with CLK. Mark what you find in the app.

## Counting the driver chips
Count all the MBI5170 chips across the sign's LED boards. That tells you the scan type for step 4: 24 chips means 1/8 scan (two rows lit at once, 3 address lines A B C); 12 chips means 1/16 scan (one row at a time, 4 address lines A B C D). If you're not sure, the Sign setup tab lets you try both." },

            new HelpTopic { Id = "setup", Title = "Step 4. Sign setup and first light", Tab = "2. Sign setup",
                Summary = "Pick an ESP32 GPIO for each signal, set the scan type, Apply, check the test patterns, Save.",
                Body =
@"# Step 4. Sign setup and first light
This tab tells the firmware which ESP32 pin drives each sign signal and how the sign scans.

## Do this
- Make sure the CPU-002 is unplugged from the ribbon (the ESP32 replaces it).
- For each signal (DATA, CLK, LAT, OE, A, B, C, D), pick an ESP32 GPIO. The defaults work on the Freenove board; the tab warns you if a pin clashes with the screen, touch or another signal.
- Wire those GPIOs through the 74HCT245 to the header pins you found (tab 3 shows exactly which wire goes where).
- Scan rows: 8 (1/8 scan, address lines A B C) or 16 (1/16 scan, A B C D).
- Press Apply (try it now). Then press the test pattern buttons on the right of the same tab: Corners + arrow (a dot top-left, a dot bottom-right and an arrow pointing right), Walk row and Walk column (a single line that should move smoothly), All on (brief, to spot dead LEDs) and Scroll text. The right side of the tab also says what to change if a pattern looks wrong.
- When it looks right, press Save to board. From now on the board starts the sign by itself on power-up.

## Fixing the picture
- Nothing lights: check GND is shared and 5 V reaches the 74HCT245. Try toggling ""OE is active-low"".
- Everything inverted (dark where it should be lit): toggle ""Invert data"".
- Rows in the wrong order, doubled or upside down: check the address lines are in the right order (A is the fastest), or toggle ""Reverse row order"". Try the other scan type (8 or 16 rows).
- Picture mirrored left-to-right: toggle ""Reverse columns"".
- Top and bottom halves swapped (1/8 scan with one data line): toggle ""bottom half is shifted first"".
- Flicker: raise Refresh (Hz). Ghosting (a faint copy of the row above): lower Brightness a little; that adds blanking time between rows.

## Brightness and safety
Brightness sets the normal level. Safety max is a hard limit nothing can go above (playlists, Home Assistant, the console). Lower it if the power supply gets warm." },

            new HelpTopic { Id = "wiring", Title = "Step 5. Permanent wiring", Tab = "3. Wiring guide",
                Summary = "A wiring list built from your pin map. Print it and follow it for the final install.",
                Body =
@"# Step 5. Permanent wiring
The Wiring guide tab builds the final connection list from your pin map (tab 1) and your GPIO choices (tab 2). It updates as you map pins.

## No 74HCT245 yet?
Tick ""No level shifter"" at the top of the Wiring guide tab. The diagram and steps switch to a direct setup: each ESP32 pin goes through a 220 Ω resistor straight to the header, grounds are joined, and the sign's 5 V stays off the ESP32. The sign's chips officially want about 3.5 V for a ""high"" and the ESP32 gives 3.3 V, so this may work fine, flicker, or not work at all. If it misbehaves, adding the 74HCT245 is the fix.

## Where the pins are on the Freenove board
The default sign pins are on the board's SPI connector (GPIO 23, 19, 18, 5) and I2C connector (GPIO 32, 25), plus GPIO 0 on the BOOT button pad. The wiring guide shows the location next to each GPIO. Leave the SD card slot empty, because it shares the SPI connector's pins. The screen uses GPIO 2, 12, 13, 14, 15, 27 and 33, so those are never offered for the sign.

## How to use it
- Fix every warning at the top of the guide first (missing pins, GPIOs not set).
- Press Save as printable page and print it, or Copy as text.
- Unplug the sign. Remove the CPU-002 board (keep it, so you can always go back).
- Build the 74HCT245 board: 5 V to pin 20, GND to pin 10, pin 1 (DIR) to 5 V, pin 19 (OE) to GND, the 100 nF capacitor across pins 20 and 10.
- Wire each line exactly as listed: ESP32 GPIO > 74HCT245 A input (pins 2 to 9) > matching B output (pins 18 to 11) > header pin. Tick each checkbox as you go.
- Connect every header pin marked GND to ground. Leave unknown and not-connected pins unconnected.
- Power: follow the guide's power section to feed the ESP32 from the sign's 5 V supply.

## Before turning it on
- With the power still off, use the multimeter to check there's no short between 5 V and GND.
- Plug the sign in. The board starts, its screen shows SIGN ON, and the sign shows the startup animation and then the playlist.

> Keep the USB cable handy for the first power-up in case you need the console, but after that everything works over WiFi." },

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
- Check GND is shared and the 74HCT245 has 5 V.
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
            else if (!mapDone) { id = "mapper"; label = "Next: map the 50-pin connector"; }
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
