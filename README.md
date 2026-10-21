# NSEdge

NSEdge is an independent Windows companion for the Sony DualSense Edge controller. It reads the active onboard profile and its saved name, and displays a notification when the profile changes.

The project is in early development (v0.2.0). The initial milestone supports a USB-connected DualSense Edge (Sony VID `054C`, PID `0DF2`); Bluetooth, LED control, and gyro control are not implemented.

## Features

- Detects the Default, Fn + Circle, and Fn + Cross onboard profiles.
- Reads saved profile names from the controller's HID feature reports.
- Shows a non-activating desktop notification when the profile changes.
- Decodes coarse USB battery bands (not precise percentages) and charging status based on the Linux DualSense input format.
- Observes the PS button without intercepting controller input; optionally displays a non-activating battery popup on a new press.
- Includes optional low-battery alerts (disabled by default pending hardware validation) with a configurable threshold.
- Runs from the Windows system tray, with a content-fitted, fixed-size settings window, profile notifications, and diagnostics.
- Allows notifications to be disabled and Windows sign-in startup to be enabled.
- Makes no controller configuration changes and sends no HID output reports.

NSEdge does not replace DS4Windows. It can monitor a controller that DS4Windows is also using, but not every DS4Windows fork, HID configuration, or controller firmware has been tested.

## Requirements

- Windows 11 x64
- Sony DualSense Edge connected by USB

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

For the prebuilt application, download the `NSEdge-win-x64` artifact from a successful [Actions run](https://github.com/eggntoast/NSEdge/actions). Debug symbols are provided separately as `NSEdge-symbols`. No official release or signing certificate has been published.

## Current limitations

- Active-profile codes have been validated on one physical DualSense Edge, not all firmware versions. Unknown codes are not labeled as known profiles.
- Saved-name decoding follows the working USB prototype; names written entirely in non-Latin scripts require further verification.
- Hardware feature-report access may be unavailable when another driver or application prevents reading the HID interface.
- An overlay may not appear above games using exclusive fullscreen.
- Battery-status and PS-button bytes match the Linux DualSense Edge-aware USB driver and DS4Windows parser. Their interpretations still need verification against physical Edge charging, discharge, and PS-button behavior.
- Readings are 10% *bands* (for example 40–49%), not measured 1% increments. Some battery status codes intentionally display unavailable rather than inventing a percentage.
- The PS button remains available to games and DS4Windows. NSEdge does not consume it, so existing guide-button actions may occur alongside its overlay.
- No Bluetooth support. Windows, controller reconnect, sleep/resume, and DS4Windows compatibility still require broader hardware testing.

The Windows build workflow compiles the solution and runs synthetic protocol regression checks. These checks do not replace testing on a real controller.

## License

MIT. Copyright (c) 2026 NS. See [LICENSE](LICENSE).

NSEdge is not affiliated with Sony Interactive Entertainment. Controller-protocol research was cross-checked against the independent [DualSense Edge profile configurator](https://github.com/steffalon/dualsense-edge-profile-web-application), which is MIT-licensed. No source files from that project are included.

## Battery protocol references

The USB report uses report ID `0x01` and 64 bytes including the report ID. Index 10 bit 0 reports PS-button state. Index 53 contains a battery level in its low nibble and a charging-state code in its high nibble; the Linux DualSense driver maps levels to 10% intervals. NSEdge rejects invalid/unknown codes and marks disconnected or stale reports as unavailable. See [Linux hid-playstation.c](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c) and the independent [DS4Windows DualSense parser](https://github.com/ds4windowsapp/DS4Windows/blob/main/DS4Windows/DS4Library/InputDevices/DualSenseDevice.cs).

Use the tray **Diagnostics** command to inspect the raw battery-status byte reported by the physical controller when checking the battery interpretation. The experimental battery and charging-state display is **not yet validated against multiple charging levels or firmware versions**.
