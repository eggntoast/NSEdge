# NSEdge

An independent Windows companion for Sony DualSense Edge controllers. NSEdge shows the active Sony onboard profile, its saved name and battery status in a compact desktop and system-tray interface.

## Features

- Detects onboard profiles over USB and Bluetooth, with automatic profile-change popups.
- Reads saved Sony profile names over USB and uses a per-controller cache when connecting by Bluetooth. Direct Bluetooth name retrieval is experimental.
- Shows approximate battery levels, charging status, PS-button battery popups and optional low-battery warnings.
- Includes standalone, **experimental** native lightbar output for USB and Bluetooth. Configure battery colours, a gradient or a single colour from the main window's **Lighting** section. Native lighting is off by default.
- Preserves controller inputs, mappings and Sony onboard settings. DS4Windows is optional. When DS4Windows or another known controller manager is running, NSEdge currently **pauses** native lighting; it does not override that application's colours.

**Hardware limitations:** Native lightbar operation and automatic restoration of earlier lighting settings have not been physically validated. Output coordination while DS4Windows runs is not implemented. Lighting preferences may be enabled while physical output is paused; the app shows that state on the main window. Fn + Square active-profile decoding remains incomplete.

## Download

Get the current unsigned Windows x64 prerelease from [Releases](https://github.com/eggntoast/NSEdge/releases). Extract the ZIP and run `NSEdge.exe` inside the `NSEdge` folder. The self-contained build does not require a separate .NET installation.

## Requirements

Windows 11 x64 and a Sony DualSense Edge controller. Other controller-management software is not required for NSEdge to run.

## Building

Install the .NET 10 SDK, then run:

```powershell
dotnet build NSEdge.slnx -c Release
dotnet run --project src/NSEdge.App -c Release
```

## License

MIT — see [LICENSE](LICENSE). Copyright (c) 2026 NS.

NSEdge is independent software and is not affiliated with Sony Interactive Entertainment.
