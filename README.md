# NSEdge

NSEdge is an independent Windows companion for the Sony DualSense Edge controller. It reads the active onboard profile and its saved name, and displays a notification when the profile changes.

The project is in early development (v0.1.2). The initial milestone supports a USB-connected DualSense Edge (Sony VID `054C`, PID `0DF2`); Bluetooth, battery displays, LED control, and gyro control are not implemented.

## Features

- Detects the Default, Fn + Circle, and Fn + Cross onboard profiles.
- Reads saved profile names from the controller's HID feature reports.
- Shows a non-activating desktop notification when the profile changes.
- Runs from the Windows system tray, with a compact, fixed-size settings window, profile notifications, and diagnostics.
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
- Windows, controller reconnect, sleep/resume, and DS4Windows compatibility still require broader hardware testing.

The Windows build workflow compiles the solution and runs synthetic protocol regression checks. These checks do not replace testing on a real controller.

## License

MIT. Copyright (c) 2026 NS. See [LICENSE](LICENSE).

NSEdge is not affiliated with Sony Interactive Entertainment. Controller-protocol research was cross-checked against the independent [DualSense Edge profile configurator](https://github.com/steffalon/dualsense-edge-profile-web-application), which is MIT-licensed. No source files from that project are included.
