# NSEdge

A small Windows companion for the Sony DualSense Edge. NSEdge runs in the system tray, shows your active onboard profile and displays a notification when you switch profiles.

## Features

- Detects onboard profile changes and reads saved profile names over USB.
- Displays profile notifications and battery information.
- Offers optional PS-button battery popups and low-battery alerts.
- Runs alongside DS4Windows without changing controller profiles or mappings.

Battery reporting and Bluetooth support are still in development.

## Download

Get the latest build from [Releases](https://github.com/eggntoast/NSEdge/releases).

Download the Windows ZIP, extract it and run `NSEdge.exe` inside the `NSEdge` folder. No .NET installation is needed for the packaged build.

## Requirements

Windows 11 (64-bit) and a Sony DualSense Edge controller. USB has been tested; Bluetooth support is experimental.

## Build from source

Install the .NET 10 SDK, then run:

```powershell
dotnet build NSEdge.slnx -c Release
dotnet run --project src/NSEdge.App -c Release
```

## License

MIT — see [LICENSE](LICENSE). Copyright (c) 2026 NS.

NSEdge is an independent project, not affiliated with Sony Interactive Entertainment.
