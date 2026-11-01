# SignTek: Casino Sign Tool

Windows app and ESP32 firmware for driving a 96x16 LED casino sign (Neurizon ED0204-24, MBI5170 drivers) from a Freenove 4" ESP32 display board.

- **Download:** get `CasinoSignTool.zip` from the [latest release](../../releases/latest), unzip, and run `CasinoSignTool.exe`. All instructions are inside the app (start on the *Start here* tab).
- **Updates:** the app checks this repository's releases when it opens and shows an *Update available* button when there is a newer version.

## What's here
- `app/`: the Windows app source (C#, .NET Framework 4.8). Build with the .NET SDK: `dotnet build -c Release`.
- `firmware/casino_sign_driver/`: the sign firmware (Arduino, ESP32 Dev Module, Minimal SPIFFS partitions). Libraries: LovyanGFX, ArduinoJson 7, PubSubClient.
- `firmware/screen_test/`: a stand-alone test that only lights the board's built-in screen.

Not tested on the real sign yet.
