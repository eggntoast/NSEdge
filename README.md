# NSEdge

NSEdge is an independent Windows companion for the Sony DualSense Edge controller. It reads the active onboard profile and its saved name, and displays a notification when the profile changes.

The project is in early development (v0.2.2). USB profile monitoring is validated on one DualSense Edge (Sony VID `054C`, PID `0DF2`). Read-only Bluetooth discovery now uses the device's HID vendor and product attributes, but **Bluetooth input has not yet been validated on a physical Edge**. LED and gyro controls are not implemented.

## Features

- Detects the Default, Fn + Circle, and Fn + Cross onboard profiles.
- Reads saved profile names from the controller's HID feature reports.
- Shows a non-activating desktop notification when the profile changes.
- Decodes coarse USB battery bands (not precise percentages) and charging status based on the Linux DualSense input format.
- Observes the PS button without intercepting controller input; optionally displays a non-activating battery popup on a new press.
- Includes optional low-battery alerts (disabled by default pending hardware validation) with a configurable threshold.
- Runs from the Windows system tray, with a content-fitted, fixed-size settings window, profile notifications, and diagnostics.
- Allows notifications to be disabled and Windows sign-in startup to be enabled.
- Detects the input-report transport from HID capabilities and accepts CRC-verified Bluetooth full reports (report `0x31`, 78 bytes) for experimental battery and PS-button monitoring.
- Exposes the Bluetooth candidate profile byte for diagnostics without interpreting unverified Edge-specific profile codes or names.
- Does not initiate Bluetooth extended-report mode or send any controller output reports.

NSEdge does not replace DS4Windows. It can monitor a controller that DS4Windows is also using, but not every DS4Windows fork, HID configuration, or controller firmware has been tested.

## Requirements

- Windows 11 x64
- Sony DualSense Edge connected by USB; experimental Bluetooth input support requires full extended reports from another host

GitHub Actions provides an unsigned self-contained development build that includes the required .NET runtime. It may trigger a Windows SmartScreen warning; only run a build whose source and provenance you trust. To build from source, install the .NET 10 SDK with Windows desktop tooling.

## Build and run

```powershell
dotnet build NSEdge.slnx -c Release
dotnet run --project src/NSEdge.App -c Release
```

To run the protocol regression checks:

```powershell
dotnet run --project tests/NSEdge.Tests -c Release
```

For a portable build:

```powershell
dotnet publish src/NSEdge.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o artifacts/NSEdge-win-x64
```

After launch, double-click the tray icon to open NSEdge. The tray menu provides profile status, name refresh, diagnostics, and Exit. Closing the settings window keeps the monitor running; Exit stops it.

Settings and the log are stored under `%LOCALAPPDATA%\NSEdge`. Downloaded development artifacts under the local `zips/` folder are ignored by Git.

For the prebuilt application, download the versioned `NSEdge-vX.Y.Z-win-x64.zip` artifact from a successful [Actions run](https://github.com/eggntoast/NSEdge/actions). Debug symbols are provided separately as `NSEdge-symbols`. No official release or signing certificate has been published.

## Current limitations

- Active-profile codes have been validated on one physical DualSense Edge, not all firmware versions. Unknown codes are not labeled as known profiles.
- Saved-name decoding follows the working USB prototype; names written entirely in non-Latin scripts require further verification.
- Hardware feature-report access may be unavailable when another driver or application prevents reading the HID interface.
- An overlay may not appear above games using exclusive fullscreen.
- Battery-status and PS-button bytes match the Linux DualSense Edge-aware USB driver and DS4Windows parser. Their interpretations still need verification against physical Edge charging, discharge, and PS-button behavior.
- Readings are 10% *bands* (for example 40–49%), not measured 1% increments. Some battery status codes intentionally display unavailable rather than inventing a percentage.
- The PS button remains available to games and DS4Windows. NSEdge does not consume it, so existing guide-button actions may occur alongside its overlay.
- Bluetooth discovery and CRC-verified full input report handling have not been validated on the physical Edge. Minimal Bluetooth reports cannot provide NSEdge's full battery/profile telemetry; the app intentionally does not send the output commands that may be needed to enable extended input mode.
- Bluetooth onboard profile selection, Sony profile names, and feature-report retrieval remain unverified and are deliberately not displayed as known profiles. Diagnostics shows a candidate profile byte only; this is not validated decoding.
- Windows, controller reconnect, sleep/resume, and DS4Windows compatibility still require broader hardware testing.

The Windows build workflow compiles the solution and runs synthetic protocol regression checks. These checks do not replace testing on a real controller.

## License

MIT. Copyright (c) 2026 NS. See [LICENSE](LICENSE).

NSEdge is not affiliated with Sony Interactive Entertainment. Controller-protocol research was cross-checked against the independent [DualSense Edge profile configurator](https://github.com/steffalon/dualsense-edge-profile-web-application), which is MIT-licensed. No source files from that project are included.

## Battery protocol references

The USB report uses report ID `0x01` and 64 bytes including the report ID. Index 10 bit 0 reports PS-button state. Index 53 contains a battery level in its low nibble and a charging-state code in its high nibble; the Linux DualSense driver maps levels to 10% intervals. NSEdge rejects invalid/unknown codes and marks disconnected or stale reports as unavailable. See [Linux hid-playstation.c](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c) and the independent [DS4Windows DualSense parser](https://github.com/ds4windowsapp/DS4Windows/blob/main/DS4Windows/DS4Library/InputDevices/DualSenseDevice.cs).

Use the tray **Diagnostics** command to inspect the raw battery-status byte reported by the physical controller when checking the battery interpretation. The experimental battery and charging-state display is **not yet validated against multiple charging levels or firmware versions**.

## Bluetooth validation

HID devices are identified using `HidD_GetAttributes` rather than text in device paths. Bluetooth HID input report length is determined using `HidD_GetPreparsedData` and `HidP_GetCaps`, not inferred from the device name. Input report `0x31` is validated against the 78-byte layout and the Bluetooth CRC-32 seeded with `0xA1`. The common input payload starts at byte 2 instead of USB byte 1, so PS is at byte 11 and battery at byte 54. This framing is documented by [Linux hid-playstation.c](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c) and independently used by [DS4Windows](https://github.com/ds4windowsapp/DS4Windows/blob/main/DS4Windows/DS4Library/InputDevices/DualSenseDevice.cs).

To validate over Bluetooth, pair the physical Edge normally, run DS4Windows if desired, then inspect NSEdge's tray **Diagnostics** and log. Verify transport, report count, raw battery byte, PS overlay, and whether only minimal reports appear. Do not assume pairing implies the application receives the extended report. If the controller requires an output command to enable extended mode, NSEdge will report that limitation rather than send a command. Current behavior is limited to read-only monitoring and no official release is available.
